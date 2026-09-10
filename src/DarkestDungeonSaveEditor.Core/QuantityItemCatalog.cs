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
        new(StringComparer.Ordinal) { "gold", "heirloom", "shard" };
    private static readonly HashSet<string> EstateInventoryTypes =
        new(StringComparer.Ordinal) { "estate", "estate_currency" };

    public static async Task<QuantityItemCatalogResult> LoadAsync(
        ActiveContentSnapshot activeContent,
        DsonSaveCodec codec,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(activeContent);
        ArgumentNullException.ThrowIfNull(codec);
        codec.ValidateAvailability();

        var scene = QuantityItemSaveScene.Read(activeContent);
        if (scene.Context == QuantityItemSaveContext.Raid)
        {
            var raidCatalog = await LoadRaidAsync(activeContent, codec, cancellationToken).ConfigureAwait(false);
            _ = QuantityItemSaveScene.Read(activeContent);
            return raidCatalog;
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
        _ = QuantityItemSaveScene.Read(activeContent);
        var catalog = Load(activeContent, JsonSupport.ReadObject(decodedPath), originalSha256);
        return scene.HasRaidResidue
            ? catalog with { Issues = catalog.Issues.Append("当前为小镇状态；残留副本文件已忽略，物品修改只作用于小镇库存。").ToArray() }
            : catalog;
    }

    private static async Task<QuantityItemCatalogResult> LoadRaidAsync(
        ActiveContentSnapshot activeContent,
        DsonSaveCodec codec,
        CancellationToken cancellationToken)
    {
        var raidPath = RaidSaveLocation.FromGame(activeContent.Profile.ProfileDirectory,
            JsonSupport.ReadObject(activeContent.DecodedGamePath)).RaidPath;
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
            .GroupBy(entry => entry.CatalogKey, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Count(),
                StringComparer.Ordinal);
        var amounts = savedEntries
            .GroupBy(entry => entry.CatalogKey, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => SumAmounts(group.Select(entry => entry.Amount), group.Key),
                StringComparer.Ordinal);

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
                     .DistinctBy(entry => entry.CatalogKey, StringComparer.Ordinal))
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

        var requestedKeys = merged.SelectMany(GetLocalizationKeys).Distinct(StringComparer.Ordinal);
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
            .GroupBy(entry => entry.CatalogKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var amounts = savedEntries
            .GroupBy(entry => entry.CatalogKey, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => SumAmounts(group.Select(entry => entry.Amount), group.Key),
                StringComparer.Ordinal);
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
                     .DistinctBy(entry => entry.CatalogKey, StringComparer.Ordinal))
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

        var requestedKeys = merged.SelectMany(GetLocalizationKeys).Distinct(StringComparer.Ordinal);
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

}
