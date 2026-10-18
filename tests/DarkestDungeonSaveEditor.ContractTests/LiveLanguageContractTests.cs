using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using DarkestDungeonSaveEditor.App;

internal static partial class ContractSuite
{
    private static void FlushLanguageBindings() => Dispatcher.CurrentDispatcher.Invoke(
        () => { }, DispatcherPriority.ApplicationIdle);

    private static Action PrepareLiveLanguageState(MainWindow window)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Get(string name) => typeof(MainWindow).GetField(name, flags)!.GetValue(window);
        void Set(string name, object value) => typeof(MainWindow).GetField(name, flags)!.SetValue(window, value);

        ((TextBox)window.FindName("ProfileDirectoryTextBox")).Text = @"E:\fixture\profile_1";
        ((TextBox)window.FindName("SearchTextBox")).Text = "food";
        var sources = new[] { new ActiveContentSource("base", "Darkest Dungeon", "base", @"E:\fixture", 0) };
        var item = new QuantityItemDefinition("provision", "food", QuantityItemStorageKind.RaidInventory,
            12, true, 4, "base", @"E:\fixture\food.darkest", false, ["base"])
        {
            LocalizedName = new BilingualContentName("食物", "Food"),
            LocalizedSource = ContentSourceLabelFormatter.Create("base", ["base"], sources),
            ReferenceStatus = QuantityItemReferenceStatus.OfficialContent
        };
        Set("_allItems", new[] { item });
        typeof(MainWindow).GetMethod("ApplyFilter", flags)!.Invoke(window, [false]);
        var grid = (DataGrid)window.FindName("ItemGrid");
        grid.SelectedIndex = 0;
        window.UpdateLayout();
        FlushLanguageBindings();
        var selected = grid.SelectedItem;
        var collection = grid.ItemsSource;
        var sourceText = new TextBlock();
        sourceText.SetBinding(TextBlock.TextProperty, new Binding("Source") { Source = selected });
        ((TextBox)window.FindName("CopiesTextBox")).Text = "37";
        Set("_selectedInitialQuirkIds", new[] { "fixture_quirk" });
        Set("_syncReady", true);
        // The language operation must retain this prepared operation, never re-prepare or write it.
        var prepared = new PreparedQuantityItemEdit("fixture-preview",
            new SaveProfile("profile_1", @"E:\fixture\profile_1", "estate", "fixture", DateTime.UtcNow),
            item, null!, new PreparedQuantityItemContentGuard("hash", sources, item.SourcePath, "hash", []),
            "", "", "", "", "", "hash", "hash", true, DateTime.UtcNow);
        Set("_preparedQuantityItemEdit", prepared);
        var revision = Get("_editRevision");
        ((TextBlock)window.FindName("PreviewSummaryTextBlock")).Text = "Existing preview";

        var map = (BattleMapView)window.FindName("BattleMapPanel");
        var tile = new BattleMapTileSnapshot("tile0", 0, 0, 0, 0, 0, 0,
            BattleMapTileKnowledge.Scouted, 2, BattleMapTileContent.Curio, 7, 0, 0, -1, 7, false);
        var snapshot = new BattleMapSnapshot(@"E:\fixture\profile_1", "map", "raid", "mapHash", "raidHash",
            "ruins", 1, 1, null, null, null, null, null, null, null, null, null, false, [],
            [new BattleMapAreaSnapshot("rooA", 1, BattleMapAreaKind.Room, BattleMapTileKnowledge.Scouted, 2, false, [tile])],
            [], DateTime.UtcNow);
        typeof(BattleMapView).GetMethod("RenderSnapshot", flags)!.Invoke(map, [snapshot, false]);
        var cells = (IList)typeof(BattleMapView).GetField("_cells", flags)!.GetValue(map)!;
        var cell = cells[0]!;
        typeof(BattleMapView).GetMethod("SelectCell", flags)!.Invoke(map, [cell]);
        var canvas = (Canvas)map.FindName("MapCanvas");
        var visual = canvas.Children[0];
        var scale = (ScaleTransform)map.FindName("MapScaleTransform");
        typeof(BattleMapView).GetField("_fitToView", flags)!.SetValue(map, false);
        scale.ScaleX = scale.ScaleY = 1.23;
        var publishedSnapshots = 0;
        map.SnapshotRefreshed += _ => publishedSnapshots++;

        // Keep definitions loaded in the original language, then open real confirmation
        // dialogs after each switch. Always cancel before any save or bridge operation.
        var sourceLabel = ContentSourceLabelFormatter.Create("base", ["base"], sources);
        var encounterGuard = new BattleEncounterTableGuard("ruins", 1, "game", "gameHash", sources, [], "hash");
        var encounter = new BattleEncounterDefinition(1, 0, BattleEncounterSourceKind.Standard, ["fixture_monster"],
            1, sourceLabel.ToString(), "source", "hash", 1, true, "", encounterGuard)
        { LocalizedSource = sourceLabel, OriginDungeonId = "ruins", OriginDifficulty = 1 };
        var attachmentGuard = new BattleRoomAttachmentCatalogGuard(prepared.Profile.ProfileDirectory,
            "game", "gameHash", sources, [], "hash");
        var attachment = new BattleRoomAttachmentDefinition("fixture_curio", BattleRoomAttachmentKind.Curio, 1,
            sourceLabel.ToString(), "source", "props/curio.props.darkest", "hash", 1, attachmentGuard)
        { LocalizedSource = sourceLabel };
        void SetMap(string name, object value) => typeof(BattleMapView).GetField(name, flags)!.SetValue(map, value);
        SetMap("_profile", prepared.Profile);
        SetMap("_activeContentSnapshot", new ActiveContentSnapshot(prepared.Profile, "base", sources, [], "", "", 0, "gameHash"));
        SetMap("_editService", new BattleMapEditService(DsonSaveCodec.CreateNative()));
        SetMap("_managedEncounterBridgeService", new ManagedBattleEncounterBridgeService(DsonSaveCodec.CreateNative()));
        SetMap("_gameDirectory", @"E:\fixture");
        SetMap("_encounterCatalog", new BattleEncounterCatalogResult("ruins", 1, [encounter], [], encounterGuard));
        SetMap("_roomAttachmentCatalog", new BattleRoomAttachmentCatalogResult([attachment], [], attachmentGuard));

        void CheckConfirmation(string method, object definition, string sourceKey)
        {
            string? message = null;
            var capture = Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                var dialog = window.OwnedWindows.OfType<ThemedDialog>().SingleOrDefault();
                if (dialog is null) return;
                message = ((TextBlock)dialog.FindName("MessageTextBlock")).Text;
                dialog.DialogResult = false;
            }));
            try
            {
                ((Task)typeof(BattleMapView).GetMethod(method, flags)!.Invoke(map, [cell, definition])!).GetAwaiter().GetResult();
            }
            finally { capture.Abort(); }
            var expectedSource = EditorText.Format(sourceKey, EditorText.Get("ContentSourceLabelFormatter_003"), "ruins");
            Assert(message is not null && message.Contains(expectedSource, StringComparison.Ordinal),
                $"{method} must show the current source language in a new confirmation, even for a previously loaded definition.");
        }

        return () =>
        {
            Assert(ReferenceEquals(grid.SelectedItem, selected) && ReferenceEquals(grid.ItemsSource, collection) &&
                   ((TextBox)window.FindName("CopiesTextBox")).Text == "37" &&
                   ((TextBox)window.FindName("SearchTextBox")).Text == "food" &&
                   ((TextBox)window.FindName("ProfileDirectoryTextBox")).Text == @"E:\fixture\profile_1" &&
                   Equals(Get("_editRevision"), revision) && ReferenceEquals(Get("_preparedQuantityItemEdit"), prepared) &&
                   ((IReadOnlyList<string>)Get("_selectedInitialQuirkIds")!).SequenceEqual(["fixture_quirk"]) &&
                   ((Button)window.FindName("ApplyButton")).IsEnabled &&
                   ((TextBlock)window.FindName("PreviewSummaryTextBlock")).Text == "Existing preview",
                "Language switching must retain input, selections, initial quirks and the prepared edit without invalidation.");
            Assert(sourceText.Text == EditorText.Get("ContentSourceLabelFormatter_003"),
                "An already-bound catalog source label must refresh without rebuilding the rows.");
            Assert(ReferenceEquals(map.CurrentSnapshot, snapshot) && ReferenceEquals(canvas.Children[0], visual) &&
                   ReferenceEquals(typeof(BattleMapView).GetField("_selectedCell", flags)!.GetValue(map), cell) &&
                   scale.ScaleX == 1.23 && scale.ScaleY == 1.23 && publishedSnapshots == 0 &&
                   ((TextBlock)map.FindName("MapSelectionTextBlock")).Text.Contains(EditorText.Get("BattleMapView_CellVisuals_012"), StringComparison.Ordinal),
                "Map language must update while preserving snapshot, cell visuals, selection and zoom, without a fake sync event.");
            CheckConfirmation("PlaceManagedEncounterAsync", encounter, "BattleMapView_Commands_017");
            CheckConfirmation("PlaceBattleAsync", encounter, "BattleMapView_Commands_049");
            CheckConfirmation("SetBattleAttachmentAsync", attachment, "BattleMapView_Commands_049");
            CheckConfirmation("PlaceContentAsync", attachment, "BattleMapView_Commands_049");
        };
    }

    private static void VerifyLanguageAcrossOldExecutionContext()
    {
        var system = EditorText.SystemCulture;
        EditorText.Initialize("en", system);
        var context = ExecutionContext.Capture()!;
        EditorText.Initialize("zh-CN", system);
        ExecutionContext.Run(context, _ => Assert(EditorText.Get("Language_Label") == "语言" && EditorText.IsChinese,
            "Callbacks captured before switching must use the new language, not their old UI culture."), null);
    }
}
