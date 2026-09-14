internal static partial class ContractSuite
{
    public static async Task RunHeroSelectionOnlyAsync(string repositoryRoot)
    {
        var fixture = BuildContractFixture(repositoryRoot);
        await VerifyHeroGenerationSelectionAsync(fixture.RunRoot, fixture.Codec);
        await VerifyHeroSkinDirectoriesAsync(fixture.RunRoot, fixture.Codec);
        Console.WriteLine($"Artifacts: {fixture.RunRoot}");
    }

    private sealed record SelectionFixture(string Root, string Source, string Prefix, bool IsMod, ActiveContentSnapshot Content);

    private static SelectionFixture HeroSelectionContent(string root, string kind, int skills = 3,
        int marked = 0, int target = 1, int classCamp = 0, int sharedCamp = 0)
    {
        var baseline = Path.Combine(root, "baseline");
        WriteMultiMash(baseline, "localization/a.string_table.xml",
            """<root><language id="english"><entry id="hero_name_0">Selection</entry></language></root>""");
        WriteMultiMash(baseline, "campaign/roster/roster.variables.json", RosterThresholds(10));
        var source = Path.Combine(root, "resource");
        Directory.CreateDirectory(source);
        var sources = new[] { new ActiveContentSource("base", "Base", "base", baseline, 0) }.Concat(QuerySources(source, kind)).ToArray();
        var f = new SelectionFixture(root, source, kind == "dlc-mod" ? "dlc/rq_feature/" : "",
            kind is "local" or "workshop" or "dlc-mod", QueryContent(root, sources));
        if (f.IsMod) File.WriteAllText(Path.Combine(source, "modfiles.txt"), "");
        var info = "weapon: .name blade\narmour: .name coat .hp 20\n" +
            string.Concat(Enumerable.Range(0, skills).Select(i =>
                $"combat_skill: .id s{i} .level 0 .generation_guaranteed {(i < marked ? "true" : "false")}\n")) +
            "skill_selection: .can_select_combat_skills true .number_of_selected_combat_skills_max 4\n" +
            $"generation: .is_generation_enabled true .number_of_random_combat_skills {target} " +
            $".number_of_class_specific_camping_skills {classCamp} .number_of_shared_camping_skills {sharedCamp}\n";
        // Keep the canonical hero template separate from the mounted skill/skin queries.
        SelectionResource(f with { Prefix = "" }, "heroes/selection/selection.info.darkest", info);
        return f;
    }

    private static string SelectionResource(SelectionFixture f, string relative, string text)
    {
        var path = WriteMultiMash(f.Source, f.Prefix + relative, text);
        if (f.IsMod) File.AppendAllText(Path.Combine(f.Source, "modfiles.txt"), f.Prefix + relative + "\n");
        return path;
    }

    private static GeneratedStagecoachHeroCandidate GenerateSelectionHero(HeroClassCatalogResult catalog, int seed = 0) =>
        StagecoachHeroCandidateFactory.Generate(catalog, catalog.HeroClasses.Single(), seed, 0, []);

    private static async Task VerifyHeroGenerationSelectionAsync(string runRoot, DsonSaveCodec codec)
    {
        foreach (var kind in QuirkRuleSourceKinds)
        foreach (var test in new[] { "none", "one", "two-target-one", "five-target-four", "two-target-two", "shortage", "selection-cap" })
        {
            var skills = test is "five-target-four" or "selection-cap" ? 6 : 3;
            var marked = test switch { "one" => 1, "five-target-four" => 5, "two-target-one" or "two-target-two" => 2, _ => 0 };
            var target = test switch { "two-target-two" => 2, "five-target-four" or "shortage" => 4, "selection-cap" => 6, _ => 1 };
            var f = HeroSelectionContent(Path.Combine(runRoot, "hero-selection", kind, test), kind, skills, marked, target);
            SelectionResource(f, "heroes/selection/selection_A/skin.png", "fixture");
            var catalog = HeroClassCatalog.Load(f.Content);
            Assert(catalog.Issues.Count == 0 && catalog.HeroClasses.Single().GenerationAvailability.All(level => level.CanGenerate),
                $"{kind}/{test}: all seven generation levels must be available.");
            var generated = Enumerable.Range(0, 512).Select(seed => GenerateSelectionHero(catalog, seed)).ToArray();
            var actualSets = generated.Select(g => string.Join(',', g.Preview.CombatSkills.Order(StringComparer.Ordinal))).ToHashSet();
            var count = Math.Min(Math.Min(target, 4), skills);
            // Acceptance oracle: enumerate valid subsets, independently of the
            // product's shuffle/replacement implementation and random seeds.
            var expectedSets = Enumerable.Range(0, 1 << skills)
                .Select(mask => Enumerable.Range(0, skills).Where(i => (mask & (1 << i)) != 0).ToArray())
                .Where(set => set.Length == count && (marked == 0 || set.Any(i => i < marked)))
                .Select(set => string.Join(',', set.Select(i => "s" + i))).ToHashSet();
            Assert(actualSets.SetEquals(expectedSets), $"{kind}/{test}: all and only eligible marked-skill subsets must be reachable.");
            Assert(generated.All(g => g.Preview.Warnings.Any(w => w.StartsWith("战斗技能要求", StringComparison.Ordinal)) == (test == "shortage")),
                "Skill-pool exhaustion warns with the actual count; the separate selection cap is preserved.");
            if (kind == "local") await VerifyHeroSelectionPersistenceAsync(f.Root, generated[0], codec);
        }
        Console.WriteLine("PASS: 42 combat generation cases, marked-skill subset coverage, shortages, selection caps and seven-level preflight.");

        foreach (var kind in QuirkRuleSourceKinds)
        foreach (var test in new[] { "within", "class-shortage", "shared-shortage", "class-empty" })
        {
            var classPool = test == "class-empty" ? 0 : 2;
            var classCount = test is "class-shortage" or "class-empty" ? 3 : 1;
            var sharedCount = test == "shared-shortage" ? 3 : 1;
            var f = HeroSelectionContent(Path.Combine(runRoot, "camp-selection", kind, test), kind,
                classCamp: classCount, sharedCamp: sharedCount);
            SelectionResource(f, "heroes/selection/selection_A/skin.png", "fixture");
            var definitions = new JsonArray(Enumerable.Range(0, classPool).Select(i => (JsonNode)new JsonObject
                { ["id"] = "camp" + i, ["hero_classes"] = new JsonArray("selection") }).ToArray());
            definitions.Add(new JsonObject { ["id"] = "shared", ["hero_classes"] = new JsonArray("selection", "selection") });
            SelectionResource(f, "raid/camping/a.camping_skills.json", new JsonObject
            {
                ["configuration"] = new JsonObject { ["class_specific_number_of_classes_threshold"] = 1 }, ["skills"] = definitions
            }.ToJsonString());
            var catalog = HeroClassCatalog.Load(f.Content);
            Assert(catalog.Issues.Count == 0 && catalog.HeroClasses.Single().GenerationAvailability.All(level => level.CanGenerate),
                $"{kind}/{test}: a small camping pool must not disable generation.");
            var generated = GenerateSelectionHero(catalog);
            Assert(generated.Preview.CampingSkills.Count == Math.Min(classCount, classPool) + 1 &&
                   generated.Preview.CampingSkills.Contains("shared") &&
                   generated.Preview.Warnings.Any(w => w.StartsWith("职业露营技能要求", StringComparison.Ordinal)) == (classCount > classPool) &&
                   generated.Preview.Warnings.Any(w => w.StartsWith("共享露营技能要求", StringComparison.Ordinal)) == (sharedCount > 1),
                $"{kind}/{test}: exhaust each camping pool independently without filling from the other pool.");
            var expectedPurchases = Enumerable.Range(0, classPool).Select(i => "selection.camp" + i).Append("selection.shared");
            Assert(expectedPurchases.All(id => generated.UpgradePurchases.Count(p => p.TreeId == id && p.RequirementCode == "0") == 1),
                "All available camping skills remain unlocked independently of equipped skills.");
            if (kind == "local") await VerifyHeroSelectionPersistenceAsync(f.Root, generated, codec);
        }
        Console.WriteLine("PASS: 24 independent camping-pool cases, shortage warnings and unchanged unlock policy.");

        var guardFixture = HeroSelectionContent(Path.Combine(runRoot, "hero-selection-guards"), "local");
        SelectionResource(guardFixture, "heroes/selection/selection_A/skin.png", "fixture");
        var guards = HeroClassCatalog.Load(guardFixture.Content);
        var hero = guards.HeroClasses.Single();
        foreach (var invalid in new[]
        {
            hero with { CombatSkillIds = [] }, hero with { GuaranteedCombatSkillIds = ["missing"] },
            hero with { SelectedCombatSkillsMax = 0 }, hero with { Generation = hero.Generation! with { RandomCombatSkills = 0 } },
            hero with { Generation = hero.Generation! with { ClassCampingSkills = -1 } },
            hero with { Generation = hero.Generation! with { SharedCampingSkills = -1 } }
        })
        {
            var rejected = false;
            try { StagecoachHeroCandidateFactory.Generate(guards, invalid, 0, 0, []); }
            catch (InvalidOperationException) { rejected = true; }
            Assert(rejected, "Missing capabilities, unknown IDs and invalid generation limits must still be rejected.");
        }
        var allSkills = StagecoachHeroCandidateFactory.Generate(guards,
            hero with { CanSelectCombatSkills = false, SelectedCombatSkillsMax = 1 }, 0, 0, []);
        Assert(allSkills.Preview.CombatSkills.SequenceEqual(hero.CombatSkillIds), "Non-selectable classes retain all skills.");
        var cappedShortage = StagecoachHeroCandidateFactory.Generate(guards, hero with
        {
            SelectedCombatSkillsMax = 2, Generation = hero.Generation! with { RandomCombatSkills = 4 }
        }, 0, 0, []);
        Assert(cappedShortage.Preview.CombatSkills.Count == 2 && cappedShortage.Preview.Warnings.Any(warning =>
                warning.Contains("战斗技能要求 4 个", StringComparison.Ordinal) && warning.Contains("仅有 3 个", StringComparison.Ordinal)),
            "A selection cap below the pool size must not hide a shortage in the generation request.");
        Console.WriteLine("PASS: six existing generation safeguards and non-selectable skill behavior retained.");
    }

    private static async Task VerifyHeroSelectionPersistenceAsync(string root, GeneratedStagecoachHeroCandidate generated, DsonSaveCodec codec)
    {
        var mutation = StagecoachHeroSaveEditor.AddCandidate(
            JsonNode.Parse("""{"base_root":{"buildings":{"stage_coach":{"store":{"hero_recruit":{"generated":{}}}}}}}""")!.AsObject(),
            JsonNode.Parse("""{"base_root":{"nextGuid":950,"heroes":{}}}""")!.AsObject(),
            JsonNode.Parse("""{"base_root":{"purchases":{}}}""")!.AsObject(), generated.Candidate, generated.UpgradePurchases);
        foreach (var (name, expected) in new[] { ("town", mutation.UpdatedTown), ("roster", mutation.UpdatedRoster), ("upgrades", mutation.UpdatedUpgrades) })
        {
            var restored = await RoundtripDirectoryQueryAsync(root, name, expected, codec);
            Assert(JsonNode.DeepEquals(expected, restored), "Selections and purchase records must survive complete DSON encoding/decoding.");
            if (name != "town") continue;
            var saved = restored["base_root"]!["buildings"]!["stage_coach"]!["store"]!["hero_recruit"]!["generated"]!["950"]!;
            Assert(saved["actor"]!["colour_variation"]!.GetValue<int>() == generated.Preview.ColourVariation,
                "Persisted colour index must match the preview.");
            foreach (var (field, preview) in new[] { ("selected_combat_skills", generated.Preview.CombatSkills), ("selected_camping_skills", generated.Preview.CampingSkills) })
                Assert(saved["skills"]![field]!.AsObject().Select(p => p.Key).SequenceEqual(preview) &&
                       saved["skills"]![field]!.AsObject().All(p => p.Value!.GetValue<int>() == 0),
                    "Persisted selected skill IDs must match preview and retain native zero map values.");
        }
    }
}
