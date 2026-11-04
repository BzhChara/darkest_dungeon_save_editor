using System.Globalization;

internal static partial class ContractSuite
{
    private static async Task VerifyEventQuestReasonsAsync(string root)
    {
        // Reduced fixtures exercise existing quest predicates and their messages.
        // They do not assert that every combination occurs in a shipped event.
        var f = await BuildTownEventFixtureAsync(root);
        var game = f.Content.Resolution!.GameDirectory;
        var path = Path.Combine(game, "campaign", "quest", "event.quest.plot_quests.json");
        var template = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var selected = TownEventCatalog.Load(f.Content).Events.Single(item => item.Id == "test_plot_quest");
        WriteMultiMash(game, "campaign/progression/progression.json", """{"dungeon":{"level_threshold_table":[0,4,8]}}""");
        WriteMultiMash(game, "campaign/progression/infestation.progression.json",
            """{"infestation":{"sequence_elements":[{"id":"before"},{"id":"after"}]}}""");
        WriteMultiMash(game, "trinkets/test.rarities.trinkets.json", """{"rarities":[{"id":"event_rarity"},{"id":"common"}]}""");

        JsonObject Root(Dictionary<string, JsonObject> saves, string name) => saves["persist." + name + ".json"]["base_root"]!.AsObject();
        void Complete(Dictionary<string, JsonObject> saves, string id) => Root(saves, "progression")["completed_plot_quests_data"] =
            new JsonObject { ["0"] = new JsonObject { ["plot_quest_id"] = unchecked((int)HashLoc2Key(id)) } };
        void Retain(Dictionary<string, JsonObject> saves) => Root(saves, "quest")["trinket_retention_ids"]!["items"] =
            JsonNode.Parse("""{"0":{"id":"old_owned","type":"trinket","amount":1}}""");

        var previousCulture = EditorText.Culture;
        var previousSystemCulture = EditorText.SystemCulture;
        try
        {
            foreach (var culture in new[] { "zh-CN", "en" })
            {
                EditorText.Initialize(culture, previousSystemCulture);
                var chinese = culture == "zh-CN";
                (Dictionary<string, JsonObject> Saves, TownEventMutationPreview Preview, JsonObject OldQuest,
                    JsonObject OldProgression, JsonObject OldEstate) Apply(
                    Action<JsonObject>? configure = null, Action<Dictionary<string, JsonObject>>? arrange = null,
                    bool secondProgression = false)
                {
                    var document = template.DeepClone().AsObject();
                    var definition = document["plot_quests"]![0]!.AsObject();
                    configure?.Invoke(definition);
                    if (secondProgression)
                    {
                        var second = definition.DeepClone().AsObject();
                        second["id"] = "next_plot";
                        document["plot_quests"]!.AsArray().Add(second);
                    }
                    File.WriteAllText(path, document.ToJsonString());
                    var saves = f.Saves.ToDictionary(pair => pair.Key, pair => pair.Value.DeepClone().AsObject());
                    arrange?.Invoke(saves);
                    var oldQuest = saves["persist.quest.json"].DeepClone().AsObject();
                    var oldProgression = saves["persist.progression.json"].DeepClone().AsObject();
                    var oldEstate = saves["persist.estate.json"].DeepClone().AsObject();
                    var eventDefinition = secondProgression ? selected with
                    {
                        Effects = [new("plot_quest", "next_plot", 1), new("plot_quest", "event_plot", 1)]
                    } : selected;
                    var preview = TownEventSaveEditor.ReplaceCurrent(saves, eventDefinition, new TownEventInitializer(f.Content, 7).Initialize);
                    Assert(Root(saves, "town_event")["current_result_event_id"]!.GetValue<int>() == selected.NativeId &&
                        Root(saves, "campaign_log")["total_weeks"]!.GetValue<int>() == 5,
                        "A quest diagnostic must not prevent event replacement or advance the week.");
                    return (saves, preview, oldQuest, oldProgression, oldEstate);
                }
                string Bypassed(string id, string reason) => chinese
                    ? $"仍生成任务 {id}（绕过自然生成限制）：{reason}。"
                    : $"Generate quest {id} despite its natural generation restriction: {reason}.";
                void Check(string zh, string en, Action<JsonObject>? configure = null,
                    Action<Dictionary<string, JsonObject>>? arrange = null)
                {
                    var result = Apply(configure, arrange);
                    var warning = Bypassed("event_plot", chinese ? zh : en);
                    Assert(result.Preview.Warnings.SequenceEqual([warning]) && result.Preview.Changes.SequenceEqual(
                        [warning, chinese ? "新增事件任务：event_plot。" : "New event quest: event_plot."]),
                        culture + ": manual generation must explain the exact bypassed condition: " + en);
                    var quest = result.Saves["persist.quest.json"].DeepClone().AsObject();
                    var board = quest["base_root"]!["quests"]!.AsObject();
                    var added = board.Single(pair => pair.Value?["id"]?.GetValue<string>() == "event_plot");
                    Assert(added.Value!["goal_ids"]![0]!.GetValue<string>() == "kill_event" &&
                        added.Value["completion_reward"] is JsonObject, "Bypassed quests must still initialize their goals and rewards.");
                    board.Remove(added.Key);
                    Assert(JsonNode.DeepEquals(result.OldQuest, quest) &&
                        JsonNode.DeepEquals(result.OldProgression, result.Saves["persist.progression.json"]) &&
                        JsonNode.DeepEquals(result.OldEstate, result.Saves["persist.estate.json"]),
                        "Manual generation preserves existing quests, retained items, completion history and estate resources.");
                }

                Check("该任务在定义中被禁用", "the quest is disabled in its definition", d => d["is_enabled"] = false);
                Check("任务已完成且不可重复", "the quest is already completed and cannot be repeated",
                    arrange: s => Complete(s, "event_plot"));
                Check("该进度任务已完成，不能再次生成", "the progression quest is already completed and cannot be generated again",
                    d => { d["is_repeatable"] = true; d["is_progression"] = true; }, s => Complete(s, "event_plot"));
                Check("前置任务 previous_plot 尚未完成", "prerequisite quest previous_plot has not been completed",
                    d => d["plot_quest_dependency"] = "previous_plot");
                Check("区域 weald 的进度等级不足（需要 2，当前 1）", "the progression level of weald is too low (requires 2, current 1)",
                    d => d["dungeon_level"] = 2, s => Root(s, "progression")["dungeon"]!["weald"] = new JsonObject { ["xp"] = 4 });
                Check("存档记录的英雄最高意志等级不足（需要 2，记录为 1）", "the saved highest hero resolve level is too low (requires 2, recorded 1)",
                    d => d["highest_hero_resolve_level_dependency"] = 2);
                Check("丢失饰品数量不足（需要 2，当前 1）", "not enough lost trinkets (requires 2, current 1)",
                    d => d["trinket_retention_count"] = 2, Retain);
                Check("失物中没有稀有度为 event_rarity 或更高的饰品", "no lost trinket meets the minimum rarity event_rarity or higher",
                    d => { d["trinket_retention_count"] = 1; d["trinket_retention_minimum_rarity"] = "event_rarity"; }, Retain);
                Check("区域 weald 已有饰品找回任务，或本次操作已为该区域生成找回任务",
                    "a trinket recovery quest for weald already exists or has been added during this operation",
                    d => d["trinket_retention_count"] = 1, s =>
                    {
                        Retain(s);
                        Root(s, "quest")["quests"]!["1"] = JsonNode.Parse("""
                            {"id":"existing_recovery","dungeon":"weald","completion_reward":
                              {"trinket_retention_ids":{"items":{"0":{"id":"old_owned","type":"trinket","amount":1}}}}}
                            """);
                    });
                Check("阶段定义中找不到任务要求的猩红诅咒蔓延阶段 missing_stage",
                    "required infestation stage missing_stage is missing from the stage definitions",
                    d => d["infestation_sequence_element_dependency"] = "missing_stage");
                Check("猩红诅咒蔓延进度尚未超过所需阶段 before", "infestation has not advanced beyond the required stage before",
                    d => d["infestation_sequence_element_dependency"] = "before",
                    s => Root(s, "progression")["infestation"] = new JsonObject { ["sequence_element_id"] = unchecked((int)HashLoc2Key("before")) });
                // Duplicate item requirements are aggregated, just as eligibility is.
                Check("所需物品 estate/keep 数量不足（需要 8，当前 7）", "not enough required items estate/keep (requires 8, current 7)",
                    d => d["estate_inventory_dependency"] = JsonNode.Parse("""
                        {"consume_on_attempt":false,"items_definition":{"items":{
                          "0":{"id":"keep","type":"estate","amount":4},"1":{"id":"keep","type":"estate","amount":4}}}}
                        """));

                var progression = Apply(d => d["is_progression"] = true, secondProgression: true);
                Assert(Root(progression.Saves, "quest")["quests"]!.AsObject().Count == 3 &&
                    progression.Preview.Warnings.Single() == Bypassed("next_plot", chinese
                        ? "本次操作已为区域 weald 生成了另一个进度任务"
                        : "another progression quest has already been added for weald during this operation"),
                    "The per-dungeon natural limit becomes a warning without changing definition order or dropping an explicit quest.");
                var existing = Apply(arrange: s => Root(s, "quest")["quests"]!["1"] = new JsonObject { ["id"] = "event_plot" });
                Assert(existing.Preview.Changes.Single() == (chinese ? "任务已存在，保留原样：event_plot。" : "Quest already exists and is preserved: event_plot.") &&
                    existing.Preview.Warnings.SequenceEqual(existing.Preview.Changes) &&
                    JsonNode.DeepEquals(existing.OldQuest, existing.Saves["persist.quest.json"]),
                    "An existing quest must keep its distinct preservation message and must not be duplicated.");

                void CheckAllowed(Action<JsonObject>? configure = null, Action<Dictionary<string, JsonObject>>? arrange = null)
                {
                    var result = Apply(configure, arrange);
                    Assert(Root(result.Saves, "quest")["quests"]!.AsObject().Count == 2 &&
                        result.Preview.Warnings.Count == 0 &&
                        result.Preview.Changes.Single() == (chinese ? "新增事件任务：event_plot。" : "New event quest: event_plot."),
                        "Detailed reasons must not add restrictions to an eligible quest.");
                }
                CheckAllowed();
                CheckAllowed(d => d["is_repeatable"] = true, s => Complete(s, "event_plot"));
                CheckAllowed(d => d["plot_quest_dependency"] = "previous_plot", s => Complete(s, "previous_plot"));
                CheckAllowed(d => d["estate_inventory_dependency"] = JsonNode.Parse("""
                    {"consume_on_attempt":true,"items_definition":{"items":{"0":{"id":"keep","type":"estate","amount":8}}}}
                    """));
                Console.WriteLine("PASS: " + culture + " manual event quests bypass natural predicates with exact warnings, preserve history/resources and deduplicate existing quests.");
            }
        }
        finally
        {
            File.WriteAllText(path, template.ToJsonString());
            EditorText.Initialize(previousCulture.Name, previousSystemCulture);
        }
    }

    private static async Task VerifyManualEventQuestTransactionsAsync(string root)
    {
        // Policy fixtures cover completed and locked plots through the real
        // binary preview/commit path; they are not claimed installed Mod cases.
        foreach (var scenario in new[] { "completed", "prerequisite", "invalid-reward", "malformed-reward", "scalar-items",
                     "scalar-thresholds", "invalid-extra-reward", "invalid-rarity", "missing-goal" })
        {
            var run = Path.Combine(root, scenario);
            var f = await BuildTownEventFixtureAsync(run);
            var profile = f.Content.Profile;
            var codec = DsonSaveCodec.CreateNative();
            var plotPath = Path.Combine(f.Content.Resolution!.GameDirectory, "campaign/quest/event.quest.plot_quests.json");
            var document = JsonNode.Parse(File.ReadAllText(plotPath))!.AsObject();
            var definition = document["plot_quests"]![0]!.AsObject();
            if (scenario == "prerequisite") definition["plot_quest_dependency"] = "unfinished_plot";
            else
            {
                var progression = f.Saves["persist.progression.json"].DeepClone().AsObject();
                progression["base_root"]!["completed_plot_quests_data"]!["0"] = new JsonObject
                    { ["plot_quest_id"] = unchecked((int)HashLoc2Key("event_plot")) };
                await codec.EncodeAsync(WriteMultiMash(run, "input/progression.json", progression.ToJsonString()),
                    Path.Combine(profile.ProfileDirectory, "persist.progression.json"), null);
            }
            if (scenario == "invalid-reward")
                definition["quest"]!["completion_reward"]!["items_definition"]!["items"]!["0"] = new JsonObject
                    { ["id"] = "missing_event_reward", ["type"] = "trinket", ["amount"] = 1 };
            if (scenario == "malformed-reward")
                definition["quest"]!["completion_reward"] = "malformed";
            if (scenario == "scalar-items")
                definition["quest"]!["completion_reward"]!["items_definition"]!["items"] = "not-an-item-list";
            if (scenario == "scalar-thresholds")
                definition["quest"]!["threshold_rewards"] = 42;
            if (scenario == "invalid-extra-reward")
                definition["quest"]!["completion_reward"]!["additional_threshold_trinket_rewards"] = new JsonArray(true);
            if (scenario == "invalid-rarity")
                definition["quest"]!["completion_reward"]!["additional_threshold_trinket_rewards"] =
                    new JsonArray(new JsonObject { ["chance_of_loot"] = 1.0, ["rarity_table"] = new JsonArray(false) });
            if (scenario == "missing-goal")
                definition["quest"]!["goal_ids"] = new JsonArray("absent_goal");
            File.WriteAllText(plotPath, document.ToJsonString());
            var selected = TownEventCatalog.Load(f.Content).Events.Single(row => row.Id == "test_plot_quest");
            var before = f.Saves.Keys.ToDictionary(name => name, name => ComputeSha256(Path.Combine(profile.ProfileDirectory, name)));
            if (scenario is not ("completed" or "prerequisite"))
            {
                InvalidDataException? failure = null;
                try { await f.Service.PrepareTownEventEditAsync(profile, selected, f.Content); }
                catch (InvalidDataException error) { failure = error; }
                var expected = scenario switch
                {
                    "invalid-reward" => EditorText.Format("QuestBoard_MissingTrinket", "missing_event_reward"),
                    "missing-goal" => EditorText.Format("QuestBoard_MissingGoal", "absent_goal"),
                    "scalar-items" => EditorText.Format("QuestBoard_InvalidRewardField", "items_definition.items"),
                    "scalar-thresholds" => EditorText.Format("QuestBoard_InvalidRewardField", "threshold_rewards"),
                    "invalid-extra-reward" => EditorText.Format("QuestBoard_InvalidRewardField", "additional_threshold_trinket_rewards[0]"),
                    "invalid-rarity" => EditorText.Format("QuestBoard_InvalidRewardField", "rarity_table[0]"),
                    _ => "Invalid completion reward for quest: event_plot."
                };
                Assert(failure?.Message == expected,
                    "Bypassing natural eligibility must still reject a quest that cannot be initialized.");
                var row = QuestBoardEditCatalog.Load(f.Content).Templates.Single(item => item.Quest.Id == "event_plot");
                Assert(row.UnavailableReason == expected, "Both entry points must identify the same initialization failure.");
                if (scenario == "missing-goal")
                {
                    var copies = f.Saves.ToDictionary(pair => pair.Key, pair => pair.Value.DeepClone().AsObject());
                    var board = copies["persist.quest.json"]["base_root"]!["quests"]!.AsObject();
                    board["0"]!["id"] = "event_plot";
                    var oldBoard = board.DeepClone();
                    var duplicate = TownEventSaveEditor.ReplaceCurrent(copies, selected, new TownEventInitializer(f.Content, 7).Initialize);
                    Assert(JsonNode.DeepEquals(board, oldBoard) && duplicate.Warnings.Contains(EditorText.Format("TownEvent_QuestAlreadyPresent", "event_plot")),
                        "An existing event quest is preserved without trying to initialize it again.");
                }
                Assert(before.All(pair => ComputeSha256(Path.Combine(profile.ProfileDirectory, pair.Key)) == pair.Value),
                    "Rejected initialization must leave every binary profile file untouched.");
                continue;
            }
            var prepared = await f.Service.PrepareTownEventEditAsync(profile, selected, f.Content);
            Assert(prepared.Preview.Warnings.Count == 1 &&
                before.All(pair => ComputeSha256(Path.Combine(profile.ProfileDirectory, pair.Key)) == pair.Value),
                "A bypassed quest must appear in the preview warning without modifying the live test profile.");
            var result = await f.Service.CommitAsync(prepared);
            var decodedQuest = Path.Combine(run, "written-quest.json");
            await codec.DecodeAsync(Path.Combine(profile.ProfileDirectory, "persist.quest.json"), decodedQuest);
            var quest = JsonNode.Parse(File.ReadAllText(decodedQuest))!["base_root"]!;
            Assert(quest["quests"]!.AsObject().Count == 2 && quest["quests"]!["1"]!["id"]!.GetValue<string>() == "event_plot" &&
                JsonNode.DeepEquals(quest["quests"]!["0"], f.Saves["persist.quest.json"]["base_root"]!["quests"]!["0"]),
                "The binary commit appends the requested plot and preserves the existing board entry.");
            Assert(ComputeSha256(Path.Combine(profile.ProfileDirectory, "persist.progression.json")) == before["persist.progression.json"] &&
                before.All(pair => ComputeSha256(Path.Combine(result.BackupDirectory, pair.Key)) == pair.Value),
                "Quest generation preserves completed-plot history and creates an exact pre-write backup.");
            var decodedLog = Path.Combine(run, "written-log.json");
            await codec.DecodeAsync(Path.Combine(profile.ProfileDirectory, "persist.campaign_log.json"), decodedLog);
            Assert(JsonNode.Parse(File.ReadAllText(decodedLog))!["base_root"]!["total_weeks"]!.GetValue<int>() == 5,
                "A committed manual event quest does not advance the week.");
        }
        Console.WriteLine("PASS: completed and prerequisite-locked event quests commit through DSON with backups; invalid rewards still reject without writes.");
    }
}
