namespace DarkestDungeonSaveEditor.Core;

// Tracks certainty in already-resolved native file order. Failed slots may
// contain any ID; successful files are recorded only after a complete read.
internal sealed class OrderedDefinitionReadState(bool firstMatch, bool orderKnown = true)
    : OrderedDefinitionReadState<string>(firstMatch, orderKnown, StringComparer.Ordinal);

internal class OrderedDefinitionReadState<TKey>(
    bool firstMatch, bool orderKnown = true, IEqualityComparer<TKey>? comparer = null) where TKey : notnull
{
    private readonly HashSet<TKey> _verified = new(comparer);
    private bool _readFailed;

    public bool CanProveAbsence => orderKnown && !_readFailed;
    public bool IsVerified(TKey id) => orderKnown && _verified.Contains(id);

    public void RecordDefinition(TKey id)
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
