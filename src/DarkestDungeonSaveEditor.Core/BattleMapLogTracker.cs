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
                changes.Add(EditorText.Format("BattleMapLogTracker_001", Position(previous), Position(snapshot)));
            }
            if (previous.InBattle != snapshot.InBattle)
            {
                changes.Add(EditorText.Format("BattleMapLogTracker_004", (previous.InBattle ? EditorText.Get("BattleMapLogTracker_002") : EditorText.Get("BattleMapLogTracker_003")), (snapshot.InBattle ? EditorText.Get("BattleMapLogTracker_002") : EditorText.Get("BattleMapLogTracker_003"))));
            }
            if (_previousMapState != mapState)
            {
                changes.Add(EditorText.Format("BattleMapLogTracker_005", snapshot.RoomCount, snapshot.CorridorCount, snapshot.TileCount));
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
            changes.Add(EditorText.Format("BattleMapLogTracker_006", removedIssues));
        }
        entries.Add(new DiagnosticLogEntry(DiagnosticLogLevel.Information, newContext
            ? EditorText.Format("BattleMapLogTracker_007", snapshot.ProfileDirectory, snapshot.DungeonId, snapshot.Difficulty) +
              EditorText.Format("BattleMapLogTracker_008", snapshot.MapSavePath, snapshot.RaidSavePath) +
              EditorText.Format("BattleMapLogTracker_009", snapshot.Length, snapshot.RoomCount, snapshot.CorridorCount, snapshot.TileCount) +
              EditorText.Format("BattleMapLogTracker_010", Position(snapshot), (snapshot.InBattle ? EditorText.Get("BattleMapLogTracker_002") : EditorText.Get("BattleMapLogTracker_003")), issues.Count)
            : EditorText.Format("BattleMapLogTracker_011", Path.GetFileName(snapshot.ProfileDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))) +
              (changes.Count > 0 ? string.Join("；", changes) : EditorText.Get("BattleMapLogTracker_012")) +
              EditorText.Format("BattleMapLogTracker_013", issues.Count)));
        entries.Add(new DiagnosticLogEntry(DiagnosticLogLevel.Trace,
            EditorText.Format("BattleMapLogTracker_014", snapshot.ProfileDirectory, snapshot.MapSavePath, snapshot.RaidSavePath) +
            $"persist.map.json SHA-256={snapshot.MapSha256}；" +
            EditorText.Format("BattleMapLogTracker_015", snapshot.RaidSha256, snapshot.ReadAtUtc)));
        entries.AddRange(issues.Except(oldIssues).Order(StringComparer.Ordinal)
            .Select(issue => new DiagnosticLogEntry(DiagnosticLogLevel.Warning, EditorText.Format("BattleMapLogTracker_016", issue))));
        return entries;
    }

    private static string Position(BattleMapSnapshot snapshot) =>
        $"{snapshot.PartyAreaId ?? EditorText.Get("BattleMapLogTracker_017")}/tile{snapshot.PartyTileIndex?.ToString() ?? "?"}";
}
