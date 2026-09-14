using System.Security.Cryptography;
using System.Text;

namespace DarkestDungeonSaveEditor.Core;

internal sealed record EffectiveInventoryCapacity(
    int MaxSlots, ActiveContentSource Source, string SourcePath, string SourceSha256);

internal static class InventorySystemConfigCatalog
{
    internal static EffectiveInventoryCapacity? ReadCapacity(
        IReadOnlyList<ActiveContentSource> sources, string type, List<string> issues)
    {
        var candidates = new List<ContentFileCandidate>();
        var prefixes = ContentFileOverlay.GetEnabledDlcPrefixes(sources);
        var unresolved = false;
        foreach (var source in sources)
        {
            if (!Directory.Exists(source.Directory))
            {
                issues.Add($"Inventory config content source is missing: {source.Directory}");
                unresolved = true;
                continue;
            }
            candidates.AddRange(EnumerateFiles(source, prefixes, issues)
                .Select(path => new ContentFileCandidate(source, path)));
        }

        var resolutionIssues = new List<string>();
        var files = NativeContentFileResolver.Resolve(candidates, sources, "Inventory system config", resolutionIssues);
        issues.AddRange(resolutionIssues);
        if (unresolved || resolutionIssues.Count > 0) return null;

        var targetHash = HashType(type);
        EffectiveInventoryCapacity? capacity = null;
        var lastAssignmentPath = string.Empty;
        foreach (var file in files)
        {
            try
            {
                // Parse and fingerprint the same captured bytes for preview/commit guards.
                var bytes = File.ReadAllBytes(file.Path);
                // The shared reader stops at NUL while retaining earlier records.
                // Fingerprint all bytes, including the unread tail, for save guards.
                var sha256 = Convert.ToHexString(SHA256.HashData(bytes));
                foreach (var (kind, body) in NativeDarkestReader.ReadRecordsFromText(Encoding.UTF8.GetString(bytes)))
                {
                    if (kind != "inventory_system_config") continue;
                    var declaredType = NativeDarkestReader.ReadString(body, ".type") ?? string.Empty;
                    if (HashType(declaredType) != targetHash) continue;
                    if (!declaredType.Equals(type, StringComparison.Ordinal))
                    {
                        issues.Add($"Inventory type '{declaredType}' collides with native type '{type}'; capacity is unresolved: {file.Path}");
                        unresolved = true;
                        continue;
                    }

                    // Build 27890: 0x1404C81C0 reuses the hash-keyed config object.
                    // 0x1404C83EF finds the last max_slots field; omission skips
                    // assignment, otherwise atoi replaces node+0x64 in file order.
                    if (NativeDarkestReader.FindValue(body, ".max_slots") < 0) continue;
                    lastAssignmentPath = file.Path;
                    var slots = NativeDarkestReader.ReadInt(body, ".max_slots");
                    capacity = slots is > 0
                        ? new EffectiveInventoryCapacity(slots.Value, file.Source, file.Path, sha256)
                        : null;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
            {
                issues.Add($"Failed to read inventory system config '{file.Path}': {ex.Message}");
                unresolved = true;
            }
        }

        if (unresolved) return null;
        if (capacity is null)
        {
            issues.Add(lastAssignmentPath.Length == 0
                ? $"No active positive {type} max_slots assignment was found; capacity editing is disabled."
                : $"The final {type} max_slots assignment is invalid or non-positive; capacity editing is disabled instead of falling back to an earlier value: {lastAssignmentPath}");
        }
        return capacity;
    }

    private static IReadOnlyList<string> EnumerateFiles(
        ActiveContentSource source, IReadOnlyList<string> enabledDlcPrefixes, List<string> issues)
    {
        if (source.Kind is not ("workshop" or "local"))
        {
            var directory = Path.Combine(source.Directory, "inventory");
            return Directory.Exists(directory)
                ? NativeDirectoryDiscovery.EnumerateFiles(directory, "*darkest", SearchOption.AllDirectories)
                    .Where(path => NativeResourceFileRules.IsInventoryConfigFile(Path.GetRelativePath(source.Directory, path), []))
                    .Order(StringComparer.Ordinal).ToArray()
                : [];
        }

        var manifest = Path.Combine(source.Directory, "modfiles.txt");
        ModManifestFile.Require(manifest);
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in ModManifestFile.ReadEntries(manifest, "darkest"))
        {
            var path = Path.GetFullPath(Path.Combine(source.Directory, entry.RelativePath));
            var relative = Path.GetRelativePath(Path.GetFullPath(source.Directory), path);
            if (Path.IsPathRooted(relative) || relative == ".." ||
                relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                issues.Add($"Ignored inventory config manifest path outside its Mod directory: {entry.RawLine.Trim()}");
                continue;
            }
            if (!NativeResourceFileRules.IsInventoryConfigFile(relative, enabledDlcPrefixes, manifestDirectory: true)) continue;
            if (!File.Exists(path))
                issues.Add($"Inventory system config listed by Mod is missing: {path}");
            // Keep missing candidates until overlay resolution. A missing winner
            // must fail its read, not expose the overridden lower-priority bytes.
            result.Add(path);
        }
        return result.Order(StringComparer.Ordinal).ToArray();
    }

    private static uint HashType(string type)
    {
        // The native type reader writes a 64-byte, NUL-terminated UTF-8 buffer.
        uint hash = 0;
        foreach (var value in Encoding.UTF8.GetBytes(type).Take(63))
        {
            if (value == 0) break;
            hash = unchecked(hash * 53 + value);
        }
        return hash;
    }
}
