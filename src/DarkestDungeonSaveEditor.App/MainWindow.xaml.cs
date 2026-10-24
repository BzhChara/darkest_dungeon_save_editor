using System.Collections.ObjectModel;
using System.ComponentModel;
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
    private const int EventTabIndex = 2;
    private const int HeroTabIndex = 3;
    private const int BattleTabIndex = 4;
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
    private readonly DesktopCodecSession _codecSession;
    private readonly SaveEditorLocations? _saveLocations;

    public MainWindow() : this(EditorLanguageSettings.DefaultPath) { }

    internal MainWindow(string languageSettingsPath, DesktopCodecSession? codecSession = null,
        SaveEditorLocations? saveLocations = null, Func<bool>? gameRunningProbe = null)
    {
        _gameRunningProbe = gameRunningProbe ?? IsDarkestDungeonRunning;
        _saveLocations = saveLocations;
        _codecSession = codecSession ?? (Application.Current as App)?.CodecSession
            ?? DesktopCodecSession.Create([], AppContext.BaseDirectory);
        _languageSettingsPath = languageSettingsPath;
        InitializeComponent();
        LocalizedUi.Set(this, TitleProperty, () => EditorText.Get("MainWindow_001") +
            (!_codecSession.IsNative ? " — " + EditorText.Get("Codec_JavaReferenceTitle") :
                DesktopCodecSession.IsNativeTestBuild ? " — " + EditorText.Get("Codec_NativeTestTitle") : string.Empty));
        CrashDiagnostics.RecordStatus($"Window DSON codec: {_codecSession.Identity}");
        InitializeLanguageSelection();
        foreach (var grid in new[] { ItemGrid, TrinketGrid, EventGrid, HeroGrid }) EditorNameColumns.Apply(grid);
        BattleMapPanel.UsesSharedProfileMonitor = true;
        ItemGrid.ItemsSource = _visibleItems;
        TrinketGrid.ItemsSource = _visibleTrinkets;
        HeroGrid.ItemsSource = _visibleHeroes;
        EventGrid.ItemsSource = _visibleEvents;
        HeroLevelComboBox.ItemsSource = _heroLevelChoices;
        BattleMapPanel.SnapshotRefreshed += BattleMapPanel_SnapshotRefreshed;
        BattleMapPanel.ActiveContentChanged += BattleMapPanel_ActiveContentChanged;
        BattleMapPanel.SaveEditApplied += BattleMapPanel_SaveEditApplied;
        BattleMapPanel.SaveEditBusyChanged += BattleMapPanel_SaveEditBusyChanged;
        BattleMapPanel.ProfileBrowseChecking += BattleMapPanel_ProfileBrowseChecking;
        BattleMapPanel.ProfileEditChecking += BattleMapPanel_ProfileEditChecking;
        UpdateCatalogMode();
        StartGameProcessMonitoring();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        NativeWindowTheme.ApplyDarkTitleBar(this);
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel) return;
        if (_catalogLoadTask is not { IsCompleted: false } loadTask) return;

        // Keep the Dispatcher alive until cancellation has killed/waited for
        // the official tool and finished its receipt/temp-directory cleanup.
        e.Cancel = true;
        if (_catalogCloseRequested) return;
        _catalogCloseRequested = true;
        IsEnabled = false;
        StopProfileSync();
        try { await loadTask; }
        catch (Exception ex) { CrashDiagnostics.RecordException("Close: catalog cleanup", ex); }
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _gameProcessPoll?.Stop();
        StopProfileSync();
        _titleLogoTimer?.Stop();
        BattleMapPanel.SnapshotRefreshed -= BattleMapPanel_SnapshotRefreshed;
        BattleMapPanel.ActiveContentChanged -= BattleMapPanel_ActiveContentChanged;
        BattleMapPanel.SaveEditApplied -= BattleMapPanel_SaveEditApplied;
        BattleMapPanel.SaveEditBusyChanged -= BattleMapPanel_SaveEditBusyChanged;
        BattleMapPanel.ProfileBrowseChecking -= BattleMapPanel_ProfileBrowseChecking;
        BattleMapPanel.ProfileEditChecking -= BattleMapPanel_ProfileEditChecking;
        BattleMapPanel.ClearProfile();
        base.OnClosed(e);
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        CheckGameProcess();
    }

}
