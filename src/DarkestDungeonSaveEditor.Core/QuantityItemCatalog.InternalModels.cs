using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace DarkestDungeonSaveEditor.Core;

public static partial class QuantityItemCatalog
{
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
