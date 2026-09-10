using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace DarkestDungeonSaveEditor.Core;

internal sealed record EditorBattlePlacement(string AreaId, string TileId, int MashType, int MashIndex,
    IReadOnlyList<string> MonsterIds);

/// <summary>Successful map commits are the placement journal; failed previews/backups never grant ownership.</summary>
internal sealed class EditorBattleHistory(DsonSaveCodec codec, SaveEditorLocations locations)
{
    internal const string CleanupOperation = "ReconcileEditorBattles";
    private readonly Dictionary<string, (string Hash, string Identity)> _legacyIdentities = new(StringComparer.OrdinalIgnoreCase);

    internal static string RaidIdentity(JsonObject map, JsonObject raid)
    {
        var root = JsonSupport.RequireObject(raid, "base_root");
        // generated_N and fixed-map topology can both recur. The raid start stamp
        // must participate; never infer ownership from the map shape alone.
        if (root["start_elapsed_time"] is not JsonValue stamp || !stamp.TryGetValue<long>(out _)) return string.Empty;
        var instance = JsonSupport.RequireObject(root, "raid_instance");
        var staticAreas = JsonSupport.RequireObject(map, "base_root", "map", "static_dynamic", "static_save", "base_root", "areas");
        var topology = new JsonObject();
        foreach (var area in staticAreas.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (area.Value is not JsonObject value) throw new InvalidDataException("地图区域结构不完整。");
            var tiles = new JsonObject();
            foreach (var tile in JsonSupport.RequireObject(value, "tiles").OrderBy(pair => pair.Key, StringComparer.Ordinal))
                tiles[tile.Key] = new JsonObject { ["type"] = tile.Value?["type"]?.DeepClone(),
                    ["mappos"] = tile.Value?["mappos"]?.DeepClone(), ["door_to"] = tile.Value?["door_to"]?.DeepClone() };
            topology[area.Key] = new JsonObject { ["id"] = value["id"]?.DeepClone(), ["kind"] = value["kind"]?.DeepClone(), ["tiles"] = tiles };
        }
        var identity = new JsonObject { ["start"] = stamp.DeepClone(), ["topology"] = topology };
        foreach (var name in new[] { "id", "map_name", "dungeon", "difficulty", "length", "type", "is_plot_quest" })
            identity[name] = instance[name]?.DeepClone();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity.ToJsonString())));
    }

    internal async Task<IReadOnlyList<EditorBattlePlacement>> ReadAsync(SaveProfile profile, BattleMapSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var directory = Path.Combine(locations.BackupDirectory, SafeSegment(profile.SteamUserId), SafeSegment(profile.ProfileId));
        if (!Directory.Exists(directory)) return [];
        var relative = Path.GetRelativePath(profile.ProfileDirectory, Path.GetDirectoryName(snapshot.MapSavePath)!);
        var raidDirectory = relative == "." ? string.Empty : RaidSaveLocation.NormalizeRelative(relative);
        var records = new List<(JsonObject Manifest, string Directory, DateTime Time)>();
        foreach (var backup in Directory.EnumerateDirectories(directory).Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var marker = Path.Combine(backup, "commit-result.json");
            var manifestPath = Path.Combine(backup, "backup-manifest.json");
            if (!File.Exists(marker) || !File.Exists(manifestPath)) continue;
            var manifest = JsonSupport.ReadObject(manifestPath);
            var operation = JsonSupport.ReadString(manifest, "operation");
            if (operation is not ("PlaceBattle" or "DeleteContent" or "PlaceContent" or CleanupOperation)) continue;
            if (!SameProfile(manifest, profile)) continue;
            if (!RaidSaveLocation.NormalizeRelative(JsonSupport.ReadString(manifest, "RaidSaveRelativeDirectory"))
                    .Equals(raidDirectory, StringComparison.OrdinalIgnoreCase)) continue;
            if (operation == CleanupOperation && (snapshot.RaidIdentity.Length == 0 ||
                    JsonSupport.ReadString(manifest, "RaidIdentity") != snapshot.RaidIdentity)) continue;
            if (operation == CleanupOperation && File.Exists(Path.Combine(backup, "maintenance-recovered.json"))) continue;
            if (!SaveCommitMarker.IsComplete(marker, profile.ProfileDirectory))
                throw new InvalidDataException($"历史写入提交记录不完整，不能据此删除地图战斗：{marker}");
            var commit = JsonSupport.ReadObject(marker);
            if (!SamePath(JsonSupport.ReadString(commit, "ProfileDirectory"), profile.ProfileDirectory)) continue;
            var time = commit["CommittedAtUtc"]?.GetValue<DateTime>() ?? throw new InvalidDataException($"写入记录缺少完成时间：{marker}");
            records.Add((manifest, backup, time));
        }
        var placements = new Dictionary<(string, string), EditorBattlePlacement>();
        var ordered = records.OrderBy(record => record.Time).ThenBy(record => record.Directory, StringComparer.Ordinal).ToArray();
        var retired = Array.FindLastIndex(ordered, record =>
            JsonSupport.ReadString(record.Manifest, "operation") == CleanupOperation && record.Manifest["Invalidated"]?.GetValue<bool>() == true);
        foreach (var record in ordered.Skip(retired + 1))
        {
            var data = record.Manifest;
            if (JsonSupport.ReadString(data, "operation") == CleanupOperation)
            {
                // Cleanup retires the old journal, including entries already consumed by the game.
                if (data["Invalidated"]?.GetValue<bool>() == true) placements.Clear();
                continue;
            }
            var identity = JsonSupport.ReadString(data, "RaidIdentity");
            if (identity.Length == 0) identity = await ReadLegacyIdentityAsync(record.Directory, data, cancellationToken).ConfigureAwait(false);
            if (identity.Length == 0 || snapshot.RaidIdentity.Length == 0)
                throw new InvalidOperationException("战斗自动清理暂缓，历史写入记录缺少可核对的副本实例标识。");
            if (identity != snapshot.RaidIdentity) continue;
            var area = JsonSupport.ReadString(data, "AreaId");
            var tile = JsonSupport.ReadString(data, "TileId");
            if (JsonSupport.ReadString(data, "operation") != "PlaceBattle")
            {
                placements.Remove((area, tile));
                continue;
            }
            var encounter = JsonSupport.RequireObject(data, "encounter");
            placements[(area, tile)] = new(area, tile, encounter["MashType"]!.GetValue<int>(),
                encounter["MashIndex"]!.GetValue<int>(), encounter["MonsterIds"]!.AsArray().Select(id => id!.GetValue<string>()).ToArray());
        }
        return placements.Values.Where(entry => snapshot.Areas.Any(area => area.AreaId == entry.AreaId &&
            area.Tiles.Any(tile => tile.TileId == entry.TileId && tile.MashType == entry.MashType &&
                tile.MashIndex == entry.MashIndex && IsBattle(tile.Content)))).ToArray();
    }

    private async Task<string> ReadLegacyIdentityAsync(string directory, JsonObject manifest, CancellationToken token)
    {
        var location = await RaidSaveLocation.ReadAsync(directory, codec, token, allowMissingGame: true).ConfigureAwait(false);
        var mapPath = location.MapPath;
        var raidPath = location.RaidPath;
        var mapHash = Hash(mapPath);
        var raidHash = Hash(raidPath);
        if (!mapHash.Equals(JsonSupport.ReadString(manifest, "MapOriginalSha256"), StringComparison.OrdinalIgnoreCase) ||
            !raidHash.Equals(JsonSupport.ReadString(manifest, "RaidOriginalSha256"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"历史写入备份已改变，不能据此删除地图战斗：{directory}");
        var key = mapHash + raidHash;
        if (_legacyIdentities.TryGetValue(directory, out var cached) && cached.Hash == key) return cached.Identity;
        var captured = await new BattleMapSnapshotReader(codec).LoadAsync(directory, token).ConfigureAwait(false);
        _legacyIdentities[directory] = (key, captured.RaidIdentity);
        return captured.RaidIdentity;
    }

    internal static bool IsBattle(BattleMapTileContent content) => content is BattleMapTileContent.Battle or
        BattleMapTileContent.Ambush or BattleMapTileContent.GuardedCurio or BattleMapTileContent.GuardedTreasure or
        BattleMapTileContent.AmbushCurio or BattleMapTileContent.AmbushTreasure;

    private static bool SameProfile(JsonObject manifest, SaveProfile profile) =>
        JsonSupport.ReadString(manifest, "ProfileId") == profile.ProfileId &&
        JsonSupport.ReadString(manifest, "SteamUserId") == profile.SteamUserId &&
        SamePath(JsonSupport.ReadString(manifest, "ProfileDirectory"), profile.ProfileDirectory);
    private static bool SamePath(string left, string right) => !string.IsNullOrWhiteSpace(left) &&
        Path.GetFullPath(left).Equals(Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
    private static string SafeSegment(string value) => string.Concat(value.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
    private static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
}
