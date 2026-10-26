internal static partial class ContractSuite
{
    private static async Task VerifyEventQuestGenerationScopeAsync(string root)
    {
        var f = await BuildTownEventFixtureAsync(root);
        var game = f.Content.Resolution!.GameDirectory;
        // Reduced native recovery definitions: real IDs, thresholds, order,
        // dungeon, levels and goals. The surrounding save is a private fixture,
        // not evidence of a naturally occurring or installed Mod campaign.
        var definitions = new JsonArray();
        foreach (var (index, rarity, level, goal) in new[]
        {
            (0, "uncommon", 1, "kill_crow_A"), (1, "rare", 3, "kill_crow_B"), (2, "very_rare", 5, "kill_crow_C")
        })
            definitions.Add(new JsonObject
            {
                ["id"] = "plot_trinket_retention_" + index, ["is_repeatable"] = true,
                ["trinket_retention_count"] = 8, ["trinket_retention_minimum_rarity"] = rarity,
                ["is_town_progression_goals_enabled"] = false,
                ["quest"] = new JsonObject
                {
                    ["is_plot_quest"] = true, ["type"] = "kill_boss", ["dungeon"] = "weald",
                    ["difficulty"] = level, ["length"] = 1, ["map_name"] = "crow_map1",
                    ["goal_ids"] = new JsonArray(goal), ["completion_reward"] = new JsonObject()
                }
            });
        WriteMultiMash(game, "campaign/quest/recovery.quest.plot_quests.json",
            new JsonObject { ["plot_quests"] = definitions }.ToJsonString());
        WriteMultiMash(game, "trinkets/recovery.rarities.trinkets.json",
            """{"rarities":[{"id":"very_rare"},{"id":"rare"},{"id":"uncommon"},{"id":"common"},{"id":"event_rarity"}]}""");

        var events = TownEventCatalog.Load(f.Content);
        var theft = events.Events.Single(item => item.Id == "test_trinket_retention_add_from_storage") with
        {
            Effects = [new TownEventEffect("trinket_retention_add_from_storage", "", 8)]
        };
        JsonObject Item(string owner = "") => new()
        {
            ["id"] = "old_owned", ["type"] = "trinket", ["amount"] = 1, ["hero_name"] = owner
        };
        JsonObject Board(Dictionary<string, JsonObject> saves) => saves["persist.quest.json"]["base_root"]!["quests"]!.AsObject();
        JsonObject Pool(Dictionary<string, JsonObject> saves) => saves["persist.quest.json"]["base_root"]!["trinket_retention_ids"]!["items"]!.AsObject();
        JsonObject Storage(Dictionary<string, JsonObject> saves) => saves["persist.estate.json"]["base_root"]!["trinkets"]!["items"]!.AsObject();
        Dictionary<string, JsonObject> Saves(string rarity, int storageCount, int lostCount = 0)
        {
            var entriesPath = Path.Combine(game, "trinkets", "event.entries.trinkets.json");
            var entries = JsonNode.Parse(File.ReadAllText(entriesPath))!.AsObject();
            entries["entries"]!.AsArray().OfType<JsonObject>().Single(item => item["id"]!.GetValue<string>() == "old_owned")["rarity"] = rarity;
            File.WriteAllText(entriesPath, entries.ToJsonString());
            var saves = f.Saves.ToDictionary(pair => pair.Key, pair => pair.Value.DeepClone().AsObject());
            var storage = Storage(saves);
            storage.Clear();
            for (var i = 0; i < storageCount; i++) storage[i.ToString()] = Item();
            for (var i = 0; i < lostCount; i++) Pool(saves)[i.ToString()] = Item("Lost hero " + i);
            Board(saves)["1"] = new JsonObject
            {
                ["id"] = "plot_crow_trinket", ["dungeon"] = "weald", ["difficulty"] = 5,
                ["completion_reward"] = new JsonObject { ["items_definition"] = new JsonObject
                    { ["items"] = new JsonObject { ["0"] = new JsonObject { ["id"] = "crow_wingfeather", ["type"] = "trinket", ["amount"] = 1 } } } }
            };
            return saves;
        }
        void Apply(Dictionary<string, JsonObject> saves) =>
            TownEventSaveEditor.ReplaceCurrent(saves, theft, new TownEventInitializer(f.Content, 7).Initialize);

        foreach (var rarity in new[] { "uncommon", "rare", "very_rare" })
        {
            var saves = Saves(rarity, 8);
            var oldBoard = Board(saves).DeepClone().AsObject();
            var log = saves["persist.campaign_log.json"]["base_root"]!;
            var oldWeek = log["total_weeks"]!.GetValue<int>();
            Apply(saves);
            Assert(JsonNode.DeepEquals(oldBoard, Board(saves)),
                "Theft alone leaves the quest board unchanged even when a native recovery variant is eligible.");
            Assert(Storage(saves).Count == 0 && Pool(saves).Count == 8 && log["total_weeks"]!.GetValue<int>() == oldWeek,
                "Theft still transfers stored trinkets to retention without advancing a week.");
            var roundtrip = Path.Combine(root, "roundtrip-" + rarity);
            Directory.CreateDirectory(roundtrip);
            var restored = await RoundtripDirectoryQueryAsync(roundtrip, "quest",
                saves["persist.quest.json"], DsonSaveCodec.CreateNative());
            Assert(JsonNode.DeepEquals(saves["persist.quest.json"], restored), "Theft and preserved quests survive native DSON roundtrip.");
        }
        foreach (var (rarity, count) in new[] { ("very_rare", 7), ("common", 8) })
        {
            var saves = Saves(rarity, count);
            Apply(saves);
            Assert(Board(saves).Count == 2 && Pool(saves).Count == count,
                "Insufficient count or rarity retains the loss without inventing a recovery quest.");
        }
        var combined = Saves("very_rare", 1, 7);
        var priorLosses = Pool(combined).DeepClone().AsObject();
        var combinedBoard = Board(combined).DeepClone();
        Apply(combined);
        Assert(JsonNode.DeepEquals(combinedBoard, Board(combined)) && Pool(combined).Count == 8 &&
            priorLosses.All(pair => JsonNode.DeepEquals(pair.Value, Pool(combined)[pair.Key])),
            "Theft preserves earlier retained instances without turning the combined losses into a new quest.");
        var delivered = combined["persist.quest.json"].DeepClone();
        TownEventSaveEditor.ReplaceCurrent(combined, events.Events.Single(item => item.Id == "test_free_activity"),
            new TownEventInitializer(f.Content, 8).Initialize);
        Assert(JsonNode.DeepEquals(delivered, combined["persist.quest.json"]), "Replacing the theft event retains existing quests and stolen items.");

        // Synthetic composite effects test the supported event contract. They
        // do not claim that the native theft event explicitly supplies a quest.
        var explicitPlot = Saves("very_rare", 8);
        var explicitBoard = Board(explicitPlot).DeepClone().AsObject();
        TownEventSaveEditor.ReplaceCurrent(explicitPlot, theft with
        {
            Effects = [new TownEventEffect("trinket_retention_add_from_storage", "", 8),
                new TownEventEffect("plot_quest", "event_plot", 1)]
        }, new TownEventInitializer(f.Content, 9).Initialize);
        Assert(Board(explicitPlot).Count == explicitBoard.Count + 1 &&
            Board(explicitPlot)["2"]!["id"]!.GetValue<string>() == "event_plot" &&
            explicitBoard.All(pair => JsonNode.DeepEquals(pair.Value, Board(explicitPlot)[pair.Key])) &&
            Storage(explicitPlot).Count == 0 && Pool(explicitPlot).Count == 8,
            "A composite event creates only its explicit plot, retaining theft and existing quests.");

        var explicitRecovery = Saves("very_rare", 1, 7);
        TownEventSaveEditor.ReplaceCurrent(explicitRecovery, theft with
        {
            Effects = [new TownEventEffect("trinket_retention_add_from_storage", "", 8),
                new TownEventEffect("plot_quest", "plot_trinket_retention_2", 1)]
        }, new TownEventInitializer(f.Content, 9).Initialize);
        var recovery = Board(explicitRecovery)["2"]!;
        var rewards = recovery["completion_reward"]!["trinket_retention_ids"]!["items"]!.AsObject();
        Assert(Board(explicitRecovery).Count == 3 && recovery["id"]!.GetValue<string>() == "plot_trinket_retention_2" &&
            rewards.Count == 8 && Pool(explicitRecovery).Count == 8 &&
            rewards.Count(pair => pair.Value?["hero_name"]?.GetValue<string>().StartsWith("Lost hero ") == true) == 7,
            "An explicitly requested recovery plot remains supported and copies prior losses without consuming them.");
        var explicitDelivered = explicitRecovery["persist.quest.json"].DeepClone();
        TownEventSaveEditor.ReplaceCurrent(explicitRecovery, events.Events.Single(item => item.Id == "test_free_activity"),
            new TownEventInitializer(f.Content, 8).Initialize);
        Assert(JsonNode.DeepEquals(explicitDelivered, explicitRecovery["persist.quest.json"]),
            "Replacing an event preserves its already delivered explicit quest and retained items.");

        var existing = Saves("very_rare", 8, 8);
        var alreadyGenerated = recovery.DeepClone();
        // Existing lower-tier recovery rewards must not be replaced just because
        // a later theft would now qualify for a higher-tier recovery quest.
        alreadyGenerated["id"] = "plot_trinket_retention_0";
        alreadyGenerated["difficulty"] = 1;
        Board(existing)["2"] = alreadyGenerated;
        var preserved = Board(existing).DeepClone();
        Apply(existing);
        Assert(JsonNode.DeepEquals(preserved, Board(existing)) && Pool(existing).Count == 16,
            "Theft does not replace existing recovery quests or their rewards while retaining additional lost items.");

        var composite = Saves("very_rare", 8, 8);
        Board(composite)["2"] = alreadyGenerated.DeepClone();
        var compositeBoard = Board(composite).DeepClone();
        TownEventSaveEditor.ReplaceCurrent(composite, theft with
        {
            Effects = [new TownEventEffect("trinket_retention_add_from_storage", "", 8),
                new TownEventEffect("plot_quest", "plot_trinket_retention_2", 1)]
        }, new TownEventInitializer(f.Content, 9).Initialize);
        Assert(JsonNode.DeepEquals(compositeBoard, Board(composite)) && Pool(composite).Count == 16,
            "An explicit plot effect still respects the existing recovery quest's dungeon reservation.");
        Console.WriteLine("PASS: theft without automatic recovery quests, explicit event plots, preserved quests/losses and unchanged weeks.");
    }
}
