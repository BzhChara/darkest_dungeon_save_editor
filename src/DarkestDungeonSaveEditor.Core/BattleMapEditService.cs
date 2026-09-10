using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DarkestDungeonSaveEditor.Core;

public sealed class BattleMapEditService
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private readonly DsonSaveCodec _codec;
    private readonly SaveEditorLocations _locations;
    internal Action<string>? BeforeTargetReplace { get; set; }
    internal Action<string>? AfterTargetReplace { get; set; }

    public BattleMapEditService(DsonSaveCodec codec, SaveEditorLocations? locations = null)
    {
        _codec = codec;
        _locations = locations ?? SaveEditorLocations.CreateDefault();
    }

    public Task<PreparedBattleMapEdit> PrepareDeleteContentAsync(
        SaveProfile profile,
        BattleMapSnapshot expectedSnapshot,
        string areaId,
        string tileId,
        CancellationToken cancellationToken = default) =>
        PrepareAsync(
            profile,
            expectedSnapshot,
            areaId,
            tileId,
            BattleMapEditKind.DeleteContent,
            encounter: null,
            attachment: null,
            cancellationToken);

    public Task<PreparedBattleMapEdit> PrepareMovePartyAsync(
        SaveProfile profile,
        BattleMapSnapshot expectedSnapshot,
        string areaId,
        string tileId,
        CancellationToken cancellationToken = default) =>
        PrepareAsync(
            profile,
            expectedSnapshot,
            areaId,
            tileId,
            BattleMapEditKind.MoveParty,
            encounter: null,
            attachment: null,
            cancellationToken);

    public Task<PreparedBattleMapEdit> PreparePlaceBattleAsync(
        SaveProfile profile,
        BattleMapSnapshot expectedSnapshot,
        string areaId,
        string tileId,
        BattleEncounterDefinition encounter,
        CancellationToken cancellationToken = default) =>
        PrepareAsync(
            profile,
            expectedSnapshot,
            areaId,
            tileId,
            BattleMapEditKind.PlaceBattle,
            encounter,
            attachment: null,
            cancellationToken);

    public Task<PreparedBattleMapEdit> PrepareSetBattleAttachmentAsync(
        SaveProfile profile,
        BattleMapSnapshot expectedSnapshot,
        string areaId,
        string tileId,
        BattleRoomAttachmentDefinition attachment,
        CancellationToken cancellationToken = default) =>
        PrepareAsync(
            profile,
            expectedSnapshot,
            areaId,
            tileId,
            BattleMapEditKind.SetBattleAttachment,
            encounter: null,
            attachment,
            cancellationToken);

    public Task<PreparedBattleMapEdit> PreparePlaceContentAsync(
        SaveProfile profile,
        BattleMapSnapshot expectedSnapshot,
        string areaId,
        string tileId,
        BattleRoomAttachmentDefinition definition,
        CancellationToken cancellationToken = default) =>
        PrepareAsync(
            profile, expectedSnapshot, areaId, tileId, BattleMapEditKind.PlaceContent,
            encounter: null, attachment: definition, cancellationToken);

    public Task<PreparedBattleMapEdit> PrepareRemoveBattleAttachmentAsync(
        SaveProfile profile,
        BattleMapSnapshot expectedSnapshot,
        string areaId,
        string tileId,
        CancellationToken cancellationToken = default) =>
        PrepareAsync(
            profile,
            expectedSnapshot,
            areaId,
            tileId,
            BattleMapEditKind.RemoveBattleAttachment,
            encounter: null,
            attachment: null,
            cancellationToken);

    public async Task<SaveCommitResult> CommitAsync(
        PreparedBattleMapEdit prepared,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureGameIsNotRunning();
        var (mapPath, raidPath) = ValidateBattleProfile(prepared.Profile);
        ValidatePreparedTarget(prepared, mapPath, raidPath);
        ValidateContentGuards(prepared);
        ValidateLivePair(prepared, mapPath, raidPath, "准备完成后");
        using var gameGuard = OpenGameGuard(prepared);

        if (!File.Exists(prepared.TargetFile.EncodedPath) ||
            !ComputeSha256(prepared.TargetFile.EncodedPath).Equals(
                prepared.TargetFile.EncodedSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("已验证的地图修改结果已经变化或丢失，请重新执行操作。");
        }

        var backupDirectory = CreateBackup(prepared.Profile, prepared);
        ValidateContentGuards(prepared);
        ValidateLivePair(prepared, mapPath, raidPath, "创建档案备份期间");
        EnsureGameIsNotRunning();

        var targetPath = prepared.TargetFile.TargetPath;
        var guardPath = EditsMap(prepared.Preview.Kind) ? raidPath : mapPath;
        using var replacement = new GuardedSaveReplacement(targetPath, prepared.TargetFile.EncodedPath,
            prepared.TargetFile.OriginalSha256, prepared.TargetFile.EncodedSha256,
            BeforeTargetReplace, AfterTargetReplace);

        try
        {
            using var guardLock = new FileStream(
                guardPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.SequentialScan);
            var expectedGuardHash = EditsMap(prepared.Preview.Kind)
                ? prepared.RaidOriginalSha256
                : prepared.MapOriginalSha256;
            if (!ComputeSha256(guardLock).Equals(expectedGuardHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "地图或副本存档在写入前发生了变化，本次修改未应用。请等待地图刷新后重试。");
            }
            ValidateContentGuards(prepared);

            replacement.Replace();
            var finalHash = replacement.Verify();
            if (!finalHash.Equals(prepared.TargetFile.EncodedSha256, StringComparison.OrdinalIgnoreCase) ||
                !ComputeSha256(guardLock).Equals(expectedGuardHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException(
                    "写入后的地图与副本存档组合不符合已验证结果，程序将尝试自动恢复。");
            }
            ValidateContentGuards(prepared);

            var result = new SaveCommitResult(
                prepared.Profile.ProfileDirectory,
                targetPath,
                backupDirectory,
                prepared.TargetFile.OriginalSha256,
                finalHash,
                DateTime.UtcNow);
            SaveCommitMarker.Publish(Path.Combine(backupDirectory, "commit-result.json"), result);
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
                        $"地图存档写入失败；{GuardedSaveReplacement.DescribeRecovery(recovery)}。" +
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
                        $"地图存档写入失败，自动恢复也未能完成。完整备份：{backupDirectory}；实际被替换版本：{replacement.DisplacedPath}",
                        commitError,
                        restoreError);
                }
            }

            throw;
        }
    }

    private async Task<PreparedBattleMapEdit> PrepareAsync(
        SaveProfile profile,
        BattleMapSnapshot expectedSnapshot,
        string areaId,
        string tileId,
        BattleMapEditKind kind,
        BattleEncounterDefinition? encounter,
        BattleRoomAttachmentDefinition? attachment,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(expectedSnapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(areaId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tileId);
        cancellationToken.ThrowIfCancellationRequested();
        _codec.ValidateAvailability();
        var (mapPath, raidPath) = ValidateBattleProfile(profile);
        ValidateExpectedSnapshot(profile, expectedSnapshot, mapPath, raidPath);
        if (kind == BattleMapEditKind.PlaceBattle)
        {
            ArgumentNullException.ThrowIfNull(encounter);
            BattleEncounterCatalog.ValidateDirectEncounter(encounter);
        }
        else if (encounter is not null)
        {
            throw new ArgumentException("只有遭遇写入操作可以携带遭遇定义。", nameof(encounter));
        }
        if (kind is BattleMapEditKind.SetBattleAttachment or BattleMapEditKind.PlaceContent)
        {
            ArgumentNullException.ThrowIfNull(attachment);
            BattleRoomAttachmentCatalog.ValidateDefinition(attachment);
            var expectedGamePath = Path.Combine(
                Path.GetFullPath(profile.ProfileDirectory),
                "persist.game.json");
            if (!Path.GetFullPath(attachment.CatalogGuard.GameSavePath).Equals(
                    expectedGamePath,
                    StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFullPath(attachment.CatalogGuard.ProfileDirectory).Equals(
                    Path.GetFullPath(profile.ProfileDirectory),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "所选地图内容目录属于另一个档案，请重新加载内容目录。");
            }
        }
        else if (attachment is not null)
        {
            throw new ArgumentException(
                "只有地图内容或战斗附加内容写入操作可以携带资源定义。",
                nameof(attachment));
        }

        var mapHash = ComputeSha256(mapPath);
        var raidHash = ComputeSha256(raidPath);
        if (!mapHash.Equals(expectedSnapshot.MapSha256, StringComparison.OrdinalIgnoreCase) ||
            !raidHash.Equals(expectedSnapshot.RaidSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "地图或副本存档在显示后已经变化，请等待地图刷新后重新操作。");
        }

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

        var mapSourceCopy = Path.Combine(sourceDirectory, "persist.map.json");
        var raidSourceCopy = Path.Combine(sourceDirectory, "persist.raid.json");
        using var writeGuard = await BattleMapWriteGuard.LoadAsync(
            profile, expectedSnapshot, _codec, workspace, cancellationToken).ConfigureAwait(false);
        ValidateCapturedPair(mapPath, raidPath, mapSourceCopy, raidSourceCopy, mapHash, raidHash);
        var mapDocument = writeGuard.MapDocument;
        var raidDocument = writeGuard.RaidDocument;
        var capturedSnapshot = writeGuard.Snapshot;

        var preview = kind switch
        {
            BattleMapEditKind.DeleteContent => BattleMapSaveEditor.DeleteContent(
                mapDocument,
                raidDocument,
                capturedSnapshot,
                areaId,
                tileId),
            BattleMapEditKind.MoveParty => BattleMapSaveEditor.MoveParty(
                mapDocument,
                raidDocument,
                capturedSnapshot,
                areaId,
                tileId),
            BattleMapEditKind.PlaceBattle => BattleMapSaveEditor.PlaceBattle(
                mapDocument,
                raidDocument,
                capturedSnapshot,
                areaId,
                tileId,
                encounter!),
            BattleMapEditKind.PlaceContent => BattleMapSaveEditor.PlaceContent(
                mapDocument, raidDocument, capturedSnapshot, areaId, tileId, attachment!),
            BattleMapEditKind.SetBattleAttachment => BattleMapSaveEditor.SetBattleAttachment(
                mapDocument,
                raidDocument,
                capturedSnapshot,
                areaId,
                tileId,
                attachment!),
            BattleMapEditKind.RemoveBattleAttachment => BattleMapSaveEditor.RemoveBattleAttachment(
                mapDocument,
                raidDocument,
                capturedSnapshot,
                areaId,
                tileId),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };

        var targetFileName = EditsMap(kind)
            ? "persist.map.json"
            : "persist.raid.json";
        var targetPath = EditsMap(kind) ? mapPath : raidPath;
        var targetSourceCopy = EditsMap(kind) ? mapSourceCopy : raidSourceCopy;
        var updatedDocument = EditsMap(kind) ? mapDocument : raidDocument;
        var proposedPath = Path.Combine(
            decodedDirectory,
            $"{Path.GetFileNameWithoutExtension(targetFileName)}.proposed.json");
        var encodedPath = Path.Combine(encodedDirectory, targetFileName);
        var roundTripPath = Path.Combine(roundTripDirectory, targetFileName);
        JsonSupport.WriteObject(proposedPath, updatedDocument);
        var sourceWasDson = DsonSaveCodec.IsDson(targetSourceCopy);
        await _codec.EncodeAsync(
            proposedPath,
            encodedPath,
            targetSourceCopy,
            cancellationToken).ConfigureAwait(false);
        await _codec.DecodeAsync(encodedPath, roundTripPath, cancellationToken).ConfigureAwait(false);
        var roundTripDocument = JsonSupport.ReadObject(roundTripPath);
        if (!JsonNode.DeepEquals(updatedDocument, roundTripDocument))
        {
            throw new InvalidDataException(
                $"地图修改（{kind}）未通过 DSON 编码回环验证，本次修改未写入。");
        }
        if (sourceWasDson && !RevisionMatches(targetSourceCopy, encodedPath))
        {
            throw new InvalidDataException(
                $"编码后的 {targetFileName} 未保留原始 DSON 修订字段，本次修改未写入。");
        }

        ValidateCapturedPair(mapPath, raidPath, mapSourceCopy, raidSourceCopy, mapHash, raidHash);
        if (encounter is not null)
        {
            BattleEncounterCatalog.ValidateDirectEncounter(encounter);
        }
        if (attachment is not null)
        {
            BattleRoomAttachmentCatalog.ValidateDefinition(attachment);
        }
        var targetFile = new PreparedSaveFile(
            targetFileName,
            targetPath,
            targetSourceCopy,
            proposedPath,
            encodedPath,
            roundTripPath,
            EditsMap(kind) ? mapHash : raidHash,
            ComputeSha256(encodedPath),
            sourceWasDson);
        var prepared = new PreparedBattleMapEdit(
            sessionId,
            profile,
            preview,
            workspace,
            targetFile,
            mapHash,
            raidHash,
            DateTime.UtcNow)
        {
            GameOriginalSha256 = writeGuard.GameSha256,
            RaidIdentity = capturedSnapshot.RaidIdentity,
            Encounter = encounter,
            Attachment = attachment
        };
        WriteJson(Path.Combine(workspace, "session.json"), prepared);
        return prepared;
    }

    private static (string MapPath, string RaidPath) ValidateBattleProfile(SaveProfile profile)
    {
        var profileDirectory = Path.GetFullPath(profile.ProfileDirectory);
        if (!Directory.Exists(profileDirectory))
        {
            throw new DirectoryNotFoundException($"找不到档案目录：{profileDirectory}");
        }

        var expectedEstatePath = Path.Combine(profileDirectory, "persist.estate.json");
        if (!Path.GetFullPath(profile.EstateSavePath)
                .Equals(expectedEstatePath, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(expectedEstatePath))
        {
            throw new InvalidOperationException(
                "所选档案缺少预期的 persist.estate.json 文件。");
        }

        var mapPath = Path.Combine(profileDirectory, "persist.map.json");
        var raidPath = Path.Combine(profileDirectory, "persist.raid.json");
        if (!File.Exists(mapPath) || !File.Exists(raidPath))
        {
            throw new InvalidOperationException(
                "所选档案已经不再处于完整的副本状态。");
        }

        return (mapPath, raidPath);
    }

    private static FileStream OpenGameGuard(PreparedBattleMapEdit prepared)
    {
        if (string.IsNullOrWhiteSpace(prepared.GameOriginalSha256))
            throw new InvalidDataException("地图修改缺少游戏状态校验，请重新准备操作。");
        var stream = new FileStream(Path.Combine(prepared.Profile.ProfileDirectory, "persist.game.json"),
            FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            if (!ComputeSha256(stream).Equals(prepared.GameOriginalSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("档案的小镇／副本状态或活动配置在准备后已变化，请重新加载地图。");
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private static void ValidateExpectedSnapshot(
        SaveProfile profile,
        BattleMapSnapshot snapshot,
        string mapPath,
        string raidPath)
    {
        if (!Path.GetFullPath(snapshot.ProfileDirectory).Equals(
                Path.GetFullPath(profile.ProfileDirectory),
                StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFullPath(snapshot.MapSavePath).Equals(mapPath, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFullPath(snapshot.RaidSavePath).Equals(raidPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "当前显示的战斗地图属于另一个档案，请重新加载内容目录。");
        }
    }

    private static void ValidatePreparedTarget(
        PreparedBattleMapEdit prepared,
        string mapPath,
        string raidPath)
    {
        var expectedPath = EditsMap(prepared.Preview.Kind) ? mapPath : raidPath;
        var expectedFileName = Path.GetFileName(expectedPath);
        if (!Path.GetFullPath(prepared.TargetFile.TargetPath).Equals(
                expectedPath,
                StringComparison.OrdinalIgnoreCase) ||
            !prepared.TargetFile.FileName.Equals(expectedFileName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "准备写入的地图目标不属于所选档案，或存档类型不正确。");
        }
    }

    private static void ValidateContentGuards(PreparedBattleMapEdit prepared)
    {
        if (prepared.Preview.Kind == BattleMapEditKind.PlaceBattle)
        {
            if (prepared.Encounter is null)
            {
                throw new InvalidDataException("遭遇写入会话缺少已验证的遭遇定义。");
            }

            BattleEncounterCatalog.ValidateDirectEncounter(prepared.Encounter);
        }
        else if (prepared.Encounter is not null)
        {
            throw new InvalidDataException("非遭遇写入会话意外包含遭遇定义。");
        }

        if (prepared.Preview.Kind is BattleMapEditKind.SetBattleAttachment or BattleMapEditKind.PlaceContent)
        {
            if (prepared.Attachment is null)
            {
                throw new InvalidDataException("地图内容写入会话缺少已验证的资源定义。");
            }

            var expectedProfileDirectory = Path.GetFullPath(prepared.Profile.ProfileDirectory);
            var expectedGamePath = Path.Combine(expectedProfileDirectory, "persist.game.json");
            if (!Path.GetFullPath(prepared.Attachment.CatalogGuard.ProfileDirectory).Equals(
                    expectedProfileDirectory,
                    StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFullPath(prepared.Attachment.CatalogGuard.GameSavePath).Equals(
                    expectedGamePath,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("地图内容写入会话属于另一个档案。");
            }

            BattleRoomAttachmentCatalog.ValidateDefinition(prepared.Attachment);
        }
        else if (prepared.Attachment is not null)
        {
            throw new InvalidDataException("非内容写入会话意外包含地图资源定义。");
        }
    }

    private static bool EditsMap(BattleMapEditKind kind) =>
        kind is BattleMapEditKind.DeleteContent or
            BattleMapEditKind.PlaceBattle or
            BattleMapEditKind.PlaceContent or
            BattleMapEditKind.SetBattleAttachment or
            BattleMapEditKind.RemoveBattleAttachment;

    private static void ValidateCapturedPair(
        string mapPath,
        string raidPath,
        string mapCopyPath,
        string raidCopyPath,
        string expectedMapHash,
        string expectedRaidHash)
    {
        if (!ComputeSha256(mapCopyPath).Equals(expectedMapHash, StringComparison.OrdinalIgnoreCase) ||
            !ComputeSha256(raidCopyPath).Equals(expectedRaidHash, StringComparison.OrdinalIgnoreCase) ||
            !ComputeSha256(mapPath).Equals(expectedMapHash, StringComparison.OrdinalIgnoreCase) ||
            !ComputeSha256(raidPath).Equals(expectedRaidHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "准备修改文件期间地图或副本存档发生了变化，本次修改未写入。");
        }
    }

    private static void ValidateLivePair(
        PreparedBattleMapEdit prepared,
        string mapPath,
        string raidPath,
        string phase)
    {
        if (!ComputeSha256(mapPath).Equals(
                prepared.MapOriginalSha256,
                StringComparison.OrdinalIgnoreCase) ||
            !ComputeSha256(raidPath).Equals(
                prepared.RaidOriginalSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"地图或副本存档在“{phase}”阶段发生了变化。为避免覆盖较新的游戏数据，请重新执行操作。");
        }
    }

    private string CreateBackup(SaveProfile profile, PreparedBattleMapEdit prepared)
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
        WriteJson(Path.Combine(backupDirectory, "backup-manifest.json"), new
        {
            version = 1,
            operation = prepared.Preview.Kind.ToString(),
            createdAtUtc = DateTime.UtcNow,
            profile.ProfileId,
            profile.SteamUserId,
            profile.ProfileDirectory,
            prepared.SessionId,
            prepared.RaidIdentity,
            prepared.Preview.AreaId,
            prepared.Preview.TileId,
            prepared.MapOriginalSha256,
            prepared.RaidOriginalSha256,
            encounter = prepared.Encounter is null
                ? null
                : new
                {
                    prepared.Encounter.MashType,
                    prepared.Encounter.MashIndex,
                    prepared.Encounter.MonsterIds,
                    prepared.Encounter.SourceLabel,
                    prepared.Encounter.SourcePath,
                    prepared.Encounter.SourceLine,
                    prepared.Encounter.SourceRecordIndex,
                    tableFingerprint = prepared.Encounter.TableGuard.Fingerprint
                },
            mapContent = prepared.Attachment is null ? null : new
            {
                prepared.Attachment.Id,
                prepared.Attachment.Kind,
                prepared.Attachment.PropHash,
                prepared.Attachment.SourcePath,
                prepared.Attachment.SourceLine,
                prepared.Attachment.SourceRecordIndex,
                catalogFingerprint = prepared.Attachment.CatalogGuard.Fingerprint
            },
            files
        });
        return backupDirectory;
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
                        "检测到《暗黑地牢》仍在运行。请完全退出游戏后再修改存档。");
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
