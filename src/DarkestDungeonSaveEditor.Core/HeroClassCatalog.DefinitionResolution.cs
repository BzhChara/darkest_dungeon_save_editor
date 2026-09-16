namespace DarkestDungeonSaveEditor.Core;

public static partial class HeroClassCatalog
{
    private static IReadOnlyList<EffectiveContentFile> ResolveFiles(
        IReadOnlyList<SourceFiles> sourceFiles,
        Func<SourceFileSet, IReadOnlyList<string>> selectFiles,
        string contentLabel,
        List<string> issues,
        string? openPath = null,
        bool effects = false)
    {
        var candidates = sourceFiles.SelectMany(item =>
            selectFiles(item.Files).Select(path => new ContentFileCandidate(item.Source, path))).ToArray();
        var sources = sourceFiles.Select(item => item.Source).ToArray();
        return openPath is not null
            ? NativeContentFileResolver.ResolveOpenedFiles(sources, [openPath], contentLabel, issues)
            : effects ? NativeContentFileResolver.ResolveEffectFiles(candidates, sources, contentLabel, issues)
            : NativeContentFileResolver.Resolve(candidates, sources, contentLabel, issues);
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
        List<string> issues,
        Func<string, uint>? hashIdentity = null)
    {
        // Files have already been overlaid in native enumeration order. Do not
        // choose a source again here: each resource has its own duplicate rule.
        var collisions = NativeResourceIdentity.FindCollisions(candidates.Values.SelectMany(group => group),
            getId, value => (hashIdentity ?? Loc2LocalizationReader.HashName)(getId(value)));
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
}
