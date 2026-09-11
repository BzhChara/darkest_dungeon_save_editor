internal static partial class ContractSuite
{
    private static ActiveContentSnapshot HeroProgressionQueryContent(string root, string kind)
    {
        var baseline = Path.Combine(root, "baseline");
        WriteMultiMash(baseline, "heroes/progression/progression.info.darkest",
            "weapon: .name blade0\nweapon: .name blade1 .upgradeRequirementCode 0\n" +
            "armour: .name coat0 .hp 20\narmour: .name coat1 .hp 40 .upgradeRequirementCode 0\n" +
            "combat_skill: .id attack .level 0\ncombat_skill: .id attack .level 1\n" +
            "generation: .is_generation_enabled true .number_of_positive_quirks_min 0 .number_of_positive_quirks_max 0 " +
            ".number_of_negative_quirks_min 0 .number_of_negative_quirks_max 0 .number_of_random_combat_skills 1 " +
            ".number_of_class_specific_camping_skills 0 .number_of_shared_camping_skills 0\n");
        WriteMultiMash(baseline, "heroes/progression/progression_A/skin.png", "fixture");
        WriteMultiMash(baseline, "localization/a.string_table.xml",
            """<root><language id="english"><entry id="hero_name_0">Progression Hero</entry></language></root>""");
        WriteMultiMash(baseline, "campaign/roster/roster.variables.json", RosterThresholds(10));
        WriteMultiMash(baseline, "upgrades/heroes/a.upgrades.json", """
            {"trees":[
              {"id":"progression.weapon","requirements":[{"code":"0","prerequisite_resolve_level":1}]},
              {"id":"progression.armour","requirements":[{"code":"0","prerequisite_resolve_level":1}]},
              {"id":"progression.attack","requirements":[{"code":"0","prerequisite_resolve_level":0},{"code":"1","prerequisite_resolve_level":1}]}]}
            """);
        var sources = new[] { new ActiveContentSource("base", "Base", "base", baseline, 0) }
            .Concat(QuerySources(Path.Combine(root, "overlay"), kind)).ToArray();
        return QueryContent(root, sources);
    }

    private static string RosterThresholds(int step) => new JsonObject
    {
        ["resolve_level_thresholds"] = new JsonArray(Enumerable.Range(0, 7).Select(i => (JsonNode)JsonValue.Create(i * step)!).ToArray())
    }.ToJsonString();

    private static GeneratedStagecoachHeroCandidate GenerateProgressionHero(HeroClassCatalogResult catalog, int level = 1) =>
        StagecoachHeroCandidateFactory.Generate(catalog, catalog.HeroClasses.Single(h => h.Id == "progression"), 1729, level, []);

    private static async Task VerifyProgressionCandidatePersistenceAsync(string root, GeneratedStagecoachHeroCandidate generated,
        DsonSaveCodec codec)
    {
        var mutation = StagecoachHeroSaveEditor.AddCandidate(
            JsonNode.Parse("""{"base_root":{"buildings":{"stage_coach":{"store":{"hero_recruit":{"generated":{}}}}}}}""")!.AsObject(),
            JsonNode.Parse("""{"base_root":{"nextGuid":950,"heroes":{}}}""")!.AsObject(),
            JsonNode.Parse("""{"base_root":{"purchases":{}}}""")!.AsObject(), generated.Candidate, generated.UpgradePurchases);
        foreach (var (name, expected) in new[] { ("town", mutation.UpdatedTown), ("roster", mutation.UpdatedRoster), ("upgrades", mutation.UpdatedUpgrades) })
        {
            var restored = await RoundtripDirectoryQueryAsync(root, name, expected, codec);
            Assert(JsonNode.DeepEquals(expected, restored), $"{root}/{name}: progression data must survive a complete DSON round trip.");
            if (name == "town")
            {
                var candidate = restored["base_root"]!["buildings"]!["stage_coach"]!["store"]!["hero_recruit"]!["generated"]!["950"]!;
                Assert(candidate["actor"]!["current_hp"]!.GetValue<double>() == generated.Preview.CurrentHp &&
                       candidate["resolveXp"]!.GetValue<int>() == generated.Preview.ResolveXp &&
                       candidate["armour_rank"]!.GetValue<int>() == generated.Preview.ArmourRank,
                    "Persisted HP, XP and armour rank must agree with the resolved progression preview.");
            }
            if (name == "upgrades")
            {
                var codes = restored["base_root"]!["purchases"]!.AsObject()
                    .Where(p => p.Value!["tree_id"]!.GetValue<int>() == unchecked((int)HashLoc2Key("progression.armour")))
                    .Select(p => p.Value!["requirement_code"]!.GetValue<string>());
                Assert(codes.SequenceEqual(generated.UpgradePurchases.Where(p => p.TreeId == "progression.armour").Select(p => p.RequirementCode)),
                    "The selected armour tree's purchase codes must be written to its native hash.");
            }
        }
    }

    private static async Task VerifyHeroProgressionResourcesAsync(string runRoot, DsonSaveCodec codec)
    {
        foreach (var kind in new[] { "base", "mode", "dlc-feature", "local", "workshop", "dlc-mod" })
        foreach (var variant in new[] { "standard", "upper-extension", "upper-kind", "first-dot", "last-dot", "physical-alias", "upper-directory", "both-aliases" })
        {
            var root = Path.Combine(runRoot, "hero-upgrade-query", kind, variant);
            var content = HeroProgressionQueryContent(root, kind);
            var source = Path.Combine(root, "overlay");
            var prefix = kind == "dlc-mod" ? "dlc/rq_feature/" : "";
            var isMod = kind is "local" or "workshop" or "dlc-mod";
            var suffix = variant switch
            {
                "upper-extension" or "physical-alias" or "both-aliases" => ".upgrades.JSON",
                "upper-kind" => ".UPGRADES.json", "first-dot" => "Xupgrades.json",
                "last-dot" => ".upgradesXjson", _ => ".upgrades.json"
            };
            var relative = prefix + (variant == "upper-directory" ? "Upgrades/" : "upgrades/") + "z" + suffix;
            var path = WriteMultiMash(source, relative,
                """{"trees":[{"id":"progression.armour","requirements":[{"code":"0","prerequisite_resolve_level":4}]}]}""");
            if (isMod)
            {
                var listed = relative.Replace(".upgrades.JSON", ".upgrades.json", StringComparison.Ordinal);
                File.WriteAllLines(Path.Combine(source, "modfiles.txt"), variant switch
                {
                    "physical-alias" => new[] { listed },
                    "both-aliases" => new[] { relative, listed },
                    _ => new[] { relative }
                });
                // A valid-looking but unlisted file must not overwrite the chosen tree.
                WriteMultiMash(source, prefix + "upgrades/zz-unlisted.upgrades.json", "invalid JSON");
            }
            var accepted = variant == "standard" || (isMod && variant is "physical-alias" or "both-aliases") ||
                (!isMod && variant == "upper-directory");
            var catalog = HeroClassCatalog.Load(content);
            var generated = GenerateProgressionHero(catalog);
            Assert(generated.Preview.ArmourRank == (accepted ? 0 : 1) && generated.Preview.CurrentHp == (accepted ? 20 : 40) &&
                   generated.UpgradePurchases.Any(p => p.TreeId == "progression.armour" && p.RequirementCode == "0") == !accepted,
                $"{kind}/{variant}: only native-query upgrade files may determine armour rank, HP and purchases.");
            var tree = catalog.HeroClasses.Single(h => h.Id == "progression").UpgradeTrees.Single(t => t.Id == "progression.armour");
            Assert(tree.SourcePath.Equals(path, StringComparison.OrdinalIgnoreCase) == accepted &&
                   catalog.HeroClasses.Single().GenerationAvailability.All(l => l.CanGenerate) && catalog.Issues.Count == 0,
                $"{kind}/{variant}: retain the actual winning source and ignore unlisted/ineligible definitions before parsing.");
            if (kind == "local" && variant is "standard" or "upper-extension" or "upper-kind" or "last-dot" or "physical-alias")
                await VerifyProgressionCandidatePersistenceAsync(root, generated, codec);
            if (isMod && variant == "upper-extension")
            {
                var before = ProfileCatalogContentFingerprint.Capture(content.Sources);
                File.WriteAllText(Path.Combine(source, "modfiles.txt"), relative.Replace(".upgrades.JSON", ".upgrades.json", StringComparison.Ordinal));
                Assert(before != ProfileCatalogContentFingerprint.Capture(content.Sources) &&
                       GenerateProgressionHero(HeroClassCatalog.Load(content)).Preview.CurrentHp == 20,
                    "A corrected manifest filename must refresh eligibility while opening the same physical file.");
            }
        }
        Console.WriteLine("PASS: 48 upgrade filename cases across six sources, manifest aliases/refresh and persisted armour, HP and purchases.");

        foreach (var kind in new[] { "base", "mode", "dlc-feature", "local", "workshop", "dlc-mod" })
        foreach (var variant in new[] { "canonical", "nested", "physical-alias" })
        {
            var root = Path.Combine(runRoot, "hero-roster-query", kind, variant);
            var content = HeroProgressionQueryContent(root, kind);
            var source = Path.Combine(root, "overlay");
            var prefix = kind == "dlc-mod" ? "dlc/rq_feature/" : "";
            var relative = prefix + (variant == "physical-alias" ? "Campaign/Roster/ROSTER.variables.JSON" :
                "campaign/roster/" + (variant == "nested" ? "notes/" : "") + "roster.variables.json");
            WriteMultiMash(source, relative, RosterThresholds(7));
            if (kind is "local" or "workshop" or "dlc-mod")
                File.WriteAllText(Path.Combine(source, "modfiles.txt"), variant == "physical-alias" ? relative.ToLowerInvariant() : relative);
            var catalog = HeroClassCatalog.Load(content);
            var step = variant == "nested" ? 10 : 7;
            Assert(catalog.ResolveLevelThresholds.SequenceEqual(Enumerable.Range(0, 7).Select(i => i * step)) &&
                   catalog.HeroClasses.Single().GenerationAvailability.All(l => l.CanGenerate) &&
                   catalog.Issues.All(i => variant == "physical-alias" && kind is "base" or "mode" or "dlc-feature" &&
                       i.StartsWith("Roster variables mount paths differ only in case; native matching is unverified:", StringComparison.Ordinal)),
                $"{kind}/{variant}: the mounted canonical roster file must supply all seven levels, without consuming nested namesakes. " +
                $"Thresholds: {string.Join(',', catalog.ResolveLevelThresholds)}; issues: {string.Join(" | ", catalog.Issues)}");
            Assert(GenerateProgressionHero(catalog).Preview.ResolveXp == step && GenerateProgressionHero(catalog, 6).Preview.ResolveXp == 6 * step,
                $"{kind}/{variant}: actual generated XP must come from the mounted effective thresholds.");
            if (variant == "canonical" && kind is "dlc-feature" or "dlc-mod")
                await VerifyProgressionCandidatePersistenceAsync(root, GenerateProgressionHero(catalog), codec);
        }
        VerifyRosterMountGuards(runRoot);
        Console.WriteLine("PASS: mounted roster XP across six sources, canonical aliases, disabled/unlisted mounts, root Mod priority and invalid thresholds.");
    }

    private static void VerifyRosterMountGuards(string runRoot)
    {
        var root = Path.Combine(runRoot, "hero-roster-guards");
        var content = HeroProgressionQueryContent(root, "dlc-mod");
        var source = Path.Combine(root, "overlay");
        const string relative = "dlc/rq_feature/campaign/roster/roster.variables.json";
        var path = WriteMultiMash(source, relative, RosterThresholds(7));
        // Keep a nonempty manifest with no eligible roster file.
        WriteMultiMash(source, "README.txt", "fixture");
        File.WriteAllText(Path.Combine(source, "modfiles.txt"), "README.txt");
        Assert(GenerateProgressionHero(HeroClassCatalog.Load(content)).Preview.ResolveXp == 10,
            "An unlisted DLC roster override must not replace the baseline.");
        File.WriteAllText(Path.Combine(source, "modfiles.txt"), relative);
        var disabled = content with { Sources = content.Sources.Where(s => s.Kind != "dlc-feature").ToArray() };
        Assert(GenerateProgressionHero(HeroClassCatalog.Load(disabled)).Preview.ResolveXp == 10,
            "A listed path in a disabled DLC mount must not replace the baseline.");
        var rootMod = Path.Combine(root, "root-mod");
        WriteMultiMash(rootMod, "campaign/roster/roster.variables.json", RosterThresholds(3));
        WriteFixtureManifest(rootMod);
        var overridden = content with { Sources = content.Sources.Append(new ActiveContentSource("local:root", "Root", "local", rootMod, -1000)).ToArray() };
        Assert(GenerateProgressionHero(HeroClassCatalog.Load(overridden)).Preview.ResolveXp == 3,
            "A root Mod must retain priority over the mounted DLC-relative roster alias.");
        foreach (var json in new[] { "{}", """{"resolve_level_thresholds":[0,7,7]}""", """{"resolve_level_thresholds":[0,1.5]}""", "invalid JSON" })
        {
            File.WriteAllText(path, json);
            var catalog = HeroClassCatalog.Load(content);
            Assert(catalog.ResolveLevelThresholds.Count == 0 && catalog.Issues.Any(i => i.Contains(path, StringComparison.OrdinalIgnoreCase)) &&
                   catalog.Issues.All(i => !i.StartsWith("No effective campaign/roster/roster.variables.json", StringComparison.Ordinal)),
                "An invalid winning DLC roster file must retain its diagnostic source and cannot fall back to a valid baseline.");
            Assert(catalog.HeroClasses.Single().GenerationAvailability.Single().ResolveLevel == 0,
                "Invalid thresholds must retain the existing level-zero-only boundary.");
        }
        File.WriteAllText(path, RosterThresholds(7));
        Assert(GenerateProgressionHero(HeroClassCatalog.Load(content)).Preview.ResolveXp == 7,
            "Repairing the same mounted file must restore progression on reload.");
    }
}
