using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace DarkestDungeonSaveEditor.Core;

/// <summary>A validated game/map/raid input held while preparing a map edit or a Bridge.</summary>
internal sealed class BattleMapWriteGuard : IDisposable
{
    private readonly List<FileStream> _locks = [];
    private FileStream? _gameLock;
    private FileStream? _mapLock;
    internal string GameSha256 { get; private set; } = string.Empty;
    internal JsonObject MapDocument { get; private set; } = null!;
    internal JsonObject RaidDocument { get; private set; } = null!;
    internal BattleMapSnapshot Snapshot { get; private set; } = null!;

    internal static async Task<BattleMapWriteGuard> LoadAsync(
        SaveProfile profile, BattleMapSnapshot expected, DsonSaveCodec codec,
        string workspace, CancellationToken cancellationToken)
    {
        var guard = new BattleMapWriteGuard();
        try
        {
            var directory = Path.GetFullPath(profile.ProfileDirectory);
            var sourceDirectory = Path.Combine(workspace, "source");
            var decodedDirectory = Path.Combine(workspace, "decoded");
            Directory.CreateDirectory(sourceDirectory);
            Directory.CreateDirectory(decodedDirectory);
            var hashes = new Dictionary<string, string>();
            var location = new RaidSaveLocation(directory, string.Empty);
            foreach (var name in new[] { "persist.game.json", "persist.map.json", "persist.raid.json" })
            {
                var stream = new FileStream(location.GetPath(name), FileMode.Open, FileAccess.Read, FileShare.Read);
                guard._locks.Add(stream);
                if (name == "persist.game.json") guard._gameLock = stream;
                if (name == "persist.map.json") guard._mapLock = stream;
                hashes[name] = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
                stream.Position = 0;
                using (var copy = new FileStream(Path.Combine(sourceDirectory, name), FileMode.CreateNew, FileAccess.Write))
                    await stream.CopyToAsync(copy, cancellationToken).ConfigureAwait(false);
                await codec.DecodeAsync(Path.Combine(sourceDirectory, name), Path.Combine(decodedDirectory, name), cancellationToken)
                    .ConfigureAwait(false);
                if (name == "persist.game.json")
                {
                    location = RaidSaveLocation.FromGame(directory, JsonSupport.ReadObject(Path.Combine(decodedDirectory, name)));
                    if (!Path.GetFullPath(expected.ProfileDirectory).Equals(directory, StringComparison.OrdinalIgnoreCase) ||
                        !Path.GetFullPath(expected.MapSavePath).Equals(location.MapPath, StringComparison.OrdinalIgnoreCase) ||
                        !Path.GetFullPath(expected.RaidSavePath).Equals(location.RaidPath, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("当前地图快照不属于所选档案或当前副本目录，请重新加载。");
                }
            }
            if (!hashes["persist.map.json"].Equals(expected.MapSha256, StringComparison.OrdinalIgnoreCase) ||
                !hashes["persist.raid.json"].Equals(expected.RaidSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("地图或副本存档在显示后已经变化，请等待地图刷新后重试。");

            guard.MapDocument = JsonSupport.ReadObject(Path.Combine(decodedDirectory, "persist.map.json"));
            guard.RaidDocument = JsonSupport.ReadObject(Path.Combine(decodedDirectory, "persist.raid.json"));
            guard.Snapshot = BattleMapSnapshotReader.Parse(directory, location.MapPath, location.RaidPath,
                hashes["persist.map.json"], hashes["persist.raid.json"], guard.MapDocument, guard.RaidDocument);
            var game = JsonSupport.RequireObject(JsonSupport.ReadObject(Path.Combine(decodedDirectory, "persist.game.json")), "base_root");
            if (game["inraid"] is not JsonValue inRaid || !inRaid.TryGetValue<bool>(out var isInRaid) ||
                game["raiddungeon"] is not JsonValue dungeon || !dungeon.TryGetValue<string>(out var dungeonId) ||
                string.IsNullOrWhiteSpace(dungeonId))
                throw new InvalidDataException("persist.game.json 缺少有效的小镇／副本状态，不能修改地图。");
            if (!isInRaid || dungeonId.Equals("none", StringComparison.Ordinal))
                throw new InvalidOperationException("所选档案已经不在副本中，不能修改残留地图。");
            if (!dungeonId.Equals(guard.Snapshot.DungeonId, StringComparison.Ordinal))
                throw new InvalidOperationException("游戏入口与地图副本不一致，请等待完整存档后重试。");
            BattleMapSaveEditor.ValidateStationaryRaidState(guard.RaidDocument);
            guard.GameSha256 = hashes["persist.game.json"];
            return guard;
        }
        catch
        {
            guard.Dispose();
            throw;
        }
    }

    // The Bridge intentionally updates game, but the displayed map and raid must stay
    // pinned until its package and configuration transaction finishes.
    internal void ReleaseGameLock()
    {
        _gameLock?.Dispose();
        _gameLock = null;
    }

    internal void ReleaseMapLock()
    {
        _mapLock?.Dispose();
        _mapLock = null;
    }

    public void Dispose()
    {
        foreach (var stream in _locks) stream.Dispose();
    }
}
