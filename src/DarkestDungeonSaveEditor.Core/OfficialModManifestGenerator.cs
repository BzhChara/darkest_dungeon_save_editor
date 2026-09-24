using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;

namespace DarkestDungeonSaveEditor.Core;

internal static class OfficialModManifestGenerator
{
    private const string VerifiedUploaderHash = "b8b74103921a421c73d141b5632964932bd6425f299cf13e3e3eb4e8b2452020";
    private static readonly string[] RuntimeFiles = ["steam_workshop_upload.exe", "SDL2.dll", "fmodstudio.dll", "fmod.dll", "steam_api.dll", "glew32.dll"];

    internal static async Task<byte[]> GenerateAsync(string game, string source, string evidence, CancellationToken token)
    {
        var tools = Path.Combine(game, "_windows", "win32");
        var executable = Path.Combine(tools, RuntimeFiles[0]);
        if (!File.Exists(executable)) throw new FileNotFoundException(EditorText.Get("OfficialModManifestGenerator_001"), executable);
        if (ModManifestFiles.Hash(executable) != VerifiedUploaderHash)
            throw new InvalidDataException(EditorText.Get("OfficialModManifestGenerator_002"));
        var stage = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "DDSEManifest_" + Guid.NewGuid().ToString("N")));
        if (stage.Any(character => character > 127))
            throw new IOException(EditorText.Get("OfficialModManifestGenerator_003"));
        Directory.CreateDirectory(stage);
        try
        {
            var runtime = Path.Combine(stage, "runtime");
            var mod = Path.Combine(stage, "mod");
            var payload = Path.Combine(mod, "content");
            var baseline = Path.Combine(stage, "base");
            Directory.CreateDirectory(runtime); Directory.CreateDirectory(payload);
            var inputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            void Copy(string from, string to)
            {
                token.ThrowIfCancellationRequested();
                ModManifestFiles.RequireRegularPath(from);
                var hash = ModManifestFiles.Hash(from);
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Copy(from, to, overwrite: false);
                if (ModManifestFiles.Hash(to) != hash) throw new IOException(EditorText.Format("OfficialModManifestGenerator_004", from));
                inputs[from] = hash;
            }
            foreach (var name in RuntimeFiles) Copy(Path.Combine(tools, name), Path.Combine(runtime, name));
            if (ModManifestFiles.Hash(Path.Combine(runtime, RuntimeFiles[0])) != VerifiedUploaderHash)
                throw new IOException(EditorText.Get("OfficialModManifestGenerator_005"));
            Copy(Path.Combine(game, "colours", "base.colours.darkest"), Path.Combine(baseline, "colours", "base.colours.darkest"));
            Copy(Path.Combine(game, "mods", "preview_icon.png"), Path.Combine(baseline, "mods", "preview_icon.png"));
            // Native startup appends these bytes to the EXE directory. No BOM or newline.
            File.WriteAllText(Path.Combine(runtime, "project_paths.txt"), "../base/", new UTF8Encoding(false));
            var original = ModManifestFiles.Snapshot(source, token);
            var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (relative, stamp) in original)
            {
                Copy(Path.Combine(source, relative), Path.Combine(payload, relative));
                var vanilla = Path.Combine(game, relative);
                var identical = false;
                if (File.Exists(vanilla))
                {
                    Copy(vanilla, Path.Combine(baseline, "content", relative));
                    identical = inputs[vanilla] == stamp.Hash;
                }
                if (!identical && relative.Contains('.') && NativeDirectoryDiscovery.IsDiscovered(payload, Path.Combine(payload, relative)))
                    expected.Add(relative);
            }
            new XDocument(new XElement("project",
                new XElement("PreviewIconFile", "preview_icon.png"),
                new XElement("ModDataPath", mod.Replace('\\', '/') + "/"),
                new XElement("Title", "DDSE Manifest Preparation"), new XElement("Language", "english"),
                new XElement("Visibility", "private"), new XElement("UploadMode", "dont_submit"),
                new XElement("VersionMajor", "0"), new XElement("VersionMinor", "1"),
                new XElement("TargetBuild", "0"), new XElement("PublishedFileId", "0"),
                new XElement("ItemDescription", "Local manifest preparation.")))
                .Save(Path.Combine(mod, "project.xml"));
            var start = new ProcessStartInfo(Path.Combine(runtime, RuntimeFiles[0]))
            {
                WorkingDirectory = runtime, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true
            };
            start.ArgumentList.Add(Path.Combine(mod, "project.xml"));
            Process process;
            var previous = SetErrorMode(3); // Suppress native crash UI in this child; restore immediately.
            try { process = Process.Start(start) ?? throw new IOException(EditorText.Get("OfficialModManifestGenerator_006")); }
            finally { SetErrorMode(previous); }
            using (process)
            {
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromMinutes(2));
                try
                {
                    try { await process.StandardInput.WriteLineAsync().ConfigureAwait(false); }
                    catch (IOException) when (process.HasExited) { }
                    finally { process.StandardInput.Close(); }
                    await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    if (token.IsCancellationRequested) throw;
                    throw new TimeoutException(EditorText.Get("OfficialModManifestGenerator_007"));
                }
                finally
                {
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                    await File.WriteAllTextAsync(Path.Combine(evidence, "stdout.txt"), await stdout, CancellationToken.None);
                    await File.WriteAllTextAsync(Path.Combine(evidence, "stderr.txt"), await stderr, CancellationToken.None);
                }
                if (process.ExitCode != 0) throw new IOException(EditorText.Format("OfficialModManifestGenerator_008", process.ExitCode, evidence));
            }
            token.ThrowIfCancellationRequested();
            ModManifestFiles.RequireSnapshot(payload, original, token);
            ModManifestFiles.RequireSnapshot(source, original, token);
            foreach (var (path, hash) in inputs)
                if (ModManifestFiles.Hash(path) != hash) throw new IOException(EditorText.Format("OfficialModManifestGenerator_009", path));
            var raw = await File.ReadAllBytesAsync(Path.Combine(mod, "modfiles.txt"), token);
            await File.WriteAllBytesAsync(Path.Combine(evidence, "official-modfiles.txt"), raw, token);
            var rows = new UTF8Encoding(false, true).GetString(raw).Split('\n')
                .Select(line => line.TrimEnd('\r')).Where(line => line.StartsWith("content/", StringComparison.Ordinal))
                .Select(line => line["content/".Length..]).ToArray();
            var bytes = Encoding.UTF8.GetBytes(string.Join('\n', rows) + "\n");
            ModManifestFiles.Validate(bytes, original);
            if (!expected.SetEquals(rows.Select(line => line[..line.LastIndexOf(' ')])))
                throw new InvalidDataException(EditorText.Get("OfficialModManifestGenerator_010"));
            return bytes;
        }
        finally
        {
            // Delete only the exact private directory created above, never a caller-supplied path.
            if (Path.GetDirectoryName(stage) == Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) &&
                Path.GetFileName(stage).StartsWith("DDSEManifest_", StringComparison.Ordinal))
            {
                try { Directory.Delete(stage, recursive: true); }
                catch (IOException) { /* A locked temporary output may be cleaned after its owner exits. */ }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    [DllImport("kernel32.dll")]
    private static extern uint SetErrorMode(uint mode);
}
