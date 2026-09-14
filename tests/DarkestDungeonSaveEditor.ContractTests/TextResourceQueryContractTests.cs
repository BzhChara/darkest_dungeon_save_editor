internal static partial class ContractSuite
{
    public static async Task RunFileQueriesOnlyAsync(string repositoryRoot)
    {
        var fixture = BuildContractFixture(repositoryRoot);
        await RunTextResourceQueryContractsAsync(fixture.RunRoot, fixture.Codec);
        await RunEncounterFileQueryContractsAsync(fixture.RunRoot, fixture.Codec);
        Console.WriteLine($"Artifacts: {fixture.RunRoot}");
    }

    private static ActiveContentSnapshot QueryContent(string root, IReadOnlyList<ActiveContentSource> sources)
    {
        var game = WriteMultiMash(root, "profile/persist.game.json",
            """{"base_root":{"inraid":false,"raiddungeon":"none","raid_save":""}}""");
        var profile = new SaveProfile("profile_query", Path.GetDirectoryName(game)!,
            Path.Combine(Path.GetDirectoryName(game)!, "persist.estate.json"), "contract-query", DateTime.UtcNow);
        return new(profile, "normal", sources, [], root, game, sources.Count(s => s.Kind is "local" or "workshop"), ComputeSha256(game));
    }

    private const string QueryTypeCsv = ",,ID STRING,,RESULT TYPES\n,,query_curio,,Nothing\n,,REGION FOUND,,Loot,1,100%,query_loot,1\n,,Item Interactions,,ITEM\n";
    private const string QueryPropCsv = "Name,Sprite,Type,UI\nquery_curio,query_sprite,query_curio,query_name\n";
    private const string QueryLootJson = """{"loot_tables":[{"id":"query_loot","entries":[{"type":"item","chances":1,"data":{"type":"estate","id":"query_token","amount":1}}]}]}""";
    private static string QueryCapacity(int count) =>
        $"inventory_system_config: .type raid .max_slots {count}\ninventory_system_config: .type trinket_storage .max_slots {count}\n";

    private static async Task RunTextResourceQueryContractsAsync(string runRoot, DsonSaveCodec codec)
    {
        await VerifyHeroProgressionResourcesAsync(runRoot, codec);
        VerifyActorReferenceQueryBoundaries(runRoot);
        VerifyItemReferenceContexts(runRoot);
        VerifyActorRecordReferenceBoundaries(runRoot);
        await VerifyResourceDirectoryQueriesAsync(runRoot, codec);
        foreach (var kind in new[] { "base", "mode", "dlc-feature", "local", "workshop", "dlc-mod" })
        {
            var root = Path.Combine(runRoot, "text-resource-queries", kind);
            var sourceRoot = Path.Combine(root, "source");
            var prefix = kind == "dlc-mod" ? "dlc/rq_feature/" : "";
            // Actor and regional pool loaders make root requests, independently
            // of the DLC-prefixed resource families exercised here.
            void Write(string path, string text) => WriteMultiMash(sourceRoot,
                (path.StartsWith("heroes/", StringComparison.Ordinal) || path == "dungeons/cove/cove.props.darkest" ? "" : prefix) + path, text);
            Write("inventory/a.inventoryXitems.darkest", "inventory_item: .type estate .id query_token .base_stack_limit 2\n");
            Write("inventory/b.inventory.items.darkest",
                "inventory_item: .type estate .id query_token .base_stack_limit 9\ninventory_item: .type estate .id single .base_stack_limit 1\n");
            Write("inventory/a.inventory.system_configs.darkest", QueryCapacity(8));
            Write("inventory/z.inventory.system_configsXdarkest", QueryCapacity(2));
            Write("heroes/query/query.info.darkest", "armour: .name coat .hp 20\ncombat_skill: .id skill .level 0 .effect QUERY\n");
            Write("shared/quirk/query.quirk_library.json", """{"quirks":[{"id":"runtime_q","random_chance":0,"is_positive":true}]}""");
            Write("effects/query.effectsXdarkest", "effect: .name QUERY .disease runtime_q\n");
            Write("curios/query_curio_type_libraryXcsv", QueryTypeCsv);
            Write("curios/query_curio_propsXcsv", QueryPropCsv);
            Write("props/support/prop_definitions.json", """{"props":[{"name":"curio_default","default_data":{"instance_type":"curio"}}]}""");
            Write("dungeons/cove/cove.props.darkest", "room_curios: .chance 1 .types query_curio\n");
            Write("loot/inventory/query.loot.json", QueryLootJson);
            foreach (var excluded in new[] { "README.md", "inventory/README.txt", "inventory/readme.darkest",
                "inventory/noXinventory.items.darkest", "effects/noXeffects.darkest", "curios/notes.csv", "curios/notesXcsv",
                "curios/query_curio_props.csv.bak", "heroes/query.effects.darkest", "raid/query_curio_type_library.csv" })
                Write(excluded, "invalid resource text");
            WriteMultiMash(sourceRoot, "dlc/disabled/inventory/a.inventory.items.darkest", "inventory_item: .type estate .id disabled .base_stack_limit 99\n");
            if (kind is "local" or "workshop" or "dlc-mod")
            {
                WriteFixtureManifest(sourceRoot);
                Write("inventory/0.inventory.items.darkest", "inventory_item: .type estate .id query_token .base_stack_limit 99\n");
                Write("effects/0.effects.darkest", "effect: .name UNLISTED .disease runtime_q\n");
            }
            var sources = QuerySources(sourceRoot, kind);
            var content = QueryContent(root, sources);
            var raid = JsonNode.Parse("""{"base_root":{"party":{"inventory":{"items":{}}}}}""")!.AsObject();
            var catalog = QuantityItemCatalog.LoadRaid(content, raid);
            var token = catalog.Items.Single(item => item.ItemId == "query_token");
            var capacity = RaidInventoryStorageCatalog.Load(content);
            Assert(token.BaseStackLimit == 2 && catalog.Items.Count == 2 &&
                   capacity.Storage?.MaxSlots == 2 && TrinketStorageCatalog.Load(content).Storage?.MaxSlots == 2,
                $"{kind}: native inventory queries must retain first-definition stack limits and last-assigned capacities.");
            Assert(HeroClassCatalog.Load(content).HeroClasses.Single(h => h.Id == "query").RuntimeQuirkSignals.Count == 1,
                $"{kind}: a manifest-eligible native Effect name must supply the skill's quirk signal.");
            var props = BattleRoomAttachmentCatalog.Load(content);
            var curio = props.Curios.Single(p => p.Id == "query_curio");
            BattleRoomAttachmentCatalog.ValidateDefinition(curio);
            if (kind is "local" or "workshop" or "dlc-mod")
                Assert(token.ReferenceStatus == QuantityItemReferenceStatus.ConfirmedActive && !token.IsHiddenByDefault,
                    $"{kind}: a native curio CSV and nested loot/inventory consumer must make the referenced item active.");
            Assert(!catalog.Issues.Concat(props.Issues).Any(issue => issue.Contains("README") || issue.Contains("notes") || issue.Contains("noX")),
                "Listed documentation and files outside native queries must not be parsed or produce missing-resource errors.");
            var mutation = RaidInventorySaveEditor.SetAmount(raid, token, 3, capacity.Storage!.MaxSlots);
            var proposed = Path.Combine(root, "raid.proposed.json");
            var binary = Path.Combine(root, "raid.dson");
            var decoded = Path.Combine(root, "raid.roundtrip.json");
            File.WriteAllText(proposed, mutation.UpdatedRoot.ToJsonString());
            await codec.EncodeAsync(proposed, binary, null);
            await codec.DecodeAsync(binary, decoded);
            Assert(JsonNode.Parse(File.ReadAllText(decoded))!["base_root"]!["party"]!["inventory"]!["items"]!.AsObject()
                .Select(p => p.Value!["amount"]!.GetValue<int>()).SequenceEqual([2, 1]), "Correct stack limits must survive DSON output.");
            Assert(await CaptureSaveFailureAsync(() => {
                RaidInventorySaveEditor.SetAmount(raid, catalog.Items.Single(i => i.ItemId == "single"), 3, capacity.Storage.MaxSlots);
                return Task.CompletedTask;
            }) is InvalidOperationException, "A third slot must be rejected under the actual two-slot capacity.");
            var edits = new[] {
                ("inventory/z.inventory.system_configsXdarkest", QueryCapacity(3)),
                ("effects/query.effectsXdarkest", "effect: .name QUERY .disease runtime_x\n"),
                ("curios/query_curio_type_libraryXcsv", QueryTypeCsv.Replace("query_loot", "other_loot", StringComparison.Ordinal)),
                ("curios/query_curio_propsXcsv", QueryPropCsv.Replace("query_name", "other_name", StringComparison.Ordinal))
            };
            foreach (var (path, text) in edits)
            {
                var full = Path.Combine(sourceRoot, prefix + path);
                var before = ProfileCatalogContentFingerprint.Capture(sources);
                var stamp = File.GetLastWriteTimeUtc(full);
                var length = new FileInfo(full).Length;
                Write(path, text);
                File.SetLastWriteTimeUtc(full, stamp);
                Assert(new FileInfo(full).Length == length && ProfileCatalogContentFingerprint.Capture(sources) != before,
                    $"{kind}/{path}: unchanged length, timestamp and manifest must not hide changed consumed bytes.");
            }
            Assert(await CaptureSaveFailureAsync(() => { BattleRoomAttachmentCatalog.ValidateDefinition(curio); return Task.CompletedTask; })
                is InvalidOperationException, "Curio preflight must reject old bindings after nonliteral CSV changes.");
            Assert(RaidInventoryStorageCatalog.Load(content).Storage!.MaxSlots == 3 &&
                   HeroClassCatalog.Load(content).HeroClasses.Single(h => h.Id == "query").RuntimeQuirkSignals.Count == 0,
                "Fresh catalogs must use the changed capacities and Effects.");
            var overrideRoot = Path.Combine(root, "override");
            WriteMultiMash(overrideRoot, prefix + "inventory/a.inventoryXitems.darkest",
                "inventory_item: .type estate .id query_token .base_stack_limit 4\n");
            WriteMultiMash(overrideRoot, prefix + "inventory/z.inventory.system_configsXdarkest", QueryCapacity(4));
            WriteFixtureManifest(overrideRoot);
            var overlaid = content with { Sources = sources.Append(new ActiveContentSource("local:query-high", "High", "local", overrideRoot, -1000)).ToArray() };
            Assert(QuantityItemCatalog.LoadRaid(overlaid, raid).Items.Single(i => i.ItemId == "query_token").BaseStackLimit == 4 &&
                   RaidInventoryStorageCatalog.Load(overlaid).Storage!.MaxSlots == 4,
                "Higher Mod same-path replacement must still precede the resource-specific duplicate-ID rules.");
        }

        foreach (var folder in new[] { "rewards", "inventory", "effects", "trinkets", "shared/buffs", "localization" })
        {
            var root = Path.Combine(runRoot, "nested-reference-queries", folder.Replace('/', '-'));
            WriteMultiMash(root, "inventory/a.inventory.items.darkest", "inventory_item: .type estate .id query_token .base_stack_limit 2\n");
            WriteMultiMash(root, "inventory/a.inventory.system_configs.darkest", QueryCapacity(2));
            WriteMultiMash(root, "curios/query_curio_type_library.csv", QueryTypeCsv);
            WriteMultiMash(root, $"loot/{folder}/query.loot.json", QueryLootJson);
            WriteFixtureManifest(root);
            var content = QueryContent(Path.Combine(root, "context"), [new("local:nested", "Nested", "local", root, 0)]);
            var raid = JsonNode.Parse("""{"base_root":{"party":{"inventory":{"items":{}}}}}""")!.AsObject();
            var token = QuantityItemCatalog.LoadRaid(content, raid).Items.Single();
            Assert(token.ReferenceStatus == QuantityItemReferenceStatus.ConfirmedActive && !token.IsHiddenByDefault,
                $"A loaded loot/{folder} subtree must not be confused with a top-level definition library.");
        }

        foreach (var eligible in new[] { false, true })
        {
            var root = Path.Combine(runRoot, "text-query-missing", eligible.ToString());
            WriteMultiMash(root, "modfiles.txt", eligible
                ? "inventory/x.inventoryXitems.darkest\ninventory/x.inventory.system_configsXdarkest\neffects/x.effectsXdarkest\ncurios/x_curio_type_libraryXcsv\n"
                : "inventory/README.md\ninventory/noXinventory.items.darkest\neffects/README.darkest\ncurios/notes.csv\n");
            var content = QueryContent(Path.Combine(root, "context"), [new("local:missing", "Missing", "local", root, 0)]);
            var raid = JsonNode.Parse("""{"base_root":{"party":{"inventory":{"items":{}}}}}""")!.AsObject();
            Assert(HeroClassCatalog.Load(content).Issues.Count(i => i.Contains("Hero catalog file listed by Mod is missing")) == (eligible ? 1 : 0) &&
                   QuantityItemCatalog.LoadRaid(content, raid).Issues.Any(i => i.Contains("Inventory item file listed by Mod is missing")) == eligible &&
                   RaidInventoryStorageCatalog.Load(content).Issues.Any(i => i.Contains("Inventory system config listed by Mod is missing")) == eligible,
                "Missing-file diagnostics must agree with native-query eligibility.");
        }
        Console.WriteLine("PASS: inventory/Effect/curio queries, six source types, manifests, nested references, refresh, preflight and capacity-safe DSON.");
    }

    private static void VerifyActorReferenceQueryBoundaries(string runRoot)
    {
        var cases = new[]
        {
            ("heroes/inventory/README.darkest", "inventory_item: .type estate .id query_token", false),
            ("monsters/inventory/README.darkest", "inventory_item: .type estate .id query_token", false),
            ("heroes/effects/README.darkest", "extra_battle_loot: .code query_loot", false),
            ("monsters/shared/buffs/README.darkest", "loot: .code query_loot", false),
            ("heroes/inventory/inventory.info.darkest", "extra_battle_loot: .code query_loot", true),
            ("monsters/inventory/inventory_A/inventory_A.info.darkest", "loot: .code query_loot", true)
        };
        foreach (var kind in new[] { "local", "workshop", "dlc-mod" })
        foreach (var (path, text, consumed) in cases)
        {
            var root = Path.Combine(runRoot, "actor-reference-query-boundaries", kind, path.Replace('/', '_'));
            var prefix = kind == "dlc-mod" ? "dlc/rq_feature/" : "";
            WriteMultiMash(root, prefix + "inventory/query.inventory.items.darkest",
                "inventory_item: .type estate .id query_token .base_stack_limit 2 .estate_can_be_provision false\n");
            WriteMultiMash(root, prefix + "loot/query.loot.json", QueryLootJson);
            var actorPath = WriteMultiMash(root, prefix + path, text + "\n");
            WriteFixtureManifest(root);
            var content = QueryContent(Path.Combine(root, "context"), QuerySources(root, kind));
            var raid = JsonNode.Parse("""{"base_root":{"party":{"inventory":{"items":{}}}}}""")!.AsObject();
            var item = QuantityItemCatalog.LoadRaid(content, raid).Items.Single();
            var active = consumed && kind != "dlc-mod";
            var expected = active ? QuantityItemReferenceStatus.ConfirmedActive : QuantityItemReferenceStatus.SuspectedUnused;
            Assert(item.ReferenceStatus == expected && item.IsHiddenByDefault != active,
                $"{kind}/{path}: only canonical actor item consumers may establish references; expected {expected}, found {item.ReferenceStatus}.");
            if (consumed) continue;
            File.Delete(actorPath);
            var missing = QuantityItemCatalog.LoadRaid(content, raid);
            Assert(missing.Items.Single().ReferenceStatus == QuantityItemReferenceStatus.SuspectedUnused &&
                   !missing.Issues.Any(issue => issue.Contains("README", StringComparison.Ordinal)),
                "Missing unconsumed actor documentation must not mark reference analysis incomplete.");
        }
    }
}
