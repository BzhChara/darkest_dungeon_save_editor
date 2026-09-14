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
            throw new InvalidOperationException("所选地图内容与目标位置不符；房间和走廊必须使用各自的候选目录。");
        }
        if (!definition.IsAvailableInDungeon(snapshot.DungeonId))
        {
            throw new InvalidOperationException("陷阱和障碍只能使用当前副本区域的资源，请重新选择。");
        }
        if (string.Equals(area.AreaId, snapshot.EntranceAreaId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("出生房间不能新建或替换地图内容。");
        }
        if (string.Equals(area.AreaId, snapshot.FinalRoomId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("最终房间不能替换为普通奇物、宝箱、陷阱或障碍。");
        }
        if (tile.Content is BattleMapTileContent.Hunger or BattleMapTileContent.SecretDoor or
            BattleMapTileContent.Ambush or BattleMapTileContent.Happening or
            BattleMapTileContent.AmbushCurio or BattleMapTileContent.AmbushTreasure or
            BattleMapTileContent.Unknown)
        {
            throw new InvalidOperationException("该格属于系统或脚本内容，不能新建或替换普通地图内容。");
        }
        if (definition.PropHash == 0 ||
            definition.PropHash != BattleRoomAttachmentCatalog.ComputePropHash(definition.Id))
        {
            throw new InvalidOperationException("所选地图内容的存档哈希无效。");
        }

        var staticTile = ResolveStaticTile(mapDocument, area.AreaId, tile.TileId);
        var dynamicTile = ResolveDynamicTile(mapDocument, area.AreaId, tile.TileId);
        if (ReadRequiredInt(dynamicTile, "content") != tile.RawContent)
        {
            throw new InvalidOperationException("目标地图格在显示后已经变化，请刷新后重试。");
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
                throw new InvalidOperationException("未知的地图内容类型。");
        }

        return new BattleMapEditPreview(
            BattleMapEditKind.PlaceContent, area.AreaId, tile.TileId, area.Kind,
            area.AreaHash, tile.RawContent, null, null);
    }
}
