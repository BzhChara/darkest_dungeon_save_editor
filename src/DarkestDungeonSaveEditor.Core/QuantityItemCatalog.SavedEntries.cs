using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace DarkestDungeonSaveEditor.Core;

public static partial class QuantityItemCatalog
{
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

}
