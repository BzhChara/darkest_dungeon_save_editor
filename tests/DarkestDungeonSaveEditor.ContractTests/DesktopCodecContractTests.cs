using System.Reflection;
using System.Windows.Controls;
using System.Windows.Threading;
using DarkestDungeonSaveEditor.App;

internal static partial class ContractSuite
{
    public static async Task RunDesktopCodecOnlyAsync(string repositoryRoot)
    {
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
        finally { UseNativeCodec = false; }
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
        try
        {
            ((TextBox)Get("GameDirectoryTextBox")!).Text = f.GameRoot;
            ((TextBox)Get("WorkshopDirectoryTextBox")!).Text = f.WorkshopRoot;
            ((TextBox)Get("LocalModDirectoryTextBox")!).Text = f.AdditionalLocalModDirectory;
            ((TextBox)Get("ProfileDirectoryTextBox")!).Text = f.ProfileRoot;
            await ((Task)Call("LoadCatalogAsync", new object?[] { null })!).WaitAsync(TimeSpan.FromSeconds(90));
            Assert((bool)Get("_syncReady")! &&
                window.Title.Contains(EditorText.Get("Codec_NativeTestTitle")) == DesktopCodecSession.IsNativeTestBuild &&
                !window.Title.Contains(EditorText.Get("Codec_JavaReferenceTitle")),
                "Default desktop load must complete initial synchronization and use the correct build title: " + Log());
            Assert(ReferenceEquals(typeof(BattleMapView).GetField("_codec", flags)!.GetValue(Get("BattleMapPanel")), session.Codec),
                "The map must receive the window's selected backend.");
            ((DispatcherTimer)Get("_gameProcessPoll")!).Stop();

            ((TabControl)Get("CatalogTabs")!).SelectedIndex = 0;
            var grid = (DataGrid)Get("ItemGrid")!;
            grid.SelectedItem = grid.Items.Cast<object>().Single(row =>
                (string)row.GetType().GetProperty("Id")!.GetValue(row)! == "gold");
            ((TextBox)Get("CopiesTextBox")!).Text = "4321";
            Call("Preview_Click", window, new RoutedEventArgs());
            var deadline = DateTime.UtcNow.AddSeconds(60);
            while ((int)Get("_busyDepth")! > 0 && DateTime.UtcNow < deadline) await Task.Delay(15);
            var prepared = Get("_preparedQuantityItemEdit") as PreparedQuantityItemEdit;
            var service = Get("_editService") as SaveEditService;
            Assert(prepared is not null && service is not null && ((Button)Get("ApplyButton")!).IsEnabled,
                "The real desktop preview must prepare native binary output: " + Log());
            Assert(ReferenceEquals(typeof(SaveEditService).GetField("_codec", flags)!.GetValue(service), session.Codec),
                "Preview and commit must use the same instance as catalog, sync and map readers.");
            Assert(prepared!.WorkspaceDirectory.StartsWith(locations.WorkspaceDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
                "Desktop contract edits must remain in their isolated workspace.");

            // Exercise exactly the prepared service used by Apply_Click, without
            // opening confirmation/message dialogs or writing any real profile.
            var result = await service!.CommitAsync(prepared);
            Assert(File.Exists(Path.Combine(result.BackupDirectory, "persist.estate.json")), "A native desktop commit must retain its backup.");
            var readback = Path.Combine(f.RunRoot, "desktop-committed.json");
            await session.Codec.DecodeAsync(f.EstatePath, readback);
            Assert(QuantityItemSaveEditor.CountAmount(JsonNode.Parse(File.ReadAllText(readback))!.AsObject(), prepared.Item) == 4321,
                "The desktop prepared native save must commit the requested amount.");
            Assert(new FileInfo(f.EstatePath).Length > 0 && DsonSaveCodec.IsDson(f.EstatePath), "The committed file must remain binary DSON.");
            Call("RequestProfileSync", false);
            await ((Task)Get("_catalogSyncTask")!).WaitAsync(TimeSpan.FromSeconds(60));
            Assert(((IReadOnlyList<QuantityItemDefinition>)Get("_allItems")!).Single(item => item.DisplayId == "gold").CurrentAmount == 4321,
                "Desktop synchronization must reload the committed native value.");
            Console.WriteLine("PASS: native WPF catalog/initial sync/map backend, real quantity preview, guarded commit/backup and saved-value refresh without Java.");
        }
        finally
        {
            Call("StopProfileSync", true);
            if (Get("_catalogSyncTask") is Task sync) await sync.WaitAsync(TimeSpan.FromSeconds(60));
            window.Close();
        }
    }
}
