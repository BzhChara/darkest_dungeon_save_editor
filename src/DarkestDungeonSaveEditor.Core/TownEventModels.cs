namespace DarkestDungeonSaveEditor.Core;

public sealed record TownEventEffect(string Type, string StringData, double NumberData);

public sealed record TownEventDefinition(
    string Id,
    IReadOnlyList<TownEventEffect> Effects,
    string Source,
    string SourcePath,
    bool HasProviderConflict)
{
    public int NativeId => unchecked((int)NativeResourceIdentity.HashCString(Id));
    public BilingualContentName LocalizedName { get; init; } = BilingualContentName.Empty;
    public BilingualContentName LocalizedDescription { get; init; } = BilingualContentName.Empty;
    internal ContentSourceLabel? LocalizedSource { get; init; }
    public string SourceLabel => LocalizedSource?.ToString() ?? Source;
}

public sealed record TownEventCatalogResult(
    IReadOnlyList<TownEventDefinition> Events,
    IReadOnlyList<string> Issues);

public sealed record TownEventMutationPreview(
    int OldEventId,
    string NewEventId,
    int Week,
    int RemovedEventCandidates,
    int RemovedRevivalOffers,
    IReadOnlyList<string> Changes);

public sealed record PreparedTownEventEdit(
    string SessionId,
    SaveProfile Profile,
    TownEventMutationPreview Preview,
    ActiveContentSnapshot Content,
    string ContentFingerprint,
    IReadOnlyDictionary<string, string?> SourceHashes,
    string WorkspaceDirectory,
    IReadOnlyList<PreparedSaveFile> Files,
    DateTime PreparedAtUtc);
