internal static partial class ContractSuite
{
    private static void VerifyQuantityReferenceProviders(string root, string kind)
    {
        var observations = new List<object>();
        var town = JsonNode.Parse("""{"base_root":{"wallet":{},"estate_items":{"items":{}}}}""")!.AsObject();
        var raid = JsonNode.Parse("""{"base_root":{"party":{"inventory":{"items":{}}}}}""")!.AsObject();
        foreach (var consumer in new[] { "upgrade", "event", "provision", "loot", "district", "actor" })
        foreach (var mode in new[] { "readable", "low-missing", "high-missing", "low-unlisted", "high-unlisted" })
        {
            var scenario = Path.Combine(root, consumer, mode);
            var definitions = Path.Combine(scenario, "definitions");
            var low = Path.Combine(scenario, "low");
            var high = Path.Combine(scenario, "high");
            const string itemPath = "inventory/reference.inventory.items.darkest";
            WriteMultiMash(definitions, itemPath, string.Join('\n', new[] { "low_token", "high_token", "unused_token" }
                .Select(id => $"inventory_item: .type heirloom .id {id} .base_stack_limit 12")));
            WriteMultiMash(definitions, "inventory/reference.inventory.system_configs.darkest", QueryCapacity(16));
            string Loot(string id) => $$$"""{"loot_tables":[{"id":"reference_loot","entries":[{"type":"item","chances":1,"data":{"type":"heirloom","id":"{{{id}}}","amount":1}}]}]}""";
            if (consumer == "loot")
                WriteMultiMash(definitions, "monsters/reference/reference_A/reference_A.info.darkest", "loot: .code reference_loot\n");
            if (consumer == "actor")
                WriteMultiMash(definitions, "loot/reference.loot.json", $$$"""{"loot_tables":[{"id":"low_token","entries":[{"type":"item","chances":1,"data":{"type":"heirloom","id":"low_token"}}]},{"id":"high_token","entries":[{"type":"item","chances":1,"data":{"type":"heirloom","id":"high_token"}}]}]}""");
            WriteFixtureManifest(definitions);
            var path = consumer switch
            {
                "upgrade" => "upgrades/building/reference.upgrades.json",
                "event" => "campaign/town_events/reference.town_events.events.json",
                "provision" => "campaign/provision/reference.provision.json",
                "loot" => "loot/reference.loot.json",
                "district" => "campaign/town/districts/reference.districts.json",
                _ => "monsters/reference/reference_A/reference_A.info.darkest"
            };
            string Text(string id) => consumer switch
            {
                "upgrade" => $$$"""{"trees":[{"id":"reference_tree","requirements":[{"code":"0","currency_cost":[{"type":"{{{id}}}","amount":1}]}]}]}""",
                "event" => $$$"""{"events":[{"id":"reference_event","data":[{"type":"bonus_currency","string_data":"{{{id}}}"}]}]}""",
                "provision" => $$$"""{"raid_starting_length_inventory_item_lists":[[{"type":"heirloom","id":"{{{id}}}"}]]}""",
                "loot" => Loot(id),
                "district" => $$$"""{"buildings":[{"currency_cost":[{"type":"{{{id}}}","amount":1}]}]}""",
                _ => $"loot: .code {id}\n"
            };
            foreach (var (source, upper) in new[] { (low, false), (high, true) })
            {
                if (mode != (upper ? "high-missing" : "low-missing"))
                    WriteMultiMash(source, path, Text(upper ? "high_token" : "low_token"));
                WriteMultiMash(source, "modfiles.txt", mode == (upper ? "high-unlisted" : "low-unlisted") ? "" : path + "\n");
            }
            var sources = new ActiveContentSource[] { new("defs", "Definitions", kind, definitions, 900),
                new("low", "Low", kind, low, 1001), new("high", "High", kind, high, 1000) };
            var content = QueryContent(scenario, sources);
            var isTown = consumer is "upgrade" or "event" or "district";
            QuantityItemCatalogResult Load() => isTown ? QuantityItemCatalog.Load(content, town) : QuantityItemCatalog.LoadRaid(content, raid);
            var catalog = Load();
            var additive = consumer == "district";
            var incomplete = mode == "high-missing" || additive && mode == "low-missing";
            bool Active(string id) => additive
                ? id == "high_token" && mode is not ("high-missing" or "high-unlisted") ||
                  id == "low_token" && mode is not ("low-missing" or "low-unlisted")
                : mode != "high-missing" && id == (mode == "high-unlisted" ? "low_token" : "high_token");
            foreach (var item in catalog.Items)
            {
                var expected = Active(item.DisplayId) ? QuantityItemReferenceStatus.ConfirmedActive : incomplete
                    ? QuantityItemReferenceStatus.AnalysisIncomplete : QuantityItemReferenceStatus.SuspectedUnused;
                Assert(item.ReferenceStatus == expected && item.IsHiddenByDefault == (expected == QuantityItemReferenceStatus.SuspectedUnused),
                    $"{kind}/{consumer}/{mode}/{item.DisplayId}: expected {expected}, got {item.ReferenceStatus}.");
                Assert(!item.HasProviderConflict && item.CurrentAmount == 0 && item.BaseStackLimit == 12,
                    "Reference failures must not change readable inventory definitions or their quantity guards.");
            }
            Assert(catalog.DefinitionReadFailures.Count == 0, "Reference-only missing files are not inventory-definition failures.");
            if (consumer == "upgrade")
                Assert(QuantityItemCatalog.LoadRaid(content, raid).Items.All(i => i.ReferenceStatus == QuantityItemReferenceStatus.SuspectedUnused),
                    "Town upgrade failures must not contaminate raid reference analysis.");
            if (mode is "low-missing" or "high-missing")
            {
                var fingerprint = ProfileCatalogContentFingerprint.Capture(sources);
                Assert(fingerprint == ProfileCatalogContentFingerprint.Capture(sources), "A stable missing state must have a stable refresh signal.");
                var restoredSource = mode == "low-missing" ? low : high;
                var manifestHash = ComputeSha256(Path.Combine(restoredSource, "modfiles.txt"));
                WriteMultiMash(restoredSource, path, Text(mode == "low-missing" ? "low_token" : "high_token"));
                Assert(ComputeSha256(Path.Combine(restoredSource, "modfiles.txt")) == manifestHash &&
                    fingerprint != ProfileCatalogContentFingerprint.Capture(sources) &&
                    Load().Items.Single(i => i.DisplayId == "high_token").ReferenceStatus == QuantityItemReferenceStatus.ConfirmedActive &&
                    Load().Items.Single(i => i.DisplayId == "unused_token").IsHiddenByDefault,
                    "Restoring bytes alone must restore complete reference classification and be detected by shared sync.");
            }
            observations.Add(new { consumer, mode, items = catalog.Items.Select(i => new { i.DisplayId, status = i.ReferenceStatus.ToString(), i.IsHiddenByDefault }), catalog.Issues });
        }
        WriteMultiMash(root, "results.json", System.Text.Json.JsonSerializer.Serialize(observations));
        Console.WriteLine($"PASS: {kind} quantity references across 30 provider scenarios, additive consumers, context boundaries and byte restoration.");
    }
}
