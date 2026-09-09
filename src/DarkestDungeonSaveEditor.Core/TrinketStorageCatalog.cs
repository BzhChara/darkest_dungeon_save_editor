namespace DarkestDungeonSaveEditor.Core;

public static class TrinketStorageCatalog
{
    public static TrinketStorageCatalogResult Load(ActiveContentSnapshot activeContent)
    {
        ArgumentNullException.ThrowIfNull(activeContent);
        return Load(activeContent.Sources);
    }

    public static TrinketStorageCatalogResult Load(IReadOnlyList<ActiveContentSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var issues = new List<string>();
        var capacity = InventorySystemConfigCatalog.ReadCapacity(sources, "trinket_storage", issues);
        var storage = capacity is null ? null : new TrinketStorageDefinition(
            capacity.MaxSlots, capacity.Source.Id, capacity.SourcePath, capacity.Source, capacity.SourceSha256);
        return new TrinketStorageCatalogResult(storage, issues);
    }
}
