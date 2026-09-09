using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace DarkestDungeonSaveEditor.Core;

public static partial class QuantityItemCatalog
{
    private sealed record ParsedItemDefinition(
        string InventoryType,
        string ItemId,
        QuantityItemStorageKind StorageKind,
        int? BaseStackLimit,
        bool? EstateCanBeProvision)
    {
        public string PersistedType => StorageKind == QuantityItemStorageKind.Wallet &&
                                       InventoryType.Equals("heirloom", StringComparison.Ordinal)
            ? ItemId
            : InventoryType;
        public string PersistedId => StorageKind is QuantityItemStorageKind.EstateItems or
            QuantityItemStorageKind.RaidInventory
            ? ItemId
            : string.Empty;
        public string CatalogKey =>
            QuantityItemDefinition.CreateCatalogKey(StorageKind, PersistedType, PersistedId);
    }

    private sealed record SavedQuantityEntry(
        QuantityItemStorageKind StorageKind,
        string PersistedType,
        string PersistedId,
        int Amount)
    {
        public string CatalogKey =>
            QuantityItemDefinition.CreateCatalogKey(StorageKind, PersistedType, PersistedId);
    }
}
