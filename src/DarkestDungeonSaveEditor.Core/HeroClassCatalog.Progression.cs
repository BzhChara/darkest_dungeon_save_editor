using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;

namespace DarkestDungeonSaveEditor.Core;

public static partial class HeroClassCatalog
{
    private static IReadOnlyDictionary<string, HeroUpgradeTreeCandidate> ResolveHeroUpgradeTrees(
        IReadOnlyList<EffectiveContentFile> files,
        List<string> issues)
    {
        var result = new Dictionary<string, HeroUpgradeTreeCandidate>(StringComparer.Ordinal);
        // IO_FindFiles supplies effective files in native order. Upgrade lookups
        // (0x1406741C0 / 0x1404703D0, build 27890) retain the last matching tree,
        // including duplicates within one file; filenames do not identify heroes.
        foreach (var file in files)
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllBytes(file.Path), new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip
                });
                if (document.RootElement.ValueKind != JsonValueKind.Object ||
                    !NativeJsonReader.TryGetProperty(document.RootElement, "trees", out var trees) ||
                    trees.ValueKind != JsonValueKind.Array)
                {
                    throw new InvalidDataException("Upgrade definition is missing its trees array.");
                }

                foreach (var tree in trees.EnumerateArray())
                {
                    // Native IDs are hashed verbatim; display-text trimming would
                    // merge distinct trees and change their effective definition.
                    var id = tree.ValueKind == JsonValueKind.Object &&
                             NativeJsonReader.TryGetProperty(tree, "id", out var idNode) && idNode.ValueKind == JsonValueKind.String
                        ? idNode.GetString() ?? string.Empty
                        : string.Empty;
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        issues.Add($"Upgrade tree is missing its id in '{file.Path}'.");
                        continue;
                    }

                    IReadOnlyList<HeroUpgradeRequirementDefinition> requirements = [];
                    var reason = string.Empty;
                    try
                    {
                        requirements = ReadHeroUpgradeRequirements(tree, id);
                    }
                    catch (InvalidDataException ex)
                    {
                        // Keep the winning ID occupied. It must not expose an
                        // earlier definition or be synthesized as a tree-less skill.
                        reason = ex.Message;
                    }
                    result[id] = new HeroUpgradeTreeCandidate(
                        id, requirements, file.Source.Id, Path.GetFullPath(file.Path), reason);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                issues.Add($"Failed to read hero upgrade definition '{file.Path}': {ex.Message}");
            }
        }

        var collisions = NativeResourceIdentity.FindCollisions(result.Keys);
        foreach (var id in collisions)
        {
            result[id] = result[id] with { UnsupportedReason = "升级树 ID 与其他 ID 的游戏哈希冲突" };
        }
        return result;
    }

    private static HeroUpgradeDefinition BindHeroUpgradeTrees(
        HeroCandidate hero,
        IReadOnlyDictionary<string, HeroUpgradeTreeCandidate> candidates,
        List<string> issues)
    {
        var trees = new List<HeroUpgradeTreeDefinition>();
        void AddTree(string id, HeroUpgradeTreeKind kind)
        {
            if (!candidates.TryGetValue(id, out var candidate)) return;
            if (!string.IsNullOrWhiteSpace(candidate.UnsupportedReason))
                issues.Add($"Hero upgrade tree '{id}' is unavailable: {candidate.UnsupportedReason} ({candidate.SourcePath})");
            // The caller constructs the ID. Tags and filenames do not change
            // which tree the game's skill/equipment lookup returns.
            trees.Add(new HeroUpgradeTreeDefinition(id, kind, candidate.Requirements)
            {
                Source = candidate.Source,
                SourcePath = candidate.SourcePath,
                UnsupportedReason = candidate.UnsupportedReason
            });
        }
        AddTree($"{hero.Id}.weapon", HeroUpgradeTreeKind.Weapon);
        AddTree($"{hero.Id}.armour", HeroUpgradeTreeKind.Armour);
        foreach (var skillId in hero.CombatSkillIds.Distinct(StringComparer.Ordinal))
            AddTree($"{hero.Id}.{skillId}", HeroUpgradeTreeKind.CombatSkill);

        IReadOnlyDictionary<string, int> Requirements(HeroUpgradeTreeKind kind) => trees
            .Where(tree => tree.Kind == kind)
            .SelectMany(tree => tree.Requirements)
            .ToDictionary(requirement => requirement.Code, requirement => requirement.PrerequisiteResolveLevel, StringComparer.Ordinal);
        return new HeroUpgradeDefinition(
            Requirements(HeroUpgradeTreeKind.Weapon), Requirements(HeroUpgradeTreeKind.Armour), trees);
    }

    private static IReadOnlyList<int> ReadEffectiveResolveLevelThresholds(
        IReadOnlyList<EffectiveContentFile> files,
        IReadOnlyList<string> enabledDlcPrefixes,
        List<string> issues)
    {
        // Effective files retain the authored path for provenance. Compare
        // the canonical open against its mounted path, including DLC aliases.
        var matches = files
            .Where(file => NativeResourceFileRules.MountedPath(file.RelativePath, enabledDlcPrefixes).Equals(
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
            if (!NativeJsonReader.TryGetProperty(document.RootElement, "resolve_level_thresholds", out var thresholdsNode) ||
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

    private static IReadOnlyList<HeroUpgradeRequirementDefinition> ReadHeroUpgradeRequirements(JsonElement tree, string treeId)
    {
        if (!NativeJsonReader.TryGetProperty(tree, "requirements", out var requirements) || requirements.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException($"Upgrade tree '{treeId}' is missing its requirements array.");
        }

        var parsedRequirements = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var requirement in requirements.EnumerateArray())
        {
            if (requirement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException($"Upgrade tree '{treeId}' contains a non-object requirement.");
            var code = NativeJsonReader.TryGetProperty(requirement, "code", out var codeNode) && codeNode.ValueKind == JsonValueKind.String
                ? codeNode.GetString() ?? string.Empty
                : string.Empty;
            var prerequisiteLevel = ReadJsonInt(requirement, "prerequisite_resolve_level");
            if (string.IsNullOrWhiteSpace(code) || prerequisiteLevel is null or < 0)
                throw new InvalidDataException($"Upgrade tree '{treeId}' requirement is missing code or prerequisite_resolve_level.");
            if (code.Length != 1 || code[0] > 0x7F)
                throw new InvalidDataException($"Upgrade tree '{treeId}' requirement code '{code}' cannot be represented " +
                    "losslessly by persist.upgrades; one ASCII character is required.");

            if (parsedRequirements.TryGetValue(code, out var existing) && existing != prerequisiteLevel.Value)
                throw new InvalidDataException($"Upgrade tree '{treeId}' requirement '{code}' has conflicting resolve prerequisites " +
                    $"{existing} and {prerequisiteLevel.Value}.");
            parsedRequirements[code] = prerequisiteLevel.Value;
        }

        return parsedRequirements.Select(pair => new HeroUpgradeRequirementDefinition(pair.Key, pair.Value))
            .OrderBy(requirement => requirement.PrerequisiteResolveLevel)
            .ThenBy(requirement => requirement.Code, StringComparer.Ordinal)
            .ToArray();
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
