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
        var requests = DiscoverActorIds(sources, directory, issues)
            .SelectMany(id => ActorOpenPaths(directory, id)).ToArray();
        return ResolveOpenRequests(sources, requests, "Actor definition", issues);
    }

    internal static IEnumerable<string> ActorOpenPaths(string directory, string id)
    {
        if (directory == "monsters" && id.Length < 2) yield break;
        var stem = directory == "heroes" ? $"heroes/{id}/{id}" : $"monsters/{id[..^2]}/{id}/{id}";
        foreach (var suffix in directory == "heroes"
                     ? new[] { ".info.darkest", ".art.darkest", ".override.darkest" }
                     : new[] { ".info.darkest", ".art.darkest" }) yield return stem + suffix;
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
        => ResolveCore(candidates, activeSources, contentLabel, issues, reportCaseDifferences, flags: 0);

    // OpenFile (0x140248040) tests the ORIGINAL request against each Mod's
    // case-sensitive manifest keys. Only physical directory devices use Windows
    // aliases. A DLC-prefixed Mod key cannot answer an unprefixed root request.
    internal static IReadOnlyList<EffectiveContentFile> ResolveOpenedFiles(
        IReadOnlyList<ActiveContentSource> sources,
        IEnumerable<string> requests,
        string contentLabel,
        List<string> issues)
        => ResolveOpenRequests(sources, requests, contentLabel, issues).Values.ToArray();

    private static IReadOnlyDictionary<string, EffectiveContentFile> ResolveOpenRequests(
        IReadOnlyList<ActiveContentSource> sources,
        IEnumerable<string> requests,
        string contentLabel,
        List<string> issues)
    {
        var comparer = Comparer<ActiveContentSource>.Create(ContentFileOverlay.ComparePriority);
        var ordered = sources.DistinctBy(source => source.Id, StringComparer.OrdinalIgnoreCase)
            .OrderBy(source => source, comparer).ToArray();
        var requestList = requests.Distinct(StringComparer.Ordinal).ToArray();
        var manifestRequests = requestList.Where(request => !request.StartsWith('>')).ToHashSet(StringComparer.Ordinal);
        var manifests = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        bool CanOpen(ActiveContentSource source, string path)
        {
            if (source.Kind is not ("local" or "workshop")) return File.Exists(Path.Combine(source.Directory, path));
            if (!manifests.TryGetValue(source.Id, out var keys))
            {
                var manifest = Path.Combine(source.Directory, "modfiles.txt");
                ModManifestFile.Require(manifest);
                keys = ModManifestFile.ReadEntries(manifest, raw =>
                    ModManifestPath.Extract(raw, "") is { } entry && manifestRequests.Contains(entry.Replace('\\', '/')) ? entry : null)
                    .Select(entry => entry.RelativePath.Replace('\\', '/')).ToHashSet(StringComparer.Ordinal);
                manifests.Add(source.Id, keys);
            }
            // A listed missing winner must remain selected so its consumer
            // reports the failure; do not silently revive lower definitions.
            return keys.Contains(path);
        }
        var result = new Dictionary<string, EffectiveContentFile>(StringComparer.Ordinal);
        foreach (var request in requestList)
        {
            var baseOnly = request.StartsWith('>');
            var path = baseOnly ? request[1..] : request;
            var eligible = ordered.Where(source => (!baseOnly || source.Kind == "base") && CanOpen(source, path)).ToArray();
            if (eligible.Length == 0) continue;
            var winner = eligible[^1];
            if (eligible.Length > 1 && comparer.Compare(eligible[^2], winner) == 0)
            {
                issues.Add($"{contentLabel} request '{request}' has multiple providers at the same unverified priority and was ignored.");
                continue;
            }
            // Preserve the requested spelling for actor IDs, even when Windows
            // opens a differently cased physical filename behind this path.
            var openedPath = Path.GetFullPath(Path.Combine(winner.Directory, path));
            var providers = eligible.Select(source => new ContentFileProvider(source.Id, Path.GetFullPath(Path.Combine(source.Directory, path)))).ToArray();
            result.Add(request, new EffectiveContentFile(winner, openedPath,
                ContentFileOverlay.NormalizeRelativePath(winner, openedPath)!,
                providers.Select(provider => provider.SourceId).ToArray(),
                providers.Select(provider => provider.Path).ToArray(), providers));
        }
        return result;
    }

    // EffectLibrary uses mode 1, flags 1 (0x1404E494D). An offset-zero
    // match is erased, then the new provider is appended at the END. A match
    // inside an already-prefixed provider appends without erasing that provider.
    internal static IReadOnlyList<EffectiveContentFile> ResolveEffectFiles(
        IReadOnlyList<ContentFileCandidate> candidates,
        IReadOnlyList<ActiveContentSource> activeSources,
        string contentLabel,
        List<string> issues)
        => ResolveCore(candidates, activeSources, contentLabel, issues, reportCaseDifferences: true, flags: 1);

    // PropLibrary (0x1404D87F7 / 0x1404D8A0A) and District (0x140559EB3)
    // use mode 1, flags 9.
    // Bit 8 prefixes Base paths with '>'; alternate paths retain their mount
    // prefix. Bit 1 only erases an offset-zero match, so a contained relative
    // tail appends these concrete providers instead of replacing a prior slot.
    internal static IReadOnlyList<EffectiveContentFile> ResolveAdditiveFiles(
        IReadOnlyList<ContentFileCandidate> candidates,
        IReadOnlyList<ActiveContentSource> activeSources,
        string contentLabel,
        List<string> issues)
        => ResolveCore(candidates, activeSources, contentLabel, issues, reportCaseDifferences: true,
            flags: 9);

    private static IReadOnlyList<EffectiveContentFile> ResolveCore(
        IReadOnlyList<ContentFileCandidate> candidates,
        IReadOnlyList<ActiveContentSource> activeSources,
        string contentLabel,
        List<string> issues,
        bool reportCaseDifferences,
        int flags)
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
            var slot = flags == 9 || MountSource(file).Kind == "base" ? -1
                : result.FindIndex(existing => existing.RelativePath.Contains(path, StringComparison.Ordinal));
            if (flags == 1 && slot >= 0)
            {
                // Base has an unprefixed request; other results have a native
                // device/mount prefix, making their matching offset positive.
                if (MountSource(result[slot]).Kind == "base" &&
                    result[slot].RelativePath.StartsWith(path, StringComparison.Ordinal)) result.RemoveAt(slot);
                slot = -1;
            }
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
        if (flags != 9 && result.Any(file => file.Source.Kind == "base"))
        {
            // Flags 0 and 1 leave Base paths unprefixed. OpenFile subsequently
            // searches alternate mounts (0x140248051-0x14024812C), even when
            // that mount replaced a different, containing enumeration slot.
            // Preserve the slots, including repeated reads of the same bytes.
            var opened = ResolveOpenRequests(activeSources,
                result.Where(file => file.Source.Kind == "base").Select(file => file.RelativePath), contentLabel, issues);
            for (var index = 0; index < result.Count; index++)
            {
                var prior = result[index];
                if (prior.Source.Kind != "base") continue;
                if (opened.TryGetValue(prior.RelativePath, out var file)) result[index] = file;
            }
        }
        return result;
    }

}
