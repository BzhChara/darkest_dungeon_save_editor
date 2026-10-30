using System.Collections;
using System.Reflection;
using System.Windows.Controls;
using DarkestDungeonSaveEditor.App;

internal static partial class ContractSuite
{
    public static async Task RunWaveBattleOnlyAsync(string repositoryRoot)
    {
        var root = Path.Combine(repositoryRoot, "artifacts", "wave-battle-contracts", Guid.NewGuid().ToString("N"));
        var profileRoot = Path.Combine(root, "profile_0");
        Directory.CreateDirectory(profileRoot);
        var profile = new SaveProfile("profile_0", profileRoot, Path.Combine(profileRoot, "persist.estate.json"), "fixture", DateTime.UtcNow);
        var codec = DsonSaveCodec.CreateNative();
        // Reduced native one-room map and raid shapes; no fixture is installed into the game.
        const string mapJson = """
        {"base_root":{"map":{"bounds":[0,0,0,0],"entrance_id":100,"final_room_id":100,"static_dynamic":{
          "static_save":{"base_root":{"areas":{"rooA":{"id":100,"kind":0,"tiles":{"tile0":{"type":3,"mappos":[0,0]}}}}}},
          "areas":{"rooA":{"knowledge":3,"reversed":false,"tiles":{"tile0":{"content":0,"knowledge":3,"mash_index":-1,"mash_type":7}}}}
        }}}}
        """;
        var raid = JsonNode.Parse("""
        {"base_root":{"raid_instance":{"id":"plot_color_of_madness_2","type":"wave","dungeon":"farm","difficulty":3,"length":2},
          "in_area":100,"areatile":1,"last_room_id":100,"inbattle":false,"wave_logic":{"currWaveDungeon":0},
          "party":{"inventory":{"items":{}}}}}
        """)!.AsObject();
        File.WriteAllText(profile.MapSavePath, mapJson);
        File.WriteAllText(profile.EstateSavePath, "{}");
        var gamePath = Path.Combine(profileRoot, "persist.game.json");
        const string gameJson = """{"base_root":{"inraid":true,"raiddungeon":"farm","raid_save":"","date":{"week":12}}}""";
        File.WriteAllText(gamePath, gameJson);
        void SaveRaid() => File.WriteAllText(profile.RaidSavePath, raid.ToJsonString());
        SaveRaid();
        var reader = new BattleMapSnapshotReader(codec);
        var wave = await reader.LoadAsync(profileRoot);
        Assert(wave.IsWaveBattle, "A Farmstead wave raid must be recognized even between battles.");

        var raidRoot = raid["base_root"]!.AsObject();
        raidRoot["wave_logic"] = new JsonObject();
        SaveRaid();
        Assert((await reader.LoadAsync(profileRoot)).IsWaveBattle,
            "The quest type must identify wave expeditions independently of the current wave payload.");
        var rejected = false;
        try { BattleMapSaveEditor.ValidateStationaryRaidState(raid); }
        catch (InvalidOperationException ex) when (ex.Message == EditorText.Get("BattleMapSaveEditor_038")) { rejected = true; }
        Assert(rejected, "The write guard must also reject a wave quest without an active battle payload.");

        raidRoot["raid_instance"]!["type"] = "kill_boss";
        SaveRaid();
        Assert(!(await reader.LoadAsync(profileRoot)).IsWaveBattle,
            "A region name alone must not classify an ordinary quest as wave combat.");
        raidRoot["wave_logic"] = new JsonObject { ["currWaveDungeon"] = 0 };
        SaveRaid();
        Assert((await reader.LoadAsync(profileRoot)).IsWaveBattle,
            "Retained wave state must block map editing even if the quest type does not identify it.");
        raidRoot["raid_instance"]!["type"] = "wave";
        raidRoot["raid_instance"]!["dungeon"] = "ship_wave";
        raidRoot["inbattle"] = true;
        SaveRaid();
        wave = await reader.LoadAsync(profileRoot);
        Assert(wave.IsWaveBattle && wave.InBattle, "Mod wave expeditions must use the same state as the native Farmstead.");

        await RunWpfContractsAsync(async () =>
        {
            var view = new BattleMapView { UsesSharedProfileMonitor = true };
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            T Named<T>(string name) => (T)view.FindName(name);
            object? Field(string name) => typeof(BattleMapView).GetField(name, flags)!.GetValue(view);
            void AssertWaveUi()
            {
                FlushLanguageBindings();
                Assert(view.CurrentSnapshot is { IsWaveBattle: true } && Named<Canvas>("MapCanvas").Visibility == Visibility.Collapsed &&
                       Named<FrameworkElement>("EmptyStatePanel").Visibility == Visibility.Visible &&
                       Named<TextBlock>("EmptyStateTitleTextBlock").Text == EditorText.Get("BattleMapView_WaveBattle_Title") &&
                       Named<TextBlock>("EmptyStateTextBlock").Text == EditorText.Get("BattleMapView_WaveBattle_Message") &&
                       Named<Button>("ForceTownButton").IsEnabled && !Named<Button>("ForceCompleteQuestButton").IsEnabled &&
                       !Named<Button>("FitMapButton").IsEnabled &&
                       !Named<Button>("ZoomInButton").IsEnabled && ((IList)Field("_cells")!).Count == 0 &&
                       Field("_selectedCell") is null && Field("_encounterCatalog") is null && Field("_roomAttachmentCatalog") is null,
                    "Wave UI must show the localized notice, detach edit targets/catalogs, disable navigation, and retain force-town.");
            }

            try
            {
                await view.LoadProfileAsync(profile, codec, gameDirectory: null);
                AssertWaveUi();
                foreach (var language in new[] { "en", "zh-CN" })
                {
                    EditorText.Initialize(language, EditorText.SystemCulture);
                    view.RefreshLanguage();
                    AssertWaveUi();
                }

                // A normal map followed by shared synchronization must detach its old visuals.
                raidRoot["raid_instance"]!["type"] = "kill_boss";
                raidRoot.Remove("wave_logic");
                raidRoot["inbattle"] = false;
                SaveRaid();
                await view.LoadProfileAsync(profile, codec, gameDirectory: null);
                Assert(Named<Canvas>("MapCanvas").Visibility == Visibility.Visible &&
                       Named<Button>("FitMapButton").IsEnabled && Named<Button>("ForceTownButton").IsEnabled,
                    "Switching to an ordinary raid must restore its map and controls.");
                typeof(BattleMapView).GetField("_selectedCell", flags)!.SetValue(view, ((IList)Field("_cells")!)[0]);
                raidRoot["raid_instance"]!["type"] = "wave";
                raidRoot["wave_logic"] = new JsonObject { ["currWaveDungeon"] = 0 };
                SaveRaid();
                var content = new ActiveContentSnapshot(profile, "base", [], [], root, gamePath, 0, ComputeSha256(gamePath));
                var quantities = QuantityItemCatalog.LoadRaid(content, raid);
                var sync = new ProfileCatalogSnapshot(content, "fixture", quantities, new Dictionary<string, string?>
                {
                    ["persist.map.json"] = ComputeSha256(profile.MapSavePath),
                    ["persist.raid.json"] = ComputeSha256(profile.RaidSavePath)
                }, DateTime.UtcNow);
                await view.SynchronizeProfileAsync(sync, contentChanged: true, CancellationToken.None);
                AssertWaveUi();
                await view.SynchronizeProfileAsync(sync, contentChanged: false, CancellationToken.None);
                AssertWaveUi();

                await view.SynchronizeProfileAsync(sync with
                {
                    QuantityItems = quantities with { SaveContext = QuantityItemSaveContext.Town }
                }, contentChanged: false, CancellationToken.None);
                Assert(view.CurrentSnapshot is null && !Named<Button>("ForceTownButton").IsEnabled,
                    "Returning to town through shared synchronization must clear the wave recovery action.");
                await view.LoadProfileAsync(profile, codec, gameDirectory: null);
                AssertWaveUi();

                view.ClearProfile();
                FlushLanguageBindings();
                Assert(view.CurrentSnapshot is null && !Named<Button>("ForceTownButton").IsEnabled &&
                       Named<TextBlock>("EmptyStateTitleTextBlock").Text == EditorText.Get("BattleMapView_008"),
                    "Clearing the profile must reset the wave notice and remove the recovery action.");
            }
            finally { view.ClearProfile(); }
        });

        // Exercise the actual guarded force-town service on this isolated wave fixture.
        raidRoot["inbattle"] = true;
        SaveRaid();
        wave = await reader.LoadAsync(profileRoot);
        var service = new ForceTownSaveService(codec,
            new SaveEditorLocations(root, Path.Combine(root, "work"), Path.Combine(root, "backups")), () => false);
        var beforeMap = ComputeSha256(profile.MapSavePath);
        var beforeRaid = ComputeSha256(profile.RaidSavePath);
        var prepared = await service.PrepareAsync(profile, wave);
        var result = await service.CommitAsync(prepared);
        var expected = JsonNode.Parse(gameJson)!;
        expected["base_root"]!["inraid"] = false;
        expected["base_root"]!["raiddungeon"] = "none";
        Assert(JsonNode.DeepEquals(expected, JsonNode.Parse(File.ReadAllText(gamePath))) &&
               beforeMap == ComputeSha256(profile.MapSavePath) && beforeRaid == ComputeSha256(profile.RaidSavePath) &&
               File.Exists(Path.Combine(result.BackupDirectory, "persist.game.json")),
            "Force-town must remain usable for wave raids, preserve week/map/raid state, and create its normal backup.");
        UseNativeCodec = true;
        try { await RunBattleMapContractsAsync(root, codec); }
        finally { UseNativeCodec = false; }
        Console.WriteLine("PASS: Wave detection, write rejection, localized WPF notice, normal/wave transitions, shared synchronization, clearing, and force-town recovery.");
        Console.WriteLine("Artifacts: " + root);
    }
}
