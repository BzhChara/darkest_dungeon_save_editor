using System.Security.Cryptography;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DarkestDungeonSaveEditor.Core;

internal enum SaveFileRecovery
{
    NotReplaced,
    RestoredOriginal,
    RestoredExternal,
    KeptExternal
}

/// <summary>
/// Captures the actual displaced version, then holds the installed file through the
/// transaction. Recovery writes only through a handle that denies other writes/renames;
/// it never replaces a pathname using an earlier observation of that pathname.
/// </summary>
internal sealed class GuardedSaveReplacement : IDisposable
{
    private readonly string _targetPath;
    private readonly string _encodedPath;
    private readonly string _originalHash;
    private readonly string _encodedHash;
    private readonly string _temporaryPath;
    private readonly Action<string>? _beforeReplace;
    private readonly Action<string>? _afterReplace;
    private FileStream? _target;
    private string? _displacedHash;
    private bool _completed;
    private bool _restoreMissingAfterPartialReplace;
    private SaveFileRecovery? _recovery;

    internal GuardedSaveReplacement(
        string targetPath, string encodedPath, string originalHash, string encodedHash,
        Action<string>? beforeReplace = null, Action<string>? afterReplace = null)
    {
        _targetPath = Path.GetFullPath(targetPath);
        _encodedPath = encodedPath;
        _originalHash = originalHash;
        _encodedHash = encodedHash;
        _beforeReplace = beforeReplace;
        _afterReplace = afterReplace;
        var directory = Path.GetDirectoryName(_targetPath)!;
        var prefix = $".{Path.GetFileName(_targetPath)}.ddse-{Guid.NewGuid():N}";
        _temporaryPath = Path.Combine(directory, prefix + ".tmp");
        DisplacedPath = Path.Combine(directory, prefix + ".displaced.tmp");
    }

    internal string TargetPath => _targetPath;
    internal string DisplacedPath { get; }
    internal bool HasReplaced { get; private set; }
    internal Action<string>? BeforeRecoveryLock { get; set; }
    internal Action<string, string, string>? AtomicReplaceOverride { get; set; }

    // Open existing file metadata with DELETE access, without changing any bytes.
    // A successful hash read alone does not establish that an external reader permits replacement.
    internal static void ValidateReplaceAccess(string path)
    {
        const uint deleteAccess = 0x00010000;
        const uint openExisting = 3;
        // Unlike the managed file APIs, this native call still depends on the
        // host's long-path manifest unless given an extended absolute path.
        var nativePath = Path.GetFullPath(path);
        if (!nativePath.StartsWith(@"\\?\", StringComparison.Ordinal) &&
            !nativePath.StartsWith(@"\\.\", StringComparison.Ordinal))
            nativePath = nativePath.StartsWith(@"\\", StringComparison.Ordinal)
                ? @"\\?\UNC\" + nativePath[2..] : @"\\?\" + nativePath;
        using var handle = CreateFileW(nativePath, deleteAccess,
            FileShare.ReadWrite | FileShare.Delete, IntPtr.Zero, openExisting, 0, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = new Win32Exception(Marshal.GetLastWin32Error());
            throw new IOException($"文件暂时不允许替换：{path}；{error.Message}", error);
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern SafeFileHandle CreateFileW(string fileName, uint desiredAccess,
        FileShare shareMode, IntPtr securityAttributes, uint creationDisposition,
        uint flagsAndAttributes, IntPtr templateFile);

    internal void Replace()
    {
        File.Copy(_encodedPath, _temporaryPath, overwrite: false);
        using (var proposed = File.OpenRead(_temporaryPath))
        {
            if (!Hash(proposed).Equals(_encodedHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("已验证的待写入文件发生变化，本次修改未应用。");
        }
        using (var original = new FileStream(_targetPath, FileMode.Open, FileAccess.Read,
                   FileShare.Read | FileShare.Delete))
        {
            if (!Hash(original).Equals(_originalHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"存档在最终写入前发生变化：{_targetPath}");
            _beforeReplace?.Invoke(_targetPath);
            try
            {
                if (AtomicReplaceOverride is { } replace)
                    replace(_temporaryPath, _targetPath, DisplacedPath);
                else
                    File.Replace(_temporaryPath, _targetPath, DisplacedPath, ignoreMetadataErrors: true);
                HasReplaced = true;
            }
            catch (IOException error) when ((error.HResult & 0xffff) == 1177)
            {
                // ReplaceFileW ERROR_UNABLE_TO_MOVE_REPLACEMENT_2: the original is
                // already at DisplacedPath although the candidate was not installed.
                HasReplaced = true;
                _restoreMissingAfterPartialReplace = true;
                using var displaced = File.OpenRead(DisplacedPath);
                _displacedHash = Hash(displaced);
                throw;
            }
        }

        using (var displaced = File.OpenRead(DisplacedPath))
            _displacedHash = Hash(displaced);
        _afterReplace?.Invoke(_targetPath);
        _target = OpenTarget();
        Verify();
        if (!_displacedHash.Equals(_originalHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"存档在原子替换瞬间被外部更新；被替换的实际版本保留在：{DisplacedPath}");
    }

    internal string Verify()
    {
        var hash = CurrentHash();
        if (!hash.Equals(_encodedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"存档在原子替换后被外部更新，程序不会用旧备份覆盖它：{_targetPath}");
        return hash;
    }

    internal string CurrentHash() => Hash(_target ?? throw new InvalidOperationException("目标文件尚未锁定。"));

    internal SaveFileRecovery Recover()
    {
        if (_recovery is { } recovered)
            return recovered;
        if (!HasReplaced)
            return SaveFileRecovery.NotReplaced;
        if (_restoreMissingAfterPartialReplace)
            return RecoverPartialReplace();

        // If the installed destination could not be locked, reacquire it before inspecting
        // it. A missing/locked file is an explicit recovery failure, never recreated blindly.
        _target?.Dispose();
        _target = null;
        BeforeRecoveryLock?.Invoke(_targetPath);
        _target = new FileStream(_targetPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        if (!CurrentHash().Equals(_encodedHash, StringComparison.OrdinalIgnoreCase))
            return (_recovery = SaveFileRecovery.KeptExternal).Value;

        using var displaced = File.OpenRead(DisplacedPath);
        var sourceHash = Hash(displaced);
        if (_displacedHash is not null && !sourceHash.Equals(_displacedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"被替换版本已发生变化，不能自动恢复：{DisplacedPath}");

        return RestoreFrom(displaced, sourceHash);
    }

    private SaveFileRecovery RecoverPartialReplace()
    {
        using var displaced = File.OpenRead(DisplacedPath);
        var sourceHash = Hash(displaced);
        if (_displacedHash is null || !sourceHash.Equals(_displacedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"被替换版本已发生变化，不能自动恢复：{DisplacedPath}");

        if (_target is null)
        {
            BeforeRecoveryLock?.Invoke(_targetPath);
            try
            {
                // CreateNew never overwrites a file created by an external writer.
                _target = new FileStream(_targetPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
            }
            catch (IOException) when (File.Exists(_targetPath))
            {
                _target = OpenTarget();
                return (_recovery = SaveFileRecovery.KeptExternal).Value;
            }
        }
        // Keep our exclusive write handle if copying fails, so a retry can repair
        // our own partial copy without treating its bytes as an external update.
        return RestoreFrom(displaced, sourceHash);
    }

    private SaveFileRecovery RestoreFrom(Stream displaced, string sourceHash)
    {
        // Retain the same read/write handle for comparison and restoration. It denies
        // FileShare.Write and FileShare.Delete, closing the rollback rename race. If I/O
        // fails during recovery, DisplacedPath and the full profile backup remain intact.
        displaced.Position = 0;
        _target!.Position = 0;
        displaced.CopyTo(_target);
        _target.SetLength(displaced.Length);
        _target.Flush(flushToDisk: true);
        if (!CurrentHash().Equals(sourceHash, StringComparison.OrdinalIgnoreCase))
            throw new IOException($"恢复后的存档未通过校验；请保留实际被替换版本：{DisplacedPath}");
        return (_recovery = sourceHash.Equals(_originalHash, StringComparison.OrdinalIgnoreCase)
            ? SaveFileRecovery.RestoredOriginal : SaveFileRecovery.RestoredExternal).Value;
    }

    internal void Complete()
    {
        Verify();
        _completed = true;
    }

    private FileStream OpenTarget() => new(_targetPath, FileMode.Open, FileAccess.Read, FileShare.Read);

    internal static string DescribeRecovery(SaveFileRecovery recovery) => recovery switch
    {
        SaveFileRecovery.RestoredOriginal => "已恢复写入前版本",
        SaveFileRecovery.RestoredExternal => "已恢复最终替换前的外部更新版本",
        SaveFileRecovery.KeptExternal => "已保留写入后出现的外部更新版本，未用旧备份覆盖",
        _ => "未替换存档"
    };

    private static string Hash(Stream stream)
    {
        stream.Position = 0;
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    public void Dispose()
    {
        _target?.Dispose();
        TryDelete(_temporaryPath);
        // Keep the exact displaced version after any failure, including interrupted or
        // incomplete recovery. It can contain progress newer than the profile backup.
        if (_completed)
            TryDelete(DisplacedPath);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
