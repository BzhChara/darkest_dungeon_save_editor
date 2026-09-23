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
                $"职业 '{heroClass.Id}' 的露营技能定义读取不完整，无法确认初始技能与完整解锁计划。");

        var unsupportedTree = heroClass.UpgradeTrees.FirstOrDefault(tree =>
            !string.IsNullOrWhiteSpace(tree.UnsupportedReason));
        if (unsupportedTree is not null)
        {
            // An authored but unreadable winner is not a missing implicit tree.
            throw new InvalidOperationException(
                $"职业 '{heroClass.Id}' 的升级树 '{unsupportedTree.Id}' 无法用于生成：" +
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
            throw new InvalidOperationException($"职业 '{heroClass.Id}' 的装备与技能指向同一购买目标 '{overlappingTarget}'，无法安全生成。");
        var targetCollisions = NativeResourceIdentity.FindCollisions(
            equipmentTargets.Values.Concat(combatTargets.Values).Concat(campingTargets.Values));
        if (targetCollisions.Count > 0)
            throw new InvalidOperationException($"职业 '{heroClass.Id}' 的装备与技能购买编号冲突：{string.Join(", ", targetCollisions)}。");
        // Selection rules control equipped skills, not the purchase-based level lookup.
        // Every tree-less skill uses the same bounded implicit progression policy.
        var combatPurchases = new List<HeroUpgradePurchase>();
        foreach (var (skillId, target) in combatTargets)
        {
            combatPurchases.AddRange(combatTrees.TryGetValue(target, out var tree)
                ? BuildAuthoredCombatPurchases(heroClass, skillId, tree, resolveLevel, warnings)
                : BuildImplicitCombatPurchases(heroClass, skillId, combatTargets, resolveLevel, warnings));
        }

        var applicableTrees = heroClass.UpgradeTrees
            .Where(tree => tree.Kind != HeroUpgradeTreeKind.CombatSkill).ToArray();
        if (applicableTrees.Any(tree => tree.Id != equipmentTargets[
                tree.Kind == HeroUpgradeTreeKind.Weapon ? "weapon" : "armour"]))
            throw new InvalidOperationException($"职业 '{heroClass.Id}' 的装备升级树与原生购买目标不一致，请刷新人物目录。");
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
                $"职业 '{heroClass.Id}' 的活动 upgrade 模板包含空树 ID 或空 requirement code。");
        }
        if (purchases.Any(purchase => !DsonSaveCodec.CanRoundTripPurchaseCode(purchase.RequirementCode)))
            throw new InvalidOperationException($"职业 '{heroClass.Id}' 的购买码不能由 DSON 转换器无损保存，请检查升级树。");

        var collisions = NativeResourceIdentity.FindCollisions(purchases.Select(purchase => purchase.TreeId));
        if (collisions.Count > 0)
        {
            throw new InvalidOperationException(
                $"职业 '{heroClass.Id}' 的升级购买编号冲突：{string.Join(", ", collisions)}。");
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
        Random random,
        List<string> warnings)
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
        if (generatedSkillCount <= 0)
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

        if (generatedSkillCount > heroClass.CombatSkillIds.Count)
        {
            warnings.Add($"战斗技能要求 {generatedSkillCount} 个、活动内容仅有 {heroClass.CombatSkillIds.Count} 个；装备数量按实际技能池和选择上限确定。");
        }
        target = Math.Min(target, heroClass.CombatSkillIds.Count);

        var guaranteed = heroClass.GuaranteedCombatSkillIds.ToHashSet(StringComparer.Ordinal);
        var remaining = heroClass.CombatSkillIds.ToList();
        Shuffle(remaining, random);
        var selected = remaining.Take(target).ToList();
        // Native initial generation (0x1405C7CF0): at least one marked
        // skill, not every marked skill. If the first draw misses all marks,
        // continue without replacement, replacing the first selection slot.
        if (guaranteed.Count > 0 && !selected.Any(guaranteed.Contains))
        {
            foreach (var skill in remaining.Skip(target))
            {
                selected[0] = skill;
                if (guaranteed.Contains(skill)) break;
            }
        }

        return heroClass.CombatSkillIds.Where(skill => selected.Contains(skill, StringComparer.Ordinal)).ToArray();
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
                $"职业露营技能要求 {classSpecificCount} 个、活动内容仅有 {heroClass.ClassCampingSkillIds.Count} 个；按实际技能池少取，不用共享技能补位。");
        }

        var selectedClass = TakeRandom(heroClass.ClassCampingSkillIds, actualClassCount, random);
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
