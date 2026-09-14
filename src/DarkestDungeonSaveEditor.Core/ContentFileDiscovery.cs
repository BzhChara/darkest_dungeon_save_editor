using System.IO.Enumeration;

namespace DarkestDungeonSaveEditor.Core;

internal sealed record ContentFileRule(string Directory, string Pattern, bool Recursive = true)
{
    public bool Matches(string relativePath)
    {
        var prefix = Directory.Trim('/') + "/";
        var path = relativePath.Replace('\\', '/');
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        var name = path[prefix.Length..];
        return (Recursive || !name.Contains('/')) &&
            FileSystemName.MatchesSimpleExpression(Pattern, Path.GetFileName(name), ignoreCase: true);
    }
}

// Discovery and ordering are separate operations. A present manifest is
// authoritative even when empty. Missing Mod manifests must be prepared before loading.
internal static class ContentFileDiscovery
{
    public static IReadOnlyList<string> Enumerate(ActiveContentSource source,
        IReadOnlyList<string> enabledDlcPrefixes, List<string> issues, string label,
        params ContentFileRule[] rules)
        => EnumerateQuery(source, enabledDlcPrefixes, issues, label, null, rules);

    internal static IReadOnlyList<string> EnumerateQuery(ActiveContentSource source,
        IReadOnlyList<string> enabledDlcPrefixes, List<string> issues, string label,
        Func<string, bool>? eligible, params ContentFileRule[] rules)
    {
        if (!Directory.Exists(source.Directory))
        {
            issues.Add($"{label} content source is missing: {source.Directory}");
            return [];
        }
        var isMod = source.Kind is "workshop" or "local";
        var prefixes = isMod ? enabledDlcPrefixes : [];
        var manifestPath = Path.Combine(source.Directory, "modfiles.txt");
        // Manifest keys are requests, even when Windows opens the same file.
        var result = new HashSet<string>(isMod ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
        if (isMod)
        {
            ModManifestFile.Require(manifestPath);
            foreach (var entry in ModManifestFile.ReadEntries(manifestPath,
                         rules.Select(rule => rule.Pattern.TrimStart('*')).Distinct().ToArray()))
            {
                var path = Path.GetFullPath(Path.Combine(source.Directory, entry.RelativePath));
                var relative = Path.GetRelativePath(Path.GetFullPath(source.Directory), path).Replace('\\', '/');
                if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith("../", StringComparison.Ordinal))
                {
                    issues.Add($"Ignored {label} manifest path outside its Mod directory: {entry.RawLine.Trim()}");
                    continue;
                }
                if (!Matches(relative, prefixes, rules) || (eligible is not null && !eligible(relative))) continue;
                if (!File.Exists(path))
                {
                    issues.Add($"{label} file listed by Mod is missing: {path}");
                    continue;
                }
                result.Add(path);
            }
        }
        else
        {
            foreach (var rule in rules)
            {
                var directory = Path.Combine(source.Directory, rule.Directory.Replace('/', Path.DirectorySeparatorChar));
                if (!Directory.Exists(directory)) continue;
                foreach (var path in NativeDirectoryDiscovery.EnumerateFiles(directory, rule.Pattern,
                             rule.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly))
                    if (eligible is null || eligible(Path.GetRelativePath(source.Directory, path)))
                        result.Add(Path.GetFullPath(path));
            }
        }
        return result.Order(StringComparer.Ordinal).ToArray();
    }

    private static bool Matches(string path, IReadOnlyList<string> prefixes, ContentFileRule[] rules) =>
        rules.Any(rule => rule.Matches(path)) || prefixes.Any(prefix =>
            path.StartsWith(prefix.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase) &&
            rules.Any(rule => rule.Matches(path[(prefix.TrimEnd('/').Length + 1)..])));
}
