using System.Text.Json.Nodes;

namespace DarkestDungeonSaveEditor.Core;

internal static class TownSceneGuard
{
    internal static void RequireTown(ActiveContentSnapshot content)
    {
        if (QuantityItemSaveScene.Read(content).Context != QuantityItemSaveContext.Town)
            throw new InvalidOperationException(EditorText.Get("TownOnlyOperation"));
    }

    internal static void RequireTown(JsonObject game)
    {
        var root = JsonSupport.RequireObject(game, "base_root");
        if (root["inraid"] is not JsonValue node || !node.TryGetValue<bool>(out var inRaid) ||
            root["raiddungeon"] is not JsonValue dungeonNode || !dungeonNode.TryGetValue<string>(out var dungeon) ||
            string.IsNullOrWhiteSpace(dungeon) || inRaid != (dungeon != "none"))
            throw new InvalidDataException(EditorText.Get("QuantityItemSaveScene_002"));
        if (inRaid) throw new InvalidOperationException(EditorText.Get("TownOnlyOperation"));
    }
}
