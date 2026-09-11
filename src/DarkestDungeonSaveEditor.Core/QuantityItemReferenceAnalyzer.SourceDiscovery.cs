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
                !IsEligibleReferencePath(normalized, enabledDlcPrefixes) ||
                !ContentDirectories.Any(directory =>
                    ContentFileOverlay.IsRootOrEnabledDlcPath(normalized, directory, enabledDlcPrefixes)) ||
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
        IReadOnlyList<string> enabledDlcPrefixes)
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
                .Distinct(StringComparer.OrdinalIgnoreCase)
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

        foreach (var path in paths)
        {
            var relativePath = ContentFileOverlay.NormalizeRelativePath(source, path);
            if (relativePath is null ||
                (!TextExtensions.Contains(Path.GetExtension(path)) &&
                 !NativeResourceFileRules.IsCurioTypeFile(relativePath, enabledDlcPrefixes) &&
                 !NativeResourceFileRules.IsLootFile(relativePath, enabledDlcPrefixes) &&
                 NativeResourceFileRules.ReferenceJsonKind(relativePath, enabledDlcPrefixes) == NativeReferenceJsonKind.None))
            {
                continue;
            }

            if (!IsEligibleReferencePath(relativePath, enabledDlcPrefixes) ||
                !ContentDirectories.Any(directory =>
                    ContentFileOverlay.IsRootOrEnabledDlcPath(relativePath, directory, enabledDlcPrefixes)) ||
                IsDefinitionOnlyPath(relativePath, enabledDlcPrefixes))
            {
                continue;
            }

            yield return path;
        }
    }

    private static bool IsEligibleReferencePath(string path, IReadOnlyList<string> enabledDlcPrefixes)
    {
        if (!NativeResourceFileRules.IsEligibleReferenceFile(path, enabledDlcPrefixes)) return false;
        if (path.EndsWith("csv", StringComparison.OrdinalIgnoreCase))
            return NativeResourceFileRules.IsCurioTypeFile(path, enabledDlcPrefixes);
        var prefix = enabledDlcPrefixes.OrderByDescending(value => value.Length)
            .FirstOrDefault(value => path.StartsWith(value + "/", StringComparison.OrdinalIgnoreCase));
        var mounted = prefix is null ? path : path[(prefix.Length + 1)..];
        var suffix = new[] { ".info.darkest", ".art.darkest", ".override.darkest" }
            .FirstOrDefault(value => mounted.EndsWith(value, StringComparison.OrdinalIgnoreCase));
        // Actor item/loot references come from canonical definitions. Merely
        // listing heroes/inventory/README.darkest must not create a consumer.
        if (suffix is null)
            return !mounted.StartsWith("heroes/", StringComparison.OrdinalIgnoreCase) &&
                   !mounted.StartsWith("monsters/", StringComparison.OrdinalIgnoreCase);
        var id = Path.GetFileName(mounted)[..^suffix.Length];
        if (mounted.StartsWith("heroes/", StringComparison.OrdinalIgnoreCase))
            return mounted.Equals($"heroes/{id}/{id}{suffix}", StringComparison.OrdinalIgnoreCase);
        if (mounted.StartsWith("monsters/", StringComparison.OrdinalIgnoreCase))
            return id.Length >= 2 && suffix != ".override.darkest" &&
                mounted.Equals($"monsters/{id[..^2]}/{id}/{id}{suffix}", StringComparison.OrdinalIgnoreCase);
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
