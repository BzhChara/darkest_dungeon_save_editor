using System.Text.Json;

internal static partial class ContractSuite
{
    private static async Task RunBuffEnumContractsAsync(string runRoot, DsonSaveCodec codec)
    {
        // Independent base-53 collision construction; no product hash helper supplies expectations.
        string Alias(string name) => ((char)(name[0] + 1)).ToString() + (char)(name[1] - 53) + name[2..];
        var results = new List<object>();
        foreach (var kind in new[] { "base", "local", "workshop" })
        {
            var root = Path.Combine(runRoot, "buff-enums", kind);
            var buffs = new JsonArray();
            var quirks = new JsonArray();
            var cases = new List<(string Id, string Stat, string Rule, bool Inverse, bool Unsafe, double? Hp)>();
            foreach (var stat in new[] { "combat_stat_add", "combat_stat_multiply" })
            foreach (var rule in new[] { "always", "no_trinkets", "afflicted", "in_mode", "lightabove" })
            foreach (var inverse in new[] { false, true })
            foreach (var unsafeHp in new[] { false, true })
            {
                var id = "enum_" + cases.Count;
                var flat = stat == "combat_stat_add";
                var amount = unsafeHp ? flat ? -20 : -1 : flat ? 4 : 0.5;
                var activeAtGeneration = (rule is "always" or "no_trinkets") && !inverse || rule == "afflicted" && inverse;
                var unreachable = rule == "always" && inverse;
                double? expected = unsafeHp && !unreachable ? null : activeAtGeneration ? flat ? 24 : 30 : 20;
                buffs.Add(new JsonObject
                {
                    ["id"] = id, ["stat_type"] = Alias(stat), ["stat_sub_type"] = "max_hp",
                    ["amount"] = amount, ["rule_type"] = Alias(rule), ["is_false_rule"] = inverse,
                    ["rule_data"] = new JsonObject { ["float"] = 50, ["string"] = "test_mode" }
                });
                quirks.Add(new JsonObject { ["id"] = id, ["is_positive"] = true, ["random_chance"] = 1, ["buffs"] = new JsonArray(id) });
                cases.Add((id, stat, rule, inverse, unsafeHp, expected));
            }
            WriteMultiMash(root, "heroes/enum_hero/enum_hero.info.darkest", """
                weapon: .name test_weapon
                armour: .name test_armour .hp 20
                generation: .is_generation_enabled true .number_of_positive_quirks_min 0 .number_of_positive_quirks_max 0 .number_of_negative_quirks_min 0 .number_of_negative_quirks_max 0 .number_of_random_combat_skills 1
                combat_skill: .id alpha .level 0
                mode: .id test_mode .is_raid_default true
                """);
            WriteMultiMash(root, "heroes/enum_hero/enum_hero_A/skin.png", "directory marker");
            WriteMultiMash(root, "localization/names.string_table.xml", """<root><language id="english"><entry id="hero_name_0">Test</entry></language></root>""");
            WriteMultiMash(root, "shared/buffs/enums.buffs.json", new JsonObject { ["buffs"] = buffs }.ToJsonString());
            WriteMultiMash(root, "shared/quirk/enums.quirk_library.json", new JsonObject { ["quirks"] = quirks }.ToJsonString());
            if (kind != "base") WriteFixtureManifest(root);
            var profile = new SaveProfile("unused", root, Path.Combine(root, "unused.json"), "test", DateTime.UtcNow);
            var content = new ActiveContentSnapshot(profile, "normal", [new(kind, kind, kind, root, 1000)], [], root, "", 0, "");
            var catalog = HeroClassCatalog.Load(content);
            var hero = catalog.HeroClasses.Single(h => h.Id == "enum_hero");
            foreach (var test in cases)
            {
                var quirk = catalog.InitialQuirks.Single(q => q.Id == test.Id);
                var modifier = quirk.MaxHpModifiers.Single();
                Assert(quirk.WriteStatus == HeroInitialQuirkWriteStatus.Direct && modifier.RuleType == test.Rule &&
                    modifier.Kind == (test.Stat == "combat_stat_add" ? HeroMaxHpModifierKind.Flat : HeroMaxHpModifierKind.Percentage),
                    "Supported native enum aliases must become canonical internal operations and conditions.");
                if (test.Hp is null)
                {
                    var selectionRejected = false;
                    var generationRejected = false;
                    try { StagecoachHeroCandidateFactory.ValidateInitialQuirkSelection(catalog, hero, [test.Id]); }
                    catch (InvalidOperationException) { selectionRejected = true; }
                    try { StagecoachHeroCandidateFactory.Generate(catalog, hero, 1729, [test.Id]); }
                    catch (InvalidOperationException) { generationRejected = true; }
                    Assert(selectionRejected && generationRejected, "Alias conditions must participate in all reachable non-positive HP guards.");
                    results.Add(new { kind, test.Id, test.Stat, test.Rule, test.Inverse, ExpectedHp = test.Hp, Rejected = true });
                    continue;
                }
                StagecoachHeroCandidateFactory.ValidateInitialQuirkSelection(catalog, hero, [test.Id]);
                var generated = StagecoachHeroCandidateFactory.Generate(catalog, hero, 1729, [test.Id]);
                Assert(generated.Preview.CurrentHp == test.Hp && generated.Candidate["actor"]!["current_hp"]!.GetValue<double>() == test.Hp,
                    $"{kind}/{test.Id}: alias condition must produce expected initial HP={test.Hp}.");
                results.Add(new { kind, test.Id, test.Stat, test.Rule, test.Inverse, ExpectedHp = test.Hp, ActualHp = generated.Preview.CurrentHp });
                if (test.Stat != "combat_stat_multiply" || test.Rule != "always" || test.Inverse || test.Unsafe) continue;
                var town = JsonNode.Parse("""{"base_root":{"buildings":{"stage_coach":{"store":{"hero_recruit":{"generated":{}}}}}}}""")!.AsObject();
                var roster = JsonNode.Parse("""{"base_root":{"nextGuid":1,"heroes":{}}}""")!.AsObject();
                var upgrades = JsonNode.Parse("""{"base_root":{"purchases":{}}}""")!.AsObject();
                var mutation = StagecoachHeroSaveEditor.AddCandidate(town, roster, upgrades, generated.Candidate, generated.UpgradePurchases);
                var seed = WriteMultiMash(root, "town.seed.json", mutation.UpdatedTown.ToJsonString());
                await codec.EncodeAsync(seed, seed + ".dson", null);
                await codec.DecodeAsync(seed + ".dson", seed + ".roundtrip.json");
                Assert(JsonNode.DeepEquals(mutation.UpdatedTown, JsonSupport.ReadObject(seed + ".roundtrip.json")),
                    "Alias-derived full HP must survive real DSON insertion without changing unrelated town data.");
            }
            Console.WriteLine($"PASS: {kind} Buff enum hashes, {cases.Count} initial/reachable HP cases and DSON roundtrip.");
        }
        WriteMultiMash(runRoot, "buff-enums/results.json", JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
    }
}
