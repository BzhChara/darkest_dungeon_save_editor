namespace DarkestDungeonSaveEditor.Core;

public readonly record struct QuestBoardPoint(double X, double Y);

// Native quest-select coordinates, before the editor's viewport transform.
// Defaults are the shipped quest_select.layout.darkest values.
public sealed record QuestBoardLayout
{
    public QuestBoardPoint BackgroundOffset { get; init; } = new(-5, -10);
    public QuestBoardPoint NameOffset { get; init; } = new(190, -8);
    public QuestBoardPoint LevelOffset { get; init; } = new(219, 20);
    public QuestBoardPoint ProgressOffset { get; init; } = new(14, 32);
    public QuestBoardPoint ProgressSize { get; init; } = new(194, 8);
    public QuestBoardPoint QuestStart { get; init; } = new(160, 92);
    public QuestBoardPoint QuestSpacing { get; init; } = new(76, 80);
    public QuestBoardPoint LengthIconOffset { get; init; }
    public QuestBoardPoint TypeIconOffset { get; init; }
    public QuestBoardPoint SelectionOffset { get; init; } = new(-94, -94);
    public QuestBoardDetailLayout Details { get; init; } = new();
    public QuestBoardRewardLayout Rewards { get; init; } = new();

    public QuestBoardPoint QuestCenter(int index, int columns) => new(
        QuestStart.X - columns * QuestSpacing.X / 2 + index % columns * QuestSpacing.X,
        QuestStart.Y + index / columns * QuestSpacing.Y);
}

public sealed record QuestBoardDetailLayout
{
    public QuestBoardPoint Name { get; init; } = new(200, 140);
    public QuestBoardPoint Description { get; init; } = new(34, 194);
    public double DescriptionWidth { get; init; } = 340;
    public QuestBoardPoint Specifics { get; init; } = new(226, 390);
    public QuestBoardPoint Camping { get; init; } = new(58, 388);
    public QuestBoardPoint Goals { get; init; } = new(34, 430);
    public QuestBoardPoint GoalDescription { get; init; } = new(50, 460);
    public QuestBoardPoint RewardHeading { get; init; } = new(200, 530);
    public QuestBoardPoint RewardInventory { get; init; } = new(22, 562);
}

public sealed record QuestBoardRewardLayout
{
    public int Columns { get; init; } = 4;
    public QuestBoardPoint Start { get; init; } = new(20, 28);
    public QuestBoardPoint Spacing { get; init; } = new(80, 160);
    public bool Centered { get; init; } = true;
}
