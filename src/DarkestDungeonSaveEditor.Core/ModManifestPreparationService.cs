using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DarkestDungeonSaveEditor.Core;

public sealed record ModManifestPreparationResult(int CreatedCount, string? ReceiptPath);

/// <summary>Explicit profile-load preparation. Catalog readers themselves never write Mods.</summary>
public sealed class ModManifestPreparationService
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private readonly SaveEditorLocations _locations;
    private readonly Func<bool> _gameRunning;
    private readonly Func<string, string, string, CancellationToken, Task<byte[]>> _generate;

    public ModManifestPreparationService(SaveEditorLocations? locations = null, Func<bool>? gameRunningProbe = null)
        : this(locations, gameRunningProbe, OfficialModManifestGenerator.GenerateAsync) { }

    internal ModManifestPreparationService(SaveEditorLocations? locations, Func<bool>? gameRunningProbe,
        Func<string, string, string, CancellationToken, Task<byte[]>> generate)
    {
        _locations = locations ?? SaveEditorLocations.CreateDefault();
        _gameRunning = gameRunningProbe ?? (() =>
        {
            var processes = Process.GetProcessesByName("Darkest");
            try { return processes.Length != 0; }
            finally { foreach (var process in processes) process.Dispose(); }
        });
        _generate = generate;
    }

    public async Task<ModManifestPreparationResult> EnsureAsync(ActiveContentSnapshot content,
        string gameDirectory, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var missing = content.Sources.Where(source => source.Kind is "local" or "workshop")
                .DistinctBy(source => Path.GetFullPath(source.Directory), StringComparer.OrdinalIgnoreCase)
                .Where(source => !ModManifestFile.Exists(Path.Combine(source.Directory, "modfiles.txt"))).ToArray();
            if (missing.Length == 0) return new(0, null);
            void Guard()
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_gameRunning()) throw new InvalidOperationException(EditorText.Get("ModManifestPreparationService_001"));
                if (ModManifestFiles.Hash(Path.Combine(content.Profile.ProfileDirectory, "persist.game.json")) != content.SourceGameSha256)
                    throw new IOException(EditorText.Get("ModManifestPreparationService_002"));
            }
            Guard();
            var workspace = Path.Combine(_locations.WorkspaceDirectory, "mod_manifests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workspace);
            var receiptPath = Path.Combine(workspace, "receipt.json");
            var prepared = new List<Prepared>();
            // Prepare all outputs before creating any installed manifest.
            for (var index = 0; index < missing.Length; index++)
            {
                Guard();
                var source = missing[index];
                progress?.Report(EditorText.Format("ModManifestPreparationService_003", index + 1, missing.Length, source.DisplayName));
                var before = ModManifestFiles.Snapshot(source.Directory, cancellationToken);
                var evidence = Path.Combine(workspace, index.ToString());
                Directory.CreateDirectory(evidence);
                var bytes = await _generate(gameDirectory, source.Directory, evidence, cancellationToken).ConfigureAwait(false);
                Guard();
                ModManifestFiles.RequireSnapshot(source.Directory, before, cancellationToken);
                var count = ModManifestFiles.Validate(bytes, before);
                var output = Path.Combine(evidence, "modfiles.txt");
                await File.WriteAllBytesAsync(output, bytes, cancellationToken).ConfigureAwait(false);
                prepared.Add(new(source.Directory, source.DisplayName, output, ModManifestFiles.Hash(output), count, before));
            }
            var created = new List<Prepared>();
            void Receipt(string status)
            {
                var temporaryReceipt = receiptPath + ".tmp";
                File.WriteAllText(temporaryReceipt, JsonSerializer.Serialize(new
                {
                    Status = status, Profile = content.Profile.ProfileDirectory,
                    Prepared = prepared.Select(item => new { Path = Path.Combine(item.Directory, "modfiles.txt"), item.Hash, item.Rows, item.Name }),
                    Created = created.Select(item => new { Path = Path.Combine(item.Directory, "modfiles.txt"), item.Hash, item.Rows, item.Name })
                }, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
                File.Move(temporaryReceipt, receiptPath, overwrite: true);
            }
            Receipt("prepared");
            try
            {
                foreach (var item in prepared)
                {
                    Guard();
                    ModManifestFiles.RequireSnapshot(item.Directory, item.Before, cancellationToken);
                    var destination = Path.Combine(item.Directory, "modfiles.txt");
                    if (ModManifestFile.Exists(destination)) throw new IOException(EditorText.Format("ModManifestPreparationService_004", destination));
                    var temporary = Path.Combine(item.Directory, ".ddse-manifest-" + Guid.NewGuid().ToString("N") + ".tmp");
                    try
                    {
                        File.Copy(item.Output, temporary, overwrite: false);
                        if (ModManifestFiles.Hash(temporary) != item.Hash)
                            throw new IOException(EditorText.Get("ModManifestPreparationService_005"));
                        using (var stream = new FileStream(temporary, FileMode.Open, FileAccess.Write, FileShare.None)) stream.Flush(true);
                        Guard();
                        File.Move(temporary, destination, overwrite: false);
                        created.Add(item);
                        Receipt("installing");
                    }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                }
                Guard();
                foreach (var item in created)
                {
                    if (ModManifestFiles.Hash(Path.Combine(item.Directory, "modfiles.txt")) != item.Hash)
                        throw new IOException(EditorText.Get("ModManifestPreparationService_006"));
                    ModManifestFiles.RequireSnapshot(item.Directory, item.Before, cancellationToken, ignoreManifest: true);
                }
                Receipt("complete");
                progress?.Report(EditorText.Format("ModManifestPreparationService_007", created.Count, receiptPath));
                return new(created.Count, receiptPath);
            }
            catch (Exception error)
            {
                // Keep successful creations identifiable, even if another Mod failed or the game started.
                try { Receipt("incomplete"); }
                catch (IOException) { /* The preceding atomic receipt retains the planned paths and hashes. */ }
                catch (UnauthorizedAccessException) { }
                throw new IOException(EditorText.Format("ModManifestPreparationService_008", created.Count, receiptPath, error.Message), error);
            }
        }
        finally { Gate.Release(); }
    }

    private sealed record Prepared(string Directory, string Name, string Output, string Hash, int Rows,
        IReadOnlyDictionary<string, ModManifestFileStamp> Before);
}

internal sealed record ModManifestFileStamp(long Length, string Hash);

internal static class ModManifestFiles
{
    internal static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    internal static void RequireRegularPath(string path)
    {
        var current = Path.GetFullPath(path);
        while (current is not null)
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException(EditorText.Format("ModManifestPreparationService_009", current));
            current = Path.GetDirectoryName(current);
        }
    }

    internal static Dictionary<string, ModManifestFileStamp> Snapshot(string root, CancellationToken token, bool ignoreManifest = false)
    {
        RequireRegularPath(root);
        var result = new Dictionary<string, ModManifestFileStamp>(StringComparer.OrdinalIgnoreCase);
        var directories = new Stack<string>(); directories.Push(root);
        while (directories.TryPop(out var directory))
        foreach (var path in Directory.EnumerateFileSystemEntries(directory))
        {
            token.ThrowIfCancellationRequested();
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException(EditorText.Format("ModManifestPreparationService_010", path));
            if ((attributes & FileAttributes.Directory) != 0) { directories.Push(path); continue; }
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            if (ignoreManifest && relative.Equals("modfiles.txt", StringComparison.OrdinalIgnoreCase)) continue;
            result.Add(relative, new(new FileInfo(path).Length, Hash(path)));
        }
        return result;
    }

    internal static void RequireSnapshot(string root, IReadOnlyDictionary<string, ModManifestFileStamp> before,
        CancellationToken token, bool ignoreManifest = false)
    {
        var after = Snapshot(root, token, ignoreManifest);
        if (before.Count != after.Count || before.Any(pair => after.GetValueOrDefault(pair.Key) != pair.Value))
            throw new IOException(EditorText.Format("ModManifestPreparationService_011", root));
    }

    internal static int Validate(byte[] bytes, IReadOnlyDictionary<string, ModManifestFileStamp> files)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in new UTF8Encoding(false, true).GetString(bytes).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var row = line.TrimEnd('\r'); var separator = row.LastIndexOf(' ');
            if (separator < 1 || !long.TryParse(row[(separator + 1)..], out var size)) throw new InvalidDataException(EditorText.Get("ModManifestPreparationService_012"));
            var relative = row[..separator];
            if (Path.IsPathRooted(relative) || relative.Contains('\\') || relative.Split('/').Any(part => part is ".." or "." or "") ||
                relative.Equals("modfiles.txt", StringComparison.OrdinalIgnoreCase) || !seen.Add(relative) ||
                !files.TryGetValue(relative, out var file) || file.Length != size)
                throw new InvalidDataException(EditorText.Format("ModManifestPreparationService_013", relative));
        }
        if (seen.Count == 0) throw new InvalidDataException(EditorText.Get("ModManifestPreparationService_014"));
        return seen.Count;
    }
}
