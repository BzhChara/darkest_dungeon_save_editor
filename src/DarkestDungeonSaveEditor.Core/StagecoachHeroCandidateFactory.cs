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
            throw new InvalidOperationException($"职业 '{heroClass.Id}' 没有可用于验证初始怪癖的 0 级生命模板。");
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
            throw new InvalidOperationException($"职业 '{heroClass.Id}' 有未解析的重复定义，不能选择初始怪癖。");
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
            throw new InvalidOperationException($"职业 '{heroClass.Id}' 有未解析的重复定义，不能生成候选。");
        }

        var generation = heroClass.Generation ??
            throw new InvalidOperationException($"职业 '{heroClass.Id}' 没有 generation 模板。");
        var levelProfile = ResolveLevelProfile(catalog, heroClass, resolveLevel);
        var baseHp = levelProfile.ArmourHp;
        if (heroClass.ColourVariationCount <= 0)
        {
            throw new InvalidOperationException($"职业 '{heroClass.Id}' 没有连续且从 A 开始的皮肤目录，无法安全写入 colour_variation。");
        }

        if (catalog.HeroNames.Count == 0)
        {
            throw new InvalidOperationException("活动内容中没有可用的 hero_name_* 姓名。");
        }

        var classCampingCount = generation.ClassCampingSkills ?? 0;
        var sharedCampingCount = generation.SharedCampingSkills ?? 0;
        if (classCampingCount < 0 || sharedCampingCount < 0)
        {
            throw new InvalidOperationException($"职业 '{heroClass.Id}' 的露营技能数量不能为负数。");
        }

        var random = new Random(seed);
        var warnings = new List<string>();
        if (generation.IsEnabled == false)
        {
            warnings.Add("该职业关闭了常规随机生成；这是用户显式选择的手动候选。");
        }

        if (!string.IsNullOrWhiteSpace(generation.TownEventDependency))
        {
            warnings.Add($"该职业声明城镇事件依赖：{generation.TownEventDependency}；手动候选不会触发该事件。");
        }

        var combatSkills = SelectCombatSkills(heroClass, generation, random);
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
                $"进化怪癖倒计时已按活动内容定义初始化：{string.Join("；", evolvingQuirkSummaries)}。");
        }

        warnings.Add("buff_group_next_guid 使用已通过测试档实机载入验证的观测基线 2。");

        var currentHp = GetValidatedInitialCurrentHp(heroClass.Id, baseHp, selectedQuirks);

        var name = catalog.HeroNames[random.Next(catalog.HeroNames.Count)];
        var colourVariation = random.Next(heroClass.ColourVariationCount);
        var positiveQuirks = selectedQuirks
            .Where(quirk => !quirk.IsDisease && quirk.IsPositive == true)
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
