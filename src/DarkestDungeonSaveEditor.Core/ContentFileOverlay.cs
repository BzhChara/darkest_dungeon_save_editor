namespace DarkestDungeonSaveEditor.Core;

internal sealed record ContentFileCandidate(
    ActiveContentSource Source,
    string Path);

internal sealed record ContentFileProvider(
    string SourceId,
    string Path);

internal sealed record EffectiveContentFile(
    ActiveContentSource Source,
    string Path,
    string RelativePath,
    IReadOnlyList<string> ProviderSources,
    IReadOnlyList<string> ProviderPaths,
    IReadOnlyList<ContentFileProvider> Providers);

internal static class ContentFileOverlay
{
    public static IReadOnlyList<string> GetEnabledDlcPrefixes(IEnumerable<ActiveContentSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        return sources
            .Where(source => GetLayer(source.Kind) == 1 && !string.IsNullOrWhiteSpace(source.VirtualPathPrefix))
            .Select(source => NormalizeVirtualPath(source.VirtualPathPrefix))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(prefix => prefix, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static bool IsRootOrEnabledDlcPath(
        string relativePath,
        string contentDirectory,
        IReadOnlyList<string> enabledDlcPrefixes)
    {
        var normalizedPath = NormalizeVirtualPath(relativePath);
        var normalizedDirectory = NormalizeVirtualPath(contentDirectory).TrimEnd('/');
        if (normalizedPath.StartsWith($"{normalizedDirectory}/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return enabledDlcPrefixes.Any(prefix =>
            normalizedPath.StartsWith(
                $"{prefix.TrimEnd('/')}/{normalizedDirectory}/",
                StringComparison.OrdinalIgnoreCase));
    }

    public static int ComparePriority(ActiveContentSource left, ActiveContentSource right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return GetPriority(left).CompareTo(GetPriority(right));
    }

    public static IReadOnlyList<EffectiveContentFile> Resolve(
        IEnumerable<ContentFileCandidate> candidates,
        string contentLabel,
        List<string> issues)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(issues);

        var byRelativePath = new Dictionary<string, List<ResolvedCandidate>>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            var fullPath = Path.GetFullPath(candidate.Path);
            var relativePath = NormalizeRelativePath(candidate.Source, fullPath);
            if (relativePath is null)
            {
                issues.Add($"Ignored {contentLabel} path outside its active content source: {fullPath}");
                continue;
            }

            if (!byRelativePath.TryGetValue(relativePath, out var matches))
            {
                matches = [];
                byRelativePath[relativePath] = matches;
            }

            matches.Add(new ResolvedCandidate(candidate.Source, fullPath, relativePath));
        }

        var effectiveFiles = new List<EffectiveContentFile>();
        foreach (var pair in byRelativePath.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            var highestPriority = pair.Value
                .Select(candidate => GetPriority(candidate.Source))
                .Max();
            var winners = pair.Value
                .Where(candidate => GetPriority(candidate.Source) == highestPriority)
                .OrderBy(candidate => candidate.Source.Id, StringComparer.OrdinalIgnoreCase)
                .ThenBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (winners
                    .Select(candidate => candidate.Source.Id)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Skip(1)
                    .Any())
            {
                issues.Add(
                    $"{contentLabel} '{pair.Key}' has multiple providers at the same unverified priority and was ignored: " +
                    string.Join(" | ", winners.Select(candidate => $"{candidate.Source.Id}:{candidate.Path}")));
                continue;
            }

            var winner = winners[0];
            var providers = pair.Value
                .OrderBy(candidate => GetApplicationOrder(candidate.Source))
                .ThenBy(candidate => candidate.Source.Id, StringComparer.OrdinalIgnoreCase)
                .ThenBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
                .Select(candidate => new ContentFileProvider(candidate.Source.Id, candidate.Path))
                .DistinctBy(
                    provider => $"{provider.SourceId}\n{provider.Path}",
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var providerSources = providers
                .Select(provider => provider.SourceId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var providerPaths = providers
                .Select(provider => provider.Path)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            effectiveFiles.Add(new EffectiveContentFile(
                winner.Source,
                winner.Path,
                winner.RelativePath,
                providerSources,
                providerPaths,
                providers));
        }

        return effectiveFiles;
    }

    internal static string? NormalizeRelativePath(ActiveContentSource source, string path)
    {
        var relativePath = Path.GetRelativePath(Path.GetFullPath(source.Directory), path);
        if (Path.IsPathRooted(relativePath) ||
            relativePath.Equals("..", StringComparison.Ordinal) ||
            relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
        {
            return null;
        }

        var normalizedRelativePath = relativePath
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
        var prefix = source.VirtualPathPrefix
            .Trim()
            .Trim('/', '\\')
            .Replace('\\', '/');
        return string.IsNullOrWhiteSpace(prefix)
            ? normalizedRelativePath
            : $"{prefix}/{normalizedRelativePath}";
    }

    private static string NormalizeVirtualPath(string path)
    {
        var normalized = path.Trim().Replace('\\', '/');
        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        return normalized.TrimEnd('/');
    }

    private static ContentPriority GetPriority(ActiveContentSource source)
    {
        var layer = GetLayer(source.Kind);
        var withinLayer = layer == 2 ? -source.LoadOrder : source.LoadOrder;
        return new ContentPriority(layer, withinLayer);
    }

    private static ContentApplicationOrder GetApplicationOrder(ActiveContentSource source)
    {
        var layer = GetLayer(source.Kind);
        var withinLayer = layer == 2 ? -source.LoadOrder : source.LoadOrder;
        return new ContentApplicationOrder(layer, withinLayer);
    }

    private static int GetLayer(string kind)
    {
        return kind switch
        {
            "base" or "mode" => 0,
            "dlc" or "dlc-package" or "dlc-feature" => 1,
            "workshop" or "local" => 2,
            _ => -1
        };
    }

    private readonly record struct ContentPriority(int Layer, int WithinLayer) : IComparable<ContentPriority>
    {
        public int CompareTo(ContentPriority other)
        {
            var layerComparison = Layer.CompareTo(other.Layer);
            return layerComparison != 0 ? layerComparison : WithinLayer.CompareTo(other.WithinLayer);
        }
    }

    private readonly record struct ContentApplicationOrder(int Layer, int WithinLayer) : IComparable<ContentApplicationOrder>
    {
        public int CompareTo(ContentApplicationOrder other)
        {
            var layerComparison = Layer.CompareTo(other.Layer);
            return layerComparison != 0 ? layerComparison : WithinLayer.CompareTo(other.WithinLayer);
        }
    }

    private sealed record ResolvedCandidate(
        ActiveContentSource Source,
        string Path,
        string RelativePath);
}
