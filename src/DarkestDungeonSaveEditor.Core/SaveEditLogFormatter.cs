namespace DarkestDungeonSaveEditor.Core;

public static class SaveEditLogFormatter
{
    public static string Describe(PreparedQuantityItemEdit edit) =>
        Context(edit.SessionId, edit.Profile) + $"；操作=物品数量；对象={edit.Item.InventoryType}/{edit.Item.ItemId}；" +
        $"位置={FormatStorage(edit.Preview.StorageKind)}；数量={edit.Preview.ExistingAmount} → {edit.Preview.TargetAmount}；" +
        $"占用条目={edit.Preview.MatchingEntries} → {edit.Preview.ResultingMatchingEntries}";

    public static string Describe(PreparedTrinketEdit edit) =>
        Context(edit.SessionId, edit.Profile) + $"；操作=生成饰品；对象={edit.Trinket.Id}；" +
        $"仓库数量={edit.Preview.ExistingCopies} → {edit.Preview.ResultingCopies}；新增={edit.Preview.RequestedCopies}";

    public static string Describe(PreparedStagecoachHeroEdit edit, StagecoachHeroCandidatePreview? hero) =>
        Context(edit.SessionId, edit.Profile) + $"；操作=生成人物；职业={edit.Preview.HeroClass}；" +
        $"姓名={hero?.Name ?? "未记录"}；等级={hero?.ResolveLevel.ToString() ?? "未记录"}；GUID={edit.Preview.CandidateGuid}；" +
        $"目标={(edit.Preview.TargetPool == StagecoachRecruitPool.Shard ? "碎片马车" : "普通马车")}；" +
        $"候选人数={edit.Preview.ExistingCandidates} → {edit.Preview.ResultingCandidates}";

    private static string Context(string sessionId, SaveProfile profile) =>
        $"操作编号={sessionId}；档案={profile.ProfileId}；目录={profile.ProfileDirectory}";

    private static string FormatStorage(QuantityItemStorageKind kind) => kind switch
    {
        QuantityItemStorageKind.Wallet => "小镇钱包",
        QuantityItemStorageKind.EstateItems => "小镇库存",
        QuantityItemStorageKind.RaidInventory => "副本背包",
        _ => kind.ToString()
    };
}
