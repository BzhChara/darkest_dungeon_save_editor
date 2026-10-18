using System.Reflection;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DarkestDungeonSaveEditor.App;
using DarkestDungeonSaveEditor.Core;

internal static partial class ContractSuite
{
    public static async Task RunOperationSyncOnlyAsync(string repositoryRoot)
    {
        UseNativeCodec = true;
        try
        {
            var fixture = BuildContractFixture(repositoryRoot);
            await SeedSyncFixtureAsync(fixture);
            await RunWpfContractsAsync(() => VerifyOperationSyncPolicyAsync(fixture));
            Console.WriteLine($"Artifacts: {fixture.RunRoot}");
        }
        finally { UseNativeCodec = false; }
    }

    private static async Task VerifyOperationSyncPolicyAsync(ContractFixture f)
    {
        var running = false;
        var locations = new SaveEditorLocations(f.RunRoot, Path.Combine(f.RunRoot, "operation-sync-work"),
            Path.Combine(f.RunRoot, "operation-sync-backups"));
        var window = new MainWindow(Path.Combine(f.RunRoot, "operation-language.json"), saveLocations: locations,
            gameRunningProbe: () => running);
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
        bool Enabled(string name) => ((FrameworkElement)Get(name)!).IsEnabled;
        long Gold() => ((IReadOnlyList<QuantityItemDefinition>)Get("_allItems")!)
            .Single(item => item.DisplayId == "gold").CurrentAmount;
        async Task Settled()
        {
            var deadline = DateTime.UtcNow.AddSeconds(60);
            while (Flag("_syncInProgress") || Flag("_syncRequested") || Flag("_checkingProfileOperation") || Flag("_completingProfileEdit"))
            {
                if (DateTime.UtcNow > deadline) throw new TimeoutException("Operation sync did not settle: " + Get("_lastSyncError"));
                await Task.Delay(15);
            }
        }
        SemaphoreSlim ReaderGate() => (SemaphoreSlim)typeof(ProfileCatalogSnapshotReader).GetField("_gate", flags)!
            .GetValue(Get("_catalogSnapshotReader"))!;
        var estateBytes = File.ReadAllBytes(f.EstatePath);
        var decoded = Path.Combine(f.RunRoot, "operation-sync-estate.json");
        await f.Codec.DecodeAsync(f.EstatePath, decoded);
        var estate = JsonNode.Parse(File.ReadAllText(decoded))!.AsObject();
        var gate = (SemaphoreSlim?)null;
        string? extraDefinition = null;
        try
        {
            ((DispatcherTimer)Get("_gameProcessPoll")!).Stop();
            ((TextBox)Get("ProfileDirectoryTextBox")!).Text = f.ProfileRoot;
            ((TextBox)Get("GameDirectoryTextBox")!).Text = f.GameRoot;
            var profile = new SaveProfile("operation_sync", f.ProfileRoot, f.EstatePath, "contract", DateTime.UtcNow);
            var content = await ActiveContentResolver.ResolveAsync(profile, f.GameRoot, f.WorkshopRoot,
                f.AdditionalLocalModDirectory, f.Codec, f.RunRoot);
            var items = await QuantityItemCatalog.LoadAsync(content, f.Codec);
            Set("_activeContentSnapshot", content);
            Set("_allItems", items.Items);
            Set("_quantitySaveContext", items.SaveContext);
            await (Task)Call("StartProfileSyncAsync", content, items, f.Codec, f.GameRoot, f.WorkshopRoot,
                f.AdditionalLocalModDirectory, ProfileCatalogContentFingerprint.Capture(content.Sources), locations)!;
            var gold = items.Items.Single(item => item.DisplayId == "gold");
            void SetGold(int amount) => File.WriteAllText(f.EstatePath,
                QuantityItemSaveEditor.SetAmount(estate, gold, amount).UpdatedRoot.ToJsonString());
            var initialGold = Gold();
            SetGold(4321);
            gate = ReaderGate(); await gate.WaitAsync();
            // A file notification, idle tick or focus change must not start a reader.
            Call("CheckGameProcess");
            Call("OnActivated", EventArgs.Empty);
            await Task.Delay(650);
            Assert(!Flag("_syncInProgress") && !Flag("_syncRequested") && Flag("_syncReady") && Gold() == initialGold,
                "Idle/focus/file changes must leave the loaded view unchanged without background save reads.");
            var check = (Task<bool>)Call("CheckProfileBeforeOperationAsync")!;
            Assert(!check.IsCompleted && Flag("_checkingProfileOperation") && !Enabled("CatalogTabs") && !Enabled("ApplyButton"),
                "A preview check must keep controls locked until its fresh read completes.");
            gate.Release(); gate = null;
            Assert(await check && Gold() == 4321 && Flag("_syncReady"),
                "An explicit preview check must publish intervening save changes before editing.");

            ((TabControl)Get("CatalogTabs")!).SelectedIndex = 0;
            Call("ApplyFilter");
            var grid = (DataGrid)Get("ItemGrid")!;
            grid.SelectedItem = grid.Items.Cast<object>().Single(row =>
                ((QuantityItemDefinition)row.GetType().GetProperty("Definition")!.GetValue(row)!).DisplayId == "gold");
            ((TextBox)Get("CopiesTextBox")!).Text = "4500";
            SetGold(4400);
            Call("Preview_Click", window, new RoutedEventArgs());
            var deadline = DateTime.UtcNow.AddSeconds(60);
            while (Get("_preparedQuantityItemEdit") is null && DateTime.UtcNow < deadline)
            {
                if (!Flag("_checkingProfileOperation") && (int)Get("_busyDepth")! == 0) break;
                await Task.Delay(15);
            }
            Assert(Get("_preparedQuantityItemEdit") is PreparedQuantityItemEdit actualPreview && actualPreview.Preview.ExistingAmount == 4400,
                "The actual Preview button must use the latest saved quantity. " + ((TextBox)Get("StatusTextBox")!).Text);
            var beforeRejectedWrite = ComputeSha256(f.EstatePath);
            extraDefinition = WriteMultiMash(f.GameRoot, "inventory/operation-sync.inventory.items.darkest",
                "inventory_item: .type estate .id operation_sync_item .base_stack_limit 3\n");
            Assert(!await (Task<bool>)Call("CheckProfileBeforeOperationAsync", true)! &&
                Get("_preparedQuantityItemEdit") is null && !Enabled("ApplyButton") &&
                ComputeSha256(f.EstatePath) == beforeRejectedWrite,
                "A pre-write content change must refresh, consume the old preview and reject writing.");
            var panel = (BattleMapView)Get("BattleMapPanel")!;
            var mapCheck = typeof(BattleMapView).GetMethod("CheckProfileEditAsync", flags)!;
            var mapBrowse = typeof(BattleMapView).GetMethod("CanBrowseProfile", flags)!;
            File.AppendAllText(extraDefinition, "inventory_item: .type estate .id operation_map_item .base_stack_limit 4\n");
            gate = ReaderGate(); await gate.WaitAsync();
            var browseRevision = Get("_editRevision");
            for (var i = 0; i < 4; i++)
                Assert((bool)mapBrowse.Invoke(panel, null)!, "Browsing must remain available from the loaded snapshot.");
            Assert(!Flag("_syncInProgress") && !Flag("_syncRequested") && Equals(browseRevision, Get("_editRevision")),
                "Battle browsing must not read the blocked reader or refresh externally changed content.");
            gate.Release(); gate = null;
            Assert(!await (Task<bool>)mapCheck.Invoke(panel, null)!,
                "Final map write checks must reject a changed content snapshot before using old candidates.");
            Assert(await (Task<bool>)mapCheck.Invoke(panel, null)!, "The next map check can proceed with the refreshed snapshot.");
            Console.WriteLine("PASS: WPF idle/focus checks do not read saves; preview and map/apply checks refresh and reject stale contexts.");

            running = true;
            Assert(!(bool)mapBrowse.Invoke(panel, null)!,
                "Opening a battle menu must catch a game launch before the process timer ticks.");
            Assert(!Flag("_syncReady") && !Enabled("PreviewButton") && !Enabled("ApplyButton") &&
                !Enabled("BattleMapPanel") && !Enabled("PathInputsBorder") && Get("_preparedQuantityItemEdit") is null,
                "Starting the game must invalidate previews and block every write/load entry.");
            var previousGold = Gold();
            SetGold(4600);
            Call("RequestProfileSync", true, true);
            Assert(!Flag("_syncInProgress") && !Flag("_syncRequested") && Gold() == previousGold &&
                !await (Task<bool>)mapCheck.Invoke(panel, null)!,
                "Running-game checks must not read saves or permit map edits.");
            gate = ReaderGate(); await gate.WaitAsync();
            running = false;
            Call("CheckGameProcess");
            Assert(Flag("_syncInProgress") && !Flag("_syncReady") && !Enabled("ApplyButton") && !Enabled("BattleMapPanel"),
                "Game exit must not re-enable edits before stable-file synchronization completes.");
            gate.Release(); gate = null; await Settled();
            Assert(Flag("_syncReady") && Gold() == 4600 && Enabled("BattleMapPanel"),
                "Game exit must publish the final save before restoring edit controls.");
            var exitRevision = Get("_editRevision");
            using (var locked = File.Open(f.EstatePath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                for (var i = 0; i < 3; i++) Call("CheckGameProcess");
                Assert(!Flag("_syncInProgress") && Flag("_syncReady") && Equals(exitRevision, Get("_editRevision")),
                    "Subsequent idle process checks must not repeat the exit synchronization.");
            }

            running = true; Call("CheckGameProcess");
            using (var locked = File.Open(f.EstatePath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                running = false; Call("CheckGameProcess"); await Settled();
                Assert(!Flag("_syncReady") && !Flag("_syncRequested") && Enabled("PathInputsBorder") &&
                    !Enabled("ApplyButton") && !Enabled("BattleMapPanel"),
                    "A failed exit sync must stay locked with reload available and no scheduled retry.");
            }
            Call("CheckGameProcess"); await Task.Delay(2200);
            Assert(!Flag("_syncInProgress") && !Flag("_syncReady") && !Flag("_syncRequested"),
                "Releasing a failed-sync input must not restart synchronization in the background.");
            Call("RequestProfileSync", true, true); await Settled();
            Assert(Flag("_syncReady"), "An explicit retry must recover after the save lock is released.");

            gate = ReaderGate(); await gate.WaitAsync();
            check = (Task<bool>)Call("CheckProfileBeforeOperationAsync")!;
            running = true; Call("CheckGameProcess");
            gate.Release(); gate = null;
            Assert(!await check && !Flag("_syncReady") && !Enabled("ApplyButton") && !Enabled("BattleMapPanel"),
                "A game launched during an awaited check must not publish an editable snapshot.");
            running = false; Call("CheckGameProcess"); await Settled();
            running = true; // The next click must catch a launch before the process timer ticks.
            Assert(!await (Task<bool>)Call("CheckProfileBeforeOperationAsync", true)! && !Enabled("ApplyButton"),
                "Operation entry must inspect the process again instead of trusting the previous timer tick.");
            Console.WriteLine("PASS: WPF game start/exit, one-shot final sync, failure recovery and launch-during-check keep edits guarded.");
        }
        finally
        {
            gate?.Release();
            Call("StopProfileSync");
            window.Close();
            File.WriteAllBytes(f.EstatePath, estateBytes);
            if (extraDefinition is not null) File.Delete(extraDefinition);
        }
        await VerifySceneTransitionsAsync(f);
    }
}
