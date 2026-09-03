using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DarkestDungeonSaveEditor.Core;

public sealed partial class SaveEditService
{
    private static void ValidateProfile(SaveProfile profile)
    {
        var expectedEstatePath = Path.GetFullPath(Path.Combine(profile.ProfileDirectory, "persist.estate.json"));
        if (!expectedEstatePath.Equals(Path.GetFullPath(profile.EstateSavePath), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Estate save path is outside the selected profile directory.");
        }

        if (!File.Exists(expectedEstatePath))
        {
            throw new FileNotFoundException("persist.estate.json was not found in the selected profile.", expectedEstatePath);
        }
    }

    private static void ValidateActiveContentSnapshot(
        SaveProfile profile,
        ActiveContentSnapshot activeContent)
    {
        if (!Path.GetFullPath(activeContent.Profile.ProfileDirectory)
                .Equals(Path.GetFullPath(profile.ProfileDirectory), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The active content snapshot belongs to a different save profile.");
        }

        var gameSavePath = Path.Combine(profile.ProfileDirectory, "persist.game.json");
        if (!File.Exists(gameSavePath) ||
            !ComputeSha256(gameSavePath).Equals(activeContent.SourceGameSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The active Mod/DLC configuration changed after the content catalog was loaded; reload the catalog.");
        }
    }

    private static IReadOnlyList<PreparedContentFileFingerprint> CaptureManifestFingerprints(
        IReadOnlyList<ActiveContentSource> sources)
    {
        return sources
            .Where(source => source.Kind is "workshop" or "local")
            .Select(source => Path.GetFullPath(Path.Combine(source.Directory, "modfiles.txt")))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => File.Exists(path)
                ? new PreparedContentFileFingerprint(path, true, ComputeSha256(path))
                : new PreparedContentFileFingerprint(path, false, string.Empty))
            .ToArray();
    }

    private static void ValidateManifestFingerprints(
        IReadOnlyList<PreparedContentFileFingerprint> fingerprints,
        string changeContext)
    {
        foreach (var fingerprint in fingerprints)
        {
            var exists = File.Exists(fingerprint.Path);
            if (exists != fingerprint.Exists ||
                (exists && !ComputeSha256(fingerprint.Path).Equals(
                    fingerprint.Sha256,
                    StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    $"An active Mod manifest changed {changeContext}: {fingerprint.Path}");
            }
        }
    }

    private static void EnsureGameIsNotRunning()
    {
        foreach (var processName in new[] { "Darkest", "DarkestDungeon" })
        {
            Process[] processes;
            try
            {
                processes = Process.GetProcessesByName(processName);
            }
            catch
            {
                continue;
            }

            try
            {
                if (processes.Length > 0)
                {
                    throw new InvalidOperationException(
                        "Darkest Dungeon is running. Close the game before applying a save edit.");
                }
            }
            finally
            {
                foreach (var process in processes)
                {
                    process.Dispose();
                }
            }
        }
    }

    private static bool RevisionMatches(string leftPath, string rightPath)
    {
        using var left = File.OpenRead(leftPath);
        using var right = File.OpenRead(rightPath);
        if (left.Length < 8 || right.Length < 8)
        {
            return false;
        }

        left.Position = 4;
        right.Position = 4;
        Span<byte> leftRevision = stackalloc byte[4];
        Span<byte> rightRevision = stackalloc byte[4];
        left.ReadExactly(leftRevision);
        right.ReadExactly(rightRevision);
        return leftRevision.SequenceEqual(rightRevision);
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string ComputeSha256(Stream stream)
    {
        stream.Position = 0;
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string SanitizePathSegment(string value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            normalized = normalized.Replace(invalid, '_');
        }

        return normalized;
    }

    private static void WriteJson<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(value, JsonSupport.SerializerOptions), Utf8NoBom);
    }
}
