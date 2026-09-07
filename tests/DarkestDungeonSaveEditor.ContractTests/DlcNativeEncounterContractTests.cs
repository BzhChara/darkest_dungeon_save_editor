internal static partial class ContractSuite
{
    private static async Task RunDlcNativeEncounterContractsAsync(string runRoot, DsonSaveCodec codec)
    {
        var root = Path.Combine(runRoot, "dlc-native-encounters");
        var game = Path.Combine(root, "game");
        var mod = Path.Combine(game, "mods", "native-root");
        const string prefix = "dlc/100_test/features/probe";
        var dlc = Path.Combine(game, prefix);
        var profile = WriteBattleSafetyProfile(Path.Combine(root, "profile"));
        var gamePath = Path.Combine(profile.ProfileDirectory, "persist.game.json");
        var config = JsonNode.Parse(File.ReadAllText(gamePath))!;
        config["base_root"]!["dlc"] = new JsonObject
        {
            ["0"] = new JsonObject { ["name"] = "probe" },
            ["1"] = new JsonObject { ["name"] = "dreams" }
        };
        config["base_root"]!["applied_ugcs_1_0"] = new JsonObject
            { ["0"] = new JsonObject { ["name"] = "Native Root", ["source"] = "mod_local_source" } };
        File.WriteAllText(gamePath, config.ToJsonString());
        const string common = "dungeons/cove/cove.2.mash.darkest";
        const string extra = "dungeons/cove/b.cove.2.mash.darkest";
        const string tail = "dungeons/cove/z.cove.2.mash.darkest";
        WriteMultiMash(dlc, common, "hall: .chance 1 .types shadowed\nroom: .chance 1 .types shadowed\nboss: .chance 1 .types shadowed\n");
        WriteMultiMash(dlc, extra, "hall: .chance 1 .types dlc_unique\nboss: .chance 1 .types dlc_boss\n");
        WriteMultiMash(Path.Combine(game, "dlc/101_named/features/dreams"), "dungeons/cove/dream.cove.2.mash.darkest",
            "named: .name dream .types shadowed\n");
        WriteMultiMash(dlc, "dungeons/farm/farm.2.mash.darkest", "hall: .chance 1 .types dlc_unique\nroom: .chance 1 .types root_room\n");
        WriteMultiMash(mod, common, "hall: .chance 1 .types root_hall\nroom: .chance 1 .types root_room\nboss: .chance 1 .types root_boss\n");
        WriteMultiMash(mod, tail, "hall: .chance 0.5 .types .types repeated\n" +
            "hall: .chance 0.7.types glued\nhall: .chance 1 .types large large\n" +
            "hall: .chance 1 .types missing_definition\nhall: .chance 1 .types last\n");
        WriteMultiMash(game, "dungeons/ruins/ruins.2.mash.darkest", "hall: .chance 1 .types foreign_hall\n" +
            "room: .chance 1 .types foreign_room\nboss: .chance 1 .types foreign_boss\n");
        File.WriteAllText(Path.Combine(mod, "project.xml"), "<project><Title>Native Root</Title></project>");
        const string unsupported = prefix + "/dungeons/unsupported/unsupported.2.mash.darkest";
        WriteMultiMash(mod, unsupported, "hall: .chance 1 .types root_hall\n");
        File.WriteAllLines(Path.Combine(mod, "modfiles.txt"), [tail + " 1", common + " 1", unsupported + " 1"]);
        CreateBattleMonsterDefinitions(game,
            ["shadowed", "dlc_unique", "dlc_boss", "root_hall", "root_room", "root_boss", "repeated", "glued", "large", "last",
             "foreign_hall", "foreign_room", "foreign_boss"], ["dlc_boss", "root_boss", "foreign_boss"]);
        File.WriteAllText(Directory.EnumerateFiles(Path.Combine(game, "monsters"), "large.info.darkest", SearchOption.AllDirectories).Single(),
            "display: .size 3\n");
        var sourceHashes = Directory.EnumerateFiles(game, "*.mash.darkest", SearchOption.AllDirectories).ToDictionary(path => path, ComputeSha256);
        var locations = new SaveEditorLocations(root, Path.Combine(root, "workspaces"), Path.Combine(root, "backups"));
        var snapshot = await new BattleMapSnapshotReader(codec).LoadAsync(profile.ProfileDirectory);
        var content = await ActiveContentResolver.ResolveAsync(profile, game, null, codec, locations.WorkspaceDirectory);
        var catalog = BattleEncounterCatalog.Load(content, snapshot);
        var halls = catalog.Encounters.Where(row => row.MashType == 0 && row.MashIndex is not null).OrderBy(row => row.MashIndex).ToArray();
        Assert(halls.Select(row => row.MonsterIds.Single()).SequenceEqual(
                ["dlc_unique", "root_hall", "repeated", "glued", "missing_definition", "last"]) &&
               catalog.TableGuard.EffectiveFiles.Where(file => !file.RelativePath.Contains("dream.cove"))
                   .Select(file => file.RelativePath).SequenceEqual([prefix + "/" + extra, common, tail]),
            "Root Mod overlays must replace a DLC-relative path in its original mount slot without double-counting the built-in table.");
        Assert(catalog.BridgeEncounters.All(row => !row.MonsterIds.Contains("shadowed")) &&
               halls[2].Weight == 0.5 && halls[3].Weight == 0.7 && !halls[4].CanPlaceDirectly && halls[5].MashIndex == 5 &&
               catalog.Encounters.Single(row => row.MonsterIds.SequenceEqual(["large", "large"])).MashIndex is null,
            "The last .types and strtod-style chance prefix must match native parsing; oversized rows are skipped while missing definitions retain slots.");
        foreach (var row in catalog.DirectEncounters) BattleEncounterCatalog.ValidateDirectEncounter(row);
        WriteMultiMash(game, common, "hall: .chance 1 .types shadowed\nroom: .chance 1 .types shadowed\nboss: .chance 1 .types shadowed\n");
        sourceHashes.Add(Path.Combine(game, common), ComputeSha256(Path.Combine(game, common)));
        catalog = BattleEncounterCatalog.Load(content, snapshot);
        Assert(catalog.DirectEncounters.Single(row => row.MashType == 0 && row.MashIndex == 0)
                   .MonsterIds.SequenceEqual(["root_hall"]) &&
               catalog.DirectEncounters.Single(row => row.MashType == 0 && row.MashIndex == 1)
                   .MonsterIds.SequenceEqual(["dlc_unique"]) &&
               catalog.TableGuard.EffectiveFiles.First().RelativePath == common,
            "Base/DLC/root-Mod aliases must retain the Base slot and replay the root Mod after the DLC mount.");
        var unsupportedCatalog = BattleEncounterCatalog.Load(content, snapshot with { DungeonId = "unsupported" });
        Assert(unsupportedCatalog.DirectEncounters.Count == 0 &&
               unsupportedCatalog.Encounters.All(row => row.UnavailableReason.Contains("仅 Mod 提供")) &&
               catalog.BridgeEncounters.Any(row => row.OriginDungeonId == "unsupported"),
            "A Mod-only new DLC-relative file can supply a Bridge formation but cannot establish an unproven target index.");
        var service = new ManagedBattleEncounterBridgeService(codec, locations, () => false);
        var maps = new BattleMapEditService(codec, locations);
        ManagedBattleEncounterBridgeResult? result = null;
        foreach (var (type, expectedIndex, localIndex, area, tile) in new[]
        {
            (0, 6, 0, "coAB", "tile1"), (1, 1, 0, "rooC", "tile0"), (2, 2, 0, "rooB", "tile0")
        })
        {
            var source = catalog.BridgeEncounters.Single(row => row.OriginDungeonId == "ruins" && row.MashType == type);
            if (type == 0)
            {
                var package = BattleEncounterBridgeBuilder.Build(catalog, source, Path.Combine(root, "probe"));
                Assert(package.ExpectedMashIndex == 6, "Probe Bridge must count native retained rows rather than authored lines.");
            }
            var old = catalog.Encounters.Where(row => row.MashIndex is not null).ToArray();
            result = await service.EnsureEncounterAsync(profile, snapshot, content, catalog, source, game, null, null);
            var manifest = JsonNode.Parse(File.ReadAllText(result.ManifestPath))!;
            var entry = manifest["Tables"]!.AsArray().SelectMany(table => table!["Entries"]!.AsArray())
                .Single(e => e!["MashType"]!.GetValue<int>() == type)!;
            Assert(result.MashIndex == expectedIndex && entry["FileRowIndex"]!.GetValue<int>() == localIndex &&
                   old.All(row => result.Catalog.Encounters.Single(r => r.MashType == row.MashType && r.MashIndex == row.MashIndex)
                       .MonsterIds.SequenceEqual(row.MonsterIds)),
                "Managed Bridge must append past proven native slots, preserve missing-definition slots, and store independent authored ordinals.");
            content = result.ActiveContent;
            catalog = result.Catalog;
            snapshot = await new BattleMapSnapshotReader(codec).LoadAsync(profile.ProfileDirectory);
            await maps.CommitAsync(await maps.PreparePlaceBattleAsync(profile, snapshot, area, tile, result.DirectEncounter));
            snapshot = await new BattleMapSnapshotReader(codec).LoadAsync(profile.ProfileDirectory);
            Assert(snapshot.Areas.Single(a => a.AreaId == area).Tiles.Single(t => t.TileId == tile).MashIndex == expectedIndex,
                "DLC/normalized-row Bridge bindings must survive DSON map save and reload.");
            var packageHash = ComputeSha256(result.MashFilePath);
            await maps.CommitAsync(await maps.PrepareDeleteContentAsync(profile, snapshot, area, tile));
            snapshot = await new BattleMapSnapshotReader(codec).LoadAsync(profile.ProfileDirectory);
            Assert(ComputeSha256(result.MashFilePath) == packageHash,
                "Deleting a map battle must not remove or renumber its persistent Bridge definition.");
        }
        var coveFiles = Directory.EnumerateFiles(result!.PackageDirectory, "*.mash.darkest", SearchOption.AllDirectories)
            .ToDictionary(path => path, ComputeSha256);
        foreach (var dungeon in new[] { "ruins", "farm", "cove" })
        {
            await SetNativeTestDungeonAsync(profile, codec, root, dungeon);
            content = await ActiveContentResolver.ResolveAsync(profile, game, null, codec, locations.WorkspaceDirectory);
            snapshot = await new BattleMapSnapshotReader(codec).LoadAsync(profile.ProfileDirectory);
            catalog = BattleEncounterCatalog.Load(content, snapshot);
            var source = catalog.BridgeEncounters.First(row => row.MashType == 0 &&
                row.MonsterIds.SequenceEqual(dungeon == "cove" ? ["foreign_hall"] : new[] { "root_hall" }));
            var reuse = await service.EnsureEncounterAsync(profile, snapshot, content, catalog, source, game, null, null);
            Assert(reuse.MashIndex == (dungeon == "cove" ? 6 : 1) && reuse.EncounterWasAdded == (dungeon != "cove"),
                "The same formation has a destination-scoped index; returning to a prior region must reuse its retained entry.");
            if (dungeon == "farm")
                Assert(reuse.DirectEncounter.SourceRelativePath == "dungeons/farm/ddse_managed.farm.2.mash.darkest",
                    "A DLC destination must receive an independent root file after its native rows.");
            Assert(coveFiles.All(pair => ComputeSha256(pair.Key) == pair.Value),
                "Generating another region must retain every prior region table byte-for-byte.");
        }
        Assert(sourceHashes.All(pair => ComputeSha256(pair.Key) == pair.Value),
            "DLC Bridge generation must never rewrite built-in DLC or source Mod files.");
        Console.WriteLine("PASS: native DLC aliases, repeated/glued fields, skipped rows, all Bridge types, and region round-trip retention.");
    }

    private static async Task SetNativeTestDungeonAsync(SaveProfile profile, DsonSaveCodec codec, string root, string dungeon)
    {
        foreach (var name in new[] { "persist.game.json", "persist.raid.json" })
        {
            var path = Path.Combine(profile.ProfileDirectory, name);
            var decoded = Path.Combine(root, dungeon + "-" + name + ".decoded.json");
            var encoded = Path.Combine(root, dungeon + "-" + name + ".encoded.json");
            await codec.DecodeAsync(path, decoded);
            var document = JsonNode.Parse(File.ReadAllText(decoded))!;
            if (name == "persist.game.json") document["base_root"]!["raiddungeon"] = dungeon;
            else document["base_root"]!["raid_instance"]!["dungeon"] = dungeon;
            File.WriteAllText(decoded, document.ToJsonString());
            await codec.EncodeAsync(decoded, encoded, path);
            File.Copy(encoded, path, overwrite: true);
        }
    }
}
