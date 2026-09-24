using System.Windows;
using System.Windows.Controls;
using DarkestDungeonSaveEditor.Core;

namespace DarkestDungeonSaveEditor.App;

public partial class BattleMapView : UserControl
{
    private MenuItem CreateRegionalContentItem(
        PrototypeMapCell target,
        string label,
        BattleMenuIcon icon,
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
            MapSelectionTextBlock.Text = EditorText.Get("BattleMapView_Content_001");
            return;
        }
        var definition = catalog.SelectRegionalContent(kind, snapshot.DungeonId, Random.Shared.NextDouble());
        if (definition is null)
        {
            MapSelectionTextBlock.Text = EditorText.Get("BattleMapView_Content_002");
            return;
        }
        await PlaceContentAsync(target, definition);
    }

    private MenuItem CreateContentPickerItem(
        PrototypeMapCell target,
        string label,
        BattleMenuIcon icon,
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
            MapSelectionTextBlock.Text = EditorText.Get("BattleMapView_Content_003");
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
            MapSelectionTextBlock.Text = EditorText.Get("BattleMapView_Content_003");
            return;
        }
        if (!TryGetWritableContext(target, out var owner, out var profile, out var snapshot, out var service))
        {
            MapSelectionTextBlock.Text = EditorText.Get("BattleMapView_Content_004");
            return;
        }
        if (!definition.IsAvailableInDungeon(snapshot.DungeonId))
        {
            MapSelectionTextBlock.Text = EditorText.Get("BattleMapView_Content_005");
            return;
        }

        var kindLabel = definition.KindLabel;
        var action = target.RawContent == 0 ? EditorText.Get("BattleMapView_Commands_002") : EditorText.Format("BattleMapView_Content_006", target.ContentLabel ?? EditorText.Get("BattleMapView_Commands_013"));
        var contentDescription = definition.IsRegionBound
            ? $"{kindLabel}（{FormatDungeon(definition.OriginDungeonId)}）"
            : $"{kindLabel}: {EditorText.ContentName(definition.LocalizedName, definition.Id)} ({definition.Id})";
        if (!ThemedDialog.Confirm(owner,
                EditorText.Format("BattleMapView_Content_007", target.DisplayName, action, contentDescription) +
                Environment.NewLine + $"ID：{definition.Id}" +
                Environment.NewLine + EditorText.Format("BattleMapView_Commands_049", definition.SourceLabel) +
                Environment.NewLine + EditorText.Get("BattleMapView_Content_008") +
                Environment.NewLine + EditorText.Get("BattleMapView_Commands_040"),
                EditorText.Format("BattleMapView_Commands_020", kindLabel)))
        {
            return;
        }

        await ApplySaveEditAsync(target, profile, snapshot, service,
            BattleMapEditKind.PlaceContent, attachment: definition);
    }
}
