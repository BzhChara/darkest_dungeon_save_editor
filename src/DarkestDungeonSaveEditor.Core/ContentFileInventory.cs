using System.Globalization;
using System.Text.RegularExpressions;

namespace DarkestDungeonSaveEditor.Core;

/// <summary>
/// Read-only discovery diagnostics. These observations do not select effective definitions
/// and must never be used as permission to generate content or write a save.
/// </summary>
public static partial class ContentFileInventory
{
    private static readonly string[] ContentDirectories =
    [
        "campaign", "curios", "dungeons", "effects", "heroes", "inventory", "localization",
        "loot", "monsters", "props", "raid", "rules", "scripts", "shared", "torch", "trinkets", "upgrades"
    ];

    private static readonly Regex ManifestLine = new(
        @"^(?<path>.+?)\s+(?<size>[+-]?\d+)\s*$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly char[] InvalidPathSegmentCharacters = Path.GetInvalidFileNameChars();

    public static ContentFileInventorySnapshot Scan(
        ActiveContentSnapshot activeContent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(activeContent);
        cancellationToken.ThrowIfCancellationRequested();
        var prefixes = ContentFileOverlay.GetEnabledDlcPrefixes(activeContent.Sources);
        var mods = new List<ContentModFileInventory>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in activeContent.Sources.Where(source => source.Kind is "workshop" or "local"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var root = Path.GetFullPath(source.Directory);
            if (seen.Add(root))
            {
                mods.Add(ScanMod(source.Id, root, prefixes, cancellationToken));
            }
        }

        return new ContentFileInventorySnapshot(
            activeContent.Profile.ProfileId,
            activeContent.Profile.ProfileDirectory,
            activeContent.SourceGameSha256,
            DateTime.UtcNow,
            mods);
    }

    private static ContentModFileInventory ScanMod(
        string sourceId,
        string root,
        IReadOnlyList<string> prefixes,
        CancellationToken cancellationToken)
    {
        var issues = new List<string>();
        var actual = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var declared = new Dictionary<string, long?>(StringComparer.OrdinalIgnoreCase);
        var manifestPath = Path.Combine(root, "modfiles.txt");
        var hasManifest = false;
        var manifestReadable = true;
        var treeComplete = true;
        try
        {
            if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            {
                return new ContentModFileInventory(sourceId, root, false, false, [], [EditorText.Get("ContentFileInventory_001")]);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ContentModFileInventory(sourceId, root, false, false, [], [EditorText.Format("ContentFileInventory_002", ex.Message)]);
        }

        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                {
                    treeComplete = false;
                    issues.Add(EditorText.Format("ContentFileInventory_003", Path.GetRelativePath(root, directory)));
                    continue;
                }

                foreach (var path in Directory.EnumerateFileSystemEntries(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var attributes = File.GetAttributes(path);
                        if ((attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            treeComplete = false;
                            issues.Add(EditorText.Format("ContentFileInventory_004", Path.GetRelativePath(root, path)));
                            continue;
                        }

                        if ((attributes & FileAttributes.Directory) != 0)
                        {
                            pending.Push(path);
                        }
                        else
                        {
                            var relative = Normalize(Path.GetRelativePath(root, path));
                            if (!actual.TryAdd(relative, new FileInfo(path).Length))
                            {
                                treeComplete = false;
                                issues.Add(EditorText.Format("ContentFileInventory_005", relative));
                            }
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        treeComplete = false;
                        issues.Add(EditorText.Format("ContentFileInventory_006", Path.GetRelativePath(root, path), ex.Message));
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                treeComplete = false;
                issues.Add(EditorText.Format("ContentFileInventory_007", Path.GetRelativePath(root, directory), ex.Message));
            }
        }

        try
        {
            // Do not follow a manifest link, or mistake a failed manifest read for no manifest.
            var attributes = File.GetAttributes(manifestPath);
            hasManifest = true;
            if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
            {
                manifestReadable = false;
                issues.Add(EditorText.Get("ContentFileInventory_008"));
            }
            else
            {
                var lineNumber = 0;
                foreach (var rawLine in File.ReadLines(manifestPath))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    lineNumber++;
                    var line = rawLine.Trim();
                    if (line.Length == 0)
                    {
                        continue;
                    }

                    try
                    {
                        var match = ManifestLine.Match(line);
                        var relative = match.Success ? match.Groups["path"].Value.Trim() : line;
                        long? length = null;
                        if (match.Success)
                        {
                            if (!long.TryParse(match.Groups["size"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
                            {
                                throw new InvalidDataException(EditorText.Get("ContentFileInventory_009"));
                            }

                            length = value;
                        }

                        relative = relative.Replace('\\', '/');
                        if (Path.IsPathRooted(relative) || relative.Contains(':'))
                        {
                            throw new InvalidDataException(EditorText.Get("ContentFileInventory_010"));
                        }

                        if (relative.Split('/').Any(segment => segment.IndexOfAny(InvalidPathSegmentCharacters) >= 0))
                        {
                            throw new InvalidDataException(EditorText.Get("ContentFileInventory_011"));
                        }

                        var fullPath = Path.GetFullPath(Path.Combine(root, relative));
                        relative = Normalize(Path.GetRelativePath(root, fullPath));
                        if (relative is "." or ".." || relative.StartsWith("../", StringComparison.Ordinal))
                        {
                            throw new InvalidDataException(EditorText.Get("ContentFileInventory_012"));
                        }

                        if (!declared.TryAdd(relative, length))
                        {
                            issues.Add(EditorText.Format("ContentFileInventory_013", lineNumber, relative));
                        }
                    }
                    catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException or InvalidDataException or RegexMatchTimeoutException)
                    {
                        manifestReadable = false;
                        issues.Add(EditorText.Format("ContentFileInventory_014", lineNumber, ex.Message));
                    }
                }
            }
        }
        catch (FileNotFoundException)
        {
            // A concurrently removed manifest leaves an incomplete observation.
            manifestReadable = !actual.ContainsKey("modfiles.txt");
            if (!manifestReadable)
            {
                hasManifest = true;
                issues.Add(EditorText.Get("ContentFileInventory_015"));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            hasManifest = true;
            manifestReadable = false;
            issues.Add(EditorText.Format("ContentFileInventory_016", ex.Message));
        }

        var files = new List<ContentInventoryFile>();
        foreach (var path in actual.Keys.Concat(declared.Keys).Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var exists = actual.TryGetValue(path, out var actualLength);
            var listed = declared.TryGetValue(path, out var declaredLength);
            var status = !manifestReadable ? ContentManifestMatch.Unknown
                : !hasManifest ? ContentManifestMatch.NoManifest
                : listed && exists ? ContentManifestMatch.Listed
                : !exists ? treeComplete ? ContentManifestMatch.Missing : ContentManifestMatch.Unknown
                : ContentManifestMatch.Unlisted;
            var kind = Classify(path);
            var candidate = kind is ContentInventoryFileKind.Data or ContentInventoryFileKind.LocalizationXml or ContentInventoryFileKind.LocalizationBinary &&
                            ContentDirectories.Any(directory => ContentFileOverlay.IsRootOrEnabledDlcPath(path, directory, prefixes));
            files.Add(new ContentInventoryFile(path, kind, status, exists ? actualLength : null,
                declaredLength, candidate));
        }

        return new ContentModFileInventory(sourceId, root, hasManifest,
            treeComplete && manifestReadable, files, issues);
    }

    private static string Normalize(string path) => path.Replace('\\', '/');

    private static ContentInventoryFileKind Classify(string path)
    {
        if (path.EndsWith(".string_table.xml", StringComparison.OrdinalIgnoreCase))
        {
            return ContentInventoryFileKind.LocalizationXml;
        }

        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".json" or ".darkest" or ".csv" => ContentInventoryFileKind.Data,
            ".loc" or ".loc2" => ContentInventoryFileKind.LocalizationBinary,
            ".png" or ".jpg" or ".jpeg" or ".dds" or ".skel" or ".atlas" or ".bank" or ".wav" or ".ogg" or ".anim" or ".ttf" => ContentInventoryFileKind.Asset,
            ".exe" or ".dll" or ".bat" or ".cmd" or ".ps1" or ".py" => ContentInventoryFileKind.Tool,
            _ => ContentInventoryFileKind.Other
        };
    }
}
