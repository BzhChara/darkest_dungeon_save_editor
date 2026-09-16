using System.Text.Json;

internal static partial class ContractSuite
{
    private sealed record LootIdentityCase(string Name, string DefinitionType, string DefinitionId,
        string EntryType, string ItemType, string ItemId, string? NestedType = null, string ChildCode = "child",
        string Chance = "1", string? FirstEntryType = null, string? FirstItemId = null,
        QuantityItemReferenceStatus Status = QuantityItemReferenceStatus.ConfirmedActive);

    private static async Task RunLootItemIdentityContractsAsync(string runRoot, DsonSaveCodec codec)
    {
        const QuantityItemReferenceStatus unused = QuantityItemReferenceStatus.SuspectedUnused;
        const QuantityItemReferenceStatus uncertain = QuantityItemReferenceStatus.AnalysisIncomplete;
        var cases = new LootIdentityCase[]
        {
            new("plain", "estate", "loot_a", "item", "estate", "loot_a"),
            new("entry-nul", "estate", "loot_a", "item\0tail", "estate", "loot_a"),
            new("entry-hash", "estate", "loot_a", "j?em", "estate", "loot_a"),
            new("type-nul", "estate", "loot_a", "item", "estate\0tail", "loot_a"),
            new("id-nul", "estate", "loot_a", "item", "estate", "loot_a\0tail"),
            new("id-hash", "estate", "Az", "item", "estate", "BE"),
            new("type-hash", "Az", "loot_a", "item", "BE", "loot_a"),
            new("id-buffer", "estate", new string('a', 63), "item", "estate", new string('a', 63) + "tail"),
            new("type-buffer", new string('a', 63), "loot_a", "item", new string('a', 63) + "tail", "loot_a"),
            new("utf8-id", "estate", new string('界', 21), "item", "estate", new string('界', 21) + "tail"),
            // At byte 63 the raw truncated suffix is [65, 231], which hashes
            // like [68, 72] ("DH"). Decoding/re-encoding the partial UTF-8 fails.
            new("split-utf8-id", "estate", new string('a', 61) + "DH", "item", "estate", new string('a', 61) + "A界"),
            new("split-utf8-type", new string('a', 61) + "DH", "loot_a", "item", new string('a', 61) + "A界", "loot_a"),
            new("nested-entry-nul", "estate", "loot_a", "item", "estate", "loot_a", "table\0tail"),
            new("nested-entry-hash", "estate", "loot_a", "item", "estate", "loot_a", "u,ble"),
            new("nested-code-nul", "estate", "loot_a", "item", "estate", "loot_a", "table", "child\0tail"),
            new("wrong-case", "estate", "loot_a", "item", "estate", "LOOT_A", Status: unused),
            new("whitespace", "estate", "loot_a", "item", "estate", " loot_a ", Status: unused),
            new("wrong-entry-case", "estate", "loot_a", "Item", "estate", "loot_a", Status: unused),
            new("zero-weight-alias", "estate", "Az", "item", "estate", "BE", Chance: "0", Status: unused),
            new("uncertain-weight-alias", "estate", "Az", "item", "estate", "BE", Chance: "null", Status: uncertain),
            new("first-entry-member", "estate", "loot_a", "item", "estate", "loot_a", FirstEntryType: "ignored", Status: unused),
            new("first-entry-alias", "estate", "loot_a", "ignored", "estate", "loot_a", FirstEntryType: "item\0tail"),
            new("first-id-member", "estate", "Az", "item", "estate", "BE", FirstItemId: "unrelated", Status: unused),
            new("first-id-alias", "estate", "Az", "item", "estate", "unrelated", FirstItemId: "BE"),
            new("blank-id", "gold", "", "item", "gold", "\0tail"),
            new("no-blank-substitution", "gold", "variant", "item", "gold", "\0tail", Status: unused)
        };

        foreach (var kind in new[] { "local", "workshop", "dlc-mod" })
        {
            var root = Path.Combine(runRoot, "loot-item-identities", kind);
            var resources = Path.Combine(root, "resources");
            var prefix = kind == "dlc-mod" ? "dlc/rq_feature/" : "";
            string Write(string path, string text) => WriteMultiMash(resources, prefix + path, text);
            string Declaration(string type, string id, int limit = 2) =>
                $"inventory_item: .type \"{type}\" .id \"{id}\" .base_stack_limit {limit} .estate_can_be_provision false\n";
            var inventory = Write("inventory/a.inventory.items.darkest", "");
            var loot = Write("loot/a.loot.json", "{}");
            Write("inventory/a.inventory.system_configs.darkest", QueryCapacity(8));
            Write("curios/a_curio_type_library.csv", QueryTypeCsv.Replace("query_loot", "audit_root"));
            Write("curios/a_curio_props.csv", QueryPropCsv);
            Write("campaign/provision/a.provision.json", "{}");
            WriteFixtureManifest(resources);
            var sources = QuerySources(resources, kind);
            var content = QueryContent(root, sources);
            var raid = JsonNode.Parse("""{"base_root":{"party":{"inventory":{"items":{}}}}}""")!.AsObject();
            var town = JsonNode.Parse("""{"base_root":{"wallet":{},"estate_items":{"items":{}}}}""")!.AsObject();
            var observations = new List<object>();
            foreach (var test in cases)
            {
                File.WriteAllText(inventory, Declaration(test.DefinitionType, test.DefinitionId));
                string String(string value) => JsonSerializer.Serialize(value);
                var firstEntry = test.FirstEntryType is null ? "" : "\"type\":" + String(test.FirstEntryType) + ",";
                var firstId = test.FirstItemId is null ? "" : "\"id\":" + String(test.FirstItemId) + ",";
                var entry = "{" + firstEntry + "\"type\":" + String(test.EntryType) + ",\"chances\":" + test.Chance +
                    ",\"data\":{" + firstId + "\"type\":" + String(test.ItemType) + ",\"id\":" + String(test.ItemId) + ",\"amount\":1}}";
                var tables = test.NestedType is null ? LootFile(LootTable("audit_root", entry)) :
                    LootFile(LootTable("audit_root", "{\"type\":" + String(test.NestedType) +
                        ",\"chances\":1,\"data\":{\"table\":" + String(test.ChildCode) + "}}"), LootTable("child", entry));
                File.WriteAllText(loot, tables);
                var catalog = QuantityItemCatalog.LoadRaid(content, raid);
                Assert(catalog.Items.Count == 1 && catalog.RaidStorage?.MaxSlots == 8 && catalog.DefinitionReadFailures.Count == 0,
                    $"{kind}/{test.Name}: fixture is incomplete.");
                var item = catalog.Items.Single();
                Assert(item.ReferenceStatus == test.Status && item.IsHiddenByDefault == (test.Status == unused),
                    $"{kind}/{test.Name}: expected {test.Status}, got {item.ReferenceStatus}.");
                if (test.Name == "uncertain-weight-alias")
                    Assert(item.ReferenceEvidence.Any(e => e.Contains("掉落权重无法确认", StringComparison.Ordinal)) &&
                        item.ReferenceEvidence.All(e => !e.Contains("物品定义冲突", StringComparison.Ordinal)),
                        "Uncertain weight evidence must not claim a definition conflict.");
                Assert(item.InventoryType == test.DefinitionType && item.ItemId == test.DefinitionId &&
                    !item.HasProviderConflict && item.SaveIdentityIssue.Length == 0,
                    $"{kind}/{test.Name}: references must not rewrite or invalidate the selected definition identity.");
                var before = raid.ToJsonString();
                var changed = RaidInventorySaveEditor.SetAmount(raid, item, 5, 8).UpdatedRoot;
                var slots = JsonSupport.RequireObject(changed, "base_root", "party", "inventory", "items");
                Assert(raid.ToJsonString() == before && slots.Count == 3 &&
                    slots.Select(p => p.Value!["amount"]!.GetValue<int>()).SequenceEqual([2, 2, 1]) &&
                    slots.All(p => p.Value!["type"]!.GetValue<string>() == test.DefinitionType && p.Value!["id"]!.GetValue<string>() == test.DefinitionId),
                    $"{kind}/{test.Name}: quantity edits must preserve identity, source input and stacking.");
                var refreshed = QuantityItemCatalog.RefreshSavedAmounts(content, catalog, changed, "synthetic").Items.Single();
                Assert(refreshed.CurrentAmount == 5 && !refreshed.IsHiddenByDefault && refreshed.ReferenceStatus == test.Status,
                    "Saved-amount refresh must retain the corrected classification and show existing items.");
                Assert(QuantityItemCatalog.Load(content, town).Items.All(i => i.ReferenceStatus == unused),
                    "Curio loot identity matching must not turn raid roots into town references.");
                observations.Add(new { test.Name, status = item.ReferenceStatus.ToString(), item.ItemId, item.InventoryType,
                    item.IsHiddenByDefault, refreshed.CurrentAmount });
            }

            // Different declared identities with equal native hashes remain
            // ambiguous and read-only; a reference cannot choose the winner.
            File.WriteAllText(inventory, Declaration("estate", "Az") + Declaration("estate", "BE"));
            File.WriteAllText(loot, LootFile(LootTable("audit_root", LootItem("BE"))));
            var service = new SaveEditService(codec, new(root, Path.Combine(root, "workspace"), Path.Combine(root, "backups")));
            var conflicted = QuantityItemCatalog.LoadRaid(content, raid).Items;
            Assert(conflicted.Count == 2 && conflicted.All(i => i.HasProviderConflict && i.ReferenceStatus == uncertain),
                "Hash-colliding definitions must stay read-only and have uncertain loot evidence.");
            Assert(conflicted.All(i => i.ReferenceEvidence.Any(e => e.Contains("物品定义冲突", StringComparison.Ordinal)) &&
                i.ReferenceEvidence.All(e => !e.Contains("掉落权重无法确认", StringComparison.Ordinal))),
                "Definition conflicts with positive weights must report the actual uncertainty cause.");
            foreach (var item in conflicted)
            {
                var refused = false;
                try { await service.PrepareQuantityItemEditAsync(content.Profile, item, 5, content); }
                catch (InvalidOperationException error) when (error.Message.Contains("read-only", StringComparison.Ordinal)) { refused = true; }
                Assert(refused, "Loot hash resolution must not bypass the definition-conflict write guard.");
            }
            File.WriteAllText(loot, LootFile(LootTable("audit_root", LootItem("BE", "null"))));
            Assert(QuantityItemCatalog.LoadRaid(content, raid).Items.All(i => i.ReferenceStatus == uncertain &&
                i.ReferenceEvidence.Any(e => e.Contains("物品定义冲突", StringComparison.Ordinal)) &&
                i.ReferenceEvidence.Any(e => e.Contains("掉落权重无法确认", StringComparison.Ordinal))),
                "Combined definition and weight uncertainty must retain both reasons.");

            // Repeated identical raw IDs retain first-match values. A changed
            // reference with the same bytes/timestamp invalidates the content
            // fingerprint without changing its manifest or saved item identity.
            File.WriteAllText(inventory, Declaration("estate", "Az") + Declaration("estate", "Az", 9));
            File.WriteAllText(loot, LootFile(LootTable("audit_root", LootItem("ZZ"))));
            var stamp = File.GetLastWriteTimeUtc(loot);
            var manifestHash = ComputeSha256(Path.Combine(resources, "modfiles.txt"));
            var fingerprint = ProfileCatalogContentFingerprint.Capture(sources);
            Assert(QuantityItemCatalog.LoadRaid(content, raid).Items.Single().IsHiddenByDefault, "Unrelated reference must stay hidden.");
            File.WriteAllText(loot, LootFile(LootTable("audit_root", LootItem("BE"))));
            File.SetLastWriteTimeUtc(loot, stamp);
            var active = QuantityItemCatalog.LoadRaid(content, raid).Items.Single();
            Assert(ProfileCatalogContentFingerprint.Capture(sources) != fingerprint && active.BaseStackLimit == 2 &&
                active.ReferenceStatus == QuantityItemReferenceStatus.ConfirmedActive && !active.HasProviderConflict &&
                ComputeSha256(Path.Combine(resources, "modfiles.txt")) == manifestHash,
                "Content-only alias changes must refresh references and keep first-match values.");

            var profile = content.Profile;
            var gameInput = WriteMultiMash(root, "seed/game.json", """{"base_root":{"inraid":true,"raiddungeon":"weald","raid_save":""}}""");
            var raidInput = WriteMultiMash(root, "seed/raid.json", raid.ToJsonString());
            await codec.EncodeAsync(gameInput, Path.Combine(profile.ProfileDirectory, "persist.game.json"), null);
            await codec.EncodeAsync(raidInput, profile.RaidSavePath, null);
            WriteMultiMash(profile.ProfileDirectory, "persist.estate.json", town.ToJsonString());
            content = content with { DecodedGamePath = gameInput, SourceGameSha256 = ComputeSha256(Path.Combine(profile.ProfileDirectory, "persist.game.json")) };
            await service.CommitAsync(await service.PrepareQuantityItemEditAsync(profile, active, 5, content));
            var decoded = Path.Combine(root, "committed.json");
            await codec.DecodeAsync(profile.RaidSavePath, decoded);
            var committed = JsonSupport.ReadObject(decoded);
            var committedSlots = JsonSupport.RequireObject(committed, "base_root", "party", "inventory", "items");
            Assert(committedSlots.Count == 3 && committedSlots.All(p => p.Value!["id"]!.GetValue<string>() == "Az") &&
                committedSlots.Select(p => p.Value!["amount"]!.GetValue<int>()).SequenceEqual([2, 2, 1]),
                "Actual DSON commits must write the chosen definition, never the loot alias.");
            Assert(QuantityItemCatalog.LoadRaid(content, committed).Items.Single().ReferenceStatus == QuantityItemReferenceStatus.ConfirmedActive,
                "A full read after commit must retain the corrected reference status.");
            WriteMultiMash(root, "results.json", JsonSerializer.Serialize(observations));
            Console.WriteLine($"PASS: {kind} loot item identities: {cases.Length} native identity cases, conflict guards, first-match values, refresh and DSON commit.");
        }
    }
}
