using System.IO;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using DarkestDungeon.ModOrder;
using DarkestDungeon.ModOrder.App;
using DarkestDungeonSaveEditor.Core;

internal static class Program
{
    private static int _passed;
    private static readonly DsonSaveCodec Codec = DsonSaveCodec.CreateNative();

    private static async Task<int> Main(string[] args)
    {
        try
        {
            if (args is ["--ui"]) { RenderUi(); Console.WriteLine("PASS bilingual WPF layout smoke"); return 0; }
            if (args.Length is 4 or 5 && args[0] == "--live") { await Live(args); return 0; }
            var tests = new (string Name, Func<Fixture, Task> Run)[]
            {
                ("native order, hidden metadata, local origin and disabled preservation", async f =>
                {
                    var plan = await Converter.PreviewAsync(f.Request());
                    Check(plan.CanExport && plan.Rows.Where(r => r.Enabled).Select(r => r.Mo2Folder).SequenceEqual(new[] { "本地副本", "Workshop renamed" }), "numeric save order must be used without reversing");
                    var before = f.Snapshot();
                    var output = await Converter.ExportAsync(plan, f.Output);
                    Check(File.ReadAllLines(Path.Combine(output, "modlist.txt")).Skip(1).SequenceEqual(new[] { "+本地副本", "-Group_separator", "+Workshop renamed", "-Disabled", "-New" }), "enabled slots, separator and disabled order");
                    Check(File.ReadAllBytes(Path.Combine(output, "original/modlist.txt")).SequenceEqual(File.ReadAllBytes(f.ListPath)), "exact original list backup");
                    f.Unchanged(before);
                }),
                ("reverse binary export changes only the Mod list and preserves revision", async f =>
                {
                    await f.Binary();
                    var before = f.Snapshot();
                    var originalView = await Decode(f.SavePath);
                    var plan = await Converter.PreviewAsync(f.Request(ConversionDirection.Mo2ToSave));
                    Check(plan.CanExport, string.Join("; ", plan.Errors));
                    var output = await Converter.ExportAsync(plan, f.Output);
                    var save = Path.Combine(output, "persist.game.json");
                    Check(DsonSaveCodec.IsDson(save), "binary remains binary");
                    var actual = await Decode(save);
                    var mods = actual["base_root"]!["applied_ugcs_1_0"]!.AsObject();
                    Check(mods.Select(p => p.Key).SequenceEqual(new[] { "0", "1" }), "continuous numeric order");
                    Check(mods["0"]!["name"]!.GetValue<string>() == "123456789012" && mods["0"]!["source"]!.GetValue<string>() == "Steam", "Workshop native identity");
                    Check(mods["1"]!["name"]!.GetValue<string>() == "本地标题" && mods["1"]!["source"]!.GetValue<string>() == "mod_local_source", "local origin overrides PublishedFileId");
                    Check(mods["1"]!["extension"]!.GetValue<string>() == "keep", "retained Mod extension fields");
                    actual["base_root"]!.AsObject().Remove("applied_ugcs_1_0");
                    originalView["base_root"]!.AsObject().Remove("applied_ugcs_1_0");
                    Check(JsonNode.DeepEquals(originalView, actual), "all unrelated fields preserved");
                    Check(File.ReadAllBytes(save).AsSpan(4, 4).SequenceEqual(File.ReadAllBytes(f.SavePath).AsSpan(4, 4)), "binary revision");
                    Check(File.ReadAllBytes(Path.Combine(output, "original/persist.game.json")).SequenceEqual(File.ReadAllBytes(f.SavePath)), "exact original binary backup");
                    f.Unchanged(before);
                }),
                ("reverse then forward preserves enabled order", async f =>
                {
                    var back = await Converter.PreviewAsync(f.Request(ConversionDirection.Mo2ToSave));
                    await Converter.ExportAsync(back, f.Output);
                    Check(!DsonSaveCodec.IsDson(Path.Combine(f.Output, "persist.game.json")), "decoded input remains decoded");
                    var forward = await Converter.PreviewAsync(f.Request() with { SavePath = Path.Combine(f.Output, "persist.game.json") });
                    Check(forward.CanExport && forward.Rows.Where(r => r.Enabled).Select(r => r.Mo2Folder).SequenceEqual(new[] { "Workshop renamed", "本地副本" }), "round-trip identities and priority");
                }),
                ("merge-only save cannot reconstruct MO2 membership", async f =>
                {
                    f.SetApplied(new JsonObject { ["0"] = Entry("merge mod", "mod_local_source") });
                    var forward = await Converter.PreviewAsync(f.Request());
                    Check(!forward.CanExport && forward.Errors.Single().Contains("merge mod"), "forward merge guard");
                    var reverse = await Converter.PreviewAsync(f.Request(ConversionDirection.Mo2ToSave));
                    Check(reverse.CanExport && reverse.EnabledCount == 2, "reverse can replace merge container with individuals");
                }),
                ("zero enabled Mods is explicit and exportable", async f =>
                {
                    f.SetApplied(new JsonObject());
                    var plan = await Converter.PreviewAsync(f.Request());
                    Check(plan.CanExport && plan.EnabledCount == 0 && plan.Rows.All(r => !r.Enabled), "empty native list");
                    f.List("-Workshop renamed", "-本地副本");
                    var reverse = await Converter.PreviewAsync(f.Request(ConversionDirection.Mo2ToSave));
                    await Converter.ExportAsync(reverse, f.Output);
                    Check((await Decode(Path.Combine(f.Output, "persist.game.json")))["base_root"]!["applied_ugcs_1_0"]!.AsObject().Count == 0, "empty reverse list");
                }),
                ("missing MO2 match is reported", async f =>
                {
                    f.SetApplied(new JsonObject { ["0"] = Entry("987654321", "Steam") });
                    await Blocked(f, ConversionDirection.SaveToMo2, "987654321");
                }),
                ("duplicate MO2 identities block forward conversion", async f =>
                {
                    f.Mod("Duplicate", "123456789012", "Other", true);
                    await Blocked(f, ConversionDirection.SaveToMo2, "Multiple MO2");
                }),
                ("enabled MO2 folder missing blocks reverse", async f =>
                {
                    f.List("+Missing");
                    await Blocked(f, ConversionDirection.Mo2ToSave, "Missing");
                }),
                ("conversion does not require original Workshop or local directories", async f =>
                {
                    Check(!Directory.Exists(Path.Combine(f.Root, "game")) && !Directory.Exists(Path.Combine(f.Root, "workshop")), "fixture has only MO2 metadata");
                    var plan = await Converter.PreviewAsync(f.Request(ConversionDirection.Mo2ToSave));
                    Check(plan.CanExport && plan.EnabledCount == 2, "MO2 metadata suffices");
                }),
                ("manual metadata with title and published ID is ambiguous in reverse", async f =>
                {
                    f.Write("mods/本地副本/meta.ini", "[General]\nmodid=789\nurl=https://example.invalid\n");
                    await Blocked(f, ConversionDirection.Mo2ToSave, "ambiguous");
                    Check((await Converter.PreviewAsync(f.Request())).CanExport, "forward save supplies source kind");
                }),
                ("manual project with local title and no published ID is resolved", async f =>
                {
                    f.Write("mods/本地副本/meta.ini", "[General]\nmodid=999888777\n");
                    f.Write("mods/本地副本/project.xml.mohidden", Project("本地标题", "0"));
                    Check((await Converter.PreviewAsync(f.Request(ConversionDirection.Mo2ToSave))).CanExport, "project identity resolves manual Mod");
                }),
                ("conflicting metadata blocks matching", async f =>
                {
                    f.Write("mods/Workshop renamed/project.xml", Project("Workshop title", "777"));
                    await Blocked(f, ConversionDirection.Mo2ToSave, "disagrees");
                }),
                ("visible and hidden project identities must agree", async f =>
                {
                    f.Write("mods/本地副本/project.xml", Project("Wrong title", "999888777"));
                    await Blocked(f, ConversionDirection.Mo2ToSave, "conflicting");
                }),
                ("duplicate enabled reverse identities block", async f =>
                {
                    f.Mod("Second copy", "123456789012", "Other", true);
                    f.List("+Workshop renamed", "+Second copy");
                    await Blocked(f, ConversionDirection.Mo2ToSave, "duplicate native");
                }),
                ("disabled broken metadata does not block unrelated Mods", async f =>
                {
                    f.Write("mods/Disabled/project.xml", "<broken");
                    Check((await Converter.PreviewAsync(f.Request())).CanExport, "forward ignores unused broken metadata");
                    Check((await Converter.PreviewAsync(f.Request(ConversionDirection.Mo2ToSave))).CanExport, "reverse ignores disabled metadata");
                }),
                ("numeric gaps and duplicate numeric keys", async f =>
                {
                    f.SetApplied(new JsonObject { ["0"] = Entry("本地标题", "mod_local_source"), ["00"] = Entry("123456789012", "Steam") });
                    await Throws(() => Converter.PreviewAsync(f.Request()), "numeric order");
                }),
                ("unknown native sources and malformed values are rejected", async f =>
                {
                    f.SetApplied(new JsonObject { ["0"] = Entry("123", "unknown") });
                    await Throws(() => Converter.PreviewAsync(f.Request()), "Unknown native");
                    f.SetApplied(new JsonObject { ["-1"] = Entry("123", "Steam") });
                    await Throws(() => Converter.PreviewAsync(f.Request()), "Malformed");
                    f.SetApplied(new JsonArray());
                    await Throws(() => Converter.PreviewAsync(f.Request()), "must be an object");
                }),
                ("duplicate JSON keys and invalid UTF8 are rejected", async f =>
                {
                    f.Write("save/persist.game.json", "{\"base_root\":{\"applied_ugcs_1_0\":{},\"applied_ugcs_1_0\":{}}}");
                    await Throws(() => Converter.PreviewAsync(f.Request()), "Duplicate");
                    File.WriteAllBytes(f.ListPath, [0xff, 0xff]);
                    await Throws(() => Converter.PreviewAsync(f.Request()), "translate");
                }),
                ("path traversal, foreign entries and duplicate folders rejected", async f =>
                {
                    f.List("+../outside"); await Throws(() => Converter.PreviewAsync(f.Request()), "invalid");
                    f.List("*Unmanaged"); await Throws(() => Converter.PreviewAsync(f.Request()), "unsupported");
                    f.List("+Disabled", "-disabled"); await Throws(() => Converter.PreviewAsync(f.Request()), "duplicate");
                }),
                ("leading plus in folder names is preserved", async f =>
                {
                    f.Mod("+Speed", "12345678", "Speed", true);
                    f.SetApplied(new JsonObject { ["0"] = Entry("12345678", "Steam") });
                    f.List("-+Speed");
                    await Converter.ExportAsync(await Converter.PreviewAsync(f.Request()), f.Output);
                    Check(File.ReadAllText(Path.Combine(f.Output, "modlist.txt")).Contains("++Speed\r\n"), "do not trim the folder's own plus");
                }),
                ("existing export cannot be overwritten", async f =>
                {
                    var plan = await Converter.PreviewAsync(f.Request());
                    f.Write("output/sentinel", "keep");
                    await Throws(() => Converter.ExportAsync(plan, f.Output), "already exists");
                    Check(File.ReadAllText(Path.Combine(f.Output, "sentinel")) == "keep", "existing output preserved");
                }),
                ("source changes invalidate preview", async f =>
                {
                    var plan = await Converter.PreviewAsync(f.Request());
                    f.List("-Workshop renamed", "+本地副本");
                    await Throws(() => Converter.ExportAsync(plan, f.Output), "Inputs changed");
                    Check(!Directory.Exists(f.Output), "no stale export");
                }),
                ("changed MO2 source metadata invalidates reverse preview", async f =>
                {
                    var plan = await Converter.PreviewAsync(f.Request(ConversionDirection.Mo2ToSave));
                    f.Write("mods/本地副本/project.xml.mohidden", Project("Renamed local title", "999888777"));
                    await Throws(() => Converter.ExportAsync(plan, f.Output), "Inputs changed");
                }),
                ("cancelled export leaves no published output", async f =>
                {
                    var plan = await Converter.PreviewAsync(f.Request());
                    using var cancellation = new CancellationTokenSource();
                    cancellation.Cancel();
                    try { await Converter.ExportAsync(plan, f.Output, cancellation.Token); throw new Exception("Expected cancellation"); }
                    catch (OperationCanceledException) { }
                    Check(!Directory.Exists(f.Output), "cancelled output absent");
                })
            };
            foreach (var (name, run) in tests)
            {
                using var fixture = new Fixture();
                await run(fixture);
                Console.WriteLine("PASS " + name);
                _passed++;
            }
            RenderUi();
            Console.WriteLine($"PASS {_passed} contracts + bilingual WPF layout smoke");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static async Task Blocked(Fixture f, ConversionDirection direction, string expected)
    {
        var plan = await Converter.PreviewAsync(f.Request(direction));
        Check(!plan.CanExport && plan.Errors.Any(e => e.Contains(expected, StringComparison.OrdinalIgnoreCase)), string.Join("; ", plan.Errors));
        await Throws(() => Converter.ExportAsync(plan, f.Output), "preview errors");
        Check(!Directory.Exists(f.Output), "blocked export absent");
    }

    private static async Task Throws(Func<Task> action, string message)
    {
        try { await action(); }
        catch (Exception ex)
        {
            Check(ex.Message.Contains(message, StringComparison.OrdinalIgnoreCase), $"Expected '{message}', got: {ex.Message}");
            return;
        }
        throw new Exception("Expected failure: " + message);
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static JsonObject Entry(string name, string source) => new() { ["name"] = name, ["source"] = source };
    private static string Project(string title, string id) => $"<project><Title>{title}</Title><PublishedFileId>{id}</PublishedFileId></project>";
    private static async Task<JsonObject> Decode(string path)
    {
        var temp = Path.Combine(Path.GetTempPath(), "mod-order-test-" + Guid.NewGuid().ToString("N") + ".json");
        try { await Codec.DecodeAsync(path, temp); return JsonNode.Parse(await File.ReadAllTextAsync(temp))!.AsObject(); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "mod-order-contract-" + Guid.NewGuid().ToString("N"));
        internal string SavePath => Path.Combine(Root, "save/persist.game.json");
        internal string ListPath => Path.Combine(Root, "profiles/Default/modlist.txt");
        internal string Output => Path.Combine(Root, "output");
        internal Fixture()
        {
            Mod("Workshop renamed", "123456789012", "Workshop title", true);
            Mod("本地副本", "999888777", "本地标题", false, hidden: true);
            Mod("Disabled", "888", "Disabled title", true);
            Mod("New", "889", "New title", true);
            List("+Workshop renamed", "-Group_separator", "+本地副本", "-Disabled");
            var local = Entry("本地标题", "mod_local_source");
            local["extension"] = "keep";
            var save = new JsonObject
            {
                ["base_root"] = new JsonObject
                {
                    ["estatename"] = "测试庄园", ["game_mode"] = "base", ["inraid"] = false,
                    ["week"] = 21, ["dlc"] = new JsonObject { ["0"] = new JsonObject { ["name"] = "crimson_court" } },
                    ["note"] = "Unicode 中文, quote \" and \\ slash",
                    ["applied_ugcs_1_0"] = new JsonObject { ["10"] = Entry("123456789012", "Steam"), ["2"] = local }
                }
            };
            Write("save/persist.game.json", save.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        internal void Write(string relative, string text)
        {
            var path = Path.Combine(Root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text, new UTF8Encoding(false));
        }
        internal void Mod(string folder, string id, string title, bool workshop, bool hidden = false)
        {
            Write($"mods/{folder}/project.xml" + (hidden ? ".mohidden" : ""), Project(title, id));
            Write($"mods/{folder}/meta.ini", $"[General]\nmodid=0\n[Plugins]\nDarkest%20Dungeon%20Mod%20Sync%20Tool\\isworkshop={workshop.ToString().ToLowerInvariant()}\n" +
                (workshop ? $"Darkest%20Dungeon%20Mod%20Sync%20Tool\\id={id}\n" : "Darkest%20Dungeon%20Mod%20Sync%20Tool\\local_id=42\n"));
        }
        internal void List(params string[] lines) => Write("profiles/Default/modlist.txt", "# test list\r\n" + string.Join("\r\n", lines) + "\r\n");
        internal void SetApplied(JsonNode applied)
        {
            var save = JsonNode.Parse(File.ReadAllText(SavePath))!;
            save["base_root"]!["applied_ugcs_1_0"] = applied;
            File.WriteAllText(SavePath, save.ToJsonString());
        }
        internal ConversionRequest Request(ConversionDirection direction = ConversionDirection.SaveToMo2) =>
            new(direction, SavePath, ListPath, Path.Combine(Root, "mods"));
        internal async Task Binary()
        {
            var encoded = Path.Combine(Root, "encoded.json");
            await Codec.EncodeAsync(SavePath, encoded, null);
            File.Copy(encoded, SavePath, overwrite: true); // Only this fixture's synthetic input.
            File.Delete(encoded);
        }
        internal Dictionary<string, string> Snapshot() => Directory.GetFiles(Root, "*", SearchOption.AllDirectories)
            .ToDictionary(p => p, p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))));
        internal void Unchanged(Dictionary<string, string> before)
        {
            foreach (var pair in before) Check(File.Exists(pair.Key) && Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(pair.Key))) == pair.Value, "input changed: " + pair.Key);
            Check(Directory.GetFiles(Root, "*", SearchOption.AllDirectories).Where(p => !p.StartsWith(Output + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)).Count() == before.Count, "unexpected new source file");
        }
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    private static void RenderUi()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var output = Path.GetFullPath("tools/Mo2ModlistConverter/artifacts/ui-smoke-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(output);
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
                var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                var appXaml = XDocument.Load("tools/Mo2ModlistConverter/App/App.xaml").Root!;
                var appResources = appXaml.Element(appXaml.Name.Namespace + "Application.Resources");
                if (appResources is not null)
                    application.Resources = (ResourceDictionary)XamlReader.Parse(appResources.Elements().Single().ToString(),
                        new ParserContext { BaseUri = new Uri("pack://application:,,,/DarkestDungeonModOrderConverter;component/") });
                using var fixture = new Fixture();
                var languageSettings = Path.Combine(output, "language.json");
                var window = new MainWindow(languageSettings, CultureInfo.GetCultureInfo("en-US"))
                {
                    WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000,
                    ShowActivated = false, ShowInTaskbar = false
                };
                window.Show();
                Pump();
                Check(window.Title == "Darkest Dungeon Mod Order Converter", "An English system defaults to English");
                var languageMenu = ((Button)window.FindName("LanguageButton")).ContextMenu;
                var languageOptions = languageMenu.Items.OfType<MenuItem>().ToArray();
                void SelectLanguage(string preference)
                {
                    var option = languageOptions.Single(item => (string)item.Tag == preference);
                    option.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, option));
                    Pump();
                    Check(languageOptions.Count(item => item.IsChecked) == 1 && option.IsChecked,
                        "exactly one language preference must be checked");
                    Check(EditorLanguageSettings.Load(languageSettings, out _) == preference,
                        "language preference must persist independently of editor settings");
                }
                Check(languageOptions.Length == 3 && languageOptions.Single(item => item.IsChecked).Tag as string == "system" && !File.Exists(languageSettings),
                    "first run follows the system without writing preferences");
                foreach (var system in new[] { "zh-CN", "zh-TW", "fr-FR" })
                {
                    var firstRun = new MainWindow(Path.Combine(output, system, "language.json"), CultureInfo.GetCultureInfo(system));
                    Check(firstRun.Title == (system.StartsWith("zh") ? "暗黑地牢 Mod 顺序转换工具" : "Darkest Dungeon Mod Order Converter"),
                        "system Chinese selects Simplified Chinese; other languages fall back to English");
                    firstRun.Close();
                }
                var editorTheme = XDocument.Load("src/DarkestDungeonSaveEditor.App/App.xaml");
                XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
                foreach (var color in editorTheme.Descendants().Where(e => e.Name.LocalName == "Color"))
                {
                    var key = color.Attribute(xaml + "Key")!.Value;
                    Check(window.FindResource(key) is Color actual && actual == (Color)ColorConverter.ConvertFromString(color.Value.Trim()), "editor palette mismatch: " + key);
                }
                var export = (Button)window.FindName("ExportButton");
                Check(!export.IsEnabled, "export disabled before preview");
                var reverse = (RadioButton)window.FindName("ToSave");
                reverse.IsChecked = true;
                Check(window.FindName("GamePath") is null && window.FindName("WorkshopPath") is null, "native directory inputs are absent");
                Check(window.FindName("WorkflowNote") is null && window.FindName("Subtitle") is null, "requested explanatory notes are removed");
                ((TextBox)window.FindName("SavePath")).Text = fixture.SavePath;
                ((TextBox)window.FindName("ListPath")).Text = fixture.ListPath;
                ((TextBox)window.FindName("ModsPath")).Text = fixture.Request().ModsDirectory;
                var preview = (Button)window.FindName("PreviewButton");
                preview.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var deadline = DateTime.UtcNow.AddSeconds(15);
                while (!preview.IsEnabled && DateTime.UtcNow < deadline) Pump();
                Check(export.IsEnabled, "UI preview should enable export: " + ((TextBox)window.FindName("Details")).Text);
                for (var i = 0; i < 2; i++)
                {
                    var content = (FrameworkElement)window.Content;
                    Pump();
                    content.UpdateLayout();
                    Pump();
                    var heading = (TextBlock)window.FindName("Heading");
                    var titleInk = VisualTreeHelper.GetDrawing(heading)!.Bounds;
                    Check(!titleInk.IsEmpty && titleInk.Top >= 0 && titleInk.Bottom <= heading.ActualHeight + 1,
                        "title glyphs must fit after switching language");
                    Check(((DataGrid)window.FindName("PreviewGrid")).Columns.All(c => c.ActualWidth > 20), "all preview columns must be visible");
                    var header = VisualChildren<DataGridColumnHeader>((DataGrid)window.FindName("PreviewGrid"))
                        .First(h => h.Content?.ToString() == (i == 0 ? "Order" : "顺序"));
                    var headerText = VisualChildren<TextBlock>(header).First(t => t.Text.Length > 0);
                    var textColor = ((SolidColorBrush)window.FindResource("TextPrimaryBrush")).Color;
                    Check(headerText.Foreground is SolidColorBrush headerBrush && headerBrush.Color == textColor,
                        "rendered header text must use the editor's primary text color, including inside control templates");
                    Check(headerText.FontFamily.Source == ((FontFamily)window.FindResource("HeadingFont")).Source,
                        "rendered header font must follow the selected language");
                    Check(((TextBlock)window.FindName("PreviewHeading")).Foreground is SolidColorBrush panelBrush && panelBrush.Color == textColor,
                        "panel heading text must match the editor's panel headers");
                    var pathBox = (TextBox)window.FindName("SavePath");
                    var host = (ScrollViewer)pathBox.Template.FindName("PART_ContentHost", pathBox);
                    Check(host.ViewportHeight >= pathBox.FontSize * 1.1, "path text must have room for a complete line");
                    var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(content);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using (var file = File.Create(Path.Combine(output, $"layout-{i}.png"))) encoder.Save(file);
                    window.Width = window.MinWidth;
                    window.Height = window.MinHeight;
                    Pump();
                    content.UpdateLayout();
                    Pump();
                    Check(((DataGrid)window.FindName("PreviewGrid")).ActualHeight >= 100,
                        "minimum window size must retain usable preview space");
                    var minTitleInk = VisualTreeHelper.GetDrawing(heading)!.Bounds;
                    Check(minTitleInk.Right <= heading.ActualWidth + 1 && minTitleInk.Bottom <= heading.ActualHeight + 1,
                        "title must remain readable at the minimum window size");
                    var minimumBitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    minimumBitmap.Render(content);
                    var minimumEncoder = new PngBitmapEncoder(); minimumEncoder.Frames.Add(BitmapFrame.Create(minimumBitmap));
                    using (var file = File.Create(Path.Combine(output, $"layout-{i}-minimum.png"))) minimumEncoder.Save(file);
                    window.Width = 1150;
                    window.Height = 820;
                    var grid = (DataGrid)window.FindName("PreviewGrid");
                    grid.SelectedIndex = 0;
                    var selected = grid.SelectedItem;
                    var rows = grid.ItemsSource;
                    SelectLanguage(i == 0 ? "zh-CN" : "en");
                    Check(export.IsEnabled && ReferenceEquals(grid.ItemsSource, rows) && ReferenceEquals(grid.SelectedItem, selected)
                        && ((TextBox)window.FindName("SavePath")).Text == fixture.SavePath,
                        "language switching must preserve the preview, selection and inputs");
                }
                SelectLanguage("zh-CN");
                var reopened = new MainWindow(languageSettings, CultureInfo.GetCultureInfo("en-US"));
                Check(reopened.Title == "暗黑地牢 Mod 顺序转换工具", "manual preference survives restart and overrides system");
                reopened.Close();
                SelectLanguage("system");
                Check(window.Title == "Darkest Dungeon Mod Order Converter", "follow system returns to the original system culture");
                SelectLanguage("system");
                reverse.IsChecked = false;
                ((RadioButton)window.FindName("ToMo2")).IsChecked = true;
                window.Width = window.MinWidth;
                window.Height = window.MinHeight;
                Pump();
                window.UpdateLayout();
                Pump();
                Check(((DataGrid)window.FindName("PreviewGrid")).ActualHeight >= 80,
                    "default Save-to-MO2 direction must retain space for its header and a data row at minimum size");
                Check(!export.IsEnabled, "direction change invalidates preview");
                preview.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                deadline = DateTime.UtcNow.AddSeconds(15);
                while (!preview.IsEnabled && DateTime.UtcNow < deadline) Pump();
                Check(export.IsEnabled, "default direction preview enables export");
                ((TextBox)window.FindName("SavePath")).Text += ".changed";
                Check(!export.IsEnabled, "UI input change invalidates preview");
                window.Close();
                application.Shutdown();
                Console.WriteLine("UI smoke renders: " + output);
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (failure is not null) throw failure;
    }

    private static IEnumerable<T> VisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var descendant in VisualChildren<T>(child)) yield return descendant;
        }
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(20) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static async Task Live(string[] args)
    {
        var save = Path.GetFullPath(args[1]);
        var mo2 = Path.GetFullPath(args[2]);
        var originalList = Path.Combine(mo2, "profiles/Default/modlist.txt");
        var list = args.Length == 5 ? Path.GetFullPath(args[4]) : originalList;
        var protectedFiles = Directory.GetDirectories(Path.Combine(mo2, "mods"))
            .SelectMany(p => new[] { Path.Combine(p, "meta.ini"), Path.Combine(p, "project.xml"), Path.Combine(p, "project.xml.mohidden") })
            .Append(save).Append(list).Append(originalList).Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(p => p, p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))));
        var request = new ConversionRequest(ConversionDirection.Mo2ToSave, save, list, Path.Combine(mo2, "mods"));
        var reverse = await Converter.PreviewAsync(request);
        Check(reverse.CanExport, string.Join(Environment.NewLine, reverse.Errors));
        var output = Path.Combine(Path.GetFullPath(args[3]), "live-" + Guid.NewGuid().ToString("N"));
        await Converter.ExportAsync(reverse, output);
        var forward = await Converter.PreviewAsync(request with { Direction = ConversionDirection.SaveToMo2, SavePath = Path.Combine(output, "persist.game.json") });
        Check(forward.CanExport, string.Join(Environment.NewLine, forward.Errors));
        Check(forward.Rows.Where(r => r.Enabled).Select(r => r.Mo2Folder).SequenceEqual(reverse.Rows.Where(r => r.Enabled).Select(r => r.Mo2Folder)), "live reverse/forward order mismatch");
        await Converter.ExportAsync(forward, Path.Combine(output, "mo2-roundtrip"));
        foreach (var pair in protectedFiles) Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(pair.Key))) == pair.Value, "live input changed: " + pair.Key);
        Console.WriteLine($"PASS live native restore + MO2 round trip: {reverse.EnabledCount} enabled Mods; {protectedFiles.Count} protected files unchanged; {output}");
    }
}
