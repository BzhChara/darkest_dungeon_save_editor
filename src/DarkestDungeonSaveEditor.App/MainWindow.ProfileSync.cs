using System.IO;
using System.Windows;
using System.Windows.Threading;
using DarkestDungeonSaveEditor.Core;

namespace DarkestDungeonSaveEditor.App;

public partial class MainWindow : Window
{
    private ProfileSaveMonitor? _catalogMonitor;
    private ProfileCatalogSnapshotReader? _catalogSnapshotReader;
    private CancellationTokenSource? _catalogSyncCancellation;
    private CancellationTokenSource? _catalogLoadCancellation;
    private Task? _catalogLoadTask;
    private bool _catalogCloseRequested;
    private DispatcherTimer? _catalogSyncRetry;
    private DispatcherTimer? _contentPoll;
    private ManagedBattleEncounterBridgeService? _battleMaintenance;
    private string? _catalogContentFingerprint;
    private string? _lastMaintenanceMessage;
    private string? _maintenanceGameDirectory;
    private string? _maintenanceLocalModDirectory;
    private IReadOnlyDictionary<string, string?>? _catalogFileHashes;
    private string? _catalogConfigurationKey;
    private string? _lastSyncError;
    private int _catalogGeneration;
    private int _busyDepth;
    private bool _syncRequested;
    private bool _syncInProgress;
    private bool _syncReady;
    private bool _restoringCatalogSelection;
    private bool IsBusy => _busyDepth > 0;

    private void StartProfileSync(ActiveContentSnapshot content, QuantityItemCatalogResult items,
        DsonSaveCodec codec, string gameDirectory, string? workshopDirectory, string? localModDirectory,
        string contentFingerprint)
    {
        StopProfileSync();
        _catalogConfigurationKey = ProfileContentConfiguration.GetKey(content.DecodedGamePath);
        _catalogContentFingerprint = contentFingerprint;
        _catalogSnapshotReader = new(content, items, codec, gameDirectory, workshopDirectory, localModDirectory,
            initialContentFingerprint: contentFingerprint);
        _battleMaintenance = new ManagedBattleEncounterBridgeService(codec);
        _maintenanceGameDirectory = gameDirectory;
        _maintenanceLocalModDirectory = localModDirectory;
        _catalogSyncCancellation = new CancellationTokenSource();
        _catalogMonitor = new ProfileSaveMonitor(content.Profile.ProfileDirectory, ProfileCatalogSnapshotReader.WatchedFileNames);
        _catalogMonitor.Changed += CatalogMonitor_Changed;
        _catalogMonitor.Start();
        _contentPoll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _contentPoll.Tick += (_, _) =>
        {
            if (!IsBusy && !_syncInProgress) RequestProfileSync(invalidatePreview: false);
        };
        _contentPoll.Start();
        RequestProfileSync(); // Catch writes between the initial load and monitor baseline.
    }

    private void StopProfileSync()
    {
        _catalogLoadCancellation?.Cancel();
        unchecked { _catalogGeneration++; }
        _catalogSyncCancellation?.Cancel();
        _catalogSyncCancellation?.Dispose();
        _catalogSyncCancellation = null;
        if (_catalogMonitor is not null)
        {
            _catalogMonitor.Changed -= CatalogMonitor_Changed;
            _catalogMonitor.Dispose();
            _catalogMonitor = null;
        }
        _catalogSyncRetry?.Stop();
        _contentPoll?.Stop();
        _contentPoll = null;
        _battleMaintenance = null;
        _catalogContentFingerprint = null;
        _lastMaintenanceMessage = null;
        _catalogSyncRetry = null;
        _catalogSnapshotReader = null;
        _catalogFileHashes = null;
        _catalogConfigurationKey = null;
        _lastSyncError = null;
        _syncRequested = false;
        _syncReady = false;
        if (ProfileSyncStatusTextBlock is not null)
        {
            ProfileSyncStatusTextBlock.Text = "尚未加载档案";
            ProfileSyncStatusTextBlock.ToolTip = null;
        }
    }

    private void CatalogMonitor_Changed(object? sender, ProfileSaveFilesChangedEventArgs e)
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
        var monitor = sender;
        try
        {
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                if (ReferenceEquals(monitor, _catalogMonitor)) RequestProfileSync();
            }));
        }
        catch (InvalidOperationException) { /* The owning window is closing. */ }
    }

    private void RequestProfileSync(bool invalidatePreview = true)
    {
        if (_catalogSnapshotReader is null || _catalogSyncCancellation is null) return;
        _syncRequested = true;
        _syncReady = false;
        // Do not clear objects used by an in-flight commit. Its backend retains all hash guards.
        if (invalidatePreview && !IsBusy)
        {
            InvalidatePreparedEdit();
            BattleMapPanel.DismissProfileMenu();
        }
        ProfileSyncStatusTextBlock.Text = IsBusy ? "操作结束后同步" : "正在同步存档…";
        UpdateEnabledState();
        _ = DrainProfileSyncAsync();
    }

    private async Task DrainProfileSyncAsync()
    {
        if (_syncInProgress || IsBusy || !_syncRequested || _catalogSnapshotReader is null ||
            _catalogSyncCancellation is null) return;
        _syncInProgress = true;
        var generation = _catalogGeneration;
        var reader = _catalogSnapshotReader;
        var maintenance = _battleMaintenance;
        var maintenanceGameDirectory = _maintenanceGameDirectory;
        var maintenanceLocalModDirectory = _maintenanceLocalModDirectory;
        var token = _catalogSyncCancellation.Token;
        try
        {
            while (_syncRequested && !IsBusy && generation == _catalogGeneration)
            {
                _syncRequested = false;
                var snapshot = await Task.Run(() => reader.ReadAsync(token, refreshContent: true), token);
                if (generation != _catalogGeneration || token.IsCancellationRequested) return;
                if (IsBusy) { _syncRequested = true; return; }
                if (maintenance is not null && maintenanceGameDirectory is not null)
                {
                    EditorBattleMaintenanceResult? result = null;
                    try
                    {
                        result = await Task.Run(() => maintenance.ReconcileAsync(snapshot.Content,
                            maintenanceGameDirectory, maintenanceLocalModDirectory, token), token);
                    }
                    catch (Exception error) when (error is not OperationCanceledException)
                    {
                        if (generation != _catalogGeneration || token.IsCancellationRequested) return;
                        var message = "战斗记录自动维护暂缓：" + error.Message;
                        if (_lastMaintenanceMessage != message)
                            AppendStatus(message);
                        _lastMaintenanceMessage = message;
                        // Unproven encounter indexes must not disable inventory/hero
                        // workflows. Map writes repeat maintenance before committing.
                        snapshot = await Task.Run(() => reader.ReadAsync(token, refreshContent: true), token);
                    }
                    if (generation != _catalogGeneration || token.IsCancellationRequested) return;
                    if (result is { } reported && (reported.Changed || reported.Deferred) && _lastMaintenanceMessage != reported.Message)
                    {
                        AppendStatus(reported.Message);
                        _lastMaintenanceMessage = reported.Message;
                    }
                    if (result is { Changed: true })
                    {
                        InvalidatePreparedEdit();
                        BattleMapPanel.DismissProfileMenu();
                        snapshot = await Task.Run(() => reader.ReadAsync(token, refreshContent: true), token);
                    }
                    if (result is { Deferred: false }) _lastMaintenanceMessage = null;
                }
                if (generation != _catalogGeneration || token.IsCancellationRequested) return;
                if (_catalogFileHashes is not null &&
                    _catalogContentFingerprint == snapshot.ContentFingerprint &&
                    ProfileCatalogSnapshotReader.HashesEqual(_catalogFileHashes, snapshot.FileHashes))
                {
                    _catalogSyncRetry?.Stop();
                    if (_lastSyncError is not null) AppendStatus("档案自动同步已恢复。");
                    _lastSyncError = null;
                    _syncReady = !_syncRequested;
                    ProfileSyncStatusTextBlock.Text = $"已同步 {snapshot.ReadAtUtc.ToLocalTime():HH:mm:ss}";
                    ProfileSyncStatusTextBlock.ToolTip = "自动检测当前档案及资源定义变化；失效的编辑器战斗记录将在关闭游戏后自动清理。";
                    continue;
                }
                if (IsBusy) { _syncRequested = true; return; }
                InvalidatePreparedEdit();
                var contentChanged = snapshot.ConfigurationKey != _catalogConfigurationKey ||
                    (_catalogContentFingerprint is not null && _catalogContentFingerprint != snapshot.ContentFingerprint);
                var changedFiles = _catalogFileHashes is null ? [] : ProfileCatalogSnapshotReader.WatchedFileNames
                    .Where(name => snapshot.FileHashes[name] != _catalogFileHashes[name]).ToArray();
                var trinkets = contentChanged
                    ? await Task.Run(() => TrinketCatalog.Load(snapshot.Content), token) : null;
                var heroes = contentChanged
                    ? await Task.Run(() => HeroClassCatalog.Load(snapshot.Content), token) : null;
                if (generation != _catalogGeneration || token.IsCancellationRequested) return;
                if (IsBusy) { _syncRequested = true; return; }

                await BattleMapPanel.SynchronizeProfileAsync(snapshot, contentChanged, token);
                if (generation != _catalogGeneration || token.IsCancellationRequested) return;
                var currentHashes = await Task.Run(() => ProfileCatalogSnapshotReader.CaptureHashes(
                    snapshot.Content.Profile), token);
                if (!ProfileCatalogSnapshotReader.HashesEqual(snapshot.FileHashes, currentHashes))
                    throw new IOException("游戏仍在保存，等待完整存档后自动重试。");
                if (contentChanged && snapshot.ContentFingerprint != await Task.Run(() =>
                        ProfileCatalogContentFingerprint.Capture(snapshot.Content.Sources, token), token))
                    throw new IOException("资源文件仍在更新，等待完整内容后自动重试。");
                if (generation != _catalogGeneration || token.IsCancellationRequested) return;
                if (IsBusy) { _syncRequested = true; return; }

                var sceneChanged = _quantitySaveContext != snapshot.QuantityItems.SaveContext;
                var itemsChanged = !ReferenceEquals(_allItems, snapshot.QuantityItems.Items);
                _activeContentSnapshot = snapshot.Content;
                _catalogProfileDirectory = snapshot.Content.Profile.ProfileDirectory;
                _catalogGameSaveSha256 = snapshot.Content.SourceGameSha256;
                _catalogEstateSaveSha256 = snapshot.FileHashes["persist.estate.json"];
                _catalogQuantitySaveSha256 = snapshot.QuantityItems.SourceSaveSha256;
                _quantitySaveContext = snapshot.QuantityItems.SaveContext;
                _allItems = snapshot.QuantityItems.Items;
                _raidInventoryStorage = snapshot.QuantityItems.RaidStorage;
                if (trinkets is not null && heroes is not null)
                {
                    _allTrinkets = trinkets.Trinkets;
                    _trinketStorage = trinkets.Storage;
                    _allHeroes = heroes.HeroClasses;
                    _heroCatalog = heroes;
                    var diagnostics = new CatalogDiagnosticBatch();
                    diagnostics.Add("活动来源", snapshot.Content.Issues);
                    diagnostics.Add("物品", snapshot.QuantityItems.Issues);
                    diagnostics.Add("饰品", trinkets.Issues);
                    diagnostics.Add("人物/怪癖/姓名", heroes.Issues);
                    CrashDiagnostics.RecordCatalogDiagnostics(diagnostics);
                    AppendStatus("活动 Mod/DLC、资源定义或模式配置已变化，内容目录已自动更新。");
                }
                ItemTab.Header = _quantitySaveContext == QuantityItemSaveContext.Raid
                    ? "副本背包  /  RAID ITEMS" : "小镇物品  /  ESTATE ITEMS";
                if (itemsChanged || contentChanged)
                    RefreshCatalogRowsPreservingInput(sceneChanged, contentChanged);
                _catalogFileHashes = snapshot.FileHashes;
                _catalogConfigurationKey = snapshot.ConfigurationKey;
                _catalogContentFingerprint = snapshot.ContentFingerprint;
                _syncReady = !_syncRequested;
                _catalogSyncRetry?.Stop();
                if (changedFiles.Length > 0)
                {
                    CrashDiagnostics.RecordStatus($"档案自动同步：档案={snapshot.Content.Profile.ProfileId}；" +
                        $"场景={FormatQuantitySaveContext(_quantitySaveContext)}；变化文件={string.Join("、", changedFiles)}；" +
                        $"默认物品={_allItems.Count(item => !item.IsHiddenByDefault)}；隐藏物品={_allItems.Count(item => item.IsHiddenByDefault)}；" +
                        $"副本占格={snapshot.QuantityItems.RaidOccupiedSlots}；内容目录重建={contentChanged}；旧预览已失效。");
                }
                if (sceneChanged)
                    AppendStatus($"已自动切换为{FormatQuantitySaveContext(_quantitySaveContext)}物品，请重新选择修改目标。");
                if (_lastSyncError is not null) AppendStatus("档案自动同步已恢复。");
                _lastSyncError = null;
                ProfileSyncStatusTextBlock.Text = $"已同步 {snapshot.ReadAtUtc.ToLocalTime():HH:mm:ss}";
                ProfileSyncStatusTextBlock.ToolTip = "自动检测当前档案及资源定义变化；失效的编辑器战斗记录将在关闭游戏后自动清理。";
                UpdateCatalogMode();
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (generation != _catalogGeneration) return;
            _syncReady = false;
            _syncRequested = true;
            ProfileSyncStatusTextBlock.Text = "等待完整存档 · 自动重试";
            ProfileSyncStatusTextBlock.ToolTip = ex.Message;
            if (_lastSyncError != ex.Message)
                CrashDiagnostics.RecordStatus($"档案自动同步暂缓，保留上一完整快照并暂停写入：{ex.Message}", DiagnosticLogLevel.Warning);
            _lastSyncError = ex.Message;
            ScheduleProfileSyncRetry(generation);
        }
        finally
        {
            _syncInProgress = false;
            UpdateEnabledState();
            // A new profile may have been loaded while the old task was cancelling.
            if (generation != _catalogGeneration && _syncRequested)
                _ = DrainProfileSyncAsync();
        }
    }

    private void ScheduleProfileSyncRetry(int generation)
    {
        _catalogSyncRetry?.Stop();
        _catalogSyncRetry = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _catalogSyncRetry.Tick += (_, _) =>
        {
            _catalogSyncRetry?.Stop();
            if (generation == _catalogGeneration) _ = DrainProfileSyncAsync();
        };
        _catalogSyncRetry.Start();
    }
}
