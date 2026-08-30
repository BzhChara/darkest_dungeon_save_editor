using System.Text.Json.Nodes;

namespace DarkestDungeonSaveEditor.Core;

public static class TrinketSaveEditor
{
    public static SaveProfileSummary Summarize(JsonObject estateRoot)
    {
        var baseRoot = JsonSupport.RequireObject(estateRoot, "base_root");
        var items = JsonSupport.RequireObject(baseRoot, "trinkets", "items");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var copies = 0;
        foreach (var pair in items)
        {
            if (pair.Value is not JsonObject item ||
                !JsonSupport.ReadString(item, "type").Equals("trinket", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var id = JsonSupport.ReadString(item, "id");
            if (!string.IsNullOrWhiteSpace(id))
            {
                ids.Add(id);
                copies++;
            }
        }

        return new SaveProfileSummary(JsonSupport.ReadInt(baseRoot, "version") ?? 0, copies, ids.Count);
    }

    public static (JsonObject UpdatedRoot, TrinketMutationPreview Preview) AddCopies(
        JsonObject estateRoot,
        TrinketDefinition definition,
        int copies,
        int storageCapacity)
    {
        ArgumentNullException.ThrowIfNull(estateRoot);
        ArgumentNullException.ThrowIfNull(definition);
        if (copies is < 1 or > 999)
        {
            throw new ArgumentOutOfRangeException(nameof(copies), "Copies must be between 1 and 999.");
        }

        if (definition.IsStateful)
        {
            throw new InvalidOperationException(
                $"'{definition.Id}' uses stateful Fire's Edge fields ({string.Join(", ", definition.StatefulFields)}). " +
                "Phase 1 refuses to create an incomplete item instance.");
        }

        var updated = estateRoot.DeepClone() as JsonObject
            ?? throw new InvalidDataException("Failed to clone decoded estate save.");
        var baseRoot = JsonSupport.RequireObject(updated, "base_root");
        var items = JsonSupport.RequireObject(baseRoot, "trinkets", "items");
        var existingEntries = items.Count;
        var existingCopies = CountCopiesInItems(items, definition.Id);
        if (storageCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(storageCapacity),
                "Trinket storage capacity must be positive.");
        }

        if (existingEntries + copies > storageCapacity)
        {
            throw new InvalidOperationException(
                $"Adding {copies} trinket(s) would exceed the active storage capacity: " +
                $"{existingEntries} + {copies} > {storageCapacity}.");
        }

        var nextNumericKey = FindHighestNumericKey(items);

        for (var index = 0; index < copies; index++)
        {
            nextNumericKey++;
            items[nextNumericKey.ToString(System.Globalization.CultureInfo.InvariantCulture)] = new JsonObject
            {
                ["id"] = definition.Id,
                ["type"] = "trinket",
                ["amount"] = 1
            };
        }

        return (
            updated,
            new TrinketMutationPreview(
                definition.Id,
                copies,
                existingCopies,
                existingCopies + copies,
                existingEntries,
                items.Count,
                definition.Limit,
                storageCapacity));
    }

    public static int CountCopies(JsonObject estateRoot, string trinketId)
    {
        var baseRoot = JsonSupport.RequireObject(estateRoot, "base_root");
        var items = JsonSupport.RequireObject(baseRoot, "trinkets", "items");
        return CountCopiesInItems(items, trinketId);
    }

    private static int CountCopiesInItems(JsonObject items, string trinketId)
    {
        return items.Count(pair =>
            pair.Value is JsonObject item &&
            JsonSupport.ReadString(item, "type").Equals("trinket", StringComparison.OrdinalIgnoreCase) &&
            JsonSupport.ReadString(item, "id").Equals(trinketId, StringComparison.OrdinalIgnoreCase));
    }

    private static int FindHighestNumericKey(JsonObject items)
    {
        var highest = -1;
        foreach (var key in items.Select(pair => pair.Key))
        {
            if (int.TryParse(key, out var value))
            {
                highest = Math.Max(highest, value);
            }
        }

        return highest;
    }
}
