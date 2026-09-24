using System.Text.Json;

namespace DarkestDungeonSaveEditor.Core;

/// <summary>Per-user language preference; save files and installation directories are never used.</summary>
public static class EditorLanguageSettings
{
    public static string DefaultPath => Path.Combine(
        SaveEditorLocations.CreateDefault().ApplicationDataDirectory, "language.json");

    public static bool IsSupported(string? preference) => preference is "system" or "zh-CN" or "en";

    public static string Load(string path, out string? error)
    {
        error = null;
        try
        {
            if (!File.Exists(path)) return "system";
            using var stream = File.OpenRead(path);
            var settings = JsonSerializer.Deserialize<Settings>(stream);
            if (IsSupported(settings?.Language)) return settings!.Language;
            error = "Unsupported or missing language preference.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            error = ex.Message;
        }
        return "system";
    }

    public static void Save(string path, string preference)
    {
        if (!IsSupported(preference)) throw new ArgumentException("Unsupported language preference.", nameof(preference));
        path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, new Settings(preference));
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private sealed record Settings(string Language);
}
