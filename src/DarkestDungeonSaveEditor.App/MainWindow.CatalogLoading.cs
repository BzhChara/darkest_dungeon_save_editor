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
    private async void LoadCatalog_Click(object sender, RoutedEventArgs e)
    {
        if (_catalogCloseRequested || _catalogLoadTask is { IsCompleted: false }) return;
        var loadTask = LoadCatalogAsync();
        _catalogLoadTask = loadTask;
        try { await loadTask; }
        finally
        {
            if (ReferenceEquals(_catalogLoadTask, loadTask)) _catalogLoadTask = null;
        }
    }

    private async Task LoadCatalogAsync(SaveEditorLocations? locations = null)
    {
        var diagnosticBatch = new CatalogDiagnosticBatch();
        using var loadCancellation = new CancellationTokenSource();
        try
        {
            CrashDiagnostics.SetStage("LoadCatalog: invalidating previous catalog");
            InvalidateCatalog();
            _catalogLoadCancellation = loadCancellation;
            var generation = _catalogGeneration;
            CrashDiagnostics.SetStage("LoadCatalog: entering busy state");
            _catalogLoading = true;
            UpdateEnabledState();
            // Cancellation is asynchronous. Include the previous reader's cleanup
            // before any path validation, scan failure or close can finish this load.
            if (_catalogSyncTask is { } previousSync) await previousSync;
            if (generation != _catalogGeneration) return;
            var gameDirectory = RequireDirectory(GameDirectoryTextBox.Text, "游戏目录");
            var workshopDirectory = string.IsNullOrWhiteSpace(WorkshopDirectoryTextBox.Text)
                ? null
                : Path.GetFullPath(WorkshopDirectoryTextBox.Text.Trim());
            var additionalLocalModDirectory = string.IsNullOrWhiteSpace(LocalModDirectoryTextBox.Text)
                ? null
                : RequireDirectory(LocalModDirectoryTextBox.Text, "本地 Mod 目录");
            var profile = SteamDiscovery.OpenProfile(ProfileDirectoryTextBox.Text.Trim());
            var jarPath = Path.Combine(AppContext.BaseDirectory, "tools", "DDSaveEditor", "DDSaveEditor.jar");
            var codec = new DsonSaveCodec(jarPath);
            AppendStatus(
                "正在读取当前档案启用的 DLC、Workshop 与本地 Mod，再扫描可计数物品、饰品和人物定义……" +
                (additionalLocalModDirectory is null
                    ? string.Empty
                    : $" 本地 Mod 目录：{additionalLocalModDirectory}"));
            CrashDiagnostics.SetStage("LoadCatalog: resolving active content");
            var activeContent = await ActiveContentResolver.ResolveAsync(
                profile,
                gameDirectory,
                workshopDirectory,
                additionalLocalModDirectory,
                codec, workspaceRoot: locations?.WorkspaceDirectory, cancellationToken: loadCancellation.Token);
            if (generation != _catalogGeneration) return;
            CrashDiagnostics.SetStage("LoadCatalog: preparing missing Mod manifests");
            var manifestProgress = new Progress<string>(message =>
            {
                if (generation == _catalogGeneration) AppendStatus(message);
            });
            await Task.Run(() => new ModManifestPreparationService().EnsureAsync(activeContent,
                gameDirectory, manifestProgress, loadCancellation.Token), loadCancellation.Token);
            if (generation != _catalogGeneration) return;
            diagnosticBatch.Add("活动来源", activeContent.Issues);
            CrashDiagnostics.RecordStatus(
                $"内容目录档案：ID={activeContent.Profile.ProfileId}；" +
                $"档案目录={activeContent.Profile.ProfileDirectory}；" +
                $"persist.game.json SHA-256={activeContent.SourceGameSha256}");
            CrashDiagnostics.SetStage("LoadCatalog: inventorying active Mod files");
            var inventory = await Task.Run(() => ScanContentFilesForDiagnostics(activeContent));
            await RecordContentFileDiagnosticsAsync(inventory);
            CrashDiagnostics.SetStage("LoadCatalog: building content catalogs");
            var contentFingerprint = await Task.Run(() => ProfileCatalogContentFingerprint.Capture(activeContent.Sources));
            var staticCatalogTask = Task.Run(() => new
            {
                Trinkets = diagnosticBatch.Capture("饰品", () => TrinketCatalog.Load(activeContent), catalog => catalog.Issues),
                Heroes = diagnosticBatch.Capture("人物/怪癖/姓名", () => HeroClassCatalog.Load(activeContent), catalog => catalog.Issues)
            });
            var quantityItemCatalogTask = diagnosticBatch.CaptureAsync("物品",
                () => QuantityItemCatalog.LoadAsync(activeContent, codec), catalog => catalog.Issues);
            await Task.WhenAll(staticCatalogTask, quantityItemCatalogTask);
            var catalogs = await staticCatalogTask;
            var quantityItems = await quantityItemCatalogTask;
            if (generation != _catalogGeneration) return;
            CrashDiagnostics.SetStage("LoadCatalog: assigning catalog results");
            _allItems = quantityItems.Items;
            _allTrinkets = catalogs.Trinkets.Trinkets;
            _allHeroes = catalogs.Heroes.HeroClasses;
            _heroCatalog = catalogs.Heroes;
            _trinketStorage = catalogs.Trinkets.Storage;
            _raidInventoryStorage = quantityItems.RaidStorage;
            _activeContentSnapshot = activeContent;
            PopulateHeroLevels(catalogs.Heroes);
            _catalogProfileDirectory = profile.ProfileDirectory;
            _catalogGameSaveSha256 = activeContent.SourceGameSha256;
            var estateSaveSha256 = ComputeSha256(profile.EstateSavePath);
            _catalogEstateSaveSha256 = estateSaveSha256;
            _catalogQuantitySaveSha256 = quantityItems.SourceSaveSha256;
            _quantitySaveContext = quantityItems.SaveContext;
            ItemTab.Header = _quantitySaveContext == QuantityItemSaveContext.Raid
                ? "副本背包  /  RAID ITEMS"
                : "小镇物品  /  ESTATE ITEMS";
            try
            {
                _ = await BattleMapPanel.LoadProfileAsync(
                    profile,
                    codec,
                    gameDirectory,
                    activeContent,
                    workshopDirectory,
                    additionalLocalModDirectory,
                    diagnosticBatch: diagnosticBatch);
            }
            catch (Exception mapException)
            {
                CrashDiagnostics.RecordException(
                    "LoadCatalog: battle map snapshot",
                    mapException,
                    $"档案={profile.ProfileId}；目录={profile.ProfileDirectory}");
                AppendStatus(
                    $"战斗地图暂时无法读取，其他目录仍已正常加载：{mapException.Message}", level: DiagnosticLogLevel.Warning);
            }
            CrashDiagnostics.SetStage("LoadCatalog: populating visible rows");
            UpdateCatalogMode();
            ApplyFilter();
            CrashDiagnostics.SetStage("LoadCatalog: recording catalog diagnostics");
            var hiddenItemCount = _allItems.Count(item => item.IsHiddenByDefault);
            var defaultVisibleItemCount = _allItems.Count - hiddenItemCount;
            var diagnostics = diagnosticBatch.Summarize();
            var quantitySourcePath = _quantitySaveContext == QuantityItemSaveContext.Raid
                ? activeContent.Profile.RaidSavePath
                : profile.EstateSavePath;
            CrashDiagnostics.RecordStatus(
                $"内容目录数量快照：场景={FormatQuantitySaveContext(_quantitySaveContext)}；" +
                $"数量来源文件={Path.GetFullPath(quantitySourcePath)}；" +
                $"SHA-256={quantityItems.SourceSaveSha256}" +
                (_quantitySaveContext == QuantityItemSaveContext.Raid
                    ? $"；persist.estate.json SHA-256={estateSaveSha256}"
                    : string.Empty));
            AppendStatus(
                $"目录扫描完成：档案 {profile.ProfileId}；模式 {catalogs.Heroes.GameMode}；" +
                CatalogLogDiagnostics.FormatSourceCounts(activeContent) + "；" +
                $"本轮目录日志说明 {diagnostics.Count(entry => entry.Level == DiagnosticLogLevel.Information)} 条，" +
                $"警告 {diagnostics.Count(entry => entry.Level == DiagnosticLogLevel.Warning)} 条（含战斗目录，跨模块按文件/原因合并，不等于不可用内容数量）。");
            AppendStatus(
                $"目录统计：{FormatQuantitySaveContext(_quantitySaveContext)}物品 {defaultVisibleItemCount} 个" +
                $"（当前场景隐藏项 {hiddenItemCount} 个）" +
                (_quantitySaveContext == QuantityItemSaveContext.Raid
                    ? $"；副本格位 {quantityItems.RaidOccupiedSlots}/" +
                      $"{FormatRaidInventoryCapacity(_raidInventoryStorage)}"
                    : string.Empty) +
                $"；饰品 {_allTrinkets.Count} 个；仓库槽位 {FormatStorageCapacity(_trinketStorage)}；" +
                $"人物 {_allHeroes.Count} 个；怪癖定义 {catalogs.Heroes.InitialQuirks.Count} 个；" +
                $"姓名 {catalogs.Heroes.HeroNames.Count} 个；" +
                $"等级 0-{Math.Max(0, catalogs.Heroes.ResolveLevelThresholds.Count - 1)}。" +
                $" 人物线索：有招募事件的人物 {_allHeroes.Count(item => item.RecruitEvents.Count > 0)} 个；" +
                $"有后续玩法怪癖线索的人物 {_allHeroes.Count(item => item.RuntimeQuirkSignals.Count > 0)} 个" +
                "（不作为初始怪癖）。");
            CrashDiagnostics.RecordCatalogDiagnostics(diagnosticBatch);

            foreach (var entry in diagnostics.Take(30))
            {
                AppendStatus(entry.Message, persist: false);
            }
            if (generation != _catalogGeneration) return;

            if (diagnostics.Count > 30)
            {
                AppendStatus($"另有 {diagnostics.Count - 30} 条目录说明/警告仅写入完整日志。", persist: false);
            }

            CrashDiagnostics.SetStage("LoadCatalog: waiting for initial profile sync");
            await StartProfileSyncAsync(activeContent, quantityItems, codec, gameDirectory,
                workshopDirectory, additionalLocalModDirectory, contentFingerprint, locations);
            CrashDiagnostics.SetStage("LoadCatalog: initial profile sync completed");
            AppendStatus("当前存档载入完成，首次同步已完成。");
        }
        catch (OperationCanceledException) when (loadCancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            CrashDiagnostics.RecordException("LoadCatalog handled exception", ex);
            AppendStatusSafely($"载入当前存档未完成：{ex.Message}", "LoadCatalog failure status", DiagnosticLogLevel.Error);
        }
        finally
        {
            // Flush partial results even if another catalog or UI update failed.
            CrashDiagnostics.RecordCatalogDiagnostics(diagnosticBatch);
            if (ReferenceEquals(_catalogLoadCancellation, loadCancellation)) _catalogLoadCancellation = null;
            try
            {
                CrashDiagnostics.SetStage("LoadCatalog: leaving busy state");
                _catalogLoading = false;
                UpdateEnabledState();
                CrashDiagnostics.SetStage("LoadCatalog: handler returned; waiting for Dispatcher");
                ScheduleLoadCatalogDispatcherProbes();
            }
            catch (Exception ex)
            {
                CrashDiagnostics.RecordException("LoadCatalog cleanup exception", ex);
                AppendStatusSafely($"恢复界面状态失败：{ex.Message}", "LoadCatalog cleanup status", DiagnosticLogLevel.Error);
            }
        }
    }

    private void ScheduleLoadCatalogDispatcherProbes()
    {
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.DataBind,
            new Action(() => CrashDiagnostics.SetStage("LoadCatalog: Dispatcher DataBind reached")));
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Render,
            new Action(() => CrashDiagnostics.SetStage("LoadCatalog: Dispatcher Render reached")));
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() => CrashDiagnostics.SetStage("LoadCatalog: Dispatcher ContextIdle reached")));
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() => CrashDiagnostics.SetStage("LoadCatalog: Dispatcher ApplicationIdle reached")));
    }

}
