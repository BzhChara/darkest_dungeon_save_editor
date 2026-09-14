internal static partial class ContractSuite
{
    public static async Task RunEncounterQueriesOnlyAsync(string repositoryRoot)
    {
        var fixture = BuildContractFixture(repositoryRoot);
        await RunEncounterCollectionQueryContractsAsync(fixture.RunRoot, fixture.Codec);
        await RunEncounterFileQueryContractsAsync(fixture.RunRoot, fixture.Codec);
        await RunEmptyEncounterSlotContractsAsync(fixture.RunRoot, fixture.Codec);
        Console.WriteLine($"Artifacts: {fixture.RunRoot}");
    }

    private static async Task RunEncounterCollectionQueryContractsAsync(string runRoot, DsonSaveCodec codec)
    {
        await VerifyEncounterCollectionQueriesAsync(runRoot);
        await VerifyRepeatedEncounterSourcesAsync(runRoot);
        await VerifyOmittedEncounterTypesAsync(runRoot);
        await VerifyEncounterCollectionPersistenceAsync(runRoot, codec);
    }

    private static BattleMapSnapshot EncounterQueryMap(ActiveContentSnapshot content, string dungeon = "cove") =>
        new(content.Profile.ProfileDirectory, "", "", "", "", dungeon, 2, 1,
            null, null, null, null, null, null, null, null, null, false, [], [], [], DateTime.UtcNow);

    private static async Task VerifyEncounterCollectionQueriesAsync(string runRoot)
    {
        (string Name, BattleEncounterSourceKind? Kind)[] cases =
        [
            ("a.cove.2.mash.darkest", BattleEncounterSourceKind.Standard),
            ("b.conditional.cove.2.mash.darkest", BattleEncounterSourceKind.Standard),
            ("c.additional.cove.2.mash.darkest", BattleEncounterSourceKind.Standard),
            ("d.CONDITIONAL.cove.2.mash.darkest", BattleEncounterSourceKind.Standard),
            ("e.cove.conditional.2.mash.darkest", BattleEncounterSourceKind.Conditional),
            ("f.cove.conditionalX2.mash.darkest", BattleEncounterSourceKind.Conditional),
            ("g.cove.additionalX2.mash.darkest", BattleEncounterSourceKind.Additional),
            ("h.cove.CONDITIONAL.2.mash.darkest", null),
            ("i.conditional.unused.2.mash.darkest", null),
            ("j.conditional.additional.2.mash.darkest", BattleEncounterSourceKind.Additional)
        ];
        foreach (var kind in new[] { "base", "mode", "dlc-feature", "local", "workshop", "dlc-mod" })
        {
            var root = Path.Combine(runRoot, "encounter-collections", kind);
            var source = Path.Combine(root, "source");
            var prefix = kind == "dlc-mod" ? "dlc/rq_feature/" : "";
            for (var index = 0; index < cases.Length; index++)
                WriteMultiMash(source, prefix + "dungeons/cove/" + cases[index].Name, QueryMashes($"actor{index}_A"));
            CreateBattleMonsterDefinitions(source, Enumerable.Range(0, cases.Length).Select(index => $"actor{index}_A").ToArray());
            var sources = QuerySources(source, kind);
            if (kind == "dlc-mod")
                foreach (var test in cases)
                    WriteMultiMash(sources[1].Directory, "dungeons/cove/" + test.Name, QueryMashes("shadowed_A"));
            if (kind is "local" or "workshop" or "dlc-mod")
            {
                WriteFixtureManifest(source);
                WriteMultiMash(source, prefix + "dungeons/cove/0.conditional.cove.2.mash.darkest", QueryMashes("unlisted_A"));
            }
            var content = QueryContent(root, sources);
            var map = EncounterQueryMap(content);
            var catalog = BattleEncounterCatalog.Load(content, map);
            Assert(catalog.Issues.Count == 0, $"{kind}: collection queries must not introduce file-order uncertainty.");
            foreach (var type in new[] { 0, 1, 2 })
            {
                var standard = catalog.Encounters.Where(row => row.MashType == type && row.SourceKind == BattleEncounterSourceKind.Standard).ToArray();
                Assert(standard.Select(row => row.MashIndex).SequenceEqual(new int?[] { 0, 1, 2, 3 }) &&
                       standard.Select(row => row.MonsterIds.Single()).SequenceEqual(new[] { "actor0_A", "actor1_A", "actor2_A", "actor3_A" }),
                    $"{kind}/{type}: words before the actual table suffix must not remove standard rows or shift indexes.");
                foreach (var row in standard) BattleEncounterCatalog.ValidateDirectEncounter(row);
                Assert(BattleEncounterCatalog.ResolveAppendTarget(catalog, type).NextMashIndex == 4 &&
                       BattleEncounterCatalog.ReadMaintenanceTable(content, "cove", 2, type).Count == 4,
                    "Append and maintenance must use the same independent standard query.");
                foreach (var test in cases)
                {
                    var rows = catalog.Encounters.Where(row => row.MashType == type && Path.GetFileName(row.SourcePath) == test.Name).ToArray();
                    Assert(test.Kind is null ? rows.Length == 0 : rows.Length == 1 && rows[0].SourceKind == test.Kind,
                        $"{kind}/{type}/{test.Name}: collection membership must follow the case-sensitive native suffix query.");
                    if (test.Kind is BattleEncounterSourceKind.Conditional or BattleEncounterSourceKind.Additional)
                        BattleEncounterCatalog.ValidateSpecialEncounter(rows.Single());
                }
            }
            foreach (var row in catalog.BridgeEncounters) BattleEncounterCatalog.ValidateBridgeEncounter(row);
            Assert(catalog.BridgeEncounters.Count == 20, "Only standard hall/room/boss and special hall/room rows are Bridge candidates.");
            var old = catalog.DirectEncounters.First();
            var fingerprint = BattleEncounterCatalog.CaptureContentFingerprint(sources);
            WriteMultiMash(source, prefix + "dungeons/cove/" + cases[5].Name, QueryMashes("actor0_A"));
            Assert(BattleEncounterCatalog.CaptureContentFingerprint(sources) != fingerprint &&
                   await CaptureSaveFailureAsync(() => { BattleEncounterCatalog.ValidateDirectEncounter(old); return Task.CompletedTask; }) is InvalidOperationException,
                "Accepted wildcard special queries participate in fingerprints and stale selection guards.");
        }

        // A region named 'conditional' legitimately asks for the same file in
        // two collections. Query identity must survive the effective-file open.
        var overlapRoot = Path.Combine(runRoot, "encounter-collection-overlap");
        WriteMultiMash(overlapRoot, "dungeons/conditional/a.conditional.2.mash.darkest", QueryMashes("overlap_A"));
        CreateBattleMonsterDefinitions(overlapRoot, ["overlap_A"]);
        var overlapContent = QueryContent(overlapRoot, QuerySources(overlapRoot, "base"));
        var overlap = BattleEncounterCatalog.Load(overlapContent, EncounterQueryMap(overlapContent, "conditional"));
        Assert(overlap.Encounters.Count == 6 && overlap.DirectEncounters.Count == 3 && overlap.SpecialEncounters.Count == 3,
            "Independent native queries can return the same file under different collection identities.");
        foreach (var row in overlap.BridgeEncounters) BattleEncounterCatalog.ValidateBridgeEncounter(row);

        var aliasRoot = Path.Combine(runRoot, "encounter-special-directory-alias");
        WriteMultiMash(aliasRoot, "dungeons/Cove/a.conditional.2.mash.darkest", QueryMashes("alias_A"));
        CreateBattleMonsterDefinitions(aliasRoot, ["alias_A"]);
        var aliasContent = QueryContent(aliasRoot, QuerySources(aliasRoot, "base"));
        var alias = BattleEncounterCatalog.Load(aliasContent, EncounterQueryMap(aliasContent));
        foreach (var row in alias.SpecialEncounters.Where(row => row.MashType is 0 or 1))
        {
            BattleEncounterCatalog.ValidateSpecialEncounter(row);
            BattleEncounterCatalog.ValidateBridgeEncounter(row);
        }
        Assert(alias.SpecialEncounters.Count == 3, "A special query uses the requested region even when the physical directory is a Windows alias.");
        Console.WriteLine("PASS: six-source standard/conditional/additional query identity, keyword decoys, wildcards, overlapping queries and guards.");
    }

    private static async Task VerifyRepeatedEncounterSourcesAsync(string runRoot)
    {
        foreach (var kind in new[] { "local", "workshop" })
        {
            var root = Path.Combine(runRoot, "encounter-repeated-source", kind);
            var game = Path.Combine(root, "game");
            var mod = Path.Combine(root, "mod");
            var high = Path.Combine(root, "high");
            WriteMultiMash(game, "dungeons/cove/a.cove.2.mash.darkest", QueryMashes("native_A"));
            foreach (var table in new[] { "ruins", "conditional", "additional" })
            {
                var path = $"dungeons/ruins/a.{table}.2.mash.darkest";
                WriteMultiMash(game, path, QueryMashes("native_A"));
                WriteMultiMash(game, "dungeons/ruins/archive/" + path, QueryMashes("native_A"));
                WriteMultiMash(mod, path, QueryMashes("foreign_A"));
            }
            CreateBattleMonsterDefinitions(game, ["native_A", "foreign_A"]);
            WriteFixtureManifest(mod);
            WriteMultiMash(high, "modfiles.txt", "");
            var content = QueryContent(root, [new("base", "Base", "base", game, 0),
                new("mod", "Mod", kind, mod, 1), new("high", "High", kind, high, 0)]);
            var catalog = BattleEncounterCatalog.Load(content, EncounterQueryMap(content));
            var foreign = catalog.BridgeEncounters.Where(row => row.OriginDungeonId == "ruins").ToArray();
            Assert(foreign.Length == 14 && foreign.All(row => row.MonsterIds.SequenceEqual(["foreign_A"])),
                "Each source record occurs twice: three standard types and two types in each special collection.");
            foreach (var row in foreign) BattleEncounterCatalog.ValidateBridgeEncounter(row);
            var local = BattleEncounterCatalog.Load(content, EncounterQueryMap(content, "ruins"));
            foreach (var row in local.SpecialEncounters) BattleEncounterCatalog.ValidateSpecialEncounter(row);
            foreach (var type in new[] { 0, 1, 2 })
            {
                var rows = local.DirectEncounters.Where(row => row.MashType == type).ToArray();
                Assert(rows.Select(row => row.MashIndex).SequenceEqual(new int?[] { 0, 1 }) &&
                       BattleEncounterCatalog.ResolveAppendTarget(local, type).NextMashIndex == 2 &&
                       BattleEncounterCatalog.ReadMaintenanceTable(content, "ruins", 2, type).Count == 2,
                    "Allowing repeated Bridge sources must not deduplicate real runtime slots.");
                foreach (var row in rows) BattleEncounterCatalog.ValidateDirectEncounter(row);
            }
            var selected = foreign.First(row => row.SourceKind == BattleEncounterSourceKind.Standard && row.MashType == 0);
            foreach (var forged in new[] { selected with { SourceSha256 = new string('0', 64) },
                selected with { SourceRecordIndex = selected.SourceRecordIndex + 1 }, selected with { SourceLine = selected.SourceLine + 1 },
                selected with { MonsterIds = ["native_A"] }, selected with { SourceKind = BattleEncounterSourceKind.Additional },
                selected with { OriginDungeonId = "cove" }, selected with { SourcePath = Path.Combine(game, selected.SourceRelativePath) } })
                Assert(await CaptureSaveFailureAsync(() => { BattleEncounterCatalog.ValidateBridgeEncounter(forged); return Task.CompletedTask; }) is InvalidOperationException,
                    "Multiplicity tolerance must still verify query, provider, hash, record and composition.");
            File.AppendAllText(selected.SourcePath, "\n");
            Assert(await CaptureSaveFailureAsync(() => { BattleEncounterCatalog.ValidateBridgeEncounter(selected); return Task.CompletedTask; }) is InvalidOperationException,
                "A foreign file's changed hash must be rejected even when the destination guard did not change.");
            var refreshed = BattleEncounterCatalog.Load(content, EncounterQueryMap(content)).BridgeEncounters.First(row =>
                row.OriginDungeonId == "ruins" && row.SourceKind == BattleEncounterSourceKind.Standard && row.MashType == 0);
            WriteMultiMash(high, selected.SourceRelativePath, QueryMashes("native_A"));
            WriteFixtureManifest(high);
            Assert(await CaptureSaveFailureAsync(() => { BattleEncounterCatalog.ValidateBridgeEncounter(refreshed); return Task.CompletedTask; }) is InvalidOperationException,
                "A newly effective higher-priority provider must invalidate the original source selection.");
        }
        Console.WriteLine("PASS: repeated local/Workshop sources, special validation, retained counts, tamper/hash and changed-provider rejection.");
    }

    private static async Task VerifyOmittedEncounterTypesAsync(string runRoot)
    {
        foreach (var kind in new[] { "base", "mode", "dlc-feature", "local", "workshop", "dlc-mod" })
        foreach (var (name, body) in new[] { ("omitted", ""), ("bare", " .types"), ("space", " .types \t "), ("quoted", " .types \"\"") })
        {
            var root = Path.Combine(runRoot, "encounter-empty-types", kind, name);
            var source = Path.Combine(root, "source");
            var prefix = kind == "dlc-mod" ? "dlc/rq_feature/" : "";
            const string path = "dungeons/cove/a.cove.2.mash.darkest";
            var text = QueryMashes("alpha_A") + string.Join('\n', new[] { "hall", "room", "boss" }.Select(type => $"{type}: .chance 0{body}")) +
                       "\n\n" + QueryMashes("bravo_A");
            WriteMultiMash(source, prefix + path, text);
            CreateBattleMonsterDefinitions(source, ["alpha_A", "bravo_A"]);
            var sources = QuerySources(source, kind);
            if (kind == "dlc-mod") WriteMultiMash(sources[1].Directory, path, text);
            if (kind is "local" or "workshop" or "dlc-mod") WriteFixtureManifest(source);
            var content = QueryContent(root, sources);
            var catalog = BattleEncounterCatalog.Load(content, EncounterQueryMap(content));
            foreach (var type in new[] { 0, 1, 2 })
            {
                var rows = catalog.Encounters.Where(row => row.MashType == type).ToArray();
                Assert(rows.Select(row => row.MashIndex).SequenceEqual(new int?[] { 0, 1, 2 }) &&
                       rows[1].MonsterIds.Count == 0 && !rows[1].CanPlaceDirectly && rows[2].MonsterIds.SequenceEqual(["bravo_A"]),
                    $"{kind}/{name}/{type}: an empty record retains index one; the following formation remains index two.");
                BattleEncounterCatalog.ValidateDirectEncounter(rows[2]);
                Assert(BattleEncounterCatalog.ResolveAppendTarget(catalog, type).NextMashIndex == 3 &&
                       BattleEncounterCatalog.ReadMaintenanceTable(content, "cove", 2, type).Count == 3,
                    "Missing and valueless .types must not block append or maintenance.");
                Assert(await CaptureSaveFailureAsync(() => { BattleEncounterCatalog.ValidateDirectEncounter(rows[1] with { CanPlaceDirectly = true }); return Task.CompletedTask; }) is InvalidOperationException,
                    "An occupied empty slot never becomes placeable, even through a forged candidate.");
            }
            Assert(catalog.BridgeEncounters.All(row => row.MonsterIds.Count > 0), "Empty formations are not Bridge sources.");
        }
        Console.WriteLine("PASS: six-source omitted/bare/whitespace/quoted-empty types retain native slots for all three battle types.");
    }

    private static async Task VerifyEncounterCollectionPersistenceAsync(string runRoot, DsonSaveCodec codec)
    {
        foreach (var scenario in new[] { "base-query", "mod-query", "repeated", "empty" })
        foreach (var type in new[] { 0, 1, 2 })
        {
            var root = Path.Combine(runRoot, "encounter-collection-binary", scenario, type.ToString());
            var game = Path.Combine(root, "game");
            var mods = Path.Combine(game, "mods");
            var mod = Path.Combine(mods, "query");
            Directory.CreateDirectory(mods);
            var profile = WriteBattleSafetyProfile(Path.Combine(root, "profile"));
            var target = scenario == "mod-query" ? mod : game;
            WriteMultiMash(target, "dungeons/cove/a.conditional.cove.2.mash.darkest", QueryMashes("alpha_A"));
            if (scenario == "empty")
                WriteMultiMash(game, "dungeons/cove/b.additional.cove.2.mash.darkest", string.Join('\n', new[] { "hall", "room", "boss" }.SelectMany(row =>
                    new[] { $"{row}: .chance 0", $"{row}: .chance 0 .types", $"{row}: .chance 0 .types \t ", $"{row}: .chance 0 .types \"\"" })));
            WriteMultiMash(target, "dungeons/cove/z.cove.2.mash.darkest", QueryMashes("bravo_A"));
            const string foreignPath = "dungeons/ruins/a.ruins.2.mash.darkest";
            WriteMultiMash(game, foreignPath, QueryMashes("foreign_A"));
            if (scenario == "repeated")
            {
                WriteMultiMash(game, "dungeons/ruins/archive/" + foreignPath, QueryMashes("alpha_A"));
                WriteMultiMash(mod, foreignPath, QueryMashes("foreign_A"));
            }
            CreateBattleMonsterDefinitions(game, ["alpha_A", "bravo_A", "foreign_A"]);
            var gameSave = Path.Combine(profile.ProfileDirectory, "persist.game.json");
            if (scenario is "mod-query" or "repeated")
            {
                WriteMultiMash(mod, "project.xml", "<project><Title>Encounter Query</Title></project>");
                WriteFixtureManifest(mod);
                var config = JsonNode.Parse(File.ReadAllText(gameSave))!;
                config["base_root"]!["applied_ugcs_1_0"] = new JsonObject { ["0"] = new JsonObject { ["name"] = "Encounter Query", ["source"] = "mod_local_source" } };
                File.WriteAllText(gameSave, config.ToJsonString());
            }
            var raid = JsonNode.Parse(File.ReadAllText(profile.RaidSavePath))!;
            raid["base_root"]!["start_elapsed_time"] = 100;
            raid["base_root"]!["raid_instance"]!["id"] = "collection-query";
            File.WriteAllText(profile.RaidSavePath, raid.ToJsonString());
            foreach (var seed in new[] { gameSave, profile.MapSavePath, profile.RaidSavePath })
            {
                var encoded = Path.Combine(root, "encoded", Path.GetFileName(seed));
                await codec.EncodeAsync(seed, encoded, originalBinaryPath: null);
                File.Copy(encoded, seed, overwrite: true);
            }
            var locations = new SaveEditorLocations(root, Path.Combine(root, "workspace"), Path.Combine(root, "backups"));
            var reader = new BattleMapSnapshotReader(codec);
            var snapshot = await reader.LoadAsync(profile.ProfileDirectory);
            var content = await ActiveContentResolver.ResolveAsync(profile, game, null, mods, codec, locations.WorkspaceDirectory);
            var catalog = BattleEncounterCatalog.Load(content, snapshot);
            var expectedDirect = scenario == "empty" ? 5 : 1;
            var direct = catalog.DirectEncounters.Single(row => row.MashType == type && row.MonsterIds.SequenceEqual(["bravo_A"]));
            Assert(direct.MashIndex == expectedDirect, "The direct candidate must include every earlier query row and empty slot.");
            var area = type == 0 ? "coAB" : type == 1 ? "rooC" : "rooB";
            var tile = type == 0 ? "tile1" : "tile0";
            var maps = new BattleMapEditService(codec, locations);
            await maps.CommitAsync(await maps.PreparePlaceBattleAsync(profile, snapshot, area, tile, direct));
            snapshot = await reader.LoadAsync(profile.ProfileDirectory);
            Assert(snapshot.Areas.Single(a => a.AreaId == area).Tiles.Single(t => t.TileId == tile).MashIndex == expectedDirect,
                "Direct DSON persistence must write the corrected query index.");
            var foreign = catalog.BridgeEncounters.Where(row => row.OriginDungeonId == "ruins" && row.MashType == type).ToArray();
            Assert(foreign.Length == (scenario == "repeated" ? 2 : 1), "The repeated source fixture must retain both native occurrences.");
            var bridge = new ManagedBattleEncounterBridgeService(codec, locations, () => false);
            var added = await bridge.EnsureEncounterAsync(profile, snapshot, content, catalog, foreign.Last(), game, null, mods);
            Assert(added.MashIndex == expectedDirect + 1, "Bridge must append after the full destination table.");
            await maps.CommitAsync(await maps.PreparePlaceBattleAsync(profile, snapshot, area, tile, added.DirectEncounter));
            snapshot = await reader.LoadAsync(profile.ProfileDirectory);
            var saved = snapshot.Areas.Single(a => a.AreaId == area).Tiles.Single(t => t.TileId == tile);
            Assert(saved.MashIndex == expectedDirect + 1 && saved.MashType == type && DsonSaveCodec.IsDson(profile.MapSavePath),
                "Cross-region Bridge replacement must persist its actual type/index through binary decode.");
            var unchanged = await bridge.ReconcileAsync(added.ActiveContent, game, mods);
            Assert(!unchanged.Changed && !unchanged.Deferred, "Stable corrected bindings must not trigger automatic cleanup.");
            await maps.CommitAsync(await maps.PrepareDeleteContentAsync(profile, snapshot, area, tile));
            snapshot = await reader.LoadAsync(profile.ProfileDirectory);
            Assert(snapshot.Areas.Single(a => a.AreaId == area).Tiles.Single(t => t.TileId == tile).Content == BattleMapTileContent.Nothing,
                "Deletion must clear the edited cell after direct/Bridge placement.");
        }
        Console.WriteLine("PASS: 12 DSON scenarios covering collection identity, repeated cross-region Bridge sources, empty slots, replacement, maintenance and deletion.");
    }
}
