internal static partial class ContractSuite
{
    private static async Task RunMultiFileEncounterContractsAsync(string runRoot, DsonSaveCodec codec)
    {
        var root = Path.Combine(runRoot, "multi-file-encounters");
        var game = Path.Combine(root, "game");
        var mods = Path.Combine(game, "mods");
        var low = Path.Combine(mods, "low");
        var high = Path.Combine(mods, "high");
        var profile = WriteBattleSafetyProfile(Path.Combine(root, "profile"));
        var mapPath = Path.Combine(profile.ProfileDirectory, "persist.map.json");
        var map = JsonNode.Parse(File.ReadAllText(mapPath))!["base_root"]!["map"]!;
        var mapDocument = map.Root;
        var statics = map["static_dynamic"]!["static_save"]!["base_root"]!["areas"]!;
        statics["rooD"] = statics["rooB"]!.DeepClone();
        statics["rooD"]!["id"] = 300;
        statics["rooD"]!["tiles"]!["tile0"]!["mappos"] = new JsonArray(8.0, 0.0);
        var dynamics = map["static_dynamic"]!["areas"]!;
        dynamics["rooD"] = dynamics["rooB"]!.DeepClone();
        map["bounds"] = new JsonArray(0.0, 8.0, 0.0, 0.0);
        File.WriteAllText(mapPath, mapDocument.ToJsonString());
        var gamePath = Path.Combine(profile.ProfileDirectory, "persist.game.json");
        var configuration = JsonNode.Parse(File.ReadAllText(gamePath))!;
        configuration["base_root"]!["applied_ugcs_1_0"] = new JsonObject
        {
            ["0"] = new JsonObject { ["name"] = "Multi High", ["source"] = "mod_local_source" },
            ["1"] = new JsonObject { ["name"] = "Multi Low", ["source"] = "mod_local_source" }
        };
        File.WriteAllText(gamePath, configuration.ToJsonString());
        const string basePath = "dungeons/cove/cove.2.mash.darkest";
        const string nestedPath = "dungeons/cove/nested/z.cove.2.mash.darkest";
        const string aPath = "dungeons/cove/a.cove.2.mash.darkest";
        const string zPath = "dungeons/cove/z.cove.2.mash.darkest";
        const string lastPath = "dungeons/cove/0.cove.2.mash.darkest";
        WriteMultiMash(game, basePath, "hall: .chance 1 .types overwritten\n");
        WriteMultiMash(game, "dungeons/cove/wrong_region.2.mash.darkest", "hall: .chance 1 .types overwritten\n");
        WriteMultiMash(low, aPath, "hall: .chance 1 .types low_a\nroom: .chance 1 .types low_room\n");
        WriteMultiMash(low, nestedPath, "hall: .chance 1 .types nested\n");
        WriteMultiMash(low, zPath, "hall: .chance 1 .types overwritten\n");
        WriteMultiMash(high, basePath,
            "hall: .chance 1 .types base_hall\nhall: .chance 1 .types base_second\n" +
            "room: .chance 1 .types base_room\nboss: .chance 1 .types base_boss\n");
        WriteMultiMash(high, zPath, "hall: .chance 1 .types high_z\nboss: .chance 1 .types tail_boss\n");
        WriteMultiMash(high, lastPath,
            "hall: .chance 1 .types last_hall\nroom: .chance 1 .types last_room\n");
        WriteMultiMash(game, "dungeons/ruins/ruins.2.mash.darkest",
            "hall: .chance 1 .types foreign_hall\nroom: .chance 0 .types foreign_room\n" +
            "boss: .chance 1 .types foreign_boss foreign_hall\n");
        File.WriteAllText(Path.Combine(low, "project.xml"), "<project><Title>Multi Low</Title></project>");
        File.WriteAllText(Path.Combine(high, "project.xml"), "<project><Title>Multi High</Title></project>");
        // Deliberately differs from native depth/strcmp enumeration.
        File.WriteAllLines(Path.Combine(low, "modfiles.txt"), [zPath + " 1", aPath + " 1", nestedPath + " 1"]);
        File.WriteAllLines(Path.Combine(high, "modfiles.txt"), [zPath + " 1", lastPath + " 1", basePath + " 1"]);
        CreateBattleMonsterDefinitions(game,
            ["overwritten", "base_hall", "base_second", "base_room", "base_boss", "nested", "low_a",
             "low_room", "high_z", "tail_boss", "last_hall", "last_room", "foreign_hall", "foreign_room", "foreign_boss"],
            ["base_boss", "tail_boss", "foreign_boss"]);
        var locations = new SaveEditorLocations(root, Path.Combine(root, "workspaces"), Path.Combine(root, "backups"));
        var snapshot = await new BattleMapSnapshotReader(codec).LoadAsync(profile.ProfileDirectory);
        var content = await ActiveContentResolver.ResolveAsync(profile, game, null, mods, codec, locations.WorkspaceDirectory);
        var catalog = BattleEncounterCatalog.Load(content, snapshot);
        Assert(catalog.TableGuard.EffectiveFiles.Select(file => file.RelativePath)
                   .SequenceEqual([basePath, nestedPath, aPath, zPath, lastPath]) &&
               catalog.DirectEncounters.Where(row => row.MashType == 0).Select(row => row.MonsterIds.Single())
                   .SequenceEqual(["base_hall", "base_second", "nested", "low_a", "high_z", "last_hall"]) &&
               catalog.DirectEncounters.Where(row => row.MashType == 1).Select(row => row.MonsterIds.Single())
                   .SequenceEqual(["base_room", "low_room", "last_room"]) &&
               catalog.DirectEncounters.Where(row => row.MashType == 2).Select(row => row.MonsterIds.Single())
                   .SequenceEqual(["base_boss", "tail_boss"]),
            "Native order must retain first-provider slots through overrides, recurse deepest first, and append each Mod's distinct files after lower priority sources.");
        foreach (var row in catalog.DirectEncounters) BattleEncounterCatalog.ValidateDirectEncounter(row);

        // An added shadowed file changes the original slot, even though no winning bytes change.
        var shadowPath = WriteMultiMash(game, lastPath, "hall: .chance 1 .types overwritten\n");
        var shadowFailure = await CaptureSaveFailureAsync(() =>
        {
            BattleEncounterCatalog.ValidateGuard(catalog.TableGuard);
            return Task.CompletedTask;
        });
        Assert(shadowFailure is InvalidOperationException,
            "Ordered fingerprints must detect slot changes caused only by a newly shadowed provider.");
        File.Delete(shadowPath);
        File.WriteAllLines(Path.Combine(low, "modfiles.txt"), [nestedPath + " 1", zPath + " 1", aPath + " 1"]);
        BattleEncounterCatalog.ValidateGuard(catalog.TableGuard);

        await VerifyNativeEncounterRowsAsync(root, game, high, lastPath, snapshot, content);
        catalog = BattleEncounterCatalog.Load(content, snapshot);
        var originalIndexes = catalog.DirectEncounters.ToArray();
        var sourceHashes = Directory.EnumerateFiles(game, "*.mash.darkest", SearchOption.AllDirectories)
            .ToDictionary(path => path, ComputeSha256);
        var service = new ManagedBattleEncounterBridgeService(codec, locations, () => false);
        var mapService = new BattleMapEditService(codec, locations);
        ManagedBattleEncounterBridgeResult? result = null;
        foreach (var (mashType, monster, expectedPath, expectedIndex, expectedLocalIndex) in new[]
        {
            (0, "foreign_hall", "dungeons/cove/ddse_managed.cove.2.mash.darkest", 6, 0),
            (1, "foreign_room", "dungeons/cove/ddse_managed.cove.2.mash.darkest", 3, 0),
            (2, "foreign_boss", "dungeons/cove/ddse_managed.cove.2.mash.darkest", 2, 0)
        })
        {
            var source = catalog.BridgeEncounters.Single(row => row.OriginDungeonId == "ruins" && row.MonsterIds[0] == monster);
            if (result is null)
            {
                var probe = BattleEncounterBridgeBuilder.Build(catalog, source, Path.Combine(root, "probe"));
                Assert(probe.ExpectedMashIndex == expectedIndex && probe.MashFilePath.EndsWith(
                        expectedPath.Replace('/', Path.DirectorySeparatorChar), StringComparison.Ordinal),
                    "Manual probe generation must share the managed Bridge's dedicated path and global index.");
            }
            result = await service.EnsureEncounterAsync(profile, snapshot, content, catalog, source, game, null, mods);
            Assert(result.MashIndex == expectedIndex && result.DirectEncounter.SourceRelativePath == expectedPath &&
                   result.DirectEncounter.Classification == source.Classification && result.DirectEncounter.Weight == 0,
                "Each Bridge type must use a dedicated file, its global index and the original classification.");
            var manifest = JsonNode.Parse(File.ReadAllText(result.ManifestPath))!;
            var entry = manifest["Tables"]!.AsArray().SelectMany(table => table!["Entries"]!.AsArray())
                .Single(node => node!["MashType"]!.GetValue<int>() == mashType)!;
            Assert(entry["FileRowIndex"]!.GetValue<int>() == expectedLocalIndex &&
                   entry["MashIndex"]!.GetValue<int>() == expectedIndex,
                "Manifest file-local ordinals must remain distinct from runtime mash indexes.");
            foreach (var original in originalIndexes)
            {
                Assert(result.Catalog.DirectEncounters.Single(row => row.MashType == original.MashType &&
                           row.MashIndex == original.MashIndex).MonsterIds.SequenceEqual(original.MonsterIds),
                    "Appending any type must preserve every existing type/index pair.");
            }
            content = result.ActiveContent;
            catalog = result.Catalog;
            snapshot = await new BattleMapSnapshotReader(codec).LoadAsync(profile.ProfileDirectory);
            var reuseSource = catalog.BridgeEncounters.Single(row => row.OriginDungeonId == "ruins" && row.MonsterIds[0] == monster);
            var packageHash = ComputeSha256(result.MashFilePath);
            var reused = await service.EnsureEncounterAsync(profile, snapshot, content, catalog, reuseSource, game, null, mods);
            Assert(!reused.EncounterWasAdded && !reused.ProfileConfigurationChanged && reused.MashIndex == expectedIndex &&
                   ComputeSha256(result.MashFilePath) == packageHash,
                "Repeated multi-file selections must reuse their recorded entry without duplicating or renumbering rows.");
            content = reused.ActiveContent;
            catalog = reused.Catalog;
            var area = mashType switch { 0 => "coAB", 1 => "rooD", _ => "rooB" };
            var tile = mashType == 0 ? "tile1" : "tile0";
            foreach (var selection in new[] { catalog.DirectEncounters.First(row => row.MashType == mashType), reused.DirectEncounter })
            {
                var prepared = await mapService.PreparePlaceBattleAsync(profile, snapshot, area, tile, selection);
                await mapService.CommitAsync(prepared);
                snapshot = await new BattleMapSnapshotReader(codec).LoadAsync(profile.ProfileDirectory);
                var placed = snapshot.Areas.Single(item => item.AreaId == area).Tiles.Single(item => item.TileId == tile);
                Assert(placed.MashType == mashType && placed.MashIndex == selection.MashIndex,
                    "New and replacement map battles must persist the selected global type/index through DSON reload.");
            }
            await mapService.CommitAsync(await mapService.PrepareDeleteContentAsync(profile, snapshot, area, tile));
            snapshot = await new BattleMapSnapshotReader(codec).LoadAsync(profile.ProfileDirectory);
            Assert(snapshot.Areas.Single(item => item.AreaId == area).Tiles.Single(item => item.TileId == tile).Content == BattleMapTileContent.Nothing &&
                   snapshot.Areas.Single(item => item.AreaId == "rooC").Tiles.Single().MashIndex == 0,
                "Deleting a multi-file battle must clear its tile while preserving unrelated saved battles.");
        }
        Assert(sourceHashes.All(pair => ComputeSha256(pair.Key) == pair.Value),
            "Managed Bridge operations must preserve the bytes of base and source Mod mash files.");
        var finalGame = Path.Combine(root, "decoded-final-game.json");
        await codec.DecodeAsync(gamePath, finalGame);
        var applied = JsonNode.Parse(File.ReadAllText(finalGame))!["base_root"]!["applied_ugcs_1_0"]!;
        Assert(applied["0"]!["name"]!.GetValue<string>() == result!.ProjectTitle &&
               applied["1"]!["name"]!.GetValue<string>() == "Multi High" &&
               applied["2"]!["name"]!.GetValue<string>() == "Multi Low",
            "Bridge activation must retain the original relative priority of enabled Mods.");
        Console.WriteLine("PASS: multi-file native ordering, overlays, guards, all three Bridge types, reuse, and map create/replace/delete.");
    }

    private static string WriteMultiMash(string root, string relativePath, string text)
    {
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text, new UTF8Encoding(false));
        return path;
    }

    private static async Task VerifyNativeEncounterRowsAsync(string root, string game, string high, string lastPath,
        BattleMapSnapshot snapshot, ActiveContentSnapshot content)
    {
        var path = Path.Combine(high, lastPath.Replace('/', Path.DirectorySeparatorChar));
        var original = File.ReadAllBytes(path);
        // Native reads four slots; an unresolved fifth ID is not a missing dependency.
        File.AppendAllText(path, "room: .chance 1 .types base_hall low_a high_z last_hall ignored_fifth\n");
        var truncated = BattleEncounterCatalog.Load(content, snapshot);
        var four = truncated.DirectEncounters.Single(row => row.MashType == 1 && row.MashIndex == 3);
        Assert(four.MonsterIds.SequenceEqual(["base_hall", "low_a", "high_z", "last_hall"]) &&
               truncated.Issues.Any(issue => issue.Contains("只读取前四个", StringComparison.Ordinal)),
            "Rows with five authored IDs must use exactly the four native slots and disclose the ignored suffix.");
        BattleEncounterCatalog.ValidateDirectEncounter(four);
        File.WriteAllBytes(path, original);
        File.AppendAllText(path, "room: .chance 1 .types base_hall \"\" low_a high_z last_hall\n");
        var sparse = BattleEncounterCatalog.Load(content, snapshot);
        Assert(sparse.DirectEncounters.Single(row => row.MashType == 1 && row.MashIndex == 3).MonsterIds
                   .SequenceEqual(["base_hall", "low_a", "high_z"]),
            "Empty quoted actor slots count toward the native four-slot limit; they must not pull a fifth actor into the formation.");
        BattleEncounterCatalog.ValidateDirectEncounter(sparse.DirectEncounters.Single(row => row.MashType == 1 && row.MashIndex == 3));
        File.WriteAllBytes(path, original);
        CreateBattleMonsterDefinitions(game, ["large"], []);
        var sizePath = Directory.EnumerateFiles(Path.Combine(game, "monsters"), "large.info.darkest", SearchOption.AllDirectories).Single();
        File.WriteAllText(sizePath, "display: .size 3\n");
        File.AppendAllText(path, "hall: .chance 1 .types large large\nhall: .chance 1 .types low_a\n");
        var skipped = BattleEncounterCatalog.Load(content, snapshot);
        Assert(skipped.DirectEncounters.Count(row => row.MashType == 0) == 7 &&
               skipped.DirectEncounters.Single(row => row.MashType == 0 && row.MashIndex == 6).MonsterIds.Single() == "low_a" &&
               skipped.Encounters.Single(row => row.MonsterIds.SequenceEqual(["large", "large"])).MashIndex is null,
            "A known oversized row is dropped by native AddMashEntry and must not consume or invalidate later indexes.");
        BattleEncounterCatalog.ValidateDirectEncounter(skipped.DirectEncounters.Single(row => row.MashType == 0 && row.MashIndex == 6));
        File.WriteAllText(sizePath, "display: .size 3 .size 1\n");
        var ambiguousSize = BattleEncounterCatalog.Load(content, snapshot);
        Assert(ambiguousSize.Encounters.Where(row => row.MashType == 0).All(row => row.MashIndex is null) &&
               ambiguousSize.DirectEncounters.Any(row => row.MashType == 1),
            "Repeated monster size fields must stay guarded instead of skipping a row using the first size and shifting later indexes.");
        var staleSize = await CaptureSaveFailureAsync(() =>
        {
            BattleEncounterCatalog.ValidateDirectEncounter(skipped.DirectEncounters.Single(row => row.MashType == 0 && row.MashIndex == 6));
            return Task.CompletedTask;
        });
        Assert(staleSize is InvalidOperationException, "A repeated-size change must invalidate a previously proven later index.");
        File.WriteAllText(sizePath, "display: .size \"3\"\n");
        var quotedSize = BattleEncounterCatalog.Load(content, snapshot);
        Assert(quotedSize.Encounters.Where(row => row.MashType == 0).All(row => row.MashIndex is null),
            "Quoted numeric sizes must not become proven sizes through string-token unquoting and incorrectly skip a native row.");
        File.WriteAllText(sizePath, "display: .size 3\n");
        File.WriteAllBytes(path, original);
        foreach (var suffix in new[] { "hall: .chance 1\n" })
        {
            File.AppendAllText(path, suffix);
            var invalid = BattleEncounterCatalog.Load(content, snapshot);
            Assert(invalid.DirectEncounters.All(row => row.MashType != 0) &&
                   invalid.DirectEncounters.Any(row => row.MashType == 1) &&
                   invalid.DirectEncounters.Any(row => row.MashType == 2),
                "Unproven rows must withhold affected indexes without disabling independent types.");
            BattleEncounterCatalog.ValidateDirectEncounter(invalid.DirectEncounters.First(row => row.MashType == 1));
            BattleEncounterCatalog.ValidateDirectEncounter(invalid.DirectEncounters.First(row => row.MashType == 2));
            var source = invalid.BridgeEncounters.Single(row => row.OriginDungeonId == "ruins" && row.MashType == 0);
            var destination = Path.Combine(root, "invalid-probe-" + Guid.NewGuid().ToString("N"));
            var error = await CaptureSaveFailureAsync(() =>
            {
                BattleEncounterBridgeBuilder.Build(invalid, source, destination);
                return Task.CompletedTask;
            });
            Assert(error is InvalidOperationException && !Directory.Exists(destination),
                "An unproven destination index must reject Bridge creation before output is written.");
            File.WriteAllBytes(path, original);
        }
        // A size update can invalidate native row count even if mash bytes and Mod order stay the same.
        var before = BattleEncounterCatalog.Load(content, snapshot);
        var sourceBefore = before.BridgeEncounters.Single(row => row.OriginDungeonId == "ruins" && row.MashType == 2);
        var foreignSize = Directory.EnumerateFiles(Path.Combine(game, "monsters"), "foreign_boss.info.darkest", SearchOption.AllDirectories).Single();
        var foreignBytes = File.ReadAllBytes(foreignSize);
        File.WriteAllText(foreignSize, "display: .size 4\n");
        var changedSize = await CaptureSaveFailureAsync(() =>
        {
            BattleEncounterCatalog.ValidateBridgeEncounter(sourceBefore);
            return Task.CompletedTask;
        });
        Assert(changedSize is InvalidOperationException,
            "Bridge commit validation must recheck monster sizes instead of trusting an earlier candidate list.");
        File.WriteAllBytes(foreignSize, foreignBytes);

        var conditionalPath = WriteMultiMash(game, "dungeons/cove/cove.conditional.2.mash.darkest", "hall: .chance 1\n");
        var separateCollections = BattleEncounterCatalog.Load(content, snapshot);
        Assert(separateCollections.DirectEncounters.Count(row => row.MashType == 0) == 6,
            "Malformed conditional rows must not affect the independent standard hall table.");
        BattleEncounterCatalog.ValidateDirectEncounter(separateCollections.DirectEncounters.First(row => row.MashType == 0));
        File.Delete(conditionalPath);

        var dlcRoot = Path.Combine(root, "dlc-feature");
        WriteMultiMash(dlcRoot, "dungeons/cove/dlc.cove.2.mash.darkest", "hall: .chance 1 .types base_hall\n");
        var otherDlcRoot = Path.Combine(root, "other-dlc-feature");
        WriteMultiMash(otherDlcRoot, "dungeons/cove/other.cove.2.mash.darkest", "hall: .chance 1 .types base_hall\n");
        var dlcContent = content with
        {
            Sources = content.Sources.Concat([new ActiveContentSource("dlc:test", "test DLC", "dlc-feature", dlcRoot, 20)
            { VirtualPathPrefix = "dlc/123_pack/features/test" },
            new ActiveContentSource("dlc:other", "other DLC", "dlc-feature", otherDlcRoot, 21)
            { VirtualPathPrefix = "dlc/124_pack/features/other" }]).ToArray()
        };
        var dlcCatalog = BattleEncounterCatalog.Load(dlcContent, snapshot);
        Assert(dlcCatalog.DirectEncounters.All(row => row.MashType != 0) &&
               dlcCatalog.Encounters.Any(row => row.UnavailableReason.Contains("多个 DLC", StringComparison.Ordinal)),
            "One-DLC mount evidence must not silently authorize the relative order of multiple DLC mounts.");

        var emptyRoot = Path.Combine(root, "empty-type-game");
        WriteMultiMash(emptyRoot, "dungeons/cove/cove.2.mash.darkest", "hall: .chance 1 .types present\nroom: .chance 1\n");
        WriteMultiMash(emptyRoot, "dungeons/ruins/ruins.2.mash.darkest", "room: .chance 1 .types present\n");
        CreateBattleMonsterDefinitions(emptyRoot, ["present"]);
        var emptyCatalog = BattleEncounterCatalog.Load(content with
        {
            Sources = [new ActiveContentSource("base", "base", "base", emptyRoot, 0)]
        }, snapshot);
        var emptyOutput = Path.Combine(root, "unproven-empty-type");
        var emptyFailure = await CaptureSaveFailureAsync(() =>
        {
            BattleEncounterBridgeBuilder.Build(emptyCatalog, emptyCatalog.BridgeEncounters.Single(row => row.MashType == 1), emptyOutput);
            return Task.CompletedTask;
        });
        Assert(emptyFailure is InvalidOperationException && !Directory.Exists(emptyOutput),
            "A skipped malformed row must not make native index zero appear available for the first Bridge entry.");

        var caseRoot = Path.Combine(root, "case-order-game");
        var caseMod = Path.Combine(root, "case-order-mod");
        WriteMultiMash(caseRoot, "dungeons/cove/Z.cove.2.mash.darkest", "hall: .chance 1 .types first\n");
        WriteMultiMash(caseRoot, "dungeons/cove/a.cove.2.mash.darkest", "hall: .chance 1 .types second\n");
        WriteMultiMash(caseMod, "dungeons/cove/z.cove.2.mash.darkest", "hall: .chance 1 .types override\n");
        CreateBattleMonsterDefinitions(caseRoot, ["first", "second", "override"]);
        var caseCatalog = BattleEncounterCatalog.Load(content with
        {
            Sources = [new ActiveContentSource("base", "base", "base", caseRoot, 0),
                new ActiveContentSource("local:case", "case", "local", caseMod, 1000)]
        }, snapshot);
        Assert(caseCatalog.TableGuard.EffectiveFiles.Select(file => Path.GetFileName(file.RelativePath))
                   .SequenceEqual(["z.cove.2.mash.darkest", "a.cove.2.mash.darkest"]) &&
               caseCatalog.DirectEncounters.Count == 0 &&
               caseCatalog.Issues.Any(issue => issue.Contains("大小写", StringComparison.Ordinal)),
            "An override must retain the first provider's case-sensitive sort slot; case-only overlay collisions stay guarded until native matching is proven.");
    }
}
