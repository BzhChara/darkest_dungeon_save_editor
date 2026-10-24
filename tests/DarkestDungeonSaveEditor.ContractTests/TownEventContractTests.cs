using DarkestDungeonSaveEditor.Core.Dson;

internal static partial class ContractSuite
{
    public static async Task RunTownEventsOnlyAsync(string repositoryRoot)
    {
        var root = Path.Combine(repositoryRoot, "artifacts", "town-event-contracts", Guid.NewGuid().ToString("N"));
        await RunTownEventContractsAsync(root);
        Console.WriteLine($"Artifacts: {root}");
    }

    private sealed record TownEventFixture(string Root, ActiveContentSnapshot Content, SaveEditService Service,
        Dictionary<string, JsonObject> Saves, string EventsPath);

    // This table is an acceptance matrix of the 31 effect enums from the
    // native event library, independent of the initializer's dispatch table.
    private static readonly (string Type, string Text, double Number, string[] Files)[] EventCases =
    [
        ("free_activity", "abbey", 1, []), ("activity_lock", "tavern", 1, []), ("activity_cost_change", "abbey", .5, []),
        ("bonus_recruit", "progression", 2, ["roster", "upgrades"]),
        ("dead_recruit", "", 1, ["roster"]), ("plot_quest", "event_plot", 1, ["quest"]),
        ("embark_party_buff", "event_buff", 1, []), ("in_activity_buff", "event_buff", 1, []),
        ("idle_buff", "event_buff", 1, []), ("idle_resolve_level", "progression", 1, []),
        ("provision_item_type_cost_change", "supply", .5, []), ("provision_item_type_amount_change", "supply", 2, []),
        ("upgrade_tag_discount", "building", .5, []), ("upgrade_tag_free", "weapon", 3, []),
        ("remove_quest_hero_level_restriction", "", 1, []),
        ("stage_coach_bonus_recruits", "", 2, ["town", "roster", "upgrades"]),
        ("trinket_retention_add_from_storage", "", 1, ["estate", "quest"]),
        ("remove_vampire", "", 1, ["roster"]), ("districts_unlocked", "", 1, ["town"]),
        ("bonus_currency", "gold", 25, ["estate"]), ("stress_treatment_debuff", "event_buff", 1, []),
        ("shard_buff", "event_buff", 1, []), ("event_cost", "gold", 50, []),
        ("quirked_recruit", "event_positive", 1, ["roster", "upgrades"]),
        ("roster_stress_heal", "", 20, []), ("roster_disease_heal", "", 1, []),
        ("trinket_sell_multiplier", "", 2, []), ("idle_negative_quirk_remove", "", 1, []),
        ("heirloom_gain_multiplier", "", 1.25, []), ("nomad_wagon_trinket_discount", "", .5, []),
        ("bonus_recruit_stage_coach_level", "progression", 1, ["roster", "upgrades"])
    ];

    private static async Task<TownEventFixture> BuildTownEventFixtureAsync(string root)
    {
        var template = HeroProgressionQueryContent(Path.Combine(root, "resources"), "mode");
        var game = template.Sources[0].Directory;
        string Write(string path, string value) => WriteMultiMash(game, path, value);
        var info = Path.Combine(game, "heroes", "progression", "progression.info.darkest");
        File.AppendAllText(info, "\ngeneration: .number_of_cards_in_deck 3 .card_chance 1\n");
        Write("campaign/roster/roster.variables.json", """
            {"resolve_level_thresholds":[0,10,20,30,40,50,60],"max_positive_quirks":5,"max_negative_quirks":5,"max_diseases":3}
            """);
        Write("shared/quirk/event.quirk_library.json", """
            {"quirks":[{"id":"event_positive","is_positive":true,"random_chance":1,"buffs":[]},
                       {"id":"disease_vampire_passive","is_positive":false,"random_chance":0,"buffs":[],"tags":["vampire"]},
                       {"id":"event_unique","is_positive":false,"random_chance":0,"buffs":[],"tags":["singleton"]},
                       {"id":"vampire","is_positive":false,"random_chance":0,"buffs":[]},
                       {"id":"singleton","is_positive":false,"random_chance":0,"buffs":[]}]}
            """);
        Write("inventory/event.inventory.items.darkest", "inventory_item: .type heirloom .id gold .base_stack_limit 1750\n");
        Write("trinkets/event.entries.trinkets.json", """
            {"entries":[{"id":"old_owned","rarity":"common","limit":0,"price":100,"buffs":[]},
             {"id":"quest_prize","rarity":"event_rarity","limit":1,"price":100,"buffs":[]},
             {"id":"protected","rarity":"common","limit":0,"price":100,"buffs":[]}]}
            """);
        Write("campaign/town/buildings/stage_coach/stage_coach.building.json", """
            {"data":{"stores":[{"id":"hero_recruit","data":{"upgraded_recruits_upgrades":[
              {"level":1,"chance":1,"upgrade_tree_id":"stagecoach","upgrade_requirement_code":"0",
               "starting_quirks":[],"number_of_extra_combat_skills":0,"number_of_extra_camping_skills":0,
               "number_of_extra_positive_quirks":0,"number_of_extra_negative_quirks":0}]}}]}}
            """);
        Write("campaign/town/buildings/nomad_wagon/nomad_wagon.building.json", """{"data":{"stores":[{"id":"trinket_supply"}]}}""");
        Write("campaign/quest/event.quest.plot_quests.json", """
            {"plot_quests":[{"id":"event_plot","is_enabled":true,"is_repeatable":false,"is_generated_by_event":true,
              "quest":{"is_plot_quest":true,"type":"kill_boss","dungeon":"weald","difficulty":1,"length":1,
                "map_name":"event_map","goal_ids":["kill_event"],"completion_reward":{"resolve_xp":4,
                  "items_definition":{"items":{"0":{"id":"","type":"gold","amount":500}}}}},
              "additional_trinket_completion_rewards":[{"rarity":"event_rarity","amount":1}],
              "is_town_progression_goals_enabled":false,"trinket_retention_count":0}]}
            """);
        var events = new JsonArray(EventCases.Select(test => (JsonNode)new JsonObject
        {
            ["id"] = "test_" + test.Type,
            ["data"] = new JsonArray(new JsonObject { ["type"] = test.Type, ["string_data"] = test.Text, ["number_data"] = test.Number })
        }).ToArray());
        var eventsPath = Write("campaign/town_events/event.town_events.events.json", new JsonObject { ["events"] = events }.ToJsonString());
        var profileRoot = Path.Combine(root, "profile_7");
        Directory.CreateDirectory(profileRoot);
        var profile = new SaveProfile("profile_7", profileRoot, Path.Combine(profileRoot, "persist.estate.json"), "event-test", DateTime.UtcNow);
        JsonObject Save(string json) => JsonNode.Parse(json)!.AsObject();
        var saves = new Dictionary<string, JsonObject>
        {
            ["persist.game.json"] = Save("""{"base_root":{"game_mode":"base","inraid":false,"raiddungeon":"none","applied_ugcs_1_0":{}}}"""),
            ["persist.town_event.json"] = Save("""
                {"base_root":{"current_result_event_id":17,"last_town_event_week":4,"result_event_history":[12,17],
                "bonus_hero_entries":{"90":{"marker":"old event offer"}},"dead_hero_entries":[2],"event_cost":{"count":1,"0":{"type":"gold","amount":9}},
                "free_upgrade_tags":{"0":{"upgrade_tag":44,"amount":2}},"has_unclaimed_interaction":true,
                "heirloom_gain_multiplier":2.0,"nomad_wagon_trinket_discount_available":true,"nomad_wagon_trinket_discount":0.25,
                "non_rolled_additional_chances":{"keep":{"chance":0.5}}}}
                """),
            ["persist.campaign_log.json"] = Save("""{"base_root":{"total_weeks":5,"chapters":{"4":{"0":{"town_event_result_id":17},"1":{"keep":"already settled"}}}}}"""),
            ["persist.estate.json"] = Save("""
                {"base_root":{"wallet":{"0":{"type":"gold","amount":100}},"estate_items":{"items":{"0":{"id":"keep","type":"estate","amount":7}}},
                "trinkets":{"items":{"0":{"id":"old_owned","type":"trinket","amount":1},"1":{"id":"protected","type":"trinket","amount":1}}}}}
                """),
            ["persist.town.json"] = Save("""
                {"base_root":{"buildings":{"stage_coach":{"store":{"hero_recruit":{"generated":{"50":{"heroClass":"progression","quirks":{},"marker":"old coach"}},
                  "deck_history_version_0":{}},"shard_hero_recruit":{"generated":{}}}},"nomad_wagon":{"store":{"trinket_supply":{"inventory":{"items":{}}}}},
                  "abbey":{"marker":"paid activity"}}}}
                """),
            ["persist.roster.json"] = Save("""{"base_root":{"nextGuid":100,"highest_resolve_xp":10,"heroes":{}}}"""),
            ["persist.upgrades.json"] = Save("""{"base_root":{"purchases":{"0":{"instance_number":0,"tree_id":0,"requirement_code":"0","is_purchased":true}}}}"""),
            ["persist.quest.json"] = Save("""
                {"base_root":{"quests":{"0":{"id":"old_event_quest","keep":"do not settle or remove","completion_reward":{"items_definition":{"items":{"0":{"id":"protected","type":"trinket","amount":1}}}}}},
                "trinket_retention_ids":[],"plot_quest_total":77}}
                """),
            ["persist.progression.json"] = Save("""{"base_root":{"completed_plot_quests_data":{},"dungeon":{},"keep":"settled progress"}}""")
        };
        saves["persist.upgrades.json"]["base_root"]!["purchases"]!["0"]!["tree_id"] = unchecked((int)HashLoc2Key("stagecoach"));
        var catalog = HeroClassCatalog.Load(template);
        var hero = catalog.HeroClasses.Single(item => item.Id == "progression");
        var candidate = StagecoachHeroCandidateFactory.Generate(catalog, hero, 25, 1, []).Candidate;
        foreach (var (key, status) in new[] { ("1", 0), ("2", 3) })
        {
            var entry = candidate.DeepClone().AsObject();
            entry["roster.status"] = status;
            entry["quirks"]!["disease_vampire_passive"] = new JsonObject { ["is_locked"] = false };
            // Negative controls: names alone must not trigger native tag removal.
            entry["quirks"]!["vampire"] = new JsonObject { ["is_locked"] = false };
            entry["quirks"]!["singleton"] = new JsonObject { ["is_locked"] = false };
            if (status == 3)
            {
                entry["quirks"]!["event_unique"] = new JsonObject { ["is_locked"] = false };
                entry["actor"]!["current_hp"] = TownEventSaveEditor.Float(0);
                entry["m_Stress"] = TownEventSaveEditor.Float(200);
                entry["is_death_heart_attack_completed"] = true;
            }
            saves["persist.roster.json"]["base_root"]!["heroes"]![key] = new JsonObject
            {
                ["hero_file_data"] = new JsonObject { ["raw_data"] = new JsonObject { ["base_root"] = entry } }
            };
        }
        var codec = DsonSaveCodec.CreateNative();
        foreach (var pair in saves)
            await codec.EncodeAsync(WriteMultiMash(root, "seed/" + pair.Key, pair.Value.ToJsonString()), Path.Combine(profileRoot, pair.Key), null);
        var content = await ActiveContentResolver.ResolveAsync(profile, game, null, codec, Path.Combine(root, "resolve"));
        return new(root, content, new SaveEditService(codec, new SaveEditorLocations(root, Path.Combine(root, "work"), Path.Combine(root, "backups"))), saves, eventsPath);
    }

    private static async Task RunTownEventContractsAsync(string root)
    {
        var fixture = await BuildTownEventFixtureAsync(Path.Combine(root, "matrix"));
        var catalog = TownEventCatalog.Load(fixture.Content);
        Assert(catalog.Events.Count == EventCases.Length && catalog.Events.All(row => !row.HasProviderConflict), "All native effect types must be discoverable.");
        var codec = DsonSaveCodec.CreateNative();
        foreach (var test in EventCases)
        {
            Directory.CreateDirectory(Path.Combine(root, "roundtrip", test.Type));
            var saves = fixture.Saves.ToDictionary(pair => pair.Key, pair => pair.Value.DeepClone().AsObject());
            var selected = catalog.Events.Single(item => item.Id == "test_" + test.Type);
            TownEventSaveEditor.ReplaceCurrent(saves, selected, new TownEventInitializer(fixture.Content, 7).Initialize);
            var state = saves["persist.town_event.json"]["base_root"]!;
            var log = saves["persist.campaign_log.json"]["base_root"]!;
            Assert(state["current_result_event_id"]!.GetValue<int>() == selected.NativeId && state["last_town_event_week"]!.GetValue<int>() == 4 &&
                   state["result_event_history"]!.AsArray().Select(value => value!.GetValue<int>()).SequenceEqual([12, selected.NativeId]) &&
                   log["total_weeks"]!.GetValue<int>() == 5 && log["chapters"]!["4"]!.AsObject().Count == 2,
                test.Type + ": replacement must edit the existing occurrence without adding time/history.");
            Assert(!state["bonus_hero_entries"]!.AsObject().ContainsKey("90") &&
                   JsonNode.DeepEquals(log["chapters"]!["4"]!["1"], fixture.Saves["persist.campaign_log.json"]["base_root"]!["chapters"]!["4"]!["1"]),
                test.Type + ": only pending old offers are retired.");
            foreach (var pair in saves)
            {
                var name = pair.Key.Replace("persist.", "").Replace(".json", "");
                if (name is not ("town_event" or "campaign_log") && !test.Files.Contains(name))
                    Assert(JsonNode.DeepEquals(pair.Value, fixture.Saves[pair.Key]), test.Type + ": unrelated " + name + " must stay byte-semantically unchanged.");
                var restored = await RoundtripDirectoryQueryAsync(Path.Combine(root, "roundtrip", test.Type), name, pair.Value, codec);
                Assert(JsonNode.DeepEquals(pair.Value, restored), test.Type + "/" + name + ": native DSON round trip.");
            }
            VerifyEventOutcome(test.Type, saves);
            Console.WriteLine("PASS: town event " + test.Type);
        }
        await VerifyEventTransactionsAsync(root, fixture, catalog);
        await VerifyEventBoundariesAsync(Path.Combine(root, "boundaries"));
        await VerifyEventQuestDependenciesAsync(Path.Combine(root, "quest-dependencies"));
        await VerifyOrdinaryEventRecruitRoutingAsync(Path.Combine(root, "ordinary-routing"));
        await VerifyEventRecruitSingletonScopeAsync(Path.Combine(root, "singleton-scope"));
    }

    private static void VerifyEventOutcome(string type, Dictionary<string, JsonObject> saves)
    {
        JsonNode Root(string name) => saves["persist." + name + ".json"]["base_root"]!;
        var state = Root("town_event");
        if (type is "bonus_recruit" or "bonus_recruit_stage_coach_level" or "quirked_recruit")
        {
            var offers = state["bonus_hero_entries"]!.AsObject();
            Assert(offers.Count == (type == "bonus_recruit" ? 2 : 1), "Recruit count follows the effect.");
            var hero = offers.First().Value!;
            Assert(hero["is_from_town_event"]!.GetValue<bool>(), "New event candidates stay in the event pool.");
            var upgraded = type != "bonus_recruit";
            Assert(hero["resolveXp"]!.GetValue<int>() == (upgraded ? 10 : 0) && hero["armour_rank"]!.GetValue<int>() == (upgraded ? 1 : 0) &&
                   hero["actor"]!["current_hp"]!.GetValue<double>() == (upgraded ? 40 : 20), "High-level offers include equipment and full HP, not XP alone.");
        }
        if (type == "stage_coach_bonus_recruits")
        {
            var pool = Root("town")["buildings"]!["stage_coach"]!["store"]!["hero_recruit"]!["generated"]!.AsObject();
            Assert(pool.Count == 3 && pool["50"]!["marker"]!.GetValue<string>() == "old coach" && pool["100"]!["resolveXp"]!.GetValue<int>() == 10,
                "Additional coach slots preserve existing candidates and use purchased upgraded-recruit rules.");
        }
        if (type == "dead_recruit")
        {
            var hero = Root("roster")["heroes"]!["2"]!["hero_file_data"]!["raw_data"]!["base_root"]!;
            Assert(state["dead_hero_entries"]!.AsArray().Single()!.GetValue<int>() == 2 && hero["roster.status"]!.GetValue<int>() == 3 &&
                   hero["actor"]!["current_hp"]!.GetValue<double>() == 40 && hero["armour_rank"]!.GetValue<int>() == 0 &&
                   !hero["quirks"]!.AsObject().ContainsKey("disease_vampire_passive") &&
                   !hero["quirks"]!.AsObject().ContainsKey("event_unique") &&
                   hero["quirks"]!["vampire"] is JsonObject && hero["quirks"]!["singleton"] is JsonObject,
                "Revival initializes offers and removes tagged quirks without recruiting heroes or removing untagged namesakes.");
        }
        if (type == "plot_quest")
        {
            var quest = Root("quest");
            Assert(quest["quests"]!.AsObject().Count == 2 && quest["quests"]!["0"]!["keep"]!.GetValue<string>() == "do not settle or remove" &&
                   quest["quests"]!["1"]!["completion_reward"]!["items_definition"]!["items"]!.AsObject().Count == 2 && quest["plot_quest_total"]!.GetValue<int>() == 77,
                "The new plot includes rewards and leaves the existing board and definition count alone.");
        }
        if (type == "trinket_retention_add_from_storage")
            Assert(Root("estate")["trinkets"]!["items"]!.AsObject().Single().Value!["id"]!.GetValue<string>() == "protected" &&
                   Root("quest")["trinket_retention_ids"]!.AsArray().Single()!.GetValue<int>() == HashLoc2Key("old_owned"), "Theft protects existing quest rewards and stores recoverable identities.");
        if (type == "remove_vampire")
        {
            var quirks = Root("roster")["heroes"]!.AsObject().Select(pair =>
                pair.Value!["hero_file_data"]!["raw_data"]!["base_root"]!["quirks"]!.AsObject()).ToArray();
            Assert(quirks.All(held => !held.ContainsKey("disease_vampire_passive") &&
                   held["vampire"] is JsonObject && held["singleton"] is JsonObject) &&
                   quirks.Any(held => held["event_unique"] is JsonObject),
                "Curse removal uses the vampire tag for all roster entries and retains singleton-tagged or untagged quirks.");
        }
        if (type == "districts_unlocked") Assert(Root("town")["districts"]?["buildings"] is JsonObject, "District unlock must persist.");
        if (type == "bonus_currency") Assert(Root("estate")["wallet"]!["0"]!["amount"]!.GetValue<int>() == 125, "Currency is granted once.");
        if (type == "event_cost") Assert(state["event_cost"]!["0"]!["amount"]!.GetValue<int>() == 50 && Root("estate")["wallet"]!["0"]!["amount"]!.GetValue<int>() == 100, "Claim costs are pending, not charged during replacement.");
        if (type == "upgrade_tag_free") Assert(state["free_upgrade_tags"]!["0"]!["amount"]!.GetValue<int>() == 3, "Unused upgrade quota is replaced.");
        Assert(state["has_unclaimed_interaction"]!.GetValue<bool>() == (type is "roster_stress_heal" or "roster_disease_heal"), "Healing waits for a game interaction.");
        Assert(state["heirloom_gain_multiplier"]!.GetValue<double>() == (type == "heirloom_gain_multiplier" ? 1.25 : 1), "Heirloom multiplier must reset.");
        Assert(state["nomad_wagon_trinket_discount_available"]!.GetValue<bool>() == (type == "nomad_wagon_trinket_discount"), "Old discounts must not leak.");
    }

    private static async Task VerifyEventTransactionsAsync(string root, TownEventFixture fixture, TownEventCatalogResult catalog)
    {
        var selected = catalog.Events.Single(row => row.Id == "test_quirked_recruit");
        var before = fixture.Saves.Keys.ToDictionary(name => name, name => ComputeSha256(Path.Combine(fixture.Content.Profile.ProfileDirectory, name)));
        var prepared = await fixture.Service.PrepareTownEventEditAsync(fixture.Content.Profile, selected, fixture.Content);
        Assert(before.All(pair => ComputeSha256(Path.Combine(fixture.Content.Profile.ProfileDirectory, pair.Key)) == pair.Value), "Preview must not alter any profile file.");
        Assert(prepared.Files.Count >= 4 && prepared.Files[^1].FileName == "persist.town_event.json", "Publish event identity after its dependent files.");
        foreach (var failureIndex in Enumerable.Range(1, prepared.Files.Count))
        {
            var writes = 0;
            fixture.Service.AfterTargetReplace = _ => { if (++writes == failureIndex) throw new IOException("injected event write failure"); };
            var failed = false;
            try { await fixture.Service.CommitAsync(prepared); } catch (Exception) { failed = true; }
            Assert(failed && before.All(pair => ComputeSha256(Path.Combine(fixture.Content.Profile.ProfileDirectory, pair.Key)) == pair.Value), "A failure after each replaced file must restore every original file.");
        }
        fixture.Service.AfterTargetReplace = null;
        var result = await fixture.Service.CommitAsync(prepared);
        Assert(Directory.Exists(result.BackupDirectory) && before.All(pair => ComputeSha256(Path.Combine(result.BackupDirectory, pair.Key)) == pair.Value), "Successful write retains a complete verified backup.");
        var repeatFailed = false;
        try { await fixture.Service.CommitAsync(prepared); } catch (InvalidOperationException) { repeatFailed = true; }
        Assert(repeatFailed, "A successful preview is single-use.");
        Console.WriteLine("PASS: event preview, per-file fault recovery, backup and stale-preview rejection.");
    }
}
