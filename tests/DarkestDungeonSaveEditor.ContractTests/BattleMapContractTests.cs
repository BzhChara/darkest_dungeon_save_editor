internal static partial class ContractSuite
{
    private static async Task RunBattleMapContractsAsync(string runRoot, DsonSaveCodec codec)
    {
        var battleMapProfileRoot = Path.Combine(runRoot, "battle-map-profile");
        Directory.CreateDirectory(battleMapProfileRoot);
        var battleMapFixturePath = Path.Combine(battleMapProfileRoot, "persist.map.json");
        var battleRaidFixturePath = Path.Combine(battleMapProfileRoot, "persist.raid.json");
        File.WriteAllText(
            battleMapFixturePath,
            """
    {
      "base_root": {
        "map": {
          "bounds": [-1.0, 5.0, 0.0, 0.0],
          "entrance_id": 100,
          "final_room_id": 200,
          "static_dynamic": {
            "static_save": {
              "base_root": {
                "areas": {
                  "rooA": {
                    "id": 100,
                    "kind": 0,
                    "tiles": {
                      "tile0": { "type": 3, "obstacle": 0, "mappos": [-1.0, 0.0] }
                    }
                  },
                  "coAB": {
                    "id": 150,
                    "kind": 1,
                    "tiles": {
                      "tile0": { "type": 2, "obstacle": 0, "mappos": [0.0, 0.0], "door_to": { "area_to": 100, "tile_to": 0, "implied": false, "type": 0 } },
                      "tile1": { "type": 1, "obstacle": 0, "mappos": [1.0, 0.0] },
                      "tile2": { "type": 1, "obstacle": 0, "mappos": [2.0, 0.0] },
                      "tile3": { "type": 1, "obstacle": 0, "mappos": [3.0, 0.0] },
                      "tile4": { "type": 2, "obstacle": 0, "mappos": [4.0, 0.0], "door_to": { "area_to": 200, "tile_to": 0, "implied": false, "type": 0 } }
                    }
                  },
                  "rooB": {
                    "id": 200,
                    "kind": 0,
                    "tiles": {
                      "tile0": { "type": 3, "obstacle": 0, "mappos": [5.0, 0.0] }
                    }
                  }
                }
              }
            },
            "areas": {
              "rooA": {
                "knowledge": 3,
                "reversed": false,
                "tiles": {
                  "tile0": { "content": 0, "knowledge": 3, "mash_index": -1, "mash_type": 7 }
                }
              },
              "coAB": {
                "knowledge": 2,
                "reversed": true,
                "tiles": {
                  "tile0": { "content": 0, "knowledge": 2, "mash_index": -1, "mash_type": 7 },
                  "tile1": { "content": 3, "knowledge": 2, "trap": 321, "mash_index": -1, "mash_type": 7 },
                  "tile2": { "content": 10, "knowledge": 2, "curio_prop": 987, "mash_index": 12, "mash_type": 1 },
                  "tile3": { "content": 8, "knowledge": 2, "mash_index": -1, "mash_type": 7 },
                  "tile4": { "content": 0, "knowledge": 2, "mash_index": -1, "mash_type": 7 }
                }
              },
              "rooB": {
                "knowledge": 1,
                "reversed": false,
                "tiles": {
                  "tile0": { "content": 1, "knowledge": 1, "mash_index": 12, "mash_type": 1 }
                }
              }
            }
          }
        }
      }
    }
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            battleRaidFixturePath,
            """
    {
      "base_root": {
        "raid_instance": { "dungeon": "cove", "difficulty": 2, "length": 3 },
        "in_area": 150,
        "areatile": 1,
        "last_room_id": 100,
        "teleported": false,
        "in_doorway": { "area_to": 1701736302, "tile_to": 0, "implied": true },
        "camp": { "phase": 0 },
        "inbattle": false,
        "loot": {
          "queue": {},
          "queue_items": { "items": {} },
          "owned_items": { "items": {} }
        },
        "party": {
          "IsMovingLeft()": false,
          "retreat_room": 150,
          "heroes": []
        }
      }
    }
    """,
            new UTF8Encoding(false));
        var battleSnapshot = await new BattleMapSnapshotReader(codec).LoadAsync(battleMapProfileRoot);
        var battleCorridor = battleSnapshot.Areas.Single(area => area.AreaId == "coAB");
        Assert(
            battleSnapshot.DungeonId == "cove" &&
            battleSnapshot.Difficulty == 2 &&
            battleSnapshot.Length == 3 &&
            battleSnapshot.RoomCount == 2 &&
            battleSnapshot.CorridorCount == 1 &&
            battleSnapshot.TileCount == 7 &&
            battleSnapshot.EntranceAreaId == "rooA" &&
            battleSnapshot.FinalRoomId == "rooB" &&
            battleSnapshot.PartyAreaId == "coAB" &&
            battleSnapshot.PartyTileIndex == 3 &&
            battleSnapshot.LastRoomId == "rooA" &&
            battleCorridor.Reversed &&
            battleCorridor.Tiles[1].Content == BattleMapTileContent.Trap &&
            battleCorridor.Tiles[1].TrapHash == 321 &&
            battleCorridor.Tiles[2].Content == BattleMapTileContent.GuardedTreasure &&
            battleCorridor.Tiles[3].Content == BattleMapTileContent.Hunger &&
            battleSnapshot.Issues.Count == 0,
            "The battle-map reader must join static topology to dynamic state, resolve hashed areas, and map direction-relative corridor areatile progress onto the physical static tile.");

        var battleEstateFixturePath = Path.Combine(battleMapProfileRoot, "persist.estate.json");
        File.WriteAllText(battleEstateFixturePath, "{}", new UTF8Encoding(false));
        var battleEditLocations = new SaveEditorLocations(
            Path.Combine(runRoot, "battle-map-appdata"),
            Path.Combine(runRoot, "battle-map-appdata", "workspaces"),
            Path.Combine(runRoot, "battle-map-appdata", "backups"));
        var battleEditProfile = new SaveProfile(
            "profile_battle",
            battleMapProfileRoot,
            battleEstateFixturePath,
            "battle-user",
            File.GetLastWriteTimeUtc(battleEstateFixturePath));
        var battleEditService = new BattleMapEditService(codec, battleEditLocations);
        var mapHashBeforeRejectedHungerDelete = ComputeSha256(battleMapFixturePath);
        var rejectedHungerDelete = false;
        try
        {
            _ = await battleEditService.PrepareDeleteContentAsync(
                battleEditProfile,
                battleSnapshot,
                "coAB",
                "tile3");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("不属于地图编辑范围", StringComparison.Ordinal))
        {
            rejectedHungerDelete = true;
        }
        Assert(
            rejectedHungerDelete &&
            ComputeSha256(battleMapFixturePath).Equals(mapHashBeforeRejectedHungerDelete, StringComparison.OrdinalIgnoreCase),
            "Persisted hunger nodes must remain outside the content-editing contract and byte-identical after a rejected delete.");
        var rejectedDoorTransitionTarget = false;
        try
        {
            _ = await battleEditService.PrepareMovePartyAsync(
                battleEditProfile,
                battleSnapshot,
                "coAB",
                "tile0");
        }
        catch (InvalidOperationException)
        {
            rejectedDoorTransitionTarget = true;
        }
        Assert(
            rejectedDoorTransitionTarget,
            "Internal type=2 door-transition nodes must never become writable party destinations.");
        var raidHashBeforeDelete = ComputeSha256(battleRaidFixturePath);
        var preparedBattleDelete = await battleEditService.PrepareDeleteContentAsync(
            battleEditProfile,
            battleSnapshot,
            "coAB",
            "tile1");
        Assert(
            preparedBattleDelete.Preview.Kind == BattleMapEditKind.DeleteContent &&
            preparedBattleDelete.Preview.PreviousRawContent == 3 &&
            preparedBattleDelete.TargetFile.FileName == "persist.map.json",
            "Battle-map deletion must bind the exact persisted area/tile and prepare only persist.map.json for replacement.");
        var battleDeleteCommit = await battleEditService.CommitAsync(preparedBattleDelete);
        var battleSnapshotAfterDelete = await new BattleMapSnapshotReader(codec).LoadAsync(battleMapProfileRoot);
        var deletedBattleTile = battleSnapshotAfterDelete.Areas
            .Single(area => area.AreaId == "coAB")
            .Tiles.Single(tile => tile.TileId == "tile1");
        var backedUpBattleMap = JsonNode.Parse(File.ReadAllText(
            Path.Combine(battleDeleteCommit.BackupDirectory, "persist.map.json")))!.AsObject();
        var backedUpDeletedTile = backedUpBattleMap["base_root"]!["map"]!["static_dynamic"]!["areas"]!["coAB"]!["tiles"]!["tile1"]!.AsObject();
        Assert(
            deletedBattleTile.RawContent == 0 &&
            deletedBattleTile.Knowledge == BattleMapTileKnowledge.Scouted &&
            deletedBattleTile.TrapHash == 321 &&
            deletedBattleTile.MashIndex == -1 &&
            deletedBattleTile.MashType == 7 &&
            ComputeSha256(battleRaidFixturePath).Equals(raidHashBeforeDelete, StringComparison.OrdinalIgnoreCase) &&
            backedUpDeletedTile["content"]!.GetValue<int>() == 3 &&
            File.Exists(Path.Combine(battleDeleteCommit.BackupDirectory, "persist.raid.json")) &&
            File.Exists(Path.Combine(battleDeleteCommit.BackupDirectory, "persist.estate.json")),
            "Deleting map content must preserve knowledge and curio/trap identity, normalize stale mash fields, leave the raid save byte-identical, and back up the full profile first.");

        var mapHashBeforeMove = ComputeSha256(battleMapFixturePath);
        var preparedCorridorMove = await battleEditService.PrepareMovePartyAsync(
            battleEditProfile,
            battleSnapshotAfterDelete,
            "coAB",
            "tile1");
        Assert(
            preparedCorridorMove.Preview.Kind == BattleMapEditKind.MoveParty &&
            preparedCorridorMove.Preview.SavedAreaTile == 3 &&
            preparedCorridorMove.Preview.PreviousRoomHash == 200 &&
            preparedCorridorMove.TargetFile.FileName == "persist.raid.json",
            "A reversed corridor move must convert the physical tile back into traversal progress and use its saved entry-side room.");
        _ = await battleEditService.CommitAsync(preparedCorridorMove);
        var corridorMoveRaid = JsonNode.Parse(File.ReadAllText(battleRaidFixturePath))!.AsObject()["base_root"]!.AsObject();
        var corridorMoveParty = corridorMoveRaid["party"]!.AsObject();
        var corridorMoveDoorway = corridorMoveRaid["in_doorway"]!.AsObject();
        var battleSnapshotAfterCorridorMove = await new BattleMapSnapshotReader(codec).LoadAsync(battleMapProfileRoot);
        Assert(
            corridorMoveRaid["in_area"]!.GetValue<int>() == 150 &&
            corridorMoveRaid["areatile"]!.GetValue<int>() == 3 &&
            corridorMoveRaid["last_room_id"]!.GetValue<int>() == 200 &&
            corridorMoveParty["retreat_room"]!.GetValue<int>() == 150 &&
            !corridorMoveParty["IsMovingLeft()"]!.GetValue<bool>() &&
            corridorMoveDoorway["area_to"]!.GetValue<int>() == 1701736302 &&
            corridorMoveDoorway["tile_to"]!.GetValue<int>() == 0 &&
            corridorMoveDoorway["implied"]!.GetValue<bool>() &&
            battleSnapshotAfterCorridorMove.PartyAreaId == "coAB" &&
            battleSnapshotAfterCorridorMove.PartyTileIndex == 1 &&
            ComputeSha256(battleMapFixturePath).Equals(mapHashBeforeMove, StringComparison.OrdinalIgnoreCase),
            "Moving within a reversed corridor must produce a stationary, canonical raid location that the reader resolves back to the selected visible tile without changing map exploration data.");

        var preparedRoomMove = await battleEditService.PrepareMovePartyAsync(
            battleEditProfile,
            battleSnapshotAfterCorridorMove,
            "rooB",
            "tile0");
        _ = await battleEditService.CommitAsync(preparedRoomMove);
        var roomMoveRaid = JsonNode.Parse(File.ReadAllText(battleRaidFixturePath))!.AsObject()["base_root"]!.AsObject();
        var roomMoveParty = roomMoveRaid["party"]!.AsObject();
        var battleSnapshotAfterRoomMove = await new BattleMapSnapshotReader(codec).LoadAsync(battleMapProfileRoot);
        Assert(
            roomMoveRaid["in_area"]!.GetValue<int>() == 200 &&
            roomMoveRaid["areatile"]!.GetValue<int>() == 1 &&
            roomMoveRaid["last_room_id"]!.GetValue<int>() == 200 &&
            roomMoveParty["retreat_room"]!.GetValue<int>() == 200 &&
            battleSnapshotAfterRoomMove.PartyAreaId == "rooB" &&
            battleSnapshotAfterRoomMove.PartyTileIndex == 0,
            "Moving to a room must write the native stationary room convention and resolve back to that room.");

        var preparedGuardedTreasureDelete = await battleEditService.PrepareDeleteContentAsync(
            battleEditProfile,
            battleSnapshotAfterRoomMove,
            "coAB",
            "tile2");
        _ = await battleEditService.CommitAsync(preparedGuardedTreasureDelete);
        var snapshotAfterGuardedTreasureDelete = await new BattleMapSnapshotReader(codec).LoadAsync(battleMapProfileRoot);
        var deletedGuardedTreasure = snapshotAfterGuardedTreasureDelete.Areas
            .Single(area => area.AreaId == "coAB")
            .Tiles.Single(tile => tile.TileId == "tile2");
        Assert(
            deletedGuardedTreasure.Content == BattleMapTileContent.Nothing &&
            deletedGuardedTreasure.RawContent == 0 &&
            deletedGuardedTreasure.CurioPropHash == 987 &&
            deletedGuardedTreasure.MashIndex == -1 &&
            deletedGuardedTreasure.MashType == 7,
            "Deleting a guarded treasure must remove the complete composite event and its battle mash while retaining only the inert native curio identity residue.");

        var forwardMapDocument = JsonNode.Parse(File.ReadAllText(battleMapFixturePath))!.AsObject();
        forwardMapDocument["base_root"]!["map"]!["static_dynamic"]!["areas"]!["coAB"]!["reversed"] = false;
        File.WriteAllText(battleMapFixturePath, forwardMapDocument.ToJsonString(), new UTF8Encoding(false));
        var forwardCorridorSnapshot = await new BattleMapSnapshotReader(codec).LoadAsync(battleMapProfileRoot);
        var mapHashBeforeForwardHungerMove = ComputeSha256(battleMapFixturePath);
        var preparedForwardCorridorMove = await battleEditService.PrepareMovePartyAsync(
            battleEditProfile,
            forwardCorridorSnapshot,
            "coAB",
            "tile3");
        Assert(
            preparedForwardCorridorMove.Preview.SavedAreaTile == 3 &&
            preparedForwardCorridorMove.Preview.PreviousRoomHash == 100,
            "A forward corridor move must use the physical tile ordinal and the tile0 endpoint room.");
        _ = await battleEditService.CommitAsync(preparedForwardCorridorMove);
        var snapshotAfterForwardCorridorMove = await new BattleMapSnapshotReader(codec).LoadAsync(battleMapProfileRoot);
        var hungerTileAfterMove = snapshotAfterForwardCorridorMove.Areas
            .Single(area => area.AreaId == "coAB")
            .Tiles.Single(tile => tile.TileId == "tile3");
        Assert(
            snapshotAfterForwardCorridorMove.PartyAreaId == "coAB" &&
            snapshotAfterForwardCorridorMove.PartyTileIndex == 3 &&
            snapshotAfterForwardCorridorMove.LastRoomId == "rooA" &&
            hungerTileAfterMove.Content == BattleMapTileContent.Hunger &&
            ComputeSha256(battleMapFixturePath).Equals(mapHashBeforeForwardHungerMove, StringComparison.OrdinalIgnoreCase),
            "Moving onto a hidden hunger node must resolve to the selected visible tile while leaving the map and its persisted hunger content byte-identical.");

        var unsafeBattleRaid = JsonNode.Parse(File.ReadAllText(battleRaidFixturePath))!.AsObject();
        unsafeBattleRaid["base_root"]!["inbattle"] = true;
        File.WriteAllText(battleRaidFixturePath, unsafeBattleRaid.ToJsonString(), new UTF8Encoding(false));
        var unsafeBattleSnapshot = await new BattleMapSnapshotReader(codec).LoadAsync(battleMapProfileRoot);
        var rejectedBattleStateMove = false;
        try
        {
            _ = await battleEditService.PrepareMovePartyAsync(
                battleEditProfile,
                unsafeBattleSnapshot,
                "coAB",
                "tile2");
        }
        catch (InvalidOperationException)
        {
            rejectedBattleStateMove = true;
        }
        Assert(
            rejectedBattleStateMove,
            "Party movement must be refused while combat state is active.");
        unsafeBattleRaid["base_root"]!["inbattle"] = false;
        File.WriteAllText(battleRaidFixturePath, unsafeBattleRaid.ToJsonString(), new UTF8Encoding(false));
        var snapshotBeforeStaleGuard = await new BattleMapSnapshotReader(codec).LoadAsync(battleMapProfileRoot);
        var preparedStaleBattleDelete = await battleEditService.PrepareDeleteContentAsync(
            battleEditProfile,
            snapshotBeforeStaleGuard,
            "rooB",
            "tile0");
        var mapHashBeforeRejectedCommit = ComputeSha256(battleMapFixturePath);
        var changedRaid = JsonNode.Parse(File.ReadAllText(battleRaidFixturePath))!.AsObject();
        changedRaid["base_root"]!["torchlight"] = 73.0;
        File.WriteAllText(battleRaidFixturePath, changedRaid.ToJsonString(), new UTF8Encoding(false));
        var rejectedStaleBattleCommit = false;
        try
        {
            _ = await battleEditService.CommitAsync(preparedStaleBattleDelete);
        }
        catch (InvalidOperationException)
        {
            rejectedStaleBattleCommit = true;
        }
        Assert(
            rejectedStaleBattleCommit &&
            ComputeSha256(battleMapFixturePath).Equals(mapHashBeforeRejectedCommit, StringComparison.OrdinalIgnoreCase),
            "A prepared map edit must reject a changed paired raid save before writing the target map file.");

        var dsonBattleProfileRoot = Path.Combine(runRoot, "battle-map-dson-profile");
        Directory.CreateDirectory(dsonBattleProfileRoot);
        var dsonBattleMapPath = Path.Combine(dsonBattleProfileRoot, "persist.map.json");
        var dsonBattleRaidPath = Path.Combine(dsonBattleProfileRoot, "persist.raid.json");
        var dsonBattleEstatePath = Path.Combine(dsonBattleProfileRoot, "persist.estate.json");
        await codec.EncodeAsync(battleMapFixturePath, dsonBattleMapPath, originalBinaryPath: null);
        await codec.EncodeAsync(battleRaidFixturePath, dsonBattleRaidPath, originalBinaryPath: null);
        File.WriteAllText(dsonBattleEstatePath, "{}", new UTF8Encoding(false));
        SetRevision(dsonBattleMapPath, [0x00, 0x00, 0x61, 0x42]);
        SetRevision(dsonBattleRaidPath, [0x00, 0x00, 0x61, 0x43]);
        var dsonBattleProfile = new SaveProfile(
            "profile_battle_dson",
            dsonBattleProfileRoot,
            dsonBattleEstatePath,
            "battle-user",
            File.GetLastWriteTimeUtc(dsonBattleEstatePath));
        var dsonBattleSnapshot = await new BattleMapSnapshotReader(codec).LoadAsync(dsonBattleProfileRoot);
        var dsonRaidHashBeforeDelete = ComputeSha256(dsonBattleRaidPath);
        var preparedDsonBattleDelete = await battleEditService.PrepareDeleteContentAsync(
            dsonBattleProfile,
            dsonBattleSnapshot,
            "rooB",
            "tile0");
        Assert(
            preparedDsonBattleDelete.TargetFile.SourceWasDson &&
            ReadRevision(preparedDsonBattleDelete.TargetFile.SourceCopyPath)
                .SequenceEqual(ReadRevision(preparedDsonBattleDelete.TargetFile.EncodedPath)),
            "A real DSON battle-map edit must preserve the source revision before commit.");
        _ = await battleEditService.CommitAsync(preparedDsonBattleDelete);
        var dsonBattleDecodedAfterPath = Path.Combine(runRoot, "battle-map-dson-after.json");
        await codec.DecodeAsync(dsonBattleMapPath, dsonBattleDecodedAfterPath);
        var dsonDeletedTile = JsonNode.Parse(File.ReadAllText(dsonBattleDecodedAfterPath))!
            ["base_root"]!["map"]!["static_dynamic"]!["areas"]!["rooB"]!["tiles"]!["tile0"]!.AsObject();
        Assert(
            dsonDeletedTile["content"]!.GetValue<int>() == 0 &&
            ReadRevision(dsonBattleMapPath).SequenceEqual(new byte[] { 0x00, 0x00, 0x61, 0x42 }) &&
            ComputeSha256(dsonBattleRaidPath).Equals(dsonRaidHashBeforeDelete, StringComparison.OrdinalIgnoreCase),
            "A committed DSON battle-map deletion must survive decode, retain revision bytes, and leave the paired raid save byte-identical.");

        File.WriteAllText(
            battleRaidFixturePath,
            """
    {
      "base_root": {
        "raid_instance": { "dungeon": "weald", "difficulty": 1, "length": 2 },
        "in_area": 999999,
        "areatile": 1,
        "last_room_id": 100,
        "inbattle": false
      }
    }
    """,
            new UTF8Encoding(false));
        var rejectedMismatchedBattlePair = false;
        try
        {
            _ = await new BattleMapSnapshotReader(codec).LoadAsync(battleMapProfileRoot);
        }
        catch (InvalidDataException)
        {
            rejectedMismatchedBattlePair = true;
        }
        Assert(
            rejectedMismatchedBattlePair,
            "A stable raid document whose party area does not belong to the map must be rejected rather than rendered without a party marker.");

        var monitorProfileRoot = Path.Combine(runRoot, "profile-monitor");
        Directory.CreateDirectory(monitorProfileRoot);
        var monitoredMapPath = Path.Combine(monitorProfileRoot, "persist.map.json");
        File.WriteAllText(monitoredMapPath, "{}", new UTF8Encoding(false));
        var monitorNotifications = new List<IReadOnlyList<string>>();
        var monitorNotificationCountAtStop = 0;
        using (var monitor = new ProfileSaveMonitor(
                   monitorProfileRoot,
                   ["persist.map.json", "persist.raid.json"],
                   TimeSpan.FromHours(1),
                   TimeSpan.FromHours(1)))
        {
            monitor.Changed += (_, eventArgs) => monitorNotifications.Add(eventArgs.FileNames);
            monitor.Start();
            File.WriteAllText(monitoredMapPath, "{\"revision\":2}", new UTF8Encoding(false));
            File.SetLastWriteTimeUtc(monitoredMapPath, DateTime.UtcNow.AddSeconds(1));
            monitor.PollNow();
            File.WriteAllText(
                Path.Combine(monitorProfileRoot, "persist.raid.json"),
                "{}",
                new UTF8Encoding(false));
            monitor.PollNow();
            monitorNotificationCountAtStop = monitorNotifications.Count;
            monitor.Stop();
            monitor.PollNow();
        }
        var monitoredChanges = monitorNotifications
            .SelectMany(notification => notification)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert(
            monitorNotificationCountAtStop >= 2 &&
            monitorNotifications.Count == monitorNotificationCountAtStop &&
            monitoredChanges.Contains("persist.map.json") &&
            monitoredChanges.Contains("persist.raid.json"),
            "The reusable profile monitor must deterministically detect changed and newly created watched saves, while becoming quiet after Stop.");
    }
}
