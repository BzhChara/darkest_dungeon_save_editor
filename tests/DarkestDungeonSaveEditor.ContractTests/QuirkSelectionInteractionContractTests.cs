using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using DarkestDungeonSaveEditor.App;

internal static partial class ContractSuite
{
    public static async Task RunQuirkSelectionOnlyAsync(string repositoryRoot)
    {
        RunUiContracts(repositoryRoot);
        var fixture = BuildContractFixture(repositoryRoot);
        await RunInitialHpConditionContractsAsync(fixture.RunRoot, fixture.Codec);
        await SeedSyncFixtureAsync(fixture);
        await RunWpfContractsAsync(async () =>
        {
            await VerifyQuirkSelectionInteractionAsync(repositoryRoot);
            await VerifyProfileSyncInteractionAsync(fixture);
        });
        Console.WriteLine($"Artifacts: {fixture.RunRoot}");
    }

    private static async Task VerifyQuirkSelectionInteractionAsync(string repositoryRoot)
    {
        var f = BuildContractFixture(repositoryRoot);
        await SeedSyncFixtureAsync(f);
        var root = Path.Combine(f.RunRoot, "quirk-selection");
        Directory.CreateDirectory(root);
        var buffs = new JsonArray();
        var quirks = new JsonArray();
        void Add(string id, double amount, bool positive, bool flat = true, string rule = "always")
        {
            buffs.Add(new JsonObject { ["id"] = id, ["stat_type"] = flat ? "combat_stat_add" : "combat_stat_multiply",
                ["stat_sub_type"] = "max_hp", ["amount"] = amount, ["rule_type"] = rule, ["is_false_rule"] = false });
            quirks.Add(new JsonObject { ["id"] = id, ["is_positive"] = positive, ["is_disease"] = false,
                ["random_chance"] = 1, ["buffs"] = new JsonArray(id) });
        }
        Add("ui_flat_loss", -30, false);
        Add("ui_flat_gain", 40, true);
        Add("ui_small_gain", 5, true);
        Add("ui_percent_loss", -1, false, flat: false);
        Add("ui_percent_gain", .5, true, flat: false);
        Add("ui_conditional_gain", 40, true, rule: "no_trinkets");
        Add("ui_unknown", 5, true, rule: "unknown_hp_rule");
        quirks.Add(new JsonObject { ["id"] = "ui_missing", ["is_positive"] = true, ["buffs"] = new JsonArray("ui_no_such_buff") });
        quirks.Add(new JsonObject { ["id"] = "ui_positive_disease", ["is_positive"] = true, ["is_disease"] = true });
        WriteMultiMash(f.GameRoot, "shared/buffs/ui_selection.buffs.json", new JsonObject { ["buffs"] = buffs }.ToJsonString());
        WriteMultiMash(f.GameRoot, "shared/quirk/ui_selection.quirk_library.json", new JsonObject { ["quirks"] = quirks }.ToJsonString());
        var profile = new SaveProfile("quirk_ui", f.ProfileRoot, f.EstatePath, "contract-user", DateTime.UtcNow);
        var content = await ActiveContentResolver.ResolveAsync(profile, f.GameRoot, f.WorkshopRoot,
            f.AdditionalLocalModDirectory, f.Codec, root);
        var incomplete = HeroClassCatalog.Load(content);
        Assert(incomplete.InitialQuirks.Single(q => q.Id == "ui_flat_gain").WriteStatus == HeroInitialQuirkWriteStatus.Unverified,
            "The general fixture's enabled but missing Workshop 333 must not certify Buff attributes.");
        // This UI test needs known Buff values. Resolve the deliberately absent
        // source with a real empty manifest before testing HP compensation.
        WriteMultiMash(f.WorkshopRoot, "333/modfiles.txt", string.Empty);
        content = await ActiveContentResolver.ResolveAsync(profile, f.GameRoot, f.WorkshopRoot,
            f.AdditionalLocalModDirectory, f.Codec, root);
        Console.WriteLine("PASS: unknown enabled provider blocks HP attributes; resolving its empty manifest restores the WPF selection fixture.");
        var catalog = HeroClassCatalog.Load(content);
        var hero = catalog.HeroClasses.Single(h => h.Id == "local_hero");
        Assert(hero.LevelProfiles.Single(p => p.ResolveLevel == 0).ArmourHp == 20, "UI fixture must have base HP 20.");
        var service = new SaveEditService(f.Codec, new(root, Path.Combine(root, "work"), Path.Combine(root, "backups")));
        var originalGame = File.ReadAllBytes(f.GameSavePath);
        var originalEstate = File.ReadAllBytes(f.EstatePath);
        var savedResults = new List<object>();

        foreach (var pair in new[] { (Loss: "ui_flat_loss", Gain: "ui_flat_gain", Hp: 30d),
                                     (Loss: "ui_percent_loss", Gain: "ui_percent_gain", Hp: 10d) })
        {
            using var dialog = new QuirkDialogSession(catalog, hero, 0, []);
            Assert(!dialog.Box(pair.Loss).IsEnabled && dialog.Box(pair.Gain).IsEnabled,
                "A nonpositive standalone HP quirk starts disabled, while its compensating quirk is enabled. " +
                dialog.Describe(pair.Loss) + " / " + dialog.Describe(pair.Gain));
            await dialog.ToggleAsync(pair.Gain);
            Assert(dialog.Box(pair.Loss).IsEnabled && dialog.Reason(pair.Loss).Length == 0,
                $"{pair.Loss}: selecting compensation must enable the negative quirk and clear its solo HP error.");
            await dialog.ToggleAsync(pair.Loss);
            Assert(dialog.SelectedIds().ToHashSet(StringComparer.Ordinal).SetEquals([pair.Loss, pair.Gain]) && dialog.Validation.Length == 0,
                "Actual template click events must retain both valid selections.");
            var candidate = StagecoachHeroCandidateFactory.Generate(catalog, hero, 1729, 0, dialog.SelectedIds());
            Assert(candidate.Preview.CurrentHp == pair.Hp, "The chosen UI combination must generate its combined HP.");
            var prepared = await service.PrepareStagecoachHeroEditAsync(profile, candidate, catalog, content);
            await service.CommitAsync(prepared);
            var decodedPath = Path.Combine(root, pair.Loss + ".town.json");
            await f.Codec.DecodeAsync(f.TownSavePath, decodedPath);
            var decoded = JsonNode.Parse(File.ReadAllText(decodedPath))!;
            var generated = decoded["base_root"]!["buildings"]!["stage_coach"]!["store"]!["hero_recruit"]!["generated"]!;
            var saved = generated[prepared.Preview.CandidateGuid.ToString()]!;
            Assert(saved["actor"]!["current_hp"]!.GetValue<double>() == pair.Hp &&
                saved["quirks"]![pair.Loss] is not null && saved["quirks"]![pair.Gain] is not null && DsonSaveCodec.IsDson(f.TownSavePath),
                "Selection must survive actual save preflight, commit and final DSON decoding.");
            savedResults.Add(new { pair.Loss, pair.Gain, pair.Hp, prepared.Preview.CandidateGuid, decodedPath });

            using (var reopened = new QuirkDialogSession(catalog, hero, 0, [pair.Gain]))
                Assert(reopened.Box(pair.Loss).IsEnabled, "Opening with existing compensation must enable the valid addition.");
            using (var reopened = new QuirkDialogSession(catalog, hero, 0, [pair.Gain, pair.Loss]))
                Assert(reopened.Box(pair.Loss).IsChecked == true && reopened.Reason(pair.Loss).Length == 0,
                    "Reopening a valid pair must not display a stale standalone error.");

            await dialog.ToggleAsync(pair.Gain);
            Assert(dialog.SelectedIds().SequenceEqual([pair.Loss]) && dialog.Validation.Length > 0 && dialog.Box(pair.Loss).IsEnabled,
                "Removing compensation must remain possible, expose the invalid remainder and allow its removal.");
            dialog.Call("Ok_Click", new Button(), new RoutedEventArgs());
            Assert(dialog.Validation.Length > 0 && dialog.Window.SelectedQuirkIds.Count == 0,
                "Confirmation must reject the invalid remaining selection.");
            Assert(dialog.Box(pair.Gain).IsEnabled, "Re-adding compensation must repair an invalid intermediate selection.");
            await dialog.ToggleAsync(pair.Gain);
            Assert(dialog.Validation.Length == 0 && dialog.Reason(pair.Loss).Length == 0,
                "Repaired selection must clear old validation and row errors.");
            ((TextBox)dialog.Window.FindName("SearchTextBox")).Text = pair.Gain;
            Assert(dialog.SelectedIds().Length == 2, "Filtering visible rows must not remove selected hidden quirks.");
            dialog.Call("Clear_Click", new Button(), new RoutedEventArgs());
            await Dispatcher.Yield(DispatcherPriority.Background);
            Assert(dialog.SelectedIds().Length == 0 && dialog.Validation.Length == 0 && !dialog.Box(pair.Loss).IsEnabled,
                "Clear must remove even filtered selections and recalculate standalone availability.");
            Console.WriteLine($"PASS: WPF {pair.Loss} compensation, reopening, removal, repair, filtering, clearing and DSON HP {pair.Hp}.");
        }

        using (var insufficient = new QuirkDialogSession(catalog, hero, 0, ["ui_small_gain"]))
            Assert(!insufficient.Box("ui_flat_loss").IsEnabled, "Insufficient compensation remains unavailable.");
        using (var conditional = new QuirkDialogSession(catalog, hero, 0, ["ui_conditional_gain"]))
            Assert(!conditional.Box("ui_flat_loss").IsEnabled, "Generation-time HP alone cannot override a nonpositive reachable runtime state.");
        using (var invalidDefinitions = new QuirkDialogSession(catalog, hero, 0, ["ui_flat_gain"]))
            Assert(!invalidDefinitions.Box("ui_missing").IsEnabled && !invalidDefinitions.Box("ui_unknown").IsEnabled,
                "Compensation cannot make missing or unknown Buff definitions writable.");
        using (var exclusions = new QuirkDialogSession(catalog, hero, 0, ["tough"]))
            Assert(!exclusions.Box("fragile").IsEnabled && exclusions.Reason("fragile").Contains("互斥", StringComparison.Ordinal),
                "Mutual exclusions remain visible and disabled.");
        Console.WriteLine("PASS: WPF insufficient/conditional HP, missing definitions, unknown rules and true exclusions remain blocked.");

        var limited = catalog with { InitialQuirkLimits = new HeroInitialQuirkLimits(1, 1, 1) };
        using (var quotas = new QuirkDialogSession(limited, hero, 0, ["ui_positive_disease"]))
        {
            Assert(!quotas.Box("ui_flat_gain").IsEnabled && !quotas.Box("test_disease").IsEnabled && quotas.Box("fragile").IsEnabled,
                "A positive disease occupies both positive and disease quotas, leaving the negative quota available.");
            await quotas.ToggleAsync("ui_positive_disease");
            Assert(quotas.Box("ui_flat_gain").IsEnabled && quotas.Box("test_disease").IsEnabled,
                "Removing a quota-consuming selection must immediately re-enable the freed categories.");
        }
        using (var unknownLimit = new QuirkDialogSession(catalog with { InitialQuirkLimits = new(null, 5, 3) }, hero, 0, []))
            Assert(!unknownLimit.Box("ui_flat_gain").IsEnabled && unknownLimit.Box("fragile").IsEnabled,
                "An unresolved category quota must not disable a different valid category.");
        using (var singleton = new QuirkDialogSession(catalog, hero, 0, []))
            Assert(singleton.Box("context_special").IsEnabled && singleton.Reason("context_special").Contains("singleton 定义上限 1", StringComparison.Ordinal) &&
                !singleton.Reason("context_special").Contains("预览统计", StringComparison.Ordinal), "Singleton keeps its compact context warning without becoming prohibited.");
        var duplicate = catalog with { InitialQuirks = catalog.InitialQuirks.Concat([catalog.InitialQuirks.Single(q => q.Id == "ui_small_gain")]).ToArray() };
        using (var ambiguous = new QuirkDialogSession(duplicate, hero, 0, []))
            Assert(!ambiguous.Box("ui_small_gain").IsEnabled, "Unresolved duplicate definitions remain disabled.");
        Console.WriteLine("PASS: WPF quotas, overlapping disease counts, unknown limits, singleton warnings and duplicate definitions.");

        using (var highLevel = new QuirkDialogSession(catalog, hero, 6, []))
            Assert(highLevel.Box("ui_flat_loss").IsEnabled, "Availability must use the selected level's armour HP.");
        using (var lowLevel = new QuirkDialogSession(catalog, hero, 0, ["ui_flat_loss", "ui_percent_loss"]))
        {
            await lowLevel.ToggleAsync("ui_flat_loss");
            Assert(lowLevel.SelectedIds().SequenceEqual(["ui_percent_loss"]) && lowLevel.Box("ui_percent_loss").IsEnabled,
                "Removing one invalid preselection must not require all other invalid selections to be fixed first.");
            await lowLevel.ToggleAsync("ui_percent_loss");
            Assert(lowLevel.SelectedIds().Length == 0 && lowLevel.Validation.Length == 0, "Every invalid selection can be removed.");
        }
        Assert(originalGame.SequenceEqual(File.ReadAllBytes(f.GameSavePath)) && originalEstate.SequenceEqual(File.ReadAllBytes(f.EstatePath)),
            "UI-selected candidate commits must not change game or estate saves.");
        Console.WriteLine("PASS: WPF level-sensitive availability and removal of multiple invalid selections.");
        File.WriteAllText(Path.Combine(root, "results.json"), JsonSerializer.Serialize(savedResults, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Quirk UI artifacts: {root}");
    }

    private sealed class QuirkDialogSession : IDisposable
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly Dictionary<string, CheckBox> _boxes = new(StringComparer.Ordinal);
        public InitialQuirkSelectionDialog Window { get; }
        public QuirkDialogSession(HeroClassCatalogResult catalog, HeroClassDefinition hero, int level, string[] ids) =>
            Window = new InitialQuirkSelectionDialog(catalog, hero, level, ids);
        private object Row(string id) => ((IEnumerable)typeof(InitialQuirkSelectionDialog).GetField("_rows", Private)!.GetValue(Window)!)
            .Cast<object>().Single(row => (string)row.GetType().GetProperty("Id")!.GetValue(row)! == id);
        public string Reason(string id) => (string)Row(id).GetType().GetProperty("UnavailableReason")!.GetValue(Row(id))!;
        public string Describe(string id) => $"{id}: control={Box(id).IsEnabled}, row={Row(id).GetType().GetProperty("IsSelectable")!.GetValue(Row(id))}, reason={Reason(id)}";
        public string Validation => ((TextBlock)Window.FindName("ValidationTextBlock")).Text;
        public object? Call(string method, params object[] args) => typeof(InitialQuirkSelectionDialog).GetMethod(method, Private)!.Invoke(Window, args);
        public string[] SelectedIds() => (string[])Call("GetSelectedIds")!;
        public CheckBox Box(string id)
        {
            if (_boxes.TryGetValue(id, out var box)) return box;
            var column = ((DataGrid)Window.FindName("QuirkGrid")).Columns.OfType<DataGridTemplateColumn>().Single();
            box = (CheckBox)column.CellTemplate.LoadContent();
            box.DataContext = Row(id);
            // Template bindings attach on the Dispatcher even when the window stays hidden.
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            box.GetBindingExpression(UIElement.IsEnabledProperty)!.UpdateTarget();
            box.GetBindingExpression(ToggleButton.IsCheckedProperty)!.UpdateTarget();
            _boxes.Add(id, box);
            return box;
        }
        public async Task ToggleAsync(string id)
        {
            var box = Box(id);
            Assert(box.IsEnabled, $"Cannot interact with disabled quirk '{id}'.");
            typeof(ToggleButton).GetMethod("OnClick", Private)!.Invoke(box, null);
            await Dispatcher.Yield(DispatcherPriority.Background);
            Assert(box.IsChecked == SelectedIds().Contains(id, StringComparer.Ordinal), "Template click handler and selection binding must agree.");
        }
        public void Dispose() => Window.Close();
    }
}
