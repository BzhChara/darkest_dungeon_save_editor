using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using DarkestDungeonSaveEditor.App;

internal static partial class ContractSuite
{
    public static async Task RunEditorLocalizationOnlyAsync(string repositoryRoot)
    {
        var runRoot = Path.Combine(repositoryRoot, "workspaces", "localization-contracts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(runRoot);
        VerifyEditorLocalizationResources(repositoryRoot, runRoot);
        RunUiContracts(repositoryRoot);
        await RunWpfContractsAsync(() => VerifyEditorLocalizationUiAsync(repositoryRoot, runRoot));
        Console.WriteLine($"Artifacts: {runRoot}");
    }

    private static XDocument LoadLocalizedContractXaml(string path)
    {
        var document = XDocument.Load(path);
        foreach (var attribute in document.Descendants().Attributes())
        {
            var match = Regex.Match(attribute.Value, @"^\{local:Text (?<key>\w+)\}$");
            if (match.Success) attribute.Value = EditorText.Get(match.Groups["key"].Value);
        }
        return document;
    }

    private static void VerifyEditorLocalizationResources(string repositoryRoot, string runRoot)
    {
        var directory = Path.Combine(repositoryRoot, "src/DarkestDungeonSaveEditor.Core/Localization");
        Dictionary<string, string> Read(string file) => XDocument.Load(Path.Combine(directory, file))
            .Root!.Elements("data").ToDictionary(e => (string)e.Attribute("name")!, e => (string)e.Element("value")!);
        var english = Read("Strings.resx");
        var chinese = Read("Strings.zh-CN.resx");
        Assert(english.Count > 1000 && english.Keys.Order().SequenceEqual(chinese.Keys.Order()),
            "English and Chinese must have identical, complete resource keys.");
        foreach (var (key, value) in english)
        {
            Assert(!string.IsNullOrWhiteSpace(value) && !string.IsNullOrWhiteSpace(chinese[key]), $"Resource {key} must not be blank.");
            var pattern = @"\{\d+(?:[^{}]*)\}";
            Assert(Regex.Matches(value, pattern).Select(m => m.Value).Order().SequenceEqual(
                    Regex.Matches(chinese[key], pattern).Select(m => m.Value).Order()),
                $"Resource {key} must preserve every placeholder and format specifier.");
            _ = System.Text.CompositeFormat.Parse(value);
            _ = System.Text.CompositeFormat.Parse(chinese[key]);
            Assert(key == "Language_Chinese" || !Regex.IsMatch(value, @"\p{IsCJKUnifiedIdeographs}"),
                $"English resource {key} must not contain untranslated Chinese UI text.");
        }
        foreach (var path in Directory.EnumerateFiles(Path.Combine(repositoryRoot, "src"), "*", SearchOption.AllDirectories)
            .Where(path => Path.GetExtension(path) is ".cs" or ".xaml" && !path.Contains("\\obj\\") && !path.Contains("\\bin\\")))
        {
            var source = File.ReadAllText(path);
            foreach (Match match in Regex.Matches(source, "EditorText\\.(?:Get|Format)\\(\"(?<key>\\w+)\"|\\{local:Text (?<key>\\w+)\\}"))
                Assert(english.ContainsKey(match.Groups["key"].Value), $"Unknown resource in {path}: {match.Value}");
        }

        var originalUi = CultureInfo.CurrentUICulture;
        var originalDefault = CultureInfo.DefaultThreadCurrentUICulture;
        var originalFormat = CultureInfo.CurrentCulture;
        try
        {
            Assert(EditorText.ResolveCulture("system", CultureInfo.GetCultureInfo("zh-CN")).Name == "zh-CN" &&
                   EditorText.ResolveCulture("system", CultureInfo.GetCultureInfo("zh-TW")).Name == "zh-CN" &&
                   EditorText.ResolveCulture("system", CultureInfo.GetCultureInfo("fr-FR")).Name == "en" &&
                   EditorText.ResolveCulture("en", CultureInfo.GetCultureInfo("zh-CN")).Name == "en" &&
                   EditorText.ResolveCulture("zh-CN", CultureInfo.GetCultureInfo("en-US")).Name == "zh-CN",
                "Explicit language wins; Chinese system languages use Simplified Chinese; unsupported languages fall back to English.");
            foreach (var (language, values) in new[] { ("en", english), ("zh-CN", chinese) })
            {
                EditorText.Initialize(language, originalUi);
                Assert(Equals(CultureInfo.CurrentCulture, originalFormat), "Changing UI language must not change numeric/file culture.");
                foreach (var (key, value) in values)
                    Assert(EditorText.Get(key).Replace("\r\n", "\n", StringComparison.Ordinal) == value,
                        $"Compiled resource {key} must resolve in {language} (allowing Windows resource newline normalization).");
                Assert(Task.Run(() => EditorText.Get("MainWindow_001")).GetAwaiter().GetResult() == values["MainWindow_001"],
                    "Asynchronous work must resolve the same UI language.");
                Assert(EditorText.ContentName(new BilingualContentName("中文", "English"), "item_id") ==
                       (language == "en" ? "English" : "中文") &&
                       EditorText.ContentName(new BilingualContentName("", "English"), "item_id") == "English" &&
                       EditorText.ContentName(BilingualContentName.Empty, "item_id") == "item_id",
                    "Content names must prefer the selected language, then the other language, then the unmodified ID.");

                var slot = CatalogIssueCode.FormatEncounterSlots(@"E:\fixture\encounter.darkest", 7, 3);
                var residue = CatalogIssueCode.TownRaidResidue + EditorText.Get("QuantityItemCatalog_001");
                var partial = new List<string>();
                var diagnostics = new LocalizationEntryDiagnostics(@"E:\fixture\names.loc");
                diagnostics.Skip("entry_id", "invalid UTF-8");
                diagnostics.AppendTo(partial);
                var inputs = new[] { slot, residue, partial.Single() };
                var otherPathCase = inputs.Select(issue => issue.Replace(@"E:\fixture", @"e:\fixture", StringComparison.Ordinal)).ToArray();
                var logs = CatalogLogDiagnostics.Summarize([("first", inputs), ("second", otherPathCase)]);
                Assert(logs.Count == 3 && logs.Count(e => e.Level == DiagnosticLogLevel.Information) == 2 &&
                       logs.Count(e => e.Level == DiagnosticLogLevel.Warning) == 1 &&
                       logs.All(e => e.Message.Contains("first", StringComparison.Ordinal) && e.Message.Contains("second", StringComparison.Ordinal)) &&
                       logs.All(e => !e.Message.Contains("[DDSE:", StringComparison.Ordinal)) &&
                       (language != "en" || logs.All(e => !Regex.IsMatch(e.Message, @"\p{IsCJKUnifiedIdeographs}"))),
                    "Diagnostic codes must preserve grouping/severity in both languages without leaking internal markers.");
            }
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");
            Assert(EditorText.Get("MainWindow_001") == "Darkest Dungeon Save Editor", "Unsupported resource cultures must fall back to English.");

            var settings = Path.Combine(runRoot, "settings", "language.json");
            Assert(EditorLanguageSettings.Load(settings, out var loadError) == "system" && loadError is null && !File.Exists(settings),
                "Missing settings must use the system language without writing any file.");
            foreach (var preference in new[] { "en", "zh-CN", "system" })
            {
                EditorLanguageSettings.Save(settings, preference);
                Assert(EditorLanguageSettings.Load(settings, out loadError) == preference && loadError is null &&
                       !Directory.EnumerateFiles(Path.GetDirectoryName(settings)!, "*.tmp").Any(),
                    "Language preferences must roundtrip atomically without leftover temporary files.");
            }
            foreach (var invalid in new[] { "{broken", "{\"Language\":\"unsupported\"}", "null" })
            {
                File.WriteAllText(settings, invalid);
                Assert(EditorLanguageSettings.Load(settings, out loadError) == "system" && loadError is not null && File.ReadAllText(settings) == invalid,
                    "Invalid preferences must fall back without overwriting the file.");
            }
            try { EditorLanguageSettings.Save(settings, "unsupported"); throw new Exception("Invalid preference accepted."); }
            catch (ArgumentException) { }
            File.WriteAllText(Path.Combine(runRoot, "blocked-settings"), "keep");
            try { EditorLanguageSettings.Save(Path.Combine(runRoot, "blocked-settings", "language.json"), "en"); throw new Exception("Blocked write accepted."); }
            catch (IOException) { }
            Assert(File.ReadAllText(Path.Combine(runRoot, "blocked-settings")) == "keep", "A failed preference write must preserve existing data.");
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalUi;
            CultureInfo.DefaultThreadCurrentUICulture = originalDefault;
        }
        Console.WriteLine("PASS: bilingual compiled resources, placeholders, fallback, async culture, invariant diagnostic grouping and atomic language settings.");
    }

    private static Task VerifyEditorLocalizationUiAsync(string repositoryRoot, string runRoot)
    {
        var originalUi = CultureInfo.CurrentUICulture;
        var originalDefault = CultureInfo.DefaultThreadCurrentUICulture;
        var originalResources = Application.Current.Resources;
        try
        {
            foreach (var language in new[] { "en", "zh-CN" })
            {
                EditorText.Initialize(language, originalUi);
                Application.Current.Resources = LoadContractTheme(repositoryRoot);
                var settings = Path.Combine(runRoot, "ui-" + language, "language.json");
                EditorLanguageSettings.Save(settings, language);
                var window = new MainWindow(settings);
                try
                {
                    ((Image)window.FindName("TitleLogoImage")).RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                    Assert(window.Title == (language == "en" ? "Darkest Dungeon Save Editor" : "Darkest Dungeon 存档修改器") &&
                           ((Button)window.FindName("LoadCatalogButton")).Content as string == EditorText.Get("MainWindow_021"),
                        "The actual compiled main window must resolve its title and load action in both languages.");
                    var languageButton = (Button)window.FindName("LanguageButton");
                    var languageOptions = languageButton.ContextMenu.Items.OfType<MenuItem>()
                        .Where(item => item.Tag is string).ToArray();
                    Assert(languageOptions.Count(item => item.IsChecked) == 1 &&
                           (string)languageOptions.Single(item => item.IsChecked).Tag == language,
                        "The compact language menu must show exactly one saved preference.");
                    foreach (var gridName in new[] { "ItemGrid", "TrinketGrid", "HeroGrid" })
                    {
                        var grid = (DataGrid)window.FindName(gridName);
                        Assert(grid.ColumnFromDisplayIndex(language == "en" ? 1 : 2).Header as string == "English",
                            "English names must precede Chinese names in English mode without removing either column.");
                    }
                    var previousTitle = window.Title;
                    var selectedPreference = language == "en" ? "zh-CN" : "en";
                    var selectedItem = languageOptions.Single(item => (string)item.Tag == selectedPreference);
                    selectedItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, selectedItem));
                    Assert(EditorLanguageSettings.Load(settings, out _) == selectedPreference &&
                           languageOptions.Count(item => item.IsChecked) == 1 && selectedItem.IsChecked &&
                           window.Title == previousTitle && CultureInfo.CurrentUICulture.Name == language,
                        "Language selection must save the next-start preference without changing the running UI or loaded state.");
                    selectedItem.IsChecked = false;
                    selectedItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, selectedItem));
                    Assert(languageOptions.Count(item => item.IsChecked) == 1 && selectedItem.IsChecked,
                        "Clicking the saved language again must not leave the menu with no selection.");
                    foreach (var size in new[] { new Size(1440, 900), new Size(1120, 720) })
                        RenderContractWindow(window, size, Path.Combine(runRoot, $"main-{language}-{size.Width}.png"));
                    RenderContractElement(languageButton.ContextMenu, new Size(240, 164), Path.Combine(runRoot, $"language-menu-{language}.png"));
                    var tabs = (TabControl)window.FindName("CatalogTabs");
                    for (var index = 1; index < tabs.Items.Count; index++)
                    {
                        tabs.SelectedIndex = index;
                        RenderContractWindow(window, new Size(1120, 720), Path.Combine(runRoot, $"main-{language}-tab-{index}.png"));
                    }
                    _ = new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
                    var support = new SupportDialog(window);
                    try { RenderContractWindow(support, new Size(support.Width, support.Height), Path.Combine(runRoot, $"support-{language}.png")); }
                    finally { support.Close(); }
                    var encounter = new BattleEncounterSelectionDialog([], 1, "Fixture");
                    try { RenderContractWindow(encounter, new Size(encounter.Width, encounter.Height), Path.Combine(runRoot, $"encounter-{language}.png")); }
                    finally { encounter.Close(); }
                    var attachment = new BattleRoomAttachmentSelectionDialog([], EditorText.Get("BattleMapView_CellVisuals_012"));
                    try { RenderContractWindow(attachment, new Size(attachment.Width, attachment.Height), Path.Combine(runRoot, $"attachment-{language}.png")); }
                    finally { attachment.Close(); }
                    var hero = new HeroClassDefinition("hero_id", "fixture", "", true, 4, null, 20,
                        [new HeroLevelProfile(0, 0, 0, 0, 20)], [], "", 1, [], [], [], [], [], [], [], [], false, ["fixture"]);
                    var catalog = new HeroClassCatalogResult("darkest", [0], [hero], [], [], ["Test"], []);
                    var quirks = new InitialQuirkSelectionDialog(catalog, hero, 0, []);
                    try { RenderContractWindow(quirks, new Size(quirks.Width, quirks.Height), Path.Combine(runRoot, $"quirks-{language}.png")); }
                    finally { quirks.Close(); }
                    var confirmation = (ThemedDialog)Activator.CreateInstance(typeof(ThemedDialog),
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null,
                        [window, EditorText.Get("MainWindow_EditWorkflow_056"), EditorText.Get("MainWindow_EditWorkflow_057"),
                            ThemedDialogKind.Warning, true], null)!;
                    try
                    {
                        Assert(((Button)confirmation.FindName("CancelButton")).IsDefault && !((Button)confirmation.FindName("ConfirmButton")).IsDefault,
                            "Localized save confirmation must retain the safe default action.");
                        RenderContractWindow(confirmation, new Size(680, 420), Path.Combine(runRoot, $"confirmation-{language}.png"));
                    }
                    finally { confirmation.Close(); }
                }
                finally { window.Close(); }
            }
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalUi;
            CultureInfo.DefaultThreadCurrentUICulture = originalDefault;
            Application.Current.Resources = originalResources;
        }
        Console.WriteLine("PASS: actual WPF windows in English/Chinese, persisted next-start selection and layout renders at normal/minimum sizes.");
        return Task.CompletedTask;
    }

    private static void RenderContractWindow(Window window, Size size, string path)
    {
        window.ApplyTemplate();
        var content = (FrameworkElement)window.Content;
        RenderContractElement(content, size, path);
    }

    private static void RenderContractElement(FrameworkElement content, Size size, string path)
    {
        content.Measure(size);
        content.Arrange(new Rect(size));
        content.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
