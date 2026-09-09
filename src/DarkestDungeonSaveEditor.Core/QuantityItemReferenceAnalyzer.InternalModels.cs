using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DarkestDungeonSaveEditor.Core;

internal static partial class QuantityItemReferenceAnalyzer
{
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
        public HashSet<string> ItemKeys { get; } = new(StringComparer.Ordinal);
        public HashSet<string> NestedTables { get; } = new(StringComparer.Ordinal);
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
                .Distinct(StringComparer.Ordinal)
                .Select(identity => new ItemIdentity(identity, ResolveIdentity(identity).ToArray()))
                .ToArray();
        }

        public IReadOnlyList<ItemIdentity> Identities { get; }

        public IEnumerable<string> Resolve(string type, string id)
        {
            if (string.IsNullOrWhiteSpace(type))
            {
                return [];
            }

            return _definitions
                .Where(definition => Matches(definition, type, id))
                .Select(definition => definition.CatalogKey)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        public IEnumerable<string> ResolveIdentity(string identity)
        {
            if (string.IsNullOrWhiteSpace(identity))
            {
                return [];
            }

            return _definitions
                .Where(definition =>
                    definition.DisplayId.Equals(identity, StringComparison.Ordinal))
                .Select(definition => definition.CatalogKey)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        private static bool Matches(QuantityItemDefinition definition, string type, string id)
        {
            if (definition.StorageKind == QuantityItemStorageKind.RaidInventory)
            {
                return definition.InventoryType.Equals(type, StringComparison.Ordinal) &&
                       definition.ItemId.Equals(id, StringComparison.Ordinal);
            }

            if (definition.StorageKind == QuantityItemStorageKind.EstateItems)
            {
                return definition.InventoryType.Equals(type, StringComparison.Ordinal) &&
                       definition.ItemId.Equals(id, StringComparison.Ordinal);
            }

            if (definition.InventoryType.Equals("heirloom", StringComparison.Ordinal))
            {
                return type.Equals("heirloom", StringComparison.Ordinal)
                    ? definition.ItemId.Equals(id, StringComparison.Ordinal)
                    : string.IsNullOrWhiteSpace(id) &&
                      definition.PersistedType.Equals(type, StringComparison.Ordinal);
            }

            return definition.InventoryType.Equals(type, StringComparison.Ordinal) &&
                   string.IsNullOrWhiteSpace(id);
        }
    }

    private sealed record ItemIdentity(string Identity, IReadOnlyList<string> CatalogKeys);
}
