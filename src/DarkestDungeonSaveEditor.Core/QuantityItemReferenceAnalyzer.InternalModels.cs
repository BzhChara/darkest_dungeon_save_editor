using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DarkestDungeonSaveEditor.Core;

internal static partial class QuantityItemReferenceAnalyzer
{
    [GeneratedRegex(
        "(?:^|\\r?\\n)\\s*(?:loot|extra_battle_loot|extra_curio_loot):\\s*\\.code\\s+\"(?<code>[^\"]+)\"",
        RegexOptions.IgnoreCase)]
    private static partial Regex DarkestLootCodeRegex();

    [GeneratedRegex(
        "\\.type\\s+\"(?<type>[^\"]+)\"[^\\r\\n]{0,320}?\\.id\\s+\"(?<id>[^\"]*)\"",
        RegexOptions.IgnoreCase)]
    private static partial Regex DarkestTypeThenIdRegex();

    [GeneratedRegex(
        "\\.id\\s+\"(?<id>[^\"]*)\"[^\\r\\n]{0,320}?\\.type\\s+\"(?<type>[^\"]+)\"",
        RegexOptions.IgnoreCase)]
    private static partial Regex DarkestIdThenTypeRegex();

    [GeneratedRegex("\\.(?:use_item_id|item_id)\\s+\"(?<id>[^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex DarkestItemIdRegex();

    [GeneratedRegex(@"(?<id>[A-Za-z0-9_.-]+)#(?<type>[A-Za-z0-9_.-]+)", RegexOptions.IgnoreCase)]
    private static partial Regex CsvTypedItemRegex();

    private sealed record ScannedContentFile(
        EffectiveContentFile File,
        string Text,
        bool IsLootFile);

    private enum ReferenceReachability
    {
        TownOnly,
        RaidCapable
    }

    private sealed class LootTableNode
    {
        public HashSet<string> ItemKeys { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> NestedTables { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class QuantityItemIndex
    {
        private readonly IReadOnlyList<QuantityItemDefinition> _definitions;

        public QuantityItemIndex(IReadOnlyList<QuantityItemDefinition> definitions)
        {
            _definitions = definitions;
            Identities = definitions
                .Select(definition => definition.DisplayId)
                .Where(identity => !string.IsNullOrWhiteSpace(identity))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(identity => new ItemIdentity(identity, ResolveIdentity(identity).ToArray()))
                .ToArray();
        }

        public IReadOnlyList<ItemIdentity> Identities { get; }

        public IEnumerable<string> Resolve(string type, string id)
        {
            type = type.Trim();
            id = id.Trim();
            if (string.IsNullOrWhiteSpace(type))
            {
                return [];
            }

            return _definitions
                .Where(definition => Matches(definition, type, id))
                .Select(definition => definition.CatalogKey)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public IEnumerable<string> ResolveIdentity(string identity)
        {
            identity = identity.Trim();
            if (string.IsNullOrWhiteSpace(identity))
            {
                return [];
            }

            return _definitions
                .Where(definition =>
                    definition.DisplayId.Equals(identity, StringComparison.OrdinalIgnoreCase))
                .Select(definition => definition.CatalogKey)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static bool Matches(QuantityItemDefinition definition, string type, string id)
        {
            if (definition.StorageKind == QuantityItemStorageKind.RaidInventory)
            {
                return definition.InventoryType.Equals(type, StringComparison.OrdinalIgnoreCase) &&
                       definition.ItemId.Equals(id, StringComparison.OrdinalIgnoreCase);
            }

            if (definition.StorageKind == QuantityItemStorageKind.EstateItems)
            {
                return definition.InventoryType.Equals(type, StringComparison.OrdinalIgnoreCase) &&
                       definition.ItemId.Equals(id, StringComparison.OrdinalIgnoreCase);
            }

            if (definition.InventoryType.Equals("heirloom", StringComparison.OrdinalIgnoreCase))
            {
                return type.Equals("heirloom", StringComparison.OrdinalIgnoreCase)
                    ? definition.ItemId.Equals(id, StringComparison.OrdinalIgnoreCase)
                    : string.IsNullOrWhiteSpace(id) &&
                      definition.PersistedType.Equals(type, StringComparison.OrdinalIgnoreCase);
            }

            return definition.InventoryType.Equals(type, StringComparison.OrdinalIgnoreCase) &&
                   string.IsNullOrWhiteSpace(id);
        }
    }

    private sealed record ItemIdentity(string Identity, IReadOnlyList<string> CatalogKeys);
}
