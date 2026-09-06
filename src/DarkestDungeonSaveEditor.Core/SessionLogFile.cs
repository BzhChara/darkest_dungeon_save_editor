using System.Text;

namespace DarkestDungeonSaveEditor.Core;

/// <summary>One append-only file per application run, including writes after midnight.</summary>
public sealed class SessionLogFile
{
    private readonly object _writeGate = new();

    public SessionLogFile(string logDirectory, DateTimeOffset startedAt, int processId)
    {
        var fileName = FormattableString.Invariant($"app-{startedAt:yyyyMMdd-HHmmss-fff}-{processId}-{Guid.NewGuid():N}.log");
        FilePath = Path.Combine(Path.GetFullPath(logDirectory), fileName);
    }

    public string FilePath { get; }

    public void Append(string entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (_writeGate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.AppendAllText(FilePath, entry, new UTF8Encoding(false));
        }
    }
}
