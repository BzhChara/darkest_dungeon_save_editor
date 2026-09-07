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
          "bounds": [-1.0, 7.0, 0.0, 0.0],
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
                      "tile2": { "type": 1, "cur": 987, "obstacle": 654, "mappos": [2.0, 0.0] },
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
                  },
                  "rooC": {
                    "id": 250,
                    "kind": 0,
                    "tiles": {
                      "tile0": { "type": 3, "cur": 0, "obstacle": 0, "mappos": [7.0, 0.0] }
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
              },
              "rooC": {
                "knowledge": 2,
                "reversed": false,
                "tiles": {
                  "tile0": { "content": 1, "knowledge": 2, "curio_prop": 0, "mash_index": 4, "mash_type": 1 }
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
            battleSnapshot.RoomCount == 3 &&
            battleSnapshot.CorridorCount == 1 &&
            battleSnapshot.TileCount == 8 &&
            battleSnapshot.EntranceAreaId == "rooA" &&
            battleSnapshot.FinalRoomId == "rooB" &&
            battleSnapshot.PartyAreaId == "coAB" &&
            battleSnapshot.PartyTileIndex == 3 &&
            battleSnapshot.LastRoomId == "rooA" &&
            battleCorridor.Reversed &&
            battleCorridor.Tiles[1].Content == BattleMapTileContent.Trap &&
            battleCorridor.Tiles[1].TrapHash == 321 &&
            battleCorridor.Tiles[2].Content == BattleMapTileContent.GuardedTreasure &&
            battleCorridor.Tiles[2].StaticCurioHash == 987 &&
            battleCorridor.Tiles[2].StaticObstacleHash == 654 &&
            battleCorridor.Tiles[3].Content == BattleMapTileContent.Hunger &&
            battleSnapshot.Issues.Count == 0,
            "The battle-map reader must join static topology to dynamic state, resolve hashed areas, and map direction-relative corridor areatile progress onto the physical static tile.");

        var battleEstateFixturePath = Path.Combine(battleMapProfileRoot, "persist.estate.json");
        File.WriteAllText(battleEstateFixturePath, "{}", new UTF8Encoding(false));
        var battleGameFixturePath = Path.Combine(battleMapProfileRoot, "persist.game.json");
        File.WriteAllText(
            battleGameFixturePath,
            "{\"base_root\":{\"inraid\":true,\"raiddungeon\":\"cove\",\"game_mode\":\"base\",\"applied_ugcs_1_0\":{}}}",
            new UTF8Encoding(false));
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
        var deletedBattleMap = JsonNode.Parse(File.ReadAllText(battleMapFixturePath))!.AsObject();
        var deletedBattleStaticTile = deletedBattleMap["base_root"]!["map"]!["static_dynamic"]!["static_save"]!["base_root"]!["areas"]!["coAB"]!["tiles"]!["tile1"]!.AsObject();
        var deletedBattleDynamicTile = deletedBattleMap["base_root"]!["map"]!["static_dynamic"]!["areas"]!["coAB"]!["tiles"]!["tile1"]!.AsObject();
        var backedUpBattleMap = JsonNode.Parse(File.ReadAllText(
            Path.Combine(battleDeleteCommit.BackupDirectory, "persist.map.json")))!.AsObject();
        var backedUpDeletedTile = backedUpBattleMap["base_root"]!["map"]!["static_dynamic"]!["areas"]!["coAB"]!["tiles"]!["tile1"]!.AsObject();
        Assert(
            deletedBattleTile.RawContent == 0 &&
            deletedBattleTile.Knowledge == BattleMapTileKnowledge.Scouted &&
            deletedBattleTile.StaticCurioHash == 0 &&
            deletedBattleTile.StaticObstacleHash == 0 &&
            deletedBattleTile.CurioPropHash == 0 &&
            deletedBattleTile.TrapHash == 0 &&
            !deletedBattleTile.HasResidualContentBinding &&
            deletedBattleTile.MashIndex == -1 &&
            deletedBattleTile.MashType == 7 &&
            deletedBattleStaticTile["cur"]!.GetValue<int>() == 0 &&
            deletedBattleStaticTile["obstacle"]!.GetValue<int>() == 0 &&
            deletedBattleDynamicTile["curio_prop"]!.GetValue<int>() == 0 &&
            deletedBattleDynamicTile["trap"]!.GetValue<int>() == 0 &&
            ComputeSha256(battleRaidFixturePath).Equals(raidHashBeforeDelete, StringComparison.OrdinalIgnoreCase) &&
            backedUpDeletedTile["content"]!.GetValue<int>() == 3 &&
            backedUpDeletedTile["trap"]!.GetValue<int>() == 321 &&
            File.Exists(Path.Combine(battleDeleteCommit.BackupDirectory, "persist.raid.json")) &&
            File.Exists(Path.Combine(battleDeleteCommit.BackupDirectory, "persist.estate.json")),
            "Deleting map content must preserve knowledge, clear every event/resource binding, normalize stale mash fields, leave the raid save byte-identical, and back up the full profile first.");

        var residualMap = JsonNode.Parse(File.ReadAllText(battleMapFixturePath))!.AsObject();
        var residualStaticTile = residualMap["base_root"]!["map"]!["static_dynamic"]!["static_save"]!["base_root"]!["areas"]!["coAB"]!["tiles"]!["tile1"]!.AsObject();
        var residualDynamicTile = residualMap["base_root"]!["map"]!["static_dynamic"]!["areas"]!["coAB"]!["tiles"]!["tile1"]!.AsObject();
        residualStaticTile["cur"] = 444;
        residualStaticTile["obstacle"] = 555;
        residualDynamicTile["curio_prop"] = 444;
        residualDynamicTile["trap"] = 666;
        residualDynamicTile["mash_index"] = 23;
        residualDynamicTile["mash_type"] = 0;
        File.WriteAllText(battleMapFixturePath, residualMap.ToJsonString(), new UTF8Encoding(false));
        var snapshotWithCompletedResidue = await new BattleMapSnapshotReader(codec)
            .LoadAsync(battleMapProfileRoot);
        var completedResidualTile = snapshotWithCompletedResidue.Areas
            .Single(area => area.AreaId == "coAB")
            .Tiles.Single(tile => tile.TileId == "tile1");
        Assert(
            completedResidualTile.RawContent == 0 &&
            completedResidualTile.HasResidualContentBinding,
            "A completed tile with stale static/dynamic resource or mash bindings must remain identifiable as deletable residue.");
        var preparedResidualDelete = await battleEditService.PrepareDeleteContentAsync(
            battleEditProfile,
            snapshotWithCompletedResidue,
            "coAB",
            "tile1");
        _ = await battleEditService.CommitAsync(preparedResidualDelete);
        battleSnapshotAfterDelete = await new BattleMapSnapshotReader(codec).LoadAsync(battleMapProfileRoot);
        var cleanedResidualTile = battleSnapshotAfterDelete.Areas
            .Single(area => area.AreaId == "coAB")
            .Tiles.Single(tile => tile.TileId == "tile1");
        Assert(
            cleanedResidualTile.RawContent == 0 &&
            !cleanedResidualTile.HasResidualContentBinding &&
            cleanedResidualTile.Knowledge == BattleMapTileKnowledge.Scouted,
            "Delete must also hard-clean a naturally completed tile's residual model bindings without resetting exploration.");
        var emptyMapHash = ComputeSha256(battleMapFixturePath);
        var rejectedEmptyDelete = false;
        try
        {
            _ = await battleEditService.PrepareDeleteContentAsync(
                battleEditProfile,
                battleSnapshotAfterDelete,
                "coAB",
                "tile1");
        }
        catch (InvalidOperationException ex) when (
            ex.Message.Contains("没有可删除的事件或残留资源", StringComparison.Ordinal))
        {
            rejectedEmptyDelete = true;
        }
        Assert(
            rejectedEmptyDelete &&
            ComputeSha256(battleMapFixturePath).Equals(emptyMapHash, StringComparison.OrdinalIgnoreCase),
            "A truly empty tile must still reject delete without touching the map save.");

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
            deletedGuardedTreasure.StaticCurioHash == 0 &&
            deletedGuardedTreasure.StaticObstacleHash == 0 &&
            deletedGuardedTreasure.CurioPropHash == 0 &&
            deletedGuardedTreasure.TrapHash == 0 &&
            !deletedGuardedTreasure.HasResidualContentBinding &&
            deletedGuardedTreasure.MashIndex == -1 &&
            deletedGuardedTreasure.MashType == 7,
            "Deleting a guarded treasure must remove the complete composite event, battle mash, and all static/dynamic prop residue.");

        var encounterGameRoot = Path.Combine(runRoot, "battle-encounter-game");
        var encounterDungeonRoot = Path.Combine(encounterGameRoot, "dungeons", "cove");
        Directory.CreateDirectory(encounterDungeonRoot);
        var encounterLocalizationRoot = Path.Combine(encounterGameRoot, "localization");
        Directory.CreateDirectory(encounterLocalizationRoot);
        File.WriteAllText(
            Path.Combine(encounterLocalizationRoot, "encounters.string_table.xml"),
            """
            <root>
              <language id="english">
                <entry id="str_monstername_collector_B"><![CDATA[The Collector]]></entry>
                <entry id="str_monstername_thing_B"><![CDATA[Thing from the Stars]]></entry>
                <entry id="str_curio_title_fish_idol"><![CDATA[Fish Idol]]></entry>
                <entry id="str_curio_title_unlocked_strongbox"><![CDATA[Unlocked Strongbox]]></entry>
                <entry id="str_curio_title_mod_telescope"><![CDATA[Mod Telescope]]></entry>
                <entry id="str_curio_title_mod_reliquary"><![CDATA[Mod Reliquary]]></entry>
                <entry id="str_curio_title_fallback_curio"><![CDATA[Fallback Curio]]></entry>
              </language>
              <language id="schinese">
                <entry id="str_monstername_collector_B"><![CDATA[收藏家]]></entry>
                <entry id="str_monstername_thing_B"><![CDATA[星空异兽]]></entry>
                <entry id="str_curio_title_fish_idol"><![CDATA[鱼神像]]></entry>
                <entry id="str_curio_title_unlocked_strongbox"><![CDATA[未上锁的保险箱]]></entry>
                <entry id="str_curio_title_mod_telescope"><![CDATA[模组望远镜]]></entry>
                <entry id="str_curio_title_mod_reliquary"><![CDATA[模组圣物匣]]></entry>
                <entry id="str_curio_title_fallback_curio"><![CDATA[无清单奇物]]></entry>
              </language>
            </root>
            """,
            new UTF8Encoding(false));
        var standardPropPath = Path.Combine(encounterDungeonRoot, "cove.props.darkest");
        WriteMapCurioFixtures(encounterGameRoot, "base", "fish_idol", "unlocked_strongbox");
        File.WriteAllText(
            standardPropPath,
            "room_curios: .chance 1 .types fish_idol\n" +
            "room_treasures: .chance 1 .types unlocked_strongbox\n",
            new UTF8Encoding(false));
        var standardEncounterPath = Path.Combine(encounterDungeonRoot, "cove.2.mash.darkest");
        File.WriteAllText(
            standardEncounterPath,
            """
            hall: .chance 1 .types brigand_cutthroat_B cultist_brawler_B
            hall: .chance 0 .types collector_B .limit 1 .can_be_ambush false
            room: .chance 1 .types pelagic_grouper_B pelagic_shaman_B
            boss: .chance 1 .types siren_B drowned_crew_anchor_B
            """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(encounterDungeonRoot, "cove.conditional.2.mash.darkest"),
            "hall: .chance 0.04 .types shambler_B .torchlight_valid_percent_range 0 0\n",
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(encounterDungeonRoot, "cove.additional.2.mash.darkest"),
            "hall: .chance 0.5 .types thing_B .random_dungeon_roaming_id thing\n",
            new UTF8Encoding(false));
        var encounterWealdRoot = Path.Combine(encounterGameRoot, "dungeons", "weald");
        Directory.CreateDirectory(encounterWealdRoot);
        File.WriteAllText(
            Path.Combine(encounterWealdRoot, "weald.2.mash.darkest"),
            "hall: .chance 1 .types carrion_eater_B\n" +
            "room: .chance 1 .types fungal_scratcher_B\n" +
            "boss: .chance 1 .types hag_B cauldron_empty_B\n",
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(encounterWealdRoot, "weald.3.mash.darkest"),
            "boss: .chance 1 .types hag_C cauldron_empty_C\n",
            new UTF8Encoding(false));
        var encounterModRoot = Path.Combine(runRoot, "battle-encounter-mod");
        var encounterModWealdRoot = Path.Combine(encounterModRoot, "dungeons", "weald");
        var encounterModShipRoot = Path.Combine(encounterModRoot, "dungeons", "ship");
        var encounterModUnusedRoot = Path.Combine(encounterModRoot, "dungeons", "unused");
        Directory.CreateDirectory(encounterModWealdRoot);
        Directory.CreateDirectory(encounterModShipRoot);
        Directory.CreateDirectory(encounterModUnusedRoot);
        var modWealdPath = Path.Combine(encounterModWealdRoot, "weald.2.mash.darkest");
        var modShipPath = Path.Combine(encounterModShipRoot, "ship.additional.2.mash.darkest");
        var modShipPropPath = Path.Combine(encounterModShipRoot, "ship.props.darkest");
        File.WriteAllText(
            modWealdPath,
            "hall: .chance 1 .types carrion_eater_B\n" +
            "room: .chance 1 .types fungal_scratcher_B\n" +
            "boss: .chance 1 .types mod_hag_B mod_cauldron_B\n",
            new UTF8Encoding(false));
        File.WriteAllText(
            modShipPath,
            "hall: .chance 0.25 .types mod_roamer_B .random_dungeon_roaming_id mod_roamer\n" +
            "hall: .chance 0.25 .types missing_dependency_B .random_dungeon_roaming_id missing_dependency\n",
            new UTF8Encoding(false));
        File.WriteAllText(
            modShipPropPath,
            "room_curios: .chance 1 .types mod_telescope\n" +
            "room_treasures: .chance 1 .types mod_reliquary\n",
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(encounterModUnusedRoot, "unused.props.darkest"),
            "room_curios: .chance 1 .types manifest_residue_curio\n",
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(encounterModUnusedRoot, "unused.additional.2.mash.darkest"),
            "hall: .chance 1 .types manifest_residue_B\n",
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(encounterModRoot, "modfiles.txt"),
            "dungeons/weald/weald.2.mash.darkest 1\n" +
            "dungeons/ship/ship.additional.2.mash.darkest 1\n" +
            "dungeons/ship/ship.props.darkest 1\n" +
            "curios/ship_curio_props.csv 1\ncurios/ship_curio_type_library.csv 1\n",
            new UTF8Encoding(false));
        WriteMapCurioFixtures(encounterModRoot, "ship", "mod_telescope", "mod_reliquary");
        var noManifestPropModRoot = Path.Combine(runRoot, "battle-room-prop-no-manifest-mod");
        var noManifestPropDungeonRoot = Path.Combine(
            noManifestPropModRoot,
            "dungeons",
            "ruins");
        Directory.CreateDirectory(noManifestPropDungeonRoot);
        WriteMapCurioFixtures(noManifestPropModRoot, "fallback", "fallback_curio");
        File.WriteAllText(
            Path.Combine(noManifestPropDungeonRoot, "ruins.props.darkest"),
            "room_curios: .chance 1 .types fallback_curio\n",
            new UTF8Encoding(false));
        CreateBattleMonsterDefinitions(
            encounterGameRoot,
            [
                "brigand_cutthroat_B",
                "cultist_brawler_B",
                "pelagic_grouper_B",
                "pelagic_shaman_B",
                "siren_B",
                "drowned_crew_anchor_B",
                "collector_B",
                "shambler_B",
                "thing_B",
                "hag_B",
                "cauldron_empty_B",
                "carrion_eater_B",
                "fungal_scratcher_B",
                "hag_C",
                "cauldron_empty_C",
                "mod_hag_B",
                "mod_cauldron_B",
                "mod_roamer_B"
            ],
            ["collector_B", "shambler_B", "thing_B"]);
        var encounterContent = new ActiveContentSnapshot(
            battleEditProfile,
            "base",
            [
                new ActiveContentSource("base", "base", "base", encounterGameRoot, 0),
                new ActiveContentSource(
                    "local:Encounter Catalog Mod",
                    "Encounter Catalog Mod",
                    "local",
                    encounterModRoot,
                    1000),
                new ActiveContentSource(
                    "local:No Manifest Prop Mod",
                    "No Manifest Prop Mod",
                    "local",
                    noManifestPropModRoot,
                    1001)
            ],
            [],
            Path.Combine(runRoot, "battle-encounter-catalog"),
            battleGameFixturePath,
            0,
            ComputeSha256(battleGameFixturePath));

        var attachmentProfileRoot = Path.Combine(runRoot, "battle-room-attachment-profile");
        Directory.CreateDirectory(attachmentProfileRoot);
        foreach (var sourcePath in Directory.EnumerateFiles(
                     battleMapProfileRoot,
                     "persist*.json",
                     SearchOption.TopDirectoryOnly))
        {
            File.Copy(
                sourcePath,
                Path.Combine(attachmentProfileRoot, Path.GetFileName(sourcePath)),
                overwrite: false);
        }
        var attachmentEstatePath = Path.Combine(attachmentProfileRoot, "persist.estate.json");
        var attachmentGamePath = Path.Combine(attachmentProfileRoot, "persist.game.json");
        var attachmentMapPath = Path.Combine(attachmentProfileRoot, "persist.map.json");
        var attachmentRaidPath = Path.Combine(attachmentProfileRoot, "persist.raid.json");
        var attachmentProfile = new SaveProfile(
            "profile_battle_attachment",
            attachmentProfileRoot,
            attachmentEstatePath,
            "battle-user",
            File.GetLastWriteTimeUtc(attachmentEstatePath));
        var attachmentContent = encounterContent with
        {
            Profile = attachmentProfile,
            WorkspaceDirectory = Path.Combine(runRoot, "battle-room-attachment-catalog"),
            DecodedGamePath = attachmentGamePath,
            SourceGameSha256 = ComputeSha256(attachmentGamePath)
        };
        var attachmentCatalog = BattleRoomAttachmentCatalog.Load(attachmentContent);
        await RunBattleMapContentContractsAsync(runRoot, codec, attachmentContent);
        var fishIdol = attachmentCatalog.Curios.Single(definition => definition.Id == "fish_idol");
        var unlockedStrongbox = attachmentCatalog.Treasures.Single(definition =>
            definition.Id == "unlocked_strongbox");
        var modTelescope = attachmentCatalog.Curios.Single(definition =>
            definition.Id == "mod_telescope");
        var fallbackCurio = attachmentCatalog.Curios.Single(definition =>
            definition.Id == "fallback_curio");
        Assert(
            attachmentCatalog.Curios.Count == 3 &&
            attachmentCatalog.Treasures.Count == 2 &&
            attachmentCatalog.Definitions.All(definition =>
                definition.Id != "manifest_residue_curio") &&
            fishIdol.PropHash == 2115706125 &&
            unlockedStrongbox.PropHash == -1086187210 &&
            fishIdol.ChineseName == "鱼神像" &&
            fishIdol.EnglishName == "Fish Idol" &&
            modTelescope.ChineseName == "模组望远镜" &&
            modTelescope.SourceLabel.Contains("本地 Mod", StringComparison.Ordinal) &&
            fallbackCurio.ChineseName == "无清单奇物" &&
            fallbackCurio.SourceLabel.Contains("No Manifest Prop Mod", StringComparison.Ordinal),
            "The room-attachment catalog must flatten active room_curios/room_treasures rows into exact hash-addressable bilingual choices, preserve signed game hashes, fall back to standard directories for Mods without manifests, and ignore unlisted manifest residue.");

        var attachmentService = new BattleMapEditService(codec, battleEditLocations);
        var attachmentSnapshot = await new BattleMapSnapshotReader(codec)
            .LoadAsync(attachmentProfileRoot);
        var preparedStaleAttachment = await attachmentService.PrepareSetBattleAttachmentAsync(
            attachmentProfile,
            attachmentSnapshot,
            "rooC",
            "tile0",
            fishIdol);
        var attachmentMapHashBeforeStaleCommit = ComputeSha256(attachmentMapPath);
        var standardPropText = File.ReadAllText(standardPropPath);
        File.AppendAllText(
            standardPropPath,
            "room_curios: .chance 1 .types changed_after_attachment_selection\n",
            new UTF8Encoding(false));
        var rejectedStaleAttachment = false;
        try
        {
            _ = await attachmentService.CommitAsync(preparedStaleAttachment);
        }
        catch (InvalidOperationException ex) when (
            ex.Message.Contains("地图内容定义", StringComparison.Ordinal))
        {
            rejectedStaleAttachment = true;
        }
        File.WriteAllText(standardPropPath, standardPropText, new UTF8Encoding(false));
        Assert(
            rejectedStaleAttachment &&
            ComputeSha256(attachmentMapPath).Equals(
                attachmentMapHashBeforeStaleCommit,
                StringComparison.OrdinalIgnoreCase),
            "A changed effective room-prop table must invalidate a selected attachment before the live map can be touched.");

        var preparedCurioAttachment = await attachmentService.PrepareSetBattleAttachmentAsync(
            attachmentProfile,
            attachmentSnapshot,
            "rooC",
            "tile0",
            fishIdol);
        Assert(
            preparedCurioAttachment.Preview.Kind == BattleMapEditKind.SetBattleAttachment &&
            preparedCurioAttachment.Attachment?.Id == "fish_idol" &&
            preparedCurioAttachment.TargetFile.FileName == "persist.map.json",
            "A selected room curio must prepare a guarded map-only attachment transaction.");
        _ = await attachmentService.CommitAsync(preparedCurioAttachment);
        var snapshotAfterCurioAttachment = await new BattleMapSnapshotReader(codec)
            .LoadAsync(attachmentProfileRoot);
        var curioRoom = snapshotAfterCurioAttachment.Areas
            .Single(area => area.AreaId == "rooC")
            .Tiles.Single(tile => tile.TileId == "tile0");
        var curioMapDocument = JsonNode.Parse(File.ReadAllText(attachmentMapPath))!.AsObject();
        var curioStaticTile = curioMapDocument["base_root"]!["map"]!["static_dynamic"]!["static_save"]!["base_root"]!["areas"]!["rooC"]!["tiles"]!["tile0"]!.AsObject();
        Assert(
            curioRoom.RawContent == (int)BattleMapTileContent.GuardedCurio &&
            curioRoom.CurioPropHash == fishIdol.PropHash &&
            curioRoom.MashType == 1 &&
            curioRoom.MashIndex == 4 &&
            curioStaticTile["cur"]!.GetValue<int>() == fishIdol.PropHash &&
            ComputeSha256(attachmentRaidPath).Equals(
                attachmentSnapshot.RaidSha256,
                StringComparison.OrdinalIgnoreCase),
            "Adding a guarded curio must write the same prop hash to static and dynamic bindings while preserving the existing room encounter and leaving raid state byte-identical.");

        var preparedTreasureAttachment = await attachmentService.PrepareSetBattleAttachmentAsync(
            attachmentProfile,
            snapshotAfterCurioAttachment,
            "rooC",
            "tile0",
            unlockedStrongbox);
        _ = await attachmentService.CommitAsync(preparedTreasureAttachment);
        var snapshotAfterTreasureAttachment = await new BattleMapSnapshotReader(codec)
            .LoadAsync(attachmentProfileRoot);
        var treasureRoom = snapshotAfterTreasureAttachment.Areas
            .Single(area => area.AreaId == "rooC")
            .Tiles.Single(tile => tile.TileId == "tile0");
        Assert(
            treasureRoom.RawContent == (int)BattleMapTileContent.GuardedTreasure &&
            treasureRoom.CurioPropHash == unlockedStrongbox.PropHash &&
            treasureRoom.MashType == 1 &&
            treasureRoom.MashIndex == 4,
            "Replacing a guarded curio with a guarded treasure must replace only the prop binding and composite content value, never the battle mash.");

        var preparedAttachmentRemoval = await attachmentService.PrepareRemoveBattleAttachmentAsync(
            attachmentProfile,
            snapshotAfterTreasureAttachment,
            "rooC",
            "tile0");
        _ = await attachmentService.CommitAsync(preparedAttachmentRemoval);
        var snapshotAfterAttachmentRemoval = await new BattleMapSnapshotReader(codec)
            .LoadAsync(attachmentProfileRoot);
        var plainBattleRoom = snapshotAfterAttachmentRemoval.Areas
            .Single(area => area.AreaId == "rooC")
            .Tiles.Single(tile => tile.TileId == "tile0");
        var plainBattleMap = JsonNode.Parse(File.ReadAllText(attachmentMapPath))!.AsObject();
        var plainBattleStaticTile = plainBattleMap["base_root"]!["map"]!["static_dynamic"]!["static_save"]!["base_root"]!["areas"]!["rooC"]!["tiles"]!["tile0"]!.AsObject();
        Assert(
            preparedAttachmentRemoval.Preview.Kind == BattleMapEditKind.RemoveBattleAttachment &&
            plainBattleRoom.RawContent == (int)BattleMapTileContent.Battle &&
            plainBattleRoom.CurioPropHash == 0 &&
            plainBattleRoom.MashType == 1 &&
            plainBattleRoom.MashIndex == 4 &&
            plainBattleStaticTile["cur"]!.GetValue<int>() == 0,
            "Removing guarded content must return the room to a plain battle, clear both prop bindings and preserve the exact encounter index.");

        var rejectedCorridorAttachment = false;
        try
        {
            _ = await attachmentService.PrepareSetBattleAttachmentAsync(
                attachmentProfile,
                snapshotAfterAttachmentRemoval,
                "coAB",
                "tile2",
                fishIdol);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("只能用于普通房间", StringComparison.Ordinal))
        {
            rejectedCorridorAttachment = true;
        }
        var rejectedFinalRoomAttachment = false;
        try
        {
            _ = await attachmentService.PrepareSetBattleAttachmentAsync(
                attachmentProfile,
                snapshotAfterAttachmentRemoval,
                "rooB",
                "tile0",
                fishIdol);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("最终房间", StringComparison.Ordinal))
        {
            rejectedFinalRoomAttachment = true;
        }
        Assert(
            rejectedCorridorAttachment && rejectedFinalRoomAttachment,
            "Corridors and the final room must remain outside the guarded room-attachment write contract.");

        var fixedBossAttachmentMap = JsonNode.Parse(File.ReadAllText(attachmentMapPath))!.AsObject();
        fixedBossAttachmentMap["base_root"]!["map"]!["static_dynamic"]!["areas"]!["rooC"]!["tiles"]!["tile0"]!["mash_type"] = 2;
        File.WriteAllText(
            attachmentMapPath,
            fixedBossAttachmentMap.ToJsonString(),
            new UTF8Encoding(false));
        var fixedBossAttachmentSnapshot = await new BattleMapSnapshotReader(codec)
            .LoadAsync(attachmentProfileRoot);
        var rejectedFixedBossAttachment = false;
        try
        {
            _ = await attachmentService.PrepareSetBattleAttachmentAsync(
                attachmentProfile,
                fixedBossAttachmentSnapshot,
                "rooC",
                "tile0",
                fishIdol);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("普通房间索引", StringComparison.Ordinal))
        {
            rejectedFixedBossAttachment = true;
        }
        Assert(
            rejectedFixedBossAttachment,
            "A fixed-boss mash_type=2 room must not expose the ordinary guarded-curio/treasure attachment contract.");

        var encounterCatalog = BattleEncounterCatalog.Load(
            encounterContent,
            snapshotAfterGuardedTreasureDelete);
        VerifyDirectEncounterDependencyContracts(encounterContent, snapshotAfterGuardedTreasureDelete, standardEncounterPath);
        VerifyEncounterDiagnosticContracts(encounterContent, snapshotAfterGuardedTreasureDelete, runRoot);
        VerifyBattleMapLogContracts(snapshotAfterGuardedTreasureDelete);
        VerifyEncounterSelectionContracts(encounterCatalog);
        var directHallEncounters = encounterCatalog.DirectEncounters
            .Where(encounter => encounter.MashType == 0)
            .ToArray();
        var directRoomEncounter = encounterCatalog.DirectEncounters
            .Single(encounter => encounter.MashType == 1);
        var directBossEncounter = encounterCatalog.DirectEncounters
            .Single(encounter => encounter.MashType == 2);
        Assert(
            directHallEncounters.Length == 2 &&
            directHallEncounters[0].MashIndex == 0 &&
            directHallEncounters[1].MashIndex == 1 &&
            directHallEncounters[1].Weight == 0 &&
            directHallEncounters[0].Classification == BattleEncounterClassification.Ordinary &&
            directHallEncounters[1].Classification ==
                BattleEncounterClassification.ConditionalOrAdditional &&
            directHallEncounters[1].ContainsBossMonster &&
            directHallEncounters[1].ChineseDisplayName == "收藏家" &&
            directRoomEncounter.MashIndex == 0 &&
            directBossEncounter.MashIndex == 0 &&
            directBossEncounter.Classification == BattleEncounterClassification.FixedBoss &&
            encounterCatalog.SpecialEncounters.Count == 2 &&
            encounterCatalog.SpecialEncounters.All(encounter =>
                !encounter.CanPlaceDirectly &&
                encounter.MashIndex is null &&
                encounter.UnavailableReason.Contains("桥接", StringComparison.Ordinal)),
            "The encounter catalog must assign zero-based indexes independently by standard mash type while keeping conditional/additional side-table rows non-addressable until bridged.");

        var globalModBoss = encounterCatalog.BridgeEncounters.Single(encounter =>
            encounter.OriginDungeonId == "weald" &&
            encounter.OriginDifficulty == 2 &&
            encounter.MashType == 2);
        var globalDifferentDifficultyBoss = encounterCatalog.BridgeEncounters.Single(encounter =>
            encounter.OriginDungeonId == "weald" &&
            encounter.OriginDifficulty == 3 &&
            encounter.MashType == 2);
        var globalOrdinaryHall = encounterCatalog.BridgeEncounters.Single(encounter =>
            encounter.OriginDungeonId == "weald" &&
            encounter.OriginDifficulty == 2 &&
            encounter.MonsterIds.SequenceEqual(["carrion_eater_B"]));
        var globalOrdinaryRoom = encounterCatalog.BridgeEncounters.Single(encounter =>
            encounter.OriginDungeonId == "weald" &&
            encounter.OriginDifficulty == 2 &&
            encounter.MonsterIds.SequenceEqual(["fungal_scratcher_B"]));
        BattleEncounterCatalog.ValidateBridgeEncounter(globalOrdinaryHall);
        BattleEncounterCatalog.ValidateBridgeEncounter(globalOrdinaryRoom);
        var globalModRoamer = encounterCatalog.BridgeEncounters.Single(encounter =>
            encounter.OriginDungeonId == "ship" &&
            encounter.OriginDifficulty == 2 &&
            encounter.RoamingId == "mod_roamer");
        var globalThingRoamingBoss = encounterCatalog.BridgeEncounters.Single(encounter =>
            encounter.SourceKind == BattleEncounterSourceKind.Additional &&
            encounter.MonsterIds.SequenceEqual(["thing_B"]));
        Assert(
            globalModBoss.MonsterIds.SequenceEqual(["mod_hag_B", "mod_cauldron_B"]) &&
            globalModBoss.Classification == BattleEncounterClassification.FixedBoss &&
            globalModBoss.SourceLabel.Contains("本地 Mod", StringComparison.Ordinal) &&
            globalDifferentDifficultyBoss.MonsterIds.SequenceEqual(["hag_C", "cauldron_empty_C"]) &&
            globalOrdinaryHall.MashType == 0 &&
            globalOrdinaryHall.Classification == BattleEncounterClassification.Ordinary &&
            !globalOrdinaryHall.CanPlaceDirectly &&
            globalOrdinaryRoom.MashType == 1 &&
            globalOrdinaryRoom.Classification == BattleEncounterClassification.Ordinary &&
            !globalOrdinaryRoom.CanPlaceDirectly &&
            globalModRoamer.MashType == 0 &&
            globalModRoamer.SourceKind == BattleEncounterSourceKind.Additional &&
            globalModRoamer.Classification == BattleEncounterClassification.RoamingEncounter &&
            !globalModRoamer.ContainsBossMonster &&
            globalThingRoamingBoss.Classification == BattleEncounterClassification.RoamingBoss &&
            globalThingRoamingBoss.ContainsBossMonster &&
            globalThingRoamingBoss.ChineseDisplayName == "星空异兽" &&
            globalThingRoamingBoss.MonsterNames.Single().English == "Thing from the Stars" &&
            globalModRoamer.ChineseDisplayName == "—" &&
            encounterCatalog.BridgeEncounters.All(encounter =>
                !encounter.MonsterIds.Contains("hag_B") &&
                !encounter.MonsterIds.Contains("manifest_residue_B") &&
                !encounter.MonsterIds.Contains("missing_dependency_B")) &&
            encounterCatalog.Issues.Any(issue =>
                issue.Contains("遭遇排除明细（全局 Bridge）：未找到活动怪物定义", StringComparison.Ordinal) &&
                issue.Contains("missing_dependency_B", StringComparison.Ordinal)),
            "The global Bridge catalog must include enabled Base/Mod encounters across regions and difficulties, preserve authored target kinds, apply exact-path overlays, and exclude unlisted Mod residue or rows with unresolved active monster dependencies.");

        var conditionalEncounter = encounterCatalog.SpecialEncounters.Single(encounter =>
            encounter.SourceKind == BattleEncounterSourceKind.Conditional);
        Assert(
            conditionalEncounter.Classification ==
                BattleEncounterClassification.ConditionalOrAdditional &&
            conditionalEncounter.ContainsBossMonster,
            "A boss-tagged conditional row without a roaming id must remain a conditional battle rather than being mislabeled as a roaming boss.");
        var sourceMashBytes = File.ReadAllBytes(standardEncounterPath);
        var bridgePackage = BattleEncounterBridgeBuilder.Build(
            encounterCatalog,
            conditionalEncounter,
            Path.Combine(runRoot, "battle-encounter-bridge-packages"));
        Assert(
            bridgePackage.ExpectedMashIndex == 2 &&
            bridgePackage.MashType == 0 &&
            bridgePackage.MonsterIds.SequenceEqual(["shambler_B"]) &&
            Path.GetFileName(bridgePackage.PackageDirectory).Contains(
                "shambler_B",
                StringComparison.Ordinal) &&
            bridgePackage.ProjectTitle.Contains("shambler_B", StringComparison.Ordinal) &&
            !bridgePackage.ProjectTitle.Contains("Probe", StringComparison.OrdinalIgnoreCase) &&
            File.ReadAllBytes(standardEncounterPath).SequenceEqual(sourceMashBytes) &&
            File.ReadAllText(bridgePackage.MashFilePath).Trim() ==
                "hall: .chance 0 .types shambler_B .limit 1 .can_be_ambush false" &&
            Path.GetFileName(bridgePackage.ManifestPath) == "ddse-encounter-bridge.json" &&
            File.Exists(bridgePackage.ManifestPath) &&
            File.Exists(Path.Combine(bridgePackage.PackageDirectory, "project.xml")) &&
            File.Exists(Path.Combine(bridgePackage.PackageDirectory, "modfiles.txt")),
            "The production Bridge package must contain only the selected zero-weight encounter and leave the native table untouched while emitting a self-describing local Mod package.");
        var fixedBossBridgePackage = BattleEncounterBridgeBuilder.Build(
            encounterCatalog,
            globalModBoss,
            Path.Combine(runRoot, "battle-encounter-bridge-packages"));
        Assert(
            fixedBossBridgePackage.MashType == 2 &&
            fixedBossBridgePackage.ExpectedMashIndex == 1 &&
            fixedBossBridgePackage.OriginDungeonId == "weald" &&
            fixedBossBridgePackage.OriginDifficulty == 2 &&
            File.ReadAllText(fixedBossBridgePackage.MashFilePath).Contains(
                "boss: .chance 0 .types mod_hag_B mod_cauldron_B",
                StringComparison.Ordinal),
            "A cross-region enabled-Mod fixed boss must become a zero-weight boss row in the current table without changing its complete authored composition.");
        var fixedBossBridgeContent = encounterContent with
        {
            Sources =
            [
                .. encounterContent.Sources,
                new ActiveContentSource(
                    "local:" + fixedBossBridgePackage.ProjectTitle,
                    fixedBossBridgePackage.ProjectTitle,
                    "local",
                    fixedBossBridgePackage.PackageDirectory,
                    999)
            ]
        };
        var fixedBossBridgedCatalog = BattleEncounterCatalog.Load(
            fixedBossBridgeContent,
            snapshotAfterGuardedTreasureDelete);
        var fixedBossBridgedEncounter = fixedBossBridgedCatalog.DirectEncounters.Single(encounter =>
            encounter.MashType == 2 &&
            encounter.MashIndex == fixedBossBridgePackage.ExpectedMashIndex);
        var preparedFixedBossPlacement = await battleEditService.PreparePlaceBattleAsync(
            battleEditProfile,
            snapshotAfterGuardedTreasureDelete,
            "rooB",
            "tile0",
            fixedBossBridgedEncounter);
        Assert(
            fixedBossBridgedEncounter.MonsterIds.SequenceEqual(["mod_hag_B", "mod_cauldron_B"]) &&
            preparedFixedBossPlacement.Encounter?.MashType == 2 &&
            preparedFixedBossPlacement.Encounter?.MashIndex == 1 &&
            preparedFixedBossPlacement.TargetFile.FileName == "persist.map.json",
            "After reload, a generated cross-region fixed-boss row must resolve as the expected current-table boss index and pass the same DSON-backed room placement preparation as native boss rows.");
        var originalModWealdText = File.ReadAllText(modWealdPath);
        File.AppendAllText(
            modWealdPath,
            "boss: .chance 1 .types changed_global_boss_B\n",
            new UTF8Encoding(false));
        var rejectedChangedGlobalEncounter = false;
        try
        {
            _ = BattleEncounterBridgeBuilder.Build(
                encounterCatalog,
                globalModBoss,
                Path.Combine(runRoot, "battle-encounter-changed-global-source"));
        }
        catch (InvalidOperationException ex) when (
            ex.Message.Contains("已变化", StringComparison.Ordinal))
        {
            rejectedChangedGlobalEncounter = true;
        }
        File.WriteAllText(modWealdPath, originalModWealdText, new UTF8Encoding(false));
        Assert(
            rejectedChangedGlobalEncounter,
            "A selected global encounter whose effective source changed must be rejected before a Bridge package is emitted.");
        var bridgeContent = encounterContent with
        {
            Sources =
            [
                .. encounterContent.Sources,
                new ActiveContentSource(
                    "local:" + bridgePackage.ProjectTitle,
                    bridgePackage.ProjectTitle,
                    "local",
                    bridgePackage.PackageDirectory,
                    1000)
            ]
        };
        var bridgedCatalog = BattleEncounterCatalog.Load(
            bridgeContent,
            snapshotAfterGuardedTreasureDelete);
        var bridgedEncounter = bridgedCatalog.DirectEncounters.Single(encounter =>
            encounter.MashType == 0 &&
            encounter.MashIndex == bridgePackage.ExpectedMashIndex);
        Assert(
            bridgedEncounter.MonsterIds.SequenceEqual(["shambler_B"]) &&
            bridgedEncounter.Weight == 0 &&
            bridgedCatalog.HasActiveGeneratedBridge &&
            bridgedEncounter.SourceLabel.Contains("本地 Mod", StringComparison.Ordinal) &&
            bridgedEncounter.SourcePath == bridgePackage.MashFilePath,
            "The separate Bridge file must append at the expected directly addressable index and preserve its own source provenance.");
        var rejectedStackedBridge = false;
        try
        {
            var bridgedGlobalBoss = bridgedCatalog.BridgeEncounters.Single(encounter =>
                encounter.OriginDungeonId == "weald" &&
                encounter.OriginDifficulty == 2 &&
                encounter.MashType == 2);
            _ = BattleEncounterBridgeBuilder.Build(
                bridgedCatalog,
                bridgedGlobalBoss,
                Path.Combine(runRoot, "battle-encounter-stacked-bridge"));
        }
        catch (InvalidOperationException ex) when (
            ex.Message.Contains("已经启用", StringComparison.Ordinal))
        {
            rejectedStackedBridge = true;
        }
        Assert(
            rejectedStackedBridge,
            "The builder must refuse to layer a second generated DDSE Bridge over an already active Bridge table.");

        var noBossGameRoot = Path.Combine(runRoot, "battle-encounter-no-boss-game");
        var noBossCoveRoot = Path.Combine(noBossGameRoot, "dungeons", "cove");
        var noBossWealdRoot = Path.Combine(noBossGameRoot, "dungeons", "weald");
        Directory.CreateDirectory(noBossCoveRoot);
        Directory.CreateDirectory(noBossWealdRoot);
        File.WriteAllText(
            Path.Combine(noBossCoveRoot, "cove.2.mash.darkest"),
            "hall: .chance 1 .types no_boss_hall_B\nroom: .chance 1 .types no_boss_room_B\n",
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(noBossWealdRoot, "weald.2.mash.darkest"),
            "boss: .chance 1 .types first_foreign_boss_B\n",
            new UTF8Encoding(false));
        CreateBattleMonsterDefinitions(noBossGameRoot, ["first_foreign_boss_B"]);
        var noBossContent = encounterContent with
        {
            Sources = [new ActiveContentSource("base", "base", "base", noBossGameRoot, 0)]
        };
        var noBossCatalog = BattleEncounterCatalog.Load(
            noBossContent,
            snapshotAfterGuardedTreasureDelete);
        var firstForeignBoss = noBossCatalog.BridgeEncounters.Single(encounter =>
            encounter.OriginDungeonId == "weald" && encounter.MashType == 2);
        var firstBossBridge = BattleEncounterBridgeBuilder.Build(
            noBossCatalog,
            firstForeignBoss,
            Path.Combine(runRoot, "battle-encounter-first-boss-bridge"));
        Assert(
            firstBossBridge.ExpectedMashIndex == 0 &&
            File.ReadAllText(firstBossBridge.MashFilePath).Contains(
                "boss: .chance 0 .types first_foreign_boss_B",
                StringComparison.Ordinal),
            "A dungeon with no native boss rows must still support a uniquely resolved first Bridge boss at mash index zero.");

        var dlcTargetRoot = Path.Combine(runRoot, "battle-encounter-dlc-target");
        var dlcCourtyardRoot = Path.Combine(dlcTargetRoot, "dungeons", "courtyard");
        Directory.CreateDirectory(dlcCourtyardRoot);
        File.WriteAllText(
            Path.Combine(dlcCourtyardRoot, "courtyard.2.mash.darkest"),
            "hall: .chance 1 .types dlc_hall_B\nroom: .chance 1 .types dlc_room_B\n",
            new UTF8Encoding(false));
        CreateBattleMonsterDefinitions(dlcTargetRoot, ["dlc_hall_B", "dlc_room_B"]);
        var dlcSnapshot = snapshotAfterGuardedTreasureDelete with
        {
            DungeonId = "courtyard"
        };
        var dlcTargetContent = encounterContent with
        {
            Sources =
            [
                encounterContent.Sources[0],
                new ActiveContentSource(
                    "dlc-feature:court",
                    "court",
                    "dlc-feature",
                    dlcTargetRoot,
                    100)
                {
                    VirtualPathPrefix = "dlc/123_pack/features/court"
                }
            ]
        };
        var dlcTargetCatalog = BattleEncounterCatalog.Load(dlcTargetContent, dlcSnapshot);
        var dlcForeignBoss = dlcTargetCatalog.BridgeEncounters.Single(encounter =>
            encounter.OriginDungeonId == "weald" &&
            encounter.OriginDifficulty == 2 &&
            encounter.MashType == 2);
        var dlcTargetBridge = BattleEncounterBridgeBuilder.Build(
            dlcTargetCatalog,
            dlcForeignBoss,
            Path.Combine(runRoot, "battle-encounter-dlc-target-bridge"));
        Assert(
            Path.GetRelativePath(dlcTargetBridge.PackageDirectory, dlcTargetBridge.MashFilePath)
                .Replace('\\', '/') ==
            "dungeons/courtyard/ddse_managed.courtyard.2.mash.darkest",
            "A Bridge targeting an enabled DLC table must use a separate root-level file appended after the DLC files without copying or overriding them.");

        var rejectedForgedEncounter = false;
        try
        {
            _ = await battleEditService.PreparePlaceBattleAsync(
                battleEditProfile,
                snapshotAfterGuardedTreasureDelete,
                "coAB",
                "tile2",
                bridgedEncounter with { MonsterIds = ["forged_enemy_B"] });
        }
        catch (InvalidOperationException ex) when (
            ex.Message.Contains("组成", StringComparison.Ordinal))
        {
            rejectedForgedEncounter = true;
        }
        Assert(
            rejectedForgedEncounter,
            "The core must reconstruct the guarded table and reject a caller-forged composition even when its mash type and index look valid.");

        var replacementResidueMap = JsonNode.Parse(File.ReadAllText(battleMapFixturePath))!.AsObject();
        var replacementResidueStaticTile = replacementResidueMap["base_root"]!["map"]!["static_dynamic"]!["static_save"]!["base_root"]!["areas"]!["coAB"]!["tiles"]!["tile2"]!.AsObject();
        var replacementResidueDynamicTile = replacementResidueMap["base_root"]!["map"]!["static_dynamic"]!["areas"]!["coAB"]!["tiles"]!["tile2"]!.AsObject();
        replacementResidueStaticTile["cur"] = 777;
        replacementResidueStaticTile["obstacle"] = 778;
        replacementResidueDynamicTile["curio_prop"] = 777;
        replacementResidueDynamicTile["trap"] = 779;
        File.WriteAllText(
            battleMapFixturePath,
            replacementResidueMap.ToJsonString(),
            new UTF8Encoding(false));
        var snapshotWithReplacementResidue = await new BattleMapSnapshotReader(codec)
            .LoadAsync(battleMapProfileRoot);
        Assert(
            snapshotWithReplacementResidue.Areas
                .Single(area => area.AreaId == "coAB")
                .Tiles.Single(tile => tile.TileId == "tile2")
                .HasResidualContentBinding,
            "A completed tile with a used resource model must be replaceable without a separate delete operation.");
        var raidHashBeforeBattlePlacement = ComputeSha256(battleRaidFixturePath);
        var preparedBattlePlacement = await battleEditService.PreparePlaceBattleAsync(
            battleEditProfile,
            snapshotWithReplacementResidue,
            "coAB",
            "tile2",
            bridgedEncounter);
        Assert(
            preparedBattlePlacement.Preview.Kind == BattleMapEditKind.PlaceBattle &&
            preparedBattlePlacement.Encounter?.MonsterIds.SequenceEqual(["shambler_B"]) == true &&
            preparedBattlePlacement.TargetFile.FileName == "persist.map.json",
            "A proven current-table battle must prepare only the selected map tile for replacement.");
        await VerifyPendingEncounterDependencyContractAsync(battleEditService, preparedBattlePlacement,
            Path.Combine(encounterGameRoot, "monsters", "shambler_B", "shambler_B.info.darkest"));
        _ = await battleEditService.CommitAsync(preparedBattlePlacement);
        var snapshotAfterBattlePlacement = await new BattleMapSnapshotReader(codec)
            .LoadAsync(battleMapProfileRoot);
        var placedBattle = snapshotAfterBattlePlacement.Areas
            .Single(area => area.AreaId == "coAB")
            .Tiles.Single(tile => tile.TileId == "tile2");
        var placedMapDocument = JsonNode.Parse(File.ReadAllText(battleMapFixturePath))!.AsObject();
        var placedStaticTile = placedMapDocument["base_root"]!["map"]!["static_dynamic"]!["static_save"]!["base_root"]!["areas"]!["coAB"]!["tiles"]!["tile2"]!.AsObject();
        var placedDynamicTile = placedMapDocument["base_root"]!["map"]!["static_dynamic"]!["areas"]!["coAB"]!["tiles"]!["tile2"]!.AsObject();
        Assert(
            placedBattle.Content == BattleMapTileContent.Battle &&
            placedBattle.MashType == 0 &&
            placedBattle.MashIndex == 2 &&
            placedStaticTile["cur"]!.GetValue<int>() == 0 &&
            placedStaticTile["obstacle"]!.GetValue<int>() == 0 &&
            placedDynamicTile["curio_prop"]!.GetValue<int>() == 0 &&
            placedDynamicTile["trap"]!.GetValue<int>() == 0 &&
            ComputeSha256(battleRaidFixturePath).Equals(
                raidHashBeforeBattlePlacement,
                StringComparison.OrdinalIgnoreCase),
            "Replacing a completed residual tile with a battle must clear every old scalar binding without a prior delete, write one proven hall mash, preserve knowledge, and leave raid-side natural encounter bookkeeping byte-identical.");

        var rejectedEntranceEncounter = false;
        try
        {
            _ = await battleEditService.PreparePlaceBattleAsync(
                battleEditProfile,
                snapshotAfterBattlePlacement,
                "rooA",
                "tile0",
                directRoomEncounter);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("出生房间", StringComparison.Ordinal))
        {
            rejectedEntranceEncounter = true;
        }
        Assert(rejectedEntranceEncounter, "The entrance room must remain unavailable for encounter creation or replacement.");

        var managedProfileRoot = Path.Combine(runRoot, "managed-encounter-profile");
        Directory.CreateDirectory(managedProfileRoot);
        foreach (var sourcePath in Directory.EnumerateFiles(
                     battleMapProfileRoot,
                     "persist*.json",
                     SearchOption.TopDirectoryOnly))
        {
            File.Copy(
                sourcePath,
                Path.Combine(managedProfileRoot, Path.GetFileName(sourcePath)),
                overwrite: false);
        }
        var managedProfile = new SaveProfile(
            "profile_managed_encounter",
            managedProfileRoot,
            Path.Combine(managedProfileRoot, "persist.estate.json"),
            "contract-user",
            DateTime.UtcNow);
        var managedLocalModRoot = Path.Combine(runRoot, "managed-encounter-local-mods");
        Directory.CreateDirectory(managedLocalModRoot);
        var existingManagedModA = Path.Combine(managedLocalModRoot, "existing-a");
        var existingManagedModB = Path.Combine(managedLocalModRoot, "existing-b");
        Directory.CreateDirectory(existingManagedModA);
        Directory.CreateDirectory(existingManagedModB);
        File.WriteAllText(
            Path.Combine(existingManagedModA, "project.xml"),
            "<project><Title>Existing Managed Test A</Title></project>",
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(existingManagedModB, "project.xml"),
            "<project><Title>Existing Managed Test B</Title></project>",
            new UTF8Encoding(false));
        var managedGamePath = Path.Combine(managedProfileRoot, "persist.game.json");
        var managedGameBefore = JsonNode.Parse(File.ReadAllText(managedGamePath))!.AsObject();
        managedGameBefore["base_root"]!["applied_ugcs_1_0"] = new JsonObject
        {
            ["3"] = new JsonObject
            {
                ["name"] = "Existing Managed Test A",
                ["source"] = "mod_local_source",
                ["custom_payload"] = new JsonObject { ["sentinel"] = 17 }
            },
            ["9"] = new JsonObject
            {
                ["name"] = "Existing Managed Test B",
                ["source"] = "mod_local_source",
                ["custom_payload"] = "keep-me"
            }
        };
        File.WriteAllText(
            managedGamePath,
            managedGameBefore.ToJsonString(),
            new UTF8Encoding(false));
        var managedLocations = new SaveEditorLocations(
            Path.Combine(runRoot, "managed-encounter-appdata"),
            Path.Combine(runRoot, "managed-encounter-appdata", "workspaces"),
            Path.Combine(runRoot, "managed-encounter-appdata", "backups"));
        var managedSnapshot = await new BattleMapSnapshotReader(codec).LoadAsync(managedProfileRoot);
        var managedInitialContent = await ActiveContentResolver.ResolveAsync(
            managedProfile,
            encounterGameRoot,
            workshopDirectory: null,
            managedLocalModRoot,
            codec,
            managedLocations.WorkspaceDirectory);
        var managedInitialCatalog = BattleEncounterCatalog.Load(
            managedInitialContent,
            managedSnapshot);
        var managedSpecialSource = managedInitialCatalog.BridgeEncounters.Single(encounter =>
            encounter.SourceKind == BattleEncounterSourceKind.Conditional &&
            encounter.MonsterIds.SequenceEqual(["shambler_B"]));
        var managedBridgeService = new ManagedBattleEncounterBridgeService(
            codec,
            managedLocations,
            () => false);
        var firstManagedResult = await managedBridgeService.EnsureEncounterAsync(
            managedProfile,
            managedSnapshot,
            managedInitialContent,
            managedInitialCatalog,
            managedSpecialSource,
            encounterGameRoot,
            workshopDirectory: null,
            managedLocalModRoot);
        var managedGameRoot = JsonNode.Parse(File.ReadAllText(managedGamePath))!["base_root"]!.AsObject();
        var managedApplied = managedGameRoot["applied_ugcs_1_0"]!.AsObject();
        Assert(
            firstManagedResult.EncounterWasAdded &&
            firstManagedResult.ProfileConfigurationChanged &&
            firstManagedResult.MashType == 0 &&
            firstManagedResult.MashIndex == 2 &&
            firstManagedResult.DirectEncounter.CanPlaceDirectly &&
            firstManagedResult.DirectEncounter.MonsterIds.SequenceEqual(["shambler_B"]) &&
            File.Exists(firstManagedResult.ManifestPath) &&
            File.Exists(firstManagedResult.MashFilePath) &&
            File.Exists(Path.Combine(firstManagedResult.PackageDirectory, "project.xml")) &&
            File.Exists(Path.Combine(firstManagedResult.PackageDirectory, "modfiles.txt")) &&
            Directory.Exists(firstManagedResult.ProfileBackupDirectory) &&
            managedApplied.Count == 3 &&
            managedApplied["0"]!["name"]!.GetValue<string>() == firstManagedResult.ProjectTitle &&
            managedApplied["0"]!["source"]!.GetValue<string>() == "mod_local_source" &&
            managedApplied["1"]!["name"]!.GetValue<string>() == "Existing Managed Test A" &&
            managedApplied["1"]!["custom_payload"]!["sentinel"]!.GetValue<int>() == 17 &&
            managedApplied["2"]!["name"]!.GetValue<string>() == "Existing Managed Test B" &&
            managedApplied["2"]!["custom_payload"]!.GetValue<string>() == "keep-me",
            "The managed Bridge must install into the configured local-Mod root, enable itself at the top of the selected profile, preserve every existing Mod's relative order and payload, preserve the native row indexes, and immediately return a guarded direct encounter without any manual copy/reload step.");

        var managedFixedBossSource = firstManagedResult.Catalog.BridgeEncounters.Single(encounter =>
            encounter.OriginDungeonId == "weald" &&
            encounter.OriginDifficulty == 2 &&
            encounter.MashType == 2 &&
            encounter.MonsterIds.SequenceEqual(["hag_B", "cauldron_empty_B"]));
        var secondManagedResult = await managedBridgeService.EnsureEncounterAsync(
            managedProfile,
            managedSnapshot,
            firstManagedResult.ActiveContent,
            firstManagedResult.Catalog,
            managedFixedBossSource,
            encounterGameRoot,
            workshopDirectory: null,
            managedLocalModRoot);
        var managedMashAfterSecond = File.ReadAllText(secondManagedResult.MashFilePath);
        var secondManifestHash = ComputeSha256(secondManagedResult.ManifestPath);
        var reusableBossSource = secondManagedResult.Catalog.BridgeEncounters.Single(encounter =>
            encounter.OriginDungeonId == "weald" &&
            encounter.OriginDifficulty == 2 &&
            encounter.MashType == 2 &&
            encounter.MonsterIds.SequenceEqual(["hag_B", "cauldron_empty_B"]));
        var reusedManagedResult = await managedBridgeService.EnsureEncounterAsync(
            managedProfile,
            managedSnapshot,
            secondManagedResult.ActiveContent,
            secondManagedResult.Catalog,
            reusableBossSource,
            encounterGameRoot,
            workshopDirectory: null,
            managedLocalModRoot);
        Assert(
            secondManagedResult.PackageDirectory.Equals(
                firstManagedResult.PackageDirectory,
                StringComparison.OrdinalIgnoreCase) &&
            secondManagedResult.EncounterWasAdded &&
            !secondManagedResult.ProfileConfigurationChanged &&
            secondManagedResult.MashType == 2 &&
            secondManagedResult.MashIndex == 1 &&
            managedMashAfterSecond.Contains(
                "hall: .chance 0 .types shambler_B .limit 1 .can_be_ambush false",
                StringComparison.Ordinal) &&
            managedMashAfterSecond.Contains(
                "boss: .chance 0 .types hag_B cauldron_empty_B",
                StringComparison.Ordinal) &&
            !reusedManagedResult.EncounterWasAdded &&
            reusedManagedResult.MashIndex == secondManagedResult.MashIndex &&
            ComputeSha256(reusedManagedResult.ManifestPath).Equals(
                secondManifestHash,
                StringComparison.OrdinalIgnoreCase),
            "The managed Bridge must append each complete formation once per target table/type, keep earlier indexes stable, and reuse the recorded index on every later placement.");

        var managedManifestHashBeforeRunningGuard = ComputeSha256(reusedManagedResult.ManifestPath);
        var managedGameHashBeforeRunningGuard = ComputeSha256(
            Path.Combine(managedProfileRoot, "persist.game.json"));
        var rejectedManagedBridgeWhileRunning = false;
        try
        {
            var runningManagedBridgeService = new ManagedBattleEncounterBridgeService(
                codec,
                managedLocations,
                () => true);
            _ = await runningManagedBridgeService.EnsureEncounterAsync(
                managedProfile,
                managedSnapshot,
                reusedManagedResult.ActiveContent,
                reusedManagedResult.Catalog,
                reusableBossSource,
                encounterGameRoot,
                workshopDirectory: null,
                managedLocalModRoot);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("仍在运行", StringComparison.Ordinal))
        {
            rejectedManagedBridgeWhileRunning = true;
        }
        Assert(
            rejectedManagedBridgeWhileRunning &&
            ComputeSha256(reusedManagedResult.ManifestPath).Equals(
                managedManifestHashBeforeRunningGuard,
                StringComparison.OrdinalIgnoreCase) &&
            ComputeSha256(Path.Combine(managedProfileRoot, "persist.game.json")).Equals(
                managedGameHashBeforeRunningGuard,
                StringComparison.OrdinalIgnoreCase),
            "Managed Bridge updates must be rejected before touching either the carrier Mod or profile while the game is running.");

        var reorderedManagedGame = JsonNode.Parse(File.ReadAllText(managedGamePath))!.AsObject();
        var reorderedApplied = reorderedManagedGame["base_root"]!["applied_ugcs_1_0"]!.AsObject();
        var originalFirstMod = reorderedApplied["1"]!.DeepClone();
        reorderedApplied["1"] = reorderedApplied["2"]!.DeepClone();
        reorderedApplied["2"] = originalFirstMod;
        File.WriteAllText(
            managedGamePath,
            reorderedManagedGame.ToJsonString(),
            new UTF8Encoding(false));
        var reorderedManagedContent = await ActiveContentResolver.ResolveAsync(
            managedProfile,
            encounterGameRoot,
            workshopDirectory: null,
            managedLocalModRoot,
            codec,
            managedLocations.WorkspaceDirectory);
        var reorderedManagedCatalog = BattleEncounterCatalog.Load(
            reorderedManagedContent,
            managedSnapshot);
        var reorderedReusableBoss = reorderedManagedCatalog.BridgeEncounters.Single(encounter =>
            encounter.OriginDungeonId == "weald" &&
            encounter.OriginDifficulty == 2 &&
            encounter.MashType == 2 &&
            encounter.MonsterIds.SequenceEqual(["hag_B", "cauldron_empty_B"]));
        var reusedAfterModReorder = await managedBridgeService.EnsureEncounterAsync(
            managedProfile,
            managedSnapshot,
            reorderedManagedContent,
            reorderedManagedCatalog,
            reorderedReusableBoss,
            encounterGameRoot,
            workshopDirectory: null,
            managedLocalModRoot);
        var rejectedAppendAfterModReorder = false;
        try
        {
            var newEncounterAfterReorder = reorderedManagedCatalog.BridgeEncounters.Single(encounter =>
                encounter.SourceKind == BattleEncounterSourceKind.Additional &&
                encounter.MonsterIds.SequenceEqual(["thing_B"]));
            _ = await managedBridgeService.EnsureEncounterAsync(
                managedProfile,
                managedSnapshot,
                reorderedManagedContent,
                reorderedManagedCatalog,
                newEncounterAfterReorder,
                encounterGameRoot,
                workshopDirectory: null,
                managedLocalModRoot);
        }
        catch (InvalidOperationException ex) when (
            ex.Message.Contains("集合或顺序", StringComparison.Ordinal))
        {
            rejectedAppendAfterModReorder = true;
        }
        Assert(
            !reusedAfterModReorder.EncounterWasAdded &&
            !reusedAfterModReorder.ProfileConfigurationChanged &&
            reusedAfterModReorder.MashIndex == secondManagedResult.MashIndex &&
            rejectedAppendAfterModReorder &&
            ComputeSha256(reusedAfterModReorder.ManifestPath).Equals(
                managedManifestHashBeforeRunningGuard,
                StringComparison.OrdinalIgnoreCase),
            "A changed non-Bridge Mod order may not invalidate an already recorded stable encounter, but it must block every new append to that older table fingerprint.");

        File.AppendAllText(
            standardEncounterPath,
            "\nhall: .chance 1 .types changed_after_selection_B\n",
            new UTF8Encoding(false));
        var rejectedChangedEncounterTable = false;
        try
        {
            _ = await battleEditService.PreparePlaceBattleAsync(
                battleEditProfile,
                snapshotAfterBattlePlacement,
                "coAB",
                "tile1",
                directHallEncounters[0]);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("遭遇文件", StringComparison.Ordinal))
        {
            rejectedChangedEncounterTable = true;
        }
        Assert(
            rejectedChangedEncounterTable,
            "A changed effective encounter table must invalidate every previously resolved mash index before save preparation.");
        File.WriteAllText(
            Path.Combine(encounterDungeonRoot, "addon.cove.2.mash.darkest"),
            "hall: .chance 1 .types second_file_enemy_B\n",
            new UTF8Encoding(false));
        var ambiguousEncounterCatalog = BattleEncounterCatalog.Load(
            encounterContent,
            snapshotAfterBattlePlacement);
        Assert(
            ambiguousEncounterCatalog.DirectEncounters.Any(encounter => encounter.MashType == 0 && encounter.MashIndex == 1) &&
            ambiguousEncounterCatalog.Encounters.Single(encounter =>
                encounter.SourceKind == BattleEncounterSourceKind.Standard &&
                encounter.MonsterIds.SequenceEqual(["second_file_enemy_B"])) is { MashIndex: 0, CanPlaceDirectly: false } &&
            ambiguousEncounterCatalog.DirectEncounters.Any(encounter => encounter.MashType == 1) &&
            ambiguousEncounterCatalog.DirectEncounters.Any(encounter => encounter.MashType == 2),
            "Multi-file types must follow native path order, retain unresolved-monster slots, and leave independent room/boss indexes usable.");

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

        const string forceTownGameJson = """
    {
      "base_root": {
        "version": 17,
        "inraid": true,
        "raiddungeon": "cove",
        "raid_save": "keep-this-value",
        "game_mode": "base",
        "unrelated_state": { "sentinel": 73 }
      }
    }
    """;
        File.WriteAllText(battleGameFixturePath, forceTownGameJson, new UTF8Encoding(false));
        var forceTownService = new ForceTownSaveService(codec, battleEditLocations, () => false);
        var gameHashBeforeForceTown = ComputeSha256(battleGameFixturePath);
        var mapHashBeforeForceTown = ComputeSha256(battleMapFixturePath);
        var raidHashBeforeForceTown = ComputeSha256(battleRaidFixturePath);
        var forceTownExpectedDocument = JsonNode.Parse(forceTownGameJson)!.AsObject();
        forceTownExpectedDocument["base_root"]!["inraid"] = false;
        forceTownExpectedDocument["base_root"]!["raiddungeon"] = "none";
        var preparedForceTown = await forceTownService.PrepareAsync(
            battleEditProfile,
            snapshotAfterForwardCorridorMove);
        Assert(
            preparedForceTown.Preview.PreviousInRaid &&
            preparedForceTown.Preview.PreviousRaidDungeon == "cove" &&
            !preparedForceTown.Preview.ResultInRaid &&
            preparedForceTown.Preview.ResultRaidDungeon == "none" &&
            ComputeSha256(battleGameFixturePath).Equals(gameHashBeforeForceTown, StringComparison.OrdinalIgnoreCase),
            "Preparing force-town must describe the documented two-field edit without changing the live profile.");
        var rejectedWhileGameRunning = false;
        try
        {
            var runningGameForceTownService = new ForceTownSaveService(
                codec,
                battleEditLocations,
                () => true);
            _ = await runningGameForceTownService.CommitAsync(preparedForceTown);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("仍在运行", StringComparison.Ordinal))
        {
            rejectedWhileGameRunning = true;
        }
        Assert(
            rejectedWhileGameRunning &&
            ComputeSha256(battleGameFixturePath).Equals(gameHashBeforeForceTown, StringComparison.OrdinalIgnoreCase),
            "Force-town must refuse to write while the game is running.");
        var forceTownResult = await forceTownService.CommitAsync(preparedForceTown);
        var forceTownDocument = JsonNode.Parse(File.ReadAllText(battleGameFixturePath))!.AsObject();
        var forceTownManifest = JsonNode.Parse(File.ReadAllText(
            Path.Combine(forceTownResult.BackupDirectory, "backup-manifest.json")))!.AsObject();
        Assert(
            JsonNode.DeepEquals(forceTownExpectedDocument, forceTownDocument) &&
            forceTownDocument["base_root"]!["raid_save"]!.GetValue<string>() == "keep-this-value" &&
            forceTownManifest["operation"]!.GetValue<string>() == "force-town-save-edit" &&
            forceTownManifest["previousRaidDungeon"]!.GetValue<string>() == "cove" &&
            File.Exists(Path.Combine(forceTownResult.BackupDirectory, "persist.game.json")) &&
            File.Exists(Path.Combine(forceTownResult.BackupDirectory, "persist.map.json")) &&
            File.Exists(Path.Combine(forceTownResult.BackupDirectory, "persist.raid.json")) &&
            File.Exists(Path.Combine(forceTownResult.BackupDirectory, "persist.estate.json")) &&
            ComputeSha256(Path.Combine(forceTownResult.BackupDirectory, "persist.game.json"))
                .Equals(gameHashBeforeForceTown, StringComparison.OrdinalIgnoreCase) &&
            ComputeSha256(battleMapFixturePath).Equals(mapHashBeforeForceTown, StringComparison.OrdinalIgnoreCase) &&
            ComputeSha256(battleRaidFixturePath).Equals(raidHashBeforeForceTown, StringComparison.OrdinalIgnoreCase),
            "Force-town must back up the full profile and change only inraid/raiddungeon while leaving raid_save, map, and raid state untouched.");

        var rejectedAlreadyInTown = false;
        try
        {
            _ = await forceTownService.PrepareAsync(
                battleEditProfile,
                snapshotAfterForwardCorridorMove);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("已经是城镇", StringComparison.Ordinal))
        {
            rejectedAlreadyInTown = true;
        }
        Assert(rejectedAlreadyInTown, "Force-town must reject a profile whose next-load route is already town.");

        File.WriteAllText(battleGameFixturePath, forceTownGameJson, new UTF8Encoding(false));
        var snapshotBeforeForceTownRace = await new BattleMapSnapshotReader(codec).LoadAsync(battleMapProfileRoot);
        var preparedForceTownRace = await forceTownService.PrepareAsync(
            battleEditProfile,
            snapshotBeforeForceTownRace);
        var concurrentGameDocument = JsonNode.Parse(forceTownGameJson)!.AsObject();
        concurrentGameDocument["base_root"]!["unrelated_state"]!["sentinel"] = 999;
        var concurrentGameJson = concurrentGameDocument.ToJsonString();
        var raceForceTownService = new ForceTownSaveService(
            codec,
            battleEditLocations,
            () => false,
            targetPath =>
            {
                var concurrentPath = Path.Combine(
                    Path.GetDirectoryName(targetPath)!,
                    $"concurrent-game-{Guid.NewGuid():N}.tmp");
                File.WriteAllText(concurrentPath, concurrentGameJson, new UTF8Encoding(false));
                File.Replace(concurrentPath, targetPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            });
        var rejectedForceTownRace = false;
        try
        {
            _ = await raceForceTownService.CommitAsync(preparedForceTownRace);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("最终写入瞬间", StringComparison.Ordinal))
        {
            rejectedForceTownRace = true;
        }
        Assert(
            rejectedForceTownRace &&
            JsonNode.DeepEquals(
                concurrentGameDocument,
                JsonNode.Parse(File.ReadAllText(battleGameFixturePath))) &&
            ComputeSha256(battleMapFixturePath).Equals(mapHashBeforeForceTown, StringComparison.OrdinalIgnoreCase) &&
            ComputeSha256(battleRaidFixturePath).Equals(raidHashBeforeForceTown, StringComparison.OrdinalIgnoreCase),
            "Force-town must detect a last-moment atomic target replacement and restore that newer game save instead of overwriting it with the prepared edit or an older backup.");

        File.WriteAllText(battleGameFixturePath, forceTownGameJson, new UTF8Encoding(false));
        var snapshotBeforeStaleForceTown = await new BattleMapSnapshotReader(codec).LoadAsync(battleMapProfileRoot);
        var preparedStaleForceTown = await forceTownService.PrepareAsync(
            battleEditProfile,
            snapshotBeforeStaleForceTown);
        var raidChangedBeforeForceTownCommit = JsonNode.Parse(File.ReadAllText(battleRaidFixturePath))!.AsObject();
        raidChangedBeforeForceTownCommit["base_root"]!["torchlight"] = 71.0;
        File.WriteAllText(
            battleRaidFixturePath,
            raidChangedBeforeForceTownCommit.ToJsonString(),
            new UTF8Encoding(false));
        var rejectedStaleForceTown = false;
        try
        {
            _ = await forceTownService.CommitAsync(preparedStaleForceTown);
        }
        catch (InvalidOperationException)
        {
            rejectedStaleForceTown = true;
        }
        Assert(
            rejectedStaleForceTown &&
            ComputeSha256(battleGameFixturePath).Equals(
                preparedStaleForceTown.GameFile.OriginalSha256,
                StringComparison.OrdinalIgnoreCase),
            "Force-town must reject a changed game/map/raid guard set without touching persist.game.json.");

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
        var dsonBattleGamePath = Path.Combine(dsonBattleProfileRoot, "persist.game.json");
        await codec.EncodeAsync(battleMapFixturePath, dsonBattleMapPath, originalBinaryPath: null);
        await codec.EncodeAsync(battleRaidFixturePath, dsonBattleRaidPath, originalBinaryPath: null);
        await codec.EncodeAsync(battleGameFixturePath, dsonBattleGamePath, originalBinaryPath: null);
        File.WriteAllText(dsonBattleEstatePath, "{}", new UTF8Encoding(false));
        SetRevision(dsonBattleMapPath, [0x00, 0x00, 0x61, 0x42]);
        SetRevision(dsonBattleRaidPath, [0x00, 0x00, 0x61, 0x43]);
        SetRevision(dsonBattleGamePath, [0x00, 0x00, 0x61, 0x44]);
        var dsonBattleProfile = new SaveProfile(
            "profile_battle_dson",
            dsonBattleProfileRoot,
            dsonBattleEstatePath,
            "battle-user",
            File.GetLastWriteTimeUtc(dsonBattleEstatePath));
        var dsonBattleSnapshot = await new BattleMapSnapshotReader(codec).LoadAsync(dsonBattleProfileRoot);
        var dsonAttachmentContent = encounterContent with
        {
            Profile = dsonBattleProfile,
            WorkspaceDirectory = Path.Combine(runRoot, "battle-room-attachment-dson-catalog"),
            DecodedGamePath = dsonBattleGamePath,
            SourceGameSha256 = ComputeSha256(dsonBattleGamePath)
        };
        var dsonAttachmentCatalog = BattleRoomAttachmentCatalog.Load(dsonAttachmentContent);
        var dsonFishIdol = dsonAttachmentCatalog.Curios.Single(definition =>
            definition.Id == "fish_idol");
        var dsonRaidHashBeforeAttachment = ComputeSha256(dsonBattleRaidPath);
        var preparedDsonAttachment = await battleEditService.PrepareSetBattleAttachmentAsync(
            dsonBattleProfile,
            dsonBattleSnapshot,
            "rooC",
            "tile0",
            dsonFishIdol);
        Assert(
            preparedDsonAttachment.TargetFile.SourceWasDson &&
            ReadRevision(preparedDsonAttachment.TargetFile.SourceCopyPath)
                .SequenceEqual(ReadRevision(preparedDsonAttachment.TargetFile.EncodedPath)),
            "A guarded room attachment prepared from a DSON map must retain its revision bytes through encoding.");
        _ = await battleEditService.CommitAsync(preparedDsonAttachment);
        var dsonAttachmentDecodedPath = Path.Combine(runRoot, "battle-room-attachment-dson-after.json");
        await codec.DecodeAsync(dsonBattleMapPath, dsonAttachmentDecodedPath);
        var dsonAttachedDocument = JsonNode.Parse(File.ReadAllText(dsonAttachmentDecodedPath))!.AsObject();
        var dsonAttachedDynamicTile = dsonAttachedDocument["base_root"]!["map"]!["static_dynamic"]!["areas"]!["rooC"]!["tiles"]!["tile0"]!.AsObject();
        var dsonAttachedStaticTile = dsonAttachedDocument["base_root"]!["map"]!["static_dynamic"]!["static_save"]!["base_root"]!["areas"]!["rooC"]!["tiles"]!["tile0"]!.AsObject();
        Assert(
            dsonAttachedDynamicTile["content"]!.GetValue<int>() ==
                (int)BattleMapTileContent.GuardedCurio &&
            dsonAttachedDynamicTile["curio_prop"]!.GetValue<int>() == dsonFishIdol.PropHash &&
            dsonAttachedDynamicTile["mash_type"]!.GetValue<int>() == 1 &&
            dsonAttachedDynamicTile["mash_index"]!.GetValue<int>() == 4 &&
            dsonAttachedStaticTile["cur"]!.GetValue<int>() == dsonFishIdol.PropHash &&
            ReadRevision(dsonBattleMapPath).SequenceEqual(new byte[] { 0x00, 0x00, 0x61, 0x42 }) &&
            ComputeSha256(dsonBattleRaidPath).Equals(
                dsonRaidHashBeforeAttachment,
                StringComparison.OrdinalIgnoreCase),
            "A committed DSON room attachment must survive decode with matching static/dynamic hashes, the original mash and byte-identical raid state.");
        dsonBattleSnapshot = await new BattleMapSnapshotReader(codec).LoadAsync(dsonBattleProfileRoot);
        var dsonRaidHashBeforeDelete = ComputeSha256(dsonBattleRaidPath);
        var preparedDsonBattleDelete = await battleEditService.PrepareDeleteContentAsync(
            dsonBattleProfile,
            dsonBattleSnapshot,
            "rooC",
            "tile0");
        Assert(
            preparedDsonBattleDelete.TargetFile.SourceWasDson &&
            ReadRevision(preparedDsonBattleDelete.TargetFile.SourceCopyPath)
                .SequenceEqual(ReadRevision(preparedDsonBattleDelete.TargetFile.EncodedPath)),
            "A real DSON battle-map edit must preserve the source revision before commit.");
        _ = await battleEditService.CommitAsync(preparedDsonBattleDelete);
        var dsonBattleDecodedAfterPath = Path.Combine(runRoot, "battle-map-dson-after.json");
        await codec.DecodeAsync(dsonBattleMapPath, dsonBattleDecodedAfterPath);
        var dsonDeletedDocument = JsonNode.Parse(File.ReadAllText(dsonBattleDecodedAfterPath))!
            .AsObject();
        var dsonDeletedTile = dsonDeletedDocument
            ["base_root"]!["map"]!["static_dynamic"]!["areas"]!["rooC"]!["tiles"]!["tile0"]!.AsObject();
        var dsonDeletedStaticTile = dsonDeletedDocument
            ["base_root"]!["map"]!["static_dynamic"]!["static_save"]!["base_root"]!["areas"]!["rooC"]!["tiles"]!["tile0"]!.AsObject();
        Assert(
            dsonDeletedTile["content"]!.GetValue<int>() == 0 &&
            dsonDeletedTile["curio_prop"]!.GetValue<int>() == 0 &&
            dsonDeletedTile["trap"]!.GetValue<int>() == 0 &&
            dsonDeletedTile["mash_index"]!.GetValue<int>() == -1 &&
            dsonDeletedTile["mash_type"]!.GetValue<int>() == 7 &&
            dsonDeletedStaticTile["cur"]!.GetValue<int>() == 0 &&
            dsonDeletedStaticTile["obstacle"]!.GetValue<int>() == 0 &&
            ReadRevision(dsonBattleMapPath).SequenceEqual(new byte[] { 0x00, 0x00, 0x61, 0x42 }) &&
            ComputeSha256(dsonBattleRaidPath).Equals(dsonRaidHashBeforeDelete, StringComparison.OrdinalIgnoreCase),
            "A committed DSON hard deletion must clear static/dynamic resource and battle bindings, survive decode, retain revision bytes, and leave the paired raid save byte-identical.");

        var dsonSnapshotBeforeForceTown = await new BattleMapSnapshotReader(codec).LoadAsync(dsonBattleProfileRoot);
        var dsonMapHashBeforeForceTown = ComputeSha256(dsonBattleMapPath);
        var dsonRaidHashBeforeForceTown = ComputeSha256(dsonBattleRaidPath);
        var dsonForceTownService = new ForceTownSaveService(codec, battleEditLocations, () => false);
        var preparedDsonForceTown = await dsonForceTownService.PrepareAsync(
            dsonBattleProfile,
            dsonSnapshotBeforeForceTown);
        Assert(
            preparedDsonForceTown.GameFile.SourceWasDson &&
            ReadRevision(preparedDsonForceTown.GameFile.SourceCopyPath)
                .SequenceEqual(ReadRevision(preparedDsonForceTown.GameFile.EncodedPath)),
            "A prepared DSON force-town edit must preserve the source game-save revision.");
        _ = await dsonForceTownService.CommitAsync(preparedDsonForceTown);
        var dsonForceTownDecodedPath = Path.Combine(runRoot, "force-town-dson-after.json");
        await codec.DecodeAsync(dsonBattleGamePath, dsonForceTownDecodedPath);
        var dsonForceTownBaseRoot = JsonNode.Parse(File.ReadAllText(dsonForceTownDecodedPath))!
            ["base_root"]!.AsObject();
        Assert(
            !dsonForceTownBaseRoot["inraid"]!.GetValue<bool>() &&
            dsonForceTownBaseRoot["raiddungeon"]!.GetValue<string>() == "none" &&
            dsonForceTownBaseRoot["raid_save"]!.GetValue<string>() == "keep-this-value" &&
            ReadRevision(dsonBattleGamePath).SequenceEqual(new byte[] { 0x00, 0x00, 0x61, 0x44 }) &&
            ComputeSha256(dsonBattleMapPath).Equals(dsonMapHashBeforeForceTown, StringComparison.OrdinalIgnoreCase) &&
            ComputeSha256(dsonBattleRaidPath).Equals(dsonRaidHashBeforeForceTown, StringComparison.OrdinalIgnoreCase),
            "A committed DSON force-town edit must survive decode, retain revision bytes, and leave map/raid files byte-identical.");

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

    private static void CreateBattleMonsterDefinitions(
        string contentRoot,
        IEnumerable<string> monsterIds,
        IEnumerable<string>? bossMonsterIds = null)
    {
        var bossIds = (bossMonsterIds ?? [])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var monsterId in monsterIds)
        {
            var monsterDirectory = Path.Combine(contentRoot, "monsters", monsterId);
            Directory.CreateDirectory(monsterDirectory);
            var bossTag = bossIds.Contains(monsterId)
                ? "tag: .id \"boss\"\n"
                : string.Empty;
            File.WriteAllText(
                Path.Combine(monsterDirectory, $"{monsterId}.info.darkest"),
                $"display: .size 1\nmonster: .id {monsterId}\n{bossTag}",
                new UTF8Encoding(false));
        }
    }
}
