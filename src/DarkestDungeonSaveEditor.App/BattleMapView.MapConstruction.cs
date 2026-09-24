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
    private void RenderSnapshot(BattleMapSnapshot snapshot, bool fitToView)
    {
        BuildSnapshotMap(snapshot);
        _selectedCell = null;
        CancelScheduledRefreshRetry();
        _currentSnapshot = snapshot;
        ShowMapSurface();
        UpdateMapBadge();
        MapTitleTextBlock.Text =
            EditorText.Format("BattleMapView_MapConstruction_002", _profileId ?? EditorText.Get("BattleMapView_MapConstruction_001"), FormatDungeon(snapshot.DungeonId), snapshot.Difficulty);
        MapSelectionTextBlock.Text =
            EditorText.Format("BattleMapView_MapConstruction_003", snapshot.RoomCount, snapshot.CorridorCount) +
            EditorText.Format("BattleMapView_MapConstruction_004", _cells.Count);
        LiveStatusTextBlock.Text =
            EditorText.Format("BattleMapView_LiveRefresh_008", snapshot.ReadAtUtc.ToLocalTime());
        SnapshotRefreshed?.Invoke(snapshot);

        if (fitToView)
        {
            _fitToView = true;
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(FitMapToViewport));
        }
    }

    private void BuildSnapshotMap(BattleMapSnapshot snapshot)
    {
        var visibleTiles = snapshot.Areas
            .SelectMany(area => area.Kind == BattleMapAreaKind.Corridor
                ? area.Tiles.Where(tile => tile.StaticType == 1)
                : area.Tiles)
            .ToArray();
        if (visibleTiles.Length == 0)
        {
            throw new InvalidDataException(EditorText.Get("BattleMapView_MapConstruction_005"));
        }

        var minimumX = visibleTiles.Min(tile => tile.MapX);
        var maximumX = visibleTiles.Max(tile => tile.MapX);
        var minimumY = visibleTiles.Min(tile => tile.MapY);
        var maximumY = visibleTiles.Max(tile => tile.MapY);
        var stagedWidth = Math.Max(320, (maximumX - minimumX) * MapGridTileSize + MapContentMargin * 2);
        var stagedHeight = Math.Max(240, (maximumY - minimumY) * MapGridTileSize + MapContentMargin * 2);
        var stagedCells = new List<PrototypeMapCell>(visibleTiles.Length);

        foreach (var area in snapshot.Areas)
        {
            foreach (var tile in area.Tiles.Where(tile =>
                         area.Kind != BattleMapAreaKind.Corridor || tile.StaticType == 1))
            {
                var kind = area.Kind == BattleMapAreaKind.Room
                    ? PrototypeCellKind.Room
                    : PrototypeCellKind.Corridor;
                var content = ConvertContent(tile.Content);
                var contentLabel = FormatSnapshotContent(tile.Content, tile.RawContent);
                if (kind == PrototypeCellKind.Room &&
                    string.Equals(area.AreaId, snapshot.EntranceAreaId, StringComparison.Ordinal))
                {
                    content = PrototypeContent.Entrance;
                    contentLabel = EditorText.Get("BattleMapView_CellVisuals_011");
                }
                else if (kind == PrototypeCellKind.Room &&
                         string.Equals(area.AreaId, snapshot.FinalRoomId, StringComparison.Ordinal))
                {
                    content = PrototypeContent.Boss;
                    contentLabel = EditorText.Get("BattleMapView_MapConstruction_006");
                }

                var centerX = MapContentMargin + (tile.MapX - minimumX) * MapGridTileSize;
                var centerY = MapContentMargin + (tile.MapY - minimumY) * MapGridTileSize;
                var displayName = kind switch
                {
                    PrototypeCellKind.Room => EditorText.Format("BattleMapView_MapConstruction_007", area.AreaId),
                    _ => EditorText.Format("BattleMapView_MapConstruction_008", area.AreaId, tile.TileId)
                };
                var hasParty =
                    string.Equals(area.AreaId, snapshot.PartyAreaId, StringComparison.Ordinal) &&
                    snapshot.PartyTileIndex == tile.TileIndex;
                stagedCells.Add(CreateCell(
                    $"{area.AreaId}.{tile.TileId}",
                    displayName,
                    kind,
                    centerX,
                    centerY,
                    ConvertKnowledge(tile.Knowledge),
                    content,
                    hasParty,
                    contentLabel,
                    tile.RawContent,
                    tile.MashIndex,
                    tile.MashType,
                    tile.HasResidualContentBinding,
                    area.AreaId,
                    tile.TileId));
            }
        }

        // Construct every visual before touching the live canvas. A malformed or partially
        // written snapshot can therefore never erase or half-replace the previous complete map.
        CloseActiveContextMenu();
        MapCanvas.Children.Clear();
        _cells.Clear();
        MapCanvas.Width = stagedWidth;
        MapCanvas.Height = stagedHeight;
        foreach (var cell in stagedCells)
        {
            _cells.Add(cell);
            AttachCellToMap(cell);
        }
    }

    private static PrototypeKnowledge ConvertKnowledge(BattleMapTileKnowledge knowledge) => knowledge switch
    {
        BattleMapTileKnowledge.Scouted => PrototypeKnowledge.Scouted,
        BattleMapTileKnowledge.Visited => PrototypeKnowledge.Visited,
        _ => PrototypeKnowledge.Unknown
    };

    private static PrototypeContent ConvertContent(BattleMapTileContent content) => content switch
    {
        BattleMapTileContent.Battle or BattleMapTileContent.Ambush => PrototypeContent.Battle,
        BattleMapTileContent.Trap => PrototypeContent.Trap,
        BattleMapTileContent.Obstacle => PrototypeContent.Obstacle,
        BattleMapTileContent.Hunger => PrototypeContent.Hunger,
        BattleMapTileContent.Happening or
            BattleMapTileContent.GuardedCurio or
            BattleMapTileContent.Curio or
            BattleMapTileContent.AmbushCurio => PrototypeContent.Curio,
        BattleMapTileContent.Treasure or
            BattleMapTileContent.GuardedTreasure or
            BattleMapTileContent.AmbushTreasure => PrototypeContent.Treasure,
        BattleMapTileContent.SecretDoor => PrototypeContent.SecretDoor,
        _ => PrototypeContent.None
    };

    private static string FormatSnapshotContent(BattleMapTileContent content, int rawContent) => content switch
    {
        BattleMapTileContent.Nothing => EditorText.Get("BattleMapView_CellVisuals_010"),
        BattleMapTileContent.Battle => EditorText.Get("BattleMapView_CellVisuals_013"),
        BattleMapTileContent.Ambush => EditorText.Get("BattleMapView_MapConstruction_009"),
        BattleMapTileContent.Trap => EditorText.Get("BattleMapView_CellVisuals_014"),
        BattleMapTileContent.Obstacle => EditorText.Get("BattleMapView_CellVisuals_017"),
        BattleMapTileContent.Happening => EditorText.Get("BattleMapView_MapConstruction_010"),
        BattleMapTileContent.GuardedCurio => EditorText.Get("BattleMapView_MapConstruction_011"),
        BattleMapTileContent.Curio => EditorText.Get("BattleMapView_CellVisuals_012"),
        BattleMapTileContent.Hunger => EditorText.Get("BattleMapView_CellVisuals_018"),
        BattleMapTileContent.Treasure => EditorText.Get("BattleMapView_CellVisuals_015"),
        BattleMapTileContent.GuardedTreasure => EditorText.Get("BattleMapView_MapConstruction_012"),
        BattleMapTileContent.AmbushCurio => EditorText.Get("BattleMapView_MapConstruction_013"),
        BattleMapTileContent.AmbushTreasure => EditorText.Get("BattleMapView_MapConstruction_014"),
        BattleMapTileContent.SecretDoor => EditorText.Get("BattleMapView_CellVisuals_019"),
        _ => EditorText.Format("BattleMapView_MapConstruction_015", rawContent)
    };

    private static string FormatDungeon(string dungeonId)
    {
        var name = dungeonId switch
        {
            "ruins" => EditorText.Get("BattleMapView_MapConstruction_016"),
            "warrens" => EditorText.Get("BattleMapView_MapConstruction_017"),
            "weald" => EditorText.Get("BattleMapView_MapConstruction_018"),
            "cove" => EditorText.Get("BattleMapView_MapConstruction_019"),
            "courtyard" => EditorText.Get("BattleMapView_MapConstruction_020"),
            "farmstead" => EditorText.Get("BattleMapView_MapConstruction_021"),
            "darkestdungeon" => EditorText.Get("BattleMapView_MapConstruction_022"),
            _ => string.Empty
        };
        return string.IsNullOrWhiteSpace(name) ? dungeonId : $"{name} / {dungeonId}";
    }

    private void BuildPrototypeMap()
    {
        CloseActiveContextMenu();
        MapCanvas.Children.Clear();
        _cells.Clear();

        AddCell("rooA", EditorText.Get("BattleMapView_MapConstruction_023"), PrototypeCellKind.Room, 80, 80,
            PrototypeKnowledge.Visited, PrototypeContent.Entrance, hasParty: false);
        AddCell("coAB.0", EditorText.Get("BattleEncounterSelectionDialog_015"), PrototypeCellKind.Corridor, 124, 80,
            PrototypeKnowledge.Visited, PrototypeContent.None, hasParty: false);
        AddCell("coAB.1", EditorText.Get("BattleEncounterSelectionDialog_015"), PrototypeCellKind.Corridor, 148, 80,
            PrototypeKnowledge.Visited, PrototypeContent.Curio, hasParty: false);
        AddCell("coAB.2", EditorText.Get("BattleEncounterSelectionDialog_015"), PrototypeCellKind.Corridor, 172, 80,
            PrototypeKnowledge.Visited, PrototypeContent.None, hasParty: false);
        AddCell("coAB.3", EditorText.Get("BattleEncounterSelectionDialog_015"), PrototypeCellKind.Corridor, 196, 80,
            PrototypeKnowledge.Visited, PrototypeContent.None, hasParty: true);
        AddCell("coAB.4", EditorText.Get("BattleEncounterSelectionDialog_015"), PrototypeCellKind.Corridor, 220, 80,
            PrototypeKnowledge.Scouted, PrototypeContent.None, hasParty: false);
        AddCell("coAB.5", EditorText.Get("BattleEncounterSelectionDialog_015"), PrototypeCellKind.Corridor, 244, 80,
            PrototypeKnowledge.Scouted, PrototypeContent.Trap, hasParty: false);
        AddCell("coAB.6", EditorText.Get("BattleEncounterSelectionDialog_015"), PrototypeCellKind.Corridor, 268, 80,
            PrototypeKnowledge.Scouted, PrototypeContent.None, hasParty: false);
        AddCell("coAB.7", EditorText.Get("BattleEncounterSelectionDialog_015"), PrototypeCellKind.Corridor, 292, 80,
            PrototypeKnowledge.Scouted, PrototypeContent.None, hasParty: false);
        AddCell("rooB", EditorText.Get("BattleMapView_MapConstruction_024"), PrototypeCellKind.Room, 336, 80,
            PrototypeKnowledge.Scouted, PrototypeContent.Battle, hasParty: false);

        AddCell("coBC.0", EditorText.Get("BattleEncounterSelectionDialog_015"), PrototypeCellKind.Corridor, 380, 80,
            PrototypeKnowledge.Unknown, PrototypeContent.None, hasParty: false);
        AddCell("coBC.1", EditorText.Get("BattleMapView_MapConstruction_025"), PrototypeCellKind.Corridor, 404, 80,
            PrototypeKnowledge.Unknown, PrototypeContent.None, hasParty: false);
        AddCell("coBC.2", EditorText.Get("BattleMapView_MapConstruction_025"), PrototypeCellKind.Corridor, 428, 80,
            PrototypeKnowledge.Unknown, PrototypeContent.None, hasParty: false);
        AddCell("coBC.3", EditorText.Get("BattleMapView_MapConstruction_025"), PrototypeCellKind.Corridor, 452, 80,
            PrototypeKnowledge.Unknown, PrototypeContent.None, hasParty: false);
        AddCell("coBC.4", EditorText.Get("BattleMapView_MapConstruction_025"), PrototypeCellKind.Corridor, 476, 80,
            PrototypeKnowledge.Unknown, PrototypeContent.None, hasParty: false);
        AddCell("coBC.5", EditorText.Get("BattleMapView_MapConstruction_025"), PrototypeCellKind.Corridor, 500, 80,
            PrototypeKnowledge.Unknown, PrototypeContent.None, hasParty: false);
        AddCell("coBC.6", EditorText.Get("BattleMapView_MapConstruction_025"), PrototypeCellKind.Corridor, 524, 80,
            PrototypeKnowledge.Unknown, PrototypeContent.None, hasParty: false);
        AddCell("coBC.7", EditorText.Get("BattleEncounterSelectionDialog_015"), PrototypeCellKind.Corridor, 548, 80,
            PrototypeKnowledge.Unknown, PrototypeContent.None, hasParty: false);
        AddCell("rooC", EditorText.Get("BattleMapView_MapConstruction_026"), PrototypeCellKind.Room, 592, 80,
            PrototypeKnowledge.Unknown, PrototypeContent.Treasure, hasParty: false);

        AddCell("coBD.0", EditorText.Get("BattleEncounterSelectionDialog_015"), PrototypeCellKind.Corridor, 336, 124,
            PrototypeKnowledge.Visited, PrototypeContent.None, hasParty: false);
        AddCell("coBD.1", EditorText.Get("BattleEncounterSelectionDialog_015"), PrototypeCellKind.Corridor, 336, 148,
            PrototypeKnowledge.Visited, PrototypeContent.None, hasParty: false);
        AddCell("coBD.2", EditorText.Get("BattleEncounterSelectionDialog_015"), PrototypeCellKind.Corridor, 336, 172,
            PrototypeKnowledge.Visited, PrototypeContent.None, hasParty: false);
        AddCell("coBD.3", EditorText.Get("BattleEncounterSelectionDialog_015"), PrototypeCellKind.Corridor, 336, 196,
            PrototypeKnowledge.Visited, PrototypeContent.None, hasParty: false);
        AddCell("coBD.4", EditorText.Get("BattleEncounterSelectionDialog_015"), PrototypeCellKind.Corridor, 336, 220,
            PrototypeKnowledge.Completed, PrototypeContent.None, hasParty: false);
        AddCell("coBD.5", EditorText.Get("BattleEncounterSelectionDialog_015"), PrototypeCellKind.Corridor, 336, 244,
            PrototypeKnowledge.Completed, PrototypeContent.Curio, hasParty: false);
        AddCell("coBD.6", EditorText.Get("BattleEncounterSelectionDialog_015"), PrototypeCellKind.Corridor, 336, 268,
            PrototypeKnowledge.Completed, PrototypeContent.None, hasParty: false);
        AddCell("coBD.7", EditorText.Get("BattleEncounterSelectionDialog_015"), PrototypeCellKind.Corridor, 336, 292,
            PrototypeKnowledge.Completed, PrototypeContent.None, hasParty: false);
        AddCell("rooD", EditorText.Get("BattleMapView_MapConstruction_027"), PrototypeCellKind.Room, 336, 336,
            PrototypeKnowledge.Completed, PrototypeContent.Curio, hasParty: false);

        AddCell("coDE.0", EditorText.Get("BattleEncounterSelectionDialog_015"), PrototypeCellKind.Corridor, 380, 336,
            PrototypeKnowledge.Visited, PrototypeContent.None, hasParty: false);
        AddCell("coDE.1", EditorText.Get("BattleEncounterSelectionDialog_015"), PrototypeCellKind.Corridor, 404, 336,
            PrototypeKnowledge.Visited, PrototypeContent.Obstacle, hasParty: false);
        AddCell("coDE.2", EditorText.Get("BattleEncounterSelectionDialog_015"), PrototypeCellKind.Corridor, 428, 336,
            PrototypeKnowledge.Visited, PrototypeContent.None, hasParty: false);
        AddCell("coDE.3", EditorText.Get("BattleEncounterSelectionDialog_015"), PrototypeCellKind.Corridor, 452, 336,
            PrototypeKnowledge.Visited, PrototypeContent.None, hasParty: false);
        AddCell("coDE.4", EditorText.Get("BattleEncounterSelectionDialog_015"), PrototypeCellKind.Corridor, 476, 336,
            PrototypeKnowledge.Visited, PrototypeContent.None, hasParty: false);
        AddCell("coDE.5", EditorText.Get("BattleEncounterSelectionDialog_015"), PrototypeCellKind.Corridor, 500, 336,
            PrototypeKnowledge.Visited, PrototypeContent.None, hasParty: false);
        AddCell("coDE.6", EditorText.Get("BattleEncounterSelectionDialog_015"), PrototypeCellKind.Corridor, 524, 336,
            PrototypeKnowledge.Visited, PrototypeContent.None, hasParty: false);
        AddCell("coDE.7", EditorText.Get("BattleEncounterSelectionDialog_015"), PrototypeCellKind.Corridor, 548, 336,
            PrototypeKnowledge.Visited, PrototypeContent.None, hasParty: false);
        AddCell("rooE", EditorText.Get("BattleMapView_MapConstruction_028"), PrototypeCellKind.Room, 592, 336,
            PrototypeKnowledge.Visited, PrototypeContent.Boss, hasParty: false);

        AddCell("coAF.0", EditorText.Get("BattleEncounterSelectionDialog_015"), PrototypeCellKind.Corridor, 80, 124,
            PrototypeKnowledge.Scouted, PrototypeContent.None, hasParty: false);
        AddCell("coAF.1", EditorText.Get("BattleEncounterSelectionDialog_015"), PrototypeCellKind.Corridor, 80, 148,
            PrototypeKnowledge.Scouted, PrototypeContent.Curio, hasParty: false);
        AddCell("coAF.2", EditorText.Get("BattleEncounterSelectionDialog_015"), PrototypeCellKind.Corridor, 80, 172,
            PrototypeKnowledge.Scouted, PrototypeContent.None, hasParty: false);
        AddCell("coAF.3", EditorText.Get("BattleEncounterSelectionDialog_015"), PrototypeCellKind.Corridor, 80, 196,
            PrototypeKnowledge.Scouted, PrototypeContent.None, hasParty: false);
        AddCell("coAF.4", EditorText.Get("BattleEncounterSelectionDialog_015"), PrototypeCellKind.Corridor, 80, 220,
            PrototypeKnowledge.Scouted, PrototypeContent.None, hasParty: false);
        AddCell("coAF.5", EditorText.Get("BattleEncounterSelectionDialog_015"), PrototypeCellKind.Corridor, 80, 244,
            PrototypeKnowledge.Scouted, PrototypeContent.None, hasParty: false);
        AddCell("coAF.6", EditorText.Get("BattleEncounterSelectionDialog_015"), PrototypeCellKind.Corridor, 80, 268,
            PrototypeKnowledge.Scouted, PrototypeContent.None, hasParty: false);
        AddCell("coAF.7", EditorText.Get("BattleEncounterSelectionDialog_015"), PrototypeCellKind.Corridor, 80, 292,
            PrototypeKnowledge.Scouted, PrototypeContent.None, hasParty: false);
        AddCell("rooF", EditorText.Get("BattleMapView_MapConstruction_029"), PrototypeCellKind.Room, 80, 336,
            PrototypeKnowledge.Scouted, PrototypeContent.None, hasParty: false);
    }

    private void AddCell(
        string id,
        string displayName,
        PrototypeCellKind kind,
        double centerX,
        double centerY,
        PrototypeKnowledge knowledge,
        PrototypeContent content,
        bool hasParty,
        string? contentLabel = null,
        int rawContent = 0,
        int mashIndex = -1,
        int mashType = 7,
        bool hasResidualContentBinding = false,
        string? sourceAreaId = null,
        string? sourceTileId = null)
    {
        var cell = CreateCell(
            id,
            displayName,
            kind,
            centerX,
            centerY,
            knowledge,
            content,
            hasParty,
            contentLabel,
            rawContent,
            mashIndex,
            mashType,
            hasResidualContentBinding,
            sourceAreaId,
            sourceTileId);
        _cells.Add(cell);
        AttachCellToMap(cell);
    }

    private void AttachCellToMap(PrototypeMapCell cell)
    {
        MapCanvas.Children.Add(cell.Visual);
    }

    private PrototypeMapCell CreateCell(
        string id,
        string displayName,
        PrototypeCellKind kind,
        double centerX,
        double centerY,
        PrototypeKnowledge knowledge,
        PrototypeContent content,
        bool hasParty,
        string? contentLabel = null,
        int rawContent = 0,
        int mashIndex = -1,
        int mashType = 7,
        bool hasResidualContentBinding = false,
        string? sourceAreaId = null,
        string? sourceTileId = null)
    {
        var size = kind == PrototypeCellKind.Room ? 64 : 24;
        var baseImage = new Image
        {
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            IsHitTestVisible = false
        };
        RenderOptions.SetBitmapScalingMode(baseImage, BitmapScalingMode.HighQuality);
        var contentMarkerImage = new Image
        {
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        RenderOptions.SetBitmapScalingMode(contentMarkerImage, BitmapScalingMode.HighQuality);
        var completionMarkerImage = new Image
        {
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        RenderOptions.SetBitmapScalingMode(completionMarkerImage, BitmapScalingMode.HighQuality);
        var partyMarkerImage = new Image
        {
            Width = kind == PrototypeCellKind.Room ? 43 : 27,
            Height = kind == PrototypeCellKind.Room ? 41 : 26,
            Margin = kind == PrototypeCellKind.Room
                ? new Thickness(0, -7, 0, 0)
                : new Thickness(0, -9, 0, 0),
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        RenderOptions.SetBitmapScalingMode(partyMarkerImage, BitmapScalingMode.HighQuality);
        var fallbackIcon = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            FontFamily = new FontFamily("SimSun, Segoe UI Symbol"),
            FontSize = kind == PrototypeCellKind.Room ? 25 : 14,
            FontWeight = FontWeights.Bold,
            IsHitTestVisible = false
        };
        var selectionOverlay = new Border
        {
            BorderThickness = new Thickness(2),
            BorderBrush = Brushes.Transparent,
            Background = Brushes.Transparent,
            IsHitTestVisible = false
        };
        var visual = new Grid
        {
            Width = size,
            Height = size,
            Background = Brushes.Transparent,
            SnapsToDevicePixels = true,
            Cursor = Cursors.Hand
        };
        visual.Children.Add(baseImage);
        visual.Children.Add(contentMarkerImage);
        visual.Children.Add(completionMarkerImage);
        visual.Children.Add(fallbackIcon);
        visual.Children.Add(partyMarkerImage);
        visual.Children.Add(selectionOverlay);
        Canvas.SetLeft(visual, centerX - size / 2);
        Canvas.SetTop(visual, centerY - size / 2);

        var cell = new PrototypeMapCell(
            id,
            displayName,
            kind,
            knowledge,
            content,
            hasParty,
            visual,
            baseImage,
            contentMarkerImage,
            completionMarkerImage,
            partyMarkerImage,
            fallbackIcon,
            selectionOverlay,
            rawContent,
            mashIndex,
            mashType,
            hasResidualContentBinding,
            sourceAreaId,
            sourceTileId)
        {
            ContentLabel = contentLabel
        };
        visual.PreviewMouseLeftButtonDown += (_, e) =>
        {
            SelectCell(cell);
            e.Handled = true;
        };
        visual.PreviewMouseRightButtonDown += (_, _) =>
        {
            SelectCell(cell);
            visual.ContextMenu = BuildContextMenu(cell);
        };

        RefreshCellVisual(cell);
        return cell;
    }

}
