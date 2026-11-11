namespace DarkestDungeonSaveEditor.Core;

public static partial class StagecoachHeroCandidateFactory
{
    private static IReadOnlyList<string> SelectInitialCombatSkills(HeroClassDefinition hero,
        HeroGenerationDefinition generation, Random random, IReadOnlyList<HeroUpgradePurchase> purchases, List<string> warnings,
        out IReadOnlyList<string>? initiallyUnlocked)
    {
        HeroCombatSkillSelection Flags(string id) => hero.CombatSkillSelection.GetValueOrDefault(id) ?? new();
        var targets = HeroSkillPurchaseTargets.Combat(hero.Id, hero.CombatSkillIds);
        var purchased = purchases.Select(p => p.TreeId).ToHashSet(StringComparer.Ordinal);
        var owned = hero.CombatSkillIds.Where(id => purchased.Contains(targets[id])).ToArray();
        initiallyUnlocked = owned;
        if (hero.CanSelectCombatSkills == false) return owned;

        // Native initial draw (0x5D5C40) also applies to ordinary templates:
        // unlock the generation count before imposing any equipment cap.
        // The draw excludes hidden and forced skills.
        // They are unlocked separately (0x5D4950 / 0x5D4480), then equipment
        // is rebuilt with forced skills first (0x5D5190). Hidden owned skills
        // remain in selected_combat_skills, but do not consume a visible slot.
        // Equipment follows class order, not the random unlock draw order.
        var draw = owned.Where(id => Flags(id) is { Visible: true, ForceSelection: false }).ToList();
        Shuffle(draw, random);
        var requested = generation.RandomCombatSkills ??
            throw new InvalidOperationException(EditorText.Format("StagecoachHeroCandidateFactory_Progression_016", hero.Id));
        if (requested < 0) throw new InvalidOperationException(
            EditorText.Format("StagecoachHeroCandidateFactory_Progression_017", hero.Id, requested, draw.Count));
        if (requested > draw.Count)
            warnings.Add(EditorText.Format("StagecoachHeroCandidateFactory_Progression_019", requested, draw.Count));
        var randomSkills = draw.Take(requested).ToHashSet(StringComparer.Ordinal);
        var guaranteed = hero.GuaranteedCombatSkillIds.ToHashSet(StringComparer.Ordinal);
        if (!randomSkills.Any(guaranteed.Contains))
        {
            var replacement = draw.Skip(requested).FirstOrDefault(guaranteed.Contains);
            if (replacement is not null)
            {
                if (randomSkills.Count > 0) randomSkills.Remove(draw[0]);
                randomSkills.Add(replacement);
            }
        }
        initiallyUnlocked = owned.Where(id => !Flags(id).Visible || Flags(id).ForceSelection || randomSkills.Contains(id)).ToArray();
        var selected = new HashSet<string>(StringComparer.Ordinal);
        var count = 0;
        var maximum = hero.SelectedCombatSkillsMax ?? 0;
        foreach (var id in owned.Where(id => Flags(id) is { ForceSelection: true, Visible: true })
                     .Concat(owned.Where(id => !Flags(id).Visible || randomSkills.Contains(id))))
        {
            if (selected.Contains(id)) continue;
            var counts = Flags(id) is { Visible: true, CountsTowardsLimit: true };
            if (counts && maximum > 0 && count >= maximum) continue;
            selected.Add(id);
            if (counts) count++;
        }
        return hero.CombatSkillIds.Where(selected.Contains).ToArray();
    }

    private static IEnumerable<HeroUpgradePurchase> BuildHiddenCombatPurchases(HeroClassDefinition hero,
        string skillId, HeroUpgradeTreeDefinition tree, int resolveLevel)
    {
        // RVA 0x5D4AA3: numeric codes in variant order; the base unlock ignores
        // resolve prerequisites. A missing or ineligible later code ends the walk.
        var count = hero.CombatSkillLevels.GetValueOrDefault(skillId)?.Count ?? 0;
        for (var index = 0; index < count; index++)
        {
            var code = ((char)('0' + index)).ToString();
            var requirement = tree.Requirements.FirstOrDefault(item => item.Code == code);
            if (requirement is null || index > 0 && requirement.PrerequisiteResolveLevel > resolveLevel) yield break;
            yield return new HeroUpgradePurchase(tree.Id, code);
        }
    }
}
