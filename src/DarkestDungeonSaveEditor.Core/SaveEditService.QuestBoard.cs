using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace DarkestDungeonSaveEditor.Core;

public sealed partial class SaveEditService
{
    private static readonly string[] QuestBoardSaveNames =
        ["persist.game.json", "persist.quest.json", "persist.estate.json", "persist.roster.json", "persist.town.json"];

    public async Task<PreparedQuestBoardEdit> PrepareQuestBoardEditAsync(SaveProfile profile, QuestBoardEditRequest request,
        ActiveContentSnapshot content, CancellationToken cancellationToken = default)
    {
        EnsureGameIsNotRunning();
        _codec.ValidateAvailability();
        ValidateProfile(profile);
        ValidateActiveContentSnapshot(profile, content);
        TownSceneGuard.RequireTown(content);
        EnsureNoUnfinishedStagecoachTransaction(profile);
        if (!Enum.IsDefined(request.Kind) ||
            (request.Kind == QuestBoardEditKind.Create) != (request.SaveKey is null) ||
            (request.Kind == QuestBoardEditKind.Delete) != (request.TemplateKey is null))
            throw new ArgumentException("Invalid quest board operation.");
        var fingerprint = ProfileCatalogContentFingerprint.Capture(content.Sources, cancellationToken);
        var hashes = QuestBoardSaveNames.ToDictionary(name => name, name =>
            File.Exists(GetProfileSavePath(profile, name)) ? ComputeSha256(GetProfileSavePath(profile, name)) : null);
        if (!string.Equals(hashes["persist.quest.json"], request.SourceSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(EditorText.Get("QuestBoard_SaveChanged"));
        var session = $"{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}";
        var workspace = Path.Combine(_locations.WorkspaceDirectory, session);
        foreach (var directory in new[] { "source", "decoded", "proposed", "encoded", "roundtrip" })
            Directory.CreateDirectory(Path.Combine(workspace, directory));
        var saves = new Dictionary<string, JsonObject>();
        foreach (var name in QuestBoardSaveNames)
        {
            if (hashes[name] is null) throw new FileNotFoundException($"Required quest board save is missing: {name}.");
            var sourcePath = Path.Combine(workspace, "source", name);
            var decodedPath = Path.Combine(workspace, "decoded", name);
            File.Copy(GetProfileSavePath(profile, name), sourcePath);
            ValidateUnchangedDuringCopy(GetProfileSavePath(profile, name), sourcePath, hashes[name]!, name);
            await _codec.DecodeAsync(sourcePath, decodedPath, cancellationToken).ConfigureAwait(false);
            saves[name] = JsonSupport.ReadObject(decodedPath);
        }
        TownSceneGuard.RequireTown(saves["persist.game.json"]);
        var document = saves["persist.quest.json"];
        var root = JsonSupport.RequireObject(document, "base_root");
        var board = JsonSupport.RequireObject(root, "quests");
        var before = QuestBoardSnapshotReader.Read(content, document, request.SourceSha256);
        var previous = request.SaveKey is null ? null : before.Quests.SingleOrDefault(item => item.SaveKey == request.SaveKey)
            ?? throw new InvalidOperationException(EditorText.Get("QuestBoard_SaveChanged"));
        var originalOrder = board.Select(pair => pair.Key).ToArray();
        if (request.SaveKey is not null) board.Remove(request.SaveKey);
        JsonObject? created = null;
        if (request.Kind != QuestBoardEditKind.Delete)
            created = new TownEventInitializer(content, RandomNumberGenerator.GetInt32(int.MaxValue))
                .CreateBoardQuest(saves, request.TemplateKey!);
        if (request.Kind == QuestBoardEditKind.Replace)
        {
            // Replace in place as one mutation. No ignored/failed-quest callback,
            // retained-item removal, progression edit or persistent-map cleanup.
            var ordered = new JsonObject();
            foreach (var key in originalOrder)
                ordered[key] = (key == request.SaveKey ? created : board[key])?.DeepClone();
            root["quests"] = ordered;
        }
        var after = QuestBoardSnapshotReader.Read(content, document, "");
        var result = created is null ? null : request.Kind == QuestBoardEditKind.Replace
            ? after.Quests.Single(item => item.SaveKey == request.SaveKey) : after.Quests.Last();
        var preview = new QuestBoardMutationPreview(request.Kind, previous, result, []);
        const string fileName = "persist.quest.json";
        var source = Path.Combine(workspace, "source", fileName);
        var proposed = Path.Combine(workspace, "proposed", fileName);
        var encoded = Path.Combine(workspace, "encoded", fileName);
        var roundTrip = Path.Combine(workspace, "roundtrip", fileName);
        JsonSupport.WriteObject(proposed, document);
        await _codec.EncodeAsync(proposed, encoded, source, cancellationToken).ConfigureAwait(false);
        await _codec.DecodeAsync(encoded, roundTrip, cancellationToken).ConfigureAwait(false);
        if (!JsonNode.DeepEquals(document, JsonSupport.ReadObject(roundTrip)))
            throw new InvalidDataException("Quest board DSON roundtrip failed.");
        var isDson = DsonSaveCodec.IsDson(source);
        if (isDson && !RevisionMatches(source, encoded)) throw new InvalidDataException("Quest board DSON revision changed.");
        var file = new PreparedSaveFile(fileName, GetProfileSavePath(profile, fileName), source, proposed,
            encoded, roundTrip, hashes[fileName]!, ComputeSha256(encoded), isDson);
        var prepared = new PreparedQuestBoardEdit(session, profile, preview, content, fingerprint, hashes, workspace, file, DateTime.UtcNow);
        ValidateQuestBoardGuard(prepared, false, cancellationToken);
        WriteJson(Path.Combine(workspace, "session.json"), prepared);
        return prepared;
    }

    public async Task<SaveCommitResult> CommitAsync(PreparedQuestBoardEdit prepared, CancellationToken cancellationToken = default)
    {
        EnsureNoUnfinishedStagecoachTransaction(prepared.Profile);
        ValidateProfile(prepared.Profile);
        var file = prepared.File;
        if (file.FileName != "persist.quest.json" ||
            !prepared.SourceHashes.TryGetValue(file.FileName, out var hash) || hash != file.OriginalSha256)
            throw new InvalidDataException("Invalid quest board transaction target.");
        ValidatePreparedStagecoachFile(prepared.Profile, file, "persist.quest.json");
        if (ComputeSha256(file.EncodedPath) != file.EncodedSha256)
            throw new InvalidDataException("The prepared quest board changed after validation.");
        ValidateQuestBoardGuard(prepared, false, cancellationToken);
        var backup = CreateQuestBoardBackup(prepared);
        ValidateQuestBoardGuard(prepared, false, cancellationToken);
        using var replacement = new GuardedSaveReplacement(file.TargetPath, file.EncodedPath,
            file.OriginalSha256, file.EncodedSha256, BeforeTargetReplace, AfterTargetReplace);
        try
        {
            replacement.Replace();
            ValidateQuestBoardGuard(prepared, true, cancellationToken);
            var result = new SaveCommitResult(prepared.Profile.ProfileDirectory, file.TargetPath, backup,
                file.OriginalSha256, replacement.Verify(), DateTime.UtcNow);
            WriteJson(Path.Combine(backup, "commit-result.json"), result);
            replacement.Complete();
            await Task.CompletedTask.ConfigureAwait(false);
            return result;
        }
        catch (Exception error)
        {
            if (!replacement.HasReplaced) throw;
            TryDeleteFile(Path.Combine(backup, "commit-result.json"));
            SaveFileRecovery recovery;
            try { recovery = replacement.Recover(); }
            catch (Exception restoreError)
            { throw new AggregateException($"Quest board commit and recovery failed. Backup: {backup}.", error, restoreError); }
            throw new InvalidOperationException(EditorText.Format("SaveEditService_Trinkets_001", GuardedSaveReplacement.DescribeRecovery(recovery)) +
                EditorText.Format("BattleMapEditService_007", backup, replacement.DisplacedPath), error);
        }
    }

    private static void ValidateQuestBoardGuard(PreparedQuestBoardEdit prepared, bool replaced, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        EnsureGameIsNotRunning();
        ValidateActiveContentSnapshot(prepared.Profile, prepared.Content);
        TownSceneGuard.RequireTown(prepared.Content);
        foreach (var name in QuestBoardSaveNames)
        {
            if (!prepared.SourceHashes.TryGetValue(name, out var original)) throw new InvalidDataException("Incomplete quest board save guard.");
            var expected = replaced && name == prepared.File.FileName ? prepared.File.EncodedSha256 : original;
            var path = GetProfileSavePath(prepared.Profile, name);
            if ((File.Exists(path) ? ComputeSha256(path) : null) != expected)
                throw new InvalidOperationException(EditorText.Get("QuestBoard_SaveChanged"));
        }
        if (ProfileCatalogContentFingerprint.Capture(prepared.Content.Sources, token) != prepared.ContentFingerprint)
            throw new InvalidOperationException(EditorText.Get("QuestBoard_DefinitionChanged"));
    }

    private string CreateQuestBoardBackup(PreparedQuestBoardEdit prepared)
    {
        var profile = prepared.Profile;
        var directory = Path.Combine(_locations.BackupDirectory, SanitizePathSegment(profile.SteamUserId),
            SanitizePathSegment(profile.ProfileId), $"{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var entries = new List<object>();
        foreach (var path in ProfileSaveFiles.Enumerate(profile.ProfileDirectory))
        {
            var destination = ProfileSaveFiles.BackupPath(profile.ProfileDirectory, directory, path);
            var hash = ComputeSha256(path);
            File.Copy(path, destination);
            ValidateUnchangedDuringCopy(path, destination, hash, Path.GetFileName(path));
            entries.Add(new { fileName = Path.GetRelativePath(profile.ProfileDirectory, path), sha256 = hash });
        }
        WriteJson(Path.Combine(directory, "backup-manifest.json"), new { version = 1, operation = "edit-quest-board",
            createdAtUtc = DateTime.UtcNow, profile.ProfileDirectory, prepared.SessionId, prepared.Preview, files = entries });
        return directory;
    }
}
