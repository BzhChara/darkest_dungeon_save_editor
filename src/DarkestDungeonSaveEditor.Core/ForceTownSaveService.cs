using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DarkestDungeonSaveEditor.Core;

public sealed record ForceTownEditPreview(
    bool PreviousInRaid,
    string PreviousRaidDungeon,
    bool ResultInRaid,
    string ResultRaidDungeon);

public sealed record PreparedForceTownEdit(
    string SessionId,
    SaveProfile Profile,
    ForceTownEditPreview Preview,
    string WorkspaceDirectory,
    PreparedSaveFile GameFile,
    string MapOriginalSha256,
    string RaidOriginalSha256,
    DateTime PreparedAtUtc);

public sealed class ForceTownSaveService
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private readonly DsonSaveCodec _codec;
    private readonly SaveEditorLocations _locations;
    private readonly Func<bool> _gameRunningProbe;
    private readonly Action<string>? _beforeTargetReplace;

    public ForceTownSaveService(
        DsonSaveCodec codec,
        SaveEditorLocations? locations = null,
        Func<bool>? gameRunningProbe = null,
        Action<string>? beforeTargetReplace = null)
    {
        _codec = codec;
        _locations = locations ?? SaveEditorLocations.CreateDefault();
        _gameRunningProbe = gameRunningProbe ?? IsGameRunning;
        _beforeTargetReplace = beforeTargetReplace;
    }

    public async Task<PreparedForceTownEdit> PrepareAsync(
        SaveProfile profile,
        BattleMapSnapshot expectedSnapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(expectedSnapshot);
        cancellationToken.ThrowIfCancellationRequested();
        _codec.ValidateAvailability();

        var paths = ValidateProfile(profile, expectedSnapshot);
        var gameHash = ComputeSha256(paths.GamePath);
        var mapHash = ComputeSha256(paths.MapPath);
        var raidHash = ComputeSha256(paths.RaidPath);
        ValidateSnapshotHashes(expectedSnapshot, mapHash, raidHash);

        var sessionId = $"{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}";
        var workspace = Path.Combine(_locations.WorkspaceDirectory, "force_town", sessionId);
        var sourceDirectory = Path.Combine(workspace, "source");
        var decodedDirectory = Path.Combine(workspace, "decoded");
        var encodedDirectory = Path.Combine(workspace, "encoded");
        var roundTripDirectory = Path.Combine(workspace, "roundtrip");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(decodedDirectory);
        Directory.CreateDirectory(encodedDirectory);
        Directory.CreateDirectory(roundTripDirectory);

        var sourceCopyPath = Path.Combine(sourceDirectory, "persist.game.json");
        var decodedPath = Path.Combine(decodedDirectory, "persist.game.json");
        var proposedPath = Path.Combine(decodedDirectory, "persist.game.proposed.json");
        var encodedPath = Path.Combine(encodedDirectory, "persist.game.json");
        var roundTripPath = Path.Combine(roundTripDirectory, "persist.game.json");
        File.Copy(paths.GamePath, sourceCopyPath, overwrite: false);
        ValidateCapturedState(paths, sourceCopyPath, gameHash, mapHash, raidHash);

        await _codec.DecodeAsync(sourceCopyPath, decodedPath, cancellationToken).ConfigureAwait(false);
        var gameDocument = JsonSupport.ReadObject(decodedPath);
        var baseRoot = JsonSupport.RequireObject(gameDocument, "base_root");
        if (baseRoot["inraid"] is not JsonValue inRaidNode ||
            !inRaidNode.TryGetValue<bool>(out var previousInRaid))
        {
            throw new InvalidDataException("persist.game.json 缺少有效的 inraid 状态，无法安全设置回城。");
        }
        if (baseRoot["raiddungeon"] is not JsonValue raidDungeonNode ||
            !raidDungeonNode.TryGetValue<string>(out var previousRaidDungeon) ||
            string.IsNullOrWhiteSpace(previousRaidDungeon))
        {
            throw new InvalidDataException("persist.game.json 缺少有效的 raiddungeon 状态，无法安全设置回城。");
        }
        if (!previousInRaid)
        {
            throw new InvalidOperationException("所选档案的读档入口已经是城镇，无需再次强制回城。");
        }

        baseRoot["inraid"] = false;
        baseRoot["raiddungeon"] = "none";
        var preview = new ForceTownEditPreview(previousInRaid, previousRaidDungeon, false, "none");
        JsonSupport.WriteObject(proposedPath, gameDocument);
        var sourceWasDson = DsonSaveCodec.IsDson(sourceCopyPath);
        await _codec.EncodeAsync(
            proposedPath,
            encodedPath,
            sourceCopyPath,
            cancellationToken).ConfigureAwait(false);
        await _codec.DecodeAsync(encodedPath, roundTripPath, cancellationToken).ConfigureAwait(false);
        var roundTripDocument = JsonSupport.ReadObject(roundTripPath);
        if (!JsonNode.DeepEquals(gameDocument, roundTripDocument))
        {
            throw new InvalidDataException("强制回城修改未通过 DSON 编码回环验证，本次修改未写入。");
        }
        if (sourceWasDson && !RevisionMatches(sourceCopyPath, encodedPath))
        {
            throw new InvalidDataException("编码后的 persist.game.json 未保留原始 DSON 修订字段，本次修改未写入。");
        }

        ValidateCapturedState(paths, sourceCopyPath, gameHash, mapHash, raidHash);
        var gameFile = new PreparedSaveFile(
            "persist.game.json",
            paths.GamePath,
            sourceCopyPath,
            proposedPath,
            encodedPath,
            roundTripPath,
            gameHash,
            ComputeSha256(encodedPath),
            sourceWasDson);
        var prepared = new PreparedForceTownEdit(
            sessionId,
            profile,
            preview,
            workspace,
            gameFile,
            mapHash,
            raidHash,
            DateTime.UtcNow);
        WriteJson(Path.Combine(workspace, "session.json"), prepared);
        return prepared;
    }

    public async Task<SaveCommitResult> CommitAsync(
        PreparedForceTownEdit prepared,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureGameIsNotRunning();
        var paths = ValidateProfile(prepared.Profile, expectedSnapshot: null);
        ValidatePreparedTarget(prepared, paths.GamePath);
        ValidateLiveState(prepared, paths, "准备完成后");
        if (!File.Exists(prepared.GameFile.EncodedPath) ||
            !ComputeSha256(prepared.GameFile.EncodedPath).Equals(
                prepared.GameFile.EncodedSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("已验证的强制回城结果已经变化或丢失，请重新执行操作。");
        }

        var backupDirectory = CreateBackup(prepared.Profile, prepared);
        ValidateLiveState(prepared, paths, "创建档案备份期间");
        EnsureGameIsNotRunning();

        var targetDirectory = Path.GetDirectoryName(paths.GamePath)
            ?? throw new InvalidOperationException($"无法确定存档目标目录：{paths.GamePath}");
        var temporaryTarget = Path.Combine(
            targetDirectory,
            $".persist.game.json.ddse-{Guid.NewGuid():N}.tmp");
        var displacedTarget = Path.Combine(
            targetDirectory,
            $".persist.game.json.ddse-displaced-{Guid.NewGuid():N}.tmp");
        var backupTargetPath = Path.Combine(backupDirectory, "persist.game.json");
        var replacementSucceeded = false;
        var preserveDisplacedTarget = false;

        try
        {
            File.Copy(prepared.GameFile.EncodedPath, temporaryTarget, overwrite: false);
            using var mapLock = OpenGuard(paths.MapPath);
            using var raidLock = OpenGuard(paths.RaidPath);
            using var gameLock = new FileStream(
                paths.GamePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read | FileShare.Delete,
                bufferSize: 4096,
                FileOptions.SequentialScan);
            if (!ComputeSha256(mapLock).Equals(prepared.MapOriginalSha256, StringComparison.OrdinalIgnoreCase) ||
                !ComputeSha256(raidLock).Equals(prepared.RaidOriginalSha256, StringComparison.OrdinalIgnoreCase) ||
                !ComputeSha256(gameLock).Equals(prepared.GameFile.OriginalSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "游戏、地图或副本存档在写入前发生了变化，本次修改未应用。请重新加载内容目录后重试。");
            }

            _beforeTargetReplace?.Invoke(paths.GamePath);
            File.Replace(
                temporaryTarget,
                paths.GamePath,
                destinationBackupFileName: displacedTarget,
                ignoreMetadataErrors: true);
            replacementSucceeded = true;

            var displacedHash = ComputeSha256(displacedTarget);
            if (!displacedHash.Equals(prepared.GameFile.OriginalSha256, StringComparison.OrdinalIgnoreCase))
            {
                replacementSucceeded = false;
                try
                {
                    RestoreFileVersion(displacedTarget, paths.GamePath, displacedHash);
                }
                catch (Exception restoreError)
                {
                    preserveDisplacedTarget = true;
                    throw new AggregateException(
                        $"persist.game.json 在最终写入瞬间被其他进程替换，程序无法自动恢复较新的版本。" +
                        $"该版本保留在：{displacedTarget}",
                        restoreError);
                }

                throw new InvalidOperationException(
                    "persist.game.json 在最终写入瞬间发生了变化；程序已恢复变化后的版本，本次强制回城未应用。");
            }

            using var finalGameLock = new FileStream(
                paths.GamePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.SequentialScan);
            var finalHash = ComputeSha256(finalGameLock);
            if (!finalHash.Equals(prepared.GameFile.EncodedSha256, StringComparison.OrdinalIgnoreCase))
            {
                replacementSucceeded = false;
                throw new InvalidOperationException(
                    "persist.game.json 在原子替换后又被其他进程更新；程序保留了更新后的版本，本次强制回城未生效。");
            }
            if (
                !ComputeSha256(mapLock).Equals(prepared.MapOriginalSha256, StringComparison.OrdinalIgnoreCase) ||
                !ComputeSha256(raidLock).Equals(prepared.RaidOriginalSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("写入后的回城状态不符合已验证结果，程序将尝试自动恢复。");
            }

            var result = new SaveCommitResult(
                prepared.Profile.ProfileDirectory,
                paths.GamePath,
                backupDirectory,
                prepared.GameFile.OriginalSha256,
                finalHash,
                DateTime.UtcNow);
            await Task.CompletedTask.ConfigureAwait(false);
            return result;
        }
        catch (Exception commitError)
        {
            if (replacementSucceeded)
            {
                try
                {
                    var restoreSource = File.Exists(displacedTarget) &&
                                        ComputeSha256(displacedTarget).Equals(
                                            prepared.GameFile.OriginalSha256,
                                            StringComparison.OrdinalIgnoreCase)
                        ? displacedTarget
                        : backupTargetPath;
                    RestoreTarget(restoreSource, prepared.GameFile);
                    throw new InvalidOperationException(
                        $"强制回城写入失败，已恢复原 persist.game.json；完整档案备份位于：{backupDirectory}",
                        commitError);
                }
                catch (InvalidOperationException ex) when (ReferenceEquals(ex.InnerException, commitError))
                {
                    throw;
                }
                catch (Exception restoreError)
                {
                    preserveDisplacedTarget = true;
                    throw new AggregateException(
                        $"强制回城写入失败，自动恢复也未能完成。请保留并使用完整备份：{backupDirectory}" +
                        (File.Exists(displacedTarget) ? $"；被替换文件：{displacedTarget}" : string.Empty),
                        commitError,
                        restoreError);
                }
            }

            throw;
        }
        finally
        {
            TryDeleteFile(temporaryTarget);
            if (!preserveDisplacedTarget)
            {
                TryDeleteFile(displacedTarget);
            }
        }
    }

    private static ForceTownProfilePaths ValidateProfile(
        SaveProfile profile,
        BattleMapSnapshot? expectedSnapshot)
    {
        var profileDirectory = Path.GetFullPath(profile.ProfileDirectory);
        if (!Directory.Exists(profileDirectory))
        {
            throw new DirectoryNotFoundException($"找不到档案目录：{profileDirectory}");
        }

        var estatePath = Path.Combine(profileDirectory, "persist.estate.json");
        var gamePath = Path.Combine(profileDirectory, "persist.game.json");
        var mapPath = Path.Combine(profileDirectory, "persist.map.json");
        var raidPath = Path.Combine(profileDirectory, "persist.raid.json");
        if (!Path.GetFullPath(profile.EstateSavePath).Equals(estatePath, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(estatePath))
        {
            throw new InvalidOperationException("所选档案缺少预期的 persist.estate.json 文件。");
        }
        if (!File.Exists(gamePath) || !File.Exists(mapPath) || !File.Exists(raidPath))
        {
            throw new InvalidOperationException("所选档案已经不再处于完整的副本状态。");
        }
        if (expectedSnapshot is not null &&
            (!Path.GetFullPath(expectedSnapshot.ProfileDirectory).Equals(profileDirectory, StringComparison.OrdinalIgnoreCase) ||
             !Path.GetFullPath(expectedSnapshot.MapSavePath).Equals(mapPath, StringComparison.OrdinalIgnoreCase) ||
             !Path.GetFullPath(expectedSnapshot.RaidSavePath).Equals(raidPath, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("当前显示的战斗地图属于另一个档案，请重新加载内容目录。");
        }

        return new ForceTownProfilePaths(gamePath, mapPath, raidPath);
    }

    private static void ValidateSnapshotHashes(
        BattleMapSnapshot snapshot,
        string mapHash,
        string raidHash)
    {
        if (!mapHash.Equals(snapshot.MapSha256, StringComparison.OrdinalIgnoreCase) ||
            !raidHash.Equals(snapshot.RaidSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("地图或副本存档在显示后已经变化，请等待地图刷新后重新操作。");
        }
    }

    private static void ValidateCapturedState(
        ForceTownProfilePaths paths,
        string gameCopyPath,
        string gameHash,
        string mapHash,
        string raidHash)
    {
        if (!ComputeSha256(gameCopyPath).Equals(gameHash, StringComparison.OrdinalIgnoreCase) ||
            !ComputeSha256(paths.GamePath).Equals(gameHash, StringComparison.OrdinalIgnoreCase) ||
            !ComputeSha256(paths.MapPath).Equals(mapHash, StringComparison.OrdinalIgnoreCase) ||
            !ComputeSha256(paths.RaidPath).Equals(raidHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("准备强制回城期间存档发生了变化，本次修改未写入。");
        }
    }

    private static void ValidatePreparedTarget(PreparedForceTownEdit prepared, string gamePath)
    {
        if (!Path.GetFullPath(prepared.GameFile.TargetPath).Equals(gamePath, StringComparison.OrdinalIgnoreCase) ||
            !prepared.GameFile.FileName.Equals("persist.game.json", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("准备写入的强制回城目标不属于所选档案。");
        }
    }

    private static void ValidateLiveState(
        PreparedForceTownEdit prepared,
        ForceTownProfilePaths paths,
        string phase)
    {
        if (!ComputeSha256(paths.GamePath).Equals(prepared.GameFile.OriginalSha256, StringComparison.OrdinalIgnoreCase) ||
            !ComputeSha256(paths.MapPath).Equals(prepared.MapOriginalSha256, StringComparison.OrdinalIgnoreCase) ||
            !ComputeSha256(paths.RaidPath).Equals(prepared.RaidOriginalSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"游戏、地图或副本存档在“{phase}”阶段发生了变化。为避免覆盖较新的游戏数据，请重新执行操作。");
        }
    }

    private string CreateBackup(SaveProfile profile, PreparedForceTownEdit prepared)
    {
        var backupDirectory = Path.Combine(
            _locations.BackupDirectory,
            SanitizePathSegment(profile.SteamUserId),
            SanitizePathSegment(profile.ProfileId),
            $"force-town_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(backupDirectory);
        var files = Directory
            .EnumerateFiles(profile.ProfileDirectory, "persist*.json", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path =>
            {
                var destination = Path.Combine(backupDirectory, Path.GetFileName(path));
                var before = ComputeSha256(path);
                File.Copy(path, destination, overwrite: false);
                var backup = ComputeSha256(destination);
                var after = ComputeSha256(path);
                if (!before.Equals(backup, StringComparison.OrdinalIgnoreCase) ||
                    !after.Equals(backup, StringComparison.OrdinalIgnoreCase))
                {
                    throw new IOException($"备份期间存档发生了变化：{path}");
                }

                return new
                {
                    fileName = Path.GetFileName(path),
                    sha256 = backup,
                    length = new FileInfo(destination).Length,
                    lastWriteTimeUtc = File.GetLastWriteTimeUtc(destination)
                };
            })
            .ToArray();
        foreach (var required in new[]
                 {
                     "persist.game.json",
                     "persist.map.json",
                     "persist.raid.json",
                     "persist.estate.json"
                 })
        {
            if (!files.Any(file => file.fileName.Equals(required, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"完整档案备份缺少 {required}，本次修改未写入。");
            }
        }

        WriteJson(Path.Combine(backupDirectory, "backup-manifest.json"), new
        {
            version = 1,
            operation = "force-town-save-edit",
            createdAtUtc = DateTime.UtcNow,
            profile.ProfileId,
            profile.SteamUserId,
            profile.ProfileDirectory,
            prepared.SessionId,
            previousInRaid = prepared.Preview.PreviousInRaid,
            previousRaidDungeon = prepared.Preview.PreviousRaidDungeon,
            resultInRaid = prepared.Preview.ResultInRaid,
            resultRaidDungeon = prepared.Preview.ResultRaidDungeon,
            gameOriginalSha256 = prepared.GameFile.OriginalSha256,
            prepared.MapOriginalSha256,
            prepared.RaidOriginalSha256,
            files
        });
        return backupDirectory;
    }

    private void EnsureGameIsNotRunning()
    {
        if (_gameRunningProbe())
        {
            throw new InvalidOperationException("检测到《暗黑地牢》仍在运行。请完全退出游戏后再强制返回城镇。");
        }
    }

    private static bool IsGameRunning()
    {
        foreach (var processName in new[] { "Darkest", "DarkestDungeon" })
        {
            Process[] processes;
            try
            {
                processes = Process.GetProcessesByName(processName);
            }
            catch
            {
                continue;
            }

            try
            {
                if (processes.Any(process => !process.HasExited))
                {
                    return true;
                }
            }
            finally
            {
                foreach (var process in processes)
                {
                    process.Dispose();
                }
            }
        }

        return false;
    }

    private static FileStream OpenGuard(string path) => new(
        path,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read,
        bufferSize: 4096,
        FileOptions.SequentialScan);

    private static void RestoreTarget(string backupPath, PreparedSaveFile targetFile)
    {
        if (!File.Exists(backupPath) ||
            !ComputeSha256(backupPath).Equals(targetFile.OriginalSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("persist.game.json 的备份丢失或与原始存档不一致。");
        }

        var targetDirectory = Path.GetDirectoryName(targetFile.TargetPath)
            ?? throw new InvalidOperationException($"无法确定存档目标目录：{targetFile.TargetPath}");
        var temporaryTarget = Path.Combine(
            targetDirectory,
            $".persist.game.json.ddse-restore-{Guid.NewGuid():N}.tmp");
        try
        {
            File.Copy(backupPath, temporaryTarget, overwrite: false);
            File.Replace(
                temporaryTarget,
                targetFile.TargetPath,
                destinationBackupFileName: null,
                ignoreMetadataErrors: true);
            if (!ComputeSha256(targetFile.TargetPath).Equals(
                    targetFile.OriginalSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("恢复后的 persist.game.json 与原始存档哈希不一致。");
            }
        }
        finally
        {
            TryDeleteFile(temporaryTarget);
        }
    }

    private static void RestoreFileVersion(
        string sourcePath,
        string targetPath,
        string expectedSha256)
    {
        if (!File.Exists(sourcePath) ||
            !ComputeSha256(sourcePath).Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("需要恢复的 persist.game.json 版本丢失或已经变化。");
        }

        var targetDirectory = Path.GetDirectoryName(targetPath)
            ?? throw new InvalidOperationException($"无法确定存档目标目录：{targetPath}");
        var temporaryTarget = Path.Combine(
            targetDirectory,
            $".persist.game.json.ddse-race-restore-{Guid.NewGuid():N}.tmp");
        try
        {
            File.Copy(sourcePath, temporaryTarget, overwrite: false);
            File.Replace(
                temporaryTarget,
                targetPath,
                destinationBackupFileName: null,
                ignoreMetadataErrors: true);
            if (!ComputeSha256(targetPath).Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("恢复后的 persist.game.json 与变化后的版本哈希不一致。");
            }
        }
        finally
        {
            TryDeleteFile(temporaryTarget);
        }
    }

    private static bool RevisionMatches(string leftPath, string rightPath)
    {
        using var left = File.OpenRead(leftPath);
        using var right = File.OpenRead(rightPath);
        if (left.Length < 8 || right.Length < 8)
        {
            return false;
        }

        left.Position = 4;
        right.Position = 4;
        Span<byte> leftRevision = stackalloc byte[4];
        Span<byte> rightRevision = stackalloc byte[4];
        left.ReadExactly(leftRevision);
        right.ReadExactly(rightRevision);
        return leftRevision.SequenceEqual(rightRevision);
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return ComputeSha256(stream);
    }

    private static string ComputeSha256(Stream stream)
    {
        stream.Position = 0;
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string SanitizePathSegment(string value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            normalized = normalized.Replace(invalid, '_');
        }

        return normalized;
    }

    private static void WriteJson<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(value, JsonSupport.SerializerOptions), Utf8NoBom);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private sealed record ForceTownProfilePaths(string GamePath, string MapPath, string RaidPath);
}
