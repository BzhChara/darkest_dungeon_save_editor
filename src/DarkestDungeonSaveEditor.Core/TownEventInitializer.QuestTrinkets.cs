using System.Text.Json.Nodes;

namespace DarkestDungeonSaveEditor.Core;

internal sealed partial class TownEventInitializer
{
    private sealed record EventTrinket(TrinketDefinition Definition, string Dungeon, IReadOnlyList<uint> Transforms);
    private Dictionary<uint, EventTrinket>? _eventTrinkets;
    private Dictionary<uint, EventTrinket> EventTrinkets => _eventTrinkets ??= LoadEventTrinkets();
    private Dictionary<uint, int>? _rarityRanks;

    private int RarityRank(string id)
    {
        _rarityRanks ??= ReadMatchingResources("trinkets", "\\.rarities.trinkets.json")
            .SelectMany(root => Objects(root["rarities"]))
            .Select((row, index) => (Id: Hash(Text(row, "id")), Rank: index))
            .GroupBy(row => row.Id).ToDictionary(group => group.Key, group => group.First().Rank);
        return _rarityRanks.TryGetValue(Hash(id), out var rank) ? rank :
            throw new InvalidDataException($"Unresolved event quest rarity: {id}.");
    }

    private IEnumerable<uint> OrderedRetainedTrinkets(IReadOnlyDictionary<string, JsonObject> saves) =>
        (Root(saves, "quest")["trinket_retention_ids"] as JsonArray ?? throw new InvalidDataException("Missing retained trinket list."))
            .Select(value => unchecked((uint)value!.GetValue<int>()))
            .OrderBy(value => RarityRank(RequireEventTrinket(value).Rarity));

    private Dictionary<uint, EventTrinket> LoadEventTrinkets()
    {
        var files = new Dictionary<string, JsonObject>();
        var result = new Dictionary<uint, EventTrinket>();
        foreach (var definition in TrinketCatalog.Load(content).Trinkets)
        {
            if (!files.TryGetValue(definition.SourcePath, out var root))
                files[definition.SourcePath] = root = ReadResource(definition.SourcePath);
            var entry = Objects(root["entries"]).FirstOrDefault(item => Hash(Text(item, "id")) == Hash(definition.Id))
                ?? throw new InvalidDataException($"Missing effective trinket: {definition.Id}.");
            var transforms = new[] { "transform_on_quest_complete", "transform_on_wearer_killed", "transform_on_trigger_limit_exhausted" }
                .Select(key => Hash(Text(entry, key))).Where(hash => hash != 0).ToArray();
            result[Hash(definition.Id)] = new EventTrinket(definition, Text(entry, "origin_dungeon"), transforms);
        }
        return result;
    }

    private TrinketDefinition RequireEventTrinket(uint id)
    {
        if (!EventTrinkets.TryGetValue(id, out var item) || item.Definition.HasProviderConflict)
            throw new InvalidDataException($"Unresolved event trinket identity: {unchecked((int)id)}.");
        NativeInventoryIdentity.RequireWritable(item.Definition.SaveIdentityIssue);
        return item.Definition;
    }

    private bool TrinketLimitReached(TrinketDefinition definition, IReadOnlyDictionary<uint, int> owned)
    {
        if (definition.Limit is not > 0) return false;
        // Native 0x1404F7E80 builds an undirected transform graph; limits
        // count the whole connected family, including reverse transforms.
        var visited = new HashSet<uint>();
        var pending = new Stack<uint>();
        pending.Push(Hash(definition.Id));
        long count = 0;
        while (pending.TryPop(out var id))
        {
            if (!visited.Add(id)) continue;
            count += owned.GetValueOrDefault(id);
            if (!EventTrinkets.TryGetValue(id, out var item)) continue;
            foreach (var next in item.Transforms.Where(EventTrinkets.ContainsKey)) pending.Push(next);
            foreach (var previous in EventTrinkets.Where(pair => pair.Value.Transforms.Contains(id))) pending.Push(previous.Key);
        }
        return count >= definition.Limit;
    }

    private Dictionary<uint, int> CountOwnedTrinkets(IReadOnlyDictionary<string, JsonObject> saves)
    {
        var counts = new Dictionary<uint, int>();
        void Inventory(JsonNode? items)
        {
            foreach (var item in Objects(items).Where(item => JsonSupport.ReadInt(item, "amount") > 0 &&
                         NativeInventoryIdentity.HashSavedField(Text(item, "type")) == Hash("trinket")))
            {
                var id = NativeInventoryIdentity.HashSavedField(Text(item, "id"));
                counts[id] = checked(counts.GetValueOrDefault(id) + 1);
            }
        }
        Inventory(Root(saves, "estate")["trinkets"]?["items"]);
        foreach (var hero in RosterHeroes(saves).Where(hero => JsonSupport.ReadInt(hero, "roster.status") != 3))
            Inventory(hero["trinkets"]?["items"]);
        foreach (var id in Root(saves, "quest")["trinket_retention_ids"]!.AsArray())
        {
            var hash = unchecked((uint)id!.GetValue<int>());
            counts[hash] = checked(counts.GetValueOrDefault(hash) + 1);
        }
        foreach (var quest in Objects(Root(saves, "quest")["quests"]))
            Inventory(quest["completion_reward"]?["items_definition"]?["items"]);
        // Native includes the ordinary Nomad Wagon stock, not shard stock.
        if (Root(saves, "town")["buildings"]?["nomad_wagon"]?["store"] is JsonObject stores)
        {
            var definition = Objects(OpenResource("campaign/town/buildings/nomad_wagon/nomad_wagon.building.json")["data"]?["stores"])
                .FirstOrDefault() ?? throw new InvalidDataException("Missing ordinary Nomad Wagon store definition.");
            var id = Hash(Text(definition, "id"));
            foreach (var store in stores.Where(pair => Hash(pair.Key) == id))
                Inventory(store.Value?["inventory"]?["items"]);
        }
        return counts;
    }
}
