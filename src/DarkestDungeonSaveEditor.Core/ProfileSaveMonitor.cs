namespace DarkestDungeonSaveEditor.Core;

public sealed class ProfileSaveFilesChangedEventArgs(
    IReadOnlyList<string> fileNames,
    DateTime detectedAtUtc) : EventArgs
{
    public IReadOnlyList<string> FileNames { get; } = fileNames;

    public DateTime DetectedAtUtc { get; } = detectedAtUtc;
}

/// <summary>
/// Watches a small, explicit set of files in one profile. FileSystemWatcher provides
/// prompt notification while the periodic stamp comparison recovers missed events.
/// </summary>
public sealed class ProfileSaveMonitor : IDisposable
{
    private static readonly TimeSpan DefaultDebounceDelay = TimeSpan.FromMilliseconds(450);
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(2);
    private readonly object _sync = new();
    private readonly string _profileDirectory;
    private readonly HashSet<string> _fileNames;
    private readonly TimeSpan _debounceDelay;
    private readonly TimeSpan _pollInterval;
    private readonly Dictionary<string, FileStamp> _baseline = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pendingFileNames = new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher? _watcher;
    private Timer? _debounceTimer;
    private Timer? _pollTimer;
    private bool _running;
    private bool _disposed;

    public ProfileSaveMonitor(
        string profileDirectory,
        IEnumerable<string> fileNames,
        TimeSpan? debounceDelay = null,
        TimeSpan? pollInterval = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileDirectory);
        ArgumentNullException.ThrowIfNull(fileNames);
        _profileDirectory = Path.GetFullPath(profileDirectory);
        _fileNames = fileNames
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase)!;
        if (_fileNames.Count == 0)
        {
            throw new ArgumentException("At least one profile file name must be watched.", nameof(fileNames));
        }

        _debounceDelay = debounceDelay ?? DefaultDebounceDelay;
        _pollInterval = pollInterval ?? DefaultPollInterval;
        if (_debounceDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(debounceDelay));
        }

        if (_pollInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(pollInterval));
        }
    }

    public event EventHandler<ProfileSaveFilesChangedEventArgs>? Changed;

    public string ProfileDirectory => _profileDirectory;

    public void Start()
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            if (_running)
            {
                return;
            }

            if (!Directory.Exists(_profileDirectory))
            {
                throw new DirectoryNotFoundException($"Profile directory was not found: {_profileDirectory}");
            }

            CaptureBaselineLocked();
            _watcher = new FileSystemWatcher(_profileDirectory, "persist*.json")
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName |
                               NotifyFilters.LastWrite |
                               NotifyFilters.Size |
                               NotifyFilters.CreationTime,
                EnableRaisingEvents = false
            };
            _watcher.Changed += Watcher_Changed;
            _watcher.Created += Watcher_Changed;
            _watcher.Deleted += Watcher_Changed;
            _watcher.Renamed += Watcher_Renamed;
            _watcher.Error += Watcher_Error;
            _debounceTimer = new Timer(_ => PollNow(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _pollTimer = new Timer(
                _ => DetectChangesAndSchedule(),
                null,
                _pollInterval,
                _pollInterval);
            _running = true;
            _watcher.EnableRaisingEvents = true;
        }
    }

    public void Stop()
    {
        FileSystemWatcher? watcher;
        Timer? debounceTimer;
        Timer? pollTimer;
        lock (_sync)
        {
            if (!_running)
            {
                return;
            }

            _running = false;
            watcher = _watcher;
            debounceTimer = _debounceTimer;
            pollTimer = _pollTimer;
            _watcher = null;
            _debounceTimer = null;
            _pollTimer = null;
        }

        if (watcher is not null)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Changed -= Watcher_Changed;
            watcher.Created -= Watcher_Changed;
            watcher.Deleted -= Watcher_Changed;
            watcher.Renamed -= Watcher_Renamed;
            watcher.Error -= Watcher_Error;
            watcher.Dispose();
        }

        debounceTimer?.Dispose();
        pollTimer?.Dispose();
    }

    /// <summary>
    /// Immediately compares watched file stamps. This is also useful to make consumers
    /// and tests deterministic instead of waiting for an operating-system event.
    /// </summary>
    public void PollNow()
    {
        IReadOnlyList<string>? changedFiles = null;
        lock (_sync)
        {
            if (!_running || _disposed)
            {
                return;
            }

            var changes = new HashSet<string>(_pendingFileNames, StringComparer.OrdinalIgnoreCase);
            _pendingFileNames.Clear();
            foreach (var fileName in _fileNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
            {
                var stamp = ReadStamp(Path.Combine(_profileDirectory, fileName));
                if (!_baseline.TryGetValue(fileName, out var previous) || previous != stamp)
                {
                    _baseline[fileName] = stamp;
                    _ = changes.Add(fileName);
                }
            }

            if (changes.Count > 0)
            {
                changedFiles = changes.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
            }
        }

        if (changedFiles is not null)
        {
            Changed?.Invoke(this, new ProfileSaveFilesChangedEventArgs(changedFiles, DateTime.UtcNow));
        }
    }

    public void Dispose()
    {
        Stop();
        lock (_sync)
        {
            _disposed = true;
            _baseline.Clear();
            _pendingFileNames.Clear();
        }

        GC.SuppressFinalize(this);
    }

    private void Watcher_Changed(object sender, FileSystemEventArgs e)
    {
        if (_fileNames.Contains(e.Name ?? string.Empty))
        {
            SchedulePoll([e.Name!]);
        }
    }

    private void Watcher_Renamed(object sender, RenamedEventArgs e)
    {
        if (_fileNames.Contains(e.Name ?? string.Empty) ||
            _fileNames.Contains(e.OldName ?? string.Empty))
        {
            SchedulePoll([e.Name ?? string.Empty, e.OldName ?? string.Empty]);
        }
    }

    private void Watcher_Error(object sender, ErrorEventArgs e) => SchedulePoll(_fileNames);

    private void SchedulePoll(IEnumerable<string> fileNames)
    {
        lock (_sync)
        {
            if (!_running || _disposed)
            {
                return;
            }

            foreach (var fileName in fileNames)
            {
                if (_fileNames.Contains(fileName))
                {
                    _ = _pendingFileNames.Add(fileName);
                }
            }
            _debounceTimer?.Change(_debounceDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private void DetectChangesAndSchedule()
    {
        lock (_sync)
        {
            if (!_running || _disposed)
            {
                return;
            }

            var scheduleNeeded = _pendingFileNames.Count == 0;
            var detectedChange = false;
            foreach (var fileName in _fileNames)
            {
                var stamp = ReadStamp(Path.Combine(_profileDirectory, fileName));
                if (!_baseline.TryGetValue(fileName, out var previous) || previous != stamp)
                {
                    _ = _pendingFileNames.Add(fileName);
                    detectedChange = true;
                }
            }

            // The fallback poll follows the same quiet-window path as FileSystemWatcher. It must
            // not publish immediately in the middle of the game's multi-file save burst.
            if (detectedChange && scheduleNeeded)
            {
                _debounceTimer?.Change(_debounceDelay, Timeout.InfiniteTimeSpan);
            }
        }
    }

    private void CaptureBaselineLocked()
    {
        _baseline.Clear();
        _pendingFileNames.Clear();
        foreach (var fileName in _fileNames)
        {
            _baseline[fileName] = ReadStamp(Path.Combine(_profileDirectory, fileName));
        }
    }

    private static FileStamp ReadStamp(string path)
    {
        try
        {
            var file = new FileInfo(path);
            return file.Exists
                ? new FileStamp(true, file.Length, file.LastWriteTimeUtc.Ticks)
                : FileStamp.Missing;
        }
        catch (IOException)
        {
            return FileStamp.Missing;
        }
        catch (UnauthorizedAccessException)
        {
            return FileStamp.Missing;
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private readonly record struct FileStamp(bool Exists, long Length, long LastWriteUtcTicks)
    {
        public static FileStamp Missing { get; } = new(false, 0, 0);
    }
}
