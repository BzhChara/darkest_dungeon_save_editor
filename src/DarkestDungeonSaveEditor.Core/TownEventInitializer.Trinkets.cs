using System.Text.Json.Nodes;

namespace DarkestDungeonSaveEditor.Core;

internal sealed partial class TownEventInitializer
{
    private void RetainTrinkets(IReadOnlyDictionary<string, JsonObject> saves, TownEventEffect effect, List<string> changes)
    {
        var quest = Root(saves, "quest");
        var protectedIds = Objects(quest["quests"])
            .SelectMany(item => Objects(item["completion_reward"]?["items_definition"]?["items"]))
            .Where(item => NativeInventoryIdentity.HashSavedField(Text(item, "type")) == Hash("trinket"))
            .Select(item => NativeInventoryIdentity.HashSavedField(Text(item, "id"))).ToHashSet();
        var storage = JsonSupport.RequireObject(Root(saves, "estate"), "trinkets", "items");
        var candidates = Objects(storage).Where(item => JsonSupport.ReadInt(item, "amount") > 0 &&
                NativeInventoryIdentity.HashSavedField(Text(item, "type")) == Hash("trinket") &&
                !protectedIds.Contains(NativeInventoryIdentity.HashSavedField(Text(item, "id"))))
            .Select(item => NativeInventoryIdentity.HashSavedField(Text(item, "id"))).ToList();
        var retained = quest["trinket_retention_ids"] as JsonArray
            ?? throw new InvalidDataException("Missing retained trinket list.");
        var count = Math.Min(Integer(effect.NumberData), candidates.Count);
        if (count < 0) throw new InvalidDataException("Negative trinket retention count.");
        for (var index = 0; index < count; index++)
        {
            var choice = _random.Next(candidates.Count);
            var id = candidates[choice];
            candidates.RemoveAt(choice);
            // Native selection is weighted per positive storage entry, then
            // removes one unit by hash; repeated copies are independent draws.
            var entry = storage.First(pair => pair.Value is JsonObject item &&
                JsonSupport.ReadInt(item, "amount") > 0 && NativeInventoryIdentity.HashSavedField(Text(item, "id")) == id &&
                NativeInventoryIdentity.HashSavedField(Text(item, "type")) == Hash("trinket"));
            var value = (JsonObject)entry.Value!;
            var amount = TownEventSaveEditor.RequiredInt(value, "amount");
            if (amount == 1) storage.Remove(entry.Key); else value["amount"] = amount - 1;
            retained.Add(unchecked((int)id));
        }
        changes.Add(EditorText.Format("TownEvent_StolenTrinkets", count));
    }
}
