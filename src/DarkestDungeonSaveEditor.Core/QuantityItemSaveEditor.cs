using System.Globalization;
using System.Text.Json.Nodes;

namespace DarkestDungeonSaveEditor.Core;

public static class QuantityItemSaveEditor
{
    public const int MaximumWalletAmount = 2_000_000_000;

    internal static void ValidateTargetAmount(QuantityItemDefinition definition, int targetAmount)
    {
        if (targetAmount < 0)
            throw new ArgumentOutOfRangeException(nameof(targetAmount), "Target amount cannot be negative.");
        if (definition.StorageKind == QuantityItemStorageKind.Wallet && targetAmount > MaximumWalletAmount)
            throw new ArgumentOutOfRangeException(nameof(targetAmount),
                EditorText.Format("QuantityItemSaveEditor_WalletAmountLimit", MaximumWalletAmount));
    }

    public static (JsonObject UpdatedRoot, QuantityItemMutationPreview Preview) SetAmount(
        JsonObject estateRoot,
        QuantityItemDefinition definition,
        int targetAmount)
    {
        ArgumentNullException.ThrowIfNull(estateRoot);
        ArgumentNullException.ThrowIfNull(definition);
        NativeInventoryIdentity.RequireWritable(definition.SaveIdentityIssue);
        ValidateTargetAmount(definition, targetAmount);

        var updated = estateRoot.DeepClone() as JsonObject
            ?? throw new InvalidDataException("Failed to clone decoded estate save.");
        var items = GetStorageItems(updated, definition.StorageKind);
        var matches = FindMatches(items, definition).ToArray();
        var existingAmount = SumAmounts(matches.Select(item => ReadRequiredAmount(item, definition.DisplayId)),
            definition.StorageKind, definition.DisplayId);
        if (existingAmount == targetAmount)
        {
            throw new InvalidOperationException(
                $"'{definition.DisplayId}' already has the requested amount {targetAmount.ToString(CultureInfo.InvariantCulture)}.");
        }

        var createdEntry = false;
        if (matches.Length == 0)
        {
            var nextKey = (FindHighestNumericKey(items) + 1).ToString(CultureInfo.InvariantCulture);
            items[nextKey] = CreateItem(definition, targetAmount);
            createdEntry = true;
        }
        else if (targetAmount > existingAmount)
        {
            var firstAmount = ReadRequiredAmount(matches[0], definition.DisplayId);
            matches[0]["amount"] = checked(firstAmount + (targetAmount - existingAmount));
        }
        else
        {
            var remaining = targetAmount;
            foreach (var match in matches)
            {
                var current = ReadRequiredAmount(match, definition.DisplayId);
                var next = Math.Min(current, remaining);
                match["amount"] = next;
                remaining -= next;
            }

            if (remaining != 0)
            {
                throw new InvalidDataException(
                    $"Failed to distribute the requested amount for '{definition.DisplayId}'.");
            }
        }

        var resultingAmount = CountAmount(updated, definition);
        if (resultingAmount != targetAmount)
        {
            throw new InvalidDataException(
                $"Quantity mutation failed for '{definition.DisplayId}': expected {targetAmount}, actual {resultingAmount}.");
        }

        return (
            updated,
            new QuantityItemMutationPreview(
                definition.DisplayId,
                definition.StorageKind,
                existingAmount,
                targetAmount,
                matches.Length,
                createdEntry));
    }

    public static int CountAmount(JsonObject estateRoot, QuantityItemDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(estateRoot);
        ArgumentNullException.ThrowIfNull(definition);
        var items = GetStorageItems(estateRoot, definition.StorageKind);
        return SumAmounts(FindMatches(items, definition).Select(item => ReadRequiredAmount(item, definition.DisplayId)),
            definition.StorageKind, definition.DisplayId);
    }

    private static JsonObject GetStorageItems(JsonObject estateRoot, QuantityItemStorageKind storageKind)
    {
        var baseRoot = JsonSupport.RequireObject(estateRoot, "base_root");
        return storageKind == QuantityItemStorageKind.Wallet
            ? JsonSupport.RequireObject(baseRoot, "wallet")
            : JsonSupport.RequireObject(baseRoot, "estate_items", "items");
    }

    private static IEnumerable<JsonObject> FindMatches(
        JsonObject items,
        QuantityItemDefinition definition)
    {
        var identity = NativeQuantityItemIdentity.FromDefinition(definition);
        return items.Select(pair => pair.Value).OfType<JsonObject>().Where(identity.Matches);
    }

    internal static int SumAmounts(IEnumerable<int> amounts, QuantityItemStorageKind storageKind, string itemId)
    {
        try
        {
            return amounts.Aggregate(
                0,
                (sum, amount) => storageKind == QuantityItemStorageKind.Wallet
                    ? (int)Math.Min(MaximumWalletAmount, (long)sum + amount)
                    : checked(sum + amount));
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

    private static JsonObject CreateItem(QuantityItemDefinition definition, int amount)
    {
        if (definition.StorageKind == QuantityItemStorageKind.Wallet)
        {
            return new JsonObject
            {
                ["amount"] = amount,
                ["type"] = definition.PersistedType
            };
        }

        return new JsonObject
        {
            ["id"] = definition.PersistedId,
            ["type"] = definition.PersistedType,
            ["amount"] = amount,
            ["added_buffs"] = 0,
            ["hero_name"] = string.Empty,
            ["previous_trinket_id"] = string.Empty,
            ["did_transform"] = false,
            ["trinkets_gained_count"] = 0
        };
    }

    private static int FindHighestNumericKey(JsonObject items)
    {
        var highest = -1;
        foreach (var key in items.Select(pair => pair.Key))
        {
            if (int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                highest = Math.Max(highest, value);
            }
        }

        return highest;
    }
}
