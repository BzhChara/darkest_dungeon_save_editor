using System.Globalization;
using System.Text.Json;

namespace DarkestDungeonSaveEditor.Core;

internal static class SaveCommitMarker
{
    internal static void Publish(string path, SaveCommitResult result)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, result, JsonSupport.SerializerOptions);
                stream.Flush(flushToDisk: true);
            }
            // The marker is visible only after its complete JSON has been flushed.
            File.Move(temporary, path, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    internal static bool IsComplete(string path, string profileDirectory)
    {
        if (!File.Exists(path)) return false;
        try
        {
            var root = JsonSupport.ReadObject(path);
            var profile = JsonSupport.ReadString(root, "ProfileDirectory");
            var target = JsonSupport.ReadString(root, "TargetPath");
            var backup = JsonSupport.ReadString(root, "BackupDirectory");
            static bool Hash(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);
            return !string.IsNullOrWhiteSpace(profile) && !string.IsNullOrWhiteSpace(target) && !string.IsNullOrWhiteSpace(backup) &&
                Path.GetFullPath(profile).Equals(Path.GetFullPath(profileDirectory), StringComparison.OrdinalIgnoreCase) &&
                IsProfileTarget(profileDirectory, target) &&
                Path.GetFullPath(backup).Equals(Path.GetDirectoryName(Path.GetFullPath(path)), StringComparison.OrdinalIgnoreCase) &&
                Hash(JsonSupport.ReadString(root, "OriginalSha256")) && Hash(JsonSupport.ReadString(root, "FinalSha256")) &&
                DateTime.TryParse(JsonSupport.ReadString(root, "CommittedAtUtc"), CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var time) && time != default;
        }
        catch (Exception error) when (error is JsonException or InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool IsProfileTarget(string profileDirectory, string target)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(profileDirectory), Path.GetFullPath(target));
        _ = RaidSaveLocation.ResolvePath(profileDirectory, relative);
        var parts = relative.Split(Path.DirectorySeparatorChar);
        return parts.Length == 1 ||
            ((Path.GetFileName(target) is "persist.map.json" or "persist.raid.json") &&
             !parts.Any(part => part.Equals("backup", StringComparison.OrdinalIgnoreCase)));
    }
}
