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
        try
        {
            CrashDiagnostics.SetStage("LoadCatalog: invalidating previous catalog");
            InvalidateCatalog();
            CrashDiagnostics.SetStage("LoadCatalog: entering busy state");
            SetBusy(true);
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
                codec);
            CrashDiagnostics.RecordStatus(
                $"内容目录档案：ID={activeContent.Profile.ProfileId}；" +
                $"档案目录={activeContent.Profile.ProfileDirectory}；" +
                $"persist.game.json SHA-256={activeContent.SourceGameSha256}");
            CrashDiagnostics.SetStage("LoadCatalog: building content catalogs");
            var staticCatalogTask = Task.Run(() => new
            {
                Trinkets = TrinketCatalog.Load(activeContent),
                Heroes = HeroClassCatalog.Load(activeContent)
            });
            var quantityItemCatalogTask = QuantityItemCatalog.LoadAsync(activeContent, codec);
            await Task.WhenAll(staticCatalogTask, quantityItemCatalogTask);
            var catalogs = await staticCatalogTask;
            var quantityItems = await quantityItemCatalogTask;
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
                    additionalLocalModDirectory);
            }
            catch (Exception mapException)
            {
                CrashDiagnostics.RecordException(
                    "LoadCatalog: battle map snapshot",
                    mapException,
                    $"档案={profile.ProfileId}；目录={profile.ProfileDirectory}");
                AppendStatus(
                    $"战斗地图暂时无法读取，其他目录仍已正常加载：{mapException.Message}");
            }
            CrashDiagnostics.SetStage("LoadCatalog: populating visible rows");
            UpdateCatalogMode();
            ApplyFilter();
            CrashDiagnostics.SetStage("LoadCatalog: recording catalog diagnostics");
            var hiddenItemCount = _allItems.Count(item => item.IsHiddenByDefault);
            var defaultVisibleItemCount = _allItems.Count - hiddenItemCount;
            var issues = activeContent.Issues
                .Concat(quantityItems.Issues)
                .Concat(catalogs.Trinkets.Issues)
                .Concat(catalogs.Heroes.Issues)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var quantitySourcePath = _quantitySaveContext == QuantityItemSaveContext.Raid
                ? Path.Combine(profile.ProfileDirectory, "persist.raid.json")
                : profile.EstateSavePath;
            CrashDiagnostics.RecordStatus(
                $"内容目录数量快照：场景={FormatQuantitySaveContext(_quantitySaveContext)}；" +
                $"数量来源文件={Path.GetFullPath(quantitySourcePath)}；" +
                $"SHA-256={quantityItems.SourceSaveSha256}" +
                (_quantitySaveContext == QuantityItemSaveContext.Raid
                    ? $"；persist.estate.json SHA-256={estateSaveSha256}"
                    : string.Empty));
            AppendStatus(
                $"目录加载完成：档案 {profile.ProfileId}；模式 {catalogs.Heroes.GameMode}；" +
                $"档案启用 Mod {activeContent.AppliedModCount} 个；活动来源 {activeContent.Sources.Count} 个；" +
                $"读取提示 {issues.Length} 条。");
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
            var issueMessages = issues
                .Select(issue => $"目录提示：{issue}")
                .ToArray();
            foreach (var issueMessage in issueMessages)
            {
                CrashDiagnostics.RecordStatus(issueMessage);
            }

            foreach (var issueMessage in issueMessages.Take(30))
            {
                AppendStatus(issueMessage, persist: false);
            }

            if (issueMessages.Length > 30)
            {
                AppendStatus($"另有 {issueMessages.Length - 30} 条目录提示仅写入完整日志。", persist: false);
            }

            CrashDiagnostics.SetStage("LoadCatalog: synchronous UI update completed");
        }
        catch (Exception ex)
        {
            CrashDiagnostics.RecordException("LoadCatalog handled exception", ex);
            AppendStatusSafely($"加载内容目录失败：{ex.Message}", "LoadCatalog failure status");
        }
        finally
        {
            try
            {
                CrashDiagnostics.SetStage("LoadCatalog: leaving busy state");
                SetBusy(false);
                CrashDiagnostics.SetStage("LoadCatalog: handler returned; waiting for Dispatcher");
                ScheduleLoadCatalogDispatcherProbes();
            }
            catch (Exception ex)
            {
                CrashDiagnostics.RecordException("LoadCatalog cleanup exception", ex);
                AppendStatusSafely($"恢复界面状态失败：{ex.Message}", "LoadCatalog cleanup status");
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
