using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace DarkestDungeonSaveEditor.Core;

internal sealed record QuantityItemSaveScene(QuantityItemSaveContext Context, bool HasRaidResidue)
{
    public static QuantityItemSaveScene Read(ActiveContentSnapshot content)
    {
        var gamePath = Path.Combine(content.Profile.ProfileDirectory, "persist.game.json");
        using (var stream = File.OpenRead(gamePath))
        {
            if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(
                    content.SourceGameSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(EditorText.Get("QuantityItemSaveScene_001"));
            }
        }

        var game = JsonSupport.ReadObject(content.DecodedGamePath);
        var root = JsonSupport.RequireObject(game, "base_root");
        if (root["inraid"] is not JsonValue inRaidNode || !inRaidNode.TryGetValue<bool>(out var inRaid) ||
            root["raiddungeon"] is not JsonValue dungeonNode || !dungeonNode.TryGetValue<string>(out var dungeon) ||
            string.IsNullOrWhiteSpace(dungeon))
        {
            throw new InvalidDataException(EditorText.Get("QuantityItemSaveScene_002"));
        }

        var location = RaidSaveLocation.FromGame(content.Profile.ProfileDirectory, game);
        var hasRaid = File.Exists(location.RaidPath);
        var hasDungeon = !dungeon.Equals("none", StringComparison.Ordinal);
        if (inRaid != hasDungeon)
        {
            throw new InvalidDataException(EditorText.Get("QuantityItemSaveScene_003"));
        }

        if (inRaid && !hasRaid)
        {
            throw new InvalidOperationException(EditorText.Get("QuantityItemSaveScene_004"));
        }

        return new QuantityItemSaveScene(
            inRaid ? QuantityItemSaveContext.Raid : QuantityItemSaveContext.Town,
            !inRaid && hasRaid);
    }
}
