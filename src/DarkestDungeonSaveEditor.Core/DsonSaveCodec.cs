using System.Diagnostics;

namespace DarkestDungeonSaveEditor.Core;

public sealed class DsonSaveCodec
{
    private static readonly byte[] Magic = [0x01, 0xB1, 0x00, 0x00];
    private readonly string _jarPath;
    private readonly string _javaExecutable;

    public DsonSaveCodec(string jarPath, string javaExecutable = "java")
    {
        _jarPath = Path.GetFullPath(jarPath);
        _javaExecutable = javaExecutable;
    }

    public string JarPath => _jarPath;

    // The bundled codec guesses one-byte printable values as chars without
    // JSON-escaping them; other bytes decode as booleans. Purchase codes must
    // survive that actual round trip, not merely fit in a native byte.
    internal static bool CanRoundTripPurchaseCode(string code) =>
        code.Length == 1 && code[0] is >= '!' and <= '~' and not ('"' or '\\');

    public void ValidateAvailability()
    {
        if (!File.Exists(_jarPath))
        {
            throw new FileNotFoundException("DDSaveEditor.jar was not found.", _jarPath);
        }
    }

    public static bool IsDson(string path)
    {
        using var stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[4];
        return stream.Read(header) == header.Length && header.SequenceEqual(Magic);
    }

    public async Task DecodeAsync(string inputPath, string outputPath, CancellationToken cancellationToken = default)
    {
        ValidateAvailability();
        inputPath = Path.GetFullPath(inputPath);
        outputPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        if (!IsDson(inputPath))
        {
            _ = JsonSupport.ReadObject(inputPath);
            File.Copy(inputPath, outputPath, overwrite: false);
            return;
        }

        await RunAsync("decode", inputPath, outputPath, cancellationToken).ConfigureAwait(false);
        _ = JsonSupport.ReadObject(outputPath);
    }

    public async Task EncodeAsync(
        string decodedJsonPath,
        string outputPath,
        string? originalBinaryPath,
        CancellationToken cancellationToken = default)
    {
        ValidateAvailability();
        decodedJsonPath = Path.GetFullPath(decodedJsonPath);
        outputPath = Path.GetFullPath(outputPath);
        _ = JsonSupport.ReadObject(decodedJsonPath);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        if (!string.IsNullOrWhiteSpace(originalBinaryPath) && !IsDson(originalBinaryPath))
        {
            File.Copy(decodedJsonPath, outputPath, overwrite: false);
            return;
        }

        await RunAsync("encode", decodedJsonPath, outputPath, cancellationToken).ConfigureAwait(false);
        if (!IsDson(outputPath))
        {
            throw new InvalidDataException($"Encoded output is not Darkest Dungeon DSON: {outputPath}");
        }

        if (!string.IsNullOrWhiteSpace(originalBinaryPath))
        {
            PreserveRevision(originalBinaryPath, outputPath);
        }
    }

    private async Task RunAsync(
        string operation,
        string inputPath,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _javaExecutable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-jar");
        startInfo.ArgumentList.Add(_jarPath);
        startInfo.ArgumentList.Add(operation);
        startInfo.ArgumentList.Add("--output");
        startInfo.ArgumentList.Add(outputPath);
        startInfo.ArgumentList.Add(inputPath);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start Java for DDSaveEditor.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        if (process.ExitCode != 0 || !File.Exists(outputPath))
        {
            throw new InvalidOperationException(
                $"DDSaveEditor {operation} failed with exit code {process.ExitCode}. {stderr.Trim()} {stdout.Trim()}".Trim());
        }
    }

    private static void PreserveRevision(string originalBinaryPath, string encodedPath)
    {
        if (!IsDson(originalBinaryPath))
        {
            return;
        }

        using var original = File.OpenRead(originalBinaryPath);
        using var encoded = File.Open(encodedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        if (original.Length < 8 || encoded.Length < 8)
        {
            throw new InvalidDataException("DSON file is too short to preserve its revision field.");
        }

        original.Position = 4;
        Span<byte> revision = stackalloc byte[4];
        original.ReadExactly(revision);
        encoded.Position = 4;
        encoded.Write(revision);
        encoded.Flush(flushToDisk: true);
    }
}
