using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DarkestDungeonSaveEditor.Core;

namespace DarkestDungeonSaveEditor.App;

public partial class MainWindow
{
    private readonly Func<bool> _gameRunningProbe;
    private DispatcherTimer? _gameProcessPoll;
    private bool _gameRunning;
    private int _gameStateVersion;
    private bool _checkingProfileOperation;
    private bool _waitForStableProfile;

    private void StartGameProcessMonitoring()
    {
        _gameProcessPoll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _gameProcessPoll.Tick += (_, _) => CheckGameProcess();
        _gameProcessPoll.Start();
        CheckGameProcess();
    }

    private void CheckGameProcess()
    {
        ObserveGameProcess();
        // Idle ticks and focus changes inspect processes only. Drain an explicit
        // request (including an exit noticed inside an awaited operation) once.
        // Failed synchronization clears the request instead of scheduling retries.
        if (!_gameRunning && _syncRequested) _ = DrainProfileSyncAsync();
    }

    private bool ObserveGameProcess()
    {
        var running = _gameRunningProbe();
        if (running == _gameRunning) return running;
        _gameRunning = running;
        unchecked { _gameStateVersion++; }
        _syncReady = false;
        _syncInvalidated = true;
        // An in-flight writer still owns its prepared objects. Its revision is
        // invalidated immediately, but cleanup waits for that operation to end.
        if (IsProfileOperationBusy) unchecked { _editRevision++; }
        else InvalidatePreparedEdit();
        BattleMapPanel.DismissProfileMenu();
        _syncRequested = !running && _catalogSnapshotReader is not null;
        if (running)
        {
            _catalogLoadCancellation?.Cancel();
            ShowGameRunningStatus();
        }
        else if (_catalogSnapshotReader is not null)
        {
            _waitForStableProfile = true;
            _contentRefreshRequested = true;
            LocalizedUi.Set(ProfileSyncStatusTextBlock, TextBlock.TextProperty,
                () => EditorText.Get("MainWindow_ProfileSync_GameExited"));
        }
        else
        {
            LocalizedUi.Set(ProfileSyncStatusTextBlock, TextBlock.TextProperty,
                () => EditorText.Get("MainWindow_ProfileSync_002"));
        }
        UpdateEnabledState();
        return running;
    }

    private void ShowGameRunningStatus()
    {
        LocalizedUi.Set(ProfileSyncStatusTextBlock, TextBlock.TextProperty,
            () => EditorText.Get("MainWindow_ProfileSync_GameRunning"));
        ProfileSyncStatusTextBlock.ToolTip = null;
    }

    private void RequireGameStopped()
    {
        if (ObserveGameProcess())
            throw new InvalidOperationException(EditorText.Get("MainWindow_ProfileSync_GameRunning"));
    }

    private async Task<bool> CheckProfileBeforeOperationAsync(bool requireUnchanged = false)
    {
        if (IsBusy || _catalogSnapshotReader is null || _catalogCloseRequested) return false;
        var generation = _catalogGeneration;
        var revision = _editRevision;
        _checkingProfileOperation = true;
        UpdateEnabledState();
        try
        {
            RequireGameStopped();
            RequestProfileSync(invalidatePreview: false, refreshContent: true);
            await DrainProfileSyncAsync();
            if (generation != _catalogGeneration) return false;
            RequireGameStopped();
            if (!_syncReady)
                throw new IOException(EditorText.Get("MainWindow_ProfileSync_001") + _lastSyncError);
            if (requireUnchanged && revision != _editRevision)
            {
                AppendStatus(EditorText.Get("MainWindow_ProfileSync_OperationChanged"), level: DiagnosticLogLevel.Warning);
                return false;
            }
            return true;
        }
        catch (Exception error)
        {
            if (generation == _catalogGeneration)
                AppendStatusSafely(error.Message, "Profile operation check", DiagnosticLogLevel.Warning);
            return false;
        }
        finally
        {
            _checkingProfileOperation = false;
            UpdateEnabledState();
        }
    }

    private static async Task WaitForStableProfileAsync(SaveProfile profile, CancellationToken token)
    {
        IReadOnlyDictionary<string, string?>? previous = null;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var current = await Task.Run(() => ProfileCatalogSnapshotReader.CaptureHashes(profile), token);
                if (previous is not null && ProfileCatalogSnapshotReader.HashesEqual(previous, current)) return;
                previous = current;
            }
            catch (IOException) when (attempt < 4) { previous = null; }
            if (attempt < 4) await Task.Delay(300, token);
        }
        throw new IOException(EditorText.Get("MainWindow_ProfileSync_006"));
    }

    private static bool IsDarkestDungeonRunning()
    {
        foreach (var name in new[] { "Darkest", "DarkestDungeon" })
        {
            Process[] processes;
            try { processes = Process.GetProcessesByName(name); }
            catch { return true; } // Do not enable writing when process inspection fails.
            try { if (processes.Length > 0) return true; }
            finally { foreach (var process in processes) process.Dispose(); }
        }
        return false;
    }
}
