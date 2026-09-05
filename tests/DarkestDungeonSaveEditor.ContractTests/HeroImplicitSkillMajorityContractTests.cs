internal static partial class ContractSuite
{
    private static void VerifyImplicitSkillMajorityContracts(
        HeroClassCatalogResult catalog,
        HeroClassDefinition hero,
        HeroUpgradeTreeDefinition normalTree,
        HeroUpgradeTreeDefinition delayedTree)
    {
        const string implicitTreeId = "local_hero.local_skill_two";
        HeroClassDefinition WithReferences(params HeroUpgradeTreeDefinition[] references) => hero with
        {
            CombatSkillIds = hero.CombatSkillIds.Concat(references.Select(tree => tree.Id[(hero.Id.Length + 1)..])).ToArray(),
            CombatSkillLevels = hero.CombatSkillLevels.Concat(references.Select(tree =>
                    new KeyValuePair<string, IReadOnlyList<int>>(tree.Id[(hero.Id.Length + 1)..],
                        Enumerable.Range(0, tree.Requirements.Count).ToArray())))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            UpgradeTrees = hero.UpgradeTrees.Concat(references).ToArray()
        };
        GeneratedStagecoachHeroCandidate Generate(HeroClassDefinition definition, int level = 1) =>
            StagecoachHeroCandidateFactory.Generate(catalog, definition, 1729, level, []);
        string[] Codes(GeneratedStagecoachHeroCandidate candidate, string treeId = implicitTreeId) =>
            candidate.UpgradePurchases.Where(purchase => purchase.TreeId == treeId)
                .Select(purchase => purchase.RequirementCode).ToArray();
        void AssertNoMajority(HeroClassDefinition definition)
        {
            var generated = Generate(definition);
            Assert(Codes(generated).SequenceEqual(["0"]) &&
                   generated.Preview.Warnings.Any(warning => warning.Contains("严格过半", StringComparison.Ordinal)),
                "A tie or plurality without a strict majority must retain a visible base-only fallback.");
        }

        var majority = WithReferences(
            normalTree with { Id = "local_hero.normal_extra" },
            delayedTree with { Id = "local_hero.delayed_extra" });
        var generatedMajority = Generate(majority);
        Assert(Codes(generatedMajority).SequenceEqual(["0", "1"]) &&
               Codes(generatedMajority, "local_hero.delayed_extra").SequenceEqual(["0"]),
            "Two of three matching reference schedules must upgrade the implicit skill without overriding an authored minority tree.");
        Assert(Codes(Generate(majority with
        {
            CombatSkillIds = majority.CombatSkillIds.Reverse().ToArray(),
            UpgradeTrees = majority.UpgradeTrees.Reverse().ToArray()
        })).SequenceEqual(["0", "1"]), "A strict-majority result must not depend on definition order.");
        var fourTierTarget = majority with
        {
            CombatSkillLevels = majority.CombatSkillLevels.ToDictionary(pair => pair.Key,
                pair => pair.Key == "local_skill_two" ? (IReadOnlyList<int>)new[] { 0, 1, 2, 3 } : pair.Value,
                StringComparer.Ordinal)
        };
        foreach (var level in hero.LevelProfiles)
        {
            Assert(Codes(Generate(fourTierTarget, level.ResolveLevel)).SequenceEqual(normalTree.Requirements.Take(4)
                       .Where(requirement => requirement.PrerequisiteResolveLevel <= level.ResolveLevel)
                       .Select(requirement => requirement.Code)),
                "A four-tier target must follow the five-tier majority's first four prerequisites and never acquire a fifth tier.");
        }

        var delayedMajority = WithReferences(
            delayedTree with { Id = "local_hero.delayed_a" },
            delayedTree with { Id = "local_hero.delayed_b" });
        var generatedDelayedMajority = Generate(delayedMajority);
        Assert(Codes(generatedDelayedMajority).SequenceEqual(["0"]) &&
               Codes(generatedDelayedMajority, normalTree.Id).SequenceEqual(["0", "1"]) &&
               !generatedDelayedMajority.Preview.Warnings.Any(warning => warning.Contains("仅做基础解锁", StringComparison.Ordinal)),
            "The majority must win even when it is less permissive, without modifying an existing minority schedule.");

        var tie = WithReferences(delayedTree with { Id = "local_hero.delayed_extra" });
        AssertNoMajority(tie);
        AssertNoMajority(WithReferences(
            normalTree with { Id = "local_hero.normal_extra" },
            delayedTree with { Id = "local_hero.delayed_a" },
            delayedTree with { Id = "local_hero.delayed_b" }));
        var slowTree = delayedTree with
        {
            Requirements = delayedTree.Requirements.Select(requirement => requirement with
            {
                PrerequisiteResolveLevel = requirement.Code == "0" ? 0 : 6
            }).ToArray()
        };
        AssertNoMajority(WithReferences(
            normalTree with { Id = "local_hero.normal_a" },
            normalTree with { Id = "local_hero.normal_b" },
            delayedTree with { Id = "local_hero.delayed_a" },
            delayedTree with { Id = "local_hero.delayed_b" },
            slowTree with { Id = "local_hero.slow_a" },
            slowTree with { Id = "local_hero.slow_b" }));

        var invalidReference = normalTree with
        {
            Id = "local_hero.invalid_reference",
            Requirements = normalTree.Requirements.Select(requirement =>
                requirement.Code == "2" ? requirement with { Code = "a" } : requirement).ToArray()
        };
        var singleLevelReference = normalTree with
        {
            Id = "local_hero.single_level",
            Requirements = [new HeroUpgradeRequirementDefinition("0", 0)]
        };
        var unrelatedTree = delayedTree with { Id = "other_class.unrelated_skill" };
        Assert(Codes(Generate(WithReferences(invalidReference, singleLevelReference) with
        {
            UpgradeTrees = hero.UpgradeTrees.Concat([invalidReference, singleLevelReference, unrelatedTree]).ToArray()
        })).SequenceEqual(["0", "1"]),
            "Invalid, single-level, equipment, and non-class trees must not vote against the sole valid multilevel reference.");

        var duplicateSkillIds = tie with
        {
            CombatSkillIds = tie.CombatSkillIds.Concat(Enumerable.Repeat("local_skill", 3)).ToArray(),
            CanSelectCombatSkills = true,
            SelectedCombatSkillsMax = 1
        };
        var generatedDuplicate = Generate(duplicateSkillIds);
        Assert(Codes(generatedDuplicate).SequenceEqual(["0"]) &&
               generatedDuplicate.Preview.Warnings.Any(warning => warning.Contains("严格过半", StringComparison.Ordinal)),
            "Duplicate skill IDs must not give one authored skill multiple votes.");

        var unreferencedTarget = majority with
        {
            CombatSkillIds = majority.CombatSkillIds.Append("another_implicit").ToArray(),
            CombatSkillLevels = majority.CombatSkillLevels.Append(new("another_implicit", new[] { 0, 1, 2, 3, 4 }))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
        };
        var generatedUnreferencedTarget = Generate(unreferencedTarget);
        Assert(Codes(generatedUnreferencedTarget).SequenceEqual(["0", "1"]) &&
               Codes(generatedUnreferencedTarget, "local_hero.another_implicit").SequenceEqual(["0", "1"]),
            "Tree-less target skills cannot vote or change the denominator of the authored reference majority.");
    }
}
