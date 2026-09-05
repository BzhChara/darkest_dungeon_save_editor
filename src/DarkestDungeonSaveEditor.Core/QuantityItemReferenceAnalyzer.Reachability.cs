using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DarkestDungeonSaveEditor.Core;

internal static partial class QuantityItemReferenceAnalyzer
{
    private static bool ReferencePathCanAffectContext(
        string relativePath,
        QuantityItemSaveContext saveContext)
    {
        return IsLootPath(relativePath) ||
               IsReachableInContext(GetDefaultReachability(relativePath), saveContext) ||
               saveContext == QuantityItemSaveContext.Town &&
               MayContainTownReachabilityOverride(relativePath) ||
               saveContext == QuantityItemSaveContext.Raid &&
               MayContainRaidReachabilityOverride(relativePath);
    }

    private static bool MayContainTownReachabilityOverride(string relativePath)
    {
        // JSON roots outside the town directories can still contain currency_cost,
        // event_cost, completion rewards, currencies, or explicit estate/wallet inventory targets. If such
        // a file cannot be inspected, fail open instead of hiding town-side definitions.
        return Path.GetExtension(relativePath).Equals(".json", StringComparison.OrdinalIgnoreCase);
    }

    private static bool MayContainRaidReachabilityOverride(string relativePath)
    {
        var normalized = $"/{relativePath.Replace('\\', '/').Trim('/')}";
        return normalized.Contains("/campaign/town/district", StringComparison.OrdinalIgnoreCase) &&
               normalized.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
    }

    private static ReferenceReachability GetDefaultReachability(string relativePath)
    {
        var normalized = $"/{relativePath.Replace('\\', '/').Trim('/')}";
        if (normalized.Contains("/campaign/provision/", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("/campaign/town/provision/", StringComparison.OrdinalIgnoreCase))
        {
            return ReferenceReachability.RaidCapable;
        }

        return normalized.Contains("/campaign/town_events/", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("/campaign/estate/", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("/campaign/town/", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("/upgrades/", StringComparison.OrdinalIgnoreCase)
            ? ReferenceReachability.TownOnly
            : ReferenceReachability.RaidCapable;
    }

    private static ReferenceReachability GetJsonReachability(
        JsonElement node,
        string parentProperty,
        ReferenceReachability inherited)
    {
        if (parentProperty.Equals("currency_cost", StringComparison.OrdinalIgnoreCase) ||
            parentProperty.Equals("currencies", StringComparison.OrdinalIgnoreCase) ||
            parentProperty.Equals("quest_fail_keep_rates", StringComparison.OrdinalIgnoreCase) ||
            parentProperty.Equals("completion_reward", StringComparison.OrdinalIgnoreCase) ||
            ReadString(node, "system_config_type").Equals("quest_rewards", StringComparison.OrdinalIgnoreCase))
        {
            return ReferenceReachability.TownOnly;
        }

        if (parentProperty.Equals("additional_provisions", StringComparison.OrdinalIgnoreCase) ||
            ReadString(node, "system_config_type").Equals("quest_provision", StringComparison.OrdinalIgnoreCase))
        {
            return ReferenceReachability.RaidCapable;
        }

        var type = ReadString(node, "type");
        if (type.Equals("bonus_currency", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("event_cost", StringComparison.OrdinalIgnoreCase))
        {
            return ReferenceReachability.TownOnly;
        }

        var targetInventory = ReadString(node, "target_inventory");
        if (targetInventory.Equals("provision", StringComparison.OrdinalIgnoreCase) ||
            targetInventory.Equals("raid", StringComparison.OrdinalIgnoreCase))
        {
            return ReferenceReachability.RaidCapable;
        }

        if (targetInventory.Equals("estate", StringComparison.OrdinalIgnoreCase) ||
            targetInventory.Equals("wallet", StringComparison.OrdinalIgnoreCase))
        {
            return ReferenceReachability.TownOnly;
        }

        if (type.Equals("DistrictReplacementInventoryEffectBuffData", StringComparison.OrdinalIgnoreCase))
        {
            return ReferenceReachability.RaidCapable;
        }

        if (type.Equals("DistrictSupplyBuffData", StringComparison.OrdinalIgnoreCase))
        {
            var itemType = ReadString(node, "item_type");
            if (itemType.Equals("supply", StringComparison.OrdinalIgnoreCase) ||
                itemType.Equals("provision", StringComparison.OrdinalIgnoreCase) ||
                itemType.Equals("quest_item", StringComparison.OrdinalIgnoreCase))
            {
                return ReferenceReachability.RaidCapable;
            }
        }

        return inherited;
    }

    private static bool IsReachableInContext(
        ReferenceReachability reachability,
        QuantityItemSaveContext saveContext)
    {
        return saveContext switch
        {
            QuantityItemSaveContext.Town => reachability == ReferenceReachability.TownOnly,
            QuantityItemSaveContext.Raid => reachability == ReferenceReachability.RaidCapable,
            _ => false
        };
    }

}
