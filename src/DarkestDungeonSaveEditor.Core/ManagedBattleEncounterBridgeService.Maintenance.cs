using System.Text.Json;
using System.Text.Json.Nodes;

namespace DarkestDungeonSaveEditor.Core;

public sealed record EditorBattleMaintenanceResult(bool Changed, bool Deferred, int RemovedCombinations,
    int ReindexedCombinations, int ClearedBattles, string? BackupDirectory, IReadOnlyList<string> Reasons)
{
    public static EditorBattleMaintenanceResult Unchanged { get; } = new(false, false, 0, 0, 0, null, []);
    public string? DeferredReason { get; init; }
    public bool RetryWhenGameExits { get; init; }
    public bool RequiresWrite { get; init; }
    public bool RecoveryPerformed { get; init; }
    public string Message => RecoveryPerformed && !Deferred && RemovedCombinations == 0 && ReindexedCombinations == 0 && ClearedBattles == 0
        ? "已恢复中断的战斗维护，正在重新同步档案。"
        : Deferred
        ? DeferredReason ?? "检测到编辑器战斗记录失效，已暂缓自动清理；退出游戏后将自动重试。"
        : $"战斗记录自动维护：删除失效 Bridge 组合 {RemovedCombinations} 条，更新编号 {ReindexedCombinations} 条，" +
          $"清空编辑器放置的战斗 {ClearedBattles} 场；备份={BackupDirectory}；原因={string.Join("；", Reasons)}";
}

public sealed partial class ManagedBattleEncounterBridgeService
{
    private EditorBattleHistory? _battleHistory;
    private readonly SemaphoreSlim _maintenanceGate = new(1, 1);
    private string? _maintenanceCheckedKey;
    private string? _maintenanceScratch;
    internal Action<string>? BeforeMaintenanceReplace { get; set; }
    internal Action<string>? AfterMaintenanceReplace { get; set; }

    public bool CanRetryDeferredMaintenance => !_gameRunningProbe();

    /// <summary>Explicit offline maintenance. Catalog readers themselves remain read-only.</summary>
    public async Task<EditorBattleMaintenanceResult> ReconcileAsync(ActiveContentSnapshot content,
        string gameDirectory, string? localModDirectory, CancellationToken cancellationToken = default,
        bool inspectOnly = false)
    {
        await _maintenanceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ActiveContentResolver.ValidateSourceBindings(content.Resolution, content.Sources, cancellationToken);
            var recovery = await RecoverInterruptedMaintenanceAsync(content.Profile, gameDirectory,
                localModDirectory, cancellationToken, inspectOnly).ConfigureAwait(false);
            if (inspectOnly && recovery) return EditorBattleMaintenanceResult.Unchanged with { RequiresWrite = true };
            var result = await ReconcileCoreAsync(content, gameDirectory, localModDirectory, cancellationToken, inspectOnly).ConfigureAwait(false);
            return recovery ? result with { Changed = true, RecoveryPerformed = true } : result;
        }
        finally
        {
            // Staging is disposable; committed backups contain the original save,
            // original package and replacement plan. Polling must not grow archives.
            var scratch = _maintenanceScratch;
            _maintenanceScratch = null;
            if (scratch is not null && IsWithinDirectory(scratch, Path.Combine(_locations.WorkspaceDirectory, "encounter_maintenance")))
            {
                try { if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            _maintenanceGate.Release();
        }
    }

    private async Task<EditorBattleMaintenanceResult> ReconcileCoreAsync(ActiveContentSnapshot content,
        string gameDirectory, string? localModDirectory, CancellationToken token, bool inspectOnly)
    {
        var profile = content.Profile;
        var unresolved = content.Issues.FirstOrDefault(issue =>
            issue.Contains("ambiguous", StringComparison.OrdinalIgnoreCase) ||
            issue.Contains("unavailable", StringComparison.OrdinalIgnoreCase) ||
            issue.Contains("Failed to read", StringComparison.OrdinalIgnoreCase) ||
            issue.Contains("could not be mapped", StringComparison.OrdinalIgnoreCase) ||
            issue.Contains("Unsupported", StringComparison.OrdinalIgnoreCase) ||
            issue.Contains("Ignored", StringComparison.OrdinalIgnoreCase) ||
            issue.Contains("invalid", StringComparison.OrdinalIgnoreCase));
        if (unresolved is not null)
            throw new InvalidOperationException("战斗自动清理暂缓，活动来源尚未完整识别：" + unresolved);
        var title = GetProjectTitle(profile);
        var installRoot = Path.GetFullPath(string.IsNullOrWhiteSpace(localModDirectory) ||
            File.Exists(Path.Combine(localModDirectory, "project.xml")) ? Path.Combine(gameDirectory, "mods") : localModDirectory);
        var package = Path.Combine(installRoot, GetPackageDirectoryName(profile));
        ValidateManagedPackagePath(installRoot, package);
        var manifestPath = Path.Combine(package, ManifestFileName);
        var hashes = ProfileCatalogSnapshotReader.CaptureHashes(profile);
        if (hashes["persist.game.json"] != content.SourceGameSha256)
            throw new IOException("活动配置在战斗清理检查前发生变化，请重新同步。");
        var fingerprint = BattleEncounterCatalog.CaptureContentFingerprint(content.Sources);
        var externalSources = content.Sources.Where(source => !Path.GetFullPath(source.Directory)
            .Equals(package, StringComparison.OrdinalIgnoreCase)).ToArray();
        var externalFingerprint = BattleEncounterCatalog.CaptureContentFingerprint(externalSources);
        var journalRoot = Path.Combine(_locations.BackupDirectory, SanitizeSegment(profile.SteamUserId, 48), SanitizeSegment(profile.ProfileId, 48));
        var journalStamp = Directory.Exists(journalRoot) ? string.Join('|', Directory.EnumerateFiles(journalRoot,
            "commit-result.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal).Select(path =>
                path + ":" + File.GetLastWriteTimeUtc(path).Ticks)) : string.Empty;
        var packageHash = File.Exists(manifestPath) ? ComputeSha256(manifestPath) : string.Empty;
        var key = Path.GetFullPath(profile.ProfileDirectory) + fingerprint + packageHash + journalStamp + string.Join('|', hashes.Values);
        if (_maintenanceCheckedKey == key) return EditorBattleMaintenanceResult.Unchanged;

        var inRaid = QuantityItemSaveScene.Read(content).Context == QuantityItemSaveContext.Raid;
        BattleMapSnapshot? snapshot = null;
        IReadOnlyList<EditorBattlePlacement> placements = [];
        if (inRaid && (Directory.Exists(journalRoot) || Directory.Exists(package)))
        {
            snapshot = await new BattleMapSnapshotReader(_codec).LoadAsync(profile.ProfileDirectory, token).ConfigureAwait(false);
            _battleHistory ??= new EditorBattleHistory(_codec, _locations);
            placements = await _battleHistory.ReadAsync(profile, snapshot, token).ConfigureAwait(false);
        }

        var reasons = new List<string>();
        var removed = 0;
        var reindexed = 0;
        var invalidated = false;
        var oldBridgeBindings = new HashSet<(int Type, int Index)>();
        var allBridgeBindings = new Dictionary<(string Dungeon, int Difficulty, int Type), HashSet<int>>();
        var affectedBridgeTables = new HashSet<(string Dungeon, int Difficulty, int Type)>();
        var stagedFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var originalFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? workspace = null;
        string Workspace() => workspace ??= _maintenanceScratch = Path.Combine(_locations.WorkspaceDirectory, "encounter_maintenance",
            $"{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}");
        var tableCache = new Dictionary<int, IReadOnlyList<BattleEncounterDefinition>>();
        foreach (var placement in placements)
        {
            if (!tableCache.TryGetValue(placement.MashType, out var rows))
                tableCache[placement.MashType] = rows = BattleEncounterCatalog.ReadMaintenanceTable(content,
                    snapshot!.DungeonId, snapshot.Difficulty, placement.MashType);
            var row = rows.SingleOrDefault(row => row.MashIndex == placement.MashIndex);
            if (row is null || !row.CanPlaceDirectly || !row.MonsterIds.SequenceEqual(placement.MonsterIds, StringComparer.Ordinal))
            {
                invalidated = true;
                reasons.Add($"{placement.AreaId}.{placement.TileId} 的原编号 {placement.MashType}/{placement.MashIndex} 已变化或组合缺失");
            }
        }

        if (Directory.Exists(package))
        {
            RejectReparsePoint(installRoot, "本地 Mod 根目录");
            RejectReparsePoint(package, "托管 Bridge 目录");
            var manifest = ReadManifest(manifestPath);
            ValidateManifestIdentity(manifest, profile, title);
            foreach (var table in manifest.Tables)
            foreach (var type in table.Entries.GroupBy(entry => entry.MashType))
                allBridgeBindings[(table.DungeonId, table.Difficulty, type.Key)] =
                    type.Select(entry => entry.MashIndex).ToHashSet();
            if (snapshot is not null)
                foreach (var table in manifest.Tables.Where(table => table.DungeonId.Equals(snapshot.DungeonId, StringComparison.Ordinal) &&
                    table.Difficulty == snapshot.Difficulty))
                    foreach (var entry in table.Entries) oldBridgeBindings.Add((entry.MashType, entry.MashIndex));
            var stage = Path.Combine(Workspace(), "package");
            CopyDirectory(package, stage);
            foreach (var path in Directory.EnumerateFiles(package, "*", SearchOption.AllDirectories))
                originalFiles[path] = ComputeSha256(path);
            var monsters = BattleEncounterCatalog.ReadMaintenanceMonsterSizes(content.Sources, usableOnly: true);
            var sourceFingerprint = ComputeContentFingerprint(content.Sources);
            var metadataChanged = false;
            foreach (var table in manifest.Tables)
            {
                var liveMash = Path.GetFullPath(Path.Combine(package, table.RelativeMashPath));
                var stageMash = Path.Combine(stage, table.RelativeMashPath);
                ValidateExistingTable(table, liveMash, sourceFingerprint, requireCurrentContentFingerprint: false);
                // Earlier generators placed options after .types. A short
                // formation therefore contains option tokens in native slots.
                // Retire those records, per the configured cleanup policy; do
                // not teach the general parser to ignore those monster IDs.
                // Ownership requires the entire old generated file, its hash,
                // and every authored ordinal to match the existing manifest.
                var oldGeneratedFile = File.ReadAllText(liveMash) == string.Concat(table.Entries.Select(entry =>
                        $"{(entry.MashType == 0 ? "hall" : entry.MashType == 1 ? "room" : "boss")}: .chance 0 .types {string.Join(' ', entry.MonsterIds)}" +
                        (entry.MashType == 2 ? string.Empty : " .limit 1 .can_be_ambush false") + Environment.NewLine));
                var obsoleteEntries = new HashSet<ManagedBridgeEncounterManifest>();
                // Use the installed carrier's authored ordinals to verify ownership,
                // even when a missing/oversized unit has no usable runtime index.
                var verificationSources = content.Sources.Any(source => Path.GetFullPath(source.Directory)
                    .Equals(package, StringComparison.OrdinalIgnoreCase)) ? content.Sources :
                    content.Sources.Append(new ActiveContentSource($"local:{title}", title, "local", package, -1)).ToArray();
                foreach (var type in table.Entries.Select(entry => entry.MashType).Distinct())
                {
                    var authored = BattleEncounterCatalog.ReadMaintenanceTable(content with { Sources = verificationSources },
                        table.DungeonId, table.Difficulty, type, authoredOnly: true)
                        .Where(row => Path.GetFullPath(row.SourcePath).Equals(liveMash, StringComparison.OrdinalIgnoreCase))
                        .OrderBy(row => row.SourceRecordIndex).ToArray();
                    var entries = table.Entries.Where(entry => entry.MashType == type).OrderBy(entry => entry.FileRowIndex).ToArray();
                    if (authored.Length != entries.Length || entries.Where((entry, index) => entry.FileRowIndex != index).Any())
                        throw new InvalidDataException("Bridge 清单与专用文件的组合记录不一致，暂缓自动清理。");
                    foreach (var (entry, index) in entries.Select((entry, index) => (entry, index)))
                    {
                        if (entry.MonsterIds.SequenceEqual(authored[index].MonsterIds, StringComparer.Ordinal)) continue;
                        if (!oldGeneratedFile)
                            throw new InvalidDataException("Bridge 清单与专用文件的组合记录不一致，暂缓自动清理。");
                        obsoleteEntries.Add(entry);
                    }
                }
                var retained = new List<ManagedBridgeEncounterManifest>();
                foreach (var entry in table.Entries)
                {
                    if (obsoleteEntries.Contains(entry))
                    {
                        affectedBridgeTables.Add((table.DungeonId, table.Difficulty, entry.MashType));
                        removed++;
                        invalidated = true;
                        reasons.Add($"{table.DungeonId}/{table.Difficulty}/{entry.MashType}/{entry.MashIndex}：旧版 Bridge 的实际怪物槽与记录不同，清除旧记录后可重新放置");
                        continue;
                    }
                    var missing = entry.MonsterIds.Where(id => !monsters.ContainsKey(id)).ToArray();
                    if (missing.Length == 0 && entry.MonsterIds.Any(id => monsters[id] is null))
                        throw new InvalidOperationException("战斗自动清理暂缓，怪物体型尚无法确认：" + string.Join(", ", entry.MonsterIds));
                    if (missing.Length > 0 || entry.MonsterIds.Count > 4 || entry.MonsterIds.Sum(id => monsters[id] ?? 0) > 4)
                    {
                        affectedBridgeTables.Add((table.DungeonId, table.Difficulty, entry.MashType));
                        removed++;
                        invalidated = true;
                        reasons.Add($"{table.DungeonId}/{table.Difficulty}/{entry.MashType}/{entry.MashIndex}：" +
                            (missing.Length > 0 ? "缺少 " + string.Join(", ", missing) : "组合超过四格"));
                    }
                    else retained.Add(entry);
                }
                // Rebuild only the editor's dedicated rows. Empty tables remain valid
                // empty files, avoiding an unrelated directory/manifest deletion transaction.
                File.WriteAllText(stageMash, string.Empty, Utf8NoBom);
                var ordinals = new Dictionary<int, int>();
                foreach (var entry in retained)
                {
                    File.AppendAllText(stageMash, EncounterBridgeRow.Format(entry.MashType, entry.MonsterIds), Utf8NoBom);
                    entry.FileRowIndex = ordinals.GetValueOrDefault(entry.MashType);
                    ordinals[entry.MashType] = entry.FileRowIndex.Value + 1;
                }
                table.Entries = retained;
                metadataChanged |= table.ContentFingerprint != sourceFingerprint;
                table.ContentFingerprint = sourceFingerprint;
                table.GeneratedMashSha256 = ComputeSha256(stageMash);
            }
            WriteManagedPackage(stage, package, manifest);
            var installedSource = content.Sources.SingleOrDefault(source =>
                Path.GetFullPath(source.Directory).Equals(package, StringComparison.OrdinalIgnoreCase));
            var sources = content.Sources.Where(source => !Path.GetFullPath(source.Directory)
                .Equals(package, StringComparison.OrdinalIgnoreCase)).ToList();
            sources.Add(installedSource is null
                ? new ActiveContentSource($"local:{title}", title, "local", stage,
                    checked(sources.Where(source => source.Kind is "local" or "workshop").Select(source => source.LoadOrder).DefaultIfEmpty(1000).Min() - 1))
                : installedSource with { Directory = stage });
            var stagedContent = content with { Sources = sources };
            foreach (var table in manifest.Tables)
            foreach (var type in table.Entries.Select(entry => entry.MashType).Distinct())
            {
                var rows = BattleEncounterCatalog.ReadMaintenanceTable(stagedContent, table.DungeonId, table.Difficulty, type)
                    .Where(row => Path.GetFullPath(row.SourcePath).Equals(Path.GetFullPath(Path.Combine(stage, table.RelativeMashPath)), StringComparison.OrdinalIgnoreCase))
                    .OrderBy(row => row.SourceRecordIndex).ToArray();
                foreach (var entry in table.Entries.Where(entry => entry.MashType == type))
                {
                    var row = rows.ElementAtOrDefault(entry.FileRowIndex!.Value);
                    if (row is null || !row.CanPlaceDirectly || !row.MonsterIds.SequenceEqual(entry.MonsterIds, StringComparer.Ordinal))
                        throw new InvalidOperationException("战斗自动清理暂缓，重建的专用文件未得到可验证的运行时编号。");
                    if (entry.MashIndex != row.MashIndex)
                    {
                        affectedBridgeTables.Add((table.DungeonId, table.Difficulty, type));
                        reasons.Add($"{table.DungeonId}/{table.Difficulty}/{type}：{entry.MashIndex} → {row.MashIndex}");
                        entry.MashIndex = row.MashIndex!.Value;
                        reindexed++;
                        invalidated = true;
                    }
                }
            }
            if (invalidated || metadataChanged)
            {
                manifest.UpdatedAtUtc = DateTime.UtcNow;
                WriteManagedPackage(stage, package, manifest);
                foreach (var path in originalFiles.Keys)
                {
                    var candidate = Path.Combine(stage, Path.GetRelativePath(package, path));
                    if (ComputeSha256(candidate) != originalFiles[path]) stagedFiles[path] = candidate;
                }
            }
        }

        if (invalidated && snapshot is not null)
        {
            var ownedCells = placements.Select(placement => (placement.AreaId, placement.TileId)).ToHashSet();
            var unproven = snapshot.Areas.SelectMany(area => area.Tiles.Select(tile => (Area: area, Tile: tile)))
                .FirstOrDefault(cell => EditorBattleHistory.IsBattle(cell.Tile.Content) &&
                    oldBridgeBindings.Contains((cell.Tile.MashType, cell.Tile.MashIndex)) &&
                    !ownedCells.Contains((cell.Area.AreaId, cell.Tile.TileId)));
            if (unproven.Tile is not null)
                throw new InvalidOperationException($"战斗自动清理暂缓：{unproven.Area.AreaId}.{unproven.Tile.TileId} 可能引用旧 Bridge，" +
                    "但缺少当前副本的成功放置记录，无法确认归属。请恢复对应记录或离开该副本后重试；不会按编号猜测删除，也不会压缩 Bridge。");
        }
        if (!invalidated && stagedFiles.Count == 0)
        {
            _maintenanceCheckedKey = key;
            return EditorBattleMaintenanceResult.Unchanged;
        }
        var retainedGuards = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var pendingRaids = new List<string>();
        if (affectedBridgeTables.Count > 0)
        {
            var retainedPaths = ProfileSaveFiles.Enumerate(profile.ProfileDirectory).Where(path =>
                Path.GetFileName(path).Equals("persist.map.json", StringComparison.OrdinalIgnoreCase) &&
                !Path.GetDirectoryName(path)!.Equals(Path.GetFullPath(profile.ProfileDirectory), StringComparison.OrdinalIgnoreCase) &&
                !path.Equals(snapshot?.MapSavePath, StringComparison.OrdinalIgnoreCase)).ToArray();
            foreach (var path in retainedPaths)
            {
                var relative = Path.GetRelativePath(profile.ProfileDirectory, Path.GetDirectoryName(path)!);
                var retained = await new BattleMapSnapshotReader(_codec).LoadPersistentAsync(profile.ProfileDirectory, relative, token)
                    .ConfigureAwait(false);
                retainedGuards[retained.MapSavePath] = retained.MapSha256;
                retainedGuards[retained.RaidSavePath] = retained.RaidSha256;
                if (retained.Areas.SelectMany(area => area.Tiles).Any(tile =>
                    EditorBattleHistory.IsBattle(tile.Content) &&
                    affectedBridgeTables.Contains((retained.DungeonId, retained.Difficulty, tile.MashType)) &&
                    allBridgeBindings[(retained.DungeonId, retained.Difficulty, tile.MashType)].Contains(tile.MashIndex)))
                    pendingRaids.Add(relative);
            }
        }
        string PendingMessage(int count) => $"已清理当前副本的编辑器战斗 {count} 场；持久副本 {string.Join("、", pendingRaids)} " +
            "仍引用待维护的 Bridge 编号，暂缓删除和重排这些条目；在对应副本状态下关闭游戏后继续清理。";
        if (pendingRaids.Count > 0)
        {
            // Clear the active map first without compacting the shared package. Visiting
            // each retained raid can then make progress, even when two raids share a table.
            if (snapshot is null || placements.Count == 0)
                return new(false, true, 0, 0, 0, null, reasons) { DeferredReason = PendingMessage(0) };
            foreach (var path in stagedFiles.Keys.Where(path => IsWithinDirectory(path, package)).ToArray())
                stagedFiles.Remove(path);
            removed = reindexed = 0;
        }
        if (_gameRunningProbe()) return new(false, true, removed, reindexed, 0, null, reasons) { RetryWhenGameExits = true };
        if (inspectOnly)
        {
            foreach (var target in stagedFiles.Keys) GuardedSaveReplacement.ValidateReplaceAccess(target);
            if (invalidated && snapshot is not null && placements.Count > 0)
                GuardedSaveReplacement.ValidateReplaceAccess(snapshot.MapSavePath);
            return new(false, false, removed, reindexed, 0, null, reasons) { RequiresWrite = true };
        }
        token.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Workspace());
        using var gameLock = new FileStream(Path.Combine(profile.ProfileDirectory, "persist.game.json"),
            FileMode.Open, FileAccess.Read, FileShare.Read);
        if (ComputeSha256(Path.Combine(profile.ProfileDirectory, "persist.game.json")) != content.SourceGameSha256)
            throw new IOException("游戏状态在战斗清理前发生变化。");
        BattleMapWriteGuard? mapGuard = null;
        var replacements = new List<GuardedSaveReplacement>();
        var unchangedPackageLocks = new List<FileStream>();
        string? backup = null;
        var cleared = 0;
        try
        {
            if (invalidated && placements.Count > 0)
            {
                mapGuard = await BattleMapWriteGuard.LoadAsync(profile, snapshot!, _codec, Path.Combine(Workspace(), "map-input"), token).ConfigureAwait(false);
                foreach (var placement in placements)
                    if (BattleMapSaveEditor.ClearRecordedBattle(mapGuard.MapDocument, placement)) cleared++;
                var proposed = Path.Combine(Workspace(), "map.proposed.json");
                var encoded = Path.Combine(Workspace(), "map.encoded.json");
                var roundTrip = Path.Combine(Workspace(), "map.roundtrip.json");
                JsonSupport.WriteObject(proposed, mapGuard.MapDocument);
                await _codec.EncodeAsync(proposed, encoded, snapshot!.MapSavePath, token).ConfigureAwait(false);
                await _codec.DecodeAsync(encoded, roundTrip, token).ConfigureAwait(false);
                if (!JsonNode.DeepEquals(mapGuard.MapDocument, JsonSupport.ReadObject(roundTrip)) ||
                    (DsonSaveCodec.IsDson(snapshot.MapSavePath) && !RevisionMatches(snapshot.MapSavePath, encoded)))
                    throw new InvalidDataException("自动清理地图未通过 DSON 编码回环验证。");
                originalFiles[snapshot.MapSavePath] = snapshot.MapSha256;
                stagedFiles[snapshot.MapSavePath] = encoded;
            }
            if (!ProfileCatalogSnapshotReader.HashesEqual(hashes, ProfileCatalogSnapshotReader.CaptureHashes(profile)) ||
                BattleEncounterCatalog.CaptureContentFingerprint(content.Sources) != fingerprint)
                throw new IOException("存档或 Mod 文件在自动清理准备期间发生变化，等待稳定后重试。");
            token.ThrowIfCancellationRequested();
            EnsureGameIsNotRunning();
            ActiveContentResolver.ValidateSourceBindings(content.Resolution, content.Sources, token);
            var readOnlyFiles = originalFiles.Where(pair => !stagedFiles.ContainsKey(pair.Key)).Concat(retainedGuards).ToArray();
            backup = CreateProfileBackup(profile, Path.GetFileName(Workspace()), content.SourceGameSha256);
            if (Directory.Exists(package)) CopyDirectory(package, Path.Combine(backup, "bridge-package"));
            File.WriteAllText(Path.Combine(backup, "backup-manifest.json"), JsonSerializer.Serialize(new
            {
                version = 2, operation = EditorBattleHistory.CleanupOperation,
                profile.ProfileId, profile.SteamUserId, profile.ProfileDirectory, profile.RaidSaveRelativeDirectory,
                Invalidated = invalidated && snapshot is not null, RaidIdentity = snapshot?.RaidIdentity,
                GameSha256 = hashes["persist.game.json"], RaidSha256 = hashes["persist.raid.json"],
                RemovedCombinations = removed, ReindexedCombinations = reindexed, ClearedBattles = cleared,
                Reasons = reasons, CreatedAtUtc = DateTime.UtcNow,
                ReadOnlyFiles = readOnlyFiles.Select(pair => new { TargetPath = pair.Key, Sha256 = pair.Value }).ToArray(),
                Files = stagedFiles.Select(pair => new { TargetPath = pair.Key, OriginalSha256 = originalFiles[pair.Key],
                    FinalSha256 = ComputeSha256(pair.Value) }).ToArray()
            }, JsonSupport.SerializerOptions), Utf8NoBom);
            // A pending marker is written only after the backup and plan are complete.
            File.WriteAllText(Path.Combine(backup, "maintenance-pending.json"), "{}", Utf8NoBom);
            foreach (var pair in readOnlyFiles)
            {
                var stream = new FileStream(pair.Key, FileMode.Open, FileAccess.Read, FileShare.Read);
                unchangedPackageLocks.Add(stream);
                if (ComputeSha256(pair.Key) != pair.Value) throw new IOException("Bridge 或保留副本在清理准备期间被外部修改。");
            }
            mapGuard?.ReleaseMapLock();
            // The map is cleared before indexes are compacted. All replacements
            // remain locked until the commit marker has been durably written.
            foreach (var pair in stagedFiles.OrderBy(pair => pair.Key == snapshot?.MapSavePath ? 0 : 1))
            {
                var replacement = new GuardedSaveReplacement(pair.Key, pair.Value, originalFiles[pair.Key], ComputeSha256(pair.Value),
                    BeforeMaintenanceReplace, AfterMaintenanceReplace);
                replacements.Add(replacement);
                replacement.Replace();
            }
            if (BattleEncounterCatalog.CaptureContentFingerprint(externalSources) != externalFingerprint)
                throw new IOException("来源 Mod 在自动清理写入期间发生变化。");
            foreach (var replacement in replacements) replacement.Verify();
            EnsureGameIsNotRunning();
            var target = snapshot?.MapSavePath ?? Path.Combine(profile.ProfileDirectory, "persist.game.json");
            var result = new SaveCommitResult(profile.ProfileDirectory, target, backup,
                hashes[Path.GetFileName(target)]!, ComputeSha256(target), DateTime.UtcNow);
            SaveCommitMarker.Publish(Path.Combine(backup, "commit-result.json"), result);
            foreach (var replacement in replacements) replacement.Complete();
            _maintenanceCheckedKey = null;
            return new(true, pendingRaids.Count > 0, removed, reindexed, cleared, backup, reasons)
            {
                DeferredReason = pendingRaids.Count > 0 ? PendingMessage(cleared) : null
            };
        }
        catch (Exception error)
        {
            var failures = new List<Exception>();
            foreach (var replacement in replacements.AsEnumerable().Reverse().Where(replacement => replacement.HasReplaced))
            {
                try
                {
                    var recovery = replacement.Recover();
                    if (recovery != SaveFileRecovery.RestoredOriginal)
                        failures.Add(new IOException($"{replacement.TargetPath}：{GuardedSaveReplacement.DescribeRecovery(recovery)}"));
                }
                catch (Exception recoveryError) { failures.Add(recoveryError); }
            }
            if (failures.Count > 0)
                throw new AggregateException($"战斗清理未能完整恢复，已保留外部版本及备份：{backup}", new[] { error }.Concat(failures));
            if (backup is not null) File.WriteAllText(Path.Combine(backup, "maintenance-recovered.json"), "{}", Utf8NoBom);
            throw new InvalidOperationException($"战斗自动清理失败，本次已写入文件已恢复；备份：{backup ?? "尚未写入"}。{error.Message}", error);
        }
        finally
        {
            foreach (var replacement in replacements) replacement.Dispose();
            foreach (var stream in unchangedPackageLocks) stream.Dispose();
            mapGuard?.Dispose();
        }
    }
}
