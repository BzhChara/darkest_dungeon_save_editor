namespace DarkestDungeonSaveEditor.Core;

public static partial class HeroClassCatalog
{
    private static IReadOnlyDictionary<uint, QuirkDefinition> ResolveQuirkDefinitions(
        IReadOnlyList<QuirkDefinition> candidates, OrderedDefinitionReadState<uint> reads, List<string> issues)
    {
        var result = new Dictionary<uint, QuirkDefinition>();
        foreach (var group in candidates.GroupBy(quirk => NativeResourceIdentity.HashCString(quirk.Id)))
        {
            // Native lookup walks the whole vector. Group the original sequence,
            // so A/B/A selects the final A even when the strings share one hash.
            var winner = group.Last();
            result.Add(group.Key, winner with
            {
                Id = NativeJsonReader.CString(winner.Id),
                AllSources = group.SelectMany(quirk => quirk.AllSources).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            });
            if (reads.IsVerified(group.Key) && NativeResourceIdentity.DescribeHashResolution(
                    group, winner, quirk => quirk.Id, quirk => quirk.SourcePath, "Catalog_QuirkHashResolution") is { } notice)
                issues.Add(notice);
        }
        return result;
    }

    private static IReadOnlyDictionary<uint, EffectQuirkAssignment> ResolveEffectDefinitions(
        IReadOnlyList<EffectQuirkAssignment> candidates, OrderedDefinitionReadState<uint> reads, List<string> issues)
    {
        var result = new Dictionary<uint, EffectQuirkAssignment>();
        foreach (var group in candidates.GroupBy(effect => NativeResourceIdentity.HashCString(effect.Name)))
        {
            if (!reads.IsVerified(group.Key)) continue;
            // Effects reuse a hash-keyed object. Only the disease field is consumed
            // here: omission retains its value, including a previous explicit "".
            var winner = group.Last(effect => effect.QuirkId is not null);
            result.Add(group.Key, winner);
            if (NativeResourceIdentity.DescribeHashResolution(
                    group, winner, effect => effect.Name, effect => effect.SourcePath, "Catalog_EffectHashResolution") is { } notice)
                issues.Add(notice);
        }
        return result;
    }

    private static IReadOnlyDictionary<uint, BuffDefinition> ResolveBuffDefinitions(
        IReadOnlyList<BuffDefinition> candidates,
        IReadOnlySet<uint> verifiedHashes,
        List<string> issues)
    {
        // Native Buff loading assigns a complete object to a uint32-keyed slot.
        // Group the original sequence by hash, not by textual ID: A/B/A must
        // retain the final A, including when A and B are different hash aliases.
        var result = new Dictionary<uint, BuffDefinition>();
        foreach (var group in candidates.GroupBy(buff => NativeResourceIdentity.HashCString(buff.Id)))
        {
            if (!verifiedHashes.Contains(group.Key)) continue;
            var winner = group.Last();
            result.Add(group.Key, winner);
            if (group.Select(buff => buff.Id).Distinct(StringComparer.Ordinal).Skip(1).Any())
            {
                var providers = string.Join("; ", group.Select(buff => (buff.Id, buff.SourcePath))
                    .Distinct().Select(provider => $"'{provider.Id}' ('{provider.SourcePath}')"));
                issues.Add(CatalogIssueCode.BuffHashOverride + EditorText.Format(
                    "HeroClassCatalog_BuffHashOverride",
                    group.Key.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    providers, winner.Id, winner.SourcePath));
            }
        }
        return result;
    }

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
