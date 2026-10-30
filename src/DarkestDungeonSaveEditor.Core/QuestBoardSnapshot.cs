namespace DarkestDungeonSaveEditor.Core;

public sealed record QuestBoardRegionSnapshot(string Id, double X, double Y, int QuestsPerRow,
    BilingualContentName LocalizedName)
{
    public bool HasLayout { get; init; } = true;
    public bool HasVisibleProgression { get; init; }
    public int? ProgressionLevel { get; init; }
    public double ProgressionFraction { get; init; }
    public string Name => EditorText.ContentName(LocalizedName, Id);
}

public sealed record QuestBoardReward(string Type, string Id, int Amount, string IconPath,
    string Rarity, BilingualContentName LocalizedName, bool IsRecovery)
{
    public string Name => EditorText.ContentName(LocalizedName, Id.Length == 0 ? Type : Id);
}

public sealed record QuestBoardQuest(string SaveKey, string Id, string Type, string Dungeon,
    int Difficulty, int Length, bool IsPlotQuest, IReadOnlyList<string> GoalIds,
    BilingualContentName LocalizedName, BilingualContentName LocalizedDescription,
    BilingualContentName LocalizedObjective, IReadOnlyList<QuestBoardReward> Rewards)
{
    public int Firewood { get; init; }
    public bool HasRandomCamping { get; init; }
    public BilingualContentName LocalizedCampingTooltip { get; init; } = BilingualContentName.Empty;
    public string CampingTooltip => EditorText.ContentName(LocalizedCampingTooltip, string.Empty);
    public string Name => EditorText.ContentName(LocalizedName, Id);
    public string Description => EditorText.ContentName(LocalizedDescription, string.Empty);
    public string Objective => EditorText.ContentName(LocalizedObjective, string.Join("; ", GoalIds));
    public string IconPath { get; init; } = string.Empty;
    public string LengthIconPath { get; init; } = string.Empty;
}

public sealed record QuestBoardSnapshot(string ProfileDirectory, string SourceSha256,
    IReadOnlyList<QuestBoardQuest> Quests, IReadOnlyList<QuestBoardRegionSnapshot> Regions,
    IReadOnlyDictionary<string, string> ImagePaths, IReadOnlyList<string> Issues);
