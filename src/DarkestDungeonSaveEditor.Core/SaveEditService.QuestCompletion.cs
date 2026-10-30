using System.Text.Json.Nodes;

namespace DarkestDungeonSaveEditor.Core;

public sealed partial class SaveEditService
{
    private static readonly string[] QuestCompletionSaveNames = ["persist.game.json", "persist.map.json", "persist.raid.json"];

    public async Task<PreparedQuestCompletionEdit> PrepareQuestCompletionAsync(SaveProfile profile,
        BattleMapSnapshot snapshot, ActiveContentSnapshot content, CancellationToken cancellationToken = default)
    {
        EnsureGameIsNotRunning();
        _codec.ValidateAvailability();
        ValidateProfile(profile);
        ValidateActiveContentSnapshot(profile, content);
        EnsureNoUnfinishedStagecoachTransaction(profile);
        var fingerprint = ProfileCatalogContentFingerprint.Capture(content.Sources, cancellationToken);
        var session = $"{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}";
        var workspace = Path.Combine(_locations.WorkspaceDirectory, session);
        using var guard = await BattleMapWriteGuard.LoadAsync(profile, snapshot, _codec, workspace, cancellationToken).ConfigureAwait(false);
        var location = RaidSaveLocation.FromGame(profile.ProfileDirectory,
            JsonSupport.ReadObject(Path.Combine(workspace, "decoded", "persist.game.json")));
        profile = location.Bind(profile);
        if (!guard.GameSha256.Equals(content.SourceGameSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(EditorText.Get("QuestCompletion_Changed"));
        var hashes = new Dictionary<string, string>
        {
            ["persist.game.json"] = guard.GameSha256,
            ["persist.map.json"] = guard.Snapshot.MapSha256,
            ["persist.raid.json"] = guard.Snapshot.RaidSha256
        };
        var changed = QuestCompletionSaveEditor.Complete(guard.MapDocument, guard.RaidDocument, guard.Snapshot,
            QuestBoardSnapshotReader.ReadGoalDefinitions(content), raid => QuantityItemCatalog.LoadRaid(content, raid));
        var documents = new[]
        {
            (Name: "persist.map.json", Before: guard.MapDocument, After: changed.Map),
            (Name: "persist.raid.json", Before: guard.RaidDocument, After: changed.Raid)
        };
        var files = new List<PreparedSaveFile>();
        foreach (var directory in new[] { "proposed", "encoded", "roundtrip" })
            Directory.CreateDirectory(Path.Combine(workspace, directory));
        foreach (var (name, before, after) in documents)
        {
            if (JsonNode.DeepEquals(before, after)) continue;
            var source = Path.Combine(workspace, "source", name);
            var proposed = Path.Combine(workspace, "proposed", name);
            var encoded = Path.Combine(workspace, "encoded", name);
            var roundtrip = Path.Combine(workspace, "roundtrip", name);
            JsonSupport.WriteObject(proposed, after);
            await _codec.EncodeAsync(proposed, encoded, source, cancellationToken).ConfigureAwait(false);
            await _codec.DecodeAsync(encoded, roundtrip, cancellationToken).ConfigureAwait(false);
            if (!JsonNode.DeepEquals(after, JsonSupport.ReadObject(roundtrip)))
                throw new InvalidDataException("Quest completion DSON roundtrip failed: " + name);
            var dson = DsonSaveCodec.IsDson(source);
            if (dson && !RevisionMatches(source, encoded))
                throw new InvalidDataException("Quest completion DSON revision changed: " + name);
            files.Add(new(name, location.GetPath(name), source, proposed, encoded, roundtrip,
                hashes[name], ComputeSha256(encoded), dson));
        }
        if (files.Count == 0) throw new InvalidOperationException(EditorText.Get("QuestCompletion_Already"));
        var prepared = new PreparedQuestCompletionEdit(session, profile, changed.Preview, content, fingerprint,
            hashes, workspace, files, DateTime.UtcNow);
        ValidateQuestCompletionGuard(prepared, [], cancellationToken, checkContent: true);
        WriteJson(Path.Combine(workspace, "session.json"), prepared);
        return prepared;
    }

    public async Task<MultiFileSaveCommitResult> CommitAsync(PreparedQuestCompletionEdit prepared,
        CancellationToken cancellationToken = default)
    {
        EnsureNoUnfinishedStagecoachTransaction(prepared.Profile);
        ValidateProfile(prepared.Profile);
        if (prepared.Files.Count == 0 || prepared.Files.Count > 2 ||
            prepared.Files.Select(file => file.FileName).Distinct().Count() != prepared.Files.Count)
            throw new InvalidDataException("Invalid quest completion transaction.");
        var location = new RaidSaveLocation(prepared.Profile.ProfileDirectory, prepared.Profile.RaidSaveRelativeDirectory);
        foreach (var file in prepared.Files)
        {
            if (file.FileName is not ("persist.map.json" or "persist.raid.json") ||
                !prepared.SourceHashes.TryGetValue(file.FileName, out var original) || original != file.OriginalSha256 ||
                !Path.GetFullPath(file.TargetPath).Equals(location.GetPath(file.FileName), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Invalid quest completion target.");
            if (ComputeSha256(file.EncodedPath) != file.EncodedSha256)
                throw new InvalidDataException(EditorText.Get("QuestCompletion_Changed"));
        }
        // Decode the active path again rather than trusting a caller-supplied record.
        var liveLocation = await RaidSaveLocation.ReadAsync(prepared.Profile.ProfileDirectory, _codec, cancellationToken).ConfigureAwait(false);
        if (!liveLocation.RaidPath.Equals(location.RaidPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(EditorText.Get("QuestCompletion_Changed"));
        ValidateQuestCompletionGuard(prepared, [], cancellationToken, checkContent: true);
        var backup = CreateQuestCompletionBackup(prepared);
        var replacements = new List<GuardedSaveReplacement>();
        var replaced = new List<PreparedSaveFile>();
        void State(string status, string? error = null) => WriteJsonAtomic(Path.Combine(backup, "transaction-state.json"),
            new { version = 1, operation = "complete-quest-objectives", prepared.SessionId, status,
                replacedFiles = replaced.Select(file => Path.GetRelativePath(prepared.Profile.ProfileDirectory, file.TargetPath)).ToArray(), error });
        void TryState(string status, string? error) { try { State(status, error); } catch { /* Preserve recovery. */ } }
        State("backup_complete");
        try
        {
            foreach (var file in prepared.Files)
            {
                ValidateQuestCompletionGuard(prepared, replaced, cancellationToken, checkContent: false);
                State("replacing_" + file.FileName);
                var replacement = new GuardedSaveReplacement(file.TargetPath, file.EncodedPath,
                    file.OriginalSha256, file.EncodedSha256, BeforeTargetReplace, AfterTargetReplace);
                replacements.Add(replacement);
                try { replacement.Replace(); }
                finally { if (replacement.HasReplaced) replaced.Add(file); }
                State("replaced_" + file.FileName);
            }
            ValidateQuestCompletionGuard(prepared, replaced, cancellationToken, checkContent: true);
            var results = prepared.Files.Select(file => new SaveFileCommitResult(file.FileName, file.TargetPath,
                file.OriginalSha256, replacements.Single(item => item.TargetPath == file.TargetPath).Verify())).ToArray();
            var result = new MultiFileSaveCommitResult(prepared.Profile.ProfileDirectory, backup, results, DateTime.UtcNow);
            WriteJsonAtomic(Path.Combine(backup, "commit-result.json"), result);
            State("committed");
            foreach (var replacement in replacements) replacement.Complete();
            return result;
        }
        catch (Exception error)
        {
            TryDeleteFile(Path.Combine(backup, "commit-result.json"));
            TryState("commit_failed_restoring", error.Message);
            try { RestoreReplacedStagecoachFiles(prepared.Files, replacements); }
            catch (Exception recovery)
            {
                TryState("restore_failed", recovery.Message);
                throw new AggregateException(EditorText.Format("QuestCompletion_RecoveryFailed", backup), error, recovery);
            }
            TryState("restored", error.Message);
            throw new InvalidOperationException(EditorText.Format("QuestCompletion_Restored", backup), error);
        }
        finally { foreach (var replacement in replacements) replacement.Dispose(); }
    }

    private static void ValidateQuestCompletionGuard(PreparedQuestCompletionEdit prepared, IReadOnlyList<PreparedSaveFile> replaced,
        CancellationToken token, bool checkContent)
    {
        token.ThrowIfCancellationRequested();
        EnsureGameIsNotRunning();
        ValidateActiveContentSnapshot(prepared.Profile, prepared.Content);
        var location = new RaidSaveLocation(prepared.Profile.ProfileDirectory, prepared.Profile.RaidSaveRelativeDirectory);
        foreach (var name in QuestCompletionSaveNames)
        {
            if (!prepared.SourceHashes.TryGetValue(name, out var original))
                throw new InvalidDataException("Incomplete quest completion save guard.");
            var expected = replaced.SingleOrDefault(file => file.FileName == name)?.EncodedSha256 ?? original;
            if (ComputeSha256(location.GetPath(name)) != expected)
                throw new InvalidOperationException(EditorText.Get("QuestCompletion_Changed"));
        }
        if (checkContent && ProfileCatalogContentFingerprint.Capture(prepared.Content.Sources, token) != prepared.ContentFingerprint)
            throw new InvalidOperationException(EditorText.Get("QuestBoard_DefinitionChanged"));
    }

    private string CreateQuestCompletionBackup(PreparedQuestCompletionEdit prepared)
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
        WriteJson(Path.Combine(directory, "backup-manifest.json"), new { version = 1, operation = "complete-quest-objectives",
            createdAtUtc = DateTime.UtcNow, profile.ProfileDirectory, prepared.SessionId, prepared.Preview, files = entries });
        return directory;
    }
}
