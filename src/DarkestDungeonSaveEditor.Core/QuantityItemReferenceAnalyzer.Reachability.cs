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
        // Only consumer-eligible files reach this diagnostic. Plot quests can
        // provide both rewards and provisions; unknown quest consumers remain
        // uncertain. Native queries can also accept names ending in Xjson.
        return relativePath.Contains("campaign/quest/", StringComparison.OrdinalIgnoreCase) &&
               relativePath.EndsWith("json", StringComparison.OrdinalIgnoreCase);
    }

    private static bool MayContainRaidReachabilityOverride(string relativePath)
    {
        var normalized = $"/{relativePath.Replace('\\', '/').Trim('/')}";
        return normalized.Contains("/campaign/town/district", StringComparison.OrdinalIgnoreCase) &&
               normalized.EndsWith("json", StringComparison.OrdinalIgnoreCase);
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
