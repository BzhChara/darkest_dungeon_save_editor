using System.Text.Json;

internal static partial class ContractSuite
{
    private static async Task VerifyResourceConsumerRecordsAsync(ActiveContentSnapshot original,
        HeroClassDefinition originalHero, string runRoot, DsonSaveCodec codec)
    {
        var readerRecords = NativeDarkestReader.ReadRecordsWithLocationsFromText(
            "# ignored: declaration\n/* block: ignored */\nfirst: .id alpha second:\n.id bravo\n\0third: .id charlie").ToArray();
        Assert(readerRecords.Select(row => row.Kind).SequenceEqual(["first", "second"]) &&
               readerRecords.Select(row => NativeDarkestReader.ReadString(row.Body, ".id")).SequenceEqual(["alpha", "bravo"]) &&
               readerRecords.Select(row => row.RecordIndex).SequenceEqual([0, 1]) && readerRecords.All(row => row.SourceLine == 3),
            "Shared reader must preserve logical boundaries and source identity independently from physical lines.");
        Assert(!NativeDarkestReader.ReadRecordsFromText("BROKEN\ninventory_item: .id ghost").Any(),
            "A bad current header must end the native stream instead of resynchronizing a regex later in the file.");
        var joined = NativeDarkestReader.ReadRecordsFromText("armour: .hp 33// comment\ncombat_skill: .id ghost").ToArray();
        Assert(joined.Length == 1 && NativeDarkestReader.ReadInt(joined[0].Body, ".hp") == 3,
            "Slash-comment removal supplies no separator: native backtracking excludes the final digit, and the invalid current prefix ends the next read.");

        var emptyTown = JsonNode.Parse("""{"base_root":{"wallet":{},"estate_items":{"items":{}}}}""")!.AsObject();
        foreach (var invalid in new[] { false, true })
        {
            var root = Path.Combine(runRoot, "invalid-resource-header", invalid.ToString());
            WriteMultiMash(root, "inventory/record.inventory.items.darkest",
                (invalid ? "BROKEN\n" : "") + "inventory_item: .type estate .id header_token .base_stack_limit 3\n");
            WriteFixtureManifest(root);
            var content = original with { Sources = [new ActiveContentSource("local:headers", "Headers", "local", root, 0)] };
            var items = QuantityItemCatalog.Load(content, emptyTown);
            Assert(items.Items.Any(item => item.ItemId == "header_token" && !item.IsSaveOnly) == !invalid,
                "Header termination must reach the public item catalog, not just a helper-level test.");
            if (!invalid)
            {
                var item = items.Items.Single(item => item.ItemId == "header_token");
                var mutation = QuantityItemSaveEditor.SetAmount(emptyTown, item, 1);
                Assert(QuantityItemSaveEditor.CountAmount(mutation.UpdatedRoot, item) == 1, "Ordinary item quantity writes must remain available.");
            }
        }

        var heroRoot = Path.Combine(runRoot, "upgrade-purchase-record-identity");
        var skillId = originalHero.CombatSkillIds.First();
        var exactSkill = skillId + " ";
        var treeId = originalHero.Id + "." + exactSkill;
        WriteMultiMash(heroRoot, $"heroes/{originalHero.Id}/{originalHero.Id}.info.darkest",
            File.ReadAllText(originalHero.SourcePath).Replace($"\"{skillId}\"", $"\"{exactSkill}\"", StringComparison.Ordinal));
        WriteMultiMash(heroRoot, "upgrades/record.upgrades.json", JsonSerializer.Serialize(new { trees = new[] {
            new { id = treeId, requirements = new[] { new { code = "0", prerequisite_resolve_level = 0 }, new { code = "1", prerequisite_resolve_level = 1 } } } } }));
        WriteFixtureManifest(heroRoot);
        var heroContent = original with { Sources = original.Sources.Append(new ActiveContentSource("local:upgrade-record", "Upgrade records", "local", heroRoot, -4000)).ToArray() };
        var heroCatalog = HeroClassCatalog.Load(heroContent);
        var hero = heroCatalog.HeroClasses.Single(row => row.Id == originalHero.Id);
        var candidate = StagecoachHeroCandidateFactory.Generate(heroCatalog, hero, 1729, 1, []);
        Assert(candidate.UpgradePurchases.Count(purchase => purchase.TreeId == treeId) == 2, "Fixture must generate the original exact tree ID before serialization.");
        var town = JsonNode.Parse("""{"base_root":{"buildings":{"stage_coach":{"store":{"hero_recruit":{"generated":{}}}}}}}""")!.AsObject();
        var roster = JsonNode.Parse("""{"base_root":{"nextGuid":894,"heroes":{}}}""")!.AsObject();
        var upgrades = JsonNode.Parse("""{"base_root":{"purchases":{}}}""")!.AsObject();
        var saved = StagecoachHeroSaveEditor.AddCandidate(town, roster, upgrades, candidate.Candidate, candidate.UpgradePurchases);
        var purchases = saved.UpdatedUpgrades["base_root"]!["purchases"]!.AsObject().Select(pair => pair.Value!).ToArray();
        var exactHash = unchecked((int)Loc2LocalizationReader.HashName(treeId));
        var trimmedHash = unchecked((int)Loc2LocalizationReader.HashName(treeId.Trim()));
        Assert(purchases.Count(row => row["tree_id"]!.GetValue<int>() == exactHash) == 2 &&
               purchases.All(row => row["tree_id"]!.GetValue<int>() != trimmedHash),
            "Upgrade purchase hashes must retain the tree identity that the generated skill actually looks up.");
        var json = Path.Combine(heroRoot, "upgrade.json");
        var dson = Path.Combine(heroRoot, "upgrade.dson");
        var decoded = Path.Combine(heroRoot, "upgrade.roundtrip.json");
        File.WriteAllText(json, saved.UpdatedUpgrades.ToJsonString());
        await codec.EncodeAsync(json, dson, null);
        await codec.DecodeAsync(dson, decoded);
        Assert(JsonNode.DeepEquals(saved.UpdatedUpgrades, JsonNode.Parse(File.ReadAllText(decoded))), "The corrected upgrade identities must survive DSON serialization.");
        foreach (var invalidPlan in new[] {
            new[] { new HeroUpgradePurchase("xAz", "0"), new HeroUpgradePurchase("xBE", "0") },
            new[] { new HeroUpgradePurchase(treeId, "0"), new HeroUpgradePurchase(treeId, "0") } })
            Assert(await CaptureSaveFailureAsync(() => { StagecoachHeroSaveEditor.AddCandidate(town, roster, upgrades, candidate.Candidate, invalidPlan); return Task.CompletedTask; }) is InvalidDataException,
                "Real tree-hash collisions and exact duplicate purchases must remain rejected.");

        VerifyCurioItemReferenceRecords(original, runRoot);
        Console.WriteLine("PASS: shared resource record termination, exact upgrade purchase persistence and curio references.");
    }

    private static void VerifyCurioItemReferenceRecords(ActiveContentSnapshot original, string runRoot)
    {
        const string header = ",,ID STRING,,RESULT TYPES\n,,audit_curio,,Nothing\n";
        const string items = ",,Item Interactions,,ITEM\n";
        var shortCode = "audit_loot" + new string('_', 21);
        var longCode = shortCode + "_trailing";
        var raid = JsonNode.Parse("""{"base_root":{"party":{"inventory":{"items":{}}}}}""")!.AsObject();
        foreach (var (name, path, csv, tokenActive, inputActive) in new[] {
            ("default-loot", "curios/curio_type_library.csv", header + ",,REGION FOUND,,Loot,1,100%,audit_loot,1\n" + items, true, false),
            ("notes-file", "curios/notes.csv", "Author notes about Loot,audit_loot\n", false, false),
            ("no-block", "curios/curio_type_library.csv", "Author notes about Loot,audit_loot\n", false, false),
            ("notes-column", "curios/curio_type_library.csv", header + ",,REGION FOUND,,Nothing,1,100%,Nothing,1,,,,,,,,,,audit_loot,Loot notes only\n" + items, false, false),
            ("zero-default-weight", "curios/curio_type_library.csv", header + ",,REGION FOUND,,Loot,0,100%,audit_loot,1\n" + items, false, false),
            ("wrong-result-case", "curios/curio_type_library.csv", header + ",,REGION FOUND,,loot,1,100%,audit_loot,1\n" + items, false, false),
            ("item-loot", "curios/curio_type_library.csv", header + items + ",,,,input_token#estate,Loot,,audit_loot,1\n", true, true),
            ("item-reference", "curios/curio_type_library.csv", header + items + ",,,,input_token#estate,Nothing,,,,,,,,,,,,\n", false, true),
            ("typed-notes", "curios/curio_type_library.csv", header + ",,,,Nothing,1,,,,,,,,,,,,input_token#estate\n" + items, false, false),
            ("past-separator", "curios/curio_type_library.csv", header + items + ",2,Next block\n,,,,input_token#estate,Loot,,audit_loot,1\n", false, false),
            ("second-loot-code", "curios/curio_type_library.csv", header + ",,REGION FOUND,,Loot,1,100%,Nothing,1,,audit_loot,1,unused\n" + items, true, false),
            ("zero-second-code", "curios/curio_type_library.csv", header + ",,REGION FOUND,,Loot,1,100%,Nothing,1,,audit_loot,0,unused\n" + items, false, false),
            ("combined-code-overflow", "curios/curio_type_library.csv", header + ",,REGION FOUND,,Loot,1,100%,audit_loot,10\n" + items, false, false),
            ("invalid-utf8", "curios/curio_type_library.csv", header + ",,REGION FOUND,,Loot,1,100%,audit_loot,1\n" + items, false, false),
            ("combined-code-delimiters", "curios/curio_type_library.csv", header + ",,REGION FOUND,,Loot,1,100%,unknown&&audit_loot&,1\n" + items, true, false),
            ("code-byte-limit", "curios/curio_type_library.csv", header + ",,REGION FOUND,,Loot,1,100%," + longCode + ",1\n" + items, true, false),
            ("weight-overflow", "curios/curio_type_library.csv", header + ",,REGION FOUND,,Loot,999999999999999999999,100%,audit_loot,1\n" + items, false, false),
            ("utf8-bom", "curios/curio_type_library.csv", header + ",,REGION FOUND,,Loot,1,100%,audit_loot,1\n" + items, true, false),
            ("utf16-bom", "curios/curio_type_library.csv", header + ",,REGION FOUND,,Loot,1,100%,audit_loot,1\n" + items, false, false),
            ("weight-cell-prefix", "curios/curio_type_library.csv", header + ",,REGION FOUND,,Loot,0.weight 1,100%,audit_loot,1\n" + items, false, false),
            ("first-count-cell-prefix", "curios/curio_type_library.csv", header + ",,REGION FOUND,,Loot,1,100%,audit_loot,0.count 999999999999\n" + items, true, false),
            ("second-count-cell-prefix", "curios/curio_type_library.csv", header + ",,REGION FOUND,,Loot,1,100%,Nothing,1,,audit_loot,0.count 1,unused\n" + items, false, false)
        })
        {
            var root = Path.Combine(runRoot, "curio-reference-records", name);
            foreach (var id in new[] { "audit_token", "input_token" })
                WriteMultiMash(root, $"inventory/{id}.inventory.items.darkest", $"inventory_item: .type estate .id {id} .base_stack_limit 3 .estate_can_be_provision false\n");
            WriteMultiMash(root, "inventory/raid.inventory.system_configs.darkest", "inventory_system_config: .type raid .max_slots 16\n");
            WriteMultiMash(root, "loot/audit.loot.json", """{"loot_tables":[{"id":"audit_loot","entries":[{"type":"item","chances":1,"data":{"type":"estate","id":"audit_token","amount":1}}]}]}""");
            if (name == "code-byte-limit")
                WriteMultiMash(root, "loot/bounded.loot.json", JsonSerializer.Serialize(new { loot_tables = new[] {
                    new { id = shortCode, entries = new[] { new { type = "item", chances = 1, data = new { type = "estate", id = "audit_token", amount = 1 } } } },
                    new { id = longCode, entries = new[] { new { type = "item", chances = 1, data = new { type = "estate", id = "input_token", amount = 1 } } } } } }));
            WriteMultiMash(root, path, csv);
            if (name == "invalid-utf8")
                File.WriteAllBytes(Path.Combine(root, path), System.Text.Encoding.UTF8.GetBytes(csv).Concat(new byte[] { 0xFF }).ToArray());
            if (name is "utf8-bom" or "utf16-bom")
            {
                var encoding = name == "utf8-bom" ? System.Text.Encoding.UTF8 : System.Text.Encoding.Unicode;
                File.WriteAllBytes(Path.Combine(root, path), encoding.GetPreamble().Concat(encoding.GetBytes(csv)).ToArray());
            }
            WriteFixtureManifest(root);
            var content = original with { Sources = [new ActiveContentSource("local:curio-refs", "References", "local", root, 0)] };
            var catalog = QuantityItemCatalog.LoadRaid(content, raid);
            foreach (var (id, active) in new[] { ("audit_token", tokenActive), ("input_token", inputActive) })
            {
                var item = catalog.Items.Single(row => row.ItemId == id);
                var incomplete = name is "combined-code-overflow" or "invalid-utf8" or "weight-overflow" or "utf16-bom";
                Assert(item.ReferenceStatus == (incomplete ? QuantityItemReferenceStatus.AnalysisIncomplete :
                           active ? QuantityItemReferenceStatus.ConfirmedActive : QuantityItemReferenceStatus.SuspectedUnused) &&
                       item.IsHiddenByDefault == (!active && !incomplete),
                    $"{name}/{id}: actual consumer columns must decide reference evidence and default visibility.");
            }
        }
    }
}
