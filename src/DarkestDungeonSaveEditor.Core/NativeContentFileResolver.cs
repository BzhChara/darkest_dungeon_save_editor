namespace DarkestDungeonSaveEditor.Core;

// Windows x64 build 27890, StorageManager::IO_FindFiles(flags=0).
// This is file enumeration/overlay order; individual resource lookup policies
// (inventory first match, quirk last match, etc.) are applied by the consumers.
// DLC application positions still come from ActiveContentSource; see the
// explicitly guarded multi-DLC cases in BattleEncounterCatalog.RuntimeOrder.
internal static class NativeContentFileResolver
{
    private static IReadOnlyList<string> EnumeratePhysicalActorFiles(ActiveContentSource source, string directory)
    {
        return new[] { source.Directory }
            .Select(root => Path.Combine(root, directory)).Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "*.darkest", SearchOption.AllDirectories))
            .Where(path => path.EndsWith(".info.darkest", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".art.darkest", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".override.darkest", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    internal static IReadOnlyDictionary<string, EffectiveContentFile> ResolveActorFiles(
        IReadOnlyList<ActiveContentSource> sources, string directory, List<string> issues)
    {
        // ActorClass opens a constructed path, not the path that discovered its
        // ID. A manifest still limits eligible Mod providers for that lookup
        // (also verified by the existing-hero manifest A/B game experiment).
        var prefixes = ContentFileOverlay.GetEnabledDlcPrefixes(sources);
        var candidates = sources.SelectMany(source => EnumerateActorOpenFiles(source, prefixes, directory, issues)
            .Select(path => new ContentFileCandidate(source, path))).ToArray();
        return Resolve(candidates, sources, "Actor definition", issues, reportCaseDifferences: false).ToDictionary(file =>
        {
            var prefix = prefixes.OrderByDescending(value => value.Length).FirstOrDefault(value =>
                file.RelativePath.StartsWith(value + "/", StringComparison.OrdinalIgnoreCase));
            return prefix is null ? file.RelativePath : file.RelativePath[(prefix.Length + 1)..];
        }, StringComparer.OrdinalIgnoreCase);
    }

    internal static IReadOnlyList<string> EnumerateActorOpenFiles(ActiveContentSource source,
        IReadOnlyList<string> prefixes, string directory, List<string> issues)
    {
        if (source.Kind is "local" or "workshop")
            return ContentFileDiscovery.Enumerate(source, prefixes, issues, "Actor definition",
                new ContentFileRule(directory, "*.info.darkest"),
                new ContentFileRule(directory, "*.art.darkest"),
                new ContentFileRule(directory, "*.override.darkest"));
        // A direct open on a directory device need not have been enumerated.
        return EnumeratePhysicalActorFiles(source, directory);
    }

    internal static IReadOnlySet<string> DiscoverActorIds(IReadOnlyList<ActiveContentSource> sources,
        string directory, List<string> issues)
    {
        var prefixes = ContentFileOverlay.GetEnabledDlcPrefixes(sources);
        return sources.SelectMany(source => EnumerateActorInfoFiles(source, prefixes, directory, issues)
                .Select(path => ReadDiscoveredActorId(Path.GetRelativePath(source.Directory, path))))
            .ToHashSet(StringComparer.Ordinal);
    }

    internal static IReadOnlyList<string> EnumerateActorInfoFiles(ActiveContentSource source,
        IReadOnlyList<string> prefixes, string directory, List<string> issues) =>
        ContentFileDiscovery.EnumerateQuery(source, prefixes, issues, "Actor discovery",
            path => NativeResourceFileRules.IsActorInfoFile(path, prefixes, directory,
                source.Kind is "local" or "workshop"), new ContentFileRule(directory, "*darkest"));

    internal static string ReadDiscoveredActorId(string relativePath)
    {
        // The discovery loader removes the last dot twice, then the directory.
        // Hero's unescaped second dot also accepts x.seed.infoXdarkest, which
        // registers x, not x.seed. Canonical OpenFile remains a separate query.
        var path = relativePath.Replace('\\', '/');
        for (var index = 0; index < 2; index++)
            if (path.LastIndexOf('.') is var dot && dot >= 0) path = path[..dot];
        return path[(path.LastIndexOf('/') + 1)..];
    }

    public static IReadOnlyList<EffectiveContentFile> Resolve(
        IReadOnlyList<ContentFileCandidate> candidates,
        IReadOnlyList<ActiveContentSource> activeSources,
        string contentLabel,
        List<string> issues,
        bool reportCaseDifferences = true)
    {
        var sources = candidates.Select(candidate => candidate.Source)
            .DistinctBy(source => source.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(source => source.Id, StringComparer.OrdinalIgnoreCase);
        var sourceComparer = Comparer<ActiveContentSource>.Create(ContentFileOverlay.ComparePriority);
        var files = ContentFileOverlay.Resolve(candidates, contentLabel, issues);
        var dlcPrefixes = ContentFileOverlay.GetEnabledDlcPrefixes(activeSources);
        string? MountPrefix(string path) => dlcPrefixes.OrderByDescending(prefix => prefix.Length)
            .FirstOrDefault(prefix => path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase));
        string MountedPath(string path) => MountPrefix(path) is { } prefix ? path[(prefix.Length + 1)..] : path;
        string ProviderPath(ContentFileProvider provider) => ContentFileOverlay.NormalizeRelativePath(
            sources[provider.SourceId], provider.Path)!;
        string SlotPath(EffectiveContentFile file) => ProviderPath(file.Providers[0]);
        ActiveContentSource MountSource(EffectiveContentFile file) => MountPrefix(file.RelativePath) is { } prefix
            ? activeSources.First(source => source.VirtualPathPrefix.Equals(prefix, StringComparison.OrdinalIgnoreCase))
            : sources[file.Providers[0].SourceId];
        // Root providers must replay at their own mount positions. Merely
        // placing a root Mod's final bytes in the Base slot lets a later DLC
        // alias incorrectly replace them before the root Mod is applied.
        var ordered = files.SelectMany(file => MountPrefix(file.RelativePath) is not null
                ? new[] { file }
                : file.Providers.Select(provider => file with
                {
                    Source = sources[provider.SourceId],
                    Path = provider.Path,
                    RelativePath = ProviderPath(provider),
                    Providers = [provider],
                    ProviderSources = [provider.SourceId],
                    ProviderPaths = [provider.Path]
                }).ToArray())
            .OrderBy(MountSource, sourceComparer)
            .ThenByDescending(file => SlotPath(file).Count(character => character == '/'))
            .ThenBy(SlotPath, StringComparer.Ordinal)
            .ToArray();
        // IO_FindFiles compares paths relative to the alternate mount. A root
        // Mod's dungeons/x/file overrides DLC-prefix/dungeons/x/file in place.
        // Preserve the authored winning path for Bridge output and provenance.
        var slots = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var result = new List<EffectiveContentFile>();
        foreach (var file in ordered)
        {
            var path = MountedPath(file.RelativePath);
            if (!slots.TryGetValue(path, out var slot))
            {
                slots.Add(path, result.Count);
                result.Add(file);
                continue;
            }
            var prior = result[slot];
            if (reportCaseDifferences && !MountedPath(prior.RelativePath).Equals(path, StringComparison.Ordinal))
                issues.Add($"{contentLabel} mount paths differ only in case; native matching is unverified: {path}");
            var providers = prior.Providers.Concat(file.Providers)
                .DistinctBy(provider => $"{provider.SourceId}\n{provider.Path}", StringComparer.OrdinalIgnoreCase).ToArray();
            result[slot] = file with
            {
                Providers = providers,
                ProviderSources = providers.Select(provider => provider.SourceId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                ProviderPaths = providers.Select(provider => provider.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            };
        }
        return result;
    }

}
