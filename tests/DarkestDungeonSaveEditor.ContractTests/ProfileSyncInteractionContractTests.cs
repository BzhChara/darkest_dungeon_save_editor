using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DarkestDungeonSaveEditor.App;

internal static partial class ContractSuite
{
    public static async Task RunContentSyncOnlyAsync(string repositoryRoot)
    {
        var fixture = BuildContractFixture(repositoryRoot);
        await SeedSyncFixtureAsync(fixture);
        await RunSourceBindingInventoryContractsAsync(Path.Combine(fixture.RunRoot, "source-bindings"), fixture.Codec);
        await RunSourceBindingBattleContractsAsync(Path.Combine(fixture.RunRoot, "battle-bindings"), fixture.Codec);
        await RunSourceBindingHeroContractsAsync(fixture);
        await RunProfileSyncContractsAsync(fixture);
        await RunProfileSyncInteractionContractsAsync(fixture);
        Console.WriteLine($"Artifacts: {fixture.RunRoot}");
    }

    private static async Task SeedSyncFixtureAsync(ContractFixture f)
    {
        foreach (var (input, output) in new[] { (f.DecodedSeedPath, f.EstatePath), (f.DecodedGameSeedPath, f.GameSavePath),
            (f.DecodedTownSeedPath, f.TownSavePath), (f.DecodedRosterSeedPath, f.RosterSavePath), (f.DecodedUpgradesSeedPath, f.UpgradesSavePath) })
            await f.Codec.EncodeAsync(input, output, null);
    }

    private static Task RunProfileSyncInteractionContractsAsync(ContractFixture f) =>
        RunWpfContractsAsync(() => VerifyProfileSyncInteractionAsync(f));

    // Exercise the shipped WPF handlers on a Dispatcher. No visible window or real profile is opened.
    private static async Task VerifyProfileSyncInteractionAsync(ContractFixture f)
    {
        var window = new MainWindow();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Get(string name) => typeof(MainWindow).GetField(name, flags)!.GetValue(window);
        void Set(string name, object? value) => typeof(MainWindow).GetField(name, flags)!.SetValue(window, value);
        object? Call(string name, params object?[] args) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, args);
        bool Flag(string name) => (bool)Get(name)!;
        async Task Settled()
        {
            var deadline = DateTime.UtcNow.AddSeconds(60);
            while (Flag("_syncInProgress") || Flag("_syncRequested"))
            {
                if (DateTime.UtcNow > deadline) throw new TimeoutException("WPF sync did not settle: " + Get("_lastSyncError"));
                await Task.Delay(15);
            }
        }
        SemaphoreSlim ReaderGate() => (SemaphoreSlim)typeof(ProfileCatalogSnapshotReader).GetField("_gate", flags)!
            .GetValue(Get("_catalogSnapshotReader"))!;
        var profile = new SaveProfile("sync_ui", f.ProfileRoot, f.EstatePath, "contract", DateTime.UtcNow);
        var content = await ActiveContentResolver.ResolveAsync(profile, f.GameRoot, f.WorkshopRoot,
            f.AdditionalLocalModDirectory, f.Codec, f.RunRoot);
        var items = await QuantityItemCatalog.LoadAsync(content, f.Codec);
        var fingerprint = ProfileCatalogContentFingerprint.Capture(content.Sources);
        var locations = new SaveEditorLocations(f.RunRoot, Path.Combine(f.RunRoot, "ui-work"), Path.Combine(f.RunRoot, "ui-backups"));
        Set("_activeContentSnapshot", content);
        Set("_allItems", items.Items);
        Set("_quantitySaveContext", items.SaveContext);
        var gate = (SemaphoreSlim?)null;
        try
        {
            Call("StartProfileSync", content, items, f.Codec, f.GameRoot, f.WorkshopRoot, f.AdditionalLocalModDirectory, fingerprint, locations);
            await Settled();
            // Poll explicitly to avoid wall-clock timer races in the contract.
            ((DispatcherTimer)Get("_contentPoll")!).Stop();
            ((ProfileSaveMonitor)Get("_catalogMonitor")!).Stop();
            var service = new SaveEditService(f.Codec, new(f.RunRoot, Path.Combine(f.RunRoot, "ui-work"), Path.Combine(f.RunRoot, "ui-backups")));
            var prepared = await service.PrepareQuantityItemEditAsync(content.Profile,
                items.Items.Single(item => item.DisplayId == "gold"), 17, content);
            Set("_preparedQuantityItemEdit", prepared);
            ((TextBox)Get("CopiesTextBox")!).Text = "17";
            Set("_preparedQuantityItemEdit", prepared); // Input changes deliberately invalidate a preview.
            Call("UpdateEnabledState");
            var status = ((TextBlock)Get("ProfileSyncStatusTextBlock")!).Text;
            var revision = Get("_editRevision");
            var logs = ((TextBox)Get("StatusTextBox")!).Text;
            for (var i = 0; i < 3; i++)
            {
                gate = ReaderGate();
                await gate.WaitAsync();
                Call("RequestProfileSync", false);
                Call("RequestProfileSync", false); // Coalesced focus + timer checks must also remain nonblocking.
                Assert(Flag("_syncInProgress") && Flag("_syncReady") && ((Control)Get("BattleMapPanel")!).IsEnabled &&
                    ((Button)Get("ApplyButton")!).IsEnabled && ReferenceEquals(Get("_preparedQuantityItemEdit"), prepared),
                    "An unchanged background check must not disable map/apply or invalidate a prepared edit.");
                if (i == 1) { Call("SetBusy", true); Call("SetBusy", false); } // An operation finished while the check was waiting.
                gate.Release(); gate = null;
                await Settled();
                Assert(Equals(Get("_editRevision"), revision) && ReferenceEquals(Get("_preparedQuantityItemEdit"), prepared) &&
                    ((TextBlock)Get("ProfileSyncStatusTextBlock")!).Text == status && ((TextBox)Get("StatusTextBox")!).Text == logs,
                    "Quiet polls must preserve preview, status and log, including a completed concurrent operation.");
            }
            Console.WriteLine("PASS: WPF idle/focus polling stays interactive, preserves previews and emits no repeated sync status.");

            var running = true;
            Set("_battleMaintenance", new ManagedBattleEncounterBridgeService(f.Codec, locations, () => running));
            Set("_battleMaintenanceDeferred", true);
            Call("RequestProfileSync", false); await Settled();
            Assert(Flag("_battleMaintenanceDeferred") && ReferenceEquals(Get("_preparedQuantityItemEdit"), prepared),
                "A cleanup deferred by the running game must not keep disrupting otherwise unchanged polls.");
            running = false;
            Call("RequestProfileSync", false); await Settled();
            Assert(!Flag("_battleMaintenanceDeferred") && Flag("_syncReady"), "Closing the game must retry pending maintenance even without save changes.");
            Console.WriteLine("PASS: WPF game-exit maintenance retry does not repeatedly block quiet polling.");

            var pending = Path.Combine(locations.BackupDirectory, profile.SteamUserId, profile.ProfileId, "pending-recovery");
            Directory.CreateDirectory(pending);
            var pendingManifest = Path.Combine(pending, "backup-manifest.json");
            File.WriteAllText(pendingManifest, System.Text.Json.JsonSerializer.Serialize(new
            {
                version = 2, operation = EditorBattleHistory.CleanupOperation, profile.ProfileId, profile.SteamUserId, profile.ProfileDirectory,
                GameSha256 = ComputeSha256(f.GameSavePath).ToLowerInvariant(), RaidSha256 = (string?)null,
                Files = Array.Empty<object>(), ReadOnlyFiles = Array.Empty<object>()
            }));
            File.WriteAllText(Path.Combine(pending, "maintenance-pending.json"), "{}");
            Set("_battleMaintenanceDeferred", true);
            Set("_preparedQuantityItemEdit", prepared);
            Call("UpdateEnabledState");
            using (var lockedHistory = new FileStream(pendingManifest, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                for (var attempt = 0; attempt < 2; attempt++)
                {
                    Call("RequestProfileSync", false); await Settled();
                    Assert(Flag("_maintenanceRetryPending") && Flag("_syncReady") && ((Button)Get("ApplyButton")!).IsEnabled &&
                        ReferenceEquals(Get("_preparedQuantityItemEdit"), prepared) && !File.Exists(Path.Combine(pending, "maintenance-recovered.json")),
                        "A transient history lock must retain retries without resetting an unchanged screen or preview.");
                }
            }
            Call("RequestProfileSync", false); await Settled();
            Assert(!Flag("_maintenanceRetryPending") && Flag("_syncReady") && File.Exists(Path.Combine(pending, "maintenance-recovered.json")),
                "Releasing the history lock must automatically finish recovery with unchanged save/content hashes. " +
                Get("_lastMaintenanceMessage") + " / " + Get("_lastSyncError"));
            Console.WriteLine("PASS: WPF transient maintenance failures retry read-only and recover after unlock without reload.");

            var pendingMap = Path.Combine(locations.BackupDirectory, profile.SteamUserId, profile.ProfileId, "pending-map-recovery");
            Directory.CreateDirectory(pendingMap);
            var originalMap = Path.Combine(pendingMap, "persist.map.json");
            File.WriteAllText(originalMap, "{\"base_root\":{\"contract\":\"before\"}}");
            File.WriteAllText(profile.MapSavePath, "{\"base_root\":{\"contract\":\"after\"}}");
            Call("RequestProfileSync", false); await Settled();
            var originalMapHash = ComputeSha256(originalMap).ToLowerInvariant();
            var changedMapHash = ComputeSha256(profile.MapSavePath).ToLowerInvariant();
            File.WriteAllText(Path.Combine(pendingMap, "backup-manifest.json"), System.Text.Json.JsonSerializer.Serialize(new
            {
                version = 2, operation = EditorBattleHistory.CleanupOperation, profile.ProfileId, profile.SteamUserId, profile.ProfileDirectory,
                GameSha256 = ComputeSha256(f.GameSavePath).ToLowerInvariant(), RaidSha256 = (string?)null,
                Files = new[] { new { TargetPath = profile.MapSavePath, OriginalSha256 = originalMapHash, FinalSha256 = changedMapHash } },
                ReadOnlyFiles = Array.Empty<object>()
            }));
            File.WriteAllText(Path.Combine(pendingMap, "maintenance-pending.json"), "{}");
            Set("_maintenanceRetryPending", true);
            Set("_preparedQuantityItemEdit", prepared);
            Call("UpdateEnabledState");
            revision = Get("_editRevision");
            using (var lockedMap = new FileStream(profile.MapSavePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                for (var attempt = 0; attempt < 2; attempt++)
                {
                    Call("RequestProfileSync", false); await Settled();
                    Assert(Flag("_maintenanceRetryPending") && Flag("_syncReady") && ((Button)Get("ApplyButton")!).IsEnabled &&
                        Equals(Get("_editRevision"), revision) && ReferenceEquals(Get("_preparedQuantityItemEdit"), prepared) &&
                        ComputeSha256(profile.MapSavePath).ToLowerInvariant() == changedMapHash &&
                        !File.Exists(Path.Combine(pendingMap, "maintenance-recovered.json")),
                        "A readable target that denies replacement must not repeatedly invalidate previews during recovery retries.");
                }
            }
            // A reader that explicitly allows replacement need not be closed for recovery.
            using (var sharedMap = new FileStream(profile.MapSavePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                Call("RequestProfileSync", false); await Settled();
                Assert(!Flag("_maintenanceRetryPending") && Flag("_syncReady") &&
                    ComputeSha256(profile.MapSavePath).ToLowerInvariant() == originalMapHash &&
                    File.Exists(Path.Combine(pendingMap, "maintenance-recovered.json")),
                    "A nonempty recovery plan must resume when replacement is allowed, including while a permissive reader is open.");
            }
            File.Delete(profile.MapSavePath);
            Call("RequestProfileSync", false); await Settled();
            Console.WriteLine("PASS: WPF readable replacement locks preserve previews; nonempty recovery resumes when replacement is allowed.");

            gate = ReaderGate(); await gate.WaitAsync();
            Call("RequestProfileSync", true);
            Assert(!Flag("_syncReady") && Get("_preparedQuantityItemEdit") is null && !((Control)Get("BattleMapPanel")!).IsEnabled,
                "A real save notification must immediately disable stale writes.");
            gate.Release(); gate = null; await Settled();

            var disabledDuringChange = false;
            var panel = (Control)Get("BattleMapPanel")!;
            panel.IsEnabledChanged += (_, _) => { if (!panel.IsEnabled) disabledDuringChange = true; };
            var hot = WriteMultiMash(f.GameRoot, "inventory/sync-ui.inventory.items.darkest",
                "inventory_item: .type estate .id sync_ui_probe .base_stack_limit 7\n");
            Call("RequestProfileSync", false); await Settled();
            Assert(disabledDuringChange && Flag("_syncReady") && ((IReadOnlyList<QuantityItemDefinition>)Get("_allItems")!).Any(item => item.ItemId == "sync_ui_probe") &&
                ((TextBox)Get("CopiesTextBox")!).Text == "17", "Actual resource updates publish exclusively and preserve user input.");
            File.Delete(hot);
            Call("RequestProfileSync", false); await Settled();
            Console.WriteLine("PASS: WPF real changes pause publication only when needed and preserve input.");

            var bytes = File.ReadAllBytes(f.GameSavePath);
            File.WriteAllText(f.GameSavePath, "{broken");
            Call("RequestProfileSync", false);
            while (Flag("_syncInProgress")) await Task.Delay(15);
            Assert(!Flag("_syncReady") && !panel.IsEnabled && Get("_lastSyncError") is not null,
                "Partial saves must pause writes and schedule recovery instead of publishing a partial catalog.");
            File.WriteAllBytes(f.GameSavePath, bytes);
            Call("RequestProfileSync", false); await Settled();
            Assert(Flag("_syncReady") && Get("_lastSyncError") is null, "Stable save recovery must restore interaction.");

            gate = ReaderGate(); await gate.WaitAsync();
            Call("RequestProfileSync", false);
            var latest = (ActiveContentSnapshot)Get("_activeContentSnapshot")!;
            var latestItems = await QuantityItemCatalog.LoadAsync(latest, f.Codec);
            Call("StopProfileSync");
            Call("StartProfileSync", latest, latestItems, f.Codec, f.GameRoot, f.WorkshopRoot,
                f.AdditionalLocalModDirectory, ProfileCatalogContentFingerprint.Capture(latest.Sources), locations);
            gate.Release(); gate = null; await Settled();
            Assert(Flag("_syncReady") && !Flag("_syncApplying"), "Cancelled older generations must not lock a newly loaded profile.");
            Console.WriteLine("PASS: WPF partial-save recovery and cancellation discard old checks without leaving controls locked.");
            await VerifyMissingResourceSyncAsync(f);
        }
        finally
        {
            gate?.Release();
            Call("StopProfileSync");
            window.Close();
        }
    }
}
