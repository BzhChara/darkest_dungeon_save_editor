internal static partial class ContractSuite
{
    private static void RunSaveReplacementContracts(string runRoot)
    {
        var root = Path.Combine(runRoot, "guarded-replacement");
        Directory.CreateDirectory(root);
        foreach (var scenario in new[] { "success", "rollback", "before", "after", "both", "recovery-race", "locked-recovery", "corrupt-displaced" })
        {
            var target = Path.Combine(root, scenario + ".json");
            var encoded = target + ".encoded";
            File.WriteAllText(target, "original");
            File.WriteAllText(encoded, "proposed");
            using var replacement = new GuardedSaveReplacement(target, encoded,
                ComputeSha256(target), ComputeSha256(encoded),
                scenario is "before" or "both" ? path => ReplaceExternalBytes(path, "external-before"u8.ToArray()) : null,
                scenario is "after" or "both" ? path => ReplaceExternalBytes(path, "external-after"u8.ToArray()) : null);
            if (scenario is "before" or "after" or "both")
            {
                var failure = CaptureSaveFailure(replacement.Replace);
                var recovery = replacement.Recover();
                replacement.Dispose();
                Assert(recovery == (scenario == "before" ? SaveFileRecovery.RestoredExternal : SaveFileRecovery.KeptExternal) &&
                       File.ReadAllText(target) == (scenario == "before" ? "external-before" : "external-after") &&
                       File.ReadAllText(replacement.DisplacedPath) == (scenario == "after" ? "original" : "external-before"),
                    $"Replacement/recovery must preserve external versions ({scenario}, {recovery}): {failure}");
                continue;
            }

            replacement.Replace();
            Assert(File.ReadAllText(target) == "proposed", "Read-only catalog readers must remain compatible with a committed-file guard.");
            _ = CaptureSaveFailure(() => ReplaceExternalBytes(target, "blocked"u8.ToArray()));
            if (scenario == "success")
            {
                replacement.Complete();
                replacement.Dispose();
                Assert(!File.Exists(replacement.DisplacedPath) && File.ReadAllText(target) == "proposed",
                    "Only successful completion may remove the exact displacement copy.");
            }
            else if (scenario == "recovery-race")
            {
                replacement.BeforeRecoveryLock = path => ReplaceExternalBytes(path, "external-during-recovery"u8.ToArray());
                var recovery = replacement.Recover();
                replacement.Dispose();
                Assert(recovery == SaveFileRecovery.KeptExternal && File.ReadAllText(target) == "external-during-recovery",
                    "Recovery must inspect the same locked handle it would write, including external replacement while reacquiring that handle.");
            }
            else if (scenario == "locked-recovery")
            {
                using (var reader = File.OpenRead(target))
                    Assert(CaptureSaveFailure(() => replacement.Recover()) is IOException,
                        "Recovery must report an incompatible observer lock instead of claiming restoration.");
                Assert(File.ReadAllText(target) == "proposed" && File.ReadAllText(replacement.DisplacedPath) == "original",
                    "Failed recovery must retain both the installed version and the recovery source.");
                Assert(replacement.Recover() == SaveFileRecovery.RestoredOriginal, "Recovery may be retried after the blocking handle closes.");
            }
            else if (scenario == "corrupt-displaced")
            {
                File.WriteAllText(replacement.DisplacedPath, "damaged");
                var error = CaptureSaveFailure(() => replacement.Recover());
                replacement.Dispose();
                Assert(error is InvalidDataException && File.ReadAllText(target) == "proposed",
                    "A changed recovery source must never be copied over the installed save.");
            }
            else
            {
                var recovery = replacement.Recover();
                replacement.Dispose();
                Assert(recovery == SaveFileRecovery.RestoredOriginal && File.ReadAllText(target) == "original",
                    "A failed transaction without external updates must recover byte-for-byte.");
            }
        }
        foreach (var scenario in new[] { "missing", "external", "corrupt" })
        {
            var target = Path.Combine(root, "partial-" + scenario + ".json");
            var encoded = target + ".encoded";
            File.WriteAllText(target, "original");
            File.WriteAllText(encoded, "proposed");
            using var replacement = new GuardedSaveReplacement(target, encoded, ComputeSha256(target), ComputeSha256(encoded));
            replacement.AtomicReplaceOverride = (_, destination, displaced) =>
            {
                File.Move(destination, displaced);
                throw new IOException("Injected ReplaceFileW partial failure", unchecked((int)0x80070499));
            };
            var error = CaptureSaveFailure(replacement.Replace);
            Assert((error.HResult & 0xffff) == 1177 && replacement.HasReplaced && !File.Exists(target),
                "A partially failed native replacement must enter recovery even though File.Replace threw.");
            if (scenario == "external")
            {
                replacement.BeforeRecoveryLock = path => File.WriteAllText(path, "external-after-partial-failure");
                Assert(replacement.Recover() == SaveFileRecovery.KeptExternal &&
                       File.ReadAllText(target) == "external-after-partial-failure",
                    "Partial-failure recovery must never overwrite a newly created external save.");
            }
            else if (scenario == "corrupt")
            {
                File.WriteAllText(replacement.DisplacedPath, "damaged");
                Assert(CaptureSaveFailure(() => replacement.Recover()) is InvalidDataException && !File.Exists(target),
                    "A corrupt displaced source must be rejected before recreating the missing target.");
            }
            else
            {
                Assert(replacement.Recover() == SaveFileRecovery.RestoredOriginal,
                    "Partial native failure must restore the missing target from its actual displaced original.");
                Assert(replacement.Recover() == SaveFileRecovery.RestoredOriginal,
                    "Completed partial-failure recovery must be idempotent.");
                replacement.Dispose();
                Assert(File.ReadAllText(target) == "original", "Recovered bytes must match the displaced original exactly.");
            }
        }
        Console.WriteLine("PASS: atomic replacement boundaries, partial native failure, external-version preservation, locked recovery, and corrupt recovery sources.");
    }

    private static Exception CaptureSaveFailure(Action action)
    {
        try { action(); }
        catch (Exception ex) { return ex; }
        throw new InvalidOperationException("Expected the save operation to fail.");
    }

    private static async Task<Exception> CaptureSaveFailureAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { return ex; }
        throw new InvalidOperationException("Expected the save operation to fail.");
    }

    private static void ReplaceExternalBytes(string path, byte[] bytes)
    {
        var temp = path + ".external-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllBytes(temp, bytes);
            File.Replace(temp, path, temp + ".previous", ignoreMetadataErrors: true);
        }
        finally { File.Delete(temp); }
    }

    private static async Task VerifyServiceReplacementRacesAsync(string target,
        Func<Action<string>?, Action<string>?, Task> commit)
    {
        var original = File.ReadAllBytes(target);
        var beforeBytes = "external-version-before"u8.ToArray();
        var afterBytes = "external-version-after"u8.ToArray();
        foreach (var before in new[] { true, false })
        {
            var injected = false;
            Action<string> update = path => { ReplaceExternalBytes(path, before ? beforeBytes : afterBytes); injected = true; };
            try
            {
                var error = await CaptureSaveFailureAsync(() => commit(before ? update : null, before ? null : update));
                Assert(injected && File.ReadAllBytes(target).SequenceEqual(before ? beforeBytes : afterBytes) &&
                       error.ToString().Contains("外部", StringComparison.Ordinal),
                    "The save service must report the failure and preserve the external writer's exact bytes at both replacement boundaries.");
            }
            finally { File.WriteAllBytes(target, original); }
        }
    }
}
