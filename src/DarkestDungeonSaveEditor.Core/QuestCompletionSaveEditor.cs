using System.Text.Json;
using System.Text.Json.Nodes;

namespace DarkestDungeonSaveEditor.Core;

public sealed record QuestCompletionPreview(string QuestId, int GoalCount, int DeletedBattles, long AddedItems, bool MovedParty)
{
    public string Summary => EditorText.Format("QuestCompletion_Summary", QuestId, GoalCount, DeletedBattles, AddedItems) +
        (MovedParty ? Environment.NewLine + EditorText.Get("QuestCompletion_Moved") : string.Empty);
}

public sealed record PreparedQuestCompletionEdit(string SessionId, SaveProfile Profile, QuestCompletionPreview Preview,
    ActiveContentSnapshot Content, string ContentFingerprint, IReadOnlyDictionary<string, string> SourceHashes,
    string WorkspaceDirectory, IReadOnlyList<PreparedSaveFile> Files, DateTime PreparedAtUtc);

internal static class QuestCompletionSaveEditor
{
    // Native goal restore/start/predicates: 0x140572710, 0x140579F80, 0x14057A740.
    // See docs/quest-completion.md. Only main goals are modified; progression is separate.
    internal static (JsonObject Map, JsonObject Raid, QuestCompletionPreview Preview) Complete(
        JsonObject map, JsonObject raid, BattleMapSnapshot snapshot,
        IReadOnlyDictionary<uint, JsonElement> definitions, Func<JsonObject, QuantityItemCatalogResult> inventoryCatalog)
    {
        if (snapshot.IsWaveBattle) throw new InvalidOperationException(EditorText.Get("QuestCompletion_Wave"));
        BattleMapSaveEditor.ValidateStationaryRaidState(raid);
        var updatedMap = (JsonObject)map.DeepClone();
        var updatedRaid = (JsonObject)raid.DeepClone();
        var instance = JsonSupport.RequireObject(updatedRaid, "base_root", "raid_instance");
        var ids = instance["goal_ids"] as JsonArray;
        if (ids is null || ids.Count == 0) throw new InvalidOperationException(EditorText.Get("QuestCompletion_NoGoals"));
        var goals = ids.Select(node => node?.GetValue<string>() ?? throw new InvalidDataException("Invalid goal ID."))
            .Select(id => (Id: id, Hash: NativeInventoryIdentity.HashSavedField(id)))
            .DistinctBy(goal => goal.Hash).ToArray();
        var deleted = new HashSet<(string Area, string Tile)>();
        var addedItems = 0L;
        QuantityItemCatalogResult? catalog = null;
        var staticAreas = JsonSupport.RequireObject(updatedMap, "base_root", "map", "static_dynamic", "static_save", "base_root", "areas");
        bool OrdinaryRoom(BattleMapAreaSnapshot area) => area.Kind == BattleMapAreaKind.Room &&
            staticAreas[area.AreaId] is JsonObject room &&
            !Enumerable.Range(0, 8).Any(index => room["door" + index] is JsonObject door && JsonSupport.ReadInt(door, "type") == 2);

        foreach (var goal in goals)
        {
            if (!definitions.TryGetValue(goal.Hash, out var definition))
                throw new InvalidOperationException(EditorText.Format("QuestCompletion_MissingGoal", goal.Id));
            var type = NativeJsonReader.ReadCString(definition, "type");
            var data = Child(definition, "data");
            JsonObject State()
            {
                var matches = instance.Where(pair => pair.Value is JsonObject &&
                    NativeResourceIdentity.HashCString(pair.Key) == goal.Hash).ToArray();
                if (matches.Length > 1) throw new InvalidDataException("Ambiguous saved quest goal state.");
                if (matches.Length == 1) return (JsonObject)matches[0].Value!;
                var state = new JsonObject();
                instance[NativeJsonReader.ReadCString(definition, "id")] = state;
                return state;
            }
            void Counter(string name, int target, string? targetName = null)
            {
                var state = State();
                var current = state.ContainsKey(name) ? JsonSupport.ReadInt(state, name)
                    ?? throw new InvalidDataException("Invalid saved quest counter: " + name) : 0;
                // These native predicates compare unsigned counters. Never reduce progress.
                if (unchecked((uint)current) < (uint)target) state[name] = target;
                else if (!state.ContainsKey(name)) state[name] = current;
                if (targetName is not null) state[targetName] = target;
            }

            switch (type)
            {
                case "explore_room":
                    Counter("explored_room_count", Threshold(data, snapshot.Areas.Count(OrdinaryRoom)), "target_room_count");
                    break;
                case "battle_room":
                    // Current count resets on reload. Delete the remaining qualifying fights
                    // so the percentage-based target recomputes to zero. Fixed amounts do not.
                    if (Amount(data) != 0)
                        throw new InvalidOperationException(EditorText.Format("QuestCompletion_FixedBattle", goal.Id));
                    _ = Threshold(data, 0);
                    foreach (var area in snapshot.Areas.Where(OrdinaryRoom))
                    foreach (var tile in area.Tiles.Where(tile => tile.StaticType == 3 &&
                                 tile.RawContent is 1 or 2 or 6 or 10 or 11 or 12))
                        if (deleted.Add((area.AreaId, tile.TileId)))
                            BattleMapSaveEditor.DeleteContent(updatedMap, updatedRaid, snapshot, area.AreaId, tile.TileId);
                    State()["current_battle_count"] = 0;
                    State()["target_battle_count"] = 0;
                    break;
                case "kill_monster":
                    Counter("monster_kill_count", Amount(data));
                    break;
                case "activate":
                    Counter("curios_activated_count", Amount(data));
                    break;
                case "encounter_monster":
                    Counter("monster_encounter_count", Amount(data));
                    break;
                case "deaths_door":
                    Counter("current_deaths_door_count", Amount(data), "target_deaths_door_count");
                    break;
                case "trait_applied":
                    Counter("current_trait_count", Amount(data), "target_trait_count");
                    break;
                case "gather":
                    var item = Child(data, "item");
                    var amount = Amount(item);
                    if (amount == 0) break;
                    catalog ??= inventoryCatalog(updatedRaid);
                    var identity = new NativeQuantityItemIdentity(QuantityItemStorageKind.RaidInventory,
                        NativeResourceIdentity.HashCString(NativeJsonReader.ReadCString(item, "type")),
                        NativeResourceIdentity.HashCString(NativeJsonReader.ReadCString(item, "id")));
                    var matches = catalog.Items.Where(candidate => !candidate.IsSaveOnly &&
                        NativeQuantityItemIdentity.FromDefinition(candidate) == identity).ToArray();
                    if (matches.Length != 1 || matches[0].HasProviderConflict || catalog.DefinitionReadFailures.Count > 0)
                        throw new InvalidOperationException(EditorText.Format("QuestCompletion_Item", NativeJsonReader.ReadCString(item, "id")));
                    var currentAmount = RaidInventorySaveEditor.CountAmount(updatedRaid, matches[0]);
                    if (currentAmount >= amount) break;
                    var capacity = catalog.RaidStorage?.MaxSlots
                        ?? throw new InvalidOperationException(EditorText.Get("QuestCompletion_Storage"));
                    updatedRaid = RaidInventorySaveEditor.SetAmount(updatedRaid, matches[0], amount, capacity).UpdatedRoot;
                    instance = JsonSupport.RequireObject(updatedRaid, "base_root", "raid_instance");
                    addedItems += amount - currentAmount;
                    break;
                case "tutorial_room":
                    throw new InvalidOperationException(EditorText.Get("BattleMapView_Tutorial_Message"));
                case "survival":
                    throw new InvalidOperationException(EditorText.Get("QuestCompletion_Wave"));
                case "battle" when Amount(data) != 0:
                    throw new InvalidOperationException(EditorText.Format("QuestCompletion_FixedBattle", goal.Id));
                default:
                    throw new InvalidOperationException(EditorText.Format("QuestCompletion_Unsupported", goal.Id, type));
            }
        }
        return (updatedMap, updatedRaid, new QuestCompletionPreview(JsonSupport.ReadString(instance, "id"),
            goals.Length, deleted.Count, addedItems, MovedParty: false));
    }

    private static JsonElement Child(JsonElement parent, string name) =>
        NativeJsonReader.TryGetProperty(parent, name, out var value) ? value : default;

    private static int Amount(JsonElement data)
    {
        if (!NativeJsonReader.TryGetProperty(data, "amount", out var value)) return 0;
        if (!value.TryGetInt32(out var amount) || amount < 0) throw new InvalidDataException("Invalid quest goal amount.");
        return amount;
    }

    private static int Threshold(JsonElement data, int count)
    {
        var amount = Amount(data);
        if (amount != 0) return amount;
        var percentage = (float)(NativeJsonReader.ReadFloat(data, "percentage") ?? 0);
        var target = (float)count * percentage;
        if (!float.IsFinite(target) || target < 0 || target >= int.MaxValue)
            throw new InvalidDataException("Invalid quest goal percentage.");
        return (int)target; // Native float32 multiplication followed by truncation.
    }
}
