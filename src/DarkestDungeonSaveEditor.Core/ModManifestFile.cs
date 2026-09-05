namespace DarkestDungeonSaveEditor.Core;

internal sealed record ModManifestEntry(string RawLine, string RelativePath);

internal static class ModManifestFile
{
    public static bool Exists(string manifestPath)
    {
        try
        {
            if ((File.GetAttributes(manifestPath) & FileAttributes.Directory) != 0)
            {
                throw new InvalidDataException($"Mod 清单路径不是文件：{manifestPath}。目录加载已停止。");
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
                    $"Mod 清单第 {index + 1} 行路径无效：{manifestPath}。目录加载已停止。", ex);
            }
        }

        return entries;
    }

    private static InvalidDataException Unreadable(string manifestPath, Exception exception) =>
        new($"无法读取 Mod 清单：{manifestPath}。目录加载已停止；请确认文件可访问后重新加载。", exception);
}
