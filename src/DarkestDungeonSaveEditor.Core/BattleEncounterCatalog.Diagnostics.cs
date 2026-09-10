namespace DarkestDungeonSaveEditor.Core;

public static partial class BattleEncounterCatalog
{
    // These suffixes are observed native mash fields, not repair rules. A suffix
    // is only a diagnostic clue when the complete ID is missing and its prefix exists.
    private static readonly string[] GluedMonsterFieldSuffixes = [".limit", ".quirk_tag"];

    private sealed record UnresolvedEncounterDiagnostic(
        BattleEncounterDefinition Encounter,
        string[] UnresolvedTokens,
        string[] GluedFieldTokens);

    private static UnresolvedEncounterDiagnostic CreateUnresolvedEncounterDiagnostic(
        BattleEncounterDefinition encounter,
        string[] unresolvedTokens,
        IReadOnlySet<string> availableIds) => new(
            encounter,
            unresolvedTokens,
            unresolvedTokens.Where(token => GluedMonsterFieldSuffixes.Any(suffix =>
                token.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) &&
                availableIds.Contains(token[..^suffix.Length]))).ToArray());

    private static void AddUnresolvedEncounterIssues(
        IReadOnlyList<BattleEncounterDefinition> candidates,
        IReadOnlySet<string> availableIds,
        List<string> issues)
    {
        var rows = candidates
            .Select(encounter => CreateUnresolvedEncounterDiagnostic(
                encounter, GetMissingMonsterIds(encounter, availableIds), availableIds))
            .Where(row => row.UnresolvedTokens.Length > 0)
            .OrderBy(row => row.Encounter.SourcePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.Encounter.SourceLine)
            .ThenBy(row => row.Encounter.SourceRecordIndex)
            .ToArray();
        if (rows.Length == 0)
        {
            return;
        }

        var gluedRows = rows.Count(row => row.GluedFieldTokens.Length > 0);
        var unresolvedTokenCount = rows.SelectMany(row => row.UnresolvedTokens)
            .Distinct(StringComparer.Ordinal).Count();
        issues.Add(
            $"遭遇排除汇总（全局 Bridge）：{rows.Length} 条遭遇行；" +
            $"疑似字段粘连 {gluedRows} 条，其他未找到活动怪物定义 {rows.Length - gluedRows} 条；" +
            $"未解析字符串 {unresolvedTokenCount} 个（去重，不等于缺失怪物数量）。" +
            "每行只计一类；当前副本不可直写的同一行不重复计数。仅排除这些组合，未禁用整个 Mod。");
        foreach (var row in rows)
        {
            issues.Add("遭遇排除明细（全局 Bridge）：" + FormatUnresolvedEncounter(row));
        }
    }

    private static string FormatUnresolvedEncounter(UnresolvedEncounterDiagnostic row)
    {
        var encounter = row.Encounter;
        var location = encounter.MashType switch
        {
            0 => "走廊 hall",
            1 => "房间 room",
            _ => "首领房间 boss"
        };
        var reason = row.GluedFieldTokens.Length > 0
            ? "疑似字段粘连；粘连线索=" + string.Join(", ", row.GluedFieldTokens) +
              "（去掉字段后缀的 ID 存在，但完整字符串不存在）；" +
              "后续字段值可能混入 .types，不应全算作缺失怪物；编辑器不自动拆分，请核对原始行的字段分隔"
            : "未找到活动怪物定义；仅凭此项无法区分拼写错误、依赖未启用或缺少对应难度版本；不猜测替代 ID";
        return $"{reason}；来源={encounter.SourceLabel}；" +
            $"文件={encounter.SourcePath}:{encounter.SourceLine}；记录={encounter.SourceRecordIndex}；" +
            $"地区={encounter.OriginDungeonId}；难度={encounter.OriginDifficulty}；位置={location}；" +
            $"原始 .types 解析结果=[{string.Join(", ", encounter.MonsterIds)}]；" +
            $"未解析字符串=[{string.Join(", ", row.UnresolvedTokens)}]";
    }
}
