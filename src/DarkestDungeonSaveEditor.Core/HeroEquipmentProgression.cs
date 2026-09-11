using System.Text.Json.Serialization;

namespace DarkestDungeonSaveEditor.Core;

// Raw invalid HP still participates in content identity. Named nonfinite values
// keep fingerprints serializable without changing the separate generation guard.
internal sealed record HeroEquipmentRank(int Rank, byte RequirementCode,
    [property: JsonNumberHandling(JsonNumberHandling.AllowNamedFloatingPointLiterals)] double? Hp, string Name = "");

internal sealed record HeroEquipmentDefinition(
    IReadOnlyList<HeroEquipmentRank> Weapon, IReadOnlyList<HeroEquipmentRank> Armour);

internal static class HeroEquipmentProgression
{
    internal static HeroEquipmentRank Resolve(
        IReadOnlyList<HeroEquipmentRank> ranks, string target, IEnumerable<HeroUpgradePurchase> purchases)
    {
        var codes = purchases.Where(p => p.TreeId.Equals(target, StringComparison.Ordinal))
            .Select(p => p.RequirementCode).ToHashSet(StringComparer.Ordinal);
        var selected = ranks[0];
        // Normal native refresh visits every slot, including slot zero. A NUL
        // requirement is free; the first missing purchase ends the traversal.
        foreach (var rank in ranks)
        {
            if (rank.RequirementCode != 0 && !codes.Contains(((char)rank.RequirementCode).ToString())) break;
            selected = rank;
        }
        return selected;
    }

    internal static void Validate(
        HeroClassDefinition hero, HeroLevelProfile profile, IReadOnlyList<HeroUpgradePurchase> purchases,
        List<string>? warnings = null)
    {
        if (hero.Equipment is not { } equipment) return;
        var targets = HeroSkillPurchaseTargets.Equipment(hero.Id);
        var weapon = Resolve(equipment.Weapon, targets["weapon"], purchases);
        var armour = Resolve(equipment.Armour, targets["armour"], purchases);
        if (weapon.Rank != profile.WeaponRank || armour.Rank != profile.ArmourRank || armour.Hp != profile.ArmourHp)
            throw new InvalidOperationException($"职业 '{hero.Id}' 的装备等级或 HP 与实际购买记录不一致，请刷新人物目录。");

        foreach (var (label, ranks, target, current) in new[]
        {
            ("武器", equipment.Weapon, targets["weapon"], weapon),
            ("护甲", equipment.Armour, targets["armour"], armour)
        })
        {
            if (ranks.Skip(current.Rank + 1).Any(rank => rank.RequirementCode == 0 || purchases.Any(p =>
                    p.TreeId == target && p.RequirementCode == ((char)rank.RequirementCode).ToString())))
                warnings?.Add($"{label}的后续档位已满足部分购买条件，但被未购买的前置档阻断；当前使用 rank {current.Rank}。");
        }
    }
}
