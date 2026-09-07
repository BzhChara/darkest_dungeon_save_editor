using System.Text.Json.Nodes;

namespace DarkestDungeonSaveEditor.Core;

public sealed partial class ManagedBattleEncounterBridgeService
{
    private void RecoverInterruptedMaintenance(SaveProfile profile, string gameDirectory, string? localModDirectory)
    {
        var journalRoot = Path.Combine(_locations.BackupDirectory, SanitizeSegment(profile.SteamUserId, 48), SanitizeSegment(profile.ProfileId, 48));
        if (!Directory.Exists(journalRoot)) return;
        var installRoot = Path.GetFullPath(string.IsNullOrWhiteSpace(localModDirectory) ||
            File.Exists(Path.Combine(localModDirectory, "project.xml")) ? Path.Combine(gameDirectory, "mods") : localModDirectory);
        var package = Path.Combine(installRoot, GetPackageDirectoryName(profile));
        foreach (var backup in Directory.EnumerateDirectories(journalRoot).Order(StringComparer.Ordinal))
        {
            if (!File.Exists(Path.Combine(backup, "maintenance-pending.json")) ||
                SaveCommitMarker.IsComplete(Path.Combine(backup, "commit-result.json"), profile.ProfileDirectory) ||
                File.Exists(Path.Combine(backup, "maintenance-recovered.json"))) continue;
            var manifest = JsonSupport.ReadObject(Path.Combine(backup, "backup-manifest.json"));
            if (JsonSupport.ReadString(manifest, "operation") != EditorBattleHistory.CleanupOperation ||
                JsonSupport.ReadString(manifest, "ProfileId") != profile.ProfileId ||
                JsonSupport.ReadString(manifest, "SteamUserId") != profile.SteamUserId ||
                !Path.GetFullPath(JsonSupport.ReadString(manifest, "ProfileDirectory"))
                    .Equals(Path.GetFullPath(profile.ProfileDirectory), StringComparison.OrdinalIgnoreCase)) continue;
            EnsureGameIsNotRunning();
            using var gameLock = new FileStream(Path.Combine(profile.ProfileDirectory, "persist.game.json"),
                FileMode.Open, FileAccess.Read, FileShare.Read);
            var raidPath = Path.Combine(profile.ProfileDirectory, "persist.raid.json");
            using var raidLock = File.Exists(raidPath) ? new FileStream(raidPath, FileMode.Open, FileAccess.Read, FileShare.Read) : null;
            if (ComputeSha256(Path.Combine(profile.ProfileDirectory, "persist.game.json")) != JsonSupport.ReadString(manifest, "GameSha256") ||
                (File.Exists(raidPath) ? ComputeSha256(raidPath) : null) != manifest["RaidSha256"]?.GetValue<string>())
                throw new IOException($"存档在清理中断后已变化，不能自动恢复旧地图；请检查备份：{backup}");
            var plan = new List<(string Target, string Original, string OriginalHash, string FinalHash)>();
            foreach (var file in manifest["Files"]!.AsArray().OfType<JsonObject>())
            {
                var target = Path.GetFullPath(JsonSupport.ReadString(file, "TargetPath"));
                var mapPath = Path.GetFullPath(Path.Combine(profile.ProfileDirectory, "persist.map.json"));
                var isMap = target.Equals(mapPath, StringComparison.OrdinalIgnoreCase);
                if (!isMap && !IsWithinDirectory(target, package))
                    throw new InvalidDataException($"中断清理计划包含不属于本档案的路径：{target}");
                var original = isMap ? Path.Combine(backup, "persist.map.json") :
                    Path.Combine(backup, "bridge-package", Path.GetRelativePath(package, target));
                var before = JsonSupport.ReadString(file, "OriginalSha256");
                var after = JsonSupport.ReadString(file, "FinalSha256");
                if (!File.Exists(original) || ComputeSha256(original) != before || !File.Exists(target))
                    throw new IOException($"中断清理的文件或备份不完整，需检查备份后恢复：{backup}");
                var current = ComputeSha256(target);
                if (current != before && current != after)
                    throw new IOException($"检测到清理中断后的外部修改，已保留该版本；请检查备份：{backup}");
                if (current == after && before != after) plan.Add((target, original, before, after));
            }
            var replacements = new List<GuardedSaveReplacement>();
            try
            {
                // Restore the old package before restoring its map references.
                foreach (var entry in plan.OrderBy(entry => entry.Target.EndsWith("persist.map.json", StringComparison.OrdinalIgnoreCase) ? 1 : 0))
                {
                    var replacement = new GuardedSaveReplacement(entry.Target, entry.Original, entry.FinalHash, entry.OriginalHash);
                    replacements.Add(replacement);
                    replacement.Replace();
                }
                foreach (var replacement in replacements) replacement.Verify();
                File.WriteAllText(Path.Combine(backup, "maintenance-recovered.json"), "{}", Utf8NoBom);
                foreach (var replacement in replacements) replacement.Complete();
                _maintenanceCheckedKey = null;
            }
            finally
            {
                // An interrupted recovery remains pending and can resume: each
                // target is still checked against the exact before/after bytes.
                foreach (var replacement in replacements) replacement.Dispose();
            }
        }
    }
}
