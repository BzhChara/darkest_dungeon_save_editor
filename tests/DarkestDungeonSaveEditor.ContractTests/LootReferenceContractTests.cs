using System.Text.Json;

internal static partial class ContractSuite
{
    public static async Task RunLootReferencesOnlyAsync(string repositoryRoot)
    {
        var fixture = BuildContractFixture(repositoryRoot);
        await RunLootReferenceContractsAsync(fixture.RunRoot, fixture.Codec);
        Console.WriteLine($"Artifacts: {fixture.RunRoot}");
    }

    private sealed record LootReferenceCase(string Name, string A, string B, string[] Active, string[]? Uncertain = null);
    private static string LootItem(string id, string chance = "1") =>
        "{\"type\":\"item\"," + (chance.Length == 0 ? "" : "\"chances\":" + chance + ",") +
        "\"data\":{\"type\":\"estate\",\"id\":" + JsonSerializer.Serialize(id) + ",\"amount\":1}}";
    private static string LootChild(string code, string chance = "1") =>
        "{\"type\":\"table\",\"chances\":" + chance + ",\"data\":{\"table\":" + JsonSerializer.Serialize(code) + "}}";
    private static string LootTable(string id, string entries, string conditions = "") =>
        "{\"id\":" + JsonSerializer.Serialize(id) + (conditions.Length == 0 ? "" : "," + conditions) + ",\"entries\":[" + entries + "]}";
    private static string LootFile(params string[] tables) => "{\"loot_tables\":[" + string.Join(',', tables) + "]}";

    private static async Task RunLootReferenceContractsAsync(string runRoot, DsonSaveCodec codec)
    {
        await RunLootItemIdentityContractsAsync(runRoot, codec);
        var cases = new List<LootReferenceCase>();
        foreach (var (name, chance, active) in new[] {
            ("zero", "0", false), ("negative", "-1", false), ("negative-overflow", "-1e50", false), ("underflow", "1e-50", false),
            ("missing", "", false), ("zero-first", "0,\"chances\":9", false),
            ("positive", "0.25", true), ("positive-first", "9,\"chances\":0", true) })
            cases.Add(new(name, LootFile(LootTable("audit_root", LootItem("loot_a", chance))), LootFile(), active ? ["loot_a"] : []));
        foreach (var chance in new[] { "null", "true", "\"1\"", "{}", "[]", "1e50", "1e400" })
            cases.Add(new("uncertain-" + chance, LootFile(LootTable("audit_root", LootItem("loot_a", chance)),
                LootTable("audit_root", LootItem("loot_b"))), LootFile(), [], ["loot_a"]));
        var a = LootItem("loot_a"); var b = LootItem("loot_b");
        string Root(string entry, string conditions = "") => LootTable("audit_root", entry, conditions);
        cases.AddRange([
            new("nested-zero", LootFile(Root(LootChild("child", "0")), LootTable("child", a)), LootFile(), []),
            new("nested-negative-overflow", LootFile(Root(LootChild("child", "-1e50")), LootTable("child", a)), LootFile(), []),
            new("nested-positive", LootFile(Root(LootChild("child")), LootTable("child", a)), LootFile(), ["loot_a"]),
            new("nested-uncertain", LootFile(Root(LootChild("child", "null")), LootTable("child", a)), LootFile(), [], ["loot_a"]),
            new("uncertain-unreachable", LootFile(Root(""), LootTable("orphan", LootItem("loot_a", "null"))), LootFile(), []),
            new("inline-ab", LootFile(Root(a), Root(b)), LootFile(), ["loot_a"]),
            new("inline-ba", LootFile(Root(b), Root(a)), LootFile(), ["loot_b"]),
            new("files-ab", LootFile(Root(a)), LootFile(Root(b)), ["loot_a"]),
            new("files-ba", LootFile(Root(b)), LootFile(Root(a)), ["loot_b"]),
            new("generic-shadows-specific", LootFile(Root(a), Root(b, "\"difficulty\":3")), LootFile(), ["loot_a"]),
            new("disjoint-difficulty", LootFile(Root(a, "\"difficulty\":1"), Root(b, "\"difficulty\":3")), LootFile(), ["loot_a", "loot_b"]),
            new("specific-then-generic", LootFile(Root(a, "\"difficulty\":1"), Root(b)), LootFile(), ["loot_a", "loot_b"]),
            new("empty-first", LootFile(Root(""), Root(b)), LootFile(), []),
            new("zero-first-table", LootFile(Root(LootItem("loot_a", "0")), Root(b)), LootFile(), []),
            new("shadowed-child", LootFile(Root(a), Root(LootChild("child")), LootTable("child", b)), LootFile(), ["loot_a"]),
            new("orphan", LootFile(LootTable("unused", a)), LootFile(), []),
            new("difficulty-first-member", LootFile(Root(a, "\"difficulty\":0,\"difficulty\":3"), Root(b)), LootFile(), ["loot_a"]),
            new("difficulty-wrong-first", LootFile(Root(a, "\"difficulty\":null,\"difficulty\":3"), Root(b)), LootFile(), ["loot_a"]),
            new("difficulty-float-default", LootFile(Root(a, "\"difficulty\":3.0"), Root(b)), LootFile(), ["loot_a"]),
            new("difficulty-uint-max", LootFile(Root(a, "\"difficulty\":4294967295"), Root(b)), LootFile(), ["loot_a", "loot_b"]),
            new("week-union-shadow", LootFile(Root(a, "\"week_max\":5"), Root(a, "\"week_min\":6"), Root(b)), LootFile(), ["loot_a"]),
            new("week-gap", LootFile(Root(a, "\"week_max\":5"), Root(a, "\"week_min\":7"), Root(b)), LootFile(), ["loot_a", "loot_b"]),
            new("week-zero-and-max", LootFile(Root(a, "\"week_max\":0"), Root(a, "\"week_min\":1"), Root(b)), LootFile(), ["loot_a"]),
            new("week-max-edge", LootFile(Root(a, "\"week_max\":4294967294"), Root(a, "\"week_min\":4294967295"), Root(b)), LootFile(), ["loot_a"]),
            new("reversed-week", LootFile(Root(a, "\"week_min\":5,\"week_max\":2"), Root(b)), LootFile(), ["loot_b"]),
            new("week-negative-default", LootFile(Root(a, "\"week_min\":-1"), Root(b)), LootFile(), ["loot_a"]),
            new("week-overflow-default", LootFile(Root(a, "\"week_min\":4294967296"), Root(b)), LootFile(), ["loot_a"]),
            new("week-first-member", LootFile(Root(a, "\"week_max\":null,\"week_max\":0"), Root(b)), LootFile(), ["loot_a"]),
            new("week-negative-zero", LootFile(Root(a, "\"week_max\":-0"), Root(b, "\"week_min\":1")), LootFile(), ["loot_a", "loot_b"]),
            new("nested-difficulty-conflict", LootFile(Root(LootChild("child"), "\"difficulty\":1"), LootTable("child", a, "\"difficulty\":3")), LootFile(), []),
            new("nested-week-conflict", LootFile(Root(LootChild("child"), "\"week_max\":5"), LootTable("child", a, "\"week_min\":6")), LootFile(), []),
            new("nested-week-boundary", LootFile(Root(LootChild("child"), "\"week_max\":5"), LootTable("child", a, "\"week_min\":5")), LootFile(), ["loot_a"]),
            new("nested-dungeon-case", LootFile(Root(LootChild("child"), "\"dungeon\":\"weald\""), LootTable("child", a, "\"dungeon\":\"Weald\"")), LootFile(), []),
            new("nested-dungeon-space", LootFile(Root(LootChild("child"), "\"dungeon\":\"weald\""), LootTable("child", a, "\"dungeon\":\" weald \"")), LootFile(), []),
            new("nested-dungeon-nul", LootFile(Root(LootChild("child"), "\"dungeon\":\"weald\\u0000tail\""), LootTable("child", a, "\"dungeon\":\"weald\"")), LootFile(), ["loot_a"]),
            new("nested-phase-conflict", LootFile(Root(LootChild("child"), "\"infestation_sequence_element\":\"a\""), LootTable("child", a, "\"infestation_sequence_element\":\"b\"")), LootFile(), []),
            new("nested-phase-match", LootFile(Root(LootChild("child"), "\"infestation_sequence_element\":\"a\""), LootTable("child", a, "\"infestation_sequence_element\":\"a\"")), LootFile(), ["loot_a"]),
            new("cycle", LootFile(Root(LootChild("child")), LootTable("child", LootChild("audit_root") + "," + a)), LootFile(), ["loot_a"]),
            new("cycle-disjoint-paths", LootFile(Root(LootChild("child"), "\"difficulty\":1"), Root(LootChild("child"), "\"difficulty\":3"),
                LootTable("child", a + "," + LootChild("audit_root"), "\"difficulty\":1"), LootTable("child", b, "\"difficulty\":3")), LootFile(), ["loot_a", "loot_b"]),
            new("uncertain-then-certain-path", LootFile(Root(LootChild("child", "null") + "," + LootChild("child")), LootTable("child", a)), LootFile(), ["loot_a"]),
            new("native-hash-alias", LootFile(Root(LootChild("BE")), LootTable("Az", a), LootTable("BE", b)), LootFile(), ["loot_a"]),
            new("native-code-buffer", LootFile(Root(LootChild(new string('t', 63) + "x")), LootTable(new string('t', 63) + "y", a), LootTable(new string('t', 63) + "z", b)), LootFile(), ["loot_a"]),
            new("native-nul-code", LootFile(Root(LootChild("child\0suffix")), LootTable("child\0other", a), LootTable("child", b)), LootFile(), ["loot_a"])
        ]);

        foreach (var kind in new[] { "local", "workshop", "dlc-mod" })
        {
            var root = Path.Combine(runRoot, "loot-references", kind);
            var resources = Path.Combine(root, "resources");
            var prefix = kind == "dlc-mod" ? "dlc/rq_feature/" : "";
            string Write(string path, string text) => WriteMultiMash(resources, prefix + path, text);
            var inventoryPath = Write("inventory/a.inventory.items.darkest", string.Join('\n', new[] { "loot_a", "loot_b" }.Select(id =>
                $"inventory_item: .type estate .id {id} .base_stack_limit 2 .estate_can_be_provision false")));
            Write("inventory/a.inventory.system_configs.darkest", QueryCapacity(8));
            Write("curios/a_curio_type_library.csv", QueryTypeCsv.Replace("query_loot", "audit_root"));
            Write("curios/a_curio_props.csv", QueryPropCsv);
            Write("campaign/provision/a.provision.json", "{}");
            var aPath = Write("loot/a.loot.json", LootFile());
            var bPath = Write("loot/b.loot.json", LootFile());
            WriteFixtureManifest(resources);
            var sources = QuerySources(resources, kind);
            var content = QueryContent(root, sources);
            var raid = JsonNode.Parse("""{"base_root":{"party":{"inventory":{"items":{}}}}}""")!.AsObject();
            foreach (var test in cases)
            {
                File.WriteAllText(aPath, test.A); File.WriteAllText(bPath, test.B);
                var catalog = QuantityItemCatalog.LoadRaid(content, raid);
                Assert(catalog.Items.Count == 2 && catalog.RaidStorage?.MaxSlots == 8, "Loot fixture or capacity was lost.");
                foreach (var item in catalog.Items)
                {
                    var expected = test.Active.Contains(item.ItemId) ? QuantityItemReferenceStatus.ConfirmedActive :
                        (test.Uncertain ?? []).Contains(item.ItemId) ? QuantityItemReferenceStatus.AnalysisIncomplete : QuantityItemReferenceStatus.SuspectedUnused;
                    Assert(item.ReferenceStatus == expected && item.IsHiddenByDefault == (expected == QuantityItemReferenceStatus.SuspectedUnused),
                        $"{kind}/{test.Name}/{item.ItemId}: expected {expected}, found {item.ReferenceStatus} ({string.Join(';', item.ReferenceEvidence)}).");
                    Assert(item.BaseStackLimit == 2 && !item.HasProviderConflict && item.SaveIdentityIssue.Length == 0,
                        "Loot reachability must not change item identity or stacking.");
                }
            }
            VerifyLootSelectionOracle(content, raid, aPath, bPath, inventoryPath);

            // Provider election precedes duplicate-table lookup. A distinct
            // later file does not replace the earlier table's entries.
            File.WriteAllText(aPath, LootFile(Root(a))); File.WriteAllText(bPath, LootFile());
            var overlayRoot = Path.Combine(root, "overlay");
            WriteMultiMash(overlayRoot, prefix + "loot/a.loot.json", LootFile(Root(b)));
            WriteFixtureManifest(overlayRoot);
            var overlayContent = content with { Sources = sources.Append(
                new ActiveContentSource("local:loot-overlay", "Higher loot Mod", "local", overlayRoot, -1000)).ToArray() };
            string[] Active(ActiveContentSnapshot snapshot) => QuantityItemCatalog.LoadRaid(snapshot, raid).Items
                .Where(i => i.ReferenceStatus == QuantityItemReferenceStatus.ConfirmedActive).Select(i => i.ItemId).ToArray();
            Assert(Active(overlayContent).SequenceEqual(["loot_b"]), "Higher Mod must replace a same-path loot file before table selection.");
            WriteMultiMash(overlayRoot, prefix + "loot/c.loot.json", LootFile(Root(b)));
            File.WriteAllText(Path.Combine(overlayRoot, "modfiles.txt"), prefix + "loot/c.loot.json\n");
            Assert(Active(overlayContent).SequenceEqual(["loot_a"]), "Distinct later loot files must keep first-table lookup and exclude unlisted overlays.");

            // A separate confirmed consumer wins; stale metadata must refresh
            // after weight/variant changes without altering the manifest.
            var manifestHash = ComputeSha256(Path.Combine(resources, "modfiles.txt"));
            File.WriteAllText(aPath, LootFile(Root(LootItem("loot_a", "0")))); File.WriteAllText(bPath, LootFile());
            var hidden = QuantityItemCatalog.LoadRaid(content, raid);
            var fingerprint = ProfileCatalogContentFingerprint.Capture(sources);
            File.WriteAllText(aPath, LootFile(Root(a)));
            Assert(ProfileCatalogContentFingerprint.Capture(sources) != fingerprint &&
                QuantityItemCatalog.LoadRaid(content, raid).Items.Single(i => i.ItemId == "loot_a").ReferenceStatus == QuantityItemReferenceStatus.ConfirmedActive,
                "A weight change must invalidate the content fingerprint and reveal the newly reachable item.");
            File.WriteAllText(aPath, LootFile(Root(LootItem("loot_a", "0"))));
            Write("campaign/provision/a.provision.json", """{"raid_starting_length_inventory_item_lists":[[{"type":"estate","id":"loot_a","amount":1}]]}""");
            Assert(QuantityItemCatalog.LoadRaid(content, raid).Items.Single(i => i.ItemId == "loot_a").ReferenceStatus == QuantityItemReferenceStatus.ConfirmedActive,
                "An independent provision must keep an item active despite a skipped loot edge.");
            Write("campaign/provision/a.provision.json", "{}");
            Assert(ComputeSha256(Path.Combine(resources, "modfiles.txt")) == manifestHash, "Reference refresh must not require manifest changes.");
            var saved = RaidInventorySaveEditor.SetAmount(raid, hidden.Items.Single(i => i.ItemId == "loot_a"), 3, 8).UpdatedRoot;
            var refreshed = QuantityItemCatalog.RefreshSavedAmounts(content, hidden, saved, "synthetic").Items.Single(i => i.ItemId == "loot_a");
            Assert(refreshed.IsPresentInSave && !refreshed.IsHiddenByDefault && refreshed.CurrentAmount == 3 && refreshed.ReferenceStatus == QuantityItemReferenceStatus.SuspectedUnused,
                "Saved unused items must remain visible and editable with their existing reference classification.");

            // Exercise the existing explicit manual write path on a hidden item.
            var profile = content.Profile;
            var gameInput = WriteMultiMash(root, "seed/game.json", """{"base_root":{"inraid":true,"raiddungeon":"weald","raid_save":""}}""");
            var raidInput = WriteMultiMash(root, "seed/raid.json", raid.ToJsonString());
            await codec.EncodeAsync(gameInput, Path.Combine(profile.ProfileDirectory, "persist.game.json"), null);
            await codec.EncodeAsync(raidInput, profile.RaidSavePath, null);
            WriteMultiMash(profile.ProfileDirectory, "persist.estate.json", """{"base_root":{"wallet":{},"estate_items":{"items":{}}}}""");
            content = content with { DecodedGamePath = gameInput, SourceGameSha256 = ComputeSha256(Path.Combine(profile.ProfileDirectory, "persist.game.json")) };
            var service = new SaveEditService(codec, new(root, Path.Combine(root, "workspace"), Path.Combine(root, "backups")));
            var originalHash = ComputeSha256(profile.RaidSavePath);
            var prepared = await service.PrepareQuantityItemEditAsync(profile, hidden.Items.Single(i => i.ItemId == "loot_a"), 3, content);
            var roundtrip = Path.Combine(root, "roundtrip.json");
            await codec.DecodeAsync(prepared.EncodedPath, roundtrip);
            var slots = JsonSupport.RequireObject(JsonSupport.ReadObject(roundtrip), "base_root", "party", "inventory", "items");
            Assert(slots.Count == 2 && slots.All(p => p.Value!["id"]!.GetValue<string>() == "loot_a") &&
                slots.Sum(p => p.Value!["amount"]!.GetValue<int>()) == 3 && ComputeSha256(profile.RaidSavePath) == originalHash,
                "Explicit hidden-item previews must preserve ID, two-slot stacking and original DSON saves.");
            Console.WriteLine($"PASS: {kind} loot references: {cases.Count} weight/variant cases, exhaustive selection oracle, overlays, refresh, saved items and DSON preview.");
        }
    }

    // Independent finite-state oracle: select the first matching record for
    // concrete contexts, then walk the graph. No interval subtraction is used.
    private sealed record OracleLoot(string Code, int Difficulty, string Dungeon, string Phase, int Min, int Max, string? Item, string? Child);
    private static void VerifyLootSelectionOracle(ActiveContentSnapshot content, JsonObject raid, string aPath, string bPath, string inventoryPath)
    {
        var random = new Random(250914);
        var originalInventory = File.ReadAllText(inventoryPath);
        File.AppendAllText(inventoryPath, "\n" + string.Join('\n', Enumerable.Range(0, 15).Select(i =>
            $"inventory_item: .type estate .id oracle_{i} .base_stack_limit 2 .estate_can_be_provision false")));
        for (var trial = 0; trial < 32; trial++)
        {
            var tables = new List<OracleLoot>();
            foreach (var code in new[] { "audit_root", "child", "leaf" })
            for (var i = 0; i < 5; i++)
            {
                var min = random.Next(0, 4); var max = random.Next(min, 5);
                tables.Add(new(code, random.Next(0, 3), new[] { "", "weald", "cove" }[random.Next(3)],
                    new[] { "", "low", "high" }[random.Next(3)], min, max,
                    random.Next(4) == 0 ? null : $"oracle_{tables.Count}", random.Next(2) == 0 ? null : new[] { "audit_root", "child", "leaf" }[random.Next(3)]));
            }
            var expected = new HashSet<string>(StringComparer.Ordinal);
            foreach (var difficulty in new[] { 0, 1, 2, 3 })
            foreach (var dungeon in new[] { "weald", "cove", "other" })
            foreach (var phase in new[] { "low", "high", "other" })
            foreach (var week in Enumerable.Range(0, 6))
            {
                var queue = new Queue<string>(); queue.Enqueue("audit_root");
                var visited = new HashSet<string>();
                while (queue.TryDequeue(out var code))
                {
                    if (!visited.Add(code)) continue;
                    var table = tables.FirstOrDefault(t => t.Code == code && (t.Difficulty == 0 || t.Difficulty == difficulty) &&
                        (t.Dungeon.Length == 0 || t.Dungeon == dungeon) && (t.Phase.Length == 0 || t.Phase == phase) && t.Min <= week && week <= t.Max);
                    if (table is null) continue;
                    if (table.Item is not null) expected.Add(table.Item);
                    if (table.Child is not null) queue.Enqueue(table.Child);
                }
            }
            File.WriteAllText(aPath, LootFile(tables.Select(t => LootTable(t.Code,
                string.Join(',', new[] { t.Item is null ? null : LootItem(t.Item), t.Child is null ? null : LootChild(t.Child) }.OfType<string>()),
                $"\"difficulty\":{t.Difficulty},\"dungeon\":{JsonSerializer.Serialize(t.Dungeon)},\"infestation_sequence_element\":{JsonSerializer.Serialize(t.Phase)},\"week_min\":{t.Min},\"week_max\":{t.Max}")).ToArray()));
            File.WriteAllText(bPath, LootFile());
            var actual = QuantityItemCatalog.LoadRaid(content, raid).Items.Where(i => i.ReferenceStatus == QuantityItemReferenceStatus.ConfirmedActive).Select(i => i.ItemId).ToHashSet();
            Assert(actual.SetEquals(expected), $"Loot variant/context traversal diverged from concrete native selection in trial {trial}.");
        }
        File.WriteAllText(inventoryPath, originalInventory);
    }
}
