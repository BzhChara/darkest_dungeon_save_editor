using System.Text.Json;

internal static partial class ContractSuite
{
    public static async Task RunUpgradeReferencesOnlyAsync(string repositoryRoot)
    {
        var fixture = BuildContractFixture(repositoryRoot);
        await RunUpgradeReferenceContractsAsync(fixture.RunRoot, fixture.Codec);
        Console.WriteLine($"Artifacts: {fixture.RunRoot}");
    }

    private static async Task RunUpgradeReferenceContractsAsync(string runRoot, DsonSaveCodec codec)
    {
        var run = Path.Combine(runRoot, "upgrade-references");
        var checks = 0;
        var previews = 0;
        const string heroId = "audit_hero";
        const string alpha = heroId + ".alpha";
        const string beta = heroId + ".beta";
        foreach (var kind in new[] { "local", "workshop", "dlc-mod" })
        {
            var root = Path.Combine(run, kind);
            var resources = Path.Combine(root, "resources");
            var upper = Path.Combine(root, "upper");
            var prefix = kind == "dlc-mod" ? "dlc/rq_feature/" : "";
            var relA = prefix + "upgrades/a.upgrades.json";
            var relB = prefix + "upgrades/b.upgrades.json";
            var itemRel = prefix + "inventory/a.inventory.items.darkest";
            WriteUpgradeFixture(resources, itemRel,
                "inventory_item: .type heirloom .id currency_a .base_stack_limit 12\n" +
                "inventory_item: .type heirloom .id currency_b .base_stack_limit 12\n");
            WriteUpgradeFixture(resources, "heroes/audit_hero/audit_hero.info.darkest", """
                weapon: .name audit_hero_weapon_0
                armour: .name audit_hero_armour_0 .hp 20
                generation: .is_generation_enabled true .number_of_positive_quirks_min 0 .number_of_positive_quirks_max 0 .number_of_negative_quirks_min 0 .number_of_negative_quirks_max 0 .number_of_random_combat_skills 1
                combat_skill: .id alpha .level 0
                combat_skill: .id alpha .level 1
                combat_skill: .id beta .level 0
                combat_skill: .id beta .level 1
                """);
            var skin = WriteUpgradeFixture(resources, "heroes/audit_hero/audit_hero_A/skin.png", "fixture-directory-marker");
            WriteUpgradeFixture(resources, "campaign/roster/roster.variables.json", """{"resolve_level_thresholds":[0,2,8,14,24,36,48]}""");
            WriteUpgradeFixture(resources, prefix + "localization/names.string_table.xml", """
                <root><language id="schinese"><entry id="hero_name_0">Audit Hero</entry></language>
                <language id="english"><entry id="hero_name_0">Audit Hero</entry></language></root>
                """);
            var a = WriteUpgradeFixture(resources, relA, UpgradeTrees());
            var b = WriteUpgradeFixture(resources, relB, UpgradeTrees());
            var upperA = WriteUpgradeFixture(upper, relA, UpgradeTrees());
            WriteUpgradeFixture(resources, "modfiles.txt", string.Join('\n', new[] { itemRel, relA, relB,
                "heroes/audit_hero/audit_hero.info.darkest", "heroes/audit_hero/audit_hero_A/skin.png",
                "campaign/roster/roster.variables.json", prefix + "localization/names.string_table.xml" }) + "\n");
            WriteUpgradeFixture(upper, "modfiles.txt", "");
            // Correct directory and extension, deliberately absent from modfiles.txt.
            WriteUpgradeFixture(resources, prefix + "upgrades/zzz-unlisted.upgrades.json", UpgradeTrees(UpgradeTree(alpha, UpgradeReq("0", 0, "currency_a"))));
            var source = new ActiveContentSource(kind + ":audit", "Upgrade audit", kind == "dlc-mod" ? "local" : kind, resources, 1000);
            var upperSource = new ActiveContentSource("local:upper", "Higher priority", "local", upper, 800);
            var sources = new List<ActiveContentSource> { source, upperSource };
            if (kind == "dlc-mod")
            {
                var feature = Path.Combine(root, "enabled-feature");
                Directory.CreateDirectory(feature);
                sources.Add(new("dlc:rq", "Query DLC", "dlc-feature", feature, 0) { VirtualPathPrefix = "dlc/rq_feature" });
            }
            var profileDir = Path.Combine(root, "profile");
            Directory.CreateDirectory(profileDir);
            var gameInput = WriteUpgradeFixture(root, "seed/game.json", """{"base_root":{"inraid":false,"raiddungeon":"none","raid_save":""}}""");
            var estateInput = WriteUpgradeFixture(root, "seed/estate.json", """{"base_root":{"wallet":{},"estate_items":{"items":{}}}}""");
            await codec.EncodeAsync(gameInput, Path.Combine(profileDir, "persist.game.json"), null);
            await codec.EncodeAsync(estateInput, Path.Combine(profileDir, "persist.estate.json"), null);
            var profile = new SaveProfile("profile_audit", profileDir, Path.Combine(profileDir, "persist.estate.json"), "audit", DateTime.UtcNow);
            var content = new ActiveContentSnapshot(profile, "normal", sources, [], root, gameInput, 2, ComputeSha256(Path.Combine(profileDir, "persist.game.json")));
            var estate = JsonNode.Parse(File.ReadAllText(estateInput))!.AsObject();
            var original = ComputeSha256(profile.EstateSavePath);
            var service = new SaveEditService(codec, new(root, Path.Combine(root, "workspace"), Path.Combine(root, "backups")));
            var refs = new[]
            {
                new UpgradeReferenceCase("single-a", UpgradeTrees(UpgradeTree(alpha, UpgradeReq("0", 0, "currency_a"))), UpgradeTrees(), null, ["currency_a"], false),
                new UpgradeReferenceCase("single-b-unlisted-control", UpgradeTrees(UpgradeTree(alpha, UpgradeReq("0", 0, "currency_b"))), UpgradeTrees(), null, ["currency_b"], false),
                new UpgradeReferenceCase("inline-ab", UpgradeTrees(UpgradeTree(alpha, UpgradeReq("0", 0, "currency_a")), UpgradeTree(alpha, UpgradeReq("0", 0, "currency_b"))), UpgradeTrees(), null, ["currency_b"], true),
                new UpgradeReferenceCase("inline-ba", UpgradeTrees(UpgradeTree(alpha, UpgradeReq("0", 0, "currency_b")), UpgradeTree(alpha, UpgradeReq("0", 0, "currency_a"))), UpgradeTrees(), null, ["currency_a"], false),
                new UpgradeReferenceCase("files-ab", UpgradeTrees(UpgradeTree(alpha, UpgradeReq("0", 0, "currency_a"))), UpgradeTrees(UpgradeTree(alpha, UpgradeReq("0", 0, "currency_b"))), null, ["currency_b"], false),
                new UpgradeReferenceCase("files-ba", UpgradeTrees(UpgradeTree(alpha, UpgradeReq("0", 0, "currency_b"))), UpgradeTrees(UpgradeTree(alpha, UpgradeReq("0", 0, "currency_a"))), null, ["currency_a"], false),
                new UpgradeReferenceCase("final-empty-tree", UpgradeTrees(UpgradeTree(alpha, UpgradeReq("0", 0, "currency_a"))), UpgradeTrees(UpgradeTree(alpha)), null, [], false),
                new UpgradeReferenceCase("final-empty-cost", UpgradeTrees(UpgradeTree(alpha, UpgradeReq("0", 0, "currency_a")), UpgradeTree(alpha, UpgradeReq("0", 0))), UpgradeTrees(), null, [], false),
                new UpgradeReferenceCase("same-code-cost-ab", UpgradeTrees(UpgradeTree(alpha, UpgradeReq("0", 0, "currency_a"), UpgradeReq("0", 0, "currency_b"))), UpgradeTrees(), null, ["currency_b"], true),
                new UpgradeReferenceCase("same-code-cost-ba", UpgradeTrees(UpgradeTree(alpha, UpgradeReq("0", 0, "currency_b"), UpgradeReq("0", 0, "currency_a"))), UpgradeTrees(), null, ["currency_a"], false),
                new UpgradeReferenceCase("same-code-empty-cost", UpgradeTrees(UpgradeTree(alpha, UpgradeReq("0", 0, "currency_a"), UpgradeReq("0", 0))), UpgradeTrees(), null, [], false),
                new UpgradeReferenceCase("distinct-codes", UpgradeTrees(UpgradeTree(alpha, UpgradeReq("0", 0, "currency_a"), UpgradeReq("1", 1, "currency_b"))), UpgradeTrees(), null, ["currency_a", "currency_b"], false),
                new UpgradeReferenceCase("distinct-trees", UpgradeTrees(UpgradeTree(alpha, UpgradeReq("0", 0, "currency_a")), UpgradeTree(beta, UpgradeReq("0", 0, "currency_b"))), UpgradeTrees(), null, ["currency_a", "currency_b"], false),
                new UpgradeReferenceCase("same-path-upper", UpgradeTrees(UpgradeTree(alpha, UpgradeReq("0", 0, "currency_a"))), UpgradeTrees(), UpgradeTrees(UpgradeTree(alpha, UpgradeReq("0", 0, "currency_b"))), ["currency_b"], false),
                new UpgradeReferenceCase("same-path-slot-then-later", UpgradeTrees(UpgradeTree(alpha, UpgradeReq("0", 0, "currency_a"))), UpgradeTrees(UpgradeTree(alpha, UpgradeReq("0", 0, "currency_a"))), UpgradeTrees(UpgradeTree(alpha, UpgradeReq("0", 0, "currency_b"))), ["currency_a"], false),
                new UpgradeReferenceCase("case-distinct-codes", UpgradeTrees(UpgradeTree(alpha, UpgradeReq("a", 0, "currency_a"), UpgradeReq("A", 0, "currency_b"))), UpgradeTrees(), null, ["currency_a", "currency_b"], false),
                new UpgradeReferenceCase("native-code-byte", UpgradeTrees(UpgradeTree(alpha, UpgradeReq("10", 0, "currency_a"), UpgradeReq("1", 0, "currency_b"))), UpgradeTrees(), null, ["currency_b"], false),
                new UpgradeReferenceCase("native-utf8-byte", UpgradeTrees(UpgradeTree(alpha, UpgradeReq("é", 0, "currency_a"), UpgradeReq("è", 0, "currency_b"))), UpgradeTrees(), null, ["currency_b"], false),
                new UpgradeReferenceCase("native-tree-hash", UpgradeTrees(UpgradeTree("Az", UpgradeReq("0", 0, "currency_a")), UpgradeTree("BE", UpgradeReq("0", 0, "currency_b"))), UpgradeTrees(), null, ["currency_b"], false),
                new UpgradeReferenceCase("native-tree-nul", UpgradeTrees(UpgradeTree(alpha + "\0suffix", UpgradeReq("0", 0, "currency_a")), UpgradeTree(alpha, UpgradeReq("0", 0, "currency_b"))), UpgradeTrees(), null, ["currency_b"], false),
                new UpgradeReferenceCase("first-code-member", UpgradeTrees(UpgradeTree(alpha, UpgradeReq("0", 0, "currency_a"), UpgradeReq("1", 0, "currency_b")))
                    .Replace("\"code\":\"0\"", "\"code\":\"0\",\"code\":\"1\""), UpgradeTrees(), null, ["currency_a", "currency_b"], false),
                new UpgradeReferenceCase("first-cost-member", UpgradeTrees(UpgradeTree(alpha, UpgradeReq("0", 0, "currency_a")))
                    .Replace("\"currency_cost\":", "\"currency_cost\":[],\"currency_cost\":"), UpgradeTrees(), null, [], false),
                new UpgradeReferenceCase("first-requirements-member", UpgradeTrees(UpgradeTree(alpha, UpgradeReq("0", 0, "currency_a")))
                    .Replace("\"requirements\":", "\"requirements\":[],\"requirements\":"), UpgradeTrees(), null, [], false),
                new UpgradeReferenceCase("first-trees-member", UpgradeTrees(UpgradeTree(alpha, UpgradeReq("0", 0, "currency_a")))
                    .Replace("\"trees\":", "\"trees\":[],\"trees\":"), UpgradeTrees(), null, [], false),
            };
            foreach (var scenario in refs)
            {
                File.WriteAllText(a, scenario.A);
                File.WriteAllText(b, scenario.B);
                File.WriteAllText(upperA, scenario.Upper ?? UpgradeTrees());
                WriteUpgradeFixture(upper, "modfiles.txt", scenario.Upper is null ? "" : relA + "\n");
                var candidates = new List<ContentFileCandidate> { new(source, a), new(source, b) };
                if (scenario.Upper is not null) candidates.Add(new(upperSource, upperA));
                var orderIssues = new List<string>();
                var order = NativeContentFileResolver.Resolve(candidates, sources, "Upgrade audit", orderIssues);
                Assert(order.Count == 2 && order[0].Path == (scenario.Upper is null ? a : upperA) && order[1].Path == b,
                    "Unexpected effective path slots: " + string.Join('|', order.Select(f => f.Path)));
                var catalog = QuantityItemCatalog.Load(content, estate, original);
                Assert(catalog.Items.Count == 2, "Broken quantity fixture: " + string.Join('|', catalog.Issues));
                var active = catalog.Items.Where(i => i.ReferenceStatus == QuantityItemReferenceStatus.ConfirmedActive)
                    .Select(i => i.DisplayId).Order().ToArray();
                Assert(active.SequenceEqual(scenario.Active.Order()), "Wrong final upgrade currency references: " + scenario.Name + "/" + string.Join(',', active));
                Assert(catalog.Items.All(i => !i.HasProviderConflict && i.SaveIdentityIssue.Length == 0 &&
                    !i.IsPresentInSave && i.BaseStackLimit == 12 && i.IsHiddenByDefault == !active.Contains(i.DisplayId)), "Unrelated quantity guard");
                if (scenario.Preview)
                {
                    var selected = catalog.Items.Single(i => i.DisplayId == "currency_a");
                    var prepared = await service.PrepareQuantityItemEditAsync(profile, selected, 3, content);
                    var roundtrip = Path.Combine(root, "roundtrip", scenario.Name + ".json");
                    await codec.DecodeAsync(prepared.EncodedPath, roundtrip);
                    var changed = JsonNode.Parse(File.ReadAllText(roundtrip))!.AsObject();
                    var wallet = changed["base_root"]!["wallet"]!.AsObject();
                    Assert(wallet.Count == 1 && wallet.Single().Value!["type"]!.GetValue<string>() == selected.PersistedType &&
                        wallet.Single().Value!["amount"]!.GetValue<int>() == 3, "Preview quantity changed");
                    Assert(ComputeSha256(profile.EstateSavePath) == original, "Preview altered source save");
                    previews++;
                }
                checks++;
            }
            WriteUpgradeFixture(upper, "modfiles.txt", "");
            var manifestPath = Path.Combine(resources, "modfiles.txt");
            var manifest = File.ReadAllText(manifestPath);
            var manifestHash = ComputeSha256(manifestPath);
            var malformed = new[] { "{", "{}", "{\"trees\":{}}", "{\"trees\":[{\"requirements\":[]}]}",
                "{\"trees\":[{\"id\":\"\\ud800\",\"requirements\":[]}]}" };
            File.WriteAllText(a, UpgradeTrees(UpgradeTree(alpha, UpgradeReq("0", 0, "currency_a"))));
            foreach (var text in malformed)
            {
                File.WriteAllText(b, text);
                var uncertain = QuantityItemCatalog.Load(content, estate);
                Assert(uncertain.Items.All(i => i.ReferenceStatus == QuantityItemReferenceStatus.AnalysisIncomplete),
                    "An unreadable replacement identity must not certify an earlier upgrade tree: " + text);
                checks++;
            }
            // Known invalid winners occupy their own ID; they do not expose old costs or block other confirmed trees.
            File.WriteAllText(b, UpgradeTrees(new JsonObject { ["id"] = alpha }, UpgradeTree(beta, UpgradeReq("0", 0, "currency_b"))));
            var invalidWinner = QuantityItemCatalog.Load(content, estate);
            Assert(invalidWinner.Items.Single(i => i.DisplayId == "currency_a").ReferenceStatus == QuantityItemReferenceStatus.AnalysisIncomplete &&
                invalidWinner.Items.Single(i => i.DisplayId == "currency_b").ReferenceStatus == QuantityItemReferenceStatus.ConfirmedActive,
                "Invalid final requirements must retain their identity and preserve independent references");
            checks++;
            File.WriteAllText(b, UpgradeTrees(UpgradeTree(alpha, UpgradeReq("bad-code", 0, "currency_a")))
                .Replace("bad-code", "\\ud800"));
            var invalidCode = QuantityItemCatalog.Load(content, estate);
            Assert(invalidCode.Items.All(i => i.ReferenceStatus == QuantityItemReferenceStatus.AnalysisIncomplete),
                "An undecodable winning purchase code must not crash the quantity catalog or fall back");
            checks++;
            File.Delete(b);
            var missing = QuantityItemCatalog.Load(content, estate);
            Assert(missing.Items.All(i => i.ReferenceStatus == QuantityItemReferenceStatus.AnalysisIncomplete) && missing.Issues.Any(i => i.Contains("missing")),
                "A manifest-listed missing upgrade file may replace prior trees");
            checks++;
            WriteUpgradeFixture(resources, prefix + "campaign/town_events/independent.town_events.events.json",
                """{"events":[{"id":"upgrade_independent","data":[{"type":"bonus_currency","string_data":"currency_a"}]}]}""");
            File.WriteAllText(manifestPath, manifest + prefix + "campaign/town_events/independent.town_events.events.json\n");
            var independent = QuantityItemCatalog.Load(content, estate);
            Assert(independent.Items.Single(i => i.DisplayId == "currency_a").ReferenceStatus == QuantityItemReferenceStatus.ConfirmedActive,
                "Upgrade uncertainty must not erase an independent active town event reference");
            checks++;
            File.WriteAllText(manifestPath, manifest);
            File.WriteAllText(b, UpgradeTrees(UpgradeTree(alpha, UpgradeReq("0", 0, "currency_b"))));
            var refreshed = QuantityItemCatalog.Load(content, estate);
            Assert(refreshed.Items.Single(i => i.DisplayId == "currency_a").IsHiddenByDefault &&
                refreshed.Items.Single(i => i.DisplayId == "currency_b").ReferenceStatus == QuantityItemReferenceStatus.ConfirmedActive &&
                ComputeSha256(manifestPath) == manifestHash,
                "Editing only upgrade contents must recompute final references without requiring a changed manifest");
            checks++;
            File.WriteAllText(b, UpgradeTrees());
            var progressions = new[]
            {
                new UpgradeProgressionCase("single-level1", [UpgradeReq("0", 0), UpgradeReq("1", 1)], false, 1),
                new UpgradeProgressionCase("single-level3", [UpgradeReq("0", 0), UpgradeReq("1", 3)], false, 3),
                new UpgradeProgressionCase("repeat-level3-then1", [UpgradeReq("0", 0), UpgradeReq("1", 3), UpgradeReq("1", 1)], true, 1),
                new UpgradeProgressionCase("repeat-level1-then3", [UpgradeReq("0", 0), UpgradeReq("1", 1), UpgradeReq("1", 3)], true, 3),
                new UpgradeProgressionCase("repeat-identical", [UpgradeReq("0", 0), UpgradeReq("1", 1), UpgradeReq("1", 1)], false, 1),
                new UpgradeProgressionCase("overwritten-unpersistable-code", [UpgradeReq("0", 0), UpgradeReq("10", 3), UpgradeReq("1", 1)], false, 1),
            };
            foreach (var scenario in progressions)
            {
                var raw = UpgradeTrees(UpgradeTree(alpha, scenario.Requirements));
                File.WriteAllText(a, raw);
                var catalog = HeroClassCatalog.Load(content);
                var hero = catalog.HeroClasses.Single(h => h.Id == heroId);
                var tree = hero.UpgradeTrees.Single(t => t.Id == alpha);
                Assert(catalog.HeroNames.Count > 0 && hero.ColourVariationCount == 1 && hero.LevelProfiles.Count == 7 && !hero.HasProviderConflict,
                    "Broken hero fixture: " + string.Join('|', catalog.Issues));
                Assert(hero.GenerationAvailability.All(l => l.CanGenerate),
                    "Repeated purchase codes must use the final requirement: " + scenario.Name + "/" + string.Join('|', catalog.Issues));
                Assert(tree.Requirements.Single(r => r.Code == "1").PrerequisiteResolveLevel == scenario.NativeLevel,
                    "Wrong final resolve prerequisite");
                var level1 = StagecoachHeroCandidateFactory.Generate(catalog, hero, 1729, 1, []);
                var level3 = StagecoachHeroCandidateFactory.Generate(catalog, hero, 1729, 3, []);
                string[] Codes(GeneratedStagecoachHeroCandidate c) => c.UpgradePurchases.Where(p => p.TreeId == alpha).Select(p => p.RequirementCode).ToArray();
                Assert(Codes(level1).SequenceEqual(scenario.NativeLevel == 1 ? new[] { "0", "1" } : new[] { "0" }) &&
                    Codes(level3).SequenceEqual(["0", "1"]), "Winning resolve level does not drive purchases");
                if (scenario.RoundTrip)
                {
                    var town = JsonNode.Parse("""{"base_root":{"buildings":{"stage_coach":{"store":{"hero_recruit":{"generated":{}}}}}}}""")!.AsObject();
                    var roster = JsonNode.Parse("""{"base_root":{"nextGuid":894,"heroes":{}}}""")!.AsObject();
                    var purchases = JsonNode.Parse("""{"base_root":{"purchases":{}}}""")!.AsObject();
                    var mutation = StagecoachHeroSaveEditor.AddCandidate(town, roster, purchases, level1.Candidate, level1.UpgradePurchases);
                    var input = WriteUpgradeFixture(root, "roundtrip/" + scenario.Name + ".input.json", mutation.UpdatedUpgrades.ToJsonString());
                    var encoded = input + ".dson";
                    var decoded = input + ".decoded.json";
                    await codec.EncodeAsync(input, encoded, null);
                    await codec.DecodeAsync(encoded, decoded);
                    Assert(JsonNode.DeepEquals(mutation.UpdatedUpgrades, JsonNode.Parse(File.ReadAllText(decoded))), "Final native purchases failed DSON roundtrip");
                    previews++;
                }
                checks++;
            }
            File.WriteAllText(a, UpgradeTrees(UpgradeTree(alpha, UpgradeReq("0", 0), UpgradeReq("1", 1), UpgradeReq("10", 3))));
            var unsupported = HeroClassCatalog.Load(content).HeroClasses.Single(h => h.Id == heroId);
            Assert(unsupported.GenerationAvailability.All(l => !l.CanGenerate) &&
                unsupported.UpgradeTrees.Single(t => t.Id == alpha).UnsupportedReason.Contains("DSON"),
                "The actual winning code must still satisfy DSON persistence constraints");
            checks++;
            Assert(ComputeSha256(profile.EstateSavePath) == original, "Synthetic source save changed");
        }
        Console.WriteLine($"PASS: final upgrade requirements and cost references ({checks} scenarios, {previews} DSON previews/roundtrips).");
    }

    private static JsonObject UpgradeReq(string code, int level, params string[] currencies) => new()
    {
        ["code"] = code, ["prerequisite_resolve_level"] = level,
        ["currency_cost"] = new JsonArray(currencies.Select(c => (JsonNode)new JsonObject { ["type"] = c, ["amount"] = 1 }).ToArray()),
        ["prerequisite_requirements"] = new JsonArray()
    };
    private static JsonObject UpgradeTree(string id, params JsonObject[] requirements) => new()
    {
        ["id"] = id, ["tags"] = new JsonArray(),
        ["requirements"] = new JsonArray(requirements.Select(r => r.DeepClone()).ToArray())
    };
    private static string UpgradeTrees(params JsonObject[] trees) => new JsonObject { ["trees"] = new JsonArray(trees.Select(t => t.DeepClone()).ToArray()) }.ToJsonString();
    private static string WriteUpgradeFixture(string root, string relative, string text)
    {
        var path = Path.GetFullPath(Path.Combine(root, relative)); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text, new UTF8Encoding(false)); return path;
    }
    private sealed record UpgradeReferenceCase(string Name, string A, string B, string? Upper, string[] Active, bool Preview);
    private sealed record UpgradeProgressionCase(string Name, JsonObject[] Requirements, bool RoundTrip, int NativeLevel);
}
