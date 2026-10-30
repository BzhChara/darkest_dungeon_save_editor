namespace DarkestDungeonSaveEditor.Core;

public enum QuestBoardEditKind { Create, Replace, Delete }

public sealed record QuestBoardEditRequest(QuestBoardEditKind Kind, string SourceSha256,
    string? SaveKey = null, string? TemplateKey = null);

public sealed record QuestBoardTemplate(string Key, QuestBoardQuest Quest, string UnavailableReason);

public sealed record QuestBoardEditCatalog(IReadOnlyList<QuestBoardTemplate> Templates,
    IReadOnlyDictionary<string, string> ImagePaths, IReadOnlyList<string> Issues)
{
    public IReadOnlyList<QuestBoardRegionSnapshot> Regions { get; init; } = [];

    public static QuestBoardEditCatalog Load(ActiveContentSnapshot content)
    {
        try { return new TownEventInitializer(content, 0).ReadQuestBoardCatalog(); }
        catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException or ArgumentException or System.Text.Json.JsonException)
        {
            return new([], new Dictionary<string, string>(), [error.Message]);
        }
    }
}

public sealed record QuestBoardMutationPreview(QuestBoardEditKind Kind, QuestBoardQuest? Previous,
    QuestBoardQuest? Result, IReadOnlyList<string> Warnings);

public sealed record PreparedQuestBoardEdit(string SessionId, SaveProfile Profile,
    QuestBoardMutationPreview Preview, ActiveContentSnapshot Content, string ContentFingerprint,
    IReadOnlyDictionary<string, string?> SourceHashes, string WorkspaceDirectory,
    PreparedSaveFile File, DateTime PreparedAtUtc);
