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
            if (File.Exists(manifestPath))
            {
                return EnumerateManifestFiles(source.Directory, manifestPath, enabledDlcPrefixes, issues);
            }

            issues.Add($"Mod has no modfiles.txt; standard fallback scan used: {source.Directory}");
        }

        return new SourceFileSet(
            EnumerateFiles(source.Directory, "heroes", $"*{HeroInfoSuffix}"),
            EnumerateFiles(source.Directory, "heroes", $"*{HeroOverrideSuffix}"),
            EnumerateFiles(source.Directory, "effects", "*.effects.darkest"),
            EnumerateFiles(source.Directory, Path.Combine("shared", "quirk"), "*quirk_library.json"),
            EnumerateFiles(source.Directory, Path.Combine("campaign", "town_events"), "*.events.json"),
            EnumerateFiles(source.Directory, Path.Combine("shared", "buffs"), "*.buffs.json"),
            EnumerateFiles(source.Directory, Path.Combine("raid", "camping"), "*.camping_skills.json"),
            EnumerateFiles(source.Directory, "localization", "*.string_table.xml"),
            EnumerateHeroUpgradeFiles(source.Directory),
            EnumerateFiles(source.Directory, Path.Combine("campaign", "roster"), "roster.variables.json"));
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
        foreach (var rawLine in File.ReadLines(manifestPath))
        {
            var relativePath = ExtractManifestPath(rawLine);
            if (relativePath is null)
            {
                continue;
            }

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
            if (ContentFileOverlay.IsRootOrEnabledDlcPath(normalizedRelative, "heroes", enabledDlcPrefixes) &&
                normalizedRelative.EndsWith(HeroInfoSuffix, StringComparison.OrdinalIgnoreCase))
            {
                target = heroFiles;
            }
            else if (ContentFileOverlay.IsRootOrEnabledDlcPath(normalizedRelative, "heroes", enabledDlcPrefixes) &&
                     normalizedRelative.EndsWith(HeroOverrideSuffix, StringComparison.OrdinalIgnoreCase))
            {
                target = heroOverrideFiles;
            }
            else if (ContentFileOverlay.IsRootOrEnabledDlcPath(normalizedRelative, "effects", enabledDlcPrefixes) &&
                     normalizedRelative.EndsWith(".effects.darkest", StringComparison.OrdinalIgnoreCase))
            {
                target = effectFiles;
            }
            else if (ContentFileOverlay.IsRootOrEnabledDlcPath(normalizedRelative, "shared/quirk", enabledDlcPrefixes) &&
                     normalizedRelative.EndsWith("quirk_library.json", StringComparison.OrdinalIgnoreCase))
            {
                target = quirkFiles;
            }
            else if (ContentFileOverlay.IsRootOrEnabledDlcPath(normalizedRelative, "campaign/town_events", enabledDlcPrefixes) &&
                     normalizedRelative.EndsWith(".events.json", StringComparison.OrdinalIgnoreCase))
            {
                target = eventFiles;
            }
            else if (ContentFileOverlay.IsRootOrEnabledDlcPath(normalizedRelative, "shared/buffs", enabledDlcPrefixes) &&
                     normalizedRelative.EndsWith(".buffs.json", StringComparison.OrdinalIgnoreCase))
            {
                target = buffFiles;
            }
            else if (ContentFileOverlay.IsRootOrEnabledDlcPath(normalizedRelative, "raid/camping", enabledDlcPrefixes) &&
                     normalizedRelative.EndsWith(".camping_skills.json", StringComparison.OrdinalIgnoreCase))
            {
                target = campingFiles;
            }
            else if (ContentFileOverlay.IsRootOrEnabledDlcPath(normalizedRelative, "localization", enabledDlcPrefixes) &&
                     normalizedRelative.EndsWith(".string_table.xml", StringComparison.OrdinalIgnoreCase))
            {
                target = nameFiles;
            }
            else if (IsHeroUpgradeManifestPath(normalizedRelative, enabledDlcPrefixes) &&
                      normalizedRelative.EndsWith(HeroUpgradeSuffix, StringComparison.OrdinalIgnoreCase))
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
            ".effects.darkest",
            "quirk_library.json",
            ".events.json",
            ".buffs.json",
            ".camping_skills.json",
            ".string_table.xml",
            HeroUpgradeSuffix,
            "roster.variables.json");
    }

    private static bool IsHeroUpgradeManifestPath(
        string normalizedRelative,
        IReadOnlyList<string> enabledDlcPrefixes)
    {
        if (ContentFileOverlay.IsRootOrEnabledDlcPath(
                normalizedRelative,
                "upgrades/heroes",
                enabledDlcPrefixes))
        {
            return true;
        }

        var separator = normalizedRelative.LastIndexOf('/');
        if (separator < 0)
        {
            return false;
        }

        var directory = normalizedRelative[..separator];
        return directory.Equals("upgrades", StringComparison.OrdinalIgnoreCase) ||
               enabledDlcPrefixes.Any(prefix => directory.Equals(
                   $"{prefix.TrimEnd('/')}/upgrades",
                   StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> EnumerateHeroUpgradeFiles(string root)
    {
        var result = new HashSet<string>(
            EnumerateFiles(root, Path.Combine("upgrades", "heroes"), $"*{HeroUpgradeSuffix}"),
            StringComparer.OrdinalIgnoreCase);
        var upgradeRoot = Path.Combine(root, "upgrades");
        if (Directory.Exists(upgradeRoot))
        {
            result.UnionWith(Directory.EnumerateFiles(
                upgradeRoot,
                $"*{HeroUpgradeSuffix}",
                SearchOption.TopDirectoryOnly));
        }

        return SortPaths(result);
    }

    private static IReadOnlyList<string> EnumerateFiles(string root, string relativeDirectory, string pattern)
    {
        var directory = Path.Combine(root, relativeDirectory);
        return Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, pattern, SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : [];
    }

    private static IReadOnlyList<string> SortPaths(IEnumerable<string> paths)
    {
        return paths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

}
