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
        return NativeContentFileResolver.Resolve(
            sourceFiles.SelectMany(item =>
                selectFiles(item.Files).Select(path => new ContentFileCandidate(item.Source, path))).ToArray(),
            sourceFiles.Select(item => item.Source).ToArray(),
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

    private static IReadOnlyDictionary<string, T> ResolveOrderedDefinitions<T>(
        Dictionary<string, List<T>> candidates,
        Func<T, string> getId,
        Func<IReadOnlyList<T>, T> select,
        string contentLabel,
        List<string> issues)
    {
        // Files have already been overlaid in native enumeration order. Do not
        // choose a source again here: each resource has its own duplicate rule.
        var collisions = NativeResourceIdentity.FindCollisions(candidates.Values.SelectMany(group => group).Select(getId));
        var result = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var pair in candidates)
        {
            if (pair.Value.Any(value => collisions.Contains(getId(value))) ||
                pair.Value.Select(getId).Distinct(StringComparer.Ordinal).Skip(1).Any())
            {
                issues.Add($"{contentLabel} '{pair.Key}' has conflicting native identities and was left unresolved.");
                continue;
            }
            result[pair.Key] = select(pair.Value);
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
