using System.Globalization;
using System.Text.Json.Nodes;

namespace DarkestDungeonSaveEditor.Core;

public static partial class StagecoachHeroCandidateFactory
{
    private const double HpSafetyTolerance = 1e-9;

    public static void ValidateInitialQuirkSelection(
        HeroClassCatalogResult catalog,
        HeroClassDefinition heroClass,
        IReadOnlyCollection<string> selectedInitialQuirkIds)
    {
        var levelZero = heroClass.LevelProfiles
            .SingleOrDefault(profile => profile.ResolveLevel == 0);
        var baseHp = levelZero?.ArmourHp ?? heroClass.BaseHp ??
            throw new InvalidOperationException(EditorText.Format("StagecoachHeroCandidateFactory_001", heroClass.Id));
        ValidateInitialQuirkSelection(catalog, heroClass, baseHp, selectedInitialQuirkIds);
    }

    public static void ValidateInitialQuirkSelection(
        HeroClassCatalogResult catalog,
        HeroClassDefinition heroClass,
        int resolveLevel,
        IReadOnlyCollection<string> selectedInitialQuirkIds)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(heroClass);
        var levelProfile = ResolveLevelProfile(catalog, heroClass, resolveLevel);
        ValidateInitialQuirkSelection(catalog, heroClass, levelProfile.ArmourHp, selectedInitialQuirkIds);
    }

    private static void ValidateInitialQuirkSelection(
        HeroClassCatalogResult catalog,
        HeroClassDefinition heroClass,
        double baseHp,
        IReadOnlyCollection<string> selectedInitialQuirkIds)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(heroClass);
        ArgumentNullException.ThrowIfNull(selectedInitialQuirkIds);
        if (heroClass.HasProviderConflict)
        {
            throw new InvalidOperationException(EditorText.Format("StagecoachHeroCandidateFactory_002", heroClass.Id));
        }

        var selectedQuirks = ResolveSelectedQuirks(
            catalog,
            selectedInitialQuirkIds);
        _ = GetValidatedInitialCurrentHp(heroClass.Id, baseHp, selectedQuirks);
    }

    public static GeneratedStagecoachHeroCandidate Generate(
        HeroClassCatalogResult catalog,
        HeroClassDefinition heroClass,
        int seed)
    {
        return Generate(catalog, heroClass, seed, 0, Array.Empty<string>());
    }

    public static GeneratedStagecoachHeroCandidate Generate(
        HeroClassCatalogResult catalog,
        HeroClassDefinition heroClass,
        int seed,
        IReadOnlyCollection<string> selectedInitialQuirkIds)
    {
        return Generate(catalog, heroClass, seed, 0, selectedInitialQuirkIds);
    }

    public static GeneratedStagecoachHeroCandidate Generate(
        HeroClassCatalogResult catalog,
        HeroClassDefinition heroClass,
        int seed,
        int resolveLevel,
        IReadOnlyCollection<string> selectedInitialQuirkIds)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(heroClass);
        ArgumentNullException.ThrowIfNull(selectedInitialQuirkIds);
        if (heroClass.HasProviderConflict)
        {
            throw new InvalidOperationException(EditorText.Format("StagecoachHeroCandidateFactory_003", heroClass.Id));
        }

        var generation = heroClass.Generation ??
            throw new InvalidOperationException(EditorText.Format("StagecoachHeroCandidateFactory_004", heroClass.Id));
        var levelProfile = ResolveLevelProfile(catalog, heroClass, resolveLevel);
        var baseHp = levelProfile.ArmourHp;
        if (heroClass.ColourVariationCount <= 0)
        {
            throw new InvalidOperationException(EditorText.Format("StagecoachHeroCandidateFactory_005", heroClass.Id));
        }

        if (catalog.HeroNames.Count == 0)
        {
            throw new InvalidOperationException(EditorText.Get("StagecoachHeroCandidateFactory_006"));
        }

        var classCampingCount = generation.ClassCampingSkills ?? 0;
        var sharedCampingCount = generation.SharedCampingSkills ?? 0;
        if (classCampingCount < 0 || sharedCampingCount < 0)
        {
            throw new InvalidOperationException(EditorText.Format("StagecoachHeroCandidateFactory_007", heroClass.Id));
        }

        var random = new Random(seed);
        var warnings = new List<string>();
        if (generation.IsEnabled == false)
        {
            warnings.Add(EditorText.Get("StagecoachHeroCandidateFactory_008"));
        }

        if (!string.IsNullOrWhiteSpace(generation.TownEventDependency))
        {
            warnings.Add(EditorText.Format("StagecoachHeroCandidateFactory_009", generation.TownEventDependency));
        }

        var combatSkills = SelectCombatSkills(heroClass, generation, random, warnings);
        var campingSkills = SelectCampingSkills(
            heroClass,
            classCampingCount,
            sharedCampingCount,
            random,
            warnings);
        var selectedQuirks = ResolveSelectedQuirks(
            catalog,
            selectedInitialQuirkIds);
        var initialQuirkStates = selectedQuirks
            .Select(quirk => new InitialQuirkPersistenceState(
                quirk,
                ResolveEvolutionDuration(seed, quirk)))
            .ToArray();
        var upgradePurchases = BuildUpgradePurchases(heroClass, levelProfile.ResolveLevel, warnings);
        HeroEquipmentProgression.Validate(heroClass, levelProfile, upgradePurchases, warnings);

        var evolvingQuirkSummaries = initialQuirkStates
            .Where(state => state.Definition.Evolution is not null)
            .Select(FormatEvolutionSummary)
            .ToArray();
        if (evolvingQuirkSummaries.Length > 0)
        {
            warnings.Add(
                EditorText.Format("StagecoachHeroCandidateFactory_010", string.Join("；", evolvingQuirkSummaries)));
        }

        warnings.Add(EditorText.Get("StagecoachHeroCandidateFactory_011"));

        var currentHp = GetValidatedInitialCurrentHp(heroClass.Id, baseHp, selectedQuirks);

        var name = catalog.HeroNames[random.Next(catalog.HeroNames.Count)];
        var colourVariation = random.Next(heroClass.ColourVariationCount);
        var positiveQuirks = selectedQuirks
            .Where(quirk => quirk.IsPositive == true)
            .Select(quirk => quirk.Id)
            .ToArray();
        var negativeQuirks = selectedQuirks
            .Where(quirk => !quirk.IsDisease && quirk.IsPositive == false)
            .Select(quirk => quirk.Id)
            .ToArray();
        var diseases = selectedQuirks
            .Where(quirk => quirk.IsDisease)
            .Select(quirk => quirk.Id)
            .ToArray();
        var candidate = BuildCandidate(
            heroClass.Id,
            name,
            levelProfile.ResolveXp,
            levelProfile.WeaponRank,
            levelProfile.ArmourRank,
            currentHp,
            colourVariation,
            initialQuirkStates,
            combatSkills,
            campingSkills);
        return new GeneratedStagecoachHeroCandidate(
            candidate,
            new StagecoachHeroCandidatePreview(
                name,
                heroClass.Id,
                levelProfile.ResolveLevel,
                levelProfile.ResolveXp,
                levelProfile.WeaponRank,
                levelProfile.ArmourRank,
                currentHp,
                colourVariation,
                positiveQuirks,
                negativeQuirks,
                diseases,
                combatSkills,
                campingSkills,
                warnings),
            upgradePurchases);
    }

}
