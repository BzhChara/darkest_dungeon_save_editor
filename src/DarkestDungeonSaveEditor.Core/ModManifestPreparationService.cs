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
                if (_gameRunning()) throw new InvalidOperationException("已启用的 Mod 缺少清单，请关闭游戏后重新载入存档，以完成清单生成。");
                if (ModManifestFiles.Hash(Path.Combine(content.Profile.ProfileDirectory, "persist.game.json")) != content.SourceGameSha256)
                    throw new IOException("档案的 Mod 配置在清单准备期间发生变化，请重新载入存档。");
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
                progress?.Report($"正在生成 Mod 清单（{index + 1}/{missing.Length}）：{source.DisplayName}");
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
                    if (ModManifestFile.Exists(destination)) throw new IOException($"清单已由其他程序创建，请重新载入：{destination}");
                    var temporary = Path.Combine(item.Directory, ".ddse-manifest-" + Guid.NewGuid().ToString("N") + ".tmp");
                    try
                    {
                        File.Copy(item.Output, temporary, overwrite: false);
                        if (ModManifestFiles.Hash(temporary) != item.Hash)
                            throw new IOException("生成的清单在安装前被修改，停止写入。");
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
                        throw new IOException("新清单在写入后被其他程序修改。");
                    ModManifestFiles.RequireSnapshot(item.Directory, item.Before, cancellationToken, ignoreManifest: true);
                }
                Receipt("complete");
                progress?.Report($"已补齐 {created.Count} 个 Mod 的清单；生成记录：{receiptPath}");
                return new(created.Count, receiptPath);
            }
            catch (Exception error)
            {
                // Keep successful creations identifiable, even if another Mod failed or the game started.
                try { Receipt("incomplete"); }
                catch (IOException) { /* The preceding atomic receipt retains the planned paths and hashes. */ }
                catch (UnauthorizedAccessException) { }
                throw new IOException($"Mod 清单准备未完成，已新增 {created.Count} 份；详细记录：{receiptPath}。{error.Message}", error);
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
                throw new IOException($"清单生成不支持链接或重解析点：{current}");
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
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException($"Mod 包含链接或重解析点：{path}");
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
            throw new IOException($"Mod 文件在生成清单期间发生变化，请重新载入：{root}");
    }

    internal static int Validate(byte[] bytes, IReadOnlyDictionary<string, ModManifestFileStamp> files)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in new UTF8Encoding(false, true).GetString(bytes).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var row = line.TrimEnd('\r'); var separator = row.LastIndexOf(' ');
            if (separator < 1 || !long.TryParse(row[(separator + 1)..], out var size)) throw new InvalidDataException("官方清单行格式无效。");
            var relative = row[..separator];
            if (Path.IsPathRooted(relative) || relative.Contains('\\') || relative.Split('/').Any(part => part is ".." or "." or "") ||
                relative.Equals("modfiles.txt", StringComparison.OrdinalIgnoreCase) || !seen.Add(relative) ||
                !files.TryGetValue(relative, out var file) || file.Length != size)
                throw new InvalidDataException($"官方清单路径或文件大小与原 Mod 不一致：{relative}");
        }
        if (seen.Count == 0) throw new InvalidDataException("官方工具未生成有效文件记录，停止加载；不会改用无清单扫描。");
        return seen.Count;
    }
}
