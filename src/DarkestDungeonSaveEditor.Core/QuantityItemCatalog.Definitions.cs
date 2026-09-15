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
        List<string>? issues = null,
        List<string>? readFailures = null)
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

        var definitions = new Dictionary<string, List<QuantityItemDefinition>>(StringComparer.Ordinal);
        foreach (var file in ResolveSamePriorityFileConflicts(candidates))
        {
            ScanFile(file, saveContext, definitions, issues, readFailures, forceProviderConflict: true);
        }

        foreach (var file in NativeContentFileResolver.Resolve(candidates, activeContent.Sources, "Inventory item definition", issues))
        {
            ScanFile(file, saveContext, definitions, issues, readFailures);
        }

        var collisionKeys = NativeResourceIdentity.FindCollisions(definitions.Values.SelectMany(group => group),
            item => item.DefinitionKey, item => (Loc2LocalizationReader.HashName(NativeJsonReader.CString(item.InventoryType)),
                Loc2LocalizationReader.HashName(NativeJsonReader.CString(item.ItemId))));
        if (collisionKeys.Count > 0) issues.Add("Inventory keys share native hashes and cannot be selected safely: " + string.Join(", ", collisionKeys));
        var merged = definitions.Values
            .Select(group => MergeDefinitions(group, issues))
            .Select(definition => collisionKeys.Contains(definition.DefinitionKey)
                ? definition with { HasProviderConflict = true, Source = "unresolved" } : definition)
            .GroupBy(definition => definition.CatalogKey, StringComparer.Ordinal)
            .Select(AggregateWalletDefinitions)
            .OrderBy(definition => definition.DisplayId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (var definition in merged.Where(item => item.SaveIdentityIssue.Length > 0))
            issues.Add($"Quantity item '{definition.DisplayId}' ({definition.SourcePath}): {definition.SaveIdentityIssue}");
        return merged;
    }

    private static void ScanFile(
        EffectiveContentFile file,
        QuantityItemSaveContext saveContext,
        Dictionary<string, List<QuantityItemDefinition>> definitions,
        List<string> issues,
        List<string>? readFailures,
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
                    providerSources.TryGetValue(parsed.DefinitionKey, out var sources)
                        ? sources
                        : [file.Source.Id]);
                if (!definitions.TryGetValue(definition.DefinitionKey, out var group))
                {
                    group = [];
                    definitions[definition.DefinitionKey] = group;
                }

                group.Add(definition);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            var reason = $"Failed to read inventory item file '{file.Path}': {ex.Message}";
            issues.Add(reason);
            readFailures?.Add(reason);
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
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var provider in file.Providers)
        {
            try
            {
                foreach (var definition in ReadDefinitions(provider.Path, saveContext))
                {
                    if (!result.TryGetValue(definition.DefinitionKey, out var sources))
                    {
                        sources = [];
                        result[definition.DefinitionKey] = sources;
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
            StringComparer.Ordinal);
    }

    private static IReadOnlyList<ParsedItemDefinition> ReadDefinitions(
        string path,
        QuantityItemSaveContext saveContext)
    {
        var result = new List<ParsedItemDefinition>();
        foreach (var (kind, body) in NativeDarkestReader.ReadRecords(path))
        {
            if (kind != "inventory_item") continue;
            var inventoryType = NativeDarkestReader.ReadString(body, ".type");
            if (inventoryType is null)
            {
                continue;
            }

            var storageKind = ResolveStorageKind(inventoryType, saveContext);
            if (storageKind is null)
            {
                continue;
            }

            var itemId = NativeDarkestReader.ReadString(body, ".id") ?? string.Empty;
            if ((inventoryType.Equals("heirloom", StringComparison.Ordinal) ||
                 storageKind == QuantityItemStorageKind.EstateItems) &&
                string.IsNullOrWhiteSpace(itemId))
            {
                continue;
            }

            result.Add(new ParsedItemDefinition(
                inventoryType,
                itemId,
                storageKind.Value,
                NativeDarkestReader.ReadInt(body, ".base_stack_limit"),
                NativeDarkestReader.ReadBoolean(body, ".estate_can_be_provision")));
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
                   inventoryType.Equals("trinket", StringComparison.Ordinal)
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
        List<string> issues)
    {
        // InventorySystem appends declarations in native file/line order;
        // GetItemInfo stops at the first matching type and ID hash (0x1404F1590).
        // Mod priority replaces paths, not every same-ID declaration globally.
        var winner = candidates[0];
        var conflict = candidates.Any(candidate => candidate.HasProviderConflict);
        if (conflict)
            issues.Add($"Quantity item '{winner.DisplayId}' has an unverified file provider priority.");
        return winner with
        {
            HasProviderConflict = conflict,
            AllSources = candidates.SelectMany(candidate => candidate.AllSources)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }

    private static QuantityItemDefinition AggregateWalletDefinitions(
        IGrouping<string, QuantityItemDefinition> definitions)
    {
        // Distinct native definitions may map to one wallet type. Select each
        // (type, id) first, then aggregate provenance and genuine uncertainty;
        // an authored gold/shard ID is not part of the saved wallet identity.
        var winner = definitions.First();
        return winner with
        {
            HasProviderConflict = definitions.Any(definition => definition.HasProviderConflict),
            AllSources = definitions.SelectMany(definition => definition.AllSources)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }
}
