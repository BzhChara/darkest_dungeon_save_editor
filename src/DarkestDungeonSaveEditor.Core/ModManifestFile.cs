namespace DarkestDungeonSaveEditor.Core;

internal sealed record ModManifestEntry(string RawLine, string RelativePath);

internal static class ModManifestFile
{
    public static void Require(string manifestPath)
    {
        if (!Exists(manifestPath))
            throw new InvalidDataException(EditorText.Format("ModManifestFile_001", Path.GetDirectoryName(manifestPath)));
    }

    public static bool Exists(string manifestPath)
    {
        try
        {
            if ((File.GetAttributes(manifestPath) & FileAttributes.Directory) != 0)
            {
                throw new InvalidDataException(EditorText.Format("ModManifestFile_002", manifestPath));
            }

            return true;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw Unreadable(manifestPath, ex);
        }
    }

    public static IReadOnlyList<ModManifestEntry> ReadEntries(string manifestPath, params string[] suffixes) =>
        ReadEntries(manifestPath, line => ModManifestPath.Extract(line, suffixes));

    public static IReadOnlyList<ModManifestEntry> ReadEntries(
        string manifestPath,
        Func<string, string?> extractPath)
    {
        string[] lines;
        try
        {
            lines = File.ReadAllLines(manifestPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw Unreadable(manifestPath, ex);
        }

        // Materialize and validate before returning anything: a failed read
        // must not expose a partial manifest or trigger manifest-free fallback.
        var root = Path.GetDirectoryName(Path.GetFullPath(manifestPath))!;
        var entries = new List<ModManifestEntry>();
        for (var index = 0; index < lines.Length; index++)
        {
            try
            {
                var relativePath = extractPath(lines[index]);
                if (relativePath is null)
                {
                    continue;
                }

                _ = Path.GetFullPath(Path.Combine(root, relativePath));
                entries.Add(new ModManifestEntry(lines[index], relativePath));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
            {
                throw new InvalidDataException(
                    EditorText.Format("ModManifestFile_003", index + 1, manifestPath), ex);
            }
        }

        return entries;
    }

    private static InvalidDataException Unreadable(string manifestPath, Exception exception) =>
        new(EditorText.Format("ModManifestFile_004", manifestPath), exception);
}
