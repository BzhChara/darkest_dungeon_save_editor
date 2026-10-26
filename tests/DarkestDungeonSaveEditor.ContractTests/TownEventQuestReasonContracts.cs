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
                (Dictionary<string, JsonObject> Saves, TownEventMutationPreview Preview, JsonObject OldQuest) Apply(
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
                    var eventDefinition = secondProgression ? selected with
                    {
                        Effects = [new("plot_quest", "next_plot", 1), new("plot_quest", "event_plot", 1)]
                    } : selected;
                    var preview = TownEventSaveEditor.ReplaceCurrent(saves, eventDefinition, new TownEventInitializer(f.Content, 7).Initialize);
                    Assert(Root(saves, "town_event")["current_result_event_id"]!.GetValue<int>() == selected.NativeId &&
                        Root(saves, "campaign_log")["total_weeks"]!.GetValue<int>() == 5,
                        "A quest diagnostic must not prevent event replacement or advance the week.");
                    return (saves, preview, oldQuest);
                }
                string Skipped(string id, string reason) => chinese ? $"未生成任务 {id}：{reason}。" : $"Quest {id} was not added: {reason}.";
                void Check(string zh, string en, Action<JsonObject>? configure = null,
                    Action<Dictionary<string, JsonObject>>? arrange = null)
                {
                    var result = Apply(configure, arrange);
                    Assert(result.Preview.Changes.Single() == Skipped("event_plot", chinese ? zh : en),
                        culture + ": the skipped quest must identify its exact cause and dependency values: " + en);
                    Assert(JsonNode.DeepEquals(result.OldQuest, result.Saves["persist.quest.json"]),
                        "Explaining an ineligible quest must preserve the existing board and retention inventory.");
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
                Assert(Root(progression.Saves, "quest")["quests"]!.AsObject().Count == 2 &&
                    progression.Preview.Changes.Last() == Skipped("next_plot", chinese
                        ? "本次操作已为区域 weald 生成了另一个进度任务"
                        : "another progression quest has already been added for weald during this operation"),
                    "The per-dungeon progression limit must explain which region was reserved without changing definition order.");
                var existing = Apply(arrange: s => Root(s, "quest")["quests"]!["1"] = new JsonObject { ["id"] = "event_plot" });
                Assert(existing.Preview.Changes.Single() == (chinese ? "任务已存在，保留原样：event_plot。" : "Quest already exists and is preserved: event_plot.") &&
                    JsonNode.DeepEquals(existing.OldQuest, existing.Saves["persist.quest.json"]),
                    "An existing quest must keep its distinct preservation message and must not be duplicated.");

                void CheckAllowed(Action<JsonObject>? configure = null, Action<Dictionary<string, JsonObject>>? arrange = null)
                {
                    var result = Apply(configure, arrange);
                    Assert(Root(result.Saves, "quest")["quests"]!.AsObject().Count == 2 &&
                        result.Preview.Changes.Single() == (chinese ? "已添加事件任务：event_plot。" : "Event quest added: event_plot."),
                        "Detailed reasons must not add restrictions to an eligible quest.");
                }
                CheckAllowed();
                CheckAllowed(d => d["is_repeatable"] = true, s => Complete(s, "event_plot"));
                CheckAllowed(d => d["plot_quest_dependency"] = "previous_plot", s => Complete(s, "previous_plot"));
                CheckAllowed(d => d["estate_inventory_dependency"] = JsonNode.Parse("""
                    {"consume_on_attempt":true,"items_definition":{"items":{"0":{"id":"keep","type":"estate","amount":8}}}}
                    """));
                Console.WriteLine("PASS: " + culture + " quest skip reasons, dependency details, preserved boards and unchanged eligible generation.");
            }
        }
        finally
        {
            File.WriteAllText(path, template.ToJsonString());
            EditorText.Initialize(previousCulture.Name, previousSystemCulture);
        }
    }
}
