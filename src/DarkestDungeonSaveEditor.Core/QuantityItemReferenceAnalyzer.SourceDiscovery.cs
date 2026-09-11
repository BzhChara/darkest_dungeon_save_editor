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
        var actorPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in activeContent.Sources)
        {
            try
            {
                foreach (var directory in new[] { "heroes", "monsters" })
                foreach (var path in NativeContentFileResolver.EnumerateActorInfoFiles(source, enabledDlcPrefixes, directory, issues))
                {
                    var id = NativeContentFileResolver.ReadDiscoveredActorId(Path.GetRelativePath(source.Directory, path));
                    if (directory == "monsters" && id.Length < 2) continue;
                    var stem = directory == "heroes" ? $"heroes/{id}/{id}" : $"monsters/{id[..^2]}/{id}/{id}";
                    foreach (var suffix in directory == "heroes"
                                 ? new[] { ".info.darkest", ".art.darkest", ".override.darkest" }
                                 : new[] { ".info.darkest", ".art.darkest" }) actorPaths.Add(stem + suffix);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                scanComplete = false;
                issues.Add($"Failed to enumerate actor item consumers in '{source.Directory}': {ex.Message}");
            }
        }
        var candidates = new List<ContentFileCandidate>();
        foreach (var source in activeContent.Sources)
        {
            try
            {
                AuditManifestReferenceFiles(
                    source,
                    enabledDlcPrefixes,
                    actorPaths,
                    saveContext,
                    issues,
                    ref scanComplete);
                foreach (var path in EnumerateCandidateFiles(source, enabledDlcPrefixes, actorPaths))
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
        foreach (var file in NativeContentFileResolver.Resolve(candidates, activeContent.Sources, "Quantity-item reference", issues))
        {
            try
            {
                if (DsonSaveCodec.IsDson(file.Path))
                {
                    continue;
                }

                result.Add(new ScannedContentFile(
                    file,
                    NativeResourceFileRules.MountedPath(file.RelativePath, enabledDlcPrefixes),
                    NativeResourceFileRules.IsCurioTypeFile(file.RelativePath, enabledDlcPrefixes)
                        ? new UTF8Encoding(false, true).GetString(File.ReadAllBytes(file.Path))
                        : File.ReadAllText(file.Path, Encoding.UTF8),
                    NativeResourceFileRules.IsLootFile(file.RelativePath, enabledDlcPrefixes),
                    NativeResourceFileRules.ReferenceJsonKind(file.RelativePath, enabledDlcPrefixes),
                    NativeResourceFileRules.IsCurioTypeFile(file.RelativePath, enabledDlcPrefixes)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
            {
                if (ReferencePathCanAffectContext(
                        NativeResourceFileRules.MountedPath(file.RelativePath, enabledDlcPrefixes), saveContext))
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
        IReadOnlySet<string> actorPaths,
        QuantityItemSaveContext saveContext,
        List<string> issues,
        ref bool scanComplete)
    {
        var manifestPath = Path.Combine(source.Directory, "modfiles.txt");
        if (source.Kind is not ("workshop" or "local"))
        {
            return;
        }

        var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in ModManifestFile.ReadEntries(manifestPath, "json", ".darkest", "csv"))
        {
            var relative = entry.RelativePath;

            var path = Path.GetFullPath(Path.Combine(source.Directory, relative));
            if (!IsInsideSource(source.Directory, path))
            {
                continue;
            }

            var normalized = ContentFileOverlay.NormalizeRelativePath(source, path);
            if (normalized is null ||
                !IsEligibleReferencePath(normalized, enabledDlcPrefixes, true, actorPaths) ||
                !ContentDirectories.Any(directory =>
                    NativeResourceFileRules.IsInDirectory(normalized, directory, enabledDlcPrefixes,
                        directory is not ("heroes" or "monsters"))) ||
                IsDefinitionOnlyPath(normalized, enabledDlcPrefixes) ||
                !ReferencePathCanAffectContext(
                    NativeResourceFileRules.MountedPath(normalized, enabledDlcPrefixes), saveContext) ||
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
        IReadOnlyList<string> enabledDlcPrefixes,
        IReadOnlySet<string> actorPaths)
    {
        if (!Directory.Exists(source.Directory))
        {
            yield break;
        }

        IEnumerable<string> paths;
        var manifestPath = Path.Combine(source.Directory, "modfiles.txt");
        if (source.Kind is "workshop" or "local")
        {
            ModManifestFile.Require(manifestPath);
            paths = ModManifestFile.ReadEntries(manifestPath, "json", ".darkest", "csv")
                .Select(entry => Path.GetFullPath(Path.Combine(source.Directory, entry.RelativePath)))
                .Where(path => IsInsideSource(source.Directory, path) && File.Exists(path))
                .ToArray();
        }

        else
        {
            paths = ContentDirectories
                .Select(directory => Path.Combine(source.Directory, directory))
                .Where(Directory.Exists)
                .SelectMany(directory => NativeDirectoryDiscovery.EnumerateFiles(
                    directory,
                    "*",
                    new EnumerationOptions
                    {
                        RecurseSubdirectories = true,
                        IgnoreInaccessible = true,
                        AttributesToSkip = FileAttributes.ReparsePoint
                    }));
        }

        // Canonical actor opens can use paths excluded from physical discovery.
        paths = paths.Concat(new[] { "heroes", "monsters" }.SelectMany(directory =>
            NativeContentFileResolver.EnumerateActorOpenFiles(source, enabledDlcPrefixes, directory, [])));
        var manifestDirectory = source.Kind is "local" or "workshop";
        var acceptedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            var relativePath = ContentFileOverlay.NormalizeRelativePath(source, path);
            if (relativePath is null ||
                (!TextExtensions.Contains(Path.GetExtension(path)) &&
                 !NativeResourceFileRules.IsCurioTypeFile(relativePath, enabledDlcPrefixes, manifestDirectory) &&
                 !NativeResourceFileRules.IsLootFile(relativePath, enabledDlcPrefixes, manifestDirectory) &&
                 NativeResourceFileRules.ReferenceJsonKind(relativePath, enabledDlcPrefixes, manifestDirectory) == NativeReferenceJsonKind.None))
            {
                continue;
            }

            if (!IsEligibleReferencePath(relativePath, enabledDlcPrefixes, manifestDirectory, actorPaths) ||
                !ContentDirectories.Any(directory =>
                    NativeResourceFileRules.IsInDirectory(relativePath, directory, enabledDlcPrefixes,
                        manifestDirectory && directory is not ("heroes" or "monsters"))) ||
                IsDefinitionOnlyPath(relativePath, enabledDlcPrefixes))
            {
                continue;
            }

            // Raw manifest aliases can have different directory eligibility.
            // Deduplicate physical paths only after rejecting ineligible names.
            if (acceptedPaths.Add(path)) yield return path;
        }
    }

    private static bool IsEligibleReferencePath(string path, IReadOnlyList<string> enabledDlcPrefixes,
        bool manifestDirectory, IReadOnlySet<string> actorPaths)
    {
        if (!NativeResourceFileRules.IsEligibleReferenceFile(path, enabledDlcPrefixes, manifestDirectory)) return false;
        if (path.EndsWith("csv", StringComparison.OrdinalIgnoreCase))
            return NativeResourceFileRules.IsCurioTypeFile(path, enabledDlcPrefixes, manifestDirectory);
        // Registered actors open canonical paths, independently of the file
        // used to discover the ID. Preserve Windows aliases for these opens.
        var mounted = NativeResourceFileRules.MountedPath(path, enabledDlcPrefixes);
        if (mounted.StartsWith("heroes/", StringComparison.OrdinalIgnoreCase) ||
            mounted.StartsWith("monsters/", StringComparison.OrdinalIgnoreCase)) return actorPaths.Contains(mounted);
        return true;
    }

    private static bool IsInsideSource(string sourceDirectory, string path)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(sourceDirectory), path);
        return !Path.IsPathRooted(relative) &&
               !relative.Equals("..", StringComparison.Ordinal) &&
               !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
               !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static bool IsDefinitionOnlyPath(string relativePath, IReadOnlyList<string> enabledDlcPrefixes)
    {
        // Only mounted resource roots identify consumers. A loot/trinkets/
        // subdirectory is still part of the loot consumer, not a trinket library.
        var normalized = NativeResourceFileRules.MountedPath(relativePath, enabledDlcPrefixes);
        return normalized.StartsWith("localization/", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("inventory/", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("effects/", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("scripts/starting_save/", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("shared/buffs/", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("trinkets/", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith(".effects.darkest", StringComparison.OrdinalIgnoreCase);
    }

}
