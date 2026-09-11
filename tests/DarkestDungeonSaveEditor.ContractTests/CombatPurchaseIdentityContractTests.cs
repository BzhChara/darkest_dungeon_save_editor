internal static partial class ContractSuite
{
    private static ActiveContentSnapshot CombatPurchaseContent(string root, string kind, IReadOnlyList<string> skills, int levels = 2)
    {
        var content = HeroProgressionQueryContent(root, kind);
        var path = Path.Combine(root, "baseline/heroes/progression/progression.info.darkest");
        var records = string.Join("\n", skills.SelectMany(skill => Enumerable.Range(0, levels).Select(level =>
            $"combat_skill: .id \"{skill}\" .level {level}"))) + "\n";
        File.WriteAllText(path, File.ReadAllText(path).Replace(
            "combat_skill: .id attack .level 0\ncombat_skill: .id attack .level 1\n", records, StringComparison.Ordinal));
        return content;
    }

    private static JsonObject CombatPurchaseTree(string id, params (string Code, int Level)[] requirements) => new()
    {
        ["id"] = id,
        ["requirements"] = new JsonArray(requirements.Select(r => (JsonNode)new JsonObject
            { ["code"] = r.Code, ["prerequisite_resolve_level"] = r.Level }).ToArray())
    };

    private static string WriteCombatPurchaseTrees(string source, string prefix, params JsonObject[] trees) =>
        WriteMultiMash(source, prefix + "upgrades/zz-combat.upgrades.json", new JsonObject
            { ["trees"] = new JsonArray(trees.Select(t => (JsonNode)t).ToArray()) }.ToJsonString());

    private static async Task VerifyCombatPurchaseIdentitiesAsync(string runRoot, DsonSaveCodec codec)
    {
        const string prefix = "progression.";
        var cases = new[]
        {
            ("short", "strike", "strike", "full", 1),
            ("ascii-63", new string('a', 51), new string('a', 51), "full", 1),
            ("ascii-64-full", new string('a', 52), new string('a', 51), "full", 0),
            ("utf8-63", new string('营', 17), new string('营', 17), "full", 1),
            ("utf8-66-full", new string('营', 18), new string('营', 17), "full", 0),
            ("ascii-64-native", new string('a', 52), new string('a', 51), "native", 1),
            ("utf8-66-native", new string('营', 18), new string('营', 17), "native", 1),
            ("ascii-64-both", new string('a', 52), new string('a', 51), "both", 0),
            ("ascii-64-implicit", new string('a', 52), new string('a', 51), "none", 0),
            ("ascii-64-majority", new string('a', 52), new string('a', 51), "reference", 1)
        };
        foreach (var kind in new[] { "base", "mode", "dlc-feature", "local", "workshop", "dlc-mod" })
        foreach (var (variant, skillId, targetSuffix, mode, expectedLevel) in cases)
        {
            var root = Path.Combine(runRoot, "combat-purchase-identity", kind, variant);
            var content = CombatPurchaseContent(root, kind, mode == "reference" ? [skillId, "attack"] : [skillId]);
            var source = Path.Combine(root, "overlay");
            var dlcPrefix = kind == "dlc-mod" ? "dlc/rq_feature/" : "";
            var target = prefix + targetSuffix;
            var trees = new List<JsonObject>();
            if (mode is "full" or "both") trees.Add(CombatPurchaseTree(prefix + skillId, ("0", 0), ("1", 1)));
            if (mode == "native") trees.Add(CombatPurchaseTree(target, ("0", 0), ("1", 1)));
            if (mode == "both") trees.Add(CombatPurchaseTree(target, ("0", 0), ("1", 3)));
            var path = WriteCombatPurchaseTrees(source, dlcPrefix, trees.ToArray());
            if (kind is "local" or "workshop" or "dlc-mod")
            {
                WriteFixtureManifest(source);
                WriteMultiMash(source, dlcPrefix + "upgrades/zzz-unlisted.upgrades.json", "invalid JSON");
            }
            var catalog = HeroClassCatalog.Load(content);
            var hero = catalog.HeroClasses.Single(h => h.Id == "progression");
            var hasBoundTree = mode is "native" or "both" || (mode == "full" && targetSuffix == skillId);
            Assert(hero.GenerationAvailability.All(a => a.CanGenerate) && catalog.Issues.Count == 0 &&
                   hero.CombatSkillIds.Contains(skillId, StringComparer.Ordinal) &&
                   hero.UpgradeTrees.Any(t => t.Id == target && t.SourcePath == path) == hasBoundTree &&
                   (targetSuffix == skillId || hero.UpgradeTrees.All(t => t.Id != prefix + skillId)),
                $"{kind}/{variant}: bind only the native combat target; keep the complete skill identity.");
            var generated = GenerateProgressionHero(catalog);
            var codes = generated.UpgradePurchases.Where(p => p.TreeId == target).Select(p => p.RequirementCode).ToArray();
            Assert(codes.SequenceEqual(expectedLevel == 1 ? ["0", "1"] : ["0"]) &&
                   (targetSuffix == skillId || generated.UpgradePurchases.All(p => p.TreeId != prefix + skillId)) &&
                   generated.Preview.CurrentHp == 40 && generated.Preview.ResolveXp == 10,
                $"{kind}/{variant}: explicit and implicit purchases must use the correct target and original skill metadata.");
            if (mode == "both")
                Assert(GenerateProgressionHero(catalog, 3).UpgradePurchases.Any(p => p.TreeId == target && p.RequirementCode == "1"),
                    "The bounded target's authored prerequisite, not the unused full-name tree, controls progression.");
            if (mode == "reference")
                Assert(generated.Preview.Warnings.Any(w => w.Contains("多数规则", StringComparison.Ordinal)),
                    "An implicit long skill must find its real variants by its full ID and inherit the valid same-class schedule.");
            if (kind == "local")
            {
                await VerifyProgressionCandidatePersistenceAsync(root, generated, codec);
                var restored = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "upgrades.roundtrip.json")))!;
                var written = restored["base_root"]!["purchases"]!.AsObject().Select(p => p.Value!.AsObject()).ToArray();
                var nativeHash = unchecked((int)HashLoc2Key(target));
                Assert(written.Where(p => p["tree_id"]!.GetValue<int>() == nativeHash)
                           .Select(p => p["requirement_code"]!.GetValue<string>()).SequenceEqual(codes) &&
                       written.All(p => p["instance_number"]!.GetValue<int>() == 950 && p["is_purchased"]!.GetValue<bool>()) &&
                       (targetSuffix == skillId || written.All(p => p["tree_id"]!.GetValue<int>() != unchecked((int)HashLoc2Key(prefix + skillId)))),
                    "Actual DSON purchases must contain the native target hash and complete consecutive unlock codes.");
                var town = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "town.roundtrip.json")))!;
                Assert(town["base_root"]!["buildings"]!["stage_coach"]!["store"]!["hero_recruit"]!["generated"]!["950"]!
                           ["skills"]!["selected_combat_skills"]!.AsObject().Select(p => p.Key).SequenceEqual(generated.Preview.CombatSkills),
                    "Equipped skill IDs remain independent of bounded purchase targets after DSON serialization.");
            }
        }

        foreach (var (variant, ids, reason) in new[]
        {
            ("split-utf8", new[] { new string('a', 50) + "营" }, "UTF-8"),
            ("same-target", new[] { new string('a', 51) + "x", new string('a', 51) + "y" }, "同一购买目标"),
            ("hash-collision", new[] { new string('a', 49) + "Aax", new string('a', 49) + "B,y" }, "购买编号冲突"),
            ("long-skill-id", new[] { new string('a', 64) }, "技能 ID 超过")
        })
        {
            var root = Path.Combine(runRoot, "combat-purchase-guards", variant);
            var content = CombatPurchaseContent(root, "base", ids);
            var catalog = HeroClassCatalog.Load(content);
            var hero = catalog.HeroClasses.Single(h => h.Id == "progression");
            Assert(hero.GenerationAvailability.All(a => !a.CanGenerate && a.UnavailableReason.Contains(reason, StringComparison.Ordinal)),
                $"{variant}: invalid targets must disable this hero in catalog preflight without crashing catalog loading.");
            var failure = await CaptureSaveFailureAsync(() => { GenerateProgressionHero(catalog); return Task.CompletedTask; });
            Assert(failure is InvalidOperationException && failure.Message.Contains(reason, StringComparison.Ordinal),
                $"{variant}: direct generation must use the same target guard.");
        }
        // The native text reader stops at NUL before a full definition is
        // available. Test the generation boundary directly, not a fictitious text record.
        var inputRoot = Path.Combine(runRoot, "combat-purchase-guards", "input-identities");
        var inputCatalog = HeroClassCatalog.Load(CombatPurchaseContent(inputRoot, "base", ["strike"]));
        var inputHero = inputCatalog.HeroClasses.Single();
        foreach (var invalid in new[]
        {
            (inputHero with { CombatSkillIds = ["strike\0suffix"] }, "NUL"),
            (inputHero with { Id = new string('a', 64) }, "职业或技能 ID 超过")
        })
            Assert(StagecoachHeroCandidateFactory.GetGenerationAvailability(inputCatalog, invalid.Item1)
                       .All(a => !a.CanGenerate && a.UnavailableReason.Contains(invalid.Item2, StringComparison.Ordinal)),
                "Invalid direct-input identities must not bypass the same generation preflight guard.");
        var aliasRoot = Path.Combine(runRoot, "combat-purchase-guards", "authored-hash-alias");
        var aliasContent = CombatPurchaseContent(aliasRoot, "base", ["Aa"]);
        var aliasPath = WriteCombatPurchaseTrees(Path.Combine(aliasRoot, "overlay"), "",
            CombatPurchaseTree("progression.B,", ("0", 0)));
        var aliasHero = HeroClassCatalog.Load(aliasContent).HeroClasses.Single(h => h.Id == "progression");
        Assert(aliasHero.UpgradeTrees.Single(t => t.Id == "progression.Aa").SourcePath == aliasPath &&
               aliasHero.GenerationAvailability.All(a => !a.CanGenerate && a.UnavailableReason.Contains("哈希冲突", StringComparison.Ordinal)),
            "An authored hash alias of a computed target must not masquerade as an absent implicit tree.");

        var invalidRoot = Path.Combine(runRoot, "combat-purchase-guards", "invalid-bounded-tree");
        var invalidContent = CombatPurchaseContent(invalidRoot, "local", [new string('a', 52)]);
        var invalidSource = Path.Combine(invalidRoot, "overlay");
        var invalidPath = WriteCombatPurchaseTrees(invalidSource, "",
            CombatPurchaseTree(prefix + new string('a', 51), ("bad", 0)));
        WriteFixtureManifest(invalidSource);
        var invalidCatalog = HeroClassCatalog.Load(invalidContent);
        Assert(invalidCatalog.HeroClasses.Single().UpgradeTrees.Any(t => t.Id == prefix + new string('a', 51) &&
                   t.SourcePath == invalidPath && t.UnsupportedReason.Length > 0) &&
               invalidCatalog.HeroClasses.Single().GenerationAvailability.All(a => !a.CanGenerate),
            "Invalid bounded winners retain their provenance and cannot fall through to implicit unlock.");
        var before = ProfileCatalogContentFingerprint.Capture(invalidContent.Sources);
        WriteCombatPurchaseTrees(invalidSource, "", CombatPurchaseTree(prefix + new string('a', 51), ("0", 0), ("1", 1)));
        Assert(ProfileCatalogContentFingerprint.Capture(invalidContent.Sources) != before &&
               HeroClassCatalog.Load(invalidContent).HeroClasses.Single().GenerationAvailability.All(a => a.CanGenerate),
            "Repairing the consumed tree must refresh availability without changing its manifest.");

        var duplicateRoot = Path.Combine(runRoot, "combat-purchase-guards", "duplicate-skill");
        var duplicateContent = CombatPurchaseContent(duplicateRoot, "base", [new string('a', 52), new string('a', 52)]);
        var duplicate = GenerateProgressionHero(HeroClassCatalog.Load(duplicateContent));
        Assert(duplicate.UpgradePurchases.Count(p => p.TreeId == prefix + new string('a', 51)) == 1,
            "Repeated declarations of the same original skill must not become duplicate purchases or target conflicts.");
        Console.WriteLine("PASS: native combat purchase targets, bounded tree binding, implicit metadata and DSON identities.");
    }
}
