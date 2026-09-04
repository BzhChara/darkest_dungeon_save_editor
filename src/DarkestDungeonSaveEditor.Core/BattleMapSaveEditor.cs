using System.Text.Json.Nodes;

namespace DarkestDungeonSaveEditor.Core;

internal static class BattleMapSaveEditor
{
    private const int NoAreaHash = 1701736302;

    internal static BattleMapEditPreview DeleteContent(
        JsonObject mapDocument,
        JsonObject raidDocument,
        BattleMapSnapshot snapshot,
        string areaId,
        string tileId)
    {
        ArgumentNullException.ThrowIfNull(mapDocument);
        ArgumentNullException.ThrowIfNull(raidDocument);
        ArgumentNullException.ThrowIfNull(snapshot);
        var (area, tile) = ResolveEditableTile(snapshot, areaId, tileId);
        ValidateStationaryRaidState(raidDocument);
        if (tile.Content == BattleMapTileContent.Hunger)
        {
            throw new InvalidOperationException("饥饿节点不属于地图编辑范围，程序不会修改它。");
        }
        if (tile.RawContent == 0 && !tile.HasResidualContentBinding)
        {
            throw new InvalidOperationException(
                $"地图格 {area.AreaId}.{tile.TileId} 已经没有可删除的事件或残留资源。");
        }

        var dynamicTile = ResolveDynamicTile(mapDocument, area.AreaId, tile.TileId);
        var persistedContent = ReadRequiredInt(dynamicTile, "content");
        if (persistedContent != tile.RawContent)
        {
            throw new InvalidOperationException(
                $"地图格 {area.AreaId}.{tile.TileId} 在显示后已经变化，请在刷新后的地图上重新操作。");
        }
        var staticTile = ResolveStaticTile(mapDocument, area.AreaId, tile.TileId);

        // Delete means a visually empty tile rather than the game's natural "consumed" state.
        // Preserve exploration/topology while removing every mutually exclusive event binding.
        SetScalarToZero(staticTile, "cur");
        SetScalarToZero(staticTile, "obstacle");
        SetScalarToZero(dynamicTile, "curio_prop");
        SetScalarToZero(dynamicTile, "trap");
        dynamicTile["content"] = 0;
        dynamicTile["mash_index"] = -1;
        dynamicTile["mash_type"] = 7;

        return new BattleMapEditPreview(
            BattleMapEditKind.DeleteContent,
            area.AreaId,
            tile.TileId,
            area.Kind,
            area.AreaHash,
            tile.RawContent,
            null,
            null);
    }

    internal static BattleMapEditPreview MoveParty(
        JsonObject mapDocument,
        JsonObject raidDocument,
        BattleMapSnapshot snapshot,
        string areaId,
        string tileId)
    {
        ArgumentNullException.ThrowIfNull(mapDocument);
        ArgumentNullException.ThrowIfNull(raidDocument);
        ArgumentNullException.ThrowIfNull(snapshot);
        var (area, tile) = ResolveEditableTile(snapshot, areaId, tileId);
        ValidateStationaryRaidState(raidDocument);
        if (string.Equals(snapshot.PartyAreaId, area.AreaId, StringComparison.OrdinalIgnoreCase) &&
            snapshot.PartyTileIndex == tile.TileIndex)
        {
            throw new InvalidOperationException("队伍已经位于所选地图格。");
        }

        var savedAreaTile = 1;
        var previousRoomHash = area.AreaHash;
        if (area.Kind == BattleMapAreaKind.Corridor)
        {
            var physicalOrdinal = Array.FindIndex(
                area.Tiles.ToArray(),
                candidate => candidate.TileId.Equals(tile.TileId, StringComparison.OrdinalIgnoreCase));
            if (physicalOrdinal < 0)
            {
                throw new InvalidDataException(
                    $"地图格 {area.AreaId}.{tile.TileId} 不在该走廊的格子顺序中。");
            }

            savedAreaTile = area.Reversed
                ? area.Tiles.Count - 1 - physicalOrdinal
                : physicalOrdinal;
            if (savedAreaTile <= 0 || savedAreaTile >= area.Tiles.Count - 1)
            {
                throw new InvalidOperationException(
                    "房门内部过渡节点不能作为队伍移动目标。");
            }

            var entryTile = area.Reversed ? area.Tiles[^1] : area.Tiles[0];
            previousRoomHash = ResolveDoorDestination(
                mapDocument,
                snapshot,
                area.AreaId,
                entryTile.TileId);
        }

        var raidRoot = JsonSupport.RequireObject(raidDocument, "base_root");
        var party = JsonSupport.RequireObject(raidRoot, "party");
        var doorway = JsonSupport.RequireObject(raidRoot, "in_doorway");
        raidRoot["in_area"] = area.AreaHash;
        raidRoot["areatile"] = savedAreaTile;
        raidRoot["last_room_id"] = previousRoomHash;
        raidRoot["teleported"] = false;
        doorway["area_to"] = NoAreaHash;
        doorway["tile_to"] = 0;
        doorway["implied"] = true;
        party["IsMovingLeft()"] = false;
        party["retreat_room"] = area.AreaHash;

        return new BattleMapEditPreview(
            BattleMapEditKind.MoveParty,
            area.AreaId,
            tile.TileId,
            area.Kind,
            area.AreaHash,
            tile.RawContent,
            savedAreaTile,
            previousRoomHash);
    }

    internal static BattleMapEditPreview PlaceBattle(
        JsonObject mapDocument,
        JsonObject raidDocument,
        BattleMapSnapshot snapshot,
        string areaId,
        string tileId,
        BattleEncounterDefinition encounter)
    {
        ArgumentNullException.ThrowIfNull(mapDocument);
        ArgumentNullException.ThrowIfNull(raidDocument);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(encounter);
        var (area, tile) = ResolveEditableTile(snapshot, areaId, tileId);
        ValidateStationaryRaidState(raidDocument);
        if (!encounter.CanPlaceDirectly || encounter.MashIndex is null)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(encounter.UnavailableReason)
                    ? "所选遭遇没有经过可写入索引验证。"
                    : encounter.UnavailableReason);
        }
        if (!encounter.TableGuard.DungeonId.Equals(
                snapshot.DungeonId,
                StringComparison.OrdinalIgnoreCase) ||
            encounter.TableGuard.Difficulty != snapshot.Difficulty)
        {
            throw new InvalidOperationException("所选遭遇不属于当前副本及难度的有效遭遇表。");
        }
        if (encounter.MashType is < 0 or > 2 || encounter.MashIndex is < 0)
        {
            throw new InvalidOperationException("所选遭遇的 mash 类型或索引无效。");
        }
        if (area.Kind != encounter.TargetAreaKind ||
            (area.Kind == BattleMapAreaKind.Corridor && encounter.MashType != 0) ||
            (area.Kind == BattleMapAreaKind.Room && encounter.MashType is not (1 or 2)))
        {
            throw new InvalidOperationException(
                encounter.MashType == 0
                    ? "走廊遭遇只能写入普通可见走廊格。"
                    : "房间或首领遭遇只能写入房间。");
        }
        if (string.Equals(area.AreaId, snapshot.EntranceAreaId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("出生房间不能新建、替换或删除地图内容。");
        }
        if (string.Equals(area.AreaId, snapshot.FinalRoomId, StringComparison.OrdinalIgnoreCase) &&
            encounter.MashType != 2)
        {
            throw new InvalidOperationException("最终房间目前只允许写入已验证的首领遭遇。");
        }
        if (tile.Content is
            BattleMapTileContent.Hunger or
            BattleMapTileContent.SecretDoor or
            BattleMapTileContent.Ambush or
            BattleMapTileContent.Happening or
            BattleMapTileContent.AmbushCurio or
            BattleMapTileContent.AmbushTreasure or
            BattleMapTileContent.Unknown)
        {
            throw new InvalidOperationException("该格属于系统或脚本内容，不能作为普通遭遇替换目标。");
        }

        var dynamicTile = ResolveDynamicTile(mapDocument, area.AreaId, tile.TileId);
        var persistedContent = ReadRequiredInt(dynamicTile, "content");
        if (persistedContent != tile.RawContent)
        {
            throw new InvalidOperationException(
                $"地图格 {area.AreaId}.{tile.TileId} 在显示后已经变化，请在刷新后的地图上重新操作。");
        }
        var staticTile = ResolveStaticTile(mapDocument, area.AreaId, tile.TileId);

        // A tile stores one active content binding. Replacing it with a battle must clear
        // every prop/trap/obstacle scalar so a naturally consumed visual cannot overlap the
        // injected encounter.
        SetScalarToZero(staticTile, "cur");
        SetScalarToZero(staticTile, "obstacle");
        SetScalarToZero(dynamicTile, "curio_prop");
        SetScalarToZero(dynamicTile, "trap");
        dynamicTile["content"] = (int)BattleMapTileContent.Battle;
        dynamicTile["mash_index"] = encounter.MashIndex.Value;
        dynamicTile["mash_type"] = encounter.MashType;

        return new BattleMapEditPreview(
            BattleMapEditKind.PlaceBattle,
            area.AreaId,
            tile.TileId,
            area.Kind,
            area.AreaHash,
            tile.RawContent,
            null,
            null);
    }

    internal static BattleMapEditPreview SetBattleAttachment(
        JsonObject mapDocument,
        JsonObject raidDocument,
        BattleMapSnapshot snapshot,
        string areaId,
        string tileId,
        BattleRoomAttachmentDefinition attachment)
    {
        ArgumentNullException.ThrowIfNull(mapDocument);
        ArgumentNullException.ThrowIfNull(raidDocument);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(attachment);
        var (area, tile, staticTile, dynamicTile) = ResolveRoomBattleAttachmentTarget(
            mapDocument,
            raidDocument,
            snapshot,
            areaId,
            tileId,
            requireExistingAttachment: false);
        if (attachment.PropHash == 0 ||
            attachment.PropHash != BattleRoomAttachmentCatalog.ComputePropHash(attachment.Id))
        {
            throw new InvalidOperationException("所选奇物或宝箱的存档哈希无效。");
        }

        SetScalarToZero(staticTile, "obstacle");
        SetScalarToZero(dynamicTile, "trap");
        staticTile["cur"] = attachment.PropHash;
        dynamicTile["curio_prop"] = attachment.PropHash;
        dynamicTile["content"] = attachment.Kind switch
        {
            BattleRoomAttachmentKind.Curio => (int)BattleMapTileContent.GuardedCurio,
            BattleRoomAttachmentKind.Treasure => (int)BattleMapTileContent.GuardedTreasure,
            _ => throw new ArgumentOutOfRangeException(
                nameof(attachment),
                attachment.Kind,
                "未知的房间战斗附加内容类型。")
        };

        return new BattleMapEditPreview(
            BattleMapEditKind.SetBattleAttachment,
            area.AreaId,
            tile.TileId,
            area.Kind,
            area.AreaHash,
            tile.RawContent,
            null,
            null);
    }

    internal static BattleMapEditPreview RemoveBattleAttachment(
        JsonObject mapDocument,
        JsonObject raidDocument,
        BattleMapSnapshot snapshot,
        string areaId,
        string tileId)
    {
        ArgumentNullException.ThrowIfNull(mapDocument);
        ArgumentNullException.ThrowIfNull(raidDocument);
        ArgumentNullException.ThrowIfNull(snapshot);
        var (area, tile, staticTile, dynamicTile) = ResolveRoomBattleAttachmentTarget(
            mapDocument,
            raidDocument,
            snapshot,
            areaId,
            tileId,
            requireExistingAttachment: true);

        SetScalarToZero(staticTile, "cur");
        SetScalarToZero(staticTile, "obstacle");
        SetScalarToZero(dynamicTile, "curio_prop");
        SetScalarToZero(dynamicTile, "trap");
        dynamicTile["content"] = (int)BattleMapTileContent.Battle;

        return new BattleMapEditPreview(
            BattleMapEditKind.RemoveBattleAttachment,
            area.AreaId,
            tile.TileId,
            area.Kind,
            area.AreaHash,
            tile.RawContent,
            null,
            null);
    }

    private static (
        BattleMapAreaSnapshot Area,
        BattleMapTileSnapshot Tile,
        JsonObject StaticTile,
        JsonObject DynamicTile) ResolveRoomBattleAttachmentTarget(
        JsonObject mapDocument,
        JsonObject raidDocument,
        BattleMapSnapshot snapshot,
        string areaId,
        string tileId,
        bool requireExistingAttachment)
    {
        var (area, tile) = ResolveEditableTile(snapshot, areaId, tileId);
        ValidateStationaryRaidState(raidDocument);
        if (area.Kind != BattleMapAreaKind.Room)
        {
            throw new InvalidOperationException("战斗附加内容只能用于普通房间战斗。");
        }
        if (string.Equals(area.AreaId, snapshot.EntranceAreaId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("出生房间不能修改战斗附加内容。");
        }
        if (string.Equals(area.AreaId, snapshot.FinalRoomId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("最终房间不能修改战斗附加内容。");
        }
        if (tile.MashType != 1 || tile.MashIndex < 0)
        {
            throw new InvalidOperationException("该房间不是带有有效普通房间索引的战斗。");
        }
        if (tile.RawContent is not (
                (int)BattleMapTileContent.Battle or
                (int)BattleMapTileContent.GuardedCurio or
                (int)BattleMapTileContent.GuardedTreasure))
        {
            throw new InvalidOperationException(
                "只有普通战斗、有奇物的战斗或有宝箱的战斗可以修改附加内容。");
        }
        if (requireExistingAttachment &&
            tile.RawContent is not (
                (int)BattleMapTileContent.GuardedCurio or
                (int)BattleMapTileContent.GuardedTreasure))
        {
            throw new InvalidOperationException("该普通房间战斗当前没有可移除的附加内容。");
        }

        var dynamicTile = ResolveDynamicTile(mapDocument, area.AreaId, tile.TileId);
        if (ReadRequiredInt(dynamicTile, "content") != tile.RawContent ||
            ReadRequiredInt(dynamicTile, "mash_type") != tile.MashType ||
            ReadRequiredInt(dynamicTile, "mash_index") != tile.MashIndex)
        {
            throw new InvalidOperationException(
                $"地图格 {area.AreaId}.{tile.TileId} 的战斗绑定在显示后已经变化，请刷新后重试。");
        }

        var staticTile = ResolveStaticTile(mapDocument, area.AreaId, tile.TileId);
        if (tile.RawContent is
                (int)BattleMapTileContent.GuardedCurio or
                (int)BattleMapTileContent.GuardedTreasure)
        {
            var staticCurioHash = JsonSupport.ReadInt(staticTile, "cur") ?? 0;
            if (tile.CurioPropHash == 0 || staticCurioHash != tile.CurioPropHash)
            {
                throw new InvalidDataException(
                    "当前守卫奇物/宝箱的静态与动态资源绑定不一致，程序不会猜测修复。");
            }
        }

        return (area, tile, staticTile, dynamicTile);
    }

    private static (BattleMapAreaSnapshot Area, BattleMapTileSnapshot Tile) ResolveEditableTile(
        BattleMapSnapshot snapshot,
        string areaId,
        string tileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(areaId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tileId);
        var area = snapshot.Areas.SingleOrDefault(candidate =>
            candidate.AreaId.Equals(areaId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"地图区域“{areaId}”已经不存在。");
        if (area.Kind is not (BattleMapAreaKind.Room or BattleMapAreaKind.Corridor))
        {
            throw new InvalidOperationException($"地图区域“{area.AreaId}”不可编辑。");
        }

        var tile = area.Tiles.SingleOrDefault(candidate =>
            candidate.TileId.Equals(tileId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"地图格“{area.AreaId}.{tileId}”已经不存在。");
        if (area.Kind == BattleMapAreaKind.Corridor && tile.StaticType != 1)
        {
            throw new InvalidOperationException(
                "只有普通可见走廊格可以编辑或作为队伍移动目标。");
        }

        return (area, tile);
    }

    private static JsonObject ResolveDynamicTile(JsonObject mapDocument, string areaId, string tileId)
    {
        var dynamicAreas = JsonSupport.RequireObject(
            mapDocument,
            "base_root",
            "map",
            "static_dynamic",
            "areas");
        var dynamicArea = dynamicAreas[areaId] as JsonObject
            ?? throw new InvalidDataException($"存档缺少动态地图区域“{areaId}”。");
        var dynamicTiles = dynamicArea["tiles"] as JsonObject
            ?? throw new InvalidDataException($"动态地图区域“{areaId}”没有格子数据。");
        return dynamicTiles[tileId] as JsonObject
            ?? throw new InvalidDataException($"存档缺少动态地图格“{areaId}.{tileId}”。");
    }

    private static JsonObject ResolveStaticTile(JsonObject mapDocument, string areaId, string tileId)
    {
        var staticAreas = JsonSupport.RequireObject(
            mapDocument,
            "base_root",
            "map",
            "static_dynamic",
            "static_save",
            "base_root",
            "areas");
        var staticArea = staticAreas[areaId] as JsonObject
            ?? throw new InvalidDataException($"存档缺少静态地图区域“{areaId}”。");
        var staticTiles = staticArea["tiles"] as JsonObject
            ?? throw new InvalidDataException($"静态地图区域“{areaId}”没有格子数据。");
        return staticTiles[tileId] as JsonObject
            ?? throw new InvalidDataException($"存档缺少静态地图格“{areaId}.{tileId}”。");
    }

    private static void SetScalarToZero(JsonObject value, string name) => value[name] = 0;

    private static int ResolveDoorDestination(
        JsonObject mapDocument,
        BattleMapSnapshot snapshot,
        string areaId,
        string tileId)
    {
        var staticAreas = JsonSupport.RequireObject(
            mapDocument,
            "base_root",
            "map",
            "static_dynamic",
            "static_save",
            "base_root",
            "areas");
        var staticArea = staticAreas[areaId] as JsonObject
            ?? throw new InvalidDataException($"存档缺少静态地图区域“{areaId}”。");
        var staticTiles = staticArea["tiles"] as JsonObject
            ?? throw new InvalidDataException($"静态地图区域“{areaId}”没有格子数据。");
        var staticTile = staticTiles[tileId] as JsonObject
            ?? throw new InvalidDataException($"存档缺少静态地图格“{areaId}.{tileId}”。");
        var door = staticTile["door_to"] as JsonObject
            ?? throw new InvalidDataException(
                $"走廊端点“{areaId}.{tileId}”没有对应房间。");
        var roomHash = ReadRequiredInt(door, "area_to");
        if (roomHash == NoAreaHash ||
            snapshot.Areas.All(candidate =>
                candidate.Kind != BattleMapAreaKind.Room || candidate.AreaHash != roomHash))
        {
            throw new InvalidDataException(
                $"走廊端点“{areaId}.{tileId}”无法对应到当前地图中的房间。");
        }

        return roomHash;
    }

    private static void ValidateStationaryRaidState(JsonObject raidDocument)
    {
        var root = JsonSupport.RequireObject(raidDocument, "base_root");
        if (ReadRequiredBool(root, "inbattle"))
        {
            throw new InvalidOperationException("当前正在战斗，不能修改地图。请先结束战斗并让游戏完成保存。");
        }
        if (HasPayload(root["battle"]) || HasPayload(root["wave_logic"]))
        {
            throw new InvalidOperationException(
                "存档仍包含战斗或脚本波次状态，不能修改地图。请先让游戏结束当前流程并完成保存。");
        }
        if (ReadRequiredBool(root, "teleported"))
        {
            throw new InvalidOperationException(
                "队伍仍处于传送过渡状态，不能修改地图。请先让游戏完成过渡并保存。");
        }

        var camp = JsonSupport.RequireObject(root, "camp");
        if (ReadRequiredInt(camp, "phase") != 0)
        {
            throw new InvalidOperationException("当前正在扎营，不能修改地图。请先结束扎营并让游戏完成保存。");
        }

        var loot = JsonSupport.RequireObject(root, "loot");
        var queue = JsonSupport.RequireObject(loot, "queue");
        var queueItems = JsonSupport.RequireObject(loot, "queue_items", "items");
        var ownedItems = JsonSupport.RequireObject(loot, "owned_items", "items");
        if (queue.Count > 0 || queueItems.Count > 0 || ownedItems.Count > 0)
        {
            throw new InvalidOperationException(
                "当前仍有战利品或奖励等待领取，不能修改地图。请先处理奖励并让游戏完成保存。");
        }

        var doorway = JsonSupport.RequireObject(root, "in_doorway");
        if (ReadRequiredInt(doorway, "area_to") != NoAreaHash ||
            ReadRequiredInt(doorway, "tile_to") != 0 ||
            !ReadRequiredBool(doorway, "implied"))
        {
            throw new InvalidOperationException(
                "队伍仍处于房门过渡状态，不能修改地图。请先让游戏完成进出房间并保存。");
        }

        var party = JsonSupport.RequireObject(root, "party");
        _ = ReadRequiredBool(party, "IsMovingLeft()");
        _ = ReadRequiredInt(party, "retreat_room");
    }

    private static bool HasPayload(JsonNode? node) => node switch
    {
        null => false,
        JsonObject value => value.Count > 0,
        JsonArray value => value.Count > 0,
        _ => true
    };

    private static int ReadRequiredInt(JsonObject value, string name) =>
        JsonSupport.ReadInt(value, name)
        ?? throw new InvalidDataException($"存档缺少必需的整数字段：{name}");

    private static bool ReadRequiredBool(JsonObject value, string name)
    {
        if (value[name] is JsonValue node && node.TryGetValue<bool>(out var result))
        {
            return result;
        }

        throw new InvalidDataException($"存档缺少必需的布尔字段：{name}");
    }
}
