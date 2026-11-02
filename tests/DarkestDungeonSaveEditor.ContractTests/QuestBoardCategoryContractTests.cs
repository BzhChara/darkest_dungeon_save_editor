using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Threading;
using DarkestDungeonSaveEditor.App;

internal static partial class ContractSuite
{
    public static async Task RunQuestBoardCategoriesAsync(string repositoryRoot, string? nativeProfile)
    {
        var root = Path.Combine(repositoryRoot, "artifacts", "quest-board-categories-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var fixture = ReadCategoryFixture(root);
        QuestBoardEditCatalog? native = null;
        if (nativeProfile is not null)
        {
            var hashes = ProfileFilesHash(nativeProfile);
            var clone = Path.Combine(root, "profile_0");
            Directory.CreateDirectory(clone);
            foreach (var name in hashes.Keys) File.Copy(Path.Combine(nativeProfile, name), Path.Combine(clone, name));
            var codec = DsonSaveCodec.CreateNative();
            var content = await ActiveContentResolver.ResolveAsync(SteamDiscovery.OpenProfile(clone),
                @"E:\Steam\steamapps\common\DarkestDungeon", @"E:\Steam\steamapps\workshop\content\262060",
                @"E:\Steam\steamapps\common\DarkestDungeon\mods",
                codec, Path.Combine(root, "resolve"));
            ActiveContentResolver.RequireResolvedModSources(content);
            native = QuestBoardEditCatalog.Load(content);
            Assert(native.Templates.Count > 0, "Native catalog must contain actual quest templates.");
            File.WriteAllText(Path.Combine(root, "native-catalog.json"), JsonSerializer.Serialize(native, new JsonSerializerOptions { WriteIndented = true }));
            var unresolved = native.Templates.Where(item => item.Quest.Category is null).Select(item => item.Quest.Id + ":" + item.Quest.Type).ToArray();
            Assert(unresolved.Length == 0, "Review unclassified installed templates: " + string.Join(", ", unresolved));
            foreach (var group in native.Templates.GroupBy(item => (item.Quest.Type, item.Quest.Category)))
                Console.WriteLine($"{group.Key.Type} -> {group.Key.Category}: {group.Count()}");
            Assert(ProfileFilesHash(nativeProfile).OrderBy(pair => pair.Key).SequenceEqual(hashes.OrderBy(pair => pair.Key)),
                "Reading classifications must leave the original profile unchanged.");
        }
        await RunWpfContractsAsync(() =>
        {
            CheckCategoryMenus(root, fixture, "fixture");
            if (native is not null) CheckCategoryMenus(root, native, "native");
            return Task.CompletedTask;
        });
        Console.WriteLine("PASS: Goal-based categories, original identities, grouped picker dispatch, filtering, languages and WPF automation names.");
        Console.WriteLine("Artifacts: " + root);
    }

    private static QuestBoardEditCatalog ReadCategoryFixture(string root)
    {
        // Reduced installed definitions, inspected on 2026-10-30. These are reader
        // inputs, not newly invented game-generation rules or complete Mod files.
        // Courtyard Plus 1419429961, Sunward Isles 1689234891, Farmstead Plus
        // 2271257412, Vermintide 2683922974 and Fiend Festival 3669966489.
        (string Type, string Goal, string GoalType, string Item, QuestBoardCategory Category)[] cases =
        [
            ("burn_hive", "inventory_activate_hive", "activate", "torch_quest", QuestBoardCategory.InventoryActivate),
            ("farm_bloodcrate", "gather_bloodcrate", "activate", "", QuestBoardCategory.Activate),
            ("kill_mushfloor", "kill_mushfloor", "activate", "aqua_regia", QuestBoardCategory.InventoryActivate),
            ("inventory_activate_ship_quest", "inv_activate_ship_quest", "activate", "talisman_large", QuestBoardCategory.InventoryActivate),
            ("gather_ship_quest", "gather_carvings", "gather", "", QuestBoardCategory.Gather),
            ("activate_sigil", "inventory_activate_sigil_rock", "activate", "hammer_quest", QuestBoardCategory.InventoryActivate),
            ("gather_farm_quest", "gather_uncorrupted_food", "gather", "", QuestBoardCategory.Gather),
            ("explore_farm", "farm_explore_all_rooms", "explore_room", "", QuestBoardCategory.Explore),
            ("cleanse_farm", "farm_battle_all_rooms", "battle_room", "", QuestBoardCategory.Cleanse),
            ("vmt_gather", "gather_tome", "gather", "", QuestBoardCategory.Gather),
            ("vmt_inventory_activate", "inventory_activate_warp_architecture", "activate", "vermintide_pickaxe", QuestBoardCategory.InventoryActivate),
            ("inventory_activate_ff", "inv_activate_carnival_yateveo", "activate", "yateveo_offering", QuestBoardCategory.InventoryActivate),
            ("gather_ff", "gather_party_supplies", "gather", "", QuestBoardCategory.Gather),
            ("kill_crex", "kill_crex", "kill_monster", "", QuestBoardCategory.KillBoss),
            ("kill_raiju", "kill_raiju", "kill_monster", "", QuestBoardCategory.KillBoss),
            ("kill_tumor_leper", "kill_tumor_leper", "kill_monster", "", QuestBoardCategory.KillBoss),
            ("kill_ringmaster_boss", "kill_ringmaster_boss", "kill_monster", "", QuestBoardCategory.KillBoss),
            ("kill_ringmaster_boss_vet", "kill_ringmaster_boss_vet", "kill_monster", "", QuestBoardCategory.KillBoss),
            ("explore", "explore_all_rooms", "explore_room", "", QuestBoardCategory.Explore),
            ("cleanse", "battle_all_rooms", "battle_room", "", QuestBoardCategory.Cleanse),
            ("gather", "gather_medicines", "gather", "", QuestBoardCategory.Gather),
            ("inventory_activate", "inventory_activate_hive", "activate", "torch_quest", QuestBoardCategory.InventoryActivate),
            ("activate", "activate_teleporter", "activate", "", QuestBoardCategory.Activate),
            ("kill_boss", "kill_crow_A", "kill_monster", "", QuestBoardCategory.KillBoss),
            ("wave", "kill_miller", "kill_monster", "", QuestBoardCategory.Wave),
            ("wave", "kill_nura", "kill_monster", "", QuestBoardCategory.Wave),
            // Deliberately unfamiliar type name: reuse a real goal to prove that
            // classification does not whitelist the installed Mod type names.
            ("abc_collect", "gather_tome", "gather", "", QuestBoardCategory.Gather)
        ];
        var mod = Path.Combine(root, "mod");
        var resource = new JsonObject { ["goals"] = new JsonArray() };
        foreach (var row in cases.DistinctBy(row => row.Goal))
            resource["goals"]!.AsArray().Add(new JsonObject
            {
                ["id"] = row.Goal, ["type"] = row.GoalType, ["data"] = new JsonObject(),
                ["starting_items"] = row.Item.Length == 0 ? new JsonArray() : new JsonArray(new JsonObject
                { ["type"] = "quest_item", ["id"] = row.Item, ["amount"] = row.Item == "aqua_regia" ? 1 : 3 })
            });
        WriteMultiMash(mod, "campaign/quest/category.quest.types.json", resource.ToJsonString());
        WriteFixtureManifest(mod);
        var content = new ActiveContentSnapshot(new("profile_0", root, "", "contract", DateTime.UtcNow),
            "base", [new("local:categories", "Categories", "local", mod, 0)], [], root, "", 1, "hash");
        var board = new JsonObject();
        for (var index = 0; index < cases.Length; index++)
        {
            var entry = BoardTestQuest("category_" + index);
            entry["type"] = cases[index].Type;
            entry["goal_ids"] = new JsonArray(cases[index].Goal);
            board[index.ToString()] = entry;
        }
        // An unresolved definition is a defensive display case, not a claim that
        // a valid game quest uses an unknown goal. It must remain inspectable.
        var unresolved = BoardTestQuest("unresolved");
        unresolved["type"] = "unresolved_display_probe";
        unresolved["goal_ids"] = new JsonArray("missing_goal");
        board["missing"] = unresolved;
        var document = new JsonObject { ["base_root"] = new JsonObject { ["quests"] = board } };
        var before = document.ToJsonString();
        var snapshot = QuestBoardSnapshotReader.Read(content, document, "hash");
        for (var index = 0; index < cases.Length; index++)
        {
            var quest = snapshot.Quests[index];
            Assert(quest.Category == cases[index].Category, "Actual objective determines the menu group: " + cases[index].Type);
            Assert(quest.Type == cases[index].Type && quest.GoalIds.SequenceEqual(new[] { cases[index].Goal }) &&
                quest.IconPath.EndsWith("quest_select_" + cases[index].Type + "_1.png", StringComparison.Ordinal),
                "Grouping must preserve native type, goals and icon lookup: " + cases[index].Type);
        }
        Assert(snapshot.Quests[^1].Category is null && document.ToJsonString() == before,
            "Unresolved display has no invented classification; reading must not mutate any saved quest.");
        return new(snapshot.Quests.Select(quest => new QuestBoardTemplate(quest.SaveKey, quest,
            quest.Id == "unresolved" ? "Unresolved fixture goal" : "")).ToArray(), snapshot.ImagePaths, snapshot.Issues)
        { Regions = snapshot.Regions };
    }

    private static void CheckCategoryMenus(string root, QuestBoardEditCatalog catalog, string label)
    {
        var window = new MainWindow(Path.Combine(root, "language.json"), gameRunningProbe: () => false)
        { Left = -20000, Top = -20000, ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual };
        var originalLanguage = EditorText.Culture.Name;
        try
        {
            window.Show();
            ((TabControl)window.FindName("CatalogTabs")).SelectedIndex = 4;
            var board = (QuestBoardPreview)window.FindName("QuestBoardPanel");
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            board.ProfileBrowseChecking -= typeof(MainWindow).GetMethod("BattleMapPanel_ProfileBrowseChecking", flags)!
                .CreateDelegate<Func<bool>>(window);
            board.SaveEditRequested -= typeof(MainWindow).GetMethod("QuestBoardPanel_SaveEditRequested", flags)!
                .CreateDelegate<Func<QuestBoardEditRequest, Task>>(window);
            board.ProfileBrowseChecking += () => true;
            var dispatched = new List<QuestBoardEditRequest>();
            board.SaveEditRequested += request => { dispatched.Add(request); return Task.CompletedTask; };
            board.IsEnabled = true;
            board.SetProfileLoaded(true);
            board.SetSnapshot(new(root, "source-hash", [], catalog.Regions, catalog.ImagePaths, []));
            board.SetEditCatalog(catalog);
            board.Visibility = Visibility.Visible;
            FlushLanguageBindings();
            Assert(board.IsVisible && board.IsEnabled, "The real quest board must be interactive before testing its menus.");
            foreach (var language in new[] { "zh-CN", "en" })
            {
                EditorText.Initialize(language, EditorText.SystemCulture);
                board.RefreshLanguage();
                foreach (var region in catalog.Templates.GroupBy(item => item.Quest.Dungeon))
                {
                    var menu = board.CreateTaskMenu(region.Key, null);
                    var groups = ((MenuItem)menu.Items[0]).Items.OfType<MenuItem>().ToArray();
                    Assert(groups.Length > 0, "Each populated region must expose picker menus.");
                    var expected = region.GroupBy(item => (item.Quest.Category, Native: item.Quest.Category is null ? item.Quest.Type : "")).ToArray();
                    Assert(groups.Length == expected.Length, "A region has one menu per semantic group, not per Mod type.");
                    foreach (var group in groups)
                    {
                        var category = group.Tag as QuestBoardCategory?;
                        var expectedRows = region.Where(item => category is not null ? item.Quest.Category == category :
                            item.Quest.Category is null && ((string)group.Header).StartsWith(item.Quest.Type + " (", StringComparison.Ordinal)).ToArray();
                        Assert((string)group.Header == (category is { } known ? QuestBoardPreviewData.CategoryName(known) : expectedRows[0].Quest.Type) + $" ({expectedRows.Length})",
                            "The category's displayed count includes every compatible template.");
                        Exception? failure = null;
                        var pickerOpened = false;
                        string? chosenKey = null;
                        window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                        {
                            var dialog = Application.Current.Windows.OfType<QuestBoardPreviewDialog>().Single();
                            pickerOpened = true;
                            try
                            {
                                var grid = (DataGrid)dialog.FindName("ChoiceGrid");
                                var filter = (TextBox)dialog.FindName("SearchTextBox");
                                var levels = (ComboBox)dialog.FindName("DifficultyChoice");
                                var confirm = (Button)dialog.FindName("ConfirmButton");
                                var peer = new DataGridAutomationPeer(grid);
                                var seen = new HashSet<string>();
                                foreach (var level in expectedRows.Select(item => item.Quest.Difficulty).Distinct())
                                {
                                    levels.SelectedValue = level;
                                    foreach (var row in grid.Items.Cast<QuestBoardPreviewDialog.Row>())
                                    {
                                        seen.Add(row.Choice.TemplateKey!);
                                        Assert(row.Choice.Category == category && row.Choice.ToString() == row.Name && row.ToString() == row.Name &&
                                            new DataGridItemAutomationPeer(row, peer).GetName() == row.Name,
                                            "The actual WPF accessible-name path must not throw for custom quest records.");
                                    }
                                }
                                Assert(seen.SetEquals(expectedRows.Select(item => item.Key)), "Difficulty filters cover exactly the grouped templates.");
                                filter.Text = "no-such-task-filter";
                                Assert(grid.Items.Count == 0 && !confirm.IsEnabled, "Empty filters disable selection.");
                                filter.Text = "";
                                var target = expectedRows.FirstOrDefault(item => item.UnavailableReason.Length == 0);
                                if (target is null) { dialog.Close(); return; }
                                levels.SelectedValue = target.Quest.Difficulty;
                                grid.SelectedItem = grid.Items.Cast<QuestBoardPreviewDialog.Row>().Single(row => row.Choice.TemplateKey == target.Key);
                                Assert(confirm.IsEnabled, "Known Mod goals remain available after regrouping.");
                                if (label == "native" && language == "zh-CN" && expectedRows.Any(item => item.Quest.Type is "burn_hive" or "vmt_gather"))
                                    RenderContractWindow(dialog, new Size(dialog.Width, dialog.Height), Path.Combine(root, $"{region.Key}-{category}.png"));
                                confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                                // The UI dispatch is asserted after ShowDialog returns below.
                                chosenKey = target.Key;
                            }
                            catch (Exception error) { failure = error; dialog.Close(); }
                        }));
                        var before = dispatched.Count;
                        group.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                        if (failure is not null) throw failure;
                        Assert(pickerOpened, "Clicking the category must actually open its picker: " + group.Header);
                        if (chosenKey is string templateKey)
                            Assert(dispatched.Count == before + 1 && dispatched[^1] == new QuestBoardEditRequest(QuestBoardEditKind.Create,
                                "source-hash", TemplateKey: templateKey), "Grouped menus dispatch the original template key without rewriting its type.");
                    }
                }
            }
        }
        finally { window.Close(); EditorText.Initialize(originalLanguage, EditorText.SystemCulture); }
    }
}
