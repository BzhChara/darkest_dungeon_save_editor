internal static partial class ContractSuite
{
    private static async Task VerifyResourceDirectoryQueriesAsync(string runRoot, DsonSaveCodec codec)
    {
        VerifyManifestReferenceAliasOrder(runRoot);
        foreach (var kind in new[] { "base", "mode", "dlc-feature", "local", "workshop", "dlc-mod" })
        foreach (var variant in new[] { "lower", "upper", "physical-alias" })
        {
            var root = Path.Combine(runRoot, "resource-directory-query", kind, variant);
            var baseline = Path.Combine(root, "baseline");
            // Base has one initial device, not two independent base mounts.
            // Exercise physical aliases by updating files inside that device.
            var source = kind == "base" ? baseline : Path.Combine(root, "overlay");
            var prefix = kind == "dlc-mod" ? "dlc/rq_feature/" : "";
            var isMod = kind is "local" or "workshop" or "dlc-mod";
            var accepted = !isMod || variant != "upper";
            WriteMultiMash(baseline, "inventory/a.inventory.items.darkest", "inventory_item: .type estate .id query_token .base_stack_limit 2\n");
            WriteMultiMash(baseline, "inventory/a.inventory.system_configs.darkest", QueryCapacity(4));
            WriteMultiMash(baseline, "heroes/query/query.info.darkest", "armour: .name coat .hp 20\nweapon: .name blade\n" +
                "combat_skill: .id attack .level 0 .effect QUERY\ngeneration: .is_generation_enabled true .number_of_random_combat_skills 1 " +
                ".number_of_class_specific_camping_skills 0 .number_of_shared_camping_skills 0\n");
            WriteMultiMash(baseline, "heroes/query/query_A/idle.png", "fixture");
            WriteMultiMash(baseline, "shared/buffs/a.buffs.json", QueryBuff(.25));
            WriteMultiMash(baseline, "shared/quirk/a.quirk_library.json",
                """{"quirks":[{"id":"rq_hp","is_positive":true,"buffs":["rq_hp"]},{"id":"runtime_q","random_chance":0,"is_positive":true}]}""");
            WriteMultiMash(baseline, "props/prop_definitions.json", """{"props":[{"name":"curio_default","default_data":{"instance_type":"curio"}}]}""");
            WriteMultiMash(baseline, "dungeons/cove/cove.props.darkest", "room_curios: .chance 1 .types query_curio\n");
            WriteMultiMash(baseline, "loot/query.loot.json", QueryLootJson);
            string Authored(string path) => prefix + (variant == "lower" ? path : char.ToUpperInvariant(path[0]) + path[1..]);
            void Write(string path, string value) => WriteMultiMash(source, Authored(path), value);
            Write("inventory/a.inventory.items.darkest", "inventory_item: .type estate .id query_token .base_stack_limit 9\n" +
                "inventory_item: .type estate .id query_added .base_stack_limit 9\n");
            Write("inventory/z.inventory.system_configs.darkest", QueryCapacity(99));
            Write("shared/buffs/z.buffs.json", QueryBuff(.75));
            Write("shared/quirk/z.quirk_library.json", """{"quirks":[{"id":"query_added","is_positive":true}]}""");
            Write("trinkets/a.entries.trinkets.json", """{"entries":[{"id":"query_added","quest_uses":7}]}""");
            Write("raid/camping/camping_skills.json", """{"skills":[{"id":"query_added","hero_classes":["query"]}]}""");
            Write("effects/query.effects.darkest", "effect: .name QUERY .disease runtime_q\n");
            Write("curios/query_curio_type_library.csv", QueryTypeCsv);
            Write("curios/query_curio_props.csv", QueryPropCsv);
            var sources = (kind == "base" ? Array.Empty<ActiveContentSource>() :
                    new[] { new ActiveContentSource("base", "Base", "base", baseline, 0) })
                .Concat(QuerySources(source, kind).Select(s => s.Kind is "local" or "workshop" ? s with { LoadOrder = -1000 } : s)).ToArray();
            if (isMod)
            {
                WriteFixtureManifest(source);
                if (variant == "physical-alias")
                {
                    var manifest = Path.Combine(source, "modfiles.txt");
                    File.WriteAllLines(manifest, File.ReadAllLines(manifest).Select(path => path.StartsWith(prefix, StringComparison.Ordinal)
                        ? prefix + char.ToLowerInvariant(path[prefix.Length]) + path[(prefix.Length + 1)..] : path));
                }
            }
            var content = QueryContent(root, sources);
            var raid = JsonNode.Parse("""{"base_root":{"party":{"inventory":{"items":{}}}}}""")!.AsObject();
            var items = QuantityItemCatalog.LoadRaid(content, raid);
            var token = items.Items.Single(i => i.ItemId == "query_token");
            var heroes = HeroClassCatalog.Load(content);
            var hero = heroes.HeroClasses.Single(h => h.Id == "query");
            var trinkets = TrinketCatalog.Load(content);
            var curio = BattleRoomAttachmentCatalog.Load(content).Curios.SingleOrDefault(c => c.Id == "query_curio");
            Assert(token.BaseStackLimit == (accepted ? 9 : 2) && items.Items.Any(i => i.ItemId == "query_added") == accepted &&
                   items.RaidStorage!.MaxSlots == (accepted ? 99 : 4) && TrinketStorageCatalog.Load(content).Storage!.MaxSlots == (accepted ? 99 : 4),
                $"{kind}/{variant}: raw manifest directories must control item definitions, stack limits and both inventory capacities.");
            Assert(trinkets.Trinkets.Any(t => t.Id == "query_added") == accepted &&
                   heroes.InitialQuirks.Any(q => q.Id == "query_added") == accepted &&
                   hero.SharedCampingSkillIds.Contains("query_added") == accepted && hero.RuntimeQuirkSignals.Any() == accepted,
                $"{kind}/{variant}: trinket, quirk, camping and Effect consumers must share the appropriate directory device.");
            if (accepted)
            {
                Assert(curio is not null, $"{kind}/{variant}: consumed curio resources must be present.");
                BattleRoomAttachmentCatalog.ValidateDefinition(curio!);
            }
            else if (curio is not null) Assert(await CaptureSaveFailureAsync(() => { BattleRoomAttachmentCatalog.ValidateDefinition(curio); return Task.CompletedTask; }) is InvalidOperationException,
                $"{kind}/{variant}: curio mapping/type files from unqueried manifest directories must not enable placement.");
            var generated = StagecoachHeroCandidateFactory.Generate(heroes with { HeroNames = ["Query Hero"] }, hero, 11, ["rq_hp"]);
            Assert(generated.Preview.CurrentHp == (accepted ? 35 : 25),
                $"{kind}/{variant}: rejected Buffs must not alter generated current_hp.");
            if (kind == "local")
            {
                var town = StagecoachHeroSaveEditor.AddCandidate(
                    JsonNode.Parse("""{"base_root":{"buildings":{"stage_coach":{"store":{"hero_recruit":{"generated":{}}}}}}}""")!.AsObject(),
                    JsonNode.Parse("""{"base_root":{"nextGuid":950,"heroes":{}}}""")!.AsObject(),
                    JsonNode.Parse("""{"base_root":{"purchases":{}}}""")!.AsObject(), generated.Candidate, generated.UpgradePurchases).UpdatedTown;
                var restored = await RoundtripDirectoryQueryAsync(root, "town", town, codec);
                Assert(restored["base_root"]!["buildings"]!["stage_coach"]!["store"]!["hero_recruit"]!["generated"]!["950"]!["actor"]!["current_hp"]!.GetValue<double>() == (accepted ? 35 : 25),
                    "Directory-correct hero HP must survive DSON persistence.");
                var slots = items.RaidStorage!.MaxSlots;
                var changed = RaidInventorySaveEditor.SetAmount(raid, token, 8, slots).UpdatedRoot;
                restored = await RoundtripDirectoryQueryAsync(root, "raid", changed, codec);
                Assert(restored["base_root"]!["party"]!["inventory"]!["items"]!.AsObject().Select(p => p.Value!["amount"]!.GetValue<int>())
                    .SequenceEqual(accepted ? new[] { 8 } : new[] { 2, 2, 2, 2 }), "Actual stack allocation must survive DSON persistence.");
                if (accepted) RaidInventorySaveEditor.SetAmount(raid, token, 40, slots);
                else Assert(await CaptureSaveFailureAsync(() => { RaidInventorySaveEditor.SetAmount(raid, token, 40, slots); return Task.CompletedTask; })
                    is InvalidOperationException, "Ignored capacity/stack overrides must not permit an oversized backpack.");
            }
            if (isMod && variant == "upper")
            {
                var before = ProfileCatalogContentFingerprint.Capture(sources);
                var manifest = Path.Combine(source, "modfiles.txt");
                File.WriteAllLines(manifest, File.ReadAllLines(manifest).Select(path => path.StartsWith(prefix, StringComparison.Ordinal)
                    ? prefix + char.ToLowerInvariant(path[prefix.Length]) + path[(prefix.Length + 1)..] : path));
                Assert(ProfileCatalogContentFingerprint.Capture(sources) != before &&
                       QuantityItemCatalog.LoadRaid(content, raid).Items.Single(i => i.ItemId == "query_token").BaseStackLimit == 9 &&
                       HeroClassCatalog.Load(content).InitialQuirks.Single(q => q.Id == "rq_hp").MaxHpModifiers.Single().Amount == .75,
                    "Changing raw manifest casing must refresh previously excluded catalogs while opening the same physical files.");
            }
        }
        VerifyManifestSubdirectoryQueries(runRoot);
        Console.WriteLine("PASS: resource directory devices across six sources, HP/stack/capacity DSON, aliases, reference queries and refresh.");
    }

    private static async Task<JsonObject> RoundtripDirectoryQueryAsync(string root, string name, JsonObject value, DsonSaveCodec codec)
    {
        var proposed = Path.Combine(root, name + ".proposed.json"); var binary = Path.Combine(root, name + ".dson");
        var decoded = Path.Combine(root, name + ".roundtrip.json");
        File.WriteAllText(proposed, value.ToJsonString());
        await codec.EncodeAsync(proposed, binary, null); await codec.DecodeAsync(binary, decoded);
        return JsonNode.Parse(File.ReadAllText(decoded))!.AsObject();
    }

    private static void VerifyManifestSubdirectoryQueries(string runRoot)
    {
        foreach (var badDirectory in new[] { "dlc/rq_feature/shared/Buffs", "Dlc/rq_feature/shared/buffs", "dlc/RQ_feature/shared/buffs" })
        {
            var root = Path.Combine(runRoot, "buff-manifest-subdirectory", badDirectory.Replace('/', '_'));
            var source = Path.Combine(root, "source");
            const string prefix = "dlc/rq_feature/";
            const string queryPath = prefix + "shared/buffs/z.buffs.json";
            WriteMultiMash(source, prefix + "shared/buffs/a.buffs.json", QueryBuff(.25));
            WriteMultiMash(source, queryPath, QueryBuff(.75));
            WriteMultiMash(source, prefix + "shared/quirk/query.quirk_library.json", """{"quirks":[{"id":"rq_hp","is_positive":true,"buffs":["rq_hp"]}]}""");
            WriteFixtureManifest(source);
            var manifest = Path.Combine(source, "modfiles.txt");
            var lowercase = File.ReadAllText(manifest);
            File.WriteAllText(manifest, lowercase.Replace(queryPath, badDirectory + "/z.buffs.json", StringComparison.Ordinal) +
                "\n" + badDirectory + "/missing.buffs.json\n");
            var content = QueryContent(root, QuerySources(source, "dlc-mod"));
            var catalog = HeroClassCatalog.Load(content);
            Assert(catalog.InitialQuirks.Single().MaxHpModifiers.Single().Amount == .25 &&
                   !catalog.Issues.Any(issue => issue.Contains("missing.buffs.json", StringComparison.Ordinal)),
                "Raw manifest subdirectories and DLC mount prefixes are case sensitive, including missing-file diagnostics.");
            File.WriteAllText(manifest, lowercase);
            Assert(HeroClassCatalog.Load(content).InitialQuirks.Single().MaxHpModifiers.Single().Amount == .75,
                "Correcting only the raw manifest directory must restore the consumed Buff.");
        }
        var cases = new (string Path, string Text)[]
        {
            ("loot/query.loot.json", QueryLootJson),
            ("campaign/provision/query.provision.json", """{"raid_starting_length_inventory_item_lists":[[{"type":"estate","id":"query_token","amount":1}]]}"""),
            ("curios/query_curio_type_library.csv", QueryTypeCsv)
        };
        foreach (var kind in new[] { "local", "workshop", "dlc-mod" })
        foreach (var (path, value) in cases)
        {
            var root = Path.Combine(runRoot, "reference-directory-query", kind, path.Replace('/', '_'));
            var source = Path.Combine(root, "source");
            var prefix = kind == "dlc-mod" ? "dlc/rq_feature/" : "";
            WriteMultiMash(source, prefix + "inventory/query.inventory.items.darkest", "inventory_item: .type estate .id query_token .base_stack_limit 2\n");
            WriteMultiMash(source, prefix + "heroes/query/query.info.darkest", "extra_battle_loot: .code query_loot\n");
            if (!path.StartsWith("loot/", StringComparison.Ordinal)) WriteMultiMash(source, prefix + "loot/query.loot.json", QueryLootJson);
            var queried = path.Contains("provision/", StringComparison.Ordinal) ? path.Replace("provision/", "Provision/", StringComparison.Ordinal)
                : char.ToUpperInvariant(path[0]) + path[1..];
            WriteMultiMash(source, prefix + queried, value);
            // Do not let the hero establish the reference when testing the CSV/provision root itself.
            if (!path.StartsWith("loot/", StringComparison.Ordinal)) File.WriteAllText(Path.Combine(source, prefix + "heroes/query/query.info.darkest"), "// registration only\n");
            WriteFixtureManifest(source);
            var content = QueryContent(root, QuerySources(source, kind));
            var raid = JsonNode.Parse("""{"base_root":{"party":{"inventory":{"items":{}}}}}""")!.AsObject();
            Assert(QuantityItemCatalog.LoadRaid(content, raid).Items.Single().ReferenceStatus == QuantityItemReferenceStatus.SuspectedUnused,
                $"{kind}/{queried}: a differently cased manifest directory must not create an item reference.");
            var manifest = Path.Combine(source, "modfiles.txt");
            File.WriteAllText(manifest, File.ReadAllText(manifest).Replace(queried, path, StringComparison.Ordinal));
            Assert(QuantityItemCatalog.LoadRaid(content, raid).Items.Single().ReferenceStatus == QuantityItemReferenceStatus.ConfirmedActive,
                $"{kind}/{path}: the lowercase query must read the existing Windows physical alias.");
        }
    }

    private static void VerifyManifestReferenceAliasOrder(string runRoot)
    {
        var cases = new (string Path, string Text)[]
        {
            ("loot/query.loot.json", QueryLootJson),
            ("curios/query_curio_type_library.csv", QueryTypeCsv),
            ("campaign/provision/query.provision.json", """{"raid_starting_length_inventory_item_lists":[[{"type":"estate","id":"query_token","amount":1}]]}""")
        };
        foreach (var kind in new[] { "local", "workshop", "dlc-mod" })
        foreach (var (path, value) in cases)
        foreach (var prefixAlias in new[] { false, true })
        foreach (var invalidFirst in new[] { false, true })
        {
            if (prefixAlias && kind != "dlc-mod") continue;
            var root = Path.Combine(runRoot, "manifest-reference-alias-order", kind, path.Replace('/', '_'), prefixAlias + "-" + invalidFirst);
            var source = Path.Combine(root, "source");
            var prefix = kind == "dlc-mod" ? "dlc/rq_feature/" : "";
            var accepted = prefix + path;
            var rejected = prefixAlias ? "Dlc/rq_feature/" + path : prefix + char.ToUpperInvariant(path[0]) + path[1..];
            WriteMultiMash(source, prefix + "inventory/query.inventory.items.darkest", "inventory_item: .type estate .id query_token .base_stack_limit 2\n");
            WriteMultiMash(source, prefix + "heroes/query/query.info.darkest", path.StartsWith("loot/", StringComparison.Ordinal)
                ? "extra_battle_loot: .code query_loot\n" : "// registration only\n");
            if (path.StartsWith("curios/", StringComparison.Ordinal)) WriteMultiMash(source, prefix + "loot/query.loot.json", QueryLootJson);
            WriteMultiMash(source, accepted, value);
            WriteFixtureManifest(source);
            var manifest = Path.Combine(source, "modfiles.txt");
            File.WriteAllText(manifest, File.ReadAllText(manifest).Replace(accepted,
                invalidFirst ? rejected + "\n" + accepted : accepted + "\n" + rejected, StringComparison.Ordinal));
            var content = QueryContent(root, QuerySources(source, kind));
            var raid = JsonNode.Parse("""{"base_root":{"party":{"inventory":{"items":{}}}}}""")!.AsObject();
            Assert(QuantityItemCatalog.LoadRaid(content, raid).Items.Single().ReferenceStatus == QuantityItemReferenceStatus.ConfirmedActive,
                $"{kind}/{path}/prefix={prefixAlias}/invalid-first={invalidFirst}: an invalid manifest alias must never discard the valid consumer before filtering.");
        }
    }
}
