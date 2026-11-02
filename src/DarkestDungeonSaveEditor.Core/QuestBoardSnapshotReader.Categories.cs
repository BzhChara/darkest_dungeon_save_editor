using System.Text.Json;

namespace DarkestDungeonSaveEditor.Core;

public static partial class QuestBoardSnapshotReader
{
    private static QuestBoardCategory? ReadCategory(string type, IReadOnlyList<string> goalIds,
        IReadOnlyDictionary<uint, JsonElement> goals)
    {
        // Preserve explicit native types, especially wave quests whose final goal
        // may be kill_monster (the Miller and the Night Parade's town invasion).
        if (QuestBoardCategories.FromNativeType(type) is { } native) return native;

        QuestBoardCategory? result = null;
        foreach (var id in goalIds)
        {
            if (!goals.TryGetValue(HashSaved(id), out var goal)) return null;
            QuestBoardCategory? category = Text(goal, "type") switch
            {
                "explore_room" => QuestBoardCategory.Explore,
                "battle_room" => QuestBoardCategory.Cleanse,
                "gather" => QuestBoardCategory.Gather,
                "kill_monster" => QuestBoardCategory.KillBoss,
                "activate" => NativeJsonReader.Array(goal, "starting_items").Any(item =>
                    Text(item, "type") == "quest_item" && Child(item, "amount") is { ValueKind: JsonValueKind.Number } value &&
                    value.TryGetInt32(out var amount) && amount > 0)
                    ? QuestBoardCategory.InventoryActivate : QuestBoardCategory.Activate,
                _ => null
            };
            // Unresolved or mixed objectives keep their native group. Browsing
            // must not invent a goal or make the underlying template unavailable.
            if (category is null || result is not null && result != category) return null;
            result = category;
        }
        return result;
    }
}
