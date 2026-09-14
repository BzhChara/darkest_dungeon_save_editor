internal static partial class ContractSuite
{
    private static async Task RunEmptyEncounterSlotContractsAsync(string runRoot, DsonSaveCodec codec)
    {
        var root = Path.Combine(runRoot, "empty-encounter-slots");
        var game = Path.Combine(root, "game");
        var mods = Path.Combine(game, "mods");
        Directory.CreateDirectory(mods);
        var profile = WriteBattleSafetyProfile(Path.Combine(root, "profile"));
        var mapPath = Path.Combine(profile.ProfileDirectory, "persist.map.json");
        var mapDocument = JsonNode.Parse(File.ReadAllText(mapPath))!;
        var map = mapDocument["base_root"]!["map"]!;
        var statics = map["static_dynamic"]!["static_save"]!["base_root"]!["areas"]!;
        statics["rooD"] = statics["rooB"]!.DeepClone();
        statics["rooD"]!["id"] = 300;
        statics["rooD"]!["tiles"]!["tile0"]!["mappos"] = new JsonArray(8.0, 0.0);
        var dynamics = map["static_dynamic"]!["areas"]!;
        dynamics["rooD"] = dynamics["rooB"]!.DeepClone();
        map["bounds"] = new JsonArray(0.0, 8.0, 0.0, 0.0);
        File.WriteAllText(mapPath, mapDocument.ToJsonString());
        var raidPath = Path.Combine(profile.ProfileDirectory, "persist.raid.json");
        var raidDocument = JsonNode.Parse(File.ReadAllText(raidPath))!;
        raidDocument["base_root"]!["start_elapsed_time"] = 100;
        raidDocument["base_root"]!["raid_instance"]!["id"] = "empty-slot-contract";
        File.WriteAllText(raidPath, raidDocument.ToJsonString());
        var nativePath = WriteMultiMash(game, "dungeons/cove/cove.2.mash.darkest",
            string.Join('\n', new[] { "hall", "room", "boss" }.Select(kind => $$"""
                {{kind}}: .chance 1 .types native
                {{kind}}: .chance 1 .types ""
                {{kind}}: .chance 1 .types native native
                {{kind}}: .chance 1 .types missing
                {{kind}}: .chance 1 .types large
                {{kind}}: .chance 1 .types "" "" "" "" large
                {{kind}}: .chance 1 .types native native native
                """)));
        WriteMultiMash(game, "dungeons/ruins/ruins.2.mash.darkest",
            string.Join('\n', new[] { "hall", "room", "boss" }.Select(kind =>
                $"{kind}: .chance 1 .types foreign\n{kind}: .chance 1 .types \"\"")));
        CreateBattleMonsterDefinitions(game, ["native", "foreign", "large"]);
        var largeInfo = Directory.EnumerateFiles(Path.Combine(game, "monsters"), "large.info.darkest", SearchOption.AllDirectories).Single();
        File.AppendAllText(largeInfo, "\ndisplay: .size 5\n");
        var locations = new SaveEditorLocations(root, Path.Combine(root, "workspaces"), Path.Combine(root, "backups"));
        var reader = new BattleMapSnapshotReader(codec);
        var snapshot = await reader.LoadAsync(profile.ProfileDirectory);
        var content = await ActiveContentResolver.ResolveAsync(profile, game, null, mods, codec, locations.WorkspaceDirectory);
        var catalog = BattleEncounterCatalog.Load(content, snapshot);
        var nativeHash = ComputeSha256(nativePath);
        foreach (var type in new[] { 0, 1, 2 })
        {
            var rows = catalog.Encounters.Where(row => row.MashType == type && row.MashIndex is not null).ToArray();
            Assert(rows.Select(row => row.MashIndex).SequenceEqual(new int?[] { 0, 1, 2, 3, 4, 5 }) &&
                   rows.Select(row => row.CanPlaceDirectly).SequenceEqual(new[] { true, false, true, false, false, true }) &&
                   rows[1].MonsterIds.Count == 0 && rows[4].MonsterIds.Count == 0 &&
                   catalog.Encounters.Single(row => row.MashType == type && row.MonsterIds.SequenceEqual(["large"])).MashIndex is null,
                "Explicit empty slots and missing monsters retain native indexes; oversized rows and fifth actors do not shift later rows.");
            foreach (var row in rows.Where(row => row.CanPlaceDirectly)) BattleEncounterCatalog.ValidateDirectEncounter(row);
            var error = await CaptureSaveFailureAsync(() =>
            {
                BattleEncounterCatalog.ValidateDirectEncounter(rows[1] with { CanPlaceDirectly = true });
                return Task.CompletedTask;
            });
            Assert(error is InvalidOperationException && error.Message.Contains("空遭遇", StringComparison.Ordinal),
                "Preflight must reject a forged placeable empty row even when its identity and numeric slot are correct.");
            var maintenanceRows = BattleEncounterCatalog.ReadMaintenanceTable(content, "cove", 2, type);
            Assert(maintenanceRows.Select(row => (row.MashIndex, row.CanPlaceDirectly))
                    .SequenceEqual(rows.Select(row => (row.MashIndex, row.CanPlaceDirectly))),
                "Maintenance and placement catalogs must agree on retained empty indexes and non-placeability.");
        }
        Assert(catalog.BridgeEncounters.All(row => row.MonsterIds.Count > 0),
            "An empty source formation from any region must stay out of the Bridge selection list.");
        var bridge = new ManagedBattleEncounterBridgeService(codec, locations, () => false);
        var maps = new BattleMapEditService(codec, locations);
        foreach (var type in new[] { 0, 1, 2 })
        {
            var source = catalog.BridgeEncounters.Single(row => row.OriginDungeonId == "ruins" && row.MashType == type);
            var added = await bridge.EnsureEncounterAsync(profile, snapshot, content, catalog, source, game, null, mods);
            Assert(added.MashIndex == 6,
                "A new foreign composition must append after all six retained slots, including empty/missing rows.");
            content = added.ActiveContent;
            catalog = added.Catalog;
            var area = type switch { 0 => "coAB", 1 => "rooD", _ => "rooB" };
            var tile = type == 0 ? "tile1" : "tile0";
            foreach (var selection in new[] { catalog.DirectEncounters.Single(row => row.MashType == type && row.MashIndex == 5), added.DirectEncounter })
            {
                await maps.CommitAsync(await maps.PreparePlaceBattleAsync(profile, snapshot, area, tile, selection));
                snapshot = await reader.LoadAsync(profile.ProfileDirectory);
                var placed = snapshot.Areas.Single(row => row.AreaId == area).Tiles.Single(row => row.TileId == tile);
                Assert(placed.MashType == type && placed.MashIndex == selection.MashIndex,
                    "Creation and replacement must serialize the proven native indexes after empty slots.");
            }
            await maps.CommitAsync(await maps.PrepareDeleteContentAsync(profile, snapshot, area, tile));
            snapshot = await reader.LoadAsync(profile.ProfileDirectory);
            Assert(snapshot.Areas.Single(row => row.AreaId == area).Tiles.Single(row => row.TileId == tile).Content == BattleMapTileContent.Nothing,
                "Deleting a battle after empty-slot placement must still clear its tile.");
        }
        var unchanged = await bridge.ReconcileAsync(content, game, mods);
        Assert(!unchanged.Changed && !unchanged.Deferred && ComputeSha256(nativePath) == nativeHash,
            "Valid empty native slots must not trigger automatic cleanup or alter source files.");

        // A byte-truncated ID that ends inside UTF-8 is still unproven.
        File.AppendAllText(nativePath, $"\nhall: .chance 1 .types {new string('界', 11)}\n");
        var uncertain = BattleEncounterCatalog.Load(content, snapshot);
        Assert(uncertain.Encounters.Where(row => row.MashType == 0).All(row => !row.CanPlaceDirectly),
            "Supporting empty actors must not remove the guard for unproven byte-truncated IDs.");
        var selected = uncertain.BridgeEncounters.Single(row => row.OriginDungeonId == "ruins" && row.MashType == 0);
        var failure = await CaptureSaveFailureAsync(() => bridge.EnsureEncounterAsync(profile, snapshot, content, uncertain, selected, game, null, mods));
        Assert(failure is InvalidOperationException, "Bridge preflight must still reject uncertain native row counts.");
        Console.WriteLine("PASS: empty encounter indexes, direct/Bridge create-replace-delete, and maintenance parity for all three supported types.");
    }
}
