using System.Globalization;
using System.Text.Json.Nodes;

namespace DarkestDungeonSaveEditor.Core;

public static partial class StagecoachHeroCandidateFactory
{
    internal static IReadOnlyList<HeroUpgradePurchase> BuildUpgradePurchases(
        HeroClassDefinition heroClass,
        int resolveLevel)
    {
        var combatTrees = heroClass.UpgradeTrees
            .Where(tree => tree.Kind == HeroUpgradeTreeKind.CombatSkill)
            .ToDictionary(tree => tree.Id, StringComparer.Ordinal);
        var expectedCombatTreeIds = heroClass.CombatSkillIds
            .Select(skillId => $"{heroClass.Id}.{skillId}")
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var singleLevelCombatTreeIds = heroClass.SingleLevelCombatSkillIds
            .Select(skillId => $"{heroClass.Id}.{skillId}")
            .ToHashSet(StringComparer.Ordinal);
        var syntheticCombatTreeIds = expectedCombatTreeIds
            .Where(treeId => !combatTrees.ContainsKey(treeId) &&
                             singleLevelCombatTreeIds.Contains(treeId))
            .ToArray();
        var missingCombatTreeIds = expectedCombatTreeIds
            .Where(treeId => !combatTrees.ContainsKey(treeId) &&
                             !singleLevelCombatTreeIds.Contains(treeId))
            .ToArray();
        if (missingCombatTreeIds.Length > 0)
        {
            throw new InvalidOperationException(
                $"职业 '{heroClass.Id}' 的活动 upgrade 模板缺少战斗技能升级树：" +
                $"{string.Join(", ", missingCombatTreeIds)}；" +
                $"不能安全生成 {resolveLevel} 级全技能解锁人物。");
        }

        var unavailableCombatTreeIds = expectedCombatTreeIds
            .Where(combatTrees.ContainsKey)
            .Where(treeId => combatTrees[treeId].Requirements.All(requirement =>
                requirement.PrerequisiteResolveLevel > resolveLevel))
            .ToArray();
        if (unavailableCombatTreeIds.Length > 0)
        {
            throw new InvalidOperationException(
                $"职业 '{heroClass.Id}' 的下列战斗技能升级树在 {resolveLevel} 级没有可用 requirement：" +
                $"{string.Join(", ", unavailableCombatTreeIds)}；不能声称该等级已全技能解锁。");
        }

        var applicableTrees = heroClass.UpgradeTrees
            .Where(tree => tree.Kind != HeroUpgradeTreeKind.CombatSkill)
            .Concat(expectedCombatTreeIds
                .Where(combatTrees.ContainsKey)
                .Select(treeId => combatTrees[treeId]));
        var syntheticCombatPurchases = syntheticCombatTreeIds
            .Select(treeId => new HeroUpgradePurchase(treeId, "0"));
        var campingPurchases = heroClass.SharedCampingSkillIds
            .Concat(heroClass.ClassCampingSkillIds)
            .Distinct(StringComparer.Ordinal)
            .Select(skillId => new HeroUpgradePurchase($"{heroClass.Id}.{skillId}", "0"));
        var purchases = applicableTrees
            .SelectMany(tree => tree.Requirements
                .Where(requirement => requirement.PrerequisiteResolveLevel <= resolveLevel)
                .Select(requirement => new HeroUpgradePurchase(tree.Id, requirement.Code)))
            .Concat(syntheticCombatPurchases)
            .Concat(campingPurchases)
            .OrderBy(purchase => purchase.TreeId, StringComparer.Ordinal)
            .ThenBy(purchase => purchase.RequirementCode, StringComparer.Ordinal)
            .ToArray();
        if (purchases.Any(purchase =>
                string.IsNullOrWhiteSpace(purchase.TreeId) ||
                string.IsNullOrWhiteSpace(purchase.RequirementCode)))
        {
            throw new InvalidOperationException(
                $"职业 '{heroClass.Id}' 的活动 upgrade 模板包含空树 ID 或空 requirement code。");
        }

        var duplicate = purchases
            .GroupBy(purchase => new { purchase.TreeId, purchase.RequirementCode })
            .FirstOrDefault(group => group.Skip(1).Any());
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"职业 '{heroClass.Id}' 的活动 upgrade 模板产生了重复购买项：" +
                $"{duplicate.First().TreeId}/{duplicate.First().RequirementCode}。");
        }

        return purchases;
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
                $"人物等级必须在 0 到 {maximumLevel} 之间，当前为 {resolveLevel}。");
        }

        var matches = heroClass.LevelProfiles
            .Where(profile => profile.ResolveLevel == resolveLevel)
            .ToArray();
        if (matches.Length != 1)
        {
            var reason = string.IsNullOrWhiteSpace(heroClass.ProgressionUnsupportedReason)
                ? "等级模板缺失或重复"
                : heroClass.ProgressionUnsupportedReason;
            throw new InvalidOperationException(
                $"职业 '{heroClass.Id}' 不能生成 {resolveLevel} 级人物：{reason}。");
        }

        var profile = matches[0];
        if (profile.ResolveXp < 0 || profile.WeaponRank < 0 || profile.ArmourRank < 0 ||
            !double.IsFinite(profile.ArmourHp) || profile.ArmourHp <= 0)
        {
            throw new InvalidOperationException(
                $"职业 '{heroClass.Id}' 的 {resolveLevel} 级模板包含无效 XP、装备 rank 或护甲 HP。");
        }

        return profile;
    }

    private static IReadOnlyList<string> SelectCombatSkills(
        HeroClassDefinition heroClass,
        HeroGenerationDefinition generation,
        Random random)
    {
        if (heroClass.CombatSkillIds.Count == 0)
        {
            throw new InvalidOperationException($"职业 '{heroClass.Id}' 没有 0 级战斗技能。");
        }

        var available = heroClass.CombatSkillIds.ToHashSet(StringComparer.Ordinal);
        if (heroClass.GuaranteedCombatSkillIds.Any(skill => !available.Contains(skill)))
        {
            throw new InvalidOperationException($"职业 '{heroClass.Id}' 的 generation_guaranteed 技能不在 0 级技能表中。");
        }

        if (heroClass.CanSelectCombatSkills == false)
        {
            return heroClass.CombatSkillIds.ToArray();
        }

        var generatedSkillCount = generation.RandomCombatSkills ??
            throw new InvalidOperationException($"职业 '{heroClass.Id}' 缺少 number_of_random_combat_skills。");
        if (generatedSkillCount <= 0 || generatedSkillCount > heroClass.CombatSkillIds.Count)
        {
            throw new InvalidOperationException(
                $"职业 '{heroClass.Id}' 要求生成 {generatedSkillCount} 个战斗技能，但只有 {heroClass.CombatSkillIds.Count} 个 0 级技能。");
        }

        var target = heroClass.SelectedCombatSkillsMax is { } maximum
            ? Math.Min(generatedSkillCount, maximum)
            : generatedSkillCount;
        if (target <= 0)
        {
            throw new InvalidOperationException(
                $"职业 '{heroClass.Id}' 的可选战斗技能上限无效：{target}。");
        }

        if (heroClass.GuaranteedCombatSkillIds.Count > target)
        {
            throw new InvalidOperationException(
                $"职业 '{heroClass.Id}' 有 {heroClass.GuaranteedCombatSkillIds.Count} 个必选技能，但生成总数只有 {target}。");
        }

        var selected = heroClass.GuaranteedCombatSkillIds.ToHashSet(StringComparer.Ordinal);
        var remaining = heroClass.CombatSkillIds.Where(skill => !selected.Contains(skill)).ToList();
        Shuffle(remaining, random);
        foreach (var skill in remaining.Take(target - selected.Count))
        {
            selected.Add(skill);
        }

        return heroClass.CombatSkillIds.Where(selected.Contains).ToArray();
    }

    private static IReadOnlyList<string> SelectCampingSkills(
        HeroClassDefinition heroClass,
        int classSpecificCount,
        int sharedCount,
        Random random,
        List<string> warnings)
    {
        if (classSpecificCount > heroClass.ClassCampingSkillIds.Count)
        {
            throw new InvalidOperationException(
                $"职业 '{heroClass.Id}' 要求 {classSpecificCount} 个职业露营技能，但活动内容中只有 {heroClass.ClassCampingSkillIds.Count} 个。");
        }

        var selectedClass = TakeRandom(heroClass.ClassCampingSkillIds, classSpecificCount, random);
        var actualSharedCount = Math.Min(sharedCount, heroClass.SharedCampingSkillIds.Count);
        if (actualSharedCount < sharedCount)
        {
            warnings.Add(
                $"共享露营技能要求 {sharedCount} 个、活动内容仅有 {heroClass.SharedCampingSkillIds.Count} 个；按游戏样本少取，不用职业技能补位。");
        }

        var selectedShared = TakeRandom(heroClass.SharedCampingSkillIds, actualSharedCount, random);
        return selectedShared.Concat(selectedClass).ToArray();
    }

}
