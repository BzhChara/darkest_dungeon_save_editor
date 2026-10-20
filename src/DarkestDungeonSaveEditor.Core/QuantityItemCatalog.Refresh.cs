using System.Text.Json.Nodes;

namespace DarkestDungeonSaveEditor.Core;

public static partial class QuantityItemCatalog
{
    /// <summary>Refresh saved state without rescanning definitions, references or translations.</summary>
    public static QuantityItemCatalogResult RefreshSavedAmounts(
        ActiveContentSnapshot content,
        QuantityItemCatalogResult cached,
        JsonObject saveRoot,
        string sourceSha256)
    {
        var issues = new List<string>();
        var entries = cached.SaveContext == QuantityItemSaveContext.Raid
            ? ReadSavedRaidEntries(saveRoot, issues) : ReadSavedEntries(saveRoot, issues);
        var saved = entries.GroupBy(entry => entry.NativeIdentity)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var merged = new List<QuantityItemDefinition>();
        var consumed = new HashSet<NativeQuantityItemIdentity>();
        foreach (var definition in cached.Items.OrderBy(item => item.IsSaveOnly))
        {
            var identity = NativeQuantityItemIdentity.FromDefinition(definition);
            saved.TryGetValue(identity, out var matches);
            if (definition.IsSaveOnly && (matches is null || consumed.Contains(identity)))
            {
                continue;
            }
            consumed.Add(identity);
            merged.Add(definition with
            {
                CurrentAmount = matches is null ? 0 : SumAmounts(matches.Select(entry => entry.Amount), identity),
                SavedEntryCount = matches?.Length ?? 0,
                IsPresentInSave = matches is not null
            });
        }

        var residue = new List<QuantityItemDefinition>();
        foreach (var (key, matches) in saved)
        {
            if (consumed.Contains(key)) continue;
            var entry = matches[0];
            residue.Add(new QuantityItemDefinition(entry.PersistedType,
                entry.StorageKind == QuantityItemStorageKind.Wallet ? string.Empty : entry.PersistedId,
                entry.StorageKind, null, null, SumAmounts(matches.Select(item => item.Amount), key),
                "save", string.Empty, cached.DefinitionReadFailures.Count > 0, [])
            {
                SourceLabel = cached.SaveContext == QuantityItemSaveContext.Raid
                    ? EditorText.Get("QuantityItemCatalog_Refresh_001") : EditorText.Get("QuantityItemCatalog_Refresh_002"),
                IsPresentInSave = true,
                SavedEntryCount = matches.Length,
                ReferenceStatus = QuantityItemReferenceStatus.SaveOnly,
                ReferenceEvidence = cached.DefinitionReadFailures.Count > 0
                    ? [EditorText.Get("QuantityItemCatalog_Refresh_003")]
                    : [cached.SaveContext == QuantityItemSaveContext.Raid
                        ? EditorText.Get("QuantityItemCatalog_Refresh_004") : EditorText.Get("QuantityItemCatalog_Refresh_005")]
            });
        }
        // Only previously unseen save-only IDs need a name lookup. Never reclassify cached definitions.
        if (residue.Count > 0)
        {
            var localization = ContentLocalizationCatalog.Load(content, residue.SelectMany(GetLocalizationKeys));
            issues.AddRange(localization.Issues);
            merged.AddRange(residue.Select(item => item with { LocalizedName = ResolveLocalizedName(localization, item) }));
        }
        return cached with
        {
            Items = merged.OrderBy(item => item.DisplayId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.InventoryType, StringComparer.OrdinalIgnoreCase).ToArray(),
            SourceSaveSha256 = sourceSha256,
            Issues = issues,
            RaidOccupiedSlots = cached.SaveContext == QuantityItemSaveContext.Raid
                ? JsonSupport.RequireObject(saveRoot, "base_root", "party", "inventory", "items").Count : 0
        };
    }
}
