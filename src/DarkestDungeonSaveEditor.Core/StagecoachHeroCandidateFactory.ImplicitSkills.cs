using System.Globalization;

namespace DarkestDungeonSaveEditor.Core;

public static partial class StagecoachHeroCandidateFactory
{
    private static IReadOnlyList<HeroUpgradePurchase> BuildImplicitCombatPurchases(
        HeroClassDefinition heroClass,
        string skillId,
        IReadOnlyDictionary<string, string> combatTargets,
        int resolveLevel,
        List<string>? warnings)
    {
        var treeId = combatTargets[skillId];
        if (heroClass.SingleLevelCombatSkillIds.Contains(skillId, StringComparer.Ordinal))
        {
            return [new HeroUpgradePurchase(treeId, "0")];
        }

        // Current game code resolves combat level from consecutive purchased ASCII
        // codes starting at '0', not from selected_combat_skills values or guild trees.
        // See docs/implicit-skill-progression.md for versioned binary/save evidence.
        var reason = string.Empty;
        IReadOnlyList<HeroUpgradeRequirementDefinition> schedule = [];
        if (!heroClass.CombatSkillLevels.TryGetValue(skillId, out var levels) ||
            !HasContiguousNumericSkillLevels(levels))
        {
            reason = "技能等级定义不连续或缺失";
        }
        else if (!TryGetImplicitCombatSchedule(heroClass, combatTargets, out schedule))
        {
            reason = "同职业没有获得有效技能严格过半支持的升级规则";
        }
        else if (levels.Count > schedule.Count)
        {
            reason = "技能档数超出同职业已知升级规则";
        }

        if (reason.Length > 0)
        {
            warnings?.Add($"无购买升级树的技能仅做基础解锁：{skillId}（{reason}）。");
            return [new HeroUpgradePurchase(treeId, "0")];
        }

        var purchases = schedule
            .Take(levels!.Count)
            .TakeWhile(requirement => requirement.PrerequisiteResolveLevel <= resolveLevel)
            .Select(requirement => new HeroUpgradePurchase(treeId, requirement.Code))
            .ToArray();
        warnings?.Add($"无购买升级树的技能已按同职业多数规则补齐至技能 {purchases.Length} 级：{skillId}。");
        return purchases;
    }

    private static bool HasContiguousNumericSkillLevels(IReadOnlyList<int> levels) =>
        levels.Count is > 0 and <= 10 && levels.SequenceEqual(Enumerable.Range(0, levels.Count));

    private static bool TryGetImplicitCombatSchedule(
        HeroClassDefinition heroClass,
        IReadOnlyDictionary<string, string> combatTargets,
        out IReadOnlyList<HeroUpgradeRequirementDefinition> schedule)
    {
        schedule = [];
        var validSchedules = new List<IReadOnlyList<HeroUpgradeRequirementDefinition>>();
        foreach (var (skillId, target) in combatTargets)
        {
            var tree = heroClass.UpgradeTrees.SingleOrDefault(candidate =>
                candidate.Kind == HeroUpgradeTreeKind.CombatSkill &&
                candidate.Id.Equals(target, StringComparison.Ordinal));
            if (tree is null || tree.Requirements.Count <= 1)
            {
                continue;
            }

            var requirements = tree.Requirements.OrderBy(requirement => requirement.Code, StringComparer.Ordinal).ToArray();
            if (!heroClass.CombatSkillLevels.TryGetValue(skillId, out var levels) ||
                !HasContiguousNumericSkillLevels(levels) || levels.Count != requirements.Length ||
                requirements[0].PrerequisiteResolveLevel != 0 ||
                requirements.Where((requirement, index) =>
                    requirement.Code != index.ToString(CultureInfo.InvariantCulture) ||
                    requirement.PrerequisiteResolveLevel < 0 ||
                    (index > 0 && requirement.PrerequisiteResolveLevel < requirements[index - 1].PrerequisiteResolveLevel)).Any())
            {
                continue;
            }

            validSchedules.Add(requirements);
        }

        // One vote per valid authored multilevel skill, comparing only level prerequisites.
        // A plurality or tie is not enough; authored minority trees keep their own purchases.
        foreach (var candidate in validSchedules)
        {
            if (validSchedules.Count(reference => reference.SequenceEqual(candidate)) > validSchedules.Count / 2)
            {
                schedule = candidate;
                return true;
            }
        }

        return false;
    }
}
