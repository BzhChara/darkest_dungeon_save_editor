using System.Text.Json.Nodes;

namespace DarkestDungeonSaveEditor.Core;

internal static partial class BattleMapSaveEditor
{
    private const int NoAreaHash = 1701736302;

    internal static bool ClearRecordedBattle(JsonObject map, EditorBattlePlacement placement)
    {
        var tile = ResolveDynamicTile(map, placement.AreaId, placement.TileId);
        var content = (BattleMapTileContent)ReadRequiredInt(tile, "content");
        if (!EditorBattleHistory.IsBattle(content) || ReadRequiredInt(tile, "mash_type") != placement.MashType ||
            ReadRequiredInt(tile, "mash_index") != placement.MashIndex) return false;
        tile["content"] = content switch
        {
            BattleMapTileContent.GuardedCurio or BattleMapTileContent.AmbushCurio => 7,
            BattleMapTileContent.GuardedTreasure or BattleMapTileContent.AmbushTreasure => 9,
            _ => 0
        };
        tile["mash_index"] = -1;
        tile["mash_type"] = 7;
        return true;
    }

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
            throw new InvalidOperationException(EditorText.Get("BattleMapSaveEditor_001"));
        }
        if (tile.RawContent == 0 && !tile.HasResidualContentBinding)
        {
            throw new InvalidOperationException(
                EditorText.Format("BattleMapSaveEditor_002", area.AreaId, tile.TileId));
        }

        var dynamicTile = ResolveDynamicTile(mapDocument, area.AreaId, tile.TileId);
        var persistedContent = ReadRequiredInt(dynamicTile, "content");
        if (persistedContent != tile.RawContent)
        {
            throw new InvalidOperationException(
                EditorText.Format("BattleMapSaveEditor_003", area.AreaId, tile.TileId));
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
        if (string.Equals(snapshot.PartyAreaId, area.AreaId, StringComparison.Ordinal) &&
            snapshot.PartyTileIndex == tile.TileIndex)
        {
            throw new InvalidOperationException(EditorText.Get("BattleMapSaveEditor_004"));
        }

        var savedAreaTile = 1;
        var previousRoomHash = area.AreaHash;
        if (area.Kind == BattleMapAreaKind.Corridor)
        {
            var physicalOrdinal = Array.FindIndex(
                area.Tiles.ToArray(),
                candidate => candidate.TileId.Equals(tile.TileId, StringComparison.Ordinal));
            if (physicalOrdinal < 0)
            {
                throw new InvalidDataException(
                    EditorText.Format("BattleMapSaveEditor_005", area.AreaId, tile.TileId));
            }

            savedAreaTile = area.Reversed
                ? area.Tiles.Count - 1 - physicalOrdinal
                : physicalOrdinal;
            if (savedAreaTile <= 0 || savedAreaTile >= area.Tiles.Count - 1)
            {
                throw new InvalidOperationException(
                    EditorText.Get("BattleMapSaveEditor_006"));
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
                    ? EditorText.Get("BattleMapSaveEditor_007")
                    : encounter.UnavailableReason);
        }
        if (!encounter.TableGuard.DungeonId.Equals(
                snapshot.DungeonId,
                StringComparison.Ordinal) ||
            encounter.TableGuard.Difficulty != snapshot.Difficulty)
        {
            throw new InvalidOperationException(EditorText.Get("BattleMapSaveEditor_008"));
        }
        if (encounter.MashType is < 0 or > 2 || encounter.MashIndex is < 0)
        {
            throw new InvalidOperationException(EditorText.Get("BattleMapSaveEditor_009"));
        }
        ValidateBattlePlacementTarget(mapDocument, snapshot, areaId, tileId, encounter.MashType);

        var dynamicTile = ResolveDynamicTile(mapDocument, area.AreaId, tile.TileId);
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

    internal static void ValidateBattlePlacementTarget(
        JsonObject mapDocument, BattleMapSnapshot snapshot, string areaId, string tileId, int mashType)
    {
        var (area, tile) = ResolveEditableTile(snapshot, areaId, tileId);
        if (mashType is < 0 or > 2 ||
            (area.Kind == BattleMapAreaKind.Corridor && mashType != 0) ||
            (area.Kind == BattleMapAreaKind.Room && mashType is not (1 or 2)))
        {
            throw new InvalidOperationException(
                mashType == 0
                    ? EditorText.Get("BattleMapSaveEditor_010")
                    : EditorText.Get("BattleMapSaveEditor_011"));
        }
        if (string.Equals(area.AreaId, snapshot.EntranceAreaId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(EditorText.Get("BattleMapSaveEditor_012"));
        }
        if (string.Equals(area.AreaId, snapshot.FinalRoomId, StringComparison.Ordinal) &&
            mashType != 2)
        {
            throw new InvalidOperationException(EditorText.Get("BattleMapSaveEditor_013"));
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
            throw new InvalidOperationException(EditorText.Get("BattleMapSaveEditor_014"));
        }
        // Display snapshots tolerate a missing content field as empty; writes require
        // the actual persisted integer before a Bridge can be installed or activated.
        var dynamicTile = ResolveDynamicTile(mapDocument, area.AreaId, tile.TileId);
        if (ReadRequiredInt(dynamicTile, "content") != tile.RawContent)
        {
            throw new InvalidOperationException(
                EditorText.Format("BattleMapSaveEditor_003", area.AreaId, tile.TileId));
        }
        _ = ResolveStaticTile(mapDocument, area.AreaId, tile.TileId);
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
            throw new InvalidOperationException(EditorText.Get("BattleMapSaveEditor_015"));
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
                EditorText.Get("BattleMapSaveEditor_016"))
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
            throw new InvalidOperationException(EditorText.Get("BattleMapSaveEditor_017"));
        }
        if (string.Equals(area.AreaId, snapshot.EntranceAreaId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(EditorText.Get("BattleMapSaveEditor_018"));
        }
        if (string.Equals(area.AreaId, snapshot.FinalRoomId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(EditorText.Get("BattleMapSaveEditor_019"));
        }
        if (tile.MashType != 1 || tile.MashIndex < 0)
        {
            throw new InvalidOperationException(EditorText.Get("BattleMapSaveEditor_020"));
        }
        if (tile.RawContent is not (
                (int)BattleMapTileContent.Battle or
                (int)BattleMapTileContent.GuardedCurio or
                (int)BattleMapTileContent.GuardedTreasure))
        {
            throw new InvalidOperationException(
                EditorText.Get("BattleMapSaveEditor_021"));
        }
        if (requireExistingAttachment &&
            tile.RawContent is not (
                (int)BattleMapTileContent.GuardedCurio or
                (int)BattleMapTileContent.GuardedTreasure))
        {
            throw new InvalidOperationException(EditorText.Get("BattleMapSaveEditor_022"));
        }

        var dynamicTile = ResolveDynamicTile(mapDocument, area.AreaId, tile.TileId);
        if (ReadRequiredInt(dynamicTile, "content") != tile.RawContent ||
            ReadRequiredInt(dynamicTile, "mash_type") != tile.MashType ||
            ReadRequiredInt(dynamicTile, "mash_index") != tile.MashIndex)
        {
            throw new InvalidOperationException(
                EditorText.Format("BattleMapSaveEditor_023", area.AreaId, tile.TileId));
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
                    EditorText.Get("BattleMapSaveEditor_024"));
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
            candidate.AreaId.Equals(areaId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(EditorText.Format("BattleMapSaveEditor_025", areaId));
        if (area.Kind is not (BattleMapAreaKind.Room or BattleMapAreaKind.Corridor))
        {
            throw new InvalidOperationException(EditorText.Format("BattleMapSaveEditor_026", area.AreaId));
        }

        var tile = area.Tiles.SingleOrDefault(candidate =>
            candidate.TileId.Equals(tileId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                EditorText.Format("BattleMapSaveEditor_027", area.AreaId, tileId));
        if (area.Kind == BattleMapAreaKind.Corridor && tile.StaticType != 1)
        {
            throw new InvalidOperationException(
                EditorText.Get("BattleMapSaveEditor_028"));
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
            ?? throw new InvalidDataException(EditorText.Format("BattleMapSaveEditor_029", areaId));
        var dynamicTiles = dynamicArea["tiles"] as JsonObject
            ?? throw new InvalidDataException(EditorText.Format("BattleMapSaveEditor_030", areaId));
        return dynamicTiles[tileId] as JsonObject
            ?? throw new InvalidDataException(EditorText.Format("BattleMapSaveEditor_031", areaId, tileId));
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
            ?? throw new InvalidDataException(EditorText.Format("BattleMapSaveEditor_032", areaId));
        var staticTiles = staticArea["tiles"] as JsonObject
            ?? throw new InvalidDataException(EditorText.Format("BattleMapSaveEditor_033", areaId));
        return staticTiles[tileId] as JsonObject
            ?? throw new InvalidDataException(EditorText.Format("BattleMapSaveEditor_034", areaId, tileId));
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
            ?? throw new InvalidDataException(EditorText.Format("BattleMapSaveEditor_032", areaId));
        var staticTiles = staticArea["tiles"] as JsonObject
            ?? throw new InvalidDataException(EditorText.Format("BattleMapSaveEditor_033", areaId));
        var staticTile = staticTiles[tileId] as JsonObject
            ?? throw new InvalidDataException(EditorText.Format("BattleMapSaveEditor_034", areaId, tileId));
        var door = staticTile["door_to"] as JsonObject
            ?? throw new InvalidDataException(
                EditorText.Format("BattleMapSaveEditor_035", areaId, tileId));
        var roomHash = ReadRequiredInt(door, "area_to");
        if (roomHash == NoAreaHash ||
            snapshot.Areas.All(candidate =>
                candidate.Kind != BattleMapAreaKind.Room || candidate.AreaHash != roomHash))
        {
            throw new InvalidDataException(
                EditorText.Format("BattleMapSaveEditor_036", areaId, tileId));
        }

        return roomHash;
    }

    internal static void ValidateStationaryRaidState(JsonObject raidDocument)
    {
        var root = JsonSupport.RequireObject(raidDocument, "base_root");
        if (ReadRequiredBool(root, "inbattle"))
        {
            throw new InvalidOperationException(EditorText.Get("BattleMapSaveEditor_037"));
        }
        if (HasPayload(root["battle"]) || HasPayload(root["wave_logic"]))
        {
            throw new InvalidOperationException(
                EditorText.Get("BattleMapSaveEditor_038"));
        }
        if (ReadRequiredBool(root, "teleported"))
        {
            throw new InvalidOperationException(
                EditorText.Get("BattleMapSaveEditor_039"));
        }

        var camp = JsonSupport.RequireObject(root, "camp");
        if (ReadRequiredInt(camp, "phase") != 0)
        {
            throw new InvalidOperationException(EditorText.Get("BattleMapSaveEditor_040"));
        }

        var loot = JsonSupport.RequireObject(root, "loot");
        var queue = JsonSupport.RequireObject(loot, "queue");
        var queueItems = JsonSupport.RequireObject(loot, "queue_items", "items");
        var ownedItems = JsonSupport.RequireObject(loot, "owned_items", "items");
        if (queue.Count > 0 || queueItems.Count > 0 || ownedItems.Count > 0)
        {
            throw new InvalidOperationException(
                EditorText.Get("BattleMapSaveEditor_041"));
        }

        var doorway = JsonSupport.RequireObject(root, "in_doorway");
        if (ReadRequiredInt(doorway, "area_to") != NoAreaHash ||
            ReadRequiredInt(doorway, "tile_to") != 0 ||
            !ReadRequiredBool(doorway, "implied"))
        {
            throw new InvalidOperationException(
                EditorText.Get("BattleMapSaveEditor_042"));
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
        ?? throw new InvalidDataException(EditorText.Format("BattleMapSaveEditor_043", name));

    private static bool ReadRequiredBool(JsonObject value, string name)
    {
        if (value[name] is JsonValue node && node.TryGetValue<bool>(out var result))
        {
            return result;
        }

        throw new InvalidDataException(EditorText.Format("BattleMapSaveEditor_044", name));
    }
}
