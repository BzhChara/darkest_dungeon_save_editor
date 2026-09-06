using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DarkestDungeonSaveEditor.Core;
using Microsoft.Win32;

namespace DarkestDungeonSaveEditor.App;

public partial class MainWindow : Window
{
    private const int BattleTabIndex = 3;
    private const int TitleLogoFrameWidth = 320;
    private const int TitleLogoFrameHeight = 100;
    private const int TitleLogoFrameColumns = 10;
    private const int TitleLogoFrameCount = 120;
    private static readonly TimeSpan TitleLogoFrameInterval = TimeSpan.FromMilliseconds(100);
    private readonly ObservableCollection<ItemRow> _visibleItems = [];
    private readonly ObservableCollection<TrinketRow> _visibleTrinkets = [];
    private readonly ObservableCollection<HeroRow> _visibleHeroes = [];
    private readonly ObservableCollection<HeroLevelChoice> _heroLevelChoices = [];
    private IReadOnlyList<QuantityItemDefinition> _allItems = [];
    private IReadOnlyList<TrinketDefinition> _allTrinkets = [];
    private IReadOnlyList<HeroClassDefinition> _allHeroes = [];
    private IReadOnlyList<string> _selectedInitialQuirkIds = [];
    private HeroClassCatalogResult? _heroCatalog;
    private TrinketStorageDefinition? _trinketStorage;
    private RaidInventoryStorageDefinition? _raidInventoryStorage;
    private ActiveContentSnapshot? _activeContentSnapshot;
    private PreparedQuantityItemEdit? _preparedQuantityItemEdit;
    private PreparedTrinketEdit? _preparedTrinketEdit;
    private PreparedStagecoachHeroEdit? _preparedHeroEdit;
    private StagecoachHeroCandidatePreview? _preparedHeroCandidatePreview;
    private SaveEditService? _editService;
    private string? _catalogProfileDirectory;
    private string? _catalogGameSaveSha256;
    private string? _catalogEstateSaveSha256;
    private string? _catalogQuantitySaveSha256;
    private QuantityItemSaveContext _quantitySaveContext = QuantityItemSaveContext.Town;
    private int _editRevision;
    private DispatcherTimer? _titleLogoTimer;
    private CroppedBitmap[]? _titleLogoFrames;
    private int _titleLogoFrameIndex;

    public MainWindow()
    {
        InitializeComponent();
        BattleMapPanel.UsesSharedProfileMonitor = true;
        ItemGrid.ItemsSource = _visibleItems;
        TrinketGrid.ItemsSource = _visibleTrinkets;
        HeroGrid.ItemsSource = _visibleHeroes;
        HeroLevelComboBox.ItemsSource = _heroLevelChoices;
        BattleMapPanel.SnapshotRefreshed += BattleMapPanel_SnapshotRefreshed;
        BattleMapPanel.ActiveContentChanged += BattleMapPanel_ActiveContentChanged;
        BattleMapPanel.SaveEditApplied += BattleMapPanel_SaveEditApplied;
        BattleMapPanel.SaveEditBusyChanged += BattleMapPanel_SaveEditBusyChanged;
        UpdateCatalogMode();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        NativeWindowTheme.ApplyDarkTitleBar(this);
    }

    protected override void OnClosed(EventArgs e)
    {
        StopProfileSync();
        _titleLogoTimer?.Stop();
        BattleMapPanel.SnapshotRefreshed -= BattleMapPanel_SnapshotRefreshed;
        BattleMapPanel.ActiveContentChanged -= BattleMapPanel_ActiveContentChanged;
        BattleMapPanel.SaveEditApplied -= BattleMapPanel_SaveEditApplied;
        BattleMapPanel.SaveEditBusyChanged -= BattleMapPanel_SaveEditBusyChanged;
        BattleMapPanel.ClearProfile();
        base.OnClosed(e);
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        RequestProfileSync(invalidatePreview: false);
    }

}
