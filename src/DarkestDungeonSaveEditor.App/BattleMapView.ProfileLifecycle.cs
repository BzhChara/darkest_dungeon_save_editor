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
        _codec = null;
        _activeContentSnapshot = null;
        _workshopDirectory = null;
        _localModDirectory = null;
        _snapshotReader = null;
        _editService = null;
        _forceTownSaveService = null;
        _managedEncounterBridgeService = null;
        _encounterCatalog = null;
        _roomAttachmentCatalog = null;
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
        ActiveContentSnapshot? activeContent = null,
        string? workshopDirectory = null,
        string? localModDirectory = null,
        CancellationToken cancellationToken = default,
        CatalogDiagnosticBatch? diagnosticBatch = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(codec);
        var ownsDiagnosticBatch = diagnosticBatch is null;
        diagnosticBatch ??= new CatalogDiagnosticBatch();
        var profileDirectory = Path.GetFullPath(profile.ProfileDirectory);
        var generation = unchecked(++_profileGeneration);
        CancelScheduledRefreshRetry();
        StopProfileMonitoring();
        _profileDirectory = profileDirectory;
        _profileId = string.IsNullOrWhiteSpace(profile.ProfileId)
            ? Path.GetFileName(profileDirectory)
            : profile.ProfileId;
        _profile = profile;
        _codec = codec;
        _activeContentSnapshot = activeContent;
        _workshopDirectory = string.IsNullOrWhiteSpace(workshopDirectory)
            ? null
            : Path.GetFullPath(workshopDirectory);
        _localModDirectory = string.IsNullOrWhiteSpace(localModDirectory)
            ? null
            : Path.GetFullPath(localModDirectory);
        _snapshotReader = new BattleMapSnapshotReader(codec);
        _editService = new BattleMapEditService(codec);
        _forceTownSaveService = new ForceTownSaveService(codec);
        _managedEncounterBridgeService = new ManagedBattleEncounterBridgeService(codec);
        _encounterCatalog = null;
        _roomAttachmentCatalog = null;
        _currentSnapshot = null;
        ConfigureOriginalMapAssets(gameDirectory);
        ShowUnavailableState("正在安全读取当前副本地图……", "正在读取存档");

        BattleMapSnapshot? snapshot = null;
        try
        {
            var location = await RaidSaveLocation.ReadAsync(profileDirectory, codec, cancellationToken);
            var mapExists = File.Exists(location.MapPath);
            var raidExists = File.Exists(location.RaidPath);
            if (mapExists && raidExists)
            {
                snapshot = await LoadSnapshotWithRetryAsync(_snapshotReader, profileDirectory, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (generation != _profileGeneration)
                {
                    return null;
                }

                if (activeContent is not null)
                {
                    try
                    {
                        _encounterCatalog = await Task.Run(
                            () => BattleEncounterCatalog.Load(activeContent, snapshot),
                            cancellationToken);
                        var directByType = _encounterCatalog.DirectEncounters
                            .GroupBy(encounter => encounter.MashType)
                            .ToDictionary(group => group.Key, group => group.Count());
                        var bridgeByType = _encounterCatalog.BridgeEncounters
                            .GroupBy(encounter => encounter.MashType)
                            .ToDictionary(group => group.Key, group => group.Count());
                        diagnosticBatch.Add("战斗遭遇", _encounterCatalog.Issues);
                        CrashDiagnostics.RecordStatus(
                            $"战斗遭遇目录：地区={snapshot.DungeonId}；难度={snapshot.Difficulty}；" +
                            $"无需 Bridge 的直接索引：走廊={directByType.GetValueOrDefault(0)}，" +
                            $"房间={directByType.GetValueOrDefault(1)}，" +
                            $"首领房间={directByType.GetValueOrDefault(2)}；" +
                            $"全局 Bridge 候选：走廊={bridgeByType.GetValueOrDefault(0)}，" +
                            $"房间={bridgeByType.GetValueOrDefault(1)}，" +
                            $"首领房间={bridgeByType.GetValueOrDefault(2)}；" +
                            "直接索引为 0 不代表没有 Bridge 候选；实际写入仍需满足存档状态和安全检查。诊断并入本轮目录日志。");
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _encounterCatalog = null;
                        CrashDiagnostics.RecordException(
                            "BattleMap: encounter catalog",
                            ex,
                            $"档案={profile.ProfileId}；地区={snapshot.DungeonId}；难度={snapshot.Difficulty}");
                    }
                    if (generation != _profileGeneration)
                    {
                        return null;
                    }

                    try
                    {
                        _roomAttachmentCatalog = await Task.Run(
                            () => BattleRoomAttachmentCatalog.Load(activeContent, snapshot.DungeonId),
                            cancellationToken);
                        diagnosticBatch.Add("地图内容", _roomAttachmentCatalog.Issues);
                        CrashDiagnostics.RecordStatus(
                            $"地图内容目录：房间奇物={_roomAttachmentCatalog.Curios.Count}；" +
                            $"房间宝箱={_roomAttachmentCatalog.Treasures.Count}；" +
                            $"走廊奇物={_roomAttachmentCatalog.HallCurios.Count}；" +
                            $"当前区域={snapshot.DungeonId}；" +
                            $"陷阱={_roomAttachmentCatalog.GetCandidates(BattleRoomAttachmentKind.Trap, snapshot.DungeonId).Count}；" +
                            $"障碍={_roomAttachmentCatalog.GetCandidates(BattleRoomAttachmentKind.Obstacle, snapshot.DungeonId).Count}；" +
                            "诊断并入本轮目录日志。");
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _roomAttachmentCatalog = null;
                        CrashDiagnostics.RecordException(
                            "BattleMap: room attachment catalog",
                            ex,
                            $"档案={profile.ProfileId}；地区={snapshot.DungeonId}；难度={snapshot.Difficulty}");
                    }
                    if (generation != _profileGeneration)
                    {
                        return null;
                    }
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
            if (ownsDiagnosticBatch)
            {
                CrashDiagnostics.RecordCatalogDiagnostics(diagnosticBatch);
            }
            if (generation == _profileGeneration)
            {
                StartProfileMonitoring();
            }
        }

        // Close the small initial-read/monitor-baseline race. If the game completed another
        // write between those operations, this hash comparison immediately adopts that state.
        if (!UsesSharedProfileMonitor && generation == _profileGeneration)
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
        MapCanvas.IsHitTestVisible = !_isApplyingEdit;
        EmptyStatePanel.Visibility = Visibility.Collapsed;
        PrototypeBadge.Visibility = Visibility.Visible;
        ZoomOutButton.IsEnabled = true;
        ZoomInButton.IsEnabled = true;
        ResetZoomButton.IsEnabled = true;
        FitMapButton.IsEnabled = true;
        ForceTownButton.IsEnabled = _currentSnapshot is not null;
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
        ForceTownButton.IsEnabled = false;
        MapTitleTextBlock.Text = "当前副本地图";
        MapSelectionTextBlock.Text = "当前没有副本地图。";
        LiveStatusTextBlock.Text = liveStatus;
    }

}
