internal static partial class ContractSuite
{
    private static async Task RunBattleMapContentContractsAsync(
        string runRoot, DsonSaveCodec codec, ActiveContentSnapshot template)
    {
        var root = Path.Combine(runRoot, "battle-standalone-content");
        var profileRoot = Path.Combine(root, "profile");
        Directory.CreateDirectory(profileRoot);
        foreach (var path in Directory.EnumerateFiles(template.Profile.ProfileDirectory, "persist*.json"))
        {
            File.Copy(path, Path.Combine(profileRoot, Path.GetFileName(path)));
        }
        var profile = template.Profile with
        {
            ProfileDirectory = profileRoot,
            EstateSavePath = Path.Combine(profileRoot, "persist.estate.json")
        };
        var gamePath = Path.Combine(profileRoot, "persist.game.json");
        var mapPath = Path.Combine(profileRoot, "persist.map.json");
        var raidPath = Path.Combine(profileRoot, "persist.raid.json");
        var baseRoot = Path.Combine(root, "game");
        var modRoot = Path.Combine(root, "mod");
        var dlcRoot = Path.Combine(baseRoot, "dlc", "enabled");
        Directory.CreateDirectory(dlcRoot);
        var poolPath = WriteMapContentFixture(baseRoot, "dungeons/cove/cove.props.darkest", """
            room_curios: .chance 0 .types fish_idol shared_curio missing_prop_curio missing_type_curio ambiguous_curio CASE_ONLY_CURIO case_type_curio case_twin CASE_TWIN
            room_treasures: .chance 1 .types unlocked_strongbox
            hall_curios: .chance 1 .types shared_curio crate valid_after_malformed
            traps: .chance 1 .types lurker shared_trap missing_trap scripted_trap cycle_one ambiguous_trap ignored_json_trap CASE_ONLY_TRAP " spaced_trap "
            obstacles: .chance 1 .types shipwreck
            secret_room_treasures: .chance 1 .types secret_stash
            prison_doors: .chance 1 .types cove_door
            """);
        WriteMapContentFixture(baseRoot, "dungeons/weald/weald.props.darkest", """
            room_curios: .chance 1 .types foreign_room_curio
            hall_curios: .chance 1 .types foreign_hall_curio
            traps: .chance 1 .types poison_cloud shared_trap
            obstacles: .chance 1 .types thorny_thicket
            """);
        var curioPaths = WriteMapCurioFixtures(baseRoot, "base",
            "fish_idol", "shared_curio", "unlocked_strongbox", "crate", "foreign_room_curio", "foreign_hall_curio",
            "case_only_curio", "case_twin", "CASE_TWIN");
        File.WriteAllText(curioPaths.Props, File.ReadAllText(curioPaths.Props)
            .Replace("foreign_room_curio,foreign_room_curio,foreign_room_curio,foreign_room_curio,foreign_room_curio",
                "foreign_room_curio,foreign_room_curio,foreign_behavior,foreign_display,foreign_room_curio", StringComparison.Ordinal) +
            "missing_type_curio,sprite,no_such_type,name,audio\n" +
            "case_type_curio,sprite,Fish_Idol,name,audio\n" +
            "valid_after_malformed,sprite,valid_after_malformed,name,audio\n" +
            "ambiguous_curio,sprite,crate,name,audio\nambiguous_curio,sprite,fish_idol,name,audio\n");
        File.WriteAllText(curioPaths.Types, File.ReadAllText(curioPaths.Types)
            .Replace(",,foreign_room_curio,,Nothing", ",,\"foreign_behavior\",,Nothing", StringComparison.Ordinal) +
            ",1,Quote toggles\n,,ID STRING,,RESULT TYPES\n,,\"bad\"suffix,,Nothing\n,,,,ITEM\n" +
            ",2,Physical lines\n,,ID STRING,,RESULT TYPES\n,,valid_after_malformed,,Nothing,,,,,,,,,,,,\"a quoted, multiline\ncomment\"\n,,,,ITEM\n");
        WriteMapContentFixture(baseRoot, "localization/curios.string_table.xml", """
            <root><language id="schinese"><entry id="str_curio_title_foreign_display">异地奇物</entry></language>
            <language id="english"><entry id="str_curio_title_foreign_display">Foreign Curio</entry></language></root>
            """);
        var resourcePath = WriteMapContentFixture(baseRoot, "props/trap_definitions.json", """
            { "props": [
              { "name": "lurker", "default_data": { "inherits_from": { "prop_type_name": "trap" } } },
              { "name": " spaced_trap ", "default_data": { "inherits_from": { "prop_type_name": "trap" } } },
              { "name": "shared_trap", "default_data": { "inherits_from": { "prop_type_name": "trap" } } },
              { "name": "poison_cloud", "default_data": { "inherits_from": { "prop_type_name": "trap" } } },
              { "name": "case_only_trap", "default_data": { "inherits_from": { "prop_type_name": "trap" } } },
              { "name": "scripted_trap", "default_data": { "inherits_from": { "prop_type_name": "trap" }, "generate_ambush": "trap" } },
              { "name": "cycle_one", "default_data": { "inherits_from": { "prop_type_name": "cycle_two" } } },
              { "name": "cycle_two", "default_data": { "inherits_from": { "prop_type_name": "cycle_one" } } },
              { "name": "ambiguous_trap", "default_data": { "inherits_from": { "prop_type_name": "trap" }, "value": 1 } },
              { "name": "ambiguous_trap", "default_data": { "inherits_from": { "prop_type_name": "trap" }, "value": 2 } }
            ] }
            """);
        WriteMapContentFixture(baseRoot, "props/prop_definitions.json", """
            { "props": [ { "name": "trap", "default_data": { "instance_type": "trap" } }, { "name": "obstacle", "default_data": { "instance_type": "obstacle", "teleport": false } } ] }
            """);
        WriteMapContentFixture(baseRoot, "props/obstacle_definitions.json", """
            { "props": [
              { "name": "shipwreck", "default_data": { "inherits_from": { "prop_type_name": "obstacle" } } },
              { "name": "thorny_thicket", "default_data": { "inherits_from": { "prop_type_name": "obstacle" } } }
            ] }
            """);
        WriteMapContentFixture(baseRoot, "props/unused_data.json", """
            { "props": [ { "name": "ignored_json_trap", "default_data": { "inherits_from": { "prop_type_name": "trap" } } } ] }
            """);
        // A canonical override supplies the complete pool; an extra basename cannot extend it.
        poolPath = WriteMapContentFixture(modRoot, "dlc/enabled/dungeons/cove/cove.props.darkest", File.ReadAllText(poolPath) + "\n" + """
            hall_curios: .chance 1 .types enabled_dlc_curio
            traps: .chance 1 .types dlc_trap
            """);
        WriteMapContentFixture(modRoot, "dlc/enabled/props/cove/trap_definitions.json", """
            { "props": [ { "name": "dlc_trap", "default_data": { "inherits_from": { "prop_type_name": "trap" } } } ] }
            """);
        var dlcCurioPaths = WriteMapCurioFixtures(Path.Combine(modRoot, "dlc/enabled"), "enabled", "enabled_dlc_curio");
        // Native COM's miller-wife definition leaves the ID row's result-type cell empty.
        File.WriteAllText(dlcCurioPaths.Types, File.ReadAllText(dlcCurioPaths.Types)
            .Replace(",,enabled_dlc_curio,,Nothing", ",,enabled_dlc_curio,,", StringComparison.Ordinal));
        WriteMapCurioFixtures(Path.Combine(modRoot, "dlc/disabled"), "disabled", "disabled_dlc_curio");
        WriteMapCurioFixtures(modRoot, "unlisted", "unlisted_curio");
        WriteMapContentFixture(modRoot, "dlc/disabled/dungeons/cove/cove.props.darkest",
            "hall_curios: .chance 1 .types disabled_dlc_curio");
        WriteMapContentFixture(modRoot, "dungeons/cove/residue.props.darkest",
            "hall_curios: .chance 1 .types unlisted_curio");
        var content = template with
        {
            Profile = profile,
            Sources = [
                new ActiveContentSource("base", "Base", "base", baseRoot, 0),
                new ActiveContentSource("dlc:enabled", "Enabled DLC", "dlc", dlcRoot, 1)
                { VirtualPathPrefix = "dlc/enabled" },
                new ActiveContentSource("local:props", "Prop Mod", "local", modRoot, 1000)
            ],
            DecodedGamePath = gamePath,
            SourceGameSha256 = ComputeSha256(gamePath),
            WorkspaceDirectory = Path.Combine(root, "catalog")
        };
        var missingRejected = false;
        try { BattleRoomAttachmentCatalog.Load(content); }
        catch (InvalidDataException error) when (error.Message.Contains("modfiles.txt", StringComparison.Ordinal)) { missingRejected = true; }
        Assert(missingRejected, "Missing manifests must prevent room attachment discovery before preparation.");
        WriteMapContentFixture(modRoot, "modfiles.txt", """
            dlc/enabled/dungeons/cove/cove.props.darkest 1
            dlc/enabled/props/cove/trap_definitions.json 1
            dlc/enabled/curios/enabled_curio_props.csv 1
            dlc/enabled/curios/enabled_curio_type_library.csv 1
            dlc/disabled/dungeons/cove/cove.props.darkest 1
            dlc/disabled/curios/disabled_curio_props.csv 1
            dlc/disabled/curios/disabled_curio_type_library.csv 1
            """);
        var catalog = BattleRoomAttachmentCatalog.Load(content);
        Assert(catalog.HallCurios.Any(item => item.Id == "enabled_dlc_curio") &&
               catalog.Definitions.All(item => item.Id is not ("unlisted_curio" or "disabled_dlc_curio")) &&
               catalog.Definitions.Count(item => item.Id == "shared_curio") == 2 &&
               catalog.Traps.Count(item => item.Id == "shared_trap") == 2 &&
               catalog.Definitions.All(item => item.Id is not ("secret_stash" or "cove_door")) &&
               catalog.HallCurios.Single(item => item.Id == "foreign_hall_curio").ChineseName == "—" &&
               catalog.Curios.Single(item => item.Id == "foreign_room_curio").LocalizedName ==
                   new BilingualContentName("异地奇物", "Foreign Curio"),
            "Manifest authority, room/hall membership, per-region trap membership and genuine missing translations must remain distinct.");
        Assert(catalog.GetCandidates(BattleRoomAttachmentKind.Trap, "cove")
                   .Select(item => item.Id).ToHashSet().SetEquals(["lurker", "shared_trap", "dlc_trap", " spaced_trap ", "ambiguous_trap"]) &&
               catalog.GetCandidates(BattleRoomAttachmentKind.Obstacle, "cove").Single().Id == "shipwreck" &&
               catalog.GetCandidates(BattleRoomAttachmentKind.Trap, "").Count == 0 &&
               catalog.Issues.Count(issue => issue.StartsWith("地图内容候选未纳入：", StringComparison.Ordinal)) == 9 &&
               catalog.Definitions.All(item => item.Id is not ("missing_prop_curio" or "missing_type_curio" or
                   "CASE_ONLY_CURIO" or "case_type_curio" or "CASE_ONLY_TRAP")) &&
               catalog.Curios.Count(item => item.Id is "case_twin" or "CASE_TWIN") == 2 &&
               catalog.HallCurios.Any(item => item.Id == "valid_after_malformed") &&
               catalog.Curios.Any(item => item.Id == "ambiguous_curio"),
            "Missing types/resources and scripted or unresolved inheritance stay unavailable; repeated JSON defaults and CSV updates are defined, and native quote toggles are not RFC CSV errors.");

        var service = new BattleMapEditService(codec, new SaveEditorLocations(
            root, Path.Combine(root, "edits"), Path.Combine(root, "backups")));
        var reader = new BattleMapSnapshotReader(codec);
        var roomCurio = catalog.Curios.Single(item => item.Id == "foreign_room_curio");
        var hallCurio = catalog.HallCurios.Single(item => item.Id == "foreign_hall_curio");
        var treasure = catalog.Treasures.Single();
        var trap = catalog.Traps.Single(item => item.Id == "lurker");
        var spacedTrap = catalog.Traps.Single(item => item.Id == " spaced_trap ");
        Assert(spacedTrap.PropHash == unchecked((int)Loc2LocalizationReader.HashName(" spaced_trap ")) &&
               spacedTrap.PropHash != unchecked((int)Loc2LocalizationReader.HashName("spaced_trap")),
            "Map writes must preserve the authored trap ID without trimming its native hash.");
        var obstacle = catalog.Obstacles.Single(item => item.Id == "shipwreck");
        var originalMap = File.ReadAllText(mapPath);
        var originalRaid = File.ReadAllText(raidPath);
        var untouchedFiles = Directory.EnumerateFiles(profileRoot, "persist*.json")
            .Where(path => !path.Equals(mapPath, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(path => path, ComputeSha256);

        // Exercise both a battle replacement and a completed/empty tile with old scenery.
        foreach (var definition in new[] { roomCurio, treasure, hallCurio, trap, obstacle, hallCurio, spacedTrap })
        {
            var areaId = definition.TargetAreaKind == BattleMapAreaKind.Room ? "rooC" : "coAB";
            var tileId = areaId == "rooC" ? "tile0" : "tile2";
            var before = JsonNode.Parse(originalMap)!.AsObject();
            var staticTile = MapContentStaticTile(before, areaId, tileId);
            var dynamicTile = MapContentDynamicTile(before, areaId, tileId);
            staticTile["cur"] = 987;
            staticTile["obstacle"] = 654;
            dynamicTile["curio_prop"] = 987;
            dynamicTile["trap"] = 321;
            dynamicTile["content"] = areaId == "rooC" ? 10 : 0;
            dynamicTile["mash_type"] = 1;
            dynamicTile["mash_index"] = 4;
            dynamicTile["knowledge"] = 3;
            dynamicTile["crit_scout"] = true;
            File.WriteAllText(mapPath, before.ToJsonString(), new UTF8Encoding(false));
            var beforeHash = ComputeSha256(mapPath);
            var snapshot = await reader.LoadAsync(profileRoot);
            var prepared = await service.PreparePlaceContentAsync(profile, snapshot, areaId, tileId, definition);
            Assert(ComputeSha256(mapPath) == beforeHash &&
                   prepared.Preview.Kind == BattleMapEditKind.PlaceContent &&
                   prepared.TargetFile.FileName == "persist.map.json",
                "Standalone preparation must be map-only and must not touch the live fixture before confirmation.");
            var result = await service.CommitAsync(prepared);
            var after = JsonNode.Parse(File.ReadAllText(mapPath))!.AsObject();
            var expected = before.DeepClone().AsObject();
            var expectedStatic = MapContentStaticTile(expected, areaId, tileId);
            var expectedDynamic = MapContentDynamicTile(expected, areaId, tileId);
            var isCurio = definition.Kind is BattleRoomAttachmentKind.Curio or
                BattleRoomAttachmentKind.Treasure or BattleRoomAttachmentKind.HallCurio;
            expectedStatic["cur"] = isCurio ? definition.PropHash : 0;
            expectedStatic["obstacle"] = definition.Kind == BattleRoomAttachmentKind.Obstacle ? definition.PropHash : 0;
            expectedDynamic["curio_prop"] = isCurio ? definition.PropHash : 0;
            expectedDynamic["trap"] = definition.Kind == BattleRoomAttachmentKind.Trap ? definition.PropHash : 0;
            expectedDynamic["content"] = (int)definition.StandaloneContent;
            expectedDynamic["mash_index"] = -1;
            expectedDynamic["mash_type"] = 7;
            Assert(JsonNode.DeepEquals(expected, after) &&
                   untouchedFiles.All(pair => ComputeSha256(pair.Key) == pair.Value) &&
                   ComputeSha256(Path.Combine(result.BackupDirectory, "persist.map.json")) == beforeHash &&
                   untouchedFiles.All(pair => File.Exists(Path.Combine(result.BackupDirectory, Path.GetFileName(pair.Key)))),
                $"{definition.Kind} placement must replace exactly its bindings, back up the whole profile, retain exploration/topology and leave raid/game/estate byte-identical.");
        }

        async Task RejectPrepare(string areaId, string tileId, BattleRoomAttachmentDefinition definition, string reason)
        {
            var beforeHash = ComputeSha256(mapPath);
            var snapshot = await reader.LoadAsync(profileRoot);
            await AssertMapContentRejectedAsync(
                () => service.PreparePlaceContentAsync(profile, snapshot, areaId, tileId, definition), reason);
            Assert(ComputeSha256(mapPath) == beforeHash, "Rejected standalone preparation must leave the map unchanged.");
        }
        await RejectPrepare("coAB", "tile2", roomCurio, "房间和走廊");
        await RejectPrepare("rooC", "tile0", trap, "房间和走廊");
        await RejectPrepare("coAB", "tile2", catalog.Traps.Single(item => item.Id == "poison_cloud"), "当前副本区域");
        await RejectPrepare("coAB", "tile2", catalog.Obstacles.Single(item => item.Id == "thorny_thicket"), "当前副本区域");
        await RejectPrepare("rooA", "tile0", roomCurio, "出生房间");
        await RejectPrepare("rooB", "tile0", roomCurio, "最终房间");
        await RejectPrepare("coAB", "tile0", hallCurio, "普通可见走廊格");
        await RejectPrepare("rooC", "tile0", roomCurio with { PropHash = 123 }, "哈希");
        await RejectPrepare("coAB", "tile2", trap with
        { SourceRelativePath = "dungeons/weald/cove.props.darkest" }, "已变化");
        foreach (var contentValue in new[] { 2, 5, 8, 11, 12, 13, 99 })
        {
            var blockedMap = JsonNode.Parse(originalMap)!.AsObject();
            MapContentDynamicTile(blockedMap, "coAB", "tile2")["content"] = contentValue;
            File.WriteAllText(mapPath, blockedMap.ToJsonString(), new UTF8Encoding(false));
            await RejectPrepare("coAB", "tile2", hallCurio, "系统或脚本内容");
        }
        File.WriteAllText(mapPath, originalMap, new UTF8Encoding(false));
        foreach (var mutate in new Action<JsonObject>[]
        {
            raid => raid["inbattle"] = true,
            raid => raid["camp"]!["phase"] = 1,
            raid => raid["loot"]!["queue"]!["pending"] = 1,
            raid => raid["in_doorway"]!["implied"] = false,
            raid => raid["teleported"] = true,
            raid => raid["wave_logic"] = new JsonObject { ["active"] = true }
        })
        {
            var blockedRaid = JsonNode.Parse(originalRaid)!.AsObject();
            mutate(blockedRaid["base_root"]!.AsObject());
            File.WriteAllText(raidPath, blockedRaid.ToJsonString(), new UTF8Encoding(false));
            await RejectPrepare("rooC", "tile0", roomCurio, "不能修改地图");
        }
        File.WriteAllText(raidPath, originalRaid, new UTF8Encoding(false));

        // Invalidate after preparation: either member of the save pair, Mod config,
        // pool text, resource definitions, and newly discovered resource files.
        foreach (var path in new[] { mapPath, raidPath, gamePath, poolPath, resourcePath, curioPaths.Props, curioPaths.Types,
                     dlcCurioPaths.Props, dlcCurioPaths.Types })
        {
            var snapshot = await reader.LoadAsync(profileRoot);
            var prepared = await service.PreparePlaceContentAsync(profile, snapshot, "coAB", "tile2", trap);
            var previous = File.ReadAllText(path);
            File.AppendAllText(path, "\n", new UTF8Encoding(false));
            var changedMapHash = ComputeSha256(mapPath);
            await AssertMapContentRejectedAsync(() => service.CommitAsync(prepared), "变化");
            if (path.EndsWith(".csv", StringComparison.Ordinal))
            {
                await AssertMapContentRejectedAsync(() => service.PreparePlaceContentAsync(
                    profile, snapshot, "rooC", "tile0", roomCurio), "变化");
            }
            Assert(ComputeSha256(mapPath) == changedMapHash, "A stale save or catalog must reject before replacing the map.");
            File.WriteAllText(path, previous, new UTF8Encoding(false));
        }
        foreach (var path in new[] { curioPaths.Props, curioPaths.Types, dlcCurioPaths.Props, dlcCurioPaths.Types })
        {
            var snapshot = await reader.LoadAsync(profileRoot);
            var prepared = await service.PreparePlaceContentAsync(profile, snapshot, "rooC", "tile0", roomCurio);
            var previous = File.ReadAllText(path);
            File.Delete(path);
            await AssertMapContentRejectedAsync(() => service.PreparePlaceContentAsync(
                profile, snapshot, "rooC", "tile0", roomCurio), "变化");
            await AssertMapContentRejectedAsync(() => service.CommitAsync(prepared), "变化");
            Assert(ComputeSha256(mapPath).Equals(snapshot.MapSha256, StringComparison.OrdinalIgnoreCase),
                "Deleting any effective curio CSV must invalidate both prepare and commit without changing the save.");
            File.WriteAllText(path, previous, new UTF8Encoding(false));
        }
        var beforeNewResource = await reader.LoadAsync(profileRoot);
        var preparedBeforeNewResource = await service.PreparePlaceContentAsync(profile, beforeNewResource, "coAB", "tile2", trap);
        WriteMapContentFixture(baseRoot, "props/new/trap_definitions.json", "{\"props\":[]}");
        await AssertMapContentRejectedAsync(() => service.CommitAsync(preparedBeforeNewResource), "变化");
        catalog = BattleRoomAttachmentCatalog.Load(content);
        var beforeNewCurio = await service.PreparePlaceContentAsync(profile, await reader.LoadAsync(profileRoot),
            "rooC", "tile0", catalog.Curios.Single(item => item.Id == "foreign_room_curio"));
        WriteMapCurioFixtures(baseRoot, "new", "new_curio");
        await AssertMapContentRejectedAsync(() => service.CommitAsync(beforeNewCurio), "变化");
        catalog = BattleRoomAttachmentCatalog.Load(content);
        trap = catalog.Traps.Single(item => item.Id == "lurker");
        var finalSnapshot = await reader.LoadAsync(profileRoot);
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            var cancelled = false;
            try
            {
                _ = await service.PreparePlaceContentAsync(profile, finalSnapshot, "coAB", "tile2", trap, cancellation.Token);
            }
            catch (OperationCanceledException) { cancelled = true; }
            Assert(cancelled && ComputeSha256(mapPath).Equals(finalSnapshot.MapSha256, StringComparison.OrdinalIgnoreCase),
                "Cancellation before standalone preparation must not change the map.");
        }

        // Verify the same placement through the binary codec, retaining per-file revision bytes.
        var binaryRoot = Path.Combine(root, "binary-profile");
        Directory.CreateDirectory(binaryRoot);
        foreach (var path in Directory.EnumerateFiles(profileRoot, "persist*.json"))
        {
            await codec.EncodeAsync(path, Path.Combine(binaryRoot, Path.GetFileName(path)), originalBinaryPath: null);
        }
        var binaryMap = Path.Combine(binaryRoot, "persist.map.json");
        var binaryRaid = Path.Combine(binaryRoot, "persist.raid.json");
        var binaryGame = Path.Combine(binaryRoot, "persist.game.json");
        SetRevision(binaryMap, [0x00, 0x00, 0x61, 0x45]);
        var binaryProfile = profile with
        {
            ProfileDirectory = binaryRoot,
            EstateSavePath = Path.Combine(binaryRoot, "persist.estate.json")
        };
        var binaryCatalog = BattleRoomAttachmentCatalog.Load(content with
        {
            Profile = binaryProfile,
            SourceGameSha256 = ComputeSha256(binaryGame),
            DecodedGamePath = binaryGame
        });
        var binaryBefore = await reader.LoadAsync(binaryRoot);
        var binaryPrepared = await service.PreparePlaceContentAsync(binaryProfile, binaryBefore, "rooC", "tile0",
            binaryCatalog.Treasures.Single());
        _ = await service.CommitAsync(binaryPrepared);
        var binaryAfter = await reader.LoadAsync(binaryRoot);
        var binaryTile = binaryAfter.Areas.Single(area => area.AreaId == "rooC").Tiles.Single();
        Assert(binaryPrepared.TargetFile.SourceWasDson && binaryTile.RawContent == 9 &&
               binaryTile.CurioPropHash == -1086187210 && binaryTile.StaticCurioHash == -1086187210 &&
               binaryTile.MashIndex == -1 && binaryTile.MashType == 7 &&
               ReadRevision(binaryMap).SequenceEqual(new byte[] { 0x00, 0x00, 0x61, 0x45 }) &&
               ComputeSha256(binaryRaid).Equals(binaryBefore.RaidSha256, StringComparison.OrdinalIgnoreCase),
            "Standalone treasure must round-trip signed hashes through DSON, preserve revision and paired raid bytes, and clear battle bindings.");
        var binaryTrap = binaryCatalog.Traps.Single(item => item.Id == " spaced_trap ");
        var binaryTrapPrepared = await service.PreparePlaceContentAsync(binaryProfile, binaryAfter, "coAB", "tile2", binaryTrap);
        _ = await service.CommitAsync(binaryTrapPrepared);
        var binaryTrapAfter = await reader.LoadAsync(binaryRoot);
        var binaryTrapTile = binaryTrapAfter.Areas.Single(area => area.AreaId == "coAB").Tiles.Single(tile => tile.TileId == "tile2");
        Assert(binaryTrapPrepared.TargetFile.SourceWasDson && binaryTrapTile.TrapHash ==
                   unchecked((int)Loc2LocalizationReader.HashName(" spaced_trap ")) &&
               binaryTrapTile.RawContent == 3 && binaryTrapTile.MashIndex == -1 && binaryTrapTile.MashType == 7 &&
               ReadRevision(binaryMap).SequenceEqual(new byte[] { 0x00, 0x00, 0x61, 0x45 }) &&
               ComputeSha256(binaryRaid).Equals(binaryBefore.RaidSha256, StringComparison.OrdinalIgnoreCase),
            "Spaced trap identities must round-trip through DSON without trimming or changing paired raid bytes.");
        RunRegionalMapContentContracts(root, content);
        RunMapPropNativeContracts(root, content);
        RunMapResourceConsumerContracts(root, content);
        Console.WriteLine("Battle standalone content contracts passed.");
    }

    private static string WriteMapContentFixture(string root, string relativePath, string text)
    {
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text, new UTF8Encoding(false));
        return path;
    }

    private static (string Props, string Types) WriteMapCurioFixtures(string root, string prefix, params string[] ids) =>
        (WriteMapContentFixture(root, $"curios/{prefix}_curio_props.csv",
            "Curio Prop Name,Sprite Reference,Curio Type,UI String Name,Audio Event Name\n" +
            string.Concat(ids.Select(id => $"{id},{id},{id},{id},{id}\n"))),
         WriteMapContentFixture(root, $"curios/{prefix}_curio_type_library.csv",
            string.Concat(ids.Select((id, index) => $",{index + 1},Curio\n,,ID STRING,,RESULT TYPES\n,,{id},,Nothing\n,,,,ITEM\n"))));

    private static JsonObject MapContentStaticTile(JsonObject map, string areaId, string tileId) =>
        map["base_root"]!["map"]!["static_dynamic"]!["static_save"]!["base_root"]!["areas"]![areaId]!["tiles"]![tileId]!.AsObject();

    private static JsonObject MapContentDynamicTile(JsonObject map, string areaId, string tileId) =>
        map["base_root"]!["map"]!["static_dynamic"]!["areas"]![areaId]!["tiles"]![tileId]!.AsObject();

    private static async Task AssertMapContentRejectedAsync(Func<Task> action, string reason)
    {
        var rejected = false;
        try { await action(); }
        catch (InvalidOperationException ex) when (ex.Message.Contains(reason, StringComparison.Ordinal)) { rejected = true; }
        Assert(rejected, $"Map content operation should have been rejected with: {reason}");
    }
}
