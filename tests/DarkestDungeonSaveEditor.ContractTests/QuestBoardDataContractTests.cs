using System.Reflection;
using System.Text.Json;
using System.Windows.Controls;
using DarkestDungeonSaveEditor.App;

internal static partial class ContractSuite
{
    public static async Task RunQuestBoardDataAsync(string repositoryRoot, string? nativeSnapshotPath)
    {
        var root = Path.Combine(repositoryRoot, "artifacts", "quest-board-data-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var (snapshot, document) = RunQuestBoardReaderContracts(root);
        RunQuestBoardCampingContracts(root);
        await RunQuestBoardSnapshotRefreshAsync(repositoryRoot, document);
        await RunWpfContractsAsync(() =>
        {
            RunQuestBoardRealUiContracts(root, snapshot, "fixture");
            if (nativeSnapshotPath is not null)
            {
                var native = JsonSerializer.Deserialize<QuestBoardSnapshot>(File.ReadAllText(nativeSnapshotPath))!;
                RunQuestBoardRealUiContracts(root, native, "native");
            }
            return Task.CompletedTask;
        });
        Console.WriteLine("PASS: Saved quest fields/rewards, active metadata/art, language, read-only UI and snapshot refresh.");
        Console.WriteLine("Artifacts: " + root);
    }

    private static (QuestBoardSnapshot Snapshot, JsonObject Document) RunQuestBoardReaderContracts(string root)
    {
        var game = Path.Combine(root, "game");
        var mod = Path.Combine(root, "mod");
        var dlc = Path.Combine(root, "dlc");
        void Write(string directory, string relative, string text)
        {
            var path = Path.Combine(directory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }
        // Reduced native resource/save shapes, used only to test reading, never quest generation.
        Write(game, "campaign/quest/quest.types.json", """
        {"goals":[{"id":"explore_all_rooms","type":"explore_room","data":{"amount":0,"percentage":0.9}}]}
        """);
        Write(game, "campaign/town/quest_select/quest_select.layout.darkest", """
        quest_select_dungeon_layout_crypts: .quest_map_pos 940 220 .quest_number_of_quests_in_row 4 .has_visible_progression 1
        quest_select_dungeon_layout_town: .quest_map_pos 1260 750 .quest_number_of_quests_in_row 6 .has_visible_progression 1
        """);
        Write(mod, "campaign/town/quest_select/carnival.quest_select.layout.darkest", """
        quest_select_dungeon_layout_carnival: .quest_map_pos 980 675 .quest_number_of_quests_in_row 4
        """);
        // Reduced shipped Farmstead/Vermintide definitions. The editor deliberately
        // displays empty quest destinations too; town and sewer share a map position.
        Write(dlc, "campaign/town/quest_select/com.quest_select.layout.darkest", """
        quest_select_dungeon_layout_farm: .quest_map_pos 570 600 .quest_number_of_quests_in_row 4 .has_visible_progression 0
        """);
        Write(mod, "campaign/town/quest_select/vermintide.quest_select.layout.darkest", """
        quest_select_dungeon_layout_vmt_mine: .quest_map_pos 580 710 .quest_number_of_quests_in_row 4 .has_visible_progression 1
        quest_select_dungeon_layout_vmt_sewer: .quest_map_pos 1260 750 .quest_number_of_quests_in_row 8 .has_visible_progression 0
        quest_select_dungeon_layout_farmest: .quest_map_pos 550 180 .quest_number_of_quests_in_row 4
        """);
        Write(mod, "campaign/town/quest_select/town_wave.quest_select.layout.darkest", """
        quest_select_dungeon_layout_town_wave: .quest_map_pos 600 600 .quest_number_of_quests_in_row 4
        """);
        Write(mod, "campaign/town/quest_select/ship_wave.quest_select.layout.darkest", """
        quest_select_dungeon_layout_ship_wave: .quest_map_pos 1500 600 .quest_number_of_quests_in_row 4
        """);
        Write(game, "dungeons/crypts/crypts.dungeon.json", """
        {"id_index":1,"is_released":true,"requires_quest_to_display":false}
        """);
        Write(game, "dungeons/town/town.dungeon.json", """
        {"id_index":6,"is_released":true,"requires_quest_to_display":true}
        """);
        Write(game, "campaign/progression/progression.json", """
        {"dungeon":{"level_threshold_table":[0,2,6,10,16,22,32,42]}}
        """);
        Write(dlc, "dungeons/farm/farm.dungeon.json", """
        {"id_index":7,"is_released":true,"requires_quest_to_display":false}
        """);
        Write(mod, "dungeons/vmt_mine/vmt_mine.dungeon.json", """
        {"id_index":3721,"is_released":true,"requires_quest_to_display":false}
        """);
        Write(mod, "dungeons/vmt_sewer/vmt_sewer.dungeon.json", """
        {"id_index":3722,"is_released":true,"requires_quest_to_display":true}
        """);
        Write(mod, "dungeons/farmest/farmest.dungeon.json", """
        {"id_index":182,"is_released":true,"requires_quest_to_display":true}
        """);
        Write(mod, "dungeons/town_wave/town_wave.dungeon.json", """
        {"id_index":11,"is_released":true,"requires_quest_to_display":true}
        """);
        Write(mod, "dungeons/ship_wave/ship_wave.dungeon.json", """
        {"id_index":12,"is_released":true,"requires_quest_to_display":true}
        """);
        // Reduced shipped destination records. These are display fixtures, not
        // complete quest templates or new conditions for native quest generation.
        Write(game, "campaign/quest/quest.plot_quests.json", """
        {"plot_quests":[{"id":"plot_town_invasion_0","quest":{"dungeon":"town"}}]}
        """);
        // Use the sewer generation entry on its own to cover both native entry
        // paths independently; the installed Mod also has sewer plot quests.
        Write(mod, "campaign/quest/vermintide.quest.generation.json", """
        {"generation":{"type":{"available_quests_table":[
          {"dungeon":"vmt_sewer","generated_quest_table":[[
            {"type":"cleanse","chance":1,"length":2,"required_plot_quest_completed":"plot_vmt_town_corruption_2"}
          ]]}
        ]}}}
        """);
        Write(game, "campaign/provision/provision.json", """
        {"raid_starting_length_inventory_item_lists":[[],[{"type":"supply","id":"firewood","amount":0}],[{"type":"supply","id":"firewood","amount":1}]]}
        """);
        Write(game, "trinkets/base.entries.trinkets.json", """
        {"entries":[{"id":"bleed_stone","rarity":"common"}]}
        """);
        Write(mod, "trinkets/base.entries.trinkets.json", """
        {"entries":[{"id":"bleed_stone","rarity":"rare"},{"id":"bleed_stone","rarity":"very_rare"}]}
        """);
        Write(game, "localization/board.string_table.xml", """
        <root><language id="english">
        <entry id="town_quest_name_explore+1+crypts+explore_all_rooms">Scout</entry>
        <entry id="town_quest_description_explore+1+crypts+explore_all_rooms">Explore the ruins.</entry>
        <entry id="town_quest_name_plot_tutorial_crypts">Scout</entry>
        <entry id="town_quest_goal_start_plural_explore_room">Explore %.0f%% of rooms.</entry>
        <entry id="dungeon_name_crypts">Ruins</entry><entry id="str_inventory_title_trinketbleed_stone">Bleed Stone</entry>
        </language><language id="schinese">
        <entry id="town_quest_name_explore+1+crypts+explore_all_rooms">探索</entry>
        <entry id="town_quest_description_explore+1+crypts+explore_all_rooms">探索遗迹。</entry>
        <entry id="town_quest_name_plot_tutorial_crypts">探索</entry>
        <entry id="town_quest_goal_start_plural_explore_room">探索 %.0f%% 的房间。</entry>
        <entry id="dungeon_name_crypts">遗迹</entry><entry id="str_inventory_title_trinketbleed_stone">出血石</entry>
        </language></root>
        """);
        var icon = "campaign/town/quest_select/quest_select_explore_3.png";
        var plotIcon = "campaign/town/quest_select/quest_select_plot_tutorial_crypts.png";
        // The reader resolves paths without decoding PNG pixels; UI missing-image handling is independent.
        Write(game, icon, "base");
        Write(mod, icon, "active");
        Write(dlc, plotIcon, "dlc");
        WriteFixtureManifest(mod);
        // An unlisted physical Mod file must not override the registered DLC definition.
        Write(mod, "dungeons/farm/farm.dungeon.json", """
        {"id_index":7,"is_released":true,"requires_quest_to_display":true}
        """);
        Write(mod, "campaign/quest/unlisted.quest.types.json", """
        {"goals":[{"id":"explore_all_rooms","type":"explore_room","data":{"percentage":0.1}}]}
        """);
        // Synthetic unlisted reference: a physical file outside the manifest must
        // not turn an internal scene into an active quest destination.
        Write(mod, "campaign/quest/unlisted.quest.plot_quests.json", """
        {"plot_quests":[{"id":"unlisted_ship_entry","quest":{"dungeon":"ship_wave"}}]}
        """);
        var profile = new SaveProfile("profile_0", Path.Combine(root, "profile_0"), "", "contract", DateTime.UtcNow);
        var content = new ActiveContentSnapshot(profile, "base", [new("base", "Base", "base", game, 0),
            new("dlc:sample", "DLC", "dlc", dlc, 1) { VirtualPathPrefix = "dlc/sample" },
            new("local:sample", "Sample", "local", mod, 2)], [], root, "", 1, "game-hash");
        var document = JsonNode.Parse("""
        {"base_root":{"version":42,"quests":{
          "0":{"id":"generated_0","is_plot_quest":false,"type":"explore","dungeon":"crypts","difficulty":3,"length":1,"goal_ids":["explore_all_rooms"],
            "completion_reward":{"items_definition":{"system_config_type":"quest_rewards","items":{"0":{"id":"","type":"gold","amount":3000},"1":{"id":"bleed_stone","type":"trinket","amount":1}}}}},
          "1":{"id":"plot_tutorial_crypts","is_plot_quest":true,"type":"explore","dungeon":"crypts","difficulty":1,"length":1,"goal_ids":["explore_all_rooms"],
            "completion_reward":{"items_definition":{"items":{}}}},
          "2":{"id":"generated_1","is_plot_quest":false,"type":"cleanse","dungeon":"carnival","difficulty":1,"length":1,"goal_ids":["unavailable_goal"],
            "completion_reward":{"items_definition":{"items":{}}}}
        }}}
        """)!.AsObject();
        var original = document.ToJsonString();
        var progression = JsonNode.Parse("""
        {"base_root":{"dungeon":{"crypts":{"xp":4},"town":{"xp":42}}}}
        """)!.AsObject();
        var board = QuestBoardSnapshotReader.Read(content, document, "quest-hash", progression);
        Assert(document.ToJsonString() == original && board.Quests.Count == 3, "Reading must not mutate or drop saved entries.");
        var generated = board.Quests[0];
        Assert(generated.Difficulty == 3 && generated.Length == 1 && generated.LocalizedName.English == "Scout" &&
               generated.LocalizedObjective.English == "Explore 90% of rooms.",
            "Generated text uses saved type/length/dungeon/goals, not difficulty or unlisted Mod resources.");
        Assert(generated.Rewards[0].Amount == 3000 && generated.Rewards[1].Rarity == "rare" && generated.Firewood == 0 &&
               board.Quests[1].Rewards.Count == 0,
            "Keep saved rewards/empty inventories; rarity uses the effective file's first native identity.");
        Assert(board.ImagePaths[icon] == Path.Combine(mod, icon.Replace('/', Path.DirectorySeparatorChar)) &&
               board.ImagePaths[plotIcon] == Path.Combine(dlc, plotIcon.Replace('/', Path.DirectorySeparatorChar)) &&
               board.Quests[1].IconPath == plotIcon,
            "Canonical image request keys must survive Mod overrides and DLC virtual mount prefixes.");
        Assert(board.Regions.Single(region => region.Id == "carnival").X == 980 &&
               board.Quests[2].Objective == "unavailable_goal" && board.Issues.Any(issue => issue.Contains("unavailable_goal")),
            "Use a Mod's real layout and retain a missing goal ID with a diagnostic.");
        Assert(new[] { "farm", "vmt_mine", "vmt_sewer", "town" }
                .All(id => board.Regions.Any(region => region.Id == id)),
            "Native always-visible regions and active quest destinations remain available even without quests.");
        Assert(new[] { "farmest", "town_wave", "ship_wave" }.All(id => board.Regions.All(region => region.Id != id)),
            "Unused placeholders and internal wave scenes without quest entries must not affect map display or framing.");
        Assert(board.Regions.Single(region => region.Id == "crypts") is { ProgressionLevel: 1, ProgressionFraction: 0.5 } &&
               board.Regions.Single(region => region.Id == "town") is { ProgressionLevel: 7, ProgressionFraction: 1 } &&
               board.Regions.Single(region => region.Id == "vmt_mine") is { ProgressionLevel: 0, ProgressionFraction: 0 } &&
               board.Regions.Single(region => region.Id == "farm") is { HasVisibleProgression: false, ProgressionLevel: null },
            "Skull levels use saved dungeon XP, native thresholds, zero for absent entries and each region's progression flag.");
        Assert(QuestBoardSnapshotReader.Read(content, document, "no-progression").Regions
                .Where(region => region.HasVisibleProgression).All(region => region.ProgressionLevel is null),
            "Unavailable progression data must not be presented as a known zero level.");
        var sewerDocument = document.DeepClone().AsObject();
        sewerDocument["base_root"]!["quests"]!["2"]!["dungeon"] = "vmt_sewer";
        Assert(QuestBoardSnapshotReader.Read(content, sewerDocument, "sewer-quest").Regions.Any(region => region.Id == "vmt_sewer"),
            "A region requiring a quest remains visible when its saved quest is present.");
        var emptyDocument = document.DeepClone().AsObject();
        emptyDocument["base_root"]!["quests"] = new JsonObject();
        Assert(QuestBoardSnapshotReader.Read(content, emptyDocument, "empty-board").Regions.Select(region => region.Id)
                .ToHashSet(StringComparer.Ordinal).SetEquals(["crypts", "farm", "vmt_mine", "town", "vmt_sewer"]),
            "An empty board retains real destinations, but not layout-only entries or unused internal scenes.");
        // Exceptional saved data is retained without claiming the game naturally
        // generates a board quest for an internal wave scene.
        var internalDocument = document.DeepClone().AsObject();
        internalDocument["base_root"]!["quests"]!["2"]!["dungeon"] = "ship_wave";
        var internalBoard = QuestBoardSnapshotReader.Read(content, internalDocument, "saved-internal-scene");
        Assert(internalBoard.Quests.Count == 3 && internalBoard.Quests[2].Dungeon == "ship_wave" &&
               internalBoard.Regions.Single(region => region.Id == "ship_wave").X == 1500,
            "Existing saved quests stay inspectable even when their region has no active entry template.");
        // Synthetic duplicate-definition contract: a discarded plot identity is
        // not another quest entry. This is not an observed farmest quest template.
        Write(game, "campaign/quest/quest.plot_quests.json", """
        {"plot_quests":[
          {"id":"plot_town_invasion_0","quest":{"dungeon":"town"}},
          {"id":"plot_town_invasion_0","quest":{"dungeon":"farmest"}}
        ]}
        """);
        var duplicatePlots = QuestBoardSnapshotReader.Read(content, emptyDocument, "duplicate-plot-destination");
        Assert(duplicatePlots.Regions.Any(region => region.Id == "town") &&
               duplicatePlots.Regions.All(region => region.Id != "farmest"),
            "Region entry lookup must use the quest constructor's first plot identity, not runtime provision lookup's last match.");
        var extra = document.DeepClone().AsObject();
        extra["base_root"]!["quests"]!["2"]!["dungeon"] = "missing_layout";
        var unpositioned = QuestBoardSnapshotReader.Read(content, extra, "missing-layout");
        Assert(!unpositioned.Regions.Single(region => region.Id == "missing_layout").HasLayout,
            "Missing map positions must be explicit instead of invented coordinates.");
        // Reader error handling only: this malformed fixture is not an observed Mod defect.
        Write(mod, "dungeons/vmt_mine/vmt_mine.dungeon.json", "[]");
        WriteFixtureManifest(mod);
        var malformed = QuestBoardSnapshotReader.Read(content, document, "malformed-region");
        Assert(malformed.Quests.Count == board.Quests.Count && !malformed.Regions.Any(region => region.Id == "vmt_mine") &&
               malformed.Issues.Any(issue => issue.Contains("vmt_mine.dungeon.json", StringComparison.Ordinal)),
            "Unreadable empty-region metadata must produce a diagnostic without discarding saved quests.");
        return (board, document);
    }

    private static async Task RunQuestBoardSnapshotRefreshAsync(string repositoryRoot, JsonObject questDocument)
    {
        var previousCodec = UseNativeCodec;
        UseNativeCodec = true;
        try
        {
            var f = BuildContractFixture(repositoryRoot);
            await SeedSyncFixtureAsync(f);
            var questPath = Path.Combine(f.ProfileRoot, "persist.quest.json");
            var decoded = Path.Combine(f.RunRoot, "quest-input.json");
            async Task SaveQuest(JsonObject value)
            {
                File.WriteAllText(decoded, value.ToJsonString());
                await f.Codec.EncodeAsync(decoded, questPath, null);
            }
            await SaveQuest(questDocument);
            var profile = SteamDiscovery.OpenProfile(f.ProfileRoot);
            var content = await ActiveContentResolver.ResolveAsync(profile, f.GameRoot, f.WorkshopRoot,
                f.AdditionalLocalModDirectory, f.Codec, f.RunRoot);
            var quantities = await QuantityItemCatalog.LoadAsync(content, f.Codec);
            var reader = new ProfileCatalogSnapshotReader(content, quantities, f.Codec, f.GameRoot,
                f.WorkshopRoot, f.AdditionalLocalModDirectory, f.RunRoot);
            var first = await reader.ReadAsync();
            Assert(first.QuestBoard?.Quests.Count == 3, "Initial town synchronization must decode the real quest board.");
            var second = await reader.ReadAsync();
            Assert(ReferenceEquals(first.QuestBoard, second.QuestBoard), "Unchanged sync must reuse the board.");
            var changed = questDocument.DeepClone().AsObject();
            changed["base_root"]!["quests"]!["0"]!["completion_reward"]!["items_definition"]!["items"]!["0"]!["amount"] = 6500;
            await SaveQuest(changed);
            var refreshed = await reader.ReadAsync();
            Assert(refreshed.QuestBoard!.Quests[0].Rewards[0].Amount == 6500 &&
                   !ReferenceEquals(first.QuestBoard, refreshed.QuestBoard), "Quest-only writes must refresh actual rewards.");
            var progressionPath = Path.Combine(f.ProfileRoot, "persist.progression.json");
            File.WriteAllText(decoded, """{"base_root":{"dungeon":{"crypts":{"xp":6}}}}""");
            await f.Codec.EncodeAsync(decoded, progressionPath, File.Exists(progressionPath) ? progressionPath : null);
            var progressionRefresh = await reader.ReadAsync();
            Assert(!ReferenceEquals(refreshed.QuestBoard, progressionRefresh.QuestBoard) &&
                   progressionRefresh.QuestBoard!.Quests[0].Rewards[0].Amount == 6500,
                "Progression-only writes must refresh the map without rerolling saved rewards.");
            WriteMultiMash(f.GameRoot, "campaign/quest/board.quest.types.json", """
            {"goals":[{"id":"unavailable_goal","type":"explore_room","data":{"percentage":0.1}}]}
            """);
            var resourceRefresh = await reader.ReadAsync(refreshContent: true);
            Assert(!ReferenceEquals(resourceRefresh.QuestBoard, refreshed.QuestBoard), "Definition changes must invalidate board metadata.");
            // Removing only the synthetic file exercises unavailable-board handling without touching user data.
            File.Delete(questPath);
            Assert((await reader.ReadAsync()).QuestBoard is null, "A missing quest file must not retain another board.");
        }
        finally { UseNativeCodec = previousCodec; }
    }

    private static void RunQuestBoardRealUiContracts(string root, QuestBoardSnapshot snapshot, string label)
    {
            var window = new MainWindow(Path.Combine(root, label + "-language.json"), gameRunningProbe: () => false)
            { Left = -20000, Top = -20000, ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual };
            try
            {
                window.Show();
                ((TabControl)window.FindName("CatalogTabs")).SelectedIndex = 4;
                var board = (QuestBoardPreview)window.FindName("QuestBoardPanel");
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(MainWindow).GetField("_catalogProfileDirectory", flags)!.SetValue(window, snapshot.ProfileDirectory);
                typeof(MainWindow).GetField("_syncReady", flags)!.SetValue(window, true);
                typeof(MainWindow).GetMethod("UpdateEnabledState", flags)!.Invoke(window, null);
                board.SetSnapshot(snapshot);
                // Configure after publication, as when the hidden board first becomes visible.
                board.Configure(Path.Combine(root, "different-root"));
                FlushLanguageBindings();
                window.UpdateLayout();
                Assert(board.Samples.Count == snapshot.Quests.Count && board.Samples.All(entry => entry.Choice.SavedQuest is not null),
                    "The loaded board must contain saved quests only.");
                var canvas = (Canvas)board.FindName("MapCanvas");
                Canvas PanelFor(QuestBoardRegionSnapshot region) => canvas.Children.OfType<Canvas>()
                    .Single(panel => Canvas.GetLeft(panel) == region.X && Canvas.GetTop(panel) == region.Y);
                var panels = canvas.Children.OfType<Canvas>().ToArray();
                var shownRegions = panels.SelectMany(panel => panel.Children.OfType<ComboBox>().SingleOrDefault() is { } choice
                    ? choice.Items.Cast<QuestBoardRegion>().Select(region => region.Id) : [(string)panel.Tag]);
                Assert(shownRegions.ToHashSet(StringComparer.Ordinal)
                        .SetEquals(snapshot.Regions.Where(region => region.HasLayout).Select(region => region.Id)) &&
                       panels.Length == snapshot.Regions.Where(region => region.HasLayout).Select(region => (region.X, region.Y)).Distinct().Count(),
                    "All valid regions must be reachable; shared positions use a single dropdown without overlapping panels.");
                CheckQuestBoardRegionSwitchPosition(board, snapshot, root, label);
                foreach (var region in snapshot.Regions.Where(region => region.HasLayout &&
                             !snapshot.Quests.Any(quest => quest.Dungeon == region.Id)))
                {
                    var panel = PanelFor(region);
                    if (panel.Children.OfType<ComboBox>().SingleOrDefault() is { } selector)
                    {
                        // Switch away first if this region is already displayed, then select it.
                        selector.SelectedIndex = (selector.SelectedIndex + 1) % selector.Items.Count;
                        PanelFor(region).Children.OfType<ComboBox>().Single().SelectedValue = region.Id;
                        panel = PanelFor(region);
                        Assert((string)panel.Tag == region.Id, "The dropdown must change the active region at the same map coordinates.");
                    }
                    else
                    {
                        var heading = panel.Children.OfType<Button>().Single();
                        Assert(((TextBlock)heading.Content).Text == region.Name && heading.IsEnabled,
                            "Empty regions must have a readable, clickable heading.");
                        heading.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    }
                    Assert(panel.Background is not null && panel.Width > 0 && panel.Height > 0,
                        "Empty regions must retain their right-click surface.");
                    Assert(board.SelectedKey is null && ((WrapPanel)board.FindName("RewardPanel")).Children.Count == 0,
                        "Selecting an empty region must clear the previous quest details.");
                    var emptyMenu = board.CreateTaskMenu(region.Id, null);
                    Assert(emptyMenu.Items.OfType<MenuItem>().Count() == 1 &&
                           emptyMenu.Items.OfType<MenuItem>().All(item => !item.IsEnabled),
                        "Empty regions offer only the existing read-only New menu, without enabling writes.");
                }
                typeof(QuestBoardPreview).GetMethod("SelectSample", flags)!.Invoke(board, [board.Samples[0].Key]);
                foreach (var language in new[] { "en", "zh-CN" })
                {
                    EditorText.Initialize(language, EditorText.SystemCulture);
                    board.RefreshLanguage();
                    RenderContractWindow(window, new Size(window.Width, window.Height), Path.Combine(root, label + "-" + language + ".png"));
                    Assert(((TextBlock)board.FindName("QuestName")).Text == snapshot.Quests[0].Name &&
                           ((WrapPanel)board.FindName("RewardPanel")).Children.Count == snapshot.Quests[0].Rewards.Count,
                        "The selected task's name and rewards must come from the snapshot in both languages.");
                    if (label == "native")
                    {
                        Assert(((Image)board.FindName("MapBackdrop")).Source is not null, "Late directory configuration must retain resolved native artwork.");
                        RenderContractElement((Canvas)board.FindName("MapCanvas"), new Size(1920, 1080),
                            Path.Combine(root, label + "-regions-" + language + ".png"));
                        var marker = canvas.Children.OfType<Canvas>().SelectMany(panel => panel.Children.OfType<Button>())
                            .Single(button => button.Tag is Guid key && key == board.SelectedKey);
                        var blood = ((Grid)marker.Content).Children.OfType<Canvas>().Single(part => Equals(part.Tag, "QuestSelection"));
                        var image = blood.Children.OfType<Image>().Single();
                        Assert(image.Source is not null && image.Width == image.Source.Width && image.Height == image.Source.Height &&
                               image.Width == 192 && Canvas.GetLeft(image) == -56 && Canvas.GetTop(image) == -56 &&
                               image.RenderTransform is System.Windows.Media.RotateTransform { Angle: >= 0 and < 360 },
                            "Selection uses the full native splatter texture and rotation, centered behind the task icon.");
                    }
                }
                foreach (var panel in canvas.Children.OfType<Canvas>())
                {
                    var region = snapshot.Regions.Single(region => region.Id == (string)panel.Tag);
                    var level = panel.Children.OfType<TextBlock>().SingleOrDefault(text => Equals(text.Tag, "DungeonLevel"));
                    Assert(region.HasVisibleProgression ? level?.Text == (region.ProgressionLevel?.ToString() ?? "?") : level is null,
                        "Region skull labels show the saved progression level only where the native layout enables it.");
                }
                var firstKey = board.Samples[0].Key;
                foreach (var entry in board.Samples.ToArray())
                {
                    typeof(QuestBoardPreview).GetMethod("SelectSample", flags)!.Invoke(board, [entry.Key]);
                    var saved = entry.Choice.SavedQuest!;
                    Assert(((TextBlock)board.FindName("CampingText")).Text == (saved.HasRandomCamping ? "×?" : $"×{saved.Firewood}"),
                        "Camp labels must show the native count or native random marker, never an unresolved placeholder.");
                }
                Assert(board.SelectedKey == board.Samples[^1].Key, "Every saved quest, including Mod tasks, must be selectable.");
                board.SetSnapshot(snapshot with { SourceSha256 = "new-hash" });
                Assert(board.SelectedKey == board.Samples[^1].Key, "Same-profile refresh must retain an existing selection.");
                var commands = board.CreateTaskMenu(board.Samples[0].RegionId, firstKey).Items.OfType<MenuItem>().ToArray();
                Assert(!commands[0].IsEnabled && commands[1].IsEnabled,
                    "Replacement needs a creation catalog; deletion uses the saved quest and attached edit handler.");
                board.RemoveSample(firstKey);
                board.ApplySample("crypts", new("cleanse"));
                Assert(board.Samples.Count == snapshot.Quests.Count, "Direct calls must not mutate real saved task presentation.");
                board.SetSnapshot(snapshot with { ProfileDirectory = Path.Combine(root, "profile_1") });
                Assert(board.SelectedKey == board.Samples[0].Key && board.SelectedKey != firstKey,
                    "Profile switching must reset selection and must not reuse another profile's task identity.");
                var orphan = snapshot.Quests[0] with { Dungeon = "no_layout", Difficulty = 0, Length = 0 };
                board.SetSnapshot(snapshot with { Quests = [orphan], Regions = [new("no_layout", 0, 0, 4, BilingualContentName.Empty) { HasLayout = false }] });
                Assert(((ComboBox)board.FindName("UnpositionedQuests")).Items.Count == 1,
                    "A task with unavailable layout must remain inspectable; uncommon numeric fields must not crash display.");
                board.SetSnapshot(snapshot with { Quests = [] });
                Assert(board.Samples.Count == 0 && ((WrapPanel)board.FindName("RewardPanel")).Children.Count == 0,
                    "An empty board must clear old details and rewards.");
                board.SetProfileLoaded(false);
                Assert(((Grid)board.FindName("BoardBody")).Visibility == Visibility.Collapsed && board.Samples.Count == 0,
                    "Unloading a profile must clear real task data.");
            }
            finally { window.Close(); }
    }

    private static void CheckQuestBoardRegionSwitchPosition(QuestBoardPreview board, QuestBoardSnapshot snapshot, string root, string label)
    {
        var town = snapshot.Regions.SingleOrDefault(region => region.Id == "town" && region.HasLayout);
        var sewer = snapshot.Regions.SingleOrDefault(region => region.Id == "vmt_sewer" && region.HasLayout);
        if (town is null || sewer is null || !snapshot.Regions.Any(region => region.Id == "crypts" && region.HasLayout)) return;
        Assert((town.X, town.Y) == (sewer.X, sewer.Y), "Town and sewer share their native map location.");
        var canvas = (Canvas)board.FindName("MapCanvas");
        var viewport = (Border)board.FindName("MapViewport");
        var backdrop = (Image)board.FindName("MapBackdrop");
        var offset = (System.Windows.Media.TranslateTransform)board.FindName("MapOffset");
        var scale = (System.Windows.Media.ScaleTransform)board.FindName("MapScale");
        void Select(string id)
        {
            canvas.Children.OfType<Canvas>().Single(panel => Canvas.GetLeft(panel) == town.X && Canvas.GetTop(panel) == town.Y)
                .Children.OfType<ComboBox>().Single().SelectedValue = id;
            FlushLanguageBindings();
            board.UpdateLayout();
        }
        double Top(FrameworkElement element) => element.TransformToAncestor(viewport).Transform(new Point()).Y;
        double HeaderTop() => Top(canvas.Children.OfType<Canvas>().Single(panel => Equals(panel.Tag, "crypts"))
            .Children.OfType<Button>().Single(button => button.Tag is not Guid));
        foreach (var mode in new[] { "default", "fit", "manual" })
        {
            Select("town");
            if (mode == "fit") ((Button)board.FindName("FitButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (mode == "manual") ((Button)board.FindName("ZoomInButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            FlushLanguageBindings();
            var before = (Offset: offset.Y, Scale: scale.ScaleY, Backdrop: Top(backdrop), Header: HeaderTop());
            Select("vmt_sewer");
            Assert((offset.Y, scale.ScaleY, Top(backdrop), HeaderTop()) == before,
                $"{mode}: switching from town to sewer must not move the map vertically or change its scale.");
            board.RefreshLanguage();
            FlushLanguageBindings();
            Assert(offset.Y == before.Offset, "Refreshing labels must keep the same vertical reference.");
            if (mode == "default" && label == "native")
                RenderContractElement((Grid)board.FindName("BoardBody"), ((Grid)board.FindName("BoardBody")).RenderSize,
                    Path.Combine(root, "native-sewer-stable.png"));
            Select("town");
            Assert((offset.Y, scale.ScaleY, Top(backdrop), HeaderTop()) == before,
                $"{mode}: switching back to town must preserve the same vertical position.");
            if (mode == "default" && label == "native")
                RenderContractElement((Grid)board.FindName("BoardBody"), ((Grid)board.FindName("BoardBody")).RenderSize,
                    Path.Combine(root, "native-town-stable.png"));
            Console.WriteLine($"{label}/{mode}: town/sewer map Y={offset.Y:F2}, scale={scale.ScaleY:P0}; unchanged.");
        }
        ((Button)board.FindName("FitButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
}
