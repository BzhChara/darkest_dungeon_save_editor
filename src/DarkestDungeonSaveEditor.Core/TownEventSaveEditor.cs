using System.Globalization;
using System.Text.Json.Nodes;

namespace DarkestDungeonSaveEditor.Core;

// Only state owned by the current event is retired. Candidates in any town
// store, roster heroes, quests, inventories, district unlocks and settled
// results are deliberately not searched for or removed on the old event's behalf.
internal static class TownEventSaveEditor
{
    internal static TownEventMutationPreview ReplaceCurrent(
        IReadOnlyDictionary<string, JsonObject> saves,
        TownEventDefinition next,
        Action<IReadOnlyDictionary<string, JsonObject>, TownEventDefinition, List<string>> initialize)
    {
        TownSceneGuard.RequireTown(saves["persist.game.json"]);
        var state = Root(saves, "town_event");
        var log = Root(saves, "campaign_log");
        var oldId = RequiredInt(state, "current_result_event_id");
        if (next.HasProviderConflict || next.NativeId == 0)
            throw new InvalidOperationException(EditorText.Get("TownEvent_Unresolved"));

        var candidates = JsonSupport.RequireObject(state, "bonus_hero_entries");
        var dead = state["dead_hero_entries"] as JsonArray
            ?? throw new InvalidDataException("Missing town event revival offers.");
        var removedCandidates = candidates.Count;
        var removedDead = dead.Count;
        var week = TownEventHistory.SetCurrent(state, log, oldId, next.NativeId);

        // The native load recreates rules/callbacks for current_result_event_id,
        // but does not replay StartEvent. Explicitly reset the old pending state
        // and initialize the selected event once, even when its ID is unchanged.
        // Never advance a week or run quest failure.
        state["bonus_hero_entries"] = new JsonObject();
        state["dead_hero_entries"] = new JsonArray();
        state["event_cost"] = new JsonObject();
        state["free_upgrade_tags"] = new JsonObject();
        state["has_unclaimed_interaction"] = false;
        state["heirloom_gain_multiplier"] = Float(1);
        state["nomad_wagon_trinket_discount_available"] = false;
        state["nomad_wagon_trinket_discount"] = Float(0);
        state["current_result_event_id"] = next.NativeId;

        var changes = new List<string>();
        initialize(saves, next, changes);
        return new TownEventMutationPreview(oldId, next.Id, week,
            removedCandidates, removedDead, changes);
    }

    internal static JsonObject Root(IReadOnlyDictionary<string, JsonObject> saves, string name) =>
        JsonSupport.RequireObject(saves["persist." + name + ".json"], "base_root");

    internal static int RequiredInt(JsonObject node, string key) => JsonSupport.ReadInt(node, key)
        ?? throw new InvalidDataException($"Required integer is missing: {key}.");

    internal static JsonNode Float(double number)
    {
        var value = (float)number;
        if (!float.IsFinite(value)) throw new InvalidDataException("Non-finite event value.");
        var text = value.ToString("R", CultureInfo.InvariantCulture);
        if (!text.Contains('.') && !text.Contains('E')) text += ".0";
        return JsonNode.Parse(text)!;
    }
}
