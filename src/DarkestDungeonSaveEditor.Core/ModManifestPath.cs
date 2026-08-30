namespace DarkestDungeonSaveEditor.Core;

internal static class ModManifestPath
{
    public static string? Extract(string rawLine, params string[] suffixes)
    {
        ArgumentNullException.ThrowIfNull(rawLine);
        ArgumentNullException.ThrowIfNull(suffixes);

        foreach (var suffix in suffixes)
        {
            var searchIndex = 0;
            while (searchIndex < rawLine.Length)
            {
                var marker = rawLine.IndexOf(suffix, searchIndex, StringComparison.OrdinalIgnoreCase);
                if (marker < 0)
                {
                    break;
                }

                var end = marker + suffix.Length;
                if (end == rawLine.Length || char.IsWhiteSpace(rawLine[end]))
                {
                    return rawLine[..end]
                        .Trim()
                        .Replace('/', Path.DirectorySeparatorChar)
                        .Replace('\\', Path.DirectorySeparatorChar);
                }

                searchIndex = end;
            }
        }

        return null;
    }
}
