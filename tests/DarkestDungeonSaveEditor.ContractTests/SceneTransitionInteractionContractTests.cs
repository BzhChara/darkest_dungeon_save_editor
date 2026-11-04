using System.Reflection;
using System.Windows.Controls;
using System.Windows.Threading;
using DarkestDungeonSaveEditor.App;

internal static partial class ContractSuite
{
    private static async Task VerifySceneTransitionsAsync(ContractFixture f)
    {
        var root = Path.Combine(f.RunRoot, "scene-transition-ui");
        var profile = WriteBattleSafetyProfile(Path.Combine(root, "profile_0"));
        var codec = f.Codec;
        CreateBattleMonsterDefinitions(f.GameRoot, ["native"]);
        WriteMultiMash(f.GameRoot, "dungeons/cove/cove.2.mash.darkest", "room: .chance 1 .types native\n");
        foreach (var (seed, name) in new[] { (f.DecodedSeedPath, "persist.estate.json"),
            (f.DecodedTownSeedPath, "persist.town.json"), (f.DecodedRosterSeedPath, "persist.roster.json"),
            (f.DecodedUpgradesSeedPath, "persist.upgrades.json") })
            File.Copy(seed, Path.Combine(profile.ProfileDirectory, name), overwrite: true);
        var gamePath = Path.Combine(profile.ProfileDirectory, "persist.game.json");
        var game = JsonNode.Parse(File.ReadAllText(gamePath))!;
        var mapText = File.ReadAllText(profile.MapSavePath);
        var raid = JsonNode.Parse(File.ReadAllText(profile.RaidSavePath))!;
        raid["base_root"]!["start_elapsed_time"] = 123;
        raid["base_root"]!["party"]!["inventory"] = JsonNode.Parse(
            """{"items":{"0":{"id":"","type":"gold","amount":25}}}""");
        async Task WriteScene(bool inRaid)
        {
            game["base_root"]!["inraid"] = inRaid;
            game["base_root"]!["raiddungeon"] = inRaid ? "cove" : "none";
            var proposed = Path.Combine(root, "scene-game.json");
            File.WriteAllText(proposed, game.ToJsonString());
            await codec.EncodeAsync(proposed, gamePath, null);
            if (inRaid)
            {
                var mapSource = Path.Combine(root, "scene-map.json");
                var raidSource = Path.Combine(root, "scene-raid.json");
                File.WriteAllText(mapSource, mapText);
                File.WriteAllText(raidSource, raid.ToJsonString());
                await codec.EncodeAsync(mapSource, profile.MapSavePath, null);
                await codec.EncodeAsync(raidSource, profile.RaidSavePath, null);
            }
        }
        // The initial town has no expedition files. Later returns deliberately
        // leave them behind, as native saves and Force town can both do.
        File.Delete(profile.MapSavePath);
        File.Delete(profile.RaidSavePath);
        await WriteScene(false);
        var locations = new SaveEditorLocations(root, Path.Combine(root, "workspaces"), Path.Combine(root, "backups"));
        var running = false;
        var languagePath = Path.Combine(root, "language.json");
        EditorLanguageSettings.Save(languagePath, EditorText.Culture.Name);
        var window = new MainWindow(languagePath, saveLocations: locations, gameRunningProbe: () => running)
        {
            Left = -20000, Top = -20000, ShowActivated = false, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual
        };
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Get(string name) => typeof(MainWindow).GetField(name, flags)!.GetValue(window);
        void Set(string name, object? value) => typeof(MainWindow).GetField(name, flags)!.SetValue(window, value);
        object? Call(string name, params object?[] args)
        {
            var method = typeof(MainWindow).GetMethod(name, flags)!;
            return method.Invoke(window, method.GetParameters().Select((parameter, index) =>
                index < args.Length ? args[index] : parameter.DefaultValue).ToArray());
        }
        bool Flag(string name) => (bool)Get(name)!;
        bool Enabled(string name) => ((FrameworkElement)Get(name)!).IsEnabled;
        SemaphoreSlim ReaderGate() => (SemaphoreSlim)typeof(ProfileCatalogSnapshotReader).GetField("_gate", flags)!
            .GetValue(Get("_catalogSnapshotReader"))!;
        async Task Until(Func<bool> ready)
        {
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (!ready())
            {
                if (DateTime.UtcNow > deadline) throw new TimeoutException("Scene transition did not settle: " + Get("_lastSyncError") +
                    "\n" + ((TextBox)Get("StatusTextBox")!).Text);
                await Task.Delay(15);
            }
        }
        Task Settled() => Until(() => !Flag("_syncInProgress") && !Flag("_syncRequested") &&
            !Flag("_checkingProfileOperation") && !Flag("_completingProfileEdit") && (int)Get("_busyDepth")! == 0);

        async Task CheckPickerInvalidation<T>(Func<Task> open, FrameworkElement? disableTemporarily = null) where T : Window
        {
            var dismissed = false;
            var before = ProfileFilesHash(profile.ProfileDirectory);
            var observe = window.Dispatcher.InvokeAsync(() =>
            {
                if (window.OwnedWindows.OfType<T>().SingleOrDefault() is not { } picker) return;
                try
                {
                    if (disableTemporarily is not null)
                    {
                        disableTemporarily.IsEnabled = false;
                        disableTemporarily.IsEnabled = true;
                    }
                    else
                    {
                        running = true;
                        Call("CheckGameProcess");
                    }
                    dismissed = !picker.IsVisible;
                }
                finally { if (picker.IsVisible) picker.Close(); }
            }, DispatcherPriority.ContextIdle);
            await open();
            await observe;
            Assert(dismissed && (disableTemporarily is not null || !Enabled("BattleMapPanel")) &&
                before.OrderBy(p => p.Key).SequenceEqual(ProfileFilesHash(profile.ProfileDirectory).OrderBy(p => p.Key)),
                typeof(T).Name + " must close on invalidation without accepting a selection or writing the profile.");
            if (running) { running = false; Call("CheckGameProcess"); await Settled(); }
        }
        var grid = (DataGrid)Get("ItemGrid")!;
        var panel = (BattleMapView)Get("BattleMapPanel")!;
        var trackTownTransition = false;
        var showedIntermediateEmptyState = false;
        ((FrameworkElement)panel.FindName("EmptyStatePanel")).IsVisibleChanged += (_, args) =>
        {
            if (trackTownTransition && args.NewValue is true) showedIntermediateEmptyState = true;
        };
        var tabs = (TabControl)Get("CatalogTabs")!;
        var amount = (TextBox)Get("CopiesTextBox")!;
        QuantityItemDefinition Gold() => ((IReadOnlyList<QuantityItemDefinition>)Get("_allItems")!).Single(item => item.DisplayId == "gold");
        async Task PrimePreview()
        {
            tabs.SelectedIndex = 0;
            grid.SelectedItem = grid.Items.Cast<object>().Single(row =>
                ((QuantityItemDefinition)row.GetType().GetProperty("Definition")!.GetValue(row)!).DisplayId == "gold");
            amount.Text = (Gold().CurrentAmount + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            Call("Preview_Click", window, new RoutedEventArgs());
            // The async click resumes after preflight briefly clears the busy flags.
            await Until(() => Get("_preparedQuantityItemEdit") is not null && Enabled("ApplyButton"));
            await Settled();
        }
        var status = (TextBox)Get("StatusTextBox")!;
        var checkedLogLength = 0;
        void CheckScene(QuantityItemSaveContext expected, int gold, bool expectItemPrompt)
        {
            var sceneMessage = EditorText.Get(expected == QuantityItemSaveContext.Raid
                ? "MainWindow_ProfileSync_SwitchedToRaid" : "MainWindow_ProfileSync_SwitchedToTown");
            var expectedMessage = expectItemPrompt ? EditorText.Format("MainWindow_ProfileSync_ReselectItem", sceneMessage) : sceneMessage;
            var messages = status.Text[checkedLogLength..].Split(Environment.NewLine)
                .Where(line => line.Contains("] " + sceneMessage, StringComparison.Ordinal)).ToArray();
            checkedLogLength = status.Text.Length;
            Assert(messages.Length == 1 && messages[0].EndsWith("] " + expectedMessage, StringComparison.Ordinal),
                $"Expected one scene notification '{expectedMessage}', got: {string.Join(" | ", messages)}");
            Assert(grid.SelectedItem is null && grid.SelectedItems.Count == 0,
                "Scene changes must clear the item selection even while another tab is active.");
            tabs.SelectedIndex = 0;
            Assert((QuantityItemSaveContext)Get("_quantitySaveContext")! == expected && Gold().CurrentAmount == gold &&
                   Gold().StorageKind == (expected == QuantityItemSaveContext.Raid ? QuantityItemStorageKind.RaidInventory : QuantityItemStorageKind.Wallet) &&
                   grid.SelectedItem is null && grid.SelectedItems.Count == 0 && amount.Text == "0" &&
                   Get("_preparedQuantityItemEdit") is null && !Enabled("ApplyButton") && Flag("_syncReady") &&
                   Equals(((TabItem)Get("ItemTab")!).Header, EditorText.Get(expected == QuantityItemSaveContext.Raid
                       ? "MainWindow_CatalogLoading_011" : "MainWindow_CatalogLoading_012")) &&
                   (panel.CurrentSnapshot is not null) == (expected == QuantityItemSaveContext.Raid) &&
                   ((Button)panel.FindName("ForceTownButton")).IsEnabled == (expected == QuantityItemSaveContext.Raid),
                "A scene change must clear the previous item/target/preview, use the correct quantity source, update the tab and show only the current expedition.");
        }
        SemaphoreSlim? heldGate = null;
        DispatcherTimer? dialogs = null;
        var dialogMessages = new List<string>();
        var confirmForceTown = false;
        try
        {
            window.Show();
            dialogs = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            dialogs.Tick += (_, _) =>
            {
                foreach (var dialog in window.OwnedWindows.OfType<ThemedDialog>().ToArray())
                {
                    dialogMessages.Add(((TextBlock)dialog.FindName("MessageTextBlock")).Text);
                    dialog.DialogResult = confirmForceTown && dialogMessages.Count == 1;
                }
            };
            dialogs.Start();
            ((DispatcherTimer)Get("_gameProcessPoll")!).Stop();
            ((TextBox)Get("ProfileDirectoryTextBox")!).Text = profile.ProfileDirectory;
            ((TextBox)Get("GameDirectoryTextBox")!).Text = f.GameRoot;
            ((TextBox)Get("LocalModDirectoryTextBox")!).Text = f.LocalModDirectory;
            var content = await ActiveContentResolver.ResolveAsync(profile, f.GameRoot, null, f.LocalModDirectory, codec, locations.WorkspaceDirectory);
            var items = await QuantityItemCatalog.LoadAsync(content, codec);
            Set("_activeContentSnapshot", content);
            Set("_allItems", items.Items);
            Set("_quantitySaveContext", items.SaveContext);
            await panel.LoadProfileAsync(profile, codec, f.GameRoot, content, localModDirectory: f.LocalModDirectory);
            await (Task)Call("StartProfileSyncAsync", content, items, codec, f.GameRoot, null, f.LocalModDirectory,
                ProfileCatalogContentFingerprint.Capture(content.Sources), locations)!;
            var heroes = HeroClassCatalog.Load(content);
            Set("_heroCatalog", heroes); Set("_allHeroes", heroes.HeroClasses);
            Call("PopulateHeroLevels", heroes); Call("ApplyFilter");
            tabs.SelectedIndex = 3;
            ((DataGrid)Get("HeroGrid")!).SelectedIndex = 0;
            var quirkButton = (Button)Get("InitialQuirksButton")!;
            Assert(quirkButton.IsEnabled, "Open the quirk picker from its enabled town button.");
            await CheckPickerInvalidation<InitialQuirkSelectionDialog>(() =>
            {
                quirkButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                return Task.CompletedTask;
            });
            Assert(((IReadOnlyList<string>)Get("_selectedInitialQuirkIds")!).Count == 0,
                "Dismissing the hero picker must leave the original selection intact.");
            var townGold = Gold();
            await PrimePreview();

            async Task ExitGameInto(bool inRaid)
            {
                running = true; Call("CheckGameProcess");
                Assert(Get("_preparedQuantityItemEdit") is null && !Enabled("ApplyButton") && !Enabled("BattleMapPanel"),
                    "Game launch must consume the old preview before any scene transition.");
                await WriteScene(inRaid);
                heldGate = ReaderGate(); await heldGate.WaitAsync();
                running = false; Call("CheckGameProcess");
                Assert(Flag("_syncInProgress") && !Enabled("ApplyButton") && !Enabled("PreviewButton") && !Enabled("BattleMapPanel"),
                    "Game exit must keep writes blocked until the new scene is fully synchronized.");
                heldGate.Release(); heldGate = null;
                await Settled();
            }
            await ExitGameInto(true);
            CheckScene(QuantityItemSaveContext.Raid, 25, expectItemPrompt: true);
            tabs.SelectedIndex = 4;
            var encounters = (BattleEncounterCatalogResult)typeof(BattleMapView).GetField("_encounterCatalog", flags)!.GetValue(panel)!;
            var pickerTarget = ((System.Collections.IEnumerable)typeof(BattleMapView).GetField("_cells", flags)!.GetValue(panel)!).Cast<object>().First();
            Assert(encounters.Encounters.Count > 0, "The encounter selection must have an available candidate.");
            Task OpenEncounter() => (Task)typeof(BattleMapView).GetMethod("SelectAndPlaceEncounterAsync", flags)!.Invoke(panel,
                [pickerTarget, encounters.Encounters.ToArray(), "Encounter"])!;
            await CheckPickerInvalidation<BattleEncounterSelectionDialog>(OpenEncounter, panel);
            await CheckPickerInvalidation<BattleEncounterSelectionDialog>(OpenEncounter);
            Console.WriteLine("PASS: game launch dismisses live hero and encounter pickers without applying their choices.");
            var forceTownAction = (Button)panel.FindName("ForceTownButton");
            var forceCompleteAction = (Button)panel.FindName("ForceCompleteQuestButton");
            Assert(forceTownAction.IsEnabled && forceCompleteAction.IsEnabled,
                "Both force actions must be available before an ordinary map edit.");
            var beforeMapEdit = ComputeSha256(profile.MapSavePath);
            var mapService = new BattleMapEditService(codec, locations)
            {
                BeforeTargetReplace = _ =>
                {
                    heldGate = ReaderGate();
                    Assert(heldGate.Wait(0), "The map edit must finish its pre-write check before commit.");
                }
            };
            var cells = (System.Collections.IEnumerable)typeof(BattleMapView).GetField("_cells", flags)!.GetValue(panel)!;
            var target = cells.Cast<object>().Single(cell =>
                (string?)cell.GetType().GetProperty("SourceAreaId")!.GetValue(cell) == "coAB" &&
                (string?)cell.GetType().GetProperty("SourceTileId")!.GetValue(cell) == "tile1");
            var edit = (Task)typeof(BattleMapView).GetMethod("ApplySaveEditAsync", flags)!.Invoke(panel,
                [target, profile, panel.CurrentSnapshot, mapService, BattleMapEditKind.DeleteContent, null, null, null])!;
            await Until(() => Flag("_completingProfileEdit") || edit.IsCompleted);
            Assert(heldGate is not null && !edit.IsCompleted && !forceTownAction.IsEnabled && !forceCompleteAction.IsEnabled,
                "Both force actions must remain disabled while a committed map edit waits for shared synchronization: " + string.Join(" | ", dialogMessages));
            heldGate!.Release(); heldGate = null;
            await edit;
            await Settled();
            Assert(ComputeSha256(profile.MapSavePath) != beforeMapEdit && forceTownAction.IsEnabled && forceCompleteAction.IsEnabled,
                "An actual map deletion must restore both force actions after its snapshot and shared synchronization finish.");
            await PrimePreview();
            tabs.SelectedIndex = 4; // Observe the visible raid-to-board transition, keeping the old item selection behind it.
            trackTownTransition = true;
            var estate = JsonNode.Parse(File.ReadAllText(f.DecodedSeedPath))!.AsObject();
            File.WriteAllText(profile.EstateSavePath, QuantityItemSaveEditor.SetAmount(estate, townGold, 4321).UpdatedRoot.ToJsonString());
            await ExitGameInto(false);
            Assert(!showedIntermediateEmptyState, "Natural return must switch directly from the disabled map to the quest board without a town-unsupported placeholder.");
            trackTownTransition = false;
            CheckScene(QuantityItemSaveContext.Town, 4321, expectItemPrompt: false);
            Assert(File.Exists(profile.RaidSavePath) && File.Exists(profile.MapSavePath),
                "Returning to town must ignore retained expedition files without deleting them.");
            Console.WriteLine("PASS: WPF game start/exit town-to-raid and natural raid-to-town clear selection/preview/target, switch wallet/backpack quantities and hide stale maps.");

            // Stay on the item tab with its selection already empty.
            await ExitGameInto(true);
            CheckScene(QuantityItemSaveContext.Raid, 25, expectItemPrompt: false);
            await PrimePreview();
            var originalRaid = ComputeSha256(profile.RaidSavePath);
            var originalMap = ComputeSha256(profile.MapSavePath);
            // Hold only post-write sync; the full pre-write check must run normally.
            var forceTown = new ForceTownSaveService(codec, locations, () => running, _ =>
            {
                var gate = ReaderGate();
                Assert(gate.Wait(0), "Pre-write checks must have released the profile reader before commit.");
                heldGate = gate;
            });
            typeof(BattleMapView).GetField("_forceTownSaveService", flags)!.SetValue(panel, forceTown);
            Assert(dialogMessages.Count == 0, "Earlier operations must not raise unexpected dialogs: " + string.Join(" | ", dialogMessages));
            confirmForceTown = true;
            tabs.SelectedIndex = 4;
            var forceTownButton = (Button)panel.FindName("ForceTownButton");
            Assert(forceTownButton.IsEnabled, "Force town must be available on the actual battle tab.");
            trackTownTransition = true;
            showedIntermediateEmptyState = false;
            forceTownButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => Flag("_completingProfileEdit") || dialogMessages.Count > 1);
            Assert(dialogMessages.Count == 1 && heldGate is not null && !Enabled("ApplyButton") &&
                   !Enabled("PreviewButton") && !Enabled("BattleMapPanel") && Get("_preparedQuantityItemEdit") is null,
                "Force town must consume the raid preview and retain the write lock through post-edit synchronization: " + string.Join(" | ", dialogMessages));
            heldGate!.Release(); heldGate = null;
            await Settled();
            Assert(!showedIntermediateEmptyState, "Force town must keep the previous disabled frame until the quest board is ready.");
            trackTownTransition = false;
            CheckScene(QuantityItemSaveContext.Town, 4321, expectItemPrompt: false);
            Assert(ComputeSha256(profile.RaidSavePath) == originalRaid && ComputeSha256(profile.MapSavePath) == originalMap &&
                   Directory.EnumerateFiles(locations.BackupDirectory, "persist.game.json", SearchOption.AllDirectories).Any(),
                "Force town must retain expedition bytes, back up its configuration write and display town data after synchronization.");
            Console.WriteLine("PASS: actual Force town confirmation/commit waits for sync, clears raid selection/preview/target, restores town quantities and retains expedition files.");
        }
        finally
        {
            dialogs?.Stop();
            heldGate?.Release();
            Call("StopProfileSync");
            window.Close();
        }
    }
}
