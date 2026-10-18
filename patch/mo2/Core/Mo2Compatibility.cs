using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Xml;
using System.Xml.Linq;

namespace DarkestDungeonSaveEditor.Core;

// Compiled only by the optional replacement Core project. No ordinary editor hooks/dependencies.
internal static class Mo2Compatibility
{
    internal const string MergeTitle = "merge mod";
    private static readonly object Gate = new();

    internal static Func<string>? RegisterSource(JsonObject configuration, string gameDirectory, string? workshopDirectory,
        Dictionary<string, IReadOnlyList<string>> localSources, CancellationToken token, bool validateBindingsOnly = false)
    {
        if (configuration["applied_ugcs_1_0"] is not JsonObject applied ||
            !applied.Any(pair => pair.Value is JsonObject entry &&
                string.Equals(JsonSupport.ReadString(entry, "name"), MergeTitle, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(JsonSupport.ReadString(entry, "source"), "mod_local_source", StringComparison.OrdinalIgnoreCase)))
            return null;

        var configPath = Path.Combine(AppContext.BaseDirectory, "mo2-compat.json");
        var settings = Mo2Settings.Read(configPath);
        var virtualSlot = Path.GetFullPath(Path.Combine(gameDirectory, "mods", "!!MOD"));
        if (localSources.TryGetValue(MergeTitle, out var physicalCandidates))
        {
            foreach (var candidate in physicalCandidates)
                if (!Path.GetFullPath(candidate).Equals(Path.GetFullPath(virtualSlot), StringComparison.OrdinalIgnoreCase) &&
                    !Path.GetFullPath(candidate).Equals(settings.OverwriteDirectory, StringComparison.OrdinalIgnoreCase))
                    throw Error($"An unrelated local project also uses the title 'merge mod': {candidate}. Resolve the title ambiguity before loading.");
        }
        // This path is only an identity token; it is never created or returned.
        // A real slot such as game/mods/!!MOD could belong to another enabled
        // project and cause directory deduplication to discard one of the sources.
        localSources[MergeTitle] = [Path.Combine(settings.CacheDirectory!, "pending-source-" + Guid.NewGuid().ToString("N"))];
        return () => validateBindingsOnly
            ? ResolveForValidation(configPath, gameDirectory, workshopDirectory, token)
            : Prepare(configPath, gameDirectory, workshopDirectory, token);
    }

    internal static string ResolveForValidation(string configPath, string gameDirectory, string? workshopDirectory,
        CancellationToken token = default)
    {
        lock (Gate)
        {
            token.ThrowIfCancellationRequested();
            // Always hash current MO2 inputs, even when size/timestamp did not change.
            // Source-binding guards only need the resulting identity; the caller's
            // ordinary catalog guards verify the cached resources used by its edit.
            // Do not copy a new snapshot or re-hash unrelated cached catalogs here.
            var plan = Mo2ContentPlan.Capture(configPath, gameDirectory, workshopDirectory, token);
            var snapshot = SafePath.Under(plan.CacheDirectory, plan.Key);
            var content = Path.Combine(snapshot, "content");
            SafePath.RejectReparseAncestors(content);
            var ready = Path.Combine(snapshot, "complete.txt");
            if (!File.Exists(ready))
                throw new InvalidOperationException("MO2 compatibility: Content changed after loading. Load the profile again before writing.");
            SafePath.RequireRegular(ready);
            if (File.ReadAllText(ready) != plan.Key)
                throw new InvalidOperationException("MO2 compatibility: Content changed after loading. Load the profile again before writing.");
            VerifyMetadata(plan, content);
            return content;
        }
    }

    internal static string Prepare(string configPath, string gameDirectory, string? workshopDirectory,
        CancellationToken token = default)
    {
        // Loading and pre-write source validation both use this path. Changed inputs produce a
        // different source directory, so existing prepared edits cannot silently use stale Mods.
        lock (Gate)
        {
            token.ThrowIfCancellationRequested();
            var plan = Mo2ContentPlan.Capture(configPath, gameDirectory, workshopDirectory, token);
            var snapshot = SafePath.Under(plan.CacheDirectory, plan.Key);
            SafePath.RejectReparseAncestors(snapshot);
            var content = Path.Combine(snapshot, "content");
            var ready = Path.Combine(snapshot, "complete.txt");
            if (File.Exists(ready) && File.ReadAllText(ready) == plan.Key)
            {
                VerifyCopy(plan, content, token);
                return content;
            }
            // Each attempt owns a new directory. Failed/cancelled work is never published or reused.
            // Leave it available for diagnosis; no automatic deletion of existing user files.
            var staging = SafePath.Under(plan.CacheDirectory, "building-" + Guid.NewGuid().ToString("N"));
            var stagedContent = Path.Combine(staging, "content");
            Directory.CreateDirectory(stagedContent);
            SafePath.RejectReparseAncestors(stagedContent);
            foreach (var file in plan.Files)
            {
                token.ThrowIfCancellationRequested();
                var destination = SafePath.Under(stagedContent, file.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file.SourcePath, destination, overwrite: false);
            }
            File.WriteAllBytes(Path.Combine(stagedContent, "project.xml"), plan.ProjectBytes);
            File.WriteAllBytes(Path.Combine(stagedContent, "modfiles.txt"), plan.ManifestBytes);
            VerifyCopy(plan, stagedContent, token);
            if (plan.Key != Mo2ContentPlan.Capture(configPath, gameDirectory, workshopDirectory, token).Key)
                throw Error("MO2 inputs changed while preparing content. Launch the game through the configured MO2 profile, fully exit it, and load the profile again.");
            File.WriteAllText(Path.Combine(staging, "complete.txt"), plan.Key);
            // Another editor process can finish the identical snapshot first.
            try { Directory.Move(staging, snapshot); }
            catch (IOException) when (File.Exists(ready) && File.ReadAllText(ready) == plan.Key)
            {
                VerifyCopy(plan, content, token);
            }
            return content;
        }
    }

    private static void VerifyCopy(Mo2ContentPlan plan, string content, CancellationToken token)
    {
        var checkedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        SafePath.RejectReparseAncestors(content, checkedDirectories);
        foreach (var file in plan.Files)
        {
            token.ThrowIfCancellationRequested();
            var path = SafePath.Under(content, file.RelativePath);
            SafePath.RequireRegular(path, checkedDirectories);
            if (new FileInfo(path).Length != file.Length ||
                (file.ContentHash is not null && HashFile(path) != file.ContentHash))
                throw Error($"The MO2 content cache is incomplete or changed: {path}. Close the editor, remove this cache snapshot, and reload.");
        }
        VerifyMetadata(plan, content);
    }

    private static void VerifyMetadata(Mo2ContentPlan plan, string content)
    {
        SafePath.RequireRegular(Path.Combine(content, "project.xml"));
        SafePath.RequireRegular(Path.Combine(content, "modfiles.txt"));
        if (!File.ReadAllBytes(Path.Combine(content, "project.xml")).SequenceEqual(plan.ProjectBytes) ||
            !File.ReadAllBytes(Path.Combine(content, "modfiles.txt")).SequenceEqual(plan.ManifestBytes))
            throw Error($"The MO2 content cache metadata changed: {content}. Close the editor and remove this cache snapshot.");
    }

    internal static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    internal static InvalidDataException Error(string message) => new("MO2 compatibility: " + message);
}

internal sealed record Mo2Settings
{
    public string ProfileDirectory { get; init; } = "";
    public string ModsDirectory { get; init; } = "";
    public string OverwriteDirectory { get; init; } = "";
    public string? CacheDirectory { get; init; }

    internal static Mo2Settings Read(string path)
    {
        if (!File.Exists(path)) throw Mo2Compatibility.Error(
            $"Configuration is missing: {path}. Copy mo2-compat.example.json to mo2-compat.json and set the three MO2 directories.");
        var settings = JsonSerializer.Deserialize<Mo2Settings>(File.ReadAllText(path), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        }) ?? throw Mo2Compatibility.Error("The configuration must be a JSON object.");
        return settings with
        {
            ProfileDirectory = SafePath.AbsoluteDirectory(settings.ProfileDirectory),
            ModsDirectory = SafePath.AbsoluteDirectory(settings.ModsDirectory),
            OverwriteDirectory = SafePath.AbsoluteDirectory(settings.OverwriteDirectory),
            CacheDirectory = settings.CacheDirectory is null
                ? Path.Combine(SaveEditorLocations.CreateDefault().WorkspaceDirectory, "mo2-compat")
                : SafePath.AbsoluteDirectory(settings.CacheDirectory, mustExist: false)
        };
    }
}

internal sealed record Mo2File(string RelativePath, string SourcePath, long Length, string? ContentHash);

internal sealed record Mo2ContentPlan(string Key, string CacheDirectory, byte[] ProjectBytes,
    byte[] ManifestBytes, IReadOnlyList<Mo2File> Files)
{
    internal static Mo2ContentPlan Capture(string configPath, string gameDirectory,
        string? workshopDirectory, CancellationToken token)
    {
        var settings = Mo2Settings.Read(configPath);
        var cache = settings.CacheDirectory!;
        foreach (var input in new[] { settings.ModsDirectory, settings.OverwriteDirectory,
                     settings.ProfileDirectory, gameDirectory, workshopDirectory }.Where(x => x is not null))
            if (SafePath.Overlaps(cache, input!))
                throw Mo2Compatibility.Error("cacheDirectory must be separate from MO2, game and Workshop input directories.");

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Add(string value) => hash.AppendData(Encoding.UTF8.GetBytes(value + "\n"));
        Add("ddse-mo2-content-v3-mapper-20261016");
        Add(Mo2Compatibility.HashFile(configPath));
        Add(Path.GetFullPath(gameDirectory));
        Add(workshopDirectory ?? "");
        var modlist = SafePath.Under(settings.ProfileDirectory, "modlist.txt");
        SafePath.RequireRegular(modlist);
        Add(Mo2Compatibility.HashFile(modlist));
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in File.ReadAllLines(modlist))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (line[0] is not ('+' or '-'))
                throw Mo2Compatibility.Error($"Unsupported modlist entry: {line}. Only regular MO2 Mods with '+' or '-' entries are supported.");
            var name = line[1..].Trim();
            SafePath.SingleName(name);
            if (!seen.Add(name)) throw Mo2Compatibility.Error($"Duplicate MO2 Mod: {name}.");
            if (line[0] == '+') names.Add(name);
        }
        // MO2 persists highest priority first; the game plugin enumerates low to high.
        names.Reverse();
        var layers = new List<Mo2Layer>();
        foreach (var name in names)
        {
            token.ThrowIfCancellationRequested();
            var directory = SafePath.Under(settings.ModsDirectory, name);
            layers.Add(Mo2Layer.Read(name, directory, token));
        }
        layers.Add(Mo2Layer.Read("overwrite", settings.OverwriteDirectory, token));
        var projectPath = SafePath.Under(settings.OverwriteDirectory, "project.xml");
        var manifestPath = SafePath.Under(settings.OverwriteDirectory, "modfiles.txt");
        SafePath.RequireRegular(projectPath);
        SafePath.RequireRegular(manifestPath);
        var project = File.ReadAllBytes(projectPath);
        using (var reader = XmlReader.Create(new MemoryStream(project), new XmlReaderSettings
               { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
        {
            var title = XDocument.Load(reader).Descendants().FirstOrDefault(x => x.Name.LocalName == "Title")?.Value.Trim();
            if (title != Mo2Compatibility.MergeTitle)
                throw Mo2Compatibility.Error("overwrite/project.xml is not the supported 'merge mod' project. Regenerate it with MO2.");
        }
        var manifest = File.ReadAllBytes(manifestPath);
        hash.AppendData(project);
        hash.AppendData(manifest);
        var mappings = Mo2VirtualFiles.Build(layers, token);
        var files = new List<Mo2File>();
        var checkedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var requests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visibleManifest = new StringBuilder();
        var removedHiddenEntry = false;
        using var text = new StringReader(new UTF8Encoding(false, true).GetString(manifest).TrimStart('\uFEFF'));
        while (text.ReadLine() is { } raw)
        {
            token.ThrowIfCancellationRequested();
            var line = raw.Trim();
            if (line.Length == 0) continue;
            var separator = line.LastIndexOf(' ');
            if (separator <= 0 || !long.TryParse(line[(separator + 1)..], NumberStyles.None,
                    CultureInfo.InvariantCulture, out var length))
                throw Mo2Compatibility.Error($"Invalid final manifest entry: {line}.");
            var relative = SafePath.Relative(line[..separator]);
            // This mapper's scanner can list files that MO2's virtual filesystem hides.
            // Exclude them only from our private manifest; never unhide or edit the originals.
            if (SafePath.IsHidden(relative))
            {
                removedHiddenEntry = true;
                continue;
            }
            if (!requests.Add(relative)) throw Mo2Compatibility.Error($"Duplicate/case-ambiguous manifest path: {relative}.");
            if (relative.Equals("modfiles.txt", StringComparison.OrdinalIgnoreCase))
                throw Mo2Compatibility.Error("The final manifest must not list itself.");
            if (!mappings.TryGetValue(relative, out var provider))
                throw Mo2Compatibility.Error($"No enabled provider for '{relative}' in the configured MO2 content. " +
                    $"Profile: '{settings.ProfileDirectory}'; Mods: '{settings.ModsDirectory}'; overwrite: '{settings.OverwriteDirectory}'. " +
                    "Check the selected profile, enabled Mod files and whether overwrite was refreshed by launching that profile. " +
                    "If the game plugin was updated, use a matching compatibility package; refreshing overwrite alone cannot resolve unsupported virtual filenames.");
            var physical = provider.SourcePath;
            SafePath.RequireRegular(physical, checkedDirectories);
            var info = new FileInfo(physical);
            if (info.Length != length)
                throw Mo2Compatibility.Error($"File size differs from the final manifest: {relative}. Launch the game through the configured MO2 profile to refresh overwrite, then fully exit it before loading.");
            // Catalog text and localization are hashed, including unchanged-size/timestamp edits.
            // Large art/audio files need only metadata checks: the editor never parses their bytes.
            var digest = IsCatalogFile(relative) ? Mo2Compatibility.HashFile(physical) : null;
            Add(relative); Add(physical); Add(length.ToString(CultureInfo.InvariantCulture));
            Add(digest ?? info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture));
            if (!relative.Equals("project.xml", StringComparison.OrdinalIgnoreCase))
                files.Add(new(relative, physical, length, digest));
            visibleManifest.Append(line).Append('\n');
        }
        // Binding changes, even with identical winning bytes, invalidate existing previews.
        foreach (var layer in layers)
        {
            Add(layer.Name); Add(layer.RootDirectory);
            foreach (var entry in layer.Files.Keys.Order(StringComparer.Ordinal)) Add(entry);
        }
        return new(Convert.ToHexString(hash.GetHashAndReset()), cache, project,
            removedHiddenEntry ? Encoding.UTF8.GetBytes(visibleManifest.ToString()) : manifest, files);
    }

    private static bool IsCatalogFile(string relative) =>
        new[] { "json", "darkest", "csv", ".xml", ".loc", ".loc2" }
            .Any(suffix => relative.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
}

internal static class SafePath
{
    internal static bool IsHidden(string relative) => relative.Replace('\\', '/').Split('/').Any(part =>
        part.EndsWith(".mohidden", StringComparison.OrdinalIgnoreCase) || part.Equals(".git", StringComparison.OrdinalIgnoreCase));

    internal static string AbsoluteDirectory(string value, bool mustExist = true)
    {
        if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value))
            throw Mo2Compatibility.Error($"An absolute directory is required: '{value}'.");
        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));
        if (mustExist && !Directory.Exists(path)) throw Mo2Compatibility.Error($"Directory not found: {path}.");
        RejectReparseAncestors(path);
        return path;
    }

    internal static string Relative(string value)
    {
        var normalized = value.Replace('\\', '/');
        if (Path.IsPathRooted(normalized) || string.IsNullOrWhiteSpace(normalized))
            throw Mo2Compatibility.Error($"Invalid relative path: {value}.");
        foreach (var part in normalized.Split('/')) SingleName(part);
        return normalized;
    }

    internal static void SingleName(string part)
    {
        if (string.IsNullOrWhiteSpace(part) || part is "." or ".." ||
            part.EndsWith(' ') || part.EndsWith('.') || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            part.Contains('/') || part.Contains('\\'))
            throw Mo2Compatibility.Error($"Invalid path component: '{part}'.");
        var stem = part.Split('.')[0];
        if (new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
                "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(stem, StringComparer.OrdinalIgnoreCase))
            throw Mo2Compatibility.Error($"Reserved Windows path: '{part}'.");
    }

    internal static string Under(string root, string relative)
    {
        var path = Path.GetFullPath(Path.Combine(root, Relative(relative).Replace('/', Path.DirectorySeparatorChar)));
        return path;
    }

    internal static bool Overlaps(string left, string right) => Within(left, right) || Within(right, left);
    private static bool Within(string child, string root) => Path.GetFullPath(child).Equals(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase) ||
        Path.GetFullPath(child).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    internal static void RequireRegular(string path, ISet<string>? checkedDirectories = null)
    {
        RejectReparseAncestors(path, checkedDirectories);
        if (!File.Exists(path) || (File.GetAttributes(path) & FileAttributes.Directory) != 0)
            throw Mo2Compatibility.Error($"Required file is missing: {path}.");
    }

    internal static void RejectReparseAncestors(string path, ISet<string>? checkedDirectories = null)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            if (checkedDirectories?.Contains(current) == true) break;
            if (!Path.Exists(current)) continue;
            var attributes = File.GetAttributes(current);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw Mo2Compatibility.Error($"Linked/reparse paths are unsupported: {current}.");
            if ((attributes & FileAttributes.Directory) != 0) checkedDirectories?.Add(current);
        }
    }
}
