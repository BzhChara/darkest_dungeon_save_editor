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
