using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace DarkestDungeonSaveEditor.Core;

/// <summary>The game selects persistent expeditions with raid_save, independently of dungeon ID.</summary>
public sealed record RaidSaveLocation(string ProfileDirectory, string RelativeDirectory, string? GameSha256 = null)
{
    public string MapPath => GetPath("persist.map.json");
    public string RaidPath => GetPath("persist.raid.json");

    public string GetPath(string fileName) => ResolvePath(ProfileDirectory,
        fileName is "persist.map.json" or "persist.raid.json"
            ? Path.Combine(RelativeDirectory, fileName) : fileName);

    public static RaidSaveLocation FromGame(string profileDirectory, JsonObject game)
    {
        var root = JsonSupport.RequireObject(game, "base_root");
        var relative = string.Empty;
        if (root.TryGetPropertyValue("raid_save", out var node))
        {
            if (node is not JsonValue value || !value.TryGetValue<string>(out relative))
                throw new InvalidDataException("persist.game.json 的 raid_save 不是有效的副本目录。");
        }
        relative = NormalizeRelative(relative);
        _ = ResolvePath(profileDirectory, relative);
        return new(Path.GetFullPath(profileDirectory), relative);
    }

    public SaveProfile Bind(SaveProfile profile)
    {
        if (!Path.GetFullPath(profile.ProfileDirectory).Equals(Path.GetFullPath(ProfileDirectory), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("副本目录不属于所选档案。");
        return profile with { RaidSaveRelativeDirectory = RelativeDirectory };
    }

    public static async Task<RaidSaveLocation> ReadAsync(string profileDirectory, DsonSaveCodec codec,
        CancellationToken cancellationToken = default, bool allowMissingGame = false)
    {
        var gamePath = ResolvePath(profileDirectory, "persist.game.json");
        if (allowMissingGame && !File.Exists(gamePath))
            return new(Path.GetFullPath(profileDirectory), string.Empty);
        var workspace = Path.Combine(Path.GetTempPath(), "DarkestDungeonSaveEditor", "raid-location", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        try
        {
            var source = Path.Combine(workspace, "game.source");
            var decoded = Path.Combine(workspace, "game.json");
            string hash;
            using (var stream = new FileStream(gamePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
                stream.Position = 0;
                using var copy = File.Create(source);
                await stream.CopyToAsync(copy, cancellationToken).ConfigureAwait(false);
            }
            await codec.DecodeAsync(source, decoded, cancellationToken).ConfigureAwait(false);
            var location = FromGame(profileDirectory, JsonSupport.ReadObject(decoded)) with { GameSha256 = hash };
            location.ValidateGameUnchanged();
            return location;
        }
        finally
        {
            try { Directory.Delete(workspace, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public void ValidateGameUnchanged()
    {
        if (GameSha256 is null) return;
        using var stream = File.OpenRead(GetPath("persist.game.json"));
        if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(GameSha256, StringComparison.OrdinalIgnoreCase))
            throw new IOException("副本入口在读取期间发生变化，请等待完整存档后重试。");
    }

    internal static string NormalizeRelative(string value)
    {
        if (Path.IsPathRooted(value) || value.StartsWith('/') || value.StartsWith('\\'))
            throw new InvalidDataException("副本存档路径必须位于当前档案内。");
        value = value.Replace('\\', '/').TrimEnd('/');
        if (value.Length == 0) return string.Empty;
        var parts = value.Split('/');
        if (parts.Any(part => part.Length == 0 || part is "." or ".." ||
            part.EndsWith('.') || part.EndsWith(' ') || part.Contains(':') ||
            part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            throw new InvalidDataException("副本存档目录包含无效路径，不能读取或写入。");
        return Path.Combine(parts);
    }

    internal static string ResolvePath(string profileDirectory, string relativePath)
    {
        var root = Path.GetFullPath(profileDirectory);
        var relative = NormalizeRelative(relativePath);
        var current = root;
        RejectLink(current);
        foreach (var part in relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            RejectLink(current);
        }
        return current;
    }

    private static void RejectLink(string path)
    {
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"副本存档路径不能经过符号链接或目录联接：{path}");
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
    }
}
