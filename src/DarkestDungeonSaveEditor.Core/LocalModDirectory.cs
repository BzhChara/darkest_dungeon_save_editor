namespace DarkestDungeonSaveEditor.Core;

/// <summary>The ordinary local-Mod root belongs to the selected game installation.</summary>
internal static class LocalModDirectory
{
    internal static string? Resolve(string gameDirectory, string? configuredDirectory, bool required = false)
    {
        var expected = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(gameDirectory, "mods")));
        if (string.IsNullOrWhiteSpace(configuredDirectory))
        {
            if (required) throw new InvalidOperationException(EditorText.Get("LocalModDirectory_Required"));
            return null;
        }

        var actual = Path.TrimEndingDirectorySeparator(Path.GetFullPath(configuredDirectory.Trim()));
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(EditorText.Format("LocalModDirectory_WrongRoot", expected));
        if (File.Exists(actual))
            throw new InvalidOperationException(EditorText.Format("LocalModDirectory_NotDirectory", actual));
        return actual;
    }
}
