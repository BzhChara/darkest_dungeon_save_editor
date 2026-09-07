internal static partial class ContractSuite
{
    private static SaveProfile WriteBattleSafetyProfile(string root)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "persist.estate.json"), "{}");
        File.WriteAllText(Path.Combine(root, "persist.game.json"),
            """{"base_root":{"inraid":true,"raiddungeon":"cove","game_mode":"base","applied_ugcs_1_0":{}}}""");
        File.WriteAllText(Path.Combine(root, "persist.map.json"), """
            {"base_root":{"map":{"bounds":[0.0,6.0,0.0,0.0],"entrance_id":100,"final_room_id":200,"static_dynamic":{
              "static_save":{"base_root":{"areas":{
                "rooA":{"id":100,"kind":0,"tiles":{"tile0":{"type":3,"mappos":[0.0,0.0]}}},
                "rooB":{"id":200,"kind":0,"tiles":{"tile0":{"type":3,"mappos":[4.0,0.0]}}},
                "rooC":{"id":250,"kind":0,"tiles":{"tile0":{"type":3,"cur":987,"mappos":[6.0,0.0]}}},
                "coAB":{"id":150,"kind":1,"tiles":{
                  "tile0":{"type":2,"mappos":[1.0,0.0],"door_to":{"area_to":100,"tile_to":0,"implied":false,"type":0}},
                  "tile1":{"type":1,"mappos":[2.0,0.0]},
                  "tile2":{"type":2,"mappos":[3.0,0.0],"door_to":{"area_to":200,"tile_to":0,"implied":false,"type":0}}
                }}
              }}},
              "areas":{
                "rooA":{"knowledge":3,"reversed":false,"tiles":{"tile0":{"content":0,"knowledge":3,"mash_index":-1,"mash_type":7}}},
                "rooB":{"knowledge":2,"reversed":false,"tiles":{"tile0":{"content":0,"knowledge":2,"mash_index":-1,"mash_type":7}}},
                "rooC":{"knowledge":2,"reversed":false,"tiles":{"tile0":{"content":10,"knowledge":2,"curio_prop":987,"mash_index":0,"mash_type":1}}},
                "coAB":{"knowledge":2,"reversed":false,"tiles":{
                  "tile0":{"content":0,"knowledge":2,"mash_index":-1,"mash_type":7},
                  "tile1":{"content":3,"knowledge":2,"trap":321,"mash_index":-1,"mash_type":7},
                  "tile2":{"content":0,"knowledge":2,"mash_index":-1,"mash_type":7}
                }}
              }
            }}}}
            """);
        File.WriteAllText(Path.Combine(root, "persist.raid.json"), """
            {"base_root":{"raid_instance":{"dungeon":"cove","difficulty":2,"length":3},
              "in_area":100,"areatile":0,"last_room_id":100,"teleported":false,"inbattle":false,"camp":{"phase":0},
              "in_doorway":{"area_to":1701736302,"tile_to":0,"implied":true},
              "loot":{"queue":{},"queue_items":{"items":{}},"owned_items":{"items":{}}},
              "party":{"IsMovingLeft()":false,"retreat_room":100,"heroes":[]}}}
            """);
        return new SaveProfile(Path.GetFileName(root), root, Path.Combine(root, "persist.estate.json"), "contract-user", DateTime.UtcNow);
    }

    private static async Task RunBattleMapWriteSafetyContractsAsync(string runRoot, DsonSaveCodec codec)
    {
        var root = Path.Combine(runRoot, "battle-write-safety");
        var profile = WriteBattleSafetyProfile(Path.Combine(root, "profile"));
        var locations = new SaveEditorLocations(root, Path.Combine(root, "workspaces"), Path.Combine(root, "backups"));
        var service = new BattleMapEditService(codec, locations);
        var snapshot = await new BattleMapSnapshotReader(codec).LoadAsync(profile.ProfileDirectory);
        var gamePath = Path.Combine(profile.ProfileDirectory, "persist.game.json");
        var originalGame = File.ReadAllBytes(gamePath);
        var mapHash = ComputeSha256(snapshot.MapSavePath);
        var raidHash = ComputeSha256(snapshot.RaidSavePath);
        Func<Task<PreparedBattleMapEdit>>[] preparations = [
            () => service.PrepareDeleteContentAsync(profile, snapshot, "coAB", "tile1"),
            () => service.PrepareMovePartyAsync(profile, snapshot, "rooB", "tile0"),
            () => service.PrepareRemoveBattleAttachmentAsync(profile, snapshot, "rooC", "tile0")
        ];
        foreach (var prepare in preparations)
        {
            var prepared = await prepare();
            File.WriteAllText(gamePath, """{"base_root":{"inraid":false,"raiddungeon":"none"}}""");
            var error = await CaptureSaveFailureAsync(() => service.CommitAsync(prepared));
            Assert(error.Message.Contains("变化", StringComparison.Ordinal) &&
                   ComputeSha256(snapshot.MapSavePath) == mapHash && ComputeSha256(snapshot.RaidSavePath) == raidHash &&
                   !Directory.Exists(locations.BackupDirectory),
                "Every map operation must reject a town transition even when map and raid bytes remain unchanged.");
            File.WriteAllBytes(gamePath, originalGame);
        }
        foreach (var state in new[] {
            """{"base_root":{"inraid":false,"raiddungeon":"none"}}""",
            """{"base_root":{"inraid":true,"raiddungeon":"ruins"}}""",
            """{"base_root":{"game_mode":"base"}}""" })
        {
            File.WriteAllText(gamePath, state);
            foreach (var prepare in preparations)
                _ = await CaptureSaveFailureAsync(async () => { _ = await prepare(); });
        }
        File.WriteAllBytes(gamePath, originalGame);
        var raceEdit = await preparations[0]();
        await VerifyServiceReplacementRacesAsync(raceEdit.TargetFile.TargetPath, async (before, after) =>
        {
            service.BeforeTargetReplace = before;
            service.AfterTargetReplace = after;
            try { await service.CommitAsync(raceEdit); }
            finally { service.BeforeTargetReplace = service.AfterTargetReplace = null; }
        });
        var committed = await service.CommitAsync(raceEdit);
        Assert(File.Exists(Path.Combine(committed.BackupDirectory, "persist.game.json")),
            "A valid offline map operation must still commit with a full game/map/raid backup after rejected operations.");
        Console.WriteLine("PASS: map delete/move/attachment game-state guards and external writer preservation.");
    }

    private static async Task VerifyBridgePreflightContractsAsync(SaveProfile profile, BattleMapSnapshot snapshot,
        ActiveContentSnapshot content, BattleEncounterCatalogResult catalog, ManagedBattleEncounterBridgeService service,
        string gameRoot, string localMods, DsonSaveCodec codec)
    {
        var gameBytes = File.ReadAllBytes(Path.Combine(profile.ProfileDirectory, "persist.game.json"));
        var raidBytes = File.ReadAllBytes(profile.RaidSavePath);
        var mapBytes = File.ReadAllBytes(snapshot.MapSavePath);
        var source = catalog.BridgeEncounters.Single(row => row.OriginDungeonId == "ruins" && row.MonsterIds.SequenceEqual(["ordinary_probe"]));
        foreach (var state in new[] { "battle", "camp", "loot", "town", "target", "missing-content", "invalid-content" })
        {
            if (state is "missing-content" or "invalid-content")
            {
                var map = JsonNode.Parse(mapBytes)!.AsObject();
                var targetTile = map["base_root"]!["map"]!["static_dynamic"]!["areas"]!["coAB"]!["tiles"]!["tile1"]!.AsObject();
                if (state == "missing-content") targetTile.Remove("content");
                else targetTile["content"] = "invalid";
                File.WriteAllText(snapshot.MapSavePath, map.ToJsonString());
            }
            var expectedMapHash = ComputeSha256(snapshot.MapSavePath);
            var raid = JsonNode.Parse(raidBytes)!.AsObject();
            if (state == "battle") raid["base_root"]!["inbattle"] = true;
            if (state == "camp") raid["base_root"]!["camp"]!["phase"] = 1;
            if (state == "loot") raid["base_root"]!["loot"]!["queue"]!["0"] = new JsonObject { ["pending"] = true };
            File.WriteAllText(profile.RaidSavePath, raid.ToJsonString());
            if (state == "town")
                File.WriteAllText(Path.Combine(profile.ProfileDirectory, "persist.game.json"), """{"base_root":{"inraid":false,"raiddungeon":"none","game_mode":"base","applied_ugcs_1_0":{}}}""");
            var actualSnapshot = await new BattleMapSnapshotReader(codec).LoadAsync(profile.ProfileDirectory);
            var actualContent = content with { SourceGameSha256 = ComputeSha256(Path.Combine(profile.ProfileDirectory, "persist.game.json")) };
            var expectedGame = File.ReadAllBytes(Path.Combine(profile.ProfileDirectory, "persist.game.json"));
            var error = await CaptureSaveFailureAsync(() => service.EnsureEncounterAsync(profile, actualSnapshot,
                actualContent, catalog, source, gameRoot, null, localMods,
                placementTarget: state == "target" ? new BattleMapPlacementTarget("rooA", "tile0") : new BattleMapPlacementTarget("coAB", "tile1")));
            Assert((state is "missing-content" or "invalid-content"
                       ? error is InvalidDataException && error.Message.Contains("content", StringComparison.Ordinal)
                       : error is InvalidOperationException) && !Directory.EnumerateFileSystemEntries(localMods).Any() &&
                   File.ReadAllBytes(Path.Combine(profile.ProfileDirectory, "persist.game.json")).SequenceEqual(expectedGame) && ComputeSha256(snapshot.MapSavePath) == expectedMapHash,
                "Invalid saved scene or placement target must be rejected before installing/enabling any Bridge package.");
            File.WriteAllBytes(Path.Combine(profile.ProfileDirectory, "persist.game.json"), gameBytes);
            File.WriteAllBytes(profile.RaidSavePath, raidBytes);
            File.WriteAllBytes(snapshot.MapSavePath, mapBytes);
        }
    }

    private static async Task VerifyBridgeReplacementContractsAsync(string runRoot, string gameRoot, DsonSaveCodec codec)
    {
        foreach (var scenario in new[] { "before", "after", "unreplaced" })
        {
            var before = scenario != "after";
            var root = Path.Combine(runRoot, "bridge-replacement-" + scenario);
            var profile = WriteBattleSafetyProfile(Path.Combine(root, "profile"));
            var localMods = Path.Combine(root, "mods");
            Directory.CreateDirectory(localMods);
            var locations = new SaveEditorLocations(root, Path.Combine(root, "workspaces"), Path.Combine(root, "backups"));
            var content = await ActiveContentResolver.ResolveAsync(profile, gameRoot, null, localMods, codec, locations.WorkspaceDirectory);
            var snapshot = await new BattleMapSnapshotReader(codec).LoadAsync(profile.ProfileDirectory);
            var catalog = BattleEncounterCatalog.Load(content, snapshot);
            var source = catalog.BridgeEncounters.Single(row => row.OriginDungeonId == "ruins" && row.MonsterIds.SequenceEqual(["ordinary_probe"]));
            var gamePath = Path.Combine(profile.ProfileDirectory, "persist.game.json");
            var externalGame = JsonNode.Parse(File.ReadAllBytes(gamePath))!.AsObject();
            externalGame["base_root"]!["external_progress"] = before ? "before" : "after";
            var externalBytes = Encoding.UTF8.GetBytes(externalGame.ToJsonString());
            var injected = false;
            Action<string> update = path =>
            {
                ReplaceExternalBytes(path, externalBytes);
                injected = true;
                if (scenario == "unreplaced") throw new IOException("Injected failure before Bridge game replacement.");
            };
            var service = new ManagedBattleEncounterBridgeService(codec, locations, () => false)
            {
                BeforeGameReplace = before ? update : null,
                AfterGameReplace = before ? null : update
            };
            var error = await CaptureSaveFailureAsync(() => service.EnsureEncounterAsync(profile, snapshot,
                content, catalog, source, gameRoot, null, localMods,
                placementTarget: new BattleMapPlacementTarget("coAB", "tile1")));
            Assert(injected && error is AggregateException && File.ReadAllBytes(gamePath).SequenceEqual(externalBytes) &&
                   Directory.EnumerateDirectories(localMods).Any() && ComputeSha256(snapshot.MapSavePath).Equals(snapshot.MapSha256, StringComparison.OrdinalIgnoreCase),
                $"A Bridge game-file race must preserve external progress and a possibly referenced carrier ({scenario}, injected={injected}): {error}");
        }
        Console.WriteLine("PASS: Bridge preflight and game-configuration replacement races.");
    }
}
