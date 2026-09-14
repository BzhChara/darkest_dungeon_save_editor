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
        NativeInventoryIdentity.RequireWritable(trinket.SaveIdentityIssue);
        _codec.ValidateAvailability();
        ValidateProfile(profile);
        if (trinket.HasProviderConflict)
        {
            throw new InvalidOperationException(
                $"Trinket '{trinket.Id}' has unresolved definitions from different content paths and is read-only.");
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
            item.Id.Equals(trinket.Id, StringComparison.Ordinal));
        if (currentTrinket is null ||
            currentTrinket.SaveIdentityIssue.Length > 0 ||
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
        var hashAfterBackup = ComputeSha256(targetPath);
        if (!hashAfterBackup.Equals(prepared.OriginalSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The live estate save changed while its backup was being created. A backup was kept at '{backupDirectory}', but no save edit was applied.");
        }

        using var replacement = new GuardedSaveReplacement(targetPath, prepared.EncodedPath,
            prepared.OriginalSha256, prepared.EncodedSha256, BeforeTargetReplace, AfterTargetReplace);
        IReadOnlyList<FileStream> contentLocks = [];

        try
        {
            contentLocks = OpenTrinketContentLocks(prepared);
            ValidateTrinketContentGuard(prepared);
            replacement.Replace();
            ValidateTrinketContentGuard(prepared);
            var finalHash = replacement.Verify();

            var result = new SaveCommitResult(
                prepared.Profile.ProfileDirectory,
                targetPath,
                backupDirectory,
                prepared.OriginalSha256,
                finalHash,
                DateTime.UtcNow);
            WriteJson(Path.Combine(backupDirectory, "commit-result.json"), result);
            replacement.Complete();
            await Task.CompletedTask.ConfigureAwait(false);
            return result;
        }
        catch (Exception commitError)
        {
            if (replacement.HasReplaced)
            {
                try
                {
                    var recovery = replacement.Recover();
                    throw new InvalidOperationException(
                        $"饰品写入失败；{GuardedSaveReplacement.DescribeRecovery(recovery)}。" +
                        $"完整备份：{backupDirectory}；实际被替换版本：{replacement.DisplacedPath}",
                        commitError);
                }
                catch (InvalidOperationException ex) when (ReferenceEquals(ex.InnerException, commitError))
                {
                    throw;
                }
                catch (Exception restoreError)
                {
                    throw new AggregateException(
                        $"饰品写入失败，自动恢复未能完成。完整备份：{backupDirectory}；实际被替换版本：{replacement.DisplacedPath}",
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
            item.Id.Equals(prepared.Trinket.Id, StringComparison.Ordinal));
        if (currentTrinket is null ||
            currentTrinket.SaveIdentityIssue.Length > 0 ||
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
        return left.Id.Equals(right.Id, StringComparison.Ordinal) &&
               left.Rarity.Equals(right.Rarity, StringComparison.Ordinal) &&
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
