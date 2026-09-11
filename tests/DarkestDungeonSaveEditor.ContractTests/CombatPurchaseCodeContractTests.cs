using System.Globalization;

internal static partial class ContractSuite
{
    private static async Task VerifyCombatPurchaseCodesAsync(string runRoot, DsonSaveCodec codec)
    {
        var cases = new[]
        {
            ("numeric", new[] { ("0", 0), ("1", 1) }, 1, true, false, 1),
            ("letter-A", new[] { ("A", 0) }, 1, false, false, -1),
            ("letters-a-A", new[] { ("a", 0), ("A", 2) }, 2, false, false, -1),
            ("gap-0-2", new[] { ("0", 0), ("2", 1) }, 1, true, true, 0),
            ("delayed-base", new[] { ("0", 3), ("1", 0) }, 1, false, false, -1),
            ("delayed-base-ready", new[] { ("0", 3), ("1", 0) }, 3, true, false, 1),
            ("numeric-delay", new[] { ("0", 0), ("1", 3) }, 1, true, false, 0),
            ("future-base", new[] { ("0", 3), ("1", 4) }, 1, false, false, -1),
            ("empty-tree", Array.Empty<(string, int)>(), 1, false, false, -1),
            ("valid-three", new[] { ("0", 0), ("1", 1), ("2", 1) }, 1, true, false, 2),
            ("extra-letters", new[] { ("0", 0), ("a", 0), ("A", 2) }, 2, true, false, 0)
        };
        foreach (var kind in new[] { "base", "mode", "dlc-feature", "local", "workshop", "dlc-mod" })
        foreach (var (variant, requirements, level, available, gapWarning, expectedNativeLevel) in cases)
        {
            var root = Path.Combine(runRoot, "combat-purchase-codes", kind, variant);
            var content = CombatPurchaseContent(root, kind, ["strike"], levels: 3);
            var source = Path.Combine(root, "overlay");
            var prefix = kind == "dlc-mod" ? "dlc/rq_feature/" : "";
            WriteCombatPurchaseTrees(source, prefix, CombatPurchaseTree("progression.strike", requirements));
            if (kind is "local" or "workshop" or "dlc-mod") WriteFixtureManifest(source);
            var catalog = HeroClassCatalog.Load(content);
            var hero = catalog.HeroClasses.Single(h => h.Id == "progression");
            var tree = hero.UpgradeTrees.Single(t => t.Id == "progression.strike");
            Assert(tree.UnsupportedReason.Length == 0 && tree.Requirements.Select(r => r.Code).Order(StringComparer.Ordinal)
                       .SequenceEqual(requirements.Select(r => r.Item1).Order(StringComparer.Ordinal)),
                "Generic tree parsing retains exact authored codes, including valid letters; combat use is validated separately.");
            Assert(hero.GenerationAvailability.Single(a => a.ResolveLevel == level).CanGenerate == available,
                $"{kind}/{variant}: catalog availability must require a purchasable base code 0.");
            if (!available)
            {
                var failure = await CaptureSaveFailureAsync(() => { GenerateProgressionHero(catalog, level); return Task.CompletedTask; });
                Assert(failure is InvalidOperationException && failure.Message.Contains("基础购买码 '0'", StringComparison.Ordinal),
                    "An authored tree without a base purchase must not enter implicit fallback or generate a locked skill.");
                continue;
            }
            var generated = GenerateProgressionHero(catalog, level);
            var codes = generated.UpgradePurchases.Where(p => p.TreeId == "progression.strike")
                .Select(p => p.RequirementCode).ToArray();
            Assert(codes.SequenceEqual(requirements.Where(r => r.Item2 <= level).Select(r => r.Item1).Order(StringComparer.Ordinal)) &&
                   generated.Preview.Warnings.Any(w => w.Contains("购买码存在空洞", StringComparison.Ordinal)) == gapWarning,
                $"{kind}/{variant}: preserve eligible codes and report unreachable later tiers without inventing missing purchases.");
            // Independent model of native 0x14058EA10: stop at the first missing
            // code, bounded by the actual three variants in this fixture.
            var nativeLevel = Enumerable.Range(0, 3).TakeWhile(i => codes.Contains(i.ToString(CultureInfo.InvariantCulture))).Count() - 1;
            Assert(nativeLevel == expectedNativeLevel && (!gapWarning || generated.Preview.Warnings.Any(w =>
                       w.Contains("只能读取第 0 档", StringComparison.Ordinal))),
                "The warning and preserved purchase plan must agree with the native consecutive-code query.");
            if (kind == "local") await VerifyProgressionCandidatePersistenceAsync(root, generated, codec);
        }

        // A bounded authored tree must also vote under its full original skill ID.
        var majorityRoot = Path.Combine(runRoot, "combat-purchase-codes", "bounded-majority");
        var longSkill = new string('a', 52);
        var majorityContent = CombatPurchaseContent(majorityRoot, "base", [longSkill, "implicit"]);
        WriteCombatPurchaseTrees(Path.Combine(majorityRoot, "overlay"), "",
            CombatPurchaseTree("progression." + new string('a', 51), ("0", 0), ("1", 1)));
        var majorityCatalog = HeroClassCatalog.Load(majorityContent);
        Assert(GenerateProgressionHero(majorityCatalog).UpgradePurchases.Where(p => p.TreeId == "progression.implicit")
                   .Select(p => p.RequirementCode).SequenceEqual(["0", "1"]),
            "Same-class majority voting must bind the native target while looking up variants by the complete skill ID.");
        Console.WriteLine("PASS: authored combat base unlocks, reachable tiers, gap warnings and original purchase codes.");
    }
}
