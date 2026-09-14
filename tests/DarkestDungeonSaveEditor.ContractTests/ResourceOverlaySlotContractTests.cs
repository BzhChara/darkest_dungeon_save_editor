internal static partial class ContractSuite
{
    public static async Task RunResourceOverlaySlotsOnlyAsync(string repositoryRoot)
    {
        var fixture = BuildContractFixture(repositoryRoot);
        await RunResourceOverlaySlotContractsAsync(fixture.RunRoot, fixture.Codec);
        Console.WriteLine($"Artifacts: {fixture.RunRoot}");
    }

    private static async Task RunResourceOverlaySlotContractsAsync(string runRoot, DsonSaveCodec codec)
    {
        VerifyOverlaySlotBoundaries(runRoot);
        VerifyOverlayReopenedFiles(runRoot);
        VerifyOverlayCatalogs(runRoot);
        VerifyOverlayOpenConsumers(runRoot);
        VerifyOverlayQueryBoundaries(runRoot);
        await VerifyOverlayBattlePersistenceAsync(runRoot, codec);
    }

    private static void VerifyOverlaySlotBoundaries(string runRoot)
    {
        var root = Path.Combine(runRoot, "overlay-slot-boundaries");
        var baseline = new ActiveContentSource("base", "Base", "base", Path.Combine(root, "base"), 0);
        var low = new ActiveContentSource("low", "Low", "local", Path.Combine(root, "low"), 1);
        var high = new ActiveContentSource("high", "High", "local", Path.Combine(root, "high"), 0);
        const string key = "inventory/a.inventory.items.darkest";
        ContentFileCandidate File(ActiveContentSource source, string path) =>
            new(source, WriteMultiMash(source.Directory, path, "fixture"));
        var first = File(baseline, "inventory/deeper/archive/" + key);
        var second = File(baseline, "inventory/archive/" + key);
        var exact = File(baseline, key);
        var overlay = File(low, key);
        var final = File(high, key);
        var issues = new List<string>();
        var files = NativeContentFileResolver.Resolve([exact, second, first, final, overlay], [baseline, low, high], "Slots", issues);
        Assert(files.Select(f => f.Path).SequenceEqual([final.Path, second.Path, final.Path]) && issues.Count == 0,
            "Each alternate replaces only the first matching slot; a surviving unprefixed Base path reopens through the highest mounted provider without removing its slot.");
        Assert(files[0].Providers.Select(p => p.SourceId).SequenceEqual(["base", "low", "high"]),
            "Slot replacement retains the provider chain needed for provenance and Bridge collision checks.");
        var opened = NativeContentFileResolver.ResolveOpenedFiles([exact, second, first, final, overlay], [baseline, low, high], "Open", issues);
        Assert(opened.Select(f => f.Path).SequenceEqual([first.Path, second.Path, final.Path]),
            "Constructed OpenFile paths must retain their full-path provider election instead of substring slot replacement.");
        var additive = NativeContentFileResolver.ResolveAdditiveFiles([exact, second, first, final, overlay],
            [baseline, low, high], "Additive", []);
        Assert(additive.Select(f => f.Path).SequenceEqual([first.Path, second.Path, exact.Path, overlay.Path, final.Path]),
            "Flags-9 explicit provider paths preserve Base and each Mod's same-path input in application order.");
        var upper = File(low, "inventory/archive/inventory/A.inventory.items.darkest");
        files = NativeContentFileResolver.Resolve([upper, overlay], [low], "Case", []);
        Assert(files.Count == 2, "strstr must not fold an uppercase filename in a different nested path.");
        var alias = File(high, "inventory/A.inventory.items.darkest");
        issues.Clear();
        _ = NativeContentFileResolver.Resolve([overlay, alias], [low, high], "Case alias", issues);
        Assert(issues.Any(i => i.Contains("differ only in case")), "Existing competing-case diagnostics must be retained.");
        var dlc = new ActiveContentSource("dlc", "DLC", "dlc-feature", Path.Combine(root, "dlc"), 0)
            { VirtualPathPrefix = "dlc/feature" };
        var physicalDlc = File(dlc, key);
        var prefixedMod = File(low, "dlc/feature/" + key);
        files = NativeContentFileResolver.Resolve([first, exact, physicalDlc, prefixedMod],
            [baseline, dlc, low], "Reopened DLC", []);
        Assert(files.Select(f => f.Path).SequenceEqual([prefixedMod.Path, physicalDlc.Path]),
            "An enumerated DLC path can reopen through a prefixed Mod file; a surviving Base root request instead falls back to the physical DLC after root Mod lookup misses.");
        Console.WriteLine("PASS: base/alternate first-match slots, Mod priority, provenance, case and canonical-open boundaries.");
    }

    private static void VerifyOverlayReopenedFiles(string runRoot)
    {
        var root = Path.Combine(runRoot, "overlay-reopened-files");
        var baseline = new ActiveContentSource("base", "Base", "base", Path.Combine(root, "base"), 0);
        var mod = new ActiveContentSource("mod", "Mod", "local", Path.Combine(root, "mod"), 0);
        const string path = "dungeons/cove/a.cove.2.mash.darkest";
        WriteMultiMash(baseline.Directory, path, QueryMashes("alpha_A"));
        WriteMultiMash(baseline.Directory, "dungeons/cove/archive/" + path, QueryMashes("alpha_A"));
        WriteMultiMash(mod.Directory, path, QueryMashes("bravo_A"));
        WriteMultiMash(baseline.Directory, "inventory/A.inventory.items.darkest",
            "inventory_item: .type estate .id shared .base_stack_limit 7 .estate_can_be_provision true\n");
        WriteMultiMash(mod.Directory, "inventory/a.inventory.items.darkest",
            "inventory_item: .type estate .id shared .base_stack_limit 13 .estate_can_be_provision true\n");
        foreach (var id in new[] { "alpha_A", "bravo_A" })
            WriteMultiMash(mod.Directory, $"monsters/{id[..^2]}/{id}/{id}.info.darkest", "display: .size 1\n");
        WriteFixtureManifest(mod.Directory);
        var content = QueryContent(root, [baseline, mod]);
        Assert(QuantityItemCatalog.LoadDefinitions(content, QuantityItemSaveContext.Raid).Single().BaseStackLimit == 7,
            "A surviving uppercase Base request must not reopen a lowercase manifest entry; the first item definition remains Base's limit 7.");
        var map = new BattleMapSnapshot(content.Profile.ProfileDirectory, "", "", "", "", "cove", 2, 1,
            null, null, null, null, null, null, null, null, null, false, [], [], [], DateTime.UtcNow);
        var catalog = BattleEncounterCatalog.Load(content, map);
        Assert(catalog.Issues.Count == 0, "Reopening a path must not make a proven table ambiguous.");
        foreach (var type in new[] { 0, 1, 2 })
        {
            var rows = catalog.Encounters.Where(row => row.MashType == type).ToArray();
            Assert(rows.Length == 2 && rows.All(row => row.MonsterIds.SequenceEqual(["bravo_A"])) &&
                rows.Select(row => row.MashIndex).SequenceEqual(new int?[] { 0, 1 }),
                "The replaced deep slot and surviving Base slot both read Mod bytes and each occupies one native encounter index.");
            foreach (var row in rows) BattleEncounterCatalog.ValidateDirectEncounter(row);
            Assert(BattleEncounterCatalog.ResolveAppendTarget(catalog, type).NextMashIndex == 2 &&
                BattleEncounterCatalog.ReadMaintenanceTable(content, "cove", 2, type).Count == 2,
                "Bridge and maintenance must preserve repeated reads instead of deduplicating physical files.");
        }
        Console.WriteLine("PASS: surviving Base paths reopen Mod bytes while preserving repeated encounter slots, direct indexes and Bridge counts.");
    }

    private static void VerifyOverlayCatalogs(string runRoot)
    {
        var count = 0;
        foreach (var kind in new[] { "base", "mode", "dlc-feature", "local", "workshop", "dlc-mod" })
        foreach (var test in new[] { "collision", "distinct", "case-distinct", "unlisted" })
        {
            var isMod = kind is "local" or "workshop" or "dlc-mod";
            if (test == "unlisted" && !isMod) continue;
            var root = Path.Combine(runRoot, "overlay-catalogs", kind, test);
            var source = Path.Combine(root, "source");
            var prefix = kind == "dlc-mod" ? "dlc/rq_feature/" : "";
            var written = new List<string>();
            void Write(string path, string text)
            {
                WriteMultiMash(source, prefix + path, text);
                written.Add(path);
            }
            var stem = test == "distinct" ? "other" : test == "case-distinct" ? "A" : "a";
            string Item(string id, int cap) => $"inventory_item: .type estate .id {id} .base_stack_limit {cap} .estate_can_be_provision true\n";
            string Trinkets(string id, int uses) => $$"""{"entries":[{"id":"shared","quest_uses":{{uses}}},{"id":"{{id}}","quest_uses":{{uses}}}]}""";
            foreach (var id in new[] { "alpha_A", "bravo_A", "zulu_A" })
                Write($"monsters/{id[..^2]}/{id}/{id}.info.darkest", "display: .size 1\n");
            Write("inventory/a.inventory.items.darkest", Item("shared", 13) + Item("real", 13));
            Write("inventory/a.inventory.system_configs.darkest", QueryCapacity(9));
            Write("trinkets/a.entries.trinkets.json", Trinkets("real", 9));
            Write("dungeons/cove/a.cove.2.mash.darkest", QueryMashes("bravo_A"));
            Write("dungeons/cove/z.cove.2.mash.darkest", QueryMashes("zulu_A"));
            if (isMod && test == "unlisted") WriteFixtureManifest(source);
            Write($"inventory/archive/inventory/{stem}.inventory.items.darkest", Item("shared", 7) + Item("ghost", 7));
            Write($"trinkets/archive/trinkets/{stem}.entries.trinkets.json", Trinkets("ghost", 2));
            Write($"dungeons/cove/archive/dungeons/cove/{stem}.cove.2.mash.darkest", QueryMashes("alpha_A"));
            if (isMod && test != "unlisted") WriteFixtureManifest(source);
            var sources = QuerySources(source, kind);
            if (kind == "dlc-mod")
                foreach (var path in written.Where(p => test != "unlisted" || !p.Contains("/archive/")))
                    WriteMultiMash(sources[1].Directory, path, File.ReadAllText(Path.Combine(source, prefix + path)));
            var content = QueryContent(root, sources);
            var keepNested = test is "distinct" or "case-distinct" || kind == "base";
            int limit = keepNested ? 7 : 13, uses = keepNested ? 2 : 9, realIndex = keepNested ? 1 : 0;
            var items = QuantityItemCatalog.LoadDefinitions(content, QuantityItemSaveContext.Raid);
            var item = items.Single(i => i.ItemId == "shared");
            var trinkets = TrinketCatalog.Load(content);
            var trinket = trinkets.Trinkets.Single(t => t.Id == "shared");
            Assert(item.BaseStackLimit == limit && trinket.QuestUses == uses &&
                   items.Any(i => i.ItemId == "ghost") == keepNested && trinkets.Trinkets.Any(t => t.Id == "ghost") == keepNested,
                $"{kind}/{test}: only files retained by this native query may supply item/trinket definitions.");
            var raid = JsonNode.Parse("""{"base_root":{"party":{"inventory":{"items":{}}}}}""")!.AsObject();
            var stacks = RaidInventorySaveEditor.SetAmount(raid, item, limit * 2 + 1, 9).UpdatedRoot;
            Assert(stacks["base_root"]!["party"]!["inventory"]!["items"]!.AsObject()
                .Select(p => p.Value!["amount"]!.GetValue<int>()).SequenceEqual([limit, limit, 1]), "The effective item limit reaches stack allocation.");
            var estate = JsonNode.Parse("""{"base_root":{"trinkets":{"items":{}}}}""")!.AsObject();
            var added = TrinketSaveEditor.AddCopies(estate, trinket, 1, 9).UpdatedRoot;
            Assert(added["base_root"]!["trinkets"]!["items"]!.AsObject().Single().Value!["quest_uses_remaining"]!.GetValue<int>() == uses,
                "The effective trinket definition reaches the new instance's saved counter.");
            var map = new BattleMapSnapshot(content.Profile.ProfileDirectory, "", "", "", "", "cove", 2, 1,
                null, null, null, null, null, null, null, null, null, false, [], [], [], DateTime.UtcNow);
            var catalog = BattleEncounterCatalog.Load(content, map);
            Assert(catalog.Issues.Count == 0, $"{kind}/{test}: unexpected encounter issue: {string.Join("; ", catalog.Issues)}");
            foreach (var type in new[] { 0, 1, 2 })
            {
                var rows = catalog.Encounters.Where(r => r.MashType == type).ToArray();
                Assert(rows.Length == realIndex + 2 && rows[realIndex].MonsterIds.SequenceEqual(["bravo_A"]),
                    "Current table and original-slot replacement must use the native file list.");
                BattleEncounterCatalog.ValidateDirectEncounter(rows[realIndex]);
                Assert(BattleEncounterCatalog.ResolveAppendTarget(catalog, type).NextMashIndex == realIndex + 2 &&
                       BattleEncounterCatalog.ReadMaintenanceTable(content, "cove", 2, type).Count == realIndex + 2,
                    "Direct placement, Bridge append and maintenance must share the corrected count.");
                Assert(catalog.BridgeEncounters.Any(r => r.MashType == type && r.MonsterIds.SequenceEqual(["alpha_A"])) == keepNested,
                    "Global Bridge discovery must not revive a shadowed formation.");
            }
            var fingerprint = BattleEncounterCatalog.CaptureContentFingerprint(sources);
            Write("dungeons/cove/a.cove.2.mash.darkest", QueryMashes("zulu_A"));
            Assert(BattleEncounterCatalog.CaptureContentFingerprint(sources) != fingerprint, "Winning content changes must refresh the fingerprint.");
            bool rejected = false;
            try { BattleEncounterCatalog.ValidateDirectEncounter(catalog.DirectEncounters.Last()); }
            catch (InvalidOperationException) { rejected = true; }
            Assert(rejected, "A changed winning file must invalidate the previously selected table.");
            count++;
        }
        Assert(count == 21, "All source/query controls must run.");
        Console.WriteLine("PASS: 21 source/query fixtures, 63 encounter tables, item stacks, trinket counters and refresh guards.");
    }

    private static void VerifyOverlayOpenConsumers(string runRoot)
    {
        foreach (var kind in new[] { "local", "workshop", "dlc-mod" })
        {
            var root = Path.Combine(runRoot, "overlay-open-consumers", kind);
            var source = Path.Combine(root, "source");
            var prefix = kind == "dlc-mod" ? "dlc/rq_feature/" : "";
            void Write(string path, string text) => WriteMultiMash(source, prefix + path, text);
            Write("heroes/query/query.info.darkest", "armour: .name coat .hp 20\nextra_battle_loot: .code query_loot\n");
            Write("heroes/archive/heroes/query/query.info.darkest", "armour: .name coat .hp 999\n");
            Write("monsters/alpha/alpha_A/alpha_A.info.darkest", "display: .size 1\n");
            Write("monsters/archive/monsters/alpha/alpha_A/alpha_A.info.darkest", "display: .size 4\n");
            Write("campaign/roster/roster.variables.json", RosterThresholds(7));
            Write("campaign/roster/archive/campaign/roster/roster.variables.json", "{ invalid decoy");
            Write("inventory/query.inventory.items.darkest", "inventory_item: .type estate .id query_token .base_stack_limit 2 .estate_can_be_provision false\n");
            Write("loot/query.loot.json", QueryLootJson);
            Write("props/prop_definitions.json", "{ invalid ignored Mod root default");
            Write("props/archive/props/prop_definitions.json", """{"props":[{"name":"nested_parent","default_data":{"inherits_from":{"prop_type_name":"root_parent"}}}]}""");
            Write("props/child/trap_definitions.json", """{"props":[{"name":"query_trap","default_data":{"inherits_from":{"prop_type_name":"nested_parent"}}}]}""");
            Write("dungeons/query/query.props.darkest", "traps: .chance 1 .types query_trap\n");
            Write("dungeons/query/archive/dungeons/query/query.props.darkest", "traps: .chance 1 .types ghost\n");
            WriteFixtureManifest(source);
            var baseline = Path.Combine(root, "baseline");
            WriteMultiMash(baseline, "props/prop_definitions.json", """{"props":[{"name":"root_parent","default_data":{"instance_type":"trap"}}]}""");
            var content = QueryContent(root, new[] { new ActiveContentSource("base", "Base", "base", baseline, 0) }
                .Concat(QuerySources(source, kind)).ToArray());
            var heroes = HeroClassCatalog.Load(content);
            Assert(heroes.HeroClasses.Single().BaseHp == 20 && heroes.ResolveLevelThresholds.SequenceEqual(Enumerable.Range(0, 7).Select(i => i * 7)),
                "Canonical hero/roster opens must not consume nested decoys.");
            Assert(BattleEncounterCatalog.ReadMaintenanceMonsterSizes(content.Sources)["alpha_A"] == 1,
                "Canonical monster metadata must retain direct-open rules.");
            var raid = JsonNode.Parse("""{"base_root":{"party":{"inventory":{"items":{}}}}}""")!.AsObject();
            Assert(QuantityItemCatalog.LoadRaid(content, raid).Items.Single().ReferenceStatus == QuantityItemReferenceStatus.ConfirmedActive,
                "Actor item-reference lookup must agree with the canonical hero definition.");
            var attachments = BattleRoomAttachmentCatalog.Load(content);
            var trap = attachments.GetCandidates(BattleRoomAttachmentKind.Trap, "query").Single();
            Assert(trap.Id == "query_trap", "Root prop opens and nested prop searches must each retain their own result list.");
            BattleRoomAttachmentCatalog.ValidateDefinition(trap);
        }
        Console.WriteLine("PASS: canonical actors, actor item references, roster variables and separate root/nested prop consumers.");
    }

    private static void VerifyOverlayQueryBoundaries(string runRoot)
    {
        var failures = new List<string>();
        foreach (var kind in new[] { "local", "workshop", "dlc-mod" })
        {
            var root = Path.Combine(runRoot, "overlay-query-boundaries", kind);
            var source = Path.Combine(root, "source");
            var prefix = kind == "dlc-mod" ? "dlc/rq_feature/" : "";
            void Write(string path, string text) => WriteMultiMash(source, prefix + path, text);
            Write("inventory/query.inventory.items.darkest", "inventory_item: .type estate .id jc_token .base_stack_limit 2\n" +
                "inventory_item: .type estate .id query_token .base_stack_limit 2 .estate_can_be_provision false\n");
            Write("campaign/town_events/loot/a.loot.json.town_events.events.json",
                """{"events":[{"id":"jc_event","data":[{"type":"bonus_currency","string_data":"jc_token"}]}]}""");
            Write("loot/a.loot.json", """{"loot_tables":[]}""");
            Write("loot/query.loot.json", QueryLootJson);
            Write("curios/archive/curios/a_curio_type_library.csv", QueryTypeCsv);
            Write("curios/a_curio_type_library.csv", QueryTypeCsv.Replace("query_curio", "other_curio").Replace("query_loot", "other_loot"));
            Write("curios/query_curio_props.csv", QueryPropCsv);
            Write("props/support/prop_definitions.json", """{"props":[{"name":"curio_default","default_data":{"instance_type":"curio"}}]}""");
            Write("dungeons/cove/cove.props.darkest", "room_curios: .chance 1 .types query_curio base_only_curio\n");
            WriteFixtureManifest(source);
            var baseline = Path.Combine(root, "baseline");
            WriteMultiMash(baseline, "curios/a_curio_type_library.csv",
                QueryTypeCsv.Replace("query_curio", "base_only_curio").Replace("query_loot", "base_loot"));
            WriteMultiMash(baseline, "curios/base_curio_props.csv", QueryPropCsv.Replace("query_curio", "base_only_curio"));
            var content = QueryContent(root, new[] { new ActiveContentSource("base", "Base", "base", baseline, 0) }
                .Concat(QuerySources(source, kind)).ToArray());
            var town = QuantityItemCatalog.Load(content, JsonNode.Parse("""{"base_root":{"wallet":{},"estate_items":{"items":{}}}}""")!.AsObject());
            var token = town.Items.Single(i => i.ItemId == "jc_token");
            if (token.ReferenceStatus != QuantityItemReferenceStatus.ConfirmedActive || token.IsHiddenByDefault)
                failures.Add($"{kind}: event query was erased by an independent Loot query ({token.ReferenceStatus}).");
            var raid = QuantityItemCatalog.LoadRaid(content, JsonNode.Parse("""{"base_root":{"party":{"inventory":{"items":{}}}}}""")!.AsObject());
            if (raid.Items.Single(i => i.ItemId == "query_token").ReferenceStatus != QuantityItemReferenceStatus.ConfirmedActive)
                failures.Add($"{kind}: flags-9 Curio item reference was erased.");
            var attachments = BattleRoomAttachmentCatalog.Load(content);
            foreach (var id in new[] { "query_curio", "base_only_curio" })
            {
                var curio = attachments.Curios.SingleOrDefault(c => c.Id == id);
                if (curio is null) failures.Add($"{kind}: flags-9 Curio type {id} was erased.");
                else
                {
                    try { BattleRoomAttachmentCatalog.ValidateDefinition(curio); }
                    catch (InvalidOperationException ex) { failures.Add($"{kind}/{id}: {ex.Message}"); }
                }
            }
        }
        Assert(failures.Count == 0, string.Join("\n", failures));
        Console.WriteLine("PASS: independent event/Loot query lists and flags-9 Curio types/references across Mod sources.");
    }

    private static async Task VerifyOverlayBattlePersistenceAsync(string runRoot, DsonSaveCodec codec)
    {
        foreach (var type in new[] { 0, 1, 2 })
        {
            var root = Path.Combine(runRoot, "overlay-binary-persistence", type.ToString());
            var game = Path.Combine(root, "game");
            var mods = Path.Combine(game, "mods");
            var mod = Path.Combine(mods, "overlay");
            var profile = WriteBattleSafetyProfile(Path.Combine(root, "profile"));
            WriteMultiMash(mod, "project.xml", "<project><Title>Overlay Slots</Title></project>");
            var deep = WriteMultiMash(mod, "dungeons/cove/archive/dungeons/cove/a.cove.2.mash.darkest", QueryMashes("alpha_A"));
            WriteMultiMash(mod, "dungeons/cove/a.cove.2.mash.darkest", QueryMashes("bravo_A"));
            WriteMultiMash(mod, "dungeons/cove/z.cove.2.mash.darkest", QueryMashes("zulu_A"));
            WriteFixtureManifest(mod);
            WriteMultiMash(game, "dungeons/ruins/a.ruins.2.mash.darkest", QueryMashes("foreign_A"));
            CreateBattleMonsterDefinitions(game, ["alpha_A", "bravo_A", "zulu_A", "foreign_A"]);
            var gameSave = Path.Combine(profile.ProfileDirectory, "persist.game.json");
            var config = JsonNode.Parse(File.ReadAllText(gameSave))!;
            config["base_root"]!["applied_ugcs_1_0"] = new JsonObject { ["0"] = new JsonObject { ["name"] = "Overlay Slots", ["source"] = "mod_local_source" } };
            File.WriteAllText(gameSave, config.ToJsonString());
            var raid = JsonNode.Parse(File.ReadAllText(profile.RaidSavePath))!;
            raid["base_root"]!["start_elapsed_time"] = 100;
            raid["base_root"]!["raid_instance"]!["id"] = "overlay-slots";
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
            var area = type == 0 ? "coAB" : type == 1 ? "rooC" : "rooB";
            var tile = type == 0 ? "tile1" : "tile0";
            var maps = new BattleMapEditService(codec, locations);
            var direct = catalog.DirectEncounters.Single(r => r.MashType == type && r.MonsterIds.SequenceEqual(["bravo_A"]));
            await maps.CommitAsync(await maps.PreparePlaceBattleAsync(profile, snapshot, area, tile, direct));
            snapshot = await reader.LoadAsync(profile.ProfileDirectory);
            Assert(snapshot.Areas.Single(a => a.AreaId == area).Tiles.Single(t => t.TileId == tile).MashIndex == 0,
                "Direct DSON placement must save the real native index zero.");
            var bridge = new ManagedBattleEncounterBridgeService(codec, locations);
            var added = await bridge.EnsureEncounterAsync(profile, snapshot, content, catalog,
                catalog.BridgeEncounters.Single(r => r.OriginDungeonId == "ruins" && r.MashType == type), game, null, mods);
            Assert(added.MashIndex == 2, "Bridge installation must append at native index two.");
            snapshot = await reader.LoadAsync(profile.ProfileDirectory);
            await maps.CommitAsync(await maps.PreparePlaceBattleAsync(profile, snapshot, area, tile, added.DirectEncounter));
            snapshot = await reader.LoadAsync(profile.ProfileDirectory);
            var saved = snapshot.Areas.Single(a => a.AreaId == area).Tiles.Single(t => t.TileId == tile);
            Assert(saved.MashIndex == 2 && saved.MashType == type && DsonSaveCodec.IsDson(profile.MapSavePath),
                "Bridge DSON replacement must preserve its corrected type/index binding.");
            File.AppendAllText(deep, QueryMashes("alpha_A"));
            Assert(!(await bridge.ReconcileAsync(added.ActiveContent, game, mods)).Changed,
                "Changing an overridden nested row must not clear correctly placed battles or change Bridge indexes.");
            await maps.CommitAsync(await maps.PrepareDeleteContentAsync(profile, snapshot, area, tile));
            snapshot = await reader.LoadAsync(profile.ProfileDirectory);
            Assert(snapshot.Areas.Single(a => a.AreaId == area).Tiles.Single(t => t.TileId == tile).Content == BattleMapTileContent.Nothing,
                "Deletion still clears the chosen battle after corrected direct/Bridge writes.");
        }
        Console.WriteLine("PASS: all three battle types, direct/Bridge DSON saves, shadowed-file maintenance and deletion.");
    }
}
