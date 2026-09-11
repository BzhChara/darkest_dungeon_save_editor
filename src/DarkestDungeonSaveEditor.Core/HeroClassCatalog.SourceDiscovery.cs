using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;

namespace DarkestDungeonSaveEditor.Core;

public static partial class HeroClassCatalog
{
    private static SourceFileSet EnumerateSourceFiles(
        ActiveContentSource source,
        IReadOnlyList<string> enabledDlcPrefixes,
        List<string> issues)
    {
        if (source.Kind is "workshop" or "local")
        {
            var manifestPath = Path.Combine(source.Directory, "modfiles.txt");
            ModManifestFile.Require(manifestPath);
            return EnumerateManifestFiles(source.Directory, manifestPath, enabledDlcPrefixes, issues);

        }

        var roots = new[] { source.Directory };
        IReadOnlyList<string> Files(string directory, string pattern) => SortPaths(
            roots.SelectMany(root => EnumerateFiles(root, directory, pattern))
                .Distinct(StringComparer.OrdinalIgnoreCase));
        IReadOnlyList<string> ResourceFiles(string directory, Func<string, IReadOnlyList<string>, bool, bool> eligible) =>
            Files(directory, "*json").Where(path => eligible(Path.GetRelativePath(source.Directory, path), [], false)).ToArray();

        return new SourceFileSet(
            NativeContentFileResolver.EnumerateActorInfoFiles(source, enabledDlcPrefixes, "heroes", issues),
            Files("heroes", $"*{HeroOverrideSuffix}"),
            Files("effects", "*darkest")
                .Where(path => NativeResourceFileRules.IsEffectFile(Path.GetRelativePath(source.Directory, path), [])).ToArray(),
            ResourceFiles(Path.Combine("shared", "quirk"), NativeResourceFileRules.IsQuirkFile),
            Files(Path.Combine("campaign", "town_events"), "*")
                .Where(path => NativeResourceFileRules.IsTownEventFile(Path.GetRelativePath(source.Directory, path), [])).ToArray(),
            ResourceFiles(Path.Combine("shared", "buffs"), NativeResourceFileRules.IsBuffFile),
            ResourceFiles(Path.Combine("raid", "camping"), NativeResourceFileRules.IsCampingSkillFile),
            Files("localization", "*.string_table.xml"),
            ResourceFiles("upgrades", NativeResourceFileRules.IsUpgradeFile),
            Files(Path.Combine("campaign", "roster"), "roster.variables.json"));
    }

    private static SourceFileSet EnumerateManifestFiles(
        string root,
        string manifestPath,
        IReadOnlyList<string> enabledDlcPrefixes,
        List<string> issues)
    {
        var heroFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var heroOverrideFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var effectFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var quirkFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var eventFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var buffFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var campingFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var nameFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var upgradeFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rosterVariableFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in ModManifestFile.ReadEntries(manifestPath, ExtractManifestPath))
        {
            var rawLine = entry.RawLine;
            var relativePath = entry.RelativePath;

            var path = Path.GetFullPath(Path.Combine(root, relativePath));
            var relativeToRoot = Path.GetRelativePath(root, path);
            if (Path.IsPathRooted(relativeToRoot) ||
                relativeToRoot.Equals("..", StringComparison.Ordinal) ||
                relativeToRoot.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                issues.Add($"Ignored hero catalog manifest path outside its Mod directory: {rawLine.Trim()}");
                continue;
            }

            var normalizedRelative = relativeToRoot.Replace('\\', '/');
            HashSet<string>? target = null;
            if (NativeResourceFileRules.IsActorInfoFile(normalizedRelative, enabledDlcPrefixes, "heroes", manifestDirectory: true))
            {
                target = heroFiles;
            }
            else if (ContentFileOverlay.IsRootOrEnabledDlcPath(normalizedRelative, "heroes", enabledDlcPrefixes) &&
                     normalizedRelative.EndsWith(HeroOverrideSuffix, StringComparison.OrdinalIgnoreCase))
            {
                target = heroOverrideFiles;
            }
            else if (NativeResourceFileRules.IsEffectFile(normalizedRelative, enabledDlcPrefixes, manifestDirectory: true))
            {
                target = effectFiles;
            }
            else if (NativeResourceFileRules.IsQuirkFile(normalizedRelative, enabledDlcPrefixes, manifestDirectory: true))
            {
                target = quirkFiles;
            }
            else if (NativeResourceFileRules.IsTownEventFile(normalizedRelative, enabledDlcPrefixes, manifestDirectory: true))
            {
                target = eventFiles;
            }
            else if (NativeResourceFileRules.IsBuffFile(normalizedRelative, enabledDlcPrefixes, manifestDirectory: true))
            {
                target = buffFiles;
            }
            else if (NativeResourceFileRules.IsCampingSkillFile(normalizedRelative, enabledDlcPrefixes, manifestDirectory: true))
            {
                target = campingFiles;
            }
            else if (NativeResourceFileRules.IsInDirectory(normalizedRelative, "localization", enabledDlcPrefixes, manifestDirectory: true) &&
                     normalizedRelative.EndsWith(".string_table.xml", StringComparison.OrdinalIgnoreCase))
            {
                target = nameFiles;
            }
            else if (NativeResourceFileRules.IsUpgradeFile(normalizedRelative, enabledDlcPrefixes, manifestDirectory: true))
            {
                target = upgradeFiles;
            }
            else if (ContentFileOverlay.IsRootOrEnabledDlcPath(normalizedRelative, "campaign/roster", enabledDlcPrefixes) &&
                     normalizedRelative.EndsWith("roster.variables.json", StringComparison.OrdinalIgnoreCase))
            {
                target = rosterVariableFiles;
            }

            if (target is null)
            {
                continue;
            }

            if (!File.Exists(path))
            {
                issues.Add($"Hero catalog file listed by Mod is missing: {path}");
                continue;
            }

            target.Add(path);
        }

        return new SourceFileSet(
            SortPaths(heroFiles),
            SortPaths(heroOverrideFiles),
            SortPaths(effectFiles),
            SortPaths(quirkFiles),
            SortPaths(eventFiles),
            SortPaths(buffFiles),
            SortPaths(campingFiles),
            SortPaths(nameFiles),
            SortPaths(upgradeFiles),
            SortPaths(rosterVariableFiles));
    }

    private static string? ExtractManifestPath(string rawLine)
    {
        return ModManifestPath.Extract(
            rawLine,
            HeroInfoSuffix,
            HeroOverrideSuffix,
            "darkest",
            "json",
            ".string_table.xml",
            HeroUpgradeSuffix,
            "roster.variables.json");
    }

    private static IReadOnlyList<string> EnumerateFiles(string root, string relativeDirectory, string pattern)
    {
        var directory = Path.Combine(root, relativeDirectory);
        return Directory.Exists(directory)
            ? NativeDirectoryDiscovery.EnumerateFiles(directory, pattern, SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : [];
    }

    private static IReadOnlyList<string> SortPaths(IEnumerable<string> paths)
    {
        return paths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

}
