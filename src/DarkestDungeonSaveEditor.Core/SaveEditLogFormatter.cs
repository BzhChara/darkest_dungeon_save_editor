namespace DarkestDungeonSaveEditor.Core;

public static class SaveEditLogFormatter
{
    public static string Describe(PreparedQuantityItemEdit edit) =>
        Context(edit.SessionId, edit.Profile) + EditorText.Format("SaveEditLogFormatter_001", edit.Item.InventoryType, edit.Item.ItemId) +
        EditorText.Format("SaveEditLogFormatter_002", FormatStorage(edit.Preview.StorageKind), edit.Preview.ExistingAmount, edit.Preview.TargetAmount) +
        EditorText.Format("SaveEditLogFormatter_003", edit.Preview.MatchingEntries, edit.Preview.ResultingMatchingEntries);

    public static string Describe(PreparedTrinketEdit edit) =>
        Context(edit.SessionId, edit.Profile) + EditorText.Format("SaveEditLogFormatter_004", edit.Trinket.Id) +
        EditorText.Format("SaveEditLogFormatter_005", edit.Preview.ExistingCopies, edit.Preview.ResultingCopies, edit.Preview.RequestedCopies);

    public static string Describe(PreparedStagecoachHeroEdit edit, StagecoachHeroCandidatePreview? hero) =>
        Context(edit.SessionId, edit.Profile) + EditorText.Format("SaveEditLogFormatter_006", edit.Preview.HeroClass) +
        EditorText.Format("SaveEditLogFormatter_008", hero?.Name ?? EditorText.Get("SaveEditLogFormatter_007"), hero?.ResolveLevel.ToString() ?? EditorText.Get("SaveEditLogFormatter_007"), edit.Preview.CandidateGuid) +
        EditorText.Format("SaveEditLogFormatter_009", (edit.Preview.TargetPool == StagecoachRecruitPool.Shard ? EditorText.Get("MainWindow_EditWorkflow_025") : EditorText.Get("MainWindow_EditWorkflow_026"))) +
        EditorText.Format("SaveEditLogFormatter_010", edit.Preview.ExistingCandidates, edit.Preview.ResultingCandidates);

    private static string Context(string sessionId, SaveProfile profile) =>
        EditorText.Format("SaveEditLogFormatter_011", sessionId, profile.ProfileId, profile.ProfileDirectory);

    private static string FormatStorage(QuantityItemStorageKind kind) => kind switch
    {
        QuantityItemStorageKind.Wallet => EditorText.Get("SaveEditLogFormatter_012"),
        QuantityItemStorageKind.EstateItems => EditorText.Get("SaveEditLogFormatter_013"),
        QuantityItemStorageKind.RaidInventory => EditorText.Get("MainWindow_Presentation_021"),
        _ => kind.ToString()
    };
}
