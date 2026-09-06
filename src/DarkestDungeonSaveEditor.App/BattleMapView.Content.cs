using System.Windows;
using System.Windows.Controls;
using DarkestDungeonSaveEditor.Core;

namespace DarkestDungeonSaveEditor.App;

public partial class BattleMapView : UserControl
{
    private MenuItem CreateRegionalContentItem(
        PrototypeMapCell target,
        string label,
        string icon,
        BattleRoomAttachmentKind kind)
    {
        var catalog = GetCurrentRoomAttachmentCatalog();
        var candidates = catalog?.GetRegionalCandidates(kind, _currentSnapshot?.DungeonId ?? string.Empty) ?? [];
        if (candidates.Count == 0)
        {
            var unavailable = CreateMenuItem($"{label}（0）", icon);
            unavailable.IsEnabled = false;
            return unavailable;
        }
        return CreateAsyncActionMenuItem(label, icon, () => PlaceRegionalContentAsync(target, kind));
    }

    private async Task PlaceRegionalContentAsync(PrototypeMapCell target, BattleRoomAttachmentKind kind)
    {
        CloseActiveContextMenu();
        var catalog = GetCurrentRoomAttachmentCatalog();
        if (catalog is null ||
            !TryGetWritableContext(target, out _, out _, out var snapshot, out _))
        {
            MapSelectionTextBlock.Text = "当前地图或内容目录已经变化，请重新选择目标。";
            return;
        }
        var definition = catalog.SelectRegionalContent(kind, snapshot.DungeonId, Random.Shared.NextDouble());
        if (definition is null)
        {
            MapSelectionTextBlock.Text = "当前区域没有可自动生成的对应资源。";
            return;
        }
        await PlaceContentAsync(target, definition);
    }

    private MenuItem CreateContentPickerItem(
        PrototypeMapCell target,
        string label,
        string icon,
        IReadOnlyCollection<BattleRoomAttachmentDefinition> candidates)
    {
        if (candidates.Count == 0)
        {
            var unavailable = CreateMenuItem($"{label}（0）", icon);
            unavailable.IsEnabled = false;
            return unavailable;
        }

        return CreateAsyncActionMenuItem($"{label}（{candidates.Count}）", icon,
            () => SelectAndPlaceContentAsync(target, candidates, label));
    }

    private async Task SelectAndPlaceContentAsync(
        PrototypeMapCell target,
        IReadOnlyCollection<BattleRoomAttachmentDefinition> candidates,
        string kindLabel)
    {
        if (GetCurrentRoomAttachmentCatalog() is null || Window.GetWindow(this) is not { } dialogOwner)
        {
            MapSelectionTextBlock.Text = "当前地图内容目录已经失效，请重新加载内容目录。";
            return;
        }

        CloseActiveContextMenu();
        var dialog = new BattleRoomAttachmentSelectionDialog(candidates, kindLabel, preserveBattle: false)
        {
            Owner = dialogOwner
        };
        if (dialog.ShowDialog() != true || dialog.SelectedAttachment is not { } definition)
        {
            return;
        }
        await PlaceContentAsync(target, definition);
    }

    private async Task PlaceContentAsync(
        PrototypeMapCell target,
        BattleRoomAttachmentDefinition definition)
    {
        CloseActiveContextMenu();
        if (GetCurrentRoomAttachmentCatalog() is null)
        {
            MapSelectionTextBlock.Text = "当前地图内容目录已经失效，请重新加载内容目录。";
            return;
        }
        if (!TryGetWritableContext(target, out var owner, out var profile, out var snapshot, out var service))
        {
            MapSelectionTextBlock.Text = "当前地图已刷新或正在处理其他操作，请重新选择目标。";
            return;
        }
        if (!definition.IsAvailableInDungeon(snapshot.DungeonId))
        {
            MapSelectionTextBlock.Text = "当前副本区域已经变化，请重新选择目标。";
            return;
        }

        var kindLabel = definition.KindLabel;
        var action = target.RawContent == 0 ? "新建" : $"替换“{target.ContentLabel ?? "当前内容"}”为";
        var contentDescription = definition.IsRegionBound
            ? $"{kindLabel}（{FormatDungeon(definition.OriginDungeonId)}）"
            : $"{kindLabel}：{definition.ChineseName} / {definition.EnglishName}";
        if (!ThemedDialog.Confirm(owner,
                $"将在 {target.DisplayName} {action}{contentDescription}" +
                Environment.NewLine + $"ID：{definition.Id}" +
                Environment.NewLine + $"来源：{definition.SourceLabel}" +
                Environment.NewLine + "原有事件和残留资源将被替换，探索状态保持不变。" +
                Environment.NewLine + "程序会先完整备份当前档案。",
                $"确认写入{kindLabel}"))
        {
            return;
        }

        await ApplySaveEditAsync(target, profile, snapshot, service,
            BattleMapEditKind.PlaceContent, attachment: definition);
    }
}
