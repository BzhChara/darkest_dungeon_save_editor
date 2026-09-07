namespace DarkestDungeonSaveEditor.Core;

// Windows x64 build 27890 directory-device FindFiles. Do not use this for
// manifest entries or canonical OpenFile lookups: those are different paths.
internal static class NativeDirectoryDiscovery
{
    public static IEnumerable<string> EnumerateFiles(string directory, string pattern, SearchOption searchOption) =>
        Directory.EnumerateFiles(directory, pattern, searchOption)
            .Where(path => IsDiscovered(directory, path));

    public static IEnumerable<string> EnumerateFiles(string directory, string pattern, EnumerationOptions options) =>
        Directory.EnumerateFiles(directory, pattern, options)
            .Where(path => IsDiscovered(directory, path));

    internal static bool IsDiscovered(string directory, string path)
    {
        var relative = Path.GetRelativePath(directory, path).Replace('\\', '/');
        // 0x140374790: skip dot names and any component containing _template.
        // 0x140375090: wcstombs_s fails outside the native default C locale's
        // single-byte range, and failed entries are not added to FindFiles.
        // Inspect the returned relative resource path, not the editor's host
        // workspace path. The latter need not match the game's mount prefix.
        return !relative.Any(character => character > '\u00ff') &&
            !relative.Split('/').Any(component => component.StartsWith('.') ||
                component.Contains("_template", StringComparison.Ordinal));
    }
}
