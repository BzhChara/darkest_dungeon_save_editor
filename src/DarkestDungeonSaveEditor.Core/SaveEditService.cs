using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DarkestDungeonSaveEditor.Core;

public sealed class SaveEditService
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private readonly DsonSaveCodec _codec;
    private readonly SaveEditorLocations _locations;

    public SaveEditService(DsonSaveCodec codec, SaveEditorLocations? locations = null)
    {
        _codec = codec;
        _locations = locations ?? SaveEditorLocations.CreateDefault();
    }

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

        if (trinket.IsStateful)
        {
            throw new InvalidOperationException(
                $"Stateful trinket '{trinket.Id}' is read-only in phase 1: {string.Join(", ", trinket.StatefulFields)}");
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
            currentTrinket.IsStateful ||
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

    public async Task<PreparedStagecoachHeroEdit> PrepareStagecoachHeroEditAsync(
        SaveProfile profile,
        GeneratedStagecoachHeroCandidate generatedCandidate,
        HeroClassCatalogResult expectedCatalog,
        ActiveContentSnapshot activeContent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(generatedCandidate);
        ArgumentNullException.ThrowIfNull(expectedCatalog);
        ArgumentNullException.ThrowIfNull(activeContent);
        _codec.ValidateAvailability();
        ValidateProfile(profile);
        EnsureNoUnfinishedStagecoachTransaction(profile);
        ValidateActiveContentSnapshot(profile, activeContent);
        var manifestFingerprints = CaptureManifestFingerprints(activeContent.Sources);
        var expectedCatalogSha256 = ComputeHeroCatalogSha256(expectedCatalog);
        var currentCatalog = HeroClassCatalog.Load(activeContent);
        ValidateManifestFingerprints(
            manifestFingerprints,
            "while the hero catalog was being checked; reload the content catalog");
        if (!ComputeHeroCatalogSha256(currentCatalog).Equals(
                expectedCatalogSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The active hero, quirk, level, or upgrade templates changed after the catalog was loaded; " +
                "reload the content catalog.");
        }

        ValidateGeneratedStagecoachCandidate(currentCatalog, generatedCandidate);
        var contentGuard = new PreparedStagecoachContentGuard(
            activeContent.SourceGameSha256,
            activeContent.GameMode,
            activeContent.Sources.ToArray(),
            expectedCatalogSha256,
            manifestFingerprints);

        var townTargetPath = GetProfileSavePath(profile, "persist.town.json");
        var rosterTargetPath = GetProfileSavePath(profile, "persist.roster.json");
        var upgradesTargetPath = GetProfileSavePath(profile, "persist.upgrades.json");
        ValidateRequiredSaveFile(townTargetPath, "persist.town.json");
        ValidateRequiredSaveFile(rosterTargetPath, "persist.roster.json");
        ValidateRequiredSaveFile(upgradesTargetPath, "persist.upgrades.json");

        var townOriginalHash = ComputeSha256(townTargetPath);
        var rosterOriginalHash = ComputeSha256(rosterTargetPath);
        var upgradesOriginalHash = ComputeSha256(upgradesTargetPath);
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

        var townSourceCopy = Path.Combine(sourceDirectory, "persist.town.json");
        var rosterSourceCopy = Path.Combine(sourceDirectory, "persist.roster.json");
        var upgradesSourceCopy = Path.Combine(sourceDirectory, "persist.upgrades.json");
        var townDecodedPath = Path.Combine(decodedDirectory, "persist.town.json");
        var rosterDecodedPath = Path.Combine(decodedDirectory, "persist.roster.json");
        var upgradesDecodedPath = Path.Combine(decodedDirectory, "persist.upgrades.json");
        var townProposedPath = Path.Combine(decodedDirectory, "persist.town.proposed.json");
        var rosterProposedPath = Path.Combine(decodedDirectory, "persist.roster.proposed.json");
        var upgradesProposedPath = Path.Combine(decodedDirectory, "persist.upgrades.proposed.json");
        var townEncodedPath = Path.Combine(encodedDirectory, "persist.town.json");
        var rosterEncodedPath = Path.Combine(encodedDirectory, "persist.roster.json");
        var upgradesEncodedPath = Path.Combine(encodedDirectory, "persist.upgrades.json");
        var townRoundTripPath = Path.Combine(roundTripDirectory, "persist.town.json");
        var rosterRoundTripPath = Path.Combine(roundTripDirectory, "persist.roster.json");
        var upgradesRoundTripPath = Path.Combine(roundTripDirectory, "persist.upgrades.json");

        File.Copy(townTargetPath, townSourceCopy, overwrite: false);
        File.Copy(rosterTargetPath, rosterSourceCopy, overwrite: false);
        File.Copy(upgradesTargetPath, upgradesSourceCopy, overwrite: false);
        ValidateUnchangedDuringCopy(townTargetPath, townSourceCopy, townOriginalHash, "town");
        ValidateUnchangedDuringCopy(rosterTargetPath, rosterSourceCopy, rosterOriginalHash, "roster");
        ValidateUnchangedDuringCopy(
            upgradesTargetPath,
            upgradesSourceCopy,
            upgradesOriginalHash,
            "upgrades");

        await _codec.DecodeAsync(townSourceCopy, townDecodedPath, cancellationToken).ConfigureAwait(false);
        await _codec.DecodeAsync(rosterSourceCopy, rosterDecodedPath, cancellationToken).ConfigureAwait(false);
        await _codec.DecodeAsync(upgradesSourceCopy, upgradesDecodedPath, cancellationToken).ConfigureAwait(false);
        var townRoot = JsonSupport.ReadObject(townDecodedPath);
        var rosterRoot = JsonSupport.ReadObject(rosterDecodedPath);
        var upgradesRoot = JsonSupport.ReadObject(upgradesDecodedPath);
        var quirkLimits = StagecoachHeroSaveEditor.AnalyzeQuirkLimits(
            townRoot,
            rosterRoot,
            generatedCandidate.Candidate,
            currentCatalog.InitialQuirks);
        var (updatedTown, updatedRoster, updatedUpgrades, mutationPreview) = StagecoachHeroSaveEditor.AddCandidate(
            townRoot,
            rosterRoot,
            upgradesRoot,
            generatedCandidate.Candidate,
            generatedCandidate.UpgradePurchases);
        var preview = mutationPreview with { QuirkLimits = quirkLimits };
        JsonSupport.WriteObject(townProposedPath, updatedTown);
        JsonSupport.WriteObject(rosterProposedPath, updatedRoster);
        JsonSupport.WriteObject(upgradesProposedPath, updatedUpgrades);

        await _codec.EncodeAsync(
            upgradesProposedPath,
            upgradesEncodedPath,
            upgradesSourceCopy,
            cancellationToken).ConfigureAwait(false);
        await _codec.EncodeAsync(
            rosterProposedPath,
            rosterEncodedPath,
            rosterSourceCopy,
            cancellationToken).ConfigureAwait(false);
        await _codec.EncodeAsync(
            townProposedPath,
            townEncodedPath,
            townSourceCopy,
            cancellationToken).ConfigureAwait(false);
        await _codec.DecodeAsync(
            upgradesEncodedPath,
            upgradesRoundTripPath,
            cancellationToken).ConfigureAwait(false);
        await _codec.DecodeAsync(rosterEncodedPath, rosterRoundTripPath, cancellationToken).ConfigureAwait(false);
        await _codec.DecodeAsync(townEncodedPath, townRoundTripPath, cancellationToken).ConfigureAwait(false);

        var upgradesRoundTripRoot = JsonSupport.ReadObject(upgradesRoundTripPath);
        var rosterRoundTripRoot = JsonSupport.ReadObject(rosterRoundTripPath);
        var townRoundTripRoot = JsonSupport.ReadObject(townRoundTripPath);
        if (!JsonNode.DeepEquals(updatedUpgrades, upgradesRoundTripRoot) ||
            !JsonNode.DeepEquals(updatedRoster, rosterRoundTripRoot) ||
            !JsonNode.DeepEquals(updatedTown, townRoundTripRoot))
        {
            throw new InvalidDataException(
                "DSON roundtrip validation failed for the prepared stagecoach town/roster/upgrades edit.");
        }

        var townWasDson = DsonSaveCodec.IsDson(townSourceCopy);
        var rosterWasDson = DsonSaveCodec.IsDson(rosterSourceCopy);
        var upgradesWasDson = DsonSaveCodec.IsDson(upgradesSourceCopy);
        if ((townWasDson && !RevisionMatches(townSourceCopy, townEncodedPath)) ||
            (rosterWasDson && !RevisionMatches(rosterSourceCopy, rosterEncodedPath)) ||
            (upgradesWasDson && !RevisionMatches(upgradesSourceCopy, upgradesEncodedPath)))
        {
            throw new InvalidDataException(
                "An encoded stagecoach save did not preserve its original DSON revision bytes.");
        }

        ValidateStagecoachContentGuard(profile, contentGuard);

        var prepared = new PreparedStagecoachHeroEdit(
            sessionId,
            profile,
            preview,
            contentGuard,
            workspace,
            new PreparedSaveFile(
                "persist.town.json",
                townTargetPath,
                townSourceCopy,
                townProposedPath,
                townEncodedPath,
                townRoundTripPath,
                townOriginalHash,
                ComputeSha256(townEncodedPath),
                townWasDson),
            new PreparedSaveFile(
                "persist.roster.json",
                rosterTargetPath,
                rosterSourceCopy,
                rosterProposedPath,
                rosterEncodedPath,
                rosterRoundTripPath,
                rosterOriginalHash,
                ComputeSha256(rosterEncodedPath),
                rosterWasDson),
            new PreparedSaveFile(
                "persist.upgrades.json",
                upgradesTargetPath,
                upgradesSourceCopy,
                upgradesProposedPath,
                upgradesEncodedPath,
                upgradesRoundTripPath,
                upgradesOriginalHash,
                ComputeSha256(upgradesEncodedPath),
                upgradesWasDson),
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
        var targetDirectory = Path.GetDirectoryName(targetPath)!;
        var temporaryTarget = Path.Combine(
            targetDirectory,
            $".{Path.GetFileName(targetPath)}.ddse-{Guid.NewGuid():N}.tmp");
        var backupTargetPath = Path.Combine(backupDirectory, Path.GetFileName(targetPath));
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
            contentLocks = OpenQuantityItemContentLocks(prepared);
            File.Copy(prepared.EncodedPath, temporaryTarget, overwrite: false);
            ValidateQuantityItemSaveContext(prepared.Profile, prepared.ContentGuard.SaveContext);
            ValidateQuantityItemContentGuard(prepared);
            using (var liveTargetLock = new FileStream(
                       targetPath,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read | FileShare.Delete,
                       bufferSize: 4096,
                       FileOptions.SequentialScan))
            {
                var hashBeforeReplace = ComputeSha256(liveTargetLock);
                if (!hashBeforeReplace.Equals(prepared.OriginalSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "The live estate save changed immediately before replacement. No save edit was applied.");
                }

                ValidateQuantityItemContentGuard(prepared);
                File.Replace(temporaryTarget, targetPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
                replacementSucceeded = true;
                ValidateQuantityItemContentGuard(prepared);
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
            if (replacementSucceeded && File.Exists(backupTargetPath))
            {
                try
                {
                    var restoreTemporary = Path.Combine(
                        targetDirectory,
                        $".{Path.GetFileName(targetPath)}.ddse-restore-{Guid.NewGuid():N}.tmp");
                    File.Copy(backupTargetPath, restoreTemporary, overwrite: false);
                    File.Replace(restoreTemporary, targetPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
                    throw new InvalidOperationException(
                        $"Save commit failed and the original target file was restored from '{backupTargetPath}'.",
                        commitError);
                }
                catch (InvalidOperationException)
                {
                    throw;
                }
                catch (Exception restoreError)
                {
                    throw new AggregateException(
                        $"Save commit failed and automatic restore also failed. Backup: {backupTargetPath}",
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
        // have both been written. Any failure still rolls every replaced file back from backup.
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
                ReplacePreparedFile(file, () => replacedFiles.Add(file));
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
                var finalHash = ComputeSha256(file.TargetPath);
                if (!finalHash.Equals(file.EncodedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new IOException(
                        $"The final paired-save check found that {file.FileName} changed after replacement.");
                }

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
                RestoreReplacedStagecoachFiles(files, replacedFiles, backupDirectory);
                TryWriteStagecoachTransactionState(
                    backupDirectory,
                    prepared,
                    "restored",
                    replacedFiles,
                    commitError.Message);
                throw new InvalidOperationException(
                    $"Stagecoach save commit failed and every replaced file was restored from '{backupDirectory}'.",
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

    private string CreateBackup(SaveProfile profile, PreparedStagecoachHeroEdit prepared)
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
            operation = "add-stagecoach-hero",
            createdAtUtc = DateTime.UtcNow,
            profile.ProfileId,
            profile.SteamUserId,
            profile.ProfileDirectory,
            prepared.SessionId,
            prepared.Preview.CandidateGuid,
            prepared.Preview.HeroClass,
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

    private static void ReplacePreparedFile(PreparedSaveFile file, Action replacementOccurred)
    {
        var targetDirectory = Path.GetDirectoryName(file.TargetPath)
            ?? throw new InvalidOperationException($"Save target has no directory: {file.TargetPath}");
        var temporaryTarget = Path.Combine(
            targetDirectory,
            $".{Path.GetFileName(file.TargetPath)}.ddse-{Guid.NewGuid():N}.tmp");
        try
        {
            File.Copy(file.EncodedPath, temporaryTarget, overwrite: false);
            var hashBeforeReplace = ComputeSha256(file.TargetPath);
            if (!hashBeforeReplace.Equals(file.OriginalSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"The live {file.FileName} save changed immediately before replacement.");
            }

            File.Replace(
                temporaryTarget,
                file.TargetPath,
                destinationBackupFileName: null,
                ignoreMetadataErrors: true);
            replacementOccurred();
            var finalHash = ComputeSha256(file.TargetPath);
            if (!finalHash.Equals(file.EncodedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException(
                    $"The committed {file.FileName} hash does not match the validated encoded save.");
            }
        }
        finally
        {
            TryDeleteFile(temporaryTarget);
        }
    }

    private static void RestoreReplacedStagecoachFiles(
        IReadOnlyList<PreparedSaveFile> allFiles,
        IReadOnlyList<PreparedSaveFile> replacedFiles,
        string backupDirectory)
    {
        var restoreErrors = new List<Exception>();
        foreach (var file in replacedFiles.Reverse())
        {
            var backupPath = Path.Combine(backupDirectory, file.FileName);
            var targetDirectory = Path.GetDirectoryName(file.TargetPath)
                ?? throw new InvalidOperationException($"Save target has no directory: {file.TargetPath}");
            var temporaryTarget = Path.Combine(
                targetDirectory,
                $".{Path.GetFileName(file.TargetPath)}.ddse-restore-{Guid.NewGuid():N}.tmp");
            try
            {
                if (!File.Exists(backupPath) ||
                    !ComputeSha256(backupPath).Equals(file.OriginalSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"Backup for {file.FileName} is missing or does not match the original hash.");
                }

                File.Copy(backupPath, temporaryTarget, overwrite: false);
                File.Replace(
                    temporaryTarget,
                    file.TargetPath,
                    destinationBackupFileName: null,
                    ignoreMetadataErrors: true);
                if (!ComputeSha256(file.TargetPath).Equals(file.OriginalSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new IOException($"Restored {file.FileName} does not match its original hash.");
                }
            }
            catch (Exception error)
            {
                restoreErrors.Add(error);
            }
            finally
            {
                TryDeleteFile(temporaryTarget);
            }
        }

        foreach (var file in allFiles)
        {
            try
            {
                if (!ComputeSha256(file.TargetPath).Equals(file.OriginalSha256, StringComparison.OrdinalIgnoreCase))
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

    private static void ValidateProfile(SaveProfile profile)
    {
        var expectedEstatePath = Path.GetFullPath(Path.Combine(profile.ProfileDirectory, "persist.estate.json"));
        if (!expectedEstatePath.Equals(Path.GetFullPath(profile.EstateSavePath), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Estate save path is outside the selected profile directory.");
        }

        if (!File.Exists(expectedEstatePath))
        {
            throw new FileNotFoundException("persist.estate.json was not found in the selected profile.", expectedEstatePath);
        }
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

        if (saveContext == QuantityItemSaveContext.Town && raidExists)
        {
            throw new InvalidOperationException(
                "当前档案已经进入副本；请重新加载内容目录，程序将切换为当前背包修改模式。");
        }
    }

    private static void ValidateActiveContentSnapshot(
        SaveProfile profile,
        ActiveContentSnapshot activeContent)
    {
        if (!Path.GetFullPath(activeContent.Profile.ProfileDirectory)
                .Equals(Path.GetFullPath(profile.ProfileDirectory), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The active content snapshot belongs to a different save profile.");
        }

        var gameSavePath = Path.Combine(profile.ProfileDirectory, "persist.game.json");
        if (!File.Exists(gameSavePath) ||
            !ComputeSha256(gameSavePath).Equals(activeContent.SourceGameSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The active Mod/DLC configuration changed after the content catalog was loaded; reload the catalog.");
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
            currentTrinket.IsStateful ||
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

    private static void ValidateStagecoachContentGuard(
        SaveProfile profile,
        PreparedStagecoachContentGuard contentGuard)
    {
        var gameSavePath = Path.Combine(profile.ProfileDirectory, "persist.game.json");
        if (!File.Exists(gameSavePath) ||
            !ComputeSha256(gameSavePath).Equals(
                contentGuard.SourceGameSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The active Mod/DLC configuration changed after the hero preview; prepare a new preview.");
        }

        ValidateManifestFingerprints(
            contentGuard.ManifestFingerprints,
            "after the hero preview; prepare a new preview");
        var currentSnapshot = new ActiveContentSnapshot(
            profile,
            contentGuard.GameMode,
            contentGuard.Sources,
            [],
            string.Empty,
            string.Empty,
            0,
            contentGuard.SourceGameSha256);
        var currentCatalog = HeroClassCatalog.Load(currentSnapshot);
        ValidateManifestFingerprints(
            contentGuard.ManifestFingerprints,
            "while the hero preview was being revalidated; prepare a new preview");
        if (!ComputeHeroCatalogSha256(currentCatalog).Equals(
                contentGuard.HeroCatalogSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The active hero, quirk, level, or upgrade templates changed after the hero preview; " +
                "prepare a new preview.");
        }
    }

    private static void ValidateGeneratedStagecoachCandidate(
        HeroClassCatalogResult catalog,
        GeneratedStagecoachHeroCandidate generatedCandidate)
    {
        var heroClassId = JsonSupport.ReadString(generatedCandidate.Candidate, "heroClass");
        var heroClass = catalog.HeroClasses.SingleOrDefault(item =>
            item.Id.Equals(heroClassId, StringComparison.Ordinal));
        if (heroClass is null ||
            !generatedCandidate.Preview.HeroClass.Equals(heroClassId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The generated stagecoach candidate does not match an exact active hero class definition.");
        }

        var levelProfile = heroClass.LevelProfiles.SingleOrDefault(profile =>
            profile.ResolveLevel == generatedCandidate.Preview.ResolveLevel);
        if (levelProfile is null ||
            levelProfile.ResolveXp != generatedCandidate.Preview.ResolveXp ||
            levelProfile.WeaponRank != generatedCandidate.Preview.WeaponRank ||
            levelProfile.ArmourRank != generatedCandidate.Preview.ArmourRank)
        {
            throw new InvalidOperationException(
                "The generated stagecoach candidate no longer matches the active hero level template.");
        }

        var expectedPurchases = StagecoachHeroCandidateFactory.BuildUpgradePurchases(
            heroClass,
            levelProfile.ResolveLevel);
        if (!expectedPurchases.SequenceEqual(generatedCandidate.UpgradePurchases))
        {
            throw new InvalidOperationException(
                "The generated stagecoach candidate no longer matches the active all-skill upgrade plan.");
        }
    }

    private static string ComputeHeroCatalogSha256(HeroClassCatalogResult catalog)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            catalog.GameMode,
            catalog.ResolveLevelThresholds,
            catalog.HeroClasses,
            catalog.RecruitEvents,
            catalog.InitialQuirks,
            catalog.HeroNames
        }, JsonSupport.SerializerOptions);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
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
               left.HasProviderConflict == right.HasProviderConflict &&
               left.AllSources.SequenceEqual(right.AllSources, StringComparer.OrdinalIgnoreCase) &&
               left.LocalizedName == right.LocalizedName;
    }

    private static IReadOnlyList<PreparedContentFileFingerprint> CaptureManifestFingerprints(
        IReadOnlyList<ActiveContentSource> sources)
    {
        return sources
            .Where(source => source.Kind is "workshop" or "local")
            .Select(source => Path.GetFullPath(Path.Combine(source.Directory, "modfiles.txt")))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => File.Exists(path)
                ? new PreparedContentFileFingerprint(path, true, ComputeSha256(path))
                : new PreparedContentFileFingerprint(path, false, string.Empty))
            .ToArray();
    }

    private static void ValidateManifestFingerprints(
        IReadOnlyList<PreparedContentFileFingerprint> fingerprints,
        string changeContext)
    {
        foreach (var fingerprint in fingerprints)
        {
            var exists = File.Exists(fingerprint.Path);
            if (exists != fingerprint.Exists ||
                (exists && !ComputeSha256(fingerprint.Path).Equals(
                    fingerprint.Sha256,
                    StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    $"An active Mod manifest changed {changeContext}: {fingerprint.Path}");
            }
        }
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

    private static void EnsureGameIsNotRunning()
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
                if (processes.Length > 0)
                {
                    throw new InvalidOperationException(
                        "Darkest Dungeon is running. Close the game before applying a save edit.");
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
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
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
}
