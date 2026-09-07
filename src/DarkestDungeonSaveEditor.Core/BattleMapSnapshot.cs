namespace DarkestDungeonSaveEditor.Core;

public enum BattleMapAreaKind
{
    Unknown,
    Room,
    Corridor
}

public enum BattleMapTileKnowledge
{
    Hidden = 0,
    Unknown = 1,
    Scouted = 2,
    Visited = 3
}

public enum BattleMapTileContent
{
    Nothing = 0,
    Battle = 1,
    Ambush = 2,
    Trap = 3,
    Obstacle = 4,
    Happening = 5,
    GuardedCurio = 6,
    Curio = 7,
    Hunger = 8,
    Treasure = 9,
    GuardedTreasure = 10,
    AmbushCurio = 11,
    AmbushTreasure = 12,
    SecretDoor = 13,
    Unknown = int.MaxValue
}

public sealed record BattleMapTileSnapshot(
    string TileId,
    int TileIndex,
    double MapX,
    double MapY,
    int StaticType,
    int StaticCurioHash,
    int StaticObstacleHash,
    BattleMapTileKnowledge Knowledge,
    int RawKnowledge,
    BattleMapTileContent Content,
    int RawContent,
    int CurioPropHash,
    int TrapHash,
    int MashIndex,
    int MashType,
    bool CriticalScout)
{
    public bool HasResidualContentBinding =>
        StaticCurioHash != 0 ||
        StaticObstacleHash != 0 ||
        CurioPropHash != 0 ||
        TrapHash != 0 ||
        MashIndex >= 0 ||
        MashType != 7;
}

public sealed record BattleMapAreaSnapshot(
    string AreaId,
    int AreaHash,
    BattleMapAreaKind Kind,
    BattleMapTileKnowledge Knowledge,
    int RawKnowledge,
    bool Reversed,
    IReadOnlyList<BattleMapTileSnapshot> Tiles);

public sealed record BattleMapSnapshot(
    string ProfileDirectory,
    string MapSavePath,
    string RaidSavePath,
    string MapSha256,
    string RaidSha256,
    string DungeonId,
    int Difficulty,
    int Length,
    int? EntranceAreaHash,
    string? EntranceAreaId,
    int? FinalRoomHash,
    string? FinalRoomId,
    int? PartyAreaHash,
    string? PartyAreaId,
    int? PartyTileIndex,
    int? LastRoomHash,
    string? LastRoomId,
    bool InBattle,
    IReadOnlyList<double> Bounds,
    IReadOnlyList<BattleMapAreaSnapshot> Areas,
    IReadOnlyList<string> Issues,
    DateTime ReadAtUtc)
{
    public string RaidIdentity { get; init; } = string.Empty;

    public int RoomCount => Areas.Count(area => area.Kind == BattleMapAreaKind.Room);

    public int CorridorCount => Areas.Count(area => area.Kind == BattleMapAreaKind.Corridor);

    public int TileCount => Areas.Sum(area => area.Tiles.Count);
}

public enum BattleMapEditKind
{
    DeleteContent,
    MoveParty,
    PlaceBattle,
    SetBattleAttachment,
    RemoveBattleAttachment,
    PlaceContent
}

public sealed record BattleMapEditPreview(
    BattleMapEditKind Kind,
    string AreaId,
    string TileId,
    BattleMapAreaKind AreaKind,
    int AreaHash,
    int PreviousRawContent,
    int? SavedAreaTile,
    int? PreviousRoomHash);

public sealed record PreparedBattleMapEdit(
    string SessionId,
    SaveProfile Profile,
    BattleMapEditPreview Preview,
    string WorkspaceDirectory,
    PreparedSaveFile TargetFile,
    string MapOriginalSha256,
    string RaidOriginalSha256,
    DateTime PreparedAtUtc)
{
    public string GameOriginalSha256 { get; init; } = string.Empty;

    public string RaidIdentity { get; init; } = string.Empty;

    public BattleEncounterDefinition? Encounter { get; init; }

    public BattleRoomAttachmentDefinition? Attachment { get; init; }
}

public sealed record BattleMapPlacementTarget(string AreaId, string TileId);
