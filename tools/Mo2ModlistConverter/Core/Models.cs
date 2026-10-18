namespace DarkestDungeon.ModOrder;

public enum ConversionDirection { SaveToMo2, Mo2ToSave }

public sealed record ConversionRequest(
    ConversionDirection Direction, string SavePath, string ModlistPath, string ModsDirectory);

public sealed record PreviewRow(int? Order, bool Enabled, string Mo2Folder,
    string NativeSource, string NativeName, string SourceDirectory, string Detail);

public sealed class ConversionPlan
{
    public ConversionRequest Request { get; }
    public IReadOnlyList<PreviewRow> Rows { get; }
    public IReadOnlyList<string> Errors { get; }
    public string Fingerprint { get; }
    public bool CanExport => Errors.Count == 0;
    public int EnabledCount => Rows.Count(row => row.Enabled);
    internal byte[] SaveBytes { get; }
    internal byte[] ModlistBytes { get; }
    internal string? OutputModlist { get; }
    internal string? OutputJson { get; }

    internal ConversionPlan(ConversionRequest request, List<PreviewRow> rows, List<string> errors,
        string fingerprint, byte[] saveBytes, byte[] modlistBytes, string? outputModlist, string? outputJson)
    {
        Request = request;
        Rows = rows.AsReadOnly();
        Errors = errors.AsReadOnly();
        Fingerprint = fingerprint;
        SaveBytes = saveBytes;
        ModlistBytes = modlistBytes;
        OutputModlist = outputModlist;
        OutputJson = outputJson;
    }
}

internal sealed record NativeId(string Source, string Name)
{
    internal string Key => Source.ToUpperInvariant() + ":" + Name.ToUpperInvariant();
    internal bool IsMerge => Source == "mod_local_source" && Name.Equals("merge mod", StringComparison.OrdinalIgnoreCase);
    public override string ToString() => Source + ":" + Name;
}

internal sealed record ModlistEntry(string Folder, char State)
{
    // MO2's separator suffix is case-sensitive (ModInfo::isSeparatorName).
    internal bool IsSeparator => Folder.EndsWith("_separator", StringComparison.Ordinal);
    internal bool Enabled => State == '+' && !IsSeparator;
}

internal sealed record InstalledMod(string Folder, IReadOnlyList<NativeId> Candidates, string? Error);
