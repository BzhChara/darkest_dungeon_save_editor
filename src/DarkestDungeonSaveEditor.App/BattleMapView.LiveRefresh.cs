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
        if (UsesSharedProfileMonitor || _profileDirectory is null || _profileMonitor is not null)
        {
            return;
        }

        try
        {
            _profileMonitor = new ProfileSaveMonitor(
                _profileDirectory,
                ["persist.game.json", "persist.map.json", "persist.raid.json"]);
            _profileMonitor.Changed += ProfileMonitor_Changed;
            _profileMonitor.Start();
        }
        catch (Exception ex)
        {
            StopProfileMonitoring();
            LiveStatusTextBlock.Text = EditorText.Format("BattleMapView_LiveRefresh_001", ex.Message);
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
        if (UsesSharedProfileMonitor || generation != _profileGeneration ||
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
            if (UsesSharedProfileMonitor ||
                generation != _profileGeneration ||
                _profileDirectory is null ||
                _snapshotReader is null)
            {
                return;
            }

            var location = await RaidSaveLocation.ReadAsync(_profileDirectory, _codec!);
            if (generation != _profileGeneration) return;
            var mapExists = File.Exists(location.MapPath);
            var raidExists = File.Exists(location.RaidPath);
            if (!mapExists || !raidExists)
            {
                if (mapExists != raidExists)
                {
                    if (_currentSnapshot is null)
                    {
                        ShowUnavailableState(
                            EditorText.Get("BattleMapView_LiveRefresh_002"),
                            EditorText.Get("BattleMapView_LiveRefresh_003"));
                    }
                    else
                    {
                        LiveStatusTextBlock.Text = EditorText.Get("BattleMapView_LiveRefresh_004");
                    }
                    ScheduleRefreshRetry(generation);
                    return;
                }

                CancelScheduledRefreshRetry();
                ShowUnavailableState(
                    EditorText.Get("BattleMapView_LiveRefresh_005"),
                    EditorText.Get("BattleMapView_LiveRefresh_006"));
                return;
            }

            LiveStatusTextBlock.Text = EditorText.Get("BattleMapView_LiveRefresh_007");
            var snapshot = await LoadSnapshotWithRetryAsync(
                _snapshotReader,
                _profileDirectory,
                CancellationToken.None);
            if (generation != _profileGeneration)
            {
                return;
            }

            if (_currentSnapshot is not null &&
                _currentSnapshot.MapSavePath.Equals(snapshot.MapSavePath, StringComparison.OrdinalIgnoreCase) &&
                _currentSnapshot.MapSha256.Equals(snapshot.MapSha256, StringComparison.OrdinalIgnoreCase) &&
                _currentSnapshot.RaidSha256.Equals(snapshot.RaidSha256, StringComparison.OrdinalIgnoreCase))
            {
                LiveStatusTextBlock.Text =
                    EditorText.Format("BattleMapView_LiveRefresh_008", snapshot.ReadAtUtc.ToLocalTime());
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
                    EditorText.Get("BattleMapView_LiveRefresh_009"),
                    EditorText.Get("BattleMapView_LiveRefresh_003"));
            }
            else
            {
                LiveStatusTextBlock.Text = EditorText.Format("BattleMapView_LiveRefresh_010", ex.Message);
            }
            ScheduleRefreshRetry(generation);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

}
