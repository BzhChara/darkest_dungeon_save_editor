using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DarkestDungeonSaveEditor.Core;

namespace DarkestDungeonSaveEditor.App;

public partial class QuestBoardPreview : UserControl
{
    private readonly QuestBoardPreviewAssets _assets = new();
    private List<QuestBoardSample> _samples = [];
    private IReadOnlyList<QuestBoardRegion> _regions = [];
    private QuestBoardSnapshot? _snapshot;
    private bool _fixtureMode;
    private bool _updatingUnpositioned;
    private Guid? _selected;
    private ContextMenu? _menu;
    private QuestBoardPreviewDialog? _picker;
    private Window? _expandedWindow;
    private bool _ready;
    private bool _profileLoaded;
    private string? _pendingGameDirectory;
    private readonly Dictionary<(double X, double Y), string> _regionChoices = [];
    private double _selectionAngle = Random.Shared.NextDouble() * 360;

    public event Func<bool>? ProfileBrowseChecking;
    public event Action<string>? PreviewActionApplied;
    public event Func<QuestBoardEditRequest, Task>? SaveEditRequested;

    public QuestBoardPreview()
    {
        InitializeComponent();
        _ready = true;
    }

    internal IReadOnlyList<QuestBoardSample> Samples => _samples;
    internal Guid? SelectedKey => _selected;

    internal void SetProfileLoaded(bool loaded)
    {
        _profileLoaded = loaded;
        if (!loaded)
        {
            DismissTransientUi();
            if (!_fixtureMode) SetSnapshot(null);
        }
        UpdateBoardState();
    }

    private bool CanInteract() => _profileLoaded && IsEnabled && IsVisible &&
        ProfileBrowseChecking?.Invoke() == true;

    internal void Configure(string? gameDirectory)
    {
        _pendingGameDirectory = gameDirectory;
        if (_ready && IsVisible && ConfigureAssets()) RebuildBoard();
    }

    private bool ConfigureAssets()
    {
        var changed = _assets.Configure(_pendingGameDirectory);
        if (!_fixtureMode) UpdateResolvedImages();
        return changed;
    }

    private void Preview_Loaded(object sender, RoutedEventArgs e)
    {
        ConfigureAssets();
        RebuildBoard();
        RefreshMapView();
    }

    private void Preview_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!_ready) return;
        if (IsVisible)
        {
            ConfigureAssets();
            RebuildBoard();
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(RefreshMapView));
        }
        else DismissTransientUi();
    }

    private void Preview_IsEnabledChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_ready && !IsEnabled) DismissTransientUi();
    }

    internal void DismissTransientUi()
    {
        CloseMenu();
        _picker?.Close();
        _expandedWindow?.Close();
        EndPan();
    }

    internal void RefreshLanguage()
    {
        if (!_ready) return;
        CloseMenu();
        RebuildBoard();
    }

    private void RebuildBoard()
    {
        MapCanvas.Children.Clear();
        var backdrop = _assets.QuestImage("quest_select.background.png");
        MissingArtNotice.Visibility = backdrop is null ? Visibility.Visible : Visibility.Collapsed;
        MapBackdrop.Source = backdrop;
        DetailBackdrop.Source = _assets.QuestImage("quest_select.questverbose_bg.png");
        EstateMapIcon.Source = _assets.QuestImage("quest_select.icon.png");

        foreach (var position in _regions.Where(region => region.HasLayout).GroupBy(region => (region.X, region.Y)))
        {
            var alternatives = position.ToArray();
            var selectedRegion = _samples.FirstOrDefault(sample => sample.Key == _selected)?.RegionId;
            var region = alternatives.FirstOrDefault(item => item.Id == selectedRegion) ??
                alternatives.FirstOrDefault(item => item.Id == _regionChoices.GetValueOrDefault(position.Key)) ??
                alternatives.FirstOrDefault(item => _samples.Any(sample => sample.RegionId == item.Id)) ?? alternatives[0];
            _regionChoices[position.Key] = region.Id;
            // The region is a layout container, not an invisible input surface.
            // Only headers and quest buttons intercept the viewport's global menu.
            var panel = new Canvas { Tag = region.Id };
            Canvas.SetLeft(panel, region.X);
            Canvas.SetTop(panel, region.Y);
            AddRegionHeader(panel, region, alternatives);
            var entries = _samples.Where(x => x.RegionId == region.Id).ToArray();
            var columns = region.QuestsPerRow;
            for (var index = 0; index < entries.Length; index++)
            {
                var entry = entries[index];
                var bounds = _assets.QuestIconBounds(entry.Choice);
                var content = new Grid { Width = bounds.Width, Height = bounds.Height };
                if (entry.Key == _selected)
                {
                    var image = _assets.QuestImage("quest_select_selected.png");
                    var dimensions = QuestBoardPreviewAssets.PixelSize(image, 192, 192);
                    var selected = QuestBoardPreviewAssets.Picture(image, dimensions.Width, dimensions.Height);
                    selected.RenderTransform = new RotateTransform(_selectionAngle, selected.Width / 2, selected.Height / 2);
                    Canvas.SetLeft(selected, _assets.Layout.SelectionOffset.X - bounds.Left);
                    Canvas.SetTop(selected, _assets.Layout.SelectionOffset.Y - bounds.Top);
                    var background = new Canvas { Width = bounds.Width, Height = bounds.Height, IsHitTestVisible = false, Tag = "QuestSelection" };
                    background.Children.Add(selected);
                    content.Children.Add(background);
                }
                content.Children.Add(_assets.QuestIcon(entry.Choice));
                var button = new QuestBoardTaskButton
                {
                    Style = (Style)FindResource("BoardMapButton"), Content = content, Tag = entry.Key,
                    ToolTip = FormatSample(entry), Width = bounds.Width, Height = bounds.Height,
                    ContainsArtwork = _assets.QuestHitTest(entry.Choice)
                };
                System.Windows.Automation.AutomationProperties.SetName(button, FormatSample(entry));
                button.Click += (_, _) => SelectSample(entry.Key);
                button.MouseRightButtonUp += (_, e) =>
                {
                    SelectSample(entry.Key);
                    ShowMenu(region.Id, entry.Key, MapViewport);
                    e.Handled = true;
                };
                var center = _assets.Layout.QuestCenter(index, columns);
                Canvas.SetLeft(button, center.X + bounds.Left);
                Canvas.SetTop(button, center.Y + bounds.Top);
                panel.Children.Add(button);
            }
            MapCanvas.Children.Add(panel);
        }
        RefreshUnpositionedQuests();
        UpdateBoardState();
        RefreshDetails();
        RefreshMapView();
    }

    private static string FormatSample(QuestBoardSample entry) =>
        $"{entry.Choice.Name} · {QuestBoardPreviewData.Difficulty(entry.Choice.Difficulty)} · {QuestBoardPreviewData.Length(entry.Choice.Length)}";

    private void SelectSample(Guid? key)
    {
        if (!CanInteract()) return;
        _selected = key;
        _selectionAngle = Random.Shared.NextDouble() * 360;
        RewardsScroll.ScrollToTop();
        RewardInventoryScroll.ScrollToTop();
        RebuildBoard();
    }

    private void RefreshDetails()
    {
        var selected = _samples.FirstOrDefault(x => x.Key == _selected);
        RewardPanel.Children.Clear();
        if (selected is null)
        {
            QuestName.Text = EditorText.Get("QuestBoard_SelectTask");
            QuestDescription.Text = QuestSpecs.Text = QuestObjective.Text = CampingText.Text = string.Empty;
            QuestName.ToolTip = QuestDescription.ToolTip = QuestObjective.ToolTip = CampingText.ToolTip = null;
            SizeRewardPanel();
            return;
        }
        QuestName.Text = selected.Choice.Name;
        QuestName.ToolTip = selected.Choice.Name;
        QuestDescription.Text = selected.Choice.Description;
        QuestDescription.ToolTip = selected.Choice.Description;
        QuestSpecs.Inlines.Clear();
        QuestSpecs.Inlines.Add(QuestBoardPreviewData.Length(selected.Choice.Length) + " | ");
        QuestSpecs.Inlines.Add(new System.Windows.Documents.Run(QuestBoardPreviewData.Difficulty(selected.Choice.Difficulty))
        {
            Foreground = new SolidColorBrush(selected.Choice.Difficulty switch
            {
                1 => Color.FromRgb(153, 192, 83),
                3 => Color.FromRgb(215, 170, 67),
                _ => Color.FromRgb(212, 74, 60)
            })
        });
        var campCount = selected.Choice.IsGenerated || selected.Choice.Kind == "kill_boss" || selected.Choice.Kind == "darkest" ||
                        selected.Choice.PlotId?.StartsWith("plot_darkest_dungeon_", StringComparison.Ordinal) == true
            ? selected.Choice.Length - 1 : 0;
        CampingText.Text = selected.Choice.SavedQuest is { } saved
            ? saved.HasRandomCamping ? "×?" : $"×{saved.Firewood}"
            : $"×{campCount}";
        CampingText.ToolTip = selected.Choice.SavedQuest is { CampingTooltip.Length: > 0 } camp ? camp.CampingTooltip : null;
        QuestObjective.Text = selected.Choice.Objective;
        QuestObjective.ToolTip = selected.Choice.Objective;
        if (selected.Choice.SavedQuest is { } quest)
        {
            foreach (var reward in quest.Rewards)
            {
                var slot = CreateRewardSlot(reward.IconPath, reward.Amount.ToString("N0"),
                    reward.Rarity.Length > 0 ? reward.Rarity : null);
                slot.ToolTip = $"{reward.Name} ×{reward.Amount:N0}";
                System.Windows.Automation.AutomationProperties.SetName(slot, (string)slot.ToolTip);
                if (_assets.Image(reward.IconPath) is null)
                    slot.Children.Add(new TextBlock
                    {
                        Text = reward.Name, TextWrapping = TextWrapping.Wrap, Width = 72,
                        Margin = new Thickness(4, 35, 0, 0), Foreground = (Brush)FindResource("GoldBrush")
                    });
                RewardPanel.Children.Add(slot);
            }
            SizeRewardPanel();
            return;
        }
        // Fixed sample rewards illustrate the detail panel, not reward-generation rules.
        RewardPanel.Children.Add(CreateRewardSlot("panels/icons_equip/gold/inv_gold+_3.png", "3,000"));
        RewardPanel.Children.Add(CreateRewardSlot("panels/icons_equip/heirloom/inv_heirloom+crest.png", "6"));
        var trinkets = new[]
        {
            (Id: "accuracy_stone", Rarity: "very_common", Name: EditorText.Get("QuestBoard_TrinketAccuracyStone")),
            (Id: "tough_ring", Rarity: "very_rare", Name: EditorText.Get("QuestBoard_TrinketToughRing")),
            (Id: "speed_stone", Rarity: "common", Name: EditorText.Get("QuestBoard_TrinketSpeedStone")),
            (Id: "stunning_satchel", Rarity: "common", Name: EditorText.Get("QuestBoard_TrinketStunningSatchel"))
        };
        foreach (var trinket in trinkets.Take(selected.Choice.Kind == "recovery" ? trinkets.Length : 1))
        {
            var slot = CreateRewardSlot($"panels/icons_equip/trinket/inv_trinket+{trinket.Id}.png",
                rarity: trinket.Rarity);
            slot.ToolTip = trinket.Name;
            System.Windows.Automation.AutomationProperties.SetName(slot, trinket.Name);
            RewardPanel.Children.Add(slot);
        }
        SizeRewardPanel();
    }

    internal ContextMenu CreateTaskMenu(string regionId, Guid? key)
    {
        var menu = new ContextMenu { Style = (Style)FindResource("BattleMapContextMenuStyle") };
        MenuItem Item(string label, MapMenuIcon icon) => new()
        {
            Header = label,
            Icon = MapMenuIcons.Create(icon, file => _assets.Image("panels/icons_map/" + file)),
            Style = (Style)FindResource("BattleMapMenuItemStyle")
        };
        void AddCategories(string label, MapMenuIcon icon, Guid? replacing)
        {
            var root = Item(EditorText.Get(label), icon);
            var choices = EditChoices(regionId);
            root.IsEnabled = choices.Length > 0 && (_fixtureMode || SaveEditRequested is not null);
            var groups = _fixtureMode
                ? Enum.GetValues<QuestBoardCategory>().Select(category => (Category: (QuestBoardCategory?)category, Type: string.Empty))
                : choices.Select(choice => (choice.Category, Type: choice.Category is null ? choice.NativeType : string.Empty)).Distinct()
                    .OrderBy(group => group.Category is { } category ? (int)category : int.MaxValue).ThenBy(group => group.Type);
            foreach (var (category, nativeType) in groups)
            {
                var count = choices.Count(choice => MatchesCategory(choice, category, nativeType));
                var child = Item((category is { } known ? QuestBoardPreviewData.CategoryName(known) : nativeType) + $" ({count})", category switch
                {
                    QuestBoardCategory.Cleanse or QuestBoardCategory.Wave => MapMenuIcon.Battle,
                    QuestBoardCategory.KillBoss => MapMenuIcon.Boss,
                    QuestBoardCategory.InventoryActivate or QuestBoardCategory.Activate => MapMenuIcon.Curio,
                    QuestBoardCategory.Gather => MapMenuIcon.Treasure,
                    _ => MapMenuIcon.Attachment
                });
                child.Tag = category;
                child.IsEnabled = count > 0;
                child.Click += async (_, e) =>
                {
                    e.Handled = true;
                    if (CanInteract()) await OpenPickerAsync(regionId, category, nativeType, replacing);
                };
                root.Items.Add(child);
            }
            menu.Items.Add(root);
        }
        if (key is { } taskKey)
        {
            AddCategories("QuestBoard_Replace", MapMenuIcon.Replace, taskKey);
            var remove = Item(EditorText.Get("QuestBoard_Delete"), MapMenuIcon.Remove);
            remove.IsEnabled = _fixtureMode || SaveEditRequested is not null;
            remove.Click += async (_, e) =>
            {
                e.Handled = true;
                if (!CanInteract()) return;
                if (_fixtureMode) RemoveSample(taskKey);
                else if (_snapshot is { } snapshot && _samples.FirstOrDefault(item => item.Key == taskKey)?.Choice.SavedQuest is { } quest)
                {
                    CloseMenu();
                    if (SaveEditRequested is { } handler)
                        await handler(new(QuestBoardEditKind.Delete, snapshot.SourceSha256, quest.SaveKey));
                }
            };
            menu.Items.Add(remove);
        }
        else AddCategories("QuestBoard_Add", MapMenuIcon.Add, null);
        return menu;
    }

    private void ShowMenu(string regionId, Guid? key, UIElement target)
    {
        if (!CanInteract()) return;
        CloseMenu();
        _menu = CreateTaskMenu(regionId, key);
        _menu.PlacementTarget = target;
        _menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        _menu.IsOpen = true;
    }

    internal ContextMenu CreateRegionMenu()
    {
        var menu = new ContextMenu { Style = (Style)FindResource("BattleMapContextMenuStyle") };
        var root = new MenuItem
        {
            Header = EditorText.Get("QuestBoard_Add"), Style = (Style)FindResource("BattleMapMenuItemStyle"),
            Icon = MapMenuIcons.Create(MapMenuIcon.Add, file => _assets.Image("panels/icons_map/" + file))
        };
        var regions = _fixtureMode ? _regions.Select(region => (region.Id, region.Name)) :
            (_editCatalog?.Regions ?? []).Select(region => (region.Id, region.Name));
        foreach (var region in regions)
        {
            if (!EditChoices(region.Id).Any()) continue;
            var regional = CreateTaskMenu(region.Id, null);
            var categories = (MenuItem)regional.Items[0];
            regional.Items.Clear();
            categories.Header = region.Name;
            root.Items.Add(categories);
        }
        root.IsEnabled = root.Items.Count > 0 && (_fixtureMode || SaveEditRequested is not null);
        menu.Items.Add(root);
        return menu;
    }

    private void Viewport_ShowRegionMenu(object sender, MouseButtonEventArgs e)
    {
        if (!CanInteract()) return;
        CloseMenu();
        _menu = CreateRegionMenu();
        _menu.PlacementTarget = MapViewport;
        _menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        _menu.IsOpen = true;
        e.Handled = true;
    }

    private void UnpositionedQuests_ShowMenu(object sender, MouseButtonEventArgs e)
    {
        if (UnpositionedQuests.SelectedValue is Guid key && _samples.FirstOrDefault(item => item.Key == key) is { } entry)
        {
            ShowMenu(entry.RegionId, key, UnpositionedQuests);
            e.Handled = true;
        }
    }

    private void CloseMenu()
    {
        if (_menu is not null) _menu.IsOpen = false;
        _menu = null;
    }

    private static bool MatchesCategory(QuestBoardChoice choice, QuestBoardCategory? category, string nativeType) =>
        category is not null ? choice.Category == category : choice.Category is null && choice.NativeType == nativeType;

    private async Task OpenPickerAsync(string regionId, QuestBoardCategory? category, string nativeType, Guid? replacing)
    {
        if (!CanInteract()) return;
        var choices = EditChoices(regionId).Where(choice => MatchesCategory(choice, category, nativeType)).ToArray();
        if (choices.Length == 0) return;
        var snapshot = _snapshot;
        CloseMenu();
        var previous = _samples.FirstOrDefault(x => x.Key == replacing);
        var regionName = _regions.FirstOrDefault(item => item.Id == regionId)?.Name ??
            EditorText.ContentName(_editCatalog?.Regions.FirstOrDefault(item => item.Id == regionId)?.LocalizedName ?? BilingualContentName.Empty, regionId);
        var dialog = new QuestBoardPreviewDialog(regionName,
            category is { } known ? QuestBoardPreviewData.CategoryName(known) : nativeType,
            choices, previous?.Choice, _assets, _fixtureMode)
        {
            Owner = _expandedWindow ?? Window.GetWindow(this)
        };
        _picker = dialog;
        try
        {
            if (dialog.ShowDialog() != true || dialog.SelectedChoice is not { } choice) return;
            if (_fixtureMode) ApplySample(regionId, choice, replacing);
            else if (ReferenceEquals(snapshot, _snapshot) && snapshot is not null && CanInteract() && SaveEditRequested is { } handler)
                await handler(new(replacing is null ? QuestBoardEditKind.Create : QuestBoardEditKind.Replace,
                    snapshot.SourceSha256, previous?.Choice.SavedQuest?.SaveKey, choice.TemplateKey));
        }
        finally { _picker = null; }
    }

    internal void ApplySample(string regionId, QuestBoardChoice choice, Guid? replacing = null)
    {
        if (!_fixtureMode || !CanInteract()) return;
        if (!QuestBoardPreviewData.Choices(regionId).Contains(choice)) return;
        var index = replacing is { } key ? _samples.FindIndex(x => x.Key == key) : -1;
        if (replacing is not null && index < 0) return;
        var entry = QuestBoardSample.Create(regionId, choice);
        if (index >= 0) _samples[index] = entry; else _samples.Add(entry);
        _selected = entry.Key;
        RewardsScroll.ScrollToTop();
        RewardInventoryScroll.ScrollToTop();
        RebuildBoard();
        PreviewActionApplied?.Invoke(EditorText.Format(index >= 0 ? "QuestBoard_Replaced" : "QuestBoard_Added", choice.Name));
    }

    internal void RemoveSample(Guid key)
    {
        if (!_fixtureMode || !CanInteract()) return;
        var selected = _samples.FirstOrDefault(x => x.Key == key);
        if (selected is null) return;
        _samples.Remove(selected);
        if (_selected == key) _selected = _samples.FirstOrDefault(x => x.RegionId == selected.RegionId)?.Key;
        RebuildBoard();
        PreviewActionApplied?.Invoke(EditorText.Format("QuestBoard_Deleted", selected.Choice.Name));
    }

}
