internal static partial class ContractSuite
{
    private static async Task RunQuantityReferenceIdentityContractsAsync(string runRoot, DsonSaveCodec codec)
    {
        foreach (var kind in new[] { "local", "workshop" })
        {
            VerifyQuantityReferenceProviders(Path.Combine(runRoot, "reference-providers", kind), kind);
            foreach (var type in new[] { "gold", "shard" })
                await VerifyWalletReferenceIdentitiesAsync(Path.Combine(runRoot, "wallet-references", kind, type), kind, type, codec);
        }
    }

    private static async Task VerifyWalletReferenceIdentitiesAsync(string root, string kind, string type, DsonSaveCodec codec)
    {
        var source = Path.Combine(root, "resources");
        var town = JsonNode.Parse("""{"base_root":{"wallet":{},"estate_items":{"items":{}}}}""")!.AsObject();
        var raid = JsonNode.Parse("""{"base_root":{"party":{"inventory":{"items":{}}}}}""")!.AsObject();
        const string inventoryPath = "inventory/reference.inventory.items.darkest";
        const string eventPath = "campaign/town_events/reference.town_events.events.json";
        const string questPath = "campaign/quest/reference.quest.plot_quests.json";
        var sources = new[] { new ActiveContentSource("mod", "Wallet references", kind, source, 1000) };
        var fixture = await BuildInventoryProviderProfileAsync(root, sources, false, codec);
        var observations = new List<object>();
        string Declaration(string inventoryType, string id, bool provision = false) =>
            $"inventory_item: .type {inventoryType} .id \"{id}\" .base_stack_limit 12 .estate_can_be_provision {provision.ToString().ToLowerInvariant()}\n";
        string Currency(string id, string eventType = "bonus_currency") =>
            $$$"""{"events":[{"id":"reference_event","data":[{"type":"{{{eventType}}}","string_data":"{{{id}}}"}]}]}""";
        string Reward(string inventoryType, string id) =>
            """{"plot_quests":[{"quest":{"completion_reward":{"items_definition":{"items":{"0":{"type":"TYPE","id":"ID","amount":1}}}}}}]}"""
                .Replace("TYPE", inventoryType).Replace("ID", id);
        var cases = new (string Name, string Definitions, string Path, string Text, bool Active)[]
        {
            ("blank-currency", Declaration(type, ""), eventPath, Currency(type), true),
            ("variant-currency", Declaration(type, "variant"), eventPath, Currency(type), true),
            ("wrong-currency", Declaration(type, "variant"), eventPath, Currency("variant"), false),
            ("variant-item", Declaration(type, "variant"), questPath, Reward(type, "variant"), true),
            ("blank-item", Declaration(type, ""), questPath, Reward(type, ""), true),
            ("undeclared-blank-item", Declaration(type, "variant"), questPath, Reward(type, ""), false),
            ("wrong-case-item", Declaration(type, "variant"), questPath, Reward(type, "VARIANT"), false),
            ("merged-item", Declaration(type, "") + Declaration(type, "variant"), questPath, Reward(type, "variant"), true),
            ("merged-item-reversed", Declaration(type, "variant") + Declaration(type, ""), questPath, Reward(type, ""), true),
            ("heirloom-alias", Declaration(type, "") + Declaration("heirloom", type), questPath, Reward("heirloom", type), true),
            ("nonwinning-provision", Declaration(type, "variant") + Declaration(type, "variant", true), eventPath, Currency("other"), false),
            ("other-variant-provision", Declaration(type, "") + Declaration(type, "variant", true), eventPath, Currency("other"), true),
            ("estate-currency", Declaration(type, "variant"), "campaign/estate/reference.estate.json", $$$"""{"currencies":[{"id":"{{{type}}}"}]}""", true),
            ("upgrade-currency", Declaration(type, "variant"), "upgrades/reference.upgrades.json", $$$"""{"trees":[{"id":"reference","requirements":[{"code":"0","currency_cost":[{"type":"{{{type}}}","amount":1}]}]}]}""", true),
            ("district-currency", Declaration(type, "variant"), "campaign/town/districts/reference.districts.json", $$$"""{"buildings":[{"currency_cost":[{"type":"{{{type}}}","amount":1}]}]}""", true),
            ("quest-currency", Declaration(type, "variant"), "campaign/quest/reference.quest.generation.json", """{"generation":{"rewards":{"heirloom_amount_table":[{"type":"TYPE"}]}}}""".Replace("TYPE", type), true),
            ("event-cost", Declaration(type, "variant"), eventPath, Currency(type, "event_cost"), true),
            ("first-json-member", Declaration(type, "variant"), eventPath, Currency(type).Replace("\"string_data\":", "\"string_data\":\"other\",\"string_data\":"), false),
            ("c-string-currency", Declaration(type, "variant"), eventPath, Currency(type + "\\u0000ignored"), true)
        };
        foreach (var test in cases)
        {
            WriteMultiMash(source, inventoryPath, test.Definitions);
            WriteMultiMash(source, test.Path, test.Text);
            WriteMultiMash(source, "inventory/reference.inventory.system_configs.darkest", QueryCapacity(16));
            WriteMultiMash(source, "modfiles.txt", inventoryPath + "\n" + test.Path + "\ninventory/reference.inventory.system_configs.darkest\n");
            var catalog = QuantityItemCatalog.Load(fixture.Content, town);
            var wallet = catalog.Items.Single();
            Assert(wallet.ReferenceStatus == (test.Active ? QuantityItemReferenceStatus.ConfirmedActive : QuantityItemReferenceStatus.SuspectedUnused) &&
                wallet.IsHiddenByDefault == !test.Active && wallet.PersistedType == type && wallet.PersistedId == "" &&
                !wallet.HasProviderConflict && wallet.SaveIdentityIssue.Length == 0,
                $"{kind}/{type}/{test.Name}: incorrect wallet reference identity or persistence target.");
            var saved = QuantityItemSaveEditor.SetAmount(town, wallet, 7).UpdatedRoot;
            var refreshed = QuantityItemCatalog.RefreshSavedAmounts(fixture.Content, catalog, saved, "saved-only-refresh").Items.Single();
            Assert(refreshed.CurrentAmount == 7 && refreshed.ReferenceStatus == wallet.ReferenceStatus && !refreshed.IsHiddenByDefault,
                "Saved-amount refresh must preserve reference classification while showing existing balances.");
            if (test.Name == "merged-item")
            {
                var bag = QuantityItemCatalog.LoadRaid(fixture.Content, raid);
                Assert(bag.Items.Count == 2 && bag.Items.All(i => i.ReferenceStatus == QuantityItemReferenceStatus.SuspectedUnused),
                    "Town reward references must not collapse or activate raid item variants.");
                await fixture.Service.CommitAsync(await fixture.Service.PrepareQuantityItemEditAsync(fixture.Content.Profile, wallet, 29, fixture.Content));
                var decoded = Path.Combine(root, "committed.json");
                await codec.DecodeAsync(fixture.Content.Profile.EstateSavePath, decoded);
                var balances = JsonSupport.RequireObject(JsonSupport.ReadObject(decoded), "base_root", "wallet");
                Assert(balances.Count == 2 && balances.Single(p => p.Value!["type"]!.GetValue<string>() == type).Value!["amount"]!.GetValue<int>() == 29 &&
                    balances.All(p => p.Value!["type"]!.GetValue<string>() is "gold" or "shard"),
                    "Actual DSON saves must retain the wallet key and other balances.");
            }
            observations.Add(new { test.Name, status = wallet.ReferenceStatus.ToString(), wallet.PersistedType, wallet.IsHiddenByDefault, wallet.ReferenceEvidence });
        }

        // Provenance belongs to the aggregate balance even if the last raw
        // identity comes only from a Mod and has no reference of its own.
        var official = Path.Combine(root, "base");
        WriteMultiMash(official, inventoryPath, Declaration(type, ""));
        WriteMultiMash(source, inventoryPath, Declaration(type, "variant"));
        WriteMultiMash(source, "modfiles.txt", inventoryPath + "\n");
        var officialContent = fixture.Content with { Sources = new[] { new ActiveContentSource("base", "Base", "base", official, 0) }.Concat(sources).ToArray() };
        // Distinct paths keep both raw definitions in the effective sequence.
        WriteMultiMash(official, "inventory/official.inventory.items.darkest", Declaration(type, ""));
        var officialWallet = QuantityItemCatalog.Load(officialContent, town).Items.Single();
        Assert(officialWallet.ReferenceStatus == QuantityItemReferenceStatus.OfficialContent && !officialWallet.IsHiddenByDefault,
            "A later Mod variant must not erase an aggregate wallet's official provenance.");
        WriteMultiMash(root, "results.json", System.Text.Json.JsonSerializer.Serialize(observations));
        Console.WriteLine($"PASS: {kind}/{type} wallet reference identities across {cases.Length} cases, raw aliases, cache refresh, official provenance and DSON commit.");
    }
}
