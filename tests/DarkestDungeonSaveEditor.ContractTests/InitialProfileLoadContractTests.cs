using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DarkestDungeonSaveEditor.App;
using DarkestDungeonSaveEditor.Core;

internal static partial class ContractSuite
{
    // Run on the existing WPF host, using separate files so manifest preparation
    // and deliberately interrupted reads cannot alter the other sync fixtures.
    private static async Task VerifyInitialProfileLoadAsync(string repositoryRoot)
    {
        var f = BuildContractFixture(repositoryRoot);
        await SeedSyncFixtureAsync(f);
        var locations = new SaveEditorLocations(f.RunRoot, Path.Combine(f.RunRoot, "load-work"),
            Path.Combine(f.RunRoot, "load-backups"));
        await VerifyReloadWaitsForPreviousSyncCleanupAsync(locations);
        foreach (var scenario in new[] { "success", "failure", "close" })
        {
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
            var paths = (FrameworkElement)Get("PathInputsBorder")!;
            var tabs = (TabControl)Get("CatalogTabs")!;
            var syncStatus = (TextBlock)Get("ProfileSyncStatusTextBlock")!;
            var log = (TextBox)Get("StatusTextBox")!;
            ((TextBox)Get("GameDirectoryTextBox")!).Text = f.GameRoot;
            ((TextBox)Get("WorkshopDirectoryTextBox")!).Text = f.WorkshopRoot;
            ((TextBox)Get("LocalModDirectoryTextBox")!).Text = f.AdditionalLocalModDirectory;
            ((TextBox)Get("ProfileDirectoryTextBox")!).Text = f.ProfileRoot;

            SemaphoreSlim? heldGate = null;
            var intercepted = false;
            var prematureEnable = false;
            var closed = false;
            window.Closed += (_, _) => closed = true;
            paths.IsEnabledChanged += (_, _) => prematureEnable |= paths.IsEnabled && Flag("_catalogLoading");
            tabs.IsEnabledChanged += (_, _) => prematureEnable |= tabs.IsEnabled && Flag("_catalogLoading");
            var descriptor = DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock));
            EventHandler holdInitialRead = (_, _) =>
            {
                if (intercepted || syncStatus.Text != "正在完成首次同步…") return;
                intercepted = true;
                heldGate = (SemaphoreSlim)typeof(ProfileCatalogSnapshotReader).GetField("_gate", flags)!
                    .GetValue(Get("_catalogSnapshotReader"))!;
                Assert(heldGate.Wait(0), "The first read must not start before its loading status is published.");
            };
            descriptor.AddValueChanged(syncStatus, holdInitialRead);
            Task? load = null;
            try
            {
                load = (Task)Call("LoadCatalogAsync", locations)!;
                // The click handler owns the same task; retain that relationship
                // so the real OnClosing path must await initial-sync cleanup.
                Set("_catalogLoadTask", load);
                var deadline = DateTime.UtcNow.AddSeconds(60);
                while (!intercepted && !load.IsCompleted && DateTime.UtcNow < deadline) await Task.Delay(15);
                Assert(intercepted && !load.IsCompleted && Flag("_syncInProgress") &&
                    Flag("_catalogLoading") && !paths.IsEnabled && !tabs.IsEnabled &&
                    !((Control)Get("BattleMapPanel")!).IsEnabled && !log.Text.Contains("当前存档载入完成"),
                    "Loading must remain pending and controls disabled throughout the initial snapshot read. " + log.Text);
                Assert(!((CancellationTokenSource)Get("_catalogLoadCancellation")!).IsCancellationRequested,
                    "Starting the first sync must not cancel its own catalog load.");

                if (scenario == "success")
                {
                    // A save written between scanning and first sync must be in
                    // the snapshot users receive when loading reports success.
                    var decoded = Path.Combine(f.RunRoot, "load-estate.decoded.json");
                    await f.Codec.DecodeAsync(f.EstatePath, decoded);
                    var gold = ((IReadOnlyList<QuantityItemDefinition>)Get("_allItems")!).Single(item => item.DisplayId == "gold");
                    var updated = QuantityItemSaveEditor.SetAmount(JsonNode.Parse(File.ReadAllText(decoded))!.AsObject(),
                        gold, 4567).UpdatedRoot;
                    File.WriteAllText(f.EstatePath, updated.ToJsonString());
                    heldGate!.Release(); heldGate = null;
                    await load.WaitAsync(TimeSpan.FromSeconds(60));
                    Assert(!Flag("_catalogLoading") && !Flag("_syncInProgress") && Flag("_syncReady") &&
                        paths.IsEnabled && tabs.IsEnabled && !prematureEnable &&
                        ((IReadOnlyList<QuantityItemDefinition>)Get("_allItems")!).Single(item => item.DisplayId == "gold").CurrentAmount == 4567 &&
                        log.Text.Contains("当前存档载入完成，首次同步已完成。"),
                        "Successful loading must include the fresh initial snapshot before enabling the screen.");
                    Console.WriteLine("PASS: WPF catalog loading includes initial synchronization and intervening save changes without enabling controls early.");
                }
                else if (scenario == "failure")
                {
                    using (var lockedSave = File.Open(f.EstatePath, FileMode.Open, FileAccess.Read, FileShare.None))
                    {
                        heldGate!.Release(); heldGate = null;
                        await load.WaitAsync(TimeSpan.FromSeconds(60));
                        ((DispatcherTimer)Get("_catalogSyncRetry")!).Stop();
                        Assert(!Flag("_catalogLoading") && !Flag("_syncReady") && paths.IsEnabled &&
                            !((Control)Get("BattleMapPanel")!).IsEnabled && !((Button)Get("ApplyButton")!).IsEnabled &&
                            !log.Text.Contains("当前存档载入完成") && log.Text.Contains("首次同步未完成"),
                            "Initial-sync failure must restore retry controls without announcing success or enabling writes.");
                    }
                    Call("RequestProfileSync", false);
                    await ((Task)Get("_catalogSyncTask")!).WaitAsync(TimeSpan.FromSeconds(60));
                    Assert(Flag("_syncReady") && Get("_lastSyncError") is null && paths.IsEnabled,
                        "Stable save recovery must remain available after the load reported an initial-sync failure.");
                    Console.WriteLine("PASS: WPF initial-sync failure exposes retry, keeps writes guarded and recovers after the save is unlocked.");
                }
                else
                {
                    window.Close();
                    await load.WaitAsync(TimeSpan.FromSeconds(60));
                    deadline = DateTime.UtcNow.AddSeconds(10);
                    while (!closed && DateTime.UtcNow < deadline) await Task.Delay(15);
                    Assert(closed && !Flag("_catalogLoading") && !Flag("_syncInProgress") && !Flag("_syncReady") &&
                        !log.Text.Contains("当前存档载入完成"),
                        "Closing during initial synchronization must cancel and await the worker without false success or locked lifecycle state.");
                    heldGate!.Release(); heldGate = null;
                    Console.WriteLine("PASS: WPF closing during initial synchronization awaits cancellation and leaves no running sync worker.");
                }
            }
            finally
            {
                descriptor.RemoveValueChanged(syncStatus, holdInitialRead);
                Call("StopProfileSync");
                heldGate?.Release();
                if (load is not null) await load.WaitAsync(TimeSpan.FromSeconds(60));
                if (Get("_catalogSyncTask") is Task sync) await sync.WaitAsync(TimeSpan.FromSeconds(60));
                Set("_catalogLoadTask", null);
                if (!closed) window.Close();
            }
        }
    }

    private static async Task VerifyReloadWaitsForPreviousSyncCleanupAsync(SaveEditorLocations locations)
    {
        var window = new MainWindow();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        void Set(string name, object? value) => typeof(MainWindow).GetField(name, flags)!.SetValue(window, value);
        object? Get(string name) => typeof(MainWindow).GetField(name, flags)!.GetValue(window);
        ((TextBox)Get("GameDirectoryTextBox")!).Text = Path.Combine(locations.ApplicationDataDirectory, "not-a-game-directory");
        // Model a reader whose cancellation cleanup outlives Cancel(), as with
        // waiting for a codec process and its redirected output pipes to exit.
        var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        var releaseCleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task PreviousSyncAsync()
        {
            try { await Task.Delay(Timeout.Infinite, token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            await releaseCleanup.Task;
            Set("_syncInProgress", false);
        }
        Set("_catalogSyncCancellation", cancellation);
        Set("_syncInProgress", true);
        var previousSync = PreviousSyncAsync();
        Set("_catalogSyncTask", previousSync);
        var closed = false;
        window.Closed += (_, _) => closed = true;
        Task? load = null;
        try
        {
            load = (Task)typeof(MainWindow).GetMethod("LoadCatalogAsync", flags)!.Invoke(window, new object?[] { locations })!;
            Set("_catalogLoadTask", load);
            Assert(token.IsCancellationRequested && !previousSync.IsCompleted && !load.IsCompleted &&
                !((FrameworkElement)Get("PathInputsBorder")!).IsEnabled,
                "Reload must await old cancellation cleanup before validating paths or starting a new scan.");
            window.Close();
            await Task.Delay(30);
            Assert(!closed && !load.IsCompleted && !previousSync.IsCompleted,
                "Close during reload must keep the Dispatcher alive until the previous reader finishes cleanup.");
            releaseCleanup.SetResult();
            await load.WaitAsync(TimeSpan.FromSeconds(60));
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!closed && DateTime.UtcNow < deadline) await Task.Delay(15);
            Assert(closed && previousSync.IsCompletedSuccessfully && !(bool)Get("_syncInProgress")! &&
                !(bool)Get("_catalogLoading")! && !((TextBox)Get("StatusTextBox")!).Text.Contains("当前存档载入完成"),
                "Cancelled reload must finish old cleanup before closing, without reporting load success.");
            Console.WriteLine("PASS: WPF reload and close await the previous background reader's cancellation cleanup, including before path validation.");
        }
        finally
        {
            if (!token.IsCancellationRequested) cancellation.Cancel();
            releaseCleanup.TrySetResult();
            await previousSync;
            if (load is not null) await load;
            Set("_catalogLoadTask", null);
            if (!closed) window.Close();
            cancellation.Dispose();
        }
    }
}
