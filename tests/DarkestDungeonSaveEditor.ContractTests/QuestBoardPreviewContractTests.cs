using System.Reflection;
using System.Text.Json.Nodes;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DarkestDungeonSaveEditor.App;
using DarkestDungeonSaveEditor.Core;

internal static partial class ContractSuite
{
    public static async Task RunQuestBoardPreviewAsync(string repositoryRoot, string? gameDirectory)
    {
        var runRoot = Path.Combine(repositoryRoot, "artifacts", "quest-board-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(runRoot);
        if (gameDirectory is not null) ValidateQuestBoardFixtures(gameDirectory);
        await RunWpfContractsAsync(() =>
        {
            foreach (var language in new[] { "en", "zh-CN" })
            {
                EditorText.Initialize(language, EditorText.SystemCulture);
                var gameRunning = false;
                var window = new MainWindow(Path.Combine(runRoot, language, "language.json"), gameRunningProbe: () => gameRunning)
                {
                    Left = -20000, Top = -20000, ShowActivated = false, ShowInTaskbar = false,
                    WindowStartupLocation = WindowStartupLocation.Manual
                };
                var defaultWindowSize = new Size(window.Width, window.Height);
                Assert(defaultWindowSize == new Size(1460, 920), "The main window must use the requested 1460 by 920 dimensions.");
                try
                {
                    window.Show();
                    ((TextBox)window.FindName("GameDirectoryTextBox")).Text = gameDirectory ?? runRoot;
                    ((TabControl)window.FindName("CatalogTabs")).SelectedIndex = 4;
                    FlushLanguageBindings();
                    var board = (QuestBoardPreview)window.FindName("QuestBoardPanel");
                    Assert(board.Samples.Count == 0, "The application must not populate the real board with fixture quests.");
                    board.UseSampleData();
                    var battle = (BattleMapView)window.FindName("BattleMapPanel");
                    var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                    void SetState(string field, object? value) => typeof(MainWindow).GetField(field, flags)!.SetValue(window, value);
                    void Refresh() => typeof(MainWindow).GetMethod("UpdateEnabledState", flags)!.Invoke(window, null);
                    Assert(board.IsVisible && !board.IsEnabled && !battle.IsVisible &&
                           ((Grid)board.FindName("ProfileRequiredPanel")).Visibility == Visibility.Visible &&
                           ((Grid)board.FindName("BoardBody")).Visibility == Visibility.Collapsed,
                        "Before loading a profile, the board must show the empty state, hide sample content, and disable interaction.");
                    var originalSamples = board.Samples.ToArray();
                    board.ApplySample("crypts", new QuestBoardChoice("cleanse"));
                    board.RemoveSample(originalSamples[0].Key);
                    QuestCategoryItem(board.CreateTaskMenu("crypts", null), 0, QuestBoardCategory.Cleanse)
                        .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    Assert(board.Samples.SequenceEqual(originalSamples) &&
                           !Application.Current.Windows.OfType<QuestBoardPreviewDialog>().Any(),
                        "Disabled editing entry points must neither mutate samples nor open the picker.");
                    RenderContractWindow(window, new Size(1120, 720), Path.Combine(runRoot, $"unloaded-{language}.png"));

                    // Exercise the UI's loaded/synchronizing states without a real save or catalog reader.
                    SetState("_catalogProfileDirectory", runRoot);
                    Refresh();
                    Assert(!board.IsEnabled, "Having a loaded profile must not unlock editing before synchronization finishes.");
                    SetState("_syncReady", true);
                    Refresh();
                    Assert(board.IsEnabled && ((Grid)board.FindName("ProfileRequiredPanel")).Visibility == Visibility.Collapsed &&
                           ((Grid)board.FindName("BoardBody")).Visibility == Visibility.Visible,
                        "The synchronized town profile must enable the sample board.");
                    ((TextBox)window.FindName("ProfileDirectoryTextBox")).Text = Path.Combine(runRoot, "changed-profile");
                    Assert(!board.IsEnabled && !((Button)board.FindName("ZoomInButton")).IsEnabled &&
                           ((Grid)board.FindName("ProfileRequiredPanel")).Visibility == Visibility.Visible,
                        "Changing the input path must immediately disable the board and toolbar, even with no selected catalog row.");
                    SetState("_catalogProfileDirectory", runRoot);
                    SetState("_syncReady", true);
                    Refresh();
                    if (gameDirectory is not null)
                        Assert(((Border)board.FindName("MissingArtNotice")).Visibility == Visibility.Collapsed,
                            "The preview must use the installed map when an actual game directory is supplied.");
                    foreach (var size in new[] { defaultWindowSize, new Size(1120, 720) })
                    {
                        RenderContractWindow(window, size, Path.Combine(runRoot, $"board-{language}-{size.Width}.png"));
                        AssertQuestBoardFrameVisible((Grid)board.FindName("BoardBody"));
                        var defaultViewport = (Border)board.FindName("MapViewport");
                        var defaultSurface = (Canvas)board.FindName("MapSurface");
                        var defaultBounds = defaultSurface.TransformToAncestor(defaultViewport).TransformBounds(new Rect(defaultSurface.RenderSize));
                        var contentBounds = QuestBoardContentBounds(board, defaultViewport);
                        Assert(((ScaleTransform)board.FindName("MapScale")).ScaleX == 0.5 &&
                               Math.Abs(defaultBounds.Width - 960) < 0.001 && Math.Abs(defaultBounds.Height - 540) < 0.001 &&
                               Math.Abs(contentBounds.Left - (defaultViewport.ActualWidth - contentBounds.Right)) < 0.001 &&
                               Math.Abs(contentBounds.Top - (defaultViewport.ActualHeight - contentBounds.Bottom)) < 0.001 &&
                               defaultBounds.Left < 0 && defaultBounds.Top < 0 && defaultViewport.ClipToBounds &&
                               !((Button)board.FindName("ZoomOutButton")).IsEnabled,
                            "Both default and minimum windows must center the region/quest content at 50%, clipping background overflow.");
                        if (size == defaultWindowSize)
                        {
                            Assert(contentBounds.Left >= 8 && contentBounds.Top >= 8 &&
                                   contentBounds.Right <= defaultViewport.ActualWidth - 8 &&
                                   contentBounds.Bottom <= defaultViewport.ActualHeight - 8,
                                "The default map must show every region header and ordinary quest marker with space inside all four edges.");
                            var darkestRegion = ((Canvas)board.FindName("MapCanvas")).Children.OfType<Canvas>()
                                .Single(panel => panel.Tag as string == "darkestdungeon");
                            var darkestHeader = darkestRegion.Children.OfType<Button>().Single(button => button.Content is TextBlock);
                            var darkestBounds = darkestHeader.TransformToAncestor(defaultViewport).TransformBounds(new Rect(darkestHeader.RenderSize));
                            Assert(darkestBounds.Top >= 8 && darkestBounds.Bottom <= defaultViewport.ActualHeight,
                                "The Darkest Dungeon title must be fully visible below the toolbar.");
                            var oldSelection = board.SelectedKey;
                            typeof(QuestBoardPreview).GetMethod("SelectSample", flags)!.Invoke(board, [board.Samples.Last().Key]);
                            FlushLanguageBindings();
                            Assert(defaultSurface.TransformToAncestor(defaultViewport).TransformBounds(new Rect(defaultSurface.RenderSize)) == defaultBounds,
                                "Selecting another quest and rotating its blood splatter must not shift the content framing.");
                            typeof(QuestBoardPreview).GetMethod("SelectSample", flags)!.Invoke(board, [oldSelection]);
                            FlushLanguageBindings();
                            var detailsScroll = (ScrollViewer)board.FindName("RewardsScroll");
                            var title = (TextBlock)board.FindName("QuestName");
                            var titleBounds = title.TransformToAncestor(detailsScroll).TransformBounds(new Rect(title.RenderSize));
                            var rewardArea = (ScrollViewer)board.FindName("RewardInventoryScroll");
                            var rewardBounds = rewardArea.TransformToAncestor(detailsScroll).TransformBounds(new Rect(rewardArea.RenderSize));
                            Assert(((Canvas)board.FindName("EstateMapHeader")).Visibility == Visibility.Collapsed && titleBounds.Top >= 0 && titleBounds.Top < 16,
                                "Details must start with the quest title, without a redundant estate-map header or blank header space.");
                            Assert(detailsScroll.VerticalOffset == 0 && rewardBounds.Left >= 0 &&
                                   rewardBounds.Right <= detailsScroll.ActualWidth && rewardArea.ScrollableHeight == 0,
                                "Ordinary rewards must remain within the detail pane's width without scrolling inside the reward row.");
                            var visibleRewardBottom = ((WrapPanel)board.FindName("RewardPanel")).Children.OfType<Canvas>()
                                .SelectMany(slot => slot.Children.OfType<Image>())
                                .Max(icon => icon.TransformToAncestor(detailsScroll).TransformBounds(new Rect(icon.RenderSize)).Bottom);
                            var clippedRewardHeight = Math.Max(0, visibleRewardBottom - detailsScroll.ActualHeight);
                            Console.WriteLine($"Default window {language} {size.Width}x{size.Height}: reward clipping={clippedRewardHeight:F2} DIP; detail scroll range={detailsScroll.ScrollableHeight:F2} DIP.");
                            Assert(clippedRewardHeight <= 0.01,
                                "Default window sizing must not clip the standard reward icons.");
                            Console.WriteLine($"Default map {language}: scale=50%; viewport={defaultViewport.ActualWidth:F2}x{defaultViewport.ActualHeight:F2}; content margins L/R={contentBounds.Left:F2}/{defaultViewport.ActualWidth - contentBounds.Right:F2}, T/B={contentBounds.Top:F2}/{defaultViewport.ActualHeight - contentBounds.Bottom:F2}; Darkest Dungeon title top={darkestBounds.Top:F2} DIP.");
                            detailsScroll.ScrollToEnd();
                            FlushLanguageBindings();
                            rewardBounds = rewardArea.TransformToAncestor(detailsScroll).TransformBounds(new Rect(rewardArea.RenderSize));
                            Assert(rewardBounds.Bottom <= detailsScroll.ActualHeight,
                                "Any rewards below the initial default viewport must be reachable by scrolling the details.");
                            detailsScroll.ScrollToTop();
                            FlushLanguageBindings();
                        }
                    }

                    Assert(board.FindName("StatusText") is null, "Quest operations must not have a separate board status strip.");
                    var boardBody = (Grid)board.FindName("BoardBody");
                    Assert(boardBody.ColumnDefinitions.Count == 2 && !boardBody.Children.OfType<GridSplitter>().Any(),
                        "The board must keep details in a separate pane without a horizontal resize handle.");
                    Assert(board.FindName("UndoButton") is null &&
                           typeof(QuestBoardPreview).GetMethod("Reset_Click", flags) is null,
                        "The board must not expose undo or reset operations.");
                    var fullMapButton = (Button)board.FindName("ExpandButton");
                    var townButton = (Button)battle.FindName("ForceTownButton");
                    Assert(fullMapButton.Parent is StackPanel toolbar && toolbar.Children.IndexOf(fullMapButton) == 0 &&
                           fullMapButton.Style == townButton.Style && fullMapButton.MinWidth == townButton.MinWidth &&
                           fullMapButton.MinHeight == townButton.MinHeight && fullMapButton.Margin == townButton.Margin &&
                           fullMapButton.Padding == townButton.Padding && fullMapButton.FontSize == townButton.FontSize,
                        "Full map must occupy the battle toolbar's return-to-town position with the identical action-button styling.");
                    var activityLog = (TextBox)window.FindName("StatusTextBox");
                    var viewport = (Border)board.FindName("MapViewport");
                    var surface = (Canvas)board.FindName("MapSurface");
                    Assert(System.Windows.Controls.Primitives.LayoutInformation.GetLayoutClip(surface) is null,
                        "The full-size map surface must not be clipped to the viewport before its zoom transform is applied.");
                    var detailCanvas = (Canvas)board.FindName("DetailsCanvas");
                    var rewards = (ScrollViewer)board.FindName("RewardsScroll");
                    var backdrop = (Image)board.FindName("MapBackdrop");
                    Assert(board.FindName("LeftTreeBackdrop") is null && board.FindName("DetailsSceneCanvas") is null &&
                           surface.Children.OfType<Image>().Single() == backdrop && !surface.IsAncestorOf(rewards) &&
                           Canvas.GetLeft(backdrop) == 0 && Canvas.GetTop(backdrop) == 0 &&
                           surface.Clip.Bounds == new Rect(0, 0, 1920, 1080),
                        "The map must show the full original background including both trees, separately from the native detail panel.");
                    var mapCanvas = (Canvas)board.FindName("MapCanvas");
                    foreach (var regionPanel in mapCanvas.Children.OfType<Canvas>())
                    {
                        var region = QuestBoardPreviewData.Regions.Single(region => region.Id == (string)regionPanel.Tag);
                        Assert(Canvas.GetLeft(regionPanel) - Canvas.GetLeft(backdrop) == region.X &&
                               Canvas.GetTop(regionPanel) - Canvas.GetTop(backdrop) == region.Y,
                            "Restoring the entire background must preserve native region positions in both axes.");
                    }
                    var markerScale = (ScaleTransform)board.FindName("MapScale");
                    var mapOffset = (TranslateTransform)board.FindName("MapOffset");
                    var defaultOffset = new Point(mapOffset.X, mapOffset.Y);
                    ((TabControl)window.FindName("CatalogTabs")).SelectedIndex = 3;
                    FlushLanguageBindings();
                    ((TabControl)window.FindName("CatalogTabs")).SelectedIndex = 4;
                    FlushLanguageBindings();
                    board.RefreshLanguage();
                    Assert(markerScale.ScaleX == 0.5 && new Point(mapOffset.X, mapOffset.Y) == defaultOffset,
                        "Tab and language refresh must not shrink the default crop back to whole-image fit.");
                    Exception? defaultExpansionFailure = null;
                    window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                    {
                        var expanded = Application.Current.Windows.OfType<Window>().Single(x => x != window);
                        try
                        {
                            FlushLanguageBindings();
                            Assert(markerScale.ScaleX > 0.5 && !((Button)board.FindName("ZoomOutButton")).IsEnabled,
                                "Full map keeps its existing larger whole-image fit instead of the embedded 50% crop.");
                            RenderContractWindow(expanded, new Size(expanded.MinWidth, expanded.MinHeight),
                                Path.Combine(runRoot, $"expanded-small-{language}.png"));
                            var wholeMapScale = Math.Min(viewport.ActualWidth / surface.Width, viewport.ActualHeight / surface.Height);
                            Assert(markerScale.ScaleX < 0.5 && Math.Abs(markerScale.ScaleX - wholeMapScale) < 0.000001,
                                "A small full-map window must still fit the entire map instead of inheriting the embedded 50% minimum.");
                        }
                        catch (Exception error) { defaultExpansionFailure = error; }
                        finally { expanded.Close(); }
                    }));
                    fullMapButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    if (defaultExpansionFailure is not null) throw defaultExpansionFailure;
                    FlushLanguageBindings();
                    Assert(markerScale.ScaleX == 0.5 && new Point(mapOffset.X, mapOffset.Y) == defaultOffset,
                        "Returning from full map must restore the centered 50% default view.");
                    ((Button)board.FindName("FitButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    FlushLanguageBindings();
                    var initialMarkerScale = markerScale.ScaleX;
                    var initialBackdropWidth = backdrop.TransformToAncestor(viewport).TransformBounds(new Rect(backdrop.RenderSize)).Width;
                    var fittedBounds = surface.TransformToAncestor(viewport).TransformBounds(new Rect(surface.RenderSize));
                    var fittedContent = QuestBoardContentBounds(board, viewport);
                    Assert(initialMarkerScale == 0.5 && fittedBounds.Left < 0 && fittedBounds.Top < 0 &&
                           Math.Abs(fittedContent.Left - (viewport.ActualWidth - fittedContent.Right)) < 0.001 &&
                           Math.Abs(fittedContent.Top - (viewport.ActualHeight - fittedContent.Bottom)) < 0.001,
                        "Embedded Fit must center the region/quest content at 50%, not shrink to show the whole background.");
                    var detailBounds = detailCanvas.TransformToAncestor(boardBody).TransformBounds(new Rect(detailCanvas.RenderSize));
                    Assert(!((Button)board.FindName("ZoomOutButton")).IsEnabled, "Fit must be the minimum scale.");
                    ((Button)board.FindName("ZoomInButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    FlushLanguageBindings();
                    var enlargedBackdropWidth = backdrop.TransformToAncestor(viewport).TransformBounds(new Rect(backdrop.RenderSize)).Width;
                    Assert(markerScale.ScaleX > initialMarkerScale &&
                           Math.Abs(enlargedBackdropWidth / initialBackdropWidth - markerScale.ScaleX / initialMarkerScale) < 0.0001,
                        "The installed map artwork and quest markers must zoom together.");
                    Assert(detailCanvas.TransformToAncestor(boardBody).TransformBounds(new Rect(detailCanvas.RenderSize)) == detailBounds,
                        "Zooming the right map must leave the independent detail panel's size and position unchanged.");
                    RenderContractWindow(window, new Size(1120, 720), Path.Combine(runRoot, $"zoomed-{language}.png"));
                    void CheckPanLimits(Rect requiredBounds)
                    {
                        foreach (var offset in new[] { -100000d, 100000d })
                        {
                            mapOffset.X = mapOffset.Y = offset;
                            typeof(QuestBoardPreview).GetMethod("ClampMapOffset", flags)!.Invoke(board, null);
                            var bounds = surface.TransformToAncestor(viewport).TransformBounds(new Rect(surface.RenderSize));
                            Assert(bounds.Left <= requiredBounds.Left + 0.001 && bounds.Top <= requiredBounds.Top + 0.001 &&
                                   bounds.Right >= requiredBounds.Right - 0.001 && bounds.Bottom >= requiredBounds.Bottom - 0.001,
                                "Dragging to either limit must keep all four edges of the fitted map area covered.");
                            AssertQuestBoardFrameVisible(boardBody);
                        }
                    }
                    CheckPanLimits(fittedBounds);
                    ((Button)board.FindName("ResetZoomButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert(markerScale.ScaleX == 1, "100% must reset the shared map scale.");
                    CheckPanLimits(fittedBounds);
                    typeof(QuestBoardPreview).GetMethod("SetZoom", flags)!.Invoke(board, [0.001, new Point(0, 0)]);
                    Assert(Math.Abs(markerScale.ScaleX - initialMarkerScale) < 0.000001,
                        "Wheel/button zoom requests below 50% must stop at the embedded minimum.");
                    CheckPanLimits(fittedBounds);
                    Assert(surface.TransformToAncestor(viewport).TransformBounds(new Rect(surface.RenderSize)) == fittedBounds,
                        "At minimum scale, dragging must not move the board at all.");
                    ((Button)board.FindName("FitButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                    // Exercise the same drag transition as the mouse handler, then shrink the viewport.
                    RenderContractWindow(window, new Size(1440, 900), Path.Combine(runRoot, $"before-fit-drag-{language}.png"));
                    var beforeShrink = markerScale.ScaleX;
                    typeof(QuestBoardPreview).GetMethod("Viewport_RightDown", flags)!.Invoke(board,
                        [viewport, new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0,
                            System.Windows.Input.MouseButton.Right)]);
                    typeof(QuestBoardPreview).GetMethod("PanMap", flags)!.Invoke(board, [new Vector(80, 60)]);
                    typeof(QuestBoardPreview).GetMethod("EndPan", flags)!.Invoke(board, null);
                    RenderContractWindow(window, new Size(1120, 720), Path.Combine(runRoot, $"after-fit-drag-shrink-{language}.png"));
                    var resizedBounds = QuestBoardContentBounds(board, viewport);
                    Assert(markerScale.ScaleX == beforeShrink && markerScale.ScaleX == 0.5 &&
                           !((Button)board.FindName("ZoomOutButton")).IsEnabled &&
                           Math.Abs(resizedBounds.Left - (viewport.ActualWidth - resizedBounds.Right)) < 0.001 &&
                           Math.Abs(resizedBounds.Top - (viewport.ActualHeight - resizedBounds.Bottom)) < 0.001,
                        "Shrinking the embedded viewport after an ineffective drag must recenter the content at the same 50% scale.");

                    var initialCount = board.Samples.Count;
                    Exception? callbackFailure = null;
                    void Picker(MenuItem action, QuestBoardCategory category, int level, string search, string screenshot)
                    {
                        window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                        {
                            var dialog = Application.Current.Windows.OfType<QuestBoardPreviewDialog>().Single();
                            try
                            {
                                var grid = (DataGrid)dialog.FindName("ChoiceGrid");
                                var difficulty = (ComboBox)dialog.FindName("DifficultyChoice");
                                var filter = (TextBox)dialog.FindName("SearchTextBox");
                                var confirm = (Button)dialog.FindName("ConfirmButton");
                                Assert(dialog.FindName("LengthChoice") is null,
                                    "The picker must not offer an independent length override for fixed quest choices.");
                                grid.SelectedIndex = 0;
                                filter.Text = "no-such-quest";
                                Assert(grid.Items.Count == 0 && grid.SelectedItem is null && !confirm.IsEnabled,
                                    "An empty search must clear selection and disable confirmation.");
                                filter.Text = string.Empty;
                                difficulty.SelectedValue = level;
                                Assert(grid.Items.Cast<QuestBoardPreviewDialog.Row>().All(row =>
                                       row.Choice.Difficulty == level && row.Choice.Category == category),
                                    "The category and difficulty must filter complete choices, not mutate a selected quest.");
                                foreach (var row in grid.Items.Cast<QuestBoardPreviewDialog.Row>())
                                {
                                    if (gameDirectory is null) continue;
                                    var typeImage = ((Grid)row.Icon).Children.OfType<Image>().Last();
                                    var bitmap = (System.Windows.Media.Imaging.BitmapImage)typeImage.Source;
                                    var expectedIcon = row.Choice.Icon.StartsWith("plot_", StringComparison.Ordinal)
                                        ? $"quest_select_{row.Choice.Icon}.png" : $"quest_select_{row.Choice.Icon}_{level}.png";
                                    Assert(bitmap.UriSource.LocalPath.EndsWith(expectedIcon, StringComparison.OrdinalIgnoreCase),
                                        "Each filtered row must show the native icon for that exact quest and difficulty.");
                                }
                                filter.Text = search;
                                Assert(grid.Items.Count > 0 && grid.SelectedItem is null && !confirm.IsEnabled,
                                    "A changed filter must require a fresh selection.");
                                grid.SelectedIndex = 0;
                                var selected = ((QuestBoardPreviewDialog.Row)grid.SelectedItem!).Choice;
                                RenderContractWindow(dialog, new Size(1180, 720), Path.Combine(runRoot, screenshot));
                                ((Button)dialog.FindName("ConfirmButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                                Assert(dialog.SelectedChoice == selected,
                                    "Confirmation must return the exact visible choice, including its difficulty and length.");
                            }
                            catch (Exception ex) { callbackFailure = ex; dialog.Close(); }
                        }));
                        action.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                        if (callbackFailure is not null) throw callbackFailure;
                    }
                    var create = board.CreateTaskMenu("crypts", null);
                    Assert(create.Items.Count == 1 && ((MenuItem)create.Items[0]).Header as string == EditorText.Get("QuestBoard_Add"),
                        "Empty-region context must offer only New.");
                    Assert(!QuestCategoryItem(create, 0, QuestBoardCategory.Wave).IsEnabled,
                        "A category with no examples in this region must be unavailable.");
                    Picker(QuestCategoryItem(create, 0, QuestBoardCategory.Cleanse), QuestBoardCategory.Cleanse, 3,
                        "generated_cleanse_3_2", $"picker-create-{language}.png");
                    Assert(board.Samples.Count == initialCount + 1 && board.Samples.Last().Choice.Kind == "cleanse" &&
                           board.Samples.Last().Choice.Difficulty == 3,
                        "The actual create menu and picker must add only one selected in-memory sample.");
                    Assert(activityLog.Text.Contains(EditorText.Format("QuestBoard_Added", board.Samples.Last().Choice.Name), StringComparison.Ordinal),
                        "Create feedback must reach the shared main activity log.");
                    var newKey = board.SelectedKey!.Value;
                    var replace = board.CreateTaskMenu("crypts", newKey);
                    Assert(replace.Items.Count == 2 && ((MenuItem)replace.Items[0]).Header as string == EditorText.Get("QuestBoard_Replace") &&
                           ((MenuItem)replace.Items[1]).Header as string == EditorText.Get("QuestBoard_Delete"),
                        "Existing-quest context must offer only Replace and Delete, never New.");
                    Picker(QuestCategoryItem(replace, 0, QuestBoardCategory.KillBoss), QuestBoardCategory.KillBoss, 5,
                        "", $"picker-replace-{language}.png");
                    Assert(board.Samples.Count == initialCount + 1 && board.SelectedKey != newKey &&
                           board.Samples.All(x => x.Key != newKey), "Replacement must replace the selected sample, not append another quest.");
                    Assert(activityLog.Text.Contains(EditorText.Format("QuestBoard_Replaced", board.Samples.Single(x => x.Key == board.SelectedKey).Choice.Name), StringComparison.Ordinal),
                        "Replacement feedback must use the same activity log.");
                    var replaced = board.SelectedKey!.Value;
                    // Exercise the actual right-click targets, not just the menu factory.
                    var questButton = mapCanvas.Children.OfType<Canvas>().SelectMany(panel => panel.Children.OfType<Button>())
                        .Single(button => Equals(button.Tag, replaced));
                    questButton.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0,
                        System.Windows.Input.MouseButton.Right) { RoutedEvent = UIElement.MouseRightButtonUpEvent });
                    var openMenu = (ContextMenu)typeof(QuestBoardPreview).GetField("_menu", flags)!.GetValue(board)!;
                    Assert(openMenu.Items.Count == 2 && board.SelectedKey == replaced &&
                           ((MenuItem)openMenu.Items[0]).Header as string == EditorText.Get("QuestBoard_Replace"),
                        "Right-clicking a quest must select it and open its edit menu without falling through to the region's New menu.");
                    openMenu.IsOpen = false;
                    var emptyRegion = mapCanvas.Children.OfType<Canvas>().Single(panel => (string)panel.Tag == "crypts");
                    emptyRegion.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0,
                        System.Windows.Input.MouseButton.Right) { RoutedEvent = UIElement.MouseRightButtonUpEvent });
                    openMenu = (ContextMenu)typeof(QuestBoardPreview).GetField("_menu", flags)!.GetValue(board)!;
                    Assert(openMenu.Items.Count == 1 && ((MenuItem)openMenu.Items[0]).Header as string == EditorText.Get("QuestBoard_Add"),
                        "Right-clicking a region's blank area must open only New, regardless of the currently selected quest.");
                    openMenu.IsOpen = false;
                    var delete = board.CreateTaskMenu("crypts", replaced);
                    ((MenuItem)delete.Items[1]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    Assert(board.Samples.Count == initialCount && board.Samples.All(x => x.Key != replaced),
                        "The delete menu must remove its selected sample.");
                    Assert(activityLog.Text.Contains(EditorText.Format("QuestBoard_Deleted", QuestBoardPreviewData.Choices("crypts")
                               .First(x => x.Kind == "kill_boss").Name), StringComparison.Ordinal),
                        "Deletion feedback must reach the shared activity log.");
                    replaced = board.SelectedKey!.Value;
                    Picker(QuestCategoryItem(board.CreateTaskMenu("weald", null), 0, QuestBoardCategory.KillBoss),
                        QuestBoardCategory.KillBoss, 3, "plot_trinket_retention_1", $"picker-recovery-{language}.png");
                    var special = board.Samples.Single(sample => sample.Key == board.SelectedKey);
                    Assert(special.Choice.PlotId == "plot_trinket_retention_1" && special.Choice.Difficulty == 3,
                        "The special picker must preserve the fixed quest ID matching its level-specific icon.");
                    board.RemoveSample(special.Key);
                    foreach (var (category, id) in new[]
                    {
                        (QuestBoardCategory.InventoryActivate, "plot_darkest_dungeon_2"),
                        (QuestBoardCategory.Activate, "plot_darkest_dungeon_3")
                    })
                    {
                        Picker(QuestCategoryItem(board.CreateTaskMenu("darkestdungeon", null), 0, category),
                            category, 6, id, $"picker-{id}-{language}.png");
                        var plotSample = board.Samples.Single(sample => sample.Key == board.SelectedKey);
                        Assert(plotSample.Choice.PlotId == id && !plotSample.Choice.IsGenerated,
                            "Fixed Darkest Dungeon interaction quests must keep their native type and plot identity.");
                        board.RemoveSample(plotSample.Key);
                    }

                    // The recovery sample deliberately presents several trinkets to exercise the compact reward layout.
                    var recovery = board.Samples.Single(x => x.Choice.Kind == "recovery");
                    typeof(QuestBoardPreview).GetMethod("SelectSample", flags)!.Invoke(board, [recovery.Key]);
                    RenderContractWindow(window, new Size(1120, 720), Path.Combine(runRoot, $"trinkets-{language}-small.png"));
                    var rewardPanel = (WrapPanel)board.FindName("RewardPanel");
                    var inventoryScroll = (ScrollViewer)board.FindName("RewardInventoryScroll");
                    Assert(rewardPanel.Children.Count == 6 && rewardPanel.Children.OfType<Canvas>().Count(x =>
                               ((string)x.Tag).Contains("/trinket/", StringComparison.Ordinal)) == 4 &&
                           detailCanvas.TransformToAncestor(rewards).TransformBounds(new Rect(detailCanvas.RenderSize)).Width <= rewards.ViewportWidth &&
                           rewards.ScrollableWidth == 0,
                        "Native reward slots must wrap within the detail panel without horizontal overflow.");
                    if (gameDirectory is not null)
                    {
                        Assert(rewardPanel.Children.OfType<Canvas>().SelectMany(x => x.Children.OfType<Image>()).All(x => x.Source is not null),
                            "Native currency icons, trinkets, and rarity frames must resolve from installed artwork.");
                        var detailImage = (Image)board.FindName("DetailBackdrop");
                        var nativeArt = (System.Windows.Media.Imaging.BitmapImage)detailImage.Source;
                        Assert(nativeArt.UriSource.LocalPath.EndsWith("quest_select.questverbose_bg.png", StringComparison.OrdinalIgnoreCase) &&
                               nativeArt.PixelWidth == 400 && nativeArt.PixelHeight == 843 &&
                               detailImage.Width == 400 && detailImage.Height == 843 && detailImage.Clip is null,
                            "The compact details must retain the native artwork's original proportions and coordinate system.");
                        Assert(Canvas.GetTop((TextBlock)board.FindName("QuestDescription")) == 194 &&
                               Canvas.GetTop((TextBlock)board.FindName("RewardHeading")) == 532,
                            "Native artwork and detail text must share the same original coordinate system.");
                    }
                    var scaleBeforeDetailsWheel = markerScale.ScaleX;
                    rewardPanel.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, -120)
                        { RoutedEvent = System.Windows.Input.Mouse.PreviewMouseWheelEvent });
                    Assert(markerScale.ScaleX == scaleBeforeDetailsWheel,
                        "Wheel input in the independent detail panel must not zoom the map.");
                    inventoryScroll.ScrollToEnd();
                    FlushLanguageBindings();
                    Assert(inventoryScroll.VerticalOffset > 0,
                        "Additional reward rows must be reachable by vertical scrolling.");
                    Assert(rewards.ScrollableHeight > 0, "A short window must allow scrolling the full native detail panel.");
                    rewards.ScrollToEnd();
                    FlushLanguageBindings();
                    Assert(rewards.VerticalOffset > 0, "The complete detail panel must remain reachable independently of map zoom.");
                    RenderContractWindow(window, new Size(1120, 720), Path.Combine(runRoot, $"trinkets-{language}-scrolled.png"));
                    AssertQuestBoardFrameVisible(boardBody);
                    rewards.ScrollToTop();
                    RenderContractWindow(window, new Size(1440, 900), Path.Combine(runRoot, $"trinkets-{language}-wide.png"));
                    typeof(QuestBoardPreview).GetMethod("SelectSample", flags)!.Invoke(board, [replaced]);

                    var menu = board.CreateTaskMenu("crypts", replaced);
                    menu.PlacementTarget = board;
                    menu.IsOpen = true;
                    FlushLanguageBindings();
                    RenderContractElement(menu, new Size(240, 125), Path.Combine(runRoot, $"context-menu-{language}.png"));
                    menu.IsOpen = false;

                    ((Button)board.FindName("FitButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    FlushLanguageBindings();
                    var beforeManualBounds = surface.TransformToAncestor(viewport).TransformBounds(new Rect(surface.RenderSize));
                    ((Button)board.FindName("ResetZoomButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    mapOffset.X -= 30;
                    mapOffset.Y -= 20;
                    typeof(QuestBoardPreview).GetMethod("ClampMapOffset", flags)!.Invoke(board, null);
                    var manualScale = markerScale.ScaleX;
                    var manualOffset = new Point(mapOffset.X, mapOffset.Y);
                    window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                    {
                        var expanded = Application.Current.Windows.OfType<Window>().Single(x => x != window);
                        try
                        {
                            Assert(board.Samples.Count == initialCount && board.SelectedKey == replaced,
                                "Expanded view must share the same sample state and selection.");
                            RenderContractWindow(expanded, new Size(expanded.Width, expanded.Height), Path.Combine(runRoot, $"expanded-{language}.png"));
                            Assert(((Canvas)board.FindName("EstateMapHeader")).IsVisible && Canvas.GetTop(detailCanvas) == 0 &&
                                   (gameDirectory is null || ((Image)board.FindName("EstateMapIcon")).Source is not null),
                                "Full map must restore the native estate heading and icon above the quest details.");
                            AssertQuestBoardFrameVisible(boardBody);
                            Assert(!((Button)board.FindName("ZoomOutButton")).IsEnabled,
                                "Opening the full view must recompute the fit minimum for its new viewport.");
                            var expandedFit = surface.TransformToAncestor(viewport).TransformBounds(new Rect(surface.RenderSize));
                            Assert(Math.Abs(expandedFit.Top - (viewport.ActualHeight - expandedFit.Bottom)) < 0.001 &&
                                   Math.Abs(expandedFit.Left - (viewport.ActualWidth - expandedFit.Right)) < 0.001,
                                "The full-map window must also center the complete background in both axes.");
                            Assert(Math.Abs(expandedFit.Top) < 0.01 && Math.Abs(expandedFit.Left) < 0.01 &&
                                   Math.Abs(viewport.ActualWidth - expandedFit.Right) < 0.01 &&
                                   Math.Abs(viewport.ActualHeight - expandedFit.Bottom) < 0.01,
                                "The opening full-map window must fit the complete native image against all four viewport edges.");
                            Assert(rewards.ScrollableHeight == 0,
                                "Full map must show the restored estate heading and ordinary reward row without scrolling.");
                            Console.WriteLine($"Full map {language}: window={expanded.Width:F2}x{expanded.Height:F2}; viewport={viewport.ActualWidth:F2}x{viewport.ActualHeight:F2}; all four gaps=0 DIP.");
                            ((Button)board.FindName("ZoomInButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            CheckPanLimits(expandedFit);
                            ((Button)board.FindName("ExpandButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        }
                        catch (Exception ex) { callbackFailure = ex; expanded.Close(); }
                    }));
                    ((Button)board.FindName("ExpandButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    if (callbackFailure is not null) throw callbackFailure;
                    FlushLanguageBindings();
                    Assert(board.Content is not null && board.SelectedKey == replaced, "Closing expanded view must restore the embedded board.");
                    Assert(((Canvas)board.FindName("EstateMapHeader")).Visibility == Visibility.Collapsed &&
                           Canvas.GetTop(detailCanvas) == -140,
                        "Returning from full map must restore the compact embedded details without losing the selected quest.");
                    Assert(markerScale.ScaleX == manualScale && new Point(mapOffset.X, mapOffset.Y) == manualOffset,
                        "Returning from full map must also preserve a manually zoomed and panned embedded view.");
                    CheckPanLimits(beforeManualBounds);

                    var other = language == "en" ? "zh-CN" : "en";
                    var languageButton = (Button)window.FindName("LanguageButton");
                    languageButton.ContextMenu.Items.OfType<MenuItem>().Single(x => x.Tag as string == other)
                        .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    FlushLanguageBindings();
                    Assert(board.SelectedKey == replaced && board.Samples.Count == initialCount &&
                           ((TextBlock)board.FindName("QuestName")).Text == board.Samples.Single(x => x.Key == replaced).Choice.Name,
                        "Live language switching must retain the sample edits and selection while updating details.");
                    Assert(((Button)board.FindName("ExpandButton")).Content as string == EditorText.Get("QuestBoard_Expand"),
                        "Expand/return must retain the button's live localization binding.");

                    typeof(MainWindow).GetField("_quantitySaveContext", flags)!.SetValue(window, QuantityItemSaveContext.Raid);
                    Refresh();
                    Assert(!board.IsVisible && battle.IsVisible, "Raid context must restore the original battle panel.");
                    typeof(MainWindow).GetField("_quantitySaveContext", flags)!.SetValue(window, QuantityItemSaveContext.Town);
                    Refresh();
                    Assert(board.IsVisible && !battle.IsVisible && board.SelectedKey == replaced,
                        "Returning to town must restore the preview without replacing its in-memory samples.");
                    gameRunning = true;
                    typeof(MainWindow).GetMethod("CheckGameProcess", flags)!.Invoke(window, null);
                    Assert(!board.IsEnabled, "Running-game state must disable preview interaction consistently with the editor.");
                    gameRunning = false;
                    typeof(MainWindow).GetMethod("CheckGameProcess", flags)!.Invoke(window, null);
                    Assert(!board.IsEnabled, "Game exit alone must not unlock the preview while synchronization is pending.");
                    SetState("_syncReady", true);
                    SetState("_syncInvalidated", false);
                    Refresh();
                    Assert(board.IsEnabled, "The preview must unlock after synchronization completes.");
                    SetState("_busyDepth", 1);
                    Refresh();
                    Assert(!board.IsEnabled, "Shared busy state must disable the board.");
                    SetState("_busyDepth", 0);
                    Refresh();
                    var beforeLock = board.Samples.Count;
                    window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                    {
                        var dialog = Application.Current.Windows.OfType<QuestBoardPreviewDialog>().Single();
                        try
                        {
                            gameRunning = true;
                            typeof(MainWindow).GetMethod("CheckGameProcess", flags)!.Invoke(window, null);
                            Assert(!dialog.IsVisible && !board.IsEnabled,
                                "Game startup while picking must dismiss the modal and disable the board.");
                        }
                        catch (Exception ex) { callbackFailure = ex; dialog.Close(); }
                    }));
                    QuestCategoryItem(board.CreateTaskMenu("crypts", null), 0, QuestBoardCategory.Cleanse)
                        .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    if (callbackFailure is not null) throw callbackFailure;
                    Assert(board.Samples.Count == beforeLock && board.SelectedKey == replaced,
                        "A picker dismissed by the game lock must not apply its pending sample choice.");
                    Assert(!Directory.EnumerateFiles(runRoot, "persist*.json", SearchOption.AllDirectories).Any(),
                        "UI-only operations must not create save files.");
                }
                finally { window.Close(); }
            }
            return Task.CompletedTask;
        });
        Console.WriteLine("PASS: Independent native details, 50% content-centered crop, visible region titles, balanced content margins, whole-image full view, bounded zoom/pan, profile gates and language.");
        Console.WriteLine($"Artifacts: {runRoot}");
    }

    private static MenuItem QuestCategoryItem(ContextMenu menu, int root, QuestBoardCategory category) =>
        ((MenuItem)menu.Items[root]).Items.OfType<MenuItem>().Single(item => Equals(item.Tag, category));

    private static Rect QuestBoardContentBounds(QuestBoardPreview board, Visual relativeTo)
    {
        var bounds = Rect.Empty;
        foreach (var child in ((Canvas)board.FindName("MapCanvas")).Children.OfType<Canvas>()
                     .SelectMany(panel => panel.Children.OfType<FrameworkElement>()))
            bounds.Union(child.TransformToAncestor(relativeTo).TransformBounds(new Rect(child.RenderSize)));
        return bounds;
    }

    private static void AssertQuestBoardFrameVisible(Grid body)
    {
        body.UpdateLayout();
        var root = (FrameworkElement)Window.GetWindow(body)!.Content;
        var bounds = body.TransformToAncestor(root).TransformBounds(new Rect(body.RenderSize));
        // Render at twice the logical resolution so a one-DIP line contains solid pixels
        // even when the window's DPI produces a fractional logical height. Use the window
        // root so the nested board's layout offset is included in the sampled coordinates.
        var width = (int)Math.Ceiling(root.ActualWidth * 2);
        var height = (int)Math.Ceiling(root.ActualHeight * 2);
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        bool IsBorderPixel(int x, int y)
        {
            var p = (y * width + x) * 4;
            return Math.Abs(pixels[p] - 0x4F) <= 2 && Math.Abs(pixels[p + 1] - 0x51) <= 2 &&
                   Math.Abs(pixels[p + 2] - 0x4A) <= 2 && pixels[p + 3] >= 253;
        }
        foreach (var fraction in new[] { 0.05, 0.3, 0.6, 0.95 })
        {
            var x = (int)((bounds.Left + bounds.Width * fraction) * 2);
            var top = (int)Math.Floor(bounds.Top * 2);
            var bottom = (int)Math.Ceiling(bounds.Bottom * 2);
            Assert(Enumerable.Range(top, 4).Any(y => IsBorderPixel(x, y)) &&
                   Enumerable.Range(bottom - 4, 4).Any(y => IsBorderPixel(x, y)),
                "The top and bottom frame lines must remain visible over both panes after fitting, zooming, panning or scrolling.");
        }
    }

    private static void ValidateQuestBoardFixtures(string gameDirectory)
    {
        var plotFiles = new[]
        {
            "campaign/quest/quest.plot_quests.json",
            "dlc/580100_crimson_court/features/crimson_court/campaign/quest/crimson_court.quest.plot_quests.json",
            "dlc/735730_color_of_madness/campaign/quest/com.quest.plot_quests.json"
        };
        var plots = plotFiles.SelectMany(relative => JsonNode.Parse(File.ReadAllText(Path.Combine(gameDirectory, relative)))!["plot_quests"]!
                .AsArray().Select(node => node!.AsObject()))
            .ToDictionary(node => node["id"]!.GetValue<string>(), StringComparer.Ordinal);
        var generation = JsonNode.Parse(File.ReadAllText(Path.Combine(gameDirectory, "campaign/quest/quest.generation.json")))!["generation"]!;
        var levels = generation["difficulty"]!["generated_resolve_level_difficulties"]!.AsArray()
            .Select(node => node!["difficulty"]!.GetValue<int>()).ToHashSet();
        foreach (var region in QuestBoardPreviewData.Regions)
        {
            var choices = QuestBoardPreviewData.Choices(region.Id);
            Assert(QuestBoardPreviewData.Samples().Where(sample => sample.RegionId == region.Id).All(sample => choices.Contains(sample.Choice)),
                "Initial examples must use choices available in their own region.");
            foreach (var choice in choices)
            {
                if (choice.PlotId is { } id)
                {
                    var definition = plots[id];
                    var quest = definition["quest"]!;
                    Assert(quest["dungeon"]!.GetValue<string>() == region.Id && quest["difficulty"]!.GetValue<int>() == choice.Difficulty &&
                           quest["length"]!.GetValue<int>() == choice.Length &&
                           quest["type"]!.GetValue<string>() == choice.NativeType && !choice.IsGenerated,
                        $"Plot fixture {id} must preserve the native region, difficulty, length and quest type independently of progression flags.");
                }
                else
                {
                    var table = generation["type"]!["available_quests_table"]!.AsArray()
                        .Single(node => node!["dungeon"]!.GetValue<string>() == region.Id)!["generated_quest_table"]!.AsArray();
                    Assert(levels.Contains(choice.Difficulty) && table.SelectMany(step => step!.AsArray()).Any(node =>
                               node!["type"]!.GetValue<string>() == choice.Kind && node["length"]!.GetValue<int>() == choice.Length),
                        $"Generated fixture {choice.Id} must use a native type/length/level combination.");
                }
            }
        }
    }
}
