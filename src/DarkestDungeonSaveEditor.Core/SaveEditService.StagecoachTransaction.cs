using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DarkestDungeonSaveEditor.Core;

public sealed partial class SaveEditService
{
    public async Task<MultiFileSaveCommitResult> CommitAsync(
        PreparedStagecoachHeroEdit prepared,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureGameIsNotRunning();
        ValidateProfile(prepared.Profile);
        ValidateStagecoachContentGuard(prepared.Profile, prepared.ContentGuard);
        ValidatePreparedStagecoachFile(prepared.Profile, prepared.TownFile, "persist.town.json");
        ValidatePreparedStagecoachFile(prepared.Profile, prepared.RosterFile, "persist.roster.json");
        ValidatePreparedStagecoachFile(prepared.Profile, prepared.UpgradesFile, "persist.upgrades.json");
        EnsureNoUnfinishedStagecoachTransaction(prepared.Profile);

        // Make the candidate visible in town only after its upgrade ownership and GUID advance
        // have both been written. Recovery preserves newer external versions of each target.
        var files = new[] { prepared.UpgradesFile, prepared.RosterFile, prepared.TownFile };
        foreach (var file in files)
        {
            var currentHash = ComputeSha256(file.TargetPath);
            if (!currentHash.Equals(file.OriginalSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"The live {file.FileName} save changed after preview. Prepare a new preview instead of overwriting newer game data.");
            }

            var encodedHash = ComputeSha256(file.EncodedPath);
            if (!encodedHash.Equals(file.EncodedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"The prepared encoded {file.FileName} save changed after validation.");
            }
        }

        var backupDirectory = CreateBackup(prepared.Profile, prepared);
        var replacedFiles = new List<PreparedSaveFile>();
        var replacements = new List<GuardedSaveReplacement>();
        WriteStagecoachTransactionState(
            backupDirectory,
            prepared,
            "backup_complete",
            replacedFiles,
            error: null);

        try
        {
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var hashAfterBackup = ComputeSha256(file.TargetPath);
                if (!hashAfterBackup.Equals(file.OriginalSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"The live {file.FileName} save changed while the profile backup was being created. " +
                        $"A backup was kept at '{backupDirectory}', but no replacement of this file was attempted.");
                }
            }

            foreach (var file in files)
            {
                ValidateStagecoachContentGuard(prepared.Profile, prepared.ContentGuard);
                WriteStagecoachTransactionState(
                    backupDirectory,
                    prepared,
                    $"replacing_{Path.GetFileNameWithoutExtension(file.FileName).Replace("persist.", string.Empty, StringComparison.Ordinal)}",
                    replacedFiles,
                    error: null);
                var replacement = new GuardedSaveReplacement(file.TargetPath, file.EncodedPath,
                    file.OriginalSha256, file.EncodedSha256, BeforeTargetReplace, AfterTargetReplace);
                replacements.Add(replacement);
                try
                {
                    replacement.Replace();
                }
                finally
                {
                    if (replacement.HasReplaced)
                        replacedFiles.Add(file);
                }
                WriteStagecoachTransactionState(
                    backupDirectory,
                    prepared,
                    $"replaced_{Path.GetFileNameWithoutExtension(file.FileName).Replace("persist.", string.Empty, StringComparison.Ordinal)}",
                    replacedFiles,
                    error: null);
            }

            ValidateStagecoachContentGuard(prepared.Profile, prepared.ContentGuard);

            var results = new List<SaveFileCommitResult>();
            foreach (var file in new[]
                     {
                         prepared.TownFile,
                         prepared.RosterFile,
                         prepared.UpgradesFile
                     })
            {
                var finalHash = replacements.Single(replacement =>
                    replacement.TargetPath.Equals(file.TargetPath, StringComparison.OrdinalIgnoreCase)).Verify();

                results.Add(new SaveFileCommitResult(
                    file.FileName,
                    file.TargetPath,
                    file.OriginalSha256,
                    finalHash));
            }

            var result = new MultiFileSaveCommitResult(
                prepared.Profile.ProfileDirectory,
                backupDirectory,
                results,
                DateTime.UtcNow);
            WriteJson(Path.Combine(backupDirectory, "commit-result.json"), result);
            WriteStagecoachTransactionState(
                backupDirectory,
                prepared,
                "committed",
                replacedFiles,
                error: null);
            foreach (var replacement in replacements)
                replacement.Complete();
            await Task.CompletedTask.ConfigureAwait(false);
            return result;
        }
        catch (Exception commitError)
        {
            TryDeleteFile(Path.Combine(backupDirectory, "commit-result.json"));
            TryWriteStagecoachTransactionState(
                backupDirectory,
                prepared,
                "commit_failed_restoring",
                replacedFiles,
                commitError.Message);
            try
            {
                RestoreReplacedStagecoachFiles(files, replacements);
                TryWriteStagecoachTransactionState(
                    backupDirectory,
                    prepared,
                    "restored",
                    replacedFiles,
                    commitError.Message);
                throw new InvalidOperationException(
                    $"Stagecoach save commit failed and every replaced file was restored. Backup: '{backupDirectory}'.",
                    commitError);
            }
            catch (InvalidOperationException ex) when (ReferenceEquals(ex.InnerException, commitError))
            {
                throw;
            }
            catch (Exception restoreError)
            {
                TryWriteStagecoachTransactionState(
                    backupDirectory,
                    prepared,
                    "restore_failed",
                    replacedFiles,
                    restoreError.Message);
                throw new AggregateException(
                    $"Stagecoach save commit failed and automatic restore also failed. Backup: {backupDirectory}",
                    commitError,
                    restoreError);
            }
        }
        finally
        {
            foreach (var replacement in replacements)
                replacement.Dispose();
        }
    }

    private string CreateBackup(SaveProfile profile, PreparedStagecoachHeroEdit prepared)
    {
        var backupDirectory = Path.Combine(
            _locations.BackupDirectory,
            SanitizePathSegment(profile.SteamUserId),
            SanitizePathSegment(profile.ProfileId),
            $"{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(backupDirectory);

        var files = ProfileSaveFiles.Enumerate(profile.ProfileDirectory)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path =>
            {
                var destination = ProfileSaveFiles.BackupPath(profile.ProfileDirectory, backupDirectory, path);
                var sourceHashBeforeCopy = ComputeSha256(path);
                File.Copy(path, destination, overwrite: false);
                var backupHash = ComputeSha256(destination);
                var sourceHashAfterCopy = ComputeSha256(path);
                if (!sourceHashBeforeCopy.Equals(backupHash, StringComparison.OrdinalIgnoreCase) ||
                    !sourceHashAfterCopy.Equals(backupHash, StringComparison.OrdinalIgnoreCase))
                {
                    throw new IOException($"Save file changed during backup: {path}");
                }

                return new
                {
                    fileName = Path.GetRelativePath(profile.ProfileDirectory, path),
                    sha256 = backupHash,
                    length = new FileInfo(destination).Length,
                    lastWriteTimeUtc = File.GetLastWriteTimeUtc(destination)
                };
            })
            .ToArray();

        WriteJson(Path.Combine(backupDirectory, "backup-manifest.json"), new
        {
            version = 1,
            operation = "add-stagecoach-hero",
            createdAtUtc = DateTime.UtcNow,
            profile.ProfileId,
            profile.SteamUserId,
            profile.ProfileDirectory,
            prepared.SessionId,
            prepared.Preview.CandidateGuid,
            prepared.Preview.HeroClass,
            targetPool = prepared.Preview.TargetPool.ToString(),
            resolveXp = prepared.Preview.ResolveXp,
            weaponRank = prepared.Preview.WeaponRank,
            armourRank = prepared.Preview.ArmourRank,
            upgradePurchaseCount = prepared.Preview.UpgradePurchaseCount,
            files
        });
        return backupDirectory;
    }

    private void EnsureNoUnfinishedStagecoachTransaction(SaveProfile profile)
    {
        var profileBackupRoot = Path.Combine(
            _locations.BackupDirectory,
            SanitizePathSegment(profile.SteamUserId),
            SanitizePathSegment(profile.ProfileId));
        if (!Directory.Exists(profileBackupRoot))
        {
            return;
        }

        foreach (var backupDirectory in Directory.EnumerateDirectories(profileBackupRoot)
                     .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var statePath = Path.Combine(backupDirectory, "transaction-state.json");
            if (!File.Exists(statePath))
            {
                continue;
            }

            string status;
            try
            {
                status = JsonSupport.ReadString(JsonSupport.ReadObject(statePath), "status");
            }
            catch (Exception error)
            {
                throw new InvalidOperationException(
                    $"A stagecoach transaction state could not be read at '{statePath}'. " +
                    "Inspect its backup before preparing another edit.",
                    error);
            }

            if (!status.Equals("committed", StringComparison.OrdinalIgnoreCase) &&
                !status.Equals("restored", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"An unfinished stagecoach save transaction ('{status}') exists at '{backupDirectory}'. " +
                    "Inspect or restore that backup before preparing another edit.");
            }
        }
    }

    private static void ValidateUnchangedDuringCopy(
        string livePath,
        string sourceCopyPath,
        string originalHash,
        string saveLabel)
    {
        var copyHash = ComputeSha256(sourceCopyPath);
        var currentHash = ComputeSha256(livePath);
        if (!copyHash.Equals(originalHash, StringComparison.OrdinalIgnoreCase) ||
            !currentHash.Equals(originalHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The {saveLabel} save changed while the edit workspace was being prepared.");
        }
    }

    private static string GetProfileSavePath(SaveProfile profile, string fileName)
    {
        return Path.GetFullPath(Path.Combine(profile.ProfileDirectory, fileName));
    }

    private static void ValidateRequiredSaveFile(string path, string fileName)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"{fileName} was not found in the selected profile.", path);
        }
    }

    private static void ValidatePreparedStagecoachFile(
        SaveProfile profile,
        PreparedSaveFile preparedFile,
        string expectedFileName)
    {
        var expectedPath = GetProfileSavePath(profile, expectedFileName);
        if (!preparedFile.FileName.Equals(expectedFileName, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFullPath(preparedFile.TargetPath).Equals(expectedPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Prepared {expectedFileName} target is outside the selected profile directory.");
        }

        ValidateRequiredSaveFile(expectedPath, expectedFileName);
        if (!File.Exists(preparedFile.EncodedPath))
        {
            throw new FileNotFoundException(
                $"Prepared encoded {expectedFileName} was not found.",
                preparedFile.EncodedPath);
        }
    }

    private static void RestoreReplacedStagecoachFiles(
        IReadOnlyList<PreparedSaveFile> allFiles,
        IReadOnlyList<GuardedSaveReplacement> replacements)
    {
        var restoreErrors = new List<Exception>();
        foreach (var replacement in replacements.Reverse())
        {
            try
            {
                var recovery = replacement.Recover();
                if (recovery is SaveFileRecovery.KeptExternal or SaveFileRecovery.RestoredExternal)
                    throw new IOException(
                        $"{replacement.TargetPath}：{GuardedSaveReplacement.DescribeRecovery(recovery)}；" +
                        $"不能声明三个文件全部恢复原状。实际被替换版本：{replacement.DisplacedPath}");
            }
            catch (Exception error)
            {
                restoreErrors.Add(new IOException(
                    $"恢复 {replacement.TargetPath} 未完成；实际被替换版本：{replacement.DisplacedPath}", error));
            }
        }

        foreach (var file in allFiles)
        {
            try
            {
                var replacement = replacements.SingleOrDefault(item => item.HasReplaced &&
                    item.TargetPath.Equals(file.TargetPath, StringComparison.OrdinalIgnoreCase));
                var currentHash = replacement is null ? ComputeSha256(file.TargetPath) : replacement.CurrentHash();
                if (!currentHash.Equals(file.OriginalSha256, StringComparison.OrdinalIgnoreCase))
                {
                    restoreErrors.Add(new IOException(
                        $"After rollback, {file.FileName} does not match the file used for preview."));
                }
            }
            catch (Exception error)
            {
                restoreErrors.Add(error);
            }
        }

        if (restoreErrors.Count > 0)
        {
            throw new AggregateException("One or more stagecoach save files could not be restored.", restoreErrors);
        }
    }

    private static void WriteStagecoachTransactionState(
        string backupDirectory,
        PreparedStagecoachHeroEdit prepared,
        string status,
        IReadOnlyList<PreparedSaveFile> replacedFiles,
        string? error)
    {
        WriteJsonAtomic(Path.Combine(backupDirectory, "transaction-state.json"), new
        {
            version = 1,
            updatedAtUtc = DateTime.UtcNow,
            prepared.SessionId,
            prepared.Preview.CandidateGuid,
            targetPool = prepared.Preview.TargetPool.ToString(),
            status,
            replacedFiles = replacedFiles.Select(file => file.FileName).ToArray(),
            error
        });
    }

    private static void TryWriteStagecoachTransactionState(
        string backupDirectory,
        PreparedStagecoachHeroEdit prepared,
        string status,
        IReadOnlyList<PreparedSaveFile> replacedFiles,
        string? error)
    {
        try
        {
            WriteStagecoachTransactionState(backupDirectory, prepared, status, replacedFiles, error);
        }
        catch
        {
            // A logging failure must not prevent rollback of already replaced save files.
        }
    }

    private static void WriteJsonAtomic<T>(string path, T value)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException($"Output path has no directory: {path}");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(path)}.ddse-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(value, JsonSupport.SerializerOptions),
                Utf8NoBom);
            if (File.Exists(path))
            {
                File.Replace(
                    temporaryPath,
                    path,
                    destinationBackupFileName: null,
                    ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, path);
            }
        }
        finally
        {
            TryDeleteFile(temporaryPath);
        }
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
            // Temporary files are harmless; preserve the primary commit or restore result.
        }
    }

}
