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

        var effectiveFiles = NativeContentFileResolver.Resolve(candidates, activeContent.Sources, "Raid inventory config", issues);
        if (issues.Any(issue => issue.Contains("multiple providers", StringComparison.Ordinal)))
            return new RaidInventoryStorageCatalogResult(null, issues);
        var signals = effectiveFiles.Select(file => ReadCandidate(file.Source, file.Path, file.RelativePath, issues))
            .Where(file => file.HasPotentialDefinition)
            .Select(file => new CapacitySignal(file.Source, file.Definitions,
                file.ReadFailed || file.HasInvalidDefinition, file.Path)).ToArray();
        if (signals.Length == 0)
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
                ? NativeDirectoryDiscovery.EnumerateFiles(inventoryRoot, $"*{InventoryConfigSuffix}", SearchOption.AllDirectories)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray()
                : [];
        }

        var manifestPath = Path.Combine(source.Directory, "modfiles.txt");
        ModManifestFile.Require(manifestPath);

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in ModManifestFile.ReadEntries(manifestPath, InventoryConfigSuffix))
        {
            var relative = entry.RelativePath;

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
