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
    private bool _usesOriginalMapAssets;
    private string? _profileDirectory;
    private string? _profileId;
    private SaveProfile? _profile;
    private BattleMapSnapshotReader? _snapshotReader;
    private BattleMapEditService? _editService;
    private ProfileSaveMonitor? _profileMonitor;
    private CancellationTokenSource? _refreshRetryCancellation;
    private BattleMapSnapshot? _currentSnapshot;
    private int _profileGeneration;
    private bool _isApplyingEdit;

    public event Action<BattleMapSnapshot>? SnapshotRefreshed;
    public event Action<string>? SaveEditApplied;
    public event Action<bool>? SaveEditBusyChanged;

    public BattleMapSnapshot? CurrentSnapshot => _currentSnapshot;

    public BattleMapView()
    {
        InitializeComponent();
        SetRaidAvailability(false);
    }

    public void SetRaidAvailability(
        bool isAvailable,
        string? profileId = null,
        string? gameDirectory = null)
    {
        unchecked
        {
            _profileGeneration++;
        }
        CancelScheduledRefreshRetry();
        StopProfileMonitoring();
        _profileDirectory = null;
        _profileId = profileId;
        _profile = null;
        _snapshotReader = null;
        _editService = null;
        _currentSnapshot = null;
        ConfigureOriginalMapAssets(gameDirectory);
        _selectedCell = null;
        if (isAvailable)
        {
            BuildPrototypeMap();
            ShowMapSurface();
            MapTitleTextBlock.Text =
                $"{(string.IsNullOrWhiteSpace(profileId) ? "当前档案" : profileId)} · 副本地图交互原型";
            MapSelectionTextBlock.Text = "右击房间或走廊格查看操作。";
            LiveStatusTextBlock.Text = "原型数据 · 未绑定存档";
            UpdateMapBadge();
            _fitToView = true;
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(FitMapToViewport));
            return;
        }

        ShowUnavailableState("载入 Profile 后，这里会在副本期间读取真实地图。", "尚未绑定存档");
    }

    public async Task<BattleMapSnapshot?> LoadProfileAsync(
        SaveProfile profile,
        DsonSaveCodec codec,
        string? gameDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(codec);
        var profileDirectory = Path.GetFullPath(profile.ProfileDirectory);
        var generation = unchecked(++_profileGeneration);
        CancelScheduledRefreshRetry();
        StopProfileMonitoring();
        _profileDirectory = profileDirectory;
        _profileId = string.IsNullOrWhiteSpace(profile.ProfileId)
            ? Path.GetFileName(profileDirectory)
            : profile.ProfileId;
        _profile = profile;
        _snapshotReader = new BattleMapSnapshotReader(codec);
        _editService = new BattleMapEditService(codec);
        _currentSnapshot = null;
        ConfigureOriginalMapAssets(gameDirectory);
        ShowUnavailableState("正在安全读取当前副本地图……", "正在读取存档");

        BattleMapSnapshot? snapshot = null;
        try
        {
            var mapExists = File.Exists(Path.Combine(profileDirectory, "persist.map.json"));
            var raidExists = File.Exists(Path.Combine(profileDirectory, "persist.raid.json"));
            if (mapExists && raidExists)
            {
                snapshot = await LoadSnapshotWithRetryAsync(_snapshotReader, profileDirectory, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (generation != _profileGeneration)
                {
                    return null;
                }

                RenderSnapshot(snapshot, fitToView: true);
            }
            else if (!mapExists && !raidExists)
            {
                ShowUnavailableState(
                    "当前档案处于小镇；进入副本并等待游戏写盘后，地图会自动出现。",
                    "等待副本");
            }
            else
            {
                ShowUnavailableState(
                    "副本存档尚未写完整；编辑器会保留监听并自动重试。",
                    "等待完整存档");
                ScheduleRefreshRetry(generation);
            }
        }
        catch
        {
            if (generation == _profileGeneration)
            {
                ShowUnavailableState(
                    "当前副本地图暂时无法读取；编辑器会继续监听下一次完整写盘。",
                    "等待完整存档");
                ScheduleRefreshRetry(generation);
            }
            throw;
        }
        finally
        {
            if (generation == _profileGeneration)
            {
                StartProfileMonitoring();
            }
        }

        // Close the small initial-read/monitor-baseline race. If the game completed another
        // write between those operations, this hash comparison immediately adopts that state.
        if (generation == _profileGeneration)
        {
            await RefreshLiveSnapshotAsync(generation);
        }

        return snapshot;
    }

    public void ClearProfile() => SetRaidAvailability(false);

    private void ShowMapSurface()
    {
        _isRaidAvailable = true;
        MapCanvas.Visibility = Visibility.Visible;
        EmptyStatePanel.Visibility = Visibility.Collapsed;
        PrototypeBadge.Visibility = Visibility.Visible;
        ZoomOutButton.IsEnabled = true;
        ZoomInButton.IsEnabled = true;
        ResetZoomButton.IsEnabled = true;
        FitMapButton.IsEnabled = true;
    }

    private void ShowUnavailableState(string message, string liveStatus)
    {
        CloseActiveContextMenu();
        _isRaidAvailable = false;
        _selectedCell = null;
        _currentSnapshot = null;
        _cells.Clear();
        MapCanvas.Children.Clear();
        MapCanvas.Visibility = Visibility.Collapsed;
        EmptyStatePanel.Visibility = Visibility.Visible;
        EmptyStateTextBlock.Text = message;
        PrototypeBadge.Visibility = Visibility.Collapsed;
        ZoomOutButton.IsEnabled = false;
        ZoomInButton.IsEnabled = false;
        ResetZoomButton.IsEnabled = false;
        FitMapButton.IsEnabled = false;
        MapTitleTextBlock.Text = "当前副本地图";
        MapSelectionTextBlock.Text = "当前没有副本地图。";
        LiveStatusTextBlock.Text = liveStatus;
    }

    private void ConfigureOriginalMapAssets(string? gameDirectory)
    {
        string? normalizedDirectory = null;
        if (!string.IsNullOrWhiteSpace(gameDirectory))
        {
            try
            {
                normalizedDirectory = Path.GetFullPath(gameDirectory.Trim());
            }
            catch (Exception)
            {
                normalizedDirectory = null;
            }
        }

        if (!string.Equals(_gameDirectory, normalizedDirectory, StringComparison.OrdinalIgnoreCase))
        {
            _gameDirectory = normalizedDirectory;
            _mapAssetCache.Clear();
        }

        var panel = TryLoadGameImage(MapPanelAsset);
        var panelBrush = CreateOriginalMapBackdrop(panel);
        var room = TryLoadMapIcon("room_empty.png");
        var hall = TryLoadMapIcon("hall_clear.png");
        var party = TryLoadMapIcon("indicator.png");
        _usesOriginalMapAssets = panelBrush is not null && room is not null && hall is not null && party is not null;

        MapViewport.Background = panelBrush is null
            ? new SolidColorBrush(Color.FromRgb(3, 3, 3))
            : panelBrush;

        UpdateMapBadge();
    }

    private void UpdateMapBadge()
    {
        PrototypeBadgeTextBlock.Text = "全局视野";
        PrototypeBadge.ToolTip = _usesOriginalMapAssets
            ? null
            : "未找到完整的原版地图素材，当前使用简化占位显示。";
    }

    private static ImageBrush? CreateOriginalMapBackdrop(ImageSource? source)
    {
        if (source is not BitmapSource bitmap || bitmap.PixelWidth < 660 || bitmap.PixelHeight < 344)
        {
            return null;
        }

        // panel_map.png also contains the game's map/inventory tab buttons on its right edge.
        // Crop the neutral grid interior in source pixels so DPI metadata cannot shrink it into a corner.
        var gridTexture = new CroppedBitmap(bitmap, new Int32Rect(12, 18, 648, 326));
        gridTexture.Freeze();
        return new ImageBrush(gridTexture)
        {
            Viewbox = new Rect(0, 0, 1, 1),
            ViewboxUnits = BrushMappingMode.RelativeToBoundingBox,
            Viewport = new Rect(0, 0, 1, 1),
            ViewportUnits = BrushMappingMode.RelativeToBoundingBox,
            TileMode = TileMode.None,
            Stretch = Stretch.Fill,
            AlignmentX = AlignmentX.Center,
            AlignmentY = AlignmentY.Center,
            Opacity = 0.92
        };
    }

    private ImageSource? TryLoadMapIcon(string fileName) =>
        TryLoadGameImage(Path.Combine(MapIconDirectory, fileName));

    private ImageSource? TryLoadGameImage(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(_gameDirectory))
        {
            return null;
        }

        if (_mapAssetCache.TryGetValue(relativePath, out var cached))
        {
            return cached;
        }

        ImageSource? source = null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.UriSource = new Uri(Path.Combine(_gameDirectory, relativePath), UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            source = image;
        }
        catch (Exception)
        {
            // The editor remains usable when an installation is incomplete or customized.
        }

        _mapAssetCache[relativePath] = source;
        return source;
    }

    private static async Task<BattleMapSnapshot> LoadSnapshotWithRetryAsync(
        BattleMapSnapshotReader reader,
        string profileDirectory,
        CancellationToken cancellationToken)
    {
        const int attemptCount = 3;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await reader.LoadAsync(profileDirectory, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch when (attempt < attemptCount)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken);
            }
        }
    }

    private void StartProfileMonitoring()
    {
        if (_profileDirectory is null || _profileMonitor is not null)
        {
            return;
        }

        try
        {
            _profileMonitor = new ProfileSaveMonitor(
                _profileDirectory,
                ["persist.map.json", "persist.raid.json"]);
            _profileMonitor.Changed += ProfileMonitor_Changed;
            _profileMonitor.Start();
        }
        catch (Exception ex)
        {
            StopProfileMonitoring();
            LiveStatusTextBlock.Text = $"自动刷新不可用：{ex.Message}";
        }
    }

    private void StopProfileMonitoring()
    {
        if (_profileMonitor is null)
        {
            return;
        }

        _profileMonitor.Changed -= ProfileMonitor_Changed;
        _profileMonitor.Dispose();
        _profileMonitor = null;
    }

    private void CancelScheduledRefreshRetry()
    {
        var cancellation = _refreshRetryCancellation;
        _refreshRetryCancellation = null;
        if (cancellation is null)
        {
            return;
        }

        cancellation.Cancel();
        cancellation.Dispose();
    }

    private void ScheduleRefreshRetry(int generation)
    {
        if (generation != _profileGeneration ||
            _profileDirectory is null ||
            _snapshotReader is null ||
            _refreshRetryCancellation is not null)
        {
            return;
        }

        var cancellation = new CancellationTokenSource();
        _refreshRetryCancellation = cancellation;
        _ = RetryLiveSnapshotAfterDelayAsync(generation, cancellation);
    }

    private async Task RetryLiveSnapshotAfterDelayAsync(
        int generation,
        CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(LiveRefreshRetryDelay, cancellation.Token);
            if (generation != _profileGeneration || cancellation.IsCancellationRequested)
            {
                return;
            }

            if (ReferenceEquals(_refreshRetryCancellation, cancellation))
            {
                _refreshRetryCancellation = null;
            }
            await RefreshLiveSnapshotAsync(generation);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_refreshRetryCancellation, cancellation))
            {
                _refreshRetryCancellation = null;
            }
            cancellation.Dispose();
        }
    }

    private void ProfileMonitor_Changed(object? sender, ProfileSaveFilesChangedEventArgs e)
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            return;
        }

        var generation = _profileGeneration;
        try
        {
            _ = Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() => _ = RefreshLiveSnapshotAsync(generation)));
        }
        catch (InvalidOperationException)
        {
            // The owning window can finish shutting down between the check and dispatch.
        }
    }

    private async Task RefreshLiveSnapshotAsync(int generation)
    {
        await _refreshGate.WaitAsync();
        try
        {
            if (generation != _profileGeneration ||
                _profileDirectory is null ||
                _snapshotReader is null)
            {
                return;
            }

            var mapExists = File.Exists(Path.Combine(_profileDirectory, "persist.map.json"));
            var raidExists = File.Exists(Path.Combine(_profileDirectory, "persist.raid.json"));
            if (!mapExists || !raidExists)
            {
                if (mapExists != raidExists)
                {
                    if (_currentSnapshot is null)
                    {
                        ShowUnavailableState(
                            "副本存档尚未写完整；编辑器会保留监听并自动重试。",
                            "等待完整存档");
                    }
                    else
                    {
                        LiveStatusTextBlock.Text = "保留上一快照 · 等待另一份副本存档";
                    }
                    ScheduleRefreshRetry(generation);
                    return;
                }

                CancelScheduledRefreshRetry();
                ShowUnavailableState(
                    "当前档案已返回小镇；下一次进入副本并写盘后，地图会自动出现。",
                    "等待副本");
                return;
            }

            LiveStatusTextBlock.Text = "检测到存档变化 · 正在安全重读";
            var snapshot = await LoadSnapshotWithRetryAsync(
                _snapshotReader,
                _profileDirectory,
                CancellationToken.None);
            if (generation != _profileGeneration)
            {
                return;
            }

            if (_currentSnapshot is not null &&
                _currentSnapshot.MapSha256.Equals(snapshot.MapSha256, StringComparison.OrdinalIgnoreCase) &&
                _currentSnapshot.RaidSha256.Equals(snapshot.RaidSha256, StringComparison.OrdinalIgnoreCase))
            {
                LiveStatusTextBlock.Text =
                    $"已同步 {snapshot.ReadAtUtc.ToLocalTime():HH:mm:ss}";
                CancelScheduledRefreshRetry();
                return;
            }

            RenderSnapshot(snapshot, fitToView: _currentSnapshot is null || _fitToView);
        }
        catch (Exception ex)
        {
            if (generation != _profileGeneration)
            {
                return;
            }

            if (_currentSnapshot is null)
            {
                ShowUnavailableState(
                    "游戏正在写入地图，暂时无法取得完整快照；编辑器会继续自动重试。",
                    "等待完整存档");
            }
            else
            {
                LiveStatusTextBlock.Text = $"保留上一快照 · 等待重试：{ex.Message}";
            }
            ScheduleRefreshRetry(generation);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private void RenderSnapshot(BattleMapSnapshot snapshot, bool fitToView)
    {
        BuildSnapshotMap(snapshot);
        _selectedCell = null;
        CancelScheduledRefreshRetry();
        _currentSnapshot = snapshot;
        ShowMapSurface();
        UpdateMapBadge();
        MapTitleTextBlock.Text =
            $"{_profileId ?? "当前档案"} · {FormatDungeon(snapshot.DungeonId)} · 等级 {snapshot.Difficulty}";
        MapSelectionTextBlock.Text =
            $"{snapshot.RoomCount} 个房间 · {snapshot.CorridorCount} 条走廊 · " +
            $"{_cells.Count} 个可操作格";
        LiveStatusTextBlock.Text =
            $"已同步 {snapshot.ReadAtUtc.ToLocalTime():HH:mm:ss}";
        SnapshotRefreshed?.Invoke(snapshot);

        if (fitToView)
        {
            _fitToView = true;
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(FitMapToViewport));
        }
    }

    private void BuildSnapshotMap(BattleMapSnapshot snapshot)
    {
        var visibleTiles = snapshot.Areas
            .SelectMany(area => area.Kind == BattleMapAreaKind.Corridor
                ? area.Tiles.Where(tile => tile.StaticType == 1)
                : area.Tiles)
            .ToArray();
        if (visibleTiles.Length == 0)
        {
            throw new InvalidDataException("当前地图没有可读取的房间或走廊格。");
        }

        var minimumX = visibleTiles.Min(tile => tile.MapX);
        var maximumX = visibleTiles.Max(tile => tile.MapX);
        var minimumY = visibleTiles.Min(tile => tile.MapY);
        var maximumY = visibleTiles.Max(tile => tile.MapY);
        var stagedWidth = Math.Max(320, (maximumX - minimumX) * MapGridTileSize + MapContentMargin * 2);
        var stagedHeight = Math.Max(240, (maximumY - minimumY) * MapGridTileSize + MapContentMargin * 2);
        var stagedCells = new List<PrototypeMapCell>(visibleTiles.Length);

        foreach (var area in snapshot.Areas)
        {
            foreach (var tile in area.Tiles.Where(tile =>
                         area.Kind != BattleMapAreaKind.Corridor || tile.StaticType == 1))
            {
                var kind = area.Kind == BattleMapAreaKind.Room
                    ? PrototypeCellKind.Room
                    : PrototypeCellKind.Corridor;
                var isHiddenSystemContent = tile.Content == BattleMapTileContent.Hunger;
                var content = ConvertContent(tile.Content);
                var contentLabel = isHiddenSystemContent
                    ? null
                    : FormatSnapshotContent(tile.Content, tile.RawContent);
                if (kind == PrototypeCellKind.Room &&
                    string.Equals(area.AreaId, snapshot.EntranceAreaId, StringComparison.OrdinalIgnoreCase))
                {
                    content = PrototypeContent.Entrance;
                    contentLabel = "入口";
                }
                else if (kind == PrototypeCellKind.Room &&
                         string.Equals(area.AreaId, snapshot.FinalRoomId, StringComparison.OrdinalIgnoreCase))
                {
                    content = PrototypeContent.Boss;
                    contentLabel = "首领房间";
                }

                var centerX = MapContentMargin + (tile.MapX - minimumX) * MapGridTileSize;
                var centerY = MapContentMargin + (tile.MapY - minimumY) * MapGridTileSize;
                var displayName = kind switch
                {
                    PrototypeCellKind.Room => $"房间 {area.AreaId}",
                    _ => $"走廊格 {area.AreaId}.{tile.TileId}"
                };
                var hasParty =
                    string.Equals(area.AreaId, snapshot.PartyAreaId, StringComparison.OrdinalIgnoreCase) &&
                    snapshot.PartyTileIndex == tile.TileIndex;
                stagedCells.Add(CreateCell(
                    $"{area.AreaId}.{tile.TileId}",
                    displayName,
                    kind,
                    centerX,
                    centerY,
                    ConvertKnowledge(tile.Knowledge),
                    content,
                    hasParty,
                    contentLabel,
                    tile.RawContent,
                    tile.MashIndex,
                    tile.MashType,
                    area.AreaId,
                    tile.TileId));
            }
        }

        // Construct every visual before touching the live canvas. A malformed or partially
        // written snapshot can therefore never erase or half-replace the previous complete map.
        CloseActiveContextMenu();
        MapCanvas.Children.Clear();
        _cells.Clear();
        MapCanvas.Width = stagedWidth;
        MapCanvas.Height = stagedHeight;
        foreach (var cell in stagedCells)
        {
            _cells.Add(cell);
            AttachCellToMap(cell);
        }
    }

    private static PrototypeKnowledge ConvertKnowledge(BattleMapTileKnowledge knowledge) => knowledge switch
    {
        BattleMapTileKnowledge.Scouted => PrototypeKnowledge.Scouted,
        BattleMapTileKnowledge.Visited => PrototypeKnowledge.Visited,
        _ => PrototypeKnowledge.Unknown
    };

    private static PrototypeContent ConvertContent(BattleMapTileContent content) => content switch
    {
        BattleMapTileContent.Battle or BattleMapTileContent.Ambush => PrototypeContent.Battle,
        BattleMapTileContent.Trap => PrototypeContent.Trap,
        BattleMapTileContent.Obstacle => PrototypeContent.Obstacle,
        BattleMapTileContent.Happening or
            BattleMapTileContent.GuardedCurio or
            BattleMapTileContent.Curio or
            BattleMapTileContent.AmbushCurio => PrototypeContent.Curio,
        BattleMapTileContent.Treasure or
            BattleMapTileContent.GuardedTreasure or
            BattleMapTileContent.AmbushTreasure => PrototypeContent.Treasure,
        BattleMapTileContent.SecretDoor => PrototypeContent.SecretDoor,
        _ => PrototypeContent.None
    };

    private static string FormatSnapshotContent(BattleMapTileContent content, int rawContent) => content switch
    {
        BattleMapTileContent.Nothing => "空白",
        BattleMapTileContent.Battle => "战斗",
        BattleMapTileContent.Ambush => "伏击",
        BattleMapTileContent.Trap => "陷阱",
        BattleMapTileContent.Obstacle => "障碍",
        BattleMapTileContent.Happening => "事件",
        BattleMapTileContent.GuardedCurio => "守卫奇物",
        BattleMapTileContent.Curio => "奇物",
        BattleMapTileContent.Hunger => "空白",
        BattleMapTileContent.Treasure => "宝藏",
        BattleMapTileContent.GuardedTreasure => "守卫宝藏",
        BattleMapTileContent.AmbushCurio => "伏击奇物",
        BattleMapTileContent.AmbushTreasure => "伏击宝藏",
        BattleMapTileContent.SecretDoor => "秘密房间入口",
        _ => $"未知内容（{rawContent}）"
    };

    private static string FormatDungeon(string dungeonId)
    {
        var name = dungeonId.ToLowerInvariant() switch
        {
            "ruins" => "遗迹",
            "warrens" => "兽窟",
            "weald" => "荒野",
            "cove" => "海湾",
            "courtyard" => "庭院",
            "farmstead" => "农场",
            "darkestdungeon" => "极暗地牢",
            _ => string.Empty
        };
        return string.IsNullOrWhiteSpace(name) ? dungeonId : $"{name} / {dungeonId}";
    }

    private void BattleMapView_Loaded(object sender, RoutedEventArgs e)
    {
        StartProfileMonitoring();
        if (_isRaidAvailable)
        {
            FitMapToViewport();
        }
        if (_profileDirectory is not null)
        {
            _ = RefreshLiveSnapshotAsync(_profileGeneration);
        }
    }

    private void BattleMapView_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_isRaidAvailable && _fitToView)
        {
            FitMapToViewport();
        }
    }

    private void BattleMapView_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!_isRaidAvailable)
        {
            return;
        }

        var factor = e.Delta > 0 ? 1.12 : 1 / 1.12;
        SetZoom(MapScaleTransform.ScaleX * factor, e.GetPosition(MapViewport));
        _fitToView = false;
        e.Handled = true;
    }

    private void BattleMapView_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_isRaidAvailable)
        {
            return;
        }

        _isRightButtonDown = true;
        _isPanning = false;
        _rightButtonOrigin = e.GetPosition(MapViewport);
        _panOrigin = new Point(MapTranslateTransform.X, MapTranslateTransform.Y);
    }

    private void BattleMapView_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isRaidAvailable || !_isRightButtonDown || e.RightButton != MouseButtonState.Pressed)
        {
            return;
        }

        var current = e.GetPosition(MapViewport);
        var delta = current - _rightButtonOrigin;
        if (!_isPanning && Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y) >= PanThreshold)
        {
            _isPanning = true;
            _fitToView = false;
            MapViewport.Cursor = Cursors.SizeAll;
            _ = Mouse.Capture(MapViewport, CaptureMode.Element);
        }

        if (!_isPanning)
        {
            return;
        }

        MapTranslateTransform.X = _panOrigin.X + delta.X;
        MapTranslateTransform.Y = _panOrigin.Y + delta.Y;
        e.Handled = true;
    }

    private void BattleMapView_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isRightButtonDown = false;
        if (!_isPanning)
        {
            return;
        }

        _isPanning = false;
        MapViewport.Cursor = Cursors.Arrow;
        if (ReferenceEquals(Mouse.Captured, MapViewport))
        {
            Mouse.Capture(null);
        }

        e.Handled = true;
    }

    private void ZoomOutButton_Click(object sender, RoutedEventArgs e)
    {
        ZoomAroundViewportCenter(1 / 1.15);
    }

    private void ZoomInButton_Click(object sender, RoutedEventArgs e)
    {
        ZoomAroundViewportCenter(1.15);
    }

    private void ResetZoomButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isRaidAvailable)
        {
            return;
        }

        _fitToView = false;
        CenterMapAtScale(1);
    }

    private void FitMapButton_Click(object sender, RoutedEventArgs e)
    {
        _fitToView = true;
        FitMapToViewport();
    }

    private void ZoomAroundViewportCenter(double factor)
    {
        if (!_isRaidAvailable)
        {
            return;
        }

        var center = new Point(MapViewport.ActualWidth / 2, MapViewport.ActualHeight / 2);
        SetZoom(MapScaleTransform.ScaleX * factor, center);
        _fitToView = false;
    }

    private void SetZoom(double requestedScale, Point anchor)
    {
        var oldScale = MapScaleTransform.ScaleX;
        var newScale = Math.Clamp(requestedScale, MinimumZoom, MaximumZoom);
        if (Math.Abs(newScale - oldScale) < 0.0001)
        {
            return;
        }

        var contentX = (anchor.X - MapTranslateTransform.X) / oldScale;
        var contentY = (anchor.Y - MapTranslateTransform.Y) / oldScale;
        MapScaleTransform.ScaleX = newScale;
        MapScaleTransform.ScaleY = newScale;
        MapTranslateTransform.X = anchor.X - contentX * newScale;
        MapTranslateTransform.Y = anchor.Y - contentY * newScale;
        UpdateZoomText();
    }

    private void FitMapToViewport()
    {
        if (!_isRaidAvailable || MapViewport.ActualWidth <= 0 || MapViewport.ActualHeight <= 0)
        {
            return;
        }

        var horizontalScale = (MapViewport.ActualWidth - 56) / MapCanvas.Width;
        var verticalScale = (MapViewport.ActualHeight - 56) / MapCanvas.Height;
        CenterMapAtScale(Math.Clamp(Math.Min(horizontalScale, verticalScale), MinimumZoom, 1.25));
    }

    private void CenterMapAtScale(double scale)
    {
        scale = Math.Clamp(scale, MinimumZoom, MaximumZoom);
        MapScaleTransform.ScaleX = scale;
        MapScaleTransform.ScaleY = scale;
        MapTranslateTransform.X = (MapViewport.ActualWidth - MapCanvas.Width * scale) / 2;
        MapTranslateTransform.Y = (MapViewport.ActualHeight - MapCanvas.Height * scale) / 2;
        UpdateZoomText();
    }

    private void UpdateZoomText()
    {
        ZoomTextBlock.Text = $"{MapScaleTransform.ScaleX * 100:0}%";
    }

    private void BuildPrototypeMap()
    {
        CloseActiveContextMenu();
        MapCanvas.Children.Clear();
        _cells.Clear();

        AddCell("rooA", "入口房间", PrototypeCellKind.Room, 80, 80,
            PrototypeKnowledge.Visited, PrototypeContent.Entrance, hasParty: false);
        AddCell("coAB.0", "走廊", PrototypeCellKind.Corridor, 124, 80,
            PrototypeKnowledge.Visited, PrototypeContent.None, hasParty: false);
        AddCell("coAB.1", "走廊", PrototypeCellKind.Corridor, 148, 80,
            PrototypeKnowledge.Visited, PrototypeContent.Curio, hasParty: false);
        AddCell("coAB.2", "走廊", PrototypeCellKind.Corridor, 172, 80,
            PrototypeKnowledge.Visited, PrototypeContent.None, hasParty: false);
        AddCell("coAB.3", "走廊", PrototypeCellKind.Corridor, 196, 80,
            PrototypeKnowledge.Visited, PrototypeContent.None, hasParty: true);
        AddCell("coAB.4", "走廊", PrototypeCellKind.Corridor, 220, 80,
            PrototypeKnowledge.Scouted, PrototypeContent.None, hasParty: false);
        AddCell("coAB.5", "走廊", PrototypeCellKind.Corridor, 244, 80,
            PrototypeKnowledge.Scouted, PrototypeContent.Trap, hasParty: false);
        AddCell("coAB.6", "走廊", PrototypeCellKind.Corridor, 268, 80,
            PrototypeKnowledge.Scouted, PrototypeContent.None, hasParty: false);
        AddCell("coAB.7", "走廊", PrototypeCellKind.Corridor, 292, 80,
            PrototypeKnowledge.Scouted, PrototypeContent.None, hasParty: false);
        AddCell("rooB", "十字房间", PrototypeCellKind.Room, 336, 80,
            PrototypeKnowledge.Scouted, PrototypeContent.Battle, hasParty: false);

        AddCell("coBC.0", "走廊", PrototypeCellKind.Corridor, 380, 80,
            PrototypeKnowledge.Unknown, PrototypeContent.None, hasParty: false);
        AddCell("coBC.1", "未知走廊", PrototypeCellKind.Corridor, 404, 80,
            PrototypeKnowledge.Unknown, PrototypeContent.None, hasParty: false);
        AddCell("coBC.2", "未知走廊", PrototypeCellKind.Corridor, 428, 80,
            PrototypeKnowledge.Unknown, PrototypeContent.None, hasParty: false);
        AddCell("coBC.3", "未知走廊", PrototypeCellKind.Corridor, 452, 80,
            PrototypeKnowledge.Unknown, PrototypeContent.None, hasParty: false);
        AddCell("coBC.4", "未知走廊", PrototypeCellKind.Corridor, 476, 80,
            PrototypeKnowledge.Unknown, PrototypeContent.None, hasParty: false);
        AddCell("coBC.5", "未知走廊", PrototypeCellKind.Corridor, 500, 80,
            PrototypeKnowledge.Unknown, PrototypeContent.None, hasParty: false);
        AddCell("coBC.6", "未知走廊", PrototypeCellKind.Corridor, 524, 80,
            PrototypeKnowledge.Unknown, PrototypeContent.None, hasParty: false);
        AddCell("coBC.7", "走廊", PrototypeCellKind.Corridor, 548, 80,
            PrototypeKnowledge.Unknown, PrototypeContent.None, hasParty: false);
        AddCell("rooC", "宝藏房间", PrototypeCellKind.Room, 592, 80,
            PrototypeKnowledge.Unknown, PrototypeContent.Treasure, hasParty: false);

        AddCell("coBD.0", "走廊", PrototypeCellKind.Corridor, 336, 124,
            PrototypeKnowledge.Visited, PrototypeContent.None, hasParty: false);
        AddCell("coBD.1", "走廊", PrototypeCellKind.Corridor, 336, 148,
            PrototypeKnowledge.Visited, PrototypeContent.None, hasParty: false);
        AddCell("coBD.2", "走廊", PrototypeCellKind.Corridor, 336, 172,
            PrototypeKnowledge.Visited, PrototypeContent.None, hasParty: false);
        AddCell("coBD.3", "走廊", PrototypeCellKind.Corridor, 336, 196,
            PrototypeKnowledge.Visited, PrototypeContent.None, hasParty: false);
        AddCell("coBD.4", "走廊", PrototypeCellKind.Corridor, 336, 220,
            PrototypeKnowledge.Completed, PrototypeContent.None, hasParty: false);
        AddCell("coBD.5", "走廊", PrototypeCellKind.Corridor, 336, 244,
            PrototypeKnowledge.Completed, PrototypeContent.Curio, hasParty: false);
        AddCell("coBD.6", "走廊", PrototypeCellKind.Corridor, 336, 268,
            PrototypeKnowledge.Completed, PrototypeContent.None, hasParty: false);
        AddCell("coBD.7", "走廊", PrototypeCellKind.Corridor, 336, 292,
            PrototypeKnowledge.Completed, PrototypeContent.None, hasParty: false);
        AddCell("rooD", "已完成房间", PrototypeCellKind.Room, 336, 336,
            PrototypeKnowledge.Completed, PrototypeContent.Curio, hasParty: false);

        AddCell("coDE.0", "走廊", PrototypeCellKind.Corridor, 380, 336,
            PrototypeKnowledge.Visited, PrototypeContent.None, hasParty: false);
        AddCell("coDE.1", "走廊", PrototypeCellKind.Corridor, 404, 336,
            PrototypeKnowledge.Visited, PrototypeContent.Obstacle, hasParty: false);
        AddCell("coDE.2", "走廊", PrototypeCellKind.Corridor, 428, 336,
            PrototypeKnowledge.Visited, PrototypeContent.None, hasParty: false);
        AddCell("coDE.3", "走廊", PrototypeCellKind.Corridor, 452, 336,
            PrototypeKnowledge.Visited, PrototypeContent.None, hasParty: false);
        AddCell("coDE.4", "走廊", PrototypeCellKind.Corridor, 476, 336,
            PrototypeKnowledge.Visited, PrototypeContent.None, hasParty: false);
        AddCell("coDE.5", "走廊", PrototypeCellKind.Corridor, 500, 336,
            PrototypeKnowledge.Visited, PrototypeContent.None, hasParty: false);
        AddCell("coDE.6", "走廊", PrototypeCellKind.Corridor, 524, 336,
            PrototypeKnowledge.Visited, PrototypeContent.None, hasParty: false);
        AddCell("coDE.7", "走廊", PrototypeCellKind.Corridor, 548, 336,
            PrototypeKnowledge.Visited, PrototypeContent.None, hasParty: false);
        AddCell("rooE", "首领预览房间", PrototypeCellKind.Room, 592, 336,
            PrototypeKnowledge.Visited, PrototypeContent.Boss, hasParty: false);

        AddCell("coAF.0", "走廊", PrototypeCellKind.Corridor, 80, 124,
            PrototypeKnowledge.Scouted, PrototypeContent.None, hasParty: false);
        AddCell("coAF.1", "走廊", PrototypeCellKind.Corridor, 80, 148,
            PrototypeKnowledge.Scouted, PrototypeContent.Curio, hasParty: false);
        AddCell("coAF.2", "走廊", PrototypeCellKind.Corridor, 80, 172,
            PrototypeKnowledge.Scouted, PrototypeContent.None, hasParty: false);
        AddCell("coAF.3", "走廊", PrototypeCellKind.Corridor, 80, 196,
            PrototypeKnowledge.Scouted, PrototypeContent.None, hasParty: false);
        AddCell("coAF.4", "走廊", PrototypeCellKind.Corridor, 80, 220,
            PrototypeKnowledge.Scouted, PrototypeContent.None, hasParty: false);
        AddCell("coAF.5", "走廊", PrototypeCellKind.Corridor, 80, 244,
            PrototypeKnowledge.Scouted, PrototypeContent.None, hasParty: false);
        AddCell("coAF.6", "走廊", PrototypeCellKind.Corridor, 80, 268,
            PrototypeKnowledge.Scouted, PrototypeContent.None, hasParty: false);
        AddCell("coAF.7", "走廊", PrototypeCellKind.Corridor, 80, 292,
            PrototypeKnowledge.Scouted, PrototypeContent.None, hasParty: false);
        AddCell("rooF", "支路房间", PrototypeCellKind.Room, 80, 336,
            PrototypeKnowledge.Scouted, PrototypeContent.None, hasParty: false);
    }

    private void AddCell(
        string id,
        string displayName,
        PrototypeCellKind kind,
        double centerX,
        double centerY,
        PrototypeKnowledge knowledge,
        PrototypeContent content,
        bool hasParty,
        string? contentLabel = null,
        int rawContent = 0,
        int mashIndex = -1,
        int mashType = 7,
        string? sourceAreaId = null,
        string? sourceTileId = null)
    {
        var cell = CreateCell(
            id,
            displayName,
            kind,
            centerX,
            centerY,
            knowledge,
            content,
            hasParty,
            contentLabel,
            rawContent,
            mashIndex,
            mashType,
            sourceAreaId,
            sourceTileId);
        _cells.Add(cell);
        AttachCellToMap(cell);
    }

    private void AttachCellToMap(PrototypeMapCell cell)
    {
        MapCanvas.Children.Add(cell.Visual);
    }

    private PrototypeMapCell CreateCell(
        string id,
        string displayName,
        PrototypeCellKind kind,
        double centerX,
        double centerY,
        PrototypeKnowledge knowledge,
        PrototypeContent content,
        bool hasParty,
        string? contentLabel = null,
        int rawContent = 0,
        int mashIndex = -1,
        int mashType = 7,
        string? sourceAreaId = null,
        string? sourceTileId = null)
    {
        var size = kind == PrototypeCellKind.Room ? 64 : 24;
        var baseImage = new Image
        {
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            IsHitTestVisible = false
        };
        RenderOptions.SetBitmapScalingMode(baseImage, BitmapScalingMode.HighQuality);
        var contentMarkerImage = new Image
        {
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        RenderOptions.SetBitmapScalingMode(contentMarkerImage, BitmapScalingMode.HighQuality);
        var completionMarkerImage = new Image
        {
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        RenderOptions.SetBitmapScalingMode(completionMarkerImage, BitmapScalingMode.HighQuality);
        var partyMarkerImage = new Image
        {
            Width = kind == PrototypeCellKind.Room ? 43 : 27,
            Height = kind == PrototypeCellKind.Room ? 41 : 26,
            Margin = kind == PrototypeCellKind.Room
                ? new Thickness(0, -7, 0, 0)
                : new Thickness(0, -9, 0, 0),
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        RenderOptions.SetBitmapScalingMode(partyMarkerImage, BitmapScalingMode.HighQuality);
        var fallbackIcon = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            FontFamily = new FontFamily("SimSun, Segoe UI Symbol"),
            FontSize = kind == PrototypeCellKind.Room ? 25 : 14,
            FontWeight = FontWeights.Bold,
            IsHitTestVisible = false
        };
        var selectionOverlay = new Border
        {
            BorderThickness = new Thickness(2),
            BorderBrush = Brushes.Transparent,
            Background = Brushes.Transparent,
            IsHitTestVisible = false
        };
        var visual = new Grid
        {
            Width = size,
            Height = size,
            Background = Brushes.Transparent,
            SnapsToDevicePixels = true,
            Cursor = Cursors.Hand
        };
        visual.Children.Add(baseImage);
        visual.Children.Add(contentMarkerImage);
        visual.Children.Add(completionMarkerImage);
        visual.Children.Add(fallbackIcon);
        visual.Children.Add(partyMarkerImage);
        visual.Children.Add(selectionOverlay);
        Canvas.SetLeft(visual, centerX - size / 2);
        Canvas.SetTop(visual, centerY - size / 2);

        var cell = new PrototypeMapCell(
            id,
            displayName,
            kind,
            knowledge,
            content,
            hasParty,
            visual,
            baseImage,
            contentMarkerImage,
            completionMarkerImage,
            partyMarkerImage,
            fallbackIcon,
            selectionOverlay,
            rawContent,
            mashIndex,
            mashType,
            sourceAreaId,
            sourceTileId)
        {
            ContentLabel = contentLabel
        };
        visual.PreviewMouseLeftButtonDown += (_, e) =>
        {
            SelectCell(cell);
            e.Handled = true;
        };
        visual.PreviewMouseRightButtonDown += (_, _) =>
        {
            SelectCell(cell);
            visual.ContextMenu = BuildContextMenu(cell);
        };

        RefreshCellVisual(cell);
        return cell;
    }

    private ContextMenu BuildContextMenu(PrototypeMapCell cell)
    {
        CloseActiveContextMenu();
        var isHiddenSystemContent = IsHiddenSystemContent(cell);
        var hasPersistedContent = cell.SourceAreaId is null
            ? cell.Content != PrototypeContent.None
            : cell.RawContent != 0;
        var contextMenu = new ContextMenu
        {
            Style = (Style)FindResource("BattleMapContextMenuStyle"),
            PlacementTarget = cell.Visual
        };
        if (!isHiddenSystemContent)
        {
            var changeRoot = CreateMenuItem(
                hasPersistedContent ? "替换" : "新建",
                hasPersistedContent ? "↻" : "＋");
            PopulateContentChoices(changeRoot, cell);
            contextMenu.Items.Add(changeRoot);

            if (hasPersistedContent)
            {
                contextMenu.Items.Add(CreateAsyncActionMenuItem(
                    "删除",
                    "×",
                    () => DeleteContentAsync(cell)));
            }

            contextMenu.Items.Add(new Separator
            {
                Style = (Style)FindResource("BattleMapSeparatorStyle")
            });
        }
        var moveItem = CreateAsyncActionMenuItem(
            "移动队伍到此",
            "◆",
            () => MovePartyAsync(cell));
        moveItem.IsEnabled = !cell.HasParty && !_isApplyingEdit;
        contextMenu.Items.Add(moveItem);
        contextMenu.Opened += (_, _) =>
        {
            if (!_cells.Contains(cell))
            {
                contextMenu.IsOpen = false;
                return;
            }
            _activeContextMenu = contextMenu;
        };
        contextMenu.Closed += (_, _) =>
        {
            if (ReferenceEquals(_activeContextMenu, contextMenu))
            {
                _activeContextMenu = null;
            }
        };
        _activeContextMenu = contextMenu;
        return contextMenu;
    }

    private static bool IsHiddenSystemContent(PrototypeMapCell cell) =>
        cell.SourceAreaId is not null &&
        cell.RawContent == (int)BattleMapTileContent.Hunger;

    private void CloseActiveContextMenu()
    {
        var contextMenu = _activeContextMenu;
        _activeContextMenu = null;
        if (contextMenu?.IsOpen == true)
        {
            contextMenu.IsOpen = false;
        }
    }

    private void PopulateContentChoices(MenuItem root, PrototypeMapCell cell)
    {
        root.Items.Add(CreateActionMenuItem("奇物", "?", () =>
            ApplyContentPreview(cell, PrototypeContent.Curio, "奇物")));

        var battleMenu = CreateMenuItem("战斗", "⚔");
        battleMenu.Items.Add(CreateActionMenuItem("普通遭遇", "⚔", () =>
            ApplyContentPreview(cell, PrototypeContent.Battle, "普通遭遇")));
        if (cell.Kind == PrototypeCellKind.Room)
        {
            var bossMenu = CreateMenuItem("首领遭遇", "☠");
            var baseBosses = CreateMenuItem("原版 / BASE", "B");
            baseBosses.Items.Add(CreateActionMenuItem("死灵法师 / Necromancer", "☠", () =>
                ApplyContentPreview(cell, PrototypeContent.Boss, "死灵法师 / Necromancer")));
            baseBosses.Items.Add(CreateActionMenuItem("海妖 / Siren", "☠", () =>
                ApplyContentPreview(cell, PrototypeContent.Boss, "海妖 / Siren")));
            var dlcBosses = CreateMenuItem("官方 DLC / DLC", "D");
            dlcBosses.Items.Add(CreateActionMenuItem("狂信者 / Fanatic", "☠", () =>
                ApplyContentPreview(cell, PrototypeContent.Boss, "狂信者 / Fanatic")));
            var modBosses = CreateMenuItem("Mod / MODS", "M");
            modBosses.Items.Add(CreateActionMenuItem("示例 Mod 首领 / Sample Mod Boss", "☠", () =>
                ApplyContentPreview(cell, PrototypeContent.Boss, "示例 Mod 首领 / Sample Mod Boss")));
            bossMenu.Items.Add(baseBosses);
            bossMenu.Items.Add(dlcBosses);
            bossMenu.Items.Add(modBosses);
            battleMenu.Items.Add(bossMenu);
        }
        root.Items.Add(battleMenu);

        root.Items.Add(CreateActionMenuItem("宝藏", "✦", () =>
            ApplyContentPreview(cell, PrototypeContent.Treasure, "宝藏")));
        if (cell.Kind != PrototypeCellKind.Room)
        {
            root.Items.Add(CreateActionMenuItem("陷阱", "!", () =>
                ApplyContentPreview(cell, PrototypeContent.Trap, "陷阱")));
            root.Items.Add(CreateActionMenuItem("障碍", "▰", () =>
                ApplyContentPreview(cell, PrototypeContent.Obstacle, "障碍")));
        }
    }

    private MenuItem CreateMenuItem(string header, string icon)
    {
        return new MenuItem
        {
            Header = header,
            Icon = new TextBlock
            {
                Text = icon,
                Foreground = new SolidColorBrush(Color.FromRgb(212, 190, 115)),
                HorizontalAlignment = HorizontalAlignment.Center,
                FontWeight = FontWeights.Bold
            },
            Style = (Style)FindResource("BattleMapMenuItemStyle")
        };
    }

    private MenuItem CreateActionMenuItem(string header, string icon, Action action)
    {
        var menuItem = CreateMenuItem(header, icon);
        menuItem.Click += (_, _) => action();
        return menuItem;
    }

    private MenuItem CreateAsyncActionMenuItem(string header, string icon, Func<Task> action)
    {
        var menuItem = CreateMenuItem(header, icon);
        menuItem.Click += async (_, _) => await action();
        return menuItem;
    }

    private void ApplyContentPreview(
        PrototypeMapCell cell,
        PrototypeContent content,
        string contentLabel)
    {
        cell.Content = content;
        cell.ContentLabel = contentLabel;
        RefreshCellVisual(cell);
        MapSelectionTextBlock.Text =
            $"已在界面预览中把 {cell.DisplayName} 设置为“{contentLabel}”；存档未发生变化。";
    }

    private async Task DeleteContentAsync(PrototypeMapCell target)
    {
        if (_currentSnapshot is null)
        {
            target.Content = PrototypeContent.None;
            target.ContentLabel = null;
            RefreshCellVisual(target);
            MapSelectionTextBlock.Text =
                $"已在界面预览中清空 {target.DisplayName}；存档未发生变化。";
            return;
        }
        if (!TryGetWritableContext(target, out var owner, out var profile, out var snapshot, out var service))
        {
            MapSelectionTextBlock.Text = "当前地图正在刷新或处理其他操作，请重新选择目标。";
            return;
        }

        var specialWarning = string.Equals(
            target.SourceAreaId,
            snapshot.FinalRoomId,
            StringComparison.OrdinalIgnoreCase)
            ? Environment.NewLine + "这是当前最终房间；删除其内容可能使任务无法完成。"
            : string.Empty;
        if (!ThemedDialog.Confirm(
                owner,
                $"将删除 {target.DisplayName} 的“{target.ContentLabel ?? "当前内容"}”。" +
                specialWarning + Environment.NewLine +
                "探索状态会保留，程序会先完整备份当前档案。",
                "确认删除地图内容"))
        {
            return;
        }

        await ApplySaveEditAsync(target, profile, snapshot, service, BattleMapEditKind.DeleteContent);
    }

    private async Task MovePartyAsync(PrototypeMapCell target)
    {
        if (_currentSnapshot is null)
        {
            MovePartyPreview(target);
            return;
        }
        if (!TryGetWritableContext(target, out var owner, out var profile, out var snapshot, out var service))
        {
            MapSelectionTextBlock.Text = "当前地图正在刷新或处理其他操作，请重新选择目标。";
            return;
        }

        var contentWarning = target.RawContent == 0 || IsHiddenSystemContent(target)
            ? string.Empty
            : Environment.NewLine +
              $"目标仍保留“{target.ContentLabel ?? "地图事件"}”；移动只修改队伍落点，" +
              "不会模拟走入该格，因此目标事件不保证立即触发。";
        if (!ThemedDialog.Confirm(
                owner,
                $"将当前队伍移动到 {target.DisplayName}。" +
                contentWarning + Environment.NewLine +
                "程序会先完整备份当前档案。",
                "确认移动队伍"))
        {
            return;
        }

        await ApplySaveEditAsync(target, profile, snapshot, service, BattleMapEditKind.MoveParty);
    }

    private bool TryGetWritableContext(
        PrototypeMapCell target,
        out Window owner,
        out SaveProfile profile,
        out BattleMapSnapshot snapshot,
        out BattleMapEditService service)
    {
        owner = Window.GetWindow(this)!;
        profile = _profile!;
        snapshot = _currentSnapshot!;
        service = _editService!;
        return owner is not null &&
               profile is not null &&
               snapshot is not null &&
               service is not null &&
               target.SourceAreaId is not null &&
               target.SourceTileId is not null &&
               _cells.Contains(target) &&
               !_isApplyingEdit;
    }

    private async Task ApplySaveEditAsync(
        PrototypeMapCell target,
        SaveProfile profile,
        BattleMapSnapshot snapshot,
        BattleMapEditService service,
        BattleMapEditKind kind)
    {
        var generation = _profileGeneration;
        var gateTaken = false;
        var committed = false;
        var refreshedSuccessfully = false;
        _isApplyingEdit = true;
        SaveEditBusyChanged?.Invoke(true);
        CloseActiveContextMenu();
        CancelScheduledRefreshRetry();
        StopProfileMonitoring();
        MapCanvas.IsHitTestVisible = false;
        MapSelectionTextBlock.Text = kind == BattleMapEditKind.DeleteContent
            ? $"正在删除 {target.DisplayName} 的内容并验证存档……"
            : $"正在把队伍移动到 {target.DisplayName} 并验证存档……";

        try
        {
            await _refreshGate.WaitAsync();
            gateTaken = true;
            if (generation != _profileGeneration ||
                !ReferenceEquals(snapshot, _currentSnapshot) ||
                !_cells.Contains(target) ||
                target.SourceAreaId is null ||
                target.SourceTileId is null)
            {
                throw new InvalidOperationException(
                    "地图已刷新；请在最新地图上重新选择目标。");
            }

            var prepared = kind == BattleMapEditKind.DeleteContent
                ? await service.PrepareDeleteContentAsync(
                    profile,
                    snapshot,
                    target.SourceAreaId,
                    target.SourceTileId)
                : await service.PrepareMovePartyAsync(
                    profile,
                    snapshot,
                    target.SourceAreaId,
                    target.SourceTileId);
            var result = await service.CommitAsync(prepared);
            committed = true;
            SaveEditApplied?.Invoke(
                $"战斗地图{(kind == BattleMapEditKind.DeleteContent ? "删除" : "移动")}已应用：" +
                $"目标={target.SourceAreaId}.{target.SourceTileId}；备份={result.BackupDirectory}");
            if (generation != _profileGeneration || _snapshotReader is null)
            {
                return;
            }

            var refreshed = await LoadSnapshotWithRetryAsync(
                _snapshotReader,
                profile.ProfileDirectory,
                CancellationToken.None);
            if (generation != _profileGeneration)
            {
                return;
            }

            RenderSnapshot(refreshed, fitToView: false);
            refreshedSuccessfully = true;
            MapSelectionTextBlock.Text = kind == BattleMapEditKind.DeleteContent
                ? $"已删除 {target.DisplayName} 的内容。"
                : $"已将队伍移动到 {target.DisplayName}。";
        }
        catch (Exception ex)
        {
            MapSelectionTextBlock.Text = committed
                ? "操作已写入，但地图刷新失败；请重新加载内容目录。"
                : "操作未应用；存档保持原状或已自动恢复。";
            if (Window.GetWindow(this) is { } owner)
            {
                ThemedDialog.ShowMessage(
                    owner,
                    ex.Message,
                    committed ? "地图刷新失败" : "战斗地图操作失败",
                    ThemedDialogKind.Error);
            }
        }
        finally
        {
            if (gateTaken)
            {
                _refreshGate.Release();
            }
            _isApplyingEdit = false;
            MapCanvas.IsHitTestVisible = true;
            SaveEditBusyChanged?.Invoke(false);
            if (generation == _profileGeneration)
            {
                StartProfileMonitoring();
            }
        }

        if (!refreshedSuccessfully && generation == _profileGeneration)
        {
            await RefreshLiveSnapshotAsync(generation);
        }
    }

    private void MovePartyPreview(PrototypeMapCell target)
    {
        foreach (var cell in _cells)
        {
            cell.HasParty = ReferenceEquals(cell, target);
            RefreshCellVisual(cell);
        }

        MapSelectionTextBlock.Text =
            $"界面预览：队伍移动至 {target.DisplayName}。存档未发生变化。";
    }

    private void SelectCell(PrototypeMapCell cell)
    {
        _selectedCell = cell;
        foreach (var item in _cells)
        {
            RefreshCellVisual(item);
        }

        MapSelectionTextBlock.Text =
            $"{cell.DisplayName} · {FormatKind(cell.Kind)} · {FormatKnowledge(cell.Knowledge)} · " +
            $"{FormatContent(cell)}；右击查看可用操作。";
    }

    private void RefreshCellVisual(PrototypeMapCell cell)
    {
        var selected = ReferenceEquals(_selectedCell, cell);
        var normalLayer = cell.Kind == PrototypeCellKind.Room ? 20 : 0;
        Panel.SetZIndex(cell.Visual, normalLayer);
        cell.Visual.Opacity = 1;
        var baseSource = ResolveBaseMapSprite(cell);
        var contentMarkerSource = ResolveContentMarkerSprite(cell);
        cell.BaseImage.Source = baseSource;
        cell.ContentMarkerImage.Source = contentMarkerSource;
        var needsCompletionMarker = cell.Kind == PrototypeCellKind.Room &&
                                    cell.Knowledge is PrototypeKnowledge.Visited or
                                        PrototypeKnowledge.Completed;
        var completionMarkerSource = needsCompletionMarker
            ? TryLoadMapIcon("marker_room_visited.png")
            : null;
        var partyMarkerSource = cell.HasParty ? TryLoadMapIcon("indicator.png") : null;
        cell.CompletionMarkerImage.Source = completionMarkerSource;
        cell.PartyMarkerImage.Source = partyMarkerSource;
        cell.SelectionOverlay.BorderBrush = selected
            ? new SolidColorBrush(Color.FromRgb(196, 43, 39))
            : Brushes.Transparent;
        cell.SelectionOverlay.Background = selected
            ? new SolidColorBrush(Color.FromArgb(82, 126, 17, 20))
            : Brushes.Transparent;

        var needsContentMarker = cell.Kind != PrototypeCellKind.Room &&
                                 cell.Content is not PrototypeContent.None and not PrototypeContent.Entrance;
        var missingPartyMarker = cell.HasParty && partyMarkerSource is null;
        var missingCompletionMarker = needsCompletionMarker && completionMarkerSource is null;
        var needsFallback = baseSource is null ||
                            (needsContentMarker && contentMarkerSource is null) ||
                            missingPartyMarker ||
                            missingCompletionMarker;
        cell.FallbackIcon.Visibility = needsFallback ? Visibility.Visible : Visibility.Collapsed;
        cell.FallbackIcon.Text = missingPartyMarker
            ? "◆"
            : missingCompletionMarker
                ? "✓"
                : FormatContentIcon(cell.Content);
        cell.FallbackIcon.Foreground = new SolidColorBrush(missingPartyMarker
            ? Color.FromRgb(225, 194, 103)
            : missingCompletionMarker
                ? Color.FromRgb(187, 178, 133)
                : FormatContentColor(cell.Content));
        var tooltip =
            $"{cell.DisplayName} ({cell.Id})\n{FormatKind(cell.Kind)} · {FormatKnowledge(cell.Knowledge)}\n" +
            $"内容：{FormatContent(cell)}" +
            (cell.MashIndex >= 0 ? $" · 遭遇索引 {cell.MashIndex} / 类型 {cell.MashType}" : string.Empty) +
            $"\n右击打开操作菜单";
        cell.Visual.ToolTip = tooltip;
    }

    private ImageSource? ResolveBaseMapSprite(PrototypeMapCell cell)
    {
        if (cell.Kind == PrototypeCellKind.Room)
        {
            var roomSprite = cell.Content switch
            {
                PrototypeContent.Entrance => "room_entrance.png",
                PrototypeContent.Battle => "room_battle.png",
                PrototypeContent.Boss => "room_boss.png",
                PrototypeContent.Curio => "room_curio.png",
                PrototypeContent.Treasure => "room_treasure.png",
                _ => "room_empty.png"
            };
            return TryLoadMapIcon(roomSprite);
        }

        return TryLoadMapIcon(cell.Knowledge is PrototypeKnowledge.Unknown or PrototypeKnowledge.Scouted
            ? "hall_dim.png"
            : "hall_clear.png");
    }

    private ImageSource? ResolveContentMarkerSprite(PrototypeMapCell cell)
    {
        if (cell.Kind == PrototypeCellKind.Room)
        {
            return null;
        }

        var marker = cell.Content switch
        {
            PrototypeContent.Battle => "marker_battle.png",
            PrototypeContent.Curio => "marker_curio.png",
            PrototypeContent.Trap => "marker_trap.png",
            PrototypeContent.Obstacle => "marker_obstacle.png",
            PrototypeContent.Treasure => "marker_curio.png",
            PrototypeContent.SecretDoor => "marker_secret.png",
            _ => null
        };
        return marker is null ? null : TryLoadMapIcon(marker);
    }

    private static string FormatKind(PrototypeCellKind kind) => kind switch
    {
        PrototypeCellKind.Room => "房间",
        _ => "普通走廊格"
    };

    private static string FormatKnowledge(PrototypeKnowledge knowledge) => knowledge switch
    {
        PrototypeKnowledge.Unknown => "未探索（全局视野已显示）",
        PrototypeKnowledge.Scouted => "已侦察",
        PrototypeKnowledge.Completed => "已完成",
        _ => "已探索"
    };

    private static string FormatContent(PrototypeMapCell cell)
    {
        if (!string.IsNullOrWhiteSpace(cell.ContentLabel))
        {
            return cell.ContentLabel;
        }

        return cell.Content switch
        {
            PrototypeContent.None => "空白",
            PrototypeContent.Entrance => "入口",
            PrototypeContent.Curio => "奇物",
            PrototypeContent.Battle => "战斗",
            PrototypeContent.Trap => "陷阱",
            PrototypeContent.Treasure => "宝藏",
            PrototypeContent.Boss => "首领遭遇",
            PrototypeContent.Obstacle => "障碍",
            PrototypeContent.SecretDoor => "秘密房间入口",
            _ => "未知"
        };
    }

    private static string FormatContentIcon(PrototypeContent content) => content switch
    {
        PrototypeContent.None => "·",
        PrototypeContent.Entrance => "◇",
        PrototypeContent.Curio => "?",
        PrototypeContent.Battle => "⚔",
        PrototypeContent.Trap => "!",
        PrototypeContent.Treasure => "✦",
        PrototypeContent.Boss => "☠",
        PrototypeContent.Obstacle => "▰",
        PrototypeContent.SecretDoor => "◆",
        _ => "·"
    };

    private static Color FormatContentColor(PrototypeContent content) => content switch
    {
        PrototypeContent.Battle => Color.FromRgb(210, 75, 67),
        PrototypeContent.Boss => Color.FromRgb(218, 45, 39),
        PrototypeContent.Trap => Color.FromRgb(211, 164, 64),
        PrototypeContent.Treasure => Color.FromRgb(225, 194, 103),
        PrototypeContent.Curio => Color.FromRgb(187, 178, 133),
        PrototypeContent.Obstacle => Color.FromRgb(142, 122, 84),
        PrototypeContent.SecretDoor => Color.FromRgb(94, 160, 151),
        _ => Color.FromRgb(97, 99, 92)
    };

    private enum PrototypeCellKind
    {
        Room,
        Corridor
    }

    private enum PrototypeKnowledge
    {
        Unknown,
        Scouted,
        Visited,
        Completed
    }

    private enum PrototypeContent
    {
        None,
        Entrance,
        Curio,
        Battle,
        Trap,
        Treasure,
        Boss,
        Obstacle,
        SecretDoor
    }

    private sealed class PrototypeMapCell(
        string id,
        string displayName,
        PrototypeCellKind kind,
        PrototypeKnowledge knowledge,
        PrototypeContent content,
        bool hasParty,
        Grid visual,
        Image baseImage,
        Image contentMarkerImage,
        Image completionMarkerImage,
        Image partyMarkerImage,
        TextBlock fallbackIcon,
        Border selectionOverlay,
        int rawContent,
        int mashIndex,
        int mashType,
        string? sourceAreaId,
        string? sourceTileId)
    {
        public string Id { get; } = id;
        public string DisplayName { get; } = displayName;
        public PrototypeCellKind Kind { get; } = kind;
        public PrototypeKnowledge Knowledge { get; set; } = knowledge;
        public PrototypeContent Content { get; set; } = content;
        public string? ContentLabel { get; set; }
        public bool HasParty { get; set; } = hasParty;
        public Grid Visual { get; } = visual;
        public Image BaseImage { get; } = baseImage;
        public Image ContentMarkerImage { get; } = contentMarkerImage;
        public Image CompletionMarkerImage { get; } = completionMarkerImage;
        public Image PartyMarkerImage { get; } = partyMarkerImage;
        public TextBlock FallbackIcon { get; } = fallbackIcon;
        public Border SelectionOverlay { get; } = selectionOverlay;
        public int RawContent { get; } = rawContent;
        public int MashIndex { get; } = mashIndex;
        public int MashType { get; } = mashType;
        public string? SourceAreaId { get; } = sourceAreaId;
        public string? SourceTileId { get; } = sourceTileId;
    }
}
