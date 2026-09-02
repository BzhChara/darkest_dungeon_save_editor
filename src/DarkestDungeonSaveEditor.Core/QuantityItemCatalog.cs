using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace DarkestDungeonSaveEditor.Core;

public static partial class QuantityItemCatalog
{
    private const string InventoryItemSuffix = ".inventory.items.darkest";
    private static readonly HashSet<string> WalletInventoryTypes =
        new(StringComparer.OrdinalIgnoreCase) { "gold", "heirloom", "shard" };
    private static readonly HashSet<string> EstateInventoryTypes =
        new(StringComparer.OrdinalIgnoreCase) { "estate", "estate_currency" };

    public static async Task<QuantityItemCatalogResult> LoadAsync(
        ActiveContentSnapshot activeContent,
        DsonSaveCodec codec,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(activeContent);
        ArgumentNullException.ThrowIfNull(codec);
        codec.ValidateAvailability();

        if (File.Exists(activeContent.Profile.RaidSavePath))
        {
            return await LoadRaidAsync(activeContent, codec, cancellationToken).ConfigureAwait(false);
        }

        var estatePath = Path.GetFullPath(activeContent.Profile.EstateSavePath);
        if (!File.Exists(estatePath))
        {
            throw new FileNotFoundException("persist.estate.json was not found in the selected profile.", estatePath);
        }

        var workspace = Path.Combine(activeContent.WorkspaceDirectory, "quantity_items");
        var sourceDirectory = Path.Combine(workspace, "source");
        var decodedDirectory = Path.Combine(workspace, "decoded");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(decodedDirectory);
        var sourceCopy = Path.Combine(sourceDirectory, "persist.estate.json");
        var decodedPath = Path.Combine(decodedDirectory, "persist.estate.json");
        var originalSha256 = ComputeSha256(estatePath);
        File.Copy(estatePath, sourceCopy, overwrite: false);
        if (!ComputeSha256(sourceCopy).Equals(originalSha256, StringComparison.OrdinalIgnoreCase) ||
            !ComputeSha256(estatePath).Equals(originalSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "persist.estate.json changed while the quantity-item catalog was being prepared.");
        }

        await codec.DecodeAsync(sourceCopy, decodedPath, cancellationToken).ConfigureAwait(false);
        return Load(activeContent, JsonSupport.ReadObject(decodedPath), originalSha256);
    }

    private static async Task<QuantityItemCatalogResult> LoadRaidAsync(
        ActiveContentSnapshot activeContent,
        DsonSaveCodec codec,
        CancellationToken cancellationToken)
    {
        var raidPath = Path.GetFullPath(activeContent.Profile.RaidSavePath);
        if (!File.Exists(raidPath))
        {
            throw new InvalidOperationException(
                "The expedition ended while the quantity-item catalog was being prepared; reload the profile.");
        }

        var workspace = Path.Combine(activeContent.WorkspaceDirectory, "quantity_items");
        var sourceDirectory = Path.Combine(workspace, "source");
        var decodedDirectory = Path.Combine(workspace, "decoded");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(decodedDirectory);
        var sourceCopy = Path.Combine(sourceDirectory, "persist.raid.json");
        var decodedPath = Path.Combine(decodedDirectory, "persist.raid.json");
        var originalSha256 = ComputeSha256(raidPath);
        File.Copy(raidPath, sourceCopy, overwrite: false);
        if (!File.Exists(raidPath) ||
            !ComputeSha256(sourceCopy).Equals(originalSha256, StringComparison.OrdinalIgnoreCase) ||
            !ComputeSha256(raidPath).Equals(originalSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "persist.raid.json changed while the expedition item catalog was being prepared.");
        }

        await codec.DecodeAsync(sourceCopy, decodedPath, cancellationToken).ConfigureAwait(false);
        return LoadRaid(activeContent, JsonSupport.ReadObject(decodedPath), originalSha256);
    }

    public static QuantityItemCatalogResult Load(
        ActiveContentSnapshot activeContent,
        JsonObject estateRoot,
        string sourceEstateSha256 = "")
    {
        ArgumentNullException.ThrowIfNull(activeContent);
        ArgumentNullException.ThrowIfNull(estateRoot);
        var issues = new List<string>();
        var definitions = LoadDefinitions(activeContent, QuantityItemSaveContext.Town, issues);
        var savedEntries = ReadSavedEntries(estateRoot, issues);
        var savedEntryCounts = savedEntries
            .GroupBy(entry => entry.CatalogKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Count(),
                StringComparer.OrdinalIgnoreCase);
        var amounts = savedEntries
            .GroupBy(entry => entry.CatalogKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => SumAmounts(group.Select(entry => entry.Amount), group.Key),
                StringComparer.OrdinalIgnoreCase);

        var referenceAnalysis = QuantityItemReferenceAnalyzer.Analyze(
            activeContent,
            definitions,
            QuantityItemSaveContext.Town,
            issues);
        var merged = new List<QuantityItemDefinition>(definitions.Count + savedEntries.Count);
        foreach (var definition in definitions)
        {
            amounts.Remove(definition.CatalogKey, out var currentAmount);
            savedEntryCounts.TryGetValue(definition.CatalogKey, out var savedEntryCount);
            referenceAnalysis.TryGetValue(definition.CatalogKey, out var reference);
            merged.Add(definition with
            {
                CurrentAmount = currentAmount,
                IsPresentInSave = savedEntryCount > 0,
                SavedEntryCount = savedEntryCount,
                ReferenceStatus = reference?.Status ?? QuantityItemReferenceStatus.AnalysisIncomplete,
                ReferenceEvidence = reference?.Evidence ?? []
            });
        }

        foreach (var entry in savedEntries
                     .Where(entry => amounts.ContainsKey(entry.CatalogKey))
                     .DistinctBy(entry => entry.CatalogKey, StringComparer.OrdinalIgnoreCase))
        {
            merged.Add(new QuantityItemDefinition(
                entry.PersistedType,
                entry.StorageKind == QuantityItemStorageKind.Wallet ? string.Empty : entry.PersistedId,
                entry.StorageKind,
                null,
                null,
                amounts[entry.CatalogKey],
                "save",
                string.Empty,
                false,
                [])
            {
                SourceLabel = "仅存档（当前内容未找到定义）",
                IsPresentInSave = true,
                SavedEntryCount = savedEntryCounts[entry.CatalogKey],
                ReferenceStatus = QuantityItemReferenceStatus.SaveOnly,
                ReferenceEvidence = ["当前存档包含该条目"]
            });
        }

        var requestedKeys = merged.SelectMany(GetLocalizationKeys).Distinct(StringComparer.OrdinalIgnoreCase);
        var localization = ContentLocalizationCatalog.Load(activeContent, requestedKeys);
        issues.AddRange(localization.Issues);
        var localized = merged
            .Select(definition => definition with
            {
                LocalizedName = ResolveLocalizedName(localization, definition),
                SourceLabel = definition.IsSaveOnly
                    ? definition.SourceLabel
                    : ContentSourceLabelFormatter.Format(
                        definition.Source,
                        definition.AllSources,
                        activeContent.Sources)
            })
            .OrderBy(definition => definition.DisplayId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(definition => definition.InventoryType, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new QuantityItemCatalogResult(
            localized,
            issues.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            sourceEstateSha256)
        {
            SaveContext = QuantityItemSaveContext.Town
        };
    }

    public static QuantityItemCatalogResult LoadRaid(
        ActiveContentSnapshot activeContent,
        JsonObject raidRoot,
        string sourceRaidSha256 = "")
    {
        ArgumentNullException.ThrowIfNull(activeContent);
        ArgumentNullException.ThrowIfNull(raidRoot);
        var issues = new List<string>();
        var occupiedSlots = JsonSupport.RequireObject(
            raidRoot,
            "base_root",
            "party",
            "inventory",
            "items").Count;
        var definitions = LoadDefinitions(activeContent, QuantityItemSaveContext.Raid, issues);
        var savedEntries = ReadSavedRaidEntries(raidRoot, issues);
        var savedEntryCounts = savedEntries
            .GroupBy(entry => entry.CatalogKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var amounts = savedEntries
            .GroupBy(entry => entry.CatalogKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => SumAmounts(group.Select(entry => entry.Amount), group.Key),
                StringComparer.OrdinalIgnoreCase);
        var referenceAnalysis = QuantityItemReferenceAnalyzer.Analyze(
            activeContent,
            definitions,
            QuantityItemSaveContext.Raid,
            issues);
        var merged = new List<QuantityItemDefinition>(definitions.Count + savedEntries.Count);
        foreach (var definition in definitions)
        {
            amounts.Remove(definition.CatalogKey, out var currentAmount);
            savedEntryCounts.TryGetValue(definition.CatalogKey, out var savedEntryCount);
            referenceAnalysis.TryGetValue(definition.CatalogKey, out var reference);
            merged.Add(definition with
            {
                CurrentAmount = currentAmount,
                IsPresentInSave = savedEntryCount > 0,
                SavedEntryCount = savedEntryCount,
                ReferenceStatus = reference?.Status ?? QuantityItemReferenceStatus.AnalysisIncomplete,
                ReferenceEvidence = reference?.Evidence ?? []
            });
        }

        foreach (var entry in savedEntries
                     .Where(entry => amounts.ContainsKey(entry.CatalogKey))
                     .DistinctBy(entry => entry.CatalogKey, StringComparer.OrdinalIgnoreCase))
        {
            merged.Add(new QuantityItemDefinition(
                entry.PersistedType,
                entry.PersistedId,
                QuantityItemStorageKind.RaidInventory,
                null,
                null,
                amounts[entry.CatalogKey],
                "save",
                string.Empty,
                false,
                [])
            {
                SourceLabel = "仅当前副本（活动内容未找到定义）",
                IsPresentInSave = true,
                SavedEntryCount = savedEntryCounts[entry.CatalogKey],
                ReferenceStatus = QuantityItemReferenceStatus.SaveOnly,
                ReferenceEvidence = ["当前副本背包包含该条目"]
            });
        }

        var requestedKeys = merged.SelectMany(GetLocalizationKeys).Distinct(StringComparer.OrdinalIgnoreCase);
        var localization = ContentLocalizationCatalog.Load(activeContent, requestedKeys);
        issues.AddRange(localization.Issues);
        var localized = merged
            .Select(definition => definition with
            {
                LocalizedName = ResolveLocalizedName(localization, definition),
                SourceLabel = definition.IsSaveOnly
                    ? definition.SourceLabel
                    : ContentSourceLabelFormatter.Format(
                        definition.Source,
                        definition.AllSources,
                        activeContent.Sources)
            })
            .OrderBy(definition => definition.DisplayId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(definition => definition.InventoryType, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var raidStorage = RaidInventoryStorageCatalog.Load(activeContent);
        issues.AddRange(raidStorage.Issues);
        return new QuantityItemCatalogResult(
            localized,
            issues.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            sourceRaidSha256)
        {
            SaveContext = QuantityItemSaveContext.Raid,
            RaidStorage = raidStorage.Storage,
            RaidOccupiedSlots = occupiedSlots
        };
    }

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

    private static IReadOnlyList<SavedQuantityEntry> ReadSavedEntries(JsonObject estateRoot, List<string> issues)
    {
        var result = new List<SavedQuantityEntry>();
        var baseRoot = JsonSupport.RequireObject(estateRoot, "base_root");
        var wallet = JsonSupport.RequireObject(baseRoot, "wallet");
        foreach (var pair in wallet)
        {
            if (pair.Value is not JsonObject item)
            {
                continue;
            }

            var persistedType = JsonSupport.ReadString(item, "type");
            var amount = JsonSupport.ReadInt(item, "amount");
            if (string.IsNullOrWhiteSpace(persistedType) || amount is null or < 0)
            {
                issues.Add($"Ignored invalid wallet quantity entry '{pair.Key}'.");
                continue;
            }

            result.Add(new SavedQuantityEntry(
                QuantityItemStorageKind.Wallet,
                persistedType,
                string.Empty,
                amount.Value));
        }

        var estateItems = JsonSupport.RequireObject(baseRoot, "estate_items", "items");
        foreach (var pair in estateItems)
        {
            if (pair.Value is not JsonObject item)
            {
                continue;
            }

            var persistedType = JsonSupport.ReadString(item, "type");
            var persistedId = JsonSupport.ReadString(item, "id");
            var amount = JsonSupport.ReadInt(item, "amount");
            if (string.IsNullOrWhiteSpace(persistedType) || string.IsNullOrWhiteSpace(persistedId) ||
                amount is null or < 0)
            {
                issues.Add($"Ignored invalid estate-item quantity entry '{pair.Key}'.");
                continue;
            }

            result.Add(new SavedQuantityEntry(
                QuantityItemStorageKind.EstateItems,
                persistedType,
                persistedId,
                amount.Value));
        }

        return result;
    }

    private static IReadOnlyList<SavedQuantityEntry> ReadSavedRaidEntries(
        JsonObject raidRoot,
        List<string> issues)
    {
        var result = new List<SavedQuantityEntry>();
        var items = JsonSupport.RequireObject(raidRoot, "base_root", "party", "inventory", "items");
        foreach (var pair in items)
        {
            if (pair.Value is not JsonObject item)
            {
                issues.Add($"Ignored invalid expedition inventory entry '{pair.Key}'.");
                continue;
            }

            var persistedType = JsonSupport.ReadString(item, "type");
            var persistedId = JsonSupport.ReadString(item, "id");
            var amount = JsonSupport.ReadInt(item, "amount");
            if (string.IsNullOrWhiteSpace(persistedType) || amount is null or < 0)
            {
                issues.Add($"Ignored invalid expedition inventory entry '{pair.Key}'.");
                continue;
            }

            if (persistedType.Equals("trinket", StringComparison.OrdinalIgnoreCase))
            {
                // Trinkets remain instance-based content even when carried in the raid bag.
                // They belong to the dedicated trinket workflow and must never be exposed as
                // an ordinary quantity row through the save-only fallback.
                continue;
            }

            result.Add(new SavedQuantityEntry(
                QuantityItemStorageKind.RaidInventory,
                persistedType,
                persistedId,
                amount.Value));
        }

        return result;
    }

    private static int SumAmounts(IEnumerable<int> amounts, string catalogKey)
    {
        try
        {
            return amounts.Aggregate(0, checked((sum, amount) => sum + amount));
        }
        catch (OverflowException ex)
        {
            throw new InvalidDataException($"Quantity total exceeds Int32 for '{catalogKey}'.", ex);
        }
    }

    private static IEnumerable<string> GetLocalizationKeys(QuantityItemDefinition definition)
    {
        foreach (var key in ContentLocalizationCatalog.GetInventoryItemKeys(
                     definition.InventoryType,
                     definition.ItemId))
        {
            yield return key;
        }

        if (definition.IsSaveOnly && definition.StorageKind == QuantityItemStorageKind.Wallet)
        {
            foreach (var key in ContentLocalizationCatalog.GetInventoryItemKeys(
                         "heirloom",
                         definition.PersistedType))
            {
                yield return key;
            }
        }
    }

    private static BilingualContentName ResolveLocalizedName(
        ContentLocalizationCatalog localization,
        QuantityItemDefinition definition)
    {
        var result = localization.GetInventoryItemName(definition.InventoryType, definition.ItemId);
        if (!definition.IsSaveOnly || definition.StorageKind != QuantityItemStorageKind.Wallet)
        {
            return result;
        }

        var heirloom = localization.GetInventoryItemName("heirloom", definition.PersistedType);
        return new BilingualContentName(
            string.IsNullOrWhiteSpace(result.Chinese) ? heirloom.Chinese : result.Chinese,
            string.IsNullOrWhiteSpace(result.English) ? heirloom.English : result.English);
    }

    private static IReadOnlyList<string> EnumerateInventoryItemFiles(
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
                ? Directory.EnumerateFiles(inventoryRoot, $"*{InventoryItemSuffix}", SearchOption.AllDirectories)
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
                    $"*{InventoryItemSuffix}",
                    SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (files.Length > 0)
            {
                issues.Add(
                    $"Mod has no modfiles.txt; quantity-item scan used its standard inventory directory: {source.Directory}");
            }

            return files;
        }

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in File.ReadLines(manifestPath))
        {
            var relativePath = ModManifestPath.Extract(rawLine, InventoryItemSuffix);
            if (relativePath is null)
            {
                continue;
            }

            var path = Path.GetFullPath(Path.Combine(source.Directory, relativePath));
            var relativeToRoot = Path.GetRelativePath(source.Directory, path);
            if (Path.IsPathRooted(relativeToRoot) ||
                relativeToRoot.Equals("..", StringComparison.Ordinal) ||
                relativeToRoot.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                issues.Add($"Ignored inventory-item manifest path outside its Mod directory: {rawLine.Trim()}");
                continue;
            }

            if (!ContentFileOverlay.IsRootOrEnabledDlcPath(relativeToRoot, "inventory", enabledDlcPrefixes))
            {
                continue;
            }

            if (!File.Exists(path))
            {
                issues.Add($"Inventory item file listed by Mod is missing: {path}");
                continue;
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

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    [GeneratedRegex(
        @"inventory_item:\s*(?<body>.*?)(?=(?:\r?\n)?\s*inventory_item:|\z)",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex InventoryItemEntryRegex();

    [GeneratedRegex("\\.type\\s+\"(?<type>[^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex InventoryTypeRegex();

    [GeneratedRegex("\\.id\\s+\"(?<id>[^\"]*)\"", RegexOptions.IgnoreCase)]
    private static partial Regex InventoryIdRegex();

    [GeneratedRegex(@"\.base_stack_limit\s+(?<limit>-?\d+)(?=\s|\z)", RegexOptions.IgnoreCase)]
    private static partial Regex BaseStackLimitRegex();

    [GeneratedRegex(@"\.estate_can_be_provision\s+(?<value>true|false)(?=\s|\z)", RegexOptions.IgnoreCase)]
    private static partial Regex EstateCanBeProvisionRegex();

    private sealed record ParsedItemDefinition(
        string InventoryType,
        string ItemId,
        QuantityItemStorageKind StorageKind,
        int? BaseStackLimit,
        bool? EstateCanBeProvision)
    {
        public string PersistedType => StorageKind == QuantityItemStorageKind.Wallet &&
                                       InventoryType.Equals("heirloom", StringComparison.OrdinalIgnoreCase)
            ? ItemId
            : InventoryType;
        public string PersistedId => StorageKind is QuantityItemStorageKind.EstateItems or
            QuantityItemStorageKind.RaidInventory
            ? ItemId
            : string.Empty;
        public string CatalogKey =>
            $"{StorageKind}:{PersistedType}:{PersistedId}".ToUpperInvariant();
    }

    private sealed record SavedQuantityEntry(
        QuantityItemStorageKind StorageKind,
        string PersistedType,
        string PersistedId,
        int Amount)
    {
        public string CatalogKey =>
            $"{StorageKind}:{PersistedType}:{PersistedId}".ToUpperInvariant();
    }
}
