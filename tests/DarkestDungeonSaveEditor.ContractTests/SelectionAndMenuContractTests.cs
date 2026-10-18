using System.Collections;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using DarkestDungeonSaveEditor.App;

internal static partial class ContractSuite
{
    public static async Task RunSelectionAndMenuOnlyAsync(string repositoryRoot)
    {
        var root = Path.Combine(repositoryRoot, "artifacts", "selection-menu-contracts", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        await RunWpfContractsAsync(() => VerifySelectionAndMenuAsync(root));
        await RunBridgeTitleContractsAsync(root, DsonSaveCodec.CreateNative());
        Console.WriteLine($"Artifacts: {root}");
    }

    private static async Task VerifySelectionAndMenuAsync(string root)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var locations = new SaveEditorLocations(root, Path.Combine(root, "selection-work"), Path.Combine(root, "selection-backups"));
        var languagePath = Path.Combine(root, "selection-language.json");
        EditorLanguageSettings.Save(languagePath, EditorText.Culture.Name);
        var window = new MainWindow(languagePath, saveLocations: locations,
            gameRunningProbe: () => false);
        try
        {
            ((DispatcherTimer)typeof(MainWindow).GetField("_gameProcessPoll", flags)!.GetValue(window)!).Stop();
            void Set(string name, object value) => typeof(MainWindow).GetField(name, flags)!.SetValue(window, value);
            var items = new[] { "first", "second" }.Select(id => new QuantityItemDefinition(
                "supply", id, QuantityItemStorageKind.RaidInventory, 2, true, 0, "base", "fixture", false, ["base"])).ToArray();
            var trinkets = new[] { "first", "second" }.Select(id => new TrinketDefinition(
                id, "common", null, 1, "base", "fixture", false, [], false, ["base"])).ToArray();
            var heroes = new[] { "first", "second" }.Select(id => new HeroClassDefinition(
                id, "base", "fixture", true, 4, null, 20, [new HeroLevelProfile(0, 0, 0, 0, 20)],
                [], "", 1, [], [], [], [], [], [], [], [], false, ["base"])).ToArray();
            Set("_allItems", items);
            Set("_allTrinkets", trinkets);
            Set("_allHeroes", heroes);
            typeof(MainWindow).GetMethod("ApplyFilter", flags)!.Invoke(window, [false]);
            var grids = new[] { "ItemGrid", "TrinketGrid", "HeroGrid" }
                .Select(name => (DataGrid)window.FindName(name)).ToArray();
            foreach (var grid in grids) grid.SelectedIndex = 0;
            for (var cycle = 0; cycle < 3; cycle++)
            {
                // Sync restores selection before WPF binds/recycles the new row visuals.
                typeof(MainWindow).GetMethod("RefreshCatalogRowsPreservingInput", flags)!.Invoke(window, [false, true]);
                foreach (var grid in grids)
                {
                    var restored = grid.SelectedItem;
                    Assert(restored is not null && grid.SelectedItems.Count == 1, "Synchronization must restore one selected row.");
                    var text = new TextBlock();
                    var beforeBinding = restored!.GetHashCode();
                    text.SetBinding(TextBlock.TextProperty, new Binding("Id") { Source = restored });
                    FlushLanguageBindings();
                    restored.GetType().GetMethod("RefreshLanguage")!.Invoke(restored, null);
                    var next = grid.Items.Cast<object>().Single(row => !ReferenceEquals(row, restored));
                    grid.SelectedItem = next;
                    Assert(grid.SelectedItems.Count == 1 && ReferenceEquals(grid.SelectedItem, next) &&
                           ReferenceEquals(grid.SelectedItems[0], next) && beforeBinding == restored.GetHashCode() &&
                           text.Text == (string)restored.GetType().GetProperty("Id")!.GetValue(restored)!,
                        "Binding after sync and refreshing language must not retain a second selection or the previous edit target.");
                    BindingOperations.ClearBinding(text, TextBlock.TextProperty);
                    grid.SelectedItem = restored;
                    Assert(grid.SelectedItems.Count == 1 && ReferenceEquals(grid.SelectedItem, restored),
                        "Unbinding and selecting the original row again must still have one edit target.");
                }
            }
            var choiceType = typeof(MainWindow).GetNestedType("HeroLevelChoice", BindingFlags.NonPublic)!;
            var first = Activator.CreateInstance(choiceType, [0, 0])!;
            var second = Activator.CreateInstance(choiceType, [1, 2])!;
            var choices = new ComboBox { ItemsSource = new[] { first, second }, SelectedItem = first };
            var choiceLabel = new TextBlock();
            choiceLabel.SetBinding(TextBlock.TextProperty, new Binding("DisplayName") { Source = first });
            FlushLanguageBindings();
            first.GetType().GetMethod("RefreshLanguage")!.Invoke(first, null);
            choices.SelectedItem = second;
            Assert(ReferenceEquals(choices.SelectedItem, second), "Hero level selection must survive late bindings and language refresh.");
            BindingOperations.ClearBinding(choiceLabel, TextBlock.TextProperty);
            Console.WriteLine("PASS: actual editor item/trinket/hero grids keep one correct edit target through repeated sync restoration, late binding and language refresh.");
        }
        finally { window.Close(); }

        var map = new BattleMapView();
        var host = new Window { Content = map, Left = -20000, Top = -20000, Width = 900, Height = 650,
            ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual };
        var browseAllowed = true;
        var browseChecks = 0;
        var fullChecks = 0;
        map.ProfileBrowseChecking += () => { browseChecks++; return browseAllowed; };
        map.ProfileEditChecking += () => { fullChecks++; return Task.FromResult(false); };
        try
        {
            host.Show();
            typeof(BattleMapView).GetMethod("BuildPrototypeMap", flags)!.Invoke(map, null);
            var cells = (IList)typeof(BattleMapView).GetField("_cells", flags)!.GetValue(map)!;
            var visual = (UIElement)cells[0]!.GetType().GetProperty("Visual")!.GetValue(cells[0])!;
            void RightClick() => visual.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Right)
                { RoutedEvent = UIElement.MouseRightButtonUpEvent });
            ContextMenu? OpenMenu() => (ContextMenu?)typeof(BattleMapView).GetField("_activeContextMenu", flags)!.GetValue(map);
            for (var i = 0; i < 4; i++)
            {
                RightClick();
                Assert(OpenMenu() is { IsOpen: true } && fullChecks == 0,
                    "Repeated real right-click events must open the cached menu without full profile checks.");
                map.DismissProfileMenu();
            }
            var actionCount = 0;
            var menuFactory = typeof(BattleMapView).GetMethod("CreateAsyncActionMenuItem", flags)!;
            var icon = Enum.ToObject(menuFactory.GetParameters()[1].ParameterType, 0);
            var item = (MenuItem)menuFactory.Invoke(map, ["Fixture", icon,
                (Func<Task>)(() => { actionCount++; return Task.CompletedTask; })])!;
            for (var i = 0; i < 4; i++) item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert(actionCount == 4 && fullChecks == 0 && browseChecks == 8,
                "Choosing menu actions must use the lightweight guard without moving the full scan to each option click.");
            browseAllowed = false;
            RightClick();
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert(OpenMenu() is null && actionCount == 4 && fullChecks == 0,
                "A stopped browse guard must block both menu opening and an already-open menu's action.");
            // Invoke the actual write entry with no writable objects. Rejection must
            // return before dereferencing them, so it cannot touch any real save.
            var apply = typeof(BattleMapView).GetMethod("ApplySaveEditAsync", flags)!;
            var args = apply.GetParameters().Select(parameter => parameter.HasDefaultValue ? parameter.DefaultValue : null).ToArray();
            args[4] = BattleMapEditKind.DeleteContent;
            await (Task)apply.Invoke(map, args)!;
            Assert(fullChecks == 1, "Final map application must still perform and honor the full pre-write check.");
            Console.WriteLine("PASS: repeated right-click/action browsing avoids full checks, immediate browse rejection blocks actions, and final map writes retain the full guard.");
        }
        finally { map.DismissProfileMenu(); host.Close(); }
    }
}
