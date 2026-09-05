namespace DarkestDungeonSaveEditor.Core;

public enum ContentInventoryFileKind
{
    Data,
    LocalizationXml,
    LocalizationBinary,
    Asset,
    Tool,
    Other
}

public enum ContentManifestMatch
{
    Listed,
    Unlisted,
    NoManifest,
    Missing,
    Unknown
}

public sealed record ContentInventoryFile(
    string RelativePath,
    ContentInventoryFileKind Kind,
    ContentManifestMatch ManifestMatch,
    long? ActualLength,
    long? DeclaredLength,
    bool IsContentCandidate)
{
    public bool HasLengthMismatch => ActualLength.HasValue && DeclaredLength.HasValue &&
                                     ActualLength.Value != DeclaredLength.Value;
}

public sealed record ContentModFileInventory(
    string SourceId,
    string Directory,
    bool HasManifest,
    bool IsComplete,
    IReadOnlyList<ContentInventoryFile> Files,
    IReadOnlyList<string> Issues);

public sealed record ContentFileInventorySnapshot(
    string ProfileId,
    string ProfileDirectory,
    string SourceGameSha256,
    DateTime ScannedAtUtc,
    IReadOnlyList<ContentModFileInventory> Mods)
{
    public bool IsComplete => Mods.All(mod => mod.IsComplete);
}
