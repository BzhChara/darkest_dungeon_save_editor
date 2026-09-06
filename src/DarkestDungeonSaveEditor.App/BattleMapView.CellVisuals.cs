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
    private void SelectCell(PrototypeMapCell cell)
    {
        _selectedCell = cell;
        foreach (var item in _cells)
        {
            RefreshCellVisual(item);
        }

        MapSelectionTextBlock.Text =
            $"{cell.DisplayName} · {FormatKind(cell.Kind)} · {FormatKnowledge(cell.Knowledge)} · " +
            $"{FormatContent(cell)}；右击查看可用操作。";
    }

    private void RefreshCellVisual(PrototypeMapCell cell)
    {
        var selected = ReferenceEquals(_selectedCell, cell);
        var normalLayer = cell.Kind == PrototypeCellKind.Room ? 20 : 0;
        Panel.SetZIndex(cell.Visual, normalLayer);
        cell.Visual.Opacity = 1;
        var baseSource = ResolveBaseMapSprite(cell);
        var contentMarkerSource = ResolveContentMarkerSprite(cell);
        cell.BaseImage.Source = baseSource;
        cell.ContentMarkerImage.Source = contentMarkerSource;
        var needsCompletionMarker = cell.Kind == PrototypeCellKind.Room &&
                                    cell.Knowledge is PrototypeKnowledge.Visited or
                                        PrototypeKnowledge.Completed;
        var completionMarkerSource = needsCompletionMarker
            ? TryLoadMapIcon("marker_room_visited.png")
            : null;
        var partyMarkerSource = cell.HasParty ? TryLoadMapIcon("indicator.png") : null;
        cell.CompletionMarkerImage.Source = completionMarkerSource;
        cell.PartyMarkerImage.Source = partyMarkerSource;
        cell.SelectionOverlay.BorderBrush = selected
            ? new SolidColorBrush(Color.FromRgb(196, 43, 39))
            : Brushes.Transparent;
        cell.SelectionOverlay.Background = selected
            ? new SolidColorBrush(Color.FromArgb(82, 126, 17, 20))
            : Brushes.Transparent;

        var needsContentMarker = cell.Kind != PrototypeCellKind.Room &&
                                 cell.Content is not PrototypeContent.None and not PrototypeContent.Entrance;
        var missingPartyMarker = cell.HasParty && partyMarkerSource is null;
        var missingCompletionMarker = needsCompletionMarker && completionMarkerSource is null;
        var needsFallback = baseSource is null ||
                            (needsContentMarker && contentMarkerSource is null) ||
                            missingPartyMarker ||
                            missingCompletionMarker;
        cell.FallbackIcon.Visibility = needsFallback ? Visibility.Visible : Visibility.Collapsed;
        cell.FallbackIcon.Text = missingPartyMarker
            ? "◆"
            : missingCompletionMarker
                ? "✓"
                : FormatContentIcon(cell.Content);
        cell.FallbackIcon.Foreground = new SolidColorBrush(missingPartyMarker
            ? Color.FromRgb(225, 194, 103)
            : missingCompletionMarker
                ? Color.FromRgb(187, 178, 133)
                : FormatContentColor(cell.Content));
        var tooltip =
            $"{cell.DisplayName} ({cell.Id})\n{FormatKind(cell.Kind)} · {FormatKnowledge(cell.Knowledge)}\n" +
            $"内容：{FormatContent(cell)}" +
            (cell.MashIndex >= 0 ? $" · 遭遇索引 {cell.MashIndex} / 类型 {cell.MashType}" : string.Empty) +
            $"\n右击打开操作菜单";
        cell.Visual.ToolTip = tooltip;
    }

    private ImageSource? ResolveBaseMapSprite(PrototypeMapCell cell)
    {
        if (cell.Kind == PrototypeCellKind.Room)
        {
            var roomSprite = cell.Content switch
            {
                PrototypeContent.Entrance => "room_entrance.png",
                PrototypeContent.Battle => "room_battle.png",
                PrototypeContent.Boss => "room_boss.png",
                PrototypeContent.Curio => "room_curio.png",
                PrototypeContent.Treasure => "room_treasure.png",
                _ => "room_empty.png"
            };
            return TryLoadMapIcon(roomSprite);
        }

        return TryLoadMapIcon(cell.Knowledge is PrototypeKnowledge.Unknown or PrototypeKnowledge.Scouted
            ? "hall_dim.png"
            : "hall_clear.png");
    }

    private ImageSource? ResolveContentMarkerSprite(PrototypeMapCell cell)
    {
        if (cell.Kind == PrototypeCellKind.Room)
        {
            return null;
        }

        var marker = cell.Content switch
        {
            PrototypeContent.Battle => "marker_battle.png",
            PrototypeContent.Curio => "marker_curio.png",
            PrototypeContent.Trap => "marker_trap.png",
            PrototypeContent.Obstacle => "marker_obstacle.png",
            PrototypeContent.Hunger => "marker_hunger.png",
            PrototypeContent.Treasure => "marker_curio.png",
            PrototypeContent.SecretDoor => "marker_secret.png",
            _ => null
        };
        return marker is null ? null : TryLoadMapIcon(marker);
    }

    private static string FormatKind(PrototypeCellKind kind) => kind switch
    {
        PrototypeCellKind.Room => "房间",
        _ => "普通走廊格"
    };

    private static string FormatKnowledge(PrototypeKnowledge knowledge) => knowledge switch
    {
        PrototypeKnowledge.Unknown => "未探索（全局视野已显示）",
        PrototypeKnowledge.Scouted => "已侦察",
        PrototypeKnowledge.Completed => "已完成",
        _ => "已探索"
    };

    private static string FormatContent(PrototypeMapCell cell)
    {
        if (!string.IsNullOrWhiteSpace(cell.ContentLabel))
        {
            return cell.ContentLabel;
        }

        return cell.Content switch
        {
            PrototypeContent.None => "空白",
            PrototypeContent.Entrance => "入口",
            PrototypeContent.Curio => "奇物",
            PrototypeContent.Battle => "战斗",
            PrototypeContent.Trap => "陷阱",
            PrototypeContent.Treasure => "宝藏",
            PrototypeContent.Boss => "首领遭遇",
            PrototypeContent.Obstacle => "障碍",
            PrototypeContent.Hunger => "进食格",
            PrototypeContent.SecretDoor => "秘密房间入口",
            _ => "未知"
        };
    }

    private static string FormatContentIcon(PrototypeContent content) => content switch
    {
        PrototypeContent.None => "·",
        PrototypeContent.Entrance => "◇",
        PrototypeContent.Curio => "?",
        PrototypeContent.Battle => "⚔",
        PrototypeContent.Trap => "!",
        PrototypeContent.Treasure => "✦",
        PrototypeContent.Boss => "☠",
        PrototypeContent.Obstacle => "▰",
        PrototypeContent.Hunger => "食",
        PrototypeContent.SecretDoor => "◆",
        _ => "·"
    };

    private static Color FormatContentColor(PrototypeContent content) => content switch
    {
        PrototypeContent.Battle => Color.FromRgb(210, 75, 67),
        PrototypeContent.Boss => Color.FromRgb(218, 45, 39),
        PrototypeContent.Trap => Color.FromRgb(211, 164, 64),
        PrototypeContent.Treasure => Color.FromRgb(225, 194, 103),
        PrototypeContent.Curio => Color.FromRgb(187, 178, 133),
        PrototypeContent.Obstacle => Color.FromRgb(142, 122, 84),
        PrototypeContent.Hunger => Color.FromRgb(211, 164, 64),
        PrototypeContent.SecretDoor => Color.FromRgb(94, 160, 151),
        _ => Color.FromRgb(97, 99, 92)
    };

    private enum PrototypeCellKind
    {
        Room,
        Corridor
    }

    private enum PrototypeKnowledge
    {
        Unknown,
        Scouted,
        Visited,
        Completed
    }

    private enum PrototypeContent
    {
        None,
        Entrance,
        Curio,
        Battle,
        Trap,
        Treasure,
        Boss,
        Obstacle,
        Hunger,
        SecretDoor
    }

    private sealed class PrototypeMapCell(
        string id,
        string displayName,
        PrototypeCellKind kind,
        PrototypeKnowledge knowledge,
        PrototypeContent content,
        bool hasParty,
        Grid visual,
        Image baseImage,
        Image contentMarkerImage,
        Image completionMarkerImage,
        Image partyMarkerImage,
        TextBlock fallbackIcon,
        Border selectionOverlay,
        int rawContent,
        int mashIndex,
        int mashType,
        bool hasResidualContentBinding,
        string? sourceAreaId,
        string? sourceTileId)
    {
        public string Id { get; } = id;
        public string DisplayName { get; } = displayName;
        public PrototypeCellKind Kind { get; } = kind;
        public PrototypeKnowledge Knowledge { get; set; } = knowledge;
        public PrototypeContent Content { get; set; } = content;
        public string? ContentLabel { get; set; }
        public bool HasParty { get; set; } = hasParty;
        public Grid Visual { get; } = visual;
        public Image BaseImage { get; } = baseImage;
        public Image ContentMarkerImage { get; } = contentMarkerImage;
        public Image CompletionMarkerImage { get; } = completionMarkerImage;
        public Image PartyMarkerImage { get; } = partyMarkerImage;
        public TextBlock FallbackIcon { get; } = fallbackIcon;
        public Border SelectionOverlay { get; } = selectionOverlay;
        public int RawContent { get; } = rawContent;
        public int MashIndex { get; } = mashIndex;
        public int MashType { get; } = mashType;
        public bool HasResidualContentBinding { get; } = hasResidualContentBinding;
        public string? SourceAreaId { get; } = sourceAreaId;
        public string? SourceTileId { get; } = sourceTileId;
    }
}
