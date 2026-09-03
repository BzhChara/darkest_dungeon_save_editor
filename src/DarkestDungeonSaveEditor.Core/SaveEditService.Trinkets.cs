using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DarkestDungeonSaveEditor.Core;

public sealed partial class SaveEditService
{
    public async Task<PreparedTrinketEdit> PrepareTrinketEditAsync(
        SaveProfile profile,
        TrinketDefinition trinket,
        int copies,
        TrinketStorageDefinition? expectedStorage,
        ActiveContentSnapshot? activeContent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(trinket);
        _codec.ValidateAvailability();
        ValidateProfile(profile);
        if (trinket.HasProviderConflict)
        {
            throw new InvalidOperationException(
                $"Trinket '{trinket.Id}' has unresolved definitions from different content paths and is read-only.");
        }

        if (trinket.UnsupportedStateFields.Count > 0)
        {
            throw new InvalidOperationException(
                $"饰品“{trinket.Id}”的次数定义无效（{string.Join(", ", trinket.UnsupportedStateFields)}），" +
                "无法安全生成初始状态。");
        }

        if (expectedStorage is null)
        {
            throw new InvalidOperationException(
                "The active trinket storage capacity could not be resolved; preview and commit are disabled.");
        }

        if (activeContent is null)
        {
            throw new InvalidOperationException(
                "The active content snapshot is unavailable; trinket preview and commit are disabled.");
        }

        ValidateActiveContentSnapshot(profile, activeContent);
        var manifestFingerprints = CaptureManifestFingerprints(activeContent.Sources);
        var currentCatalog = TrinketCatalog.Load(activeContent);
        ValidateManifestFingerprints(manifestFingerprints, "after the catalog was loaded; reload the content catalog");
        var currentStorage = currentCatalog.Storage
            ?? throw new InvalidOperationException(
                "The active trinket storage capacity could not be resolved; preview and commit are disabled.");
        if (!StorageDefinitionMatches(currentStorage, expectedStorage))
        {
            throw new InvalidOperationException(
                "The active trinket storage configuration changed after the catalog was loaded; reload the content catalog.");
        }

        var currentTrinket = currentCatalog.Trinkets.SingleOrDefault(item =>
            item.Id.Equals(trinket.Id, StringComparison.OrdinalIgnoreCase));
        if (currentTrinket is null ||
            currentTrinket.UnsupportedStateFields.Count > 0 ||
            currentTrinket.HasProviderConflict ||
            !TrinketDefinitionMatches(currentTrinket, trinket))
        {
            throw new InvalidOperationException(
                "The selected trinket definition changed after the catalog was loaded; reload the content catalog.");
        }

        var currentTrinketSourceSha256 = ComputeSha256(currentTrinket.SourcePath);

        var originalHash = ComputeSha256(profile.EstateSavePath);
        var sessionId = $"{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}";
        var workspace = Path.Combine(_locations.WorkspaceDirectory, sessionId);
        var sourceDirectory = Path.Combine(workspace, "source");
        var decodedDirectory = Path.Combine(workspace, "decoded");
        var encodedDirectory = Path.Combine(workspace, "encoded");
        var roundTripDirectory = Path.Combine(workspace, "roundtrip");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(decodedDirectory);
        Directory.CreateDirectory(encodedDirectory);
        Directory.CreateDirectory(roundTripDirectory);

        var sourceCopy = Path.Combine(sourceDirectory, "persist.estate.json");
        var decodedPath = Path.Combine(decodedDirectory, "persist.estate.json");
        var proposedPath = Path.Combine(decodedDirectory, "persist.estate.proposed.json");
        var encodedPath = Path.Combine(encodedDirectory, "persist.estate.json");
        var roundTripPath = Path.Combine(roundTripDirectory, "persist.estate.json");
        File.Copy(profile.EstateSavePath, sourceCopy, overwrite: false);

        var copyHash = ComputeSha256(sourceCopy);
        var currentHash = ComputeSha256(profile.EstateSavePath);
        if (!copyHash.Equals(originalHash, StringComparison.OrdinalIgnoreCase) ||
            !currentHash.Equals(originalHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The estate save changed while the edit workspace was being prepared.");
        }

        var sourceWasDson = DsonSaveCodec.IsDson(sourceCopy);
        await _codec.DecodeAsync(sourceCopy, decodedPath, cancellationToken).ConfigureAwait(false);
        var originalRoot = JsonSupport.ReadObject(decodedPath);
        var originalSummary = TrinketSaveEditor.Summarize(originalRoot);
        var (updatedRoot, preview) = TrinketSaveEditor.AddCopies(
            originalRoot,
            trinket,
            copies,
            currentStorage.MaxSlots);
        var expectedSummary = TrinketSaveEditor.Summarize(updatedRoot);
        JsonSupport.WriteObject(proposedPath, updatedRoot);

        await _codec.EncodeAsync(proposedPath, encodedPath, sourceCopy, cancellationToken).ConfigureAwait(false);
        await _codec.DecodeAsync(encodedPath, roundTripPath, cancellationToken).ConfigureAwait(false);
        var roundTripRoot = JsonSupport.ReadObject(roundTripPath);
        var resultSummary = TrinketSaveEditor.Summarize(roundTripRoot);
        var roundTripCopies = TrinketSaveEditor.CountCopies(roundTripRoot, trinket.Id);
        if (roundTripCopies != preview.ResultingCopies ||
            resultSummary != expectedSummary ||
            !JsonNode.DeepEquals(updatedRoot, roundTripRoot))
        {
            throw new InvalidDataException(
                $"DSON roundtrip validation failed for '{trinket.Id}': " +
                $"expectedCopies={preview.ResultingCopies}, actualCopies={roundTripCopies}, " +
                $"fullDocumentMatch={JsonNode.DeepEquals(updatedRoot, roundTripRoot)}.");
        }

        if (sourceWasDson && !RevisionMatches(sourceCopy, encodedPath))
        {
            throw new InvalidDataException("Encoded save did not preserve the original DSON revision bytes.");
        }

        var prepared = new PreparedTrinketEdit(
            sessionId,
            profile,
            trinket,
            preview,
            new PreparedTrinketContentGuard(
                activeContent.SourceGameSha256,
                activeContent.Sources.ToArray(),
                currentStorage.MaxSlots,
                currentStorage.Source,
                currentStorage.SourcePath,
                currentStorage.SourceSha256,
                currentTrinket.SourcePath,
                currentTrinketSourceSha256,
                manifestFingerprints),
            originalSummary,
            resultSummary,
            workspace,
            sourceCopy,
            proposedPath,
            encodedPath,
            roundTripPath,
            originalHash,
            ComputeSha256(encodedPath),
            sourceWasDson,
            DateTime.UtcNow);
        WriteJson(Path.Combine(workspace, "session.json"), prepared);
        return prepared;
    }

    public async Task<SaveCommitResult> CommitAsync(
        PreparedTrinketEdit prepared,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureGameIsNotRunning();
        ValidateProfile(prepared.Profile);
        ValidateTrinketContentGuard(prepared);

        var currentHash = ComputeSha256(prepared.Profile.EstateSavePath);
        if (!currentHash.Equals(prepared.OriginalSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The live estate save changed after preview. Prepare a new preview instead of overwriting newer game data.");
        }

        var encodedHash = ComputeSha256(prepared.EncodedPath);
        if (!encodedHash.Equals(prepared.EncodedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The prepared encoded save changed after validation.");
        }

        var backupDirectory = CreateBackup(prepared.Profile, prepared);
        var targetPath = prepared.Profile.EstateSavePath;
        var targetDirectory = Path.GetDirectoryName(targetPath)!;
        var temporaryTarget = Path.Combine(
            targetDirectory,
            $".{Path.GetFileName(targetPath)}.ddse-{Guid.NewGuid():N}.tmp");
        var backupEstatePath = Path.Combine(backupDirectory, Path.GetFileName(targetPath));
        var hashAfterBackup = ComputeSha256(targetPath);
        if (!hashAfterBackup.Equals(prepared.OriginalSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The live estate save changed while its backup was being created. A backup was kept at '{backupDirectory}', but no save edit was applied.");
        }

        var replacementSucceeded = false;
        IReadOnlyList<FileStream> contentLocks = [];

        try
        {
            contentLocks = OpenTrinketContentLocks(prepared);
            File.Copy(prepared.EncodedPath, temporaryTarget, overwrite: false);
            ValidateTrinketContentGuard(prepared);
            using (var liveEstateLock = new FileStream(
                       targetPath,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read | FileShare.Delete,
                       bufferSize: 4096,
                       FileOptions.SequentialScan))
            {
                var hashBeforeReplace = ComputeSha256(liveEstateLock);
                if (!hashBeforeReplace.Equals(prepared.OriginalSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "The live estate save changed immediately before replacement. No save edit was applied.");
                }

                ValidateTrinketContentGuard(prepared);
                File.Replace(temporaryTarget, targetPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
                replacementSucceeded = true;
                ValidateTrinketContentGuard(prepared);
            }

            var finalHash = ComputeSha256(targetPath);
            if (!finalHash.Equals(prepared.EncodedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("The committed save hash does not match the validated encoded save.");
            }

            var result = new SaveCommitResult(
                prepared.Profile.ProfileDirectory,
                targetPath,
                backupDirectory,
                prepared.OriginalSha256,
                finalHash,
                DateTime.UtcNow);
            WriteJson(Path.Combine(backupDirectory, "commit-result.json"), result);
            await Task.CompletedTask.ConfigureAwait(false);
            return result;
        }
        catch (Exception commitError)
        {
            if (replacementSucceeded && File.Exists(backupEstatePath))
            {
                try
                {
                    var restoreTemporary = Path.Combine(
                        targetDirectory,
                        $".{Path.GetFileName(targetPath)}.ddse-restore-{Guid.NewGuid():N}.tmp");
                    File.Copy(backupEstatePath, restoreTemporary, overwrite: false);
                    File.Replace(restoreTemporary, targetPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
                    throw new InvalidOperationException(
                        $"Save commit failed and the original estate file was restored from '{backupEstatePath}'.",
                        commitError);
                }
                catch (InvalidOperationException)
                {
                    throw;
                }
                catch (Exception restoreError)
                {
                    throw new AggregateException(
                        $"Save commit failed and automatic restore also failed. Backup: {backupEstatePath}",
                        commitError,
                        restoreError);
                }
            }

            throw;
        }
        finally
        {
            foreach (var contentLock in contentLocks)
            {
                contentLock.Dispose();
            }

            TryDeleteFile(temporaryTarget);
        }
    }

    private string CreateBackup(SaveProfile profile, PreparedTrinketEdit prepared)
    {
        var backupDirectory = Path.Combine(
            _locations.BackupDirectory,
            SanitizePathSegment(profile.SteamUserId),
            SanitizePathSegment(profile.ProfileId),
            $"{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(backupDirectory);

        var files = Directory.EnumerateFiles(profile.ProfileDirectory, "persist*.json", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path =>
            {
                var destination = Path.Combine(backupDirectory, Path.GetFileName(path));
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
                    fileName = Path.GetFileName(path),
                    sha256 = backupHash,
                    length = new FileInfo(destination).Length,
                    lastWriteTimeUtc = File.GetLastWriteTimeUtc(destination)
                };
            })
            .ToArray();

        WriteJson(Path.Combine(backupDirectory, "backup-manifest.json"), new
        {
            version = 1,
            createdAtUtc = DateTime.UtcNow,
            profile.ProfileId,
            profile.SteamUserId,
            profile.ProfileDirectory,
            prepared.SessionId,
            prepared.Trinket.Id,
            prepared.Preview.RequestedCopies,
            files
        });
        return backupDirectory;
    }

    private static void ValidateTrinketContentGuard(PreparedTrinketEdit prepared)
    {
        var gameSavePath = Path.Combine(prepared.Profile.ProfileDirectory, "persist.game.json");
        if (!File.Exists(gameSavePath) ||
            !ComputeSha256(gameSavePath).Equals(
                prepared.ContentGuard.SourceGameSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The active Mod/DLC configuration changed after preview; prepare a new preview.");
        }

        ValidateManifestFingerprints(
            prepared.ContentGuard.ManifestFingerprints,
            "after preview; prepare a new preview");

        var currentSnapshot = new ActiveContentSnapshot(
            prepared.Profile,
            string.Empty,
            prepared.ContentGuard.Sources,
            [],
            string.Empty,
            string.Empty,
            0,
            prepared.ContentGuard.SourceGameSha256);
        var currentCatalog = TrinketCatalog.Load(currentSnapshot);
        var currentStorage = currentCatalog.Storage;
        if (currentStorage is null ||
            currentStorage.MaxSlots != prepared.ContentGuard.StorageCapacity ||
            !currentStorage.Source.Equals(prepared.ContentGuard.StorageSource, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFullPath(currentStorage.SourcePath).Equals(
                Path.GetFullPath(prepared.ContentGuard.StorageSourcePath),
                StringComparison.OrdinalIgnoreCase) ||
            !currentStorage.SourceSha256.Equals(
                prepared.ContentGuard.StorageSourceSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The active trinket storage configuration changed after preview; prepare a new preview.");
        }


        var currentTrinket = currentCatalog.Trinkets.SingleOrDefault(item =>
            item.Id.Equals(prepared.Trinket.Id, StringComparison.OrdinalIgnoreCase));
        if (currentTrinket is null ||
            currentTrinket.UnsupportedStateFields.Count > 0 ||
            currentTrinket.HasProviderConflict ||
            !TrinketDefinitionMatches(currentTrinket, prepared.Trinket) ||
            !Path.GetFullPath(currentTrinket.SourcePath).Equals(
                Path.GetFullPath(prepared.ContentGuard.TrinketSourcePath),
                StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(currentTrinket.SourcePath) ||
            !ComputeSha256(currentTrinket.SourcePath).Equals(
                prepared.ContentGuard.TrinketSourceSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The selected trinket definition changed after preview; prepare a new preview.");
        }
    }

    private static bool StorageDefinitionMatches(
        TrinketStorageDefinition left,
        TrinketStorageDefinition right)
    {
        return left.MaxSlots == right.MaxSlots &&
               left.Source.Equals(right.Source, StringComparison.OrdinalIgnoreCase) &&
               Path.GetFullPath(left.SourcePath).Equals(
                   Path.GetFullPath(right.SourcePath),
                   StringComparison.OrdinalIgnoreCase) &&
               left.SourceSha256.Equals(right.SourceSha256, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TrinketDefinitionMatches(
        TrinketDefinition left,
        TrinketDefinition right)
    {
        return left.Id.Equals(right.Id, StringComparison.OrdinalIgnoreCase) &&
               left.Rarity.Equals(right.Rarity, StringComparison.OrdinalIgnoreCase) &&
               left.Limit == right.Limit &&
               left.Price == right.Price &&
               left.Source.Equals(right.Source, StringComparison.OrdinalIgnoreCase) &&
               Path.GetFullPath(left.SourcePath).Equals(
                   Path.GetFullPath(right.SourcePath),
                   StringComparison.OrdinalIgnoreCase) &&
               left.IsStateful == right.IsStateful &&
               left.StatefulFields.SequenceEqual(right.StatefulFields, StringComparer.OrdinalIgnoreCase) &&
               left.QuestUses == right.QuestUses &&
               left.TriggerLimit == right.TriggerLimit &&
               left.UnsupportedStateFields.SequenceEqual(
                   right.UnsupportedStateFields,
                   StringComparer.OrdinalIgnoreCase) &&
               left.HasProviderConflict == right.HasProviderConflict &&
               left.AllSources.SequenceEqual(right.AllSources, StringComparer.OrdinalIgnoreCase) &&
               left.LocalizedName == right.LocalizedName;
    }

    private static IReadOnlyList<FileStream> OpenTrinketContentLocks(PreparedTrinketEdit prepared)
    {
        var paths = new[]
            {
                Path.Combine(prepared.Profile.ProfileDirectory, "persist.game.json"),
                prepared.ContentGuard.StorageSourcePath,
                prepared.ContentGuard.TrinketSourcePath
            }
        .Concat(prepared.ContentGuard.ManifestFingerprints
            .Where(fingerprint => fingerprint.Exists)
            .Select(fingerprint => fingerprint.Path))
        .Select(Path.GetFullPath)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
        .ToArray();
        var result = new List<FileStream>(paths.Length);
        try
        {
            foreach (var path in paths)
            {
                result.Add(new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 4096,
                    FileOptions.SequentialScan));
            }

            return result;
        }
        catch
        {
            foreach (var stream in result)
            {
                stream.Dispose();
            }

            throw;
        }
    }

}
