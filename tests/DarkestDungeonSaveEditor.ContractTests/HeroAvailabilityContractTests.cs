internal static partial class ContractSuite
{
    private static void VerifyHeroAvailabilityContracts(HeroClassCatalogResult catalog, HeroClassDefinition hero)
    {
        foreach (var definition in catalog.HeroClasses)
        {
            Assert(definition.GenerationAvailability.Count == Math.Max(1, catalog.ResolveLevelThresholds.Count),
                "Every catalog hero must expose a preflight result for every selectable level.");
            foreach (var availability in definition.GenerationAvailability)
            {
                var reason = string.Empty;
                try
                {
                    _ = StagecoachHeroCandidateFactory.Generate(catalog, definition, 1729, availability.ResolveLevel, []);
                }
                catch (Exception error) when (error is InvalidOperationException or ArgumentException)
                {
                    reason = error.Message;
                }
                Assert(availability.UnavailableReason == reason,
                    "Catalog availability must agree with the complete generation path, including at a different random seed.");
            }
        }

        var missingCombatTree = hero with
        {
            UpgradeTrees = hero.UpgradeTrees.Where(tree => tree.Id != "local_hero.local_skill_two").ToArray()
        };
        foreach (var selectability in new bool?[] { false, true, null })
        {
            var implicitSkillHero = missingCombatTree with { CanSelectCombatSkills = selectability, SelectedCombatSkillsMax = 1 };
            Assert(StagecoachHeroCandidateFactory.GetGenerationAvailability(catalog, implicitSkillHero).All(level => level.CanGenerate),
                "Missing multilevel trees must use the same implicit policy regardless of skill selectability.");
            foreach (var level in hero.LevelProfiles)
            {
                var generated = StagecoachHeroCandidateFactory.Generate(catalog, implicitSkillHero, 1729, level.ResolveLevel, []);
                Assert(generated.UpgradePurchases.Where(purchase => purchase.TreeId == "local_hero.local_skill_two")
                           .Select(purchase => purchase.RequirementCode).SequenceEqual(["0"]) &&
                       generated.Preview.CombatSkills.Count == (selectability == false ? hero.CombatSkillIds.Count : 1) &&
                       generated.Preview.WeaponRank == level.WeaponRank && generated.Preview.ArmourRank == level.ArmourRank &&
                       generated.Preview.Warnings.Any(warning => warning.Contains("仅做基础解锁", StringComparison.Ordinal)),
                    "A missing numeric reference schedule must retain a visible base-only unlock without changing selection or equipment progression.");
            }
        }
        var missingCamping = hero with { ClassCampingSkillIds = [] };
        Assert(StagecoachHeroCandidateFactory.GetGenerationAvailability(catalog, missingCamping)
                .All(level => level.CanGenerate),
            "An exhausted class camping pool must not disable otherwise valid generation levels.");
        foreach (var level in hero.LevelProfiles)
        {
            var generated = StagecoachHeroCandidateFactory.Generate(catalog, missingCamping, 1729, level.ResolveLevel, []);
            Assert(generated.Preview.CampingSkills.Count == Math.Min(hero.Generation!.SharedCampingSkills ?? 0, hero.SharedCampingSkillIds.Count) &&
                   generated.Preview.CampingSkills.All(hero.SharedCampingSkillIds.Contains) &&
                   generated.Preview.Warnings.Any(warning => warning.Contains("职业露营技能要求", StringComparison.Ordinal)),
                "Class-pool exhaustion must preserve shared selection and report the shortage at every available level.");
        }
        var delayedSkill = hero with
        {
            UpgradeTrees = hero.UpgradeTrees.Select(tree => tree.Id == "local_hero.local_skill_two"
                ? tree with { Requirements = [new HeroUpgradeRequirementDefinition("0", 1)] }
                : tree).ToArray()
        };
        var partialLevels = StagecoachHeroCandidateFactory.GetGenerationAvailability(catalog, delayedSkill);
        Assert(!partialLevels[0].CanGenerate && partialLevels.Skip(1).All(level => level.CanGenerate),
            "A skill unlocked at a later resolve level must not hide the hero's valid higher-level generation paths.");
        Assert(StagecoachHeroCandidateFactory.GetGenerationAvailability(catalog with { HeroNames = [] }, hero)
                .All(level => !level.CanGenerate),
            "Preflight must include non-progression prerequisites such as the hero name catalog.");
    }
}
