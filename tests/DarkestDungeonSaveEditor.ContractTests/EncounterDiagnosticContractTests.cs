internal static partial class ContractSuite
{
    private static void VerifyEncounterDiagnosticContracts(
        ActiveContentSnapshot content,
        BattleMapSnapshot snapshot,
        string runRoot)
    {
        var root = Path.Combine(runRoot, "encounter-diagnostic-contracts");
        var dungeonRoot = Path.Combine(root, "dungeons", snapshot.DungeonId);
        Directory.CreateDirectory(dungeonRoot);
        CreateBattleMonsterDefinitions(root, ["probe_B", "literal_B", "literal_B.limit"], []);
        var mashPath = Path.Combine(dungeonRoot, $"{snapshot.DungeonId}.{snapshot.Difficulty}.mash.darkest");
        File.WriteAllText(mashPath,
            """
            hall: .chance 1 .types probe_B
            hall: .chance 1 .types missing_B missing_B
            hall: .chance 1 .types probe_B.limit 1
            room: .chance 1 .types probe_B.quirk_tag watched .limit 1
            room: .chance 1 .types probe_B.quirk_tag watched_evolved .limit 1
            boss: .chance 1 .types absent_B probe_B.limit 1
            hall: .chance 1 .types probe_B.custom
            hall: .chance 1 .types unknown.limit
            hall: .chance 1 .types "literal_B.limit"
            hall: .chance 1 .types probe_B .limit 1
            room: .chance 1 .types probe_B .quirk_tag watched .limit 1
            """, new UTF8Encoding(false));
        var isolated = content with
        {
            Sources = [new ActiveContentSource("base", "base", "base", root, 0)]
        };
        var mashHash = ComputeSha256(mashPath);
        var gamePath = Path.Combine(content.Profile.ProfileDirectory, "persist.game.json");
        var gameHash = ComputeSha256(gamePath);
        var catalog = BattleEncounterCatalog.Load(isolated, snapshot);
        var summary = catalog.Issues.Single(issue => issue.StartsWith("遭遇排除汇总", StringComparison.Ordinal));
        var details = catalog.Issues.Where(issue => issue.StartsWith("遭遇排除明细", StringComparison.Ordinal)).ToArray();
        Assert(summary.Contains("9 条遭遇行；疑似字段粘连 4 条，其他未找到活动怪物定义 5 条", StringComparison.Ordinal) &&
               summary.Contains("未解析字符串 11 个（去重，不等于缺失怪物数量）", StringComparison.Ordinal) &&
               summary.Contains("每行只计一类", StringComparison.Ordinal) &&
               details.Length == 9,
            "Exclusion logs must count source rows separately from unique unresolved tokens, count a mixed row only once, and not double-count direct failures.");
        var glued = details.Single(issue => issue.Contains($"{mashPath}:4；", StringComparison.Ordinal));
        Assert(glued.Contains("疑似字段粘连", StringComparison.Ordinal) &&
               glued.Contains("粘连线索=probe_B.quirk_tag", StringComparison.Ordinal) &&
               glued.Contains("后续字段值可能混入 .types", StringComparison.Ordinal) &&
               glued.Contains("未解析字符串=[probe_B.quirk_tag, watched, .limit, 1]", StringComparison.Ordinal) &&
               glued.Contains("编辑器不自动拆分", StringComparison.Ordinal) &&
               glued.Contains("来源=", StringComparison.Ordinal) &&
               glued.Contains($"地区={snapshot.DungeonId}；难度={snapshot.Difficulty}；位置=房间 room", StringComparison.Ordinal),
            "Field-glue diagnostics must give traceable source/line/context and must not describe field values as real missing monsters or silently repair them.");
        Assert(details.Single(issue => issue.Contains($"{mashPath}:7；", StringComparison.Ordinal))
                   .StartsWith("遭遇排除明细（全局 Bridge）：未找到活动怪物定义", StringComparison.Ordinal) &&
               details.Single(issue => issue.Contains($"{mashPath}:8；", StringComparison.Ordinal))
                   .StartsWith("遭遇排除明细（全局 Bridge）：未找到活动怪物定义", StringComparison.Ordinal) &&
               details.All(issue => !issue.Contains("literal_B.limit", StringComparison.Ordinal)),
            "An arbitrary dot, an absent field-prefix ID, and an existing complete dotted ID must not be treated as proven field glue.");
        Assert(catalog.Encounters.Count == 11 && catalog.DirectEncounters.Count == 2 && catalog.BridgeEncounters.Count == 2 &&
               catalog.Encounters.Count(row => !row.CanPlaceDirectly) == 9 &&
               catalog.DirectEncounters.Single(row => row.SourceLine == 9).MashIndex == 5 &&
               catalog.Encounters.Single(row => row.SourceLine == 10).MashIndex == 6 &&
               catalog.Encounters.Single(row => row.SourceLine == 10).MonsterIds.SequenceEqual(["probe_B", ".limit", "1"]) &&
               catalog.Encounters.Single(row => row.SourceLine == 3).MonsterIds.SequenceEqual(["probe_B.limit", "1"]) &&
               catalog.Issues.Any(issue => issue.StartsWith("当前副本遭遇不可直写（索引 2 保留，不重排后续索引）：疑似字段粘连", StringComparison.Ordinal)) &&
               mashHash == ComputeSha256(mashPath) && gameHash == ComputeSha256(gamePath),
            "Diagnostics must preserve parsing, eligibility, direct indexes, Mod definitions, and source saves.");

        File.WriteAllText(mashPath,
            "hall: .chance 1 .limit 1 .types probe_B\n" +
            "room: .chance 1 .quirk_tag watched .limit 1 .types probe_B\n" +
            "boss: .chance 1 .types literal_B.limit\n", new UTF8Encoding(false));
        var validCatalog = BattleEncounterCatalog.Load(isolated, snapshot);
        Assert(validCatalog.DirectEncounters.Count == 3 && validCatalog.BridgeEncounters.Count == 3 &&
               validCatalog.Issues.All(issue => !issue.StartsWith("遭遇排除", StringComparison.Ordinal) &&
                   !issue.StartsWith("当前副本遭遇不可直写", StringComparison.Ordinal)),
            "Valid fields and complete dotted IDs must not produce spurious exclusion logs.");

        File.WriteAllText(mashPath,
            "hall: .chance 1\n" +
            "room: .chance 1 .types\n" +
            "boss: .chance 1 .types literal_B.limit\n", new UTF8Encoding(false));
        var emptyMashHash = ComputeSha256(mashPath);
        var emptyCatalog = BattleEncounterCatalog.Load(isolated, snapshot);
        var emptyLogs = CatalogLogDiagnostics.Summarize([("战斗遭遇", emptyCatalog.Issues)]);
        Assert(emptyLogs.Count == 2 &&
               emptyLogs.All(entry => entry.Level == DiagnosticLogLevel.Warning) &&
               new[] { 1, 2 }.All(line => emptyLogs.Any(entry => entry.Message ==
                   $"目录警告：空遭遇保留编号，不影响后续索引，不能放置：{mashPath}:{line}；报告模块=战斗遭遇")),
            "Missing and valueless lists must report retained empty slots with source path, line, module and severity.");
        Assert(emptyCatalog.Encounters.Count == 3 && emptyCatalog.DirectEncounters.Count == 1 &&
               emptyCatalog.Encounters.All(row => row.MashIndex == 0) &&
               emptyCatalog.BridgeEncounters.Count == 1 &&
               emptyCatalog.DirectEncounters.Single().SourceLine == 3 &&
               emptyMashHash == ComputeSha256(mashPath) && gameHash == ComputeSha256(gamePath),
            "Each independent type retains its own zero slot; empty rows stay unavailable without changing source files or saves.");
    }
}
