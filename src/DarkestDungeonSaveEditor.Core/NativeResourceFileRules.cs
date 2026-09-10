using System.Text.RegularExpressions;

namespace DarkestDungeonSaveEditor.Core;

internal enum NativeReferenceJsonKind
{
    None, TownEvents, Provision, Estate, PlotQuests, QuestGeneration, QuestTypes,
    Districts, Upgrades, Building, Unverified
}

// Consumer queries, after manifest eligibility and before definition/reference
// parsing. Keep these independent of the same-path provider election.
internal static partial class NativeResourceFileRules
{
    // x64 build 27890: LootLibrary (0x140441410) and town-event loading
    // (0x1403E877E). Dots left unescaped here are unescaped in those queries.
    [GeneratedRegex(@"loot.json\z", RegexOptions.CultureInvariant)]
    private static partial Regex LootName();

    [GeneratedRegex(@"\.town_events.events.json\z", RegexOptions.CultureInvariant)]
    private static partial Regex TownEventName();

    // Loader queries: provision 0x1404503C0, estate 0x14044CDA0,
    // quests 0x140455680/0x140458980/0x14045CC90, districts 0x140559E60.
    [GeneratedRegex(@"provision.json\z", RegexOptions.CultureInvariant)]
    private static partial Regex ProvisionName();
    [GeneratedRegex(@"estate.json\z", RegexOptions.CultureInvariant)]
    private static partial Regex EstateName();
    [GeneratedRegex(@"quest.plot_quests.json\z", RegexOptions.CultureInvariant)]
    private static partial Regex PlotQuestName();
    [GeneratedRegex(@"quest.generation.json\z", RegexOptions.CultureInvariant)]
    private static partial Regex QuestGenerationName();
    [GeneratedRegex(@"quest.types.json\z", RegexOptions.CultureInvariant)]
    private static partial Regex QuestTypesName();
    [GeneratedRegex(@"quest.modifiers.json\z", RegexOptions.CultureInvariant)]
    private static partial Regex QuestModifiersName();
    [GeneratedRegex(@"/.*districts.json\z", RegexOptions.CultureInvariant)]
    private static partial Regex DistrictsName();
    [GeneratedRegex(@"\.upgrades\.json\z", RegexOptions.CultureInvariant)]
    private static partial Regex UpgradesName();

    // Catalog queries: trinkets 0x1403E8333, Buffs 0x1404A32BF,
    // quirks 0x1404DDC5F, camping 0x1404A4B5A. Only the first dots
    // in the trinket and Buff suffixes are escaped by the game.
    [GeneratedRegex(@"\.entries.trinkets.json\z", RegexOptions.CultureInvariant)]
    private static partial Regex TrinketName();
    [GeneratedRegex(@"\.buffs.json\z", RegexOptions.CultureInvariant)]
    private static partial Regex BuffName();
    [GeneratedRegex(@"quirk_library.json\z", RegexOptions.CultureInvariant)]
    private static partial Regex QuirkName();
    [GeneratedRegex(@"camping_skills.json\z", RegexOptions.CultureInvariant)]
    private static partial Regex CampingSkillName();

    // Inventory queries at 0x1404C7FE4/0x1404C808B and Effect query at
    // 0x1404E4965: only the leading dot is escaped by the native loader.
    [GeneratedRegex(@"\.inventory.items.darkest\z", RegexOptions.CultureInvariant)]
    private static partial Regex InventoryItemName();
    [GeneratedRegex(@"\.inventory.system_configs.darkest\z", RegexOptions.CultureInvariant)]
    private static partial Regex InventoryConfigName();
    [GeneratedRegex(@"\.effects.darkest\z", RegexOptions.CultureInvariant)]
    private static partial Regex EffectName();
    // PropLibrary queries at 0x1404D8A22/0x1404D8ACE.
    [GeneratedRegex(@"curio_type_library.csv\z", RegexOptions.CultureInvariant)]
    private static partial Regex CurioTypeName();
    [GeneratedRegex(@"curio_props.csv\z", RegexOptions.CultureInvariant)]
    private static partial Regex CurioPropName();

    internal static bool IsInventoryItemFile(string path, IReadOnlyList<string> prefixes)
    {
        var mounted = MountedPath(path, prefixes);
        return mounted.StartsWith("inventory/", StringComparison.OrdinalIgnoreCase) && InventoryItemName().IsMatch(mounted);
    }

    internal static bool IsInventoryConfigFile(string path, IReadOnlyList<string> prefixes)
    {
        var mounted = MountedPath(path, prefixes);
        return mounted.StartsWith("inventory/", StringComparison.OrdinalIgnoreCase) && InventoryConfigName().IsMatch(mounted);
    }

    internal static bool IsEffectFile(string path, IReadOnlyList<string> prefixes)
    {
        var mounted = MountedPath(path, prefixes);
        return mounted.StartsWith("effects/", StringComparison.OrdinalIgnoreCase) && EffectName().IsMatch(mounted);
    }

    internal static bool IsCurioTypeFile(string path, IReadOnlyList<string> prefixes)
    {
        var mounted = MountedPath(path, prefixes);
        return mounted.StartsWith("curios/", StringComparison.OrdinalIgnoreCase) && CurioTypeName().IsMatch(mounted);
    }

    internal static bool IsCurioPropFile(string path, IReadOnlyList<string> prefixes)
    {
        var mounted = MountedPath(path, prefixes);
        return mounted.StartsWith("curios/", StringComparison.OrdinalIgnoreCase) && CurioPropName().IsMatch(mounted);
    }

    [GeneratedRegex(@"/prop_definitions.json\z", RegexOptions.CultureInvariant)]
    private static partial Regex PropDefinitionsName();
    [GeneratedRegex(@"/trap_definitions.json\z", RegexOptions.CultureInvariant)]
    private static partial Regex TrapDefinitionsName();
    [GeneratedRegex(@"/obstacle_definitions.json\z", RegexOptions.CultureInvariant)]
    private static partial Regex ObstacleDefinitionsName();

    internal static bool IsTrinketFile(string path, IReadOnlyList<string> enabledDlcPrefixes)
    {
        var mounted = MountedPath(path, enabledDlcPrefixes);
        return mounted.StartsWith("trinkets/", StringComparison.OrdinalIgnoreCase) && TrinketName().IsMatch(mounted);
    }

    internal static bool IsBuffFile(string path, IReadOnlyList<string> enabledDlcPrefixes)
    {
        var mounted = MountedPath(path, enabledDlcPrefixes);
        return mounted.StartsWith("shared/buffs/", StringComparison.OrdinalIgnoreCase) && BuffName().IsMatch(mounted);
    }

    internal static bool IsQuirkFile(string path, IReadOnlyList<string> enabledDlcPrefixes)
    {
        var mounted = MountedPath(path, enabledDlcPrefixes);
        return mounted.StartsWith("shared/quirk/", StringComparison.OrdinalIgnoreCase) && QuirkName().IsMatch(mounted);
    }

    internal static bool IsCampingSkillFile(string path, IReadOnlyList<string> enabledDlcPrefixes)
    {
        var mounted = MountedPath(path, enabledDlcPrefixes);
        return mounted.StartsWith("raid/camping/", StringComparison.OrdinalIgnoreCase) && CampingSkillName().IsMatch(mounted);
    }

    internal static int PropResourceStage(string path, IReadOnlyList<string> enabledDlcPrefixes)
    {
        var mounted = MountedPath(path, enabledDlcPrefixes);
        // 0x1404D8770 opens three exact root paths, then searches three
        // nested families. A root name cannot use the search's wildcard dot.
        if (mounted.Equals("props/prop_definitions.json", StringComparison.OrdinalIgnoreCase)) return 0;
        if (mounted.Equals("props/obstacle_definitions.json", StringComparison.OrdinalIgnoreCase)) return 1;
        if (mounted.Equals("props/trap_definitions.json", StringComparison.OrdinalIgnoreCase)) return 2;
        if (!mounted.StartsWith("props/", StringComparison.OrdinalIgnoreCase) || !mounted[6..].Contains('/')) return -1;
        if (PropDefinitionsName().IsMatch(mounted)) return 3;
        if (TrapDefinitionsName().IsMatch(mounted)) return 4;
        return ObstacleDefinitionsName().IsMatch(mounted) ? 5 : -1;
    }

    internal static NativeReferenceJsonKind ReferenceJsonKind(string path, IReadOnlyList<string> enabledDlcPrefixes)
    {
        var mounted = MountedPath(path, enabledDlcPrefixes);
        if (IsTownEventFile(mounted, [])) return NativeReferenceJsonKind.TownEvents;
        if (mounted.StartsWith("campaign/provision/", StringComparison.OrdinalIgnoreCase) && ProvisionName().IsMatch(mounted))
            return NativeReferenceJsonKind.Provision;
        if (mounted.StartsWith("campaign/estate/", StringComparison.OrdinalIgnoreCase) && EstateName().IsMatch(mounted))
            return NativeReferenceJsonKind.Estate;
        if (mounted.StartsWith("campaign/quest/", StringComparison.OrdinalIgnoreCase))
        {
            if (PlotQuestName().IsMatch(mounted)) return NativeReferenceJsonKind.PlotQuests;
            if (QuestGenerationName().IsMatch(mounted)) return NativeReferenceJsonKind.QuestGeneration;
            if (QuestTypesName().IsMatch(mounted)) return NativeReferenceJsonKind.QuestTypes;
            if (QuestModifiersName().IsMatch(mounted) || mounted is
                "campaign/quest/quest.restriction.json" or "campaign/quest/quest.exit_penalty.json")
                return NativeReferenceJsonKind.Unverified;
        }
        if (mounted.StartsWith("campaign/town/districts/", StringComparison.OrdinalIgnoreCase) && DistrictsName().IsMatch(mounted))
            return NativeReferenceJsonKind.Districts;
        if (mounted.StartsWith("upgrades/", StringComparison.OrdinalIgnoreCase) && UpgradesName().IsMatch(mounted))
            return NativeReferenceJsonKind.Upgrades;
        // Building::Load (0x140536860) formats a query for the building ID,
        // e.g. stage_coach/.*stage_coach.building.json, within its directory.
        const string buildings = "campaign/town/buildings/";
        if (mounted.StartsWith(buildings, StringComparison.OrdinalIgnoreCase))
        {
            var rest = mounted[buildings.Length..];
            var slash = rest.IndexOf('/');
            if (slash > 0 && Regex.IsMatch(rest[(slash + 1)..],
                    Regex.Escape(rest[..slash]) + @".building.json\z", RegexOptions.CultureInvariant))
                return NativeReferenceJsonKind.Building;
        }
        return NativeReferenceJsonKind.None;
    }

    internal static string MountedPath(string path, IReadOnlyList<string> enabledDlcPrefixes)
    {
        var normalized = path.Replace('\\', '/');
        var prefix = enabledDlcPrefixes.OrderByDescending(value => value.Length)
            .FirstOrDefault(value => normalized.StartsWith(value + "/", StringComparison.OrdinalIgnoreCase));
        return prefix is null ? normalized : normalized[(prefix.Length + 1)..];
    }

    internal static bool IsLootFile(string path, IReadOnlyList<string> enabledDlcPrefixes)
    {
        var mounted = MountedPath(path, enabledDlcPrefixes);
        return mounted.StartsWith("loot/", StringComparison.OrdinalIgnoreCase) && LootName().IsMatch(mounted);
    }

    internal static bool IsTownEventFile(string path, IReadOnlyList<string> enabledDlcPrefixes)
    {
        var mounted = MountedPath(path, enabledDlcPrefixes);
        return mounted.StartsWith("campaign/town_events/", StringComparison.OrdinalIgnoreCase) && TownEventName().IsMatch(mounted);
    }

    internal static bool IsEligibleReferenceFile(string path, IReadOnlyList<string> enabledDlcPrefixes)
    {
        var mounted = MountedPath(path, enabledDlcPrefixes);
        if (mounted.StartsWith("loot/", StringComparison.OrdinalIgnoreCase) || LootName().IsMatch(mounted))
            return IsLootFile(mounted, []);
        if (mounted.EndsWith("json", StringComparison.OrdinalIgnoreCase))
            return ReferenceJsonKind(mounted, []) != NativeReferenceJsonKind.None;
        return true;
    }
}
