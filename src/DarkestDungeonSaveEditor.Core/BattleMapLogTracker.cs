using System.Text.Json;

namespace DarkestDungeonSaveEditor.Core;

/// <summary>Tracks diagnostic deltas only; never participates in map refresh or write decisions.</summary>
public sealed class BattleMapLogTracker
{
    private BattleMapSnapshot? _previous;
    private string? _previousMapState;

    public void Reset()
    {
        _previous = null;
        _previousMapState = null;
    }

    public IReadOnlyList<DiagnosticLogEntry> Observe(BattleMapSnapshot snapshot)
    {
        var mapState = JsonSerializer.Serialize(new
        {
            snapshot.EntranceAreaHash,
            snapshot.FinalRoomHash,
            snapshot.Bounds,
            Areas = snapshot.Areas.OrderBy(area => area.AreaId, StringComparer.Ordinal).Select(area =>
                area with { Tiles = area.Tiles.OrderBy(tile => tile.TileId, StringComparer.Ordinal).ToArray() })
        });
        var previous = _previous;
        var newContext = previous is null ||
            !previous.ProfileDirectory.Equals(snapshot.ProfileDirectory, StringComparison.OrdinalIgnoreCase) ||
            !previous.MapSavePath.Equals(snapshot.MapSavePath, StringComparison.OrdinalIgnoreCase) ||
            !previous.RaidSavePath.Equals(snapshot.RaidSavePath, StringComparison.OrdinalIgnoreCase) ||
            previous.RaidIdentity != snapshot.RaidIdentity ||
            previous.DungeonId != snapshot.DungeonId || previous.Difficulty != snapshot.Difficulty || previous.Length != snapshot.Length;
        var changes = new List<string>();
        if (!newContext)
        {
            if (previous!.PartyAreaId != snapshot.PartyAreaId || previous.PartyTileIndex != snapshot.PartyTileIndex)
            {
                changes.Add($"队伍位置 {Position(previous)} → {Position(snapshot)}");
            }
            if (previous.InBattle != snapshot.InBattle)
            {
                changes.Add($"战斗状态 {(previous.InBattle ? "战斗中" : "非战斗")} → {(snapshot.InBattle ? "战斗中" : "非战斗")}");
            }
            if (_previousMapState != mapState)
            {
                changes.Add($"地图结构/格子状态变化（房间={snapshot.RoomCount}；走廊={snapshot.CorridorCount}；格子={snapshot.TileCount}）");
            }
        }

        var oldIssues = newContext ? new HashSet<string>(StringComparer.Ordinal) : previous!.Issues.ToHashSet(StringComparer.Ordinal);
        var issues = snapshot.Issues.ToHashSet(StringComparer.Ordinal);
        var hashChanged = newContext || !previous!.MapSha256.Equals(snapshot.MapSha256, StringComparison.OrdinalIgnoreCase) ||
            !previous.RaidSha256.Equals(snapshot.RaidSha256, StringComparison.OrdinalIgnoreCase);
        _previous = snapshot;
        _previousMapState = mapState;
        if (!hashChanged && changes.Count == 0 && issues.SetEquals(oldIssues))
        {
            return [];
        }

        var entries = new List<DiagnosticLogEntry>();
        var removedIssues = oldIssues.Except(issues).Count();
        if (removedIssues > 0)
        {
            changes.Add($"先前 {removedIssues} 条解析警告已消失");
        }
        entries.Add(new DiagnosticLogEntry(DiagnosticLogLevel.Information, newContext
            ? $"战斗地图初始快照：档案目录={snapshot.ProfileDirectory}；地区={snapshot.DungeonId}；难度={snapshot.Difficulty}；" +
              $"地图文件={snapshot.MapSavePath}；副本文件={snapshot.RaidSavePath}；" +
              $"长度={snapshot.Length}；房间={snapshot.RoomCount}；走廊={snapshot.CorridorCount}；格子={snapshot.TileCount}；" +
              $"队伍位置={Position(snapshot)}；状态={(snapshot.InBattle ? "战斗中" : "非战斗")}；解析警告={issues.Count}"
            : $"战斗地图同步：档案={Path.GetFileName(snapshot.ProfileDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))}；" +
              (changes.Count > 0 ? string.Join("；", changes) : "已显示的队伍位置、地图及战斗状态未变；其他存档字段可能变化") +
              $"；解析警告={issues.Count}"));
        entries.Add(new DiagnosticLogEntry(DiagnosticLogLevel.Trace,
            $"战斗地图校验：档案目录={snapshot.ProfileDirectory}；地图文件={snapshot.MapSavePath}；副本文件={snapshot.RaidSavePath}；" +
            $"persist.map.json SHA-256={snapshot.MapSha256}；" +
            $"persist.raid.json SHA-256={snapshot.RaidSha256}；快照读取 UTC={snapshot.ReadAtUtc:O}"));
        entries.AddRange(issues.Except(oldIssues).Order(StringComparer.Ordinal)
            .Select(issue => new DiagnosticLogEntry(DiagnosticLogLevel.Warning, $"战斗地图解析警告：{issue}")));
        return entries;
    }

    private static string Position(BattleMapSnapshot snapshot) =>
        $"{snapshot.PartyAreaId ?? "未解析"}/tile{snapshot.PartyTileIndex?.ToString() ?? "?"}";
}
