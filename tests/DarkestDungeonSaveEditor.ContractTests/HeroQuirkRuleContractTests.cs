internal static partial class ContractSuite
{
    public static async Task RunQuirkRulesOnlyAsync(string repositoryRoot)
    {
        var fixture = BuildContractFixture(repositoryRoot);
        await VerifyHeroQuirkClassificationAsync(fixture.RunRoot, fixture.Codec);
        VerifyHeroQuirkProbabilityPrecision(fixture.RunRoot);
        await VerifyHeroQuirkClassificationGuardsAsync(fixture.RunRoot, fixture.Codec);
        await VerifyHeroQuirkRulesAsync(fixture.RunRoot, fixture.Codec);
        await VerifyHeroQuirkEvolutionDefaultsAsync(fixture.RunRoot, fixture.Codec);
        await VerifyHeroQuirkRuleGuardsAsync(fixture.RunRoot, fixture.Codec);
        Console.WriteLine($"Artifacts: {fixture.RunRoot}");
    }

    private static readonly string[] QuirkRuleSourceKinds = ["base", "mode", "dlc-feature", "local", "workshop", "dlc-mod"];

    private static (ActiveContentSnapshot Content, string Source, string Prefix, string Quirks) QuirkRuleContent(
        string root, string kind)
    {
        var content = HeroProgressionQueryContent(root, kind);
        var source = Path.Combine(root, "overlay");
        var prefix = kind == "dlc-mod" ? "dlc/rq_feature/" : "";
        var quirks = new JsonArray();
        foreach (var category in new[] { "p", "n", "d" })
        for (var i = 0; i < 7; i++)
            quirks.Add(new JsonObject { ["id"] = category + i, ["is_positive"] = category == "p", ["is_disease"] = category == "d" });
        var path = WriteMultiMash(source, prefix + "shared/quirk/a.quirk_library.json", new JsonObject { ["quirks"] = quirks }.ToJsonString());
        if (kind is "local" or "workshop" or "dlc-mod") WriteFixtureManifest(source);
        return (content, source, prefix, path);
    }

    private static GeneratedStagecoachHeroCandidate GenerateQuirkRuleHero(HeroClassCatalogResult catalog, params string[] ids) =>
        StagecoachHeroCandidateFactory.Generate(catalog, catalog.HeroClasses.Single(h => h.Id == "progression"), 1729, 0, ids);

    private static void AssertQuirkRuleRejected(HeroClassCatalogResult catalog, string[] ids, string reason)
    {
        var hero = catalog.HeroClasses.Single(h => h.Id == "progression");
        foreach (var validateOnly in new[] { true, false })
        {
            InvalidOperationException? failure = null;
            try
            {
                if (validateOnly) StagecoachHeroCandidateFactory.ValidateInitialQuirkSelection(catalog, hero, 0, ids);
                else GenerateQuirkRuleHero(catalog, ids);
            }
            catch (InvalidOperationException error) { failure = error; }
            Assert(failure is not null && failure.Message.Contains(reason, StringComparison.Ordinal),
                $"Selection and generation reject '{string.Join(',', ids)}' with '{reason}', actual: {failure?.Message}");
        }
    }

    private static async Task VerifyHeroQuirkRulesAsync(string runRoot, DsonSaveCodec codec)
    {
        foreach (var kind in QuirkRuleSourceKinds)
        {
            var root = Path.Combine(runRoot, "quirk-rule-values", kind);
            var f = QuirkRuleContent(root, kind);
            var rules = WriteMultiMash(f.Source, f.Prefix + "shared/rules.json",
                """{"quirks_max_positive":7,"quirks_max_negative":7,"quirks_max_diseases":5}""");
            if (kind is "local" or "workshop" or "dlc-mod") WriteFixtureManifest(f.Source);
            var catalog = HeroClassCatalog.Load(f.Content);
            Assert(catalog.InitialQuirkLimits == new HeroInitialQuirkLimits(7, 7, 5), $"{kind}: read all active limits.");
            var ids = Enumerable.Range(0, 6).Select(i => "p" + i).Concat(Enumerable.Range(0, 6).Select(i => "n" + i))
                .Concat(Enumerable.Range(0, 4).Select(i => "d" + i)).ToArray();
            var generated = GenerateQuirkRuleHero(catalog, ids);
            Assert(generated.Candidate["quirks"]!.AsObject().Count == 16, "Raised limits retain every selected quirk.");
            await VerifyProgressionCandidatePersistenceAsync(root, generated, codec);

            File.WriteAllText(rules, """{"quirks_max_positive":1,"quirks_max_negative":1,"quirks_max_diseases":1}""");
            catalog = HeroClassCatalog.Load(f.Content);
            foreach (var category in new[] { "p", "n", "d" }) AssertQuirkRuleRejected(catalog, [category + "0", category + "1"], "最多");
            GenerateQuirkRuleHero(catalog, "p0", "n0", "d0");
            File.WriteAllText(rules, """{"quirks_max_positive":0,"quirks_max_negative":0,"quirks_max_diseases":0}""");
            catalog = HeroClassCatalog.Load(f.Content);
            GenerateQuirkRuleHero(catalog);
            foreach (var id in new[] { "p0", "n0", "d0" }) AssertQuirkRuleRejected(catalog, [id], "最多");

            File.WriteAllText(rules, "{}");
            Assert(HeroClassCatalog.Load(f.Content).InitialQuirkLimits == HeroInitialQuirkLimits.Default,
                "Omitted rules use the verified native initialization, not a missing-resource error.");
            foreach (var (json, expected) in new (string, HeroInitialQuirkLimits)[]
            {
                ("""{"quirks_max_positive":2,"quirks_max_positive":9,"QUIRKS_MAX_NEGATIVE":1}""", new(2,5,3)),
                ("""{"quirks_max_positive":"2","quirks_max_positive":9}""", new(null,5,3)),
                ("""{"quirks_max_positive":2147483647}""", new(int.MaxValue,5,3)),
                ("""{"quirks_max_negative":-1}""", new(5,null,3)),
                ("""{"quirks_max_diseases":1.5}""", new(5,5,null)),
                ("{ broken", new(null,null,null)), ("[]", new(null,null,null))
            })
            {
                File.WriteAllText(rules, json);
                catalog = HeroClassCatalog.Load(f.Content);
                Assert(catalog.InitialQuirkLimits == expected, $"{kind}: first exact fields, defaults and invalid effective values: {json}");
                GenerateQuirkRuleHero(catalog);
                foreach (var (category, limit) in new[] { ("p", expected.Positive), ("n", expected.Negative), ("d", expected.Diseases) })
                    if (limit is null) AssertQuirkRuleRejected(catalog, [category + "0"], "上限无法确定");
                    else GenerateQuirkRuleHero(catalog, category + "0");
            }
        }

        foreach (var kind in QuirkRuleSourceKinds)
        foreach (var variant in new[] { "root", "nested", "wildcard-dot", "upper-name", "upper-extension", "upper-directory",
                     "wrong-root", "network", "notes", "template", "dot-name", "non-ascii", "disabled-dlc", "unlisted", "physical-alias" })
        {
            var root = Path.Combine(runRoot, "quirk-rule-query", kind, variant);
            var f = QuirkRuleContent(root, kind);
            var mod = kind is "local" or "workshop" or "dlc-mod";
            var relative = variant switch
            {
                "nested" => "shared/nested/custom.rules.json", "wildcard-dot" => "shared/plainrulesXjson",
                "upper-name" => "shared/RULES.json", "upper-extension" => "shared/rules.JSON",
                "upper-directory" => "Shared/rules.json", "wrong-root" => "heroes/rules.json",
                "network" => "shared/rules.network.json", "notes" => "shared/readme.json",
                "template" => "shared/_template/rules.json", "dot-name" => "shared/.notes.rules.json",
                "non-ascii" => "shared/备注/rules.json", "disabled-dlc" => "dlc/disabled/shared/rules.json",
                "physical-alias" => "Shared/rules.JSON", _ => "shared/rules.json"
            };
            WriteMultiMash(f.Source, f.Prefix + relative, """{"quirks_max_positive":8}""");
            if (mod && variant != "unlisted")
            {
                // Keep raw manifest spelling independent of Windows' existing
                // lowercase shared directory created for the quirk library.
                File.AppendAllText(Path.Combine(f.Source, "modfiles.txt"), f.Prefix + relative + "\n");
                if (variant == "physical-alias")
                    File.AppendAllText(Path.Combine(f.Source, "modfiles.txt"), f.Prefix + "shared/rules.json\n");
            }
            var accepted = variant switch
            {
                "root" or "nested" or "wildcard-dot" => true,
                "upper-directory" => !mod,
                "template" or "dot-name" or "non-ascii" or "physical-alias" => mod,
                "unlisted" => !mod, _ => false
            };
            var catalog = HeroClassCatalog.Load(f.Content);
            Assert(catalog.InitialQuirkLimits.Positive == (accepted ? 8 : 5), $"{kind}/{variant}: native shared-rule eligibility.");
        }

        var overlayRoot = Path.Combine(runRoot, "quirk-rule-overlay");
        var overlayFixture = QuirkRuleContent(overlayRoot, "local");
        WriteMultiMash(Path.Combine(overlayRoot, "baseline"), "shared/a.rules.json", """{"quirks_max_positive":2,"quirks_max_negative":2}""");
        var a = WriteMultiMash(overlayFixture.Source, "shared/a.rules.json", """{"quirks_max_positive":7}""");
        var z = WriteMultiMash(overlayFixture.Source, "shared/z.rules.json", """{"quirks_max_positive":9}""");
        WriteFixtureManifest(overlayFixture.Source);
        Assert(HeroClassCatalog.Load(overlayFixture.Content).InitialQuirkLimits == new HeroInitialQuirkLimits(9,5,3),
            "Same-path replacement is whole-file; independent later files update only present fields.");
        var top = Path.Combine(overlayRoot, "top");
        WriteMultiMash(top, "shared/a.rules.json", """{"quirks_max_positive":11}""");
        WriteFixtureManifest(top);
        var withTop = overlayFixture.Content with { Sources = overlayFixture.Content.Sources.Append(new ActiveContentSource("top", "Top", "local", top, -1000)).ToArray() };
        Assert(HeroClassCatalog.Load(withTop).InitialQuirkLimits.Positive == 9,
            "Replacing a.rules keeps its original slot before z.rules, despite higher Mod priority.");
        File.WriteAllText(a, """{"quirks_max_positive":"invalid"}""");
        File.WriteAllText(z, """{"quirks_max_negative":6}""");
        Assert(HeroClassCatalog.Load(overlayFixture.Content).InitialQuirkLimits == new HeroInitialQuirkLimits(null,6,3),
            "Unrelated later fields cannot restore an unknown positive limit.");
        File.WriteAllText(z, """{"quirks_max_positive":8}""");
        Assert(HeroClassCatalog.Load(overlayFixture.Content).InitialQuirkLimits == new HeroInitialQuirkLimits(8,5,3),
            "An explicit later value can resolve an earlier invalid field.");
        File.Delete(a);
        File.WriteAllText(z, "{}");
        var missing = HeroClassCatalog.Load(overlayFixture.Content);
        Assert(missing.InitialQuirkLimits == new HeroInitialQuirkLimits(null,null,null) && missing.Issues.Any(i => i.Contains(a)),
            "A listed missing winning file must not silently uncover its valid baseline.");

        Console.WriteLine("PASS: shared quirk limits, six-source queries, whole-file overlays, per-field order and invalid winners.");
    }
}
