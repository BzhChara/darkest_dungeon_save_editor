using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace DarkestDungeonSaveEditor.Core;

public static partial class QuantityItemCatalog
{
    internal static IReadOnlyList<QuantityItemDefinition> LoadDefinitions(
        ActiveContentSnapshot activeContent,
        QuantityItemSaveContext saveContext = QuantityItemSaveContext.Town,
        List<string>? issues = null)
    {
        issues ??= [];
        var candidates = new List<ContentFileCandidate>();
        var enabledDlcPrefixes = ContentFileOverlay.GetEnabledDlcPrefixes(activeContent.Sources);
        foreach (var source in activeContent.Sources.OrderBy(item => item.LoadOrder))
        {
            foreach (var path in EnumerateInventoryItemFiles(source, enabledDlcPrefixes, issues))
            {
                candidates.Add(new ContentFileCandidate(source, path));
            }
        }

        var definitions = new Dictionary<string, List<QuantityItemDefinition>>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in ResolveSamePriorityFileConflicts(candidates))
        {
            ScanFile(file, saveContext, definitions, issues, forceProviderConflict: true);
        }

        foreach (var file in ContentFileOverlay.Resolve(candidates, "Inventory item definition", issues))
        {
            ScanFile(file, saveContext, definitions, issues);
        }

        var sourcesById = activeContent.Sources.ToDictionary(
            source => source.Id,
            StringComparer.OrdinalIgnoreCase);
        return definitions.Values
            .Select(group => MergeDefinitions(group, issues, sourcesById))
            .OrderBy(definition => definition.DisplayId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void ScanFile(
        EffectiveContentFile file,
        QuantityItemSaveContext saveContext,
        Dictionary<string, List<QuantityItemDefinition>> definitions,
        List<string> issues,
        bool forceProviderConflict = false)
    {
        try
        {
            var providerSources = ReadProviderSourcesByKey(file, saveContext);
            foreach (var parsed in ReadDefinitions(file.Path, saveContext))
            {
                var definition = new QuantityItemDefinition(
                    parsed.InventoryType,
                    parsed.ItemId,
                    parsed.StorageKind,
                    parsed.BaseStackLimit,
                    parsed.EstateCanBeProvision,
                    0,
                    file.Source.Id,
                    Path.GetFullPath(file.Path),
                    forceProviderConflict,
                    providerSources.TryGetValue(parsed.CatalogKey, out var sources)
                        ? sources
                        : [file.Source.Id]);
                if (!definitions.TryGetValue(definition.CatalogKey, out var group))
                {
                    group = [];
                    definitions[definition.CatalogKey] = group;
                }

                group.Add(definition);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            issues.Add($"Failed to read inventory item file '{file.Path}': {ex.Message}");
        }
    }

    private static IReadOnlyList<EffectiveContentFile> ResolveSamePriorityFileConflicts(
        IReadOnlyList<ContentFileCandidate> candidates)
    {
        var normalized = candidates
            .Select(candidate => new
            {
                Candidate = candidate,
                FullPath = Path.GetFullPath(candidate.Path),
                RelativePath = ContentFileOverlay.NormalizeRelativePath(
                    candidate.Source,
                    Path.GetFullPath(candidate.Path))
            })
            .Where(item => item.RelativePath is not null)
            .ToArray();
        var result = new List<EffectiveContentFile>();
        foreach (var group in normalized
                     .GroupBy(item => item.RelativePath!, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            var highestPrioritySource = group.First().Candidate.Source;
            foreach (var item in group.Skip(1))
            {
                if (ContentFileOverlay.ComparePriority(item.Candidate.Source, highestPrioritySource) > 0)
                {
                    highestPrioritySource = item.Candidate.Source;
                }
            }

            var winners = group
                .Where(item => ContentFileOverlay.ComparePriority(
                    item.Candidate.Source,
                    highestPrioritySource) == 0)
                .OrderBy(item => item.Candidate.Source.Id, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.FullPath, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (!winners
                    .Select(item => item.Candidate.Source.Id)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Skip(1)
                    .Any())
            {
                continue;
            }

            var providers = group
                .OrderBy(item => item.Candidate.Source.Id, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.FullPath, StringComparer.OrdinalIgnoreCase)
                .Select(item => new ContentFileProvider(item.Candidate.Source.Id, item.FullPath))
                .DistinctBy(
                    provider => $"{provider.SourceId}\n{provider.Path}",
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var providerSources = providers
                .Select(provider => provider.SourceId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var providerPaths = providers
                .Select(provider => provider.Path)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            result.AddRange(winners.Select(winner => new EffectiveContentFile(
                winner.Candidate.Source,
                winner.FullPath,
                group.Key,
                providerSources,
                providerPaths,
                providers)));
        }

        return result;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> ReadProviderSourcesByKey(
        EffectiveContentFile file,
        QuantityItemSaveContext saveContext)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in file.Providers)
        {
            try
            {
                foreach (var definition in ReadDefinitions(provider.Path, saveContext))
                {
                    if (!result.TryGetValue(definition.CatalogKey, out var sources))
                    {
                        sources = [];
                        result[definition.CatalogKey] = sources;
                    }

                    if (!sources.Contains(provider.SourceId, StringComparer.OrdinalIgnoreCase))
                    {
                        sources.Add(provider.SourceId);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
            {
                // Provenance is supplemental. The effective winning file is parsed separately.
            }
        }

        return result.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)pair.Value.ToArray(),
            StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<ParsedItemDefinition> ReadDefinitions(
        string path,
        QuantityItemSaveContext saveContext)
    {
        var text = StripLineComments(File.ReadAllText(path, Encoding.UTF8));
        var result = new List<ParsedItemDefinition>();
        foreach (Match entry in InventoryItemEntryRegex().Matches(text))
        {
            var body = entry.Groups["body"].Value;
            var typeMatch = InventoryTypeRegex().Match(body);
            if (!typeMatch.Success)
            {
                continue;
            }

            var inventoryType = typeMatch.Groups["type"].Value.Trim();
            var storageKind = ResolveStorageKind(inventoryType, saveContext);
            if (storageKind is null)
            {
                continue;
            }

            var idMatch = InventoryIdRegex().Match(body);
            var itemId = idMatch.Success ? idMatch.Groups["id"].Value.Trim() : string.Empty;
            if ((inventoryType.Equals("heirloom", StringComparison.OrdinalIgnoreCase) ||
                 storageKind == QuantityItemStorageKind.EstateItems) &&
                string.IsNullOrWhiteSpace(itemId))
            {
                continue;
            }

            var stackLimitMatch = BaseStackLimitRegex().Match(body);
            int? stackLimit = stackLimitMatch.Success && int.TryParse(
                stackLimitMatch.Groups["limit"].Value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsedLimit)
                ? parsedLimit
                : null;
            var provisionMatch = EstateCanBeProvisionRegex().Match(body);
            bool? estateCanBeProvision = provisionMatch.Success
                ? provisionMatch.Groups["value"].Value.Equals("true", StringComparison.OrdinalIgnoreCase)
                : null;
            result.Add(new ParsedItemDefinition(
                inventoryType,
                itemId,
                storageKind.Value,
                stackLimit,
                estateCanBeProvision));
        }

        return result;
    }

    private static QuantityItemStorageKind? ResolveStorageKind(
        string inventoryType,
        QuantityItemSaveContext saveContext)
    {
        if (saveContext == QuantityItemSaveContext.Raid)
        {
            return string.IsNullOrWhiteSpace(inventoryType) ||
                   inventoryType.Equals("trinket", StringComparison.OrdinalIgnoreCase)
                ? null
                : QuantityItemStorageKind.RaidInventory;
        }

        if (WalletInventoryTypes.Contains(inventoryType))
        {
            return QuantityItemStorageKind.Wallet;
        }

        return EstateInventoryTypes.Contains(inventoryType)
            ? QuantityItemStorageKind.EstateItems
            : null;
    }

    private static QuantityItemDefinition MergeDefinitions(
        IReadOnlyList<QuantityItemDefinition> candidates,
        List<string> issues,
        IReadOnlyDictionary<string, ActiveContentSource> sourcesById)
    {
        var highestPrioritySource = candidates[0];
        foreach (var candidate in candidates.Skip(1))
        {
            if (ComparePriority(candidate, highestPrioritySource, sourcesById) > 0)
            {
                highestPrioritySource = candidate;
            }
        }

        var winners = candidates
            .Where(candidate => ComparePriority(candidate, highestPrioritySource, sourcesById) == 0)
            .OrderBy(candidate => candidate.SourcePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var conflict = winners.Any(candidate => candidate.HasProviderConflict) || winners
            .Select(candidate => (
                candidate.InventoryType,
                candidate.ItemId,
                candidate.BaseStackLimit,
                candidate.EstateCanBeProvision))
            .Distinct()
            .Skip(1)
            .Any();
        if (conflict)
        {
            issues.Add(
                $"Quantity item '{candidates[0].DisplayId}' has conflicting definitions at the same priority; " +
                $"the deterministic first provider is shown: {string.Join(" | ", winners.Select(item => item.SourcePath))}");
        }

        var winner = winners[0];
        return winner with
        {
            HasProviderConflict = conflict,
            AllSources = candidates
                .SelectMany(candidate => candidate.AllSources)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }

    private static int ComparePriority(
        QuantityItemDefinition left,
        QuantityItemDefinition right,
        IReadOnlyDictionary<string, ActiveContentSource> sourcesById)
    {
        var leftSource = sourcesById[left.Source];
        var rightSource = sourcesById[right.Source];
        return ContentFileOverlay.ComparePriority(leftSource, rightSource);
    }

}
