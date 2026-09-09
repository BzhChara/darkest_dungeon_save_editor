internal static partial class ContractSuite
{
    private static async Task VerifyHeroUpgradeTreeResolutionAsync(
        ActiveContentSnapshot content, DsonSaveCodec codec, string runRoot)
    {
        var root = Path.Combine(runRoot, "hero-upgrade-tree-resolution");
        var lowerRoot = Path.Combine(root, "lower");
        var upperRoot = Path.Combine(root, "upper");
        const string heroId = "upgrade_rule_hero";
        const string otherId = "upgrade_other_hero";
        const string alpha = heroId + ".alpha";
        const string priority = heroId + ".priority";
        static JsonObject Tree(string id, params (string Code, int Level)[] schedule) => new()
        {
            ["id"] = id,
            ["requirements"] = new JsonArray(schedule.Select(item => (JsonNode)new JsonObject
            {
                ["code"] = item.Code, ["prerequisite_resolve_level"] = item.Level
            }).ToArray())
        };
        static void WriteTrees(string path, params JsonObject[] trees)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, new JsonObject
            {
                ["trees"] = new JsonArray(trees.Select(tree => tree.DeepClone()).ToArray())
            }.ToJsonString());
        }
        static void WriteHero(string modRoot, string id, params string[] skills)
        {
            var directory = Path.Combine(modRoot, "heroes", id);
            Directory.CreateDirectory(Path.Combine(directory, id + "_A"));
            File.WriteAllBytes(Path.Combine(directory, id + "_A", "skin.png"), [1]);
            File.WriteAllText(Path.Combine(directory, id + ".info.darkest"), $$"""
                weapon: .name "{{id}}_weapon_0"
                weapon: .name "{{id}}_weapon_1" .upgradeRequirementCode 0
                armour: .name "{{id}}_armour_0" .hp 20
                armour: .name "{{id}}_armour_1" .hp 30 .upgradeRequirementCode 0
                generation: .is_generation_enabled true .number_of_positive_quirks_min 0 .number_of_positive_quirks_max 0 .number_of_negative_quirks_min 0 .number_of_negative_quirks_max 0 .number_of_random_combat_skills 1
                """ + "\n" + string.Join("\n", skills.SelectMany(skill => new[]
                {
                    $"combat_skill: .id {skill} .level 0", $"combat_skill: .id {skill} .level 1"
                })));
        }
        WriteHero(lowerRoot, heroId, "alpha", "beta", "priority");
        WriteHero(lowerRoot, otherId, "alpha");
        WriteTrees(Path.Combine(lowerRoot, "upgrades", "heroes", "unrelated-filename.upgrades.json"),
            Tree(heroId + ".weapon", ("0", 1)), Tree(heroId + ".armour", ("0", 1)),
            Tree(alpha, ("0", 0), ("1", 1)), Tree(heroId + ".beta", ("0", 0), ("1", 1)),
            Tree(otherId + ".weapon", ("0", 1)), Tree(otherId + ".armour", ("0", 1)),
            Tree(otherId + ".alpha", ("0", 0), ("1", 1)));
        var sharedRelative = Path.Combine("upgrades", "heroes", "shared.upgrades.json");
        WriteTrees(Path.Combine(lowerRoot, sharedRelative), Tree(priority, ("0", 0), ("1", 4)));
        WriteTrees(Path.Combine(upperRoot, sharedRelative), Tree(priority, ("0", 0), ("1", 0)));
        var lowerLater = Path.Combine(lowerRoot, "upgrades", "zz-later.upgrades.json");
        WriteTrees(lowerLater, Tree(priority, ("0", 0), ("1", 3)));
        var partialPath = Path.Combine(upperRoot, "upgrades", "custom", "deep", "mixed.upgrades.json");
        WriteTrees(partialPath, Tree(alpha, ("a", 0), ("A", 2)),
            Tree(otherId + ".alpha", ("0", 0), ("1", 3)),
            Tree(heroId + ".ALPHA", ("x", 6)), Tree(heroId + ".unused", ("0", 0)));
        var unlistedPath = Path.Combine(upperRoot, "upgrades", "zzz-unlisted.upgrades.json");
        var testContent = content with
        {
            Sources = content.Sources.Concat(new[]
            {
                new ActiveContentSource("local:upgrade-lower", "Lower", "local", lowerRoot, 900),
                new ActiveContentSource("local:upgrade-upper", "Upper", "local", upperRoot, 800)
            }).ToArray()
        };
        HeroClassCatalogResult Load()
        {
            WriteFixtureManifest(lowerRoot);
            WriteFixtureManifest(upperRoot);
            return HeroClassCatalog.Load(testContent);
        }
        static HeroClassDefinition Hero(HeroClassCatalogResult catalog, string id = heroId) =>
            catalog.HeroClasses.Single(hero => hero.Id == id);
        static HeroUpgradeTreeDefinition Upgrade(HeroClassCatalogResult catalog, string id, string hero = heroId) =>
            Hero(catalog, hero).UpgradeTrees.Single(tree => tree.Id == id);
        static GeneratedStagecoachHeroCandidate Generate(HeroClassCatalogResult catalog, int level, string id = heroId) =>
            StagecoachHeroCandidateFactory.Generate(catalog, Hero(catalog, id), 1729, level, []);
        static string[] Codes(GeneratedStagecoachHeroCandidate candidate, string id) => candidate.UpgradePurchases
            .Where(purchase => purchase.TreeId == id).Select(purchase => purchase.RequirementCode).ToArray();

        var catalog = Load();
        Assert(Upgrade(catalog, alpha).Requirements.Select(item => item.Code).SequenceEqual(["a", "A"]) &&
               Upgrade(catalog, alpha).SourcePath == partialPath && Upgrade(catalog, alpha).Source == "local:upgrade-upper" &&
               Hero(catalog).UpgradeTrees.Count == 5,
            "Arbitrary nested filenames must contribute only referenced exact tree IDs with their winning source.");
        Assert(Codes(Generate(catalog, 1), alpha).SequenceEqual(["a"]) &&
               Codes(Generate(catalog, 2), alpha).SequenceEqual(["A", "a"]) &&
               Codes(Generate(catalog, 1), heroId + ".beta").SequenceEqual(["0", "1"]) &&
               Generate(catalog, 1).Preview.ArmourRank == 1 && Generate(catalog, 1).Preview.CurrentHp == 30 &&
               Codes(Generate(catalog, 2, otherId), otherId + ".alpha").SequenceEqual(["0"]) &&
               Codes(Generate(catalog, 3, otherId), otherId + ".alpha").SequenceEqual(["0", "1"]),
            "Partial mixed-class tree replacements must retain untouched equipment/skills and drive each class's purchases.");
        Assert(Upgrade(catalog, priority).SourcePath == lowerLater &&
               Codes(Generate(catalog, 2), priority).SequenceEqual(["0"]),
            "A top Mod's same-path replacement keeps its earlier enumeration slot; a later different file still wins by tree ID.");
        // An unlisted physical definition must never enter the native file sequence.
        WriteTrees(unlistedPath, Tree(alpha, ("x", 0)));
        Assert(Upgrade(HeroClassCatalog.Load(testContent), alpha).SourcePath == partialPath,
            "Manifest gating must apply to arbitrary upgrade paths.");
        File.Delete(unlistedPath);
        File.Delete(lowerLater);
        catalog = Load();
        Assert(Upgrade(catalog, priority).SourcePath == Path.Combine(upperRoot, sharedRelative) &&
               Codes(Generate(catalog, 0), priority).SequenceEqual(["0", "1"]),
            "Without the later independent row, the upper Mod must win the same-path tree definition.");

        var laterPath = Path.Combine(upperRoot, "upgrades", "zz-last.upgrades.json");
        WriteTrees(laterPath, Tree(alpha, ("bad", 0)), Tree(alpha, ("b", 0)),
            Tree(alpha, ("a", 0), ("A", 2)), Tree(otherId + ".alpha", ("0", 0), ("1", 4)),
            Tree(" " + alpha, ("x", 0)), Tree(alpha + " ", ("y", 0)), Tree("\t" + alpha + "\n", ("z", 0)));
        catalog = Load();
        Assert(Upgrade(catalog, alpha).Requirements.Select(item => item.Code).SequenceEqual(["a", "A"]) &&
               Hero(catalog).UpgradeTrees.Count == 5,
            "Whitespace in raw tree IDs must remain significant; distinct unreferenced IDs cannot overwrite the hero's exact tree.");
        Assert(Upgrade(catalog, alpha).SourcePath == laterPath &&
               Upgrade(catalog, alpha).Requirements.Select(item => item.Code).SequenceEqual(["a", "A"]) &&
               Codes(Generate(catalog, 3, otherId), otherId + ".alpha").SequenceEqual(["0"]),
            "Same-Mod files and same-file duplicate trees must use the final full tree without merging old codes or rejecting healthy following rows.");
        var candidate = Generate(catalog, 2);
        var town = JsonNode.Parse("""{"base_root":{"buildings":{"stage_coach":{"store":{"hero_recruit":{"generated":{}}}}}}}""")!.AsObject();
        var roster = JsonNode.Parse("""{"base_root":{"nextGuid":894,"heroes":{}}}""")!.AsObject();
        var purchases = JsonNode.Parse("""{"base_root":{"purchases":{}}}""")!.AsObject();
        var mutation = StagecoachHeroSaveEditor.AddCandidate(town, roster, purchases, candidate.Candidate, candidate.UpgradePurchases);
        var written = mutation.UpdatedUpgrades["base_root"]!["purchases"]!.AsObject().Select(pair => pair.Value!.AsObject())
            .Where(item => item["tree_id"]!.GetValue<int>() == unchecked((int)HashLoc2Key(alpha))).ToArray();
        Assert(written.Select(item => item["requirement_code"]!.GetValue<string>()).SequenceEqual(["A", "a"]) &&
               written.All(item => item["instance_number"]!.GetValue<int>() == 894),
            "Winning case-sensitive purchase codes must be written to the correct tree hash and new hero GUID.");
        var decoded = Path.Combine(root, "persist.upgrades.decoded.json");
        var encoded = Path.Combine(root, "persist.upgrades.json");
        var roundtrip = Path.Combine(root, "persist.upgrades.roundtrip.json");
        await File.WriteAllTextAsync(decoded, mutation.UpdatedUpgrades.ToJsonString());
        await codec.EncodeAsync(decoded, encoded, null);
        await codec.DecodeAsync(encoded, roundtrip);
        Assert(JsonNode.DeepEquals(mutation.UpdatedUpgrades, JsonNode.Parse(await File.ReadAllTextAsync(roundtrip))),
            "Resolved multi-file upgrade purchases must survive actual DSON encode/decode.");

        foreach (var malformed in new JsonObject[]
                 {
                     Tree(alpha, ("too_long", 0)),
                     Tree(alpha, (" a ", 0)),
                     new() { ["id"] = alpha },
                     new() { ["id"] = alpha, ["requirements"] = new JsonArray(JsonValue.Create(7)) },
                     Tree(alpha, ("0", 0), ("0", 2))
                 })
        {
            WriteTrees(laterPath, malformed, Tree(otherId + ".alpha", ("0", 0), ("1", 2)));
            catalog = Load();
            Assert(Upgrade(catalog, alpha).UnsupportedReason.Length > 0 &&
                   Upgrade(catalog, alpha).SourcePath == laterPath &&
                   Hero(catalog).GenerationAvailability.All(level => !level.CanGenerate) &&
                   Hero(catalog, otherId).GenerationAvailability.All(level => level.CanGenerate),
                "An invalid winning tree must block its own hero, retain provenance, and not fall back or poison neighboring classes.");
            var rejected = false;
            try { _ = Generate(catalog, 0); }
            catch (InvalidOperationException ex) when (ex.Message.Contains(alpha, StringComparison.Ordinal)) { rejected = true; }
            Assert(rejected, "Actual generation must reject the same invalid tree as catalog preflight.");
        }
        WriteTrees(laterPath, Tree(alpha, ("a", 0), ("A", 2)));
        catalog = Load();
        Assert(Hero(catalog).GenerationAvailability.All(level => level.CanGenerate),
            "Fixing the winning file must clear the prior failure on the next catalog load.");

        const string collisionHero = "upgrade_collision_hero";
        WriteHero(lowerRoot, collisionHero, "tree_1e");
        var collisionId = collisionHero + ".tree_1e";
        var collidingId = collisionHero + ".tree_20";
        Assert(HashLoc2Key(collisionId) == HashLoc2Key(collidingId), "Upgrade collision fixture is invalid.");
        WriteTrees(Path.Combine(upperRoot, "upgrades", "hash.upgrades.json"),
            Tree(collisionHero + ".weapon", ("0", 1)), Tree(collisionHero + ".armour", ("0", 1)),
            Tree(collisionId, ("0", 0)), Tree(collidingId, ("0", 0)));
        catalog = Load();
        Assert(Hero(catalog, collisionHero).GenerationAvailability.All(level => !level.CanGenerate) &&
               Upgrade(catalog, collisionId, collisionHero).UnsupportedReason.Contains("哈希", StringComparison.Ordinal) &&
               Hero(catalog).GenerationAvailability.All(level => level.CanGenerate),
            "A collision with even an unreferenced tree must block affected generation without blocking unrelated heroes.");
        Console.WriteLine("PASS: native per-tree last match, partial/mixed-class/nested files, path-slot priority, invalid winners, hash safety and DSON purchases.");
    }
}
