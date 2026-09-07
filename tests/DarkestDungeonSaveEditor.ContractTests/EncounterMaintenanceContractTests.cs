internal static partial class ContractSuite
{
    public static async Task RunMaintenanceOnlyAsync(string repositoryRoot)
    {
        var fixture = BuildContractFixture(repositoryRoot);
        await RunEncounterMaintenanceContractsAsync(fixture.RunRoot, fixture.Codec);
        Console.WriteLine($"Artifacts: {fixture.RunRoot}");
    }

    private static async Task RunEncounterMaintenanceContractsAsync(string runRoot, DsonSaveCodec codec)
    {
        foreach (var scenario in new[] { "cleanup", "binary", "new-raid", "ambiguous-new-raid", "missing-history", "partial-history",
            "unmapped-local", "rollback", "deferred", "direct-only", "unknown", "stats-only", "interrupted", "truncated-marker", "incomplete-marker", "external" })
        {
            var root = Path.Combine(runRoot, "encounter-maintenance", scenario);
            var game = Path.Combine(root, "game");
            var mods = Path.Combine(game, "mods");
            Directory.CreateDirectory(mods);
            var profile = WriteBattleSafetyProfile(Path.Combine(root, "profile"));
            var seedMapPath = Path.Combine(profile.ProfileDirectory, "persist.map.json");
            var seedMap = JsonNode.Parse(File.ReadAllText(seedMapPath))!;
            seedMap["base_root"]!["map"]!["final_room_id"] = 250;
            File.WriteAllText(seedMapPath, seedMap.ToJsonString());
            var raidPath = Path.Combine(profile.ProfileDirectory, "persist.raid.json");
            var raid = JsonNode.Parse(File.ReadAllText(raidPath))!;
            raid["base_root"]!["start_elapsed_time"] = 100;
            raid["base_root"]!["raid_instance"]!["id"] = "generated_1";
            File.WriteAllText(raidPath, raid.ToJsonString());
            var nativeMash = WriteMultiMash(game, "dungeons/cove/cove.2.mash.darkest",
                "hall: .chance 1 .types native\nroom: .chance 1 .types native\nboss: .chance 1 .types native\n");
            WriteMultiMash(game, "dungeons/ruins/ruins.2.mash.darkest", "hall: .chance 1 .types lost\nhall: .chance 1 .types valid\n");
            CreateBattleMonsterDefinitions(game, ["native", "valid", "added"]);
            var localProject = Path.Combine(mods, "monster-provider", "project.xml");
            if (scenario == "unmapped-local")
            {
                CreateBattleMonsterDefinitions(Path.GetDirectoryName(localProject)!, ["lost"]);
                File.WriteAllText(localProject, "<project><Title>Monster Provider</Title></project>");
                var gamePath = Path.Combine(profile.ProfileDirectory, "persist.game.json");
                var configuration = JsonNode.Parse(File.ReadAllText(gamePath))!;
                configuration["base_root"]!["applied_ugcs_1_0"] = new JsonObject
                {
                    ["0"] = new JsonObject { ["name"] = "Monster Provider", ["source"] = "mod_local_source" }
                };
                File.WriteAllText(gamePath, configuration.ToJsonString());
            }
            else CreateBattleMonsterDefinitions(game, ["lost"]);
            var locations = new SaveEditorLocations(root, Path.Combine(root, "workspaces"), Path.Combine(root, "backups"));
            var snapshotReader = new BattleMapSnapshotReader(codec);
            var snapshot = await snapshotReader.LoadAsync(profile.ProfileDirectory);
            var content = await ActiveContentResolver.ResolveAsync(profile, game, null, mods, codec, locations.WorkspaceDirectory);
            var running = false;
            var bridge = new ManagedBattleEncounterBridgeService(codec, locations, () => running);
            ManagedBattleEncounterBridgeResult? installed = null;
            if (scenario != "direct-only")
            {
                foreach (var monster in new[] { "lost", "valid" })
                {
                    var catalog = BattleEncounterCatalog.Load(content, snapshot);
                    var source = catalog.BridgeEncounters.Single(row => row.OriginDungeonId == "ruins" && row.MonsterIds.SequenceEqual([monster]));
                    installed = await bridge.EnsureEncounterAsync(profile, snapshot, content, catalog, source, game, null, mods);
                    content = installed.ActiveContent;
                }
                Assert(installed!.MashIndex == 2, "The valid Bridge row starts at global hall index 2.");
            }
            var mapService = new BattleMapEditService(codec, locations);
            var direct = BattleEncounterCatalog.Load(content, snapshot).DirectEncounters.Single(row => row.MashType == 1 && row.MashIndex == 0);
            var localPlacement = await mapService.PreparePlaceBattleAsync(profile, snapshot, "rooB", "tile0", direct);
            var localCommit = await mapService.CommitAsync(localPlacement);
            Assert(SaveCommitMarker.IsComplete(Path.Combine(localCommit.BackupDirectory, "commit-result.json"), profile.ProfileDirectory),
                "A successful map placement publishes a complete ownership marker.");
            snapshot = await snapshotReader.LoadAsync(profile.ProfileDirectory);
            SaveCommitResult? bridgeCommit = null;
            if (installed is not null)
            {
                var placement = await mapService.PreparePlaceBattleAsync(profile, snapshot, "coAB", "tile1", installed.DirectEncounter);
                bridgeCommit = await mapService.CommitAsync(placement);
                snapshot = await snapshotReader.LoadAsync(profile.ProfileDirectory);
            }
            var mapPath = snapshot.MapSavePath;
            // An independently edited attachment must survive removal of the battle.
            var placedMap = JsonNode.Parse(File.ReadAllText(mapPath))!;
            var localTile = placedMap["base_root"]!["map"]!["static_dynamic"]!["areas"]!["rooB"]!["tiles"]!["tile0"]!;
            localTile["content"] = 6;
            localTile["curio_prop"] = 789;
            placedMap["base_root"]!["map"]!["static_dynamic"]!["static_save"]!["base_root"]!["areas"]!["rooB"]!["tiles"]!["tile0"]!["cur"] = 789;
            File.WriteAllText(mapPath, placedMap.ToJsonString());
            if (scenario == "binary")
            {
                var encodedMap = Path.Combine(root, "binary-map.json");
                await codec.EncodeAsync(mapPath, encodedMap, originalBinaryPath: null);
                SetRevision(encodedMap, [0x00, 0x00, 0x4A, 0x72]);
                File.Copy(encodedMap, mapPath, overwrite: true);
            }
            var originalMap = placedMap.DeepClone();
            var beforeMap = ComputeSha256(mapPath);
            var nativeTile = originalMap["base_root"]!["map"]!["static_dynamic"]!["areas"]!["rooC"]!.DeepClone();
            var stable = await bridge.ReconcileAsync(content, game, mods);
            Assert(!stable.Changed && !stable.Deferred, "Placing valid direct/Bridge encounters must not invalidate their own history.");
            if (scenario == "unmapped-local")
            {
                var packageHashes = Directory.EnumerateFiles(installed!.PackageDirectory, "*", SearchOption.AllDirectories)
                    .ToDictionary(path => path, ComputeSha256);
                File.WriteAllText(localProject, "<project><Title></Title></project>");
                var unresolvedContent = await ActiveContentResolver.ResolveAsync(profile, game, null, mods, codec, locations.WorkspaceDirectory);
                Assert(unresolvedContent.Issues.Any(issue => issue.Contains("could not be mapped", StringComparison.OrdinalIgnoreCase)),
                    "The real resolver must report the unmapped enabled local Mod despite its files remaining installed.");
                var error = await CaptureSaveFailureAsync(() => bridge.ReconcileAsync(unresolvedContent, game, mods));
                Assert(error is InvalidOperationException && error.Message.Contains("活动来源尚未完整识别", StringComparison.Ordinal) &&
                    ComputeSha256(mapPath) == beforeMap && packageHashes.All(pair => ComputeSha256(pair.Key) == pair.Value),
                    "An unresolved project title must not turn installed monsters into missing formations or clear any placement.");
                File.WriteAllText(localProject, "<project><Title>Monster Provider</Title></project>");
                content = await ActiveContentResolver.ResolveAsync(profile, game, null, mods, codec, locations.WorkspaceDirectory);
                Assert(!(await bridge.ReconcileAsync(content, game, mods)).Changed, "Restoring source resolution must preserve all valid battles.");
                continue;
            }
            if (scenario == "stats-only")
            {
                var info = Directory.EnumerateFiles(Path.Combine(game, "monsters"), "valid.info.darkest", SearchOption.AllDirectories).Single();
                File.AppendAllText(info, "\n// stats-only update\nstats: .hp 999\n");
                var result = await bridge.ReconcileAsync(content, game, mods);
                Assert(!result.Changed && ComputeSha256(mapPath) == beforeMap, "Content bytes changing without index/availability changes must preserve placements.");
                continue;
            }
            if (scenario is "new-raid" or "ambiguous-new-raid")
            {
                raid["base_root"]!["start_elapsed_time"] = 200;
                File.WriteAllText(raidPath, raid.ToJsonString());
                if (scenario == "new-raid")
                {
                    // A fresh native encounter must not inherit the prior raid's placement record.
                    placedMap["base_root"]!["map"]!["static_dynamic"]!["areas"]!["coAB"]!["tiles"]!["tile1"]!["mash_index"] = 0;
                    File.WriteAllText(mapPath, placedMap.ToJsonString());
                    beforeMap = ComputeSha256(mapPath);
                }
            }
            if (scenario == "cleanup")
            {
                // Import the pre-feature successful backup format, not a guessed numeric ID.
                var manifestPath = Path.Combine(localCommit.BackupDirectory, "backup-manifest.json");
                var record = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
                record.Remove("RaidIdentity");
                File.WriteAllText(manifestPath, record.ToJsonString());
                bridge = new ManagedBattleEncounterBridgeService(codec, locations, () => running);
            }
            File.WriteAllText(nativeMash,
                "hall: .chance 1 .types added\nhall: .chance 1 .types added\nhall: .chance 1 .types native\n" +
                "room: .chance 1 .types added\nroom: .chance 1 .types native\nboss: .chance 1 .types native\n");
            if (installed is not null)
                File.Delete(Directory.EnumerateFiles(Path.Combine(game, "monsters"), "lost.info.darkest", SearchOption.AllDirectories).Single());
            var bridgeBefore = installed is null ? null : Directory.EnumerateFiles(installed.PackageDirectory, "*", SearchOption.AllDirectories)
                .ToDictionary(path => path, ComputeSha256);
            if (scenario is "missing-history" or "partial-history" or "ambiguous-new-raid")
            {
                var bridgeMarker = Path.Combine(bridgeCommit!.BackupDirectory, "commit-result.json");
                var markerBytes = File.ReadAllBytes(bridgeMarker);
                if (scenario == "partial-history") File.Delete(bridgeMarker);
                bridge = new ManagedBattleEncounterBridgeService(codec, scenario == "missing-history"
                    ? locations with { BackupDirectory = Path.Combine(root, "empty-backups") } : locations, () => false);
                var error = await CaptureSaveFailureAsync(() => bridge.ReconcileAsync(content, game, mods));
                Assert(error is InvalidOperationException && error.Message.Contains("缺少当前副本的成功放置记录", StringComparison.Ordinal) &&
                    ComputeSha256(mapPath) == beforeMap && bridgeBefore!.All(pair => ComputeSha256(pair.Key) == pair.Value),
                    "Missing ownership evidence must defer index compaction and preserve all map cells instead of guessing from numeric IDs.");
                if (scenario == "ambiguous-new-raid") continue;
                if (scenario == "partial-history") File.WriteAllBytes(bridgeMarker, markerBytes);
                bridge = new ManagedBattleEncounterBridgeService(codec, locations, () => false);
            }
            if (scenario == "unknown")
            {
                File.AppendAllText(nativeMash, "hall: .chance 1 .types\n");
                var error = await CaptureSaveFailureAsync(() => bridge.ReconcileAsync(content, game, mods));
                Assert(error is not null && ComputeSha256(mapPath) == beforeMap && bridgeBefore!.All(pair => ComputeSha256(pair.Key) == pair.Value),
                    "Unparseable index tables must defer without turning uncertainty into deletions.");
                continue;
            }
            if (scenario == "deferred")
            {
                running = true;
                var deferred = await bridge.ReconcileAsync(content, game, mods);
                Assert(deferred.Deferred && !deferred.Changed && ComputeSha256(mapPath) == beforeMap &&
                    bridgeBefore!.All(pair => ComputeSha256(pair.Key) == pair.Value), "A running game must defer every save and package write.");
                running = false;
            }
            if (scenario == "rollback")
            {
                var calls = 0;
                bridge.AfterMaintenanceReplace = _ => { if (++calls == 2) throw new IOException("injected maintenance failure"); };
                var error = await CaptureSaveFailureAsync(() => bridge.ReconcileAsync(content, game, mods));
                Assert(error is not null && calls == 2 && ComputeSha256(mapPath) == beforeMap &&
                    bridgeBefore!.All(pair => ComputeSha256(pair.Key) == pair.Value), "Failure after map and package replacement must roll both back.");
                bridge.AfterMaintenanceReplace = null;
            }
            var cleaned = await bridge.ReconcileAsync(content, game, mods);
            Assert(cleaned.Changed && !cleaned.Deferred && cleaned.ClearedBattles == (scenario == "new-raid" ? 0 : installed is null ? 1 : 2),
                "Any confirmed invalidation clears all and only still-matching placements in the same raid, including direct local placements.");
            var decodedAfter = Path.Combine(root, "after-map.json");
            await codec.DecodeAsync(mapPath, decodedAfter);
            var after = JsonNode.Parse(File.ReadAllText(decodedAfter))!;
            if (scenario == "binary") Assert(DsonSaveCodec.IsDson(mapPath) &&
                File.ReadAllBytes(mapPath).AsSpan(4, 4).SequenceEqual(new byte[] { 0x00, 0x00, 0x4A, 0x72 }),
                "Cleanup must roundtrip actual DSON and preserve its revision bytes.");
            var afterAreas = after["base_root"]!["map"]!["static_dynamic"]!["areas"]!;
            Assert(JsonNode.DeepEquals(afterAreas["rooC"], nativeTile), "The native battle stays byte-for-field unchanged even when its numeric meaning changes.");
            if (scenario == "new-raid") Assert(ComputeSha256(mapPath) == beforeMap, "An identical fixed map in a later raid must not inherit editor ownership.");
            else
            {
                localTile["content"] = 7;
                localTile["mash_index"] = -1;
                localTile["mash_type"] = 7;
                if (installed is not null)
                {
                    var hall = placedMap["base_root"]!["map"]!["static_dynamic"]!["areas"]!["coAB"]!["tiles"]!["tile1"]!;
                    hall["content"] = 0; hall["mash_index"] = -1; hall["mash_type"] = 7;
                }
                Assert(JsonNode.DeepEquals(after, placedMap), "Only battle content/index/type fields change; curios, topology and exploration remain unchanged.");
            }
            if (installed is not null)
            {
                var catalog = BattleEncounterCatalog.Load(content, await snapshotReader.LoadAsync(profile.ProfileDirectory));
                var owned = catalog.Encounters.Where(row => row.SourcePath == installed.MashFilePath).ToArray();
                Assert(cleaned.RemovedCombinations == 1 && cleaned.ReindexedCombinations == 1 && owned.Length == 1 &&
                    owned[0].MashIndex == 3 && owned[0].MonsterIds.SequenceEqual(["valid"]) && owned[0].CanPlaceDirectly,
                    "The invalid Bridge row is deleted, while the retained row uses the current native count and compacted Bridge offset.");
                var source = catalog.BridgeEncounters.Single(row => row.OriginDungeonId == "ruins" && row.MonsterIds.SequenceEqual(["valid"]));
                var reused = await bridge.EnsureEncounterAsync(profile, await snapshotReader.LoadAsync(profile.ProfileDirectory), content,
                    catalog, source, game, null, mods);
                Assert(!reused.EncounterWasAdded && reused.MashIndex == 3, "Future placement must reuse the recalculated index without duplicating a formation.");
            }
            Assert(File.Exists(Path.Combine(cleaned.BackupDirectory!, "persist.map.json")) &&
                File.Exists(Path.Combine(cleaned.BackupDirectory!, "commit-result.json")), "Successful cleanup must retain a complete profile backup and completion record.");
            Assert(!(await bridge.ReconcileAsync(content, game, mods)).Changed, "A completed cleanup retires old placements and must not repeat on the next poll.");
            if (scenario is "interrupted" or "truncated-marker" or "incomplete-marker" or "external")
            {
                var marker = Path.Combine(cleaned.BackupDirectory!, "commit-result.json");
                if (scenario is "truncated-marker" or "incomplete-marker")
                    File.WriteAllText(marker, scenario == "truncated-marker" ? "{" : "{}");
                else File.Delete(marker);
                Assert(!SaveCommitMarker.IsComplete(marker, profile.ProfileDirectory), "A missing, truncated or incomplete marker cannot commit a cleanup.");
                bridge = new ManagedBattleEncounterBridgeService(codec, locations, () => false);
                if (scenario == "external")
                {
                    File.AppendAllText(mapPath, "\n ");
                    var externalHash = ComputeSha256(mapPath);
                    var externalError = await CaptureSaveFailureAsync(() => bridge.ReconcileAsync(content, game, mods));
                    Assert(externalError is IOException && ComputeSha256(mapPath) == externalHash &&
                        !File.Exists(Path.Combine(cleaned.BackupDirectory!, "maintenance-recovered.json")),
                        "Interrupted cleanup must never restore a backup over an external map version.");
                }
                else
                {
                    var resumed = await bridge.ReconcileAsync(content, game, mods);
                    Assert(resumed.Changed && File.Exists(Path.Combine(cleaned.BackupDirectory!, "maintenance-recovered.json")),
                        "An interrupted group with matching before/after hashes must recover on the next service instance and then retry cleanup.");
                    bridge = new ManagedBattleEncounterBridgeService(codec, locations, () => false);
                    Assert(!(await bridge.ReconcileAsync(content, game, mods)).Changed,
                        "A recovered malformed marker must not poison later history reads or cause repeated cleanup.");
                }
            }
        }
        Console.WriteLine("PASS: encounter maintenance: direct + Bridge cleanup, native/attachment preservation, history import and loss, raid isolation, missing/shifted rows, reuse, unresolved local Mods, deferral, rollback, malformed commit recovery, external preservation, uncertainty and stats-only changes.");
    }
}
