using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;

namespace DarkestDungeonSaveEditor.Core;

public static partial class HeroClassCatalog
{
    private static IReadOnlyDictionary<string, HeroUpgradeDefinition> ResolveHeroUpgradeDefinitions(
        Dictionary<string, List<HeroUpgradeDefinition>> candidates,
        IReadOnlyDictionary<string, List<HeroCandidate>> heroCandidates,
        IReadOnlyDictionary<string, IReadOnlyList<EffectiveContentFile>> heroOverridesByClass,
        IReadOnlyDictionary<string, ActiveContentSource> sourcesById,
        List<string> issues)
    {
        var result = new Dictionary<string, HeroUpgradeDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in candidates)
        {
            var effective = SelectEffectiveDefinitions(
                pair.Value,
                sourcesById,
                definition => definition.Source,
                definition => definition.SourcePath,
                GetHeroUpgradeSignature);
            pair.Value.Clear();
            pair.Value.AddRange(effective);
            if (effective.Count == 1)
            {
                result[pair.Key] = effective[0];
                continue;
            }

            HeroCandidate? selectedHero = null;
            if (heroCandidates.TryGetValue(pair.Key, out var classCandidates))
            {
                var effectiveHeroes = SelectEffectiveDefinitions(
                    classCandidates,
                    sourcesById,
                    candidate => candidate.Source,
                    candidate => candidate.SourcePath,
                    GetHeroCandidateSignature);
                if (effectiveHeroes.Count == 1)
                {
                    selectedHero = ApplyHeroOverrides(
                        effectiveHeroes[0],
                        heroOverridesByClass.TryGetValue(pair.Key, out var overrideFiles)
                            ? overrideFiles
                            : [],
                        sourcesById);
                }
            }

            var compatible = selectedHero is null
                ? null
                : SelectCompatibleHeroUpgrade(selectedHero, effective);
            if (compatible is not null)
            {
                result[pair.Key] = compatible;
                continue;
            }

            issues.Add(
                $"Hero upgrade '{pair.Key}' has conflicting definitions at the same effective priority and was left unresolved: " +
                string.Join(
                    " | ",
                    effective.Select(candidate => $"{candidate.Source}:{candidate.SourcePath}")));
        }

        return result;
    }

    private static HeroUpgradeDefinition? SelectCompatibleHeroUpgrade(
        HeroCandidate hero,
        IReadOnlyList<HeroUpgradeDefinition> candidates)
    {
        var expectedTreeIds = hero.CombatSkillIds
            .ToDictionary(
                skillId => $"{hero.Id}.{skillId}",
                skillId => skillId,
                StringComparer.Ordinal);
        var singleLevelSkillIds = hero.CombatSkillLevels
            .Where(pair => pair.Value.Count == 1 && pair.Value[0] == 0)
            .Select(pair => pair.Key)
            .ToHashSet(StringComparer.Ordinal);
        var ranked = candidates
            .Select(candidate =>
            {
                var combatTreeIds = candidate.Trees
                    .Where(tree => tree.Kind == HeroUpgradeTreeKind.CombatSkill)
                    .Select(tree => tree.Id)
                    .ToHashSet(StringComparer.Ordinal);
                var unsupportedMissing = expectedTreeIds
                    .Where(pair => !combatTreeIds.Contains(pair.Key) &&
                                   !singleLevelSkillIds.Contains(pair.Value))
                    .Count();
                var equipmentFailures =
                    (string.IsNullOrWhiteSpace(BuildEquipmentProgression(
                        "weapon",
                        hero.WeaponRanks,
                        candidate.WeaponRequirements,
                        requireHp: false).UnsupportedReason) ? 0 : 1) +
                    (string.IsNullOrWhiteSpace(BuildEquipmentProgression(
                        "armour",
                        hero.ArmourRanks,
                        candidate.ArmourRequirements,
                        requireHp: true).UnsupportedReason) ? 0 : 1);
                return new HeroUpgradeCompatibility(
                    candidate,
                    equipmentFailures + unsupportedMissing,
                    expectedTreeIds.Keys.Count(combatTreeIds.Contains),
                    combatTreeIds.Count(treeId => !expectedTreeIds.ContainsKey(treeId)));
            })
            .OrderBy(item => item.HardFailureCount)
            .ThenByDescending(item => item.MatchedCombatTreeCount)
            .ThenBy(item => item.UnexpectedCombatTreeCount)
            .ToArray();
        if (ranked.Length == 0)
        {
            return null;
        }

        var best = ranked[0];
        if (best.HardFailureCount != 0)
        {
            return null;
        }

        return ranked.Skip(1).Any(item =>
            item.HardFailureCount == best.HardFailureCount &&
            item.MatchedCombatTreeCount == best.MatchedCombatTreeCount &&
            item.UnexpectedCombatTreeCount == best.UnexpectedCombatTreeCount)
            ? null
            : best.Definition;
    }
    private static IReadOnlyList<int> ReadEffectiveResolveLevelThresholds(
        IReadOnlyList<EffectiveContentFile> files,
        List<string> issues)
    {
        var matches = files
            .Where(file => file.RelativePath.Equals(
                "campaign/roster/roster.variables.json",
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matches.Length == 0)
        {
            issues.Add("No effective campaign/roster/roster.variables.json was found; only level-zero hero generation is available.");
            return [];
        }

        if (matches.Length > 1)
        {
            issues.Add(
                "Multiple effective root roster.variables files were found; only level-zero hero generation is available: " +
                string.Join(" | ", matches.Select(file => file.Path)));
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(
                File.ReadAllBytes(matches[0].Path),
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip
                });
            if (!document.RootElement.TryGetProperty("resolve_level_thresholds", out var thresholdsNode) ||
                thresholdsNode.ValueKind != JsonValueKind.Array)
            {
                issues.Add($"Resolve level thresholds are missing from '{matches[0].Path}'.");
                return [];
            }

            var thresholds = new List<int>();
            foreach (var item in thresholdsNode.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Number || !item.TryGetInt32(out var threshold))
                {
                    issues.Add($"Resolve level thresholds contain a non-integer value in '{matches[0].Path}'.");
                    return [];
                }

                thresholds.Add(threshold);
            }

            if (thresholds.Count == 0 || thresholds[0] != 0 ||
                thresholds.Any(value => value < 0) ||
                thresholds.Zip(thresholds.Skip(1), (left, right) => right > left).Any(increasing => !increasing))
            {
                issues.Add($"Resolve level thresholds are not a strictly increasing sequence beginning at zero in '{matches[0].Path}'.");
                return [];
            }

            return thresholds;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            issues.Add($"Failed to read resolve level thresholds '{matches[0].Path}': {ex.Message}");
            return [];
        }
    }

    private static HeroUpgradeDefinition ReadHeroUpgrade(EffectiveContentFile file)
    {
        var fileName = Path.GetFileName(file.Path);
        var heroClassId = fileName.EndsWith(HeroUpgradeSuffix, StringComparison.OrdinalIgnoreCase)
            ? fileName[..^HeroUpgradeSuffix.Length]
            : Path.GetFileNameWithoutExtension(fileName);
        var weaponRequirements = new Dictionary<string, int>(StringComparer.Ordinal);
        var armourRequirements = new Dictionary<string, int>(StringComparer.Ordinal);
        var upgradeTrees = new List<HeroUpgradeTreeDefinition>();
        using var document = JsonDocument.Parse(
            File.ReadAllBytes(file.Path),
            new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });
        if (document.RootElement.TryGetProperty("trees", out var trees) &&
            trees.ValueKind == JsonValueKind.Array)
        {
            foreach (var tree in trees.EnumerateArray())
            {
                if (tree.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var tags = ReadJsonStringArray(tree, "tags");
                HeroUpgradeTreeKind? kind = tags.Contains("weapon", StringComparer.OrdinalIgnoreCase)
                    ? HeroUpgradeTreeKind.Weapon
                    : tags.Contains("armour", StringComparer.OrdinalIgnoreCase)
                        ? HeroUpgradeTreeKind.Armour
                        : tags.Contains("combat_skill", StringComparer.OrdinalIgnoreCase)
                            ? HeroUpgradeTreeKind.CombatSkill
                            : null;
                if (kind is null)
                {
                    continue;
                }

                var treeId = ReadJsonString(tree, "id");
                if (string.IsNullOrWhiteSpace(treeId))
                {
                    throw new InvalidDataException(
                        $"A {string.Join('/', tags)} upgrade tree is missing its id.");
                }
                if (!tree.TryGetProperty("requirements", out var requirements) ||
                    requirements.ValueKind != JsonValueKind.Array)
                {
                    throw new InvalidDataException(
                        $"Upgrade tree '{treeId}' is missing its requirements array.");
                }

                var parsedRequirements = new Dictionary<string, int>(StringComparer.Ordinal);

                foreach (var requirement in requirements.EnumerateArray())
                {
                    var code = ReadJsonString(requirement, "code");
                    var prerequisiteLevel = ReadJsonInt(requirement, "prerequisite_resolve_level");
                    if (string.IsNullOrWhiteSpace(code) || prerequisiteLevel is null or < 0)
                    {
                        throw new InvalidDataException(
                            $"A {string.Join('/', tags)} requirement is missing code or prerequisite_resolve_level.");
                    }
                    if (code.Length != 1 || code[0] > 0x7F)
                    {
                        throw new InvalidDataException(
                            $"Upgrade tree '{treeId}' requirement code '{code}' cannot be represented " +
                            "losslessly by persist.upgrades; one ASCII character is required.");
                    }

                    if (parsedRequirements.TryGetValue(code, out var existing) && existing != prerequisiteLevel.Value)
                    {
                        throw new InvalidDataException(
                            $"Upgrade tree '{treeId}' requirement '{code}' has conflicting resolve prerequisites " +
                            $"{existing} and {prerequisiteLevel.Value}.");
                    }

                    parsedRequirements[code] = prerequisiteLevel.Value;
                }

                if (upgradeTrees.Any(existing =>
                        existing.Id.Equals(treeId, StringComparison.Ordinal)))
                {
                    throw new InvalidDataException($"Upgrade tree '{treeId}' is defined more than once.");
                }

                upgradeTrees.Add(new HeroUpgradeTreeDefinition(
                    treeId,
                    kind.Value,
                    parsedRequirements
                        .Select(pair => new HeroUpgradeRequirementDefinition(pair.Key, pair.Value))
                        .OrderBy(requirement => requirement.PrerequisiteResolveLevel)
                        .ThenBy(requirement => requirement.Code, StringComparer.Ordinal)
                        .ToArray()));

                var target = kind.Value switch
                {
                    HeroUpgradeTreeKind.Weapon => weaponRequirements,
                    HeroUpgradeTreeKind.Armour => armourRequirements,
                    _ => null
                };
                if (target is null)
                {
                    continue;
                }

                foreach (var requirement in parsedRequirements)
                {
                    if (target.TryGetValue(requirement.Key, out var existing) &&
                        existing != requirement.Value)
                    {
                        throw new InvalidDataException(
                            $"Upgrade requirement '{requirement.Key}' has conflicting resolve prerequisites " +
                            $"{existing} and {requirement.Value}.");
                    }

                    target[requirement.Key] = requirement.Value;
                }
            }
        }

        return new HeroUpgradeDefinition(
            heroClassId,
            weaponRequirements,
            armourRequirements,
            upgradeTrees,
            file.Source.Id,
            Path.GetFullPath(file.Path));
    }

    private static HeroProgressionBuildResult BuildHeroProgression(
        HeroCandidate hero,
        HeroUpgradeDefinition? upgrade,
        IReadOnlyList<int> resolveLevelThresholds)
    {
        var armourRankZero = hero.ArmourRanks.FirstOrDefault(rank => rank.Rank == 0);
        var levelZeroHp = armourRankZero?.Hp ?? hero.BaseHp;
        var fallback = levelZeroHp is > 0 and < double.PositiveInfinity
            ? new[] { new HeroLevelProfile(0, 0, 0, 0, levelZeroHp.Value) }
            : [];
        if (levelZeroHp is not (> 0 and < double.PositiveInfinity))
        {
            return new HeroProgressionBuildResult([], "缺少可验证的 0 级护甲 HP");
        }

        if (resolveLevelThresholds.Count == 0)
        {
            return new HeroProgressionBuildResult(fallback, "缺少有效的 resolve_level_thresholds");
        }

        var weapon = BuildEquipmentProgression(
            "武器",
            hero.WeaponRanks,
            upgrade?.WeaponRequirements,
            requireHp: false);
        var armour = BuildEquipmentProgression(
            "护甲",
            hero.ArmourRanks,
            upgrade?.ArmourRequirements,
            requireHp: true);
        var reason = string.Join(
            "；",
            new[] { weapon.UnsupportedReason, armour.UnsupportedReason }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
        if (!string.IsNullOrWhiteSpace(reason))
        {
            return new HeroProgressionBuildResult(fallback, reason);
        }

        var profiles = resolveLevelThresholds
            .Select((xp, level) =>
            {
                var weaponRank = weapon.Ranks
                    .Where(rank => rank.MinimumResolveLevel <= level)
                    .MaxBy(rank => rank.Rank)!;
                var armourRank = armour.Ranks
                    .Where(rank => rank.MinimumResolveLevel <= level)
                    .MaxBy(rank => rank.Rank)!;
                return new HeroLevelProfile(
                    level,
                    xp,
                    weaponRank.Rank,
                    armourRank.Rank,
                    armourRank.Hp!.Value);
            })
            .ToArray();
        return new HeroProgressionBuildResult(profiles, string.Empty);
    }

    private static EquipmentProgressionBuildResult BuildEquipmentProgression(
        string label,
        IReadOnlyList<HeroEquipmentRank> sourceRanks,
        IReadOnlyDictionary<string, int>? requirements,
        bool requireHp)
    {
        var ranks = sourceRanks.Count == 0 && !requireHp
            ? new[] { new HeroEquipmentRank(0, string.Empty, null) }
            : sourceRanks.OrderBy(rank => rank.Rank).ToArray();
        if (ranks.Length == 0 || ranks[0].Rank != 0)
        {
            return new EquipmentProgressionBuildResult([], $"{label}缺少 rank 0 定义");
        }

        for (var index = 0; index < ranks.Length; index++)
        {
            if (ranks[index].Rank != index)
            {
                return new EquipmentProgressionBuildResult([], $"{label} rank 必须从 0 连续定义");
            }

            if (requireHp && ranks[index].Hp is not (> 0 and < double.PositiveInfinity))
            {
                return new EquipmentProgressionBuildResult([], $"{label} rank {index} 缺少有效 HP");
            }
        }

        if (ranks.Length == 1 && (requirements is null || requirements.Count == 0))
        {
            return new EquipmentProgressionBuildResult(
                [new ResolvedEquipmentRank(0, 0, ranks[0].Hp)],
                string.Empty);
        }

        if (requirements is null || requirements.Count == 0)
        {
            return new EquipmentProgressionBuildResult([], $"{label}有多个 rank，但缺少有效 upgrade 模板");
        }

        var resolved = new List<ResolvedEquipmentRank>
        {
            new(0, 0, ranks[0].Hp)
        };
        var usedCodes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rank in ranks.Skip(1))
        {
            if (string.IsNullOrWhiteSpace(rank.RequirementCode) ||
                !requirements.TryGetValue(rank.RequirementCode, out var prerequisiteLevel))
            {
                return new EquipmentProgressionBuildResult(
                    [],
                    $"{label} rank {rank.Rank} 的 upgradeRequirementCode '{rank.RequirementCode}' 无法解析");
            }

            usedCodes.Add(rank.RequirementCode);
            resolved.Add(new ResolvedEquipmentRank(rank.Rank, prerequisiteLevel, rank.Hp));
        }

        var unusedCodes = requirements.Keys.Where(code => !usedCodes.Contains(code)).ToArray();
        if (unusedCodes.Length > 0)
        {
            return new EquipmentProgressionBuildResult(
                [],
                $"{label} upgrade 模板存在未对应 rank 的代码：{string.Join(", ", unusedCodes)}");
        }

        return new EquipmentProgressionBuildResult(resolved, string.Empty);
    }

}
