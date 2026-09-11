internal static partial class ContractSuite
{
    private static void VerifyItemReferenceContexts(string runRoot)
    {
        foreach (var kind in new[] { "local", "workshop", "dlc-mod" })
        foreach (var folder in new[] { "rewards", "upgrades", "campaign/town_events", "campaign/estate", "campaign/town", "loot/campaign/quest" })
        {
            var root = Path.Combine(runRoot, "reference-contexts", kind, folder.Replace('/', '_'));
            var source = Path.Combine(root, "source");
            var prefix = kind == "dlc-mod" ? "dlc/rq_feature/" : "";
            void Write(string path, string text) => WriteMultiMash(source, prefix + path, text);
            Write("inventory/query.inventory.items.darkest",
                "inventory_item: .type estate .id query_token .base_stack_limit 2 .estate_can_be_provision false\n");
            Write("inventory/query.inventory.system_configs.darkest", QueryCapacity(2));
            Write("loot/query.loot.json", QueryLootJson);
            var relative = $"curios/{folder}/query_curio_type_library.csv";
            Write(relative, QueryTypeCsv);
            Write("curios/query_curio_props.csv", QueryPropCsv);
            WriteFixtureManifest(source);
            var content = QueryContent(root, QuerySources(source, kind));
            var raid = JsonNode.Parse("""{"base_root":{"party":{"inventory":{"items":{}}}}}""")!.AsObject();
            var estate = JsonNode.Parse("""{"base_root":{"wallet":{},"estate_items":{"items":{}}}}""")!.AsObject();
            var raidItem = QuantityItemCatalog.LoadRaid(content, raid).Items.Single();
            var townItem = QuantityItemCatalog.Load(content, estate).Items.Single();
            Assert(raidItem.ReferenceStatus == QuantityItemReferenceStatus.ConfirmedActive && !raidItem.IsHiddenByDefault &&
                   townItem.ReferenceStatus == QuantityItemReferenceStatus.SuspectedUnused && townItem.IsHiddenByDefault,
                $"{kind}/{relative}: a Curio subdirectory must not change its raid consumer into a town consumer.");
            Assert(raidItem.BaseStackLimit == 2 && townItem.BaseStackLimit == 2,
                "Reference classification must preserve the inventory definition's stack limit.");

            var fingerprint = ProfileCatalogContentFingerprint.Capture(content.Sources);
            Write(relative, QueryTypeCsv.Replace("Loot,1,100%,query_loot,1", "Nothing", StringComparison.Ordinal));
            Assert(ProfileCatalogContentFingerprint.Capture(content.Sources) != fingerprint &&
                   QuantityItemCatalog.LoadRaid(content, raid).Items.Single().ReferenceStatus == QuantityItemReferenceStatus.SuspectedUnused,
                "A changed Curio root must refresh visibility without a manifest change.");

            // A listed missing or unreadable Curio is uncertainty for raid only,
            // regardless of any misleading subdirectory or DLC mount prefix.
            File.Delete(Path.Combine(source, prefix + relative));
            var missingRaid = QuantityItemCatalog.LoadRaid(content, raid);
            var missingTown = QuantityItemCatalog.Load(content, estate);
            Assert(missingRaid.Items.Single().ReferenceStatus == QuantityItemReferenceStatus.AnalysisIncomplete &&
                   missingRaid.Issues.Any(issue => issue.Contains("reference file listed by active Mod is missing")) &&
                   missingTown.Items.Single().ReferenceStatus == QuantityItemReferenceStatus.SuspectedUnused &&
                   !missingTown.Issues.Any(issue => issue.Contains("reference file listed by active Mod is missing")),
                "Missing reference diagnostics must use the same mounted consumer context as successful reads.");
            File.WriteAllBytes(Path.Combine(source, prefix + relative), [0xff]);
            Assert(QuantityItemCatalog.LoadRaid(content, raid).Items.Single().ReferenceStatus == QuantityItemReferenceStatus.AnalysisIncomplete &&
                   QuantityItemCatalog.Load(content, estate).Items.Single().ReferenceStatus == QuantityItemReferenceStatus.SuspectedUnused,
                "An unreadable Curio must not contaminate the town reference analysis.");
        }
    }

    private static void VerifyActorRecordReferenceBoundaries(string runRoot)
    {
        foreach (var kind in new[] { "local", "workshop", "dlc-mod" })
        foreach (var hero in new[] { true, false })
        {
            var actor = hero ? "heroes/upgrades/upgrades.info.darkest" : "monsters/upgrades/upgrades_A/upgrades_A.info.darkest";
            var valid = hero ? "extra_battle_loot" : "loot";
            var records = new (string Name, string Text, bool Consumed)[]
            {
                ("notes-pair", "notes: .type estate .id query_token", false),
                ("notes-item", "notes: .item_id query_token", false),
                ("notes-use", "notes: .use_item_id query_token", false),
                ("unknown", "inventory_item: .type estate .id query_token", false),
                ("wrong-fields", "armour: .name coat .hp 20 .type estate .id query_token .item_id query_token", false),
                ("comment", $"// {valid}: .code query_loot", false),
                ("valid", $"{valid}: .code ignored .code query_loot", true),
                ("curio-or-foreign", "extra_curio_loot: .code query_loot", hero)
            };
            foreach (var (name, text, consumed) in records)
            {
                var root = Path.Combine(runRoot, "actor-record-references", kind, hero ? "hero" : "monster", name);
                var source = Path.Combine(root, "source");
                var prefix = kind == "dlc-mod" ? "dlc/rq_feature/" : "";
                WriteMultiMash(source, prefix + "inventory/query.inventory.items.darkest",
                    "inventory_item: .type estate .id query_token .base_stack_limit 2 .estate_can_be_provision false\n");
                WriteMultiMash(source, prefix + "inventory/query.inventory.system_configs.darkest", QueryCapacity(2));
                WriteMultiMash(source, prefix + "loot/query.loot.json", QueryLootJson);
                var actorPath = WriteMultiMash(source, prefix + actor, text + "\n");
                WriteFixtureManifest(source);
                var content = QueryContent(root, QuerySources(source, kind));
                var raid = JsonNode.Parse("""{"base_root":{"party":{"inventory":{"items":{}}}}}""")!.AsObject();
                var estate = JsonNode.Parse("""{"base_root":{"wallet":{},"estate_items":{"items":{}}}}""")!.AsObject();
                var item = QuantityItemCatalog.LoadRaid(content, raid).Items.Single();
                Assert(item.ReferenceStatus == (consumed ? QuantityItemReferenceStatus.ConfirmedActive : QuantityItemReferenceStatus.SuspectedUnused) &&
                       item.IsHiddenByDefault != consumed &&
                       QuantityItemCatalog.Load(content, estate).Items.Single().ReferenceStatus == QuantityItemReferenceStatus.SuspectedUnused,
                    $"{kind}/{hero}/{name}: only the native actor's loot records establish raid references.");
                if (name != "notes-item") continue;
                var fingerprint = ProfileCatalogContentFingerprint.Capture(content.Sources);
                File.WriteAllText(actorPath, $"{valid}: .code query_loot\n");
                Assert(ProfileCatalogContentFingerprint.Capture(content.Sources) != fingerprint &&
                       QuantityItemCatalog.LoadRaid(content, raid).Items.Single().ReferenceStatus == QuantityItemReferenceStatus.ConfirmedActive,
                    "Changing an ignored actor note into a consumed loot record must refresh item visibility.");
            }
        }
    }
}
