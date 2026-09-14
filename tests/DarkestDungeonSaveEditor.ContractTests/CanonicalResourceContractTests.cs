internal static partial class ContractSuite
{
    public static async Task RunCanonicalResourcesOnlyAsync(string repositoryRoot)
    {
        var fixture = BuildContractFixture(repositoryRoot);
        await RunCanonicalResourceContractsAsync(fixture.RunRoot, fixture.Codec);
        Console.WriteLine($"Artifacts: {fixture.RunRoot}");
    }

    private static async Task RunCanonicalResourceContractsAsync(string runRoot, DsonSaveCodec codec)
    {
        VerifyCanonicalRegionRequests(runRoot);
        VerifyCanonicalOpenCatalogs(runRoot);
        VerifyEffectAndDistrictQueries(runRoot);
        VerifyEffectSlotOrder(runRoot);
        await VerifyCanonicalBattlePersistenceAsync(runRoot, codec);
    }

    private static void VerifyCanonicalRegionRequests(string runRoot)
    {
        foreach (var kind in new[] { "local", "workshop" })
        {
            var root = Path.Combine(runRoot, "canonical-region-requests", kind);
            var baseline = Path.Combine(root, "base"); var mod = Path.Combine(root, "mod");
            WriteMultiMash(baseline, "dungeons/cove/cove.props.darkest",
                "traps: .chance 1 .types base_trap\ntraps: .chance 3 .types shared\n");
            WriteMultiMash(mod, "dungeons/Cove/Cove.props.darkest", "traps: .chance 900 .types mod_trap shared\n");
            WriteMultiMash(baseline, "props/trap_definitions.json", """
                {"props":[{"name":"base_trap","default_data":{"instance_type":"trap"}},
                          {"name":"mod_trap","default_data":{"instance_type":"trap"}},
                          {"name":"shared","default_data":{"instance_type":"trap"}}]}
                """);
            WriteFixtureManifest(mod);
            var content = QueryContent(root, [new("base", "Base", "base", baseline, 0), new("mod", "Mod", kind, mod, 0)]);
            var catalog = BattleRoomAttachmentCatalog.Load(content);
            Assert(catalog.GetCandidates(BattleRoomAttachmentKind.Trap, "cove").Select(row => row.Id).ToHashSet().SetEquals(["base_trap", "shared"]) &&
                   catalog.GetCandidates(BattleRoomAttachmentKind.Trap, "Cove").Select(row => row.Id).ToHashSet().SetEquals(["mod_trap", "shared"]),
                "Canonical region requests must not merge different-case Mod and Base pools after opening.");
            AssertRegionalDistribution(catalog, "cove", new() { ["base_trap"] = 100, ["shared"] = 300 });
            AssertRegionalDistribution(catalog, "Cove", new() { ["mod_trap"] = 100, ["shared"] = 100 });
            Assert(!catalog.GetCandidates(BattleRoomAttachmentKind.Trap, "Cove").First().IsAvailableInDungeon("cove"),
                "A different-case region cannot pass the map placement region check.");
            var alias = BattleRoomAttachmentCatalog.Load(content, "COVE");
            Assert(alias.Guard.RequestedDungeonId == "COVE" &&
                   alias.GetCandidates(BattleRoomAttachmentKind.Trap, "COVE").Select(row => row.Id).ToHashSet().SetEquals(["base_trap", "shared"]),
                "The current original request may use a physical Base alias, but cannot match the differently cased Mod key.");
            foreach (var row in alias.GetCandidates(BattleRoomAttachmentKind.Trap, "COVE")) BattleRoomAttachmentCatalog.ValidateDefinition(row);
            var reboundGuard = alias.Guard with { GameSaveSha256 = "rebound" };
            var rebound = alias with { Guard = reboundGuard, Definitions = alias.Definitions.Select(row => row with { CatalogGuard = reboundGuard }).ToArray() };
            Assert(ReferenceEquals(rebound.SelectRegionalContent(BattleRoomAttachmentKind.Trap, "COVE", 0)!.CatalogGuard, reboundGuard),
                "Rebinding after synchronization must retain the original region request and current choice guards.");
            var manifest = Path.Combine(mod, "modfiles.txt");
            File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("Cove", "cove", StringComparison.Ordinal));
            var updated = BattleRoomAttachmentCatalog.Load(content, "cove");
            Assert(updated.GetCandidates(BattleRoomAttachmentKind.Trap, "cove").Select(row => row.Id).ToHashSet().SetEquals(["mod_trap", "shared"]),
                "A correct lowercase Mod key may open a capitalized physical directory and filename.");
            File.WriteAllText(Path.Combine(mod, "dungeons/Cove/Cove.props.darkest"), "traps: .chance 1 .types \"\"\n");
            Assert(BattleRoomAttachmentCatalog.Load(content, "cove").GetCandidates(BattleRoomAttachmentKind.Trap, "cove").Count == 0,
                "An effective empty Mod pool must not revive the Base pool.");
        }
        foreach (var kind in new[] { "base", "mode", "dlc-feature" })
        {
            var root = Path.Combine(runRoot, "canonical-region-physical-alias", kind);
            var source = Path.Combine(root, "source"); var baseline = Path.Combine(root, "baseline");
            WriteMultiMash(source, "dungeons/mIsTyGrOvE/MistyGrove.props.DARKEST", "traps: .chance 1 .types alias_trap\n");
            WriteMultiMash(baseline, "props/trap_definitions.json", """{"props":[{"name":"alias_trap","default_data":{"instance_type":"trap"}}]}""");
            var sources = new[] { new ActiveContentSource("base:defaults", "Defaults", "base", baseline, -1) }.Concat(QuerySources(source, kind)).ToArray();
            var catalog = BattleRoomAttachmentCatalog.Load(QueryContent(root, sources), "MistyGrove");
            var selected = catalog.GetRegionalCandidates(BattleRoomAttachmentKind.Trap, "MistyGrove").Single();
            Assert(selected.Id == "alias_trap" && selected.OriginDungeonId == "MistyGrove", "Physical directory aliases must preserve the actual custom region ID.");
            BattleRoomAttachmentCatalog.ValidateDefinition(selected);
            WriteMultiMash(source, "dungeons/arena/arena.props.darkest", "traps: .chance 1 .types alias_trap\n");
            foreach (var request in new[] { "arena", "ARENA" })
            {
                var arena = BattleRoomAttachmentCatalog.Load(QueryContent(root, sources), request);
                Assert(arena.GetCandidates(BattleRoomAttachmentKind.Trap, request).Count == 0 &&
                       arena.SelectRegionalContent(BattleRoomAttachmentKind.Trap, request, 0) is null,
                    "Explicit current-region requests must retain the existing arena pool exclusion.");
            }
        }
        Console.WriteLine("PASS: canonical region IDs, case-distinct pools and independent automatic-selection weights.");
    }

    private static void VerifyCanonicalOpenCatalogs(string runRoot)
    {
        foreach (var kind in new[] { "local", "workshop" })
        foreach (var variant in new[] { "uppercase-key", "physical-alias", "upper-first", "lower-first", "dlc-only", "dlc-root" })
        {
            var root = Path.Combine(runRoot, "canonical-open-catalogs", kind, variant);
            var baseline = Path.Combine(root, "base");
            var mod = Path.Combine(root, "mod");
            var dlc = Path.Combine(root, "dlc");
            var isDlc = variant.StartsWith("dlc-");
            var modWins = variant is not ("uppercase-key" or "dlc-only");
            var sources = new List<ActiveContentSource> { new("base", "Base", "base", baseline, 0) };
            var lower = isDlc ? dlc : baseline;
            string Hero(int hp, string loot) => $"armour: .name coat .hp {hp}\nextra_battle_loot: .code {loot}\n";
            var definitions = new Dictionary<string, (string Lower, string Mod)>
            {
                ["heroes/query/query.info.darkest"] = (Hero(isDlc ? 30 : 20, "base_loot"), Hero(99, "mod_loot")),
                ["heroes/query/query.art.darkest"] = ("// base art", "// mod art"),
                ["heroes/query/query.override.darkest"] = ("// base override", "// mod override"),
                ["monsters/alpha/alpha_A/alpha_A.info.darkest"] = ("display: .size 1\nloot: .code base_loot\n", "display: .size 3\nloot: .code mod_loot\n"),
                ["monsters/alpha/alpha_A/alpha_A.art.darkest"] = ("// base art", "// mod art"),
                ["campaign/roster/roster.variables.json"] = (RosterThresholds(10), RosterThresholds(17)),
                ["dungeons/cove/cove.props.darkest"] = ("traps: .chance 1 .types base_trap\n", "traps: .chance 1 .types mod_trap\n")
            };
            if (isDlc) sources.Add(new("dlc", "DLC", "dlc-feature", dlc, 100) { VirtualPathPrefix = "dlc/feature" });
            var prefix = variant == "dlc-only" ? "dlc/feature/" : "";
            var manifest = new List<string>();
            foreach (var (path, text) in definitions)
            {
                WriteMultiMash(lower, path, text.Lower);
                var alias = prefix + path.Replace(".darkest", ".DARKEST", StringComparison.Ordinal)
                    .Replace(".json", ".JSON", StringComparison.Ordinal);
                WriteMultiMash(mod, isDlc ? prefix + path : alias, text.Mod);
                manifest.AddRange(variant switch
                {
                    "uppercase-key" => new[] { alias },
                    "upper-first" => new[] { alias, prefix + path },
                    "lower-first" => new[] { prefix + path, alias },
                    _ => new[] { prefix + path }
                });
            }
            WriteMultiMash(mod, "inventory/query.inventory.items.darkest", string.Join('\n', new[] { "base_token", "mod_token" }
                .Select(id => $"inventory_item: .type estate .id {id} .base_stack_limit 5 .estate_can_be_provision false")));
            manifest.Add("inventory/query.inventory.items.darkest");
            WriteMultiMash(mod, "modfiles.txt", string.Join('\n', manifest));
            sources.Add(new("mod", "Mod", kind, mod, 0));
            WriteMultiMash(baseline, "monsters/bravo/bravo_A/bravo_A.info.darkest", "display: .size 1\n");
            WriteMultiMash(baseline, "dungeons/cove/a.cove.2.mash.darkest", QueryMashes("alpha_A alpha_A") + QueryMashes("bravo_A"));
            WriteMultiMash(baseline, "props/trap_definitions.json", """{"props":[{"name":"base_trap","default_data":{"instance_type":"trap"}},{"name":"mod_trap","default_data":{"instance_type":"trap"}}]}""");
            WriteMultiMash(baseline, "loot/query.loot.json", """{"loot_tables":[{"id":"base_loot","entries":[{"type":"item","chances":1,"data":{"type":"estate","id":"base_token","amount":1}}]},{"id":"mod_loot","entries":[{"type":"item","chances":1,"data":{"type":"estate","id":"mod_token","amount":1}}]}]}""");
            var content = QueryContent(root, sources.ToArray());
            var heroes = HeroClassCatalog.Load(content);
            Assert(heroes.HeroClasses.Single().BaseHp == (modWins ? 99 : isDlc ? 30 : 20) &&
                heroes.ResolveLevelThresholds[1] == (modWins ? 17 : 10), $"{kind}/{variant}: HP and XP must follow the exact root request.");
            foreach (var dir in new[] { "heroes", "monsters" })
            foreach (var file in NativeContentFileResolver.ResolveActorFiles(content.Sources, dir, [])
                         .Where(pair => definitions.ContainsKey(pair.Key)))
                Assert(File.ReadAllText(file.Value.Path) == (modWins ? definitions[file.Key].Mod : definitions[file.Key].Lower),
                    "Info, art and override must all use the same manifest/device boundary.");
            var raid = JsonNode.Parse("""{"base_root":{"party":{"inventory":{"items":{}}}}}""")!.AsObject();
            foreach (var item in QuantityItemCatalog.LoadRaid(content, raid).Items)
            {
                var active = item.ItemId == (modWins ? "mod_token" : "base_token");
                Assert(item.ReferenceStatus == (active ? QuantityItemReferenceStatus.ConfirmedActive : QuantityItemReferenceStatus.SuspectedUnused) &&
                    item.IsHiddenByDefault == !active, "Actor loot references must follow the file the game opens.");
            }
            var props = BattleRoomAttachmentCatalog.Load(content).GetCandidates(BattleRoomAttachmentKind.Trap, "cove");
            Assert(props.Single().Id == (modWins ? "mod_trap" : "base_trap"), "Regional pool requests must not use uppercase-only or DLC-prefixed Mod keys.");
            var map = new BattleMapSnapshot(content.Profile.ProfileDirectory, "", "", "", "", "cove", 2, 1,
                null, null, null, null, null, null, null, null, null, false, [], [], [], DateTime.UtcNow);
            var catalog = BattleEncounterCatalog.Load(content, map);
            foreach (var type in new[] { 0, 1, 2 })
            {
                var bravo = catalog.DirectEncounters.Single(row => row.MashType == type && row.MonsterIds.SequenceEqual(["bravo_A"]));
                Assert(bravo.MashIndex == (modWins ? 0 : 1), "Only genuinely loaded size-three monsters may remove the oversized row.");
                BattleEncounterCatalog.ValidateDirectEncounter(bravo);
                Assert(BattleEncounterCatalog.ResolveAppendTarget(catalog, type).NextMashIndex == (modWins ? 1 : 2) &&
                    BattleEncounterCatalog.ReadMaintenanceTable(content, "cove", 2, type).Count == (modWins ? 1 : 2), "Append and maintenance must agree with direct placement.");
            }
            if (variant == "physical-alias")
            {
                var before = ProfileCatalogContentFingerprint.Capture(content.Sources);
                File.Delete(Path.Combine(mod, "heroes/query/query.info.darkest"));
                var missing = HeroClassCatalog.Load(content);
                Assert(missing.HeroClasses.All(hero => hero.Id != "query") && missing.Issues.Any(issue => issue.Contains("Failed to read hero definition")),
                    "A listed missing winner must be diagnosed, not replaced by the old Base hero.");
                Assert(ProfileCatalogContentFingerprint.Capture(content.Sources) != before, "Missing canonical content must refresh catalogs.");
            }
        }
        Console.WriteLine("PASS: 12 canonical-source fixtures, 36 encounter tables, raw manifest aliases, DLC fallbacks, info/art/override, HP/XP, loot and regional pools.");
    }

    private static void VerifyEffectAndDistrictQueries(string runRoot)
    {
        foreach (var kind in new[] { "local", "workshop" })
        foreach (var variant in new[] { "same-path", "distinct", "clear-disease" })
        {
            var root = Path.Combine(runRoot, "effect-district-queries", kind, variant);
            var baseline = Path.Combine(root, "base"); var low = Path.Combine(root, "low"); var high = Path.Combine(root, "high");
            WriteMultiMash(baseline, "heroes/query/query.info.darkest", "armour: .name coat .hp 20\ncombat_skill: .id clue .level 0 .effect RETAIN\n");
            WriteMultiMash(baseline, "shared/quirk/query.quirk_library.json", """{"quirks":[{"id":"special","random_chance":0,"is_positive":true}]}""");
            WriteMultiMash(low, "effects/a.effects.darkest", "effect: .name RETAIN .disease special\n");
            WriteMultiMash(low, "campaign/town/districts/a.districts.json", """
                {"buildings":[{"id":"supply","buff_list":[
                {"type":"DistrictSupplyBuffData","target_inventory":"estate","item_type":"estate","item_name":"town_token","range_min":1,"range_max":1},
                {"type":"DistrictSupplyBuffData","target_inventory":"provision","item_type":"estate","item_name":"raid_token","range_min":1,"range_max":1}]}]}
                """);
            WriteMultiMash(low, "inventory/a.inventory.items.darkest", string.Join('\n', new[] { "town_token", "raid_token" }
                .Select(id => $"inventory_item: .type estate .id {id} .base_stack_limit 5 .estate_can_be_provision false")));
            var name = variant == "distinct" ? "z" : "a";
            WriteMultiMash(high, $"effects/{name}.effects.darkest", "effect: .name RETAIN .duration 3" + (variant == "clear-disease" ? " .disease \"\"" : ""));
            WriteMultiMash(high, $"campaign/town/districts/{name}.districts.json", "{\"buildings\":[]}");
            WriteFixtureManifest(low); WriteFixtureManifest(high);
            var content = QueryContent(root, [new("base", "Base", "base", baseline, 0), new("low", "Low", kind, low, 1), new("high", "High", kind, high, 0)]);
            var signals = HeroClassCatalog.Load(content).HeroClasses.Single().RuntimeQuirkSignals;
            Assert(signals.Count == (variant == "clear-disease" ? 0 : 1) && signals.All(s => s.QuirkId == "special"),
                "Both Mod Effect files are read: omitted disease retains, explicit empty clears.");
            var town = QuantityItemCatalog.Load(content, JsonNode.Parse("""{"base_root":{"wallet":{},"estate_items":{"items":{}}}}""")!.AsObject());
            var raid = QuantityItemCatalog.LoadRaid(content, JsonNode.Parse("""{"base_root":{"party":{"inventory":{"items":{}}}}}""")!.AsObject());
            foreach (var (catalog, activeId) in new[] { (town, "town_token"), (raid, "raid_token") })
            foreach (var item in catalog.Items)
                Assert(item.ReferenceStatus == (item.ItemId == activeId ? QuantityItemReferenceStatus.ConfirmedActive : QuantityItemReferenceStatus.SuspectedUnused) &&
                    item.IsHiddenByDefault == (item.ItemId != activeId), "District providers are retained with the correct town/raid target.");
        }
        Console.WriteLine("PASS: six Effect/District cases, omission/clear controls, preserved providers and town/raid visibility.");
    }

    private static void VerifyEffectSlotOrder(string runRoot)
    {
        var root = Path.Combine(runRoot, "effect-slot-order");
        var baseline = new ActiveContentSource("base", "Base", "base", Path.Combine(root, "base"), 0);
        var low = new ActiveContentSource("low", "Low", "local", Path.Combine(root, "low"), 1);
        var high = new ActiveContentSource("high", "High", "local", Path.Combine(root, "high"), 0);
        const string key = "effects/a.effects.darkest";
        var a = new ContentFileCandidate(baseline, WriteMultiMash(baseline.Directory, key, "fixture"));
        var b = new ContentFileCandidate(baseline, WriteMultiMash(baseline.Directory, "effects/b.effects.darkest", "fixture"));
        var lower = new ContentFileCandidate(low, WriteMultiMash(low.Directory, key, "fixture"));
        var upper = new ContentFileCandidate(high, WriteMultiMash(high.Directory, key, "fixture"));
        WriteFixtureManifest(low.Directory); WriteFixtureManifest(high.Directory);
        Assert(NativeContentFileResolver.ResolveEffectFiles([a, b, lower, upper], [baseline, low, high], "Effect", [])
            .Select(f => f.Path).SequenceEqual([b.Path, lower.Path, upper.Path]), "Flags 1 erases the offset-zero Base slot and appends, while retaining later prefixed providers.");
        Assert(NativeContentFileResolver.ResolveAdditiveFiles([a, b, lower, upper], [baseline, low, high], "District", [])
            .Select(f => f.Path).SequenceEqual([a.Path, b.Path, lower.Path, upper.Path]), "Flags 9 retains Base bytes because the native slot has a leading >.");
        var nested = new ContentFileCandidate(baseline, WriteMultiMash(baseline.Directory, "effects/archive/" + key, "fixture"));
        Assert(NativeContentFileResolver.ResolveEffectFiles([a, nested, lower], [baseline, low], "Effect", [])
            .Select(f => f.Path).SequenceEqual([nested.Path, lower.Path, lower.Path]), "A positive-offset first match appends and surviving Base requests reopen without deduplicating slots.");
        Console.WriteLine("PASS: distinct flags 0/1/9 boundaries, Effect erase-and-append order and repeated opened slots.");
    }

    private static async Task VerifyCanonicalBattlePersistenceAsync(string runRoot, DsonSaveCodec codec)
    {
        foreach (var type in new[] { 0, 1, 2 })
        {
            var root = Path.Combine(runRoot, "canonical-persistence", type.ToString());
            var game = Path.Combine(root, "game"); var mods = Path.Combine(game, "mods"); var mod = Path.Combine(mods, "case-probe");
            WriteMultiMash(game, "monsters/alpha/alpha_A/alpha_A.info.darkest", "display: .size 1\n");
            foreach (var id in new[] { "bravo_A", "foreign_A" }) WriteMultiMash(game, $"monsters/{id[..^2]}/{id}/{id}.info.darkest", "display: .size 1\n");
            WriteMultiMash(game, "dungeons/cove/a.cove.2.mash.darkest", QueryMashes("alpha_A alpha_A") + QueryMashes("bravo_A"));
            WriteMultiMash(game, "dungeons/ruins/a.ruins.2.mash.darkest", QueryMashes("foreign_A"));
            WriteMultiMash(mod, "monsters/alpha/alpha_A/alpha_A.info.DARKEST", "display: .size 3\n");
            WriteMultiMash(mod, "modfiles.txt", "monsters/alpha/alpha_A/alpha_A.info.DARKEST\n");
            WriteMultiMash(mod, "project.xml", "<project><Title>Canonical Test</Title></project>");
            var profile = WriteBattleSafetyProfile(Path.Combine(root, "profile"));
            var gamePath = Path.Combine(profile.ProfileDirectory, "persist.game.json");
            var config = JsonNode.Parse(File.ReadAllText(gamePath))!;
            config["base_root"]!["applied_ugcs_1_0"] = new JsonObject { ["0"] = new JsonObject { ["name"] = "Canonical Test", ["source"] = "mod_local_source" } };
            File.WriteAllText(gamePath, config.ToJsonString());
            var raid = JsonNode.Parse(File.ReadAllText(profile.RaidSavePath))!;
            raid["base_root"]!["start_elapsed_time"] = 100;
            raid["base_root"]!["raid_instance"]!["id"] = "canonical-open";
            File.WriteAllText(profile.RaidSavePath, raid.ToJsonString());
            foreach (var name in new[] { "persist.game.json", "persist.map.json", "persist.raid.json" })
            {
                var seed = Path.Combine(profile.ProfileDirectory, name); var encoded = Path.Combine(root, "seeds", name);
                await codec.EncodeAsync(seed, encoded, originalBinaryPath: null); File.Copy(encoded, seed, overwrite: true);
            }
            var locations = new SaveEditorLocations(root, Path.Combine(root, "workspace"), Path.Combine(root, "backups"));
            var reader = new BattleMapSnapshotReader(codec); var snapshot = await reader.LoadAsync(profile.ProfileDirectory);
            var content = await ActiveContentResolver.ResolveAsync(profile, game, null, mods, codec, locations.WorkspaceDirectory);
            var catalog = BattleEncounterCatalog.Load(content, snapshot);
            var maps = new BattleMapEditService(codec, locations);
            var area = type == 0 ? "coAB" : type == 1 ? "rooC" : "rooB"; var tile = type == 0 ? "tile1" : "tile0";
            await maps.CommitAsync(await maps.PreparePlaceBattleAsync(profile, snapshot, area, tile,
                catalog.DirectEncounters.Single(e => e.MashType == type && e.MonsterIds.SequenceEqual(["bravo_A"]))));
            snapshot = await reader.LoadAsync(profile.ProfileDirectory);
            Assert(snapshot.Areas.Single(a => a.AreaId == area).Tiles.Single(t => t.TileId == tile).MashIndex == 1 && DsonSaveCodec.IsDson(profile.MapSavePath),
                "Direct DSON placement must retain the valid size-two row and save bravo at index 1.");
            var bridge = new ManagedBattleEncounterBridgeService(codec, locations);
            var added = await bridge.EnsureEncounterAsync(profile, snapshot, content, catalog,
                catalog.BridgeEncounters.Single(e => e.OriginDungeonId == "ruins" && e.MashType == type), game, null, mods);
            snapshot = await reader.LoadAsync(profile.ProfileDirectory);
            await maps.CommitAsync(await maps.PreparePlaceBattleAsync(profile, snapshot, area, tile, added.DirectEncounter));
            snapshot = await reader.LoadAsync(profile.ProfileDirectory);
            var saved = snapshot.Areas.Single(a => a.AreaId == area).Tiles.Single(t => t.TileId == tile);
            Assert(added.MashIndex == 2 && saved.MashIndex == 2 && saved.MashType == type && DsonSaveCodec.IsDson(profile.MapSavePath),
                "Cross-region Bridge replacement must persist native index 2 for each battle type.");
            File.WriteAllText(Path.Combine(mod, "monsters/alpha/alpha_A/alpha_A.info.DARKEST"), "display: .size 4\n");
            Assert(!(await bridge.ReconcileAsync(added.ActiveContent, game, mods)).Changed, "Unused uppercase manifest bytes must not clear placed battles.");
            await maps.CommitAsync(await maps.PrepareDeleteContentAsync(profile, snapshot, area, tile));
            snapshot = await reader.LoadAsync(profile.ProfileDirectory);
            Assert(snapshot.Areas.Single(a => a.AreaId == area).Tiles.Single(t => t.TileId == tile).Content == BattleMapTileContent.Nothing,
                "Deletion remains functional after the corrected source selection and Bridge replacement.");
        }
        Console.WriteLine("PASS: three battle types, corrected direct/Bridge DSON indexes, unused-file maintenance and deletion.");
    }
}
