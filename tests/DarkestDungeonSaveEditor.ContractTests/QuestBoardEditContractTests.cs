using System.Security.Cryptography;
using System.Text.Json;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DarkestDungeonSaveEditor.App;

internal static partial class ContractSuite
{
    public static async Task RunQuestBoardEditsAsync(string repositoryRoot, string? nativeProfile = null)
    {
        var root = Path.Combine(repositoryRoot, "artifacts", "quest-board-edits-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        if (nativeProfile is not null) { await RunNativeQuestBoardEditsAsync(root, nativeProfile); return; }
        var fixture = await BuildTownEventFixtureAsync(Path.Combine(root, "fixture"));
        var game = fixture.Content.Sources[0].Directory;
        WriteMultiMash(game, "dungeons/weald/weald.dungeon.json", """{"requires_quest_to_display":false}""");
        WriteMultiMash(game, "campaign/quest/board.quest.types.json", """
        {"types":[{"id":"explore","goal_lists":[{"dungeon":"all","goals":[["explore_all_rooms"]]},
          {"dungeon":"weald","goals":[["regional_goal"]]}]},{"id":"kill_boss","goal_lists":[]}],
         "goals":[{"id":"explore_all_rooms","type":"explore_room","data":{"percentage":0.9}},
          {"id":"regional_goal","type":"activate","data":{"amount":1}},{"id":"kill_event","type":"kill_monster","data":{}}]}
        """);
        // Reduced shipped generation tables. The extra region goal and locked
        // plot below are explicit synthetic controls, not claimed live Mod cases.
        WriteMultiMash(game, "campaign/quest/board.quest.generation.json", """
        {"generation":{"difficulty":{"generated_resolve_level_difficulties":[{"difficulty":1,"resolve_levels":[0,1,2]}]},
          "type":{"available_quests_table":[{"dungeon":"weald","generated_quest_table":[[
            {"type":"explore","length":1,"chance":1,"difficulty_override":0},{"type":"explore","length":2,"chance":1}]]}]},
          "rewards":{"item_table":[[],[[],[{"type":"gold","id":"","amount":3000}],[{"type":"gold","id":"","amount":4500}]]],
            "resolve_xp_table":[[],[0,2,3]],"heirloom_type_map":[{"dungeon":"weald","types":["crest"]}],
            "heirloom_amount_table":[{"type":"crest","amounts":[[],[0,3,5]]}],
            "trinket_chance_table":[{"rarity":"common","chances":[[],[0,1,1]]}]}}}
        """);
        WriteMultiMash(game, "trinkets/board.rarities.trinkets.json", """{"rarities":[{"id":"event_rarity","award_category":"quest"},{"id":"common","award_category":"universal"}]}""");
        WriteMultiMash(game, "campaign/quest/board.quest.plot_quests.json", """
        {"plot_quests":[{"id":"locked_board_plot","is_enabled":false,"is_repeatable":false,"plot_quest_dependency":"uncompleted",
          "is_started_by_event":true,"highest_hero_resolve_level_dependency":6,"dungeon_level":7,
          "quest":{"is_plot_quest":true,"type":"kill_boss","dungeon":"weald","difficulty":5,"length":3,"goal_ids":["kill_event"],
            "completion_reward":{"resolve_xp":16,"items_definition":{"items":[{"id":"","type":"gold","amount":15000}]}}}},
         {"id":"recover_board_plot","trinket_retention_count":8,"trinket_retention_minimum_rarity":"event_rarity",
          "quest":{"is_plot_quest":true,"type":"kill_boss","dungeon":"weald","difficulty":3,"length":1,"goal_ids":["kill_event"]}},
         {"id":"invalid_board_plot","quest":{"is_plot_quest":true,"type":"missing_type","dungeon":"weald","difficulty":1,"length":1,"goal_ids":[]}},
         {"id":"missing_reward_plot","quest":{"is_plot_quest":true,"type":"kill_boss","dungeon":"weald","difficulty":1,"length":1,"goal_ids":["kill_event"],
           "completion_reward":{"items_definition":{"items":[{"type":"trinket","id":"missing_reward","amount":1}]}}}}]}
        """);
        var document = fixture.Saves["persist.quest.json"];
        document["base_root"]!["quests"] = new JsonObject
        {
            ["0"] = BoardTestQuest("generated_0"), ["1"] = BoardTestQuest("generated_1"), ["2"] = BoardTestQuest("generated_2")
        };
        var profile = fixture.Content.Profile;
        var codec = DsonSaveCodec.CreateNative();
        async Task Save(string name, JsonObject value) => await codec.EncodeAsync(
            WriteMultiMash(root, "inputs/" + name, value.ToJsonString()), Path.Combine(profile.ProfileDirectory, name), null);
        await Save("persist.quest.json", document);
        var original = ProfileFilesHash(profile.ProfileDirectory);
        var catalog = QuestBoardEditCatalog.Load(fixture.Content);
        Assert(catalog.Templates.Count >= 5, "Board catalog includes native-shaped generated and plot definitions.");
        Assert(catalog.Templates.Where(item => !item.Quest.IsPlotQuest).All(item => item.Quest.Difficulty == 1),
            "An explicit zero difficulty override and an omitted override both retain generated difficulty, as in the native branch.");
        Assert(catalog.Templates.Single(item => item.Quest.Id == "invalid_board_plot").UnavailableReason.Length > 0,
            "An unresolved type remains visible but unavailable.");
        Assert(catalog.Templates.Single(item => item.Quest.Id == "missing_reward_plot").UnavailableReason.Contains("missing_reward"),
            "A missing reward disables its own task with the specific dependency ID.");
        var ordinary = catalog.Templates.Single(item => !item.Quest.IsPlotQuest && item.Quest.Length == 1);
        Assert(ordinary.Quest.GoalIds.SequenceEqual(new[] { "explore_all_rooms", "regional_goal" }), "Native goal lists concatenate all and region entries.");
        var deleted = await fixture.Service.PrepareQuestBoardEditAsync(profile, new(QuestBoardEditKind.Delete, original["persist.quest.json"], "1"), fixture.Content);
        Assert(ProfileFilesHash(profile.ProfileDirectory).OrderBy(pair => pair.Key).SequenceEqual(original.OrderBy(pair => pair.Key)), "Preparing a deletion never changes the profile.");
        var proposal = JsonSupport.ReadObject(deleted.File.ProposedDecodedPath);
        Assert(proposal["base_root"]!["quests"]!.AsObject().Select(pair => pair.Key).SequenceEqual(new[] { "0", "2" }) &&
            JsonNode.DeepEquals(proposal["base_root"]!["quests"]!["2"], document["base_root"]!["quests"]!["2"]) &&
            proposal["base_root"]!["plot_quest_total"]!.GetValue<int>() == 77,
            "Deletion preserves other entries, slot identities and plot definition bookkeeping.");
        var result = await fixture.Service.CommitAsync(deleted);
        Assert(File.Exists(Path.Combine(result.BackupDirectory, "persist.quest.json")), "Quest commits keep the original full-profile backup.");
        foreach (var pair in original.Where(pair => pair.Key != "persist.quest.json"))
            Assert(ProfileFilesHash(profile.ProfileDirectory)[pair.Key] == pair.Value, "Deletion changes only the quest save: " + pair.Key);
        var created = await fixture.Service.PrepareQuestBoardEditAsync(profile,
            new(QuestBoardEditKind.Create, ProfileFilesHash(profile.ProfileDirectory)["persist.quest.json"], TemplateKey: ordinary.Key), fixture.Content);
        Assert(created.Preview.Result is { Id: "generated_1", Difficulty: 1, Length: 1 } &&
            created.Preview.Result.Rewards.Any(item => item.Type == "gold" && item.Amount == 3000) &&
            created.Preview.Result.Rewards.Any(item => item.Id == "crest" && item.Amount == 3),
            "Generated quest uses an unused native ID and shipped difficulty/length reward cells.");
        var locked = catalog.Templates.Single(item => item.Quest.Id == "locked_board_plot");
        var replacement = await fixture.Service.PrepareQuestBoardEditAsync(profile,
            new(QuestBoardEditKind.Replace, created.File.OriginalSha256, "0", locked.Key), fixture.Content);
        Assert(replacement.Preview.Result?.Id == "locked_board_plot" &&
            JsonSupport.ReadObject(replacement.File.ProposedDecodedPath)["base_root"]!["quests"]!.AsObject().Select(pair => pair.Key).SequenceEqual(new[] { "0", "2" }),
            "Manual replacement bypasses natural eligibility and preserves board position.");
        var initializer = new TownEventInitializer(fixture.Content, 13);
        var copies = fixture.Saves.ToDictionary(pair => pair.Key, pair => pair.Value.DeepClone().AsObject());
        copies["persist.quest.json"]["base_root"]!["trinket_retention_ids"]!["items"]!["0"] =
            new JsonObject { ["type"] = "trinket", ["id"] = "old_owned", ["amount"] = 1, ["added_buffs"] = 7 };
        var recovery = initializer.CreateBoardQuest(copies, catalog.Templates.Single(item => item.Quest.Id == "recover_board_plot").Key);
        Assert(recovery["completion_reward"]!["trinket_retention_ids"]!["items"]!.AsObject().Count == 1 &&
            recovery["completion_reward"]!["trinket_retention_ids"]!["items"]!["0"]!["added_buffs"]!.GetValue<int>() == 7 &&
            copies["persist.quest.json"]["base_root"]!["trinket_retention_ids"]!["items"]!.AsObject().Count == 1,
            "Manual recovery bypasses eight-item threshold, copies the one actual loss intact, and does not consume it.");
        await AssertBoardRejectedAsync(() => fixture.Service.CommitAsync(deleted), "Stale board preview must be rejected.");
        var questHash = ProfileFilesHash(profile.ProfileDirectory)["persist.quest.json"];
        fixture.Service.AfterTargetReplace = _ => throw new IOException("Injected replacement failure");
        await AssertBoardRejectedAsync(() => fixture.Service.CommitAsync(replacement), "An interrupted commit must recover.");
        fixture.Service.AfterTargetReplace = null;
        Assert(ProfileFilesHash(profile.ProfileDirectory)["persist.quest.json"] == questHash, "Commit recovery restores the original quest save.");
        File.AppendAllText(Path.Combine(game, "campaign/quest/board.quest.types.json"), "\n");
        await AssertBoardRejectedAsync(() => fixture.Service.CommitAsync(created), "Changed resources invalidate a prepared board.");
        var boardSnapshot = QuestBoardSnapshotReader.Read(fixture.Content, document, original["persist.quest.json"]);
        await RunWpfContractsAsync(() =>
        {
            RunQuestBoardEditUiContracts(root, boardSnapshot, catalog);
            return Task.CompletedTask;
        });
        Console.WriteLine("PASS: Quest board catalog, native-shaped generation, manual eligibility, deletion/replacement, DSON, backup and guarded recovery.");
        Console.WriteLine("Artifacts: " + root);
    }

    private static async Task AssertBoardRejectedAsync(Func<Task> action, string message)
    {
        try { await action(); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("Expected rejection: " + message);
    }

    private static JsonObject BoardTestQuest(string id)
    {
        var result = JsonNode.Parse("""
        {"id":"placeholder","map_name":"","torch_setting":"","raid_rules_override":"","is_plot_quest":false,
         "type":"explore","dungeon":"weald","difficulty":1,"length":1,"counted_in_generation":true,
         "goal_ids":["explore_all_rooms"],"progression_goal_ids":[],"use_default_progression_goals":true,
         "completion_reward":{"resolve_xp":2,"items_definition":{"items":{}}},"threshold_rewards":{},
         "completion_threshold":0,"is_from_town_event":false}
        """)!.AsObject();
        result["id"] = id;
        return result;
    }

    private static Dictionary<string, string> ProfileFilesHash(string directory) => Directory.GetFiles(directory, "persist*.json")
        .ToDictionary(path => Path.GetFileName(path)!, path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))))!;

    private static void RunQuestBoardEditUiContracts(string root, QuestBoardSnapshot snapshot, QuestBoardEditCatalog catalog)
    {
        var window = new MainWindow(Path.Combine(root, "ui-language.json"), gameRunningProbe: () => false)
        { Left = -20000, Top = -20000, ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual };
        try
        {
            window.Show();
            ((TabControl)window.FindName("CatalogTabs")).SelectedIndex = 4;
            var board = (QuestBoardPreview)window.FindName("QuestBoardPanel");
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var editHandler = typeof(MainWindow).GetMethod("QuestBoardPanel_SaveEditRequested", flags)!;
            var browseHandler = typeof(MainWindow).GetMethod("BattleMapPanel_ProfileBrowseChecking", flags)!;
            board.SaveEditRequested -= editHandler.CreateDelegate<Func<QuestBoardEditRequest, Task>>(window);
            board.ProfileBrowseChecking -= browseHandler.CreateDelegate<Func<bool>>(window);
            var allowed = true;
            board.ProfileBrowseChecking += () => allowed;
            var requests = new List<QuestBoardEditRequest>();
            board.SaveEditRequested += request => { requests.Add(request); return Task.CompletedTask; };
            board.IsEnabled = true;
            board.SetProfileLoaded(true);
            board.SetSnapshot(snapshot);
            board.SetEditCatalog(catalog);
            FlushLanguageBindings();
            Assert(((MenuItem)board.CreateRegionMenu().Items[0]).Items.Count ==
                catalog.Regions.Count(region => catalog.Templates.Any(item => item.Quest.Dungeon == region.Id)),
                "Empty-map creation reaches every region with templates, including currently hidden regions.");
            var first = board.Samples[0];
            var remove = (MenuItem)board.CreateTaskMenu(first.RegionId, first.Key).Items[1];
            remove.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert(requests.Single() == new QuestBoardEditRequest(QuestBoardEditKind.Delete, snapshot.SourceSha256, first.Choice.SavedQuest!.SaveKey),
                "Deleting dispatches the exact saved slot and snapshot hash, without a fixture mutation.");
            Assert(board.Samples.Count == snapshot.Quests.Count, "Saved board stays unchanged until synchronization publishes the write result.");
            allowed = false;
            remove.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert(requests.Count == 1, "The shared game/ready guard blocks dispatch from an already-open menu.");
            allowed = true;

            void Choose(Guid? previous, bool changeSnapshot)
            {
                Exception? failure = null;
                window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                {
                    var dialog = Application.Current.Windows.OfType<QuestBoardPreviewDialog>().Single();
                    try
                    {
                        var grid = (DataGrid)dialog.FindName("ChoiceGrid");
                        grid.SelectedIndex = 0;
                        if (changeSnapshot) board.SetSnapshot(snapshot with { SourceSha256 = "changed" });
                        ((Button)dialog.FindName("ConfirmButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    }
                    catch (Exception error) { failure = error; dialog.Close(); }
                }));
                var menu = board.CreateTaskMenu(first.RegionId, previous);
                var group = ((MenuItem)menu.Items[0]).Items.OfType<MenuItem>()
                    .Single(item => item.Tag is QuestBoardCategory.Explore);
                group.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                if (failure is not null) throw failure;
            }
            Choose(null, false);
            Assert(requests.Count == 2 && requests[^1].Kind == QuestBoardEditKind.Create && requests[^1].SaveKey is null &&
                catalog.Templates.Any(item => item.Key == requests[^1].TemplateKey), "The real filtered picker dispatches a catalog template, not a sample ID.");
            Choose(first.Key, false);
            Assert(requests.Count == 3 && requests[^1].Kind == QuestBoardEditKind.Replace && requests[^1].SaveKey == first.Choice.SavedQuest.SaveKey,
                "Replacement retains the selected saved slot.");
            Choose(first.Key, true);
            Assert(requests.Count == 3, "A snapshot change while picking invalidates the selection before preview dispatch.");
            board.SetProfileLoaded(false);
            remove.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert(requests.Count == 3, "Unloading prevents stale command dispatch.");
        }
        finally { window.Close(); }
    }

    private static async Task RunNativeQuestBoardEditsAsync(string root, string originalProfile)
    {
        var originalHashes = ProfileFilesHash(originalProfile);
        var directory = Path.Combine(root, "profile_0");
        Directory.CreateDirectory(directory);
        foreach (var path in Directory.GetFiles(originalProfile, "persist*.json"))
            File.Copy(path, Path.Combine(directory, Path.GetFileName(path)));
        var codec = DsonSaveCodec.CreateNative();
        var profile = SteamDiscovery.OpenProfile(directory);
        var content = await ActiveContentResolver.ResolveAsync(profile, @"E:\Steam\steamapps\common\DarkestDungeon",
            @"E:\Steam\steamapps\workshop\content\262060", codec, Path.Combine(root, "resolve"));
        var saves = new Dictionary<string, JsonObject>();
        foreach (var path in Directory.GetFiles(directory, "persist*.json"))
        {
            var decoded = Path.Combine(root, "decoded", Path.GetFileName(path));
            Directory.CreateDirectory(Path.GetDirectoryName(decoded)!);
            await codec.DecodeAsync(path, decoded);
            saves[Path.GetFileName(path)] = JsonSupport.ReadObject(decoded);
        }
        var catalog = QuestBoardEditCatalog.Load(content);
        var snapshot = QuestBoardSnapshotReader.Read(content, saves["persist.quest.json"], originalHashes["persist.quest.json"],
            saves.GetValueOrDefault("persist.progression.json"));
        File.WriteAllText(Path.Combine(root, "snapshot.json"), JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(root, "catalog.json"), JsonSerializer.Serialize(catalog, new JsonSerializerOptions { WriteIndented = true }));
        Assert(catalog.Templates.Count > 0, "Actual active content supplies task templates: " + string.Join(";", catalog.Issues));
        var initializer = new TownEventInitializer(content, 23);
        var results = new List<object>();
        var failed = 0;
        var unavailable = 0;
        foreach (var template in catalog.Templates)
        {
            if (template.UnavailableReason.Length > 0)
            {
                unavailable++;
                results.Add(new { template.Key, template.Quest.Id, Result = "unavailable", Reason = template.UnavailableReason });
                continue;
            }
            var copy = saves.ToDictionary(pair => pair.Key, pair => pair.Value.DeepClone().AsObject());
            copy["persist.quest.json"]["base_root"]!["quests"] = new JsonObject();
            try
            {
                var quest = initializer.CreateBoardQuest(copy, template.Key);
                var questDocument = copy["persist.quest.json"];
                var source = Path.Combine(root, "native-proposed.json");
                JsonSupport.WriteObject(source, questDocument);
                var encoded = Path.Combine(root, "native-encoded.json");
                await codec.EncodeAsync(source, encoded, Path.Combine(directory, "persist.quest.json"));
                var decoded = Path.Combine(root, "native-roundtrip.json");
                await codec.DecodeAsync(encoded, decoded);
                Assert(JsonNode.DeepEquals(questDocument, JsonSupport.ReadObject(decoded)), "Actual quest roundtrip: " + template.Key);
                results.Add(new { template.Key, Id = quest["id"]!.GetValue<string>(), Result = "passed" });
            }
            catch (Exception error) { failed++; results.Add(new { template.Key, Error = error.ToString() }); }
        }
        File.WriteAllText(Path.Combine(root, "initialization-results.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        var service = new SaveEditService(codec, new SaveEditorLocations(root, Path.Combine(root, "work"), Path.Combine(root, "backups")));
        var hash = ProfileFilesHash(directory)["persist.quest.json"];
        var slot = saves["persist.quest.json"]["base_root"]!["quests"]!.AsObject().First().Key;
        var prepared = await service.PrepareQuestBoardEditAsync(profile, new(QuestBoardEditKind.Delete, hash, slot), content);
        await service.CommitAsync(prepared);
        var generated = catalog.Templates.First(item => !item.Quest.IsPlotQuest && item.UnavailableReason.Length == 0);
        var create = await service.PrepareQuestBoardEditAsync(profile,
            new(QuestBoardEditKind.Create, ProfileFilesHash(directory)["persist.quest.json"], TemplateKey: generated.Key), content);
        await service.CommitAsync(create);
        var existingIds = saves["persist.quest.json"]["base_root"]!["quests"]!.AsObject()
            .Select(pair => pair.Value!["id"]!.GetValue<string>()).ToHashSet();
        var lockedPlot = catalog.Templates.First(item => item.Quest.Id.StartsWith("plot_darkest_dungeon_", StringComparison.Ordinal) &&
            !existingIds.Contains(item.Quest.Id));
        var replace = await service.PrepareQuestBoardEditAsync(profile,
            new(QuestBoardEditKind.Replace, ProfileFilesHash(directory)["persist.quest.json"], create.Preview.Result!.SaveKey, lockedPlot.Key), content);
        await service.CommitAsync(replace);
        foreach (var pair in originalHashes.Where(pair => pair.Key != "persist.quest.json"))
            Assert(ProfileFilesHash(directory)[pair.Key] == pair.Value, "Live-shaped board edits preserve every other save: " + pair.Key);
        Assert(ProfileFilesHash(originalProfile).OrderBy(pair => pair.Key).SequenceEqual(originalHashes.OrderBy(pair => pair.Key)), "Native source profile must remain unchanged.");
        Console.WriteLine($"Native quest templates: {catalog.Templates.Count}; explicitly unavailable: {unavailable}; unexpected initialization failures: {failed}.");
        Console.WriteLine("Artifacts: " + root);
        Assert(failed == 0, "Review actual initialization failures in initialization-results.json.");
    }
}
