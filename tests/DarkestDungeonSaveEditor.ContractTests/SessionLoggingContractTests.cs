internal static partial class ContractSuite
{
    private static void VerifySessionLogContracts(string runRoot)
    {
        var directory = Path.Combine(runRoot, "session-logs");
        Directory.CreateDirectory(directory);
        var legacy = Path.Combine(directory, "app-20260906.log");
        File.WriteAllText(legacy, "historical log must remain unchanged", new UTF8Encoding(false));
        var start = new DateTimeOffset(2026, 9, 6, 23, 59, 59, 123, TimeSpan.FromHours(8));
        var first = new SessionLogFile(directory, start, 1234);
        var second = new SessionLogFile(directory, start, 1234);
        first.Append("before midnight\n");
        first.Append("after midnight\n");
        second.Append("another application run\n");
        Assert(first.FilePath != second.FilePath &&
               Path.GetFileName(first.FilePath).StartsWith("app-20260906-235959-123-1234-", StringComparison.Ordinal) &&
               File.ReadAllText(first.FilePath) == "before midnight\nafter midnight\n" &&
               File.ReadAllText(second.FilePath) == "another application run\n" &&
               File.ReadAllText(legacy) == "historical log must remain unchanged" &&
               Directory.EnumerateFiles(directory).Count() == 3,
            "Each application run must retain one stable append-only file even with identical start time/PID, without merging sessions or touching historical daily logs.");
        Parallel.For(0, 100, index => first.Append($"parallel-entry-{index:D3}\n"));
        var lines = File.ReadAllLines(first.FilePath);
        Assert(lines.Length == 102 && lines.Skip(2).Distinct(StringComparer.Ordinal).Count() == 100 &&
               Enumerable.Range(0, 100).All(index => lines.Contains($"parallel-entry-{index:D3}", StringComparer.Ordinal)),
            "Concurrent log appends within one session must retain complete, non-interleaved entries.");
    }

    private static void VerifyCatalogDiagnosticBatchContracts()
    {
        const string path = @"E:\fixture\localization\failed.xml";
        const string failure = $"Failed to read localization '{path}': Invalid XML at line 2.";
        const string partialPath = @"E:\fixture\localization\english.loc";
        const string partial = CatalogIssueCode.PartialLocalization + partialPath + "';" +
            "本地化部分读取：跳过 1 个无效项，其余有效条目继续读取。";
        var batch = new CatalogDiagnosticBatch();
        var original = new[] { failure, partial };
        batch.Add("人物/怪癖/姓名", original);
        batch.Add("物品", [failure]);
        batch.Add("战斗遭遇", [failure.Replace(path, path.ToLowerInvariant(), StringComparison.Ordinal), partial.Replace(partialPath, partialPath.ToLowerInvariant(), StringComparison.Ordinal), "An unrelated encounter warning."]);
        batch.Add("战斗附加内容", [failure, partial]);
        var summary = batch.Summarize();
        var written = batch.Drain();
        Assert(summary.Count == 3 && written.SequenceEqual(summary) && batch.Drain().Count == 0 && original[0] == failure,
            "One shared load must emit each file/cause only once across general, encounter and attachment catalogs without changing raw issues or writing twice in cleanup.");
        var xml = summary.Single(entry => entry.Message.Contains("Invalid XML", StringComparison.Ordinal));
        Assert(new[] { "人物/怪癖/姓名", "物品", "战斗遭遇", "战斗附加内容" }.All(module => xml.Message.Contains(module, StringComparison.Ordinal)) &&
               summary.Single(entry => entry.Message.Contains("本地化部分读取", StringComparison.Ordinal)).Message.Contains("战斗附加内容", StringComparison.Ordinal),
            "Cross-module grouping must retain all reporting modules for full and partial localization failures.");
        var nextLoad = new CatalogDiagnosticBatch();
        nextLoad.Add("人物/怪癖/姓名", [failure]);
        Assert(nextLoad.Drain().Count == 1,
            "A repeated warning in a later load must not be swallowed by a process-wide deduplication cache.");
        foreach (var cancel in new[] { false, true })
        {
            foreach (var failingModule in new[] { "quantity", "heroes" })
            {
                var interrupted = new CatalogDiagnosticBatch();
                var output = new List<DiagnosticLogEntry>();
                Exception Failure() => cancel
                    ? new OperationCanceledException("Simulated catalog cancellation.")
                    : new InvalidDataException("Simulated catalog failure.");
                var staticTask = Task.Run(() => new
                {
                    Trinkets = interrupted.Capture("饰品", () => new[] { failure }, result => result),
                    Heroes = interrupted.Capture("人物/怪癖/姓名", () =>
                        failingModule == "heroes" ? throw Failure() : new[] { failure }, result => result)
                });
                var quantityTask = interrupted.CaptureAsync("物品", () =>
                    failingModule == "quantity" ? Task.FromException<string[]>(Failure()) : Task.FromResult(new[] { failure }), result => result);
                var caughtOriginalFailure = false;
                try
                {
                    Task.WhenAll(staticTask, quantityTask).GetAwaiter().GetResult();
                }
                catch (Exception ex) when (ex is OperationCanceledException or InvalidDataException)
                {
                    caughtOriginalFailure = true;
                }
                finally
                {
                    output.AddRange(interrupted.Drain());
                }
                var successfulSibling = failingModule == "quantity" ? "人物/怪癖/姓名" : "物品";
                var failedName = failingModule == "quantity" ? "物品" : "人物/怪癖/姓名";
                Assert(caughtOriginalFailure && output.Count == 1 && output[0].Message.Contains("饰品", StringComparison.Ordinal) &&
                       output[0].Message.Contains(successfulSibling, StringComparison.Ordinal) &&
                       !output[0].Message.Contains(failedName, StringComparison.Ordinal),
                    "The actual capture/WhenAll orchestration must retain successful sibling catalogs after quantity or hero failure/cancellation, without swallowing the original error or reporting a failed catalog as completed.");
            }
        }
        var concurrent = new CatalogDiagnosticBatch();
        Parallel.For(0, 100, index => concurrent.Capture($"module_{index:D3}", () => new[] { failure }, result => result));
        var concurrentEntry = concurrent.Drain().Single();
        Assert(Enumerable.Range(0, 100).All(index => concurrentEntry.Message.Contains($"module_{index:D3}", StringComparison.Ordinal)),
            "Concurrent successful readers must not lose reporting modules while appending to the shared diagnostic batch.");
    }
}
