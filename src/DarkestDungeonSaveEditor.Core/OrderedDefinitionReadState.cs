namespace DarkestDungeonSaveEditor.Core;

// Tracks certainty in already-resolved native file order. Failed slots may
// contain any ID; successful files are recorded only after a complete read.
internal sealed class OrderedDefinitionReadState(bool firstMatch, bool orderKnown = true)
{
    private readonly HashSet<string> _verified = new(StringComparer.Ordinal);
    private bool _readFailed;

    public bool CanProveAbsence => orderKnown && !_readFailed;
    public bool IsVerified(string id) => orderKnown && _verified.Contains(id);

    public void RecordDefinition(string id)
    {
        // First-match definitions before a failure remain authoritative; ones
        // first encountered afterwards cannot establish their precedence.
        if (!firstMatch || !_readFailed) _verified.Add(id);
    }

    public void RecordFailure()
    {
        _readFailed = true;
        // Last-match values must be established again after this unknown slot.
        if (!firstMatch) _verified.Clear();
    }
}
