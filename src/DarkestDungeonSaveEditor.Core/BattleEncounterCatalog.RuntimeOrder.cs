namespace DarkestDungeonSaveEditor.Core;

public static partial class BattleEncounterCatalog
{
    // Windows build 27890: StorageManager::IO_FindFiles replaces an overridden
    // path in place, and appends new paths in each mount's depth/strcmp order.
    // Sorting only the final winners loses the slot established by a base file
    // or a lower-priority Mod. See docs/encounter-runtime-order.md.
    private static IReadOnlyList<EffectiveContentFile> ResolveRuntimeFileOrder(
        IReadOnlyList<ContentFileCandidate> candidates,
        IReadOnlyList<ActiveContentSource> activeSources,
        List<string> issues)
    {
        var sources = candidates.Select(candidate => candidate.Source)
            .DistinctBy(source => source.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(source => source.Id, StringComparer.OrdinalIgnoreCase);
        var files = ContentFileOverlay.Resolve(candidates, "Encounter mash", issues);
        var dlcPrefixes = ContentFileOverlay.GetEnabledDlcPrefixes(activeSources);
        string? MountPrefix(string path) => dlcPrefixes.OrderByDescending(prefix => prefix.Length)
            .FirstOrDefault(prefix => path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase));
        string ProviderPath(ContentFileProvider provider) => ContentFileOverlay.NormalizeRelativePath(
            sources[provider.SourceId], provider.Path)!;
        var indexedFiles = files.Where(file => ClassifyFile(file.Path) == BattleEncounterSourceKind.Standard &&
            HasIndexedTableDeclarations(file.Path)).ToArray();
        foreach (var file in indexedFiles)
        {
            if (file.Providers.Select(ProviderPath).Distinct(StringComparer.Ordinal).Skip(1).Any())
                issues.Add($"标准遭遇覆盖路径的大小写不一致，尚未验证原生匹配规则：{file.RelativePath}");
            if (MountPrefix(file.RelativePath) is not null &&
                file.Providers.All(provider => sources[provider.SourceId].Kind is "local" or "workshop"))
                issues.Add($"仅 Mod 提供的 DLC 子目录新增遭遇文件尚未验证原生发现规则：{file.RelativePath}");
        }
        foreach (var group in indexedFiles
                     .GroupBy(file =>
                     {
                         TryDescribeMashFile(file.RelativePath, out var dungeon, out var difficulty);
                         return (dungeon, difficulty);
                     }))
        {
            if (group.Select(file => MountPrefix(file.RelativePath)).Where(prefix => prefix is not null)
                .Distinct(StringComparer.OrdinalIgnoreCase).Skip(1).Any())
                issues.Add($"同一地区/难度跨越多个 DLC 挂载目录，尚未验证这些挂载之间的顺序：{group.Key.dungeon}/{group.Key.difficulty}");
        }
        return NativeContentFileResolver.Resolve(candidates, activeSources, "Encounter mash", issues);
    }

    private static bool HasIndexedTableDeclarations(string path) => File.ReadLines(path).Any(raw =>
    {
        var line = StripComment(raw).Trim();
        var separator = line.IndexOf(':');
        return separator > 0 && line[..separator].Trim().ToLowerInvariant() is "hall" or "room" or "boss";
    });

    private static bool HasProvenFileOrder(
        IReadOnlyList<BattleEncounterDefinition> rows,
        out string reason)
    {
        reason = string.Empty;
        if (rows.Select(row => row.SourcePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() <= 1)
            return true;

        var prefixes = ContentFileOverlay.GetEnabledDlcPrefixes(rows[0].TableGuard.ActiveSources);
        if (rows.Any(row => !row.SourceRelativePath.StartsWith($"dungeons/{row.OriginDungeonId}/", StringComparison.Ordinal) &&
                !prefixes.Any(prefix => row.SourceRelativePath.StartsWith(
                    $"{prefix}/dungeons/{row.OriginDungeonId}/", StringComparison.Ordinal))))
        {
            reason = "该类型由多个文件提供且跨越 DLC 或非标准挂载目录，尚未验证其运行时索引顺序";
            return false;
        }
        return true;
    }

    private static bool HasProvenRowCounts(
        IEnumerable<BattleEncounterDefinition> rows,
        AvailableMonsterDefinitions monsters,
        out string reason)
    {
        foreach (var row in rows)
        {
            // AddMashEntry sums the four resolved monster sizes; unresolved
            // IDs contribute zero but retain the native row. A sum > 4 is
            // dropped by the game. Do not silently assign indexes past it.
            if (row.MonsterIds.Count > 4 ||
                row.MonsterIds.Any(id => monsters.Sizes.TryGetValue(id, out var size) && size is null) ||
                row.MonsterIds.Sum(id => monsters.Sizes.GetValueOrDefault(id) ?? 0) > 4)
            {
                reason = $"遭遇组合的怪物体型无法确认或超过四格，不能证明该类型的运行时行数：{row.SourcePath}:{row.SourceLine}";
                return false;
            }
        }
        reason = string.Empty;
        return true;
    }

    private static bool IsNativeSkippedRow(BattleEncounterDefinition row, AvailableMonsterDefinitions monsters) =>
        !row.MonsterIds.Any(id => monsters.Sizes.TryGetValue(id, out var size) && size is null) &&
        row.MonsterIds.Sum(id => monsters.Sizes.GetValueOrDefault(id) ?? 0) > 4;

    private static BattleEncounterDefinition[] RuntimeRows(
        IEnumerable<BattleEncounterDefinition> rows, AvailableMonsterDefinitions monsters) =>
        rows.Where(row => !IsNativeSkippedRow(row, monsters)).ToArray();

    internal static string DedicatedMashPath(string dungeonId, int difficulty)
    {
        if (string.IsNullOrWhiteSpace(dungeonId) || difficulty < 0 ||
            dungeonId.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '_' and not '-'))
            throw new InvalidOperationException("地区或难度不能用于生成专用遭遇文件。");
        return $"dungeons/{dungeonId}/ddse_managed.{dungeonId}.{difficulty.ToString(System.Globalization.CultureInfo.InvariantCulture)}.mash.darkest";
    }

    internal static BattleEncounterAppendTarget ResolveAppendTarget(
        BattleEncounterCatalogResult catalog, int mashType, string? managedPackageDirectory = null)
    {
        ValidateGuard(catalog.TableGuard);
        if (mashType is < 0 or > 2)
            throw new InvalidOperationException("专用遭遇文件仅支持走廊、房间和首领战斗。");
        // Also covers a type whose only authored rows could not be parsed.
        // An empty parsed list alone does not prove that native index 0 is free.
        var authoredRows = ParseGuardedFiles(catalog.TableGuard, BattleEncounterSourceKind.Standard, mashType);
        var monsters = ResolveAvailableMonsterDefinitions(catalog.TableGuard.ActiveSources, []);
        var rows = RuntimeRows(authoredRows, monsters);
        if (!HasProvenFileOrder(rows, out _) || !HasProvenRowCounts(rows, monsters, out _))
            throw new InvalidOperationException("当前类型没有可证明的标准遭遇索引，不能更新 Bridge。");
        var indexed = catalog.Encounters.Where(row => row.SourceKind == BattleEncounterSourceKind.Standard &&
            row.MashType == mashType && row.MashIndex is not null).OrderBy(row => row.MashIndex).ToArray();
        if (indexed.Length != rows.Length || !indexed.Select((row, index) =>
                row.MashIndex == index && SameEncounterIdentity(row, rows[index])).All(matches => matches))
            throw new InvalidOperationException("当前类型没有连续且可证明的标准遭遇索引，不能更新 Bridge。");

        var relativePath = DedicatedMashPath(catalog.DungeonId, catalog.Difficulty);
        var expectedPath = managedPackageDirectory is null ? null :
            Path.GetFullPath(Path.Combine(managedPackageDirectory, relativePath));
        var aliases = ResolveEffectiveMashFiles(catalog.TableGuard.ActiveSources, catalog.DungeonId,
            catalog.Difficulty, []).Where(file =>
            file.RelativePath.Equals(relativePath, StringComparison.OrdinalIgnoreCase) ||
            file.RelativePath.EndsWith("/" + relativePath, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (aliases.Any(file => expectedPath is null ||
                !Path.GetFullPath(file.Path).Equals(expectedPath, StringComparison.OrdinalIgnoreCase) ||
                file.ProviderPaths.Any(path => !Path.GetFullPath(path).Equals(expectedPath, StringComparison.OrdinalIgnoreCase))))
            throw new InvalidOperationException("专用 Bridge 文件路径已被其他来源使用，不能覆盖或改变其加载位置。");

        // An existing dedicated file must remain the tail for each type. New files
        // are unique root paths appended by the highest-priority managed mount;
        // the staged catalog below the caller verifies every previous binding.
        var firstDedicated = Array.FindIndex(authoredRows.ToArray(), row =>
            expectedPath is not null && row.SourcePath.Equals(expectedPath, StringComparison.OrdinalIgnoreCase));
        if (firstDedicated >= 0 && authoredRows.Skip(firstDedicated).Any(row =>
                !row.SourcePath.Equals(expectedPath, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("专用 Bridge 已不在当前类型的末尾，请恢复其最高优先级后重新加载。");
        }
        return new BattleEncounterAppendTarget(relativePath, rows.Length);
    }

    internal static void ValidateExistingIndexes(
        BattleEncounterCatalogResult original,
        BattleEncounterCatalogResult updated)
    {
        foreach (var row in original.Encounters.Where(row => row.MashIndex is not null))
        {
            var after = updated.Encounters.SingleOrDefault(candidate =>
                candidate.SourceKind == BattleEncounterSourceKind.Standard &&
                candidate.MashType == row.MashType && candidate.MashIndex == row.MashIndex);
            if (after is null ||
                !after.MonsterIds.SequenceEqual(row.MonsterIds, StringComparer.Ordinal) ||
                !after.SourceRelativePath.Equals(row.SourceRelativePath, StringComparison.Ordinal) ||
                after.SourceLine != row.SourceLine)
                throw new InvalidOperationException("Bridge 会改变已有战斗的索引，本次不会写入。");
        }
    }
}

internal sealed record BattleEncounterAppendTarget(
    string RelativeMashPath,
    int NextMashIndex);
