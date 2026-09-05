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
                return new ContentModFileInventory(sourceId, root, false, false, [], ["跳过来源目录重解析点，未读取其目标。"]);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ContentModFileInventory(sourceId, root, false, false, [], [$"来源目录无法读取：{ex.Message}"]);
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
                    issues.Add($"跳过扫描期间变化的重解析点：{Path.GetRelativePath(root, directory)}");
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
                            issues.Add($"跳过重解析点：{Path.GetRelativePath(root, path)}");
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
                                issues.Add($"文件路径大小写冲突：{relative}");
                            }
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        treeComplete = false;
                        issues.Add($"文件信息无法读取：{Path.GetRelativePath(root, path)}（{ex.Message}）");
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                treeComplete = false;
                issues.Add($"目录未完成扫描：{Path.GetRelativePath(root, directory)}（{ex.Message}）");
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
                issues.Add("清单不是普通文件，未读取其目标。");
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
                                throw new InvalidDataException("文件长度无效");
                            }

                            length = value;
                        }

                        relative = relative.Replace('\\', '/');
                        if (Path.IsPathRooted(relative) || relative.Contains(':'))
                        {
                            throw new InvalidDataException("清单路径不是相对路径");
                        }

                        if (relative.Split('/').Any(segment => segment.IndexOfAny(InvalidPathSegmentCharacters) >= 0))
                        {
                            throw new InvalidDataException("清单路径包含非法文件名字符");
                        }

                        var fullPath = Path.GetFullPath(Path.Combine(root, relative));
                        relative = Normalize(Path.GetRelativePath(root, fullPath));
                        if (relative is "." or ".." || relative.StartsWith("../", StringComparison.Ordinal))
                        {
                            throw new InvalidDataException("清单路径越出 Mod 目录");
                        }

                        if (!declared.TryAdd(relative, length))
                        {
                            issues.Add($"清单重复路径（第 {lineNumber} 行）：{relative}");
                        }
                    }
                    catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException or InvalidDataException or RegexMatchTimeoutException)
                    {
                        manifestReadable = false;
                        issues.Add($"清单第 {lineNumber} 行未解析：{ex.Message}");
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
                issues.Add("清单在扫描期间消失，请重新加载目录。");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            hasManifest = true;
            manifestReadable = false;
            issues.Add($"清单无法读取：{ex.Message}");
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
