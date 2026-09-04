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
/// Maintains one append-only encounter carrier per save profile. The game still consumes
/// numeric mash indexes, but callers never have to install, reorder, or retire one-off Mods.
/// </summary>
public sealed class ManagedBattleEncounterBridgeService
{
    public const string ManifestFileName = "ddse-managed-encounter-bridge.json";
    public const int ManifestVersion = 3;

    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly DsonSaveCodec _codec;
    private readonly SaveEditorLocations _locations;
    private readonly Func<bool> _gameRunningProbe;

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
        CancellationToken cancellationToken = default)
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

        var sessionId = $"{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}";
        var workspace = Path.Combine(
            Path.GetFullPath(_locations.WorkspaceDirectory),
            "managed_encounter_bridge",
            sessionId);
        var stagedPackage = Path.Combine(workspace, "package");
        var rollbackPackage = Path.Combine(workspace, "rollback-package");
        Directory.CreateDirectory(workspace);

        var packageExisted = Directory.Exists(packageDirectory);
        ManagedBridgeManifest manifest;
        if (packageExisted)
        {
            RejectReparsePoint(packageDirectory, "托管 Encounter Bridge 目录");
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
        var tableTarget = ResolveTableTarget(catalog, encounter.MashType);
        var table = manifest.Tables.SingleOrDefault(candidate =>
            candidate.DungeonId.Equals(catalog.DungeonId, StringComparison.OrdinalIgnoreCase) &&
            candidate.Difficulty == catalog.Difficulty &&
            candidate.RelativeMashPath.Equals(
                tableTarget.RelativeMashPath,
                StringComparison.OrdinalIgnoreCase));
        var stagedMashPath = Path.Combine(
            stagedPackage,
            tableTarget.RelativeMashPath.Replace('/', Path.DirectorySeparatorChar));
        ManagedBridgeEncounterManifest? existingEntry = null;

        if (table is null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(stagedMashPath)!);
            File.Copy(tableTarget.SourceMashPath, stagedMashPath, overwrite: false);
            table = new ManagedBridgeTableManifest
            {
                DungeonId = catalog.DungeonId,
                Difficulty = catalog.Difficulty,
                RelativeMashPath = tableTarget.RelativeMashPath,
                BaseSourcePath = Path.GetFullPath(tableTarget.SourceMashPath),
                BaseSourceSha256 = ComputeSha256(tableTarget.SourceMashPath),
                BaseLength = new FileInfo(tableTarget.SourceMashPath).Length,
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
            var expectedMashIndex = CountStandardRows(stagedMashPath, encounter.MashType);
            if (table.Entries.Any(candidate =>
                    candidate.MashType == encounter.MashType &&
                    candidate.MashIndex == expectedMashIndex))
            {
                throw new InvalidDataException(
                    "托管 Encounter Bridge 清单中的索引与实际遭遇表不一致，本次不会覆盖已有条目。");
            }

            AppendEncounterRow(stagedMashPath, encounter);
            entry = new ManagedBridgeEncounterManifest
            {
                MashType = encounter.MashType,
                MashIndex = expectedMashIndex,
                MonsterIds = encounter.MonsterIds.ToList(),
                SourceKind = encounter.SourceKind.ToString(),
                SourceLabel = encounter.SourceLabel,
                SourceRelativePath = encounter.SourceRelativePath,
                SourceLine = encounter.SourceLine,
                OriginDungeonId = encounter.OriginDungeonId,
                OriginDifficulty = encounter.OriginDifficulty,
                RoamingId = encounter.RoamingId,
                AddedAtUtc = DateTime.UtcNow
            };
            table.Entries.Add(entry);
            table.GeneratedMashSha256 = ComputeSha256(stagedMashPath);
            manifest.UpdatedAtUtc = DateTime.UtcNow;
        }

        WriteManagedPackage(stagedPackage, packageDirectory, manifest);
        var gameUpdate = await PrepareGameConfigurationAsync(
            profile,
            activeContent,
            projectTitle,
            workspace,
            cancellationToken).ConfigureAwait(false);
        var packageChanged = !packageExisted || !DirectoriesHaveSameFiles(packageDirectory, stagedPackage);
        var packageCommitted = false;
        var gameCommitted = false;
        string? profileBackupDirectory = null;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureGameIsNotRunning();
            BattleEncounterCatalog.ValidateBridgeEncounter(encounter);

            if (gameUpdate is not null)
            {
                profileBackupDirectory = CreateProfileBackup(profile, sessionId, activeContent.SourceGameSha256);
            }

            if (packageChanged)
            {
                packageCommitted = true;
                ApplyStagedPackage(stagedPackage, packageDirectory);
            }

            if (gameUpdate is not null)
            {
                gameCommitted = true;
                CommitGameConfiguration(gameUpdate);
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
            var directEncounter = resolvedCatalog.DirectEncounters.SingleOrDefault(candidate =>
                candidate.MashType == entry.MashType &&
                candidate.MashIndex == entry.MashIndex &&
                candidate.MonsterIds.SequenceEqual(entry.MonsterIds, StringComparer.Ordinal) &&
                IsWithinDirectory(candidate.SourcePath, packageDirectory));
            if (directEncounter is null)
            {
                throw new InvalidOperationException(
                    "托管 Encounter Bridge 已生成，但重新解析后没有得到预期的稳定索引；所有更改将自动恢复。");
            }

            BattleEncounterCatalog.ValidateDirectEncounter(directEncounter);
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
            if (gameCommitted && gameUpdate is not null)
            {
                try
                {
                    RestoreGameConfiguration(gameUpdate);
                }
                catch (Exception rollbackError)
                {
                    rollbackErrors.Add(rollbackError);
                }
            }

            if (packageCommitted)
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

            if (rollbackErrors.Count > 0)
            {
                throw new AggregateException(
                    "托管 Encounter Bridge 更新失败，自动恢复也未能完整完成。请保留备份和工作目录。",
                    [primaryError, .. rollbackErrors]);
            }

            throw;
        }
    }

    public static string GetProjectTitle(SaveProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var identity = $"{profile.SteamUserId}/{profile.ProfileId}";
        var readableProfile = SanitizeSegment(profile.ProfileId, 24);
        var hash = ShortHash(identity);
        return $"DDSE Managed Encounter Bridge - {readableProfile}-{hash}";
    }

    public static bool IsManagedBridgeSource(ActiveContentSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.DisplayName.StartsWith(
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
        var profileDirectory = Path.GetFullPath(profile.ProfileDirectory);
        if (!Path.GetFullPath(snapshot.ProfileDirectory).Equals(
                profileDirectory,
                StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFullPath(activeContent.Profile.ProfileDirectory).Equals(
                profileDirectory,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("地图、活动内容与所选档案不属于同一个 Profile。");
        }

        if (!catalog.DungeonId.Equals(snapshot.DungeonId, StringComparison.OrdinalIgnoreCase) ||
            catalog.Difficulty != snapshot.Difficulty ||
            !encounter.TableGuard.Fingerprint.Equals(
                catalog.TableGuard.Fingerprint,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("所选遭遇不属于当前副本的最新目录快照。");
        }

        var gameSavePath = Path.Combine(profileDirectory, "persist.game.json");
        if (!File.Exists(gameSavePath) ||
            !ComputeSha256(gameSavePath).Equals(
                activeContent.SourceGameSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("活动 Mod 配置已经变化，请重新加载内容目录。");
        }
    }

    private static ManagedBridgeTableTarget ResolveTableTarget(
        BattleEncounterCatalogResult catalog,
        int mashType)
    {
        var currentRows = catalog.Encounters
            .Where(candidate =>
                candidate.SourceKind == BattleEncounterSourceKind.Standard &&
                candidate.MashType == mashType)
            .OrderBy(candidate => candidate.MashIndex)
            .ToArray();
        if (currentRows.Select(candidate => candidate.SourcePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Skip(1)
                .Any())
        {
            throw new InvalidOperationException(
                "当前类型由多个标准遭遇文件共同扩展，无法证明追加后的运行时索引顺序。");
        }

        string sourceMashPath;
        if (currentRows.Length > 0)
        {
            if (!currentRows.Select((candidate, index) =>
                        candidate.CanPlaceDirectly && candidate.MashIndex == index)
                    .All(matches => matches))
            {
                throw new InvalidOperationException(
                    "当前类型没有连续且可证明的标准遭遇索引，不能更新托管 Bridge。");
            }
            sourceMashPath = currentRows[0].SourcePath;
        }
        else
        {
            var expectedFileName =
                $"{catalog.DungeonId}.{catalog.Difficulty.ToString(CultureInfo.InvariantCulture)}.mash.darkest";
            var exact = catalog.TableGuard.EffectiveFiles
                .Where(file =>
                    BattleEncounterCatalog.ClassifyFile(file.Path) == BattleEncounterSourceKind.Standard &&
                    Path.GetFileName(file.RelativePath).Equals(
                        expectedFileName,
                        StringComparison.OrdinalIgnoreCase))
                .OrderBy(file => file.RelativePath.Length)
                .ThenBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            var fallback = exact ?? catalog.TableGuard.EffectiveFiles
                .Where(file =>
                    BattleEncounterCatalog.ClassifyFile(file.Path) == BattleEncounterSourceKind.Standard)
                .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            sourceMashPath = fallback?.Path ?? throw new InvalidOperationException(
                "当前副本没有可承载首个索引的标准遭遇文件。");
        }

        var sourceFingerprint = catalog.TableGuard.EffectiveFiles.SingleOrDefault(file =>
            Path.GetFullPath(file.Path).Equals(
                Path.GetFullPath(sourceMashPath),
                StringComparison.OrdinalIgnoreCase));
        if (sourceFingerprint is null ||
            !File.Exists(sourceMashPath) ||
            !ComputeSha256(sourceMashPath).Equals(
                sourceFingerprint.Sha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("标准遭遇文件在托管 Bridge 准备期间已经变化。");
        }

        var relativeMashPath = sourceFingerprint.RelativePath.Replace('\\', '/').TrimStart('/');
        if (!$"/{relativeMashPath}".Contains("/dungeons/", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("标准遭遇文件不位于可桥接的 dungeons 目录。");
        }

        return new ManagedBridgeTableTarget(
            Path.GetFullPath(sourceMashPath),
            relativeMashPath);
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
                "启用 Mod 的集合或顺序已在托管 Bridge 建立后变化。为避免重排仍被存档引用的索引，" +
                "本次不会自动重建旧遭遇表。");
        }
        if (!File.Exists(stagedMashPath) ||
            !ComputeSha256(stagedMashPath).Equals(
                table.GeneratedMashSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "托管 Encounter Bridge 的遭遇表已被外部修改，无法安全追加新索引。");
        }
        if (table.Entries
            .GroupBy(entry => (entry.MashType, entry.MashIndex))
            .Any(group => group.Count() > 1))
        {
            throw new InvalidDataException("托管 Encounter Bridge 清单包含重复索引。");
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
            throw new InvalidOperationException("活动 Mod 配置在托管 Bridge 准备期间已经变化。");
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
                    $"活动 Mod 列表包含无法安全重排的条目：{pair.Key}");
            }
            orderedEntries.Add((order, (JsonObject)value.DeepClone()));
        }

        var retainedEntries = orderedEntries
            .OrderBy(entry => entry.Order)
            .Select(entry => entry.Value)
            .Where(entry =>
                !JsonSupport.ReadString(entry, "name").Equals(
                    projectTitle,
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
                "托管 Bridge 的活动 Mod 配置未通过 DSON 编码回环验证。");
        }
        if (sourceWasDson && !RevisionMatches(sourcePath, encodedPath))
        {
            throw new InvalidDataException("编码后的 persist.game.json 未保留原始 DSON 修订字段。");
        }

        return new PreparedManagedGameUpdate(
            gamePath,
            sourcePath,
            encodedPath,
            originalHash,
            ComputeSha256(encodedPath));
    }

    private void CommitGameConfiguration(PreparedManagedGameUpdate update)
    {
        EnsureGameIsNotRunning();
        if (!ComputeSha256(update.TargetPath).Equals(
                update.OriginalSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "persist.game.json 在托管 Bridge 写入前发生了变化，本次操作未应用。");
        }

        var temporaryPath = Path.Combine(
            Path.GetDirectoryName(update.TargetPath)!,
            $".{Path.GetFileName(update.TargetPath)}.ddse-managed-{Guid.NewGuid():N}.tmp");
        try
        {
            File.Copy(update.EncodedPath, temporaryPath, overwrite: false);
            File.Replace(
                temporaryPath,
                update.TargetPath,
                destinationBackupFileName: null,
                ignoreMetadataErrors: true);
            if (!ComputeSha256(update.TargetPath).Equals(
                    update.FinalSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("托管 Bridge 的活动 Mod 配置写入后校验失败。");
            }
        }
        finally
        {
            TryDeleteFile(temporaryPath);
        }
    }

    private static void RestoreGameConfiguration(PreparedManagedGameUpdate update)
    {
        if (!File.Exists(update.SourcePath) ||
            !ComputeSha256(update.SourcePath).Equals(
                update.OriginalSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("托管 Bridge 的 persist.game.json 恢复源丢失或损坏。");
        }
        var currentSha256 = ComputeSha256(update.TargetPath);
        if (currentSha256.Equals(update.OriginalSha256, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        if (!currentSha256.Equals(update.FinalSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "persist.game.json 在托管 Bridge 写入后又发生了变化，程序不会覆盖较新的数据。");
        }

        var temporaryPath = Path.Combine(
            Path.GetDirectoryName(update.TargetPath)!,
            $".{Path.GetFileName(update.TargetPath)}.ddse-managed-restore-{Guid.NewGuid():N}.tmp");
        try
        {
            File.Copy(update.SourcePath, temporaryPath, overwrite: false);
            File.Replace(
                temporaryPath,
                update.TargetPath,
                destinationBackupFileName: null,
                ignoreMetadataErrors: true);
            if (!ComputeSha256(update.TargetPath).Equals(
                    update.OriginalSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("persist.game.json 自动恢复后的哈希不一致。");
            }
        }
        finally
        {
            TryDeleteFile(temporaryPath);
        }
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
        var files = Directory.EnumerateFiles(
                profile.ProfileDirectory,
                "persist*.json",
                SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path =>
            {
                var before = ComputeSha256(path);
                var destination = Path.Combine(backupDirectory, Path.GetFileName(path));
                File.Copy(path, destination, overwrite: false);
                var backupHash = ComputeSha256(destination);
                var after = ComputeSha256(path);
                if (!before.Equals(backupHash, StringComparison.OrdinalIgnoreCase) ||
                    !after.Equals(backupHash, StringComparison.OrdinalIgnoreCase))
                {
                    throw new IOException($"备份期间存档发生了变化：{path}");
                }
                return new
                {
                    fileName = Path.GetFileName(path),
                    sha256 = backupHash,
                    length = new FileInfo(destination).Length
                };
            })
            .ToArray();
        var gameBackup = Path.Combine(backupDirectory, "persist.game.json");
        if (!File.Exists(gameBackup) ||
            !ComputeSha256(gameBackup).Equals(expectedGameSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException("完整档案备份中的 persist.game.json 与目录快照不一致。");
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
        var kind = encounter.MashType switch
        {
            0 => "hall",
            1 => "room",
            2 => "boss",
            _ => throw new InvalidOperationException("托管 Encounter Bridge 不支持该 mash_type。")
        };
        var options = encounter.MashType == 2
            ? string.Empty
            : " .limit 1 .can_be_ambush false";
        var existingBytes = File.ReadAllBytes(mashPath);
        var prefix = existingBytes.Length > 0 && existingBytes[^1] is not (byte)'\r' and not (byte)'\n'
            ? Environment.NewLine
            : string.Empty;
        var line =
            $"{prefix}{kind}: .chance 0 .types {string.Join(' ', encounter.MonsterIds)}" +
            options + Environment.NewLine;
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
            _ => throw new InvalidOperationException("托管 Encounter Bridge 不支持该 mash_type。")
        };
        return File.ReadLines(path)
            .Select(StripLineComment)
            .Select(line => line.Trim())
            .Count(line => IsMashRow(line, expectedKind));
    }

    private static bool IsMashRow(string line, string expectedKind)
    {
        var separator = line.IndexOf(':');
        if (separator <= 0 ||
            !line[..separator].Trim().Equals(expectedKind, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var body = line[(separator + 1)..];
        var tokenIndex = body.IndexOf(".types", StringComparison.OrdinalIgnoreCase);
        return tokenIndex >= 0 &&
               (tokenIndex == 0 || char.IsWhiteSpace(body[tokenIndex - 1])) &&
               (tokenIndex + 6 == body.Length || char.IsWhiteSpace(body[tokenIndex + 6]));
    }

    private static string StripLineComment(string line)
    {
        var quoted = false;
        for (var index = 0; index < line.Length - 1; index++)
        {
            if (line[index] == '"' && (index == 0 || line[index - 1] != '\\'))
            {
                quoted = !quoted;
            }
            if (!quoted && line[index] == '/' && line[index + 1] == '/')
            {
                return line[..index];
            }
        }
        return line;
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
        File.WriteAllText(
            projectPath,
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <project>
              <PreviewIconFile/>
              <ItemDescriptionShort>Persistent encounter carrier managed by Darkest Dungeon Save Editor.</ItemDescriptionShort>
              <ModDataPath>{SecurityElement.Escape(modDataPath)}</ModDataPath>
              <Title>{SecurityElement.Escape(manifest.ProjectTitle)}</Title>
              <Language>english</Language>
              <Visibility>private</Visibility>
              <UploadMode>direct_upload</UploadMode>
              <VersionMajor>0</VersionMajor>
              <VersionMinor>0</VersionMinor>
              <TargetBuild>0</TargetBuild>
              <Tags><Tags>Gameplay Tweaks</Tags></Tags>
              <ItemDescription>This local Mod is maintained automatically. Keep it enabled; existing mash indexes are append-only and are never reordered.</ItemDescription>
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
                        $"托管 Encounter Bridge 清单引用了不存在的文件：{table.RelativeMashPath}");
                }
                return $"{table.RelativeMashPath} {new FileInfo(path).Length}";
            });
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
                $"现有托管 Bridge 目录缺少 {ManifestFileName}，程序不会覆盖它。");
        }
        try
        {
            return JsonSerializer.Deserialize<ManagedBridgeManifest>(
                       File.ReadAllText(path, Encoding.UTF8),
                       ManifestJsonOptions)
                   ?? throw new InvalidDataException("托管 Encounter Bridge 清单为空。");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("托管 Encounter Bridge 清单不是有效 JSON。", ex);
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
                "现有托管 Encounter Bridge 不属于当前档案或版本不兼容，程序不会覆盖它。");
        }
        if (manifest.Tables is null || manifest.Tables.Any(table => table is null))
        {
            throw new InvalidDataException("托管 Encounter Bridge 清单缺少有效的遭遇表列表。");
        }
        foreach (var table in manifest.Tables)
        {
            if (string.IsNullOrWhiteSpace(table.DungeonId) ||
                !IsSafeRelativePath(table.RelativeMashPath) ||
                string.IsNullOrWhiteSpace(table.ContentFingerprint) ||
                string.IsNullOrWhiteSpace(table.GeneratedMashSha256) ||
                table.Entries is null ||
                table.Entries.Any(entry =>
                    entry is null ||
                    entry.MashType is < 0 or > 2 ||
                    entry.MashIndex < 0 ||
                    entry.MonsterIds is null ||
                    entry.MonsterIds.Count == 0 ||
                    entry.MonsterIds.Any(string.IsNullOrWhiteSpace)))
            {
                throw new InvalidDataException("托管 Encounter Bridge 清单包含无效的遭遇表或条目。");
            }
            if (table.Entries
                .GroupBy(entry => $"{entry.MashType}\n{string.Join('\n', entry.MonsterIds)}")
                .Any(group => group.Count() > 1))
            {
                throw new InvalidDataException("托管 Encounter Bridge 清单包含重复的完整遭遇。");
            }
        }
        if (manifest.Tables
            .GroupBy(
                table => table.RelativeMashPath,
                StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1))
        {
            throw new InvalidDataException("托管 Encounter Bridge 清单包含重复的遭遇表路径。");
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
            throw new IOException($"本地 Mod 根目录被同名文件占用：{root}");
        }
        Directory.CreateDirectory(root);
        RejectReparsePoint(root, "本地 Mod 根目录");
        return root;
    }

    private static string GetPackageDirectoryName(SaveProfile profile) =>
        $"DDSE_Managed_Encounter_Bridge_{ShortHash($"{profile.SteamUserId}/{profile.ProfileId}")}";

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
            throw new InvalidOperationException("托管 Encounter Bridge 的目标目录超出了本地 Mod 根目录。");
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
            throw new IOException($"{label}不能是符号链接或目录联接：{path}");
        }
    }

    private static void ApplyStagedPackage(string source, string destination)
    {
        if (!Directory.Exists(destination))
        {
            CopyDirectory(source, destination);
            return;
        }

        RejectReparsePoint(destination, "托管 Encounter Bridge 目录");
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
            RejectReparsePoint(packageDirectory, "托管 Encounter Bridge 目录");
            Directory.Delete(packageDirectory, recursive: true);
        }
        if (rollbackPackage is not null)
        {
            CopyDirectory(rollbackPackage, packageDirectory);
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        RejectReparsePoint(source, "复制源目录");
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            RejectReparsePoint(directory, "复制源子目录");
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException($"复制源文件不能是符号链接：{file}");
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
                "检测到《暗黑地牢》仍在运行。请完全退出游戏后再修改遭遇。");
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

    private sealed record ManagedBridgeTableTarget(
        string SourceMashPath,
        string RelativeMashPath);

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
        public string BaseSourcePath { get; set; } = string.Empty;
        public string BaseSourceSha256 { get; set; } = string.Empty;
        public long BaseLength { get; set; }
        public string ContentFingerprint { get; set; } = string.Empty;
        public string GeneratedMashSha256 { get; set; } = string.Empty;
        public List<ManagedBridgeEncounterManifest> Entries { get; set; } = [];
    }

    private sealed class ManagedBridgeEncounterManifest
    {
        public int MashType { get; set; }
        public int MashIndex { get; set; }
        public List<string> MonsterIds { get; set; } = [];
        public string SourceKind { get; set; } = string.Empty;
        public string SourceLabel { get; set; } = string.Empty;
        public string SourceRelativePath { get; set; } = string.Empty;
        public int SourceLine { get; set; }
        public string OriginDungeonId { get; set; } = string.Empty;
        public int OriginDifficulty { get; set; }
        public string? RoamingId { get; set; }
        public DateTime AddedAtUtc { get; set; }
    }
}
