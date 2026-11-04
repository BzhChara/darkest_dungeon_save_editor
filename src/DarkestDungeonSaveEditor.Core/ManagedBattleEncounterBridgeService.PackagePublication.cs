using System.Security.Cryptography;

namespace DarkestDungeonSaveEditor.Core;

public sealed partial class ManagedBattleEncounterBridgeService
{
    private sealed class PackagePublication(string directory, string originalDirectory, Action requireGameStopped) : IDisposable
    {
        private readonly List<GuardedSaveReplacement> _replacements = [];
        private readonly List<(string Path, string Hash)> _newFiles = [];
        private readonly List<string> _newDirectories = [];
        private readonly List<FileStream> _readGuards = [];

        internal bool HasChanges => _newDirectories.Count > 0 || _newFiles.Count > 0 ||
                                    _replacements.Any(replacement => replacement.HasReplaced);

        internal void Publish(string stagedDirectory) => CopyDirectory(stagedDirectory, directory, PublishFile, EnsureDirectory);

        private void EnsureDirectory(string path)
        {
            if (Directory.Exists(path))
            {
                RejectReparsePoint(path, EditorText.Get("ManagedBattleEncounterBridgeService_005"));
                return;
            }
            Directory.CreateDirectory(path);
            _newDirectories.Add(path);
        }

        private void PublishFile(string source, string target)
        {
            var original = Path.Combine(originalDirectory, Path.GetRelativePath(directory, target));
            var hash = ComputeSha256(source);
            if (File.Exists(original))
            {
                var originalHash = ComputeSha256(original);
                if (originalHash == hash)
                {
                    GuardPublishedFile(target, originalHash);
                    return;
                }
                var replacement = new GuardedSaveReplacement(target, source, originalHash, hash, requireGameStopped: requireGameStopped);
                _replacements.Add(replacement);
                replacement.Replace();
                return;
            }

            var temporary = target + $".ddse-{Guid.NewGuid():N}.tmp";
            try
            {
                File.Copy(source, temporary, overwrite: false);
                requireGameStopped();
                File.Move(temporary, target);
                _newFiles.Add((target, hash));
                GuardPublishedFile(target, hash);
            }
            finally { TryDeleteFile(temporary); }
        }

        private void GuardPublishedFile(string path, string expectedHash)
        {
            var guard = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            _readGuards.Add(guard);
            if (!Convert.ToHexString(SHA256.HashData(guard)).Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new IOException(EditorText.Format("GuardedSaveReplacement_005", path));
        }

        internal void Complete()
        {
            foreach (var replacement in _replacements) replacement.Complete();
        }

        internal void Recover(List<Exception> errors)
        {
            ReleaseReadGuards();
            foreach (var replacement in _replacements.AsEnumerable().Reverse().Where(file => file.HasReplaced))
            {
                try
                {
                    var result = replacement.Recover();
                    if (result != SaveFileRecovery.RestoredOriginal)
                        throw new IOException($"{replacement.TargetPath}: {GuardedSaveReplacement.DescribeRecovery(result)}");
                    // The original also exists in the transaction's workspace.
                    File.Delete(replacement.DisplacedPath);
                }
                catch (Exception error) { errors.Add(error); }
            }
            foreach (var file in _newFiles.AsEnumerable().Reverse())
            {
                try { RemovePublishedFile(file.Path, file.Hash); }
                catch (Exception error) { errors.Add(error); }
            }
            foreach (var path in _newDirectories.AsEnumerable().Reverse())
            {
                try
                {
                    if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
                        Directory.Delete(path);
                }
                catch (Exception error) { errors.Add(error); }
            }
        }

        private static void RemovePublishedFile(string path, string expectedHash)
        {
            // Inspect the actual version removed by the rename, so rollback cannot
            // delete a newer file that appeared after an earlier check of this path.
            var displaced = path + $".ddse-{Guid.NewGuid():N}.tmp";
            File.Move(path, displaced);
            try
            {
                if (ComputeSha256(displaced) != expectedHash)
                    throw new IOException(EditorText.Format("GuardedSaveReplacement_005", path));
                File.Delete(displaced);
            }
            catch
            {
                File.Move(displaced, path, overwrite: false);
                throw;
            }
        }

        public void Dispose()
        {
            ReleaseReadGuards();
            foreach (var replacement in _replacements) replacement.Dispose();
        }

        private void ReleaseReadGuards()
        {
            foreach (var guard in _readGuards) guard.Dispose();
            _readGuards.Clear();
        }
    }
}
