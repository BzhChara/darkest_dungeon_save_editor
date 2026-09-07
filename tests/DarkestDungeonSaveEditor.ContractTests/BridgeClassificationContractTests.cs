internal static partial class ContractSuite
{
    private static async Task RunBridgeClassificationContractsAsync(string runRoot, DsonSaveCodec codec)
    {
        var root = Path.Combine(runRoot, "bridge-classification");
        var game = Path.Combine(root, "game");
        var localMods = Path.Combine(game, "mods");
        var profileRoot = Path.Combine(root, "profile_classification");
        Directory.CreateDirectory(localMods);
        Directory.CreateDirectory(profileRoot);
        foreach (var dungeon in new[] { "cove", "ruins" })
        {
            Directory.CreateDirectory(Path.Combine(game, "dungeons", dungeon));
        }
        File.WriteAllText(Path.Combine(game, "dungeons", "cove", "cove.2.mash.darkest"),
            "hall: .chance 1 .types native_probe\n", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(game, "dungeons", "ruins", "ruins.2.mash.darkest"),
            "hall: .chance 1 .types ordinary_probe\n" +
            "hall: .chance 0 .types authored_zero_probe\n" +
            "hall: .chance 1 .types roaming_probe .random_dungeon_roaming_id roaming_test\n",
            new UTF8Encoding(false));
        CreateBattleMonsterDefinitions(game,
            ["native_probe", "ordinary_probe", "authored_zero_probe", "roaming_probe"], ["roaming_probe"]);
        var gamePath = Path.Combine(profileRoot, "persist.game.json");
        File.WriteAllText(gamePath,
            """{"base_root":{"inraid":true,"raiddungeon":"cove","game_mode":"base","applied_ugcs_1_0":{}}}""",
            new UTF8Encoding(false));
        var mapPath = Path.Combine(profileRoot, "persist.map.json");
        WriteBattleSafetyProfile(profileRoot);
        var mapHash = ComputeSha256(mapPath);
        var profile = new SaveProfile("profile_classification", profileRoot,
            Path.Combine(profileRoot, "persist.estate.json"), "contract-user", DateTime.UtcNow);
        var snapshot = await new BattleMapSnapshotReader(codec).LoadAsync(profileRoot);
        var locations = new SaveEditorLocations(root, Path.Combine(root, "workspaces"), Path.Combine(root, "backups"));
        var content = await ActiveContentResolver.ResolveAsync(profile, game, null, localMods, codec, locations.WorkspaceDirectory);
        var catalog = BattleEncounterCatalog.Load(content, snapshot);
        var service = new ManagedBattleEncounterBridgeService(codec, locations, () => false);
        await VerifyBridgePreflightContractsAsync(profile, snapshot, content, catalog, service, game, localMods, codec);
        await VerifyBridgeReplacementContractsAsync(runRoot, game, codec);
        ManagedBattleEncounterBridgeResult? last = null;
        foreach (var (monsterId, expected) in new[]
        {
            ("ordinary_probe", BattleEncounterClassification.Ordinary),
            ("authored_zero_probe", BattleEncounterClassification.ConditionalOrAdditional),
            ("roaming_probe", BattleEncounterClassification.RoamingBoss)
        })
        {
            var source = catalog.BridgeEncounters.Single(row => row.OriginDungeonId == "ruins" && row.MonsterIds.SequenceEqual([monsterId]));
            last = await service.EnsureEncounterAsync(profile, snapshot, content, catalog, source, game, null, localMods);
            Assert(last.DirectEncounter.Classification == expected && last.DirectEncounter.HasKnownClassification &&
                   last.DirectEncounter.Weight == 0,
                "A managed zero-weight carrier must preserve ordinary, authored zero-weight special, and roaming-boss classifications.");
            content = last.ActiveContent;
            catalog = last.Catalog;
        }
        Assert(catalog.DirectEncounters.Single(row => row.MashIndex == 0).MonsterIds.SequenceEqual(["native_probe"]) &&
               catalog.BridgeEncounters.Where(row => row.MonsterIds.SequenceEqual(["ordinary_probe"]))
                   .All(row => row.Classification == BattleEncounterClassification.Ordinary),
            "Appending classifications must keep native indexes stable and must not reclassify ordinary picker groups as special.");

        var manifestPath = last!.ManifestPath;
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        var entries = manifest["Tables"]![0]!["Entries"]!.AsArray();
        Assert(entries.Select(entry => entry!["Classification"]!.GetValue<string>())
                .SequenceEqual(["Ordinary", "ConditionalOrAdditional", "RoamingBoss"]),
            "New managed entries must store the original selected classification, independently of carrier weight.");
        foreach (var entry in entries)
        {
            entry!.AsObject().Remove("Classification");
        }
        File.WriteAllText(manifestPath, manifest.ToJsonString(), new UTF8Encoding(false));
        var legacyManifestHash = ComputeSha256(manifestPath);
        var mashHash = ComputeSha256(last.MashFilePath);
        var legacyCatalog = BattleEncounterCatalog.Load(content, snapshot);
        Assert(legacyCatalog.DirectEncounters.Single(row => row.MashIndex == 1).Classification == BattleEncounterClassification.Ordinary &&
               legacyCatalog.DirectEncounters.Single(row => row.MashIndex == 2).Classification == BattleEncounterClassification.ConditionalOrAdditional &&
               legacyCatalog.DirectEncounters.Single(row => row.MashIndex == 3).Classification == BattleEncounterClassification.RoamingBoss &&
               ComputeSha256(manifestPath) == legacyManifestHash && ComputeSha256(last.MashFilePath) == mashHash,
            "Entries without optional classification metadata must recover from original rows/roaming metadata without rewriting the package or losing their separate local ordinal.");

        entries[0]!["SourceRelativePath"] = "missing-original.mash.darkest";
        File.WriteAllText(manifestPath, manifest.ToJsonString(), new UTF8Encoding(false));
        var unresolvedCatalog = BattleEncounterCatalog.Load(content, snapshot);
        Assert(!unresolvedCatalog.Encounters.Single(row => row.MashIndex == 1).HasKnownClassification &&
               unresolvedCatalog.BridgeEncounters.All(row => row.OriginDungeonId != "cove" || !row.MonsterIds.SequenceEqual(["ordinary_probe"])) &&
               unresolvedCatalog.Issues.Any(issue => issue.Contains("无法确认原始分类", StringComparison.Ordinal)) &&
               ComputeSha256(mapPath) == mapHash && ComputeSha256(last.MashFilePath) == mashHash,
            "An unresolved legacy carrier must not silently masquerade as a special encounter or alter an existing map/index.");

        var badHashManifest = manifest.DeepClone().AsObject();
        badHashManifest["Tables"]![0]!["GeneratedMashSha256"] = new string('0', 64);
        var badIndexManifest = manifest.DeepClone().AsObject();
        badIndexManifest["Tables"]![0]!["Entries"]![0]!["MashIndex"] = 99;
        var missingLocalIndexManifest = manifest.DeepClone().AsObject();
        missingLocalIndexManifest["Tables"]![0]!["Entries"]![0]!.AsObject().Remove("FileRowIndex");
        var invalidRegionManifest = manifest.DeepClone().AsObject();
        invalidRegionManifest["Tables"]![0]!["DungeonId"] = "../cove";
        foreach (var invalidManifest in new[] { "{", badHashManifest.ToJsonString(), badIndexManifest.ToJsonString(),
                     missingLocalIndexManifest.ToJsonString(), invalidRegionManifest.ToJsonString() })
        {
            File.WriteAllText(manifestPath, invalidManifest, new UTF8Encoding(false));
            var invalidManifestHash = ComputeSha256(manifestPath);
            var invalidCatalog = BattleEncounterCatalog.Load(content, snapshot);
            Assert(invalidCatalog.DirectEncounters.Single(row => row.MashIndex == 0).HasKnownClassification &&
                   invalidCatalog.Encounters.Where(row => row.MashIndex > 0).All(row => !row.HasKnownClassification) &&
                   invalidCatalog.BridgeEncounters.Where(row => row.SourcePath == last.MashFilePath).All(row => row.Weight > 0) &&
                   invalidCatalog.Issues.Any(issue => issue.Contains("分类读取失败", StringComparison.Ordinal)) &&
                   ComputeSha256(manifestPath) == invalidManifestHash && ComputeSha256(mapPath) == mapHash &&
                   ComputeSha256(last.MashFilePath) == mashHash,
                "Malformed JSON, mismatched hashes, and invalid carrier indexes must produce diagnostics instead of aborting unrelated encounter discovery or rewriting files.");
        }
    }
}
