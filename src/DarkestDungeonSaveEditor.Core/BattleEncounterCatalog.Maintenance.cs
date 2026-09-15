using System.Security.Cryptography;
using System.Text;

namespace DarkestDungeonSaveEditor.Core;

public static partial class BattleEncounterCatalog
{
    // Includes file bytes, inventories and mount order. Save hashes alone do not
    // change when Steam updates an already enabled Mod in place.
    public static string CaptureContentFingerprint(IReadOnlyList<ActiveContentSource> sources)
    {
        var issues = new List<string>();
        var prefixes = ContentFileOverlay.GetEnabledDlcPrefixes(sources);
        var parts = new List<string>();
        foreach (var source in sources)
        {
            parts.Add($"{source.Id}|{source.Kind}|{source.LoadOrder}|{Path.GetFullPath(source.Directory)}|{source.VirtualPathPrefix}");
            var paths = EnumerateMashFiles(source, prefixes, issues)
                .Concat(EnumerateMonsterInfoFiles(source, prefixes, issues))
                .Concat(NativeContentFileResolver.EnumerateActorOpenFiles(source, prefixes, "monsters", issues))
                .Concat(new[] { "modfiles.txt", "project.xml", ManagedBattleEncounterBridgeService.ManifestFileName }
                    .Select(name => Path.Combine(source.Directory, name)).Where(File.Exists));
            foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
                parts.Add($"{Path.GetFullPath(path)}|{ComputeSha256(path)}");
        }
        parts.AddRange(issues.Order(StringComparer.Ordinal)); // Missing listed files are observable changes.
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', parts))));
    }

    internal static IReadOnlyDictionary<string, int?> ReadMaintenanceMonsterSizes(
        IReadOnlyList<ActiveContentSource> sources, bool usableOnly = false)
    {
        var issues = new List<string>();
        var monsters = ResolveAvailableMonsterDefinitions(sources, issues);
        RequireCompleteMaintenanceScan(issues);
        return usableOnly ? monsters.Sizes.Where(pair => monsters.Ids.Contains(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal) : monsters.Sizes;
    }

    internal static IReadOnlyList<BattleEncounterDefinition> ReadMaintenanceTable(
        ActiveContentSnapshot content, string dungeonId, int difficulty, int mashType, bool authoredOnly = false)
    {
        var issues = new List<string>();
        var files = ResolveEffectiveMashFiles(content.Sources, dungeonId, difficulty, issues);
        if (issues.Count > 0)
            throw new InvalidOperationException($"战斗自动清理暂缓，无法确认 {dungeonId}/{difficulty}/{mashType} 的文件顺序：{issues[0]}");
        var fingerprints = files.Select(file => new BattleEncounterFileFingerprint(file.Source.Id,
            Path.GetFullPath(file.Path), file.RelativePath, ComputeSha256(file.Path))).ToArray();
        var guard = new BattleEncounterTableGuard(dungeonId, difficulty,
            Path.Combine(content.Profile.ProfileDirectory, "persist.game.json"), content.SourceGameSha256,
            content.Sources, fingerprints, ComputeTableFingerprint(fingerprints)) { Resolution = content.Resolution };
        var unparsed = new HashSet<int>();
        var rows = files.SelectMany(file => ParseFile(file, content.Sources, guard, issues, unparsed))
            .Where(row => row.SourceKind == BattleEncounterSourceKind.Standard && row.MashType == mashType).ToArray();
        if (authoredOnly)
        {
            if (unparsed.Contains(mashType)) throw new InvalidDataException("专用遭遇文件含无法解析的行，暂缓自动清理。");
            return rows;
        }
        var monsterIssues = new List<string>();
        var monsters = ResolveAvailableMonsterDefinitions(content.Sources, monsterIssues);
        RequireCompleteMaintenanceScan(monsterIssues);
        var runtimeRows = RuntimeRows(rows, monsters);
        if (unparsed.Contains(mashType) || !HasProvenFileOrder(runtimeRows, out _) ||
            !HasProvenRowCounts(runtimeRows, monsters, out _))
            throw new InvalidOperationException($"战斗自动清理暂缓，{dungeonId}/{difficulty}/{mashType} 的运行时编号尚不能确认。");
        return runtimeRows.Select((row, index) => row with
        {
            MashIndex = index,
            CanPlaceDirectly = row.MonsterIds.Count > 0 && row.MonsterIds.All(monsters.Ids.Contains),
            UnavailableReason = row.MonsterIds.Count == 0 ? "空遭遇占用运行时编号，但不能放置" : row.UnavailableReason
        }).ToArray();
    }

    private static void RequireCompleteMaintenanceScan(IEnumerable<string> issues)
    {
        // A missing file is evidence of removal. Access failures or invalid paths
        // are not evidence that every monster in that source was removed.
        var failure = issues.FirstOrDefault(issue => !issue.Contains(" is missing:", StringComparison.Ordinal));
        if (failure is not null)
            throw new IOException($"战斗自动清理暂缓，内容扫描未完成：{failure}");
    }
}
