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
        await RunCodecCancellationContractsAsync(fixture.RunRoot);
        await SeedSyncFixtureAsync(fixture);
        await RunSourceBindingInventoryContractsAsync(Path.Combine(fixture.RunRoot, "source-bindings"), fixture.Codec);
        await RunSourceBindingBattleContractsAsync(Path.Combine(fixture.RunRoot, "battle-bindings"), fixture.Codec);
        await RunSourceBindingHeroContractsAsync(fixture);
        await RunProfileSyncContractsAsync(fixture);
        await RunProfileSyncInteractionContractsAsync(fixture);
        Console.WriteLine($"Artifacts: {fixture.RunRoot}");
    }

    private static async Task SeedSyncFixtureAsync(ContractFixture f, bool includeMissingWorkshop = false)
    {
        foreach (var (input, output) in new[] { (f.DecodedSeedPath, f.EstatePath), (f.DecodedGameSeedPath, f.GameSavePath),
            (f.DecodedTownSeedPath, f.TownSavePath), (f.DecodedRosterSeedPath, f.RosterSavePath), (f.DecodedUpgradesSeedPath, f.UpgradesSavePath) })
            await f.Codec.EncodeAsync(input, output, null);
        // The discovery fixture deliberately references an absent Workshop item.
        // Interactive loading now requires all enabled sources to resolve.
        if (!includeMissingWorkshop)
        {
            var game = JsonNode.Parse(File.ReadAllText(f.DecodedGameSeedPath))!.AsObject();
            game["base_root"]!["applied_ugcs_1_0"]!.AsObject().Remove("11");
            await WriteModLoadGameAsync(f, game);
        }
    }

    private static Task RunProfileSyncInteractionContractsAsync(ContractFixture f) =>
        RunWpfContractsAsync(() => VerifyProfileSyncInteractionAsync(f));

    // Exercise the shipped WPF handlers on a Dispatcher. No visible window or real profile is opened.
    private static async Task VerifyProfileSyncInteractionAsync(ContractFixture f)
    {
        await VerifyOperationSyncPolicyAsync(f);
        await VerifyRaidStateMaintenanceSyncAsync(f);
        await VerifyInitialProfileLoadAsync(Path.GetFullPath(Path.Combine(f.RunRoot, "..", "..", "..")));
        var window = new MainWindow();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Get(string name) => typeof(MainWindow).GetField(name, flags)!.GetValue(window);
        void Set(string name, object? value) => typeof(MainWindow).GetField(name, flags)!.SetValue(window, value);
        object? Call(string name, params object?[] args)
        {
            var method = typeof(MainWindow).GetMethod(name, flags)!;
            return method.Invoke(window, method.GetParameters().Select((parameter, index) =>
                index < args.Length ? args[index] : parameter.DefaultValue).ToArray());
        }
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
            await (Task)Call("StartProfileSyncAsync", content, items, f.Codec, f.GameRoot, f.WorkshopRoot, f.AdditionalLocalModDirectory, fingerprint, locations)!;
            await Settled();
            ((DispatcherTimer)Get("_gameProcessPoll")!).Stop();
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
                Call("RequestProfileSync", false); // Coalesced explicit requests must preserve the same snapshot.
                Assert(Flag("_syncInProgress") && Flag("_syncReady") && ((Control)Get("BattleMapPanel")!).IsEnabled &&
                    ((Button)Get("ApplyButton")!).IsEnabled && ReferenceEquals(Get("_preparedQuantityItemEdit"), prepared),
                    "An unchanged explicit reader check must not invalidate a prepared edit.");
                if (i == 1) { Call("SetBusy", true); Call("SetBusy", false); } // An operation finished while the check was waiting.
                gate.Release(); gate = null;
                await Settled();
                Assert(Equals(Get("_editRevision"), revision) && ReferenceEquals(Get("_preparedQuantityItemEdit"), prepared) &&
                    ((TextBlock)Get("ProfileSyncStatusTextBlock")!).Text == status && ((TextBox)Get("StatusTextBox")!).Text == logs,
                    "Unchanged explicit checks must preserve preview, status and log, including a completed concurrent operation.");
            }
            Console.WriteLine("PASS: WPF coalesced explicit reads preserve previews and emit no repeated sync status.");

            foreach (var mapOperation in new[] { false, true })
            {
                // Block the reader after the write has ended, including an already
                // running reader check. Controls must not briefly become live.
                gate = ReaderGate();
                await gate.WaitAsync();
                Call("RequestProfileSync", false);
                if (mapOperation) await (Task)Call("BattleMapPanel_SaveEditBusyChanged", true)!;
                else Call("SetBusy", true);
                var completion = (Task)(mapOperation
                    ? Call("BattleMapPanel_SaveEditBusyChanged", false) : Call("CompleteProfileEditAsync"))!;
                Assert(!completion.IsCompleted && Flag("_completingProfileEdit") && !Flag("_syncReady") &&
                    !((Control)Get("BattleMapPanel")!).IsEnabled && !((TabControl)Get("CatalogTabs")!).IsEnabled &&
                    !((Button)Get("ApplyButton")!).IsEnabled && !((Button)Get("PreviewButton")!).IsEnabled,
                    "Inventory and map completion must await full sync under an uninterrupted UI lock.");
                gate.Release(); gate = null;
                await completion;
                await Settled();
                Assert(!Flag("_completingProfileEdit") && Flag("_syncReady") && ((TabControl)Get("CatalogTabs")!).IsEnabled &&
                    Get("_preparedQuantityItemEdit") is null,
                    "Successful completion must publish sync before restoring controls and consume the old preview.");
            }
            using (var lockedSave = new FileStream(f.EstatePath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Call("SetBusy", true);
                await (Task)Call("CompleteProfileEditAsync")!;
                Assert(!Flag("_completingProfileEdit") && !Flag("_syncReady") &&
                    !((Control)Get("BattleMapPanel")!).IsEnabled && !((Button)Get("PreviewButton")!).IsEnabled &&
                    !((Button)Get("ApplyButton")!).IsEnabled && ((FrameworkElement)Get("PathInputsBorder")!).IsEnabled,
                    "A failed post-write sync must keep editing disabled while allowing reload/recovery.");
            }
            Call("RequestProfileSync", false); await Settled();
            Set("_preparedQuantityItemEdit", prepared); Call("UpdateEnabledState");
            Console.WriteLine("PASS: WPF inventory/map completion awaits sync, consumes previews and recovers after sync failure.");

            Set("_battleMaintenanceDeferred", false);
            Set("_maintenanceRetryPending", false);
            var estateBytes = File.ReadAllBytes(f.EstatePath);
            var estateCopy = Path.Combine(f.RunRoot, "sync-ui-estate.decoded.json");
            await f.Codec.DecodeAsync(f.EstatePath, estateCopy);
            var gold = ((IReadOnlyList<QuantityItemDefinition>)Get("_allItems")!).Single(item => item.DisplayId == "gold");
            var estate = QuantityItemSaveEditor.SetAmount(JsonNode.Parse(File.ReadAllText(estateCopy))!.AsObject(), gold, 4321).UpdatedRoot;
            var battleProbe = WriteMultiMash(f.GameRoot, "dungeons/cove/quantity-only-ui.1.mash.darkest",
                "hall: .chance 1 .types sync_quantity_monster\n");
            using (var lockedBattle = File.Open(battleProbe, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                File.WriteAllText(f.EstatePath, estate.ToJsonString());
                Call("RequestProfileSync", false);
                while (Flag("_syncInProgress")) await Task.Delay(15);
                Assert(Flag("_syncReady") && !Flag("_syncRequested") && !Flag("_maintenanceRetryPending") &&
                    ((IReadOnlyList<QuantityItemDefinition>)Get("_allItems")!).Single(item => item.DisplayId == "gold").CurrentAmount == 4321,
                    "A quantity-only save change must publish amounts without inspecting unreadable, unrelated battle resources.");
            }
            File.Delete(battleProbe);
            File.WriteAllBytes(f.EstatePath, estateBytes);
            Call("RequestProfileSync", false); await Settled();
            Assert(!Flag("_maintenanceRetryPending"), "Restoring quantity-only save bytes must also reuse battle state.");
            Set("_preparedQuantityItemEdit", prepared);
            Call("UpdateEnabledState");
            Console.WriteLine("PASS: WPF quantity-only saves refresh amounts without repeated battle maintenance.");

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
            Set("_preparedQuantityItemEdit", prepared);
            Call("CheckGameProcess"); await Settled();
            Assert(!disabledDuringChange && ReferenceEquals(Get("_preparedQuantityItemEdit"), prepared) &&
                !((IReadOnlyList<QuantityItemDefinition>)Get("_allItems")!).Any(item => item.ItemId == "sync_ui_probe"),
                "Idle process/focus checks must preserve previews without scanning changed resource files.");
            Call("RequestProfileSync", false, true); await Settled();
            Assert(disabledDuringChange && Flag("_syncReady") && ((IReadOnlyList<QuantityItemDefinition>)Get("_allItems")!).Any(item => item.ItemId == "sync_ui_probe") &&
                ((TextBox)Get("CopiesTextBox")!).Text == "17", "Actual resource updates publish exclusively and preserve user input.");
            File.Delete(hot);
            gate = ReaderGate(); await gate.WaitAsync();
            Call("RequestProfileSync", false);
            Call("RequestProfileSync", false, true);
            gate.Release(); gate = null; await Settled();
            Assert(!((IReadOnlyList<QuantityItemDefinition>)Get("_allItems")!).Any(item => item.ItemId == "sync_ui_probe") &&
                !Flag("_contentRefreshRequested"),
                "An explicit resource refresh queued during a save check must survive coalescing.");
            Console.WriteLine("PASS: WPF resources refresh only on explicit checks; queued refreshes preserve user input.");

            hot = WriteMultiMash(f.GameRoot, "inventory/sync-ui.inventory.items.darkest",
                "inventory_item: .type estate .id sync_ui_retry .base_stack_limit 8\n");
            using (var lockedResource = File.Open(hot, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Call("RequestProfileSync", false, true);
                while (Flag("_syncInProgress")) await Task.Delay(15);
                Assert(!Flag("_syncReady") && Flag("_contentRefreshRequested") && !Flag("_syncRequested"),
                    "A failed resource check must retain refresh intent without scheduling a background retry.");
            }
            Call("RequestProfileSync", false); await Settled();
            Assert(Flag("_syncReady") && ((IReadOnlyList<QuantityItemDefinition>)Get("_allItems")!).Any(item => item.ItemId == "sync_ui_retry"),
                "The next explicit request must finish a previously failed resource refresh.");
            File.Delete(hot);
            Call("RequestProfileSync", false, true); await Settled();
            Console.WriteLine("PASS: WPF resource failures retain full-refresh intent and recover on explicit retry.");

            var bytes = File.ReadAllBytes(f.GameSavePath);
            File.WriteAllText(f.GameSavePath, "{broken");
            Call("RequestProfileSync", false);
            while (Flag("_syncInProgress")) await Task.Delay(15);
            Assert(!Flag("_syncReady") && !panel.IsEnabled && Get("_lastSyncError") is not null,
                "Partial saves must pause writes until explicit recovery instead of publishing a partial catalog.");
            File.WriteAllBytes(f.GameSavePath, bytes);
            Call("RequestProfileSync", false); await Settled();
            Assert(Flag("_syncReady") && Get("_lastSyncError") is null, "Stable save recovery must restore interaction.");

            gate = ReaderGate(); await gate.WaitAsync();
            Call("RequestProfileSync", false);
            var latest = (ActiveContentSnapshot)Get("_activeContentSnapshot")!;
            var latestItems = await QuantityItemCatalog.LoadAsync(latest, f.Codec);
            Call("StopProfileSync");
            var restart = (Task)Call("StartProfileSyncAsync", latest, latestItems, f.Codec, f.GameRoot, f.WorkshopRoot,
                f.AdditionalLocalModDirectory, ProfileCatalogContentFingerprint.Capture(latest.Sources), locations)!;
            gate.Release(); gate = null; await restart; await Settled();
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
    private static async Task VerifyRaidStateMaintenanceSyncAsync(ContractFixture f)
    {
        var root = Path.Combine(f.RunRoot, "sync-deferred-raid");
        var game = Path.Combine(root, "game");
        var mods = Path.Combine(game, "mods");
        Directory.CreateDirectory(mods);
        var profile = WriteBattleSafetyProfile(Path.Combine(root, "profile"));
        foreach (var name in new[] { "persist.estate.json", "persist.roster.json", "persist.town.json", "persist.upgrades.json" })
            File.WriteAllText(Path.Combine(profile.ProfileDirectory, name), "{\"base_root\":{}}");
        var map = JsonNode.Parse(File.ReadAllText(profile.MapSavePath))!;
        map["base_root"]!["map"]!["final_room_id"] = 250;
        File.WriteAllText(profile.MapSavePath, map.ToJsonString());
        var raid = JsonNode.Parse(File.ReadAllText(profile.RaidSavePath))!;
        raid["base_root"]!["start_elapsed_time"] = 100;
        raid["base_root"]!["raid_instance"]!["id"] = "sync-deferred-raid";
        raid["base_root"]!["party"]!["inventory"] = JsonNode.Parse("{\"items\":{\"0\":{\"id\":\"\",\"type\":\"gold\",\"amount\":100}}}");
        File.WriteAllText(profile.RaidSavePath, raid.ToJsonString());
        WriteMultiMash(game, "inventory/base.inventory.items.darkest", "inventory_item: .type gold .id \"\" .base_stack_limit 1750\n");
        CreateBattleMonsterDefinitions(game, ["native", "added"]);
        var table = WriteMultiMash(game, "dungeons/cove/cove.2.mash.darkest", "room: .chance 1 .types native\n");
        var locations = new SaveEditorLocations(root, Path.Combine(root, "workspaces"), Path.Combine(root, "backups"));
        var content = await ActiveContentResolver.ResolveAsync(profile, game, null, mods, f.Codec, locations.WorkspaceDirectory);
        var snapshot = await new BattleMapSnapshotReader(f.Codec).LoadAsync(profile.ProfileDirectory);
        var encounter = BattleEncounterCatalog.Load(content, snapshot).DirectEncounters.Single(item => item.MashType == 1);
        var edits = new BattleMapEditService(f.Codec, locations);
        await edits.CommitAsync(await edits.PreparePlaceBattleAsync(profile, snapshot, "rooB", "tile0", encounter));
        File.WriteAllText(table, "room: .chance 1 .types added\nroom: .chance 1 .types native\n");
        raid["base_root"]!["inbattle"] = true;
        File.WriteAllText(profile.RaidSavePath, raid.ToJsonString());
        var originalMapHash = ComputeSha256(profile.MapSavePath);
        var originalRaidHash = ComputeSha256(profile.RaidSavePath);
        var items = await QuantityItemCatalog.LoadAsync(content, f.Codec);
        var window = new MainWindow();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Get(string name) => typeof(MainWindow).GetField(name, flags)!.GetValue(window);
        void Set(string name, object? value) => typeof(MainWindow).GetField(name, flags)!.SetValue(window, value);
        object? Call(string name, params object?[] args)
        {
            var method = typeof(MainWindow).GetMethod(name, flags)!;
            return method.Invoke(window, method.GetParameters().Select((parameter, index) =>
                index < args.Length ? args[index] : parameter.DefaultValue).ToArray());
        }
        Set("_activeContentSnapshot", content);
        Set("_allItems", items.Items);
        Set("_quantitySaveContext", items.SaveContext);
        try
        {
            await (Task)Call("StartProfileSyncAsync", content, items, f.Codec, game, null, mods,
                ProfileCatalogContentFingerprint.Capture(content.Sources), locations)!;
            ((DispatcherTimer)Get("_gameProcessPoll")!).Stop();
            Assert((bool)Get("_syncReady")! && !(bool)Get("_maintenanceRetryPending")! && !(bool)Get("_battleMaintenanceDeferred")!,
                "Saved combat must defer cleanup without registering an error or game-exit retry.");
            var revision = Get("_editRevision");
            var status = ((TextBlock)Get("ProfileSyncStatusTextBlock")!).Text;
            var logs = ((TextBox)Get("StatusTextBox")!).Text;
            var disabled = false;
            var tabs = (TabControl)Get("CatalogTabs")!;
            tabs.IsEnabledChanged += (_, _) => { if (!tabs.IsEnabled) disabled = true; };
            for (var attempt = 0; attempt < 3; attempt++)
            {
                Call("RequestProfileSync", false, attempt == 2);
                await (Task)Call("DrainProfileSyncAsync")!;
                Assert(!disabled && (bool)Get("_syncReady")! && !(bool)Get("_syncRequested")! &&
                    Equals(revision, Get("_editRevision")) && ((TextBlock)Get("ProfileSyncStatusTextBlock")!).Text == status &&
                    ((TextBox)Get("StatusTextBox")!).Text == logs && ComputeSha256(profile.MapSavePath) == originalMapHash &&
                    ComputeSha256(profile.RaidSavePath) == originalRaidHash,
                    "Repeated explicit checks during saved combat must preserve controls, preview revision, status, logs and saves.");
            }
            raid["base_root"]!["inbattle"] = false;
            File.WriteAllText(profile.RaidSavePath, raid.ToJsonString());
            Call("RequestProfileSync", false);
            await (Task)Call("DrainProfileSyncAsync")!;
            Assert((bool)Get("_syncReady")! && !(bool)Get("_maintenanceRetryPending")! &&
                ComputeSha256(profile.MapSavePath) != originalMapHash &&
                (await new BattleMapSnapshotReader(f.Codec).LoadAsync(profile.ProfileDirectory)).Areas
                    .Single(area => area.AreaId == "rooB").Tiles.Single().MashIndex == -1,
                "A new stationary raid save must automatically resume and finish the deferred cleanup.");
            Console.WriteLine("PASS: WPF saved-combat cleanup deferral does not loop or disable controls; a changed raid resumes maintenance.");
        }
        finally
        {
            Call("StopProfileSync");
            window.Close();
        }
    }
}
