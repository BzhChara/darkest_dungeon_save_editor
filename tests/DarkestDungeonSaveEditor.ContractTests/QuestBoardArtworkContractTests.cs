using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DarkestDungeonSaveEditor.App;

internal static partial class ContractSuite
{
    private const string BoardArtRoot = "campaign/town/quest_select/";

    private static void WriteBoardTestImage(string path, int width, int height, byte red, int opaqueSide = 0)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var offset = (y * width + x) * 4;
                pixels[offset + 2] = red;
                pixels[offset + 3] = (byte)(opaqueSide == 0 || (opaqueSide < 0 ? x < width / 2 : x >= width / 2) ? 255 : 0);
            }
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static async Task RunQuestBoardArtworkRefreshAsync(ContractFixture f, ProfileCatalogSnapshotReader reader)
    {
        // Presentation-only resources in the isolated fixture: no player save,
        // installed Mod or natural quest-generation rule is modified.
        var baseSnapshot = await reader.ReadAsync(refreshContent: true);
        var rewardPath = baseSnapshot.QuestBoard!.Quests[0].Rewards[0].IconPath;
        var framePath = BoardArtRoot + "quest_select_length_generated_1.png";
        var backgroundPath = BoardArtRoot + "quest_select.background.png";
        var paths = new[] { backgroundPath, BoardArtRoot + "quest_select.questverbose_bg.png",
            BoardArtRoot + "quest_select_selected.png", BoardArtRoot + "dungeon_progressionbar.png",
            BoardArtRoot + "quest_select_explore_3.png", framePath, rewardPath };
        foreach (var path in paths) WriteBoardTestImage(Path.Combine(f.ActiveWorkshopRoot, path), 40, 40, 32);
        WriteBoardTestImage(Path.Combine(f.GameRoot, backgroundPath), 1920, 1080, 16);
        var manifest = Path.Combine(f.ActiveWorkshopRoot, "modfiles.txt");
        foreach (var path in paths)
            File.AppendAllText(manifest, $"\n{path} {new FileInfo(Path.Combine(f.ActiveWorkshopRoot, path)).Length}\n");
        var before = await reader.ReadAsync(refreshContent: true);
        foreach (var path in paths)
        {
            var fullPath = Path.Combine(f.ActiveWorkshopRoot, path);
            var time = File.GetLastWriteTimeUtc(fullPath);
            WriteBoardTestImage(fullPath, 40, 40, 64);
            File.SetLastWriteTimeUtc(fullPath, time);
            var after = await reader.ReadAsync(refreshContent: true);
            Assert(after.QuestBoardArtworkFingerprint != before.QuestBoardArtworkFingerprint &&
                   after.ContentFingerprint == before.ContentFingerprint &&
                   ProfileCatalogSnapshotReader.HashesEqual(after.FileHashes, before.FileHashes) &&
                   !ReferenceEquals(after.QuantityItems, before.QuantityItems) && !ReferenceEquals(after.QuestBoard, before.QuestBoard),
                $"Replacing used artwork at {path} must refresh the board and common catalogs without changing saves.");
            // Disabled artwork-only experiment: ReferenceEquals(after.QuantityItems, before.QuantityItems)
            Assert(QuestBoardArtwork.Capture(after.Content.Sources, after.QuestBoard, after.QuestBoardCatalog) == after.QuestBoardArtworkFingerprint,
                "The published art fingerprint must describe the same resolved paths used by rendering.");
            var quiet = await reader.ReadAsync(refreshContent: true);
            Assert(ReferenceEquals(after.QuestBoard, quiet.QuestBoard) && ReferenceEquals(after.QuestBoardCatalog, quiet.QuestBoardCatalog),
                "Unchanged artwork must not repeatedly rebuild snapshots.");
            before = after;
        }

        // An unregistered Mod file and an unused reward must not enter the art dependency set.
        WriteBoardTestImage(Path.Combine(f.ActiveWorkshopRoot, BoardArtRoot + "quest_select.icon.png"), 40, 40, 64);
        WriteBoardTestImage(Path.Combine(f.GameRoot, "panels/icons_equip/trinket/inv_trinket+unused_art_fixture.png"), 40, 40, 64);
        Assert(ReferenceEquals(before.QuestBoard, (await reader.ReadAsync(refreshContent: true)).QuestBoard),
            "Unused or unregistered artwork must not trigger a refresh.");
        // An existing lower-priority image is irrelevant while a registered Mod wins.
        WriteBoardTestImage(Path.Combine(f.GameRoot, backgroundPath), 1920, 1080, 48);
        Assert(ReferenceEquals(before.QuestBoard, (await reader.ReadAsync(refreshContent: true)).QuestBoard),
            "Changes to a shadowed image must not replace the effective provider.");

        var winnerPath = Path.Combine(f.ActiveWorkshopRoot, backgroundPath);
        File.Delete(winnerPath);
        var missing = await reader.ReadAsync(refreshContent: true);
        Assert(missing.QuestBoardArtworkFingerprint != before.QuestBoardArtworkFingerprint &&
               missing.QuestBoard!.ImagePaths[backgroundPath] == Path.GetFullPath(winnerPath),
            "A deleted registered winner must refresh its missing state without reviving the base image.");
        WriteBoardTestImage(winnerPath, 1920, 1080, 64);
        var recovered = await reader.ReadAsync(refreshContent: true);
        Assert(recovered.QuestBoardArtworkFingerprint != missing.QuestBoardArtworkFingerprint,
            "Restoring a missing provider at the same path must refresh it.");

        // A plot icon newly added to a physical directory changes the preferred icon.
        var plotIcon = BoardArtRoot + "quest_select_plot_tutorial_crypts.png";
        WriteBoardTestImage(Path.Combine(f.GameRoot, plotIcon), 48, 48, 64);
        var dedicated = await reader.ReadAsync(refreshContent: true);
        Assert(dedicated.QuestBoard!.Quests[1].IconPath == plotIcon,
            "A previously absent dedicated plot image must be discovered without changing the save.");
        File.Delete(Path.Combine(f.GameRoot, plotIcon));
        var fallback = await reader.ReadAsync(refreshContent: true);
        Assert(fallback.QuestBoard!.Quests[1].IconPath == BoardArtRoot + "quest_select_explore_1.png",
            "Deleting a physical plot override must restore the ordinary plot-type image lookup.");

        // This reduced template is display-only and is not present on the saved board.
        WriteMultiMash(f.GameRoot, "campaign/quest/art.quest.plot_quests.json", """
        {"plot_quests":[{"id":"art_picker_only","quest":{"type":"explore","dungeon":"crypts",
          "difficulty":1,"length":1,"goal_ids":["explore_all_rooms"]}},
          {"id":"plot_tutorial_crypts","quest":{"type":"explore","dungeon":"crypts",
          "difficulty":1,"length":1,"goal_ids":["explore_all_rooms"]}}]}
        """);
        var pickerPath = Path.Combine(f.GameRoot, BoardArtRoot + "quest_select_art_picker_only.png");
        WriteBoardTestImage(pickerPath, 48, 48, 32);
        var pickerBefore = await reader.ReadAsync(refreshContent: true);
        Assert(pickerBefore.QuestBoardCatalog!.Templates.Any(entry => entry.Quest.Id == "art_picker_only"),
            "The refresh contract must include an available choice absent from the saved board.");
        WriteBoardTestImage(pickerPath, 64, 64, 64);
        var pickerAfter = await reader.ReadAsync(refreshContent: true);
        Assert(pickerAfter.QuestBoardArtworkFingerprint != pickerBefore.QuestBoardArtworkFingerprint &&
               pickerAfter.ContentFingerprint == pickerBefore.ContentFingerprint &&
               !ReferenceEquals(pickerAfter.QuestBoardCatalog, pickerBefore.QuestBoardCatalog),
            "Picker-only artwork must invalidate the available-choice image cache too.");

        // Normal post-write sync must not publish fresh board artwork together
        // with stale picker bindings and then regard that mixed state as current.
        var questPath = Path.Combine(f.ProfileRoot, "persist.quest.json");
        var artSave = Path.Combine(f.RunRoot, "art-save-refresh.json");
        await f.Codec.DecodeAsync(questPath, artSave);
        var questSave = JsonSupport.ReadObject(artSave);
        questSave["base_root"]!["quests"]!["0"]!["completion_reward"]!["items_definition"]!["items"]!["0"]!["amount"] = 6501;
        File.WriteAllText(artSave, questSave.ToJsonString());
        await f.Codec.EncodeAsync(artSave, questPath, null);
        WriteBoardTestImage(Path.Combine(f.GameRoot, plotIcon), 48, 48, 96);
        File.Delete(pickerPath);
        // Model a file replacement between the two independent metadata reads.
        var laterCatalog = QuestBoardEditCatalog.Load(pickerAfter.Content);
        Assert(pickerAfter.QuestBoard!.Quests[1].IconPath != plotIcon &&
               laterCatalog.Templates.Single(entry => entry.Quest.Id == "plot_tutorial_crypts").Quest.IconPath == plotIcon,
            "This fixture must represent two real reads on opposite sides of an image addition.");
        var coherent = QuestBoardArtwork.Refresh(pickerAfter.Content.Sources, pickerAfter.QuestBoard, laterCatalog);
        Assert(coherent.Board.Quests[1].IconPath == plotIcon &&
               coherent.Catalog!.Templates.Single(entry => entry.Quest.Id == "plot_tutorial_crypts").Quest.IconPath == plotIcon &&
               QuestBoardArtwork.CaptureResolved(coherent.Board, coherent.Catalog) ==
                   QuestBoardArtwork.Capture(pickerAfter.Content.Sources, coherent.Board, coherent.Catalog),
            "Publication must unify both views' bindings before recording a fresh fingerprint.");
        var saveOnly = await reader.ReadAsync();
        Assert(saveOnly.ContentFingerprint == pickerAfter.ContentFingerprint &&
               !ReferenceEquals(saveOnly.QuantityItems, pickerAfter.QuantityItems) &&
               !ReferenceEquals(saveOnly.QuestBoardCatalog!.Templates, pickerAfter.QuestBoardCatalog!.Templates) &&
               saveOnly.QuestBoard!.Quests[1].IconPath == plotIcon &&
               saveOnly.QuestBoardCatalog!.Templates.Single(entry => entry.Quest.Id == "plot_tutorial_crypts").Quest.IconPath == plotIcon &&
               saveOnly.QuestBoardCatalog.Templates.Single(entry => entry.Quest.Id == "art_picker_only").Quest.IconPath == BoardArtRoot + "quest_select_explore_1.png",
            "Artwork discovered during a save-only board rebuild must refresh shared bindings and common definition catalogs.");
        // Disabled artwork-only experiment: ReferenceEquals(saveOnly.QuantityItems, pickerAfter.QuantityItems)
        var saveOnlyChecked = await reader.ReadAsync(refreshContent: true);
        Assert(ReferenceEquals(saveOnly.QuestBoardCatalog, saveOnlyChecked.QuestBoardCatalog),
            "An explicit check after save-only publication must see a consistent cached picker.");
        WriteBoardTestImage(pickerPath, 48, 48, 96);
        File.Delete(Path.Combine(f.GameRoot, plotIcon));
        var progressionPath = Path.Combine(f.ProfileRoot, "persist.progression.json");
        File.WriteAllText(artSave, """{"base_root":{"dungeon":{"crypts":{"xp":7}}}}""");
        await f.Codec.EncodeAsync(artSave, progressionPath, null);
        var progressionOnly = await reader.ReadAsync();
        Assert(!ReferenceEquals(progressionOnly.QuantityItems, saveOnlyChecked.QuantityItems) &&
               !ReferenceEquals(progressionOnly.QuestBoardCatalog!.Templates, saveOnlyChecked.QuestBoardCatalog!.Templates) &&
               progressionOnly.QuestBoard!.Quests[1].IconPath == BoardArtRoot + "quest_select_explore_1.png" &&
               progressionOnly.QuestBoardCatalog!.Templates.Single(entry => entry.Quest.Id == "plot_tutorial_crypts").Quest.IconPath == BoardArtRoot + "quest_select_explore_1.png" &&
               progressionOnly.QuestBoardCatalog.Templates.Single(entry => entry.Quest.Id == "art_picker_only").Quest.IconPath == BoardArtRoot + "quest_select_art_picker_only.png",
            "Progression-only refreshes must route restored/removed artwork through the common catalog rebuild too.");

        var layoutPath = WriteMultiMash(f.GameRoot, BoardArtRoot + "art.quest_select.layout.darkest",
            "quest_select_dungeon_layout_crypts: .quest_map_pos 940 220 .quest_number_of_quests_in_row 4\n");
        var layoutBefore = await reader.ReadAsync(refreshContent: true);
        File.WriteAllText(layoutPath, "quest_select_dungeon_layout_crypts: .quest_map_pos 980 240 .quest_number_of_quests_in_row 4\n");
        var layoutAfter = await reader.ReadAsync(refreshContent: true);
        Assert(layoutAfter.QuestBoard!.Regions.Single(region => region.Id == "crypts").X == 980 &&
               layoutAfter.ContentFingerprint != layoutBefore.ContentFingerprint,
            "Layout-only edits must still use definition refresh and replace the displayed coordinates.");

        await VerifyQuestBoardArtworkPublicationAsync(f, layoutAfter);
    }

    private static async Task VerifyQuestBoardArtworkPublicationAsync(ContractFixture f, ProfileCatalogSnapshot initial)
    {
        var framePath = Path.Combine(f.ActiveWorkshopRoot, BoardArtRoot + "quest_select_length_generated_1.png");
        WriteBoardTestImage(framePath, 40, 40, 64, opaqueSide: 1);
        // Use a completely transparent type layer to observe the frame's alpha hit test alone.
        var typePath = Path.Combine(f.ActiveWorkshopRoot, BoardArtRoot + "quest_select_explore_3.png");
        var bitmap = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4], 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(typePath)) encoder.Save(stream);
        var window = new MainWindow(Path.Combine(f.RunRoot, "art-refresh-settings.json"), gameRunningProbe: () => false)
        { Left = -20000, Top = -20000, ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual };
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Get(string name) => typeof(MainWindow).GetField(name, flags)!.GetValue(window);
        void Set(string name, object? value) => typeof(MainWindow).GetField(name, flags)!.SetValue(window, value);
        object? Call(string name, params object?[] args)
        {
            var method = typeof(MainWindow).GetMethod(name, flags)!;
            return method.Invoke(window, method.GetParameters().Select((p, i) => i < args.Length ? args[i] : p.DefaultValue).ToArray());
        }
        try
        {
            window.Show();
            ((TabControl)window.FindName("CatalogTabs")).SelectedIndex = 4;
            Set("_activeContentSnapshot", initial.Content);
            Set("_allItems", initial.QuantityItems.Items);
            Set("_quantitySaveContext", initial.QuantityItems.SaveContext);
            var locations = new SaveEditorLocations(f.RunRoot, Path.Combine(f.RunRoot, "art-ui-work"), Path.Combine(f.RunRoot, "art-ui-backups"));
            await (Task)Call("StartProfileSyncAsync", initial.Content, initial.QuantityItems, f.Codec, f.GameRoot,
                f.WorkshopRoot, f.LocalModDirectory, initial.ContentFingerprint, locations)!;
            ((DispatcherTimer)Get("_gameProcessPoll")!).Stop();
            var board = (QuestBoardPreview)window.FindName("QuestBoardPanel");
            QuestBoardTaskButton Selected() => ((Canvas)board.FindName("MapCanvas")).Children.OfType<Canvas>()
                .SelectMany(region => region.Children.OfType<QuestBoardTaskButton>()).Single(button => Equals(button.Tag, board.SelectedKey));
            var before = Selected();
            Assert(before.Width == 40 && !before.ContainsArtwork(new Point(8, 20)) && before.ContainsArtwork(new Point(32, 20)),
                "Initial task geometry must use the original image size and right-side opaque pixels.");
            var oldBackground = ((Image)board.FindName("MapBackdrop")).Source;
            var tabs = (TabControl)window.FindName("CatalogTabs");
            for (var i = 0; i < 3; i++)
            {
                tabs.SelectedIndex = 0;
                window.UpdateLayout();
                tabs.SelectedIndex = 4;
                window.UpdateLayout();
                await Dispatcher.Yield(DispatcherPriority.ContextIdle);
                Call("UpdateEnabledState");
                Assert(ReferenceEquals(before, Selected()) &&
                       ReferenceEquals(oldBackground, ((Image)board.FindName("MapBackdrop")).Source),
                    "Re-entering the board and updating enabled state must reuse its task visuals and decoded artwork.");
            }
            // A save-only board publication may rebuild task controls, but unchanged
            // image providers must retain their decoded pixels and alpha masks.
            board.SynchronizeProfile(initial.QuestBoard! with { }, initial.QuestBoardCatalog, contentChanged: false);
            Assert(ReferenceEquals(oldBackground, ((Image)board.FindName("MapBackdrop")).Source),
                "A save-only board publication must not evict unchanged artwork.");
            before = Selected();
            WriteBoardTestImage(framePath, 80, 80, 96, opaqueSide: -1);
            WriteBoardTestImage(Path.Combine(f.ActiveWorkshopRoot, BoardArtRoot + "quest_select.background.png"), 1920, 1080, 96);
            Call("RequestProfileSync", false, true);
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while ((bool)Get("_syncInProgress")! || (bool)Get("_syncRequested")!)
            {
                if (DateTime.UtcNow > deadline) throw new TimeoutException("Quest artwork sync did not settle.");
                await Task.Delay(15);
            }
            window.UpdateLayout();
            Assert((bool)Get("_syncReady")! && Get("_lastSyncError") is null, "Artwork refresh must finish through the shared UI workflow.");
            var after = Selected();
            Assert(!ReferenceEquals(before, after) && after.Width == 80 && after.Height == 80 &&
                   after.ContainsArtwork(new Point(8, 20)) && !after.ContainsArtwork(new Point(72, 20)),
                "Shared publication must rebuild image bounds and discard stale alpha masks without reopening the tab.");
            Assert(!ReferenceEquals(oldBackground, ((Image)board.FindName("MapBackdrop")).Source),
                "The visible background must reload after a same-path replacement.");
            var viewport = (Border)board.FindName("MapViewport");
            DependencyObject? Hit(Point local)
            {
                var point = after.TranslatePoint(local, viewport);
                Assert(new Rect(viewport.RenderSize).Contains(point), "The hit-test fixture must be visible in the viewport.");
                var hit = viewport.InputHitTest(point) as DependencyObject;
                while (hit is not null && hit is not Button) hit = VisualTreeHelper.GetParent(hit);
                return hit;
            }
            Assert(ReferenceEquals(Hit(new Point(8, 20)), after) && !ReferenceEquals(Hit(new Point(72, 20)), after),
                "Actual WPF point routing must follow refreshed opaque pixels, not the old image or its bounding rectangle.");
        }
        finally { Call("StopProfileSync"); window.Close(); }
    }
}
