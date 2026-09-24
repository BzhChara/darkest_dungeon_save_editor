using System.Globalization;
using System.Text.Json.Nodes;

namespace DarkestDungeonSaveEditor.Core;

public static class RaidInventorySaveEditor
{
    public static (JsonObject UpdatedRoot, QuantityItemMutationPreview Preview) SetAmount(
        JsonObject raidRoot,
        QuantityItemDefinition definition,
        int targetAmount,
        int inventoryCapacity)
    {
        ArgumentNullException.ThrowIfNull(raidRoot);
        ArgumentNullException.ThrowIfNull(definition);
        NativeInventoryIdentity.RequireWritable(definition.SaveIdentityIssue);
        if (definition.StorageKind != QuantityItemStorageKind.RaidInventory)
        {
            throw new ArgumentException("The selected item does not target the expedition inventory.", nameof(definition));
        }

        if (targetAmount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetAmount), "Target amount cannot be negative.");
        }

        if (inventoryCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(inventoryCapacity), "Inventory capacity must be positive.");
        }

        var updated = raidRoot.DeepClone() as JsonObject
            ?? throw new InvalidDataException("Failed to clone decoded expedition save.");
        var items = GetItems(updated);
        if (items.Count > inventoryCapacity)
        {
            throw new InvalidDataException(
                $"The current expedition inventory already contains {items.Count} slots, above the active capacity {inventoryCapacity}.");
        }

        var matches = FindMatches(items, definition).ToArray();
        var existingAmount = SumAmounts(matches.Select(match => match.Item), definition.DisplayId);
        var hasZeroAmountStack = matches.Any(match =>
            ReadRequiredAmount(match.Item, definition.DisplayId) == 0);
        if (existingAmount == targetAmount && !hasZeroAmountStack)
        {
            throw new InvalidOperationException(
                $"'{definition.DisplayId}' already has the requested amount {targetAmount.ToString(CultureInfo.InvariantCulture)}.");
        }

        var existingInventoryEntries = items.Count;
        var createdEntries = 0;
        var removedEntries = 0;
        if (targetAmount <= existingAmount)
        {
            var remaining = targetAmount;
            foreach (var match in matches)
            {
                var current = ReadRequiredAmount(match.Item, definition.DisplayId);
                var next = Math.Min(current, remaining);
                remaining -= next;
                if (next == 0)
                {
                    items.Remove(match.Key);
                    removedEntries++;
                }
                else
                {
                    match.Item["amount"] = next;
                }
            }

            if (remaining != 0)
            {
                throw new InvalidDataException(
                    $"Failed to distribute the requested expedition amount for '{definition.DisplayId}'.");
            }
        }
        else
        {
            var stackLimit = definition.BaseStackLimit;
            if (stackLimit is null or <= 0)
            {
                throw new InvalidOperationException(
                    $"'{definition.DisplayId}' has no valid active stack limit, so its expedition amount cannot be increased safely.");
            }

            var remainingIncrease = targetAmount - existingAmount;
            foreach (var match in matches)
            {
                var current = ReadRequiredAmount(match.Item, definition.DisplayId);
                var available = Math.Max(0, stackLimit.Value - current);
                var added = Math.Min(available, remainingIncrease);
                if (added > 0)
                {
                    match.Item["amount"] = checked(current + added);
                    remainingIncrease -= added;
                }

                if (remainingIncrease == 0)
                {
                    break;
                }
            }

            while (remainingIncrease > 0)
            {
                if (items.Count >= inventoryCapacity)
                {
                    throw new InvalidOperationException(
                        EditorText.Format("RaidInventorySaveEditor_001", definition.DisplayId) +
                        EditorText.Format("RaidInventorySaveEditor_002", items.Count, inventoryCapacity));
                }

                var slot = FindFirstFreeSlot(items, inventoryCapacity);
                var stackAmount = Math.Min(stackLimit.Value, remainingIncrease);
                items[slot] = CreateItem(definition, stackAmount);
                remainingIncrease -= stackAmount;
                createdEntries++;
            }
        }

        var resultingAmount = CountAmount(updated, definition);
        if (resultingAmount != targetAmount)
        {
            throw new InvalidDataException(
                $"Expedition quantity mutation failed for '{definition.DisplayId}': " +
                $"expected {targetAmount}, actual {resultingAmount}.");
        }

        var resultingMatches = FindMatches(items, definition).Count();
        return (
            updated,
            new QuantityItemMutationPreview(
                definition.DisplayId,
                definition.StorageKind,
                existingAmount,
                targetAmount,
                matches.Length,
                createdEntries > 0)
            {
                ResultingMatchingEntries = resultingMatches,
                ExistingInventoryEntries = existingInventoryEntries,
                ResultingInventoryEntries = items.Count,
                CreatedEntries = createdEntries,
                RemovedEntries = removedEntries,
                InventoryCapacity = inventoryCapacity
            });
    }

    public static int CountAmount(JsonObject raidRoot, QuantityItemDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(raidRoot);
        ArgumentNullException.ThrowIfNull(definition);
        return SumAmounts(
            FindMatches(GetItems(raidRoot), definition).Select(match => match.Item),
            definition.DisplayId);
    }

    private static JsonObject GetItems(JsonObject raidRoot) =>
        JsonSupport.RequireObject(raidRoot, "base_root", "party", "inventory", "items");

    private static IEnumerable<SavedRaidItem> FindMatches(
        JsonObject items,
        QuantityItemDefinition definition)
    {
        return items
            .Where(pair => pair.Value is JsonObject item &&
                           JsonSupport.ReadRawString(item, "type")
                               .Equals(definition.PersistedType, StringComparison.Ordinal) &&
                           JsonSupport.ReadRawString(item, "id")
                               .Equals(definition.PersistedId, StringComparison.Ordinal))
            .Select(pair => new SavedRaidItem(pair.Key, (JsonObject)pair.Value!))
            .OrderBy(match => TryParseSlot(match.Key, out var slot) ? slot : int.MaxValue)
            .ThenBy(match => match.Key, StringComparer.OrdinalIgnoreCase);
    }

    private static int SumAmounts(IEnumerable<JsonObject> items, string itemId)
    {
        try
        {
            return items.Aggregate(0, (sum, item) => checked(sum + ReadRequiredAmount(item, itemId)));
        }
        catch (OverflowException ex)
        {
            throw new InvalidDataException($"Quantity total exceeds Int32 for '{itemId}'.", ex);
        }
    }

    private static int ReadRequiredAmount(JsonObject item, string itemId)
    {
        var amount = JsonSupport.ReadInt(item, "amount");
        if (amount is null or < 0)
        {
            throw new InvalidDataException($"'{itemId}' has a missing, invalid, or negative amount.");
        }

        return amount.Value;
    }

    private static string FindFirstFreeSlot(JsonObject items, int inventoryCapacity)
    {
        for (var slot = 0; slot < inventoryCapacity; slot++)
        {
            var key = slot.ToString(CultureInfo.InvariantCulture);
            if (!items.ContainsKey(key))
            {
                return key;
            }
        }

        throw new InvalidOperationException("The expedition inventory has no addressable empty slot.");
    }

    private static JsonObject CreateItem(QuantityItemDefinition definition, int amount) => new()
    {
        ["id"] = definition.PersistedId,
        ["type"] = definition.PersistedType,
        ["amount"] = amount
    };

    private static bool TryParseSlot(string key, out int slot) =>
        int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out slot) && slot >= 0;

    private sealed record SavedRaidItem(string Key, JsonObject Item);
}
