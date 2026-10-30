using System.Text.Json;
using System.Reflection;
using System.Windows.Controls;
using DarkestDungeonSaveEditor.App;

internal static partial class ContractSuite
{
    public static async Task RunQuestCompletionOnlyAsync(string repositoryRoot)
    {
        var root = Path.Combine(repositoryRoot, "artifacts", "quest-completion-contracts", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        // Objective data copied from installed Base/DLC definitions and the effective
        // Sunward/Vermintide definitions inspected on 2026-10-29. Composite tests below
        // are explicit transaction controls, not claims about a real combined mission.
        const string definitionsJson = """
        {"goals":[
          {"id":"explore_all_rooms","type":"explore_room","data":{"amount":0,"percentage":0.9}},
          {"id":"battle_all_rooms","type":"battle_room","data":{"amount":0,"percentage":1}},
          {"id":"farm_battle_all_rooms","type":"battle_room","data":{"amount":0,"percentage":0.6}},
          {"id":"kill_formless_flesh_A","type":"kill_monster","data":{"monster_class_ids":["formless_guard_A","formless_melee_A","formless_ranged_A","formless_weak_A"],"amount":4}},
          {"id":"kill_practitioner","type":"kill_monster","data":{"monster_class_ids":["torii_D"],"amount":2}},
          {"id":"inventory_activate_beacons","type":"activate","data":{"curio_name":"beacon","amount":3}},
          {"id":"gather_bloodcrate","type":"activate","data":{"curio_name":"cc_wine_crate_quest","additional_loot":"BLOOD_CRATE","amount":3}},
          {"id":"gather_holy_relic","type":"gather","data":{"curio_name":"reliquary","item":{"type":"quest_item","id":"holy_relic","amount":3}}},
          {"id":"gather_tome","type":"gather","data":{"curio_name":"vermintide_bone_alter","item":{"type":"quest_item","id":"vermintide_tome","amount":3}}},
          {"id":"tutorial_final_room","type":"tutorial_room","data":{"room_id":"rooB"}},
          {"id":"town_progression_battle","type":"battle","data":{"amount":2,"percentage":0}}
        ]}
        """;
        var gameDirectory = Path.Combine(root, "game");
        var definitionPath = WriteMultiMash(gameDirectory, "campaign/quest/quest.types.json", definitionsJson);
        var source = new ActiveContentSource("base", "Base game", "base", gameDirectory, 0);
        var profileRoot = Path.Combine(root, "profile_0");
        Directory.CreateDirectory(profileRoot);
        // Persistent expeditions must write the active nested pair, not stale root files.
        var profile = new SaveProfile("profile_0", profileRoot, Path.Combine(profileRoot, "persist.estate.json"), "test", DateTime.UtcNow)
            { RaidSaveRelativeDirectory = "plot_trial" };
        Directory.CreateDirectory(Path.GetDirectoryName(profile.MapSavePath)!);
        var gamePath = WriteMultiMash(profileRoot, "persist.game.json",
            """{"base_root":{"inraid":true,"raiddungeon":"weald","raid_save":"plot_trial","date":{"week":13}}}""");
        File.WriteAllText(profile.EstateSavePath, "{}");
        var content = new ActiveContentSnapshot(profile, "base", [source], [], root, gamePath, 0, ComputeSha256(gamePath));
        var definitions = QuestBoardSnapshotReader.ReadGoalDefinitions(content);
        var map = CompletionTestMap();
        var codec = DsonSaveCodec.CreateNative();
        var service = new SaveEditService(codec, new(root, Path.Combine(root, "work"), Path.Combine(root, "backups")));
        var item = new QuantityItemDefinition("quest_item", "holy_relic", QuantityItemStorageKind.RaidInventory,
            1, false, 0, "base", "fixture.inventory.items.darkest", false, ["base"]);
        QuantityItemCatalogResult Inventory(JsonObject _) => new([item], [], "")
        { RaidStorage = new(16, "base", "fixture.inventory.system_configs.darkest", source, ""), SaveContext = QuantityItemSaveContext.Raid };
        BattleMapSnapshot Snapshot(JsonObject raid) => BattleMapSnapshotReader.Parse(profileRoot, profile.MapSavePath,
            profile.RaidSavePath, "", "", map, raid);
        (JsonObject Map, JsonObject Raid, QuestCompletionPreview Preview) Complete(params string[] ids)
        {
            var raid = CompletionTestRaid(ids);
            return QuestCompletionSaveEditor.Complete(map, raid, Snapshot(raid), definitions, Inventory);
        }
        JsonNode? Goal(JsonObject raid, string id) => raid["base_root"]!["raid_instance"]![id];
        var originalMap = map.ToJsonString();

        var exploration = Complete("explore_all_rooms");
        Assert(Goal(exploration.Raid, "explore_all_rooms")!["explored_room_count"]!.GetValue<int>() == 9,
            "Ten ordinary rooms at 90 percent require nine; the room with a type-2 door is excluded.");
        Assert(exploration.Map.ToJsonString() == originalMap, "Exploration counters do not rewrite the map.");
        var kill = Complete("kill_formless_flesh_A");
        Assert(Goal(kill.Raid, "kill_formless_flesh_A")!["monster_kill_count"]!.GetValue<int>() == 4 &&
               kill.Map.ToJsonString() == originalMap, "The Flesh goal uses four native kills, not its single displayed boss.");
        var modKill = Complete("kill_practitioner");
        Assert(Goal(modKill.Raid, "kill_practitioner")!["monster_kill_count"]!.GetValue<int>() == 2,
            "The actual Sunward goal requires two kills and is not classified by a quest ID containing wave.");
        foreach (var id in new[] { "inventory_activate_beacons", "gather_bloodcrate" })
        {
            var activated = Complete(id);
            Assert(Goal(activated.Raid, id)!["curios_activated_count"]!.GetValue<int>() == 3 &&
                   activated.Raid["base_root"]!["party"]!["inventory"]!["items"]!.AsObject().Count == 0,
                "Activation marks the objective without simulating interaction loot; a gather-prefixed ID is not a gather type.");
        }
        var battles = Complete("battle_all_rooms");
        var dynamic = battles.Map["base_root"]!["map"]!["static_dynamic"]!["areas"]!;
        Assert(battles.Preview.DeletedBattles == 2 && dynamic["rooB"]!["tiles"]!["tile0"]!["content"]!.GetValue<int>() == 0 &&
               dynamic["rooC"]!["tiles"]!["tile0"]!["content"]!.GetValue<int>() == 0 &&
               dynamic["rooS"]!["tiles"]!["tile0"]!["content"]!.GetValue<int>() == 1,
            "Clear-battle completion deletes ordinary required battles, leaving hidden-room battles outside the goal.");
        Assert(dynamic["rooB"]!["tiles"]!["tile0"]!["curio_prop"]!.GetValue<int>() == 0 &&
               battles.Map["base_root"]!["map"]!["static_dynamic"]!["static_save"]!["base_root"]!["areas"]!["rooB"]!["tiles"]!["tile0"]!["cur"]!.GetValue<int>() == 0,
            "Existing deletion clears treasure/curio bindings as approved.");
        Assert(Goal(battles.Raid, "battle_all_rooms")!["target_battle_count"]!.GetValue<int>() == 0,
            "The remaining ordinary battle-room count is zero, matching the native startup target.");
        var gather = Complete("gather_holy_relic");
        Assert(RaidInventorySaveEditor.CountAmount(gather.Raid, item) == 3 && gather.Preview.AddedItems == 3,
            "Gather uses actual one-per-stack relics, not a fabricated progress counter.");
        void Reject(JsonObject raid, IReadOnlyDictionary<uint, JsonElement>? goals = null)
        {
            var before = raid.ToJsonString();
            var rejected = false;
            try { QuestCompletionSaveEditor.Complete(map, raid, Snapshot(raid), goals ?? definitions, Inventory); }
            catch (Exception error) when (error is InvalidOperationException or InvalidDataException) { rejected = true; }
            Assert(rejected && raid.ToJsonString() == before && map.ToJsonString() == originalMap,
                "Unsupported or nonstationary input must fail without any partial mutation.");
        }
        var wave = CompletionTestRaid(["battle_all_rooms"]); wave["base_root"]!["raid_instance"]!["type"] = "wave"; Reject(wave);
        Reject(CompletionTestRaid(["tutorial_final_room"]));
        var fighting = CompletionTestRaid(["battle_all_rooms"]); fighting["base_root"]!["inbattle"] = true; Reject(fighting);
        Reject(CompletionTestRaid(["missing_goal"]));
        // A known progression definition is deliberately placed in the main list solely
        // to test refusal of the unsolved fixed-count case, not as a claimed real quest.
        Reject(CompletionTestRaid(["battle_all_rooms", "town_progression_battle"]));
        var full = CompletionTestRaid(["gather_holy_relic"]);
        for (var i = 0; i < 16; i++) full["base_root"]!["party"]!["inventory"]!["items"]![i.ToString()] =
            new JsonObject { ["type"] = "gold", ["id"] = "", ["amount"] = 100 };
        Reject(full);

        async Task Encode(string name, JsonObject document)
        {
            var input = WriteMultiMash(root, "input/" + name, document.ToJsonString());
            await codec.EncodeAsync(input, profile.RaidLocation.GetPath(name), null);
        }
        // Explicit composite transaction fixture: exercise a map AND a raid replacement.
        var transactionRaid = CompletionTestRaid(["battle_all_rooms", "kill_formless_flesh_A"]);
        await Encode("persist.map.json", map);
        await Encode("persist.raid.json", transactionRaid);
        File.WriteAllText(Path.Combine(profileRoot, "persist.map.json"), "stale root map");
        File.WriteAllText(Path.Combine(profileRoot, "persist.raid.json"), "stale root raid");
        var originals = ProfileFilesHash(profileRoot);
        var snapshot = await new BattleMapSnapshotReader(codec).LoadAsync(profileRoot);
        var prepared = await service.PrepareQuestCompletionAsync(profile, snapshot, content);
        Assert(prepared.Files.Count == 2 && prepared.Files.All(file => DsonSaveCodec.IsDson(file.EncodedPath)) &&
               ProfileFilesHash(profileRoot).OrderBy(p => p.Key).SequenceEqual(originals.OrderBy(p => p.Key)),
            "Preview produces a native DSON roundtrip without touching the profile.");
        var failOnce = true;
        service.BeforeTargetReplace = path => { if (path == profile.RaidSavePath && failOnce) { failOnce = false; throw new IOException("Injected second-file failure"); } };
        var failed = false;
        try { await service.CommitAsync(prepared); } catch (InvalidOperationException) { failed = true; }
        Assert(failed && ProfileFilesHash(profileRoot).OrderBy(p => p.Key).SequenceEqual(originals.OrderBy(p => p.Key)),
            "A failure on the second file restores the first file, preserving the complete original profile.");
        service.BeforeTargetReplace = null;
        var result = await service.CommitAsync(prepared);
        Assert(File.Exists(Path.Combine(result.BackupDirectory, "plot_trial", "persist.map.json")) &&
               File.ReadAllText(Path.Combine(profileRoot, "persist.raid.json")) == "stale root raid" &&
               ComputeSha256(gamePath) == originals["persist.game.json"],
            "Commit backs up the nested expedition and never changes root leftovers, week or inraid.");
        await codec.DecodeAsync(profile.RaidSavePath, Path.Combine(root, "result.raid.json"));
        var committed = JsonSupport.ReadObject(Path.Combine(root, "result.raid.json"));
        Assert(JsonNode.DeepEquals(committed["base_root"]!["raid_instance"]!["town_progression_goals"],
            transactionRaid["base_root"]!["raid_instance"]!["town_progression_goals"]), "Town progression goals stay untouched.");
        Assert(JsonNode.DeepEquals(committed["base_root"]!["raid_instance"]!["completion_reward"],
            transactionRaid["base_root"]!["raid_instance"]!["completion_reward"]), "Native completion rewards stay untouched.");

        await RunWpfContractsAsync(async () =>
        {
            var view = new BattleMapView { UsesSharedProfileMonitor = true };
            var button = (Button)view.FindName("ForceCompleteQuestButton");
            var notify = typeof(BattleMapView).GetMethod("NotifySaveEditBusyAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Task Busy(bool value) => (Task)notify.Invoke(view, [value])!;
            try
            {
                Assert(!button.IsEnabled, "Quest completion is disabled before loading.");
                await view.LoadProfileAsync(profile, codec, null, content);
                Assert(button.IsEnabled, "A loaded ordinary raid exposes quest completion.");
                await Busy(true);
                Assert(!button.IsEnabled, "Quest completion is disabled during other writes.");
                var synchronization = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                view.SaveEditBusyChanged += busy => busy ? Task.CompletedTask : synchronization.Task;
                var finishing = Busy(false);
                Assert(!button.IsEnabled && !finishing.IsCompleted, "The button stays disabled until synchronization completes.");
                synchronization.SetResult();
                await finishing;
                Assert(button.IsEnabled, "Successful synchronization restores quest completion.");
                foreach (var language in new[] { "en", "zh-CN" })
                {
                    EditorText.Initialize(language, EditorText.SystemCulture);
                    view.RefreshLanguage();
                    RenderContractElement(view, new Size(1008, 425), Path.Combine(root, "quest-completion-" + language + ".png"));
                    Assert(button.Content?.ToString() == EditorText.Get("QuestCompletion_Button"), "The new action follows live language switching.");
                }
            }
            finally { view.ClearProfile(); }
            Assert(!button.IsEnabled, "Clearing a profile disables quest completion.");
        });

        await Encode("persist.map.json", map); await Encode("persist.raid.json", transactionRaid);
        snapshot = await new BattleMapSnapshotReader(codec).LoadAsync(profileRoot);
        prepared = await service.PrepareQuestCompletionAsync(profile, snapshot, content);
        File.AppendAllText(definitionPath, " ");
        failed = false;
        try { await service.CommitAsync(prepared); }
        catch (InvalidOperationException error) when (error.Message == EditorText.Get("QuestBoard_DefinitionChanged")) { failed = true; }
        Assert(failed, "Changed Mod definitions invalidate the prepared completion before writing.");
        Assert(ComputeSha256(profile.MapSavePath).Equals(prepared.SourceHashes["persist.map.json"], StringComparison.OrdinalIgnoreCase),
            "Rejecting stale definitions leaves the map unchanged.");
        File.WriteAllText(definitionPath, definitionsJson);
        File.AppendAllText(gamePath, " ");
        failed = false;
        try { await service.CommitAsync(prepared); }
        catch (InvalidOperationException error) when (error.Message.Contains("configuration changed", StringComparison.Ordinal)) { failed = true; }
        Assert(failed && ComputeSha256(profile.RaidSavePath).Equals(prepared.SourceHashes["persist.raid.json"], StringComparison.OrdinalIgnoreCase),
            "Changed game/profile state invalidates the prepared completion without writing.");
        File.WriteAllText(gamePath, """{"base_root":{"inraid":true,"raiddungeon":"weald","raid_save":"plot_trial","date":{"week":13}}}""");

        var externalMap = (JsonObject)map.DeepClone();
        externalMap["base_root"]!["map"]!["static_dynamic"]!["areas"]!["rooA"]!["knowledge"] = 2;
        var externalInput = WriteMultiMash(root, "external/map.json", externalMap.ToJsonString());
        var externalBinary = Path.Combine(root, "external", "map.bin");
        await codec.EncodeAsync(externalInput, externalBinary, null);
        service.AfterTargetReplace = path =>
        {
            if (path == profile.MapSavePath) File.Copy(externalBinary, path, overwrite: true);
        };
        failed = false;
        try { await service.CommitAsync(prepared); } catch (AggregateException) { failed = true; }
        Assert(failed && ComputeSha256(profile.MapSavePath) == ComputeSha256(externalBinary) &&
               ComputeSha256(profile.RaidSavePath).Equals(prepared.SourceHashes["persist.raid.json"], StringComparison.OrdinalIgnoreCase),
            "Recovery preserves a newer external map and leaves the unwritten raid untouched.");
        Console.WriteLine("PASS: Native/Mod objective types, deletion semantics, inventory, tutorial/wave rejection, DSON roundtrip, nested targets, rollback and resource invalidation.");
        Console.WriteLine("Artifacts: " + root);
    }

    private static JsonObject CompletionTestRaid(string[] goals) => JsonNode.Parse("""
    {"base_root":{"raid_instance":{"id":"generated_1","type":"cleanse","dungeon":"weald","difficulty":1,"length":1,
      "goal_ids":[],"completion_reward":{"resolve_xp":2},"town_progression_goals":{"town_progression_battle":{"current_battle_count":1,"target_battle_count":2}}},
      "in_area":1097822066,"areatile":1,"last_room_id":1097822066,"inbattle":false,"teleported":false,
      "camp":{"phase":0},"loot":{"queue":{},"queue_items":{"items":{}},"owned_items":{"items":{}}},
      "in_doorway":{"area_to":1701736302,"tile_to":0,"implied":true},
      "party":{"IsMovingLeft()":false,"retreat_room":1097822066,"inventory":{"items":{}}}}}
    """)!.AsObject().WithCompletionGoals(goals);

    private static JsonObject CompletionTestMap()
    {
        var areas = new JsonObject(); var dynamic = new JsonObject();
        foreach (var suffix in "ABCDEFGHIJS")
        {
            var id = "roo" + suffix;
            var room = new JsonObject { ["id"] = CompletionAreaId(id), ["kind"] = 0 };
            for (var i = 0; i < 8; i++) room["door" + i] = new JsonObject
                { ["area_to"] = 1701736302, ["tile_to"] = 0, ["implied"] = true, ["type"] = suffix == 'S' && i == 0 ? 2 : 0 };
            room["tiles"] = new JsonObject { ["tile0"] = new JsonObject
                { ["type"] = 3, ["cur"] = suffix == 'B' ? 123 : 0, ["obstacle"] = 0, ["mappos"] = new JsonArray((double)(suffix - 'A'), 0.0) } };
            areas[id] = room;
            var battle = suffix is 'B' or 'C' or 'S';
            dynamic[id] = new JsonObject { ["knowledge"] = 3, ["reversed"] = false,
                ["tiles"] = new JsonObject { ["tile0"] = new JsonObject { ["content"] = suffix == 'B' ? 10 : battle ? 1 : 0,
                    ["knowledge"] = 3, ["curio_prop"] = suffix == 'B' ? 123 : 0, ["mash_index"] = battle ? 0 : -1, ["mash_type"] = battle ? 1 : 7 } } };
        }
        return new JsonObject { ["base_root"] = new JsonObject { ["map"] = new JsonObject
        { ["bounds"] = new JsonArray(0.0, 18.0, 0.0, 0.0), ["entrance_id"] = CompletionAreaId("rooA"),
            ["final_room_id"] = CompletionAreaId("rooJ"), ["static_dynamic"] = new JsonObject
            { ["static_save"] = new JsonObject { ["base_root"] = new JsonObject { ["areas"] = areas } }, ["areas"] = dynamic } } } };
    }

    private static int CompletionAreaId(string id) => System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(Encoding.ASCII.GetBytes(id));
}

internal static class CompletionFixtureExtensions
{
    internal static JsonObject WithCompletionGoals(this JsonObject document, string[] goals)
    {
        document["base_root"]!["raid_instance"]!["goal_ids"] = new JsonArray(goals.Select(id => (JsonNode?)JsonValue.Create(id)).ToArray());
        return document;
    }
}
