namespace DarkestDungeonSaveEditor.Core;

public static partial class StagecoachHeroCandidateFactory
{
    private static IReadOnlyList<HeroUpgradePurchase> BuildAuthoredCombatPurchases(
        HeroClassDefinition heroClass, string skillId, HeroUpgradeTreeDefinition tree,
        int resolveLevel, List<string>? warnings, bool forceBaseUnlock = false)
    {
        var purchases = tree.Requirements.Where(requirement => requirement.PrerequisiteResolveLevel <= resolveLevel)
            .Select(requirement => new HeroUpgradePurchase(tree.Id, requirement.Code)).ToList();
        var codes = purchases.Select(purchase => purchase.RequirementCode).ToHashSet(StringComparer.Ordinal);
        // Forced visible skills use the native direct purchase helper
        // (28385 RVA 0x5D4480 -> 0x5D5A70), ignoring code 0 prerequisites.
        if (forceBaseUnlock && codes.Add("0")) purchases.Add(new(tree.Id, "0"));
        if (!codes.Contains("0"))
            throw new InvalidOperationException(
                EditorText.Format("StagecoachHeroCandidateFactory_CombatUpgrades_001", heroClass.Id, skillId, resolveLevel, tree.Id));

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
                    EditorText.Format("StagecoachHeroCandidateFactory_CombatUpgrades_002", skillId, (char)('0' + reachable), reachable - 1, reachable));
        }
        return purchases;
    }
}
