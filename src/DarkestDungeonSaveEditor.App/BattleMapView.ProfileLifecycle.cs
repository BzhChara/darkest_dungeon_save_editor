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
                EditorText.Format("BattleMapView_ProfileLifecycle_001", (string.IsNullOrWhiteSpace(profileId) ? EditorText.Get("BattleMapView_MapConstruction_001") : profileId));
            MapSelectionTextBlock.Text = EditorText.Get("BattleMapView_ProfileLifecycle_002");
            LiveStatusTextBlock.Text = EditorText.Get("BattleMapView_ProfileLifecycle_003");
            UpdateMapBadge();
            _fitToView = true;
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(FitMapToViewport));
            return;
        }

        ShowUnavailableState(EditorText.Get("BattleMapView_ProfileLifecycle_004"), EditorText.Get("BattleMapView_ProfileLifecycle_005"));
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
        ShowUnavailableState(EditorText.Get("BattleMapView_ProfileLifecycle_006"), EditorText.Get("BattleMapView_ProfileLifecycle_007"));

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
                        diagnosticBatch.Add(EditorText.Get("BattleMapView_Commands_032"), _encounterCatalog.Issues);
                        CrashDiagnostics.RecordStatus(
                            EditorText.Format("BattleMapView_ProfileLifecycle_008", snapshot.DungeonId, snapshot.Difficulty) +
                            EditorText.Format("BattleMapView_ProfileLifecycle_009", directByType.GetValueOrDefault(0)) +
                            EditorText.Format("BattleMapView_ProfileLifecycle_010", directByType.GetValueOrDefault(1)) +
                            EditorText.Format("BattleMapView_ProfileLifecycle_011", directByType.GetValueOrDefault(2)) +
                            EditorText.Format("BattleMapView_ProfileLifecycle_012", bridgeByType.GetValueOrDefault(0)) +
                            EditorText.Format("BattleMapView_ProfileLifecycle_010", bridgeByType.GetValueOrDefault(1)) +
                            EditorText.Format("BattleMapView_ProfileLifecycle_011", bridgeByType.GetValueOrDefault(2)) +
                            EditorText.Get("BattleMapView_ProfileLifecycle_013"));
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
                            EditorText.Format("BattleMapView_ProfileLifecycle_014", profile.ProfileId, snapshot.DungeonId, snapshot.Difficulty));
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
                        diagnosticBatch.Add(EditorText.Get("BattleMapView_ProfileLifecycle_015"), _roomAttachmentCatalog.Issues);
                        CrashDiagnostics.RecordStatus(
                            EditorText.Format("BattleMapView_ProfileLifecycle_016", _roomAttachmentCatalog.Curios.Count) +
                            EditorText.Format("BattleMapView_ProfileLifecycle_017", _roomAttachmentCatalog.Treasures.Count) +
                            EditorText.Format("BattleMapView_ProfileLifecycle_018", _roomAttachmentCatalog.HallCurios.Count) +
                            EditorText.Format("BattleMapView_ProfileLifecycle_019", snapshot.DungeonId) +
                            EditorText.Format("BattleMapView_ProfileLifecycle_020", _roomAttachmentCatalog.GetCandidates(BattleRoomAttachmentKind.Trap, snapshot.DungeonId).Count) +
                            EditorText.Format("BattleMapView_ProfileLifecycle_021", _roomAttachmentCatalog.GetCandidates(BattleRoomAttachmentKind.Obstacle, snapshot.DungeonId).Count) +
                            EditorText.Get("BattleMapView_ProfileLifecycle_022"));
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
                            EditorText.Format("BattleMapView_ProfileLifecycle_014", profile.ProfileId, snapshot.DungeonId, snapshot.Difficulty));
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
                    EditorText.Get("BattleMapView_ProfileLifecycle_023"),
                    EditorText.Get("BattleMapView_LiveRefresh_006"));
            }
            else
            {
                ShowUnavailableState(
                    EditorText.Get("BattleMapView_LiveRefresh_002"),
                    EditorText.Get("BattleMapView_LiveRefresh_003"));
                ScheduleRefreshRetry(generation);
            }
        }
        catch
        {
            if (generation == _profileGeneration)
            {
                ShowUnavailableState(
                    EditorText.Get("BattleMapView_ProfileLifecycle_024"),
                    EditorText.Get("BattleMapView_LiveRefresh_003"));
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
        MapTitleTextBlock.Text = EditorText.Get("BattleMapView_ProfileLifecycle_025");
        MapSelectionTextBlock.Text = EditorText.Get("BattleMapView_ProfileLifecycle_026");
        LiveStatusTextBlock.Text = liveStatus;
    }

}
