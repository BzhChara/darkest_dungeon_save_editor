using System.Diagnostics;
using System.Globalization;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DarkestDungeonSaveEditor.Core;

public sealed record ManagedBattleEncounterBridgeResult(
    string PackageDirectory,
    string ProjectTitle,
    string ManifestPath,
    string MashFilePath,
    int MashType,
    int MashIndex,
    IReadOnlyList<string> MonsterIds,
    bool EncounterWasAdded,
    bool ProfileConfigurationChanged,
    string? ProfileBackupDirectory,
    ActiveContentSnapshot ActiveContent,
    BattleEncounterCatalogResult Catalog,
    BattleEncounterDefinition DirectEncounter);

/// <summary>
/// Maintains one package per save profile with dedicated files per region/difficulty. The game still consumes
/// numeric mash indexes, but callers never have to install, reorder, or retire one-off Mods.
/// </summary>
public sealed partial class ManagedBattleEncounterBridgeService
{
    public const string ManifestFileName = "ddse-managed-encounter-bridge.json";
    public const int ManifestVersion = 4;
    private const string ProjectTitlePrefix = "DDSE_Managed_Encounter_Bridge";

    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly DsonSaveCodec _codec;
    private readonly SaveEditorLocations _locations;
    private readonly Func<bool> _gameRunningProbe;
    internal Action<string>? BeforeGameReplace { get; set; }
    internal Action<string>? AfterGameReplace { get; set; }

    public ManagedBattleEncounterBridgeService(
        DsonSaveCodec codec,
        SaveEditorLocations? locations = null,
        Func<bool>? gameRunningProbe = null)
    {
        _codec = codec ?? throw new ArgumentNullException(nameof(codec));
        _locations = locations ?? SaveEditorLocations.CreateDefault();
        _gameRunningProbe = gameRunningProbe ?? IsGameRunning;
    }

    public async Task<ManagedBattleEncounterBridgeResult> EnsureEncounterAsync(
        SaveProfile profile,
        BattleMapSnapshot snapshot,
        ActiveContentSnapshot activeContent,
        BattleEncounterCatalogResult catalog,
        BattleEncounterDefinition encounter,
        string gameDirectory,
        string? workshopDirectory,
        string? localModDirectory,
        CancellationToken cancellationToken = default,
        BattleMapPlacementTarget? placementTarget = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(activeContent);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(encounter);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDirectory);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureGameIsNotRunning();
        ValidateInputs(profile, snapshot, activeContent, catalog, encounter);
        BattleEncounterCatalog.ValidateBridgeEncounter(encounter);

        var normalizedGameDirectory = Path.GetFullPath(gameDirectory);
        var normalizedWorkshopDirectory = string.IsNullOrWhiteSpace(workshopDirectory)
            ? null
            : Path.GetFullPath(workshopDirectory);
        var normalizedConfiguredLocalDirectory = string.IsNullOrWhiteSpace(localModDirectory)
            ? null
            : Path.GetFullPath(localModDirectory);
        var installRoot = ResolveInstallRoot(
            normalizedGameDirectory,
            normalizedConfiguredLocalDirectory);
        var projectTitle = GetProjectTitle(profile);
        var packageDirectory = Path.Combine(installRoot, GetPackageDirectoryName(profile));
        ValidateManagedPackagePath(installRoot, packageDirectory);
        var oldPackageDirectory = Path.Combine(installRoot,
            $"DDSE_Managed_Encounter_Bridge_{ShortHash($"{profile.SteamUserId}/{profile.ProfileId}")}");
        if (Directory.Exists(oldPackageDirectory))
            throw new InvalidOperationException(EditorText.Get("ManagedBattleEncounterBridgeService_001"));
        foreach (var source in activeContent.Sources.Where(IsManagedBridgeSource))
        {
            if (!Path.GetFullPath(source.Directory).Equals(packageDirectory, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    EditorText.Format("ManagedBattleEncounterBridgeService_002", source.DisplayName) +
                    EditorText.Get("ManagedBattleEncounterBridgeService_003"));
            ValidateManifestIdentity(ReadManifest(Path.Combine(source.Directory, ManifestFileName)), profile, projectTitle);
        }

        var sessionId = $"{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}";
        var workspace = Path.Combine(
            Path.GetFullPath(_locations.WorkspaceDirectory),
            "managed_encounter_bridge",
            sessionId);
        var stagedPackage = Path.Combine(workspace, "package");
        var rollbackPackage = Path.Combine(workspace, "rollback-package");
        Directory.CreateDirectory(workspace);
        using var writeGuard = await BattleMapWriteGuard.LoadAsync(
            profile, snapshot, _codec, Path.Combine(workspace, "map-input"), cancellationToken).ConfigureAwait(false);
        if (!writeGuard.GameSha256.Equals(activeContent.SourceGameSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(EditorText.Get("ManagedBattleEncounterBridgeService_004"));
        if (placementTarget is not null)
            BattleMapSaveEditor.ValidateBattlePlacementTarget(writeGuard.MapDocument, writeGuard.Snapshot,
                placementTarget.AreaId, placementTarget.TileId, encounter.MashType);

        var packageExisted = Directory.Exists(packageDirectory);
        ManagedBridgeManifest manifest;
        if (packageExisted)
        {
            RejectReparsePoint(packageDirectory, EditorText.Get("ManagedBattleEncounterBridgeService_005"));
            CopyDirectory(packageDirectory, stagedPackage);
            CopyDirectory(packageDirectory, rollbackPackage);
            manifest = ReadManifest(Path.Combine(stagedPackage, ManifestFileName));
            ValidateManifestIdentity(manifest, profile, projectTitle);
        }
        else
        {
            Directory.CreateDirectory(stagedPackage);
            manifest = new ManagedBridgeManifest
            {
                Version = ManifestVersion,
                ProjectTitle = projectTitle,
                ProfileId = profile.ProfileId,
                SteamUserId = profile.SteamUserId,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
        }

        var contentFingerprint = ComputeContentFingerprint(activeContent.Sources);
        var table = manifest.Tables.SingleOrDefault(candidate =>
            candidate.DungeonId.Equals(catalog.DungeonId, StringComparison.Ordinal) &&
            candidate.Difficulty == catalog.Difficulty);
        var relativeMashPath = table?.RelativeMashPath ?? AllocateMashPath(
            manifest, stagedPackage, catalog.DungeonId, catalog.Difficulty);
        var tableTarget = BattleEncounterCatalog.ResolveAppendTarget(
            catalog, encounter.MashType, packageDirectory, relativeMashPath);
        var stagedMashPath = Path.Combine(
            stagedPackage,
            tableTarget.RelativeMashPath.Replace('/', Path.DirectorySeparatorChar));
        ManagedBridgeEncounterManifest? existingEntry = null;

        if (table is null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(stagedMashPath)!);
            using (File.Open(stagedMashPath, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
            table = new ManagedBridgeTableManifest
            {
                DungeonId = catalog.DungeonId,
                Difficulty = catalog.Difficulty,
                RelativeMashPath = tableTarget.RelativeMashPath,
                ContentFingerprint = contentFingerprint,
                GeneratedMashSha256 = ComputeSha256(stagedMashPath)
            };
            manifest.Tables.Add(table);
        }
        else
        {
            existingEntry = table.Entries.FirstOrDefault(candidate =>
                candidate.MashType == encounter.MashType &&
                candidate.MonsterIds.SequenceEqual(encounter.MonsterIds, StringComparer.Ordinal));
            ValidateExistingTable(
                table,
                stagedMashPath,
                contentFingerprint,
                requireCurrentContentFingerprint: existingEntry is null);
        }

        var encounterWasAdded = existingEntry is null;
        ManagedBridgeEncounterManifest entry;
        if (existingEntry is not null)
        {
            entry = existingEntry;
        }
        else
        {
            var fileRowIndex = CountStandardRows(stagedMashPath, encounter.MashType);
            // Authored ordinals include native-skipped oversized rows; runtime
            // indexes do not. Keep the two counts independent in the manifest.
            var expectedMashIndex = tableTarget.NextMashIndex;
            if (table.Entries.Any(candidate =>
                    candidate.MashType == encounter.MashType &&
                    candidate.MashIndex == expectedMashIndex))
            {
                throw new InvalidDataException(
                    EditorText.Get("ManagedBattleEncounterBridgeService_006"));
            }

            AppendEncounterRow(stagedMashPath, encounter);
            entry = new ManagedBridgeEncounterManifest
            {
                MashType = encounter.MashType,
                MashIndex = expectedMashIndex,
                FileRowIndex = fileRowIndex,
                MonsterIds = encounter.MonsterIds.ToList(),
                SourceKind = encounter.SourceKind.ToString(),
                SourceLabel = encounter.SourceLabel,
                SourceRelativePath = encounter.SourceRelativePath,
                SourceLine = encounter.SourceLine,
                SourceRecordIndex = encounter.SourceRecordIndex,
                OriginDungeonId = encounter.OriginDungeonId,
                OriginDifficulty = encounter.OriginDifficulty,
                RoamingId = encounter.RoamingId,
                Classification = encounter.Classification.ToString(),
                AddedAtUtc = DateTime.UtcNow
            };
            table.Entries.Add(entry);
            table.GeneratedMashSha256 = ComputeSha256(stagedMashPath);
            manifest.UpdatedAtUtc = DateTime.UtcNow;
        }

        WriteManagedPackage(stagedPackage, packageDirectory, manifest);
        // Resolve the staged overlay before changing the installed package or
        // activating it in the save. Local row ordinals and runtime indexes
        // differ when another effective file contributes the same mash type.
        var stagedSources = activeContent.Sources.Where(source => !Path.GetFullPath(source.Directory)
                .Equals(packageDirectory, StringComparison.OrdinalIgnoreCase)).ToList();
        var stagedOrder = checked(stagedSources.Where(source => source.Kind is "workshop" or "local")
            .Select(source => source.LoadOrder).DefaultIfEmpty(1000).Min() - 1);
        stagedSources.Add(new ActiveContentSource($"local:{projectTitle}", projectTitle, "local", stagedPackage, stagedOrder));
        var stagedCatalog = BattleEncounterCatalog.Load(activeContent with { Sources = stagedSources }, snapshot);
        BattleEncounterCatalog.ValidateExistingIndexes(catalog, stagedCatalog);
        ValidateManifestIndexes(manifest, stagedPackage, stagedCatalog);
        var gameUpdate = await PrepareGameConfigurationAsync(
            profile,
            activeContent,
            projectTitle,
            workspace,
            cancellationToken).ConfigureAwait(false);
        var packageChanged = !packageExisted || !DirectoriesHaveSameFiles(packageDirectory, stagedPackage);
        var packageCommitted = false;
        using var gameReplacement = gameUpdate is null ? null : new GuardedSaveReplacement(
            gameUpdate.TargetPath, gameUpdate.EncodedPath, gameUpdate.OriginalSha256,
            gameUpdate.FinalSha256, BeforeGameReplace, AfterGameReplace);
        string? profileBackupDirectory = null;
        FileStream? recoveryGameGuard = null;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureGameIsNotRunning();
            BattleEncounterCatalog.ValidateBridgeEncounter(encounter);

            if (gameUpdate is not null)
            {
                profileBackupDirectory = CreateProfileBackup(profile, sessionId, activeContent.SourceGameSha256);
                writeGuard.ReleaseGameLock();
            }

            if (packageChanged)
            {
                packageCommitted = true;
                ApplyStagedPackage(stagedPackage, packageDirectory);
            }

            if (gameUpdate is not null)
            {
                EnsureGameIsNotRunning();
                gameReplacement!.Replace();
            }

            var resolvedContent = await ActiveContentResolver.ResolveAsync(
                    profile,
                    normalizedGameDirectory,
                    normalizedWorkshopDirectory,
                    normalizedConfiguredLocalDirectory,
                    _codec,
                    _locations.WorkspaceDirectory,
                    cancellationToken)
                .ConfigureAwait(false);
            var resolvedCatalog = BattleEncounterCatalog.Load(resolvedContent, snapshot);
            BattleEncounterCatalog.ValidateExistingIndexes(catalog, resolvedCatalog);
            ValidateManifestIndexes(manifest, packageDirectory, resolvedCatalog);
            var directEncounter = resolvedCatalog.DirectEncounters.SingleOrDefault(candidate =>
                candidate.MashType == entry.MashType &&
                candidate.MashIndex == entry.MashIndex &&
                candidate.MonsterIds.SequenceEqual(entry.MonsterIds, StringComparer.Ordinal) &&
                IsWithinDirectory(candidate.SourcePath, packageDirectory));
            if (directEncounter is null)
            {
                throw new InvalidOperationException(
                    EditorText.Get("ManagedBattleEncounterBridgeService_007"));
            }

            BattleEncounterCatalog.ValidateDirectEncounter(directEncounter);
            gameReplacement?.Complete();
            return new ManagedBattleEncounterBridgeResult(
                packageDirectory,
                projectTitle,
                Path.Combine(packageDirectory, ManifestFileName),
                Path.Combine(
                    packageDirectory,
                    table.RelativeMashPath.Replace('/', Path.DirectorySeparatorChar)),
                entry.MashType,
                entry.MashIndex,
                entry.MonsterIds,
                encounterWasAdded,
                gameUpdate is not null,
                profileBackupDirectory,
                resolvedContent,
                resolvedCatalog,
                directEncounter);
        }
        catch (Exception primaryError)
        {
            var rollbackErrors = new List<Exception>();
            var canRestorePackage = true;
            if (gameUpdate is not null)
            {
                try
                {
                    if (gameReplacement is { HasReplaced: true })
                    {
                        var recovery = gameReplacement.Recover();
                        canRestorePackage = recovery == SaveFileRecovery.RestoredOriginal;
                    }
                    else
                    {
                        recoveryGameGuard = new FileStream(gameUpdate.TargetPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                        canRestorePackage = Convert.ToHexString(SHA256.HashData(recoveryGameGuard))
                            .Equals(gameUpdate.OriginalSha256, StringComparison.OrdinalIgnoreCase);
                    }
                }
                catch (Exception rollbackError)
                {
                    canRestorePackage = false;
                    rollbackErrors.Add(rollbackError);
                }
            }

            if (packageCommitted && canRestorePackage)
            {
                try
                {
                    RestorePackage(packageDirectory, packageExisted ? rollbackPackage : null);
                }
                catch (Exception rollbackError)
                {
                    rollbackErrors.Add(rollbackError);
                }
            }
            else if (packageCommitted)
            {
                rollbackErrors.Add(new IOException(
                    EditorText.Format("ManagedBattleEncounterBridgeService_008", packageDirectory) +
                    EditorText.Format("ManagedBattleEncounterBridgeService_009", rollbackPackage, gameReplacement?.DisplacedPath)));
            }

            if (rollbackErrors.Count > 0)
            {
                throw new AggregateException(
                    EditorText.Format("ManagedBattleEncounterBridgeService_010", profileBackupDirectory, workspace),
                    [primaryError, .. rollbackErrors]);
            }

            throw;
        }
        finally
        {
            recoveryGameGuard?.Dispose();
        }
    }

    public static string GetProjectTitle(SaveProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        // Both fields remain readable and keep different Steam accounts' profile_1
        // packages distinct. Reject lossy/overlong names instead of creating aliases.
        if (SanitizeSegment(profile.ProfileId, 64) != profile.ProfileId ||
            SanitizeSegment(profile.SteamUserId, 32) != profile.SteamUserId)
            throw new InvalidOperationException(EditorText.Get("ManagedBattleEncounterBridgeService_011"));
        var title = $"{ProjectTitlePrefix}（{profile.ProfileId} - {profile.SteamUserId}）";
        if (Utf8NoBom.GetByteCount(title) >= 128)
            throw new InvalidOperationException(EditorText.Get("ManagedBattleEncounterBridgeService_012"));
        return title;
    }

    public static bool IsManagedBridgeSource(ActiveContentSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.DisplayName.StartsWith(ProjectTitlePrefix + "（", StringComparison.Ordinal) ||
               source.DisplayName.StartsWith(
                   "DDSE Managed Encounter Bridge - ",
                   StringComparison.OrdinalIgnoreCase) ||
               File.Exists(Path.Combine(source.Directory, ManifestFileName));
    }

    private static void ValidateInputs(
        SaveProfile profile,
        BattleMapSnapshot snapshot,
        ActiveContentSnapshot activeContent,
        BattleEncounterCatalogResult catalog,
        BattleEncounterDefinition encounter)
    {
        ActiveContentResolver.ValidateSourceBindings(activeContent.Resolution, activeContent.Sources);
        var profileDirectory = Path.GetFullPath(profile.ProfileDirectory);
        if (!Path.GetFullPath(snapshot.ProfileDirectory).Equals(
                profileDirectory,
                StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFullPath(activeContent.Profile.ProfileDirectory).Equals(
                profileDirectory,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(EditorText.Get("ManagedBattleEncounterBridgeService_013"));
        }

        if (!catalog.DungeonId.Equals(snapshot.DungeonId, StringComparison.Ordinal) ||
            catalog.Difficulty != snapshot.Difficulty ||
            !encounter.TableGuard.Fingerprint.Equals(
                catalog.TableGuard.Fingerprint,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(EditorText.Get("ManagedBattleEncounterBridgeService_014"));
        }

        var gameSavePath = Path.Combine(profileDirectory, "persist.game.json");
        if (!File.Exists(gameSavePath) ||
            !ComputeSha256(gameSavePath).Equals(
                activeContent.SourceGameSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(EditorText.Get("ManagedBattleEncounterBridgeService_015"));
        }
    }

    private static void ValidateManifestIndexes(
        ManagedBridgeManifest manifest,
        string packageDirectory,
        BattleEncounterCatalogResult catalog)
    {
        foreach (var table in manifest.Tables.Where(table =>
                     table.DungeonId.Equals(catalog.DungeonId, StringComparison.Ordinal) &&
                     table.Difficulty == catalog.Difficulty))
        {
            var path = Path.GetFullPath(Path.Combine(packageDirectory, table.RelativeMashPath));
            foreach (var entry in table.Entries)
            {
                var row = catalog.Encounters.Where(row =>
                        row.SourceKind == BattleEncounterSourceKind.Standard && row.MashType == entry.MashType &&
                        row.SourcePath.Equals(path, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(row => row.SourceRecordIndex).ElementAtOrDefault(entry.FileRowIndex!.Value);
                if (row is null || row.MashIndex != entry.MashIndex ||
                    !row.MonsterIds.SequenceEqual(entry.MonsterIds, StringComparer.Ordinal))
                    throw new InvalidOperationException(EditorText.Get("ManagedBattleEncounterBridgeService_016"));
            }
        }
    }

    private static void ValidateExistingTable(
        ManagedBridgeTableManifest table,
        string stagedMashPath,
        string contentFingerprint,
        bool requireCurrentContentFingerprint)
    {
        if (requireCurrentContentFingerprint &&
            !table.ContentFingerprint.Equals(contentFingerprint, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                EditorText.Get("ManagedBattleEncounterBridgeService_017") +
                EditorText.Get("ManagedBattleEncounterBridgeService_018"));
        }
        if (!File.Exists(stagedMashPath) ||
            !ComputeSha256(stagedMashPath).Equals(
                table.GeneratedMashSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                EditorText.Get("ManagedBattleEncounterBridgeService_019"));
        }
        if (table.Entries
            .GroupBy(entry => (entry.MashType, entry.MashIndex))
            .Any(group => group.Count() > 1))
        {
            throw new InvalidDataException(EditorText.Get("ManagedBattleEncounterBridgeService_020"));
        }
    }

    private async Task<PreparedManagedGameUpdate?> PrepareGameConfigurationAsync(
        SaveProfile profile,
        ActiveContentSnapshot activeContent,
        string projectTitle,
        string workspace,
        CancellationToken cancellationToken)
    {
        var gamePath = Path.GetFullPath(Path.Combine(profile.ProfileDirectory, "persist.game.json"));
        var originalHash = ComputeSha256(gamePath);
        if (!originalHash.Equals(activeContent.SourceGameSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(EditorText.Get("ManagedBattleEncounterBridgeService_021"));
        }

        var gameWorkspace = Path.Combine(workspace, "game");
        Directory.CreateDirectory(gameWorkspace);
        var sourcePath = Path.Combine(gameWorkspace, "persist.game.source.json");
        var decodedPath = Path.Combine(gameWorkspace, "persist.game.decoded.json");
        var proposedPath = Path.Combine(gameWorkspace, "persist.game.proposed.json");
        var encodedPath = Path.Combine(gameWorkspace, "persist.game.encoded.json");
        var roundTripPath = Path.Combine(gameWorkspace, "persist.game.roundtrip.json");
        File.Copy(gamePath, sourcePath, overwrite: false);
        await _codec.DecodeAsync(sourcePath, decodedPath, cancellationToken).ConfigureAwait(false);
        var root = JsonSupport.ReadObject(decodedPath);
        var baseRoot = JsonSupport.RequireObject(root, "base_root");
        var applied = baseRoot["applied_ugcs_1_0"] as JsonObject ?? new JsonObject();
        var orderedEntries = new List<(int Order, JsonObject Value)>();
        foreach (var pair in applied)
        {
            if (!int.TryParse(pair.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var order) ||
                pair.Value is not JsonObject value)
            {
                throw new InvalidDataException(
                    EditorText.Format("ManagedBattleEncounterBridgeService_022", pair.Key));
            }
            orderedEntries.Add((order, (JsonObject)value.DeepClone()));
        }

        var retainedEntries = orderedEntries
            .OrderBy(entry => entry.Order)
            .Select(entry => entry.Value)
            .Where(entry =>
                !JsonSupport.ReadString(entry, "name").Equals(projectTitle, StringComparison.OrdinalIgnoreCase) &&
                !JsonSupport.ReadString(entry, "name").Equals(
                    $"DDSE Managed Encounter Bridge - {SanitizeSegment(profile.ProfileId, 24)}-{ShortHash($"{profile.SteamUserId}/{profile.ProfileId}")}",
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var updatedApplied = new JsonObject
        {
            ["0"] = new JsonObject
            {
                ["name"] = projectTitle,
                ["source"] = "mod_local_source"
            }
        };
        for (var index = 0; index < retainedEntries.Length; index++)
        {
            updatedApplied[(index + 1).ToString(CultureInfo.InvariantCulture)] = retainedEntries[index];
        }

        if (JsonNode.DeepEquals(applied, updatedApplied))
        {
            return null;
        }

        baseRoot["applied_ugcs_1_0"] = updatedApplied;
        JsonSupport.WriteObject(proposedPath, root);
        var sourceWasDson = DsonSaveCodec.IsDson(sourcePath);
        await _codec.EncodeAsync(
            proposedPath,
            encodedPath,
            sourcePath,
            cancellationToken).ConfigureAwait(false);
        await _codec.DecodeAsync(encodedPath, roundTripPath, cancellationToken).ConfigureAwait(false);
        if (!JsonNode.DeepEquals(root, JsonSupport.ReadObject(roundTripPath)))
        {
            throw new InvalidDataException(
                EditorText.Get("ManagedBattleEncounterBridgeService_023"));
        }
        if (sourceWasDson && !RevisionMatches(sourcePath, encodedPath))
        {
            throw new InvalidDataException(EditorText.Get("ManagedBattleEncounterBridgeService_024"));
        }

        return new PreparedManagedGameUpdate(
            gamePath,
            sourcePath,
            encodedPath,
            originalHash,
            ComputeSha256(encodedPath));
    }

    private string CreateProfileBackup(
        SaveProfile profile,
        string sessionId,
        string expectedGameSha256)
    {
        var backupDirectory = Path.Combine(
            Path.GetFullPath(_locations.BackupDirectory),
            SanitizeSegment(profile.SteamUserId, 48),
            SanitizeSegment(profile.ProfileId, 48),
            $"{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(backupDirectory);
        var files = ProfileSaveFiles.Enumerate(profile.ProfileDirectory)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path =>
            {
                var before = ComputeSha256(path);
                var destination = ProfileSaveFiles.BackupPath(profile.ProfileDirectory, backupDirectory, path);
                File.Copy(path, destination, overwrite: false);
                var backupHash = ComputeSha256(destination);
                var after = ComputeSha256(path);
                if (!before.Equals(backupHash, StringComparison.OrdinalIgnoreCase) ||
                    !after.Equals(backupHash, StringComparison.OrdinalIgnoreCase))
                {
                    throw new IOException(EditorText.Format("BattleMapEditService_029", path));
                }
                return new
                {
                    fileName = Path.GetRelativePath(profile.ProfileDirectory, path),
                    sha256 = backupHash,
                    length = new FileInfo(destination).Length
                };
            })
            .ToArray();
        var gameBackup = Path.Combine(backupDirectory, "persist.game.json");
        if (!File.Exists(gameBackup) ||
            !ComputeSha256(gameBackup).Equals(expectedGameSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException(EditorText.Get("ManagedBattleEncounterBridgeService_025"));
        }

        File.WriteAllText(
            Path.Combine(backupDirectory, "backup-manifest.json"),
            JsonSerializer.Serialize(
                new
                {
                    version = 1,
                    operation = "EnableManagedEncounterBridge",
                    createdAtUtc = DateTime.UtcNow,
                    profile.ProfileId,
                    profile.SteamUserId,
                    profile.ProfileDirectory,
                    sessionId,
                    files
                },
                JsonSupport.SerializerOptions),
            Utf8NoBom);
        return backupDirectory;
    }

    private static void AppendEncounterRow(
        string mashPath,
        BattleEncounterDefinition encounter)
    {
        var existingBytes = File.ReadAllBytes(mashPath);
        var prefix = existingBytes.Length > 0 && existingBytes[^1] is not (byte)'\r' and not (byte)'\n'
            ? Environment.NewLine
            : string.Empty;
        var line = prefix + EncounterBridgeRow.Format(encounter.MashType, encounter.MonsterIds);
        using var output = new FileStream(
            mashPath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.None);
        output.Write(Utf8NoBom.GetBytes(line));
        output.Flush(flushToDisk: true);
    }

    private static int CountStandardRows(string path, int mashType)
    {
        var expectedKind = mashType switch
        {
            0 => "hall",
            1 => "room",
            2 => "boss",
            _ => throw new InvalidOperationException(EditorText.Get("EncounterBridgeRow_001"))
        };
        return NativeDarkestReader.ReadRecords(path)
            .Count(record => record.Kind == expectedKind && NativeDarkestReader.FindValue(record.Body, ".types") >= 0);
    }

    private static void WriteManagedPackage(
        string stagedPackage,
        string finalPackageDirectory,
        ManagedBridgeManifest manifest)
    {
        var projectPath = Path.Combine(stagedPackage, "project.xml");
        var modDataPath = Path.GetFullPath(finalPackageDirectory)
            .Replace('\\', '/')
            .TrimEnd('/') + "/";
        EncounterBridgeBranding.WritePreview(stagedPackage);
        File.WriteAllText(
            projectPath,
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <project>
              <PreviewIconFile>{SecurityElement.Escape(modDataPath + EncounterBridgeBranding.PreviewFileName)}</PreviewIconFile>
              <ItemDescriptionShort>{SecurityElement.Escape(EditorText.Get("ManagedBattleEncounterBridgeService_026"))}</ItemDescriptionShort>
              <ModDataPath>{SecurityElement.Escape(modDataPath)}</ModDataPath>
              <Title>{SecurityElement.Escape(manifest.ProjectTitle)}</Title>
              <Language>english</Language>
              <Visibility>private</Visibility>
              <UploadMode>direct_upload</UploadMode>
              <VersionMajor>0</VersionMajor>
              <VersionMinor>0</VersionMinor>
              <TargetBuild>0</TargetBuild>
              <Tags><Tags>Gameplay Tweaks</Tags></Tags>
              <ItemDescription>{SecurityElement.Escape(EditorText.Get("ManagedBattleEncounterBridgeService_027"))}</ItemDescription>
              <PublishedFileId>0</PublishedFileId>
            </project>
            """,
            Utf8NoBom);
        File.WriteAllText(
            Path.Combine(stagedPackage, ManifestFileName),
            JsonSerializer.Serialize(manifest, ManifestJsonOptions),
            Utf8NoBom);
        var modFiles = manifest.Tables
            .OrderBy(table => table.RelativeMashPath, StringComparer.OrdinalIgnoreCase)
            .Select(table =>
            {
                var path = Path.Combine(
                    stagedPackage,
                    table.RelativeMashPath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(path))
                {
                    throw new InvalidDataException(
                        EditorText.Format("ManagedBattleEncounterBridgeService_028", table.RelativeMashPath));
                }
                return $"{table.RelativeMashPath} {new FileInfo(path).Length}";
            })
            .Append($"{EncounterBridgeBranding.PreviewFileName} {new FileInfo(Path.Combine(stagedPackage, EncounterBridgeBranding.PreviewFileName)).Length}");
        File.WriteAllText(
            Path.Combine(stagedPackage, "modfiles.txt"),
            string.Join(Environment.NewLine, modFiles) + Environment.NewLine,
            Utf8NoBom);
    }

    private static ManagedBridgeManifest ReadManifest(string path)
    {
        if (!File.Exists(path))
        {
            throw new InvalidDataException(
                EditorText.Format("ManagedBattleEncounterBridgeService_029", ManifestFileName));
        }
        try
        {
            return JsonSerializer.Deserialize<ManagedBridgeManifest>(
                       File.ReadAllText(path, Encoding.UTF8),
                       ManifestJsonOptions)
                   ?? throw new InvalidDataException(EditorText.Get("ManagedBattleEncounterBridgeService_030"));
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(EditorText.Get("ManagedBattleEncounterBridgeService_031"), ex);
        }
    }

    private static void ValidateManifestIdentity(
        ManagedBridgeManifest manifest,
        SaveProfile profile,
        string projectTitle)
    {
        if (manifest.Version != ManifestVersion ||
            !string.Equals(manifest.ProjectTitle, projectTitle, StringComparison.Ordinal) ||
            !string.Equals(manifest.ProfileId, profile.ProfileId, StringComparison.Ordinal) ||
            !string.Equals(manifest.SteamUserId, profile.SteamUserId, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                EditorText.Get("ManagedBattleEncounterBridgeService_032"));
        }
        if (manifest.Tables is null || manifest.Tables.Any(table => table is null))
        {
            throw new InvalidDataException(EditorText.Get("ManagedBattleEncounterBridgeService_033"));
        }
        foreach (var table in manifest.Tables)
        {
            if (string.IsNullOrWhiteSpace(table.DungeonId) ||
                table.Difficulty < 0 ||
                table.DungeonId.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '_' and not '-') ||
                !IsSafeRelativePath(table.RelativeMashPath) ||
                !BattleEncounterCatalog.IsDedicatedMashPath(table.RelativeMashPath, table.DungeonId, table.Difficulty) ||
                string.IsNullOrWhiteSpace(table.ContentFingerprint) ||
                string.IsNullOrWhiteSpace(table.GeneratedMashSha256) ||
                table.Entries is null ||
                table.Entries.Any(entry =>
                    entry is null ||
                    entry.MashType is < 0 or > 2 ||
                    entry.MashIndex < 0 ||
                    entry.FileRowIndex is null or < 0 ||
                    entry.MonsterIds is null ||
                    entry.MonsterIds.Count == 0 ||
                    entry.MonsterIds.Any(string.IsNullOrWhiteSpace)))
            {
                throw new InvalidDataException(EditorText.Get("ManagedBattleEncounterBridgeService_034"));
            }
            if (table.Entries
                .GroupBy(entry => $"{entry.MashType}\n{string.Join('\n', entry.MonsterIds)}")
                .Any(group => group.Count() > 1))
            {
                throw new InvalidDataException(EditorText.Get("ManagedBattleEncounterBridgeService_035"));
            }
        }
        if (manifest.Tables.GroupBy(table => (table.DungeonId, table.Difficulty)).Any(group => group.Count() > 1))
            throw new InvalidDataException(EditorText.Get("ManagedBattleEncounterBridgeService_036"));
        if (manifest.Tables
            .GroupBy(
                table => table.RelativeMashPath,
                StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1))
        {
            throw new InvalidDataException(EditorText.Get("ManagedBattleEncounterBridgeService_037"));
        }
    }

    private static string AllocateMashPath(ManagedBridgeManifest manifest, string package, string dungeonId, int difficulty)
    {
        // Region IDs are exact; their physical Windows paths are not. Assign a
        // distinct carrier name on collision and keep that mapping in the table.
        var occupied = manifest.Tables.Select(table => table.RelativeMashPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var number = 0; ; number = checked(number + 1))
        {
            var relative = BattleEncounterCatalog.DedicatedMashPath(dungeonId, difficulty, number);
            var path = Path.Combine(package, relative);
            if (!occupied.Contains(relative) && !File.Exists(path) && !Directory.Exists(path)) return relative;
        }
    }

    private static string ResolveInstallRoot(
        string gameDirectory,
        string? configuredLocalModDirectory)
    {
        var defaultRoot = Path.GetFullPath(Path.Combine(gameDirectory, "mods"));
        var candidate = configuredLocalModDirectory;
        if (candidate is not null && File.Exists(Path.Combine(candidate, "project.xml")))
        {
            candidate = null;
        }
        var root = Path.GetFullPath(candidate ?? defaultRoot);
        if (File.Exists(root))
        {
            throw new IOException(EditorText.Format("ManagedBattleEncounterBridgeService_038", root));
        }
        Directory.CreateDirectory(root);
        RejectReparsePoint(root, EditorText.Get("ManagedBattleEncounterBridgeService_Maintenance_008"));
        return root;
    }

    private static string GetPackageDirectoryName(SaveProfile profile) =>
        GetProjectTitle(profile);

    private static string ComputeContentFingerprint(IReadOnlyList<ActiveContentSource> sources)
    {
        var normalized = sources
            .OrderBy(source => source.LoadOrder)
            .Where(source => !IsAnyGeneratedBridgeSource(source))
            .Select((source, index) => string.Join(
                '|',
                index.ToString(CultureInfo.InvariantCulture),
                source.Kind,
                source.Id,
                source.DisplayName,
                Path.GetFullPath(source.Directory),
                source.VirtualPathPrefix.Replace('\\', '/')));
        return ComputeSha256(Encoding.UTF8.GetBytes(string.Join('\n', normalized)));
    }

    private static bool IsAnyGeneratedBridgeSource(ActiveContentSource source) =>
        IsManagedBridgeSource(source) ||
        source.DisplayName.StartsWith("DDSE Encounter Bridge", StringComparison.OrdinalIgnoreCase) ||
        File.Exists(Path.Combine(source.Directory, "ddse-encounter-bridge.json")) ||
        File.Exists(Path.Combine(source.Directory, "ddse-encounter-bridge-probe.json"));

    private static void ValidateManagedPackagePath(string installRoot, string packageDirectory)
    {
        if (!IsWithinDirectory(packageDirectory, installRoot) ||
            Path.GetFullPath(packageDirectory).Equals(
                Path.GetFullPath(installRoot),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(EditorText.Get("ManagedBattleEncounterBridgeService_039"));
        }
    }

    private static bool IsSafeRelativePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
        {
            return false;
        }
        var segments = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length > 0 &&
               segments.All(segment =>
                   segment is not "." and not ".." &&
                   segment.IndexOfAny(Path.GetInvalidFileNameChars()) < 0);
    }

    private static bool IsWithinDirectory(string candidate, string root)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(candidate));
        return !Path.IsPathRooted(relative) &&
               !relative.Equals("..", StringComparison.Ordinal) &&
               !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
               !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static void RejectReparsePoint(string path, string label)
    {
        if (Directory.Exists(path) &&
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException(EditorText.Format("ManagedBattleEncounterBridgeService_040", label, path));
        }
    }

    private static void ApplyStagedPackage(string source, string destination)
    {
        if (!Directory.Exists(destination))
        {
            CopyDirectory(source, destination);
            return;
        }

        RejectReparsePoint(destination, EditorText.Get("ManagedBattleEncounterBridgeService_005"));
        foreach (var sourcePath in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, sourcePath);
            var destinationPath = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            var temporaryPath = destinationPath + $".ddse-{Guid.NewGuid():N}.tmp";
            try
            {
                File.Copy(sourcePath, temporaryPath, overwrite: false);
                if (File.Exists(destinationPath))
                {
                    File.Replace(
                        temporaryPath,
                        destinationPath,
                        destinationBackupFileName: null,
                        ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(temporaryPath, destinationPath);
                }
            }
            finally
            {
                TryDeleteFile(temporaryPath);
            }
        }
    }

    private static void RestorePackage(string packageDirectory, string? rollbackPackage)
    {
        if (Directory.Exists(packageDirectory))
        {
            RejectReparsePoint(packageDirectory, EditorText.Get("ManagedBattleEncounterBridgeService_005"));
            Directory.Delete(packageDirectory, recursive: true);
        }
        if (rollbackPackage is not null)
        {
            CopyDirectory(rollbackPackage, packageDirectory);
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        RejectReparsePoint(source, EditorText.Get("ManagedBattleEncounterBridgeService_041"));
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            RejectReparsePoint(directory, EditorText.Get("ManagedBattleEncounterBridgeService_042"));
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException(EditorText.Format("ManagedBattleEncounterBridgeService_043", file));
            }
            var destinationPath = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(file, destinationPath, overwrite: false);
        }
    }

    private static bool DirectoriesHaveSameFiles(string left, string right)
    {
        if (!Directory.Exists(left) || !Directory.Exists(right))
        {
            return false;
        }
        var leftFiles = Directory.EnumerateFiles(left, "*", SearchOption.AllDirectories)
            .ToDictionary(
                path => Path.GetRelativePath(left, path).Replace('\\', '/'),
                ComputeSha256,
                StringComparer.OrdinalIgnoreCase);
        var rightFiles = Directory.EnumerateFiles(right, "*", SearchOption.AllDirectories)
            .ToDictionary(
                path => Path.GetRelativePath(right, path).Replace('\\', '/'),
                ComputeSha256,
                StringComparer.OrdinalIgnoreCase);
        return leftFiles.Count == rightFiles.Count &&
               leftFiles.All(pair =>
                   rightFiles.TryGetValue(pair.Key, out var hash) &&
                   hash.Equals(pair.Value, StringComparison.OrdinalIgnoreCase));
    }

    private void EnsureGameIsNotRunning()
    {
        if (_gameRunningProbe())
        {
            throw new InvalidOperationException(
                EditorText.Get("ManagedBattleEncounterBridgeService_044"));
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
                if (processes.Length > 0)
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

    private static string SanitizeSegment(string value, int maximumLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            normalized = normalized.Replace(invalid, '_');
        }
        return normalized.Length <= maximumLength ? normalized : normalized[..maximumLength];
    }

    private static string ShortHash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant()[..12];

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string ComputeSha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

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
            // A stale temporary file must not mask the primary operation result.
        }
    }

    private sealed record PreparedManagedGameUpdate(
        string TargetPath,
        string SourcePath,
        string EncodedPath,
        string OriginalSha256,
        string FinalSha256);

    private sealed class ManagedBridgeManifest
    {
        public int Version { get; set; }
        public string ProjectTitle { get; set; } = string.Empty;
        public string ProfileId { get; set; } = string.Empty;
        public string SteamUserId { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
        public List<ManagedBridgeTableManifest> Tables { get; set; } = [];
    }

    private sealed class ManagedBridgeTableManifest
    {
        public string DungeonId { get; set; } = string.Empty;
        public int Difficulty { get; set; }
        public string RelativeMashPath { get; set; } = string.Empty;
        public string ContentFingerprint { get; set; } = string.Empty;
        public string GeneratedMashSha256 { get; set; } = string.Empty;
        public List<ManagedBridgeEncounterManifest> Entries { get; set; } = [];
    }

    private sealed class ManagedBridgeEncounterManifest
    {
        public int MashType { get; set; }
        public int MashIndex { get; set; }
        public int? FileRowIndex { get; set; }
        public List<string> MonsterIds { get; set; } = [];
        public string SourceKind { get; set; } = string.Empty;
        public string SourceLabel { get; set; } = string.Empty;
        public string SourceRelativePath { get; set; } = string.Empty;
        public int SourceLine { get; set; }
        public int? SourceRecordIndex { get; set; }
        public string OriginDungeonId { get; set; } = string.Empty;
        public int OriginDifficulty { get; set; }
        public string? RoamingId { get; set; }
        public string? Classification { get; set; }
        public DateTime AddedAtUtc { get; set; }
    }
}
