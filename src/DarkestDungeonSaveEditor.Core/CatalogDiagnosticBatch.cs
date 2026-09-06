namespace DarkestDungeonSaveEditor.Core;

/// <summary>Diagnostic-only accumulation for one load; never shared with a later load.</summary>
public sealed class CatalogDiagnosticBatch
{
    private readonly object _gate = new();
    private readonly List<(string Module, IReadOnlyList<string> Issues)> _inputs = [];
    private bool _drained;

    public void Add(string module, IReadOnlyList<string> issues)
    {
        lock (_gate)
        {
            if (_drained)
            {
                throw new InvalidOperationException("A completed diagnostic batch cannot accept another catalog.");
            }
            _inputs.Add((module, issues.ToArray()));
        }
    }

    public T Capture<T>(string module, Func<T> load, Func<T, IReadOnlyList<string>> getIssues)
    {
        var result = load();
        Add(module, getIssues(result));
        return result;
    }

    public async Task<T> CaptureAsync<T>(string module, Func<Task<T>> load, Func<T, IReadOnlyList<string>> getIssues)
    {
        var result = await load().ConfigureAwait(false);
        Add(module, getIssues(result));
        return result;
    }

    public IReadOnlyList<DiagnosticLogEntry> Summarize()
    {
        lock (_gate)
        {
            return CatalogLogDiagnostics.Summarize(_inputs);
        }
    }

    public IReadOnlyList<DiagnosticLogEntry> Drain()
    {
        lock (_gate)
        {
            if (_drained)
            {
                return [];
            }
            var entries = CatalogLogDiagnostics.Summarize(_inputs);
            _drained = true;
            return entries;
        }
    }
}
