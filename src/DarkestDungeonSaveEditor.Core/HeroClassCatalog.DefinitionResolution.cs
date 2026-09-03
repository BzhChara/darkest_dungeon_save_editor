using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;

namespace DarkestDungeonSaveEditor.Core;

public static partial class HeroClassCatalog
{
    private static IReadOnlyList<EffectiveContentFile> ResolveFiles(
        IReadOnlyList<SourceFiles> sourceFiles,
        Func<SourceFileSet, IReadOnlyList<string>> selectFiles,
        string contentLabel,
        List<string> issues)
    {
        return ContentFileOverlay.Resolve(
            sourceFiles.SelectMany(item =>
                selectFiles(item.Files).Select(path => new ContentFileCandidate(item.Source, path))),
            contentLabel,
            issues);
    }

    private static void AddCandidate<T>(
        Dictionary<string, List<T>> candidates,
        string id,
        T candidate)
    {
        if (!candidates.TryGetValue(id, out var matches))
        {
            matches = [];
            candidates[id] = matches;
        }

        matches.Add(candidate);
    }

    private static IReadOnlyDictionary<string, T> ResolveUniqueDefinitions<T>(
        Dictionary<string, List<T>> candidates,
        IReadOnlyDictionary<string, ActiveContentSource> sourcesById,
        Func<T, string> getSource,
        Func<T, string> getSourcePath,
        Func<T, string> getSemanticSignature,
        string contentLabel,
        List<string> issues,
        bool reportConflicts = true)
    {
        var result = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in candidates)
        {
            var effective = SelectEffectiveDefinitions(
                pair.Value,
                sourcesById,
                getSource,
                getSourcePath,
                getSemanticSignature);
            pair.Value.Clear();
            pair.Value.AddRange(effective);
            if (effective.Count == 1)
            {
                result[pair.Key] = effective[0];
                continue;
            }

            if (reportConflicts)
            {
                issues.Add(
                    $"{contentLabel} '{pair.Key}' has conflicting definitions at the same effective priority and was left unresolved: " +
                    string.Join(
                        " | ",
                        effective.Select(candidate => $"{getSource(candidate)}:{getSourcePath(candidate)}")));
            }
        }

        return result;
    }
    private static IReadOnlyList<T> SelectEffectiveDefinitions<T>(
        IReadOnlyList<T> candidates,
        IReadOnlyDictionary<string, ActiveContentSource> sourcesById,
        Func<T, string> getSource,
        Func<T, string> getSourcePath,
        Func<T, string> getSemanticSignature)
    {
        var lastDeclarationByPath = candidates
            .GroupBy(getSourcePath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToArray();
        if (lastDeclarationByPath.Length <= 1)
        {
            return lastDeclarationByPath;
        }

        var highestPrioritySource = sourcesById[getSource(lastDeclarationByPath[0])];
        foreach (var candidate in lastDeclarationByPath.Skip(1))
        {
            var source = sourcesById[getSource(candidate)];
            if (ContentFileOverlay.ComparePriority(source, highestPrioritySource) > 0)
            {
                highestPrioritySource = source;
            }
        }

        var highestPriorityCandidates = lastDeclarationByPath
            .Where(candidate => ContentFileOverlay.ComparePriority(
                sourcesById[getSource(candidate)],
                highestPrioritySource) == 0)
            .ToArray();
        return highestPriorityCandidates
            .Select(getSemanticSignature)
            .Distinct(StringComparer.Ordinal)
            .Skip(1)
            .Any()
            ? highestPriorityCandidates
            : [highestPriorityCandidates[^1]];
    }

    private static string GetQuirkSignature(QuirkDefinition definition) => JsonSerializer.Serialize(new
    {
        definition.RandomChance,
        definition.IsPositive,
        definition.IsDisease,
        IncompatibleQuirks = SortSemanticValues(definition.IncompatibleQuirks),
        BuffIds = SortSemanticValues(definition.BuffIds),
        Tags = SortSemanticValues(definition.Tags),
        definition.RosterLimit,
        EvolutionSignature = definition.Evolution?.Signature
    });

    private static string GetBuffSignature(BuffDefinition definition) => JsonSerializer.Serialize(new
    {
        definition.StatType,
        definition.StatSubType,
        definition.Amount,
        definition.RuleType,
        definition.IsFalseRule,
        definition.RuleFloat,
        definition.RuleString
    });

    private static string GetHeroUpgradeSignature(HeroUpgradeDefinition definition) => JsonSerializer.Serialize(new
    {
        Weapon = definition.WeaponRequirements.OrderBy(pair => pair.Key, StringComparer.Ordinal),
        Armour = definition.ArmourRequirements.OrderBy(pair => pair.Key, StringComparer.Ordinal),
        Trees = definition.Trees
            .OrderBy(tree => tree.Id, StringComparer.Ordinal)
            .Select(tree => new
            {
                tree.Id,
                tree.Kind,
                Requirements = tree.Requirements
                    .OrderBy(requirement => requirement.PrerequisiteResolveLevel)
                    .ThenBy(requirement => requirement.Code, StringComparer.Ordinal)
            })
    });

    private static string GetRecruitEventSignature(RecruitEventGroup definition) => JsonSerializer.Serialize(
        definition.Recruits
            .OrderBy(recruit => recruit.HeroClass, StringComparer.OrdinalIgnoreCase)
            .ThenBy(recruit => recruit.Count)
            .Select(recruit => new { recruit.HeroClass, recruit.Count }));

    private static string GetHeroCandidateSignature(HeroCandidate candidate) => JsonSerializer.Serialize(new
    {
        candidate.CanSelectCombatSkills,
        candidate.SelectedCombatSkillsMax,
        candidate.Generation,
        candidate.BaseHp,
        candidate.WeaponRanks,
        candidate.ArmourRanks,
        candidate.ColourVariationCount,
        CombatSkillIds = SortSemanticValues(candidate.CombatSkillIds),
        CombatSkillLevels = candidate.CombatSkillLevels
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new
            {
                Id = pair.Key,
                Levels = pair.Value.OrderBy(level => level).ToArray()
            }),
        GuaranteedCombatSkillIds = SortSemanticValues(candidate.GuaranteedCombatSkillIds),
        IncompatibleInitialQuirkIds = SortSemanticValues(candidate.IncompatibleInitialQuirkIds),
        SkillEffects = candidate.SkillEffects
            .OrderBy(reference => reference.SkillId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(reference => reference.AttributeKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(reference => reference.EffectName, StringComparer.OrdinalIgnoreCase)
            .ToArray()
    });

    private static string[] SortSemanticValues(IEnumerable<string> values) => values
        .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
        .ToArray();
}
