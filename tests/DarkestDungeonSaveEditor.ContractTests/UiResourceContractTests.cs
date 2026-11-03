using System.Reflection;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DarkestDungeonSaveEditor.App;

internal static partial class ContractSuite
{
    public static async Task RunUiResourcesAsync(string repositoryRoot, string gameDirectory)
    {
        var root = Path.Combine(repositoryRoot, "artifacts", "ui-resources-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var (_, document) = RunQuestBoardReaderContracts(Path.Combine(root, "reader"));
        var profile = new SaveProfile("profile_0", Path.Combine(root, "profile_0"), "", "UI contract", DateTime.UtcNow);
        var baseContent = new ActiveContentSnapshot(profile, "base", [new("base", "Base", "base", gameDirectory, 0)], [], root, "", 0, "game");
        var native = QuestBoardSnapshotReader.Read(baseContent, document, "unchanged");
        Assert(native.Layout.Details == new QuestBoardDetailLayout() && native.Layout.Rewards == new QuestBoardRewardLayout(),
            "Shipped detail and reward fields must match the native defaults.");

        // Presentation fixtures change real layout fields, not gameplay definitions or
        // eligibility. They are not claims about a particular installed Mod.
        var mod = Path.Combine(root, "layout-mod");
        const string layoutPath = "campaign/town/quest_select/quest_select.layout.darkest";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(mod, layoutPath))!);
        var modified = File.ReadAllText(Path.Combine(gameDirectory, layoutPath))
            .Replace(".name_text_offset 200 140", ".name_text_offset 205 164")
            .Replace(".description_text_offset 34 194", ".description_text_offset 47 210")
            .Replace(".description_text_width 340", ".description_text_width 286")
            .Replace(".specifics_text_offset 226 390", ".specifics_text_offset 224 416")
            .Replace(".camping_text_offset 58 388", ".camping_text_offset 60 416")
            .Replace(".goals_text_offset 34 430", ".goals_text_offset 40 458")
            .Replace(".goal_description_text_start_offset 50 460", ".goal_description_text_start_offset 46 496")
            .Replace(".rewards_text_offset 200 530", ".rewards_text_offset 210 670")
            .Replace(".rewards_inventory_system_offset 22 562", ".rewards_inventory_system_offset 32 725")
            .Replace(".number_of_columns 4", ".number_of_columns 2")
            .Replace(".start_pos 20 28", ".start_pos 14 30")
            .Replace(".offset 80 160", ".offset 96 170")
            .Replace(".is_centred 1", ".is_centred 0");
        File.WriteAllText(Path.Combine(mod, layoutPath), modified);
        WriteFixtureManifest(mod);
        var active = baseContent with { Sources = [.. baseContent.Sources, new("local:layout", "Layout", "local", mod, 1)] };
        var changed = QuestBoardSnapshotReader.Read(active, document, "unchanged");
        Assert(changed.Layout.Details.Description == new QuestBoardPoint(47, 210) && changed.Layout.Details.DescriptionWidth == 286 &&
               changed.Layout.Details.RewardInventory == new QuestBoardPoint(32, 725) && changed.Layout.Rewards.Columns == 2 &&
               changed.Layout.Rewards.Spacing == new QuestBoardPoint(96, 170) && !changed.Layout.Rewards.Centered,
            "Enabled Mod details and reward-grid fields must override the base layout.");
        var originalDocument = document.ToJsonString();
        await RunWpfContractsAsync(async () =>
        {
            foreach (var language in new[] { "en", "zh-CN" })
            {
                EditorText.Initialize(language, EditorText.SystemCulture);
                VerifyConfiguredDetails(root, native, changed, language);
            }
            await VerifyBattleArtworkAsync(root, gameDirectory, baseContent);
        });
        Assert(document.ToJsonString() == originalDocument, "Artwork/layout presentation must not change saved quests.");
        Console.WriteLine("PASS: Effective detail/reward configuration, text ellipsis, reward scroll bounds, enabled artwork overlays and cache refresh.");
        Console.WriteLine("Artifacts: " + root);
    }

    private static void VerifyConfiguredDetails(string root, QuestBoardSnapshot native, QuestBoardSnapshot changed, string language)
    {
        var window = new MainWindow(Path.Combine(root, language + "-settings.json"), gameRunningProbe: () => false)
        { Left = -20000, Top = -20000, ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual };
        try
        {
            window.Show();
            ((TabControl)window.FindName("CatalogTabs")).SelectedIndex = 4;
            var board = (QuestBoardPreview)window.FindName("QuestBoardPanel");
            board.ProfileBrowseChecking += () => true;
            board.SetProfileLoaded(true);
            T Named<T>(string name) => (T)board.FindName(name);
            void Render(QuestBoardSnapshot snapshot, string? name = null)
            {
                board.SetSnapshot(snapshot);
                if (name is not null)
                    RenderContractWindow(window, new Size(1460, 920), Path.Combine(root, name + "-" + language + ".png"));
                else
                {
                    // Changed coordinates deliberately have no matching skin artwork.
                    // Measure them for configuration checks, not appearance screenshots.
                    window.UpdateLayout();
                    FlushLanguageBindings();
                    window.UpdateLayout();
                }
            }
            Render(native, "native-details");
            Assert(Canvas.GetLeft(Named<TextBlock>("QuestDescription")) == 34 &&
                   Canvas.GetTop(Named<TextBlock>("RewardHeading")) == 530,
                "Default details must use the native text anchors.");
            // Long localized text and multiple reward rows exercise display
            // clipping only. They do not invent an event or a reward generation rule.
            var quest = changed.Quests[0] with
            {
                LocalizedDescription = new(string.Concat(Enumerable.Repeat("这是一段用于检查省略显示的任务说明。", 30)),
                    string.Concat(Enumerable.Repeat("A long description to check ellipsis. ", 30))),
                LocalizedObjective = new(string.Concat(Enumerable.Repeat("完成任务目标并返回小镇。", 30)),
                    string.Concat(Enumerable.Repeat("Complete the objective and return to town. ", 30))),
                Rewards = Enumerable.Repeat(changed.Quests[0].Rewards[0], 5).ToArray()
            };
            var many = changed with { Quests = [quest] };
            Render(many);
            var description = Named<TextBlock>("QuestDescription");
            var objective = Named<TextBlock>("QuestObjective");
            var inventory = Named<ScrollViewer>("RewardInventoryScroll");
            var panel = Named<WrapPanel>("RewardPanel");
            Assert(Canvas.GetLeft(description) == 47 && Canvas.GetTop(description) == 210 && description.Width == 286 &&
                   Canvas.GetTop(Named<TextBlock>("CampingText")) == 416 && Canvas.GetTop(Named<TextBlock>("ObjectiveHeading")) == 458 &&
                   Canvas.GetTop(objective) == 496 && Canvas.GetTop(Named<TextBlock>("RewardHeading")) == 670,
                "Descriptions, camping, objectives and reward headings must use effective anchors.");
            Assert(Canvas.GetLeft(inventory) == 46 && Canvas.GetTop(inventory) == 755 && panel.Width == 192 &&
                   panel.ItemWidth == 96 && panel.ItemHeight == 170 && inventory.ScrollableHeight > 0,
                "The configured grid must use two columns at its configured origin, with vertical scrolling for extra rows.");
            Assert(description.Height == 180 && objective.Height == 150 &&
                   description.TextTrimming == TextTrimming.CharacterEllipsis && objective.TextTrimming == TextTrimming.CharacterEllipsis &&
                   description.Parent is Canvas && objective.Parent is Canvas,
                "Long text must stay in configured line-aligned areas with ellipsis, without individual scroll viewers.");
            var textHeights = new[] { description.Height, objective.Height };
            typeof(QuestBoardPreview).GetMethod("SetExpandedDetails", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(board, [true]);
            Render(many);
            Assert(description.Height == textHeights[0] && objective.Height == textHeights[1] &&
                   description.TextTrimming == TextTrimming.CharacterEllipsis && objective.TextTrimming == TextTrimming.CharacterEllipsis,
                "Full-map details must keep the same text limits and ellipsis, rather than expanding long text.");
            typeof(QuestBoardPreview).GetMethod("SetExpandedDetails", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(board, [false]);
            Render(many);
            inventory.ScrollToBottom();
            Named<ScrollViewer>("RewardsScroll").ScrollToBottom();
            FlushLanguageBindings();
            Assert(inventory.VerticalOffset > 0 && inventory.ScrollableWidth == 0,
                "All reward rows must remain accessible without horizontal scrolling.");
            var viewport = Named<Canvas>("DetailsViewport");
            var bottom = inventory.TransformToAncestor(viewport).TransformBounds(new Rect(inventory.RenderSize)).Bottom;
            Assert(bottom <= viewport.Height && viewport.Height > 610,
                "Rewards below the former vanilla crop must be inside the detail viewport.");
            foreach (var centered in new[] { false, true })
            {
                var centeredMany = many with { Layout = many.Layout with { Rewards = many.Layout.Rewards with { Centered = centered } } };
                Render(centeredMany);
                Assert(((TranslateTransform)panel.Children[4].RenderTransform).X == (centered ? 48 : 0),
                    "Native centering must also center the partial final row, not just a single short inventory.");
                var shortRow = many with { Quests = [quest with { Rewards = [quest.Rewards[0]] }],
                    Layout = many.Layout with { Rewards = many.Layout.Rewards with { Centered = centered } } };
                Render(shortRow);
                Assert(Canvas.GetLeft(inventory) == (centered ? 94 : 46),
                    "The centering flag must shift a short row within the configured two-column grid.");
            }
            Render(native, "restored-details");
            Assert(description.Width == 340 && Canvas.GetTop(inventory) == 590 && panel.ItemWidth == 80 && panel.ItemHeight == 160,
                "Switching away from a layout Mod must discard its cached detail/grid values.");
            Render(native with { Quests = [native.Quests[0] with
                { LocalizedDescription = quest.LocalizedDescription, LocalizedObjective = quest.LocalizedObjective }] }, "long-text");
            Assert(description.Height == 180 && objective.Height == 50,
                "Long text must not increase the original artwork's text areas.");
        }
        finally { window.Close(); }
    }

    private static async Task VerifyBattleArtworkAsync(string root, string game, ActiveContentSnapshot native)
    {
        const string room = "panels/icons_map/room_empty.png";
        const string boss = "panels/icons_map/room_boss.png";
        var dlc = Path.Combine(root, "art-dlc");
        var mod = Path.Combine(root, "art-mod");
        foreach (var directory in new[] { dlc, mod })
        {
            Directory.CreateDirectory(Path.Combine(directory, "panels/icons_map"));
            File.Copy(Path.Combine(game, room), Path.Combine(directory, room));
        }
        WriteFixtureManifest(mod);
        File.Copy(Path.Combine(game, boss), Path.Combine(mod, boss)); // Deliberately unregistered artwork.
        var withDlc = native with { Sources = [.. native.Sources, new("dlc:art", "DLC", "dlc", dlc, 1)] };
        var withMod = withDlc with { Sources = [.. withDlc.Sources, new("local:art", "Mod", "local", mod, 2)] };
        static string Full(string directory, string path) => Path.GetFullPath(Path.Combine(directory, path));
        var issues = new List<string>();
        var paths = BattleMapArtwork.Resolve(withMod, issues);
        Assert(issues.Count == 0 && paths[room] == Full(mod, room) && paths[boss] == Full(game, boss),
            "Only enabled, registered Mod art may override the base/DLC image.");
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var view = new BattleMapView { UsesSharedProfileMonitor = true };
        var configure = typeof(BattleMapView).GetMethod("ConfigureOriginalMapAssets", flags)!;
        var image = typeof(BattleMapView).GetMethod("TryLoadGameImage", flags)!;
        BitmapImage? Read(string path) => image.Invoke(view, [path]) as BitmapImage;
        void Configure(ActiveContentSnapshot content) => configure.Invoke(view, [game, content, null]);
        Configure(withMod);
        var first = Read(room)!;
        Assert(first.UriSource.LocalPath == Full(mod, room), "Map rendering must decode the resolved Mod image.");
        Assert(ReferenceEquals(first, Read(room)), "Repeated drawing must use the decoded image cache.");
        typeof(BattleMapView).GetField("_profileDirectory", flags)!.SetValue(view, native.Profile.ProfileDirectory);
        typeof(BattleMapView).GetField("_snapshotReader", flags)!.SetValue(view, new BattleMapSnapshotReader(DsonSaveCodec.CreateNative()));
        var beforeContent = ProfileCatalogContentFingerprint.Capture(withMod.Sources);
        var beforeArt = BattleMapArtwork.Capture(withMod.Sources);
        File.Copy(Path.Combine(game, "panels/icons_map/room_curio.png"), Path.Combine(mod, room), overwrite: true);
        var afterArt = BattleMapArtwork.Capture(withMod.Sources);
        Assert(beforeArt != afterArt && beforeContent == ProfileCatalogContentFingerprint.Capture(withMod.Sources),
            "Replacing a map icon must change only the artwork signal.");
        await view.SynchronizeProfileAsync(new(withMod, "configuration", new([], [], "estate"),
            new Dictionary<string, string?>(), DateTime.UtcNow) { ContentFingerprint = beforeContent,
                BattleMapArtworkFingerprint = afterArt, BattleMapImagePaths = BattleMapArtwork.Resolve(withMod, []) },
            // Disabled artwork-only experiment passed contentChanged: false.
            contentChanged: true, CancellationToken.None);
        Assert(!ReferenceEquals(first, Read(room)), "A same-path image edit must invalidate drawing through the shared refresh signal.");
        first = Read(room)!;
        Configure(withMod);
        Assert(!ReferenceEquals(first, Read(room)), "Reloading unchanged paths must invalidate decoded pixels.");

        await view.SynchronizeProfileAsync(new(withDlc, "configuration", new([], [], "estate"),
            new Dictionary<string, string?>(), DateTime.UtcNow) { BattleMapArtworkFingerprint = BattleMapArtwork.Capture(withDlc.Sources),
                BattleMapImagePaths = BattleMapArtwork.Resolve(withDlc, []) }, contentChanged: true, CancellationToken.None);
        Assert(Read(room)!.UriSource.LocalPath == Full(dlc, room),
            "Shared content synchronization must drop disabled Mod art, even before returning to a raid.");
        File.Delete(Path.Combine(mod, room)); // The manifest still registers the missing winning provider.
        Configure(withMod);
        Assert(Read(room) is null, "A missing registered winner must not silently resurrect base artwork.");
        Configure(native);
        Assert(Read(room)!.UriSource.LocalPath == Full(game, room), "Switching to a base-only profile must clear the overlay.");
        view.ClearProfile();
        Assert(Read(room) is null, "Unloading must clear artwork paths from the previous profile.");
    }
}
