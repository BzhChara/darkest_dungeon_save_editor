using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DarkestDungeonSaveEditor.Core;

internal static partial class QuantityItemReferenceAnalyzer
{
    private static bool ReferencePathCanAffectContext(
        string mountedPath,
        QuantityItemSaveContext saveContext)
    {
        return NativeResourceFileRules.IsLootFile(mountedPath, []) ||
               IsReachableInContext(GetDefaultReachability(mountedPath), saveContext) ||
               saveContext == QuantityItemSaveContext.Town &&
               MayContainTownReachabilityOverride(mountedPath) ||
               saveContext == QuantityItemSaveContext.Raid &&
               MayContainRaidReachabilityOverride(mountedPath);
    }

    private static bool MayContainTownReachabilityOverride(string mountedPath)
    {
        // Only consumer-eligible files reach this diagnostic. Plot quests can
        // provide both rewards and provisions; unknown quest consumers remain
        // uncertain. Native queries can also accept names ending in Xjson.
        return mountedPath.StartsWith("campaign/quest/", StringComparison.OrdinalIgnoreCase) &&
               mountedPath.EndsWith("json", StringComparison.OrdinalIgnoreCase);
    }

    private static bool MayContainRaidReachabilityOverride(string mountedPath)
    {
        return mountedPath.StartsWith("campaign/town/districts/", StringComparison.OrdinalIgnoreCase) &&
               mountedPath.EndsWith("json", StringComparison.OrdinalIgnoreCase);
    }

    private static ReferenceReachability GetDefaultReachability(string mountedPath)
    {
        // The DLC mount prefix was removed using the active source inventory.
        // Only resource roots select a consumer: curios/upgrades/ is still Curio.
        if (mountedPath.StartsWith("campaign/provision/", StringComparison.OrdinalIgnoreCase) ||
            mountedPath.StartsWith("campaign/town/provision/", StringComparison.OrdinalIgnoreCase))
        {
            return ReferenceReachability.RaidCapable;
        }

        return mountedPath.StartsWith("campaign/town_events/", StringComparison.OrdinalIgnoreCase) ||
               mountedPath.StartsWith("campaign/estate/", StringComparison.OrdinalIgnoreCase) ||
               mountedPath.StartsWith("campaign/town/", StringComparison.OrdinalIgnoreCase) ||
               mountedPath.StartsWith("upgrades/", StringComparison.OrdinalIgnoreCase)
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
