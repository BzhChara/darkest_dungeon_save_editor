internal static partial class ContractSuite
{
    private static async Task VerifyBridgeCaseIdentitiesAsync(string runRoot, DsonSaveCodec codec)
    {
        foreach (var sameManifest in new[] { false, true })
        foreach (var type in new[] { 0, 1, 2 })
        {
            var root = Path.Combine(runRoot, "case-bridge", sameManifest ? "manifest-aliases" : "separate-paths", type.ToString());
            var game = Path.Combine(root, "game");
            var mods = Path.Combine(game, "mods");
            Directory.CreateDirectory(mods);
            var profile = WriteBattleSafetyProfile(Path.Combine(root, "profile"));
            var gamePath = Path.Combine(profile.ProfileDirectory, "persist.game.json");
            var originalMap = File.ReadAllText(profile.MapSavePath);
            var regions = new[] { "cove", "Cove", "COVE" };
            var foreignIds = new[] { "foreign_a_A", "foreign_b_A", "foreign_c_A" };
            for (var index = 0; index < regions.Length; index++)
                WriteMultiMash(game, $"dungeons/{regions[index]}/{index}.{regions[index]}.2.mash.darkest",
                    string.Concat(Enumerable.Repeat(QueryMashes("alpha_A"), sameManifest ? index : index + 2)));
            if (sameManifest)
            {
                var mod = Path.Combine(mods, "case_aliases");
                WriteMultiMash(mod, "project.xml", "<project><Title>Case Aliases</Title></project>");
                WriteMultiMash(mod, "dungeons/cove/z.cove.2.mash.darkest", QueryMashes("alpha_A") + QueryMashes("alpha_A"));
                File.WriteAllLines(Path.Combine(mod, "modfiles.txt"), regions.Select(region => $"dungeons/{region}/z.{region}.2.mash.darkest"));
                var config = JsonSupport.ReadObject(gamePath);
                config["base_root"]!["applied_ugcs_1_0"] = new JsonObject
                    { ["0"] = new JsonObject { ["name"] = "Case Aliases", ["source"] = "mod_local_source" } };
                File.WriteAllText(gamePath, config.ToJsonString());
            }
            WriteMultiMash(game, "dungeons/ruins/a.ruins.2.mash.darkest", string.Concat(foreignIds.Select(QueryMashes)));
            CreateBattleMonsterDefinitions(game, foreignIds.Prepend("alpha_A").ToArray());
            foreach (var source in new[] { gamePath, profile.MapSavePath, profile.RaidSavePath })
            {
                var encoded = Path.Combine(root, "encoded", Path.GetFileName(source));
                await codec.EncodeAsync(source, encoded, originalBinaryPath: null);
                File.Copy(encoded, source, overwrite: true);
            }
            var locations = new SaveEditorLocations(root, Path.Combine(root, "workspace"), Path.Combine(root, "backups"));
            var bridge = new ManagedBattleEncounterBridgeService(codec, locations, () => false);
            var reader = new BattleMapSnapshotReader(codec);
            var maps = new BattleMapEditService(codec, locations);
            var area = type == 0 ? "coAB" : type == 1 ? "rooC" : "rooB";
            var tile = type == 0 ? "tile1" : "tile0";
            var carriers = new Dictionary<string, string>(StringComparer.Ordinal);
            var visit = 0;
            async Task Enter(string dungeon, bool nativeBattle = false)
            {
                foreach (var name in new[] { "persist.game.json", "persist.raid.json" })
                {
                    var source = Path.Combine(profile.ProfileDirectory, name);
                    var decoded = Path.Combine(root, "visits", visit.ToString(), name);
                    await codec.DecodeAsync(source, decoded);
                    var document = JsonSupport.ReadObject(decoded);
                    if (name == "persist.game.json") document["base_root"]!["raiddungeon"] = dungeon;
                    else
                    {
                        document["base_root"]!["raid_instance"]!["dungeon"] = dungeon;
                        document["base_root"]!["raid_instance"]!["id"] = "case-visit-" + visit;
                        document["base_root"]!["start_elapsed_time"] = 100 + visit;
                    }
                    File.WriteAllText(decoded, document.ToJsonString());
                    await codec.EncodeAsync(decoded, source, originalBinaryPath: null);
                }
                var map = JsonNode.Parse(originalMap)!;
                if (nativeBattle)
                {
                    var cell = map["base_root"]!["map"]!["static_dynamic"]!["areas"]![area]!["tiles"]![tile]!;
                    cell["content"] = (int)BattleMapTileContent.Battle;
                    cell["mash_type"] = type;
                    cell["mash_index"] = 2;
                }
                var mapSeed = WriteMultiMash(root, $"visits/{visit}/map.json", map.ToJsonString());
                await codec.EncodeAsync(mapSeed, profile.MapSavePath, originalBinaryPath: null);
                visit++;
            }
            foreach (var dungeon in regions.Concat(regions))
            {
                await Enter(dungeon);
                var index = Array.IndexOf(regions, dungeon);
                var snapshot = await reader.LoadAsync(profile.ProfileDirectory);
                var content = await ActiveContentResolver.ResolveAsync(profile, game, null, mods, codec, locations.WorkspaceDirectory);
                var catalog = BattleEncounterCatalog.Load(content, snapshot);
                var foreign = catalog.BridgeEncounters.First(row => row.OriginDungeonId == "ruins" && row.MashType == type && row.MonsterIds.SequenceEqual([foreignIds[index]]));
                var added = await bridge.EnsureEncounterAsync(profile, snapshot, content, catalog, foreign, game, null, mods);
                Assert(added.MashIndex == index + 2, "Every case-distinct region keeps its own base count and append index.");
                if (carriers.TryGetValue(dungeon, out var prior))
                    Assert(!added.EncounterWasAdded && added.MashFilePath == prior, "Revisiting a region reuses its assigned carrier and entry.");
                else
                {
                    Assert(carriers.Values.All(path => !path.Equals(added.MashFilePath, StringComparison.OrdinalIgnoreCase)),
                        "Distinct region IDs must never share a physical Windows carrier file.");
                    carriers.Add(dungeon, added.MashFilePath);
                }
                await maps.CommitAsync(await maps.PreparePlaceBattleAsync(profile, snapshot, area, tile, added.DirectEncounter));
                snapshot = await reader.LoadAsync(profile.ProfileDirectory);
                var saved = snapshot.Areas.Single(a => a.AreaId == area).Tiles.Single(t => t.TileId == tile);
                Assert(saved.MashType == type && saved.MashIndex == index + 2, "DSON placement retains the destination region's exact index.");
                var stable = await bridge.ReconcileAsync(added.ActiveContent, game, mods);
                Assert(!stable.Changed && !stable.Deferred, "Stable case-distinct tables do not invalidate each other during maintenance.");
                await maps.CommitAsync(await maps.PrepareDeleteContentAsync(profile, snapshot, area, tile));
            }
            // A native Cove index 2 is not cove's Bridge index 2. Removing only
            // cove's missing source must neither claim that cell nor block cleanup.
            await Enter("Cove", nativeBattle: true);
            var active = await ActiveContentResolver.ResolveAsync(profile, game, null, mods, codec, locations.WorkspaceDirectory);
            var nativeMapHash = ComputeSha256(profile.MapSavePath);
            var retainedHash = ComputeSha256(carriers["Cove"]);
            File.Delete(Path.Combine(game, "monsters/foreign_a/foreign_a_A/foreign_a_A.info.darkest"));
            var maintenance = await bridge.ReconcileAsync(active, game, mods);
            Assert(maintenance.Changed && !maintenance.Deferred && maintenance.RemovedCombinations == 1 && maintenance.ClearedBattles == 0 &&
                ComputeSha256(profile.MapSavePath) == nativeMapHash && ComputeSha256(carriers["Cove"]) == retainedHash,
                "Maintenance must compare exact region identities when identifying old Bridge map references.");
        }
        Console.WriteLine("PASS: case-distinct Bridge allocation, reuse, all-type DSON placement/deletion and region-isolated invalid-source maintenance.");
    }
}
