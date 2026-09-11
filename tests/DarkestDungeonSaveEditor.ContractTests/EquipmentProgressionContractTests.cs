internal static partial class ContractSuite
{
    private sealed record EquipmentCase(string Name, string HeroId, string?[] Weapon, string?[] Armour,
        (string Code, int Level)[] Requirements, int ExpectedWeapon, int ExpectedArmour,
        int ResolveLevel = 1, string TreeMode = "full");

    private static (ActiveContentSnapshot Content, string Source, string Prefix, string Info, string Trees)
        EquipmentContent(string root, string kind, EquipmentCase sample)
    {
        var baseline = Path.Combine(root, "baseline");
        WriteMultiMash(baseline, "localization/a.string_table.xml",
            """<root><language id="english"><entry id="hero_name_0">Equipment Hero</entry></language></root>""");
        WriteMultiMash(baseline, "campaign/roster/roster.variables.json", RosterThresholds(10));
        var source = Path.Combine(root, "overlay");
        var prefix = kind == "dlc-mod" ? "dlc/rq_feature/" : "";
        var hp = new[] { 20, 40, 70 };
        var text = string.Join("\n", sample.Weapon.Select((code, i) => $"weapon: .name blade{i}" +
            (code is null ? "" : " .upgradeRequirementCode " + code))) + "\n" +
            string.Join("\n", sample.Armour.Select((code, i) => $"armour: .name coat{i} .hp {hp[i]}" +
            (code is null ? "" : " .upgradeRequirementCode " + code))) + "\n" +
            "combat_skill: .id c .level 0\n" +
            "generation: .is_generation_enabled true .number_of_positive_quirks_min 0 .number_of_positive_quirks_max 0 " +
            ".number_of_negative_quirks_min 0 .number_of_negative_quirks_max 0 .number_of_random_combat_skills 1 " +
            ".number_of_class_specific_camping_skills 0 .number_of_shared_camping_skills 0\n";
        var info = WriteMultiMash(source, prefix + $"heroes/{sample.HeroId}/{sample.HeroId}.info.darkest", text);
        WriteMultiMash(source, prefix + $"heroes/{sample.HeroId}/{sample.HeroId}_A/skin.png", "fixture");
        var trees = new List<JsonObject>();
        foreach (var suffix in new[] { "weapon", "armour" })
        {
            var raw = sample.HeroId + "." + suffix;
            var bounded = Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(raw).Take(63).ToArray());
            if (sample.TreeMode is "full" or "both") trees.Add(CombatPurchaseTree(raw, sample.Requirements));
            if (sample.TreeMode is "native" or "both") trees.Add(CombatPurchaseTree(bounded,
                sample.TreeMode == "both" ? [("a", 3)] : sample.Requirements));
        }
        var treePath = WriteCombatPurchaseTrees(source, prefix, trees.ToArray());
        var sources = new[] { new ActiveContentSource("base", "Base", "base", baseline, 0) }
            .Concat(QuerySources(source, kind)).ToArray();
        if (kind is "local" or "workshop" or "dlc-mod")
        {
            WriteFixtureManifest(source);
            WriteMultiMash(source, prefix + "upgrades/zzz-unlisted.upgrades.json", "invalid JSON");
        }
        return (QueryContent(root, sources), source, prefix, info, treePath);
    }

    private static int EquipmentRankFromSave(string?[] rawCodes, string target, JsonObject purchases)
    {
        var bought = purchases.Select(p => p.Value!).Where(p => p["instance_number"]!.GetValue<int>() == 950 &&
                p["tree_id"]!.GetValue<int>() == unchecked((int)HashLoc2Key(target)) && p["is_purchased"]!.GetValue<bool>())
            .Select(p => p["requirement_code"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
        var rank = 0;
        for (var i = 0; i < rawCodes.Length; i++)
        {
            var value = rawCodes[i] ?? "";
            var at = value.LastIndexOf(".upgradeRequirementCode", StringComparison.Ordinal);
            if (at >= 0) value = value[(at + ".upgradeRequirementCode".Length)..];
            value = value.TrimStart(' ', '\t', '\r', '\n', '\v', '\f');
            var code = value.Length == 0 ? (byte)0 : Encoding.UTF8.GetBytes(value)[0];
            if (code != 0 && !bought.Contains(((char)code).ToString())) break;
            rank = i;
        }
        return rank;
    }

    private static async Task VerifyEquipmentProgressionAsync(string runRoot, DsonSaveCodec codec)
    {
        string?[] ordinary = [null, "a"];
        (string, int)[] one = [("a", 1)];
        var samples = new EquipmentCase[]
        {
            new("ordinary", "equipment", ordinary, ordinary, one, 1, 1),
            new("reversed", "equipment", [null,"a","b"], [null,"a","b"], [("a",3),("b",1)], 0, 0),
            new("reversed-ready", "equipment", [null,"a","b"], [null,"a","b"], [("a",3),("b",1)], 2, 2, 3),
            new("same-code", "equipment", [null,"a","a"], [null,"a","a"], one, 2, 2),
            new("case-sensitive", "equipment", [null,"a","A"], [null,"a","A"], [("a",1),("A",2)], 1, 1),
            new("last-field", "equipment", [null,"x .upgradeRequirementCode a"], [null,"x .upgradeRequirementCode a"], one, 1, 1),
            new("quoted-armour", "equipment", ordinary, [null,"\"a\""], one, -1, -1),
            new("quoted-weapon", "equipment", [null,"\"a\""], ordinary, one, -1, -1),
            new("quote-code", "equipment", [null,"\"a\""], [null,"\"a\""], [("\"",1)], -1, -1),
            new("long-token", "equipment", [null,"alpha"], [null,"alpha"], one, 1, 1),
            new("free-upper", "equipment", [null,null], [null,null], [], 1, 1),
            new("free-zero", "equipment", [null,null], [null,null], [], 1, 1, 0),
            new("no-tree-free", "equipment", [null,null], [null,null], [], 1, 1, 1, "none"),
            new("unused-requirement", "equipment", ordinary, ordinary, [("a",1),("z",0)], 1, 1),
            new("base-gate", "equipment", ["a","b"], ["a","b"], [("a",3),("b",1)], 0, 0),
            new("partial-free-zero", "equipment", [null,null,"z"], [null,null,"z"], [], 1, 1, 0),
            new("partial-free-higher", "equipment", [null,null,"z"], [null,null,"z"], [], -1, -1),
            new("target-63", new string('h',56), ordinary, ordinary, one, 1, 1),
            new("target-64-full", new string('h',57), ordinary, ordinary, one, -1, -1),
            new("target-64-native", new string('h',57), ordinary, ordinary, one, 1, 1, 1, "native"),
            new("target-64-both", new string('h',57), ordinary, ordinary, one, 0, 0, 1, "both"),
            new("target-68-native", new string('h',61), ordinary, ordinary, one, 1, 1, 1, "native")
        };
        foreach (var kind in new[] { "base", "mode", "dlc-feature", "local", "workshop", "dlc-mod" })
        foreach (var sample in samples)
        {
            var root = Path.Combine(runRoot, "equipment-progression", kind, sample.Name);
            var fixture = EquipmentContent(root, kind, sample);
            var catalog = HeroClassCatalog.Load(fixture.Content);
            var hero = catalog.HeroClasses.Single(h => h.Id == sample.HeroId);
            var available = hero.GenerationAvailability.Single(a => a.ResolveLevel == sample.ResolveLevel);
            Assert(available.CanGenerate == (sample.ExpectedWeapon >= 0), $"{kind}/{sample.Name}: correct equipment generation availability.");
            if (sample.ExpectedWeapon < 0)
            {
                var failure = await CaptureSaveFailureAsync(() => { StagecoachHeroCandidateFactory.Generate(catalog, hero, 11, sample.ResolveLevel, []); return Task.CompletedTask; });
                Assert(failure is InvalidOperationException && failure.Message.Contains(
                        sample.Name == "quote-code" ? "DSON codec" : "upgradeRequirementCode", StringComparison.Ordinal),
                    $"{kind}/{sample.Name}: retain a specific restriction for an unresolved nonzero native purchase code.");
                continue;
            }
            var generated = StagecoachHeroCandidateFactory.Generate(catalog, hero, 11, sample.ResolveLevel, []);
            Assert(generated.Preview.WeaponRank == sample.ExpectedWeapon && generated.Preview.ArmourRank == sample.ExpectedArmour &&
                   generated.Preview.CurrentHp == new[] {20d,40d,70d}[sample.ExpectedArmour] &&
                   generated.Preview.ResolveXp == sample.ResolveLevel * 10 && generated.Preview.CombatSkills.SequenceEqual(["c"]),
                $"{kind}/{sample.Name}: actual sequential purchases control equipment rank and initial HP.");
            if (sample.Name is "reversed" or "base-gate")
                Assert(generated.Preview.Warnings.Any(w => w.Contains("前置档阻断", StringComparison.Ordinal)) &&
                       generated.UpgradePurchases.Count(p => p.RequirementCode == "b") == 2,
                    "Unreachable later purchases remain intact and their current equipment limit is visible.");
            if (sample.Name == "unused-requirement")
                Assert(generated.UpgradePurchases.Count(p => p.RequirementCode == "z") == 2,
                    "Extra authored equipment purchases do not invalidate ranks and are not discarded.");
            foreach (var suffix in new[] { "weapon", "armour" })
            {
                var full = sample.HeroId + "." + suffix;
                var target = Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(full).Take(63).ToArray());
                Assert(hero.UpgradeTrees.Where(t => t.Kind.ToString().Equals(suffix, StringComparison.OrdinalIgnoreCase))
                           .All(t => t.Id == target && t.SourcePath == fixture.Trees) &&
                       (target == full || generated.UpgradePurchases.All(p => p.TreeId != full)),
                    "Only the native equipment target is bound and written; registry IDs are not globally renamed.");
            }
            if (kind == "local")
            {
                await VerifyProgressionCandidatePersistenceAsync(root, generated, codec);
                var saved = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "upgrades.roundtrip.json")))!["base_root"]!["purchases"]!.AsObject();
                foreach (var (suffix, codes, expectedRank) in new[] { ("weapon",sample.Weapon,sample.ExpectedWeapon), ("armour",sample.Armour,sample.ExpectedArmour) })
                {
                    var target = Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(sample.HeroId + "." + suffix).Take(63).ToArray());
                    Assert(EquipmentRankFromSave(codes,target,saved) == expectedRank,
                        $"{sample.Name}/{suffix}: saved target hashes and original purchase codes must reach the previewed rank.");
                }
            }
        }
        Console.WriteLine("PASS: native equipment purchase codes, sequential ranks, free tiers and bounded targets with DSON persistence.");
    }
}
