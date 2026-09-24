using System.Text.Json.Nodes;

namespace DarkestDungeonSaveEditor.Core;

internal static partial class BattleMapSaveEditor
{
    internal static BattleMapEditPreview PlaceContent(
        JsonObject mapDocument,
        JsonObject raidDocument,
        BattleMapSnapshot snapshot,
        string areaId,
        string tileId,
        BattleRoomAttachmentDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var (area, tile) = ResolveEditableTile(snapshot, areaId, tileId);
        ValidateStationaryRaidState(raidDocument);
        if (area.Kind != definition.TargetAreaKind)
        {
            throw new InvalidOperationException(EditorText.Get("BattleMapSaveEditor_Content_001"));
        }
        if (!definition.IsAvailableInDungeon(snapshot.DungeonId))
        {
            throw new InvalidOperationException(EditorText.Get("BattleMapSaveEditor_Content_002"));
        }
        if (string.Equals(area.AreaId, snapshot.EntranceAreaId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(EditorText.Get("BattleMapSaveEditor_Content_003"));
        }
        if (string.Equals(area.AreaId, snapshot.FinalRoomId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(EditorText.Get("BattleMapSaveEditor_Content_004"));
        }
        if (tile.Content is BattleMapTileContent.Hunger or BattleMapTileContent.SecretDoor or
            BattleMapTileContent.Ambush or BattleMapTileContent.Happening or
            BattleMapTileContent.AmbushCurio or BattleMapTileContent.AmbushTreasure or
            BattleMapTileContent.Unknown)
        {
            throw new InvalidOperationException(EditorText.Get("BattleMapSaveEditor_Content_005"));
        }
        if (definition.PropHash == 0 ||
            definition.PropHash != BattleRoomAttachmentCatalog.ComputePropHash(definition.Id))
        {
            throw new InvalidOperationException(EditorText.Get("BattleMapSaveEditor_Content_006"));
        }

        var staticTile = ResolveStaticTile(mapDocument, area.AreaId, tile.TileId);
        var dynamicTile = ResolveDynamicTile(mapDocument, area.AreaId, tile.TileId);
        if (ReadRequiredInt(dynamicTile, "content") != tile.RawContent)
        {
            throw new InvalidOperationException(EditorText.Get("BattleMapSaveEditor_Content_007"));
        }

        // A whole-cell replacement is not an attachment edit. Clear every old event
        // binding (including consumed props), but never change knowledge or topology.
        SetScalarToZero(staticTile, "cur");
        SetScalarToZero(staticTile, "obstacle");
        SetScalarToZero(dynamicTile, "curio_prop");
        SetScalarToZero(dynamicTile, "trap");
        dynamicTile["mash_index"] = -1;
        dynamicTile["mash_type"] = 7;
        dynamicTile["content"] = (int)definition.StandaloneContent;
        switch (definition.StandaloneContent)
        {
            case BattleMapTileContent.Curio:
            case BattleMapTileContent.Treasure:
                staticTile["cur"] = definition.PropHash;
                dynamicTile["curio_prop"] = definition.PropHash;
                break;
            case BattleMapTileContent.Trap:
                dynamicTile["trap"] = definition.PropHash;
                break;
            case BattleMapTileContent.Obstacle:
                staticTile["obstacle"] = definition.PropHash;
                break;
            default:
                throw new InvalidOperationException(EditorText.Get("BattleMapSaveEditor_Content_008"));
        }

        return new BattleMapEditPreview(
            BattleMapEditKind.PlaceContent, area.AreaId, tile.TileId, area.Kind,
            area.AreaHash, tile.RawContent, null, null);
    }
}
