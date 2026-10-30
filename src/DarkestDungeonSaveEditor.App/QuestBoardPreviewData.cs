using DarkestDungeonSaveEditor.Core;

namespace DarkestDungeonSaveEditor.App;

// Presentation fixtures only, with categories and level/length combinations checked against
// quest.generation.json and quest.plot_quests.json. They are not loaded save entries.
// Group by the native quest.type; progression/event/repeatability are separate flags.
internal enum QuestBoardCategory { Explore, Cleanse, Gather, InventoryActivate, Activate, KillBoss, Wave }

internal sealed record QuestBoardRegion(string Id, double X, double Y)
{
    public BilingualContentName? LocalizedName { get; init; }
    public int QuestsPerRow { get; init; } = 4;
    public bool HasLayout { get; init; } = true;
    public bool HasVisibleProgression { get; init; }
    public int? ProgressionLevel { get; init; }
    public double ProgressionFraction { get; init; }
    public string Name => LocalizedName is { } name ? EditorText.ContentName(name, Id) : Id switch
    {
        "crypts" => EditorText.Get("QuestBoard_Region_crypts"),
        "weald" => EditorText.Get("QuestBoard_Region_weald"),
        "warrens" => EditorText.Get("QuestBoard_Region_warrens"),
        "cove" => EditorText.Get("QuestBoard_Region_cove"),
        "darkestdungeon" => EditorText.Get("QuestBoard_Region_darkestdungeon"),
        "town" => EditorText.Get("QuestBoard_Region_town"),
        "courtyard" => EditorText.Get("QuestBoard_Region_courtyard"),
        "farm" => EditorText.Get("QuestBoard_Region_farm"),
        _ => throw new ArgumentOutOfRangeException(nameof(Id))
    };
}

internal sealed record QuestBoardChoice(string Kind, int Difficulty = 1, int Length = 1, string? PlotId = null)
{
    public QuestBoardQuest? SavedQuest { get; init; }
    public string? TemplateKey { get; init; }
    public string UnavailableReason { get; init; } = string.Empty;
    public string Id => SavedQuest?.Id ?? PlotId ?? $"generated_{Kind}_{Difficulty}_{Length}";
    public string NativeType => Kind switch
    {
        "darkest" or "courtyard" or "invasion" or "recovery" => "kill_boss",
        "darkest_beacons" or "courtyard_intro" => "inventory_activate",
        "darkest_portal" => "activate",
        _ => Kind
    };
    public QuestBoardCategory Category => NativeType switch
    {
        "explore" => QuestBoardCategory.Explore,
        "cleanse" => QuestBoardCategory.Cleanse,
        "gather" => QuestBoardCategory.Gather,
        "inventory_activate" => QuestBoardCategory.InventoryActivate,
        "activate" => QuestBoardCategory.Activate,
        "kill_boss" => QuestBoardCategory.KillBoss,
        "wave" => QuestBoardCategory.Wave,
        _ => throw new ArgumentOutOfRangeException(nameof(Kind))
    };
    public string Name => SavedQuest?.Name ?? Kind switch
    {
        "explore" => EditorText.Get("QuestBoard_Kind_explore"),
        "cleanse" => EditorText.Get("QuestBoard_Kind_cleanse"),
        "gather" => EditorText.Get("QuestBoard_Kind_gather"),
        "inventory_activate" => EditorText.Get("QuestBoard_Kind_inventory_activate"),
        "kill_boss" => EditorText.Get("QuestBoard_Kind_kill_boss"),
        "recovery" => EditorText.Get("QuestBoard_Kind_recovery"),
        "darkest" => EditorText.Get("QuestBoard_Kind_darkest"),
        "darkest_beacons" => EditorText.Get("QuestBoard_Kind_darkest_beacons"),
        "darkest_portal" => EditorText.Get("QuestBoard_Kind_darkest_portal"),
        "courtyard_intro" => EditorText.Get("QuestBoard_Kind_courtyard_intro"),
        "invasion" => EditorText.Get("QuestBoard_Kind_invasion"),
        "courtyard" => EditorText.Get("QuestBoard_Kind_courtyard"),
        "wave" => EditorText.Get("QuestBoard_Kind_wave"),
        _ => throw new ArgumentOutOfRangeException(nameof(Kind))
    };
    public string Objective => SavedQuest?.Objective ?? Kind switch
    {
        "explore" => EditorText.Get("QuestBoard_Goal_explore"),
        "cleanse" => EditorText.Get("QuestBoard_Goal_cleanse"),
        "gather" => EditorText.Get("QuestBoard_Goal_gather"),
        "inventory_activate" => EditorText.Get("QuestBoard_Goal_inventory_activate"),
        "kill_boss" => EditorText.Get("QuestBoard_Goal_kill_boss"),
        "recovery" => EditorText.Get("QuestBoard_Goal_recovery"),
        "darkest" => EditorText.Get("QuestBoard_Goal_darkest"),
        "darkest_beacons" => EditorText.Get("QuestBoard_Goal_darkest_beacons"),
        "darkest_portal" => EditorText.Get("QuestBoard_Goal_darkest_portal"),
        "courtyard_intro" => EditorText.Get("QuestBoard_Goal_courtyard_intro"),
        "invasion" => EditorText.Get("QuestBoard_Goal_invasion"),
        "courtyard" => EditorText.Get("QuestBoard_Goal_courtyard"),
        "wave" => EditorText.Get("QuestBoard_Goal_wave"),
        _ => throw new ArgumentOutOfRangeException(nameof(Kind))
    };
    public string Description => SavedQuest?.Description ?? Kind switch
    {
        "explore" => EditorText.Get("QuestBoard_Description_explore"),
        "cleanse" => EditorText.Get("QuestBoard_Description_cleanse"),
        "gather" => EditorText.Get("QuestBoard_Description_gather"),
        "inventory_activate" => EditorText.Get("QuestBoard_Description_inventory_activate"),
        "kill_boss" => EditorText.Get("QuestBoard_Description_kill_boss"),
        "recovery" => EditorText.Get("QuestBoard_Description_recovery"),
        "darkest" => EditorText.Get("QuestBoard_Description_darkest"),
        "darkest_beacons" => EditorText.Get("QuestBoard_Description_darkest_beacons"),
        "darkest_portal" => EditorText.Get("QuestBoard_Description_darkest_portal"),
        "courtyard_intro" => EditorText.Get("QuestBoard_Description_courtyard_intro"),
        "invasion" => EditorText.Get("QuestBoard_Description_invasion"),
        "courtyard" => EditorText.Get("QuestBoard_Description_courtyard"),
        "wave" => EditorText.Get("QuestBoard_Description_wave"),
        _ => throw new ArgumentOutOfRangeException(nameof(Kind))
    };
    public bool IsGenerated => PlotId is null;
    public string Icon => Kind switch
    {
        "courtyard" => "plot_crimson_court_1",
        "recovery" => PlotId ?? throw new InvalidOperationException("Recovery fixtures require their native plot ID."),
        _ => NativeType
    };
}

internal sealed record QuestBoardSample(Guid Key, string RegionId, QuestBoardChoice Choice)
{
    public static QuestBoardSample Create(string region, QuestBoardChoice choice) => new(Guid.NewGuid(), region, choice);
}

internal static class QuestBoardPreviewData
{
    internal static QuestBoardCategory? NativeCategory(string type) => type switch
    {
        "explore" => QuestBoardCategory.Explore, "cleanse" => QuestBoardCategory.Cleanse,
        "gather" => QuestBoardCategory.Gather, "inventory_activate" => QuestBoardCategory.InventoryActivate,
        "activate" => QuestBoardCategory.Activate, "kill_boss" => QuestBoardCategory.KillBoss,
        "wave" => QuestBoardCategory.Wave, _ => null
    };
    // Native quest-map anchors, in the 1920 x 1080 background's coordinate space.
    internal static IReadOnlyList<QuestBoardRegion> Regions { get; } =
    [
        new("crypts", 940, 220), new("weald", 1000, 525), new("warrens", 815, 400),
        new("cove", 1260, 400), new("darkestdungeon", 1260, 100), new("town", 1260, 750),
        new("courtyard", 570, 330), new("farm", 570, 600)
    ];

    internal static string CategoryName(QuestBoardCategory category) => category switch
    {
        QuestBoardCategory.Explore => EditorText.Get("QuestBoard_CategoryExplore"),
        QuestBoardCategory.Cleanse => EditorText.Get("QuestBoard_CategoryCleanse"),
        QuestBoardCategory.Gather => EditorText.Get("QuestBoard_CategoryGather"),
        QuestBoardCategory.InventoryActivate => EditorText.Get("QuestBoard_CategoryInventoryActivate"),
        QuestBoardCategory.Activate => EditorText.Get("QuestBoard_CategoryActivate"),
        QuestBoardCategory.KillBoss => EditorText.Get("QuestBoard_CategoryKillBoss"),
        QuestBoardCategory.Wave => EditorText.Get("QuestBoard_CategoryWave"),
        _ => throw new ArgumentOutOfRangeException(nameof(category))
    };

    internal static IReadOnlyList<QuestBoardChoice> Choices(string region)
    {
        if (region == "darkestdungeon") return
        [
            new("darkest", 6, 2, "plot_darkest_dungeon_1"),
            new("darkest_beacons", 6, 3, "plot_darkest_dungeon_2"),
            new("darkest_portal", 6, 4, "plot_darkest_dungeon_3")
        ];
        if (region == "town") return [new("invasion", 6, 1, "plot_town_invasion_0")];
        if (region == "courtyard") return
        [
            new("courtyard_intro", 1, 1, "plot_crimson_court_0"),
            new("courtyard", 3, 5, "plot_crimson_court_1")
        ];
        if (region == "farm") return
        [
            new("wave", 1, 1, "plot_color_of_madness_0"),
            new("wave", 3, 2, "plot_color_of_madness_1"),
            new("wave", 6, 3, "plot_color_of_madness_2")
        ];
        var boss = region switch
        {
            "crypts" => "necromancer",
            "weald" => "hag",
            "warrens" => "swine_prince",
            "cove" => "siren",
            _ => throw new ArgumentOutOfRangeException(nameof(region))
        };
        var choices = new List<QuestBoardChoice>();
        foreach (var level in new[] { 1, 3, 5 })
        {
            foreach (var kind in new[] { "explore", "cleanse" })
                foreach (var length in new[] { 1, 2, 3 }) choices.Add(new(kind, level, length));
            choices.Add(new("gather", level, 2));
            choices.Add(new("inventory_activate", level, 2));
            choices.Add(new("kill_boss", level, 2, $"plot_kill_{boss}_{(level + 1) / 2}"));
            if (region == "weald") choices.Add(new("recovery", level, 1, $"plot_trinket_retention_{(level - 1) / 2}"));
        }
        return choices;
    }

    internal static List<QuestBoardSample> Samples() =>
    [
        QuestBoardSample.Create("crypts", new("explore")),
        QuestBoardSample.Create("crypts", new("cleanse", 3, 2)),
        QuestBoardSample.Create("crypts", new("kill_boss", 5, 2, "plot_kill_necromancer_3")),
        QuestBoardSample.Create("weald", new("gather", 3, 2)),
        QuestBoardSample.Create("weald", new("recovery", 5, 1, "plot_trinket_retention_2")),
        QuestBoardSample.Create("warrens", new("cleanse", 1, 1)),
        QuestBoardSample.Create("cove", new("inventory_activate", 3, 2)),
        QuestBoardSample.Create("cove", new("explore", 5, 3)),
        QuestBoardSample.Create("darkestdungeon", new("darkest", 6, 2, "plot_darkest_dungeon_1")),
        QuestBoardSample.Create("town", new("invasion", 6, 1, "plot_town_invasion_0")),
        QuestBoardSample.Create("courtyard", new("courtyard", 3, 5, "plot_crimson_court_1")),
        QuestBoardSample.Create("farm", new("wave", 6, 3, "plot_color_of_madness_2"))
    ];

    internal static string Difficulty(int value) => value switch
    {
        1 => EditorText.Get("QuestBoard_Difficulty_1"),
        3 => EditorText.Get("QuestBoard_Difficulty_3"),
        5 => EditorText.Get("QuestBoard_Difficulty_5"),
        6 => EditorText.Get("QuestBoard_Difficulty_6"),
        _ => EditorText.Format("QuestBoard_DifficultyOther", value)
    };
    internal static string Length(int value) => value switch
    {
        1 => EditorText.Get("QuestBoard_Length_1"),
        2 => EditorText.Get("QuestBoard_Length_2"),
        3 => EditorText.Get("QuestBoard_Length_3"),
        4 => EditorText.Get("QuestBoard_Length_4"),
        5 => EditorText.Get("QuestBoard_Length_5"),
        _ => EditorText.Format("QuestBoard_LengthOther", value)
    };
}
