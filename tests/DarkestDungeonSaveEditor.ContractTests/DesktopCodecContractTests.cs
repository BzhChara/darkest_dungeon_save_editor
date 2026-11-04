using System.Reflection;
using System.Windows.Controls;
using System.Windows.Threading;
using DarkestDungeonSaveEditor.App;

internal static partial class ContractSuite
{
    public static async Task RunDesktopCodecOnlyAsync(string repositoryRoot)
    {
        var previousCodec = UseNativeCodec;
        UseNativeCodec = true;
        try
        {
            var fixture = BuildContractFixture(repositoryRoot);
            await SeedSyncFixtureAsync(fixture);
            VerifyDesktopCodecSelection(fixture.RunRoot);
            VerifyEditorLocalizationResources(repositoryRoot, fixture.RunRoot);
            await RunWpfContractsAsync(async () =>
            {
                await VerifyEditorLocalizationUiAsync(repositoryRoot, fixture.RunRoot);
                VerifyDesktopJavaReferenceTitle(fixture.RunRoot);
                await VerifyDesktopNativeWorkflowAsync(fixture);
                await VerifyInitialProfileLoadAsync(repositoryRoot);
            });
            Console.WriteLine($"Artifacts: {fixture.RunRoot}");
        }
        finally { UseNativeCodec = previousCodec; }
    }

    private static void VerifyDesktopCodecSelection(string root)
    {
        var noJava = Path.Combine(root, "no-java-installed");
        var automatic = DesktopCodecSession.Create([], noJava);
        automatic.Codec.ValidateAvailability();
        Assert(automatic.IsNative && automatic.Codec.JarPath == string.Empty,
            "Both regular and test builds must default to native without a JAR.");
        var native = DesktopCodecSession.Create(["--codec=native"], noJava);
        native.Codec.ValidateAvailability();
        Assert(native.IsNative && native.Codec.JarPath == string.Empty, "Native startup must not require a JAR.");
        var java = DesktopCodecSession.Create(["--codec=java"], noJava);
        Assert(!java.IsNative && java.Codec.JarPath == Path.Combine(noJava, "tools", "DDSaveEditor", "DDSaveEditor.jar"),
            "Java must be selected explicitly without silently falling back from native.");
        foreach (var arguments in new[] { new[] { "--codec=typo" }, new[] { "--codec" },
            new[] { "--codec=native", "--codec=java" } })
        {
            try { DesktopCodecSession.Create(arguments, root); throw new InvalidOperationException("Invalid codec selection was accepted."); }
            catch (ArgumentException) { }
        }
        Console.WriteLine($"PASS: desktop backend selection, invalid arguments, native without Java; native test build={DesktopCodecSession.IsNativeTestBuild}.");
    }

    private static void VerifyDesktopJavaReferenceTitle(string root)
    {
        var session = DesktopCodecSession.Create(["--codec=java"], Path.Combine(root, "no-java-installed"));
        var window = new MainWindow(Path.Combine(root, "language.json"), session);
        try
        {
            Assert(window.Title.EndsWith(" — " + EditorText.Get("Codec_JavaReferenceTitle"), StringComparison.Ordinal),
                "Explicit Java selection must identify the reference backend in both regular and test builds.");
        }
        finally { window.Close(); }
        Console.WriteLine("PASS: explicit JAR reference window title.");
    }

    private static async Task VerifyDesktopNativeWorkflowAsync(ContractFixture f)
    {
        var session = DesktopCodecSession.Create([], Path.Combine(f.RunRoot, "no-java-installed"));
        var locations = new SaveEditorLocations(f.RunRoot, Path.Combine(f.RunRoot, "desktop-work"), Path.Combine(f.RunRoot, "desktop-backups"));
        var window = new MainWindow(Path.Combine(f.RunRoot, "language.json"), session, locations);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Get(string name) => typeof(MainWindow).GetField(name, flags)!.GetValue(window);
        object? Call(string name, params object?[] args)
        {
            var method = typeof(MainWindow).GetMethod(name, flags)!;
            return method.Invoke(window, method.GetParameters().Select((parameter, index) =>
                index < args.Length ? args[index] : parameter.DefaultValue).ToArray());
        }
        string Log() => ((TextBox)Get("StatusTextBox")!).Text;
        void SelectRow(int tab, string gridName, string id)
        {
            ((TabControl)Get("CatalogTabs")!).SelectedIndex = tab;
            var grid = (DataGrid)Get(gridName)!;
            grid.SelectedItem = grid.Items.Cast<object>().Single(row =>
                (string)row.GetType().GetProperty("Id")!.GetValue(row)! == id);
        }

        async Task PreviewAsync(Action? duringPreparation = null)
        {
            var field = typeof(MainWindow).GetField("_activeContentSnapshot", flags)!;
            var content = (ActiveContentSnapshot)field.GetValue(window)!;
            var dispatcherResponded = false;
            var observed = new PreviewSources(content.Sources, () =>
            {
                if (window.Dispatcher.CheckAccess()) return;
                window.Dispatcher.Invoke(() =>
                {
                    dispatcherResponded = (int)Get("_busyDepth")! > 0 && !((Button)Get("ApplyButton")!).IsEnabled;
                    duringPreparation?.Invoke();
                });
            });
            var instrumented = content with { Sources = observed, Resolution = content.Resolution };
            field.SetValue(window, instrumented);
            try
            {
                var button = (Button)Get("PreviewButton")!;
                Assert(button.IsEnabled, "The selected row must allow preview: " + Log());
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var deadline = DateTime.UtcNow.AddSeconds(60);
                do
                {
                    await Task.Delay(15);
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                } while (((bool)Get("_checkingProfileOperation")! || (int)Get("_busyDepth")! > 0) && DateTime.UtcNow < deadline);
                Assert(!(bool)Get("_checkingProfileOperation")! && (int)Get("_busyDepth")! == 0 && dispatcherResponded,
                    "The dispatcher must respond while the preview reads active sources with Apply disabled: " + Log());
            }
            finally
            {
                if (ReferenceEquals(field.GetValue(window), instrumented)) field.SetValue(window, content);
            }
        }
        try
        {
            ((TextBox)Get("GameDirectoryTextBox")!).Text = f.GameRoot;
            ((TextBox)Get("WorkshopDirectoryTextBox")!).Text = f.WorkshopRoot;
            ((TextBox)Get("LocalModDirectoryTextBox")!).Text = f.LocalModDirectory;
            ((TextBox)Get("ProfileDirectoryTextBox")!).Text = f.ProfileRoot;
            await ((Task)Call("LoadCatalogAsync", new object?[] { null })!).WaitAsync(TimeSpan.FromSeconds(90));
            Assert((bool)Get("_syncReady")! &&
                window.Title.Contains(EditorText.Get("Codec_NativeTestTitle")) == DesktopCodecSession.IsNativeTestBuild &&
                !window.Title.Contains(EditorText.Get("Codec_JavaReferenceTitle")),
                "Default desktop load must complete initial synchronization and use the correct build title: " + Log());
            Assert(ReferenceEquals(typeof(BattleMapView).GetField("_codec", flags)!.GetValue(Get("BattleMapPanel")), session.Codec),
                "The map must receive the window's selected backend.");
            ((DispatcherTimer)Get("_gameProcessPoll")!).Stop();

            SelectRow(1, "TrinketGrid", "focus_ring");
            ((TextBox)Get("CopiesTextBox")!).Text = "2";
            await PreviewAsync();
            Assert(Get("_preparedTrinketEdit") is PreparedTrinketEdit { Preview: { TrinketId: "focus_ring", RequestedCopies: 2 } }
                && ((Button)Get("ApplyButton")!).IsEnabled, "The trinket preview must retain the requested copies: " + Log());

            SelectRow(3, "HeroGrid", "local_hero");
            ((ComboBox)Get("HeroLevelComboBox")!).SelectedIndex = 1;
            typeof(MainWindow).GetField("_selectedInitialQuirkIds", flags)!.SetValue(window, new[] { "tough" });
            await PreviewAsync();
            Assert(Get("_preparedHeroCandidatePreview") is StagecoachHeroCandidatePreview { HeroClass: "local_hero", ResolveLevel: 1 } hero
                && hero.PositiveQuirks.SequenceEqual(["tough"]) && Get("_preparedHeroEdit") is PreparedStagecoachHeroEdit
                && ((Button)Get("ApplyButton")!).IsEnabled, "The hero preview must retain the selected class, level and quirks: " + Log());

            SelectRow(0, "ItemGrid", "gold");
            ((TextBox)Get("CopiesTextBox")!).Text = "4320";
            await PreviewAsync(() => ((TextBox)Get("CopiesTextBox")!).Text = "4321");
            Assert(Get("_preparedQuantityItemEdit") is null && !((Button)Get("ApplyButton")!).IsEnabled,
                "Changing the input while a preview is being prepared must discard that result.");
            await PreviewAsync();
            var prepared = Get("_preparedQuantityItemEdit") as PreparedQuantityItemEdit;
            var service = Get("_editService") as SaveEditService;
            Assert(prepared is not null && service is not null && ((Button)Get("ApplyButton")!).IsEnabled,
                "The real desktop preview must prepare native binary output: " + Log());
            Assert(ReferenceEquals(typeof(SaveEditService).GetField("_codec", flags)!.GetValue(service), session.Codec),
                "Preview and commit must use the same instance as catalog, sync and map readers.");
            Assert(prepared!.WorkspaceDirectory.StartsWith(locations.WorkspaceDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
                "Desktop contract edits must remain in their isolated workspace.");

            // Exercise the desktop apply path with the prepared binary and isolate the dialog from this test.
            var uiRespondedDuringCommit = false;
            service!.BeforeTargetReplace = _ =>
            {
                if (!window.Dispatcher.CheckAccess())
                    window.Dispatcher.Invoke(() => { uiRespondedDuringCommit = true; });
            };
            var backup = await ((Task<string?>)Call("CommitPreparedEditAsync", service, "native quantity test")!).WaitAsync(TimeSpan.FromSeconds(60));
            Assert(uiRespondedDuringCommit && backup is not null && File.Exists(Path.Combine(backup, "persist.estate.json")),
                "The dispatcher must remain responsive during native commit and the complete backup must be retained.");
            var readback = Path.Combine(f.RunRoot, "desktop-committed.json");
            await session.Codec.DecodeAsync(f.EstatePath, readback);
            Assert(QuantityItemSaveEditor.CountAmount(JsonNode.Parse(File.ReadAllText(readback))!.AsObject(), prepared.Item) == 4321,
                "The desktop prepared native save must commit the requested amount.");
            Assert(new FileInfo(f.EstatePath).Length > 0 && DsonSaveCodec.IsDson(f.EstatePath), "The committed file must remain binary DSON.");
            Call("RequestProfileSync", false);
            await ((Task)Get("_catalogSyncTask")!).WaitAsync(TimeSpan.FromSeconds(60));
            Assert(((IReadOnlyList<QuantityItemDefinition>)Get("_allItems")!).Single(item => item.DisplayId == "gold").CurrentAmount == 4321,
                "Desktop synchronization must reload the committed native value.");
            Console.WriteLine("PASS: native WPF item, trinket and hero background previews, stale preview rejection, guarded commit/backup and saved-value refresh.");
        }
        finally
        {
            Call("StopProfileSync", true);
            if (Get("_catalogSyncTask") is Task sync) await sync.WaitAsync(TimeSpan.FromSeconds(60));
            window.Close();
        }
    }

    private sealed class PreviewSources(IReadOnlyList<ActiveContentSource> sources, Action onFirstRead) : IReadOnlyList<ActiveContentSource>
    {
        private int _read;
        public int Count => sources.Count;
        public ActiveContentSource this[int index] => sources[index];
        public IEnumerator<ActiveContentSource> GetEnumerator()
        {
            if (Interlocked.Exchange(ref _read, 1) == 0) onFirstRead();
            return sources.GetEnumerator();
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
