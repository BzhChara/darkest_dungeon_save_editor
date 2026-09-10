namespace DarkestDungeonSaveEditor.Core;

/// <summary>Profile saves, including persistent expedition folders, without game backup copies or links.</summary>
internal static class ProfileSaveFiles
{
    internal static IEnumerable<string> Enumerate(string profileDirectory)
    {
        var root = RaidSaveLocation.ResolvePath(profileDirectory, string.Empty);
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            foreach (var file in Directory.EnumerateFiles(directory, "persist*.json", SearchOption.TopDirectoryOnly))
            {
                _ = RaidSaveLocation.ResolvePath(root, Path.GetRelativePath(root, file));
                yield return file;
            }
            foreach (var child in Directory.EnumerateDirectories(directory))
            {
                if (Path.GetFileName(child).Equals("backup", StringComparison.OrdinalIgnoreCase) ||
                    (File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) continue;
                pending.Push(child);
            }
        }
    }

    internal static string BackupPath(string profileDirectory, string backupDirectory, string source)
    {
        var path = RaidSaveLocation.ResolvePath(backupDirectory, Path.GetRelativePath(profileDirectory, source));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }
}
