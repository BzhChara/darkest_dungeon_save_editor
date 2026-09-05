internal static partial class ContractSuite
{
    private static async Task VerifyImplicitSkillProgressionContractsAsync(
        HeroClassCatalogResult catalog,
        HeroClassDefinition fixtureHero,
        DsonSaveCodec codec,
        string runRoot)
    {
        Assert(fixtureHero.CombatSkillLevels["local_skill_two"].SequenceEqual([0, 1]),
            "The effective catalog must preserve each skill's actual level definitions.");

        const string implicitId = "local_skill_two";
        const string implicitTree = "local_hero.local_skill_two";
        var schedule = new[] { 0, 1, 2, 3, 5 }
            .Select((level, index) => new HeroUpgradeRequirementDefinition(index.ToString(), level)).ToArray();
        var normalTree = new HeroUpgradeTreeDefinition("local_hero.local_skill", HeroUpgradeTreeKind.CombatSkill, schedule);
        var hero = fixtureHero with
        {
            CanSelectCombatSkills = false,
            UpgradeTrees = fixtureHero.UpgradeTrees.Where(tree => tree.Kind != HeroUpgradeTreeKind.CombatSkill)
                .Append(normalTree).ToArray(),
            CombatSkillLevels = fixtureHero.CombatSkillIds.ToDictionary(
                id => id, _ => (IReadOnlyList<int>)new[] { 0, 1, 2, 3, 4 }, StringComparer.Ordinal)
        };

        string[] Codes(GeneratedStagecoachHeroCandidate candidate, string treeId = implicitTree) =>
            candidate.UpgradePurchases.Where(purchase => purchase.TreeId == treeId)
                .Select(purchase => purchase.RequirementCode).ToArray();
        GeneratedStagecoachHeroCandidate Generate(HeroClassDefinition definition, int level = 6) =>
            StagecoachHeroCandidateFactory.Generate(catalog, definition, 1729, level, []);
        HeroClassDefinition WithLevels(string skillId, params int[] levels) => hero with
        {
            CombatSkillLevels = hero.CombatSkillLevels.ToDictionary(
                pair => pair.Key, pair => pair.Key == skillId ? (IReadOnlyList<int>)levels : pair.Value, StringComparer.Ordinal)
        };
        void AssertBaseOnly(HeroClassDefinition definition, string reason)
        {
            var generated = Generate(definition);
            Assert(Codes(generated).SequenceEqual(["0"]) &&
                   generated.Preview.Warnings.Any(warning => warning.Contains("仅做基础解锁", StringComparison.Ordinal)), reason);
        }

        foreach (var selectability in new bool?[] { false, true, null })
        {
            var selectableHero = hero with { CanSelectCombatSkills = selectability, SelectedCombatSkillsMax = 1 };
            Assert(StagecoachHeroCandidateFactory.GetGenerationAvailability(catalog, selectableHero).All(level => level.CanGenerate),
                "Implicit upgrades must be available at every valid level for true, false, and unknown selectability.");
            foreach (var level in hero.LevelProfiles)
            {
                var generated = Generate(selectableHero, level.ResolveLevel);
                var expected = schedule.Where(requirement => requirement.PrerequisiteResolveLevel <= level.ResolveLevel)
                    .Select(requirement => requirement.Code);
                Assert(Codes(generated).SequenceEqual(expected) && Codes(generated, normalTree.Id).SequenceEqual(expected),
                    "Every tree-less skill must receive the complete consecutive same-class purchase sequence, even when it is not equipped.");
                Assert(generated.Preview.CombatSkills.Count == (selectability == false ? hero.CombatSkillIds.Count : 1) &&
                       generated.Preview.WeaponRank == level.WeaponRank && generated.Preview.ArmourRank == level.ArmourRank &&
                       generated.Candidate["skills"]!["selected_combat_skills"]!.AsObject().All(pair => pair.Value!.GetValue<int>() == 0),
                    "Implicit progression must not change skill selection placeholders, equipped counts, or equipment ranks.");
                Assert(!generated.Preview.Warnings.Any(warning => warning.Contains("仅做基础解锁", StringComparison.Ordinal)),
                    "Successful implicit skill progression must not retain the old base-only warning.");
            }
        }

        Assert(Codes(Generate(WithLevels(implicitId, 0, 1, 2))).SequenceEqual(["0", "1", "2"]),
            "Implicit purchases must never exceed the target skill's defined level count.");
        Assert(Codes(Generate(WithLevels(implicitId, 0) with { SingleLevelCombatSkillIds = [implicitId] })).SequenceEqual(["0"]),
            "A genuinely single-level skill must retain exactly its base unlock.");
        Assert(Codes(Generate(hero with
        {
            UpgradeTrees = hero.UpgradeTrees.Append(normalTree with { Id = implicitTree, Requirements = schedule.Take(4).ToArray() }).ToArray()
        })).SequenceEqual(["0", "1", "2", "3"]),
            "An authored four-tier purchase tree must not acquire a fifth tier merely because the skill defines five variants.");
        var delayedSchedule = normalTree with
        {
            Requirements = schedule.Select(requirement => requirement with
            {
                PrerequisiteResolveLevel = requirement.PrerequisiteResolveLevel == 0 ? 0 : requirement.PrerequisiteResolveLevel + 1
            }).ToArray()
        };
        var delayedHero = hero with { UpgradeTrees = hero.UpgradeTrees.Where(tree => tree != normalTree).Append(delayedSchedule).ToArray() };
        Assert(Codes(Generate(delayedHero, 1)).SequenceEqual(["0"]) && Codes(Generate(delayedHero, 5)).SequenceEqual(["0", "1", "2", "3"]),
            "Implicit skills must use the active class's progression, not hard-coded vanilla or equipment levels.");

        AssertBaseOnly(WithLevels(implicitId, 0, 2), "Gaps in target definitions must never be filled with invented levels.");
        AssertBaseOnly(WithLevels(implicitId, 0, 1, 2, 3, 4, 5), "An unknown extra target tier must not inherit a guessed resolve prerequisite.");
        AssertBaseOnly(WithLevels(implicitId, Enumerable.Range(0, 11).ToArray()), "Implicit purchase codes must remain single ASCII digits.");
        AssertBaseOnly(hero with { CombatSkillLevels = new Dictionary<string, IReadOnlyList<int>>() },
            "Missing definition metadata must retain a visible base-only fallback.");
        AssertBaseOnly(WithLevels("local_skill", 0, 1), "The reference tree must agree with its own skill definition count.");
        AssertBaseOnly(hero with { UpgradeTrees = hero.UpgradeTrees.Where(tree => tree.Kind != HeroUpgradeTreeKind.CombatSkill).ToArray() },
            "Equipment trees alone cannot define a missing combat progression schedule.");
        AssertBaseOnly(hero with
        {
            CombatSkillIds = hero.CombatSkillIds.Append("different_schedule").ToArray(),
            CombatSkillLevels = hero.CombatSkillLevels.Append(new("different_schedule", new[] { 0, 1, 2, 3, 4 }))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            UpgradeTrees = hero.UpgradeTrees.Append(delayedSchedule with { Id = "local_hero.different_schedule" }).ToArray()
        }, "Conflicting class schedules must not be resolved by file or skill order.");
        foreach (var requirements in new[]
                 {
                     schedule.Select(requirement => requirement.Code == "2" ? requirement with { Code = "a" } : requirement).ToArray(),
                     schedule.Select(requirement => requirement.Code == "2" ? requirement with { PrerequisiteResolveLevel = 0 } : requirement).ToArray(),
                     schedule.Select(requirement => requirement with { PrerequisiteResolveLevel = requirement.PrerequisiteResolveLevel + 1 }).ToArray()
                 })
        {
            AssertBaseOnly(hero with
            {
                UpgradeTrees = hero.UpgradeTrees.Where(tree => tree != normalTree).Append(normalTree with { Requirements = requirements }).ToArray()
            }, "Non-numeric, non-monotonic, or delayed-base schedules cannot define an implicit progression.");
        }

        VerifyImplicitSkillMajorityContracts(catalog, hero, normalTree, delayedSchedule);

        var candidate = Generate(hero with { CanSelectCombatSkills = true, SelectedCombatSkillsMax = 1 });
        var town = JsonNode.Parse("""{"base_root":{"buildings":{"stage_coach":{"store":{"hero_recruit":{"generated":{}}}}}}}""")!.AsObject();
        var roster = JsonNode.Parse("""{"base_root":{"nextGuid":894,"heroes":{}}}""")!.AsObject();
        var upgrades = JsonNode.Parse("""{"base_root":{"purchases":{}}}""")!.AsObject();
        var originalInputs = new[] { town.ToJsonString(), roster.ToJsonString(), upgrades.ToJsonString() };
        var mutation = StagecoachHeroSaveEditor.AddCandidate(town, roster, upgrades, candidate.Candidate, candidate.UpgradePurchases);
        Assert(originalInputs.SequenceEqual(new[] { town.ToJsonString(), roster.ToJsonString(), upgrades.ToJsonString() }),
            "Writing an implicit purchase plan must preserve caller-owned source save objects.");
        var outputRoot = Path.Combine(runRoot, "implicit_skill_roundtrip");
        Directory.CreateDirectory(outputRoot);
        foreach (var output in new[]
                 {
                     (Name: "town", Root: mutation.UpdatedTown),
                     (Name: "roster", Root: mutation.UpdatedRoster),
                     (Name: "upgrades", Root: mutation.UpdatedUpgrades)
                 })
        {
            var decodedPath = Path.Combine(outputRoot, $"persist.{output.Name}.decoded.json");
            var binaryPath = Path.Combine(outputRoot, $"persist.{output.Name}.json");
            var roundtripPath = Path.Combine(outputRoot, $"persist.{output.Name}.roundtrip.json");
            await File.WriteAllTextAsync(decodedPath, output.Root.ToJsonString(), new UTF8Encoding(false));
            await codec.EncodeAsync(decodedPath, binaryPath, null);
            await codec.DecodeAsync(binaryPath, roundtripPath);
            Assert(JsonNode.DeepEquals(output.Root, JsonNode.Parse(await File.ReadAllTextAsync(roundtripPath))),
                $"Implicit skill generation must survive the actual DSON {output.Name} encode/decode round trip.");
        }

        var treeHash = unchecked((int)HashLoc2Key(implicitTree));
        var writtenCodes = mutation.UpdatedUpgrades["base_root"]!["purchases"]!.AsObject()
            .Select(pair => pair.Value!.AsObject())
            .Where(purchase => purchase["tree_id"]!.GetValue<int>() == treeHash && purchase["instance_number"]!.GetValue<int>() == 894)
            .Select(purchase => purchase["requirement_code"]!.GetValue<string>()).ToArray();
        Assert(writtenCodes.SequenceEqual(["0", "1", "2", "3", "4"]),
            "All five implicit purchases must be associated with the new hero GUID and correct class/skill hash.");
    }
}
