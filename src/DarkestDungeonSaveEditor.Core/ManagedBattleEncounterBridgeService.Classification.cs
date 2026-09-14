namespace DarkestDungeonSaveEditor.Core;

public sealed partial class ManagedBattleEncounterBridgeService
{
    internal static string ClassificationKey(BattleEncounterDefinition encounter) =>
        $"{Path.GetFullPath(encounter.SourcePath)}\n{encounter.SourceRecordIndex}";

    internal static IReadOnlyDictionary<string, BattleEncounterClassification?> ReadClassificationOverrides(
        ActiveContentSnapshot content,
        IReadOnlyList<BattleEncounterDefinition> classifiedRows,
        List<string> issues)
    {
        var result = new Dictionary<string, BattleEncounterClassification?>(StringComparer.OrdinalIgnoreCase);
        var origins = new List<(BattleEncounterDefinition Row, ManagedBridgeEncounterManifest Entry)>();
        foreach (var source in content.Sources.Where(IsManagedBridgeSource))
        {
            try
            {
                var manifest = ReadManifest(Path.Combine(source.Directory, ManifestFileName));
                ValidateManifestIdentity(manifest, content.Profile, GetProjectTitle(content.Profile));
                foreach (var table in manifest.Tables)
                {
                    var path = Path.GetFullPath(Path.Combine(source.Directory, table.RelativeMashPath));
                    var rows = classifiedRows.Where(row => Path.GetFullPath(row.SourcePath)
                            .Equals(path, StringComparison.OrdinalIgnoreCase))
                        .OrderBy(row => row.SourceRecordIndex).ToArray();
                    if (rows.Length == 0)
                    {
                        continue;
                    }
                    if (rows.Any(row => !row.SourceSha256.Equals(table.GeneratedMashSha256, StringComparison.OrdinalIgnoreCase)))
                    {
                        throw new InvalidDataException("遭遇表哈希与清单不一致");
                    }
                    foreach (var entry in table.Entries)
                    {
                        var row = rows.Where(row => row.SourceKind == BattleEncounterSourceKind.Standard &&
                                                    row.MashType == entry.MashType)
                            .ElementAtOrDefault(entry.FileRowIndex!.Value);
                        if (row is null ||
                            (row.MashIndex is { } runtimeIndex && runtimeIndex != entry.MashIndex) ||
                            !row.MonsterIds.SequenceEqual(entry.MonsterIds, StringComparer.Ordinal))
                        {
                            throw new InvalidDataException("追加索引或敌方组成与清单不一致");
                        }
                        var key = ClassificationKey(row);
                        if (!result.TryAdd(key, null))
                        {
                            throw new InvalidDataException("清单含重复追加索引");
                        }
                        origins.Add((row, entry));
                    }
                }
            }
            catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException)
            {
                issues.Add($"托管 Bridge 分类读取失败：{source.DisplayName}；{error.Message}。未确认的零权重条目不进入生成目录。");
                var sourcePrefix = Path.GetFullPath(source.Directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                var sourceRows = classifiedRows.Where(row => Path.GetFullPath(row.SourcePath)
                    .StartsWith(sourcePrefix, StringComparison.OrdinalIgnoreCase)).ToArray();
                var sourceKeys = sourceRows.Select(ClassificationKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
                origins.RemoveAll(origin => sourceKeys.Contains(ClassificationKey(origin.Row)));
                foreach (var row in sourceRows.Where(row => row.Weight is <= 0))
                {
                    result[ClassificationKey(row)] = null;
                }
            }
        }

        foreach (var (row, entry) in origins)
        {
            BattleEncounterClassification? classification = null;
            if (Enum.TryParse<BattleEncounterClassification>(entry.Classification, out var recorded) &&
                Enum.IsDefined(recorded) &&
                (row.MashType == 2) == (recorded == BattleEncounterClassification.FixedBoss))
            {
                classification = recorded;
            }
            else if (entry.Classification is null)
            {
                // Recover optional classification metadata from the authored source
                // without confusing its weight with the zero-weight carrier row.
                if (row.MashType == 2)
                {
                    classification = BattleEncounterClassification.FixedBoss;
                }
                else if (!string.IsNullOrWhiteSpace(entry.RoamingId))
                {
                    classification = row.ContainsBossMonster
                        ? BattleEncounterClassification.RoamingBoss
                        : BattleEncounterClassification.RoamingEncounter;
                }
                else if (entry.SourceKind is nameof(BattleEncounterSourceKind.Conditional) or nameof(BattleEncounterSourceKind.Additional))
                {
                    classification = BattleEncounterClassification.ConditionalOrAdditional;
                }
                else
                {
                    var matches = classifiedRows.Where(candidate =>
                            !result.ContainsKey(ClassificationKey(candidate)) &&
                            candidate.SourceKind.ToString() == entry.SourceKind &&
                            candidate.MashType == entry.MashType && candidate.SourceLine == entry.SourceLine &&
                            (entry.SourceRecordIndex is null || candidate.SourceRecordIndex == entry.SourceRecordIndex) &&
                            candidate.OriginDifficulty == entry.OriginDifficulty &&
                            candidate.OriginDungeonId.Equals(entry.OriginDungeonId, StringComparison.Ordinal) &&
                            candidate.SourceRelativePath.Equals(entry.SourceRelativePath, StringComparison.OrdinalIgnoreCase) &&
                            candidate.MonsterIds.SequenceEqual(entry.MonsterIds, StringComparer.Ordinal))
                        .Select(candidate => candidate.Classification).Distinct().ToArray();
                    if (matches.Length == 1)
                    {
                        classification = matches[0];
                    }
                }
            }
            result[ClassificationKey(row)] = classification;
            if (classification is null)
            {
                issues.Add($"托管 Bridge 条目无法确认原始分类，已从生成目录排除：{row.DisplayName}。既有地图和索引保持不变。");
            }
        }
        return result;
    }
}
