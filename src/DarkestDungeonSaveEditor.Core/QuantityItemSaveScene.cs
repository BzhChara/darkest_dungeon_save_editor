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
                throw new InvalidOperationException("档案状态或活动内容已变化，请重新加载内容目录。");
            }
        }

        var root = JsonSupport.RequireObject(JsonSupport.ReadObject(content.DecodedGamePath), "base_root");
        if (root["inraid"] is not JsonValue inRaidNode || !inRaidNode.TryGetValue<bool>(out var inRaid) ||
            root["raiddungeon"] is not JsonValue dungeonNode || !dungeonNode.TryGetValue<string>(out var dungeon) ||
            string.IsNullOrWhiteSpace(dungeon))
        {
            throw new InvalidDataException("无法确定当前档案的小镇／副本状态，请在游戏中正常保存后重新加载。");
        }

        var hasRaid = File.Exists(content.Profile.RaidSavePath);
        var hasDungeon = !dungeon.Equals("none", StringComparison.OrdinalIgnoreCase);
        if (inRaid != hasDungeon)
        {
            throw new InvalidDataException("当前档案的小镇／副本状态不一致，请在游戏中完成过渡并保存后重新加载。");
        }

        if (inRaid && !hasRaid)
        {
            throw new InvalidOperationException("档案仍标记为副本中，但副本背包数据缺失；请重新加载，不能改写小镇库存。");
        }

        return new QuantityItemSaveScene(
            inRaid ? QuantityItemSaveContext.Raid : QuantityItemSaveContext.Town,
            !inRaid && hasRaid);
    }
}
