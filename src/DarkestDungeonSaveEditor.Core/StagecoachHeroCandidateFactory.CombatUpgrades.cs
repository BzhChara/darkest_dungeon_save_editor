namespace DarkestDungeonSaveEditor.Core;

public static partial class StagecoachHeroCandidateFactory
{
    private static IReadOnlyList<HeroUpgradePurchase> BuildAuthoredCombatPurchases(
        HeroClassDefinition heroClass, string skillId, HeroUpgradeTreeDefinition tree,
        int resolveLevel, List<string>? warnings)
    {
        var purchases = tree.Requirements.Where(requirement => requirement.PrerequisiteResolveLevel <= resolveLevel)
            .Select(requirement => new HeroUpgradePurchase(tree.Id, requirement.Code)).ToArray();
        var codes = purchases.Select(purchase => purchase.RequirementCode).ToHashSet(StringComparer.Ordinal);
        if (!codes.Contains("0"))
            throw new InvalidOperationException(
                $"职业 '{heroClass.Id}' 的战斗技能 '{skillId}' 在 {resolveLevel} 级缺少可购买的基础购买码 '0'（升级树 '{tree.Id}'），不能生成未解锁的技能。");

        // Native 0x14058EA10 reads consecutive ASCII codes from '0', bounded
        // by the actual variant count. Authored letters are not renamed or
        // synthesized into numeric upgrades, and equipment uses its own rules.
        if (heroClass.CombatSkillLevels.TryGetValue(skillId, out var levels) && levels.Count > 0)
        {
            var reachable = 0;
            while (reachable < levels.Count && codes.Contains(((char)('0' + reachable)).ToString()))
                reachable++;
            if (codes.Any(code => code.Length == 1 && code[0] - '0' >= reachable && code[0] - '0' < levels.Count))
                warnings?.Add(
                    $"战斗技能 '{skillId}' 的购买码存在空洞，缺少代码 '{(char)('0' + reachable)}'；游戏只能读取第 {reachable - 1} 档（技能 {reachable} 级），后续已购条目暂不生效。");
        }
        return purchases;
    }
}
