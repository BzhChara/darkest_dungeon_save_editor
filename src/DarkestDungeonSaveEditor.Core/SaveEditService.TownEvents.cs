using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DarkestDungeonSaveEditor.Core;

public sealed partial class SaveEditService
{
    private static readonly string[] TownEventSaveNames =
    [
        "persist.game.json", "persist.town_event.json", "persist.campaign_log.json", "persist.estate.json",
        "persist.roster.json", "persist.town.json", "persist.upgrades.json", "persist.quest.json", "persist.progression.json"
    ];

    public async Task<PreparedTownEventEdit> PrepareTownEventEditAsync(SaveProfile profile,
        TownEventDefinition selectedEvent, ActiveContentSnapshot content, CancellationToken cancellationToken = default)
    {
        EnsureGameIsNotRunning();
        _codec.ValidateAvailability();
        ValidateProfile(profile);
        ValidateActiveContentSnapshot(profile, content);
        TownSceneGuard.RequireTown(content);
        var unavailable = TownEventAvailability.UnavailableReason(selectedEvent);
        if (unavailable.Length > 0) throw new InvalidOperationException(unavailable);
        EnsureNoUnfinishedStagecoachTransaction(profile);
        var fingerprint = ProfileCatalogContentFingerprint.Capture(content.Sources, cancellationToken);
        var current = TownEventCatalog.Load(content).Events.SingleOrDefault(item => item.NativeId == selectedEvent.NativeId);
        if (current is null || current.HasProviderConflict || current.Id != selectedEvent.Id ||
            !current.Effects.SequenceEqual(selectedEvent.Effects))
            throw new InvalidOperationException(EditorText.Get("TownEvent_Unresolved"));

        var hashes = TownEventSaveNames.ToDictionary(name => name, name =>
            File.Exists(GetProfileSavePath(profile, name)) ? ComputeSha256(GetProfileSavePath(profile, name)) : null);
        var session = $"{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}";
        var workspace = Path.Combine(_locations.WorkspaceDirectory, session);
        foreach (var directory in new[] { "source", "decoded", "proposed", "encoded", "roundtrip" })
            Directory.CreateDirectory(Path.Combine(workspace, directory));
        var saves = new Dictionary<string, JsonObject>();
        foreach (var name in TownEventSaveNames.Where(name => hashes[name] is not null))
        {
            var source = Path.Combine(workspace, "source", name);
            var decoded = Path.Combine(workspace, "decoded", name);
            File.Copy(GetProfileSavePath(profile, name), source);
            ValidateUnchangedDuringCopy(GetProfileSavePath(profile, name), source, hashes[name]!, name);
            await _codec.DecodeAsync(source, decoded, cancellationToken).ConfigureAwait(false);
            saves[name] = JsonSupport.ReadObject(decoded);
        }
        foreach (var name in new[] { "persist.game.json", "persist.town_event.json", "persist.campaign_log.json" })
            if (!saves.ContainsKey(name)) throw new FileNotFoundException($"Required event save is missing: {name}.");
        var originals = saves.ToDictionary(pair => pair.Key, pair => (JsonObject)pair.Value.DeepClone());
        var preview = TownEventSaveEditor.ReplaceCurrent(saves, current,
            new TownEventInitializer(content, RandomNumberGenerator.GetInt32(int.MaxValue)).Initialize);
        var files = new List<PreparedSaveFile>();
        // Publish the current event after all of its owned data and dependencies.
        foreach (var name in TownEventSaveNames.Where(name => saves.ContainsKey(name))
                     .OrderBy(name => name == "persist.town_event.json" ? 1 : 0))
        {
            // Reinitializing the same event can leave its identity/history intact.
            // Keep both required event files in the guarded transaction anyway.
            if (name is not ("persist.town_event.json" or "persist.campaign_log.json") &&
                JsonNode.DeepEquals(originals[name], saves[name])) continue;
            var source = Path.Combine(workspace, "source", name);
            var proposed = Path.Combine(workspace, "proposed", name);
            var encoded = Path.Combine(workspace, "encoded", name);
            var roundTrip = Path.Combine(workspace, "roundtrip", name);
            JsonSupport.WriteObject(proposed, saves[name]);
            await _codec.EncodeAsync(proposed, encoded, source, cancellationToken).ConfigureAwait(false);
            await _codec.DecodeAsync(encoded, roundTrip, cancellationToken).ConfigureAwait(false);
            if (!JsonNode.DeepEquals(saves[name], JsonSupport.ReadObject(roundTrip)))
                throw new InvalidDataException($"Town event DSON roundtrip failed: {name}.");
            var isDson = DsonSaveCodec.IsDson(source);
            if (isDson && !RevisionMatches(source, encoded))
                throw new InvalidDataException($"Town event DSON revision changed: {name}.");
            files.Add(new PreparedSaveFile(name, GetProfileSavePath(profile, name), source, proposed,
                encoded, roundTrip, hashes[name]!, ComputeSha256(encoded), isDson));
        }
        var prepared = new PreparedTownEventEdit(session, profile, preview, content, fingerprint, hashes,
            workspace, files, DateTime.UtcNow);
        ValidateTownEventGuard(prepared, [], cancellationToken, checkContent: true);
        WriteJson(Path.Combine(workspace, "session.json"), prepared);
        return prepared;
    }

    public async Task<MultiFileSaveCommitResult> CommitAsync(PreparedTownEventEdit prepared,
        CancellationToken cancellationToken = default)
    {
        EnsureNoUnfinishedStagecoachTransaction(prepared.Profile);
        ValidateProfile(prepared.Profile);
        if (prepared.Files.Count == 0 || prepared.Files.Select(file => file.FileName).Distinct().Count() != prepared.Files.Count ||
            !prepared.Files.Any(file => file.FileName == "persist.town_event.json") ||
            !prepared.Files.Any(file => file.FileName == "persist.campaign_log.json"))
            throw new InvalidDataException("Incomplete prepared town event transaction.");
        foreach (var file in prepared.Files)
        {
            if (!TownEventSaveNames.Contains(file.FileName) || file.FileName == "persist.game.json" ||
                !prepared.SourceHashes.TryGetValue(file.FileName, out var original) || original != file.OriginalSha256)
                throw new InvalidDataException("Invalid town event transaction target.");
            ValidatePreparedStagecoachFile(prepared.Profile, file, file.FileName);
            if (ComputeSha256(file.EncodedPath) != file.EncodedSha256)
                throw new InvalidDataException($"Prepared event file changed: {file.FileName}.");
        }
        ValidateTownEventGuard(prepared, [], cancellationToken, checkContent: true);
        var backup = CreateTownEventBackup(prepared);
        var replacements = new List<GuardedSaveReplacement>();
        var replaced = new List<PreparedSaveFile>();
        void State(string status, string? error = null) => WriteJsonAtomic(Path.Combine(backup, "transaction-state.json"),
            new { version = 1, prepared.SessionId, operation = "replace-town-event", status,
                replacedFiles = replaced.Select(file => file.FileName).ToArray(), error });
        void TryState(string status, string? error) { try { State(status, error); } catch { /* Never interrupt recovery. */ } }
        State("backup_complete");
        try
        {
            foreach (var file in prepared.Files)
            {
                ValidateTownEventGuard(prepared, replaced, cancellationToken, checkContent: false);
                State("replacing_" + file.FileName);
                var replacement = new GuardedSaveReplacement(file.TargetPath, file.EncodedPath,
                    file.OriginalSha256, file.EncodedSha256, BeforeTargetReplace, AfterTargetReplace);
                replacements.Add(replacement);
                try { replacement.Replace(); }
                finally { if (replacement.HasReplaced) replaced.Add(file); }
                State("replaced_" + file.FileName);
            }
            ValidateTownEventGuard(prepared, replaced, cancellationToken, checkContent: true);
            var results = prepared.Files.Select(file => new SaveFileCommitResult(file.FileName, file.TargetPath,
                file.OriginalSha256, replacements.Single(item => item.TargetPath == file.TargetPath).Verify())).ToArray();
            var result = new MultiFileSaveCommitResult(prepared.Profile.ProfileDirectory, backup, results, DateTime.UtcNow);
            WriteJson(Path.Combine(backup, "commit-result.json"), result);
            State("committed");
            foreach (var replacement in replacements) replacement.Complete();
            await Task.CompletedTask.ConfigureAwait(false);
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
                throw new AggregateException($"Town event commit and recovery failed. Backup: {backup}.", error, recovery);
            }
            TryState("restored", error.Message);
            throw new InvalidOperationException($"Town event commit failed; replaced files were restored. Backup: {backup}.", error);
        }
        finally { foreach (var replacement in replacements) replacement.Dispose(); }
    }

    private static void ValidateTownEventGuard(PreparedTownEventEdit prepared, IReadOnlyList<PreparedSaveFile> replaced,
        CancellationToken token, bool checkContent)
    {
        token.ThrowIfCancellationRequested();
        EnsureGameIsNotRunning();
        ValidateActiveContentSnapshot(prepared.Profile, prepared.Content);
        TownSceneGuard.RequireTown(prepared.Content);
        foreach (var name in TownEventSaveNames)
        {
            if (!prepared.SourceHashes.TryGetValue(name, out var original))
                throw new InvalidDataException("Incomplete town event save guard.");
            var expected = replaced.SingleOrDefault(file => file.FileName == name)?.EncodedSha256 ?? original;
            var path = GetProfileSavePath(prepared.Profile, name);
            var actual = File.Exists(path) ? ComputeSha256(path) : null;
            if (actual != expected) throw new InvalidOperationException($"{name} changed after preview; preview again.");
        }
        if (checkContent && ProfileCatalogContentFingerprint.Capture(prepared.Content.Sources, token) != prepared.ContentFingerprint)
            throw new InvalidOperationException("Active event resources changed; reload the catalog and preview again.");
    }

    private string CreateTownEventBackup(PreparedTownEventEdit prepared)
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
        WriteJson(Path.Combine(directory, "backup-manifest.json"), new { version = 1, operation = "replace-town-event",
            createdAtUtc = DateTime.UtcNow, profile.ProfileDirectory, prepared.SessionId, prepared.Preview, files = entries });
        return directory;
    }
}
