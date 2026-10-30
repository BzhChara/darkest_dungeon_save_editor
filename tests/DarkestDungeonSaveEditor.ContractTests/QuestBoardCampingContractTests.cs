using System.Text;

internal static partial class ContractSuite
{
    private static void RunQuestBoardCampingContracts(string root)
    {
        var game = Path.Combine(root, "camping", "game");
        var mod = Path.Combine(root, "camping", "mod");
        void Write(string directory, string relative, string text)
        {
            var path = Path.Combine(directory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text, new UTF8Encoding(true));
        }
        // Native provision lengths and CoM survival/disabled-provision shapes. The extra
        // Mod values below isolate the verified append/lookup consumers, not a reported live bug.
        Write(game, "campaign/provision/provision.json", """
        {"raid_starting_length_inventory_item_lists":[[],[{"type":"supply","id":"firewood","amount":0}],
          [{"type":"supply","id":"firewood","amount":1}],[{"type":"supply","id":"firewood","amount":2}]]}
        """);
        Write(mod, "campaign/provision/extra.provision.json", """
        {"raid_starting_length_inventory_item_lists":[[],[],[{"type":"supply","id":"firewood","amount":2}]]}
        """);
        Write(game, "campaign/quest/quest.types.json", """
        {"goals":[
          {"id":"survive_endless","type":"survival","data":{"amount":0},"starting_items":[]},
          {"id":"survive_short","type":"survival","data":{"amount":20},"starting_items":[]},
          {"id":"kill_formless_flesh_A","type":"kill_monster","data":{"monster_class_ids":["formless_guard_A","formless_melee_A","formless_ranged_A","formless_weak_A"],"amount":4}},
          {"id":"supplied_goal","type":"activate","data":{},"starting_items":[{"type":"supply","id":"firewood","amount":4}]}
        ]}
        """);
        foreach (var part in new[] { "guard", "melee", "ranged", "weak" })
            Write(game, $"monsters/formless_{part}/formless_{part}_A/formless_{part}_A.info.darkest", "shared_health: .id formless");
        Write(game, "campaign/quest/quest.plot_quests.json", """
        {"plot_quests":[
          {"id":"supplied_plot","additional_provisions":{"items":{"0":{"type":"supply","id":"firewood","amount":1}}}},
          {"id":"supplied_plot","additional_provisions":{"items":{"0":{"type":"supply","id":"firewood","amount":5}}}},
          {"id":"plot_endless","disabled_provisions":[{"item_type":"supply","item_id":"firewood"}]},
          {"id":"plot_wave_short","disabled_provisions":[{"item_type":"supply","item_id":"firewood"}]}
        ]}
        """);
        Write(game, "localization/camp.string_table.xml", """
        <root><language id="english"><entry id="town_quest_number_of_camps_random">Random camps</entry>
        <entry id="town_quest_number_of_camps3">Three camps</entry>
        <entry id="town_quest_goal_start_single_survival">Defeat all enemies.</entry>
        <entry id="town_quest_goal_start_plural_survival">Defeat as many enemies as possible.</entry>
        <entry id="town_quest_goal_start_plural_kill_monster">Kill %d %s.</entry>
        <entry id="str_monstername_formless_weak_A">Inchoate Flesh</entry></language>
        <language id="schinese"><entry id="town_quest_number_of_camps_random">随机扎营</entry>
        <entry id="town_quest_number_of_camps3">三次扎营</entry></language></root>
        """);
        WriteFixtureManifest(mod);
        var profile = new SaveProfile("profile_0", Path.Combine(root, "camping", "profile_0"), "", "contract", DateTime.UtcNow);
        var content = new ActiveContentSnapshot(profile, "base", [new("base", "Base", "base", game, 0),
            new("local:camping", "Camping", "local", mod, 1)], [], root, "", 1, "game-hash");
        JsonObject Quest(string id, string type, int length, bool plot, params string[] goals) => new()
        {
            ["id"] = id, ["type"] = type, ["length"] = length, ["is_plot_quest"] = plot,
            ["dungeon"] = "crypts", ["difficulty"] = 1,
            ["goal_ids"] = new JsonArray(goals.Select(id => (JsonNode?)JsonValue.Create(id)).ToArray()),
            ["completion_reward"] = new JsonObject()
        };
        QuestBoardSnapshot Read(params JsonObject[] quests)
        {
            var saved = new JsonObject();
            for (var i = 0; i < quests.Length; i++) saved[i.ToString()] = quests[i];
            return QuestBoardSnapshotReader.Read(content, new JsonObject { ["base_root"] = new JsonObject { ["quests"] = saved } }, "camp-hash");
        }
        var board = Read(Quest("short", "explore", 1, false), Quest("medium", "explore", 2, false),
            Quest("long", "explore", 3, false), Quest("supplied_plot", "activate", 2, true, "supplied_goal"),
            Quest("plot_wave_short", "wave", 2, true, "survive_short", "supplied_goal"),
            Quest("plot_endless", "wave", 2, true, "survive_endless"),
            Quest("not_wave", "explore", 2, true, "survive_endless"),
            Quest("not_plot", "wave", 2, false, "survive_endless"), Quest("no_bucket", "explore", 5, false));
        Assert(board.Quests.Select(quest => quest.Firewood).SequenceEqual(new[] { 0, 3, 2, 12, 0, 0, 3, 3, 0 }),
            "Native camps append effective length lists, positive plot/goal inventories and remove all disabled firewood; plot lookup retains its last identity.");
        Assert(board.Quests.Select(quest => quest.HasRandomCamping).SequenceEqual(new[] { false, false, false, false, false, true, false, false, false }),
            "Only a plot of type wave with a zero-amount survival goal uses the native random camp label.");
        Assert(board.Quests[1].LocalizedCampingTooltip.English == "Three camps" &&
               board.Quests[5].LocalizedCampingTooltip.Chinese == "随机扎营",
            "Camp tooltips use the active game's localized keys, including the random-camp tooltip.");
        Assert(board.Quests[4].LocalizedObjective.English.StartsWith("Defeat all enemies.", StringComparison.Ordinal) &&
               board.Quests[5].LocalizedObjective.English == "Defeat as many enemies as possible.",
            "CoM's finite survival goal uses the single template for any nonzero amount; zero selects the endless template.");
        Assert(Read(Quest("plot_kill_formless_flesh_1", "kill_boss", 2, true, "kill_formless_flesh_A"))
                .Quests[0].LocalizedObjective.English == "Kill 1 Inchoate Flesh.",
            "The shipped Flesh goal's four parts share one health group; show one boss with the last target's name, as the native formatter does.");
        // Same-path overriding still happens before the per-length append operation.
        Write(mod, "campaign/provision/provision.json", """
        {"raid_starting_length_inventory_item_lists":[[],[],[{"type":"supply","id":"firewood","amount":7}]]}
        """);
        WriteFixtureManifest(mod);
        Assert(Read(Quest("medium", "explore", 2, false)).Quests[0].Firewood == 9,
            "A replaced provision file must not also contribute the base file's count.");
    }
}
