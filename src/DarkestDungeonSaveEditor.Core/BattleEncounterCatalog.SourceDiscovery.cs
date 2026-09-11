using System.Globalization;
using System.Text.RegularExpressions;

namespace DarkestDungeonSaveEditor.Core;

public static partial class BattleEncounterCatalog
{
    // MashGuide formats .*<table>.<difficulty>.mash.darkest. Its dots are
    // regex wildcards; %d supplies a canonical decimal rather than leading zeros.
    private const string MashDifficultyQuery = @".(?<difficulty>-?(?:0|[1-9][0-9]*)).mash.darkest\z";

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

        return ResolveRuntimeFileOrder(candidates, sources, issues);
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
            ModManifestFile.Require(manifestPath);
            return EnumerateManifestMashFiles(
                source,
                manifestPath,
                enabledDlcPrefixes,
                issues);
        }

        try
        {
            return new[] { source.Directory }
                .Select(root => Path.Combine(root, "dungeons"))
                .Where(Directory.Exists)
                .SelectMany(directory => NativeDirectoryDiscovery.EnumerateFiles(
                    directory, "*darkest", SearchOption.AllDirectories))
                .Where(path => TryDescribeMashFile(Path.GetRelativePath(source.Directory, path), out _, out _))
                .Distinct(StringComparer.OrdinalIgnoreCase)
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
            foreach (var entry in ModManifestFile.ReadEntries(manifestPath, "darkest"))
            {
                var rawLine = entry.RawLine;
                var relativePath = entry.RelativePath;

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
                var manifestPathKey = relativeToRoot.Replace('\\', '/');
                // Mod directory-tree insertion and lookup hash raw path bytes
                // (0x1403EBE30 / 0x1402397E0). Unlike FindFirstFileW, this
                // query does not fold the directory spelling to match Windows.
                if (!manifestPathKey.StartsWith("dungeons/", StringComparison.Ordinal) &&
                    !enabledDlcPrefixes.Any(prefix => manifestPathKey.StartsWith(
                        prefix + "/dungeons/", StringComparison.Ordinal)))
                {
                    continue;
                }
                if (!TryDescribeMashFile(relativeToRoot, out _, out _, manifestDirectory: true)) continue;
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
        var prefixes = ContentFileOverlay.GetEnabledDlcPrefixes(sources);
        var candidates = sources.SelectMany(source => EnumerateMonsterInfoFiles(source, prefixes, issues)
            .Select(path => new ContentFileCandidate(source, path))).ToArray();
        var ids = candidates.Select(file => Path.GetFileName(file.Path)[..^".info.darkest".Length])
            .ToHashSet(StringComparer.Ordinal);
        var bossIds = new HashSet<string>(StringComparer.Ordinal);
        var usableIds = new HashSet<string>(StringComparer.Ordinal);
        var hashCollisions = NativeResourceIdentity.FindCollisions(ids);
        var sizes = new Dictionary<string, int?>(StringComparer.Ordinal);
        var actorFiles = NativeContentFileResolver.ResolveActorFiles(sources, "monsters", issues);
        foreach (var id in ids)
        {
            // MonsterClass::Create copies the ID, removes its final TWO bytes
            // for the family path, then opens canonical info followed by art.
            // The discovery file's directory is not the definition directory.
            if (hashCollisions.Contains(id) || id.Length < 2 || id.Length > 63 || id.Any(character => character > 127))
            {
                sizes[id] = null; // Native byte truncation has not been reproduced here.
                usableIds.Add(id); // Uncertainty must defer numbering/cleanup, not prove removal.
                continue;
            }
            var stem = $"monsters/{id[..^2]}/{id}/{id}";
            var info = actorFiles.GetValueOrDefault(stem + ".info.darkest");
            var art = actorFiles.GetValueOrDefault(stem + ".art.darkest");
            // An allocated, zero-initialized native class still occupies table
            // slots, but does not prove that a combat definition was loaded.
            // Preserve its native size for numbering; reject only its placement.
            if (info is not null || art is not null) usableIds.Add(id);
            int? size = 0; // Zero-initialized ActorClass, also observed in the runtime table.
            foreach (var file in new[] { info, art }.OfType<EffectiveContentFile>())
            {
                size = ReadMonsterSize(file.Path, size);
                if (DeclaresBossTag(file.Path, issues)) bossIds.Add(id);
            }
            sizes[id] = size;
        }
        return new AvailableMonsterDefinitions(usableIds, bossIds, sizes);
    }

    private static int? ReadMonsterSize(string path, int? size)
    {
        try
        {
            foreach (var (kind, body) in NativeDarkestReader.ReadRecords(path))
            {
                if (kind != "display" || !body.Contains(".size", StringComparison.Ordinal)) continue;
                size = NativeDarkestReader.ReadInt(body, ".size");
                if (size < 0) size = null;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        return size;
    }

    private static bool DeclaresBossTag(string path, List<string> issues)
    {
        try
        {
            return NativeDarkestReader.ReadRecords(path).Any(record => record.Kind == "tag" &&
                NativeDarkestReader.ReadString(record.Body, ".id") == "boss");
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
            ModManifestFile.Require(manifestPath);
            return EnumerateManifestMonsterInfoFiles(
                source,
                manifestPath,
                enabledDlcPrefixes,
                issues);
        }

        try
        {
            return new[] { source.Directory }
                .Select(root => Path.Combine(root, "monsters"))
                .Where(Directory.Exists)
                .SelectMany(directory => NativeDirectoryDiscovery.EnumerateFiles(
                    directory, "*.info.darkest", SearchOption.AllDirectories))
                .Distinct(StringComparer.OrdinalIgnoreCase)
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
            foreach (var entry in ModManifestFile.ReadEntries(manifestPath, ".info.darkest"))
            {
                var rawLine = entry.RawLine;
                var relativePath = entry.RelativePath;

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
        out int difficulty,
        bool manifestDirectory = false)
    {
        dungeonId = string.Empty;
        difficulty = 0;
        var normalized = $"/{relativePath.Replace('\\', '/').TrimStart('/')}";
        const string marker = "/dungeons/";
        var markerIndex = normalized.IndexOf(marker,
            manifestDirectory ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
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

        // The physical directory is a Windows path, not the table ID passed to
        // MashGuide. Infer the authored table spelling from the filename while
        // allowing its directory alias (Cove/a.cove.2.mash.darkest). The actual
        // requested table is still compared ordinally by the current-table query.
        // Manifest discovery instead matches the original directory-tree bytes.
        // Keep the suffix regex and conditional/additional collections unchanged.
        var standard = ClassifyFile(relativePath) == BattleEncounterSourceKind.Standard;
        var directory = Regex.Escape(dungeonId);
        var table = standard
            ? $"(?<table>{(manifestDirectory ? directory : $"(?i:{directory})")})" : string.Empty;
        var match = Regex.Match(normalized, table + MashDifficultyQuery, RegexOptions.CultureInvariant);
        if (standard && match.Success) dungeonId = match.Groups["table"].Value;
        return match.Success &&
               int.TryParse(
                   match.Groups["difficulty"].Value,
                   NumberStyles.Integer,
                   CultureInfo.InvariantCulture,
               out difficulty) &&
               match.Groups["difficulty"].Value == difficulty.ToString(CultureInfo.InvariantCulture);
    }

    private sealed record AvailableMonsterDefinitions(
        HashSet<string> Ids,
        HashSet<string> BossIds,
        IReadOnlyDictionary<string, int?> Sizes);
}
