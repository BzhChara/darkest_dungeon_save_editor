using System.Security.Cryptography;
using System.Text;

namespace DarkestDungeonSaveEditor.Core;

public enum BattleRoomAttachmentKind
{
    Curio,
    Treasure,
    HallCurio,
    Trap,
    Obstacle
}

public sealed record BattleRoomAttachmentFileFingerprint(
    string SourceId,
    string Path,
    string RelativePath,
    string Sha256);

public sealed record BattleRoomAttachmentCatalogGuard(
    string ProfileDirectory,
    string GameSavePath,
    string GameSaveSha256,
    IReadOnlyList<ActiveContentSource> ActiveSources,
    IReadOnlyList<BattleRoomAttachmentFileFingerprint> EffectiveFiles,
    string Fingerprint);

public sealed record BattleRoomAttachmentDefinition(
    string Id,
    BattleRoomAttachmentKind Kind,
    int PropHash,
    string SourceLabel,
    string SourcePath,
    string SourceRelativePath,
    string SourceSha256,
    int SourceLine,
    BattleRoomAttachmentCatalogGuard CatalogGuard)
{
    public BilingualContentName LocalizedName { get; init; } = BilingualContentName.Empty;

    // Several declarations can share a physical line; keep their provenance distinct.
    public int SourceRecordIndex { get; init; }

    public string OriginDungeonId
    {
        get
        {
            var parts = SourceRelativePath.Replace('\\', '/').Split('/');
            var index = Array.FindIndex(parts, part => part.Equals("dungeons", StringComparison.OrdinalIgnoreCase));
            return index >= 0 && index + 2 < parts.Length ? parts[index + 1] : string.Empty;
        }
    }

    public bool IsRegionBound => Kind is BattleRoomAttachmentKind.Trap or BattleRoomAttachmentKind.Obstacle;

    public bool IsAvailableInDungeon(string? dungeonId) => !IsRegionBound ||
        (!string.IsNullOrWhiteSpace(OriginDungeonId) &&
         OriginDungeonId.Equals(dungeonId, StringComparison.OrdinalIgnoreCase));

    // The original room-attachment catalog is shared with standalone map content.
    // Keep hall and room curios distinct even when they reference the same prop ID.
    public BattleMapAreaKind TargetAreaKind => Kind switch
    {
        BattleRoomAttachmentKind.Curio or BattleRoomAttachmentKind.Treasure => BattleMapAreaKind.Room,
        BattleRoomAttachmentKind.HallCurio or BattleRoomAttachmentKind.Trap or
            BattleRoomAttachmentKind.Obstacle => BattleMapAreaKind.Corridor,
        _ => throw new InvalidOperationException("未知的地图内容类型。")
    };

    public BattleMapTileContent StandaloneContent => Kind switch
    {
        BattleRoomAttachmentKind.Curio or BattleRoomAttachmentKind.HallCurio => BattleMapTileContent.Curio,
        BattleRoomAttachmentKind.Treasure => BattleMapTileContent.Treasure,
        BattleRoomAttachmentKind.Trap => BattleMapTileContent.Trap,
        BattleRoomAttachmentKind.Obstacle => BattleMapTileContent.Obstacle,
        _ => throw new InvalidOperationException("未知的地图内容类型。")
    };

    public string KindLabel => StandaloneContent switch
    {
        BattleMapTileContent.Curio => "奇物",
        BattleMapTileContent.Treasure => "宝箱",
        BattleMapTileContent.Trap => "陷阱",
        BattleMapTileContent.Obstacle => "障碍",
        _ => throw new InvalidOperationException("未知的地图内容类型。")
    };

    public string ChineseName => string.IsNullOrWhiteSpace(LocalizedName.Chinese)
        ? "—"
        : LocalizedName.Chinese;

    public string EnglishName => string.IsNullOrWhiteSpace(LocalizedName.English)
        ? "—"
        : LocalizedName.English;
}

public sealed partial record BattleRoomAttachmentCatalogResult(
    IReadOnlyList<BattleRoomAttachmentDefinition> Definitions,
    IReadOnlyList<string> Issues,
    BattleRoomAttachmentCatalogGuard Guard)
{
    public IReadOnlyList<BattleRoomAttachmentDefinition> Curios => Definitions
        .Where(definition => definition.Kind == BattleRoomAttachmentKind.Curio)
        .ToArray();

    public IReadOnlyList<BattleRoomAttachmentDefinition> Treasures => Definitions
        .Where(definition => definition.Kind == BattleRoomAttachmentKind.Treasure)
        .ToArray();

    public IReadOnlyList<BattleRoomAttachmentDefinition> HallCurios => Definitions
        .Where(definition => definition.Kind == BattleRoomAttachmentKind.HallCurio)
        .ToArray();

    public IReadOnlyList<BattleRoomAttachmentDefinition> Traps => Definitions
        .Where(definition => definition.Kind == BattleRoomAttachmentKind.Trap)
        .ToArray();

    public IReadOnlyList<BattleRoomAttachmentDefinition> Obstacles => Definitions
        .Where(definition => definition.Kind == BattleRoomAttachmentKind.Obstacle)
        .ToArray();

    public IReadOnlyList<BattleRoomAttachmentDefinition> GetCandidates(
        BattleRoomAttachmentKind kind, string dungeonId) => Definitions
        .Where(definition => definition.Kind == kind && definition.IsAvailableInDungeon(dungeonId))
        .ToArray();
}

public static partial class BattleRoomAttachmentCatalog
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static BattleRoomAttachmentCatalogResult Load(ActiveContentSnapshot activeContent)
    {
        ArgumentNullException.ThrowIfNull(activeContent);
        var profileDirectory = Path.GetFullPath(activeContent.Profile.ProfileDirectory);
        var gameSavePath = Path.Combine(profileDirectory, "persist.game.json");
        if (!File.Exists(gameSavePath))
        {
            throw new FileNotFoundException("当前档案缺少 persist.game.json。", gameSavePath);
        }
        if (!ComputeSha256(gameSavePath).Equals(
                activeContent.SourceGameSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "活动 Mod 配置在地图内容目录加载前已经变化，请重新加载内容目录。");
        }

        var issues = new List<string>();
        var effectiveFiles = ResolveEffectivePropFiles(activeContent.Sources, issues);
        var resourceFiles = ResolveEffectiveResourceFiles(activeContent.Sources, issues);
        var resources = ReadResources(resourceFiles);
        var curioFiles = ResolveEffectiveCurioFiles(activeContent.Sources, issues);
        var curios = ReadCurioResources(curioFiles, resources, issues,
            ContentFileOverlay.GetEnabledDlcPrefixes(activeContent.Sources));
        var fingerprints = effectiveFiles
            .Concat(resourceFiles)
            .Concat(curioFiles)
            .Select(file => new BattleRoomAttachmentFileFingerprint(
                file.Source.Id,
                Path.GetFullPath(file.Path),
                file.RelativePath,
                ComputeSha256(file.Path)))
            .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(file => file.SourceId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var guard = new BattleRoomAttachmentCatalogGuard(
            profileDirectory,
            Path.GetFullPath(gameSavePath),
            activeContent.SourceGameSha256,
            activeContent.Sources.ToArray(),
            fingerprints,
            ComputeCatalogFingerprint(fingerprints));

        var parsed = effectiveFiles
            .SelectMany(file => ParseFile(file, activeContent.Sources, guard, issues))
            .ToArray();
        var selected = parsed
            .GroupBy(candidate => DefinitionKey(candidate.Definition), StringComparer.Ordinal)
            .Select(SelectRepresentative)
            .ToArray();

        var collisions = selected
            .GroupBy(candidate => candidate.Definition.PropHash)
            .Where(group => group
                .Select(candidate => candidate.Definition.Id)
                .Distinct(StringComparer.Ordinal)
                .Skip(1)
                .Any())
            .ToArray();
        var collidingIds = collisions
            .SelectMany(group => group.Select(candidate => candidate.Definition.Id))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var collision in collisions)
        {
            issues.Add(
                $"Room attachment hash collision {collision.Key}: " +
                string.Join(", ", collision.Select(item => item.Definition.Id)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)));
        }

        var definitions = selected
            .Select(candidate => candidate.Definition)
            .Where(definition => !collidingIds.Contains(definition.Id))
            .Where(definition =>
            {
                var reason = GetResourceRejection(definition, resources, curios);
                if (reason is null)
                {
                    return true;
                }
                issues.Add($"地图内容候选未纳入：{definition.KindLabel}/{definition.Id}；原因={reason}；" +
                    $"来源={definition.SourcePath}:{definition.SourceLine}");
                return false;
            })
            .ToArray();
        var localization = ContentLocalizationCatalog.Load(
            activeContent,
            definitions
                .Select(definition => ContentLocalizationCatalog.GetCurioTitleKey(GetCurioNameId(definition, resources)))
                .Distinct(StringComparer.Ordinal));
        issues.AddRange(localization.Issues);
        definitions = definitions
            .Select(definition => definition with
            {
                LocalizedName = localization.GetCurioTitle(GetCurioNameId(definition, resources))
            })
            .OrderBy(definition => definition.Kind)
            .ThenBy(definition => definition.ChineseName == "—")
            .ThenBy(definition => definition.ChineseName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(definition => definition.EnglishName == "—")
            .ThenBy(definition => definition.EnglishName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(definition => definition.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new BattleRoomAttachmentCatalogResult(
            definitions,
            issues.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            guard)
        {
            RegionalPool = BuildRegionalPool(parsed, definitions)
        };
    }

    public static void ValidateGuard(BattleRoomAttachmentCatalogGuard guard)
    {
        ArgumentNullException.ThrowIfNull(guard);
        if (!File.Exists(guard.GameSavePath) ||
            !ComputeSha256(guard.GameSavePath).Equals(
                guard.GameSaveSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "活动 Mod 配置在地图内容选择后已经变化，请重新加载内容目录。");
        }

        var issues = new List<string>();
        var currentFiles = ResolveEffectivePropFiles(guard.ActiveSources, issues)
            .Concat(ResolveEffectiveResourceFiles(guard.ActiveSources, issues))
            .Concat(ResolveEffectiveCurioFiles(guard.ActiveSources, issues))
            .Select(file => new BattleRoomAttachmentFileFingerprint(
                file.Source.Id,
                Path.GetFullPath(file.Path),
                file.RelativePath,
                ComputeSha256(file.Path)))
            .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(file => file.SourceId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (!currentFiles.SequenceEqual(guard.EffectiveFiles) ||
            !ComputeCatalogFingerprint(currentFiles).Equals(
                guard.Fingerprint,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "有效地图内容定义在选择后已经变化，请重新加载内容目录。");
        }
    }

    public static void ValidateDefinition(BattleRoomAttachmentDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ValidateGuard(definition.CatalogGuard);
        if (definition.PropHash == 0 || definition.PropHash != ComputePropHash(definition.Id))
        {
            throw new InvalidOperationException("所选地图内容的 ID 与存档哈希无效或不一致。");
        }

        var issues = new List<string>();
        var parsed = ResolveEffectivePropFiles(definition.CatalogGuard.ActiveSources, issues)
            .SelectMany(file => ParseFile(
                file,
                definition.CatalogGuard.ActiveSources,
                definition.CatalogGuard,
                issues))
            .ToArray();
        if (parsed.Any(candidate => candidate.Definition.PropHash == definition.PropHash &&
                !candidate.Definition.Id.Equals(definition.Id, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("所选地图内容的存档哈希与另一项资源冲突。");
        }
        var resources = ReadResources(ResolveEffectiveResourceFiles(definition.CatalogGuard.ActiveSources, issues));
        var curios = ReadCurioResources(ResolveEffectiveCurioFiles(definition.CatalogGuard.ActiveSources, issues), resources, issues,
            ContentFileOverlay.GetEnabledDlcPrefixes(definition.CatalogGuard.ActiveSources));
        if (GetResourceRejection(definition, resources, curios) is { } rejection)
        {
            throw new InvalidOperationException($"所选地图内容不可写入：{rejection}");
        }
        var matches = parsed.Any(candidate =>
                candidate.Definition.Kind == definition.Kind &&
                candidate.Definition.Id.Equals(definition.Id, StringComparison.Ordinal) &&
                candidate.Definition.PropHash == definition.PropHash &&
                candidate.Definition.SourceLine == definition.SourceLine &&
                candidate.Definition.SourceRecordIndex == definition.SourceRecordIndex &&
                candidate.Definition.SourceRelativePath.Equals(
                    definition.SourceRelativePath,
                    StringComparison.OrdinalIgnoreCase) &&
                Path.GetFullPath(candidate.Definition.SourcePath).Equals(
                    Path.GetFullPath(definition.SourcePath),
                    StringComparison.OrdinalIgnoreCase) &&
                candidate.Definition.SourceSha256.Equals(
                    definition.SourceSha256,
                    StringComparison.OrdinalIgnoreCase));
        // Repeated identical types are distinct weighted entries, not ambiguity.
        if (!matches)
        {
            throw new InvalidOperationException(
                "所选地图内容已变化、消失或存在歧义，请重新加载内容目录。");
        }
    }

    public static int ComputePropHash(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return unchecked((int)Loc2LocalizationReader.HashName(id));
    }

    private static ParsedAttachment SelectRepresentative(
        IGrouping<string, ParsedAttachment> group)
    {
        var candidates = group.ToArray();
        var highestPriority = candidates[0].Source;
        foreach (var candidate in candidates.Skip(1))
        {
            if (ContentFileOverlay.ComparePriority(candidate.Source, highestPriority) > 0)
            {
                highestPriority = candidate.Source;
            }
        }

        return candidates
            .Where(candidate =>
                ContentFileOverlay.ComparePriority(candidate.Source, highestPriority) == 0)
            .OrderBy(candidate => candidate.Definition.SourceRelativePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.Definition.SourceLine)
            .ThenBy(candidate => candidate.Definition.SourceRecordIndex)
            .ThenBy(candidate => candidate.Definition.SourcePath, StringComparer.OrdinalIgnoreCase)
            .First();
    }

    private static IReadOnlyList<EffectiveContentFile> ResolveEffectivePropFiles(
        IReadOnlyList<ActiveContentSource> sources,
        List<string> issues)
    {
        var enabledDlcPrefixes = ContentFileOverlay.GetEnabledDlcPrefixes(sources);
        var candidates = new List<ContentFileCandidate>();
        foreach (var source in sources)
        {
            foreach (var path in EnumerateCanonicalPropFiles(source, enabledDlcPrefixes, issues))
            {
                candidates.Add(new ContentFileCandidate(source, path));
            }
        }

        return NativeContentFileResolver.Resolve(candidates, sources, "Room prop", issues);
    }

    internal static IReadOnlyList<string> EnumerateCanonicalPropFiles(ActiveContentSource source,
        IReadOnlyList<string> enabledDlcPrefixes, List<string> issues) =>
        EnumeratePropFiles(source, enabledDlcPrefixes, issues)
            .Where(path => IsCanonicalPropPath(source, path, enabledDlcPrefixes)).ToArray();

    private static bool IsCanonicalPropPath(ActiveContentSource source, string path,
        IReadOnlyList<string> enabledDlcPrefixes)
    {
        var relative = ContentFileOverlay.NormalizeRelativePath(source, path);
        if (relative is null) return false;
        var prefix = enabledDlcPrefixes.OrderByDescending(value => value.Length)
            .FirstOrDefault(value => relative.StartsWith(value + "/", StringComparison.OrdinalIgnoreCase));
        if (prefix is not null) relative = relative[(prefix.Length + 1)..];
        var parts = relative.Split('/');
        // Dungeon::Load opens this constructed path (build 27890, 0x1404AF1F2).
        // It never enumerates arbitrary names or classification subdirectories.
        return parts.Length == 3 && parts[0].Equals("dungeons", StringComparison.OrdinalIgnoreCase) &&
            !parts[1].Equals("arena", StringComparison.OrdinalIgnoreCase) &&
            parts[2].Equals(parts[1] + ".props.darkest", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> EnumeratePropFiles(
        ActiveContentSource source,
        IReadOnlyList<string> enabledDlcPrefixes,
        List<string> issues,
        string contentDirectory = "dungeons",
        string pattern = "*.props.darkest",
        string extension = ".props.darkest",
        Func<string, bool>? eligible = null)
    {
        if (!Directory.Exists(source.Directory))
        {
            issues.Add($"Room prop content source is missing: {source.Directory}");
            return [];
        }

        if (source.Kind is "workshop" or "local")
        {
            var manifestPath = Path.Combine(source.Directory, "modfiles.txt");
            ModManifestFile.Require(manifestPath);
            return EnumerateManifestPropFiles(
                source,
                manifestPath,
                enabledDlcPrefixes,
                issues, contentDirectory, extension, eligible);
        }

        try
        {
            return new[] { source.Directory }
                .Select(root => Path.Combine(root, contentDirectory))
                .Where(Directory.Exists)
                // A canonical OpenFile is not directory-device FindFiles: a
                // region containing _template must not lose its standard pool.
                .SelectMany(directory => extension == ".props.darkest"
                    ? Directory.EnumerateFiles(directory, pattern, SearchOption.AllDirectories)
                    : NativeDirectoryDiscovery.EnumerateFiles(directory, pattern, SearchOption.AllDirectories))
                .Where(path => eligible is null || eligible(Path.GetRelativePath(source.Directory, path)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            issues.Add($"Room prop content source could not be scanned: {source.Directory} ({ex.Message})");
            return [];
        }
    }

    private static IReadOnlyList<string> EnumerateManifestPropFiles(
        ActiveContentSource source,
        string manifestPath,
        IReadOnlyList<string> enabledDlcPrefixes,
        List<string> issues,
        string contentDirectory,
        string extension,
        Func<string, bool>? eligible)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var entry in ModManifestFile.ReadEntries(manifestPath, extension))
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
                        $"Ignored room prop manifest path outside its Mod directory: {rawLine.Trim()}");
                    continue;
                }
                if (!ContentFileOverlay.IsRootOrEnabledDlcPath(
                        relativeToRoot,
                        contentDirectory,
                        enabledDlcPrefixes) || (eligible is not null && !eligible(relativeToRoot)))
                {
                    continue;
                }
                if (!File.Exists(path))
                {
                    issues.Add($"Room prop file listed by Mod is missing: {path}");
                    // Do not expose lower bytes when a winning pool cannot be opened.
                    // A shadowed missing provider may still resolve to readable bytes.
                    if (extension != ".props.darkest") continue;
                }

                result.Add(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            issues.Add($"Room prop Mod manifest could not be read: {manifestPath} ({ex.Message})");
        }

        return result.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IEnumerable<ParsedAttachment> ParseFile(
        EffectiveContentFile file,
        IReadOnlyList<ActiveContentSource> activeSources,
        BattleRoomAttachmentCatalogGuard guard,
        List<string> issues)
    {
        byte[] bytes;
        string content;
        try
        {
            bytes = File.ReadAllBytes(file.Path);
            var nul = Array.IndexOf(bytes, (byte)0);
            content = StrictUtf8.GetString(bytes, 0, nul < 0 ? bytes.Length : nul);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            issues.Add($"Room prop file could not be read: {file.Path} ({ex.Message})");
            yield break;
        }
        catch (DecoderFallbackException)
        {
            issues.Add($"地图资源池包含无效 UTF-8，无法安全识别原始 ID，已跳过：{file.Path}");
            yield break;
        }

        var sourceSha256 = Convert.ToHexString(SHA256.HashData(bytes));
        var sourceLabel = ContentSourceLabelFormatter.Format(
            file.Source.Id,
            file.ProviderSources,
            activeSources);
        var recordIndex = -1;
        foreach (var (recordKind, body, sourceLine) in
                 NativeDarkestReader.ReadRecordsWithSourceLinesFromText(content))
        {
            recordIndex++;
            // Dungeon::Load dispatches with case-sensitive strstr at offset 0,
            // not equality of the complete header (e.g. traps_extra is a trap pool).
            var kind = recordKind switch
            {
                _ when recordKind.StartsWith("room_curios", StringComparison.Ordinal) => BattleRoomAttachmentKind.Curio,
                _ when recordKind.StartsWith("hall_curios", StringComparison.Ordinal) => BattleRoomAttachmentKind.HallCurio,
                _ when recordKind.StartsWith("room_treasures", StringComparison.Ordinal) => BattleRoomAttachmentKind.Treasure,
                _ when recordKind.StartsWith("traps", StringComparison.Ordinal) => BattleRoomAttachmentKind.Trap,
                _ when recordKind.StartsWith("obstacles", StringComparison.Ordinal) => BattleRoomAttachmentKind.Obstacle,
                _ => (BattleRoomAttachmentKind?)null
            };
            if (kind is null)
            {
                continue;
            }

            if (NativeDarkestReader.FindValue(body, ".types") < 0)
            {
                issues.Add($"地图内容记录缺少资源列表（.types），已跳过：{file.Path}:{sourceLine}");
                continue;
            }

            var ids = new List<string>();
            foreach (var raw in NativeDarkestReader.ReadRawStringSlots(body, ".types", 64))
            {
                if (raw.Length == 0) break;
                var encoded = Encoding.UTF8.GetBytes(raw);
                try
                {
                    var id = StrictUtf8.GetString(encoded, 0, Math.Min(encoded.Length, 63));
                    if (!string.IsNullOrWhiteSpace(id)) ids.Add(id);
                    else issues.Add($"地图资源 ID 仅含空白，未纳入：{file.Path}:{sourceLine}");
                }
                catch (DecoderFallbackException)
                {
                    issues.Add($"地图资源 ID 的原生 63 字节边界截断了 UTF-8 字符，无法安全写入：{file.Path}:{sourceLine}");
                }
            }
            if (ids.Count == 0)
            {
                issues.Add($"地图内容记录的资源列表（.types）没有可用 ID，已跳过：{file.Path}:{sourceLine}");
                continue;
            }

            var regionalWeight = kind is BattleRoomAttachmentKind.Trap or BattleRoomAttachmentKind.Obstacle
                ? ReadRegionalWeight(body, file.Path, sourceLine, issues)
                : 0;
            foreach (var id in ids)
            {
                yield return new ParsedAttachment(
                    file.Source,
                    new BattleRoomAttachmentDefinition(
                        id,
                        kind.Value,
                        ComputePropHash(id),
                        sourceLabel,
                        Path.GetFullPath(file.Path),
                        file.RelativePath,
                        sourceSha256,
                        sourceLine,
                        guard) { SourceRecordIndex = recordIndex },
                    regionalWeight);
            }
        }
    }

    private static string ComputeCatalogFingerprint(
        IEnumerable<BattleRoomAttachmentFileFingerprint> files)
    {
        var payload = string.Join(
            "\n",
            files.Select(file =>
                $"{file.SourceId}\t{file.RelativePath}\t{file.Path}\t{file.Sha256}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private sealed record ParsedAttachment(
        ActiveContentSource Source,
        BattleRoomAttachmentDefinition Definition,
        double RegionalWeight);
}
