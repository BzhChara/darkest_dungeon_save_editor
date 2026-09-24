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
        profile = (await RaidSaveLocation.ReadAsync(profile.ProfileDirectory, _codec, cancellationToken)
            .ConfigureAwait(false)).Bind(profile);

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
        var capturedLocation = RaidSaveLocation.FromGame(profile.ProfileDirectory, gameDocument);
        if (!capturedLocation.MapPath.Equals(paths.MapPath, StringComparison.OrdinalIgnoreCase))
            throw new IOException(EditorText.Get("ForceTownSaveService_001"));
        var baseRoot = JsonSupport.RequireObject(gameDocument, "base_root");
        if (baseRoot["inraid"] is not JsonValue inRaidNode ||
            !inRaidNode.TryGetValue<bool>(out var previousInRaid))
        {
            throw new InvalidDataException(EditorText.Get("ForceTownSaveService_002"));
        }
        if (baseRoot["raiddungeon"] is not JsonValue raidDungeonNode ||
            !raidDungeonNode.TryGetValue<string>(out var previousRaidDungeon) ||
            string.IsNullOrWhiteSpace(previousRaidDungeon))
        {
            throw new InvalidDataException(EditorText.Get("ForceTownSaveService_003"));
        }
        if (!previousInRaid)
        {
            throw new InvalidOperationException(EditorText.Get("ForceTownSaveService_004"));
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
            throw new InvalidDataException(EditorText.Get("ForceTownSaveService_005"));
        }
        if (sourceWasDson && !RevisionMatches(sourceCopyPath, encodedPath))
        {
            throw new InvalidDataException(EditorText.Get("ForceTownSaveService_006"));
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
        ValidateLiveState(prepared, paths, EditorText.Get("BattleMapEditService_001"));
        if (!File.Exists(prepared.GameFile.EncodedPath) ||
            !ComputeSha256(prepared.GameFile.EncodedPath).Equals(
                prepared.GameFile.EncodedSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(EditorText.Get("ForceTownSaveService_007"));
        }

        var backupDirectory = CreateBackup(prepared.Profile, prepared);
        ValidateLiveState(prepared, paths, EditorText.Get("BattleMapEditService_003"));
        EnsureGameIsNotRunning();

        var targetDirectory = Path.GetDirectoryName(paths.GamePath)
            ?? throw new InvalidOperationException(EditorText.Format("ForceTownSaveService_008", paths.GamePath));
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
                    EditorText.Get("ForceTownSaveService_009"));
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
                        EditorText.Format("ForceTownSaveService_010") +
                        EditorText.Format("ForceTownSaveService_011", displacedTarget),
                        restoreError);
                }

                throw new InvalidOperationException(
                    EditorText.Get("ForceTownSaveService_012"));
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
                    EditorText.Get("ForceTownSaveService_013"));
            }
            if (
                !ComputeSha256(mapLock).Equals(prepared.MapOriginalSha256, StringComparison.OrdinalIgnoreCase) ||
                !ComputeSha256(raidLock).Equals(prepared.RaidOriginalSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException(EditorText.Get("ForceTownSaveService_014"));
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
                        EditorText.Format("ForceTownSaveService_015", backupDirectory),
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
                        EditorText.Format("ForceTownSaveService_016", backupDirectory) +
                        (File.Exists(displacedTarget) ? EditorText.Format("ForceTownSaveService_017", displacedTarget) : string.Empty),
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
            throw new DirectoryNotFoundException(EditorText.Format("BattleMapEditService_015", profileDirectory));
        }

        var estatePath = Path.Combine(profileDirectory, "persist.estate.json");
        var gamePath = Path.Combine(profileDirectory, "persist.game.json");
        var mapPath = profile.MapSavePath;
        var raidPath = profile.RaidSavePath;
        if (!Path.GetFullPath(profile.EstateSavePath).Equals(estatePath, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(estatePath))
        {
            throw new InvalidOperationException(EditorText.Get("BattleMapEditService_016"));
        }
        if (!File.Exists(gamePath) || !File.Exists(mapPath) || !File.Exists(raidPath))
        {
            throw new InvalidOperationException(EditorText.Get("BattleMapEditService_017"));
        }
        if (expectedSnapshot is not null &&
            (!Path.GetFullPath(expectedSnapshot.ProfileDirectory).Equals(profileDirectory, StringComparison.OrdinalIgnoreCase) ||
             !Path.GetFullPath(expectedSnapshot.MapSavePath).Equals(mapPath, StringComparison.OrdinalIgnoreCase) ||
             !Path.GetFullPath(expectedSnapshot.RaidSavePath).Equals(raidPath, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(EditorText.Get("BattleMapEditService_020"));
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
            throw new InvalidOperationException(EditorText.Get("BattleMapEditService_012"));
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
            throw new InvalidOperationException(EditorText.Get("ForceTownSaveService_018"));
        }
    }

    private static void ValidatePreparedTarget(PreparedForceTownEdit prepared, string gamePath)
    {
        if (!Path.GetFullPath(prepared.GameFile.TargetPath).Equals(gamePath, StringComparison.OrdinalIgnoreCase) ||
            !prepared.GameFile.FileName.Equals("persist.game.json", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(EditorText.Get("ForceTownSaveService_019"));
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
                EditorText.Format("ForceTownSaveService_020", phase));
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
        var files = ProfileSaveFiles.Enumerate(profile.ProfileDirectory)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path =>
            {
                var destination = ProfileSaveFiles.BackupPath(profile.ProfileDirectory, backupDirectory, path);
                var before = ComputeSha256(path);
                File.Copy(path, destination, overwrite: false);
                var backup = ComputeSha256(destination);
                var after = ComputeSha256(path);
                if (!before.Equals(backup, StringComparison.OrdinalIgnoreCase) ||
                    !after.Equals(backup, StringComparison.OrdinalIgnoreCase))
                {
                    throw new IOException(EditorText.Format("BattleMapEditService_029", path));
                }

                return new
                {
                    fileName = Path.GetRelativePath(profile.ProfileDirectory, path),
                    sha256 = backup,
                    length = new FileInfo(destination).Length,
                    lastWriteTimeUtc = File.GetLastWriteTimeUtc(destination)
                };
            })
            .ToArray();
        foreach (var required in new[]
                 {
                     "persist.game.json",
                     Path.GetRelativePath(profile.ProfileDirectory, profile.MapSavePath),
                     Path.GetRelativePath(profile.ProfileDirectory, profile.RaidSavePath),
                     "persist.estate.json"
                 })
        {
            if (!files.Any(file => file.fileName.Equals(required, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(EditorText.Format("ForceTownSaveService_021", required));
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
            throw new InvalidOperationException(EditorText.Get("ForceTownSaveService_022"));
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
            throw new InvalidDataException(EditorText.Get("ForceTownSaveService_023"));
        }

        var targetDirectory = Path.GetDirectoryName(targetFile.TargetPath)
            ?? throw new InvalidOperationException(EditorText.Format("ForceTownSaveService_008", targetFile.TargetPath));
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
                throw new IOException(EditorText.Get("ForceTownSaveService_024"));
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
            throw new InvalidDataException(EditorText.Get("ForceTownSaveService_025"));
        }

        var targetDirectory = Path.GetDirectoryName(targetPath)
            ?? throw new InvalidOperationException(EditorText.Format("ForceTownSaveService_008", targetPath));
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
                throw new IOException(EditorText.Get("ForceTownSaveService_026"));
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
