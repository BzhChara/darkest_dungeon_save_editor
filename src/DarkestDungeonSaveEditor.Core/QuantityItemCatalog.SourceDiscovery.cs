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
                ? Directory.EnumerateFiles(inventoryRoot, $"*{InventoryItemSuffix}", SearchOption.AllDirectories)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray()
                : [];
        }

        var manifestPath = Path.Combine(source.Directory, "modfiles.txt");
        if (!ModManifestFile.Exists(manifestPath))
        {
            var files = ContentFileOverlay.GetFallbackContentRoots(source.Directory, enabledDlcPrefixes)
                .Select(root => Path.Combine(root, "inventory"))
                .Where(Directory.Exists)
                .SelectMany(directory => Directory.EnumerateFiles(
                    directory, $"*{InventoryItemSuffix}", SearchOption.AllDirectories))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (files.Length > 0)
            {
                issues.Add(
                    $"Mod has no modfiles.txt; quantity-item scan used standard inventory directories including enabled DLC paths: {source.Directory}");
            }

            return files;
        }

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in ModManifestFile.ReadEntries(manifestPath, InventoryItemSuffix))
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

            if (!ContentFileOverlay.IsRootOrEnabledDlcPath(relativeToRoot, "inventory", enabledDlcPrefixes))
            {
                continue;
            }

            if (!File.Exists(path))
            {
                issues.Add($"Inventory item file listed by Mod is missing: {path}");
                continue;
            }

            result.Add(path);
        }

        return result.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string StripLineComments(string text)
    {
        var result = new StringBuilder(text.Length);
        var inQuotes = false;
        var escaped = false;
        for (var index = 0; index < text.Length; index++)
        {
            var current = text[index];
            if (!inQuotes && current == '/' && index + 1 < text.Length && text[index + 1] == '/')
            {
                index += 2;
                while (index < text.Length && text[index] is not ('\r' or '\n'))
                {
                    index++;
                }

                if (index >= text.Length)
                {
                    break;
                }

                current = text[index];
            }

            result.Append(current);
            if (current is '\r' or '\n')
            {
                inQuotes = false;
                escaped = false;
                continue;
            }

            if (inQuotes && current == '\\' && !escaped)
            {
                escaped = true;
                continue;
            }

            if (current == '"' && !escaped)
            {
                inQuotes = !inQuotes;
            }

            escaped = false;
        }

        return result.ToString();
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

}
