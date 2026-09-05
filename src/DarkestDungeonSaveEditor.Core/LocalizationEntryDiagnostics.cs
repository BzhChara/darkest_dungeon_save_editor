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
            issues.Add($"本地化部分读取：'{path}'；跳过 {_count} 个无效项，其余有效条目继续读取。" +
                       $"示例：{string.Join("；", _samples)}" + (_count > SampleLimit ? "；…" : string.Empty));
        }
    }
}
