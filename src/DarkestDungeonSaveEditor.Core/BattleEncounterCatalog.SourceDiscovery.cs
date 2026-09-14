using System.Globalization;
using System.Text.RegularExpressions;

namespace DarkestDungeonSaveEditor.Core;

public static partial class BattleEncounterCatalog
{
    // MashGuide formats .*<table>.<difficulty>.mash.darkest. Its dots are
    // regex wildcards; %d supplies a canonical decimal rather than leading zeros.
    private const string MashDifficultyQuery = @".(?<difficulty>-?(?:0|[1-9][0-9]*)).mash.darkest\z";

    private sealed record MashQuery(string DungeonId, int Difficulty, BattleEncounterSourceKind SourceKind);

    // Keep the native query identity even when IO_FindFiles reopens a different
    // provider or returns the same physical file in multiple result slots.
    private sealed record EffectiveMashFile(EffectiveContentFile File, MashQuery Query)
    {
        public ActiveContentSource Source => File.Source;
        public string Path => File.Path;
        public string RelativePath => File.RelativePath;
        public IReadOnlyList<string> ProviderSources => File.ProviderSources;
        public IReadOnlyList<string> ProviderPaths => File.ProviderPaths;
    }

    private static IReadOnlyList<EffectiveMashFile> ResolveGlobalEffectiveMashFiles(
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
                if (relativePath is not null)
                {
                    candidates.Add(new ContentFileCandidate(source, path));
                }
            }
        }

        var queries = candidates.SelectMany(candidate => DescribeMashQueries(
                ContentFileOverlay.NormalizeRelativePath(candidate.Source, candidate.Path)!,
                candidate.Source.Kind is "local" or "workshop"))
            .Distinct();
        return queries.SelectMany(query => ResolveMashQuery(candidates, sources, query, issues)).ToArray();
    }

    private static IReadOnlyList<EffectiveMashFile> ResolveMashQuery(
        IReadOnlyList<ContentFileCandidate> candidates,
        IReadOnlyList<ActiveContentSource> sources,
        MashQuery query,
        List<string> issues) => ResolveRuntimeFileOrder(candidates.Where(candidate => MatchesMashQuery(
                ContentFileOverlay.NormalizeRelativePath(candidate.Source, candidate.Path)!, query,
                candidate.Source.Kind is "local" or "workshop")).ToArray(), sources, query, issues);

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
                .Where(path => DescribeMashQueries(Path.GetRelativePath(source.Directory, path), false).Any())
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
        var result = new HashSet<string>(StringComparer.Ordinal);
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
                if (!DescribeMashQueries(relativeToRoot, true).Any()) continue;
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
        var ids = candidates.Select(file => NativeContentFileResolver.ReadDiscoveredActorId(Path.GetRelativePath(file.Source.Directory, file.Path)))
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
        ActiveContentSource source, IReadOnlyList<string> enabledDlcPrefixes, List<string> issues)
    {
        try
        {
            return NativeContentFileResolver.EnumerateActorInfoFiles(source, enabledDlcPrefixes, "monsters", issues);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            issues.Add($"Monster definition source could not be read: {source.Directory} ({ex.Message})");
            return [];
        }
    }

    private static bool TryReadMashDirectory(
        string relativePath,
        bool manifestDirectory,
        out string dungeonId,
        out string fileName)
    {
        dungeonId = string.Empty;
        fileName = string.Empty;
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
        fileName = normalized[(normalized.LastIndexOf('/') + 1)..];
        return true;
    }

    private static IEnumerable<MashQuery> DescribeMashQueries(string relativePath, bool manifestDirectory)
    {
        if (!TryReadMashDirectory(relativePath, manifestDirectory, out var dungeonId, out var fileName)) yield break;

        // The physical directory is a Windows path, not the table ID passed to
        // MashGuide. Infer the authored table spelling from the filename while
        // allowing its directory alias (Cove/a.cove.2.mash.darkest). The actual
        // requested table is still compared ordinally by the current-table query.
        // Manifest discovery instead matches the original directory-tree bytes.
        var directory = Regex.Escape(dungeonId);
        foreach (var kind in Enum.GetValues<BattleEncounterSourceKind>())
        {
            var table = kind == BattleEncounterSourceKind.Standard
                ? $"(?<table>{(manifestDirectory ? directory : $"(?i:{directory})")})"
                : QueryTableName(dungeonId, kind);
            var match = Regex.Match(fileName, table + MashDifficultyQuery, RegexOptions.CultureInvariant);
            if (match.Success && int.TryParse(match.Groups["difficulty"].Value, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var difficulty) &&
                match.Groups["difficulty"].Value == difficulty.ToString(CultureInfo.InvariantCulture))
            {
                yield return new MashQuery(kind == BattleEncounterSourceKind.Standard
                    ? match.Groups["table"].Value : dungeonId, difficulty, kind);
            }
        }
    }

    private static bool MatchesMashQuery(string relativePath, MashQuery query, bool manifestDirectory) =>
        TryReadMashDirectory(relativePath, manifestDirectory, out var dungeonId, out var fileName) &&
        dungeonId.Equals(query.DungeonId, manifestDirectory ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase) &&
        Regex.IsMatch(fileName, Regex.Escape(QueryTableName(query.DungeonId, query.SourceKind)) +
            "." + query.Difficulty.ToString(CultureInfo.InvariantCulture) + @".mash.darkest\z", RegexOptions.CultureInvariant);

    private static string QueryTableName(string dungeonId, BattleEncounterSourceKind kind) => kind switch
    {
        BattleEncounterSourceKind.Standard => dungeonId,
        BattleEncounterSourceKind.Conditional => "conditional",
        BattleEncounterSourceKind.Additional => "additional",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private sealed record AvailableMonsterDefinitions(
        HashSet<string> Ids,
        HashSet<string> BossIds,
        IReadOnlyDictionary<string, int?> Sizes);
}
