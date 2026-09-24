using System.Diagnostics;

internal static partial class ContractSuite
{
    private static string CodecProbeHost => Path.Combine(AppContext.BaseDirectory,
        "DarkestDungeonSaveEditor.ContractTests.exe");

    public static async Task RunCodecCancellationOnlyAsync(string repositoryRoot)
    {
        var root = Path.Combine(repositoryRoot, "workspaces", "codec_contracts", Guid.NewGuid().ToString("N"));
        await RunCodecCancellationContractsAsync(root);
        Console.WriteLine($"Artifacts: {root}");
    }

    private static async Task RunCodecCancellationContractsAsync(string runRoot)
    {
        var root = Path.Combine(runRoot, "codec-cancellation");
        Directory.CreateDirectory(root);
        var jar = Path.Combine(root, "codec-process-probe.jar");
        var binary = Path.Combine(root, "input.dson");
        var json = Path.Combine(root, "input.json");
        File.WriteAllText(jar, "Contract process fixture; not a Java archive.");
        File.WriteAllBytes(binary, [0x01, 0xB1, 0x00, 0x00]);
        File.WriteAllText(json, "{\"base_root\":{}}");
        var codec = new DsonSaveCodec(jar, CodecProbeHost);

        async Task RequireCancellation(Task operation)
        {
            try { await operation.WaitAsync(TimeSpan.FromSeconds(10)); }
            catch (OperationCanceledException) { return; }
            throw new InvalidOperationException("The codec operation did not report cancellation.");
        }

        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            var unavailableCodec = new DsonSaveCodec(jar, Path.Combine(root, "must-not-start.exe"));
            foreach (var operation in new[] { "decode-dson", "decode-json", "encode-dson", "encode-json" })
            {
                var output = Path.Combine(root, operation, "output.json");
                await RequireCancellation(operation switch
                {
                    "decode-dson" => unavailableCodec.DecodeAsync(binary, output, cancelled.Token),
                    "decode-json" => unavailableCodec.DecodeAsync(json, output, cancelled.Token),
                    "encode-dson" => unavailableCodec.EncodeAsync(json, output, null, cancelled.Token),
                    _ => unavailableCodec.EncodeAsync(json, output, json, cancelled.Token)
                });
                Assert(!Directory.Exists(Path.GetDirectoryName(output)),
                    "Pre-cancelled binary and JSON operations must not create output directories or start the codec.");
            }
        }
        Console.WriteLine("PASS: pre-cancelled DSON/JSON encode and decode perform no output work.");

        foreach (var operation in new[] { "decode", "encode" })
        {
            var output = Path.Combine(root, operation + "-running.json");
            using var cancellation = new CancellationTokenSource();
            Process? parent = null;
            Process? child = null;
            var task = operation == "decode"
                ? codec.DecodeAsync(binary, output, cancellation.Token)
                : codec.EncodeAsync(json, output, null, cancellation.Token);
            try
            {
                await WaitForCodecProbeFileAsync(output + ".started");
                var ids = File.ReadAllText(output + ".started").Split(',').Select(int.Parse).ToArray();
                parent = Process.GetProcessById(ids[0]);
                child = Process.GetProcessById(ids[1]);
                cancellation.Cancel();
                await RequireCancellation(task);
                // An abandoned process can now finish and expose any writes made
                // after the public operation already reported cancellation.
                File.WriteAllText(output + ".release", string.Empty);
                await Task.WhenAll(parent.WaitForExitAsync(), child.WaitForExitAsync())
                    .WaitAsync(TimeSpan.FromSeconds(10));
                Assert(parent.HasExited && child.HasExited && !File.Exists(output) &&
                    !File.Exists(output + ".child-output"),
                    "Cancellation must terminate the codec tree and prevent late output from either process.");
            }
            finally
            {
                cancellation.Cancel();
                File.WriteAllText(output + ".release", string.Empty);
                foreach (var process in new[] { parent, child })
                {
                    if (process is null) continue;
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                    process.Dispose();
                }
                try { await task.WaitAsync(TimeSpan.FromSeconds(10)); }
                catch (Exception) when (task.IsCompleted) { }
            }
        }
        Console.WriteLine("PASS: running encode/decode cancellation terminates the process tree, drains both pipes and prevents late writes.");
    }

    private static async Task WaitForCodecProbeFileAsync(string path)
    {
        var deadline = Stopwatch.StartNew();
        while (!File.Exists(path))
        {
            if (deadline.Elapsed > TimeSpan.FromSeconds(10))
                throw new TimeoutException("Codec contract process did not become ready: " + path);
            await Task.Delay(20);
        }
    }

    // Invoked only in an isolated test executable. It deliberately waits for a
    // release file so cancellation cannot race a small, fast Java conversion.
    public static async Task<bool> TryRunCodecProcessProbeAsync(string[] args)
    {
        if (args is ["--codec-probe-child", var childOutput])
        {
            File.WriteAllText(childOutput + ".child-started", string.Empty);
            await WaitForCodecProbeFileAsync(childOutput + ".release");
            File.WriteAllText(childOutput + ".child-output", "late child write");
            return true;
        }
        if (args is not ["-jar", var jar, "decode" or "encode", "--output", var output, _] ||
            Path.GetFileName(jar) != "codec-process-probe.jar") return false;

        var start = new ProcessStartInfo(CodecProbeHost) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("--codec-probe-child");
        start.ArgumentList.Add(output);
        using var child = Process.Start(start) ?? throw new IOException("Cannot start codec contract child.");
        try
        {
            await WaitForCodecProbeFileAsync(output + ".child-started");
            Console.Out.Write(new string('o', 128 * 1024));
            Console.Error.Write(new string('e', 128 * 1024));
            var marker = output + ".starting";
            File.WriteAllText(marker, $"{Environment.ProcessId},{child.Id}");
            File.Move(marker, output + ".started");
            await child.WaitForExitAsync();
            File.WriteAllText(output, "{\"base_root\":{}}");
        }
        finally
        {
            if (!child.HasExited) child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync();
        }
        return true;
    }
}
