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

}
