internal static partial class ContractSuite
{
    private static string QueryMashes(string actor) =>
        $"hall: .chance 1 .types {actor}\nroom: .chance 1 .types {actor}\nboss: .chance 1 .types {actor}\n";

    private static async Task RunEncounterFileQueryContractsAsync(string runRoot, DsonSaveCodec codec)
    {
        foreach (var kind in new[] { "base", "mode", "dlc-feature", "local", "workshop", "dlc-mod" })
        {
            var root = Path.Combine(runRoot, "encounter-file-queries", kind);
            var sourceRoot = Path.Combine(root, "source");
            var prefix = kind == "dlc-mod" ? "dlc/rq_feature/" : "";
            void Write(string path, string value) => WriteMultiMash(sourceRoot, prefix + path, value);
            const string first = "dungeons/cove/a.coveX2.mash.darkest";
            const string second = "dungeons/cove/b.cove.2.mashXdarkest";
            const string empty = "dungeons/cove/c.cove22XmashYdarkest";
            Write(first, QueryMashes("alpha_A"));
            Write(second, QueryMashes("bravo_A"));
            Write(empty, QueryMashes("\"\""));
            foreach (var ignored in new[] { "dungeons/cove/README.md", "dungeons/cove/notes.darkest",
                "dungeons/cove/wrong.2.mash.darkest", "dungeons/cove/d.cove.02.mash.darkest",
                "dungeons/cove/e.COVE.2.mash.darkest", "dungeons/cove/f.cove.2.mash.darkest.bak" })
                Write(ignored, QueryMashes("missing_A"));
            Write("dungeons/ruins/a.ruinsX2.mash.darkest", QueryMashes("bravo_A"));
            foreach (var id in new[] { "alpha_A", "bravo_A" })
                Write($"monsters/{id[..^2]}/{id}/{id}.info.darkest", "display: .size 1\n");
            WriteMultiMash(sourceRoot, "dlc/disabled/dungeons/cove/disabled.cove.2.mash.darkest", QueryMashes("missing_A"));
            var sources = QuerySources(sourceRoot, kind);
            if (kind == "dlc-mod")
            {
                // The existing DLC-only-new-Mod-file guard is retained. Give
                // each alias an official mount slot before the Mod replaces it.
                foreach (var path in new[] { first, second, empty })
                    WriteMultiMash(sources[1].Directory, path, QueryMashes("alpha_A"));
            }
            if (kind is "local" or "workshop" or "dlc-mod")
            {
                WriteFixtureManifest(sourceRoot);
                Write("dungeons/cove/0.cove.2.mash.darkest", QueryMashes("missing_A"));
            }
            var content = QueryContent(root, sources);
            var map = new BattleMapSnapshot(content.Profile.ProfileDirectory, "", "", "", "", "cove", 2, 1,
                null, null, null, null, null, null, null, null, null, false, [], [], [], DateTime.UtcNow);
            var catalog = BattleEncounterCatalog.Load(content, map);
            foreach (var type in new[] { 0, 1, 2 })
            {
                var rows = catalog.Encounters.Where(row => row.MashType == type).ToArray();
                Assert(rows.Length == 3 && rows.Select(row => row.MashIndex).SequenceEqual(new int?[] { 0, 1, 2 }) &&
                       rows[0].MonsterIds.SequenceEqual(["alpha_A"]) && rows[1].MonsterIds.SequenceEqual(["bravo_A"]) &&
                       rows[2].MonsterIds.Count == 0 && !rows[2].CanPlaceDirectly,
                    $"{kind}/{type}: native wildcard separators and empty slots must preserve the entire table.");
                BattleEncounterCatalog.ValidateDirectEncounter(rows[1]);
                Assert(BattleEncounterCatalog.ResolveAppendTarget(catalog, type).NextMashIndex == 3 &&
                       BattleEncounterCatalog.ReadMaintenanceTable(content, "cove", 2, type).Count == 3,
                    "Direct, Bridge append and maintenance must count the same three slots.");
                BattleEncounterCatalog.ValidateBridgeEncounter(catalog.BridgeEncounters.Single(row =>
                    row.OriginDungeonId == "ruins" && row.MashType == type));
            }
            var fingerprint = BattleEncounterCatalog.CaptureContentFingerprint(sources);
            Write(second, QueryMashes("alpha_A"));
            Assert(BattleEncounterCatalog.CaptureContentFingerprint(sources) != fingerprint,
                "Encounter maintenance must observe changes in accepted nonliteral mash suffixes.");
            Assert(await CaptureSaveFailureAsync(() => {
                BattleEncounterCatalog.ValidateDirectEncounter(catalog.DirectEncounters.Last()); return Task.CompletedTask;
            }) is InvalidOperationException, "Old encounter guards must reject changed nonliteral files.");
            Assert(BattleEncounterCatalog.ReadMaintenanceTable(content, "cove", 2, 0)[1].MonsterIds.SequenceEqual(["alpha_A"]),
                "A maintenance rescan must read updated bytes from the same eligible table.");

            var highRoot = Path.Combine(root, "high");
            WriteMultiMash(highRoot, prefix + first, QueryMashes("bravo_A"));
            WriteFixtureManifest(highRoot);
            var highContent = content with { Sources = sources.Append(new ActiveContentSource("local:query-high", "High", "local", highRoot, -1000)).ToArray() };
            Assert(BattleEncounterCatalog.Load(highContent, map).DirectEncounters.First().MonsterIds.SequenceEqual(["bravo_A"]),
                "Query changes must preserve high-priority same-path replacement and the original slot.");
        }

        foreach (var type in new[] { 0, 1, 2 })
        {
            var root = Path.Combine(runRoot, "encounter-query-persistence", type.ToString());
            var game = Path.Combine(root, "game");
            var mods = Path.Combine(game, "mods");
            Directory.CreateDirectory(mods);
            var profile = WriteBattleSafetyProfile(Path.Combine(root, "profile"));
            var raidPath = Path.Combine(profile.ProfileDirectory, "persist.raid.json");
            var raid = JsonNode.Parse(File.ReadAllText(raidPath))!;
            raid["base_root"]!["start_elapsed_time"] = 100;
            raid["base_root"]!["raid_instance"]!["id"] = "query-persistence";
            File.WriteAllText(raidPath, raid.ToJsonString());
            WriteMultiMash(game, "dungeons/cove/a.coveX2.mash.darkest", QueryMashes("alpha"));
            WriteMultiMash(game, "dungeons/cove/b.cove.2.mashXdarkest", QueryMashes("bravo"));
            WriteMultiMash(game, "dungeons/ruins/a.ruinsX2.mash.darkest", QueryMashes("foreign"));
            CreateBattleMonsterDefinitions(game, ["alpha", "bravo", "foreign"]);
            var locations = new SaveEditorLocations(root, Path.Combine(root, "workspace"), Path.Combine(root, "backups"));
            var reader = new BattleMapSnapshotReader(codec);
            var snapshot = await reader.LoadAsync(profile.ProfileDirectory);
            var content = await ActiveContentResolver.ResolveAsync(profile, game, null, mods, codec, locations.WorkspaceDirectory);
            var catalog = BattleEncounterCatalog.Load(content, snapshot);
            var bridge = new ManagedBattleEncounterBridgeService(codec, locations, () => false);
            var foreign = catalog.BridgeEncounters.Single(row => row.OriginDungeonId == "ruins" && row.MashType == type);
            var added = await bridge.EnsureEncounterAsync(profile, snapshot, content, catalog, foreign, game, null, mods);
            Assert(added.MashIndex == 2, "Bridge must append at the actual second index after both native query files.");
            var maps = new BattleMapEditService(codec, locations);
            var area = type == 0 ? "coAB" : type == 1 ? "rooC" : "rooB";
            var tile = type == 0 ? "tile1" : "tile0";
            foreach (var selection in new[] { added.Catalog.DirectEncounters.Single(row => row.MashType == type && row.MashIndex == 1), added.DirectEncounter })
            {
                await maps.CommitAsync(await maps.PreparePlaceBattleAsync(profile, snapshot, area, tile, selection));
                snapshot = await reader.LoadAsync(profile.ProfileDirectory);
                var placed = snapshot.Areas.Single(row => row.AreaId == area).Tiles.Single(row => row.TileId == tile);
                Assert(placed.MashIndex == selection.MashIndex && placed.MashType == type,
                    "Direct replacement and Bridge creation must persist the corrected type/index through DSON reload.");
            }
            await maps.CommitAsync(await maps.PrepareDeleteContentAsync(profile, snapshot, area, tile));
            snapshot = await reader.LoadAsync(profile.ProfileDirectory);
            Assert(snapshot.Areas.Single(row => row.AreaId == area).Tiles.Single(row => row.TileId == tile).Content == BattleMapTileContent.Nothing,
                "Deleting the generated battle must still clear its edited cell.");
            Assert(!(await bridge.ReconcileAsync(added.ActiveContent, game, mods)).Changed,
                "Correct query-derived Bridge bindings must remain stable during maintenance.");
        }
        Console.WriteLine("PASS: native encounter filename queries, manifest/overlay guards, all three types, maintenance and direct/Bridge DSON writes.");
    }
}
