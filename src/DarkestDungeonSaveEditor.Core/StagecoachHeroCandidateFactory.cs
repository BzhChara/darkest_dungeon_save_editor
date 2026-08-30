using System.Globalization;
using System.Text.Json.Nodes;

namespace DarkestDungeonSaveEditor.Core;

public static class StagecoachHeroCandidateFactory
{
    public const int MaximumPositiveInitialQuirks = 5;
    public const int MaximumNegativeInitialQuirks = 5;
    public const int MaximumInitialDiseases = 3;

    public static void ValidateInitialQuirkSelection(
        HeroClassCatalogResult catalog,
        HeroClassDefinition heroClass,
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
            catalog.InitialQuirks,
            selectedInitialQuirkIds);
        _ = GetValidatedMaxHpModifierTotal(heroClass.Id, selectedQuirks);
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
            catalog.InitialQuirks,
            selectedInitialQuirkIds);
        var initialQuirkStates = selectedQuirks
            .Select(quirk => new InitialQuirkPersistenceState(
                quirk,
                ResolveEvolutionDuration(seed, quirk)))
            .ToArray();
        var upgradePurchases = BuildUpgradePurchases(heroClass, levelProfile.ResolveLevel);

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

        var maxHpModifierTotal = GetValidatedMaxHpModifierTotal(heroClass.Id, selectedQuirks);
        var currentHp = baseHp * (1.0 + maxHpModifierTotal);
        if (!double.IsFinite(currentHp) || currentHp <= 0)
        {
            throw new InvalidOperationException($"职业 '{heroClass.Id}' 的初始怪癖计算出了无效当前生命：{currentHp}。");
        }

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

    internal static IReadOnlyList<HeroUpgradePurchase> BuildUpgradePurchases(
        HeroClassDefinition heroClass,
        int resolveLevel)
    {
        var combatTrees = heroClass.UpgradeTrees
            .Where(tree => tree.Kind == HeroUpgradeTreeKind.CombatSkill)
            .ToDictionary(tree => tree.Id, StringComparer.Ordinal);
        var expectedCombatTreeIds = heroClass.CombatSkillIds
            .Select(skillId => $"{heroClass.Id}.{skillId}")
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var missingCombatTreeIds = expectedCombatTreeIds
            .Where(treeId => !combatTrees.ContainsKey(treeId))
            .ToArray();
        if (missingCombatTreeIds.Length > 0)
        {
            throw new InvalidOperationException(
                $"职业 '{heroClass.Id}' 的活动 upgrade 模板缺少战斗技能升级树：" +
                $"{string.Join(", ", missingCombatTreeIds)}；" +
                $"不能安全生成 {resolveLevel} 级全技能解锁人物。");
        }

        var unavailableCombatTreeIds = expectedCombatTreeIds
            .Where(treeId => combatTrees[treeId].Requirements.All(requirement =>
                requirement.PrerequisiteResolveLevel > resolveLevel))
            .ToArray();
        if (unavailableCombatTreeIds.Length > 0)
        {
            throw new InvalidOperationException(
                $"职业 '{heroClass.Id}' 的下列战斗技能升级树在 {resolveLevel} 级没有可用 requirement：" +
                $"{string.Join(", ", unavailableCombatTreeIds)}；不能声称该等级已全技能解锁。");
        }

        var applicableTrees = heroClass.UpgradeTrees
            .Where(tree => tree.Kind != HeroUpgradeTreeKind.CombatSkill)
            .Concat(expectedCombatTreeIds.Select(treeId => combatTrees[treeId]));
        var campingPurchases = heroClass.SharedCampingSkillIds
            .Concat(heroClass.ClassCampingSkillIds)
            .Distinct(StringComparer.Ordinal)
            .Select(skillId => new HeroUpgradePurchase($"{heroClass.Id}.{skillId}", "0"));
        var purchases = applicableTrees
            .SelectMany(tree => tree.Requirements
                .Where(requirement => requirement.PrerequisiteResolveLevel <= resolveLevel)
                .Select(requirement => new HeroUpgradePurchase(tree.Id, requirement.Code)))
            .Concat(campingPurchases)
            .OrderBy(purchase => purchase.TreeId, StringComparer.Ordinal)
            .ThenBy(purchase => purchase.RequirementCode, StringComparer.Ordinal)
            .ToArray();
        if (purchases.Any(purchase =>
                string.IsNullOrWhiteSpace(purchase.TreeId) ||
                string.IsNullOrWhiteSpace(purchase.RequirementCode)))
        {
            throw new InvalidOperationException(
                $"职业 '{heroClass.Id}' 的活动 upgrade 模板包含空树 ID 或空 requirement code。");
        }

        var duplicate = purchases
            .GroupBy(purchase => new { purchase.TreeId, purchase.RequirementCode })
            .FirstOrDefault(group => group.Skip(1).Any());
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"职业 '{heroClass.Id}' 的活动 upgrade 模板产生了重复购买项：" +
                $"{duplicate.First().TreeId}/{duplicate.First().RequirementCode}。");
        }

        return purchases;
    }

    private static HeroLevelProfile ResolveLevelProfile(
        HeroClassCatalogResult catalog,
        HeroClassDefinition heroClass,
        int resolveLevel)
    {
        var maximumLevel = catalog.ResolveLevelThresholds.Count > 0
            ? catalog.ResolveLevelThresholds.Count - 1
            : 0;
        if (resolveLevel < 0 || resolveLevel > maximumLevel)
        {
            throw new InvalidOperationException(
                $"人物等级必须在 0 到 {maximumLevel} 之间，当前为 {resolveLevel}。");
        }

        var matches = heroClass.LevelProfiles
            .Where(profile => profile.ResolveLevel == resolveLevel)
            .ToArray();
        if (matches.Length != 1)
        {
            var reason = string.IsNullOrWhiteSpace(heroClass.ProgressionUnsupportedReason)
                ? "等级模板缺失或重复"
                : heroClass.ProgressionUnsupportedReason;
            throw new InvalidOperationException(
                $"职业 '{heroClass.Id}' 不能生成 {resolveLevel} 级人物：{reason}。");
        }

        var profile = matches[0];
        if (profile.ResolveXp < 0 || profile.WeaponRank < 0 || profile.ArmourRank < 0 ||
            !double.IsFinite(profile.ArmourHp) || profile.ArmourHp <= 0)
        {
            throw new InvalidOperationException(
                $"职业 '{heroClass.Id}' 的 {resolveLevel} 级模板包含无效 XP、装备 rank 或护甲 HP。");
        }

        return profile;
    }

    private static IReadOnlyList<string> SelectCombatSkills(
        HeroClassDefinition heroClass,
        HeroGenerationDefinition generation,
        Random random)
    {
        if (heroClass.CombatSkillIds.Count == 0)
        {
            throw new InvalidOperationException($"职业 '{heroClass.Id}' 没有 0 级战斗技能。");
        }

        var available = heroClass.CombatSkillIds.ToHashSet(StringComparer.Ordinal);
        if (heroClass.GuaranteedCombatSkillIds.Any(skill => !available.Contains(skill)))
        {
            throw new InvalidOperationException($"职业 '{heroClass.Id}' 的 generation_guaranteed 技能不在 0 级技能表中。");
        }

        if (heroClass.CanSelectCombatSkills == false)
        {
            return heroClass.CombatSkillIds.ToArray();
        }

        var generatedSkillCount = generation.RandomCombatSkills ??
            throw new InvalidOperationException($"职业 '{heroClass.Id}' 缺少 number_of_random_combat_skills。");
        if (generatedSkillCount <= 0 || generatedSkillCount > heroClass.CombatSkillIds.Count)
        {
            throw new InvalidOperationException(
                $"职业 '{heroClass.Id}' 要求生成 {generatedSkillCount} 个战斗技能，但只有 {heroClass.CombatSkillIds.Count} 个 0 级技能。");
        }

        var target = heroClass.SelectedCombatSkillsMax is { } maximum
            ? Math.Min(generatedSkillCount, maximum)
            : generatedSkillCount;
        if (target <= 0)
        {
            throw new InvalidOperationException(
                $"职业 '{heroClass.Id}' 的可选战斗技能上限无效：{target}。");
        }

        if (heroClass.GuaranteedCombatSkillIds.Count > target)
        {
            throw new InvalidOperationException(
                $"职业 '{heroClass.Id}' 有 {heroClass.GuaranteedCombatSkillIds.Count} 个必选技能，但生成总数只有 {target}。");
        }

        var selected = heroClass.GuaranteedCombatSkillIds.ToHashSet(StringComparer.Ordinal);
        var remaining = heroClass.CombatSkillIds.Where(skill => !selected.Contains(skill)).ToList();
        Shuffle(remaining, random);
        foreach (var skill in remaining.Take(target - selected.Count))
        {
            selected.Add(skill);
        }

        return heroClass.CombatSkillIds.Where(selected.Contains).ToArray();
    }

    private static IReadOnlyList<string> SelectCampingSkills(
        HeroClassDefinition heroClass,
        int classSpecificCount,
        int sharedCount,
        Random random,
        List<string> warnings)
    {
        if (classSpecificCount > heroClass.ClassCampingSkillIds.Count)
        {
            throw new InvalidOperationException(
                $"职业 '{heroClass.Id}' 要求 {classSpecificCount} 个职业露营技能，但活动内容中只有 {heroClass.ClassCampingSkillIds.Count} 个。");
        }

        var selectedClass = TakeRandom(heroClass.ClassCampingSkillIds, classSpecificCount, random);
        var actualSharedCount = Math.Min(sharedCount, heroClass.SharedCampingSkillIds.Count);
        if (actualSharedCount < sharedCount)
        {
            warnings.Add(
                $"共享露营技能要求 {sharedCount} 个、活动内容仅有 {heroClass.SharedCampingSkillIds.Count} 个；按游戏样本少取，不用职业技能补位。");
        }

        var selectedShared = TakeRandom(heroClass.SharedCampingSkillIds, actualSharedCount, random);
        return selectedShared.Concat(selectedClass).ToArray();
    }

    private static IReadOnlyList<HeroInitialQuirkDefinition> ResolveSelectedQuirks(
        IReadOnlyList<HeroInitialQuirkDefinition> allQuirks,
        IReadOnlyCollection<string> selectedIds)
    {
        var uniqueIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var selected = new List<HeroInitialQuirkDefinition>(selectedIds.Count);
        foreach (var rawId in selectedIds)
        {
            var id = rawId?.Trim();
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new InvalidOperationException("初始怪癖 ID 不能为空。");
            }
            if (!uniqueIds.Add(id))
            {
                throw new InvalidOperationException($"初始怪癖 '{id}' 被重复选择。");
            }

            var matches = allQuirks
                .Where(quirk => quirk.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (matches.Length != 1)
            {
                throw new InvalidOperationException(
                    matches.Length == 0
                        ? $"初始怪癖 '{id}' 不在当前活动内容目录中。"
                        : $"初始怪癖 '{id}' 有多个未解析定义，不能安全创建。");
            }

            var quirk = matches[0];
            if (quirk.IsPositive is null)
            {
                throw new InvalidOperationException($"初始怪癖 '{quirk.Id}' 没有明确的正负类型。");
            }
            var canWriteWithPreviewLimitCheck =
                quirk.WriteStatus == HeroInitialQuirkWriteStatus.RequiresSaveContext &&
                quirk.DefinitionLimit is > 0;
            if (quirk.WriteStatus != HeroInitialQuirkWriteStatus.Direct &&
                !canWriteWithPreviewLimitCheck)
            {
                throw new InvalidOperationException(
                    $"初始怪癖 '{quirk.Id}' 当前不能显式写入：{quirk.WriteStatusReason}");
            }
            selected.Add(quirk);
        }

        var positiveCount = selected.Count(quirk => !quirk.IsDisease && quirk.IsPositive == true);
        var negativeCount = selected.Count(quirk => !quirk.IsDisease && quirk.IsPositive == false);
        var diseaseCount = selected.Count(quirk => quirk.IsDisease);
        if (positiveCount > MaximumPositiveInitialQuirks ||
            negativeCount > MaximumNegativeInitialQuirks ||
            diseaseCount > MaximumInitialDiseases)
        {
            throw new InvalidOperationException(
                $"初始怪癖最多正面 {MaximumPositiveInitialQuirks} 个、负面 {MaximumNegativeInitialQuirks} 个、" +
                $"疾病 {MaximumInitialDiseases} 个；当前选择为 +{positiveCount}/-{negativeCount}/疾病 {diseaseCount}。");
        }

        for (var leftIndex = 0; leftIndex < selected.Count; leftIndex++)
        {
            for (var rightIndex = leftIndex + 1; rightIndex < selected.Count; rightIndex++)
            {
                var left = selected[leftIndex];
                var right = selected[rightIndex];
                if (left.IncompatibleQuirkIds.Contains(right.Id, StringComparer.OrdinalIgnoreCase) ||
                    right.IncompatibleQuirkIds.Contains(left.Id, StringComparer.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"初始怪癖 '{left.Id}' 与 '{right.Id}' 互斥，不能同时选择。");
                }
            }
        }

        return selected;
    }

    private static double GetValidatedMaxHpModifierTotal(
        string heroClassId,
        IEnumerable<HeroInitialQuirkDefinition> selectedQuirks)
    {
        var modifierTotal = selectedQuirks
            .Where(quirk => quirk.MaxHpModifier is not null)
            .Sum(quirk => quirk.MaxHpModifier!.Amount);

        if (!double.IsFinite(modifierTotal) || 1.0 + modifierTotal <= 0.0)
        {
            throw new InvalidOperationException(
                $"职业 '{heroClassId}' 的初始怪癖合计 HP 修正无效：{modifierTotal}。");
        }

        return modifierTotal;
    }

    private static int ResolveEvolutionDuration(int seed, HeroInitialQuirkDefinition quirk)
    {
        if (quirk.Evolution is not { } evolution)
        {
            return 0;
        }

        if (evolution.DurationMin == evolution.DurationMax)
        {
            return evolution.DurationMin;
        }

        var span = (ulong)((long)evolution.DurationMax - evolution.DurationMin + 1L);
        var offset = (int)(ComputeStableEvolutionHash(seed, quirk.Id) % span);
        return evolution.DurationMin + offset;
    }

    private static ulong ComputeStableEvolutionHash(int seed, string quirkId)
    {
        const ulong offsetBasis = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        var hash = offsetBasis;
        var seedBits = unchecked((uint)seed);
        for (var shift = 0; shift < 32; shift += 8)
        {
            hash ^= (byte)(seedBits >> shift);
            hash *= prime;
        }

        foreach (var character in quirkId)
        {
            hash ^= (byte)character;
            hash *= prime;
            hash ^= (byte)(character >> 8);
            hash *= prime;
        }

        return hash;
    }

    private static string FormatEvolutionSummary(InitialQuirkPersistenceState state)
    {
        var evolution = state.Definition.Evolution!;
        var outcome = evolution.CausesDeath
            ? string.IsNullOrWhiteSpace(evolution.TargetQuirkId)
                ? "到期死亡"
                : $"→ {evolution.TargetQuirkId} / 到期死亡"
            : $"→ {evolution.TargetQuirkId}";
        return $"{state.Definition.Id}={state.EvolutionDurationRemaining}" +
               $"（配置 {evolution.DurationMin}–{evolution.DurationMax}，{outcome}）";
    }

    private static JsonObject BuildCandidate(
        string heroClass,
        string name,
        int resolveXp,
        int weaponRank,
        int armourRank,
        double currentHp,
        int colourVariation,
        IEnumerable<InitialQuirkPersistenceState> quirks,
        IEnumerable<string> combatSkills,
        IEnumerable<string> campingSkills)
    {
        var quirkMap = new JsonObject();
        foreach (var quirk in quirks)
        {
            quirkMap[quirk.Definition.Id] = new JsonObject
            {
                ["is_new"] = true,
                ["is_locked"] = false,
                ["mission_count"] = 0,
                ["replaces_quirk"] = 0,
                ["replaces_quirk_viewed"] = false,
                ["evolution_duration_remaining"] = quirk.EvolutionDurationRemaining
            };
        }

        return new JsonObject
        {
            ["rescued"] = false,
            ["actor"] = new JsonObject
            {
                ["name"] = name,
                ["current_hp"] = CreateFloat(currentHp),
                ["stunned"] = 0,
                ["combat_ready"] = false,
                ["damage_source_data"] = 0,
                ["damage_source_type"] = 0,
                ["damage_type"] = 0,
                ["colour_variation"] = colourVariation,
                ["enemy_rank_targets"] = 0,
                ["friendly_rank_targets"] = 0,
                ["performing_turn"] = 0,
                ["controlling_actor_guid"] = 0,
                ["controlling_duration"] = 0,
                ["current_mode_id"] = 0,
                ["rounds_in_ranks"] = 0,
                ["check_round_ranks"] = 0,
                ["health_damage_blocks"] = 0,
                ["buff_group_next_guid"] = 2,
                ["buff_group"] = new JsonObject(),
                ["actor_dot"] = new JsonObject()
            },
            ["heroClass"] = heroClass,
            ["resolveXp"] = resolveXp,
            ["m_Stress"] = CreateFloat(0),
            ["is_death_heart_attack_completed"] = false,
            ["visited_deaths_door"] = false,
            ["deaths_door_enter_effect_round_cooldown"] = 0,
            ["has_had_heart_attack"] = false,
            ["backer_hero"] = false,
            ["steps_taken"] = 0,
            ["enemies_killed"] = 0,
            ["weapon_rank"] = weaponRank,
            ["armour_rank"] = armourRank,
            ["dd_test_survived"] = 0,
            ["affliction_type_id"] = string.Empty,
            ["affliction_severity"] = 0,
            ["virtue_type_id"] = string.Empty,
            ["provisions_consumed"] = 0,
            ["quirks"] = quirkMap,
            ["skills"] = new JsonObject
            {
                ["selected_combat_skills"] = CreateZeroMap(combatSkills),
                ["selected_camping_skills"] = CreateZeroMap(campingSkills)
            },
            ["trinkets"] = new JsonObject { ["items"] = new JsonObject() },
            ["has_item_Tracking"] = true,
            ["item_tracking"] = new JsonObject { ["supply"] = new JsonObject() },
            ["number_of_successful_darkest_dungeon_quests"] = 0,
            ["is_from_town_event"] = false
        };
    }

    private static JsonObject CreateZeroMap(IEnumerable<string> ids)
    {
        var result = new JsonObject();
        foreach (var id in ids)
        {
            result[id] = 0;
        }

        return result;
    }

    private static JsonNode CreateFloat(double value)
    {
        return JsonNode.Parse(value.ToString("0.0###############", CultureInfo.InvariantCulture))!;
    }

    private static IReadOnlyList<string> TakeRandom(
        IReadOnlyList<string> source,
        int count,
        Random random)
    {
        var values = source.ToList();
        Shuffle(values, random);
        return values.Take(count).ToArray();
    }

    private static void Shuffle<T>(IList<T> values, Random random)
    {
        for (var index = values.Count - 1; index > 0; index--)
        {
            var replacement = random.Next(index + 1);
            (values[index], values[replacement]) = (values[replacement], values[index]);
        }
    }

    private sealed record InitialQuirkPersistenceState(
        HeroInitialQuirkDefinition Definition,
        int EvolutionDurationRemaining);
}
