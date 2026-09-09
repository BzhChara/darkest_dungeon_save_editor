namespace DarkestDungeonSaveEditor.Core;

public static class RaidInventoryStorageCatalog
{
    public static RaidInventoryStorageCatalogResult Load(ActiveContentSnapshot activeContent)
    {
        ArgumentNullException.ThrowIfNull(activeContent);
        var issues = new List<string>();
        var capacity = InventorySystemConfigCatalog.ReadCapacity(activeContent.Sources, "raid", issues);
        var storage = capacity is null ? null : new RaidInventoryStorageDefinition(
            capacity.MaxSlots, capacity.Source.Id, capacity.SourcePath, capacity.Source, capacity.SourceSha256);
        return new RaidInventoryStorageCatalogResult(storage, issues);
    }
}
