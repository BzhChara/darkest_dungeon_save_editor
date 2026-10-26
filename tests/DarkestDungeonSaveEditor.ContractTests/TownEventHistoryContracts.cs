using DarkestDungeonSaveEditor.Core.Dson;

internal static partial class ContractSuite
{
    private static async Task VerifyTownEventHistoryAsync(string root)
    {
        var fixture = await BuildTownEventFixtureAsync(root);
        var catalog = TownEventCatalog.Load(fixture.Content);
        var first = catalog.Events.Single(item => item.Id == "test_free_activity");
        var second = catalog.Events.Single(item => item.Id == "test_activity_lock");
        Dictionary<string, JsonObject> Copies() => fixture.Saves.ToDictionary(pair => pair.Key, pair => pair.Value.DeepClone().AsObject());
        JsonObject State(Dictionary<string, JsonObject> saves) => saves["persist.town_event.json"]["base_root"]!.AsObject();
        JsonObject Log(Dictionary<string, JsonObject> saves) => saves["persist.campaign_log.json"]["base_root"]!.AsObject();
        TownEventMutationPreview Apply(Dictionary<string, JsonObject> saves, TownEventDefinition next) =>
            TownEventSaveEditor.ReplaceCurrent(saves, next, new TownEventInitializer(fixture.Content, 7).Initialize);
        JsonObject EventRecord(int id) => new() { ["rtti"] = 844919810, ["town_event_result_id"] = id };
        void NoActiveEvent(Dictionary<string, JsonObject> saves)
        {
            var state = State(saves);
            state["current_result_event_id"] = 0;
            state["bonus_hero_entries"] = new JsonObject();
            state["dead_hero_entries"] = new JsonArray();
            state["event_cost"] = new JsonObject();
            state["free_upgrade_tags"] = new JsonObject();
            state["has_unclaimed_interaction"] = false;
            state["heirloom_gain_multiplier"] = TownEventSaveEditor.Float(1);
            state["nomad_wagon_trinket_discount_available"] = false;
            state["nomad_wagon_trinket_discount"] = TownEventSaveEditor.Float(0);
        }

        // Real profile_0 layout: serialized block numbers diverge from chapterIndex,
        // and early retreats can append several blocks without advancing total_weeks.
        var shifted = Copies();
        var shiftedLog = Log(shifted);
        shiftedLog["chapters"] = JsonNode.Parse("""
            {"4":{"chapterIndex":3,"0":{"rtti":844919810,"town_event_result_id":12}},
             "13":{"chapterIndex":4,"0":{"rtti":844919810,"town_event_result_id":17}},
             "14":{"chapterIndex":4,"0":{"rtti":2006063882,"start":false,"success":false}}}
            """);
        var older = shiftedLog["chapters"]!["4"]!.DeepClone();
        var retreat = shiftedLog["chapters"]!["14"]!.DeepClone();
        Apply(shifted, first);
        Assert(shiftedLog["chapters"]!["13"]!["0"]!["town_event_result_id"]!.GetValue<int>() == first.NativeId &&
            JsonNode.DeepEquals(older, shiftedLog["chapters"]!["4"]) && JsonNode.DeepEquals(retreat, shiftedLog["chapters"]!["14"]),
            "Replacement locates the event by actual chapter week and occurrence, not the outer save key.");

        var added = Copies();
        var addedState = State(added);
        var addedLog = Log(added);
        NoActiveEvent(added);
        addedLog["chapters"] = JsonNode.Parse("""
            {"13":{"chapterIndex":4,"0":{"rtti":844919810,"town_event_result_id":17}},
             "14":{"chapterIndex":5,"0":{"rtti":2006063882,"start":false,"success":false}},
             "15":{"chapterIndex":5,"0":{"rtti":14865927,"trinket_feedback_data":{}}}}
            """);
        var oldLog = addedLog.DeepClone();
        var oldEventChapter = oldLog["chapters"]!["13"]!;
        var preview = Apply(added, first);
        Assert(preview.OldEventId == 0 && preview.Week == 5 &&
            addedState["last_town_event_week"]!.GetValue<int>() == 5 && addedLog["total_weeks"]!.GetValue<int>() == 5 &&
            addedState["result_event_history"]!.AsArray().Select(n => n!.GetValue<int>()).SequenceEqual([12, 17, first.NativeId]) &&
            JsonNode.DeepEquals(oldEventChapter, addedLog["chapters"]!["13"]) &&
            JsonNode.DeepEquals(oldLog["chapters"]!["14"], addedLog["chapters"]!["14"]) &&
            JsonNode.DeepEquals(oldLog["chapters"]!["15"]!["0"], addedLog["chapters"]!["15"]!["0"]) &&
            JsonNode.DeepEquals(addedLog["chapters"]!["15"]!["1"], EventRecord(first.NativeId)),
            "No-event insertion uses the current native chapter, appends one history/log occurrence, and preserves earlier events and week count.");
        Apply(added, second);
        Assert(addedState["result_event_history"]!.AsArray().Count == 3 &&
            addedState["result_event_history"]![2]!.GetValue<int>() == second.NativeId &&
            addedLog["chapters"]!["15"]!.AsObject().Count == 3 && addedLog["total_weeks"]!.GetValue<int>() == 5 &&
            JsonNode.DeepEquals(addedLog["chapters"]!["15"]!["1"], EventRecord(second.NativeId)) &&
            JsonNode.DeepEquals(oldEventChapter, addedLog["chapters"]!["13"]),
            "Replacing the newly added event updates that occurrence without another history entry or week.");
        foreach (var name in new[] { "estate", "roster", "town", "quest", "progression", "upgrades", "game" })
            Assert(JsonNode.DeepEquals(added["persist." + name + ".json"], fixture.Saves["persist." + name + ".json"]),
                "Adding or replacing rule-only events preserves delivered state: " + name);

        // A no-event roll after an ordinary settlement still belongs to the last
        // chapter, even though total_weeks is already one greater.
        var noRoll = Copies();
        NoActiveEvent(noRoll);
        State(noRoll)["result_event_history"] = new JsonArray(12);
        Log(noRoll)["chapters"] = JsonNode.Parse("""
            {"1":{"chapterIndex":3,"0":{"rtti":844919810,"town_event_result_id":12}},
             "2":{"chapterIndex":4,"0":{"rtti":14865927,"trinket_feedback_data":{}}}}
            """);
        Assert(Apply(noRoll, first).Week == 4 && Log(noRoll)["total_weeks"]!.GetValue<int>() == 5 &&
            State(noRoll)["last_town_event_week"]!.GetValue<int>() == 4,
            "No-event insertion does not guess its week from total_weeks alone.");

        // The native max_campaign_log_file_size option can prune old event log
        // items without removing their separate result_event_history entries.
        var pruned = Copies();
        NoActiveEvent(pruned);
        Log(pruned)["chapters"]!["4"]!.AsObject().Remove("0");
        Apply(pruned, first);
        Assert(State(pruned)["result_event_history"]!.AsArray().Select(n => n!.GetValue<int>())
                .SequenceEqual([12, 17, first.NativeId]) && Log(pruned)["total_weeks"]!.GetValue<int>() == 5,
            "Native pruning of previous campaign event records does not prevent adding a new current event.");

        var empty = Copies();
        NoActiveEvent(empty);
        State(empty)["last_town_event_week"] = -1;
        State(empty)["result_event_history"] = new JsonArray();
        Log(empty)["chapters"] = new JsonObject();
        Log(empty)["total_weeks"] = 1;
        Apply(empty, first);
        Assert(Log(empty)["total_weeks"]!.GetValue<int>() == 1 &&
            Log(empty)["chapters"]!["1"]!["chapterIndex"]!.GetValue<int>() == 1 &&
            JsonNode.DeepEquals(Log(empty)["chapters"]!["1"]!["0"], EventRecord(first.NativeId)),
            "An empty native log gets its first chapter at the existing week.");

        // Repeated event IDs are legitimate historical occurrences; only the
        // latest occurrence is current, including two occurrences in one week.
        var repeated = Copies();
        State(repeated)["result_event_history"] = new JsonArray(17, 17);
        Log(repeated)["chapters"] = JsonNode.Parse("""
            {"1":{"chapterIndex":4,"0":{"rtti":844919810,"town_event_result_id":17}},
             "2":{"chapterIndex":4,"0":{"rtti":844919810,"town_event_result_id":17}}}
            """);
        Apply(repeated, first);
        Assert(Log(repeated)["chapters"]!["1"]!["0"]!["town_event_result_id"]!.GetValue<int>() == 17 &&
            Log(repeated)["chapters"]!["2"]!["0"]!["town_event_result_id"]!.GetValue<int>() == first.NativeId,
            "Replacing a repeated ID does not rewrite its earlier occurrence.");

        foreach (var corrupt in new Action<Dictionary<string, JsonObject>>[]
        {
            saves => State(saves)["result_event_history"] = new JsonArray(12, 99),
            saves => State(saves)["last_town_event_week"] = 3,
            saves => Log(saves)["chapters"]!["4"]!["0"]!["rtti"] = 2006063882,
            saves => Log(saves)["chapters"]!["4"]!["chapterIndex"] = 9
        })
        {
            var invalid = Copies();
            corrupt(invalid);
            var before = invalid.ToDictionary(pair => pair.Key, pair => pair.Value.DeepClone());
            var rejected = false;
            try { Apply(invalid, first); } catch (Exception error) when (error is InvalidDataException or InvalidOperationException) { rejected = true; }
            Assert(rejected && before.All(pair => JsonNode.DeepEquals(pair.Value, invalid[pair.Key])),
                "Inconsistent event/history data is rejected before mutation, rather than patched by guessing.");
        }

        var codec = DsonSaveCodec.CreateNative();
        foreach (var saves in new[] { shifted, added, noRoll, pruned, empty, repeated })
        foreach (var name in new[] { "persist.town_event.json", "persist.campaign_log.json" })
        {
            var path = Path.Combine(root, "roundtrip", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            Assert(JsonNode.DeepEquals(saves[name], await RoundtripDirectoryQueryAsync(path, name, saves[name], codec)),
                "Event insertion/history correction preserves native DSON types and round-trips exactly.");
        }
        Assert(DsonTypes.KnownKind(["base_root", "heirloom_gain_multiplier"]) == DsonKind.Float &&
            DsonTypes.KnownKind(["base_root", "nomad_wagon_trinket_discount"]) == DsonKind.Float,
            "The previously added event multiplier/discount float types remain registered.");

        // Exercise the production preview and commit path for a no-event profile.
        var source = Copies();
        NoActiveEvent(source);
        State(source)["result_event_history"] = new JsonArray();
        Log(source)["chapters"]!["4"]!.AsObject().Remove("0");
        foreach (var pair in source)
            await codec.EncodeAsync(WriteMultiMash(root, "addition-source/" + pair.Key, pair.Value.ToJsonString()),
                Path.Combine(fixture.Content.Profile.ProfileDirectory, pair.Key), null);
        var prepared = await fixture.Service.PrepareTownEventEditAsync(fixture.Content.Profile, first, fixture.Content);
        Assert(prepared.Preview.OldEventId == 0 && prepared.Preview.Week == 4, "Production preview exposes insertion without advancing a week.");
        await fixture.Service.CommitAsync(prepared);
        var replacement = await fixture.Service.PrepareTownEventEditAsync(fixture.Content.Profile, second, fixture.Content);
        Assert(replacement.Preview.OldEventId == first.NativeId && replacement.Preview.Week == 4,
            "A committed insertion can be previewed as a replacement using the same week.");
        await fixture.Service.CommitAsync(replacement);
        Console.WriteLine("PASS: native chapter weeks, empty-event insertion, repeated replacement, delivered-state preservation and event float encoding.");
    }
}
