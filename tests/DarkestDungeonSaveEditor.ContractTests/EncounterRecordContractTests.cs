internal static partial class ContractSuite
{
    private static async Task RunEncounterRecordContractsAsync(string runRoot, DsonSaveCodec codec)
    {
        foreach (var type in new[] { 0, 1, 2 })
        {
            string[] ids = ["space unit_A", "hash#_A"];
            var emitted = NativeDarkestReader.ReadRecordsFromText(EncounterBridgeRow.Format(type, ids) +
                EncounterBridgeRow.Format(type, ["foreign"])).ToArray();
            Assert(emitted.Length == 2 && NativeDarkestReader.ReadRawStringSlots(emitted[0].Body, ".types", 4).SequenceEqual(ids),
                "Bridge emission must preserve spaces and hash characters without consuming the next record.");
        }
        foreach (var kind in new[] { "hall", "room", "boss" })
        foreach (var (name, text, expected) in new[]
        {
            ("ordinary", $"{kind}: .chance 1 .types alpha\n{kind}: .chance 1 .types bravo\n{kind}: .chance 1 .types charlie\n", new[] { "alpha", "bravo", "charlie" }),
            ("same-line", $"{kind}: .chance 1 .types alpha {kind}: .chance 1 .types bravo\n{kind}: .chance 1 .types charlie\n", new[] { "alpha", "bravo", "charlie" }),
            ("same-identity", $"{kind}: .chance 1 .types alpha {kind}: .chance 1 .types alpha\n", new[] { "alpha", "alpha" }),
            ("multiline", $"{kind}:\n.chance \n 1 .types\nalpha\n{kind}: .chance 1 .types bravo\n", new[] { "alpha", "bravo" }),
            ("block-comment", $"/*\n{kind}: .chance 1 .types alpha\n*/\n{kind}: .chance 1 .types bravo\n", new[] { "bravo" }),
            ("nul", $"{kind}: .chance 1 .types alpha\n\0\n{kind}: .chance 1 .types bravo\n", new[] { "alpha" }),
            ("case", $"{kind.ToUpperInvariant()}: .chance 1 .types alpha\n{kind}: .chance 1 .types bravo\n", new[] { "bravo" }),
            ("prefix-comment", $"# bogus: declarations\n// more bogus: declarations\n{kind}: .chance 1 .types alpha\n", new[] { "alpha" })
        })
        {
            var root = Path.Combine(runRoot, "encounter-records", kind + "-" + name);
            var game = Path.Combine(root, "game");
            var mods = Path.Combine(game, "mods");
            Directory.CreateDirectory(mods);
            var profile = WriteBattleSafetyProfile(Path.Combine(root, "profile"));
            var raidPath = Path.Combine(profile.ProfileDirectory, "persist.raid.json");
            var raid = JsonNode.Parse(File.ReadAllText(raidPath))!;
            raid["base_root"]!["start_elapsed_time"] = 100;
            raid["base_root"]!["raid_instance"]!["id"] = "logical-record-fixture";
            File.WriteAllText(raidPath, raid.ToJsonString());
            WriteMultiMash(game, "dungeons/cove/cove.2.mash.darkest", text);
            WriteMultiMash(game, "dungeons/ruins/ruins.2.mash.darkest",
                $"{kind}: .chance 1 .types foreign {kind}: .chance 1 .types foreign\n");
            CreateBattleMonsterDefinitions(game, ["alpha", "bravo", "charlie", "foreign"]);
            var locations = new SaveEditorLocations(root, Path.Combine(root, "workspace"), Path.Combine(root, "backups"));
            var reader = new BattleMapSnapshotReader(codec);
            var snapshot = await reader.LoadAsync(profile.ProfileDirectory);
            var content = await ActiveContentResolver.ResolveAsync(profile, game, null, mods, codec, locations.WorkspaceDirectory);
            var catalog = BattleEncounterCatalog.Load(content, snapshot);
            var type = kind == "hall" ? 0 : kind == "room" ? 1 : 2;
            var rows = catalog.Encounters.Where(row => row.MashType == type).ToArray();
            Assert(rows.Select(row => row.MonsterIds.Single()).SequenceEqual(expected) &&
                   rows.Select(row => row.MashIndex).SequenceEqual(Enumerable.Range(0, expected.Length).Select(index => (int?)index)) &&
                   rows.All(row => row.CanPlaceDirectly && row.Classification == (type == 2 ? BattleEncounterClassification.FixedBoss : BattleEncounterClassification.Ordinary)),
                $"{kind}/{name}: logical declarations must preserve native composition, order and classification.");
            foreach (var row in rows) BattleEncounterCatalog.ValidateDirectEncounter(row);
            Assert(BattleEncounterCatalog.ResolveAppendTarget(catalog, type).NextMashIndex == expected.Length,
                $"{kind}/{name}: append preflight must use the same logical table as the catalog.");
            var foreign = catalog.BridgeEncounters.Where(row => row.OriginDungeonId == "ruins" && row.MashType == type).ToArray();
            Assert(foreign.Length == 2 && foreign[0].SourceLine == foreign[1].SourceLine &&
                   foreign[0].SourceRecordIndex != foreign[1].SourceRecordIndex,
                "Identical declarations on one source line must retain two distinct record identities.");
            foreach (var source in foreign) BattleEncounterCatalog.ValidateBridgeEncounter(source);
            Assert(await CaptureSaveFailureAsync(() => {
                BattleEncounterCatalog.ValidateBridgeEncounter(foreign[1] with { SourceRecordIndex = 999 });
                return Task.CompletedTask;
            }) is InvalidOperationException, "Source record identity must participate in the write preflight.");
            if (name is not ("same-line" or "same-identity")) continue;

            var bridge = new ManagedBattleEncounterBridgeService(codec, locations, () => false);
            var added = await bridge.EnsureEncounterAsync(profile, snapshot, content, catalog, foreign[1], game, null, mods);
            Assert(added.MashIndex == expected.Length && added.DirectEncounter.MonsterIds.SequenceEqual(["foreign"]),
                "Bridge must append after logical records and its options must not become extra monster slots.");
            var maps = new BattleMapEditService(codec, locations);
            var area = type == 0 ? "coAB" : type == 1 ? "rooC" : "rooB";
            var tile = type == 0 ? "tile1" : "tile0";
            foreach (var selection in new[] { added.Catalog.DirectEncounters.Single(row => row.MashType == type && row.MashIndex == expected.Length - 1), added.DirectEncounter })
            {
                await maps.CommitAsync(await maps.PreparePlaceBattleAsync(profile, snapshot, area, tile, selection));
                snapshot = await reader.LoadAsync(profile.ProfileDirectory);
                var placed = snapshot.Areas.Single(row => row.AreaId == area).Tiles.Single(row => row.TileId == tile);
                Assert(placed.MashType == type && placed.MashIndex == selection.MashIndex,
                    "Direct replacement and Bridge creation must persist the proven runtime index.");
            }
            await maps.CommitAsync(await maps.PrepareDeleteContentAsync(profile, snapshot, area, tile));
            snapshot = await reader.LoadAsync(profile.ProfileDirectory);
            Assert(snapshot.Areas.Single(row => row.AreaId == area).Tiles.Single(row => row.TileId == tile).Content == BattleMapTileContent.Nothing,
                "Deleting a battle must still clear the edited tile after logical-record placement.");
            Assert(!(await bridge.ReconcileAsync(added.ActiveContent, game, mods)).Changed,
                "A valid canonical Bridge must not trigger maintenance after this parser change.");
        }

        var slotRoot = Path.Combine(runRoot, "encounter-raw-slots");
        var slotGame = Path.Combine(slotRoot, "game");
        var slotProfile = WriteBattleSafetyProfile(Path.Combine(slotRoot, "profile"));
        var shortId = new string('q', 29) + "_A";
        var longId = shortId + "_extra_A";
        foreach (var (id, size) in new[] { ("alpha", 1), ("bravo", 1), (shortId, 4), (longId, 1), (".oversize_A", 4) })
            WriteMultiMash(slotGame, $"monsters/{id[..^2]}/{id}/{id}.info.darkest", $"display: .size {size}\n");
        var slotSnapshot = await new BattleMapSnapshotReader(codec).LoadAsync(slotProfile.ProfileDirectory);
        var slotContent = await ActiveContentResolver.ResolveAsync(slotProfile, slotGame, null, codec, Path.Combine(slotRoot, "workspace"));
        // Directory discovery skips dot-prefixed names. A manifest-backed Mod
        // is required to actually register the .oversize_A test definition.
        slotContent = slotContent with { Sources = [new ActiveContentSource("local:raw-slots", "Raw slots", "local", slotGame, 0)] };
        foreach (var actors in new[] { "alpha " + longId, "alpha .oversize_A" })
        {
            WriteMultiMash(slotGame, "dungeons/cove/cove.2.mash.darkest", $"hall: .chance 1 .types {actors}\nhall: .chance 1 .types bravo\n");
            WriteFixtureManifest(slotGame);
            var catalog = BattleEncounterCatalog.Load(slotContent, slotSnapshot);
            Assert(catalog.DirectEncounters is [{ MashIndex: 0 }] && catalog.DirectEncounters[0].MonsterIds.SequenceEqual(["bravo"]) &&
                   catalog.Encounters.Count == 2 && catalog.Encounters[0].MashIndex is null &&
                   BattleEncounterCatalog.ResolveAppendTarget(catalog, 0).NextMashIndex == 1,
                $"{actors}: truncation and dot-prefixed raw slots must be resolved before native size-based row skipping.");
            BattleEncounterCatalog.ValidateDirectEncounter(catalog.DirectEncounters[0]);
        }
        WriteMultiMash(slotGame, "dungeons/cove/cove.2.mash.darkest", """
            hall: .chance 1 .random_dungeon_roaming_id shambler .random_dungeon_roaming_id "" .types alpha
            hall: .chance 1 .RANDOM_DUNGEON_ROAMING_ID shambler .types bravo
            hall: .chance 1 .random_dungeon_roaming_id "" .random_dungeon_roaming_id shambler .types alpha
            hall: .chance 1 .types "" alpha "" "" invalid_fifth
            """);
        var roaming = BattleEncounterCatalog.Load(slotContent, slotSnapshot);
        Assert(roaming.DirectEncounters.Select(row => row.Classification).SequenceEqual(new[] {
                BattleEncounterClassification.Ordinary, BattleEncounterClassification.Ordinary,
                BattleEncounterClassification.RoamingEncounter, BattleEncounterClassification.Ordinary }) &&
               roaming.Encounters[0].RoamingId == "" && roaming.Encounters[1].RoamingId is null &&
               roaming.Encounters[3].MonsterIds.SequenceEqual(["alpha"]),
            "Last exact roaming fields, explicit clears, empty slots and ignored fifth slots must retain native meaning.");
        foreach (var row in roaming.DirectEncounters) BattleEncounterCatalog.ValidateDirectEncounter(row);
        WriteMultiMash(slotGame, "dungeons/cove/cove.2.mash.darkest", $"hall: .chance 1 .types {new string('界', 11)}\nhall: .chance 1 .types bravo\n");
        var uncertain = BattleEncounterCatalog.Load(slotContent, slotSnapshot);
        Assert(uncertain.DirectEncounters.Count == 0 && uncertain.Issues.Any(issue => issue.Contains("UTF-8")),
            "A partial UTF-8 native slot cannot be silently dropped to number later rows.");
        Assert(await CaptureSaveFailureAsync(() => { BattleEncounterCatalog.ResolveAppendTarget(uncertain, 0); return Task.CompletedTask; }) is InvalidOperationException,
            "Bridge append must reject unresolved slot bytes as well as direct placement.");
        await VerifyObsoleteBridgeRecordsAsync(runRoot, codec);
        Console.WriteLine("PASS: logical encounter records, raw slots, roaming classification, Bridge and map persistence.");
    }

    private static async Task VerifyObsoleteBridgeRecordsAsync(string runRoot, DsonSaveCodec codec)
    {
        foreach (var scenario in new[] { "owned", "altered", "unowned" })
        {
            var root = Path.Combine(runRoot, "obsolete-bridge-records", scenario);
            var game = Path.Combine(root, "game");
            var mods = Path.Combine(game, "mods");
            Directory.CreateDirectory(mods);
            var profile = WriteBattleSafetyProfile(Path.Combine(root, "profile"));
            var mapPath = Path.Combine(profile.ProfileDirectory, "persist.map.json");
            var mapDocument = JsonNode.Parse(File.ReadAllText(mapPath))!;
            mapDocument["base_root"]!["map"]!["final_room_id"] = 250;
            var nativeArea = mapDocument["base_root"]!["map"]!["static_dynamic"]!["areas"]!["rooC"]!.DeepClone();
            File.WriteAllText(mapPath, mapDocument.ToJsonString());
            var raidPath = Path.Combine(profile.ProfileDirectory, "persist.raid.json");
            var raid = JsonNode.Parse(File.ReadAllText(raidPath))!;
            raid["base_root"]!["start_elapsed_time"] = 100;
            raid["base_root"]!["raid_instance"]!["id"] = "obsolete-bridge-fixture";
            File.WriteAllText(raidPath, raid.ToJsonString());
            WriteMultiMash(game, "dungeons/cove/cove.2.mash.darkest", "hall: .chance 1 .types native\nroom: .chance 1 .types native\n");
            WriteMultiMash(game, "dungeons/ruins/ruins.2.mash.darkest", "hall: .chance 1 .types foreign\n");
            CreateBattleMonsterDefinitions(game, ["native", "foreign"]);
            var locations = new SaveEditorLocations(root, Path.Combine(root, "workspace"), Path.Combine(root, "backups"));
            var reader = new BattleMapSnapshotReader(codec);
            var snapshot = await reader.LoadAsync(profile.ProfileDirectory);
            var content = await ActiveContentResolver.ResolveAsync(profile, game, null, mods, codec, locations.WorkspaceDirectory);
            var catalog = BattleEncounterCatalog.Load(content, snapshot);
            var bridge = new ManagedBattleEncounterBridgeService(codec, locations, () => false);
            var added = await bridge.EnsureEncounterAsync(profile, snapshot, content, catalog,
                catalog.BridgeEncounters.Single(row => row.OriginDungeonId == "ruins"), game, null, mods);
            var maps = new BattleMapEditService(codec, locations);
            await maps.CommitAsync(await maps.PreparePlaceBattleAsync(profile, snapshot, "rooB", "tile0",
                added.Catalog.DirectEncounters.Single(row => row.MashType == 1)));
            snapshot = await reader.LoadAsync(profile.ProfileDirectory);
            var commit = await maps.CommitAsync(await maps.PreparePlaceBattleAsync(profile, snapshot, "coAB", "tile1", added.DirectEncounter));
            if (scenario == "unowned") File.Delete(Path.Combine(commit.BackupDirectory, "commit-result.json"));

            // Represent a package produced by the previous released generator.
            // An altered package also matches its supplied SHA, so cleanup must
            // require the exact generated text, not trust a manifest hash alone.
            File.WriteAllText(added.MashFilePath, "hall: .chance 0 .types foreign .limit 1 .can_be_ambush false" +
                Environment.NewLine + (scenario == "altered" ? "// external change\n" : ""), new System.Text.UTF8Encoding(false));
            var manifestPath = Path.Combine(added.PackageDirectory, ManagedBattleEncounterBridgeService.ManifestFileName);
            var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!;
            manifest["Tables"]![0]!["GeneratedMashSha256"] = ComputeSha256(added.MashFilePath);
            File.WriteAllText(manifestPath, manifest.ToJsonString());
            var beforeMap = ComputeSha256(mapPath);
            var beforeMash = ComputeSha256(added.MashFilePath);
            if (scenario != "owned")
            {
                var failure = await CaptureSaveFailureAsync(() => bridge.ReconcileAsync(added.ActiveContent, game, mods));
                Assert(failure is InvalidDataException or InvalidOperationException &&
                       ComputeSha256(mapPath) == beforeMap && ComputeSha256(added.MashFilePath) == beforeMash,
                    "Obsolete syntax must not bypass package-text or current-map ownership checks.");
                continue;
            }
            var result = await bridge.ReconcileAsync(added.ActiveContent, game, mods);
            Assert(result.Changed && result.RemovedCombinations == 1 && result.ClearedBattles == 2,
                "Owned obsolete records must be retired and all editor battles cleared, including direct local placements.");
            var after = JsonNode.Parse(File.ReadAllText(mapPath))!;
            Assert(JsonNode.DeepEquals(nativeArea, after["base_root"]!["map"]!["static_dynamic"]!["areas"]!["rooC"]),
                "Cleaning obsolete editor records must leave the natural map area unchanged.");
            snapshot = await reader.LoadAsync(profile.ProfileDirectory);
            content = await ActiveContentResolver.ResolveAsync(profile, game, null, mods, codec, locations.WorkspaceDirectory);
            catalog = BattleEncounterCatalog.Load(content, snapshot);
            var replacement = await bridge.EnsureEncounterAsync(profile, snapshot, content, catalog,
                catalog.BridgeEncounters.Single(row => row.OriginDungeonId == "ruins"), game, null, mods);
            Assert(replacement.MashIndex == 1 && replacement.DirectEncounter.MonsterIds.SequenceEqual(["foreign"]),
                "After cleanup a new canonical Bridge must be usable at the recalculated index.");
        }
    }
}
