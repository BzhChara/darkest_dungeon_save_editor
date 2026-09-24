using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;

namespace DarkestDungeonSaveEditor.Core;

public static class ActiveContentResolver
{
    public static Task<ActiveContentSnapshot> ResolveAsync(
        SaveProfile profile,
        string gameDirectory,
        string? workshopDirectory,
        DsonSaveCodec codec,
        string? workspaceRoot = null,
        CancellationToken cancellationToken = default)
    {
        return ResolveAsync(
            profile,
            gameDirectory,
            workshopDirectory,
            additionalLocalModDirectory: null,
            codec,
            workspaceRoot,
            cancellationToken);
    }

    public static async Task<ActiveContentSnapshot> ResolveAsync(
        SaveProfile profile,
        string gameDirectory,
        string? workshopDirectory,
        string? additionalLocalModDirectory,
        DsonSaveCodec codec,
        string? workspaceRoot = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(codec);
        gameDirectory = Path.GetFullPath(gameDirectory);
        if (!Directory.Exists(gameDirectory))
        {
            throw new DirectoryNotFoundException($"Game directory was not found: {gameDirectory}");
        }

        var normalizedAdditionalLocalModDirectory = string.IsNullOrWhiteSpace(additionalLocalModDirectory)
            ? null
            : Path.GetFullPath(additionalLocalModDirectory);
        if (normalizedAdditionalLocalModDirectory is not null &&
            !Directory.Exists(normalizedAdditionalLocalModDirectory))
        {
            throw new DirectoryNotFoundException(
                $"Additional local Mod directory was not found: {normalizedAdditionalLocalModDirectory}");
        }

        var gameSavePath = Path.GetFullPath(Path.Combine(profile.ProfileDirectory, "persist.game.json"));
        if (!File.Exists(gameSavePath))
        {
            throw new FileNotFoundException("persist.game.json was not found in the selected profile.", gameSavePath);
        }

        workspaceRoot ??= SaveEditorLocations.CreateDefault().WorkspaceDirectory;
        var workspace = Path.Combine(
            Path.GetFullPath(workspaceRoot),
            "content_catalog",
            $"{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}");
        var sourceDirectory = Path.Combine(workspace, "source");
        var decodedDirectory = Path.Combine(workspace, "decoded");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(decodedDirectory);

        var sourceCopyPath = Path.Combine(sourceDirectory, "persist.game.json");
        var decodedGamePath = Path.Combine(decodedDirectory, "persist.game.json");
        var originalHash = ComputeSha256(gameSavePath);
        File.Copy(gameSavePath, sourceCopyPath, overwrite: false);
        if (!ComputeSha256(sourceCopyPath).Equals(originalHash, StringComparison.OrdinalIgnoreCase) ||
            !ComputeSha256(gameSavePath).Equals(originalHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("persist.game.json changed while the active content snapshot was being prepared.");
        }

        await codec.DecodeAsync(sourceCopyPath, decodedGamePath, cancellationToken).ConfigureAwait(false);
        return ResolveDecoded(profile, gameDirectory, workshopDirectory,
            normalizedAdditionalLocalModDirectory, workspace, decodedGamePath, originalHash, cancellationToken);
    }

    public static ActiveContentSnapshot ResolveDecoded(
        SaveProfile profile,
        string gameDirectory,
        string? workshopDirectory,
        string? additionalLocalModDirectory,
        string workspace,
        string decodedGamePath,
        string originalHash,
        CancellationToken cancellationToken = default)
    {
        gameDirectory = Path.GetFullPath(gameDirectory);
        var normalizedAdditionalLocalModDirectory = string.IsNullOrWhiteSpace(additionalLocalModDirectory)
            ? null : Path.GetFullPath(additionalLocalModDirectory);
        var root = JsonSupport.ReadObject(decodedGamePath);
        profile = RaidSaveLocation.FromGame(profile.ProfileDirectory, root).Bind(profile);
        var baseRoot = JsonSupport.RequireObject(root, "base_root");
        var resolved = ResolveSources(baseRoot, gameDirectory, workshopDirectory,
            normalizedAdditionalLocalModDirectory, cancellationToken);
        return new ActiveContentSnapshot(profile, resolved.GameMode, resolved.Sources, resolved.Issues,
            workspace, decodedGamePath, resolved.AppliedModCount, originalHash)
        {
            Resolution = new(gameDirectory,
                string.IsNullOrWhiteSpace(workshopDirectory) ? null : Path.GetFullPath(workshopDirectory),
                normalizedAdditionalLocalModDirectory, ProfileContentConfiguration.Capture(root).ToJsonString())
        };
    }

    public static void ValidateSourceBindings(ActiveContentResolution? resolution,
        IReadOnlyList<ActiveContentSource> expectedSources, CancellationToken cancellationToken = default)
    {
        // Explicit source lists are also used by standalone catalog consumers. They do not
        // claim a saved Mod-title mapping. Every ResolveAsync/ResolveDecoded snapshot does.
        if (resolution is null) return;
        var configuration = JsonNode.Parse(resolution.ConfigurationJson)!.AsObject();
        var current = ResolveSources(configuration, resolution.GameDirectory, resolution.WorkshopDirectory,
            resolution.AdditionalLocalModDirectory, cancellationToken);
        if (current.Sources.Count != expectedSources.Count ||
            current.Sources.Where((source, index) => !SameBinding(source, expectedSources[index])).Any() ||
            current.Sources.Any(source => !Directory.Exists(source.Directory)))
            throw new InvalidOperationException(EditorText.Get("ActiveContentResolver_001"));
    }

    private static bool SameBinding(ActiveContentSource left, ActiveContentSource right) =>
        left.Id == right.Id && left.Kind == right.Kind && left.LoadOrder == right.LoadOrder &&
        Path.GetFullPath(left.Directory).Equals(Path.GetFullPath(right.Directory), StringComparison.OrdinalIgnoreCase) &&
        left.VirtualPathPrefix.Equals(right.VirtualPathPrefix, StringComparison.OrdinalIgnoreCase);

    internal static bool HasUnresolvedModSources(ActiveContentSnapshot content)
    {
        // Explicit/hypothetical source lists do not claim a saved Mod mapping.
        // ResolveSources can omit an enabled Mod whose directory/title cannot
        // be mapped; absence in that partial scan is not confirmed absence.
        if (content.Resolution is not { } resolution) return false;
        var configuration = JsonNode.Parse(resolution.ConfigurationJson)!.AsObject();
        var issues = new List<string>();
        var enabled = ReadAppliedEntries(configuration["applied_ugcs_1_0"] as JsonObject, issues);
        return issues.Count > 0 || enabled.Count > content.Sources.Count(source => source.Kind is "local" or "workshop");
    }

    private static (string GameMode, IReadOnlyList<ActiveContentSource> Sources,
        IReadOnlyList<string> Issues, int AppliedModCount) ResolveSources(JsonObject baseRoot,
        string gameDirectory, string? workshopDirectory, string? normalizedAdditionalLocalModDirectory,
        CancellationToken cancellationToken)
    {
        var gameMode = JsonSupport.ReadString(baseRoot, "game_mode");
        if (string.IsNullOrWhiteSpace(gameMode))
        {
            gameMode = "base";
        }
        else
        {
            gameMode = gameMode.Trim();
        }

        var issues = new List<string>();
        var sources = new List<ActiveContentSource>
        {
            new("base", "base", "base", gameDirectory, 0)
        };
        var seenDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            gameDirectory
        };

        AddGameModeSource(gameDirectory, gameMode, sources, seenDirectories, issues);
        AddEnabledDlcSources(gameDirectory, baseRoot, sources, seenDirectories, issues);

        var applied = baseRoot["applied_ugcs_1_0"] as JsonObject;
        var appliedEntries = ReadAppliedEntries(applied, issues);
        var localSources = DiscoverLocalModSources(
            gameDirectory,
            normalizedAdditionalLocalModDirectory,
            issues);
        var normalizedWorkshopDirectory = string.IsNullOrWhiteSpace(workshopDirectory)
            ? null
            : Path.GetFullPath(workshopDirectory);

        foreach (var entry in appliedEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? directory = null;
            string kind;
            string id;
            if (entry.Source.Equals("Steam", StringComparison.OrdinalIgnoreCase))
            {
                kind = "workshop";
                id = $"workshop:{entry.Name}";
                if (normalizedWorkshopDirectory is null || !Directory.Exists(normalizedWorkshopDirectory))
                {
                    issues.Add($"Enabled Workshop item '{entry.Name}' cannot be resolved because the Workshop directory is unavailable.");
                    continue;
                }

                if (entry.Name.Length == 0 || entry.Name.Any(character => !char.IsAsciiDigit(character)))
                {
                    issues.Add($"Enabled Workshop item has an invalid numeric id: {entry.Name}");
                    continue;
                }

                directory = Path.Combine(normalizedWorkshopDirectory, entry.Name);
                if (!Directory.Exists(directory))
                {
                    issues.Add($"Enabled Workshop item is not installed: {entry.Name}");
                    continue;
                }
            }
            else if (entry.Source.Equals("mod_local_source", StringComparison.OrdinalIgnoreCase))
            {
                kind = "local";
                id = $"local:{entry.Name}";
                if (!localSources.TryGetValue(entry.Name, out var candidates) || candidates.Count == 0)
                {
                    issues.Add($"Enabled local Mod could not be mapped by project title: {entry.Name}");
                    continue;
                }

                if (candidates.Count > 1)
                {
                    issues.Add(
                        $"Enabled local Mod title is ambiguous and was not scanned: {entry.Name} -> {string.Join(" | ", candidates)}");
                    continue;
                }

                directory = candidates[0];
            }
            else
            {
                issues.Add($"Unsupported enabled Mod source '{entry.Source}' for '{entry.Name}'.");
                continue;
            }

            directory = Path.GetFullPath(directory);
            if (!seenDirectories.Add(directory))
            {
                issues.Add($"Enabled content directory appears more than once and was scanned once: {directory}");
                continue;
            }

            // applied_ugcs_1_0 is stored in UI top-to-bottom order. Keep that numeric
            // order here; ContentFileOverlay applies the game's bottom-to-top Mod overlay.
            sources.Add(new ActiveContentSource(id, entry.Name, kind, directory, 1000 + entry.Order));
        }

        return (gameMode, sources.OrderBy(source => source.LoadOrder).ToArray(), issues, appliedEntries.Count);
    }

    private static void AddGameModeSource(
        string gameDirectory,
        string gameMode,
        List<ActiveContentSource> sources,
        HashSet<string> seenDirectories,
        List<string> issues)
    {
        if (gameMode.Equals("base", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (gameMode is "." or ".." ||
            !Path.GetFileName(gameMode).Equals(gameMode, StringComparison.Ordinal) ||
            gameMode.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            issues.Add($"Ignored invalid game mode id: {gameMode}");
            return;
        }

        var modeDirectory = Path.GetFullPath(Path.Combine(gameDirectory, "modes", gameMode));
        if (!Directory.Exists(modeDirectory))
        {
            issues.Add($"The selected game mode directory is missing: {gameMode} -> {modeDirectory}");
            return;
        }

        if (!seenDirectories.Add(modeDirectory))
        {
            return;
        }

        sources.Add(new ActiveContentSource(
            $"mode:{gameMode}",
            gameMode,
            "mode",
            modeDirectory,
            50));
    }

    private static void AddEnabledDlcSources(
        string gameDirectory,
        JsonObject baseRoot,
        List<ActiveContentSource> sources,
        HashSet<string> seenDirectories,
        List<string> issues)
    {
        var enabledEntries = ReadOrderedNamedEntries(baseRoot["dlc"] as JsonObject, issues);
        var dlcDirectory = Path.Combine(gameDirectory, "dlc");
        if (enabledEntries.Count == 0 || !Directory.Exists(dlcDirectory))
        {
            return;
        }

        var enabledOrder = enabledEntries
            .GroupBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Min(entry => entry.Order), StringComparer.OrdinalIgnoreCase);
        foreach (var directory in Directory.EnumerateDirectories(dlcDirectory)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var packageName = NormalizeDlcDirectoryName(Path.GetFileName(directory));
            var featureRoot = Path.Combine(directory, "features");
            var featureDirectories = Directory.Exists(featureRoot)
                ? Directory.EnumerateDirectories(featureRoot)
                    .Select(path => new DlcFeatureDirectory(Path.GetFileName(path), Path.GetFullPath(path)))
                    .Where(feature => enabledOrder.ContainsKey(feature.Name) &&
                                      !feature.Name.Contains("arena", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(feature => enabledOrder[feature.Name])
                    .ToArray()
                : [];

            if (Directory.Exists(featureRoot))
            {
                if (featureDirectories.Length > 0)
                {
                    var packageOrder = featureDirectories.Min(feature => enabledOrder[feature.Name]);
                    AddDlcSource(
                        new ActiveContentSource(
                            $"dlc-package:{packageName}",
                            packageName,
                            "dlc-package",
                            Path.GetFullPath(directory),
                            100 + (packageOrder * 10))
                        {
                            VirtualPathPrefix = GetVirtualPathPrefix(gameDirectory, directory)
                        },
                        sources,
                        seenDirectories,
                        issues);
                    foreach (var feature in featureDirectories)
                    {
                        AddDlcSource(
                            new ActiveContentSource(
                                $"dlc-feature:{feature.Name}",
                                feature.Name,
                                "dlc-feature",
                                feature.Directory,
                                101 + (enabledOrder[feature.Name] * 10))
                            {
                                VirtualPathPrefix = GetVirtualPathPrefix(gameDirectory, feature.Directory)
                            },
                            sources,
                            seenDirectories,
                            issues);
                    }
                }

                continue;
            }

            if (!enabledOrder.TryGetValue(packageName, out var order) ||
                packageName.Contains("arena", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            AddDlcSource(
                new ActiveContentSource(
                    $"dlc:{packageName}",
                    packageName,
                    "dlc",
                    Path.GetFullPath(directory),
                    100 + (order * 10))
                {
                    VirtualPathPrefix = GetVirtualPathPrefix(gameDirectory, directory)
                },
                sources,
                seenDirectories,
                issues);
        }
    }

    private static void AddDlcSource(
        ActiveContentSource source,
        List<ActiveContentSource> sources,
        HashSet<string> seenDirectories,
        List<string> issues)
    {
        if (!seenDirectories.Add(source.Directory))
        {
            issues.Add($"Enabled DLC directory appears more than once and was scanned once: {source.Directory}");
            return;
        }

        sources.Add(source);
    }

    private static string GetVirtualPathPrefix(string gameDirectory, string directory)
    {
        return Path.GetRelativePath(Path.GetFullPath(gameDirectory), Path.GetFullPath(directory))
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
    }

    private static IReadOnlyList<NamedContentEntry> ReadOrderedNamedEntries(JsonObject? entries, List<string> issues)
    {
        if (entries is null)
        {
            return [];
        }

        var result = new List<NamedContentEntry>();
        foreach (var pair in entries)
        {
            if (!int.TryParse(pair.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var order) ||
                pair.Value is not JsonObject entry)
            {
                issues.Add($"Ignored malformed DLC entry: {pair.Key}");
                continue;
            }

            var name = JsonSupport.ReadString(entry, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                issues.Add($"Ignored incomplete DLC entry: {pair.Key}");
                continue;
            }

            result.Add(new NamedContentEntry(order, name));
        }

        return result.OrderBy(entry => entry.Order).ToArray();
    }

    private static IReadOnlyList<AppliedContentEntry> ReadAppliedEntries(JsonObject? applied, List<string> issues)
    {
        if (applied is null)
        {
            return [];
        }

        var result = new List<AppliedContentEntry>();
        foreach (var pair in applied)
        {
            if (!int.TryParse(pair.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var order) ||
                pair.Value is not JsonObject entry)
            {
                issues.Add($"Ignored malformed applied_ugcs_1_0 entry: {pair.Key}");
                continue;
            }

            var name = JsonSupport.ReadString(entry, "name");
            var source = JsonSupport.ReadString(entry, "source");
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(source))
            {
                issues.Add($"Ignored incomplete applied_ugcs_1_0 entry: {pair.Key}");
                continue;
            }

            result.Add(new AppliedContentEntry(order, name, source));
        }

        return result.OrderBy(entry => entry.Order).ToArray();
    }

    private static Dictionary<string, IReadOnlyList<string>> DiscoverLocalModSources(
        string gameDirectory,
        string? additionalLocalModDirectory,
        List<string> issues)
    {
        var candidates = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var searchRoots = new[]
            {
                Path.Combine(gameDirectory, "mods"),
                Path.Combine(gameDirectory, "dlc"),
                additionalLocalModDirectory
            }
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path!))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var enumerationOptions = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false
        };
        foreach (var searchRoot in searchRoots)
        {
            if (!Directory.Exists(searchRoot))
            {
                continue;
            }

            foreach (var projectPath in Directory.EnumerateFiles(searchRoot, "project.xml", enumerationOptions)
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                var directory = Path.GetDirectoryName(projectPath)!;
                if (HasAncestorProject(directory, searchRoot))
                {
                    continue;
                }

                try
                {
                    var title = ReadProjectTitle(projectPath);
                    if (string.IsNullOrWhiteSpace(title))
                    {
                        continue;
                    }

                    if (!candidates.TryGetValue(title, out var paths))
                    {
                        paths = [];
                        candidates[title] = paths;
                    }

                    paths.Add(Path.GetFullPath(directory));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException)
                {
                    issues.Add($"Failed to read local Mod project '{projectPath}': {ex.Message}");
                }
            }
        }

        return candidates.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)pair.Value
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            StringComparer.OrdinalIgnoreCase);
    }

    private static bool HasAncestorProject(string directory, string searchRoot)
    {
        var normalizedRoot = Path.GetFullPath(searchRoot);
        var normalizedDirectory = Path.GetFullPath(directory);
        if (normalizedDirectory.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var parent = Directory.GetParent(normalizedDirectory);
        while (parent is not null && IsWithinDirectory(parent.FullName, normalizedRoot))
        {
            if (File.Exists(Path.Combine(parent.FullName, "project.xml")))
            {
                return true;
            }

            if (parent.FullName.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            parent = parent.Parent;
        }

        return false;
    }

    private static bool IsWithinDirectory(string candidate, string root)
    {
        var relative = Path.GetRelativePath(root, candidate);
        return relative.Equals(".", StringComparison.Ordinal) ||
               (!Path.IsPathRooted(relative) &&
                !relative.Equals("..", StringComparison.Ordinal) &&
                !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal));
    }

    private static string ReadProjectTitle(string path)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        };
        using var reader = XmlReader.Create(path, settings);
        var document = XDocument.Load(reader, LoadOptions.None);
        return document.Descendants()
            .FirstOrDefault(element => element.Name.LocalName.Equals("Title", StringComparison.OrdinalIgnoreCase))?
            .Value.Trim() ?? string.Empty;
    }

    private static string NormalizeDlcDirectoryName(string value)
    {
        var separator = value.IndexOf('_');
        return separator > 0 && value[..separator].All(char.IsAsciiDigit)
            ? value[(separator + 1)..]
            : value;
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private sealed record AppliedContentEntry(int Order, string Name, string Source);
    private sealed record NamedContentEntry(int Order, string Name);
    private sealed record DlcFeatureDirectory(string Name, string Directory);
}
