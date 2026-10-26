using System.Reflection;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using DarkestDungeonSaveEditor.App;

internal static partial class ContractSuite
{
    private static void VerifyTownEventRequirementUi(MainWindow window, string runRoot)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        VerifyCatalogTabEdges();
        // UI fixtures retain the real invasion/revival trigger thresholds.
        var invasion = new TownEventDefinition("plot_quest_town_invasion_0", [], "base", "", false)
        {
            LocalizedName = new("土匪袭击", "Brigand Incursion"),
            // Deliberately long UI text, not a new event rule or an actual Mod definition.
            LocalizedDescription = new(string.Concat(Enumerable.Repeat("用于验证事件详情自然换行的长段说明。", 12)),
                string.Concat(Enumerable.Repeat("Long event description used to verify wrapping within the visible catalog. ", 10))),
            TriggerRules = new() { MinimumWeek = 40, HeroLevelCounts = [new(5, 4)], CooldownEvents = 4 }
        };
        var revival = new TownEventDefinition("dead_recruit", [], "base", "", false)
        {
            LocalizedName = new("死者复生", "From Beyond"),
            TriggerRules = new() { MinimumWeek = 15, DeadHeroes = 3, CooldownEvents = 3 }
        };
        ((TextBox)window.FindName("SearchTextBox")).Text = string.Empty;
        typeof(MainWindow).GetField("_allEvents", flags)!.SetValue(window, new[] { invasion, revival });
        typeof(MainWindow).GetMethod("FilterEvents", flags)!.Invoke(window, [string.Empty]);
        var tabs = (TabControl)window.FindName("CatalogTabs");
        tabs.SelectedIndex = 2;
        var grid = (DataGrid)window.FindName("EventGrid");
        grid.SelectedIndex = 0;
        var selected = grid.SelectedItem;
        var originalLanguage = EditorText.Culture.Name;
        var languageOptions = ((Button)window.FindName("LanguageButton")).ContextMenu.Items.OfType<MenuItem>();
        void SelectLanguage(string language)
        {
            var option = languageOptions.Single(item => item.Tag as string == language);
            option.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, option));
            FlushLanguageBindings();
            window.UpdateLayout();
        }
        static TextBlock? FindDetail(DependencyObject parent, string name)
        {
            if (parent is TextBlock found && found.Name == name) return found;
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
                if (FindDetail(VisualTreeHelper.GetChild(parent, index), name) is { } descendant) return descendant;
            return null;
        }
        TextBlock? SelectedDetail(string name = "EventTriggerConditionsText") =>
            grid.ItemContainerGenerator.ContainerFromItem(grid.SelectedItem) is DataGridRow row ? FindDetail(row, name) : null;
        static string VisibleText(TextBlock? block) => block is null ? string.Empty : new TextRange(block.ContentStart, block.ContentEnd).Text;
        try
        {
            foreach (var language in new[] { "en", "zh-CN" })
            {
                SelectLanguage(language);
                RenderContractWindow(window, new Size(1440, 900), Path.Combine(runRoot, $"event-conditions-{language}.png"));
                var detail = SelectedDetail();
                Assert(ReferenceEquals(selected, grid.SelectedItem) && detail is not null &&
                    VisibleText(detail) == EditorText.Get("TownEvent_RequirementsLabel") + invasion.TriggerRules.Describe() && detail.TextWrapping == TextWrapping.Wrap &&
                    detail.ActualWidth > 0 && detail.ActualWidth <= grid.ActualWidth,
                    "Selected event conditions must wrap within the actual grid and switch language without replacing the selected row.");
                Assert(((Button)window.FindName("PreviewButton")).Content as string ==
                    (language == "zh-CN" ? "生成事件安全预览" : "Preview event"),
                    "The event preview action must follow the other catalog action labels.");
                CheckDescription();
                window.Width = 1120;
                window.Height = 720;
                window.UpdateLayout();
                // At minimum height, row details share a pixel-scrolling viewport.
                detail!.BringIntoView();
                FlushLanguageBindings();
                RenderContractWindow(window, new Size(1120, 720), Path.Combine(runRoot, $"event-conditions-{language}-minimum.png"));
                var viewport = FindViewport(detail);
                var bounds = detail.TransformToAncestor(viewport).TransformBounds(new Rect(detail.RenderSize));
                Assert(bounds.Top >= -1 && bounds.Bottom <= viewport.ActualHeight + 1 &&
                    bounds.Left >= -1 && bounds.Right <= viewport.ActualWidth + 1,
                    "Full condition text must remain reachable without clipping in the minimum-size scroll viewport.");
                CheckDescription();
            }
            grid.SelectedIndex = 1;
            window.UpdateLayout();
            FlushLanguageBindings();
            Assert(VisibleText(SelectedDetail()) == EditorText.Get("TownEvent_RequirementsLabel") + revival.TriggerRules.Describe(),
                "Selecting another event must display its own conditions rather than stale row details.");
        }
        finally
        {
            SelectLanguage(originalLanguage);
            tabs.SelectedIndex = 0;
        }

        void CheckDescription()
        {
            var description = SelectedDetail("EventDescriptionText")!;
            var viewport = FindViewport(description);
            var viewer = (ScrollViewer)viewport.TemplatedParent;
            var bounds = description.TransformToAncestor(viewport).TransformBounds(new Rect(description.RenderSize));
            Assert(description.Text == EditorText.ContentName(invasion.LocalizedDescription, invasion.Id) &&
                description.ActualHeight > description.FontSize * 2 && bounds.Left >= 0 &&
                bounds.Right <= viewport.ActualWidth + 1 && viewer.ScrollableWidth < 1,
                "A long bilingual event description must wrap within the viewport without creating horizontal scrolling.");
        }
    }

    private static ScrollContentPresenter FindViewport(DependencyObject child)
    {
        for (var parent = VisualTreeHelper.GetParent(child); parent is not null; parent = VisualTreeHelper.GetParent(parent))
            if (parent is ScrollContentPresenter viewport) return viewport;
        throw new InvalidOperationException("Event details must have a scroll viewport.");
    }

    private static void VerifyCatalogTabEdges()
    {
        foreach (var scale in new[] { 1.0, 1.25, 1.5, 2.0 })
        foreach (var width in Enumerable.Range(660, 25))
        foreach (var selection in new[] { 2, 4 })
        {
            var tabs = new TabControl { UseLayoutRounding = true, SnapsToDevicePixels = true };
            foreach (var label in new[] { "ITEMS", "TRINKETS", "EVENTS", "HEROES", "BATTLE" })
                tabs.Items.Add(new TabItem { Header = label });
            tabs.SelectedIndex = selection;
            // A desktop window supplies a physical-pixel-aligned viewport.
            var viewportWidth = Math.Round(width * scale) / scale;
            tabs.Measure(new Size(viewportWidth, 90));
            tabs.Arrange(new Rect(0, 0, viewportWidth, 90));
            tabs.UpdateLayout();
            VisualTreeHelper.SetRootDpi(tabs, new DpiScale(scale, scale));
            tabs.Measure(new Size(viewportWidth, 90));
            tabs.Arrange(new Rect(0, 0, viewportWidth, 90));
            tabs.UpdateLayout();
            var panel = (FrameworkElement)tabs.Template.FindName("HeaderPanel", tabs);
            var last = (TabItem)tabs.Items[^1];
            var border = (Border)last.Template.FindName("TabChrome", last);
            var expected = ((SolidColorBrush)border.BorderBrush).Color;
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Round(panel.ActualWidth * scale),
                (int)Math.Round(panel.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render(panel);
            var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
            bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
            var offset = (bitmap.PixelWidth * (bitmap.PixelHeight / 2) + bitmap.PixelWidth - 1) * 4;
            Assert(pixels[offset] == expected.B && pixels[offset + 1] == expected.G && pixels[offset + 2] == expected.R && pixels[offset + 3] == 255,
                $"The Battle tab's right edge must remain visible at {scale * 100}% scaling, width {width}, selected tab {selection}; actual panel DPI {VisualTreeHelper.GetDpi(panel).DpiScaleX}, panel width {panel.ActualWidth}, pixel {string.Join(',', pixels.Skip(offset).Take(4))}.");
            var headers = tabs.Items.Cast<TabItem>().Select(item =>
                item.TransformToAncestor(panel).TransformBounds(new Rect(item.RenderSize))).ToArray();
            Assert(headers[0].Left == 0 && Math.Abs(headers[^1].Right - panel.ActualWidth) < 1 / scale &&
                headers.Zip(headers.Skip(1), (left, right) => Math.Abs(left.Right - right.Left) < 1 / scale).All(value => value),
                "Catalog headers must share the available width without overlaps, gaps or a clipped last tab.");
        }
    }
}
