using System.Reflection;
using System.Text.Json.Nodes;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DarkestDungeonSaveEditor.App;

internal static partial class ContractSuite
{
    public static async Task RunQuestBoardLayoutAsync(string repositoryRoot, string gameDirectory, string compactMod)
    {
        var root = Path.Combine(repositoryRoot, "artifacts", "quest-board-layout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var (_, document) = RunQuestBoardReaderContracts(root);
        var template = (JsonObject)document["base_root"]!["quests"]!["0"]!.DeepClone();
        var profile = new SaveProfile("profile_0", Path.Combine(root, "profile_0"), "", "layout test", DateTime.UtcNow);
        var cases = new List<(QuestBoardSnapshot Snapshot, bool Compact)>();
        foreach (var compact in new[] { false, true })
        {
            var quests = new JsonObject();
            // Presentation-only dense rows, using native saved quest fields.
            // Native frame padding overlaps other tasks in this arrangement.
            // No game profile is written, and this does not test quest generation.
            foreach (var region in new[] { "warrens", "weald", "crypts", "cove", "town", "darkestdungeon" })
                for (var i = 0; i < (region is "warrens" or "weald" ? 8 : 2); i++)
                {
                    var entry = (JsonObject)template.DeepClone();
                    entry["id"] = "layout_preview_" + quests.Count;
                    entry["dungeon"] = region;
                    quests[quests.Count.ToString()] = entry;
                }
            document["base_root"]!["quests"] = quests;
            var original = document.ToJsonString();
            var sources = new List<ActiveContentSource> { new("base", "Base", "base", gameDirectory, 0) };
            if (compact) sources.Add(new("workshop:1689631977", "Compact Quest Screen", "workshop", compactMod, 1));
            var content = new ActiveContentSnapshot(profile, "base", sources, [], root, "", 1, "layout-test");
            var snapshot = QuestBoardSnapshotReader.Read(content, document, "read-only-layout-test");
            var layout = snapshot.Layout;
            Assert(layout.QuestStart == (compact ? new QuestBoardPoint(84, 80) : new(160, 92)) &&
                   layout.QuestSpacing == (compact ? new QuestBoardPoint(56, 60) : new(76, 80)) &&
                   layout.ProgressSize.X == (compact ? 109 : 194) &&
                   layout.LevelOffset.X == (compact ? 137 : 219) &&
                   layout.SelectionOffset.X == (compact ? -48 : -94),
                "Effective shared layout must come from the registered Mod or the native file.");
            var warrens = snapshot.Regions.Single(item => item.Id == "warrens");
            Assert(warrens.QuestsPerRow == (compact ? 2 : 4), "The effective region controls the number of columns.");
            // Verified native render: start.x - columns * spacing.x / 2 + col * spacing.x.
            Assert(layout.QuestCenter(0, warrens.QuestsPerRow) == (compact ? new QuestBoardPoint(28, 80) : new(8, 92)) &&
                   layout.QuestCenter(warrens.QuestsPerRow, warrens.QuestsPerRow).Y == (compact ? 140 : 172),
                "Task centers must follow the native row origin, not a guessed top-left grid.");
            cases.Add((snapshot, compact));
            Assert(document.ToJsonString() == original, "Layout inspection must not mutate the quest document.");
        }
        await RunWpfContractsAsync(() =>
        {
            foreach (var (snapshot, compact) in cases)
                foreach (var language in new[] { "en", "zh-CN" })
                {
                    EditorText.Initialize(language, EditorText.SystemCulture);
                    CheckLayoutUi(root, snapshot, compact, language);
                }
            return Task.CompletedTask;
        });
        Console.WriteLine("PASS: Native/Compact layout, image layers, point hit tests, contextual menus and relative zoom.");
        Console.WriteLine("Artifacts: " + root);
    }

    private static void CheckLayoutUi(string root, QuestBoardSnapshot snapshot, bool compact, string language)
    {
        var label = (compact ? "compact" : "native") + "-" + language;
        var window = new MainWindow(Path.Combine(root, label + "-language.json"), gameRunningProbe: () => false)
        { Left = -20000, Top = -20000, ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual };
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        try
        {
            window.Show();
            ((TabControl)window.FindName("CatalogTabs")).SelectedIndex = 4;
            typeof(MainWindow).GetField("_catalogProfileDirectory", flags)!.SetValue(window, snapshot.ProfileDirectory);
            typeof(MainWindow).GetField("_syncReady", flags)!.SetValue(window, true);
            typeof(MainWindow).GetMethod("UpdateEnabledState", flags)!.Invoke(window, null);
            var board = (QuestBoardPreview)window.FindName("QuestBoardPanel");
            board.SetSnapshot(snapshot);
            board.SetEditCatalog(new(snapshot.Quests.Select(quest => new QuestBoardTemplate(quest.SaveKey, quest, "")).ToArray(),
                snapshot.ImagePaths, []) { Regions = snapshot.Regions });
            RenderContractWindow(window, new Size(1460, 920), Path.Combine(root, label + ".png"));
            var canvas = (Canvas)board.FindName("MapCanvas");
            var viewport = (Border)board.FindName("MapViewport");
            var zoom = (ScaleTransform)board.FindName("MapScale");
            var offset = (TranslateTransform)board.FindName("MapOffset");
            IEnumerable<Button> Buttons() => canvas.Children.OfType<Canvas>().SelectMany(panel => panel.Children.OfType<Button>())
                .Where(button => button.Tag is Guid);
            Button? Hit(Point point)
            {
                var hit = viewport.InputHitTest(point) as DependencyObject;
                while (hit is not null && hit is not Button) hit = VisualTreeHelper.GetParent(hit);
                return hit as Button;
            }
            void CheckHits()
            {
                var checkedCount = 0;
                foreach (var button in Buttons())
                    foreach (var fraction in new[] { new Point(0.5, 0.5), new Point(0.35, 0.35), new Point(0.65, 0.65) })
                    {
                        var point = button.TranslatePoint(new Point(button.ActualWidth * fraction.X, button.ActualHeight * fraction.Y), viewport);
                        if (!new Rect(viewport.RenderSize).Contains(point)) continue;
                        var hit = Hit(point);
                        Assert(ReferenceEquals(hit, button), $"{label}: visible quest {button.Tag} in {((FrameworkElement)button.Parent).Tag} at {point}, fraction {fraction}, hit {hit?.Tag} ({hit?.ToolTip}); raw {viewport.InputHitTest(point)?.GetType().Name}.");
                        checkedCount++;
                    }
                Assert(checkedCount >= 3, "Point hit tests must exercise visible task graphics.");
                if (!compact)
                {
                    var firstCrypts = canvas.Children.OfType<Canvas>().Single(panel => Equals(panel.Tag, "crypts"))
                        .Children.OfType<Button>().First(button => button.Tag is Guid);
                    // The installed frame has opaque pixels at x76/77, while
                    // its next neighbor begins at76 with a transparent edge.
                    var edge = firstCrypts.TranslatePoint(new Point(76.5, 48.5), viewport);
                    if (new Rect(viewport.RenderSize).Contains(edge))
                        Assert(ReferenceEquals(Hit(edge), firstCrypts),
                            "Transparent padding must not steal an opaque edge of the previous task.");
                }
            }
            var marker = Buttons().Single(button => Equals(button.Tag, board.SelectedKey));
            var layers = ((Grid)marker.Content).Children.OfType<Grid>().Single().Children.OfType<Image>().ToArray();
            Assert(layers[0].Width == (compact ? 64 : 96) && layers[1].Width == (compact ? 32 : 48),
                "The length frame and inner type icon must retain their separate native pixel sizes.");
            var blood = ((Grid)marker.Content).Children.OfType<Canvas>().Single().Children.OfType<Image>().Single();
            Assert(blood.Width == (compact ? 96 : 192) && Canvas.GetLeft(blood) == (compact ? -16 : -46),
                "Selected artwork must use the active texture and configured offset from the icon center.");
            Assert(canvas.Children.OfType<Canvas>().All(panel => panel.Background is null),
                "Region layout containers must not claim background input.");
            Assert(((TextBlock)board.FindName("ZoomText")).Text == "100%" && !((Button)board.FindName("ZoomOutButton")).IsEnabled,
                "The embedded default/minimum view must be reported as 100%.");
            var topRegion = canvas.Children.OfType<Canvas>().Single(panel => Equals(panel.Tag, "darkestdungeon"));
            var topHeader = topRegion.Children.OfType<Button>().Single(button => button.Tag is not Guid);
            Assert(topHeader.TranslatePoint(new Point(), viewport).Y >= 0, "The top region heading must not use the old vanilla crop center.");
            var title = (TextBlock)topHeader.Content;
            var naturalTitle = new TextBlock { Text = title.Text, FontFamily = title.FontFamily,
                FontSize = title.FontSize, FontWeight = title.FontWeight };
            naturalTitle.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Assert(title.ActualWidth >= naturalTitle.DesiredSize.Width - 0.01,
                $"A name anchor must not truncate a longer English region label: declared {title.Width}, actual {title.ActualWidth}, measured {naturalTitle.DesiredSize.Width}.");
            CheckHits();
            void RightClick(UIElement element) => element.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right)
                { RoutedEvent = UIElement.MouseRightButtonUpEvent });
            ContextMenu Menu() => (ContextMenu)typeof(QuestBoardPreview).GetField("_menu", flags)!.GetValue(board)!;
            RightClick(marker);
            Assert(Menu().Items.Count == 2 && Equals(((MenuItem)Menu().Items[0]).Header, EditorText.Get("QuestBoard_Replace")),
                "Task right-click must not fall through to either creation menu.");
            Menu().IsOpen = false;
            var heading = canvas.Children.OfType<Canvas>().Single(panel => Equals(panel.Tag, "warrens"))
                .Children.OfType<Button>().Single(button => button.Tag is not Guid);
            RightClick(heading);
            Assert(Menu().Items.Count == 1 && ((MenuItem)Menu().Items[0]).Items.OfType<MenuItem>().All(item => item.Tag is QuestBoardCategory),
                "A region heading must open its categories, without the global region level.");
            Menu().IsOpen = false;
            RightClick(viewport);
            Assert(((MenuItem)Menu().Items[0]).Items.OfType<MenuItem>().Any(item => Equals(item.Header, snapshot.Regions.Single(r => r.Id == "weald").Name)),
                "Background right-click must allow an explicit choice of region.");
            Menu().IsOpen = false;
            ((Button)board.FindName("ZoomInButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            FlushLanguageBindings();
            Assert(((TextBlock)board.FindName("ZoomText")).Text == "115%", "Zoom must be relative to the default view.");
            offset.X -= 20;
            offset.Y -= 20;
            typeof(QuestBoardPreview).GetMethod("ClampMapOffset", flags)!.Invoke(board, null);
            FlushLanguageBindings();
            CheckHits();
            typeof(QuestBoardPreview).GetMethod("Viewport_RightDown", flags)!.Invoke(board,
                [viewport, new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right)]);
            typeof(QuestBoardPreview).GetMethod("PanMap", flags)!.Invoke(board, [new Vector(80, 60)]);
            var release = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right)
                { RoutedEvent = UIElement.PreviewMouseRightButtonUpEvent };
            viewport.RaiseEvent(release);
            Assert(release.Handled, "Releasing a right-button drag must suppress all context menus.");
            release.RoutedEvent = UIElement.MouseRightButtonUpEvent;
            viewport.RaiseEvent(release);
            Assert(typeof(QuestBoardPreview).GetField("_menu", flags)!.GetValue(board) is not ContextMenu { IsOpen: true },
                "The global menu must stay closed after panning.");
            ((Button)board.FindName("ResetZoomButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(zoom.ScaleX == 0.5 && ((TextBlock)board.FindName("ZoomText")).Text == "100%", "Reset must restore the half-native-size default.");
            Exception? expansionFailure = null;
            window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                var expanded = Application.Current.Windows.OfType<Window>().Single(item => item != window);
                try
                {
                    RenderContractWindow(expanded, new Size(expanded.ActualWidth, expanded.ActualHeight), Path.Combine(root, label + "-full.png"));
                    Assert(((TextBlock)board.FindName("ZoomText")).Text == "100%", "Full map uses its own whole-image baseline.");
                    CheckHits();
                }
                catch (Exception error) { expansionFailure = error; }
                finally { expanded.Close(); }
            }));
            ((Button)board.FindName("ExpandButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (expansionFailure is not null) throw expansionFailure;
            FlushLanguageBindings();
            Assert(zoom.ScaleX == 0.5 && ((TextBlock)board.FindName("ZoomText")).Text == "100%", "Closing full map must restore embedded zoom.");
            CheckHits();
        }
        finally { window.Close(); }
    }
}
