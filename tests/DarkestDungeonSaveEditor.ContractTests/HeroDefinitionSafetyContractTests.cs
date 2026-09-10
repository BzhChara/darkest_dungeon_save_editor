internal static partial class ContractSuite
{
    private static void VerifyHeroDefinitionSafetyContracts(ActiveContentSnapshot content,
        HeroClassDefinition originalHero, string runRoot)
    {
        var root = Path.Combine(runRoot, "hero-definition-safety");
        var heroRoot = Path.Combine(root, "heroes", originalHero.Id);
        var effectRoot = Path.Combine(root, "effects");
        var quirkRoot = Path.Combine(root, "shared", "quirk");
        Directory.CreateDirectory(heroRoot);
        Directory.CreateDirectory(effectRoot);
        Directory.CreateDirectory(quirkRoot);
        File.WriteAllText(Path.Combine(heroRoot, "local_hero.info.darkest"),
            File.ReadAllText(originalHero.SourcePath) + "\n" + """
            armour: .name "local_hero_armour_0" .hp 33 // old .hp 999
            combat_skill: .id "local_skill" .level 0 .effect ".Grant // \"Quoted\"" // .effect "Missing"
            """);
        File.WriteAllText(Path.Combine(effectRoot, "safety.effects.darkest"), """
            effect: .name ".Grant // \"Quoted\"" .target "performer" .chance 100% .disease "priority_top_quirk" // .disease "missing_quirk"
            """);
        var quirks = new JsonArray();
        static JsonObject Evolution(string id, string? target, bool death = false) => new()
        {
            ["id"] = id, ["random_chance"] = 1, ["is_positive"] = true, ["is_disease"] = false,
            ["buffs"] = new JsonArray(), ["evolution_duration_min"] = 3, ["evolution_duration_max"] = 3,
            ["evolution_class_id"] = target, ["evolution_causes_death"] = death
        };
        quirks.Add(Evolution("missing_direct", "missing_terminal"));
        quirks.Add(Evolution("missing_chain", "missing_direct"));
        quirks.Add(Evolution("conflicting_target", "ambiguous_quirk"));
        quirks.Add(Evolution("broken_chain", "evolution_inverted"));
        quirks.Add(Evolution("cycle_a", "cycle_b"));
        quirks.Add(Evolution("cycle_b", "cycle_a"));
        var deathOnly = Evolution("death_only", null, death: true);
        deathOnly.Remove("evolution_class_id");
        quirks.Add(deathOnly);
        File.WriteAllText(Path.Combine(quirkRoot, "safety.quirk_library.json"), new JsonObject { ["quirks"] = quirks }.ToJsonString());
        WriteFixtureManifest(root);
        var safetyContent = content with
        {
            Sources = content.Sources.Append(new ActiveContentSource("local:definition-safety", "Safety", "local", root, 900)).ToArray()
        };
        var catalog = HeroClassCatalog.Load(safetyContent);
        var hero = catalog.HeroClasses.Single(row => row.Id == originalHero.Id);
        var candidate = StagecoachHeroCandidateFactory.Generate(catalog, hero, seed: 1729);
        Assert(hero.BaseHp == 33 && candidate.Preview.CurrentHp == 33 &&
               !hero.RuntimeQuirkSignals.Any(signal => signal.EffectName.StartsWith(".Grant", StringComparison.Ordinal)),
            "Slash comments are stripped before native strings; a quoted dot-prefixed effect is not a runtime quirk signal.");
        foreach (var id in new[] { "missing_direct", "missing_chain", "broken_chain" })
        {
            var quirk = catalog.InitialQuirks.Single(row => row.Id == id);
            Assert(quirk.WriteStatus == HeroInitialQuirkWriteStatus.Unverified && !quirk.IsNaturalRandomEligible &&
                   quirk.WriteStatusReason.Contains("进化", StringComparison.Ordinal),
                "Every unresolved or invalid downstream evolution target must exclude the source from direct and random candidate generation.");
            AssertHeroGenerationRejected(catalog, hero, [id], "当前不能显式写入");
        }
        foreach (var id in new[] { "cycle_a", "cycle_b", "death_only", "conflicting_target" })
        {
            var quirk = catalog.InitialQuirks.Single(row => row.Id == id);
            Assert(quirk.WriteStatus == HeroInitialQuirkWriteStatus.Direct,
                $"A resolved cycle/death-only evolution must remain supported ({id}): {quirk.WriteStatusReason}");
            _ = StagecoachHeroCandidateFactory.Generate(catalog, hero, seed: 1729, selectedInitialQuirkIds: [id]);
        }
        File.WriteAllText(Path.Combine(heroRoot, "local_hero.override.darkest"),
            "armour: .name \"local_hero_armour_0\" .hp 37 // old .hp 777\n");
        WriteFixtureManifest(root);
        var overrideCatalog = HeroClassCatalog.Load(safetyContent);
        var overrideHero = overrideCatalog.HeroClasses.Single(row => row.Id == originalHero.Id);
        Assert(overrideHero.BaseHp == 37 && StagecoachHeroCandidateFactory.Generate(overrideCatalog, overrideHero, seed: 1729).Preview.CurrentHp == 37,
            "The same comment handling must apply to active hero override files and serialized candidate HP.");
        Console.WriteLine("PASS: hero/effect/override tokenization and complete quirk evolution target validation.");
    }
}
