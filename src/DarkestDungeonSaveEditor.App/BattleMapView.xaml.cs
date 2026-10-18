using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DarkestDungeonSaveEditor.Core;

namespace DarkestDungeonSaveEditor.App;

public partial class BattleMapView : UserControl
{
    private const double MinimumZoom = 0.45;
    private const double MaximumZoom = 2.40;
    private const double PanThreshold = 5;
    private const double MapGridTileSize = 24;
    private const double MapContentMargin = 52;
    private const string MapPanelAsset = @"panels\panel_map.png";
    private const string MapIconDirectory = @"panels\icons_map";
    private static readonly TimeSpan LiveRefreshRetryDelay = TimeSpan.FromSeconds(2);
    private readonly List<PrototypeMapCell> _cells = [];
    private readonly Dictionary<string, ImageSource?> _mapAssetCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private bool _isRaidAvailable;
    private bool _isRightButtonDown;
    private bool _isPanning;
    private bool _fitToView = true;
    private Point _rightButtonOrigin;
    private Point _panOrigin;
    private PrototypeMapCell? _selectedCell;
    private ContextMenu? _activeContextMenu;
    private string? _gameDirectory;
    private string? _workshopDirectory;
    private string? _localModDirectory;
    private bool _usesOriginalMapAssets;
    private string? _profileDirectory;
    private string? _profileId;
    private SaveProfile? _profile;
    private DsonSaveCodec? _codec;
    private ActiveContentSnapshot? _activeContentSnapshot;
    private BattleMapSnapshotReader? _snapshotReader;
    private BattleMapEditService? _editService;
    private ForceTownSaveService? _forceTownSaveService;
    private ManagedBattleEncounterBridgeService? _managedEncounterBridgeService;
    private BattleEncounterCatalogResult? _encounterCatalog;
    private BattleRoomAttachmentCatalogResult? _roomAttachmentCatalog;
    private ProfileSaveMonitor? _profileMonitor;
    private CancellationTokenSource? _refreshRetryCancellation;
    private BattleMapSnapshot? _currentSnapshot;
    private int _profileGeneration;
    private bool _isApplyingEdit;

    public event Action<BattleMapSnapshot>? SnapshotRefreshed;
    public event Action<ActiveContentSnapshot>? ActiveContentChanged;
    public event Action<string>? SaveEditApplied;
    public event Func<bool, Task>? SaveEditBusyChanged;
    public event Func<bool>? ProfileBrowseChecking;
    public event Func<Task<bool>>? ProfileEditChecking;

    private bool CanBrowseProfile()
    {
        if (_isApplyingEdit || !IsEnabled) return false;
        if (ProfileBrowseChecking is not { } handlers) return true;
        foreach (Func<bool> handler in handlers.GetInvocationList())
            if (!handler()) return false;
        return true;
    }

    private async Task<bool> CheckProfileEditAsync()
    {
        if (_isApplyingEdit) return false;
        if (ProfileEditChecking is not { } handlers) return true;
        foreach (Func<Task<bool>> handler in handlers.GetInvocationList())
            if (!await handler()) return false;
        return true;
    }

    private async Task NotifySaveEditBusyAsync(bool busy)
    {
        if (SaveEditBusyChanged is not { } handlers) return;
        foreach (Func<bool, Task> handler in handlers.GetInvocationList())
            await handler(busy);
    }

    public BattleMapSnapshot? CurrentSnapshot => _currentSnapshot;

    public BattleMapView()
    {
        InitializeComponent();
        SetRaidAvailability(false);
    }

    internal void RefreshLanguage()
    {
        CloseActiveContextMenu();
        foreach (var cell in _cells)
        {
            if (cell.SourceAreaId is not null)
                cell.ContentLabel = cell.Content switch
                {
                    PrototypeContent.Entrance => EditorText.Get("BattleMapView_CellVisuals_011"),
                    PrototypeContent.Boss => EditorText.Get("BattleMapView_MapConstruction_006"),
                    _ => FormatSnapshotContent((BattleMapTileContent)cell.RawContent, cell.RawContent)
                };
            RefreshCellVisual(cell);
        }
        // Do not rebuild the map or publish a snapshot: selection, zoom, scroll and
        // the guarded edit state belong to the current profile, not its UI language.
        System.Windows.Data.BindingOperations.GetBindingExpression(MapSelectionTextBlock, TextBlock.TextProperty)?.UpdateTarget();
        UpdateMapBadge();
    }
}
