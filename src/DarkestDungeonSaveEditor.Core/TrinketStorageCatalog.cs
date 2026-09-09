using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace DarkestDungeonSaveEditor.Core;

public static partial class TrinketStorageCatalog
{
    private const string InventoryConfigSuffix = ".inventory.system_configs.darkest";

    public static TrinketStorageCatalogResult Load(ActiveContentSnapshot activeContent)
    {
        ArgumentNullException.ThrowIfNull(activeContent);
        return Load(activeContent.Sources);
    }

    public static TrinketStorageCatalogResult Load(IReadOnlyList<ActiveContentSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var issues = new List<string>();
        var candidates = new List<ContentFileCandidate>();
        var enabledDlcPrefixes = ContentFileOverlay.GetEnabledDlcPrefixes(sources);
        foreach (var source in sources.OrderBy(item => item.LoadOrder))
        {
            foreach (var path in EnumerateInventoryConfigFiles(
                         source,
                         enabledDlcPrefixes,
                         issues))
            {
                candidates.Add(new ContentFileCandidate(source, path));
            }
        }

        var effectiveFiles = NativeContentFileResolver.Resolve(candidates, sources, "Inventory system config", issues);
        if (issues.Any(issue => issue.Contains("multiple providers", StringComparison.Ordinal)))
            return new TrinketStorageCatalogResult(null, issues);
        var signals = effectiveFiles.Select(file => ReadCandidate(file.Source, file.Path, file.RelativePath, issues))
            .Where(file => file.HasPotentialCapacityDefinition)
            .Select(file => new CapacitySignal(file.Source, file.Definitions,
                file.ReadFailed || file.HasInvalidDefinition, file.Path)).ToArray();
        if (signals.Length == 0)
        {
            issues.Add("No active trinket_storage max_slots definition was found; total storage capacity cannot be validated.");
            return new TrinketStorageCatalogResult(null, issues);
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
            .Where(signal =>
                ContentFileOverlay.ComparePriority(signal.Source, highestPrioritySource) == 0)
            .OrderBy(signal => signal.Description, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (winners.Any(signal => signal.IsInvalid))
        {
            issues.Add(
                "The highest-priority trinket_storage configuration is unreadable, invalid, or unresolved; " +
                "capacity validation was disabled instead of falling back to a lower-priority value: " +
                string.Join(" | ", winners.Where(signal => signal.IsInvalid).Select(signal => signal.Description)));
            return new TrinketStorageCatalogResult(null, issues);
        }

        var definitions = winners.SelectMany(signal => signal.Definitions).ToArray();
        if (definitions.Length == 0)
        {
            issues.Add("The highest-priority trinket_storage configuration produced no valid max_slots value.");
            return new TrinketStorageCatalogResult(null, issues);
        }

        var distinctCapacities = definitions
            .Select(definition => definition.MaxSlots)
            .Distinct()
            .ToArray();
        if (distinctCapacities.Length > 1)
        {
            issues.Add(
                "trinket_storage has conflicting max_slots definitions at the same effective priority and capacity validation was disabled: " +
                string.Join(" | ", definitions.Select(definition =>
                    $"{definition.Source}:{definition.MaxSlots.ToString(CultureInfo.InvariantCulture)}:{definition.SourcePath}")));
            return new TrinketStorageCatalogResult(null, issues);
        }

        var winner = definitions
            .OrderBy(definition => definition.SourcePath, StringComparer.OrdinalIgnoreCase)
            .First();
        return new TrinketStorageCatalogResult(winner, issues);
    }



    private static IReadOnlyList<string> EnumerateInventoryConfigFiles(
        ActiveContentSource source,
        IReadOnlyList<string> enabledDlcPrefixes,
        List<string> issues)
    {
        var root = source.Directory;
        if (!Directory.Exists(root))
        {
            return [];
        }

        if (source.Kind is not ("workshop" or "local"))
        {
            var inventoryRoot = Path.Combine(root, "inventory");
            return Directory.Exists(inventoryRoot)
                ? NativeDirectoryDiscovery.EnumerateFiles(
                        inventoryRoot,
                        $"*{InventoryConfigSuffix}",
                        SearchOption.AllDirectories)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray()
                : [];
        }

        var manifestPath = Path.Combine(root, "modfiles.txt");
        ModManifestFile.Require(manifestPath);

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in ModManifestFile.ReadEntries(manifestPath, InventoryConfigSuffix))
        {
            var rawLine = entry.RawLine;
            var relativePath = entry.RelativePath;

            var path = Path.GetFullPath(Path.Combine(root, relativePath));
            var relativeToRoot = Path.GetRelativePath(root, path);
            if (Path.IsPathRooted(relativeToRoot) ||
                relativeToRoot.Equals("..", StringComparison.Ordinal) ||
                relativeToRoot.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                issues.Add($"Ignored inventory config manifest path outside its Mod directory: {rawLine.Trim()}");
                continue;
            }

            if (!ContentFileOverlay.IsRootOrEnabledDlcPath(
                    relativeToRoot,
                    "inventory",
                    enabledDlcPrefixes))
            {
                continue;
            }

            if (!File.Exists(path))
            {
                issues.Add($"Inventory system config listed by Mod is missing: {path}");
                result.Add(path);
                continue;
            }

            result.Add(path);
        }

        return result.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
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
            var sourceSha256 = Convert.ToHexString(SHA256.HashData(bytes));
            var text = StripLineComments(Encoding.UTF8.GetString(bytes));
            var mentionsStorage = text.Contains("trinket_storage", StringComparison.OrdinalIgnoreCase);
            var definitions = new List<TrinketStorageDefinition>();
            var foundStorageEntry = false;
            var hasInvalidDefinition = false;
            foreach (Match entry in InventoryEntryRegex().Matches(text))
            {
                var body = entry.Groups["body"].Value;
                var typeMatch = InventoryTypeRegex().Match(body);
                if (!typeMatch.Success)
                {
                    if (body.Contains("trinket_storage", StringComparison.OrdinalIgnoreCase))
                    {
                        issues.Add($"A trinket_storage inventory entry has an invalid type declaration: {path}");
                        hasInvalidDefinition = true;
                    }

                    continue;
                }

                if (!typeMatch.Groups["type"].Value.Equals("trinket_storage", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foundStorageEntry = true;
                var slotDeclarations = MaxSlotsDeclarationRegex().Matches(body);
                var slotsMatches = MaxSlotsRegex().Matches(body);
                if (slotDeclarations.Count != 1 ||
                    slotsMatches.Count != 1 ||
                    !int.TryParse(
                        slotsMatches.Count == 1 ? slotsMatches[0].Groups["slots"].Value : string.Empty,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var maxSlots) ||
                    maxSlots <= 0)
                {
                    issues.Add(
                        $"trinket_storage entry must contain exactly one complete positive max_slots value: {path}");
                    hasInvalidDefinition = true;
                    continue;
                }

                definitions.Add(new TrinketStorageDefinition(
                    maxSlots,
                    source.Id,
                    path,
                    source,
                    sourceSha256));
            }

            if (mentionsStorage && !foundStorageEntry)
            {
                issues.Add($"Inventory config mentions trinket_storage but its entry could not be parsed: {path}");
                hasInvalidDefinition = true;
            }

            return new ParsedInventoryFile(
                source,
                path,
                virtualPath,
                definitions,
                mentionsStorage,
                hasInvalidDefinition,
                false);
        }
        catch (Exception ex)
        {
            issues.Add($"Failed to read inventory system config '{path}': {ex.Message}");
            return new ParsedInventoryFile(source, path, virtualPath, [], true, true, true);
        }
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

    [GeneratedRegex(@"\.max_slots\s+(?<slots>-?\d+)(?=\s|\z)", RegexOptions.IgnoreCase)]
    private static partial Regex MaxSlotsRegex();

    [GeneratedRegex(@"\.max_slots\b", RegexOptions.IgnoreCase)]
    private static partial Regex MaxSlotsDeclarationRegex();

    private sealed record ParsedInventoryFile(
        ActiveContentSource Source,
        string Path,
        string VirtualPath,
        IReadOnlyList<TrinketStorageDefinition> Definitions,
        bool HasPotentialCapacityDefinition,
        bool HasInvalidDefinition,
        bool ReadFailed);

    private sealed record CapacitySignal(
        ActiveContentSource Source,
        IReadOnlyList<TrinketStorageDefinition> Definitions,
        bool IsInvalid,
        string Description);
}
