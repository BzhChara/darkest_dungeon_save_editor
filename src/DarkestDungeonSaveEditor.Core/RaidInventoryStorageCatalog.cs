using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace DarkestDungeonSaveEditor.Core;

public static partial class RaidInventoryStorageCatalog
{
    private const string InventoryConfigSuffix = ".inventory.system_configs.darkest";

    public static RaidInventoryStorageCatalogResult Load(ActiveContentSnapshot activeContent)
    {
        ArgumentNullException.ThrowIfNull(activeContent);
        var issues = new List<string>();
        var candidates = new List<ContentFileCandidate>();
        var enabledDlcPrefixes = ContentFileOverlay.GetEnabledDlcPrefixes(activeContent.Sources);
        foreach (var source in activeContent.Sources.OrderBy(item => item.LoadOrder))
        {
            foreach (var path in EnumerateInventoryConfigFiles(source, enabledDlcPrefixes, issues))
            {
                candidates.Add(new ContentFileCandidate(source, path));
            }
        }

        var parsedFiles = new List<ParsedInventoryFile>();
        foreach (var candidate in candidates)
        {
            var fullPath = Path.GetFullPath(candidate.Path);
            var virtualPath = ContentFileOverlay.NormalizeRelativePath(candidate.Source, fullPath);
            if (virtualPath is null)
            {
                issues.Add($"Ignored raid inventory config path outside its active content source: {fullPath}");
                continue;
            }

            parsedFiles.Add(ReadCandidate(candidate.Source, fullPath, virtualPath, issues));
        }

        var signals = ResolveEffectiveCapacitySignals(parsedFiles, issues);
        if (signals.Count == 0)
        {
            issues.Add("No active raid max_slots definition was found; expedition inventory editing is disabled.");
            return new RaidInventoryStorageCatalogResult(null, issues);
        }

        var highestPrioritySource = signals[0].Source;
        foreach (var signal in signals.Skip(1))
        {
            if (ContentFileOverlay.ComparePriority(signal.Source, highestPrioritySource) > 0)
            {
                highestPrioritySource = signal.Source;
            }
        }

        var winners = signals
            .Where(signal => ContentFileOverlay.ComparePriority(signal.Source, highestPrioritySource) == 0)
            .OrderBy(signal => signal.Description, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (winners.Any(signal => signal.IsInvalid))
        {
            issues.Add(
                "The highest-priority raid inventory configuration is unreadable or invalid; " +
                "expedition inventory editing is disabled: " +
                string.Join(" | ", winners.Where(signal => signal.IsInvalid).Select(signal => signal.Description)));
            return new RaidInventoryStorageCatalogResult(null, issues);
        }

        var definitions = winners.SelectMany(signal => signal.Definitions).ToArray();
        var capacities = definitions.Select(definition => definition.MaxSlots).Distinct().ToArray();
        if (capacities.Length != 1)
        {
            issues.Add(
                "The effective raid inventory configuration has no unique max_slots value; " +
                "expedition inventory editing is disabled: " +
                string.Join(" | ", definitions.Select(definition =>
                    $"{definition.Source}:{definition.MaxSlots.ToString(CultureInfo.InvariantCulture)}:{definition.SourcePath}")));
            return new RaidInventoryStorageCatalogResult(null, issues);
        }

        var winner = definitions
            .OrderBy(definition => definition.SourcePath, StringComparer.OrdinalIgnoreCase)
            .First();
        return new RaidInventoryStorageCatalogResult(winner, issues);
    }

    private static IReadOnlyList<CapacitySignal> ResolveEffectiveCapacitySignals(
        IReadOnlyList<ParsedInventoryFile> files,
        List<string> issues)
    {
        var result = new List<CapacitySignal>();
        foreach (var group in files
                     .GroupBy(file => file.VirtualPath, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            var highestPrioritySource = group.First().Source;
            foreach (var file in group.Skip(1))
            {
                if (ContentFileOverlay.ComparePriority(file.Source, highestPrioritySource) > 0)
                {
                    highestPrioritySource = file.Source;
                }
            }

            var winners = group
                .Where(file => ContentFileOverlay.ComparePriority(file.Source, highestPrioritySource) == 0)
                .OrderBy(file => file.Source.Id, StringComparer.OrdinalIgnoreCase)
                .ThenBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (winners
                    .Select(file => file.Source.Id)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Skip(1)
                    .Any())
            {
                var description = string.Join(" | ", winners.Select(file => $"{file.Source.Id}:{file.Path}"));
                issues.Add(
                    $"Raid inventory config '{group.Key}' has multiple providers at the same unverified priority and was ignored: " +
                    description);
                if (winners.Any(file => file.HasPotentialDefinition))
                {
                    result.Add(new CapacitySignal(
                        highestPrioritySource,
                        [],
                        true,
                        $"unresolved providers for {group.Key}: {description}"));
                }

                continue;
            }

            var winner = winners[0];
            if (winner.HasPotentialDefinition)
            {
                result.Add(new CapacitySignal(
                    winner.Source,
                    winner.Definitions,
                    winner.ReadFailed || winner.HasInvalidDefinition,
                    winner.Path));
            }
        }

        return result;
    }

    private static ParsedInventoryFile ReadCandidate(
        ActiveContentSource source,
        string path,
        string virtualPath,
        List<string> issues)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            var sourceSha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var text = StripLineComments(Encoding.UTF8.GetString(bytes));
            var mentionsRaid = RaidTypeTokenRegex().IsMatch(text);
            var definitions = new List<RaidInventoryStorageDefinition>();
            var foundRaidEntry = false;
            var invalid = false;
            foreach (Match entry in InventoryEntryRegex().Matches(text))
            {
                var body = entry.Groups["body"].Value;
                var typeMatch = InventoryTypeRegex().Match(body);
                if (!typeMatch.Success ||
                    !typeMatch.Groups["type"].Value.Equals("raid", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foundRaidEntry = true;
                var declarations = MaxSlotsDeclarationRegex().Matches(body);
                var values = MaxSlotsRegex().Matches(body);
                if (declarations.Count != 1 ||
                    values.Count != 1 ||
                    !int.TryParse(
                        values.Count == 1 ? values[0].Groups["slots"].Value : string.Empty,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var maxSlots) ||
                    maxSlots <= 0)
                {
                    invalid = true;
                    issues.Add($"raid inventory entry must contain exactly one positive max_slots value: {path}");
                    continue;
                }

                definitions.Add(new RaidInventoryStorageDefinition(
                    maxSlots,
                    source.Id,
                    path,
                    source,
                    sourceSha256));
            }

            if (mentionsRaid && !foundRaidEntry)
            {
                invalid = true;
                issues.Add($"Inventory config mentions raid storage but its entry could not be parsed: {path}");
            }

            return new ParsedInventoryFile(
                source,
                path,
                virtualPath,
                definitions,
                mentionsRaid,
                invalid,
                false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            issues.Add($"Failed to read raid inventory system config '{path}': {ex.Message}");
            return new ParsedInventoryFile(source, path, virtualPath, [], true, true, true);
        }
    }

    private static IReadOnlyList<string> EnumerateInventoryConfigFiles(
        ActiveContentSource source,
        IReadOnlyList<string> enabledDlcPrefixes,
        List<string> issues)
    {
        if (!Directory.Exists(source.Directory))
        {
            return [];
        }

        if (source.Kind is not ("workshop" or "local"))
        {
            var inventoryRoot = Path.Combine(source.Directory, "inventory");
            return Directory.Exists(inventoryRoot)
                ? Directory.EnumerateFiles(inventoryRoot, $"*{InventoryConfigSuffix}", SearchOption.AllDirectories)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray()
                : [];
        }

        var manifestPath = Path.Combine(source.Directory, "modfiles.txt");
        if (!File.Exists(manifestPath))
        {
            var inventoryRoot = Path.Combine(source.Directory, "inventory");
            if (!Directory.Exists(inventoryRoot))
            {
                return [];
            }

            var files = Directory.EnumerateFiles(
                    inventoryRoot,
                    $"*{InventoryConfigSuffix}",
                    SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (files.Length > 0)
            {
                issues.Add($"Mod has no modfiles.txt; raid inventory config scan used its standard inventory directory: {source.Directory}");
            }

            return files;
        }

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadLines(manifestPath))
        {
            var relative = ModManifestPath.Extract(line, InventoryConfigSuffix);
            if (relative is null)
            {
                continue;
            }

            var path = Path.GetFullPath(Path.Combine(source.Directory, relative));
            var relativeToRoot = Path.GetRelativePath(source.Directory, path);
            if (Path.IsPathRooted(relativeToRoot) ||
                relativeToRoot.Equals("..", StringComparison.Ordinal) ||
                relativeToRoot.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                !ContentFileOverlay.IsRootOrEnabledDlcPath(relativeToRoot, "inventory", enabledDlcPrefixes))
            {
                continue;
            }

            if (!File.Exists(path))
            {
                issues.Add($"Raid inventory config listed by Mod is missing: {path}");
            }

            result.Add(path);
        }

        return result.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string StripLineComments(string text)
    {
        var result = new StringBuilder(text.Length);
        var inQuotes = false;
        var escaped = false;
        for (var index = 0; index < text.Length; index++)
        {
            var current = text[index];
            if (!inQuotes && current == '/' && index + 1 < text.Length && text[index + 1] == '/')
            {
                index += 2;
                while (index < text.Length && text[index] is not ('\r' or '\n'))
                {
                    index++;
                }

                if (index >= text.Length)
                {
                    break;
                }

                current = text[index];
            }

            result.Append(current);
            if (current is '\r' or '\n')
            {
                inQuotes = false;
                escaped = false;
                continue;
            }

            if (inQuotes && current == '\\' && !escaped)
            {
                escaped = true;
                continue;
            }

            if (current == '"' && !escaped)
            {
                inQuotes = !inQuotes;
            }

            escaped = false;
        }

        return result.ToString();
    }

    [GeneratedRegex(
        @"inventory_system_config:\s*(?<body>.*?)(?=(?:\r?\n)?\s*inventory_system_config:|\z)",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex InventoryEntryRegex();

    [GeneratedRegex("\\.type\\s+\"(?<type>[^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex InventoryTypeRegex();

    [GeneratedRegex("\\.type\\s+\"raid\"", RegexOptions.IgnoreCase)]
    private static partial Regex RaidTypeTokenRegex();

    [GeneratedRegex(@"\.max_slots\s+(?<slots>-?\d+)(?=\s|\z)", RegexOptions.IgnoreCase)]
    private static partial Regex MaxSlotsRegex();

    [GeneratedRegex(@"\.max_slots\b", RegexOptions.IgnoreCase)]
    private static partial Regex MaxSlotsDeclarationRegex();

    private sealed record ParsedInventoryFile(
        ActiveContentSource Source,
        string Path,
        string VirtualPath,
        IReadOnlyList<RaidInventoryStorageDefinition> Definitions,
        bool HasPotentialDefinition,
        bool HasInvalidDefinition,
        bool ReadFailed);

    private sealed record CapacitySignal(
        ActiveContentSource Source,
        IReadOnlyList<RaidInventoryStorageDefinition> Definitions,
        bool IsInvalid,
        string Description);
}
