using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace DarkestDungeonSaveEditor.Core;

public static partial class QuantityItemCatalog
{
    private static IReadOnlyList<string> EnumerateInventoryItemFiles(
        ActiveContentSource source,
        IReadOnlyList<string> enabledDlcPrefixes,
        List<string> issues)
    {
        if (!Directory.Exists(source.Directory))
        {
            return [];
        }

        if (source.Kind is not ("workshop" or "local"))
        {
            var inventoryRoot = Path.Combine(source.Directory, "inventory");
            return Directory.Exists(inventoryRoot)
                ? NativeDirectoryDiscovery.EnumerateFiles(inventoryRoot, "*darkest", SearchOption.AllDirectories)
                    .Where(path => NativeResourceFileRules.IsInventoryItemFile(Path.GetRelativePath(source.Directory, path), []))
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray()
                : [];
        }

        var manifestPath = Path.Combine(source.Directory, "modfiles.txt");
        ModManifestFile.Require(manifestPath);

        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in ModManifestFile.ReadEntries(manifestPath, "darkest"))
        {
            var rawLine = entry.RawLine;
            var relativePath = entry.RelativePath;

            var path = Path.GetFullPath(Path.Combine(source.Directory, relativePath));
            var relativeToRoot = Path.GetRelativePath(source.Directory, path);
            if (Path.IsPathRooted(relativeToRoot) ||
                relativeToRoot.Equals("..", StringComparison.Ordinal) ||
                relativeToRoot.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                issues.Add($"Ignored inventory-item manifest path outside its Mod directory: {rawLine.Trim()}");
                continue;
            }

            if (!NativeResourceFileRules.IsInventoryItemFile(relativeToRoot, enabledDlcPrefixes, manifestDirectory: true))
            {
                continue;
            }

            if (!File.Exists(path))
            {
                issues.Add($"Inventory item file listed by Mod is missing: {path}");
            }

            // Manifest entries participate in the overlay even if their bytes are missing.
            result.Add(path);
        }

        return result.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

}
