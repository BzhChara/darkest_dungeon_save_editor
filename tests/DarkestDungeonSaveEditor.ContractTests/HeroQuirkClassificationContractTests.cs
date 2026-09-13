using System.Reflection;

internal static partial class ContractSuite
{
    private static async Task VerifyHeroQuirkClassificationAsync(string runRoot, DsonSaveCodec codec)
    {
        foreach (var kind in QuirkRuleSourceKinds)
        {
            var root = Path.Combine(runRoot, "quirk-classification", kind);
            var f = QuirkRuleContent(root, kind);
            File.WriteAllText(f.Quirks, """
                {"quirks":[
                  {"id":"p0","is_positive":true,"is_disease":false,"random_chance":1},
                  {"id":"pd","is_positive":true,"is_disease":true,"random_chance":0},
                  {"id":"n0","is_positive":false,"is_disease":false,"random_chance":1},
                  {"id":"d0","is_positive":false,"is_disease":true,"random_chance":0}]}
                """);
            var rulePath = WriteMultiMash(f.Source, f.Prefix + "shared/rules.json",
                """{"quirks_max_positive":1,"quirks_max_negative":1,"quirks_max_diseases":1}""");
            if (kind is "local" or "workshop" or "dlc-mod") WriteFixtureManifest(f.Source);
            var catalog = HeroClassCatalog.Load(f.Content);
            AssertQuirkRuleRejected(catalog, ["p0", "pd"], "最多");
            AssertQuirkRuleRejected(catalog, ["pd", "d0"], "最多");
            var generated = GenerateQuirkRuleHero(catalog, "pd", "n0");
            Assert(generated.Preview.PositiveQuirks.SequenceEqual(["pd"]) &&
                   generated.Preview.NegativeQuirks.SequenceEqual(["n0"]) &&
                   generated.Preview.Diseases.SequenceEqual(["pd"]) &&
                   generated.Candidate["quirks"]!.AsObject().Count == 2,
                $"{kind}: a positive disease counts toward both quotas but is serialized once.");
            Assert(catalog.InitialQuirks.Single(q => q.Id == "pd").Kind == HeroInitialQuirkKind.Disease,
                "Counting positive diseases must preserve the separate disease display category.");
            await VerifyProgressionCandidatePersistenceAsync(root, generated, codec);
            GenerateQuirkRuleHero(catalog, "p0", "n0", "d0");

            foreach (var (rules, reason) in new[]
            {
                ("""{"quirks_max_positive":0,"quirks_max_negative":1,"quirks_max_diseases":1}""", "最多"),
                ("""{"quirks_max_positive":"unknown","quirks_max_negative":1,"quirks_max_diseases":1}""", "上限无法确定")
            })
            {
                File.WriteAllText(rulePath, rules);
                catalog = HeroClassCatalog.Load(f.Content);
                AssertQuirkRuleRejected(catalog, ["pd"], reason);
                GenerateQuirkRuleHero(catalog, "n0", "d0");
            }
        }
        Console.WriteLine("PASS: positive disease quotas, preview lists, zero/unknown limits and single-record persistence across six sources.");
    }

    private static void VerifyHeroQuirkProbabilityPrecision(string runRoot)
    {
        var fingerprintMethod = typeof(SaveEditService).GetMethod("ComputeHeroCatalogSha256", BindingFlags.NonPublic | BindingFlags.Static)!;
        string Fingerprint(HeroClassCatalogResult catalog) => (string)fingerprintMethod.Invoke(null, [catalog])!;
        foreach (var kind in QuirkRuleSourceKinds)
        {
            var root = Path.Combine(runRoot, "quirk-probability", kind);
            var f = QuirkRuleContent(root, kind);
            WriteMultiMash(Path.Combine(root, "baseline"), "heroes/progression/progression.override.darkest",
                "combat_skill: .id attack .level 0 .effect \"GrantProbabilityQuirk\"\n");
            WriteMultiMash(f.Source, f.Prefix + "effects/probability.effects.darkest",
                "effect: .name \"GrantProbabilityQuirk\" .disease \"p0\"\n");
            if (kind is "local" or "workshop" or "dlc-mod") WriteFixtureManifest(f.Source);
            string? zeroFingerprint = null;
            foreach (var (field, expected) in new (string, double?)[]
            {
                ("\"random_chance\":0", 0), ("\"random_chance\":1e-50", 0),
                ("\"random_chance\":1e-30", (double)(float)1e-30), ("\"random_chance\":1", 1),
                ("\"random_chance\":0.2", (double)(float)0.2), ("\"random_chance\":-0.2", (double)(float)-0.2),
                ("\"random_chance\":1e40", null), ("\"random_chance\":-1e40", null),
                ("\"random_chance\":1e400", null), ("\"random_chance\":\"0\"", null),
                ("\"RANDOM_CHANCE\":1", null), ("\"random_chance\":1e-50,\"random_chance\":1", 0),
                ("\"random_chance\":\"invalid\",\"random_chance\":0", null)
            })
            {
                File.WriteAllText(f.Quirks, "{\"quirks\":[{\"id\":\"p0\",\"is_positive\":true,\"is_disease\":false," + field + "}]}");
                var catalog = HeroClassCatalog.Load(f.Content);
                var quirk = catalog.InitialQuirks.Single();
                var hero = catalog.HeroClasses.Single();
                var natural = expected is > 0;
                Assert(quirk.RandomChance == expected && quirk.IsNaturalRandomEligible == natural &&
                       quirk.Kind == (natural ? HeroInitialQuirkKind.Natural : HeroInitialQuirkKind.Special),
                    $"{kind}/{field}: use native float precision and retain first exact JSON member semantics.");
                Assert(hero.RuntimeQuirkSignals.Count == (expected is <= 0 ? 1 : 0) &&
                       hero.RuntimeQuirkSignals.All(s => s.QuirkId == "p0" && s.EffectName == "GrantProbabilityQuirk"),
                    $"{kind}/{field}: only resolved nonpositive probabilities contribute special-quirk skill signals.");
                var fingerprint = Fingerprint(catalog);
                if (field == "\"random_chance\":0") zeroFingerprint = fingerprint;
                if (field == "\"random_chance\":1e-50")
                    Assert(fingerprint == zeroFingerprint, "Native-equivalent probabilities produce the same save guard fingerprint.");
                // Manual selection does not persist or simulate the random weight.
                GenerateQuirkRuleHero(catalog, "p0");
            }
        }
        Console.WriteLine("PASS: 78 quirk probability, skill-signal, overflow/fingerprint and exact JSON-field cases across six sources.");
    }
}
