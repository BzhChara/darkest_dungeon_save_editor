using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DarkestDungeonSaveEditor.Core;

public sealed partial class SaveEditService
{
    public async Task<PreparedQuantityItemEdit> PrepareQuantityItemEditAsync(
        SaveProfile profile,
        QuantityItemDefinition item,
        int targetAmount,
        ActiveContentSnapshot? activeContent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(item);
        if (targetAmount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetAmount), "Target amount cannot be negative.");
        }

        if (item.HasProviderConflict)
        {
            throw new InvalidOperationException(
                $"Quantity item '{item.DisplayId}' has unresolved definitions at the same priority and is read-only.");
        }

        _codec.ValidateAvailability();
        ValidateProfile(profile);
        if (activeContent is null)
        {
            throw new InvalidOperationException(
                "The active content snapshot is unavailable; quantity-item preview and commit are disabled.");
        }

        ValidateActiveContentSnapshot(profile, activeContent);
        var saveContext = item.StorageKind == QuantityItemStorageKind.RaidInventory
            ? QuantityItemSaveContext.Raid
            : QuantityItemSaveContext.Town;
        if (QuantityItemSaveScene.Read(activeContent).Context != saveContext)
        {
            throw new InvalidOperationException(saveContext == QuantityItemSaveContext.Town
                ? "当前档案已经进入副本；请重新加载内容目录，程序将切换为当前背包修改模式。"
                : "当前档案已不在副本中；请重新加载内容目录，程序不会把背包修改误写到小镇存档。");
        }
        ValidateQuantityItemSaveContext(profile, saveContext);
        var manifestFingerprints = CaptureManifestFingerprints(activeContent.Sources);
        var currentDefinitions = QuantityItemCatalog.LoadDefinitions(activeContent, saveContext);
        ValidateManifestFingerprints(
            manifestFingerprints,
            "after the quantity-item catalog was loaded; reload the content catalog");
        var currentItem = currentDefinitions.SingleOrDefault(definition =>
            definition.CatalogKey.Equals(item.CatalogKey, StringComparison.OrdinalIgnoreCase));
        if (item.IsSaveOnly)
        {
            if (currentItem is not null)
            {
                throw new InvalidOperationException(
                    "The selected save-only item gained an active content definition after the catalog was loaded; reload the content catalog.");
            }
        }
        else if (currentItem is null ||
                 currentItem.HasProviderConflict ||
                 !QuantityItemDefinitionMatches(currentItem, item))
        {
            throw new InvalidOperationException(
                "The selected quantity-item definition changed after the catalog was loaded; reload the content catalog.");
        }

        RaidInventoryStorageDefinition? raidStorage = null;
        if (saveContext == QuantityItemSaveContext.Raid)
        {
            raidStorage = RaidInventoryStorageCatalog.Load(activeContent).Storage
                ?? throw new InvalidOperationException(
                    "The active expedition inventory capacity could not be resolved; reload the content catalog and review its diagnostics.");
        }

        var itemSourceSha256 = item.IsSaveOnly ? string.Empty : ComputeSha256(item.SourcePath);
        var targetSavePath = saveContext == QuantityItemSaveContext.Raid
            ? profile.RaidSavePath
            : profile.EstateSavePath;
        var targetFileName = Path.GetFileName(targetSavePath);
        var originalHash = ComputeSha256(targetSavePath);
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

        var sourceCopy = Path.Combine(sourceDirectory, targetFileName);
        var decodedPath = Path.Combine(decodedDirectory, targetFileName);
        var proposedPath = Path.Combine(
            decodedDirectory,
            $"{Path.GetFileNameWithoutExtension(targetFileName)}.proposed.json");
        var encodedPath = Path.Combine(encodedDirectory, targetFileName);
        var roundTripPath = Path.Combine(roundTripDirectory, targetFileName);
        File.Copy(targetSavePath, sourceCopy, overwrite: false);

        var copyHash = ComputeSha256(sourceCopy);
        _ = QuantityItemSaveScene.Read(activeContent);
        ValidateQuantityItemSaveContext(profile, saveContext);
        var currentHash = ComputeSha256(targetSavePath);
        if (!copyHash.Equals(originalHash, StringComparison.OrdinalIgnoreCase) ||
            !currentHash.Equals(originalHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The target save changed while the item edit workspace was being prepared.");
        }

        var sourceWasDson = DsonSaveCodec.IsDson(sourceCopy);
        await _codec.DecodeAsync(sourceCopy, decodedPath, cancellationToken).ConfigureAwait(false);
        var originalRoot = JsonSupport.ReadObject(decodedPath);
        var (updatedRoot, preview) = saveContext == QuantityItemSaveContext.Raid
            ? RaidInventorySaveEditor.SetAmount(
                originalRoot,
                item,
                targetAmount,
                raidStorage!.MaxSlots)
            : QuantityItemSaveEditor.SetAmount(originalRoot, item, targetAmount);
        JsonSupport.WriteObject(proposedPath, updatedRoot);

        await _codec.EncodeAsync(proposedPath, encodedPath, sourceCopy, cancellationToken).ConfigureAwait(false);
        await _codec.DecodeAsync(encodedPath, roundTripPath, cancellationToken).ConfigureAwait(false);
        var roundTripRoot = JsonSupport.ReadObject(roundTripPath);
        var roundTripAmount = saveContext == QuantityItemSaveContext.Raid
            ? RaidInventorySaveEditor.CountAmount(roundTripRoot, item)
            : QuantityItemSaveEditor.CountAmount(roundTripRoot, item);
        if (roundTripAmount != targetAmount || !JsonNode.DeepEquals(updatedRoot, roundTripRoot))
        {
            throw new InvalidDataException(
                $"DSON roundtrip validation failed for '{item.DisplayId}': " +
                $"expectedAmount={targetAmount}, actualAmount={roundTripAmount}, " +
                $"fullDocumentMatch={JsonNode.DeepEquals(updatedRoot, roundTripRoot)}.");
        }

        if (sourceWasDson && !RevisionMatches(sourceCopy, encodedPath))
        {
            throw new InvalidDataException("Encoded save did not preserve the original DSON revision bytes.");
        }

        var prepared = new PreparedQuantityItemEdit(
            sessionId,
            profile,
            item,
            preview,
            new PreparedQuantityItemContentGuard(
                activeContent.SourceGameSha256,
                activeContent.Sources.ToArray(),
                item.SourcePath,
                itemSourceSha256,
                manifestFingerprints)
            {
                SaveContext = saveContext,
                RaidInventoryCapacity = raidStorage?.MaxSlots,
                RaidStorageSourcePath = raidStorage?.SourcePath ?? string.Empty,
                RaidStorageSourceSha256 = raidStorage?.SourceSha256 ?? string.Empty
            },
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
        PreparedQuantityItemEdit prepared,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureGameIsNotRunning();
        ValidateProfile(prepared.Profile);
        ValidateQuantityItemSaveContext(prepared.Profile, prepared.ContentGuard.SaveContext);
        ValidateQuantityItemContentGuard(prepared);

        var targetPath = GetQuantityItemTargetPath(prepared.Profile, prepared.ContentGuard.SaveContext);
        var currentHash = ComputeSha256(targetPath);
        if (!currentHash.Equals(prepared.OriginalSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The live target save changed after preview. Prepare a new preview instead of overwriting newer game data.");
        }

        var encodedHash = ComputeSha256(prepared.EncodedPath);
        if (!encodedHash.Equals(prepared.EncodedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The prepared encoded save changed after validation.");
        }

        var backupDirectory = CreateBackup(prepared.Profile, prepared);
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
            contentLocks = OpenQuantityItemContentLocks(prepared);
            ValidateQuantityItemSaveContext(prepared.Profile, prepared.ContentGuard.SaveContext);
            ValidateQuantityItemContentGuard(prepared);
            replacement.Replace();
            ValidateQuantityItemContentGuard(prepared);
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
                        $"物品写入失败；{GuardedSaveReplacement.DescribeRecovery(recovery)}。" +
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
                        $"物品写入失败，自动恢复未能完成。完整备份：{backupDirectory}；实际被替换版本：{replacement.DisplacedPath}",
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

    private string CreateBackup(SaveProfile profile, PreparedQuantityItemEdit prepared)
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
            itemId = prepared.Item.DisplayId,
            storage = prepared.Item.StorageKind.ToString(),
            prepared.Preview.ExistingAmount,
            prepared.Preview.TargetAmount,
            files
        });
        return backupDirectory;
    }

    private static string GetQuantityItemTargetPath(
        SaveProfile profile,
        QuantityItemSaveContext saveContext) =>
        saveContext == QuantityItemSaveContext.Raid
            ? profile.RaidSavePath
            : profile.EstateSavePath;

    private static void ValidateQuantityItemSaveContext(
        SaveProfile profile,
        QuantityItemSaveContext saveContext)
    {
        var raidExists = File.Exists(profile.RaidSavePath);
        if (saveContext == QuantityItemSaveContext.Raid && !raidExists)
        {
            throw new InvalidOperationException(
                "当前档案已不在副本中；请重新加载内容目录，程序不会把背包修改误写到小镇存档。");
        }

    }

    private static void ValidateQuantityItemContentGuard(PreparedQuantityItemEdit prepared)
    {
        ValidateQuantityItemSaveContext(prepared.Profile, prepared.ContentGuard.SaveContext);
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
        var currentDefinitions = QuantityItemCatalog.LoadDefinitions(
            currentSnapshot,
            prepared.ContentGuard.SaveContext);
        if (prepared.ContentGuard.SaveContext == QuantityItemSaveContext.Raid)
        {
            var currentStorage = RaidInventoryStorageCatalog.Load(currentSnapshot).Storage;
            if (currentStorage is null ||
                currentStorage.MaxSlots != prepared.ContentGuard.RaidInventoryCapacity ||
                !Path.GetFullPath(currentStorage.SourcePath).Equals(
                    Path.GetFullPath(prepared.ContentGuard.RaidStorageSourcePath),
                    StringComparison.OrdinalIgnoreCase) ||
                !currentStorage.SourceSha256.Equals(
                    prepared.ContentGuard.RaidStorageSourceSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "The active expedition inventory capacity changed after preview; prepare a new preview.");
            }
        }

        var currentItem = currentDefinitions.SingleOrDefault(definition =>
            definition.CatalogKey.Equals(prepared.Item.CatalogKey, StringComparison.OrdinalIgnoreCase));
        if (prepared.Item.IsSaveOnly)
        {
            if (currentItem is not null)
            {
                throw new InvalidOperationException(
                    "The selected save-only item gained an active content definition after preview; prepare a new preview.");
            }

            return;
        }

        if (currentItem is null ||
            currentItem.HasProviderConflict ||
            !QuantityItemDefinitionMatches(currentItem, prepared.Item) ||
            !Path.GetFullPath(currentItem.SourcePath).Equals(
                Path.GetFullPath(prepared.ContentGuard.ItemSourcePath),
                StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(currentItem.SourcePath) ||
            !ComputeSha256(currentItem.SourcePath).Equals(
                prepared.ContentGuard.ItemSourceSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The selected quantity-item definition changed after preview; prepare a new preview.");
        }
    }

    private static bool QuantityItemDefinitionMatches(
        QuantityItemDefinition left,
        QuantityItemDefinition right)
    {
        return left.CatalogKey.Equals(right.CatalogKey, StringComparison.OrdinalIgnoreCase) &&
               left.InventoryType.Equals(right.InventoryType, StringComparison.OrdinalIgnoreCase) &&
               left.ItemId.Equals(right.ItemId, StringComparison.OrdinalIgnoreCase) &&
               left.StorageKind == right.StorageKind &&
               left.BaseStackLimit == right.BaseStackLimit &&
               left.EstateCanBeProvision == right.EstateCanBeProvision &&
               left.Source.Equals(right.Source, StringComparison.OrdinalIgnoreCase) &&
               Path.GetFullPath(left.SourcePath).Equals(
                   Path.GetFullPath(right.SourcePath),
                   StringComparison.OrdinalIgnoreCase) &&
               left.HasProviderConflict == right.HasProviderConflict &&
               left.AllSources.SequenceEqual(right.AllSources, StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<FileStream> OpenQuantityItemContentLocks(PreparedQuantityItemEdit prepared)
    {
        var paths = new[]
            {
                Path.Combine(prepared.Profile.ProfileDirectory, "persist.game.json")
            }
            .Concat(string.IsNullOrWhiteSpace(prepared.ContentGuard.ItemSourcePath)
                ? []
                : [prepared.ContentGuard.ItemSourcePath])
            .Concat(string.IsNullOrWhiteSpace(prepared.ContentGuard.RaidStorageSourcePath)
                ? []
                : [prepared.ContentGuard.RaidStorageSourcePath])
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
