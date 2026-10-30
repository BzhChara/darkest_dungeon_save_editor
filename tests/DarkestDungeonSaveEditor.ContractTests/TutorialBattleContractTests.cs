using System.Collections;
using System.Reflection;
using System.Windows.Controls;
using DarkestDungeonSaveEditor.App;

internal static partial class ContractSuite
{
    public static async Task RunTutorialBattleOnlyAsync(string repositoryRoot)
    {
        var root = Path.Combine(repositoryRoot, "artifacts", "tutorial-battle-contracts", Guid.NewGuid().ToString("N"));
        var profileRoot = Path.Combine(root, "profile_0");
        Directory.CreateDirectory(profileRoot);
        var profile = new SaveProfile("profile_0", profileRoot, Path.Combine(profileRoot, "persist.estate.json"), "fixture", DateTime.UtcNow);
        var codec = DsonSaveCodec.CreateNative();
        var map = CompletionTestMap();
        var raid = CompletionTestRaid(["tutorial_final_room"]);
        var instance = raid["base_root"]!["raid_instance"]!.AsObject();
        // Identifiers from scripts/starting_save/persist.{game,raid}.json.
        // Reduced topology tests the rejection policy, not tutorial gameplay.
        instance["id"] = "tutorial";
        instance["type"] = "explore";
        var game = JsonNode.Parse("""{"base_root":{"inraid":true,"raiddungeon":"tutorial","raid_save":""}}""")!.AsObject();
        var gamePath = Path.Combine(profileRoot, "persist.game.json");
        File.WriteAllText(profile.EstateSavePath, "{}");
        File.WriteAllText(profile.MapSavePath, map.ToJsonString());
        void Save() { File.WriteAllText(profile.RaidSavePath, raid.ToJsonString()); File.WriteAllText(gamePath, game.ToJsonString()); }
        Save();
        var reader = new BattleMapSnapshotReader(codec);
        var tutorial = await reader.LoadAsync(profileRoot);
        Assert(tutorial.IsTutorialRaid && !tutorial.IsWaveBattle && tutorial.DungeonId == "weald",
            "The Old Road is identified by its exact quest ID, not its Weald region.");
        var beforeMap = map.ToJsonString();
        var beforeRaid = raid.ToJsonString();
        void Reject(Action action)
        {
            var rejected = false;
            try { action(); }
            catch (InvalidOperationException error) when (error.Message == EditorText.Get("BattleMapView_Tutorial_Message")) { rejected = true; }
            Assert(rejected && map.ToJsonString() == beforeMap && raid.ToJsonString() == beforeRaid,
                "Tutorial operations must fail with the tutorial notice and leave inputs untouched.");
        }
        Reject(() => BattleMapSaveEditor.DeleteContent(map, raid, tutorial, "rooB", "tile0"));
        Reject(() => BattleMapSaveEditor.MoveParty(map, raid, tutorial, "rooB", "tile0"));
        Reject(() => QuestCompletionSaveEditor.Complete(map, raid, tutorial, new Dictionary<uint, System.Text.Json.JsonElement>(),
            _ => throw new Exception("Tutorial completion must not initialize inventory.")));

        var locations = new SaveEditorLocations(root, Path.Combine(root, "work"), Path.Combine(root, "backups"));
        var forceTown = new ForceTownSaveService(codec, locations, () => false);
        var originalHashes = ProfileFilesHash(profileRoot);
        var rejectedTown = false;
        // The saved game route also rejects a stale/caller-supplied classification.
        try { await forceTown.PrepareAsync(profile, tutorial with { IsTutorialRaid = false }); }
        catch (InvalidOperationException error) when (error.Message == EditorText.Get("BattleMapView_Tutorial_Message")) { rejectedTown = true; }
        Assert(rejectedTown && ProfileFilesHash(profileRoot).OrderBy(p => p.Key).SequenceEqual(originalHashes.OrderBy(p => p.Key)),
            "Force-town must reject the native tutorial game route without changing the save.");

        await RunWpfContractsAsync(async () =>
        {
            var view = new BattleMapView { UsesSharedProfileMonitor = true };
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            T Named<T>(string name) => (T)view.FindName(name);
            object? Field(string name) => typeof(BattleMapView).GetField(name, flags)!.GetValue(view);
            void AssertTutorialUi()
            {
                FlushLanguageBindings();
                Assert(view.CurrentSnapshot is { IsTutorialRaid: true } &&
                       Named<Canvas>("MapCanvas").Visibility == Visibility.Collapsed &&
                       Named<FrameworkElement>("EmptyStatePanel").Visibility == Visibility.Visible &&
                       Named<TextBlock>("EmptyStateTitleTextBlock").Text == EditorText.Get("BattleMapView_Tutorial_Title") &&
                       Named<TextBlock>("EmptyStateTextBlock").Text == EditorText.Get("BattleMapView_Tutorial_Message") &&
                       !Named<Button>("ForceTownButton").IsEnabled && !Named<Button>("ForceCompleteQuestButton").IsEnabled &&
                       !Named<Button>("FitMapButton").IsEnabled && !Named<Button>("ZoomInButton").IsEnabled &&
                       ((IList)Field("_cells")!).Count == 0 && Field("_selectedCell") is null &&
                       Field("_encounterCatalog") is null && Field("_roomAttachmentCatalog") is null,
                    "Tutorial UI must display the notice, detach map targets and disable editing/recovery controls.");
            }
            try
            {
                await view.LoadProfileAsync(profile, codec, gameDirectory: null);
                AssertTutorialUi();
                foreach (var language in new[] { "en", "zh-CN" })
                {
                    EditorText.Initialize(language, EditorText.SystemCulture);
                    view.RefreshLanguage();
                    AssertTutorialUi();
                }
                // The first Ruins quest contains 'tutorial' in its ID, but is not Old Road.
                instance["id"] = "plot_tutorial_crypts";
                instance["dungeon"] = "crypts";
                instance["goal_ids"] = new JsonArray("explore_all_rooms");
                game["base_root"]!["raiddungeon"] = "crypts";
                Save();
                await view.LoadProfileAsync(profile, codec, gameDirectory: null);
                Assert(view.CurrentSnapshot is { IsTutorialRaid: false } && Named<Canvas>("MapCanvas").Visibility == Visibility.Visible &&
                       Named<Button>("ForceTownButton").IsEnabled && Named<Button>("FitMapButton").IsEnabled,
                    "The first Ruins mission must retain the ordinary map and recovery action.");
                typeof(BattleMapView).GetField("_selectedCell", flags)!.SetValue(view, ((IList)Field("_cells")!)[0]);
                instance["id"] = "tutorial";
                instance["dungeon"] = "weald";
                instance["goal_ids"] = new JsonArray("tutorial_final_room");
                game["base_root"]!["raiddungeon"] = "tutorial";
                Save();
                var content = new ActiveContentSnapshot(profile, "base", [], [], root, gamePath, 0, ComputeSha256(gamePath));
                var sync = new ProfileCatalogSnapshot(content, "fixture", QuantityItemCatalog.LoadRaid(content, raid),
                    new Dictionary<string, string?>
                    {
                        ["persist.map.json"] = ComputeSha256(profile.MapSavePath),
                        ["persist.raid.json"] = ComputeSha256(profile.RaidSavePath)
                    }, DateTime.UtcNow);
                await view.SynchronizeProfileAsync(sync, contentChanged: true, CancellationToken.None);
                AssertTutorialUi();
                await view.SynchronizeProfileAsync(sync, contentChanged: false, CancellationToken.None);
                AssertTutorialUi();
                await (Task)typeof(BattleMapView).GetMethod("NotifySaveEditBusyAsync", flags)!.Invoke(view, [false])!;
                AssertTutorialUi();
                view.ClearProfile();
                FlushLanguageBindings();
                Assert(view.CurrentSnapshot is null && !Named<Button>("ForceTownButton").IsEnabled &&
                       Named<TextBlock>("EmptyStateTitleTextBlock").Text == EditorText.Get("BattleMapView_008"),
                    "Clearing the profile must reset the tutorial notice.");
            }
            finally { view.ClearProfile(); }
        });
        Console.WriteLine("PASS: Old Road detection, map/completion/force-town rejection, localized WPF notice, synchronization, clearing, and ordinary Ruins availability.");
        Console.WriteLine("Artifacts: " + root);
    }
}
