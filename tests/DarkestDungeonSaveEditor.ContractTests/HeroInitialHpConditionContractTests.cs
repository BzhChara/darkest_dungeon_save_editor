using System.Text.Json;

internal static partial class ContractSuite
{
    private static async Task RunInitialHpConditionContractsAsync(string runRoot, DsonSaveCodec codec)
    {
        var root = Path.Combine(runRoot, "initial-hp-conditions");
        JsonObject Buff(string id, string rule, bool inverse, double amount, bool flat = false) => new()
        {
            ["id"] = id, ["stat_type"] = flat ? "combat_stat_add" : "combat_stat_multiply",
            ["stat_sub_type"] = "max_hp", ["amount"] = amount,
            ["rule_type"] = rule, ["is_false_rule"] = inverse, ["remove_if_not_active"] = false,
            ["rule_data"] = new JsonObject { ["float"] = 0, ["string"] = "audit_mode" }
        };
        var buffs = new[]
        {
            Buff("always", "always", false, 0.5), Buff("not_always", "always", true, 0.5),
            Buff("unequipped", "no_trinkets", false, 0.5), Buff("equipped", "no_trinkets", true, 0.5),
            Buff("afflicted", "afflicted", false, 0.5), Buff("unafflicted", "afflicted", true, 0.5),
            Buff("unafflicted_negative", "afflicted", true, -0.25), Buff("unafflicted_flat", "afflicted", true, 4, flat: true),
            Buff("mode", "in_mode", false, 0.5), Buff("not_mode", "in_mode", true, 0.5),
            Buff("light", "lightabove", false, 0.5), Buff("not_light", "lightabove", true, 0.5),
            Buff("afflicted_half", "afflicted", false, -0.5), Buff("unafflicted_half", "afflicted", true, -0.5),
            Buff("afflicted_zero", "afflicted", false, -1), Buff("unafflicted_zero", "afflicted", true, -1),
            Buff("afflicted_flat_zero", "afflicted", false, -20, flat: true), Buff("unafflicted_flat_zero", "afflicted", true, -20, flat: true),
            Buff("unknown", "unknown_hp_rule", false, 0.5), Buff("not_unknown", "unknown_hp_rule", true, 0.5)
        };
        // Expected values are full HP for a fresh, unequipped, unafflicted hero outside a raid.
        // The native afflicted applicability/inversion branches are recorded in the 2026-09-15 audit.
        (string Name, string[] Buffs, double? Hp)[] cases =
        [
            ("always", ["always"], 30), ("not_always", ["not_always"], 20),
            ("unequipped", ["unequipped"], 30), ("equipped", ["equipped"], 20),
            ("afflicted", ["afflicted"], 20), ("unafflicted", ["unafflicted"], 30),
            ("unafflicted_negative", ["unafflicted_negative"], 15), ("unafflicted_flat", ["unafflicted_flat"], 24),
            ("mode", ["mode"], 20), ("not_mode", ["not_mode"], 20),
            ("light", ["light"], 20), ("not_light", ["not_light"], 20),
            ("mixed", ["unafflicted_flat", "always"], 36),
            ("summed", ["unafflicted", "always"], 40),
            ("repeated", ["unafflicted", "unafflicted"], 40),
            ("exclusive", ["afflicted_half", "unafflicted_half"], 10),
            ("afflicted_zero", ["afflicted_zero"], null), ("unafflicted_zero", ["unafflicted_zero"], null),
            ("afflicted_flat_zero", ["afflicted_flat_zero"], null), ("unafflicted_flat_zero", ["unafflicted_flat_zero"], null),
            ("unknown", ["unknown"], null), ("not_unknown", ["not_unknown"], null)
        ];
        var results = new List<object>();
        var roundTrips = 0;
        foreach (var kind in new[] { "base", "mode", "dlc-package", "dlc-feature", "local", "workshop" })
        {
            var sourceRoot = Path.Combine(root, kind, "content");
            void Write(string path, string text) => WriteMultiMash(sourceRoot, path, text);
            Write("heroes/hp_hero/hp_hero.info.darkest", """
                weapon: .name hp_weapon
                armour: .name hp_armour .hp 20
                generation: .is_generation_enabled true .number_of_positive_quirks_min 0 .number_of_positive_quirks_max 0 .number_of_negative_quirks_min 0 .number_of_negative_quirks_max 0 .number_of_random_combat_skills 1
                combat_skill: .id alpha .level 0
                mode: .id audit_mode .is_raid_default true
                """);
            Write("heroes/hp_hero/hp_hero_A/skin.png", "skin directory marker");
            Write("localization/names.string_table.xml", """
                <root><language id="schinese"><entry id="hero_name_0">HP Test</entry></language>
                <language id="english"><entry id="hero_name_0">HP Test</entry></language></root>
                """);
            Write("shared/buffs/hp.buffs.json", new JsonObject
            {
                ["buffs"] = new JsonArray(buffs.Select(buff => buff.DeepClone()).ToArray())
            }.ToJsonString());
            Write("shared/quirk/hp.quirk_library.json", new JsonObject
            {
                ["quirks"] = new JsonArray(cases.Select(test => (JsonNode)new JsonObject
                {
                    ["id"] = "hp_" + test.Name, ["is_positive"] = true, ["random_chance"] = 1,
                    ["buffs"] = new JsonArray(test.Buffs.Select(id => (JsonNode)JsonValue.Create(id)!).ToArray())
                }).ToArray())
            }.ToJsonString());
            if (kind is "local" or "workshop") WriteFixtureManifest(sourceRoot);
            var source = new ActiveContentSource(kind + ":initial-hp", "Initial HP", kind, sourceRoot, 1000)
            {
                VirtualPathPrefix = kind is "dlc-package" or "dlc-feature" ? "dlc/hp_feature" : ""
            };
            var profileRoot = Path.Combine(root, kind, "profile");
            var profile = new SaveProfile("profile_hp", profileRoot, Path.Combine(profileRoot, "persist.estate.json"), "test", DateTime.UtcNow);
            var content = new ActiveContentSnapshot(profile, "normal", [source], [], root, "", kind is "local" or "workshop" ? 1 : 0, "");
            var catalog = HeroClassCatalog.Load(content);
            var hero = catalog.HeroClasses.Single(h => h.Id == "hp_hero");
            Assert(hero.BaseHp == 20 && hero.ColourVariationCount == 1 && catalog.HeroNames.Count > 0,
                $"{kind}: initial HP fixture must have valid generation prerequisites.");
            foreach (var test in cases)
            {
                var quirkId = "hp_" + test.Name;
                var quirk = catalog.InitialQuirks.Single(q => q.Id == quirkId);
                var unknown = test.Name is "unknown" or "not_unknown";
                Assert(quirk.WriteStatus == (unknown ? HeroInitialQuirkWriteStatus.Unverified : HeroInitialQuirkWriteStatus.Direct),
                    $"{kind}/{test.Name}: recognized HP conditions and unknown conditions must retain distinct write statuses.");
                if (test.Hp is null)
                {
                    var selectionRejected = false;
                    var generationRejected = false;
                    try { StagecoachHeroCandidateFactory.ValidateInitialQuirkSelection(catalog, hero, [quirkId]); }
                    catch (InvalidOperationException) { selectionRejected = true; }
                    try { StagecoachHeroCandidateFactory.Generate(catalog, hero, 1729, [quirkId]); }
                    catch (InvalidOperationException) { generationRejected = true; }
                    Assert(selectionRejected && generationRejected,
                        $"{kind}/{test.Name}: unknown or reachable non-positive HP must be rejected by both selection and generation.");
                    results.Add(new { Kind = kind, test.Name, ExpectedHp = test.Hp, Rejected = true });
                    continue;
                }
                StagecoachHeroCandidateFactory.ValidateInitialQuirkSelection(catalog, hero, [quirkId]);
                var generated = StagecoachHeroCandidateFactory.Generate(catalog, hero, 1729, [quirkId]);
                var hp = generated.Candidate["actor"]!["current_hp"]!.GetValue<double>();
                Assert(generated.Preview.CurrentHp == test.Hp && hp == test.Hp,
                    $"{kind}/{test.Name}: expected initial HP {test.Hp}, got preview {generated.Preview.CurrentHp}, candidate {hp}.");
                Assert(generated.Candidate["actor"]!["buff_group"]!.AsObject().Count == 0 &&
                       quirk.MaxHpModifiers.Count == test.Buffs.Length,
                    "Repeated Buff references must participate in HP without serializing duplicate actor Buffs.");
                results.Add(new { Kind = kind, test.Name, ExpectedHp = test.Hp, ActualHp = hp, Rejected = false });
                if (test.Name is not ("always" or "unafflicted" or "unafflicted_negative" or "unafflicted_flat")) continue;
                foreach (var shard in test.Name == "unafflicted" ? new[] { false, true } : new[] { false })
                {
                    var candidate = (JsonObject)generated.Candidate.DeepClone();
                    if (shard) candidate["quirks"]!["shard_hungry"] = new JsonObject { ["is_positive"] = false, ["is_locked"] = false };
                    var town = JsonNode.Parse("""{"base_root":{"buildings":{"stage_coach":{"store":{"hero_recruit":{"generated":{"893":{"actor":{"current_hp":13.5}}}},"shard_hero_recruit":{"generated":{"891":{"actor":{"current_hp":11.5}}}}}}}}}""")!.AsObject();
                    var roster = JsonNode.Parse("""{"base_root":{"nextGuid":894,"heroes":{"892":{"actor":{"current_hp":7.5}}}}}""")!.AsObject();
                    var upgrades = JsonNode.Parse("""{"base_root":{"purchases":{}}}""")!.AsObject();
                    var originals = new[] { town.ToJsonString(), roster.ToJsonString(), upgrades.ToJsonString(), candidate.ToJsonString() };
                    var mutation = StagecoachHeroSaveEditor.AddCandidate(town, roster, upgrades, candidate, generated.UpgradePurchases);
                    var prefix = Path.Combine(kind, test.Name + (shard ? "-shard" : "-ordinary"));
                    var input = WriteMultiMash(root, prefix + ".input.json", mutation.UpdatedTown.ToJsonString());
                    var binary = input + ".dson";
                    var decoded = input + ".decoded.json";
                    await codec.EncodeAsync(input, binary, null);
                    await codec.DecodeAsync(binary, decoded);
                    var restored = JsonNode.Parse(File.ReadAllText(decoded))!.AsObject();
                    var pool = shard ? "shard_hero_recruit" : "hero_recruit";
                    Assert(JsonNode.DeepEquals(mutation.UpdatedTown, restored) &&
                           restored["base_root"]!["buildings"]!["stage_coach"]!["store"]![pool]!["generated"]!["894"]!["actor"]!["current_hp"]!.GetValue<double>() == test.Hp &&
                           SaveEditService.HasEquivalentStagecoachTownRoundTrip(mutation.UpdatedTown, restored, mutation.Preview),
                        $"{kind}/{test.Name}/{pool}: corrected HP and the entire town document must survive DSON.");
                    Assert(originals.SequenceEqual(new[] { town.ToJsonString(), roster.ToJsonString(), upgrades.ToJsonString(), candidate.ToJsonString() }) &&
                           JsonNode.DeepEquals(roster["base_root"]!["heroes"], mutation.UpdatedRoster["base_root"]!["heroes"]) &&
                           mutation.UpdatedRoster["base_root"]!["nextGuid"]!.GetValue<int>() == 895,
                        "Candidate insertion must preserve its inputs and existing roster while allocating the new GUID.");
                    roundTrips++;
                }
            }
        }
        WriteMultiMash(root, "results.json", JsonSerializer.Serialize(new { Cases = results, RoundTrips = roundTrips }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"PASS: initial HP known-state conditions, {results.Count} cases, {roundTrips} ordinary/shard DSON roundtrips, unknown and non-positive HP guards.");
    }
}
