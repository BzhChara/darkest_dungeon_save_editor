using System.Text.RegularExpressions;

namespace DarkestDungeonSaveEditor.Core;

// HeroClass::Load (0x1404857C6): immediate directory query under heroes/<id>/.
// A skin is a slot in the mounted directory list, not an A-based letter index.
internal static class HeroSkinDirectoryDiscovery
{
    internal static IReadOnlyList<string> Enumerate(ActiveContentSource source,
        IReadOnlyList<string> prefixes, List<string> issues)
    {
        if (!Directory.Exists(source.Directory))
        {
            issues.Add($"Hero skin source directory not found: {source.Directory}");
            return [];
        }

        if (source.Kind is "local" or "workshop")
        {
            var manifest = Path.Combine(source.Directory, "modfiles.txt");
            ModManifestFile.Require(manifest);
            var paths = new HashSet<string>(StringComparer.Ordinal);
            // Manifest devices build their directory tree from every listed
            // descendant, without testing its extension or physical existence.
            foreach (var entry in ModManifestFile.ReadEntries(manifest, ""))
            {
                var relative = entry.RelativePath.Replace('\\', '/');
                if (Path.IsPathRooted(relative) || relative.Split('/').Any(part => part is "." or "..")) continue;
                var mounted = NativeResourceFileRules.MountedPath(relative, prefixes, manifestDirectory: true);
                var parts = mounted.Split('/');
                if (parts.Length < 4 || parts[0] != "heroes" || parts.Take(4).Any(string.IsNullOrEmpty)) continue;
                var directory = relative[..(relative.Length - mounted.Length)] + string.Join('/', parts.Take(3));
                paths.Add(Path.Combine(source.Directory, directory.Replace('/', Path.DirectorySeparatorChar)));
            }
            return paths.Order(StringComparer.Ordinal).ToArray();
        }

        var root = Path.Combine(source.Directory, "heroes");
        if (!Directory.Exists(root)) return [];
        return Directory.EnumerateDirectories(root)
            .SelectMany(hero => Directory.EnumerateDirectories(hero)
                .Where(skin => NativeDirectoryDiscovery.IsDiscovered(hero, skin)))
            .Order(StringComparer.Ordinal).ToArray();
    }

    internal static int Count(string heroId, IReadOnlyList<ContentFileCandidate> directories,
        IReadOnlyList<ActiveContentSource> sources)
    {
        var prefixes = ContentFileOverlay.GetEnabledDlcPrefixes(sources);
        // The native format inserts the class ID into this regex verbatim.
        var namePattern = new Regex($@"\A(.*){heroId}_([A-Z])/\z", RegexOptions.CultureInvariant);
        var eligible = directories.Where(candidate =>
        {
            var isManifest = candidate.Source.Kind is "local" or "workshop";
            var relative = Path.GetRelativePath(candidate.Source.Directory, candidate.Path).Replace('\\', '/');
            var mounted = NativeResourceFileRules.MountedPath(relative, prefixes, isManifest);
            var root = $"heroes/{heroId}/";
            return mounted.StartsWith(root, isManifest ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase) &&
                   namePattern.IsMatch(mounted[root.Length..] + "/");
        }).ToArray();
        // Return-path mode 0 merges immediate directory names with strncmp
        // (0x140247C90), not the file overlay's case-insensitive path keys.
        // Exact duplicate names share a slot across root/DLC/Mod mounts;
        // Pack_<id>_C and pack_<id>_C retain distinct slots. Only the range is
        // needed here; no provider bytes or letter-based numbers are selected.
        return eligible.Select(candidate => Path.GetFileName(candidate.Path))
            .Distinct(StringComparer.Ordinal).Count();
    }
}
