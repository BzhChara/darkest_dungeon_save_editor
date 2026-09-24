namespace DarkestDungeonSaveEditor.Core;

internal sealed class LocalizationEntryDiagnostics(string path)
{
    private const int SampleLimit = 3;
    private readonly List<string> _samples = [];
    private int _count;

    public void Skip(string identity, string reason)
    {
        _count++;
        if (_samples.Count < SampleLimit)
        {
            var label = identity.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
            if (label.Length > 120)
            {
                label = label[..120] + "…";
            }

            _samples.Add($"{label}：{reason}");
        }
    }

    // Publish only after the file's structural checks have succeeded.
    public void AppendTo(ICollection<string> issues)
    {
        if (_count > 0)
        {
            issues.Add(CatalogIssueCode.PartialLocalization + path + "';" +
                       EditorText.Format("LocalizationEntryDiagnostics_001", _count) +
                       EditorText.Format("LocalizationEntryDiagnostics_002", string.Join("；", _samples)) + (_count > SampleLimit ? "；…" : string.Empty));
        }
    }
}
