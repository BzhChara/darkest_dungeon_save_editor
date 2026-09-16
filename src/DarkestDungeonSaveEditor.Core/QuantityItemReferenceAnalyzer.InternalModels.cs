using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DarkestDungeonSaveEditor.Core;

internal static partial class QuantityItemReferenceAnalyzer
{
    private sealed record ScannedContentFile(
        EffectiveContentFile File,
        string MountedPath,
        string Text,
        bool IsLootFile,
        NativeReferenceJsonKind JsonKind,
        bool IsCurioTypeFile);

    private enum ReferenceReachability
    {
        TownOnly,
        RaidCapable
    }

    private sealed class LootTableLibrary
    {
        public Dictionary<uint, List<LootTableVariant>> Tables { get; } = [];
        public HashSet<string> Codes { get; } = new(StringComparer.Ordinal);
    }

    private readonly record struct LootCode(uint Hash, string Name);

    private sealed class LootTableVariant(LootContext context, string source)
    {
        public LootContext Context { get; } = context;
        public string Source { get; } = source;
        public HashSet<string> ItemKeys { get; } = new(StringComparer.Ordinal);
        public HashSet<LootCode> NestedTables { get; } = [];
        public HashSet<string> UncertainItemKeys { get; } = new(StringComparer.Ordinal);
        public HashSet<LootCode> UncertainNestedTables { get; } = [];
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
                .Where(definition => definition.InventoryType.Equals(type, StringComparison.Ordinal) &&
                    definition.ItemId.Equals(id, StringComparison.Ordinal))
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

        public IEnumerable<string> ResolveCurrencyHash(string identity)
        {
            if (identity.Length == 0) return [];
            var hash = Loc2LocalizationReader.HashName(identity);
            // Currency consumers name the wallet type, not an inventory
            // variant's display ID. Estate currencies retain their item ID.
            return _definitions.Where(definition => Loc2LocalizationReader.HashName(
                    definition.StorageKind == QuantityItemStorageKind.Wallet
                        ? definition.PersistedType : definition.ItemId) == hash)
                .Select(definition => definition.CatalogKey).Distinct(StringComparer.Ordinal).ToArray();
        }
    }

    private sealed record ItemIdentity(string Identity, IReadOnlyList<string> CatalogKeys);
}
