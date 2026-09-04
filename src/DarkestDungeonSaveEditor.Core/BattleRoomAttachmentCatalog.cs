using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace DarkestDungeonSaveEditor.Core;

public enum BattleRoomAttachmentKind
{
    Curio,
    Treasure
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

    public string ChineseName => string.IsNullOrWhiteSpace(LocalizedName.Chinese)
        ? "—"
        : LocalizedName.Chinese;

    public string EnglishName => string.IsNullOrWhiteSpace(LocalizedName.English)
        ? "—"
        : LocalizedName.English;
}

public sealed record BattleRoomAttachmentCatalogResult(
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
}

public static class BattleRoomAttachmentCatalog
{
    private static readonly Regex TokenPattern = new(
        "\"(?:\\\\.|[^\"])*\"|\\S+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

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
                "活动 Mod 配置在房间附加内容目录加载前已经变化，请重新加载内容目录。");
        }

        var issues = new List<string>();
        var effectiveFiles = ResolveEffectivePropFiles(activeContent.Sources, issues);
        var fingerprints = effectiveFiles
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
            .GroupBy(
                candidate => $"{candidate.Definition.Kind}\n{candidate.Definition.Id}",
                StringComparer.OrdinalIgnoreCase)
            .Select(SelectRepresentative)
            .ToArray();

        var collisions = selected
            .GroupBy(candidate => candidate.Definition.PropHash)
            .Where(group => group
                .Select(candidate => candidate.Definition.Id)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Skip(1)
                .Any())
            .ToArray();
        var collidingIds = collisions
            .SelectMany(group => group.Select(candidate => candidate.Definition.Id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var collision in collisions)
        {
            issues.Add(
                $"Room attachment hash collision {collision.Key}: " +
                string.Join(", ", collision.Select(item => item.Definition.Id)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)));
        }

        var definitions = selected
            .Select(candidate => candidate.Definition)
            .Where(definition => !collidingIds.Contains(definition.Id))
            .ToArray();
        var localization = ContentLocalizationCatalog.Load(
            activeContent,
            definitions
                .Select(definition => ContentLocalizationCatalog.GetCurioTitleKey(definition.Id))
                .Distinct(StringComparer.OrdinalIgnoreCase));
        issues.AddRange(localization.Issues);
        definitions = definitions
            .Select(definition => definition with
            {
                LocalizedName = localization.GetCurioTitle(definition.Id)
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
            guard);
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
                "活动 Mod 配置在附加内容选择后已经变化，请重新加载内容目录。");
        }

        var issues = new List<string>();
        var currentFiles = ResolveEffectivePropFiles(guard.ActiveSources, issues)
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
                "有效房间奇物/宝箱定义在选择后已经变化，请重新加载内容目录。");
        }
    }

    public static void ValidateDefinition(BattleRoomAttachmentDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ValidateGuard(definition.CatalogGuard);
        if (definition.PropHash != ComputePropHash(definition.Id))
        {
            throw new InvalidOperationException("所选附加内容的 ID 与存档哈希不一致。");
        }

        var issues = new List<string>();
        var matches = ResolveEffectivePropFiles(definition.CatalogGuard.ActiveSources, issues)
            .SelectMany(file => ParseFile(
                file,
                definition.CatalogGuard.ActiveSources,
                definition.CatalogGuard,
                issues))
            .Count(candidate =>
                candidate.Definition.Kind == definition.Kind &&
                candidate.Definition.Id.Equals(definition.Id, StringComparison.OrdinalIgnoreCase) &&
                candidate.Definition.SourceLine == definition.SourceLine &&
                candidate.Definition.SourceRelativePath.Equals(
                    definition.SourceRelativePath,
                    StringComparison.OrdinalIgnoreCase) &&
                Path.GetFullPath(candidate.Definition.SourcePath).Equals(
                    Path.GetFullPath(definition.SourcePath),
                    StringComparison.OrdinalIgnoreCase) &&
                candidate.Definition.SourceSha256.Equals(
                    definition.SourceSha256,
                    StringComparison.OrdinalIgnoreCase));
        if (matches != 1)
        {
            throw new InvalidOperationException(
                "所选房间附加内容已变化、消失或存在歧义，请重新加载内容目录。");
        }
    }

    public static int ComputePropHash(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return unchecked((int)Loc2LocalizationReader.HashName(id.Trim()));
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
            foreach (var path in EnumeratePropFiles(source, enabledDlcPrefixes, issues))
            {
                candidates.Add(new ContentFileCandidate(source, path));
            }
        }

        return ContentFileOverlay.Resolve(candidates, "Room prop", issues);
    }

    private static IReadOnlyList<string> EnumeratePropFiles(
        ActiveContentSource source,
        IReadOnlyList<string> enabledDlcPrefixes,
        List<string> issues)
    {
        if (!Directory.Exists(source.Directory))
        {
            issues.Add($"Room prop content source is missing: {source.Directory}");
            return [];
        }

        if (source.Kind is "workshop" or "local")
        {
            var manifestPath = Path.Combine(source.Directory, "modfiles.txt");
            if (File.Exists(manifestPath))
            {
                return EnumerateManifestPropFiles(
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
                    "*.props.darkest",
                    SearchOption.AllDirectories)
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
        List<string> issues)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var rawLine in File.ReadLines(manifestPath))
            {
                var relativePath = ModManifestPath.Extract(rawLine, ".props.darkest");
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
                        $"Ignored room prop manifest path outside its Mod directory: {rawLine.Trim()}");
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
                    issues.Add($"Room prop file listed by Mod is missing: {path}");
                    continue;
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
        string[] lines;
        try
        {
            lines = File.ReadAllLines(file.Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            issues.Add($"Room prop file could not be read: {file.Path} ({ex.Message})");
            yield break;
        }

        var sourceSha256 = ComputeSha256(file.Path);
        var sourceLabel = ContentSourceLabelFormatter.Format(
            file.Source.Id,
            file.ProviderSources,
            activeSources);
        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var line = StripComment(lines[lineIndex]).Trim();
            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            var kind = line[..separator].Trim().ToLowerInvariant() switch
            {
                "room_curios" => BattleRoomAttachmentKind.Curio,
                "room_treasures" => BattleRoomAttachmentKind.Treasure,
                _ => (BattleRoomAttachmentKind?)null
            };
            if (kind is null)
            {
                continue;
            }

            var tokens = TokenPattern.Matches(line[(separator + 1)..])
                .Select(match => Unquote(match.Value))
                .ToArray();
            var typesIndex = Array.FindIndex(tokens, token =>
                token.Equals(".types", StringComparison.OrdinalIgnoreCase));
            if (typesIndex < 0)
            {
                issues.Add($"Room prop row has no .types list: {file.Path}:{lineIndex + 1}");
                continue;
            }

            var ids = tokens
                .Skip(typesIndex + 1)
                .TakeWhile(token => !token.StartsWith(".", StringComparison.Ordinal))
                .Where(token => !string.IsNullOrWhiteSpace(token))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (ids.Length == 0)
            {
                issues.Add($"Room prop row has an empty .types list: {file.Path}:{lineIndex + 1}");
                continue;
            }

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
                        lineIndex + 1,
                        guard));
            }
        }
    }

    private static string StripComment(string line)
    {
        var comment = line.IndexOf("//", StringComparison.Ordinal);
        return comment >= 0 ? line[..comment] : line;
    }

    private static string Unquote(string value) =>
        value.Length >= 2 && value[0] == '"' && value[^1] == '"'
            ? value[1..^1].Replace("\\\"", "\"", StringComparison.Ordinal)
            : value;

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
        BattleRoomAttachmentDefinition Definition);
}
