using System.Globalization;
using System.Text.RegularExpressions;

namespace DarkestDungeonSaveEditor.Core;

public static partial class BattleEncounterCatalog
{
    private static readonly Regex MashDifficultyPattern = new(
        @"\.(?<difficulty>-?\d+)\.mash\.darkest$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static IReadOnlyList<EffectiveContentFile> ResolveGlobalEffectiveMashFiles(
        IReadOnlyList<ActiveContentSource> sources,
        List<string> issues)
    {
        var enabledDlcPrefixes = ContentFileOverlay.GetEnabledDlcPrefixes(sources);
        var candidates = new List<ContentFileCandidate>();
        foreach (var source in sources)
        {
            foreach (var path in EnumerateMashFiles(source, enabledDlcPrefixes, issues))
            {
                var relativePath = ContentFileOverlay.NormalizeRelativePath(source, path);
                if (relativePath is not null &&
                    TryDescribeMashFile(relativePath, out _, out _))
                {
                    candidates.Add(new ContentFileCandidate(source, path));
                }
            }
        }

        return ContentFileOverlay.Resolve(candidates, "Encounter mash", issues);
    }

    private static IReadOnlyList<string> EnumerateMashFiles(
        ActiveContentSource source,
        IReadOnlyList<string> enabledDlcPrefixes,
        List<string> issues)
    {
        if (!Directory.Exists(source.Directory))
        {
            issues.Add($"Encounter content source is missing: {source.Directory}");
            return [];
        }

        if (source.Kind is "workshop" or "local")
        {
            var manifestPath = Path.Combine(source.Directory, "modfiles.txt");
            if (File.Exists(manifestPath))
            {
                return EnumerateManifestMashFiles(
                    source,
                    manifestPath,
                    enabledDlcPrefixes,
                    issues);
            }
        }

        var dungeonDirectory = Path.Combine(source.Directory, "dungeons");
        if (!Directory.Exists(dungeonDirectory))
        {
            return [];
        }

        try
        {
            return Directory.EnumerateFiles(
                    dungeonDirectory,
                    "*.mash.darkest",
                    SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            issues.Add(
                $"Encounter content source could not be scanned: {source.Directory} ({ex.Message})");
            return [];
        }
    }

    private static IReadOnlyList<string> EnumerateManifestMashFiles(
        ActiveContentSource source,
        string manifestPath,
        IReadOnlyList<string> enabledDlcPrefixes,
        List<string> issues)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var rawLine in File.ReadLines(manifestPath))
            {
                var relativePath = ModManifestPath.Extract(rawLine, ".mash.darkest");
                if (relativePath is null)
                {
                    continue;
                }

                var path = Path.GetFullPath(Path.Combine(source.Directory, relativePath));
                var relativeToRoot = Path.GetRelativePath(source.Directory, path);
                if (Path.IsPathRooted(relativeToRoot) ||
                    relativeToRoot.Equals("..", StringComparison.Ordinal) ||
                    relativeToRoot.StartsWith(
                        $"..{Path.DirectorySeparatorChar}",
                        StringComparison.Ordinal))
                {
                    issues.Add(
                        $"Ignored encounter manifest path outside its Mod directory: {rawLine.Trim()}");
                    continue;
                }
                if (!ContentFileOverlay.IsRootOrEnabledDlcPath(
                        relativeToRoot,
                        "dungeons",
                        enabledDlcPrefixes))
                {
                    continue;
                }
                if (!File.Exists(path))
                {
                    issues.Add($"Encounter mash listed by Mod is missing: {path}");
                    continue;
                }

                result.Add(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            issues.Add($"Encounter Mod manifest could not be read: {manifestPath} ({ex.Message})");
        }

        return result.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static AvailableMonsterDefinitions ResolveAvailableMonsterDefinitions(
        IReadOnlyList<ActiveContentSource> sources,
        List<string> issues)
    {
        var enabledDlcPrefixes = ContentFileOverlay.GetEnabledDlcPrefixes(sources);
        var candidatesById = new Dictionary<string, List<MonsterDefinitionCandidate>>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources)
        {
            foreach (var path in EnumerateMonsterInfoFiles(
                         source,
                         enabledDlcPrefixes,
                         issues))
            {
                var fileName = Path.GetFileName(path);
                const string suffix = ".info.darkest";
                if (fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    var monsterId = fileName[..^suffix.Length];
                    if (!candidatesById.TryGetValue(monsterId, out var candidates))
                    {
                        candidates = [];
                        candidatesById[monsterId] = candidates;
                    }

                    candidates.Add(new MonsterDefinitionCandidate(source, path));
                }
            }
        }

        var ids = candidatesById.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var bossIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in candidatesById)
        {
            var highestPriority = pair.Value[0].Source;
            foreach (var candidate in pair.Value.Skip(1))
            {
                if (ContentFileOverlay.ComparePriority(candidate.Source, highestPriority) > 0)
                {
                    highestPriority = candidate.Source;
                }
            }

            var winners = pair.Value
                .Where(candidate =>
                    ContentFileOverlay.ComparePriority(candidate.Source, highestPriority) == 0)
                .ToArray();
            if (winners.Length > 0 && winners.All(candidate => DeclaresBossTag(candidate.Path, issues)))
            {
                bossIds.Add(pair.Key);
            }
        }

        return new AvailableMonsterDefinitions(ids, bossIds);
    }

    private static bool DeclaresBossTag(string path, List<string> issues)
    {
        try
        {
            foreach (var rawLine in File.ReadLines(path))
            {
                var line = StripComment(rawLine).Trim();
                var separator = line.IndexOf(':');
                if (separator <= 0 ||
                    !line[..separator].Trim().Equals("tag", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var tokens = TokenPattern.Matches(line[(separator + 1)..])
                    .Select(match => Unquote(match.Value))
                    .ToArray();
                var idIndex = Array.FindIndex(tokens, token =>
                    token.Equals(".id", StringComparison.OrdinalIgnoreCase));
                if (idIndex >= 0 && idIndex + 1 < tokens.Length &&
                    tokens[idIndex + 1].Equals("boss", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            issues.Add($"Monster definition could not be read for boss classification: {path} ({ex.Message})");
        }

        return false;
    }

    private static IReadOnlyList<string> EnumerateMonsterInfoFiles(
        ActiveContentSource source,
        IReadOnlyList<string> enabledDlcPrefixes,
        List<string> issues)
    {
        if (!Directory.Exists(source.Directory))
        {
            return [];
        }

        if (source.Kind is "workshop" or "local")
        {
            var manifestPath = Path.Combine(source.Directory, "modfiles.txt");
            if (File.Exists(manifestPath))
            {
                return EnumerateManifestMonsterInfoFiles(
                    source,
                    manifestPath,
                    enabledDlcPrefixes,
                    issues);
            }
        }

        var monsterDirectory = Path.Combine(source.Directory, "monsters");
        if (!Directory.Exists(monsterDirectory))
        {
            return [];
        }

        try
        {
            return Directory.EnumerateFiles(
                    monsterDirectory,
                    "*.info.darkest",
                    SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            issues.Add(
                $"Monster definition source could not be scanned: {source.Directory} ({ex.Message})");
            return [];
        }
    }

    private static IReadOnlyList<string> EnumerateManifestMonsterInfoFiles(
        ActiveContentSource source,
        string manifestPath,
        IReadOnlyList<string> enabledDlcPrefixes,
        List<string> issues)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var rawLine in File.ReadLines(manifestPath))
            {
                var relativePath = ModManifestPath.Extract(rawLine, ".info.darkest");
                if (relativePath is null)
                {
                    continue;
                }

                var path = Path.GetFullPath(Path.Combine(source.Directory, relativePath));
                var relativeToRoot = Path.GetRelativePath(source.Directory, path);
                if (Path.IsPathRooted(relativeToRoot) ||
                    relativeToRoot.Equals("..", StringComparison.Ordinal) ||
                    relativeToRoot.StartsWith(
                        $"..{Path.DirectorySeparatorChar}",
                        StringComparison.Ordinal))
                {
                    issues.Add(
                        $"Ignored monster manifest path outside its Mod directory: {rawLine.Trim()}");
                    continue;
                }
                if (!ContentFileOverlay.IsRootOrEnabledDlcPath(
                        relativeToRoot,
                        "monsters",
                        enabledDlcPrefixes))
                {
                    continue;
                }
                if (!File.Exists(path))
                {
                    issues.Add($"Monster definition listed by Mod is missing: {path}");
                    continue;
                }

                result.Add(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            issues.Add($"Monster Mod manifest could not be read: {manifestPath} ({ex.Message})");
        }

        return result.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool TryDescribeMashFile(
        string relativePath,
        out string dungeonId,
        out int difficulty)
    {
        dungeonId = string.Empty;
        difficulty = 0;
        var normalized = $"/{relativePath.Replace('\\', '/').TrimStart('/')}";
        const string marker = "/dungeons/";
        var markerIndex = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return false;
        }

        var dungeonStart = markerIndex + marker.Length;
        var dungeonEnd = normalized.IndexOf('/', dungeonStart);
        if (dungeonEnd <= dungeonStart)
        {
            return false;
        }
        dungeonId = normalized[dungeonStart..dungeonEnd];

        var match = MashDifficultyPattern.Match(Path.GetFileName(normalized));
        return match.Success &&
               int.TryParse(
                   match.Groups["difficulty"].Value,
                   NumberStyles.Integer,
                   CultureInfo.InvariantCulture,
               out difficulty);
    }

    private sealed record MonsterDefinitionCandidate(
        ActiveContentSource Source,
        string Path);

    private sealed record AvailableMonsterDefinitions(
        HashSet<string> Ids,
        HashSet<string> BossIds);
}
