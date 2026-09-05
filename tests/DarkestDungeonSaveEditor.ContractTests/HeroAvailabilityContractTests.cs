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
        Assert(StagecoachHeroCandidateFactory.GetGenerationAvailability(catalog, missingCombatTree)
                .All(level => !level.CanGenerate && level.UnavailableReason.Contains("local_hero.local_skill_two", StringComparison.Ordinal)),
            "A missing multilevel skill tree must be reported before attempting a preview at every level.");
        var missingCamping = hero with { ClassCampingSkillIds = [] };
        Assert(StagecoachHeroCandidateFactory.GetGenerationAvailability(catalog, missingCamping)
                .All(level => !level.CanGenerate && level.UnavailableReason.Contains("职业露营技能", StringComparison.Ordinal)),
            "Incomplete required camping skills must make the displayed generation ability unavailable.");
        var delayedSkill = hero with
        {
            UpgradeTrees = hero.UpgradeTrees.Select(tree => tree.Id == "local_hero.local_skill_two"
                ? tree with { Requirements = [new HeroUpgradeRequirementDefinition("a", 1)] }
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
