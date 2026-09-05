using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DarkestDungeonSaveEditor.Core;

internal static partial class QuantityItemReferenceAnalyzer
{
    private static IReadOnlyList<ScannedContentFile> LoadEffectiveFiles(
        ActiveContentSnapshot activeContent,
        QuantityItemSaveContext saveContext,
        List<string> issues,
        ref bool scanComplete)
    {
        var enabledDlcPrefixes = ContentFileOverlay.GetEnabledDlcPrefixes(activeContent.Sources);
        var candidates = new List<ContentFileCandidate>();
        foreach (var source in activeContent.Sources)
        {
            try
            {
                AuditManifestReferenceFiles(
                    source,
                    enabledDlcPrefixes,
                    saveContext,
                    issues,
                    ref scanComplete);
                foreach (var path in EnumerateCandidateFiles(source, enabledDlcPrefixes))
                {
                    candidates.Add(new ContentFileCandidate(source, path));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                scanComplete = false;
                issues.Add($"Failed to enumerate quantity-item reference files in '{source.Directory}': {ex.Message}");
            }
        }

        var result = new List<ScannedContentFile>();
        foreach (var file in ContentFileOverlay.Resolve(candidates, "Quantity-item reference", issues))
        {
            try
            {
                if (DsonSaveCodec.IsDson(file.Path))
                {
                    continue;
                }

                result.Add(new ScannedContentFile(
                    file,
                    File.ReadAllText(file.Path, Encoding.UTF8),
                    IsLootPath(file.RelativePath)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
            {
                if (ReferencePathCanAffectContext(file.RelativePath, saveContext))
                {
                    scanComplete = false;
                    issues.Add($"Failed to read quantity-item reference file '{file.Path}': {ex.Message}");
                }
            }
        }

        return result;
    }

    private static void AuditManifestReferenceFiles(
        ActiveContentSource source,
        IReadOnlyList<string> enabledDlcPrefixes,
        QuantityItemSaveContext saveContext,
        List<string> issues,
        ref bool scanComplete)
    {
        var manifestPath = Path.Combine(source.Directory, "modfiles.txt");
        if (source.Kind is not ("workshop" or "local") || !ModManifestFile.Exists(manifestPath))
        {
            return;
        }

        var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in ModManifestFile.ReadEntries(manifestPath, ".json", ".darkest", ".csv"))
        {
            var relative = entry.RelativePath;

            var path = Path.GetFullPath(Path.Combine(source.Directory, relative));
            if (!IsInsideSource(source.Directory, path))
            {
                continue;
            }

            var normalized = ContentFileOverlay.NormalizeRelativePath(source, path);
            if (normalized is null ||
                !ContentDirectories.Any(directory =>
                    ContentFileOverlay.IsRootOrEnabledDlcPath(normalized, directory, enabledDlcPrefixes)) ||
                IsDefinitionOnlyPath(normalized) ||
                !ReferencePathCanAffectContext(normalized, saveContext) ||
                File.Exists(path) ||
                !reported.Add(path))
            {
                continue;
            }

            scanComplete = false;
            issues.Add($"Quantity-item reference file listed by active Mod is missing: {path}");
        }
    }

    private static IEnumerable<string> EnumerateCandidateFiles(
        ActiveContentSource source,
        IReadOnlyList<string> enabledDlcPrefixes)
    {
        if (!Directory.Exists(source.Directory))
        {
            yield break;
        }

        IEnumerable<string> paths;
        var manifestPath = Path.Combine(source.Directory, "modfiles.txt");
        if ((source.Kind is "workshop" or "local") && ModManifestFile.Exists(manifestPath))
        {
            paths = ModManifestFile.ReadEntries(manifestPath, ".json", ".darkest", ".csv")
                .Select(entry => Path.GetFullPath(Path.Combine(source.Directory, entry.RelativePath)))
                .Where(path => IsInsideSource(source.Directory, path) && File.Exists(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        else if (source.Kind is "workshop" or "local")
        {
            paths = Directory.EnumerateFiles(
                source.Directory,
                "*",
                new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.ReparsePoint
                });
        }
        else
        {
            paths = ContentDirectories
                .Select(directory => Path.Combine(source.Directory, directory))
                .Where(Directory.Exists)
                .SelectMany(directory => Directory.EnumerateFiles(
                    directory,
                    "*",
                    new EnumerationOptions
                    {
                        RecurseSubdirectories = true,
                        IgnoreInaccessible = true,
                        AttributesToSkip = FileAttributes.ReparsePoint
                    }));
        }

        foreach (var path in paths)
        {
            if (!TextExtensions.Contains(Path.GetExtension(path)))
            {
                continue;
            }

            var relativePath = ContentFileOverlay.NormalizeRelativePath(source, path);
            if (relativePath is null ||
                !ContentDirectories.Any(directory =>
                    ContentFileOverlay.IsRootOrEnabledDlcPath(relativePath, directory, enabledDlcPrefixes)) ||
                IsDefinitionOnlyPath(relativePath))
            {
                continue;
            }

            yield return path;
        }
    }

    private static bool IsInsideSource(string sourceDirectory, string path)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(sourceDirectory), path);
        return !Path.IsPathRooted(relative) &&
               !relative.Equals("..", StringComparison.Ordinal) &&
               !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
               !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static bool IsDefinitionOnlyPath(string relativePath)
    {
        var normalized = $"/{relativePath.Replace('\\', '/').Trim('/')}";
        return normalized.Contains("/localization/", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("/inventory/", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("/effects/", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("/scripts/starting_save/", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("/shared/buffs/", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("/trinkets/", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith(".effects.darkest", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLootPath(string relativePath)
    {
        var normalized = $"/{relativePath.Replace('\\', '/').Trim('/')}";
        return normalized.Contains("/loot/", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith(".loot.json", StringComparison.OrdinalIgnoreCase);
    }

}
