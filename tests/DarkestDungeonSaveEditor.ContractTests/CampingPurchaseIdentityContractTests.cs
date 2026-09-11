using System.Text.Json;

internal static partial class ContractSuite
{
    private static async Task VerifyCampingPurchaseIdentitiesAsync(string runRoot, DsonSaveCodec codec)
    {
        const string heroId = "progression";
        const string treePrefix = heroId + ".";
        // The native name-format buffer holds 63 UTF-8 bytes plus NUL.
        // These expected strings are explicit boundary cases, not calls to the product helper.
        var cases = new[]
        {
            ("short", "camp", "camp", "0"),
            ("ascii-63", new string('a', 51), new string('a', 51), "0"),
            ("ascii-64", new string('a', 52), new string('a', 51), "0"),
            ("utf8-63", new string('营', 17), new string('营', 17), "0"),
            ("utf8-66", new string('营', 18), new string('营', 17), "0"),
            ("authored-code-A", "camp", "camp", "A")
        };
        string WriteSkills(string root, string prefix, IEnumerable<string> ids, string code = "0") =>
            WriteMultiMash(root, prefix + "raid/camping/camping_skills.json", JsonSerializer.Serialize(new
            {
                configuration = new { class_specific_number_of_classes_threshold = 1 },
                skills = ids.Select(id => new
                {
                    id, level = 0, cost = 2, use_limit = 1, effects = Array.Empty<object>(), hero_classes = new[] { heroId },
                    upgrade_requirements = new[] { new { code, currency_cost = Array.Empty<object>(), prerequisite_requirements = Array.Empty<object>() } }
                })
            }));
        ActiveContentSnapshot Content(string root, string kind)
        {
            var content = HeroProgressionQueryContent(root, kind);
            File.AppendAllText(Path.Combine(root, "baseline", $"heroes/{heroId}/{heroId}.info.darkest"),
                "generation: .number_of_class_specific_camping_skills 1\n");
            return content;
        }
        foreach (var kind in new[] { "base", "mode", "dlc-feature", "local", "workshop", "dlc-mod" })
        foreach (var (variant, skillId, targetSuffix, code) in cases)
        {
            var root = Path.Combine(runRoot, "camping-purchase-identity", kind, variant);
            var content = Content(root, kind);
            var source = Path.Combine(root, "overlay");
            var prefix = kind == "dlc-mod" ? "dlc/rq_feature/" : "";
            WriteSkills(source, prefix, [skillId], code);
            if (kind is "local" or "workshop" or "dlc-mod")
            {
                WriteFixtureManifest(source);
                WriteMultiMash(source, prefix + "raid/camping/unlisted.camping_skills.json", "invalid JSON");
            }
            var catalog = HeroClassCatalog.Load(content);
            var hero = catalog.HeroClasses.Single(h => h.Id == heroId);
            Assert(hero.GenerationAvailability.All(l => l.CanGenerate) && catalog.Issues.Count == 0 &&
                   hero.ClassCampingSkillIds.SequenceEqual([skillId]),
                $"{kind}/{variant}: supported native targets remain available without changing the skill's own ID.");
            var generated = GenerateProgressionHero(catalog);
            var expected = new HeroUpgradePurchase(treePrefix + targetSuffix, "0");
            Assert(generated.UpgradePurchases.Contains(expected) &&
                   (targetSuffix == skillId || !generated.UpgradePurchases.Any(p => p.TreeId == treePrefix + skillId)) &&
                   generated.Preview.CampingSkills.SequenceEqual([skillId]),
                $"{kind}/{variant}: use the native camping purchase target and code 0 while retaining the equipped skill ID.");
            Assert(generated.UpgradePurchases.Where(p => p != expected).SequenceEqual(new[]
                {
                    new HeroUpgradePurchase("progression.armour", "0"), new HeroUpgradePurchase("progression.attack", "0"),
                    new HeroUpgradePurchase("progression.attack", "1"), new HeroUpgradePurchase("progression.weapon", "0")
                }), "Camping target normalization must not alter equipment or combat upgrade purchases.");
            if (kind == "local")
            {
                await VerifyProgressionCandidatePersistenceAsync(root, generated, codec);
                var restored = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "upgrades.roundtrip.json")))!;
                var hashes = restored["base_root"]!["purchases"]!.AsObject().Select(p => p.Value!["tree_id"]!.GetValue<int>()).ToArray();
                Assert(hashes.Contains(unchecked((int)HashLoc2Key(expected.TreeId))) &&
                       (targetSuffix == skillId || !hashes.Contains(unchecked((int)HashLoc2Key(treePrefix + skillId)))),
                    "DSON must contain the game's truncated target hash, never the obsolete full-string hash.");
                var town = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "town.roundtrip.json")))!;
                Assert(town["base_root"]!["buildings"]!["stage_coach"]!["store"]!["hero_recruit"]!["generated"]!["950"]!
                       ["skills"]!["selected_camping_skills"]!.AsObject().Select(p => p.Key).SequenceEqual([skillId]),
                    "Town serialization must retain the full skill ID independently of the purchase target.");
            }
        }

        foreach (var (variant, ids, reason) in new[]
        {
            ("split-utf8", new[] { new string('a', 50) + "营" }, "UTF-8"),
            ("same-target", new[] { new string('a', 51) + "x", new string('a', 51) + "y" }, "同一购买目标"),
            // Aa and B, have the same polynomial-53 hash; the distinct suffixes
            // keep the complete skill IDs distinct until the purchase-name truncation.
            ("hash-collision", new[] { new string('a', 49) + "Aa" + "x", new string('a', 49) + "B," + "y" }, "购买编号冲突"),
            ("nul-id", new[] { "camp\0suffix" }, "NUL")
        })
        {
            var root = Path.Combine(runRoot, "camping-purchase-guards", variant);
            var content = Content(root, "local"); var source = Path.Combine(root, "overlay");
            var path = WriteSkills(source, "", ids); WriteFixtureManifest(source);
            var catalog = HeroClassCatalog.Load(content); var hero = catalog.HeroClasses.Single(h => h.Id == heroId);
            Assert(hero.ClassCampingSkillIds.Count == ids.Length && hero.GenerationAvailability.All(l => !l.CanGenerate &&
                       l.UnavailableReason.Contains(reason, StringComparison.Ordinal)),
                $"{variant}: preflight must reject ambiguous or unsupported targets with the specific reason.");
            var failure = await CaptureSaveFailureAsync(() => { GenerateProgressionHero(catalog); return Task.CompletedTask; });
            Assert(failure is InvalidOperationException && failure.Message.Contains(reason, StringComparison.Ordinal),
                $"{variant}: direct generation must enforce the same guard as catalog preflight.");
            var before = ProfileCatalogContentFingerprint.Capture(content.Sources);
            WriteSkills(source, "", ["camp"]);
            Assert(ProfileCatalogContentFingerprint.Capture(content.Sources) != before && File.Exists(path),
                "Fixing a consumed camping definition must invalidate the content fingerprint without a manifest change.");
            Assert(HeroClassCatalog.Load(content).HeroClasses.Single(h => h.Id == heroId).GenerationAvailability.All(l => l.CanGenerate),
                "Reloading a corrected definition must remove the prior generation block.");
        }
        // Two authored references to the same exact skill remain one purchase.
        var duplicateRoot = Path.Combine(runRoot, "camping-purchase-guards", "same-skill");
        var duplicateContent = Content(duplicateRoot, "base");
        WriteSkills(Path.Combine(duplicateRoot, "overlay"), "", ["camp", "camp"]);
        var duplicateCatalog = HeroClassCatalog.Load(duplicateContent);
        Assert(GenerateProgressionHero(duplicateCatalog).UpgradePurchases.Count(p => p.TreeId == "progression.camp") == 1,
            "Repeated declarations of one exact skill are not a collision between distinct native targets.");
        Console.WriteLine("PASS: native camping purchase targets, UTF-8 bounds, identity guards and DSON persistence.");
    }
}
