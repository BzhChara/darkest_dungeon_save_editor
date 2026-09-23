using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;

namespace DarkestDungeonSaveEditor.Core;

public static partial class HeroClassCatalog
{
    private static HeroUpgradeTreeResolution ResolveHeroUpgradeTrees(
        IReadOnlyList<EffectiveContentFile> files,
        List<string> issues,
        bool orderKnown)
    {
        var result = new Dictionary<string, HeroUpgradeTreeCandidate>(StringComparer.Ordinal);
        var reads = new OrderedDefinitionReadState(firstMatch: false, orderKnown);
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
                    reads.RecordDefinition(id);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                reads.RecordFailure();
                issues.Add($"Failed to read hero upgrade definition '{file.Path}': {ex.Message}");
            }
        }

        var collisions = NativeResourceIdentity.FindCollisions(result.Keys);
        foreach (var id in collisions)
        {
            result[id] = result[id] with { UnsupportedReason = "升级树 ID 与其他 ID 的游戏哈希冲突" };
        }
        return new HeroUpgradeTreeResolution(result, reads);
    }

    private static HeroUpgradeDefinition BindHeroUpgradeTrees(
        HeroCandidate hero,
        HeroUpgradeTreeResolution resolution,
        List<string> issues)
    {
        var candidates = resolution.Candidates;
        var trees = new List<HeroUpgradeTreeDefinition>();
        void AddTree(string id, HeroUpgradeTreeKind kind)
        {
            if (!resolution.Reads.IsVerified(id) && !resolution.Reads.CanProveAbsence)
            {
                // Unknown is not a tree-less skill/equipment default. Even an
                // ID absent from the readable subset may occur in a failed slot.
                trees.Add(new HeroUpgradeTreeDefinition(id, kind, [])
                {
                    Source = "unresolved",
                    SourcePath = candidates.GetValueOrDefault(id)?.SourcePath ?? string.Empty,
                    UnsupportedReason = "升级树文件读取不完整，无法确认当前升级条件"
                });
                issues.Add($"Hero upgrade tree '{id}' is unavailable because its effective definition could not be verified.");
                return;
            }
            if (!candidates.TryGetValue(id, out var candidate))
            {
                // A computed target can alias an authored ID even when the
                // authored IDs do not collide with one another. It is not a missing tree.
                var hash = Loc2LocalizationReader.HashName(id);
                var alias = candidates.Values.FirstOrDefault(tree => Loc2LocalizationReader.HashName(tree.Id) == hash);
                if (alias is null) return;
                trees.Add(new HeroUpgradeTreeDefinition(id, kind, [])
                {
                    Source = alias.Source,
                    SourcePath = alias.SourcePath,
                    UnsupportedReason = $"升级购买目标 '{id}' 与定义 '{alias.Id}' 的游戏哈希冲突"
                });
                return;
            }
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
        try
        {
            var targets = HeroSkillPurchaseTargets.Equipment(hero.Id);
            AddTree(targets["weapon"], HeroUpgradeTreeKind.Weapon);
            AddTree(targets["armour"], HeroUpgradeTreeKind.Armour);
        }
        catch (InvalidOperationException error)
        {
            issues.Add($"Hero equipment upgrade targets are unavailable: {error.Message}");
        }
        try
        {
            foreach (var target in HeroSkillPurchaseTargets.Combat(hero.Id, hero.CombatSkillIds).Values)
                AddTree(target, HeroUpgradeTreeKind.CombatSkill);
        }
        catch (InvalidOperationException error)
        {
            // Keep the hero visible. The same target validation in generation
            // makes its preflight unavailable without aborting other classes.
            issues.Add($"Hero combat upgrade targets are unavailable: {error.Message}");
        }

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
        var parsedRequirements = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var requirement in NativeUpgradeRequirements.Read(tree, treeId))
        {
            var code = NativeJsonReader.TryGetProperty(requirement, "code", out var codeNode) && codeNode.ValueKind == JsonValueKind.String
                ? codeNode.GetString() ?? string.Empty
                : string.Empty;
            var prerequisiteLevel = ReadJsonInt(requirement, "prerequisite_resolve_level");
            if (string.IsNullOrWhiteSpace(code) || prerequisiteLevel is null or < 0)
                throw new InvalidDataException($"Upgrade tree '{treeId}' requirement is missing code or prerequisite_resolve_level.");
            if (!DsonSaveCodec.CanRoundTripPurchaseCode(code))
                throw new InvalidDataException($"Upgrade tree '{treeId}' requirement code '{code}' cannot be represented " +
                    "losslessly by the DSON codec; one printable ASCII character excluding double quote and backslash is required.");

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
        var equipment = CreateEquipmentDefinition(hero);
        if (equipment.Armour.FirstOrDefault()?.Hp is not (> 0 and < double.PositiveInfinity))
            return new HeroProgressionBuildResult([], "缺少可验证的 0 级护甲 HP");

        IReadOnlyDictionary<string, string> targets;
        try
        {
            targets = HeroSkillPurchaseTargets.Equipment(hero.Id);
        }
        catch (InvalidOperationException error)
        {
            return new HeroProgressionBuildResult([], error.Message);
        }

        var reason = string.Join(
            "；",
            new[]
            {
                string.Join("；", (upgrade?.Trees ?? [])
                    .Where(tree => tree.Kind is HeroUpgradeTreeKind.Weapon or HeroUpgradeTreeKind.Armour)
                    .Select(tree => tree.UnsupportedReason).Where(value => !string.IsNullOrWhiteSpace(value))),
                ValidateEquipmentRequirements("武器", equipment.Weapon, upgrade?.WeaponRequirements, false),
                ValidateEquipmentRequirements("护甲", equipment.Armour, upgrade?.ArmourRequirements, true),
                resolveLevelThresholds.Count == 0 ? "缺少有效的 resolve_level_thresholds" : string.Empty
            }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
        // Preserve the existing level-zero-only policy for incomplete templates,
        // but even that profile must reflect free ranks and actual level-zero buys.
        var thresholds = reason.Length == 0 ? resolveLevelThresholds : new[] { 0 };
        var profiles = thresholds
            .Select((xp, level) =>
            {
                var purchases = (upgrade?.Trees ?? [])
                    .Where(tree => tree.Kind is HeroUpgradeTreeKind.Weapon or HeroUpgradeTreeKind.Armour)
                    .SelectMany(tree => tree.Requirements.Where(r => r.PrerequisiteResolveLevel <= level)
                        .Select(r => new HeroUpgradePurchase(tree.Id, r.Code))).ToArray();
                var weaponRank = HeroEquipmentProgression.Resolve(equipment.Weapon, targets["weapon"], purchases);
                var armourRank = HeroEquipmentProgression.Resolve(equipment.Armour, targets["armour"], purchases);
                return new HeroLevelProfile(
                    level,
                    xp,
                    weaponRank.Rank,
                    armourRank.Rank,
                    armourRank.Hp ?? double.NaN);
            })
            .Where(profile => profile.ArmourHp is > 0 and < double.PositiveInfinity)
            .ToArray();
        return new HeroProgressionBuildResult(profiles, reason);
    }

    private static HeroEquipmentDefinition CreateEquipmentDefinition(HeroCandidate hero) => new(
        hero.WeaponRanks.Count == 0 ? [new HeroEquipmentRank(0, 0, null)] : hero.WeaponRanks,
        hero.ArmourRanks);

    private static string ValidateEquipmentRequirements(
        string label,
        IReadOnlyList<HeroEquipmentRank> ranks,
        IReadOnlyDictionary<string, int>? requirements,
        bool requireHp)
    {
        for (var index = 0; index < ranks.Count; index++)
        {
            if (ranks[index].Rank != index)
                return $"{label} rank 必须从 0 连续定义";

            if (requireHp && ranks[index].Hp is not (> 0 and < double.PositiveInfinity))
                return $"{label} rank {index} 缺少有效 HP";

            var code = ranks[index].RequirementCode;
            if (code == 0) continue;
            if (code > 0x7F)
                return $"{label} rank {index} 的购买码 0x{code:X2} 不能无损写入存档";
            if (requirements is null || !requirements.ContainsKey(((char)code).ToString()))
                return $"{label} rank {index} 的 upgradeRequirementCode '{(char)code}' 无法解析";
        }
        return string.Empty;
    }

}
