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
            var gameDirectory = RequireDirectory(GameDirectoryTextBox.Text, EditorText.Get("MainWindow_CatalogLoading_001"));
            var workshopDirectory = string.IsNullOrWhiteSpace(WorkshopDirectoryTextBox.Text)
                ? null
                : Path.GetFullPath(WorkshopDirectoryTextBox.Text.Trim());
            var additionalLocalModDirectory = string.IsNullOrWhiteSpace(LocalModDirectoryTextBox.Text)
                ? null
                : RequireDirectory(LocalModDirectoryTextBox.Text, EditorText.Get("MainWindow_CatalogLoading_002"));
            var profile = SteamDiscovery.OpenProfile(ProfileDirectoryTextBox.Text.Trim());
            var jarPath = Path.Combine(AppContext.BaseDirectory, "tools", "DDSaveEditor", "DDSaveEditor.jar");
            var codec = new DsonSaveCodec(jarPath);
            AppendStatus(
                EditorText.Get("MainWindow_CatalogLoading_003") +
                (additionalLocalModDirectory is null
                    ? string.Empty
                    : EditorText.Format("MainWindow_CatalogLoading_004", additionalLocalModDirectory)));
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
            diagnosticBatch.Add(EditorText.Get("MainWindow_CatalogLoading_005"), activeContent.Issues);
            CrashDiagnostics.RecordStatus(
                EditorText.Format("MainWindow_CatalogLoading_006", activeContent.Profile.ProfileId) +
                EditorText.Format("MainWindow_CatalogLoading_007", activeContent.Profile.ProfileDirectory) +
                $"persist.game.json SHA-256={activeContent.SourceGameSha256}");
            CrashDiagnostics.SetStage("LoadCatalog: inventorying active Mod files");
            var inventory = await Task.Run(() => ScanContentFilesForDiagnostics(activeContent));
            await RecordContentFileDiagnosticsAsync(inventory);
            CrashDiagnostics.SetStage("LoadCatalog: building content catalogs");
            var contentFingerprint = await Task.Run(() => ProfileCatalogContentFingerprint.Capture(activeContent.Sources));
            var staticCatalogTask = Task.Run(() => new
            {
                Trinkets = diagnosticBatch.Capture(EditorText.Get("MainWindow_CatalogLoading_008"), () => TrinketCatalog.Load(activeContent), catalog => catalog.Issues),
                Heroes = diagnosticBatch.Capture(EditorText.Get("MainWindow_CatalogLoading_009"), () => HeroClassCatalog.Load(activeContent), catalog => catalog.Issues)
            });
            var quantityItemCatalogTask = diagnosticBatch.CaptureAsync(EditorText.Get("MainWindow_CatalogLoading_010"),
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
                ? EditorText.Get("MainWindow_CatalogLoading_011")
                : EditorText.Get("MainWindow_CatalogLoading_012");
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
                    EditorText.Format("MainWindow_CatalogLoading_013", profile.ProfileId, profile.ProfileDirectory));
                AppendStatus(
                    EditorText.Format("MainWindow_CatalogLoading_014", mapException.Message), level: DiagnosticLogLevel.Warning);
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
                EditorText.Format("MainWindow_CatalogLoading_015", FormatQuantitySaveContext(_quantitySaveContext)) +
                EditorText.Format("MainWindow_CatalogLoading_016", Path.GetFullPath(quantitySourcePath)) +
                $"SHA-256={quantityItems.SourceSaveSha256}" +
                (_quantitySaveContext == QuantityItemSaveContext.Raid
                    ? $"；persist.estate.json SHA-256={estateSaveSha256}"
                    : string.Empty));
            AppendStatus(
                EditorText.Format("MainWindow_CatalogLoading_017", profile.ProfileId, catalogs.Heroes.GameMode) +
                CatalogLogDiagnostics.FormatSourceCounts(activeContent) + "；" +
                EditorText.Format("MainWindow_CatalogLoading_018", diagnostics.Count(entry => entry.Level == DiagnosticLogLevel.Information)) +
                EditorText.Format("MainWindow_CatalogLoading_019", diagnostics.Count(entry => entry.Level == DiagnosticLogLevel.Warning)));
            AppendStatus(
                EditorText.Format("MainWindow_CatalogLoading_020", FormatQuantitySaveContext(_quantitySaveContext), defaultVisibleItemCount) +
                EditorText.Format("MainWindow_CatalogLoading_021", hiddenItemCount) +
                (_quantitySaveContext == QuantityItemSaveContext.Raid
                    ? EditorText.Format("MainWindow_CatalogLoading_022", quantityItems.RaidOccupiedSlots) +
                      $"{FormatRaidInventoryCapacity(_raidInventoryStorage)}"
                    : string.Empty) +
                EditorText.Format("MainWindow_CatalogLoading_023", _allTrinkets.Count, FormatStorageCapacity(_trinketStorage)) +
                EditorText.Format("MainWindow_CatalogLoading_024", _allHeroes.Count, catalogs.Heroes.InitialQuirks.Count) +
                EditorText.Format("MainWindow_CatalogLoading_025", catalogs.Heroes.HeroNames.Count) +
                EditorText.Format("MainWindow_CatalogLoading_026", Math.Max(0, catalogs.Heroes.ResolveLevelThresholds.Count - 1)) +
                EditorText.Format("MainWindow_CatalogLoading_027", _allHeroes.Count(item => item.RecruitEvents.Count > 0)) +
                EditorText.Format("MainWindow_CatalogLoading_028", _allHeroes.Count(item => item.RuntimeQuirkSignals.Count > 0)) +
                EditorText.Get("MainWindow_CatalogLoading_029"));
            CrashDiagnostics.RecordCatalogDiagnostics(diagnosticBatch);

            foreach (var entry in diagnostics.Take(30))
            {
                AppendStatus(entry.Message, persist: false);
            }
            if (generation != _catalogGeneration) return;

            if (diagnostics.Count > 30)
            {
                AppendStatus(EditorText.Format("MainWindow_CatalogLoading_030", diagnostics.Count - 30), persist: false);
            }

            CrashDiagnostics.SetStage("LoadCatalog: waiting for initial profile sync");
            await StartProfileSyncAsync(activeContent, quantityItems, codec, gameDirectory,
                workshopDirectory, additionalLocalModDirectory, contentFingerprint, locations);
            CrashDiagnostics.SetStage("LoadCatalog: initial profile sync completed");
            AppendStatus(EditorText.Get("MainWindow_CatalogLoading_031"));
        }
        catch (OperationCanceledException) when (loadCancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            CrashDiagnostics.RecordException("LoadCatalog handled exception", ex);
            AppendStatusSafely(EditorText.Format("MainWindow_CatalogLoading_032", ex.Message), "LoadCatalog failure status", DiagnosticLogLevel.Error);
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
                AppendStatusSafely(EditorText.Format("MainWindow_CatalogLoading_033", ex.Message), "LoadCatalog cleanup status", DiagnosticLogLevel.Error);
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
