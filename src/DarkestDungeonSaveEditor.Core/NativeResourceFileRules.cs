using System.Text.RegularExpressions;

namespace DarkestDungeonSaveEditor.Core;

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
        if (mounted.StartsWith("campaign/town_events/", StringComparison.OrdinalIgnoreCase))
            return IsTownEventFile(mounted, []) || mounted is
                "campaign/town_events/town_events.settings.json" or
                "campaign/town_events/town_events.quest_type_event_guarantees.json";
        return true;
    }
}
