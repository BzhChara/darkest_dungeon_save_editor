namespace DarkestDungeonSaveEditor.Core;

// Windows x64 build 27890, consumer-specific IO_FindFiles and canonical opens.
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
        return ResolveOpenedFiles(candidates, sources, "Actor definition", issues, reportCaseDifferences: false).ToDictionary(file =>
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
        => ResolveCore(candidates, activeSources, contentLabel, issues, reportCaseDifferences, exactPaths: false, appendOnly: false);

    // Constructed OpenFile paths do not consume an IO_FindFiles result list.
    // Keep their Windows path aliases and mounted same-path provider election.
    internal static IReadOnlyList<EffectiveContentFile> ResolveOpenedFiles(
        IReadOnlyList<ContentFileCandidate> candidates,
        IReadOnlyList<ActiveContentSource> activeSources,
        string contentLabel,
        List<string> issues,
        bool reportCaseDifferences = true)
        => ResolveCore(candidates, activeSources, contentLabel, issues, reportCaseDifferences, exactPaths: true, appendOnly: false);

    // PropLibrary uses mode 1, flags 9 (0x1404D87F7 / 0x1404D8A0A).
    // Bit 8 prefixes Base paths with '>'; alternate paths retain their mount
    // prefix. Bit 1 only erases an offset-zero match, so a contained relative
    // tail appends these concrete providers instead of replacing a prior slot.
    internal static IReadOnlyList<EffectiveContentFile> ResolveAdditiveFiles(
        IReadOnlyList<ContentFileCandidate> candidates,
        IReadOnlyList<ActiveContentSource> activeSources,
        string contentLabel,
        List<string> issues)
        => ResolveCore(candidates, activeSources, contentLabel, issues, reportCaseDifferences: true,
            exactPaths: false, appendOnly: true);

    private static IReadOnlyList<EffectiveContentFile> ResolveCore(
        IReadOnlyList<ContentFileCandidate> candidates,
        IReadOnlyList<ActiveContentSource> activeSources,
        string contentLabel,
        List<string> issues,
        bool reportCaseDifferences,
        bool exactPaths,
        bool appendOnly)
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
        // Mode 1, flags 0: 0x140247C0A calls case-sensitive strstr on each
        // existing path, then replaces the FIRST match in place. A nested
        // path can contain the entire new relative path without equalling it.
        // Base establishes the initial list directly; only alternate mounts
        // execute this merge. Keep canonical opens on their separate rule.
        var spellings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<EffectiveContentFile>();
        foreach (var file in ordered)
        {
            var path = MountedPath(file.RelativePath);
            if (reportCaseDifferences && spellings.TryGetValue(path, out var spelling) &&
                !spelling.Equals(path, StringComparison.Ordinal))
                issues.Add($"{contentLabel} mount paths differ only in case; native matching is unverified: {path}");
            spellings[path] = path;
            var slot = exactPaths
                ? result.FindIndex(existing => MountedPath(existing.RelativePath).Equals(path, StringComparison.OrdinalIgnoreCase))
                : appendOnly || MountSource(file).Kind == "base" ? -1
                : result.FindIndex(existing => existing.RelativePath.Contains(path, StringComparison.Ordinal));
            if (slot < 0)
            {
                result.Add(file);
                continue;
            }
            var prior = result[slot];
            var providers = prior.Providers.Concat(file.Providers)
                .DistinctBy(provider => $"{provider.SourceId}\n{provider.Path}", StringComparer.OrdinalIgnoreCase).ToArray();
            result[slot] = file with
            {
                Providers = providers,
                ProviderSources = providers.Select(provider => provider.SourceId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                ProviderPaths = providers.Select(provider => provider.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            };
        }
        if (!exactPaths && !appendOnly && result.Any(file => file.Source.Kind == "base"))
        {
            // Flags 0 leaves Base paths unprefixed. OpenFile subsequently
            // searches alternate mounts (0x140248051-0x14024812C), even when
            // that mount replaced a different, containing enumeration slot.
            // Preserve the slots, including repeated reads of the same bytes.
            var openCandidates = candidates.Select(candidate =>
                    (Candidate: candidate, Path: ContentFileOverlay.NormalizeRelativePath(candidate.Source, candidate.Path)))
                .Where(entry => entry.Path is not null)
                .GroupBy(entry => MountedPath(entry.Path!), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < result.Count; index++)
            {
                var prior = result[index];
                if (prior.Source.Kind != "base" || !openCandidates.TryGetValue(prior.RelativePath, out var matches)) continue;
                // 0x1402480DC/0x1402393C0 uses the original request against
                // case-sensitive Mod manifest keys. A DLC-prefixed Mod path
                // cannot answer a root request; physical DLC/mode fallback
                // still uses the Windows directory device.
                var eligible = matches.Where(entry => entry.Candidate.Source.Kind is not ("local" or "workshop") ||
                        entry.Path!.Equals(prior.RelativePath, StringComparison.Ordinal))
                    .Select(entry => entry.Candidate).ToArray();
                var opened = ResolveOpenedFiles(eligible, activeSources, contentLabel, issues, reportCaseDifferences: false);
                if (opened.Count == 1) result[index] = opened[0];
            }
        }
        return result;
    }

}
