using System.Globalization;
using System.Text.Json.Nodes;

namespace DarkestDungeonSaveEditor.Core;

public static partial class StagecoachHeroCandidateFactory
{
    internal static IReadOnlyList<HeroUpgradePurchase> BuildUpgradePurchases(
        HeroClassDefinition heroClass,
        int resolveLevel,
        List<string>? warnings = null)
    {
        // Generation unlocks all applicable camping skills, even when none
        // are equipped. A readable subset cannot prove that complete plan.
        if (!heroClass.CampingSkillsComplete)
            throw new InvalidOperationException(
                EditorText.Format("StagecoachHeroCandidateFactory_Progression_001", heroClass.Id));

        var unsupportedTree = heroClass.UpgradeTrees.FirstOrDefault(tree =>
            !string.IsNullOrWhiteSpace(tree.UnsupportedReason));
        if (unsupportedTree is not null)
        {
            // An authored but unreadable winner is not a missing implicit tree.
            throw new InvalidOperationException(
                EditorText.Format("StagecoachHeroCandidateFactory_Progression_002", heroClass.Id, unsupportedTree.Id) +
                $"{unsupportedTree.UnsupportedReason}（{unsupportedTree.SourcePath}）。");
        }

        var combatTrees = heroClass.UpgradeTrees
            .Where(tree => tree.Kind == HeroUpgradeTreeKind.CombatSkill)
            .ToDictionary(tree => tree.Id, StringComparer.Ordinal);
        var combatTargets = HeroSkillPurchaseTargets.Combat(heroClass.Id, heroClass.CombatSkillIds);
        var equipmentTargets = HeroSkillPurchaseTargets.Equipment(heroClass.Id);
        var campingTargets = HeroSkillPurchaseTargets.Camping(heroClass.Id,
            heroClass.SharedCampingSkillIds.Concat(heroClass.ClassCampingSkillIds));
        var overlappingTarget = equipmentTargets.Values.FirstOrDefault(target =>
            combatTargets.Values.Contains(target, StringComparer.Ordinal) ||
            campingTargets.Values.Contains(target, StringComparer.Ordinal));
        if (overlappingTarget is not null)
            throw new InvalidOperationException(EditorText.Format("StagecoachHeroCandidateFactory_Progression_003", heroClass.Id, overlappingTarget));
        var targetCollisions = NativeResourceIdentity.FindCollisions(
            equipmentTargets.Values.Concat(combatTargets.Values).Concat(campingTargets.Values));
        if (targetCollisions.Count > 0)
            throw new InvalidOperationException(EditorText.Format("StagecoachHeroCandidateFactory_Progression_004", heroClass.Id, string.Join(", ", targetCollisions)));
        // Selection rules control equipped skills, not the purchase-based level lookup.
        // Every tree-less skill uses the same bounded implicit progression policy.
        var combatPurchases = new List<HeroUpgradePurchase>();
        foreach (var (skillId, target) in combatTargets)
        {
            // Hidden skills are auto-purchased only through authored upgrade trees
            // (28385 RVA 0x5D4950); a missing tree does not create an unlock.
            if (heroClass.CombatSkillSelection.GetValueOrDefault(skillId)?.Visible == false)
            {
                if (combatTrees.TryGetValue(target, out var hiddenTree))
                    combatPurchases.AddRange(BuildHiddenCombatPurchases(heroClass, skillId, hiddenTree, resolveLevel));
                continue;
            }
            combatPurchases.AddRange(combatTrees.TryGetValue(target, out var tree)
                ? BuildAuthoredCombatPurchases(heroClass, skillId, tree, resolveLevel, warnings,
                    heroClass.CanSelectCombatSkills != false && heroClass.CombatSkillSelection.GetValueOrDefault(skillId)?.ForceSelection == true)
                : BuildImplicitCombatPurchases(heroClass, skillId, combatTargets, resolveLevel, warnings));
        }

        var applicableTrees = heroClass.UpgradeTrees
            .Where(tree => tree.Kind != HeroUpgradeTreeKind.CombatSkill).ToArray();
        if (applicableTrees.Any(tree => tree.Id != equipmentTargets[
                tree.Kind == HeroUpgradeTreeKind.Weapon ? "weapon" : "armour"]))
            throw new InvalidOperationException(EditorText.Format("StagecoachHeroCandidateFactory_Progression_005", heroClass.Id));
        var campingPurchases = BuildCampingUpgradePurchases(heroClass);
        var purchases = applicableTrees
            .SelectMany(tree => tree.Requirements
                .Where(requirement => requirement.PrerequisiteResolveLevel <= resolveLevel)
                .Select(requirement => new HeroUpgradePurchase(tree.Id, requirement.Code)))
            .Concat(combatPurchases)
            .Concat(campingPurchases)
            .OrderBy(purchase => purchase.TreeId, StringComparer.Ordinal)
            .ThenBy(purchase => purchase.RequirementCode, StringComparer.Ordinal)
            .ToArray();
        if (purchases.Any(purchase =>
                string.IsNullOrWhiteSpace(purchase.TreeId) ||
                string.IsNullOrWhiteSpace(purchase.RequirementCode)))
        {
            throw new InvalidOperationException(
                EditorText.Format("StagecoachHeroCandidateFactory_Progression_006", heroClass.Id));
        }
        if (purchases.Any(purchase => !DsonSaveCodec.CanRoundTripPurchaseCode(purchase.RequirementCode)))
            throw new InvalidOperationException(EditorText.Format("StagecoachHeroCandidateFactory_Progression_007", heroClass.Id));

        var collisions = NativeResourceIdentity.FindCollisions(purchases.Select(purchase => purchase.TreeId));
        if (collisions.Count > 0)
        {
            throw new InvalidOperationException(
                EditorText.Format("StagecoachHeroCandidateFactory_Progression_008", heroClass.Id, string.Join(", ", collisions)));
        }

        var duplicate = purchases
            .GroupBy(purchase => new { purchase.TreeId, purchase.RequirementCode })
            .FirstOrDefault(group => group.Skip(1).Any());
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                EditorText.Format("StagecoachHeroCandidateFactory_Progression_009", heroClass.Id) +
                $"{duplicate.First().TreeId}/{duplicate.First().RequirementCode}。");
        }

        return purchases;
    }

    private static IReadOnlyList<HeroUpgradePurchase> BuildCampingUpgradePurchases(HeroClassDefinition heroClass)
    {
        // The game uses code 0 even when the camping JSON authors another code.
        return HeroSkillPurchaseTargets.Camping(heroClass.Id,
                heroClass.SharedCampingSkillIds.Concat(heroClass.ClassCampingSkillIds)).Values
            .Select(target => new HeroUpgradePurchase(target, "0")).ToArray();
    }

    private static HeroLevelProfile ResolveLevelProfile(
        HeroClassCatalogResult catalog,
        HeroClassDefinition heroClass,
        int resolveLevel)
    {
        var maximumLevel = catalog.ResolveLevelThresholds.Count > 0
            ? catalog.ResolveLevelThresholds.Count - 1
            : 0;
        if (resolveLevel < 0 || resolveLevel > maximumLevel)
        {
            throw new InvalidOperationException(
                EditorText.Format("StagecoachHeroCandidateFactory_Progression_010", maximumLevel, resolveLevel));
        }

        var matches = heroClass.LevelProfiles
            .Where(profile => profile.ResolveLevel == resolveLevel)
            .ToArray();
        if (matches.Length != 1)
        {
            var reason = string.IsNullOrWhiteSpace(heroClass.ProgressionUnsupportedReason)
                ? EditorText.Get("StagecoachHeroCandidateFactory_Progression_011")
                : heroClass.ProgressionUnsupportedReason;
            throw new InvalidOperationException(
                EditorText.Format("StagecoachHeroCandidateFactory_Progression_012", heroClass.Id, resolveLevel, reason));
        }

        var profile = matches[0];
        if (profile.ResolveXp < 0 || profile.WeaponRank < 0 || profile.ArmourRank < 0 ||
            !double.IsFinite(profile.ArmourHp) || profile.ArmourHp <= 0)
        {
            throw new InvalidOperationException(
                EditorText.Format("StagecoachHeroCandidateFactory_Progression_013", heroClass.Id, resolveLevel));
        }

        return profile;
    }

    private static IReadOnlyList<string> SelectCombatSkills(
        HeroClassDefinition heroClass,
        HeroGenerationDefinition generation,
        Random random,
        List<string> warnings,
        IReadOnlyList<HeroUpgradePurchase> purchases,
        out IReadOnlyList<string>? initiallyUnlocked)
    {
        initiallyUnlocked = null;
        if (heroClass.CombatSkillIds.Count == 0)
        {
            throw new InvalidOperationException(EditorText.Format("StagecoachHeroCandidateFactory_Progression_014", heroClass.Id));
        }

        var available = heroClass.CombatSkillIds.ToHashSet(StringComparer.Ordinal);
        if (heroClass.GuaranteedCombatSkillIds.Any(skill => !available.Contains(skill)))
        {
            throw new InvalidOperationException(EditorText.Format("StagecoachHeroCandidateFactory_Progression_015", heroClass.Id));
        }

        var ordinary = heroClass.CombatSkillSelection.Values.All(flags => flags == new HeroCombatSkillSelection());
        // Preserve ordinary-template safety guards. A zero random draw can now
        // be usable when generation_guaranteed supplies a skill; hidden/forced/
        // exempt templates retain their existing capability rules.
        if (ordinary && heroClass.CanSelectCombatSkills != false && heroClass.SelectedCombatSkillsMax is <= 0)
            throw new InvalidOperationException(
                EditorText.Format("StagecoachHeroCandidateFactory_Progression_018", heroClass.Id, heroClass.SelectedCombatSkillsMax));

        var selected = SelectInitialCombatSkills(heroClass, generation, random, purchases, warnings, out initiallyUnlocked);
        if (ordinary && selected.Count == 0)
            throw new InvalidOperationException(EditorText.Format("StagecoachHeroCandidateFactory_Progression_017",
                heroClass.Id, generation.RandomCombatSkills, heroClass.CombatSkillIds.Count));
        return selected;
    }

    private static IReadOnlyList<string> SelectCampingSkills(
        HeroClassDefinition heroClass,
        int classSpecificCount,
        int sharedCount,
        Random random,
        List<string> warnings)
    {
        var actualClassCount = Math.Min(classSpecificCount, heroClass.ClassCampingSkillIds.Count);
        if (actualClassCount < classSpecificCount)
        {
            warnings.Add(
                EditorText.Format("StagecoachHeroCandidateFactory_Progression_020", classSpecificCount, heroClass.ClassCampingSkillIds.Count));
        }

        var selectedClass = TakeRandom(heroClass.ClassCampingSkillIds, actualClassCount, random);
        var actualSharedCount = Math.Min(sharedCount, heroClass.SharedCampingSkillIds.Count);
        if (actualSharedCount < sharedCount)
        {
            warnings.Add(
                EditorText.Format("StagecoachHeroCandidateFactory_Progression_021", sharedCount, heroClass.SharedCampingSkillIds.Count));
        }

        var selectedShared = TakeRandom(heroClass.SharedCampingSkillIds, actualSharedCount, random);
        return selectedShared.Concat(selectedClass).ToArray();
    }

}
