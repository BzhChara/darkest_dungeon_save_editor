using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace DarkestDungeonSaveEditor.Core;

public enum BattleEncounterSourceKind
{
    Standard,
    Conditional,
    Additional
}

public enum BattleEncounterClassification
{
    Ordinary,
    FixedBoss,
    RoamingBoss,
    RoamingEncounter,
    ConditionalOrAdditional
}

public sealed record BattleEncounterFileFingerprint(
    string SourceId,
    string Path,
    string RelativePath,
    string Sha256);

public sealed record BattleEncounterTableGuard(
    string DungeonId,
    int Difficulty,
    string GameSavePath,
    string GameSaveSha256,
    IReadOnlyList<ActiveContentSource> ActiveSources,
    IReadOnlyList<BattleEncounterFileFingerprint> EffectiveFiles,
    string Fingerprint);

public sealed record BattleEncounterDefinition(
    int MashType,
    int? MashIndex,
    BattleEncounterSourceKind SourceKind,
    IReadOnlyList<string> MonsterIds,
    double? Weight,
    string SourceLabel,
    string SourcePath,
    string SourceSha256,
    int SourceLine,
    bool CanPlaceDirectly,
    string UnavailableReason,
    BattleEncounterTableGuard TableGuard)
{
    public string DisplayName => string.Join(" + ", MonsterIds);

    public string ChineseDisplayName =>
        MonsterNames.Count == MonsterIds.Count
            ? string.Join(
                " + ",
                MonsterNames.Select(name =>
                    string.IsNullOrWhiteSpace(name.Chinese) ? "—" : name.Chinese))
            : "—";

    public string OriginDungeonId { get; init; } = string.Empty;

    public int OriginDifficulty { get; init; }

    public string SourceRelativePath { get; init; } = string.Empty;

    public string? RoamingId { get; init; }

    public BattleEncounterClassification Classification { get; init; }

    public bool HasKnownClassification { get; init; } = true;

    public bool ContainsBossMonster { get; init; }

    public IReadOnlyList<BilingualContentName> MonsterNames { get; init; } = [];

    public BattleMapAreaKind TargetAreaKind => MashType == 0
        ? BattleMapAreaKind.Corridor
        : BattleMapAreaKind.Room;
}

public sealed record BattleEncounterCatalogResult(
    string DungeonId,
    int Difficulty,
    IReadOnlyList<BattleEncounterDefinition> Encounters,
    IReadOnlyList<string> Issues,
    BattleEncounterTableGuard TableGuard)
{
    public IReadOnlyList<BattleEncounterDefinition> BridgeEncounters { get; init; } = [];

    public bool HasActiveGeneratedBridge { get; init; }

    public IReadOnlyList<BattleEncounterDefinition> DirectEncounters => Encounters
        .Where(encounter => encounter.CanPlaceDirectly)
        .ToArray();

    public IReadOnlyList<BattleEncounterDefinition> SpecialEncounters => Encounters
        .Where(encounter => encounter.SourceKind is
            BattleEncounterSourceKind.Conditional or BattleEncounterSourceKind.Additional)
        .ToArray();
}

public static partial class BattleEncounterCatalog
{
    private static readonly Regex TokenPattern = new(
        "\"(?:\\\\.|[^\"])*\"|\\S+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    public static BattleEncounterCatalogResult Load(
        ActiveContentSnapshot activeContent,
        BattleMapSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(activeContent);
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!Path.GetFullPath(activeContent.Profile.ProfileDirectory).Equals(
                Path.GetFullPath(snapshot.ProfileDirectory),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("活动内容目录与当前战斗地图不属于同一个档案。");
        }

        var issues = new List<string>();
        var effectiveFiles = ResolveEffectiveMashFiles(
            activeContent.Sources,
            snapshot.DungeonId,
            snapshot.Difficulty,
            issues);
        var gameSavePath = Path.Combine(snapshot.ProfileDirectory, "persist.game.json");
        if (!File.Exists(gameSavePath))
        {
            throw new FileNotFoundException("当前档案缺少 persist.game.json。", gameSavePath);
        }
        if (!ComputeSha256(gameSavePath).Equals(
                activeContent.SourceGameSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "活动 Mod 配置在目录加载后已经变化，请重新加载内容目录。");
        }

        var fingerprints = effectiveFiles
            .Select(file => new BattleEncounterFileFingerprint(
                file.Source.Id,
                Path.GetFullPath(file.Path),
                file.RelativePath,
                ComputeSha256(file.Path)))
            .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(file => file.SourceId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var guard = new BattleEncounterTableGuard(
            snapshot.DungeonId,
            snapshot.Difficulty,
            Path.GetFullPath(gameSavePath),
            activeContent.SourceGameSha256,
            activeContent.Sources.ToArray(),
            fingerprints,
            ComputeTableFingerprint(fingerprints));

        var parsed = effectiveFiles
            .SelectMany(file => ParseFile(file, activeContent.Sources, guard, issues))
            .ToArray();
        var directFileCounts = parsed
            .Where(entry => entry.SourceKind == BattleEncounterSourceKind.Standard)
            .GroupBy(entry => entry.MashType)
            .ToDictionary(
                group => group.Key,
                group => group.Select(entry => entry.SourcePath)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count());
        var indexesByType = new Dictionary<int, int>();
        var encounters = new List<BattleEncounterDefinition>(parsed.Length);
        foreach (var entry in parsed)
        {
            if (entry.SourceKind != BattleEncounterSourceKind.Standard)
            {
                encounters.Add(entry with
                {
                    MashIndex = null,
                    CanPlaceDirectly = false,
                    UnavailableReason = "条件/额外遭遇需要先桥接到当前普通遭遇表"
                });
                continue;
            }

            var nextIndex = indexesByType.GetValueOrDefault(entry.MashType);
            indexesByType[entry.MashType] = nextIndex + 1;
            if (directFileCounts.GetValueOrDefault(entry.MashType) != 1)
            {
                encounters.Add(entry with
                {
                    MashIndex = null,
                    CanPlaceDirectly = false,
                    UnavailableReason = "该遭遇类型由多个文件共同扩展，尚不能证明运行时索引顺序"
                });
                continue;
            }

            encounters.Add(entry with
            {
                MashIndex = nextIndex,
                CanPlaceDirectly = true,
                UnavailableReason = string.Empty
            });
        }

        var globalIssues = new List<string>();
        var globalRows = ResolveGlobalEffectiveMashFiles(
                activeContent.Sources,
                globalIssues)
            .SelectMany(file => ParseFile(file, activeContent.Sources, guard, globalIssues))
            .ToArray();
        var bridgeCandidateRows = globalRows.Where(IsBridgeCandidate).ToArray();
        var availableMonsters = ResolveAvailableMonsterDefinitions(
            activeContent.Sources,
            globalIssues);
        var localization = ContentLocalizationCatalog.Load(
            activeContent,
            parsed
                .Concat(bridgeCandidateRows)
                .SelectMany(encounter => encounter.MonsterIds)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(ContentLocalizationCatalog.GetMonsterNameKey));
        globalIssues.AddRange(localization.Issues);
        var classifiedRows = encounters.Concat(globalRows)
            .DistinctBy(ManagedBattleEncounterBridgeService.ClassificationKey, StringComparer.OrdinalIgnoreCase)
            .Select(encounter => ClassifyEncounter(encounter, availableMonsters.BossIds)).ToArray();
        var managedClassifications = ManagedBattleEncounterBridgeService.ReadClassificationOverrides(
            activeContent, classifiedRows, globalIssues);
        BattleEncounterDefinition ClassifyWithOrigin(BattleEncounterDefinition encounter)
        {
            var classified = ClassifyEncounter(encounter, availableMonsters.BossIds);
            return managedClassifications.TryGetValue(ManagedBattleEncounterBridgeService.ClassificationKey(encounter), out var original)
                ? classified with { Classification = original ?? classified.Classification, HasKnownClassification = original.HasValue }
                : classified;
        }
        for (var index = 0; index < encounters.Count; index++)
        {
            encounters[index] = LocalizeEncounter(
                ClassifyWithOrigin(encounters[index]),
                localization);
        }

        var unresolvedBridgeRows = bridgeCandidateRows
            .Select(encounter => new
            {
                Encounter = encounter,
                MissingIds = encounter.MonsterIds
                    .Where(monsterId => !availableMonsters.Ids.Contains(monsterId))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            })
            .Where(item => item.MissingIds.Length > 0)
            .ToArray();
        if (unresolvedBridgeRows.Length > 0)
        {
            var unresolvedIds = unresolvedBridgeRows
                .SelectMany(item => item.MissingIds)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            globalIssues.Add(
                $"Excluded {unresolvedBridgeRows.Length} Bridge encounter rows with " +
                $"{unresolvedIds.Length} unresolved active monster ids: " +
                string.Join(", ", unresolvedIds));
        }

        var bridgeEncounters = bridgeCandidateRows
            .Where(encounter => encounter.MonsterIds.All(availableMonsters.Ids.Contains))
            .Select(encounter => LocalizeEncounter(
                ClassifyWithOrigin(encounter) with
                {
                    MashIndex = null,
                    CanPlaceDirectly = false,
                    UnavailableReason = "需要 Encounter Bridge 才能写入当前副本遭遇表"
                },
                localization))
            .Where(encounter => encounter.HasKnownClassification)
            .OrderBy(encounter => encounter.OriginDungeonId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(encounter => encounter.OriginDifficulty)
            .ThenBy(encounter => encounter.MashType)
            .ThenBy(encounter => encounter.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(encounter => encounter.SourcePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(encounter => encounter.SourceLine)
            .ToArray();
        issues.AddRange(globalIssues);

        return new BattleEncounterCatalogResult(
            snapshot.DungeonId,
            snapshot.Difficulty,
            encounters,
            issues.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            guard)
        {
            BridgeEncounters = bridgeEncounters,
            HasActiveGeneratedBridge = activeContent.Sources.Any(source =>
                ManagedBattleEncounterBridgeService.IsManagedBridgeSource(source) ||
                source.DisplayName.StartsWith(
                    "DDSE Encounter Bridge",
                    StringComparison.OrdinalIgnoreCase) ||
                File.Exists(Path.Combine(source.Directory, "ddse-encounter-bridge.json")) ||
                File.Exists(Path.Combine(source.Directory, "ddse-encounter-bridge-probe.json")))
        };
    }

    public static void ValidateGuard(BattleEncounterTableGuard guard)
    {
        ArgumentNullException.ThrowIfNull(guard);
        if (!File.Exists(guard.GameSavePath) ||
            !ComputeSha256(guard.GameSavePath).Equals(
                guard.GameSaveSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "活动 Mod 配置在遭遇选择后已经变化，请重新加载内容目录。");
        }

        var issues = new List<string>();
        var currentFiles = ResolveEffectiveMashFiles(
                guard.ActiveSources,
                guard.DungeonId,
                guard.Difficulty,
                issues)
            .Select(file => new BattleEncounterFileFingerprint(
                file.Source.Id,
                Path.GetFullPath(file.Path),
                file.RelativePath,
                ComputeSha256(file.Path)))
            .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(file => file.SourceId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (issues.Count > 0 ||
            !currentFiles.SequenceEqual(guard.EffectiveFiles) ||
            !ComputeTableFingerprint(currentFiles).Equals(
                guard.Fingerprint,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "当前副本的有效遭遇文件或索引输入已经变化，请重新加载内容目录。");
        }
    }

    public static void ValidateDirectEncounter(BattleEncounterDefinition encounter)
    {
        ArgumentNullException.ThrowIfNull(encounter);
        if (!encounter.CanPlaceDirectly ||
            encounter.MashIndex is null or < 0 ||
            encounter.SourceKind != BattleEncounterSourceKind.Standard)
        {
            throw new InvalidOperationException("所选遭遇不是可直接写入的标准表条目。");
        }

        ValidateGuard(encounter.TableGuard);
        var parsed = ParseGuardedFiles(encounter.TableGuard);
        var rows = parsed
            .Where(candidate =>
                candidate.SourceKind == BattleEncounterSourceKind.Standard &&
                candidate.MashType == encounter.MashType)
            .ToArray();
        if (rows.Select(candidate => candidate.SourcePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Skip(1)
                .Any() ||
            encounter.MashIndex.Value >= rows.Length)
        {
            throw new InvalidOperationException(
                "当前遭遇类型不再具有唯一且可证明的运行时索引。");
        }

        var expected = rows[encounter.MashIndex.Value];
        if (!SameEncounterIdentity(encounter, expected))
        {
            throw new InvalidOperationException(
                "所选遭遇的索引、组成或来源与当前有效表不一致，请重新加载内容目录。");
        }
    }

    public static void ValidateSpecialEncounter(BattleEncounterDefinition encounter)
    {
        ArgumentNullException.ThrowIfNull(encounter);
        if (encounter.CanPlaceDirectly ||
            encounter.MashIndex is not null ||
            encounter.SourceKind is not (
                BattleEncounterSourceKind.Conditional or BattleEncounterSourceKind.Additional))
        {
            throw new InvalidOperationException("所选遭遇不是可桥接的条件或额外遭遇。");
        }

        ValidateGuard(encounter.TableGuard);
        var matches = ParseGuardedFiles(encounter.TableGuard)
            .Where(candidate =>
                candidate.SourceKind is
                    BattleEncounterSourceKind.Conditional or BattleEncounterSourceKind.Additional &&
                SameEncounterIdentity(encounter, candidate))
            .Take(2)
            .Count();
        if (matches != 1)
        {
            throw new InvalidOperationException(
                "所选特殊遭遇已变化、消失或存在歧义，请重新加载内容目录。");
        }
    }

    public static void ValidateBridgeEncounter(BattleEncounterDefinition encounter)
    {
        ArgumentNullException.ThrowIfNull(encounter);
        if (!encounter.HasKnownClassification || !IsBridgeCandidate(encounter) ||
            encounter.CanPlaceDirectly ||
            encounter.MashIndex is not null)
        {
            throw new InvalidOperationException(
                "所选遭遇不是可桥接的普通战斗、游荡首领、特殊遭遇或固定首领。");
        }

        ValidateGuard(encounter.TableGuard);
        var issues = new List<string>();
        var availableMonsters = ResolveAvailableMonsterDefinitions(
            encounter.TableGuard.ActiveSources,
            issues);
        var missingMonsterIds = encounter.MonsterIds
            .Where(monsterId => !availableMonsters.Ids.Contains(monsterId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (missingMonsterIds.Length > 0)
        {
            throw new InvalidOperationException(
                "所选遭遇引用了当前启用内容中不存在的敌方定义：" +
                string.Join(", ", missingMonsterIds));
        }

        var matchingFile = ResolveGlobalEffectiveMashFiles(
                encounter.TableGuard.ActiveSources,
                issues)
            .SingleOrDefault(file =>
                file.RelativePath.Equals(
                    encounter.SourceRelativePath,
                    StringComparison.OrdinalIgnoreCase));
        if (matchingFile is null)
        {
            throw new InvalidOperationException(
                "所选遭遇的有效来源文件已经消失或被其他内容覆盖，请重新加载内容目录。");
        }

        var matches = ParseFile(
                matchingFile,
                encounter.TableGuard.ActiveSources,
                encounter.TableGuard,
                issues)
            .Where(candidate => IsBridgeCandidate(candidate) &&
                                SameEncounterIdentity(encounter, candidate))
            .Take(2)
            .Count();
        if (matches != 1)
        {
            throw new InvalidOperationException(
                "所选遭遇已变化、消失或存在歧义，请重新加载内容目录。");
        }
    }

    private static IReadOnlyList<EffectiveContentFile> ResolveEffectiveMashFiles(
        IReadOnlyList<ActiveContentSource> sources,
        string dungeonId,
        int difficulty,
        List<string> issues)
    {
        var enabledDlcPrefixes = ContentFileOverlay.GetEnabledDlcPrefixes(sources);
        var candidates = new List<ContentFileCandidate>();
        foreach (var source in sources)
        {
            var discoveryIssues = new List<string>();
            foreach (var path in EnumerateMashFiles(source, enabledDlcPrefixes, discoveryIssues))
            {
                var relativePath = ContentFileOverlay.NormalizeRelativePath(source, path);
                if (relativePath is not null &&
                    IsCurrentDungeonDifficultyFile(relativePath, dungeonId, difficulty))
                {
                    candidates.Add(new ContentFileCandidate(source, path));
                }
            }

            issues.AddRange(discoveryIssues.Where(issue =>
                issue.StartsWith("Encounter content source is missing:", StringComparison.Ordinal) ||
                issue.StartsWith("Encounter content source could not be scanned:", StringComparison.Ordinal) ||
                issue.StartsWith("Encounter Mod manifest could not be read:", StringComparison.Ordinal)));
        }

        return ContentFileOverlay.Resolve(candidates, "Encounter mash", issues);
    }

    private static IReadOnlyList<BattleEncounterDefinition> ParseGuardedFiles(
        BattleEncounterTableGuard guard)
    {
        var issues = new List<string>();
        var effectiveFiles = ResolveEffectiveMashFiles(
            guard.ActiveSources,
            guard.DungeonId,
            guard.Difficulty,
            issues);
        var parsed = effectiveFiles
            .SelectMany(file => ParseFile(file, guard.ActiveSources, guard, issues))
            .ToArray();
        if (issues.Count > 0)
        {
            throw new InvalidOperationException(
                "当前遭遇表无法完整复核，请重新加载内容目录。");
        }

        return parsed;
    }

    private static bool SameEncounterIdentity(
        BattleEncounterDefinition left,
        BattleEncounterDefinition right) =>
        left.MashType == right.MashType &&
        left.SourceKind == right.SourceKind &&
        left.SourceLine == right.SourceLine &&
        Path.GetFullPath(left.SourcePath).Equals(
            Path.GetFullPath(right.SourcePath),
            StringComparison.OrdinalIgnoreCase) &&
        left.SourceSha256.Equals(right.SourceSha256, StringComparison.OrdinalIgnoreCase) &&
        left.SourceRelativePath.Equals(
            right.SourceRelativePath,
            StringComparison.OrdinalIgnoreCase) &&
        left.OriginDungeonId.Equals(
            right.OriginDungeonId,
            StringComparison.OrdinalIgnoreCase) &&
        left.OriginDifficulty == right.OriginDifficulty &&
        string.Equals(left.RoamingId, right.RoamingId, StringComparison.Ordinal) &&
        Nullable.Equals(left.Weight, right.Weight) &&
        left.MonsterIds.SequenceEqual(right.MonsterIds, StringComparer.Ordinal);

    private static bool IsBridgeCandidate(BattleEncounterDefinition encounter) =>
        ((encounter.SourceKind is BattleEncounterSourceKind.Conditional or
            BattleEncounterSourceKind.Additional) && encounter.MashType is 0 or 1) ||
        encounter.SourceKind == BattleEncounterSourceKind.Standard &&
        encounter.MashType is 0 or 1 or 2;

    private static BattleEncounterDefinition ClassifyEncounter(
        BattleEncounterDefinition encounter,
        IReadOnlySet<string> bossMonsterIds)
    {
        var containsBossMonster = encounter.MonsterIds.Any(bossMonsterIds.Contains);
        var classification = encounter.MashType switch
        {
            2 => BattleEncounterClassification.FixedBoss,
            0 or 1 when !string.IsNullOrWhiteSpace(encounter.RoamingId) && containsBossMonster =>
                BattleEncounterClassification.RoamingBoss,
            0 or 1 when !string.IsNullOrWhiteSpace(encounter.RoamingId) =>
                BattleEncounterClassification.RoamingEncounter,
            0 or 1 when encounter.SourceKind is
                    BattleEncounterSourceKind.Conditional or BattleEncounterSourceKind.Additional ||
                encounter.Weight is <= 0 =>
                BattleEncounterClassification.ConditionalOrAdditional,
            _ => BattleEncounterClassification.Ordinary
        };
        return encounter with
        {
            Classification = classification,
            ContainsBossMonster = containsBossMonster
        };
    }

    private static BattleEncounterDefinition LocalizeEncounter(
        BattleEncounterDefinition encounter,
        ContentLocalizationCatalog localization) =>
        encounter with
        {
            MonsterNames = encounter.MonsterIds
                .Select(localization.GetMonsterName)
                .ToArray()
        };

    private static bool IsCurrentDungeonDifficultyFile(
        string relativePath,
        string dungeonId,
        int difficulty)
    {
        var normalized = relativePath.Replace('\\', '/');
        var dungeonSegment = $"/dungeons/{dungeonId.Trim()}/";
        var padded = $"/{normalized.TrimStart('/')}";
        return padded.Contains(dungeonSegment, StringComparison.OrdinalIgnoreCase) &&
               Path.GetFileName(normalized).EndsWith(
                   $".{difficulty.ToString(CultureInfo.InvariantCulture)}.mash.darkest",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<BattleEncounterDefinition> ParseFile(
        EffectiveContentFile file,
        IReadOnlyList<ActiveContentSource> activeSources,
        BattleEncounterTableGuard guard,
        List<string> issues)
    {
        string[] lines;
        try
        {
            lines = File.ReadAllLines(file.Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            issues.Add($"Encounter mash could not be read: {file.Path} ({ex.Message})");
            yield break;
        }

        if (!TryDescribeMashFile(
                file.RelativePath,
                out var originDungeonId,
                out var originDifficulty))
        {
            issues.Add($"Encounter mash path has no dungeon/difficulty identity: {file.Path}");
            yield break;
        }

        var sourceKind = ClassifyFile(file.Path);
        var sourceHash = ComputeSha256(file.Path);
        var sourceLabel = ContentSourceLabelFormatter.Format(
            file.Source.Id,
            file.ProviderSources,
            activeSources);
        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var line = StripComment(lines[lineIndex]).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }
            var kind = line[..separator].Trim();
            var mashType = kind.ToLowerInvariant() switch
            {
                "hall" => 0,
                "room" => 1,
                "boss" => 2,
                _ => -1
            };
            if (mashType < 0)
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
                issues.Add($"Encounter row has no .types list: {file.Path}:{lineIndex + 1}");
                continue;
            }
            var monsters = tokens
                .Skip(typesIndex + 1)
                .TakeWhile(token => !token.StartsWith(".", StringComparison.Ordinal))
                .Where(token => !string.IsNullOrWhiteSpace(token))
                .ToArray();
            if (monsters.Length == 0)
            {
                issues.Add($"Encounter row has an empty .types list: {file.Path}:{lineIndex + 1}");
                continue;
            }

            double? weight = null;
            var chanceIndex = Array.FindIndex(tokens, token =>
                token.Equals(".chance", StringComparison.OrdinalIgnoreCase));
            if (chanceIndex >= 0 && chanceIndex + 1 < tokens.Length &&
                double.TryParse(
                    tokens[chanceIndex + 1],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var parsedWeight))
            {
                weight = parsedWeight;
            }

            string? roamingId = null;
            var roamingIdIndex = Array.FindIndex(tokens, token =>
                token.Equals(".random_dungeon_roaming_id", StringComparison.OrdinalIgnoreCase));
            if (roamingIdIndex >= 0 && roamingIdIndex + 1 < tokens.Length &&
                !tokens[roamingIdIndex + 1].StartsWith(".", StringComparison.Ordinal))
            {
                roamingId = tokens[roamingIdIndex + 1];
            }

            yield return new BattleEncounterDefinition(
                mashType,
                null,
                sourceKind,
                monsters,
                weight,
                sourceLabel,
                Path.GetFullPath(file.Path),
                sourceHash,
                lineIndex + 1,
                false,
                string.Empty,
                guard)
            {
                OriginDungeonId = originDungeonId,
                OriginDifficulty = originDifficulty,
                SourceRelativePath = file.RelativePath,
                RoamingId = roamingId
            };
        }
    }

    internal static BattleEncounterSourceKind ClassifyFile(string path)
    {
        var fileName = Path.GetFileName(path);
        if (fileName.Contains(".conditional.", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("conditional.", StringComparison.OrdinalIgnoreCase))
        {
            return BattleEncounterSourceKind.Conditional;
        }
        if (fileName.Contains(".additional.", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("additional.", StringComparison.OrdinalIgnoreCase))
        {
            return BattleEncounterSourceKind.Additional;
        }

        return BattleEncounterSourceKind.Standard;
    }

    private static string StripComment(string line)
    {
        var quoted = false;
        for (var index = 0; index < line.Length - 1; index++)
        {
            if (line[index] == '"' && (index == 0 || line[index - 1] != '\\'))
            {
                quoted = !quoted;
            }
            if (!quoted && line[index] == '/' && line[index + 1] == '/')
            {
                return line[..index];
            }
        }

        return line;
    }

    private static string Unquote(string token) =>
        token.Length >= 2 && token[0] == '"' && token[^1] == '"'
            ? token[1..^1].Replace("\\\"", "\"", StringComparison.Ordinal)
            : token;

    private static string ComputeTableFingerprint(
        IReadOnlyList<BattleEncounterFileFingerprint> files)
    {
        var canonical = string.Join(
            "\n",
            files.Select(file =>
                $"{file.SourceId}\t{file.RelativePath}\t{file.Path}\t{file.Sha256}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
