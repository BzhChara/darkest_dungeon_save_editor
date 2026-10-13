using System.Text.Json;
using ReferenceStatus = DarkestDungeonSaveEditor.Core.QuantityItemReferenceStatus;

internal static partial class ContractSuite
{
    private static async Task RunBuildingItemReferenceContractsAsync(string runRoot, DsonSaveCodec codec)
    {
        var root = Path.Combine(runRoot, "building-references");
        var definitions = Path.Combine(root, "definitions");
        WriteMultiMash(definitions, "inventory/building.inventory.items.darkest", """
            inventory_item: .type estate .id building_token .base_stack_limit 6 .estate_can_be_provision false
            inventory_item: .type estate .id other_token .base_stack_limit 6 .estate_can_be_provision false
            inventory_item: .type estate .id Az .base_stack_limit 6 .estate_can_be_provision false
            inventory_item: .type gold .id building_variant .base_stack_limit 6 .estate_can_be_provision false
            """);
        WriteMultiMash(definitions, "inventory/building.inventory.system_configs.darkest", QueryCapacity(16));
        WriteMultiMash(definitions, "loot/building.loot.json", """
            {"loot_tables":[{"id":"building_loot","entries":[{"type":"item","chances":1,"data":{"type":"estate","id":"other_token","amount":1}}]}]}
            """);
        WriteFixtureManifest(definitions);
        var definitionSource = new ActiveContentSource("local:building-defs", "Building definitions", "local", definitions, -4000);
        var town = JsonNode.Parse("""{"base_root":{"wallet":{},"estate_items":{"items":{}}}}""")!.AsObject();
        var raid = JsonNode.Parse("""{"base_root":{"party":{"inventory":{"items":{}}}}}""")!.AsObject();
        const string cost = """{"cost_currency":{"type":"building_token","amount":10}}""";
        string Activity(string id, string data) => "{\"data\":{\"activities\":[{\"id\":" + JsonSerializer.Serialize(id) + ",\"data\":" + data + "}]}}";
        string Costs(string id = "meditation", string key = "cost_upgrades") => Activity(id, "{\"" + key + "\":[" + cost + "]}");
        string Effect(string id, string type) => Activity(id, "{\"side_effects\":{\"chance\":1,\"results\":[{\"type\":" +
            JsonSerializer.Serialize(type) + ",\"chance\":1,\"data\":[{\"chance\":1,\"type\":\"building_token\",\"amount\":10}]}]}}");
        var checks = 0;
        var observations = new List<object>();
        QuantityItemCatalogResult Check(ActiveContentSnapshot content, string label, ReferenceStatus expected,
            string id = "building_token", bool warning = false)
        {
            var catalog = QuantityItemCatalog.Load(content, town);
            var item = catalog.Items.Single(i => i.DisplayId == id);
            var bag = QuantityItemCatalog.LoadRaid(content, raid).Items.Single(i => i.DisplayId == id);
            Assert(item.ReferenceStatus == expected && item.IsHiddenByDefault == (expected == ReferenceStatus.SuspectedUnused),
                $"{label}: expected {expected}, got {item.ReferenceStatus}: {string.Join("; ", item.ReferenceEvidence)}");
            Assert(bag.ReferenceStatus == ReferenceStatus.SuspectedUnused && bag.IsHiddenByDefault &&
                !item.IsPresentInSave && item.CurrentAmount == 0 && item.BaseStackLimit == 6,
                $"{label}: building references must not activate raid items or change inventory values.");
            Assert(catalog.Issues.Any(i => i.Contains("JSON=")) == warning,
                $"{label}: unexpected unverified-structure diagnostics: {string.Join("; ", catalog.Issues)}");
            checks += 2;
            observations.Add(new { label, id, Status = item.ReferenceStatus.ToString(), item.ReferenceEvidence, catalog.Issues });
            return catalog;
        }

        foreach (var kind in new[] { "base", "mode", "dlc-feature", "local", "workshop", "dlc-mod" })
        {
            var cases = new List<(string Name, string Building, string Text, ReferenceStatus Status, string Id)>();
            void Add(string name, string building, string text, ReferenceStatus status = ReferenceStatus.ConfirmedActive, string id = "building_token") =>
                cases.Add((name, building, text, status, id));
            foreach (var (building, ids) in new[] {
                ("abbey", new[] { "meditation", "prayer", "flagellation" }),
                ("tavern", new[] { "bar", "gambling", "brothel" }) })
            foreach (var id in ids)
            {
                Add(id + "-cost", building, Costs(id));
                foreach (var effect in new[] { "add_currency", "remove_currency" })
                    Add(id + "-" + effect, building, Effect(id, effect));
            }
            foreach (var id in new[] { "treatment", "disease_treatment" })
            {
                foreach (var key in new[] { "positive_quirk_cost_upgrades", "negative_quirk_cost_upgrades",
                    "permanent_negative_quirk_cost_upgrades", "disease_quirk_cost_upgrades" })
                    Add(id + "-" + key, "sanitarium", Costs(id, key));
                Add(id + "-side-effect", "sanitarium", Effect(id, "add_currency"));
            }
            Add("all-cost-levels", "abbey", Costs().Replace("[" + cost, "[{\"cost_currency\":{\"type\":\"other_token\",\"amount\":1}}," + cost));
            Add("currency-hash-alias", "abbey", Costs().Replace("building_token", "BE"), id: "Az");
            Add("currency-c-string", "abbey", Costs().Replace("building_token", "building_token\\u0000tail"));
            Add("activity-c-string", "abbey", Costs("meditation\0tail"));
            Add("effect-c-string", "tavern", Effect("bar", "add_currency\0tail"));
            Add("wallet-type", "abbey", Costs().Replace("building_token", "gold"), id: "building_variant");
            Add("wallet-not-variant", "abbey", Costs().Replace("building_token", "building_variant"), ReferenceStatus.SuspectedUnused, "building_variant");
            Add("currency-not-inventory-pair", "abbey", Costs().Replace("\"type\":\"building_token\"", "\"type\":\"estate\",\"id\":\"building_token\""), ReferenceStatus.SuspectedUnused);
            Add("first-root-null", "abbey", "{\"data\":null," + Costs()[1..], ReferenceStatus.SuspectedUnused);
            Add("first-activities-empty", "abbey", Costs().Replace("\"activities\":", "\"activities\":[],\"activities\":"), ReferenceStatus.SuspectedUnused);
            Add("first-cost-list-null", "abbey", Costs().Replace("\"cost_upgrades\":", "\"cost_upgrades\":null,\"cost_upgrades\":"), ReferenceStatus.SuspectedUnused);
            Add("first-currency-null", "abbey", Costs().Replace("\"cost_currency\":", "\"cost_currency\":null,\"cost_currency\":"), ReferenceStatus.SuspectedUnused);
            Add("first-currency-type", "abbey", Costs().Replace("\"type\":", "\"type\":\"other_token\",\"type\":"), ReferenceStatus.SuspectedUnused);
            Add("wrong-cost-shape", "abbey", Activity("meditation", "{\"cost_upgrades\":" + cost + "}"), ReferenceStatus.SuspectedUnused);
            Add("wrong-side-effect-shape", "tavern", Effect("bar", "add_currency").Replace("\"data\":[{", "\"data\":{\"ignored\":{").Replace("10}]", "10}}"), ReferenceStatus.SuspectedUnused);
            Add("first-effect-type-null", "tavern", Effect("bar", "add_currency").Replace("\"type\":\"add_currency\"", "\"type\":null,\"type\":\"add_currency\""), ReferenceStatus.AnalysisIncomplete);
            Add("wrong-activity", "abbey", Costs("bar"), ReferenceStatus.AnalysisIncomplete);
            Add("wrong-activity-case", "abbey", Costs("MEDITATION"), ReferenceStatus.AnalysisIncomplete);
            Add("first-activity-id-null", "abbey", Costs().Replace("\"id\":", "\"id\":null,\"id\":"), ReferenceStatus.AnalysisIncomplete);
            Add("wrong-cost-list", "abbey", Costs("meditation", "disease_quirk_cost_upgrades"), ReferenceStatus.AnalysisIncomplete);
            Add("unknown-extension", "abbey", Activity("meditation", """{"custom_cost":{"type":"building_token"}}"""), ReferenceStatus.AnalysisIncomplete);
            Add("unknown-key-only", "abbey", Activity("meditation", """{"building_\u0074oken":null}"""), ReferenceStatus.AnalysisIncomplete);
            Add("first-unknown-member", "abbey", Activity("meditation", """{"custom_cost":null,"custom_cost":"building_token"}"""), ReferenceStatus.SuspectedUnused);
            foreach (var building in new[] { "blacksmith", "guild", "camping_trainer", "nomad_wagon", "stage_coach", "graveyard", "statue" })
                Add(building + "-no-universal-cost-walker", building, Costs(), ReferenceStatus.AnalysisIncomplete);

            foreach (var test in cases)
            {
                var scenario = Path.Combine(root, kind, test.Name);
                var source = Path.Combine(scenario, "resources");
                var prefix = kind == "dlc-mod" ? "dlc/rq_feature/" : "";
                WriteMultiMash(source, $"{prefix}campaign/town/buildings/{test.Building}/test.{test.Building}.building.json", test.Text);
                WriteFixtureManifest(source);
                var content = QueryContent(scenario, QuerySources(source, kind).Append(definitionSource).ToArray());
                var catalog = Check(content, kind + "/" + test.Name, test.Status, test.Id, test.Status == ReferenceStatus.AnalysisIncomplete);
                if (test.Status == ReferenceStatus.ConfirmedActive)
                    Assert(catalog.Items.Single(i => i.DisplayId == test.Id).ReferenceEvidence.Any(e => e.Contains("$.data.activities[0].data")),
                        "Confirmed building evidence must identify the actual activity currency field.");
            }
        }

        foreach (var kind in new[] { "local", "workshop" })
        {
            var scenario = Path.Combine(root, "overlay-" + kind);
            var low = Path.Combine(scenario, "low");
            var high = Path.Combine(scenario, "high");
            const string buildingPath = "campaign/town/buildings/abbey/abbey.building.json";
            WriteMultiMash(low, buildingPath, Costs()); WriteFixtureManifest(low);
            var highPath = WriteMultiMash(high, buildingPath, Costs().Replace("building_token", "other_token")); WriteFixtureManifest(high);
            var sources = new[] { definitionSource, new ActiveContentSource("low", "Low", kind, low, 1001), new ActiveContentSource("high", "High", kind, high, 1000) };
            var fixture = await BuildInventoryProviderProfileAsync(scenario, sources, false, codec);
            Check(fixture.Content, kind + "/shadowed", ReferenceStatus.SuspectedUnused);
            Check(fixture.Content, kind + "/winning", ReferenceStatus.ConfirmedActive, "other_token");
            var fingerprint = ProfileCatalogContentFingerprint.Capture(sources);
            File.WriteAllText(highPath, Costs());
            Assert(ProfileCatalogContentFingerprint.Capture(sources) != fingerprint, "Building bytes must invalidate catalog snapshots without a manifest change.");
            var active = Check(fixture.Content, kind + "/refreshed", ReferenceStatus.ConfirmedActive).Items.Single(i => i.ItemId == "building_token");
            await fixture.Service.CommitAsync(await fixture.Service.PrepareQuantityItemEditAsync(fixture.Content.Profile, active, 7, fixture.Content));
            var decoded = Path.Combine(scenario, "committed.json");
            await codec.DecodeAsync(fixture.Content.Profile.EstateSavePath, decoded);
            var saved = JsonSupport.ReadObject(decoded);
            var inventory = saved["base_root"]!["estate_items"]!["items"]!.AsObject();
            Assert(inventory.Sum(p => p.Value!["id"]!.GetValue<string>() == "building_token" ? p.Value["amount"]!.GetValue<int>() : 0) == 7 &&
                inventory.Count(p => p.Value!["id"]!.GetValue<string>() == "building_token") == 1 &&
                JsonNode.DeepEquals(saved["base_root"]!["wallet"], fixture.Estate["base_root"]!["wallet"]),
                "A newly confirmed building currency must save under its canonical identity as a town balance and preserve wallet data.");
            var refreshed = QuantityItemCatalog.RefreshSavedAmounts(fixture.Content, QuantityItemCatalog.Load(fixture.Content, town), saved, "building-refresh");
            Assert(refreshed.Items.Single(i => i.ItemId == "building_token").CurrentAmount == 7, "Saved-amount refresh must retain building inventory counts.");
            WriteMultiMash(high, "modfiles.txt", "");
            Check(fixture.Content, kind + "/unlisted-uses-lower", ReferenceStatus.ConfirmedActive);
        }

        var diagnosticRoot = Path.Combine(root, "diagnostics");
        const string diagnosticPath = "campaign/town/buildings/abbey/abbey.building.json";
        WriteMultiMash(diagnosticRoot, diagnosticPath, Activity("meditation", """{"custom_cost":{"item":"building_\u0074oken","loot":"building_loot"}}"""));
        WriteFixtureManifest(diagnosticRoot);
        var diagnostics = QueryContent(root, [definitionSource, new("local:diag", "Diagnostics", "local", diagnosticRoot, 0)]);
        foreach (var language in new[] { "zh-CN", "en" })
        {
            try
            {
                EditorText.Initialize(language, System.Globalization.CultureInfo.CurrentUICulture);
                var catalog = Check(diagnostics, language + "/diagnostics", ReferenceStatus.AnalysisIncomplete, warning: true);
                var warning = catalog.Issues.Single(i => i.Contains("JSON="));
                Assert(warning.Contains(Path.Combine(diagnosticRoot, diagnosticPath).Replace('/', Path.DirectorySeparatorChar)) &&
                    warning.Contains("$.data.activities[0].data[\"custom_cost\"]") && warning.Contains("building_token") && warning.Contains("building_loot") &&
                    warning.Contains(language == "zh-CN" ? "分析不完整" : "incomplete analysis"),
                    "Unverified warnings must name the effective file, JSON location, decoded item IDs, loot IDs and consequence in both languages.");
                Assert(catalog.Items.Single(i => i.ItemId == "other_token").ReferenceStatus == ReferenceStatus.AnalysisIncomplete &&
                    catalog.Items.Single(i => i.ItemId == "Az").ReferenceStatus == ReferenceStatus.SuspectedUnused,
                    "Uncertain loot must reach only its own items, without contaminating unrelated inventory.");
            }
            finally { EditorText.Initialize("zh-CN", System.Globalization.CultureInfo.CurrentUICulture); }
        }
        WriteMultiMash(root, "results.json", JsonSerializer.Serialize(observations, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"PASS: {checks} building town/raid checks across six source kinds, costs/side effects, identity/JSON boundaries, overlays, bilingual diagnostics and 2 DSON commits.");
    }
}
