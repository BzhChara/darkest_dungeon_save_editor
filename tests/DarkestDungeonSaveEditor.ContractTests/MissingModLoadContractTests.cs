using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DarkestDungeonSaveEditor.App;

internal static partial class ContractSuite
{
    public static async Task RunMissingModLoadOnlyAsync(string repositoryRoot)
    {
        UseNativeCodec = true;
        try { await RunMissingModLoadContractsAsync(repositoryRoot); }
        finally { UseNativeCodec = false; }
    }

    private static async Task RunMissingModLoadContractsAsync(string repositoryRoot)
    {
        var f = BuildContractFixture(repositoryRoot);
        await SeedSyncFixtureAsync(f);
        VerifyMissingModSourceGuard(f);
        await RunWpfContractsAsync(() => VerifyMissingModLoadUiAsync(f));
        Console.WriteLine($"Artifacts: {f.RunRoot}");
    }

    private static async Task WriteModLoadGameAsync(ContractFixture f, JsonObject game)
    {
        var decoded = Path.Combine(f.RunRoot, "mod-load-game.decoded.json");
        File.WriteAllText(decoded, game.ToJsonString());
        await f.Codec.EncodeAsync(decoded, f.GameSavePath, null);
    }

    private static JsonObject ModLoadEntry(string name, string source = "mod_local_source") =>
        new() { ["name"] = name, ["source"] = source };

    private static string ModLoadFailure(ActiveContentSnapshot content)
    {
        try { ActiveContentResolver.RequireResolvedModSources(content); }
        catch (InvalidOperationException error) { return error.Message; }
        throw new InvalidOperationException("An unresolved enabled Mod was accepted for loading.");
    }

    private static void VerifyMissingModSourceGuard(ContractFixture f)
    {
        var profile = SteamDiscovery.OpenProfile(f.ProfileRoot);
        var game = JsonNode.Parse(File.ReadAllText(f.DecodedGameSeedPath))!.AsObject();
        var applied = game["base_root"]!["applied_ugcs_1_0"]!.AsObject();
        applied.Remove("11");
        ActiveContentSnapshot Resolve(string? workshop, string? local)
        {
            var path = Path.Combine(f.RunRoot, "mod-guard.decoded.json");
            File.WriteAllText(path, game.ToJsonString());
            return ActiveContentResolver.ResolveDecoded(profile, f.GameRoot, workshop, local,
                f.RunRoot, path, "guard-fixture");
        }

        // Ordinary parse warnings and a deduplicated source are not missing Mods.
        WriteMultiMash(f.AdditionalLocalModDirectory, "disabled-invalid/project.xml", "<project>");
        applied["20"] = ModLoadEntry("Local Test Mod");
        var complete = Resolve(f.WorkshopRoot, f.AdditionalLocalModDirectory);
        Assert(complete.Issues.Any(issue => issue.StartsWith("Failed to read local Mod project", StringComparison.Ordinal)) &&
            complete.Issues.Any(issue => issue.StartsWith("Enabled content directory appears more than once", StringComparison.Ordinal)),
            "The guard fixture must exercise unrelated discovery warnings and duplicate resolved entries.");
        ActiveContentResolver.RequireResolvedModSources(complete);
        applied.Remove("20");
        Console.WriteLine("PASS: load preflight accepts resolved Mods, duplicate references and unrelated disabled-project warnings.");

        applied["30"] = ModLoadEntry("Missing Local A");
        applied["31"] = ModLoadEntry("Missing Local B");
        applied["32"] = ModLoadEntry("999999", "Steam");
        var missing = Resolve(f.WorkshopRoot, f.AdditionalLocalModDirectory);
        var previousCulture = EditorText.Culture;
        try
        {
            foreach (var language in new[] { "en", "zh-CN" })
            {
                EditorText.Initialize(language, CultureInfo.GetCultureInfo(language));
                var message = ModLoadFailure(missing);
                Assert(message.StartsWith(EditorText.Get("ActiveContentResolver_UnresolvedMods"), StringComparison.Ordinal) &&
                    message.Contains("Missing Local A") && message.Contains("Missing Local B") && message.Contains("999999") &&
                    message.Split(Environment.NewLine).Length == 4 &&
                    !message.Contains(EditorText.Get("CatalogLogDiagnostics_012")),
                    "Both languages must list every unresolved Mod separately without claiming other sources continue loading.");
            }
        }
        finally { EditorText.Initialize(previousCulture.Name, previousCulture); }
        Console.WriteLine("PASS: missing local/Workshop Mods are listed together in English and Chinese before loading.");
        applied.Remove("30"); applied.Remove("31"); applied.Remove("32");

        var unavailable = ModLoadFailure(Resolve(null, f.AdditionalLocalModDirectory));
        Assert(unavailable.Contains(EditorText.Format("CatalogLogDiagnostics_UnavailableWorkshopDirectory", "111")),
            "An unset Workshop root must identify the saved Workshop ID.");
        var ambiguous = ModLoadFailure(Resolve(f.WorkshopRoot, f.AmbiguousDirectLocalModRoot));
        Assert(ambiguous.Contains("Local Test Mod") && ambiguous.Contains(f.AmbiguousDirectLocalModRoot),
            "Ambiguous enabled local projects must report the title and conflicting paths.");
        applied["30"] = ModLoadEntry("Unknown Source Mod", "unsupported");
        applied["31"] = ModLoadEntry("not-numeric", "Steam");
        applied["bad-key"] = ModLoadEntry("Malformed Mod");
        applied["32"] = new JsonObject { ["name"] = "Incomplete Mod" };
        var invalid = ModLoadFailure(Resolve(f.WorkshopRoot, f.AdditionalLocalModDirectory));
        Assert(invalid.Contains("Unknown Source Mod") && invalid.Contains("not-numeric") &&
            invalid.Contains(EditorText.Format("ActiveContentResolver_InvalidModRecord", "bad-key")) &&
            invalid.Contains(EditorText.Format("ActiveContentResolver_InvalidModRecord", "32")),
            "Unsupported, invalid and incomplete enabled records must not silently permit a partial load.");
        Console.WriteLine("PASS: unavailable roots, ambiguous local titles and invalid enabled records block partial loading.");
    }

    private static async Task VerifyMissingModLoadUiAsync(ContractFixture f)
    {
        var originalGame = File.ReadAllBytes(f.GameSavePath);
        var game = JsonNode.Parse(File.ReadAllText(Path.Combine(f.RunRoot, "mod-load-game.decoded.json")))!.AsObject();
        var applied = game["base_root"]!["applied_ugcs_1_0"]!.AsObject();
        applied["30"] = ModLoadEntry("Missing Local A");
        applied["31"] = ModLoadEntry("999999", "Steam");
        var pendingProject = WriteMultiMash(f.AdditionalLocalModDirectory, "pending-manifest/project.xml",
            "<project><Title>Pending Manifest Mod</Title></project>");
        applied["32"] = ModLoadEntry("Pending Manifest Mod");
        await WriteModLoadGameAsync(f, game);
        var manifest = Path.Combine(Path.GetDirectoryName(pendingProject)!, "modfiles.txt");
        var resource = Path.Combine(f.GameRoot, "trinkets", "fixture.entries.trinkets.json");
        var before = ProfileCatalogSnapshotReader.CaptureHashes(SteamDiscovery.OpenProfile(f.ProfileRoot));
        var locations = new SaveEditorLocations(f.RunRoot, Path.Combine(f.RunRoot, "mod-load-work"),
            Path.Combine(f.RunRoot, "mod-load-backups"));
        var window = new MainWindow(Path.Combine(f.RunRoot, "mod-load-language.json"),
            saveLocations: locations, gameRunningProbe: () => false);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Get(string name) => typeof(MainWindow).GetField(name, flags)!.GetValue(window);
        object? Call(string name, params object?[] args)
        {
            var method = typeof(MainWindow).GetMethod(name, flags)!;
            return method.Invoke(window, method.GetParameters().Select((parameter, index) =>
                index < args.Length ? args[index] : parameter.DefaultValue).ToArray());
        }
        bool Flag(string name) => (bool)Get(name)!;
        bool Enabled(string name) => ((FrameworkElement)Get(name)!).IsEnabled;
        var log = (TextBox)Get("StatusTextBox")!;
        Task Load() => ((Task)Call("LoadCatalogAsync", locations)!).WaitAsync(TimeSpan.FromSeconds(60));
        void AssertBlocked(string message)
        {
            Assert(!Flag("_catalogLoading") && !Flag("_syncReady") && !Enabled("PreviewButton") &&
                !Enabled("ApplyButton") && !Enabled("BattleMapPanel") && Enabled("PathInputsBorder") &&
                message.Contains("Missing Local A") && message.Contains("999999"),
                "Missing sources must identify every Mod, leave retry available and disable all edits. " + message);
        }
        try
        {
            ((DispatcherTimer)Get("_gameProcessPoll")!).Stop();
            ((TextBox)Get("GameDirectoryTextBox")!).Text = f.GameRoot;
            ((TextBox)Get("WorkshopDirectoryTextBox")!).Text = f.WorkshopRoot;
            ((TextBox)Get("LocalModDirectoryTextBox")!).Text = f.AdditionalLocalModDirectory;
            ((TextBox)Get("ProfileDirectoryTextBox")!).Text = f.ProfileRoot;
            // If content hashing/scanning runs before the guard, the locked resource
            // or absent official manifest generator will fail for a different reason.
            using (File.Open(resource, FileMode.Open, FileAccess.Read, FileShare.None)) await Load();
            AssertBlocked(log.Text);
            Assert(!File.Exists(manifest) && Get("_activeContentSnapshot") is null &&
                ((IReadOnlyList<QuantityItemDefinition>)Get("_allItems")!).Count == 0 &&
                !log.Text.Contains(EditorText.Get("MainWindow_CatalogLoading_031")) &&
                ProfileCatalogSnapshotReader.HashesEqual(before,
                    ProfileCatalogSnapshotReader.CaptureHashes(SteamDiscovery.OpenProfile(f.ProfileRoot))),
                "Rejected loading must stop before manifest generation/catalog scans and leave all save files unchanged. " + log.Text);
            Console.WriteLine("PASS: WPF missing-Mod load stops before manifests/resource reads, lists every missing Mod and preserves save bytes.");

            File.WriteAllBytes(f.GameSavePath, originalGame);
            log.Clear();
            await Load();
            Assert(Flag("_syncReady") && ((IReadOnlyList<QuantityItemDefinition>)Get("_allItems")!).Count > 0,
                "Fixing enabled records must allow an ordinary successful load. " + log.Text);
            var published = Get("_activeContentSnapshot");
            applied.Remove("32");
            await WriteModLoadGameAsync(f, game);
            using (File.Open(resource, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Call("RequestProfileSync", true, true);
                await ((Task)Get("_catalogSyncTask")!).WaitAsync(TimeSpan.FromSeconds(60));
            }
            AssertBlocked((string)Get("_lastSyncError")!);
            Assert(ReferenceEquals(published, Get("_activeContentSnapshot")) && !Flag("_syncRequested"),
                "A failed refresh must not publish a partial snapshot or continuously retry.");
            Console.WriteLine("PASS: later synchronization rejects missing Mods before resource hashing and keeps stale data non-editable.");

            log.Clear();
            using (File.Open(resource, FileMode.Open, FileAccess.Read, FileShare.None)) await Load();
            AssertBlocked(log.Text);
            Assert(Get("_activeContentSnapshot") is null &&
                ((IReadOnlyList<QuantityItemDefinition>)Get("_allItems")!).Count == 0 &&
                ((IReadOnlyList<TrinketDefinition>)Get("_allTrinkets")!).Count == 0 && Get("_heroCatalog") is null,
                "A failed explicit reload must clear catalogs from the previous profile.");
            File.WriteAllBytes(f.GameSavePath, originalGame);
            log.Clear();
            await Load();
            Assert(Flag("_syncReady") && Get("_lastSyncError") is null && Enabled("BattleMapPanel"),
                "Corrected sources must recover through explicit reload without restarting. " + log.Text);
            Console.WriteLine("PASS: failed reload clears old catalogs; corrected Mods restore normal loading and editing.");
        }
        finally
        {
            Call("StopProfileSync");
            if (Get("_catalogSyncTask") is Task sync) await sync.WaitAsync(TimeSpan.FromSeconds(60));
            window.Close();
        }
    }
}
