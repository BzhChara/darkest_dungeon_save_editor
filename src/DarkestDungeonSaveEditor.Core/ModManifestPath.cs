namespace DarkestDungeonSaveEditor.Core;

internal static class ModManifestPath
{
    public static string? Extract(string rawLine, params string[] suffixes)
    {
        ArgumentNullException.ThrowIfNull(rawLine);
        ArgumentNullException.ThrowIfNull(suffixes);

        var path = rawLine.Trim();
        var lastFieldStart = path.Length;
        while (lastFieldStart > 0 && !char.IsWhiteSpace(path[lastFieldStart - 1]))
        {
            lastFieldStart--;
        }

        // The uploader appends a byte count. An extension followed by a space
        // may instead be part of a directory name, so never split at it.
        if (lastFieldStart > 0 && path[lastFieldStart..].All(character => character is >= '0' and <= '9'))
        {
            path = path[..lastFieldStart].TrimEnd();
        }

        return suffixes.Any(suffix => path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            ? path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar)
            : null;
    }
}
