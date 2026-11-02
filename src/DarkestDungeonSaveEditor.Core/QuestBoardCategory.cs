namespace DarkestDungeonSaveEditor.Core;

/// <summary>Editor menu groups. These never replace the quest's native type or goals.</summary>
public enum QuestBoardCategory { Explore, Cleanse, Gather, InventoryActivate, Activate, KillBoss, Wave }

public static class QuestBoardCategories
{
    public static QuestBoardCategory? FromNativeType(string type) => type switch
    {
        "explore" => QuestBoardCategory.Explore,
        "cleanse" => QuestBoardCategory.Cleanse,
        "gather" => QuestBoardCategory.Gather,
        "inventory_activate" => QuestBoardCategory.InventoryActivate,
        "activate" => QuestBoardCategory.Activate,
        "kill_boss" => QuestBoardCategory.KillBoss,
        "wave" => QuestBoardCategory.Wave,
        _ => null
    };
}
