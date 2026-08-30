using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DarkestDungeonSaveEditor.Core;

try
{
    if (args.Length != 1)
    {
        throw new InvalidOperationException("Usage: DarkestDungeonSaveEditor.ContractTests <repository-root>");
    }

var repositoryRoot = Path.GetFullPath(args[0]);
var mainWindowXamlPath = Path.Combine(
    repositoryRoot,
    "src",
    "DarkestDungeonSaveEditor.App",
    "MainWindow.xaml");
var mainWindowXaml = System.Xml.Linq.XDocument.Load(mainWindowXamlPath);
var mainWindowCode = File.ReadAllText(Path.Combine(
    repositoryRoot,
    "src",
    "DarkestDungeonSaveEditor.App",
    "MainWindow.xaml.cs"));
var appCode = File.ReadAllText(Path.Combine(
    repositoryRoot,
    "src",
    "DarkestDungeonSaveEditor.App",
    "App.xaml.cs"));
var appXamlPath = Path.Combine(
    repositoryRoot,
    "src",
    "DarkestDungeonSaveEditor.App",
    "App.xaml");
var appXaml = System.Xml.Linq.XDocument.Load(appXamlPath);
var appXamlText = File.ReadAllText(appXamlPath);
var appProjectPath = Path.Combine(
    repositoryRoot,
    "src",
    "DarkestDungeonSaveEditor.App",
    "DarkestDungeonSaveEditor.App.csproj");
var appProjectText = File.ReadAllText(appProjectPath);
var nativeWindowThemeCode = File.ReadAllText(Path.Combine(
    repositoryRoot,
    "src",
    "DarkestDungeonSaveEditor.App",
    "NativeWindowTheme.cs"));
Assert(
    !mainWindowCode.Contains("Loaded += (_, _) => Discover();", StringComparison.Ordinal) &&
    mainWindowCode.Contains("Discover_Click(object sender, RoutedEventArgs e) => Discover();", StringComparison.Ordinal) &&
    mainWindowCode.Contains("WorkshopDirectoryTextBox.Text = game.WorkshopDirectory;", StringComparison.Ordinal) &&
    mainWindowCode.Contains("LocalModDirectoryTextBox.Text = game.DefaultLocalModDirectory;", StringComparison.Ordinal),
    "Path discovery must run only after the user clicks the button and must then fill the derived Workshop/local Mod paths.");
Assert(
    appCode.Contains("SaveEditorLocations.ResolveLogDirectory(AppContext.BaseDirectory)", StringComparison.Ordinal) &&
    mainWindowCode.Contains("CrashDiagnostics.RecordStatus(message);", StringComparison.Ordinal) &&
    mainWindowCode.Contains("foreach (var issueMessage in issueMessages)", StringComparison.Ordinal),
    "Project-local logging must persist ordinary UI status and every catalog issue.");
Assert(
    mainWindowCode.Contains("内容目录档案：ID={activeContent.Profile.ProfileId}", StringComparison.Ordinal) &&
    mainWindowCode.Contains("档案目录={activeContent.Profile.ProfileDirectory}", StringComparison.Ordinal) &&
    mainWindowCode.Contains("persist.game.json SHA-256={activeContent.SourceGameSha256}", StringComparison.Ordinal),
    "Catalog loading must log the selected profile id, full profile directory, and source persist.game.json SHA-256.");
var presentationNamespace = mainWindowXaml.Root?.Name.Namespace ??
    throw new InvalidDataException("MainWindow.xaml has no root namespace.");
var xamlName = System.Xml.Linq.XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml");
var generationModeColumn = mainWindowXaml
    .Descendants(presentationNamespace + "DataGridTextColumn")
    .Single(column => column.Attribute("Header")?.Value == "生成方式");
Assert(
    generationModeColumn.Attribute("Binding")?.Value == "{Binding GenerationMode}" &&
    !mainWindowXaml.Descendants(presentationNamespace + "DataGridCheckBoxColumn").Any() &&
    mainWindowCode.Contains("{ IsEnabled: true } => \"游戏自然 / 编辑器手动\"", StringComparison.Ordinal) &&
    mainWindowCode.Contains("{ IsEnabled: false } => \"仅编辑器手动\"", StringComparison.Ordinal) &&
    mainWindowCode.Contains("_ => \"自然状态未知 / 编辑器手动\"", StringComparison.Ordinal),
    "The hero catalog must distinguish game-side natural generation from editor-side manual generation instead of showing a misleading availability checkbox.");
Assert(
    mainWindowXaml.Descendants(presentationNamespace + "ComboBox")
        .Any(element => element.Attribute(
            System.Xml.Linq.XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value == "HeroLevelComboBox"),
    "MainWindow must expose a hero level selector.");
Assert(
    mainWindowXaml.Descendants(presentationNamespace + "TextBox")
        .Any(element => element.Attribute(
            System.Xml.Linq.XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value == "LocalModDirectoryTextBox"),
    "MainWindow must expose an optional additional local Mod directory selector.");
var pathTextBoxNames = new[]
{
    "GameDirectoryTextBox",
    "WorkshopDirectoryTextBox",
    "LocalModDirectoryTextBox",
    "ProfileDirectoryTextBox"
};
var pathTextBoxes = pathTextBoxNames
    .Select(name => mainWindowXaml.Descendants(presentationNamespace + "TextBox")
        .Single(element => element.Attribute(
            System.Xml.Linq.XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value == name))
    .ToArray();
Assert(
    pathTextBoxes.All(textBox => textBox.Attribute("Text") is null),
    "All path inputs must be empty before the user clicks automatic discovery.");
Assert(
    pathTextBoxes[1].Attribute("ToolTip")?.Value.Contains("点击自动发现后按 Steam 库默认路径填写", StringComparison.Ordinal) == true,
    "The Workshop directory must communicate that the button derives its Steam library path.");
var trinketLimitColumn = mainWindowXaml
    .Descendants(presentationNamespace + "DataGridTextColumn")
    .Single(column => column.Attribute("Header")?.Value == "定义上限");
Assert(
    trinketLimitColumn.Attribute("Binding")?.Value == "{Binding LimitDisplay}" &&
    mainWindowCode.Contains("0 => \"无限\"", StringComparison.Ordinal) &&
    mainWindowCode.Contains("PreviewWarningTextBlock", StringComparison.Ordinal),
    "The trinket UI must render limit zero as unlimited and expose a dedicated preview warning area.");
Assert(
    mainWindowCode.Contains(
        "FormatHeroQuirkLimitWarnings(preparedHeroEdit.Preview)",
        StringComparison.Ordinal) &&
    mainWindowCode.Contains("全部马车池", StringComparison.Ordinal) &&
    mainWindowCode.Contains("（singleton）", StringComparison.Ordinal) &&
    mainWindowCode.Contains("编辑器会按控制台模式保留写入能力", StringComparison.Ordinal) &&
    !mainWindowCode.Contains("HeroQuirkLimitKind.RosterLimit", StringComparison.Ordinal),
    "Hero quirk warnings must remain scoped to singleton duplication across the roster and all stagecoach pools.");
System.Xml.Linq.XElement ReadNamedMainElement(string elementName, string name) => mainWindowXaml
    .Descendants(presentationNamespace + elementName)
    .Single(element => element.Attribute(xamlName)?.Value == name);
var trinketGridElement = ReadNamedMainElement("DataGrid", "TrinketGrid");
var heroGridElement = ReadNamedMainElement("DataGrid", "HeroGrid");
var dataGridStyle = appXaml
    .Descendants(presentationNamespace + "Style")
    .Single(element => element.Attribute("TargetType")?.Value == "DataGrid");
var trinketColumnHeaders = trinketGridElement
    .Descendants()
    .Where(element => element.Name == presentationNamespace + "DataGridTextColumn" ||
                      element.Name == presentationNamespace + "DataGridCheckBoxColumn")
    .Select(element => element.Attribute("Header")?.Value)
    .ToArray();
var heroColumnHeaders = heroGridElement
    .Descendants()
    .Where(element => element.Name == presentationNamespace + "DataGridTextColumn" ||
                      element.Name == presentationNamespace + "DataGridCheckBoxColumn")
    .Select(element => element.Attribute("Header")?.Value)
    .ToArray();
Assert(
    trinketColumnHeaders.SequenceEqual(
        new[] { "饰品 ID", "中文名", "English", "稀有度", "来源", "定义上限" },
        StringComparer.Ordinal) &&
    heroColumnHeaders.SequenceEqual(
        new[] { "职业 ID", "中文名", "English", "来源", "生成方式", "等级范围", "自然怪癖范围" },
        StringComparer.Ordinal) &&
    ReadStyleSetter(dataGridStyle, "HorizontalContentAlignment") == "Stretch" &&
    ReadStyleSetter(dataGridStyle, "VerticalContentAlignment") == "Top" &&
    trinketGridElement.Descendants(presentationNamespace + "DataGridTextColumn")
        .All(column => column.Attribute("ElementStyle")?.Value == "{StaticResource DataGridTextElementStyle}") &&
    heroGridElement.Descendants(presentationNamespace + "DataGridTextColumn")
        .All(column => column.Attribute("ElementStyle")?.Value == "{StaticResource DataGridTextElementStyle}"),
    "The main catalogs must keep only user-relevant columns and apply vertically centered, ellipsized text elements.");
var xamlNamespace = System.Xml.Linq.XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml");
var applicationResourceKeys = appXaml
    .Descendants()
    .Select(element => element.Attribute(xamlNamespace)?.Value)
    .Where(value => value is not null)
    .ToHashSet(StringComparer.Ordinal);
Assert(
    applicationResourceKeys.Contains("CardStyle") &&
    applicationResourceKeys.Contains("PrimaryButtonStyle") &&
    applicationResourceKeys.Contains("GoldButtonStyle") &&
    applicationResourceKeys.Contains("PanelHeaderStyle") &&
    applicationResourceKeys.Contains("RedPanelHeaderStyle") &&
    applicationResourceKeys.Contains("OlivePanelHeaderStyle") &&
    applicationResourceKeys.Contains("LogTextBoxStyle") &&
    mainWindowCode.Split("FormatStorageCapacity(_trinketStorage)", StringSplitOptions.None).Length >= 3,
    "The game-panel application shell and effective storage-capacity summary must remain wired into the main window.");
var mainWindowRoot = mainWindowXaml.Root
    ?? throw new InvalidDataException("MainWindow.xaml has no root element.");
System.Xml.Linq.XElement ReadAppStyle(string key) => appXaml
    .Descendants(presentationNamespace + "Style")
    .Single(element => element.Attribute(xamlNamespace)?.Value == key);
string? ReadStyleSetter(System.Xml.Linq.XElement style, string property) => style
    .Elements(presentationNamespace + "Setter")
    .SingleOrDefault(element => element.Attribute("Property")?.Value == property)
    ?.Attribute("Value")
    ?.Value;
bool HasOnlySquareCorners(params System.Xml.Linq.XDocument[] documents) => documents
    .SelectMany(document => document.Descendants())
    .All(element =>
        element.Attribute("CornerRadius") is not { } cornerRadiusAttribute ||
        cornerRadiusAttribute.Value == "0") &&
    documents
        .SelectMany(document => document.Descendants(presentationNamespace + "Setter"))
        .All(setter =>
            setter.Attribute("Property")?.Value != "CornerRadius" ||
            setter.Attribute("Value")?.Value == "0");
bool HasGamePanelWindowFrame(System.Xml.Linq.XDocument document)
{
    var rootGrid = document.Root?.Element(presentationNamespace + "Grid");
    var outerFrame = rootGrid?.Elements(presentationNamespace + "Border").SingleOrDefault();
    var innerFrame = outerFrame?.Element(presentationNamespace + "Border");
    return outerFrame?.Attribute("Margin")?.Value == "8" &&
           outerFrame.Attribute("BorderBrush")?.Value == "{StaticResource GoldDarkBrush}" &&
           outerFrame.Attribute("BorderThickness")?.Value == "1" &&
           innerFrame?.Attribute("Margin")?.Value == "3" &&
           innerFrame.Attribute("BorderBrush")?.Value == "#4B5351" &&
           innerFrame.Attribute("BorderThickness")?.Value == "1";
}
string ReadAppColor(string key) => appXaml
    .Descendants(presentationNamespace + "Color")
    .Single(element => element.Attribute(xamlNamespace)?.Value == key)
    .Value
    .Trim();
Assert(
    mainWindowRoot.Attribute("Background")?.Value == "{StaticResource AppTextureBrush}" &&
    mainWindowRoot.Attribute("Foreground")?.Value == "{StaticResource TextPrimaryBrush}" &&
    mainWindowRoot.Attribute("FontFamily")?.Value == "SimHei, Microsoft YaHei UI" &&
    mainWindowRoot.Attribute("FontSize")?.Value == "14" &&
    applicationResourceKeys.Contains("AppTextureBrush") &&
    appProjectText.Contains("<Resource Include=\"Assets\\charcoal-cloth.png\"", StringComparison.Ordinal) &&
    File.Exists(Path.Combine(
        repositoryRoot,
        "src",
        "DarkestDungeonSaveEditor.App",
        "Assets",
        "charcoal-cloth.png")),
    "The themed shell must set its texture, foreground, game-matched Simplified Chinese body font, and package the original texture asset.");
var titleAtlasPath = Path.Combine(
    repositoryRoot,
    "src",
    "DarkestDungeonSaveEditor.App",
    "Assets",
    "darkest-dungeon-title-atlas.png");
var titleLoopPath = Path.Combine(
    repositoryRoot,
    "src",
    "DarkestDungeonSaveEditor.App",
    "Assets",
    "dd-title-logo-loop.png");
var applicationIconPath = Path.Combine(
    repositoryRoot,
    "src",
    "DarkestDungeonSaveEditor.App",
    "Assets",
    "save-editor.ico");
var titleLoopSize = ReadPngSize(titleLoopPath);
var titleLoopSha256 = ComputeSha256(titleLoopPath);
var titleLoopBitmap = LoadBgra32(titleLoopPath);
var titleLoopAnchorPixels = Enumerable.Range(0, 120)
    .Select(frameIndex =>
    {
        var frameX = frameIndex % 10 * 320;
        var frameY = frameIndex / 10 * 100;
        return (
            OuterAntialiasedOutline: ReadPixel(titleLoopBitmap, frameX + 21, frameY + 37),
            SmokeMotion: ReadPixel(titleLoopBitmap, frameX + 100, frameY + 28),
            FlameMotion: ReadPixel(titleLoopBitmap, frameX + 158, frameY + 30),
            LowerLeftTitle: ReadPixel(titleLoopBitmap, frameX + 109, frameY + 69),
            LowerTorchHandle: ReadPixel(titleLoopBitmap, frameX + 154, frameY + 82),
            LowerRightTitle: ReadPixel(titleLoopBitmap, frameX + 208, frameY + 69),
            LeftRedOutline: ReadPixel(titleLoopBitmap, frameX + 116, frameY + 51),
            RightRedOutline: ReadPixel(titleLoopBitmap, frameX + 191, frameY + 42));
    })
    .ToArray();
var stableOuterOutlinePixels = titleLoopAnchorPixels
    .Select(frame => frame.OuterAntialiasedOutline)
    .Distinct()
    .ToArray();
var distinctSmokeMotionFrames = titleLoopAnchorPixels
    .Select(frame => frame.SmokeMotion)
    .Distinct()
    .Count();
var distinctFlameMotionFrames = titleLoopAnchorPixels
    .Select(frame => frame.FlameMotion)
    .Distinct()
    .Count();
Assert(
    mainWindowRoot.Attribute("Icon")?.Value == "Assets/save-editor.ico" &&
    appProjectText.Contains("<ApplicationIcon>Assets\\save-editor.ico</ApplicationIcon>", StringComparison.Ordinal) &&
    appProjectText.Contains("<Resource Include=\"Assets\\save-editor.ico\"", StringComparison.Ordinal) &&
    appProjectText.Contains("<Resource Include=\"Assets\\darkest-dungeon-title-atlas.png\"", StringComparison.Ordinal) &&
    appProjectText.Contains("<Resource Include=\"Assets\\dd-title-logo-loop.png\"", StringComparison.Ordinal) &&
    File.Exists(applicationIconPath) &&
    new FileInfo(applicationIconPath).Length > 0 &&
    File.Exists(titleAtlasPath) &&
    new FileInfo(titleAtlasPath).Length > 0 &&
    File.Exists(titleLoopPath) &&
    new FileInfo(titleLoopPath).Length > 3_000_000 &&
    titleLoopSize == (3200, 1200) &&
    titleLoopSha256 == "BD86502DBEFB8C02D5CF59E6D2FDA3B9C92985D44C4138586A256BE58AE082C1",
    "The executable icon, source title atlas, and target-resolution pre-rendered original title loop must be packaged as application resources.");
Assert(
    stableOuterOutlinePixels.Length == 1 &&
    stableOuterOutlinePixels[0] == (R: (byte)255, G: (byte)43, B: (byte)0, A: (byte)32) &&
    distinctSmokeMotionFrames >= 90 &&
    distinctFlameMotionFrames >= 100 &&
    titleLoopAnchorPixels.All(frame =>
        IsStraightAlphaRedEdge(frame.OuterAntialiasedOutline) &&
        IsOpaqueDark(frame.LowerLeftTitle, 16) &&
        IsOpaqueDark(frame.LowerTorchHandle, 8) &&
        IsOpaqueDark(frame.LowerRightTitle, 16) &&
        IsRedOutline(frame.LeftRedOutline, 195, 240) &&
        IsRedOutline(frame.RightRedOutline, 205, 250)),
    "Every title-loop frame must keep a stable straight-alpha outer wordmark edge while retaining varied smoke and flame motion, complete black title interiors, the dark torch handle, and original red outlines; flame-synchronized outline opacity, animation freezing, crossed region triangles, double alpha multiplication, and artificial brightening must not alter the editor logo style.");
var titleLogoImage = ReadNamedMainElement("Image", "TitleLogoImage");
Assert(
    titleLogoImage.Attribute("Width")?.Value == "320" &&
    titleLogoImage.Attribute("Height")?.Value == "100" &&
    titleLogoImage.Attribute("Stretch")?.Value == "Uniform" &&
    titleLogoImage.Attribute("RenderOptions.BitmapScalingMode")?.Value == "HighQuality" &&
    titleLogoImage.Parent?.Attribute("Width")?.Value == "320" &&
    titleLogoImage.Parent?.Attribute("Height")?.Value == "100" &&
    mainWindowXaml.Descendants(presentationNamespace + "RowDefinition")
        .First().Attribute("Height")?.Value == "124" &&
    titleLogoImage.Attribute("Loaded")?.Value == "TitleLogoImage_Loaded" &&
    titleLogoImage.Attribute("Unloaded")?.Value == "TitleLogoImage_Unloaded" &&
    titleLogoImage.Attribute("SnapsToDevicePixels")?.Value == "True" &&
    !titleLogoImage.Descendants(presentationNamespace + "DropShadowEffect").Any() &&
    !mainWindowXaml.Descendants()
        .Any(element => element.Attribute(xamlNamespace + "Name")?.Value == "TitleLogoContrastOverlay") &&
    !mainWindowXaml.Descendants(presentationNamespace + "Border")
        .Any(border => border.Attribute("Background")?.Value == "#9A211E" &&
                       border.Attribute("Grid.Column")?.Value == "0") &&
    mainWindowCode.Contains("private const int TitleLogoFrameWidth = 320;", StringComparison.Ordinal) &&
    mainWindowCode.Contains("private const int TitleLogoFrameHeight = 100;", StringComparison.Ordinal) &&
    mainWindowCode.Contains("private const int TitleLogoFrameColumns = 10;", StringComparison.Ordinal) &&
    mainWindowCode.Contains("private const int TitleLogoFrameCount = 120;", StringComparison.Ordinal) &&
    mainWindowCode.Contains("TimeSpan.FromMilliseconds(100)", StringComparison.Ordinal) &&
    mainWindowCode.Contains("new CroppedBitmap(", StringComparison.Ordinal) &&
    mainWindowCode.Contains("dd-title-logo-loop.png", StringComparison.Ordinal) &&
    !appXaml.Descendants(presentationNamespace + "BitmapImage")
        .Any(image => image.Attribute(xamlNamespace)?.Value?.StartsWith("TitleFire", StringComparison.Ordinal) == true) &&
    !mainWindowXaml.Descendants(presentationNamespace + "TextBlock")
        .Any(textBlock => textBlock.Attribute("Text")?.Value == "祖宅档案台"),
    "The main header must play the full 12-second original title/torch loop as 120 target-resolution frames without runtime downsampling and omit both artificial tint/glow layers and the obsolete red divider.");
var pageTitleStyle = ReadAppStyle("PageTitleStyle");
var panelHeaderStyle = ReadAppStyle("PanelHeaderStyle");
var logTextBoxStyle = ReadAppStyle("LogTextBoxStyle");
Assert(
    ReadStyleSetter(pageTitleStyle, "FontFamily") == "SimSun, Microsoft YaHei UI" &&
    ReadStyleSetter(panelHeaderStyle, "FontFamily") == "SimSun, Microsoft YaHei UI" &&
    ReadStyleSetter(logTextBoxStyle, "FontFamily") == "SimHei, Microsoft YaHei UI" &&
    applicationResourceKeys.Contains("StoneBandBrush") &&
    applicationResourceKeys.Contains("RedBandBrush") &&
    applicationResourceKeys.Contains("BandVignetteBrush") &&
    !appXaml.Descendants(presentationNamespace + "DropShadowEffect").Any() &&
    HasOnlySquareCorners(appXaml, mainWindowXaml),
    "The UI must use the game's SimSun/SimHei Simplified Chinese mapping, weathered band resources, and square shadow-free panel geometry.");
Assert(
    mainWindowRoot.Attribute("MinWidth")?.Value == "1120" &&
    mainWindowRoot.Attribute("MinHeight")?.Value == "720" &&
    HasGamePanelWindowFrame(mainWindowXaml),
    "The main editor must retain its supported minimum viewport and the gold/steel square double frame.");
Assert(
    ContrastRatio(ReadAppColor("TextPrimaryColor"), ReadAppColor("AppBackgroundColor")) >= 12 &&
    ContrastRatio(ReadAppColor("TextSecondaryColor"), ReadAppColor("SurfaceInsetColor")) >= 8 &&
    ContrastRatio(ReadAppColor("GoldColor"), ReadAppColor("SurfaceRaisedColor")) >= 6 &&
    ContrastRatio(ReadAppColor("TextDisabledColor"), ReadAppColor("DisabledSurfaceColor")) >= 6 &&
    !appXamlText.Contains("Property=\"Opacity\" Value=\"0.62\"", StringComparison.Ordinal) &&
    !appXamlText.Contains("Property=\"Opacity\" Value=\"0.64\"", StringComparison.Ordinal) &&
    !appXamlText.Contains(
        "TargetName=\"InnerChrome\" Property=\"Background\" Value=\"{StaticResource SurfaceHoverBrush}\"",
        StringComparison.Ordinal),
    "Primary, secondary, gold, and disabled UI text must retain high contrast without dimming the whole control.");
Assert(
    applicationResourceKeys.Contains("ScrollBarThumbStyle") &&
    applicationResourceKeys.Contains("VerticalScrollBarTemplate") &&
    applicationResourceKeys.Contains("HorizontalScrollBarTemplate") &&
    appXamlText.Split("x:Name=\"PART_Track\"", StringSplitOptions.None).Length == 3 &&
    appXamlText.Contains("TargetType=\"ScrollBar\"", StringComparison.Ordinal),
    "Vertical and horizontal scroll bars must use the themed track and thumb resources instead of the system template.");
var comboBoxStyle = appXaml
    .Descendants(presentationNamespace + "Style")
    .Single(element =>
        element.Attribute("TargetType")?.Value == "ComboBox" &&
        element.Attribute(xamlNamespace) is null);
var modernTextBoxStyle = ReadAppStyle("ModernTextBoxStyle");
var textBoxTriggers = modernTextBoxStyle
    .Descendants(presentationNamespace + "Trigger")
    .ToArray();
Assert(
    comboBoxStyle.Descendants(presentationNamespace + "Popup")
        .Any(element => element.Attribute(xamlName)?.Value == "PART_Popup") &&
    comboBoxStyle.Descendants(presentationNamespace + "ToggleButton")
        .Any(element => element.Attribute(xamlName)?.Value == "DropDownToggle") &&
    comboBoxStyle.Descendants(presentationNamespace + "Border")
        .Any(element => element.Attribute(xamlName)?.Value == "DropDownBorder") &&
    comboBoxStyle.Descendants(presentationNamespace + "TextBlock")
        .Any(element => element.Attribute("Text")?.Value == "选择") &&
    ReadStyleSetter(comboBoxStyle, "BorderThickness") == "1" &&
    ReadStyleSetter(modernTextBoxStyle, "BorderThickness") == "1" &&
    !comboBoxStyle.Descendants(presentationNamespace + "Trigger")
        .Any(trigger => trigger.Attribute("Property")?.Value == "IsKeyboardFocusWithin") &&
    textBoxTriggers.Any(trigger =>
        trigger.Attribute("Property")?.Value == "IsMouseOver" &&
        trigger.Attribute("Value")?.Value == "True") &&
    !textBoxTriggers.Any(trigger => trigger.Attribute("Property")?.Value == "IsKeyboardFocused") &&
    !textBoxTriggers.SelectMany(trigger => trigger.Descendants(presentationNamespace + "Setter"))
        .Any(setter => setter.Attribute("Property")?.Value == "BorderThickness") &&
    !comboBoxStyle.DescendantsAndSelf()
        .SelectMany(element => element.Attributes("Background"))
        .Any(attribute => attribute.Value.Equals("White", StringComparison.OrdinalIgnoreCase) ||
                          attribute.Value.Equals("#FFFFFF", StringComparison.OrdinalIgnoreCase)),
    "Text and hero-level fields must retain fixed one-pixel geometry, show gold only while hovered, and use the custom dark ComboBox popup without a retained focus frame.");
var modernButtonStyle = ReadAppStyle("ModernButtonStyle");
var buttonTriggers = modernButtonStyle
    .Descendants(presentationNamespace + "Trigger")
    .ToArray();
var pressedButtonTrigger = buttonTriggers
    .Single(trigger =>
        trigger.Attribute("Property")?.Value == "IsPressed" &&
        trigger.Attribute("Value")?.Value == "True");
Assert(
    buttonTriggers.Any(trigger =>
        trigger.Attribute("Property")?.Value == "IsMouseOver" &&
        trigger.Attribute("Value")?.Value == "True") &&
    !buttonTriggers.Any(trigger => trigger.Attribute("Property")?.Value == "IsKeyboardFocused") &&
    !pressedButtonTrigger.Descendants(presentationNamespace + "Setter")
        .Any(setter => setter.Attribute("Property")?.Value is "Opacity" or "BorderThickness"),
    "Buttons must use gold only while hovered; pressing or retained keyboard focus must not resize, fade, or leave a gold click state behind.");
var dataGridCellStyle = appXaml
    .Descendants(presentationNamespace + "Style")
    .Single(element =>
        element.Attribute("TargetType")?.Value == "DataGridCell" &&
        element.Attribute(xamlNamespace) is null);
var dataGridHeaderStyle = appXaml
    .Descendants(presentationNamespace + "Style")
    .Single(element =>
        element.Attribute("TargetType")?.Value == "DataGridColumnHeader" &&
        element.Attribute(xamlNamespace) is null);
Assert(
    ReadStyleSetter(dataGridCellStyle, "VerticalContentAlignment") == "Center" &&
    dataGridCellStyle.Descendants(presentationNamespace + "ContentPresenter")
        .Any(element => element.Attribute("VerticalAlignment")?.Value == "Center") &&
    ReadStyleSetter(dataGridHeaderStyle, "VerticalContentAlignment") == "Center" &&
    ReadStyleSetter(dataGridHeaderStyle, "HorizontalContentAlignment") == "Center" &&
    applicationResourceKeys.Contains("DataGridTextElementStyle") &&
    !dataGridCellStyle.Descendants(presentationNamespace + "Trigger")
        .Any(trigger => trigger.Attribute("Property")?.Value == "IsKeyboardFocusWithin"),
    "Catalog row text and headers must remain vertically centered, and clicking a row must not draw a separate gold focus box around one cell.");
var tabItemStyle = appXaml
    .Descendants(presentationNamespace + "Style")
    .Single(element =>
        element.Attribute("TargetType")?.Value == "TabItem" &&
        element.Attribute(xamlNamespace) is null);
var catalogTabHeaderPanel = appXaml
    .Descendants(presentationNamespace + "UniformGrid")
    .Single(element => element.Attribute(xamlName)?.Value == "HeaderPanel");
var tabHeaderPresenter = tabItemStyle
    .Descendants(presentationNamespace + "ContentPresenter")
    .Single(element => element.Attribute("ContentSource")?.Value == "Header");
Assert(
    appXaml.Descendants(presentationNamespace + "ContentPresenter")
        .Any(element => element.Attribute(xamlName)?.Value == "PART_SelectedContentHost") &&
    ReadStyleSetter(tabItemStyle, "MinHeight") == "42" &&
    ReadStyleSetter(tabItemStyle, "Margin") == "0" &&
    ReadStyleSetter(tabItemStyle, "HorizontalContentAlignment") == "Stretch" &&
    ReadStyleSetter(tabItemStyle, "VerticalContentAlignment") == "Stretch" &&
    tabHeaderPresenter.Attribute("HorizontalAlignment")?.Value == "Center" &&
    tabHeaderPresenter.Attribute("VerticalAlignment")?.Value == "Center" &&
    catalogTabHeaderPanel.Attribute("Rows")?.Value == "1" &&
    catalogTabHeaderPanel.Attribute("Columns")?.Value == "2" &&
    catalogTabHeaderPanel.Attribute("Margin")?.Value == "0,0,0,5" &&
    !tabItemStyle.Descendants(presentationNamespace + "Trigger")
        .Any(trigger => trigger.Attribute("Property")?.Value == "IsKeyboardFocused") &&
    mainWindowXaml.Descendants(presentationNamespace + "TabItem")
        .All(tabItem => tabItem.Elements(presentationNamespace + "DataGrid").Count() == 1 &&
                        !tabItem.Elements(presentationNamespace + "Border").Any()),
    "The two catalog tabs must share the full width equally, keep only their headers centered, stretch result content from the top edge, and avoid a nested border around their grids.");
var rightSelectionPanel = mainWindowXaml
    .Descendants(presentationNamespace + "ContentControl")
    .Single(control => control.Attribute("Content")?.Value == "选择要生成的内容")
    .Ancestors(presentationNamespace + "Border")
    .First();
var logPanel = mainWindowXaml
    .Descendants(presentationNamespace + "ContentControl")
    .Single(control => control.Attribute("Content")?.Value == "运行日志")
    .Ancestors(presentationNamespace + "Border")
    .First();
var previewButtonElement = ReadNamedMainElement("Button", "PreviewButton");
var applyButtonElement = ReadNamedMainElement("Button", "ApplyButton");
var actionGrid = previewButtonElement.Parent;
var catalogAndActionsGrid = actionGrid?.Parent;
var filterGrid = ReadNamedMainElement("TextBox", "SearchTextBox").Parent;
var filterColumns = filterGrid?.Element(presentationNamespace + "Grid.ColumnDefinitions")
    ?.Elements(presentationNamespace + "ColumnDefinition")
    .ToArray() ?? [];
Assert(
    rightSelectionPanel.Attribute("BorderThickness")?.Value == "0" &&
    logPanel.Attribute("BorderThickness")?.Value == "0" &&
    !mainWindowXaml.Descendants(presentationNamespace + "ContentControl")
        .Any(control => control.Attribute("Content")?.Value == "预览与写入") &&
    !mainWindowXaml.Descendants(presentationNamespace + "TextBlock")
        .Any(textBlock => textBlock.Attribute("Text")?.Value == "同步保存到项目 logs 目录") &&
    actionGrid is not null &&
    actionGrid == applyButtonElement.Parent &&
    actionGrid.Attribute("Grid.Row")?.Value == "1" &&
    previewButtonElement.Attribute("Grid.Column")?.Value == "1" &&
    applyButtonElement.Attribute("Grid.Column")?.Value == "2" &&
    catalogAndActionsGrid?.Elements(presentationNamespace + "TabControl")
        .Any(tabControl => tabControl.Attribute(xamlName)?.Value == "CatalogTabs") == true &&
    filterColumns.Length == 5 &&
    filterColumns[1].Attribute("Width")?.Value == "3*" &&
    filterColumns[3].Attribute("Width")?.Value == "2*" &&
    filterColumns[3].Attribute("MinWidth")?.Value == "190" &&
    filterColumns[3].Attribute("MaxWidth")?.Value == "260" &&
    ReadNamedMainElement("TextBox", "CopiesTextBox").Attribute("Width")?.Value == "116",
    "The right workspace must rely on its title bands instead of grey outer separators, place preview/apply at the catalog's lower right, remove the redundant log helper, and reserve more width for the hero level/XP field.");
Assert(
    applicationResourceKeys.Contains("OpenPanelStyle") &&
    mainWindowXaml.Descendants(presentationNamespace + "Border")
        .Count(border => border.Attribute("Style")?.Value == "{StaticResource OpenPanelStyle}") >= 3 &&
    !mainWindowXaml.Descendants(presentationNamespace + "Border")
        .Any(border => border.Attribute("Style")?.Value == "{StaticResource CardStyle}"),
    "Ordinary main-window regions must use open-sided separators instead of nested closed cards; important controls keep their own button frames.");
Assert(
    mainWindowCode.Contains("if (_trinketStorage is null)", StringComparison.Ordinal) &&
    mainWindowCode.Contains("饰品预览与应用已禁用", StringComparison.Ordinal),
    "The UI must fail closed when the effective trinket storage capacity cannot be resolved.");

var discoveredGame = new GameInstallation(
    @"C:\Steam",
    @"D:\SteamLibrary",
    @"D:\SteamLibrary\steamapps\common\DarkestDungeon",
    @"D:\SteamLibrary\steamapps\workshop\content\262060");
Assert(
    discoveredGame.DefaultLocalModDirectory == @"D:\SteamLibrary\steamapps\common\DarkestDungeon\mods" &&
    discoveredGame.WorkshopDirectory == @"D:\SteamLibrary\steamapps\workshop\content\262060",
    "Automatic discovery must derive the default local Mod and Workshop paths from the discovered Steam library installation.");

var discoveryProfiles = new[]
{
    new SaveProfile("profile_3", @"C:\contract\profile_3", @"C:\contract\profile_3\persist.estate.json", "user", DateTime.UtcNow),
    new SaveProfile("PROFILE_0", @"C:\contract\profile_0", @"C:\contract\profile_0\persist.estate.json", "user", DateTime.UtcNow.AddDays(-1))
};
Assert(
    SteamDiscovery.SelectDefaultProfile(discoveryProfiles)?.ProfileId == "PROFILE_0",
    "Automatic save selection must prefer profile_0 even when another profile is newer.");
Assert(
    SteamDiscovery.SelectDefaultProfile(discoveryProfiles.Take(1).ToArray()) == discoveryProfiles[0] &&
    SteamDiscovery.SelectDefaultProfile([]) is null,
    "Automatic save selection must fall back to the first discovered profile and support an empty result.");
var initialQuirkDialogXaml = System.Xml.Linq.XDocument.Load(Path.Combine(
    repositoryRoot,
    "src",
    "DarkestDungeonSaveEditor.App",
    "InitialQuirkSelectionDialog.xaml"));
var initialQuirkDialogCode = File.ReadAllText(Path.Combine(
    repositoryRoot,
    "src",
    "DarkestDungeonSaveEditor.App",
    "InitialQuirkSelectionDialog.xaml.cs"));
var initialQuirkOpenPanels = initialQuirkDialogXaml
    .Descendants(presentationNamespace + "ContentControl")
    .Where(control => control.Attribute("Content")?.Value is "写入规则" or "怪癖目录")
    .Select(control => control.Ancestors(presentationNamespace + "Border").First())
    .ToArray();
Assert(
    initialQuirkDialogXaml.Root?.Attribute("FontFamily")?.Value == "SimHei, Microsoft YaHei UI" &&
    initialQuirkDialogXaml.Root?.Attribute("Icon")?.Value == "Assets/save-editor.ico" &&
    initialQuirkDialogXaml.Root?.Attribute("Width")?.Value == "1220" &&
    initialQuirkDialogXaml.Root?.Attribute("MinWidth")?.Value == "900" &&
    initialQuirkDialogXaml.Root?.Attribute("MinHeight")?.Value == "560" &&
    HasGamePanelWindowFrame(initialQuirkDialogXaml) &&
    HasOnlySquareCorners(appXaml, mainWindowXaml, initialQuirkDialogXaml) &&
    mainWindowXaml.Descendants(presentationNamespace + "ContentControl")
        .Count(element => element.Attribute("Style")?.Value.Contains("PanelHeaderStyle", StringComparison.Ordinal) == true) >= 3 &&
    initialQuirkDialogXaml.Descendants(presentationNamespace + "ContentControl")
        .Count(element => element.Attribute("Style")?.Value.Contains("PanelHeaderStyle", StringComparison.Ordinal) == true) >= 2 &&
    initialQuirkDialogXaml.Descendants(presentationNamespace + "Border")
        .Count(border => border.Attribute("Style")?.Value == "{StaticResource OpenPanelStyle}") >= 2 &&
    initialQuirkOpenPanels.Length == 2 &&
    initialQuirkOpenPanels.All(border => border.Attribute("BorderThickness")?.Value == "0") &&
    !initialQuirkDialogXaml.Descendants(presentationNamespace + "Border")
        .Any(border => border.Attribute("Background")?.Value == "#9A211E" &&
                       border.Attribute("Grid.Column")?.Value == "0") &&
    !initialQuirkDialogXaml.Descendants(presentationNamespace + "Border")
        .Any(border => border.Attribute("Style")?.Value == "{StaticResource CardStyle}"),
    "Both editor windows must use the app icon, game-matched body font, supported viewport, square double frame, and title-led panels without duplicate outer separators or red header dividers.");
Assert(
    mainWindowCode.Contains("NativeWindowTheme.ApplyDarkTitleBar(this);", StringComparison.Ordinal) &&
    initialQuirkDialogCode.Contains("NativeWindowTheme.ApplyDarkTitleBar(this);", StringComparison.Ordinal) &&
    nativeWindowThemeCode.Contains("DwmwaUseImmersiveDarkMode = 20", StringComparison.Ordinal) &&
    nativeWindowThemeCode.Contains("DwmwaCaptionColor = 35", StringComparison.Ordinal) &&
    nativeWindowThemeCode.Contains("DwmwaTextColor = 36", StringComparison.Ordinal) &&
    nativeWindowThemeCode.Contains("DwmwaBorderColor = 34", StringComparison.Ordinal),
    "Both editor windows must request a dark native caption, light caption text, and themed system border.");
var initialQuirkBindings = initialQuirkDialogXaml
    .Descendants(presentationNamespace + "DataGridTextColumn")
    .Select(column => column.Attribute("Binding")?.Value)
    .ToArray();
Assert(
    initialQuirkBindings.Contains("{Binding Kind}", StringComparer.Ordinal) &&
    initialQuirkBindings.Contains("{Binding WriteStatus}", StringComparer.Ordinal) &&
    initialQuirkDialogXaml.Descendants(presentationNamespace + "DataGridTextColumn")
        .Single(column => column.Attribute("Binding")?.Value == "{Binding Kind}")
        .Attribute("Width")?.Value == "96" &&
    initialQuirkDialogXaml.Descendants(presentationNamespace + "DataGridTextColumn")
        .Single(column => column.Attribute("Header")?.Value == "来源")
        .Attribute("MinWidth")?.Value == "135" &&
    initialQuirkDialogXaml.Descendants(presentationNamespace + "DataGridTextColumn")
        .Single(column => column.Attribute("Header")?.Value == "不可用原因")
        .Attribute("MinWidth")?.Value == "190" &&
    !initialQuirkDialogXaml.ToString().Contains("职业禁用项", StringComparison.Ordinal) &&
    initialQuirkDialogXaml.ToString().Contains(
        "roster_limit 由游戏在招募进入 roster 时执行，生成马车候选不检查也不警告",
        StringComparison.Ordinal) &&
    !initialQuirkDialogCode.Contains("selectedIds.Append(row.Id)", StringComparison.Ordinal) &&
    initialQuirkDialogCode.Contains("IncompatibleQuirkIds.Contains", StringComparison.Ordinal),
    "The initial quirk dialog must fully show fixed/special kind text, keep quota feedback in the summary/click validation, and reserve row-level dynamic reasons for actual incompatibilities.");
var initialQuirkGrid = initialQuirkDialogXaml
    .Descendants(presentationNamespace + "DataGrid")
    .Single(grid => grid.Attribute(xamlName)?.Value == "QuirkGrid");
var initialQuirkCheckBox = initialQuirkGrid
    .Descendants(presentationNamespace + "CheckBox")
    .Single(checkBox => checkBox.Attribute("Click")?.Value == "QuirkCheckBox_Click");
Assert(
    initialQuirkGrid.Attribute("IsReadOnly")?.Value == "True" &&
    initialQuirkCheckBox.Attribute("IsChecked")?.Value == "{Binding IsSelected, Mode=OneWay}" &&
    initialQuirkDialogCode.Contains("_view is IEditableCollectionView editableView", StringComparison.Ordinal) &&
    initialQuirkDialogCode.Contains("editableView.CommitEdit();", StringComparison.Ordinal) &&
    mainWindowCode.Contains("if (dialog?.IsVisible == true)", StringComparison.Ordinal) &&
    mainWindowCode.Contains("dialog.Close();", StringComparison.Ordinal),
    "The quirk selector must not open DataGrid edit transactions and must close an orphaned dialog if ShowDialog unwinds unexpectedly.");

var jarPath = Path.Combine(repositoryRoot, "tools", "DDSaveEditor", "DDSaveEditor.jar");
var runRoot = Path.Combine(
    repositoryRoot,
    "workspaces",
    "contract_tests",
    $"{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}");
var syntheticProjectRoot = Path.Combine(runRoot, "synthetic-log-project");
var syntheticApplicationDirectory = Path.Combine(
    syntheticProjectRoot,
    "src",
    "DarkestDungeonSaveEditor.App",
    "bin",
    "Release",
    "net8.0-windows");
Directory.CreateDirectory(syntheticApplicationDirectory);
File.WriteAllText(
    Path.Combine(syntheticProjectRoot, "DarkestDungeonSaveEditor.sln"),
    string.Empty,
    new UTF8Encoding(false));
Assert(
    SaveEditorLocations.ResolveLogDirectory(syntheticApplicationDirectory)
        .Equals(Path.Combine(syntheticProjectRoot, "logs"), StringComparison.OrdinalIgnoreCase),
    "Development logging should resolve to the nearest project root logs directory.");
var standaloneApplicationDirectory = Path.Combine(
    Path.GetTempPath(),
    $"ddse-standalone-log-contract-{Guid.NewGuid():N}");
Assert(
    SaveEditorLocations.ResolveLogDirectory(standaloneApplicationDirectory)
        .Equals(Path.Combine(standaloneApplicationDirectory, "logs"), StringComparison.OrdinalIgnoreCase),
    "Standalone logging should fall back to a logs directory beside the application.");
var gameRoot = Path.Combine(runRoot, "game");
var workshopRoot = Path.Combine(runRoot, "workshop");
var activeWorkshopRoot = Path.Combine(workshopRoot, "111");
var disabledWorkshopRoot = Path.Combine(workshopRoot, "222");
var localModRoot = Path.Combine(gameRoot, "mods", "LocalTestMod");
var localBackupRoot = Path.Combine(localModRoot, "project-backup");
var additionalLocalModDirectory = Path.Combine(runRoot, "external-local-mods");
var externalLocalModRoot = Path.Combine(additionalLocalModDirectory, "ExternalTestMod");
var externalLocalLocalizationRoot = Path.Combine(externalLocalModRoot, "localization");
var ambiguousDirectLocalModRoot = Path.Combine(runRoot, "ambiguous-direct-local-mod");
var baseHeroRoot = Path.Combine(gameRoot, "heroes", "base_hero");
var activeRuntimeHeroRoot = Path.Combine(activeWorkshopRoot, "heroes", "runtime_hero");
var activeRuntimeHeroDuplicateRoot = Path.Combine(activeWorkshopRoot, "heroes", "runtime_hero_patch");
var activeOverrideHeroRoot = Path.Combine(activeWorkshopRoot, "heroes", "base_hero");
var activeWorkshopLocalHeroRoot = Path.Combine(activeWorkshopRoot, "heroes", "local_hero");
var activeWorkshopPriorityHeroRoot = Path.Combine(activeWorkshopRoot, "heroes", "priority_hero_patch");
var activeWorkshopUpgradeRoot = Path.Combine(activeWorkshopRoot, "upgrades");
var activeWorkshopBuildingUpgradeRoot = Path.Combine(activeWorkshopUpgradeRoot, "building");
var activeWorkshopBuffRoot = Path.Combine(activeWorkshopRoot, "shared", "buffs");
var activeWorkshopLocalizationRoot = Path.Combine(activeWorkshopRoot, "localization");
var baseInventoryRoot = Path.Combine(gameRoot, "inventory");
var activeWorkshopInventoryRoot = Path.Combine(activeWorkshopRoot, "inventory");
var disabledWorkshopInventoryRoot = Path.Combine(disabledWorkshopRoot, "inventory");
var activeWorkshopDlcRoot = Path.Combine(activeWorkshopRoot, "dlc", "100_feature_pack");
var activeWorkshopDlcTrinketRoot = Path.Combine(activeWorkshopDlcRoot, "trinkets");
var activeWorkshopOfficialOverrideHeroRoot = Path.Combine(
    activeWorkshopDlcRoot,
    "heroes",
    "official_override_hero");
var activeWorkshopEnabledDlcFeatureRoot = Path.Combine(
    activeWorkshopDlcRoot,
    "features",
    "enabled_feature");
var ignoredBackupRoot = Path.Combine(activeWorkshopRoot, "backup");
var ignoredModeRoot = Path.Combine(activeWorkshopRoot, "modes", "bloodmoon");
var ignoredDisabledFeatureHeroRoot = Path.Combine(
    activeWorkshopDlcRoot,
    "features",
    "disabled_feature",
    "heroes",
    "disabled_mod_hero");
var ignoredDisabledFeatureTrinketRoot = Path.Combine(
    activeWorkshopDlcRoot,
    "features",
    "disabled_feature",
    "trinkets");
var disabledHeroRoot = Path.Combine(disabledWorkshopRoot, "heroes", "disabled_hero");
var localHeroRoot = Path.Combine(localModRoot, "heroes", "local_hero");
var localPriorityHeroRoot = Path.Combine(localModRoot, "heroes", "priority_hero");
var localHeroUpgradeRoot = Path.Combine(localModRoot, "upgrades");
var localNestedHeroUpgradeRoot = Path.Combine(localHeroUpgradeRoot, "heroes");
var localBuildingUpgradeRoot = Path.Combine(localHeroUpgradeRoot, "building");
var localLocalizationRoot = Path.Combine(localModRoot, "localization");
var baseBuffRoot = Path.Combine(gameRoot, "shared", "buffs");
var baseQuirkRoot = Path.Combine(gameRoot, "shared", "quirk");
var baseCampingRoot = Path.Combine(gameRoot, "raid", "camping");
var baseLocalizationRoot = Path.Combine(gameRoot, "localization");
var baseRosterConfigurationRoot = Path.Combine(gameRoot, "campaign", "roster");
var radiantRosterConfigurationRoot = Path.Combine(gameRoot, "modes", "radiant", "campaign", "roster");
var dlcPackageRoot = Path.Combine(gameRoot, "dlc", "100_feature_pack");
var dlcOverrideHeroRoot = Path.Combine(dlcPackageRoot, "heroes", "official_override_hero");
var enabledDlcFeatureRoot = Path.Combine(dlcPackageRoot, "features", "enabled_feature");
var disabledDlcFeatureRoot = Path.Combine(dlcPackageRoot, "features", "disabled_feature");
var profileRoot = Path.Combine(runRoot, "profile_7");
var decodedSeedPath = Path.Combine(runRoot, "seed.persist.estate.json");
var decodedGameSeedPath = Path.Combine(runRoot, "seed.persist.game.json");
var decodedTownSeedPath = Path.Combine(runRoot, "seed.persist.town.json");
var decodedRosterSeedPath = Path.Combine(runRoot, "seed.persist.roster.json");
var decodedUpgradesSeedPath = Path.Combine(runRoot, "seed.persist.upgrades.json");
var estatePath = Path.Combine(profileRoot, "persist.estate.json");
var gameSavePath = Path.Combine(profileRoot, "persist.game.json");
var townSavePath = Path.Combine(profileRoot, "persist.town.json");
var rosterSavePath = Path.Combine(profileRoot, "persist.roster.json");
var upgradesSavePath = Path.Combine(profileRoot, "persist.upgrades.json");
Directory.CreateDirectory(Path.Combine(gameRoot, "trinkets"));
Directory.CreateDirectory(Path.Combine(activeWorkshopRoot, "trinkets"));
Directory.CreateDirectory(Path.Combine(disabledWorkshopRoot, "trinkets"));
Directory.CreateDirectory(Path.Combine(localModRoot, "trinkets"));
Directory.CreateDirectory(localBackupRoot);
Directory.CreateDirectory(Path.Combine(externalLocalModRoot, "trinkets"));
Directory.CreateDirectory(externalLocalLocalizationRoot);
Directory.CreateDirectory(ambiguousDirectLocalModRoot);
Directory.CreateDirectory(baseHeroRoot);
Directory.CreateDirectory(activeRuntimeHeroRoot);
Directory.CreateDirectory(activeRuntimeHeroDuplicateRoot);
Directory.CreateDirectory(activeOverrideHeroRoot);
Directory.CreateDirectory(activeWorkshopLocalHeroRoot);
Directory.CreateDirectory(activeWorkshopPriorityHeroRoot);
Directory.CreateDirectory(Path.Combine(activeWorkshopPriorityHeroRoot, "priority_hero_A"));
Directory.CreateDirectory(Path.Combine(activeWorkshopPriorityHeroRoot, "priority_hero_B"));
Directory.CreateDirectory(activeWorkshopUpgradeRoot);
Directory.CreateDirectory(activeWorkshopBuildingUpgradeRoot);
Directory.CreateDirectory(activeWorkshopBuffRoot);
Directory.CreateDirectory(activeWorkshopLocalizationRoot);
Directory.CreateDirectory(baseInventoryRoot);
Directory.CreateDirectory(activeWorkshopInventoryRoot);
Directory.CreateDirectory(disabledWorkshopInventoryRoot);
Directory.CreateDirectory(activeWorkshopDlcTrinketRoot);
Directory.CreateDirectory(activeWorkshopOfficialOverrideHeroRoot);
Directory.CreateDirectory(Path.Combine(activeWorkshopDlcRoot, "heroes", "dlc_shared_hero"));
Directory.CreateDirectory(Path.Combine(activeWorkshopDlcRoot, "effects"));
Directory.CreateDirectory(Path.Combine(activeWorkshopDlcRoot, "shared", "quirk"));
Directory.CreateDirectory(Path.Combine(activeWorkshopDlcRoot, "campaign", "town_events"));
Directory.CreateDirectory(Path.Combine(activeWorkshopEnabledDlcFeatureRoot, "trinkets"));
Directory.CreateDirectory(Path.Combine(activeWorkshopEnabledDlcFeatureRoot, "heroes", "enabled_dlc_hero"));
Directory.CreateDirectory(Path.Combine(ignoredBackupRoot, "heroes", "backup_hero"));
Directory.CreateDirectory(Path.Combine(ignoredBackupRoot, "trinkets"));
Directory.CreateDirectory(Path.Combine(ignoredModeRoot, "effects"));
Directory.CreateDirectory(Path.Combine(ignoredModeRoot, "shared", "quirk"));
Directory.CreateDirectory(Path.Combine(ignoredModeRoot, "campaign", "town_events"));
Directory.CreateDirectory(ignoredDisabledFeatureHeroRoot);
Directory.CreateDirectory(ignoredDisabledFeatureTrinketRoot);
Directory.CreateDirectory(disabledHeroRoot);
Directory.CreateDirectory(localHeroRoot);
Directory.CreateDirectory(localPriorityHeroRoot);
Directory.CreateDirectory(localHeroUpgradeRoot);
Directory.CreateDirectory(localNestedHeroUpgradeRoot);
Directory.CreateDirectory(localBuildingUpgradeRoot);
Directory.CreateDirectory(localLocalizationRoot);
Directory.CreateDirectory(Path.Combine(localHeroRoot, "local_hero_A"));
Directory.CreateDirectory(Path.Combine(localHeroRoot, "local_hero_B"));
Directory.CreateDirectory(baseBuffRoot);
Directory.CreateDirectory(baseQuirkRoot);
Directory.CreateDirectory(baseCampingRoot);
Directory.CreateDirectory(baseLocalizationRoot);
Directory.CreateDirectory(baseRosterConfigurationRoot);
Directory.CreateDirectory(radiantRosterConfigurationRoot);
Directory.CreateDirectory(Path.Combine(activeWorkshopRoot, "effects"));
Directory.CreateDirectory(Path.Combine(activeWorkshopRoot, "shared", "quirk"));
Directory.CreateDirectory(Path.Combine(activeWorkshopRoot, "campaign", "town_events"));
Directory.CreateDirectory(Path.Combine(disabledWorkshopRoot, "campaign", "town_events"));
Directory.CreateDirectory(Path.Combine(localModRoot, "campaign", "town_events"));
Directory.CreateDirectory(Path.Combine(localModRoot, "effects"));
Directory.CreateDirectory(Path.Combine(localModRoot, "shared", "quirk"));
Directory.CreateDirectory(Path.Combine(dlcPackageRoot, "trinkets"));
Directory.CreateDirectory(Path.Combine(dlcPackageRoot, "heroes", "dlc_shared_hero"));
Directory.CreateDirectory(dlcOverrideHeroRoot);
Directory.CreateDirectory(Path.Combine(dlcPackageRoot, "effects"));
Directory.CreateDirectory(Path.Combine(dlcPackageRoot, "shared", "quirk"));
Directory.CreateDirectory(Path.Combine(dlcPackageRoot, "campaign", "town_events"));
Directory.CreateDirectory(Path.Combine(enabledDlcFeatureRoot, "trinkets"));
Directory.CreateDirectory(Path.Combine(enabledDlcFeatureRoot, "heroes", "enabled_dlc_hero"));
Directory.CreateDirectory(Path.Combine(disabledDlcFeatureRoot, "trinkets"));
Directory.CreateDirectory(Path.Combine(disabledDlcFeatureRoot, "heroes", "disabled_dlc_hero"));
Directory.CreateDirectory(profileRoot);

File.WriteAllText(
    Path.Combine(baseBuffRoot, "generation.buffs.json"),
    """
    {
      "buffs": [
        { "id": "MAXHP10", "stat_type": "combat_stat_multiply", "stat_sub_type": "max_hp", "amount": 0.1, "rule_type": "always", "is_false_rule": false },
        { "id": "MAXHP20_NO_TRINKETS", "stat_type": "combat_stat_multiply", "stat_sub_type": "max_hp", "amount": 0.2, "rule_type": "no_trinkets", "is_false_rule": false },
        { "id": "MAXHP-5", "stat_type": "combat_stat_multiply", "stat_sub_type": "max_hp", "amount": -0.05, "rule_type": "always", "is_false_rule": false },
        { "id": "MAXHP-10", "stat_type": "combat_stat_multiply", "stat_sub_type": "max_hp", "amount": -0.1, "rule_type": "always", "is_false_rule": false },
        { "id": "MAXHP-100", "stat_type": "combat_stat_multiply", "stat_sub_type": "max_hp", "amount": -1.0, "rule_type": "always", "is_false_rule": false },
        { "id": "MAXHP_CONDITIONAL", "stat_type": "combat_stat_multiply", "stat_sub_type": "max_hp", "amount": 0.5, "rule_type": "in_rank", "is_false_rule": false },
        { "id": "MAXHP_FLAT", "stat_type": "combat_stat_add", "stat_sub_type": "max_hp", "amount": 4, "rule_type": "always", "is_false_rule": false },
        { "id": "ACC5", "stat_type": "combat_stat_add", "stat_sub_type": "accuracy", "amount": 5, "rule_type": "always", "is_false_rule": false }
      ]
    }
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(baseQuirkRoot, "generation.quirk_library.json"),
    """
    {
      "quirks": [
        { "id": "tough", "random_chance": 1, "is_positive": true, "is_disease": false, "incompatible_quirks": ["fragile"], "buffs": ["MAXHP10"] },
        { "id": "natural", "random_chance": 1, "is_positive": true, "is_disease": false, "buffs": ["MAXHP20_NO_TRINKETS"] },
        { "id": "steady", "random_chance": 1, "is_positive": true, "is_disease": false, "buffs": [] },
        { "id": "eagle_eye", "random_chance": 1, "is_positive": true, "is_disease": false, "buffs": ["ACC5"] },
        { "id": "hard_skinned", "random_chance": 1, "is_positive": true, "is_disease": false, "buffs": [] },
        { "id": "slugger", "random_chance": 1, "is_positive": true, "is_disease": false, "buffs": [] },
        { "id": "warrior_of_light", "random_chance": 1, "is_positive": true, "is_disease": false, "buffs": [] },
        { "id": "robust", "random_chance": 1, "is_positive": true, "is_disease": false, "buffs": [] },
        { "id": "same_path_duplicate", "random_chance": 0, "is_positive": false, "is_disease": false, "buffs": [] },
        { "id": "same_path_duplicate", "random_chance": 1, "is_positive": true, "is_disease": false, "buffs": [] },
        { "id": "context_special", "random_chance": 0, "is_positive": true, "is_disease": false, "tags": ["singleton"], "buffs": [] },
        { "id": "context_roster_limited", "random_chance": 0, "is_positive": false, "is_disease": false, "roster_limit": 2, "buffs": [] },
        { "id": "excluded_quirk", "random_chance": 100, "is_positive": true, "is_disease": false, "buffs": [] },
        { "id": "semantic_priority_quirk", "random_chance": 1, "is_positive": false, "is_disease": false, "buffs": [] },
        { "id": "unknown_hp_rule", "random_chance": 100, "is_positive": true, "is_disease": false, "buffs": ["MAXHP_CONDITIONAL"] },
        { "id": "evolving_quirk", "random_chance": 100, "is_positive": true, "is_disease": false, "buffs": [], "evolution_duration_min": 60, "evolution_duration_max": 60, "evolution_class_id": "evolved_quirk" },
        { "id": "evolving_variable", "random_chance": 100, "is_positive": true, "is_disease": false, "buffs": [], "evolution_duration_min": 3, "evolution_duration_max": 15, "evolution_town_progression_duration_change": 1, "evolution_class_id": "evolved_variable" },
        { "id": "evolving_zero", "random_chance": 100, "is_positive": true, "is_disease": false, "buffs": [], "evolution_duration_min": 0, "evolution_duration_max": 0, "evolution_class_id": "evolved_immediately" },
        { "id": "evolving_death", "random_chance": 100, "is_positive": false, "is_disease": true, "buffs": [], "evolution_duration_min": 61, "evolution_duration_max": 61, "evolution_town_progression_duration_change": 30, "evolution_town_attempt_use_item_duration_threshold": 61, "evolution_causes_death": true },
        { "id": "evolution_missing_max", "random_chance": 100, "is_positive": false, "is_disease": false, "buffs": [], "evolution_duration_min": 3, "evolution_class_id": "broken_target" },
        { "id": "evolution_inverted", "random_chance": 100, "is_positive": false, "is_disease": false, "buffs": [], "evolution_duration_min": 15, "evolution_duration_max": 3, "evolution_class_id": "broken_target" },
        { "id": "evolution_fractional", "random_chance": 100, "is_positive": false, "is_disease": false, "buffs": [], "evolution_duration_min": 3.5, "evolution_duration_max": 15, "evolution_class_id": "broken_target" },
        { "id": "evolution_missing_outcome", "random_chance": 100, "is_positive": false, "is_disease": false, "buffs": [], "evolution_duration_min": 3, "evolution_duration_max": 15 },
        { "id": "evolving_unknown_hp", "random_chance": 100, "is_positive": true, "is_disease": false, "buffs": ["MAXHP_CONDITIONAL"], "evolution_duration_min": 30, "evolution_duration_max": 60, "evolution_class_id": "evolved_unknown_hp" },
        { "id": "flat_hp_quirk", "random_chance": 1, "is_positive": true, "is_disease": false, "buffs": ["MAXHP_FLAT"] },
        { "id": "multiple_hp_quirk", "random_chance": 1, "is_positive": true, "is_disease": false, "buffs": ["MAXHP10", "MAXHP-5"] },
        { "id": "fragile", "random_chance": 1, "is_positive": false, "is_disease": false, "incompatible_quirks": ["tough"], "buffs": ["MAXHP-10"] },
        { "id": "soft", "random_chance": 1, "is_positive": false, "is_disease": false, "incompatible_quirks": ["hard_skinned"], "buffs": ["MAXHP-5"] },
        { "id": "fatal_weakness", "random_chance": 1, "is_positive": false, "is_disease": false, "buffs": ["MAXHP-100"] },
        { "id": "clumsy", "random_chance": 1, "is_positive": false, "is_disease": false, "buffs": [] },
        { "id": "slowdraw", "random_chance": 1, "is_positive": false, "is_disease": false, "buffs": [] },
        { "id": "off_guard", "random_chance": 1, "is_positive": false, "is_disease": false, "buffs": [] },
        { "id": "nervous", "random_chance": 1, "is_positive": false, "is_disease": false, "buffs": [] },
        { "id": "weak_grip", "random_chance": 1, "is_positive": false, "is_disease": false, "buffs": [] },
        { "id": "fearful", "random_chance": 1, "is_positive": false, "is_disease": false, "buffs": [] },
        { "id": "test_disease", "random_chance": 1, "is_positive": false, "is_disease": true, "buffs": [] },
        { "id": "test_disease_two", "random_chance": 1, "is_positive": false, "is_disease": true, "buffs": [] },
        { "id": "test_disease_three", "random_chance": 1, "is_positive": false, "is_disease": true, "buffs": [] },
        { "id": "test_disease_four", "random_chance": 1, "is_positive": false, "is_disease": true, "buffs": [] },
        { "id": "evolving_disease", "random_chance": 1, "is_positive": false, "is_disease": true, "buffs": [], "evolution_duration_min": 30, "evolution_duration_max": 60, "evolution_class_id": "evolved_disease" },
        { "id": "unknown_hp_disease", "random_chance": 1, "is_positive": false, "is_disease": true, "buffs": ["MAXHP_CONDITIONAL"] }
      ]
    }
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(baseQuirkRoot, "source_probe.quirk_library.json"),
    """
    {
      "quirks": [
        { "id": "source_probe_quirk", "random_chance": 1, "is_positive": true, "is_disease": false, "buffs": [] }
      ]
    }
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(baseCampingRoot, "default.camping_skills.json"),
    """
    {
      "configuration": { "class_specific_number_of_classes_threshold": 4 },
      "skills": [
        { "id": "encourage", "hero_classes": ["base_hero", "local_hero", "runtime_hero", "dlc_shared_hero", "enabled_dlc_hero"] },
        { "id": "first_aid", "hero_classes": ["base_hero", "local_hero", "runtime_hero", "dlc_shared_hero", "enabled_dlc_hero"] },
        { "id": "local_camp_one", "hero_classes": ["local_hero"] },
        { "id": "local_camp_two", "hero_classes": ["local_hero"] }
      ]
    }
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(baseLocalizationRoot, "names.string_table.xml"),
    """
    <?xml version="1.0" encoding="UTF-8"?>
    <root>
      <language id="english">
        <entry id="str_inventory_title_trinketfocus_ring"><![CDATA[Contract Focus Ring]]></entry>
        <entry id="str_quirk_name_natural"><![CDATA[Contract Natural Constitution]]></entry>
      </language>
      <language id="schinese">
        <entry id="hero_name_0"><![CDATA[Contract One]]></entry>
        <entry id="hero_name_1"><![CDATA[Contract Two]]></entry>
        <entry id="str_inventory_title_trinketfocus_ring"><![CDATA[契约专注戒指]]></entry>
        <entry id="str_quirk_name_natural"><![CDATA[契约自然体质]]></entry>
      </language>
    </root>
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(baseRosterConfigurationRoot, "roster.variables.json"),
    """
    { "resolve_level_thresholds": [0, 2, 8, 14, 24, 36, 48] }
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(radiantRosterConfigurationRoot, "roster.variables.json"),
    """
    { "resolve_level_thresholds": [0, 1, 6, 12, 20, 30, 42] }
    """,
    new UTF8Encoding(false));

File.WriteAllText(
    Path.Combine(gameRoot, "trinkets", "fixture.entries.trinkets.json"),
    """
    {
      "entries": [
        { "id": "focus_ring", "rarity": "very_rare", "limit": 1, "price": 7500 },
        { "id": "unlimited_probe", "rarity": "common", "limit": 0, "price": 100 },
        { "id": "fire_probe", "rarity": "rare", "quest_uses": 3, "price": 2500 },
        { "id": "ambiguous_trinket", "rarity": "common", "price": 100 }
      ]
    }
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(gameRoot, "trinkets", "origin_probe.entries.trinkets.json"),
    """
    { "entries": [ { "id": "origin_probe_trinket", "rarity": "common", "price": 100 } ] }
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(baseInventoryRoot, "base.inventory.system_configs.darkest"),
    """
    inventory_system_config: .type "trinket_storage" .max_slots 9999 .use_stack_limits true
    inventory_system_config: .type "hero_equipped_trinkets" .max_slots 2 .use_stack_limits true
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(activeWorkshopInventoryRoot, "base.inventory.system_configs.darkest"),
    """
    // inventory_system_config: .type "trinket_storage" .max_slots 9999
    inventory_system_config: .type "trinket_storage" // .max_slots 9999 must stay ignored
        .max_slots 3 .use_stack_limits true
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(disabledWorkshopInventoryRoot, "base.inventory.system_configs.darkest"),
    """
    inventory_system_config: .type "trinket_storage" .max_slots 1 .use_stack_limits true
    """,
    new UTF8Encoding(false));

File.WriteAllText(
    Path.Combine(activeWorkshopRoot, "trinkets", "active.entries.trinkets.json"),
    """
    {
      "entries": [
        { "id": "active_workshop_trinket", "rarity": "rare", "price": 3000 },
        { "id": "ambiguous_trinket", "rarity": "rare", "price": 9000 }
      ]
    }
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(activeWorkshopRoot, "modfiles.txt"),
    """
    trinkets/active.entries.trinkets.json 100
    trinkets/local.entries.trinkets.json 100
    trinkets/origin_probe.entries.trinkets.json 100
    inventory/base.inventory.system_configs.darkest 100
    dlc/100_feature_pack/trinkets/shared.entries.trinkets.json 100
    dlc/100_feature_pack/heroes/dlc_shared_hero/dlc_shared_hero.info.darkest 100
    dlc/100_feature_pack/heroes/official_override_hero/official_override_hero.override.darkest 100
    dlc/100_feature_pack/effects/dlc_priority.effects.darkest 100
    dlc/100_feature_pack/shared/quirk/dlc_priority.quirk_library.json 100
    dlc/100_feature_pack/campaign/town_events/dlc_priority.events.json 100
    dlc/100_feature_pack/features/enabled_feature/trinkets/enabled.entries.trinkets.json 100
    dlc/100_feature_pack/features/enabled_feature/heroes/enabled_dlc_hero/enabled_dlc_hero.info.darkest 100
    backup/heroes/backup_hero/backup_hero.info.darkest 100
    backup/trinkets/backup.entries.trinkets.json 100
    heroes/../backup/heroes/backup_hero/backup_hero.info.darkest 100
    trinkets/../backup/trinkets/backup.entries.trinkets.json 100
    modes/bloodmoon/effects/ignored.effects.darkest 100
    modes/bloodmoon/shared/quirk/ignored.quirk_library.json 100
    modes/bloodmoon/campaign/town_events/ignored.events.json 100
    dlc/100_feature_pack/features/disabled_feature/heroes/disabled_mod_hero/disabled_mod_hero.info.darkest 100
    dlc/100_feature_pack/features/enabled_feature/heroes/../../disabled_feature/heroes/disabled_mod_hero/disabled_mod_hero.info.darkest 100
    dlc/100_feature_pack/features/enabled_feature/trinkets/../../disabled_feature/trinkets/disabled.entries.trinkets.json 100
    heroes/runtime_hero/runtime_hero.info.darkest 100
    heroes/runtime_hero/runtime_hero.override.darkest 100
    heroes/runtime_hero_patch/runtime_hero.info.darkest 100
    heroes/base_hero/base_hero.info.darkest 100
    heroes/base_hero/base_hero.override.darkest 100
    heroes/local_hero/local_hero.info.darkest 100
    heroes/priority_hero_patch/priority_hero.info.darkest 100
    upgrades/runtime_hero.upgrades.json 100
    upgrades/building/runtime_hero.upgrades.json 100
    effects/runtime_hero.effects.darkest 100
    effects/priority.effects.darkest 100
    effects/ambiguous_a.effects.darkest 100
    effects/ambiguous_b.effects.darkest 100
    shared/quirk/runtime_hero.quirk_library.json 100
    shared/quirk/source_probe.quirk_library.json 100
    shared/quirk/priority.quirk_library.json 100
    shared/quirk/buff_conflicts.quirk_library.json 100
    shared/quirk/semantic_priority_bottom.quirk_library.json 100
    shared/quirk/ambiguous_a.quirk_library.json 100
    shared/quirk/ambiguous_b.quirk_library.json 100
    shared/buffs/non_hp_a.buffs.json 100
    shared/buffs/non_hp_b.buffs.json 100
    shared/buffs/max_hp_a.buffs.json 100
    shared/buffs/max_hp_b.buffs.json 100
    campaign/town_events/runtime_hero.events.json 100
    campaign/town_events/local_hero.events.json 100
    campaign/town_events/ambiguous_a.events.json 100
    campaign/town_events/ambiguous_b.events.json 100
    localization/111_english.loc2 100
    localization/111_schinese.loc2 100
    localization/ignored_schinese.loc2.unused 100
    localization/windows/ignored_schinese.loc2 100
    localization/ignored.string_table.xml.unused 100
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(activeWorkshopRoot, "trinkets", "local.entries.trinkets.json"),
    """
    {
      "entries": [
        { "id": "local_mod_trinket", "rarity": "very_rare", "price": 9900 }
      ]
    }
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(activeWorkshopRoot, "trinkets", "origin_probe.entries.trinkets.json"),
    """
    { "entries": [ { "id": "origin_probe_trinket", "rarity": "rare", "price": 500 } ] }
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(activeWorkshopDlcTrinketRoot, "shared.entries.trinkets.json"),
    """
    {
      "entries": [
        { "id": "dlc_shared_trinket", "rarity": "very_rare", "price": 8800 }
      ]
    }
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(activeWorkshopDlcRoot, "heroes", "dlc_shared_hero", "dlc_shared_hero.info.darkest"),
    """
    combat_skill: .id "modded_dlc_skill" .level 0 .effect "DLC Priority Effect" .generation_guaranteed
    generation: .is_generation_enabled true .number_of_random_combat_skills 1
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(activeWorkshopOfficialOverrideHeroRoot, "official_override_hero.override.darkest"),
    """
    generation: .is_generation_enabled true
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(activeWorkshopDlcRoot, "effects", "dlc_priority.effects.darkest"),
    """
    effect: .name "DLC Priority Effect" .target "performer" .chance 100% .disease "dlc_top_quirk" .on_hit true
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(activeWorkshopDlcRoot, "shared", "quirk", "dlc_priority.quirk_library.json"),
    """
    {
      "quirks": [
        { "id": "dlc_top_quirk", "random_chance": 0, "is_positive": true }
      ]
    }
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(activeWorkshopDlcRoot, "campaign", "town_events", "dlc_priority.events.json"),
    """
    {
      "events": [
        {
          "id": "recruit_dlc_shared_hero",
          "data": [
            { "type": "bonus_recruit", "string_data": "dlc_shared_hero", "number_data": 6.0 }
          ]
        }
      ]
    }
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(activeWorkshopEnabledDlcFeatureRoot, "trinkets", "enabled.entries.trinkets.json"),
    """
    { "entries": [ { "id": "enabled_dlc_trinket", "rarity": "very_rare", "price": 8200 } ] }
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(activeWorkshopEnabledDlcFeatureRoot, "heroes", "enabled_dlc_hero", "enabled_dlc_hero.info.darkest"),
    """
    combat_skill: .id "modded_enabled_dlc_skill" .level 0
    generation: .is_generation_enabled true .number_of_random_combat_skills 1
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(ignoredBackupRoot, "heroes", "backup_hero", "backup_hero.info.darkest"),
    """
    combat_skill: .id "backup_skill" .level 0
    generation: .is_generation_enabled true .number_of_random_combat_skills 1
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(ignoredBackupRoot, "trinkets", "backup.entries.trinkets.json"),
    """
    { "entries": [ { "id": "backup_trinket", "rarity": "common", "price": 1 } ] }
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(ignoredModeRoot, "effects", "ignored.effects.darkest"),
    """
    effect: .name "Priority Effect" .target "performer" .chance 100% .disease "ignored_mode_quirk" .on_hit true
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(ignoredModeRoot, "shared", "quirk", "ignored.quirk_library.json"),
    """
    {
      "quirks": [
        { "id": "priority_top_quirk", "random_chance": 0, "is_positive": false }
      ]
    }
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(ignoredModeRoot, "campaign", "town_events", "ignored.events.json"),
    """
    {
      "events": [
        {
          "id": "recruit_local_hero",
          "data": [
            { "type": "bonus_recruit", "string_data": "local_hero", "number_data": 99.0 }
          ]
        }
      ]
    }
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(ignoredDisabledFeatureHeroRoot, "disabled_mod_hero.info.darkest"),
    """
    combat_skill: .id "disabled_mod_skill" .level 0
    generation: .is_generation_enabled true .number_of_random_combat_skills 1
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(ignoredDisabledFeatureTrinketRoot, "disabled.entries.trinkets.json"),
    """
    { "entries": [ { "id": "disabled_mod_trinket", "rarity": "common", "price": 1 } ] }
    """,
    new UTF8Encoding(false));

File.WriteAllText(
    Path.Combine(disabledWorkshopRoot, "trinkets", "disabled.entries.trinkets.json"),
    """
    {
      "entries": [
        { "id": "disabled_workshop_trinket", "rarity": "rare", "price": 3000 }
      ]
    }
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(disabledWorkshopRoot, "modfiles.txt"),
    """
    trinkets/disabled.entries.trinkets.json 100
    inventory/base.inventory.system_configs.darkest 100
    heroes/disabled_hero/disabled_hero.info.darkest 100
    campaign/town_events/disabled_hero.events.json 100
    """,
    new UTF8Encoding(false));

File.WriteAllText(
    Path.Combine(localModRoot, "project.xml"),
    """
    <?xml version="1.0" encoding="UTF-8"?>
    <project>
      <Title>Local Test Mod</Title>
    </project>
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(localModRoot, "trinkets", "local.entries.trinkets.json"),
    """
    {
      "entries": [
        { "id": "local_mod_trinket", "rarity": "uncommon", "price": 1500 }
      ]
    }
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(localBackupRoot, "project.xml"),
    """
    <?xml version="1.0" encoding="UTF-8"?>
    <project>
      <Title>Local Test Mod</Title>
    </project>
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(externalLocalModRoot, "project.xml"),
    """
    <?xml version="1.0" encoding="UTF-8"?>
    <project>
      <Title>External Test Mod</Title>
    </project>
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(externalLocalModRoot, "trinkets", "external.entries.trinkets.json"),
    """
    {
      "entries": [
        { "id": "external_local_trinket", "rarity": "rare", "price": 2750 }
      ]
    }
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(externalLocalLocalizationRoot, "external.string_table.xml"),
    """
    <?xml version="1.0" encoding="UTF-8"?>
    <root>
      <language id="english">
        <entry id="str_inventory_title_trinketexternal_local_trinket"><![CDATA[External Contract Trinket]]></entry>
      </language>
      <language id="schinese">
        <entry id="str_inventory_title_trinketexternal_local_trinket"><![CDATA[外部契约饰品]]></entry>
      </language>
    </root>
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(ambiguousDirectLocalModRoot, "project.xml"),
    """
    <?xml version="1.0" encoding="UTF-8"?>
    <project>
      <Title>Local Test Mod</Title>
    </project>
    """,
    new UTF8Encoding(false));

File.WriteAllText(
    Path.Combine(baseHeroRoot, "base_hero.info.darkest"),
    """
    combat_skill: .id "base_strike" .level 0
    skill_selection: .can_select_combat_skills true .number_of_selected_combat_skills_max 4
    generation: .is_generation_enabled true .number_of_positive_quirks_min 1 .number_of_positive_quirks_max 2 .number_of_negative_quirks_min 1 .number_of_negative_quirks_max 2 .number_of_random_combat_skills 4
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(activeRuntimeHeroRoot, "runtime_hero.info.darkest"),
    """
    weapon: .name "runtime_hero_weapon_0" .dmg 4 8
    weapon: .name "runtime_hero_weapon_1" .dmg 5 9 .upgradeRequirementCode 0
    armour: .name "runtime_hero_armour_0" .def 0% .prot 0 .hp 18 .spd 0
    armour: .name "runtime_hero_armour_1" .def 5% .prot 0 .hp 22 .spd 0 .upgradeRequirementCode 0
    combat_skill: .id "runtime_strike" .level 0 .effect "Grant Runtime Quirk" .generation_guaranteed
    combat_skill: .id "runtime_guard" .level 0 .effect "Grant Runtime Quirk" .target_effects "Priority Effect" .generation_guaranteed
    quirk_modifier: .incompatible_class_ids runtime_excluded_a runtime_excluded_b
    skill_selection: .can_select_combat_skills true .number_of_selected_combat_skills_max 2
    generation: .is_generation_enabled true .number_of_positive_quirks_min 0 .number_of_positive_quirks_max 0 .number_of_negative_quirks_min 0 .number_of_negative_quirks_max 0 .number_of_random_combat_skills 2
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(activeRuntimeHeroRoot, "runtime_hero.override.darkest"),
    """
    weapon: .name "runtime_hero_weapon_1" .dmg 99 101
    armour: .name "runtime_hero_armour_0" .def 10%
    armour: .name "runtime_hero_armour_1" .hp 25
    combat_skill: .id "runtime_guard" .level 0 .effect .generation_guaranteed false
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(activeRuntimeHeroDuplicateRoot, "runtime_hero.info.darkest"),
    """
    weapon: .name "runtime_hero_weapon_0" .dmg 4 8
    weapon: .name "runtime_hero_weapon_1" .dmg 5 9 .upgradeRequirementCode 0
    armour: .name "runtime_hero_armour_0" .def 0% .prot 0 .hp 18 .spd 0
    armour: .name "runtime_hero_armour_1" .def 5% .prot 0 .hp 22 .spd 0 .upgradeRequirementCode 0
    combat_skill: .id "runtime_guard" .level 0 .effect "Grant Runtime Quirk" .target_effects "Priority Effect" .generation_guaranteed
    combat_skill: .id "runtime_strike" .level 0 .effect "Grant Runtime Quirk" .generation_guaranteed
    quirk_modifier: .incompatible_class_ids runtime_excluded_b runtime_excluded_a
    skill_selection: .can_select_combat_skills true .number_of_selected_combat_skills_max 2
    generation: .is_generation_enabled true .number_of_positive_quirks_min 0 .number_of_positive_quirks_max 0 .number_of_negative_quirks_min 0 .number_of_negative_quirks_max 0 .number_of_random_combat_skills 2
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(activeWorkshopUpgradeRoot, "runtime_hero.upgrades.json"),
    """
    {
      "trees": [
        {
          "id": "runtime_hero.weapon",
          "tags": ["weapon", "first_level_not_upgrade"],
          "requirements": [
            { "code": "0", "prerequisite_resolve_level": 2 }
          ]
        },
        {
          "id": "runtime_hero.armour",
          "tags": ["armour", "first_level_not_upgrade"],
          "requirements": [
            { "code": "0", "prerequisite_resolve_level": 2 }
          ]
        }
      ]
    }
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(activeWorkshopBuildingUpgradeRoot, "runtime_hero.upgrades.json"),
    """
    {
      "trees": [
        {
          "id": "runtime_hero.weapon",
          "tags": ["weapon"],
          "requirements": [
            { "code": "0", "prerequisite_resolve_level": 6 }
          ]
        }
      ]
    }
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(activeOverrideHeroRoot, "base_hero.info.darkest"),
    """
    combat_skill: .id "overridden_strike" .level 0
    skill_selection: .can_select_combat_skills true .number_of_selected_combat_skills_max 1
    generation: .is_generation_enabled true .number_of_positive_quirks_min 0 .number_of_positive_quirks_max 0 .number_of_negative_quirks_min 0 .number_of_negative_quirks_max 0 .number_of_random_combat_skills 1
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(activeOverrideHeroRoot, "base_hero.override.darkest"),
    """
    generation: .number_of_positive_quirks_min 4
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(activeWorkshopPriorityHeroRoot, "priority_hero.info.darkest"),
    """
    armour: .name "priority_hero_armour_0" .def 0% .prot 0 .hp 17 .spd 0
    combat_skill: .id "workshop_priority_skill" .level 0
    generation: .is_generation_enabled true .number_of_random_combat_skills 1
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(localPriorityHeroRoot, "priority_hero.info.darkest"),
    """
    armour: .name "priority_hero_armour_0" .def 0% .prot 0 .hp 21 .spd 0
    combat_skill: .id "local_priority_skill" .level 0
    generation: .is_generation_enabled true .number_of_random_combat_skills 1
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(activeWorkshopLocalHeroRoot, "local_hero.info.darkest"),
    """
    combat_skill: .id "workshop_lower_priority_skill" .level 0 .effect "Priority Effect" .generation_guaranteed
    skill_selection: .can_select_combat_skills true .number_of_selected_combat_skills_max 1
    generation: .is_generation_enabled true .number_of_positive_quirks_min 0 .number_of_positive_quirks_max 0 .number_of_negative_quirks_min 0 .number_of_negative_quirks_max 0 .number_of_random_combat_skills 1
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(activeWorkshopRoot, "effects", "runtime_hero.effects.darkest"),
    """
    effect: .name "Grant Runtime Quirk" .target "performer" .chance 100% .disease "runtime_fixed_quirk" .on_hit true
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(activeWorkshopRoot, "effects", "priority.effects.darkest"),
    """
    effect: .name "Priority Effect" .target "performer" .chance 100% .disease "priority_bottom_quirk" .on_hit true
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(activeWorkshopRoot, "effects", "ambiguous_a.effects.darkest"),
    """
    effect: .name "Ambiguous Effect" .target "performer" .chance 100% .disease "ambiguous_quirk" .on_hit true
    effect: .name "Ordinary Duplicate" .target "target" .chance 100% .health_damage 1 .on_hit true
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(activeWorkshopRoot, "effects", "ambiguous_b.effects.darkest"),
    """
    effect: .name "Ambiguous Effect" .target "performer" .chance 100% .disease "runtime_fixed_quirk" .on_hit true
    effect: .name "Ordinary Duplicate" .target "target_group" .chance 100% .health_damage 2 .on_hit true
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(activeWorkshopRoot, "shared", "quirk", "runtime_hero.quirk_library.json"),
    """
    {
      "quirks": [
        { "id": "runtime_fixed_quirk", "random_chance": 0, "is_positive": true }
      ]
    }
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(activeWorkshopRoot, "shared", "quirk", "source_probe.quirk_library.json"),
    """
    {
      "quirks": [
        { "id": "source_probe_quirk", "random_chance": 1, "is_positive": true, "is_disease": false, "buffs": [] }
      ]
    }
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(activeWorkshopRoot, "shared", "quirk", "priority.quirk_library.json"),
    """
    {
      "quirks": [
        { "id": "priority_bottom_quirk", "random_chance": 0, "is_positive": true },
        { "id": "non_hp_conflict_quirk", "random_chance": 1, "is_positive": true, "buffs": ["CONFLICT_ACC"] },
        { "id": "max_hp_conflict_quirk", "random_chance": 1, "is_positive": true, "buffs": ["CONFLICT_MAXHP"] }
      ]
    }
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(activeWorkshopRoot, "shared", "quirk", "buff_conflicts.quirk_library.json"),
    """
    {
      "quirks": [
        { "id": "non_hp_conflict_quirk", "random_chance": 1, "is_positive": true, "buffs": ["CONFLICT_ACC"] },
        { "id": "max_hp_conflict_quirk", "random_chance": 1, "is_positive": true, "buffs": ["CONFLICT_MAXHP"] }
      ]
    }
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(activeWorkshopRoot, "shared", "quirk", "semantic_priority_bottom.quirk_library.json"),
    """
    {
      "quirks": [
        { "id": "semantic_priority_quirk", "random_chance": 1, "is_positive": false }
      ]
    }
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(localModRoot, "shared", "quirk", "semantic_priority_top.quirk_library.json"),
    """
    {
      "quirks": [
        { "id": "semantic_priority_quirk", "random_chance": 1, "is_positive": true }
      ]
    }
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(activeWorkshopRoot, "shared", "quirk", "ambiguous_a.quirk_library.json"),
    """
    {
      "quirks": [
        { "id": "ambiguous_quirk", "random_chance": 0, "is_positive": true },
        { "id": "identical_cross_path_quirk", "random_chance": 1, "is_positive": true, "buffs": [] },
        { "id": "evolution_conflict_quirk", "random_chance": 1, "is_positive": false, "is_disease": true, "buffs": [], "evolution_duration_min": 30, "evolution_duration_max": 60, "evolution_class_id": "evolution_target_a" },
        { "id": "identical_evolution_quirk", "random_chance": 1, "is_positive": false, "is_disease": true, "buffs": [], "evolution_duration_min": 30, "evolution_duration_max": 60, "evolution_class_id": "same_target" }
      ]
    }
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(activeWorkshopRoot, "shared", "quirk", "ambiguous_b.quirk_library.json"),
    """
    {
      "quirks": [
        { "id": "ambiguous_quirk", "random_chance": 0, "is_positive": false },
        { "id": "identical_cross_path_quirk", "random_chance": 1, "is_positive": true, "buffs": [] },
        { "id": "evolution_conflict_quirk", "random_chance": 1, "is_positive": false, "is_disease": true, "buffs": [], "evolution_duration_min": 90, "evolution_duration_max": 120, "evolution_class_id": "evolution_target_b" },
        { "id": "identical_evolution_quirk", "random_chance": 1, "is_positive": false, "is_disease": true, "buffs": [], "evolution_duration_min": 30.0, "evolution_duration_max": 60.0, "evolution_class_id": "same_target" }
      ]
    }
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(activeWorkshopBuffRoot, "non_hp_a.buffs.json"),
    """
    { "buffs": [
      { "id": "CONFLICT_ACC", "stat_type": "combat_stat_add", "stat_sub_type": "accuracy", "amount": 3, "rule_type": "always", "is_false_rule": false }
    ] }
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(activeWorkshopBuffRoot, "non_hp_b.buffs.json"),
    """
    { "buffs": [
      { "id": "CONFLICT_ACC", "stat_type": "combat_stat_add", "stat_sub_type": "accuracy", "amount": 5, "rule_type": "always", "is_false_rule": false }
    ] }
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(activeWorkshopBuffRoot, "max_hp_a.buffs.json"),
    """
    { "buffs": [
      { "id": "CONFLICT_MAXHP", "stat_type": "combat_stat_multiply", "stat_sub_type": "max_hp", "amount": 0.1, "rule_type": "always", "is_false_rule": false }
    ] }
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(activeWorkshopBuffRoot, "max_hp_b.buffs.json"),
    """
    { "buffs": [
      { "id": "CONFLICT_MAXHP", "stat_type": "combat_stat_multiply", "stat_sub_type": "max_hp", "amount": 0.2, "rule_type": "always", "is_false_rule": false }
    ] }
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(activeWorkshopRoot, "campaign", "town_events", "runtime_hero.events.json"),
    """
    {
      "events": [
        {
          "id": "recruit_runtime_hero",
          "data": [
            { "type": "bonus_recruit", "string_data": "runtime_hero", "number_data": 1.0 }
          ]
        }
      ]
    }
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(activeWorkshopRoot, "campaign", "town_events", "local_hero.events.json"),
    """
    {
      "events": [
        {
          "id": "recruit_local_hero",
          "data": [
            { "type": "bonus_recruit", "string_data": "local_hero", "number_data": 9.0 }
          ]
        }
      ]
    }
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(activeWorkshopRoot, "campaign", "town_events", "ambiguous_a.events.json"),
    """
    {
      "events": [
        {
          "id": "ambiguous_recruit",
          "data": [
            { "type": "bonus_recruit", "string_data": "runtime_hero", "number_data": 1.0 }
          ]
        },
        {
          "id": "identical_recruit",
          "data": [
            { "type": "bonus_recruit", "string_data": "event_only_hero", "number_data": 1.0 }
          ]
        }
      ]
    }
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(activeWorkshopRoot, "campaign", "town_events", "ambiguous_b.events.json"),
    """
    {
      "events": [
        {
          "id": "ambiguous_recruit",
          "data": [
            { "type": "bonus_recruit", "string_data": "local_hero", "number_data": 7.0 }
          ]
        },
        {
          "id": "identical_recruit",
          "data": [
            { "type": "bonus_recruit", "string_data": "event_only_hero", "number_data": 1.0 }
          ]
        }
      ]
    }
    """,
    new UTF8Encoding(false));

File.WriteAllText(
    Path.Combine(disabledHeroRoot, "disabled_hero.info.darkest"),
    """
    combat_skill: .id "disabled_strike" .level 0
    generation: .is_generation_enabled true .number_of_random_combat_skills 1
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(disabledWorkshopRoot, "campaign", "town_events", "disabled_hero.events.json"),
    """
    {
      "events": [
        {
          "id": "recruit_disabled_hero",
          "data": [
            { "type": "bonus_recruit", "string_data": "disabled_hero", "number_data": 1.0 }
          ]
        }
      ]
    }
    """,
    new UTF8Encoding(false));

File.WriteAllText(
    Path.Combine(localHeroRoot, "local_hero.info.darkest"),
    """
    weapon: .name "local_hero_weapon_0" .dmg 4 8
    weapon: .name "local_hero_weapon_1" .dmg 5 9 .upgradeRequirementCode 0
    weapon: .name "local_hero_weapon_2" .dmg 6 10 .upgradeRequirementCode 1
    weapon: .name "local_hero_weapon_3" .dmg 7 11 .upgradeRequirementCode 2
    weapon: .name "local_hero_weapon_4" .dmg 8 12 .upgradeRequirementCode 3
    armour: .name "local_hero_armour_0" .def 0% .prot 0 .hp 20 .spd 0
    armour: .name "local_hero_armour_1" .def 5% .prot 0 .hp 24 .spd 0 .upgradeRequirementCode 0
    armour: .name "local_hero_armour_2" .def 10% .prot 0 .hp 28 .spd 0 .upgradeRequirementCode 1
    armour: .name "local_hero_armour_3" .def 15% .prot 0 .hp 32 .spd 0 .upgradeRequirementCode 2
    armour: .name "local_hero_armour_4" .def 20% .prot 0 .hp 36 .spd 0 .upgradeRequirementCode 3
    combat_skill: .id "local_skill" .level 0 .effect "Priority Effect" .generation_guaranteed
    combat_skill: .id "local_skill_two" .level 0
    skill_selection: .can_select_combat_skills true
    skill_selection: .number_of_selected_combat_skills_max 1
    generation: .is_generation_enabled true .number_of_positive_quirks_min 1
    generation: .number_of_positive_quirks_max 3 .number_of_negative_quirks_min 0
    generation: .number_of_negative_quirks_max 1 .number_of_class_specific_camping_skills 1 .number_of_shared_camping_skills 1 .number_of_random_combat_skills 2
    quirk_modifier: .incompatible_class_ids excluded_quirk
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(localHeroUpgradeRoot, "local_hero.upgrades.json"),
    """
    {
      "trees": [
        {
          "id": "local_hero.weapon",
          "tags": ["weapon", "first_level_not_upgrade"],
          "requirements": [
            { "code": "0", "prerequisite_resolve_level": 1 },
            { "code": "1", "prerequisite_resolve_level": 2 },
            { "code": "2", "prerequisite_resolve_level": 3 },
            { "code": "3", "prerequisite_resolve_level": 5 }
          ]
        },
        {
          "id": "local_hero.armour",
          "tags": ["armour", "first_level_not_upgrade"],
          "requirements": [
            { "code": "0", "prerequisite_resolve_level": 1 },
            { "code": "1", "prerequisite_resolve_level": 2 },
            { "code": "2", "prerequisite_resolve_level": 3 },
            { "code": "3", "prerequisite_resolve_level": 5 }
          ]
        },
        {
          "id": "local_hero.local_skill",
          "tags": ["combat_skill"],
          "requirements": [
            { "code": "a", "prerequisite_resolve_level": 0 },
            { "code": "A", "prerequisite_resolve_level": 1 },
            { "code": "b", "prerequisite_resolve_level": 2 },
            { "code": "B", "prerequisite_resolve_level": 3 },
            { "code": "c", "prerequisite_resolve_level": 5 }
          ]
        },
        {
          "id": "local_hero.local_skill_two",
          "tags": ["combat_skill"],
          "requirements": [
            { "code": "a", "prerequisite_resolve_level": 0 },
            { "code": "A", "prerequisite_resolve_level": 1 },
            { "code": "b", "prerequisite_resolve_level": 2 },
            { "code": "B", "prerequisite_resolve_level": 3 },
            { "code": "c", "prerequisite_resolve_level": 5 }
          ]
        }
      ]
    }
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(localBuildingUpgradeRoot, "local_hero.upgrades.json"),
    """
    {
      "trees": [
        {
          "id": "local_hero.weapon",
          "tags": ["weapon"],
          "requirements": [
            { "code": "0", "prerequisite_resolve_level": 6 }
          ]
        }
      ]
    }
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(localHeroUpgradeRoot, "case_probe_hero.upgrades.json"),
    """
    {
      "trees": [
        {
          "id": "case_probe_hero.case_skill",
          "tags": ["combat_skill"],
          "requirements": [
            { "code": "a", "prerequisite_resolve_level": 0 }
          ]
        }
      ]
    }
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(localNestedHeroUpgradeRoot, "case_probe_hero.upgrades.json"),
    """
    {
      "trees": [
        {
          "id": "case_probe_hero.case_skill",
          "tags": ["combat_skill"],
          "requirements": [
            { "code": "A", "prerequisite_resolve_level": 0 }
          ]
        }
      ]
    }
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(localLocalizationRoot, "local_hero.string_table.xml"),
    """
    <?xml version="1.0" encoding="UTF-8"?>
    <root>
      <language id="english">
        <entry id="hero_class_name_local_hero"><![CDATA[Local Contract Hero]]></entry>
        <entry id="str_quirk_name_priority_top_quirk"><![CDATA[Priority Legacy]]></entry>
      </language>
      <language id="schinese">
        <entry id="hero_class_name_local_hero"><![CDATA[{colour_start|G2}本地契约英雄{colour_end}]]></entry>
        <entry id="str_quirk_name_priority_top_quirk"><![CDATA[优先传承]]></entry>
      </language>
    </root>
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(localLocalizationRoot, "lenient.string_table.xml"),
    """
    <?xml version="1.1" encoding="UTF-8"?>
    <root>
      <!-- a Mod-style illegal -- comment -->
      <language id="english">
        <entry id="hero_name_99"><![CDATA[Contract Lenient]]></entry>
        <entry id="str_inventory_title_trinketfire_probe"><![CDATA[Lenient Fire Probe]]></entry>
      </language>
      <language id="schinese">
        <entry id="str_inventory_title_trinketfire_probe"><![CDATA[容错火焰探针]]></einntry>
      </language>
    </root>
    """,
    new UTF8Encoding(false));
WriteLoc2(
    Path.Combine(localLocalizationRoot, "LocalTest_english.loc2"),
    new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["str_inventory_title_trinketlocal_mod_trinket"] = "Compiled Local Trinket",
        ["hero_class_name_local_hero"] = "Compiled Local Hero",
        ["str_quirk_name_priority_top_quirk"] = "Compiled Priority Legacy"
    });
var duplicateZeroHashPath = Path.Combine(localLocalizationRoot, "legal_duplicate_zero_english.loc2");
WriteLoc2(
    duplicateZeroHashPath,
    new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["str_inventory_title_trinketlocal_mod_trinket"] = "Compiled Local Trinket",
        ["duplicate_zero_sentinel_one"] = "sentinel one",
        ["duplicate_zero_sentinel_two"] = "sentinel two"
    });
SetLoc2Hash(duplicateZeroHashPath, "duplicate_zero_sentinel_one", 0);
SetLoc2Hash(duplicateZeroHashPath, "duplicate_zero_sentinel_two", 0);
var partiallyBrokenBoundsPath = Path.Combine(localLocalizationRoot, "partially_broken_bounds_english.loc2");
WriteLoc2(
    partiallyBrokenBoundsPath,
    new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["str_inventory_title_trinketlocal_mod_trinket"] = "Corrupt File Must Be Ignored",
        ["unrequested_corrupt_bounds"] = "broken"
    });
CorruptLoc2ValueOffset(partiallyBrokenBoundsPath, "unrequested_corrupt_bounds");
var partiallyBrokenUtf8Path = Path.Combine(localLocalizationRoot, "partially_broken_utf8_english.loc2");
WriteLoc2(
    partiallyBrokenUtf8Path,
    new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["str_inventory_title_trinketlocal_mod_trinket"] = "Corrupt File Must Be Ignored",
        ["unrequested_corrupt_utf8"] = "broken"
    });
CorruptLoc2ValueUtf8(partiallyBrokenUtf8Path, "unrequested_corrupt_utf8");
var partiallyBrokenNulPath = Path.Combine(localLocalizationRoot, "partially_broken_nul_english.loc2");
WriteLoc2(
    partiallyBrokenNulPath,
    new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["str_inventory_title_trinketlocal_mod_trinket"] = "Corrupt File Must Be Ignored",
        ["unrequested_corrupt_nul"] = "broken"
    });
CorruptLoc2ValueTerminator(partiallyBrokenNulPath, "unrequested_corrupt_nul");
var partiallyBrokenZeroLengthPath = Path.Combine(localLocalizationRoot, "partially_broken_zero_length_english.loc2");
WriteLoc2(
    partiallyBrokenZeroLengthPath,
    new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["str_inventory_title_trinketlocal_mod_trinket"] = "Corrupt File Must Be Ignored",
        ["unrequested_corrupt_zero_length"] = "broken"
    });
CorruptLoc2ValueLength(partiallyBrokenZeroLengthPath, "unrequested_corrupt_zero_length", 0);
WriteLoc2Raw(
    Path.Combine(localLocalizationRoot, "LocalTest_schinese.loc2"),
    new Dictionary<string, byte[]>(StringComparer.Ordinal)
    {
        ["str_inventory_title_trinketlocal_mod_trinket"] = EncodeLoc2ColourOpenOnly("编译本地饰品"),
        ["hero_class_name_local_hero"] = ConcatenateBytes(
            Encoding.UTF8.GetBytes("</c>"),
            Encoding.UTF8.GetBytes("编译本地英雄")),
        ["str_quirk_name_priority_top_quirk"] = Encoding.UTF8.GetBytes("编译优先传承")
    });
File.WriteAllBytes(
    Path.Combine(localLocalizationRoot, "broken_english.loc2"),
    [0x01, 0x02, 0x03]);
WriteLoc2Raw(
    Path.Combine(activeWorkshopLocalizationRoot, "111_english.loc2"),
    new Dictionary<string, byte[]>(StringComparer.Ordinal)
    {
        ["str_inventory_title_trinketactive_workshop_trinket"] = ConcatenateBytes(
            Encoding.UTF8.GetBytes("Compiled "),
            EncodeLoc2Colour("Workshop"),
            Encoding.UTF8.GetBytes(" Trinket"))
    });
WriteLoc2(
    Path.Combine(activeWorkshopLocalizationRoot, "111_schinese.loc2"),
    new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["str_inventory_title_trinket_active_workshop_trinket"] = "编译工坊饰品"
    });
File.WriteAllText(
    Path.Combine(activeWorkshopLocalizationRoot, "unlisted_authoring.string_table.xml"),
    """
    <?xml version="1.0" encoding="UTF-8"?>
    <root>
      <language id="english">
        <entry id="hero_class_name_runtime_hero"><![CDATA[Unlisted Runtime Hero]]></entry>
      </language>
      <language id="schinese">
        <entry id="hero_class_name_runtime_hero"><![CDATA[未列清单英雄]]></entry>
      </language>
    </root>
    """,
    new UTF8Encoding(false));
File.WriteAllBytes(
    Path.Combine(activeWorkshopLocalizationRoot, "ignored_schinese.loc2.unused"),
    [0x01, 0x02, 0x03]);
var ignoredPlatformLocalizationRoot = Path.Combine(activeWorkshopLocalizationRoot, "windows");
Directory.CreateDirectory(ignoredPlatformLocalizationRoot);
WriteLoc2(
    Path.Combine(ignoredPlatformLocalizationRoot, "ignored_schinese.loc2"),
    new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["str_inventory_title_trinketactive_workshop_trinket"] = "不应读取的平台名称"
    });
File.WriteAllText(
    Path.Combine(activeWorkshopLocalizationRoot, "ignored.string_table.xml.unused"),
    """
    <?xml version="1.0" encoding="UTF-8"?>
    <root>
      <language id="english">
        <entry id="hero_name_unused"><![CDATA[Ignored Unused Name]]></entry>
      </language>
    </root>
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(localModRoot, "effects", "priority.effects.darkest"),
    """
    effect: .name "Priority Effect" .target "performer" .chance 100% .disease "priority_top_quirk" .on_hit true
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(localModRoot, "shared", "quirk", "priority.quirk_library.json"),
    """
    {
      "quirks": [
        { "id": "priority_top_quirk", "random_chance": 0, "is_positive": true }
      ]
    }
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(localModRoot, "campaign", "town_events", "local_hero.events.json"),
    """
    {
      "events": [
        {
          "id": "recruit_local_hero",
          "data": [
            { "type": "bonus_recruit", "string_data": "local_hero", "number_data": 2.0 }
          ]
        }
      ]
    }
    """,
    new UTF8Encoding(false));

File.WriteAllText(
    Path.Combine(dlcPackageRoot, "trinkets", "shared.entries.trinkets.json"),
    """
    { "entries": [ { "id": "dlc_shared_trinket", "rarity": "rare", "price": 2000 } ] }
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(dlcPackageRoot, "heroes", "dlc_shared_hero", "dlc_shared_hero.info.darkest"),
    """
    combat_skill: .id "shared_dlc_skill" .level 0 .effect "DLC Priority Effect" .generation_guaranteed
    generation: .is_generation_enabled true .number_of_random_combat_skills 1
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(dlcOverrideHeroRoot, "official_override_hero.info.darkest"),
    """
    combat_skill: .id "official_override_skill" .level 0
    generation: .is_generation_enabled false .number_of_random_combat_skills 1
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(dlcOverrideHeroRoot, "official_override_hero.override.darkest"),
    """
    generation: .is_generation_enabled true
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(dlcPackageRoot, "effects", "dlc_priority.effects.darkest"),
    """
    effect: .name "DLC Priority Effect" .target "performer" .chance 100% .disease "dlc_bottom_quirk" .on_hit true
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(dlcPackageRoot, "shared", "quirk", "dlc_priority.quirk_library.json"),
    """
    {
      "quirks": [
        { "id": "dlc_bottom_quirk", "random_chance": 0, "is_positive": true }
      ]
    }
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(dlcPackageRoot, "campaign", "town_events", "dlc_priority.events.json"),
    """
    {
      "events": [
        {
          "id": "recruit_dlc_shared_hero",
          "data": [
            { "type": "bonus_recruit", "string_data": "dlc_shared_hero", "number_data": 1.0 }
          ]
        }
      ]
    }
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(enabledDlcFeatureRoot, "trinkets", "enabled.entries.trinkets.json"),
    """
    { "entries": [ { "id": "enabled_dlc_trinket", "rarity": "rare", "price": 2000 } ] }
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(enabledDlcFeatureRoot, "heroes", "enabled_dlc_hero", "enabled_dlc_hero.info.darkest"),
    """
    combat_skill: .id "enabled_dlc_skill" .level 0
    generation: .is_generation_enabled true .number_of_random_combat_skills 1
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(disabledDlcFeatureRoot, "trinkets", "disabled.entries.trinkets.json"),
    """
    { "entries": [ { "id": "disabled_dlc_trinket", "rarity": "rare", "price": 2000 } ] }
    """,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(disabledDlcFeatureRoot, "heroes", "disabled_dlc_hero", "disabled_dlc_hero.info.darkest"),
    """
    combat_skill: .id "disabled_dlc_skill" .level 0
    generation: .is_generation_enabled true .number_of_random_combat_skills 1
    """,
    new UTF8Encoding(false));

File.WriteAllText(
    decodedSeedPath,
    """
    {
      "base_root": {
        "version": 34,
        "wallet": {},
        "trinkets": {
          "items": {
            "0": { "id": "bleed_charm", "type": "trinket", "amount": 1 }
          }
        },
        "darkest_dungeon_trinket_unlocks": {},
        "estate_items": { "items": {} }
      }
    }
    """,
    new UTF8Encoding(false));

File.WriteAllText(
    decodedGameSeedPath,
    """
    {
      "base_root": {
        "version": 2,
        "game_mode": "base",
        "applied_ugcs_1_0": {
          "10": { "name": "111", "source": "Steam" },
          "2": { "name": "Local Test Mod", "source": "mod_local_source" },
          "3": { "name": "External Test Mod", "source": "mod_local_source" },
          "11": { "name": "333", "source": "Steam" }
        },
        "persistent_ugcs": {
          "applied_ugcs_1_0": {
            "0": { "name": "222", "source": "Steam" }
          }
        },
        "dlc": {
          "0": { "name": "enabled_feature" }
        }
      }
    }
    """,
    new UTF8Encoding(false));

File.WriteAllText(
    decodedTownSeedPath,
    """
    {
      "base_root": {
        "version": 513,
        "buildings": {
          "stage_coach": {
            "store": {
              "hero_recruit": {
                "generated": {},
                "deck_history_version_0": {}
              },
              "shard_hero_recruit": {
                "generated": {
                  "300": {
                    "heroClass": "shieldbreaker",
                    "actor": {},
                    "quirks": {
                      "context_roster_limited": {}
                    }
                  }
                },
                "deck_history_version_0": {}
              }
            }
          }
        }
      }
    }
    """,
    new UTF8Encoding(false));

var rosterHeroesSeed = new JsonObject();
for (var index = 0; index < 36; index++)
{
    rosterHeroesSeed[index.ToString()] = new JsonObject
    {
        ["heroClass"] = "crusader"
    };
}

((JsonObject)rosterHeroesSeed["0"]!)["hero_file_data"] = new JsonObject
{
    ["raw_data"] = new JsonObject
    {
        ["base_root"] = new JsonObject
        {
            ["quirks"] = new JsonObject
            {
                ["context_special"] = new JsonObject(),
                ["context_roster_limited"] = new JsonObject()
            }
        }
    }
};

var rosterSeed = new JsonObject
{
    ["base_root"] = new JsonObject
    {
        ["version"] = 513,
        ["nextGuid"] = 364,
        ["heroes"] = rosterHeroesSeed
    }
};
File.WriteAllText(decodedRosterSeedPath, rosterSeed.ToJsonString(), new UTF8Encoding(false));

File.WriteAllText(
    decodedUpgradesSeedPath,
    """
    {
      "base_root": {
        "version": 1,
        "purchases": {
          "7": {
            "instance_number": 1,
            "tree_id": 123456,
            "requirement_code": "e",
            "is_purchased": true
          }
        }
      }
    }
    """,
    new UTF8Encoding(false));

var codec = new DsonSaveCodec(jarPath);
await codec.EncodeAsync(decodedSeedPath, estatePath, originalBinaryPath: null);
await codec.EncodeAsync(decodedGameSeedPath, gameSavePath, originalBinaryPath: null);
await codec.EncodeAsync(decodedTownSeedPath, townSavePath, originalBinaryPath: null);
await codec.EncodeAsync(decodedRosterSeedPath, rosterSavePath, originalBinaryPath: null);
await codec.EncodeAsync(decodedUpgradesSeedPath, upgradesSavePath, originalBinaryPath: null);
SetRevision(estatePath, [0x00, 0x00, 0x4A, 0x66]);
SetRevision(townSavePath, [0x00, 0x00, 0x4A, 0x67]);
SetRevision(rosterSavePath, [0x00, 0x00, 0x4A, 0x68]);
SetRevision(upgradesSavePath, [0x00, 0x00, 0x4A, 0x69]);

var profile = new SaveProfile(
    "profile_7",
    profileRoot,
    estatePath,
    "contract-user",
    File.GetLastWriteTimeUtc(estatePath));
var locations = new SaveEditorLocations(
    Path.Combine(runRoot, "appdata"),
    Path.Combine(runRoot, "appdata", "workspaces"),
    Path.Combine(runRoot, "appdata", "backups"));
var activeContent = await ActiveContentResolver.ResolveAsync(
    profile,
    gameRoot,
    workshopRoot,
    additionalLocalModDirectory,
    codec,
    locations.WorkspaceDirectory);
var activeModSources = activeContent.Sources
    .Where(source => source.Kind is "local" or "workshop")
    .ToArray();
Assert(activeContent.AppliedModCount == 4, "Expected four applied Mod records, including one missing Workshop item.");
Assert(activeModSources.Length == 3, "Expected two resolved local Mods and one resolved Workshop Mod.");
Assert(activeModSources[0].Id == "local:Local Test Mod", "Applied Mod keys should be sorted numerically.");
Assert(activeModSources[1].Id == "local:External Test Mod", "The selected extra local Mod root should participate in numeric applied order.");
Assert(
    activeModSources[1].Directory.Equals(Path.GetFullPath(externalLocalModRoot), StringComparison.OrdinalIgnoreCase),
    "The enabled external local Mod should resolve from the selected additional directory.");
Assert(activeModSources[2].Id == "workshop:111", "Workshop load order should follow the numeric applied key.");
Assert(activeContent.Issues.Any(issue => issue.Contains("333", StringComparison.Ordinal)), "Missing enabled Workshop content should be reported.");
Assert(activeContent.Sources.Any(source => source.Id == "dlc-package:feature_pack"), "An enabled DLC feature should activate its package root content.");
Assert(activeContent.Sources.Any(source => source.Id == "dlc-feature:enabled_feature"), "The explicitly enabled DLC feature is missing.");
Assert(activeContent.Sources.All(source => source.Id != "dlc-feature:disabled_feature"), "A disabled DLC feature must not become an active source.");
Assert(
    activeContent.Sources.Single(source => source.Id == "dlc-package:feature_pack").VirtualPathPrefix == "dlc/100_feature_pack",
    "A DLC package source must preserve its game-root virtual path prefix.");
Assert(
    activeContent.Sources.Single(source => source.Id == "dlc-feature:enabled_feature").VirtualPathPrefix == "dlc/100_feature_pack/features/enabled_feature",
    "A DLC feature source must preserve its game-root virtual path prefix.");

var defaultLocalContent = await ActiveContentResolver.ResolveAsync(
    profile,
    gameRoot,
    workshopRoot,
    codec,
    Path.Combine(runRoot, "default-local-workspaces"));
Assert(
    defaultLocalContent.Sources.Any(source => source.Id == "local:Local Test Mod") &&
    defaultLocalContent.Sources.All(source => source.Id != "local:External Test Mod") &&
    defaultLocalContent.Issues.Any(issue => issue.Contains("External Test Mod", StringComparison.Ordinal)),
    "The compatible resolver overload should keep scanning default local roots and report an external Mod when no extra root is selected.");

var ambiguousLocalContent = await ActiveContentResolver.ResolveAsync(
    profile,
    gameRoot,
    workshopRoot,
    ambiguousDirectLocalModRoot,
    codec,
    Path.Combine(runRoot, "ambiguous-local-workspaces"));
Assert(
    ambiguousLocalContent.Sources.All(source => source.Id != "local:Local Test Mod") &&
    ambiguousLocalContent.Issues.Any(issue =>
        issue.Contains("Local Test Mod", StringComparison.Ordinal) &&
        issue.Contains("ambiguous", StringComparison.OrdinalIgnoreCase)),
    "Selecting a single Mod project directory must detect a duplicate project title across local roots and refuse to guess.");

var radiantProfileRoot = Path.Combine(runRoot, "profile_radiant");
Directory.CreateDirectory(radiantProfileRoot);
var radiantEstatePath = Path.Combine(radiantProfileRoot, "persist.estate.json");
var radiantGamePath = Path.Combine(radiantProfileRoot, "persist.game.json");
var radiantDecodedGamePath = Path.Combine(runRoot, "seed.persist.game.radiant.json");
File.Copy(estatePath, radiantEstatePath, overwrite: false);
var radiantGameRoot = JsonNode.Parse(File.ReadAllText(decodedGameSeedPath)) as JsonObject
    ?? throw new InvalidDataException("Radiant game seed is invalid.");
((JsonObject)radiantGameRoot["base_root"]!)["game_mode"] = "radiant";
File.WriteAllText(radiantDecodedGamePath, radiantGameRoot.ToJsonString(), new UTF8Encoding(false));
await codec.EncodeAsync(radiantDecodedGamePath, radiantGamePath, originalBinaryPath: null);
var radiantProfile = new SaveProfile(
    "profile_radiant",
    radiantProfileRoot,
    radiantEstatePath,
    "contract-user",
    File.GetLastWriteTimeUtc(radiantEstatePath));
var radiantContent = await ActiveContentResolver.ResolveAsync(
    radiantProfile,
    gameRoot,
    workshopRoot,
    additionalLocalModDirectory,
    codec,
    Path.Combine(runRoot, "radiant-workspaces"));
Assert(
    radiantContent.GameMode == "radiant" &&
    radiantContent.Sources.Any(source => source is { Id: "mode:radiant", Kind: "mode" }),
    "A non-base profile should activate its game-mode content source.");
var radiantHeroCatalog = HeroClassCatalog.Load(radiantContent);
Assert(
    radiantHeroCatalog.ResolveLevelThresholds.SequenceEqual([0, 1, 6, 12, 20, 30, 42]),
    "The active game mode should override base resolve XP thresholds.");
Assert(
    radiantHeroCatalog.HeroClasses.Single(hero => hero.Id == "local_hero")
        .LevelProfiles.Select(profile => profile.ResolveXp)
        .SequenceEqual([0, 1, 6, 12, 20, 30, 42]),
    "A Mod hero should reuse the selected game mode's resolve progression.");

var activeCatalog = TrinketCatalog.Load(activeContent);
var noTrinketDlcRoot = Path.Combine(runRoot, "empty_enabled_dlc");
Directory.CreateDirectory(noTrinketDlcRoot);
var catalogWithEmptyDlc = TrinketCatalog.Load(activeContent with
{
    Sources = activeContent.Sources
        .Append(new ActiveContentSource("dlc:empty", "empty", "dlc", noTrinketDlcRoot, 999))
        .ToArray()
});
Assert(
    catalogWithEmptyDlc.Trinkets.Count == activeCatalog.Trinkets.Count,
    "An enabled DLC source without a trinkets directory should be ignored without failing the catalog.");
Assert(activeCatalog.Trinkets.Count == 10, "Active catalog should contain base, enabled DLC package/feature, active Workshop, default/extra local Mods, provenance probe, and one unresolved duplicate trinket.");
var activeWorkshopTrinket = activeCatalog.Trinkets.Single(item => item.Id == "active_workshop_trinket");
Assert(
    activeWorkshopTrinket.LocalizedName == new BilingualContentName("编译工坊饰品", "Compiled Workshop Trinket"),
    "A manifest-listed LOC2 pair should provide bilingual Workshop trinket names.");
Assert(activeCatalog.Trinkets.Any(item => item.Id == "local_mod_trinket"), "Enabled local Mod trinket is missing.");
var externalLocalTrinket = activeCatalog.Trinkets.Single(item => item.Id == "external_local_trinket");
Assert(
    externalLocalTrinket.Source == "local:External Test Mod" &&
    externalLocalTrinket.LocalizedName == new BilingualContentName("外部契约饰品", "External Contract Trinket"),
    "The selected extra local Mod directory should feed content and bilingual localization catalogs.");
Assert(activeCatalog.Trinkets.Any(item => item.Id == "dlc_shared_trinket"), "Enabled DLC package root trinket is missing.");
Assert(activeCatalog.Trinkets.Any(item => item.Id == "enabled_dlc_trinket"), "Enabled DLC feature trinket is missing.");
Assert(activeCatalog.Trinkets.All(item => item.Id != "disabled_dlc_trinket"), "A disabled DLC feature trinket must not be scanned.");
Assert(activeCatalog.Trinkets.All(item => item.Id != "disabled_workshop_trinket"), "Persistent history must not enable a disabled Workshop Mod.");
Assert(activeCatalog.Trinkets.All(item => item.Id != "backup_trinket"), "A manifest backup path must not enter the active trinket catalog.");
Assert(activeCatalog.Trinkets.All(item => item.Id != "disabled_mod_trinket"), "A normalized Mod path under a disabled DLC feature must not enter the active trinket catalog.");
Assert(activeCatalog.Issues.Any(issue => issue.Contains("no modfiles.txt", StringComparison.Ordinal)), "A local development Mod without a manifest should use the reported fallback scan.");
Assert(
    activeCatalog.Storage is { MaxSlots: 3, Source: "workshop:111" } &&
    !string.IsNullOrWhiteSpace(activeCatalog.Storage.SourceSha256) &&
    activeCatalog.Storage.SourcePath.Equals(
        Path.GetFullPath(Path.Combine(activeWorkshopInventoryRoot, "base.inventory.system_configs.darkest")),
        StringComparison.OrdinalIgnoreCase),
    "The highest-priority active inventory config should define the effective trinket storage capacity without reading commented max_slots values.");
var invalidCapacityModRoot = Path.Combine(runRoot, "invalid_capacity_mod");
var invalidCapacityInventoryRoot = Path.Combine(invalidCapacityModRoot, "inventory");
Directory.CreateDirectory(invalidCapacityInventoryRoot);
File.WriteAllText(
    Path.Combine(invalidCapacityInventoryRoot, "broken.inventory.system_configs.darkest"),
    """
    inventory_system_config: .type "trinket_storage" .max_slots invalid
    """,
    new UTF8Encoding(false));
var invalidCapacityContent = activeContent with
{
    Sources = activeContent.Sources
        .Append(new ActiveContentSource(
            "local:invalid-capacity",
            "Invalid Capacity",
            "local",
            invalidCapacityModRoot,
            -1000))
        .ToArray()
};
var invalidCapacityCatalog = TrinketStorageCatalog.Load(invalidCapacityContent);
Assert(
    invalidCapacityCatalog.Storage is null &&
    invalidCapacityCatalog.Issues.Any(issue =>
        issue.Contains("instead of falling back", StringComparison.Ordinal)),
    "An invalid highest-priority storage definition must fail closed instead of falling back to a lower capacity source.");
var duplicateCapacityCatalog = LoadStorageCapacityProbe(
    activeContent,
    runRoot,
    "duplicate_capacity",
    """inventory_system_config: .type "trinket_storage" .max_slots 3 .max_slots 1""");
Assert(
    duplicateCapacityCatalog.Storage is null,
    "A highest-priority storage entry with duplicate max_slots declarations must fail closed.");
var malformedCapacityTokenCatalog = LoadStorageCapacityProbe(
    activeContent,
    runRoot,
    "malformed_capacity_token",
    """inventory_system_config: .type "trinket_storage" .max_slots 3oops""");
Assert(
    malformedCapacityTokenCatalog.Storage is null,
    "A highest-priority storage entry with a numeric-prefix max_slots token must fail closed.");
var conflictingCapacityModRootA = Path.Combine(runRoot, "conflicting_capacity_mod_a");
var conflictingCapacityModRootB = Path.Combine(runRoot, "conflicting_capacity_mod_b");
var conflictingCapacityInventoryRootA = Path.Combine(conflictingCapacityModRootA, "inventory");
var conflictingCapacityInventoryRootB = Path.Combine(conflictingCapacityModRootB, "inventory");
Directory.CreateDirectory(conflictingCapacityInventoryRootA);
Directory.CreateDirectory(conflictingCapacityInventoryRootB);
File.WriteAllText(
    Path.Combine(conflictingCapacityInventoryRootA, "shared.inventory.system_configs.darkest"),
    """inventory_system_config: .type "trinket_storage" .max_slots 4""",
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(conflictingCapacityInventoryRootB, "shared.inventory.system_configs.darkest"),
    """inventory_system_config: .type "trinket_storage" .max_slots 5""",
    new UTF8Encoding(false));
var conflictingCapacityContent = activeContent with
{
    Sources = activeContent.Sources
        .Concat([
            new ActiveContentSource(
                "local:capacity-conflict-a",
                "Capacity Conflict A",
                "local",
                conflictingCapacityModRootA,
                -2000),
            new ActiveContentSource(
                "local:capacity-conflict-b",
                "Capacity Conflict B",
                "local",
                conflictingCapacityModRootB,
                -2000)
        ])
        .ToArray()
};
var conflictingCapacityCatalog = TrinketStorageCatalog.Load(conflictingCapacityContent);
Assert(
    conflictingCapacityCatalog.Storage is null &&
    conflictingCapacityCatalog.Issues.Any(issue =>
        issue.Contains("multiple providers", StringComparison.Ordinal)),
    "Same-priority storage providers for one virtual path must fail closed.");
var ordinary = activeCatalog.Trinkets.Single(item => item.Id == "focus_ring");
var unlimited = activeCatalog.Trinkets.Single(item => item.Id == "unlimited_probe");
var stateful = activeCatalog.Trinkets.Single(item => item.Id == "fire_probe");
var prioritizedTrinket = activeCatalog.Trinkets.Single(item => item.Id == "local_mod_trinket");
var originProbeTrinket = activeCatalog.Trinkets.Single(item => item.Id == "origin_probe_trinket");
var overriddenDlcTrinket = activeCatalog.Trinkets.Single(item => item.Id == "dlc_shared_trinket");
var overriddenDlcFeatureTrinket = activeCatalog.Trinkets.Single(item => item.Id == "enabled_dlc_trinket");
var ambiguousTrinket = activeCatalog.Trinkets.Single(item => item.Id == "ambiguous_trinket");
Assert(!ordinary.IsStateful, "focus_ring should be writable.");
Assert(ordinary.Limit == 1, "focus_ring should retain its per-id definition limit independently of storage capacity.");
Assert(ordinary.SourceLabel == "原版", "An untouched base trinket should display its original-game provenance.");
Assert(
    ordinary.LocalizedName == new BilingualContentName("契约专注戒指", "Contract Focus Ring"),
    "Trinket catalog should expose distinct Simplified Chinese and English names.");
Assert(stateful.IsStateful && stateful.StatefulFields.Contains("quest_uses"), "fire_probe should be stateful.");
Assert(
    stateful.LocalizedName == new BilingualContentName("容错火焰探针", "Lenient Fire Probe"),
    "Display-name parsing should tolerate common malformed Mod XML without changing the source file.");
Assert(
    prioritizedTrinket.Source == "local:Local Test Mod" &&
    prioritizedTrinket.Rarity == "uncommon" &&
    prioritizedTrinket.AllSources.Count == 2 &&
    !prioritizedTrinket.HasProviderConflict &&
    prioritizedTrinket.LocalizedName == new BilingualContentName("编译本地饰品", "Compiled Local Trinket"),
    "The top Mod should win an exact-relative-path trinket override without becoming a semantic conflict.");
Assert(
    originProbeTrinket.Source == "workshop:111" &&
    originProbeTrinket.SourceLabel == "原版（当前由 创意工坊 Mod：111 覆盖）",
    "A base trinket overridden by a Mod should keep its original-game provenance while naming the effective provider.");
Assert(
    activeCatalog.Issues.Any(issue =>
        issue.Contains("broken_english.loc2", StringComparison.OrdinalIgnoreCase) &&
        issue.Contains("Failed to read localization", StringComparison.Ordinal)),
    "A malformed LOC2 file should be reported without preventing other localized names from loading.");
Assert(
    new[]
    {
        "partially_broken_bounds_english.loc2",
        "partially_broken_utf8_english.loc2",
        "partially_broken_nul_english.loc2",
        "partially_broken_zero_length_english.loc2"
    }.All(fileName => activeCatalog.Issues.Any(issue =>
        issue.Contains(fileName, StringComparison.OrdinalIgnoreCase) &&
        issue.Contains("Failed to read localization", StringComparison.Ordinal))),
    "A LOC2 file must be rejected when any unrequested value has invalid bounds, UTF-8, or termination.");
Assert(
    activeCatalog.Issues.All(issue =>
        !issue.Contains("legal_duplicate_zero_english.loc2", StringComparison.OrdinalIgnoreCase)),
    "Repeated zero-hash compiler sentinel records must not reject an otherwise valid LOC2 file.");
Assert(
    overriddenDlcTrinket.Source == "workshop:111" &&
    overriddenDlcTrinket.Price == 8800 &&
    overriddenDlcTrinket.AllSources.Count == 2 &&
    !overriddenDlcTrinket.HasProviderConflict,
    "A Mod should override a DLC file through its game-root virtual relative path.");
Assert(
    overriddenDlcFeatureTrinket.Source == "workshop:111" &&
    overriddenDlcFeatureTrinket.Price == 8200 &&
    overriddenDlcFeatureTrinket.AllSources.Count == 2 &&
    !overriddenDlcFeatureTrinket.HasProviderConflict,
    "A Mod should override an enabled DLC feature trinket through its full virtual path.");
Assert(
    ambiguousTrinket.HasProviderConflict && ambiguousTrinket.Source == "unresolved",
    "The same trinket id from different effective paths must remain unresolved.");
Assert(
    activeCatalog.Issues.Any(issue => issue.Contains("Trinket 'ambiguous_trinket'", StringComparison.Ordinal)),
    "An unresolved semantic trinket duplicate should be reported.");

var heroCatalog = HeroClassCatalog.Load(activeContent);
Assert(activeContent.GameMode == "base" && heroCatalog.GameMode == "base", "The profile game mode should flow into the hero catalog.");
Assert(
    heroCatalog.ResolveLevelThresholds.SequenceEqual([0, 2, 8, 14, 24, 36, 48]),
    "Base resolve XP thresholds were not loaded from the effective roster variables.");
Assert(heroCatalog.HeroClasses.Count == 7, "Hero catalog should contain base replacement/patch, enabled DLC package/feature, active Workshop, and local classes.");
Assert(heroCatalog.RecruitEvents.Count == 4, "Resolved Workshop, DLC-overlay, local, and identical duplicate bonus_recruit events should be active.");
Assert(heroCatalog.HeroClasses.All(item => item.Id != "disabled_hero"), "Persistent history must not enable a disabled hero class.");
Assert(heroCatalog.HeroClasses.Any(item => item.Id == "dlc_shared_hero"), "Enabled DLC package root hero is missing.");
var officialOverrideHero = heroCatalog.HeroClasses.Single(item => item.Id == "official_override_hero");
Assert(
    officialOverrideHero.Generation?.IsEnabled == true &&
    officialOverrideHero.CombatSkillIds.SequenceEqual(["official_override_skill"]),
    "An official-style hero .override.darkest file must patch its active base .info.darkest definition without replacing the rest of the class template.");
Assert(
    officialOverrideHero.SourceLabel.Contains("官方 DLC：", StringComparison.Ordinal) &&
    officialOverrideHero.SourceLabel.Contains("创意工坊 Mod：111", StringComparison.Ordinal),
    "A Mod that supplies only an .override.darkest file must be named as the current provider without hiding the hero's official origin.");
Assert(heroCatalog.HeroClasses.Any(item => item.Id == "enabled_dlc_hero"), "Enabled DLC feature hero is missing.");
Assert(heroCatalog.HeroClasses.All(item => item.Id != "disabled_dlc_hero"), "A disabled DLC feature hero must not be scanned.");
Assert(heroCatalog.HeroClasses.All(item => item.Id != "backup_hero"), "A manifest backup path must not enter the active hero catalog.");
Assert(heroCatalog.HeroClasses.All(item => item.Id != "disabled_mod_hero"), "A Mod path under a disabled DLC feature must not enter the active hero catalog.");
Assert(heroCatalog.RecruitEvents.All(item => item.HeroClass != "disabled_hero"), "Persistent history must not enable a disabled recruit event.");

var overriddenDlcHero = heroCatalog.HeroClasses.Single(item => item.Id == "dlc_shared_hero");
var overriddenDlcFeatureHero = heroCatalog.HeroClasses.Single(item => item.Id == "enabled_dlc_hero");
Assert(
    overriddenDlcHero.Source == "workshop:111" &&
    overriddenDlcHero.AllSources.Count == 2 &&
    !overriddenDlcHero.HasProviderConflict &&
    overriddenDlcHero.CombatSkillIds.SequenceEqual(["modded_dlc_skill"]),
    "A Mod manifest DLC path should override the matching DLC hero file.");
Assert(
    overriddenDlcHero.RuntimeQuirkSignals.Single().QuirkId == "dlc_top_quirk",
    "Mod manifest DLC paths should be classified for effect and quirk overlays.");
Assert(
    overriddenDlcHero.RecruitEvents.Single().Count == 6.0,
    "Mod manifest DLC paths should be classified for town-event overlays.");
Assert(
    overriddenDlcFeatureHero.Source == "workshop:111" &&
    overriddenDlcFeatureHero.AllSources.Count == 2 &&
    !overriddenDlcFeatureHero.HasProviderConflict &&
    overriddenDlcFeatureHero.CombatSkillIds.SequenceEqual(["modded_enabled_dlc_skill"]),
    "A Mod should override an enabled DLC feature hero through its full virtual path.");

var overriddenHero = heroCatalog.HeroClasses.Single(item => item.Id == "base_hero");
Assert(overriddenHero.Source == "workshop:111", "A Mod should override the base game at the same relative hero path.");
Assert(!overriddenHero.HasProviderConflict && overriddenHero.AllSources.Count == 2, "A verified same-path override should retain its provider chain without becoming ambiguous.");
Assert(
    overriddenHero.SourceLabel == "原版（当前由 创意工坊 Mod：111 覆盖）",
    "A base hero overridden by a Mod should still display as original content and identify its current override provider.");
Assert(overriddenHero.CombatSkillIds.SequenceEqual(["overridden_strike"]), "Effective hero definition should come from the active override.");
Assert(
    overriddenHero.Generation?.PositiveQuirksMin == 4,
    "A manifest-listed hero .override.darkest file must be applied after its selected .info.darkest template.");

var runtimeHero = heroCatalog.HeroClasses.Single(item => item.Id == "runtime_hero");
Assert(
    !runtimeHero.HasProviderConflict &&
    runtimeHero.Source == "workshop:111" &&
    runtimeHero.CombatSkillIds.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(["runtime_strike", "runtime_guard"]) &&
    runtimeHero.GuaranteedCombatSkillIds.SequenceEqual(["runtime_strike"]) &&
    runtimeHero.IncompatibleInitialQuirkIds.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(["runtime_excluded_a", "runtime_excluded_b"]),
    "Hero definitions that only reorder set-like fields should merge, while an explicit false override must remove a previously guaranteed skill.");
Assert(
    runtimeHero.LocalizedName == new BilingualContentName("未列清单英雄", "Unlisted Runtime Hero"),
    "A direct authoring string table should supply hero names when a Mod manifest lists only an unreadable legacy .loc file or omits the XML source.");
Assert(runtimeHero.RecruitEvents.Single().Id == "recruit_runtime_hero", "Workshop bonus_recruit event was not linked to its hero class.");
Assert(
    runtimeHero.RuntimeQuirkSignals.Count == 2 &&
    runtimeHero.RuntimeQuirkSignals.Single(signal => signal.SkillId == "runtime_strike").QuirkId == "runtime_fixed_quirk" &&
    runtimeHero.RuntimeQuirkSignals.Single(signal => signal.SkillId == "runtime_guard").QuirkId == "priority_top_quirk" &&
    runtimeHero.RuntimeQuirkSignals.All(signal => signal.EffectName != "Grant Runtime Quirk" || signal.SkillId != "runtime_guard"),
    "A skill override must replace only the named effect field, clear an explicitly empty field, and retain untouched *_effects fields.");
Assert(
    string.IsNullOrWhiteSpace(runtimeHero.ProgressionUnsupportedReason) &&
    runtimeHero.LevelProfiles.Select(profile => profile.WeaponRank).SequenceEqual([0, 0, 1, 1, 1, 1, 1]) &&
    runtimeHero.LevelProfiles.Select(profile => profile.ArmourHp).SequenceEqual([18d, 18d, 25d, 25d, 25d, 25d, 25d]),
    "A partial equipment override must retain omitted HP/upgrade codes and update only the supplied rank field without breaking 0-max progression.");

var priorityHero = heroCatalog.HeroClasses.Single(item => item.Id == "priority_hero");
Assert(
    !priorityHero.HasProviderConflict &&
    priorityHero.Source == "local:Local Test Mod" &&
    priorityHero.AllSources.Count == 2 &&
    priorityHero.CombatSkillIds.SequenceEqual(["local_priority_skill"]) &&
    priorityHero.ColourVariationCount == 2,
    "A top-listed Mod should win a genuinely different hero definition while inheriting lower-layer art that it does not replace.");

var localHero = heroCatalog.HeroClasses.Single(item => item.Id == "local_hero");
Assert(
    localHero.Source == "local:Local Test Mod" &&
    localHero.AllSources.Count == 2 &&
    !localHero.HasProviderConflict,
    "The top Mod should win an exact-relative-path hero override.");
Assert(localHero.Generation?.PositiveQuirksMin == 1 && localHero.Generation.PositiveQuirksMax == 3, "Repeated generation lines should merge instead of replacing earlier fields.");
Assert(localHero.Generation?.NegativeQuirksMin == 0 && localHero.Generation.NegativeQuirksMax == 1, "Split negative quirk bounds were not merged.");
Assert(localHero.GuaranteedCombatSkillIds.Contains("local_skill"), "Local fallback hero skill flags were not parsed.");
Assert(localHero.BaseHp == 20, "The level-zero armour HP was not parsed from the effective Mod hero template.");
Assert(
    localHero.LocalizedName == new BilingualContentName("编译本地英雄", "Compiled Local Hero"),
    "A Mod hero should prefer the compiled LOC2 names that the game loads over its XML authoring source.");
Assert(
    string.IsNullOrWhiteSpace(localHero.ProgressionUnsupportedReason) &&
    localHero.LevelProfiles.Select(profile => profile.ResolveXp).SequenceEqual([0, 2, 8, 14, 24, 36, 48]) &&
    localHero.LevelProfiles.Select(profile => profile.WeaponRank).SequenceEqual([0, 1, 2, 3, 3, 4, 4]) &&
    localHero.LevelProfiles.Select(profile => profile.ArmourRank).SequenceEqual([0, 1, 2, 3, 3, 4, 4]) &&
    localHero.LevelProfiles.Select(profile => profile.ArmourHp).SequenceEqual([20d, 24d, 28d, 32d, 32d, 36d, 36d]),
    "The Mod hero level profiles should be derived from active XP and upgrade templates.");
Assert(
    localHero.UpgradeTrees.Count == 4 &&
    localHero.UpgradeTrees.Count(tree => tree.Kind == HeroUpgradeTreeKind.CombatSkill) == 2 &&
    localHero.UpgradeTrees.Single(tree => tree.Id == "local_hero.local_skill")
        .Requirements.Select(requirement => requirement.Code)
        .SequenceEqual(["a", "A", "b", "B", "c"]),
    "The active hero upgrade template should retain exact, case-sensitive tree ids and custom requirement codes for save purchases.");
Assert(
    heroCatalog.Issues.Any(issue =>
        issue.Contains("Hero upgrade 'case_probe_hero'", StringComparison.Ordinal) &&
        issue.Contains("conflicting definitions", StringComparison.Ordinal)),
    "Same-priority hero upgrade templates that differ only by requirement-code case must remain an explicit conflict.");
Assert(localHero.ColourVariationCount == 2, "Only the continuous A/B skin directories should be available for random colour selection.");
Assert(localHero.ClassCampingSkillIds.Count == 2 && localHero.SharedCampingSkillIds.Count == 2, "Class and shared camping skills were not separated by the camping configuration.");
Assert(localHero.IncompatibleInitialQuirkIds.Contains("excluded_quirk"), "Class-level incompatible initial quirks were not parsed.");
Assert(localHero.RecruitEvents.Single().Count == 2.0, "Local bonus_recruit count was not parsed.");
Assert(localHero.RuntimeQuirkSignals.Single().QuirkId == "priority_top_quirk", "Top-priority same-path effect and quirk files should supply the non-initial runtime signal.");
Assert(heroCatalog.RecruitEvents.All(item => item.Id != "ambiguous_recruit"), "A same-priority town event conflict must not select a winner.");
Assert(heroCatalog.RecruitEvents.Single(item => item.Id == "identical_recruit").HeroClass == "event_only_hero", "Identical town event definitions should merge.");
var fallbackMessage = $"Mod has no modfiles.txt; standard fallback scan used: {Path.GetFullPath(localModRoot)}";
Assert(heroCatalog.Issues.Contains(fallbackMessage, StringComparer.OrdinalIgnoreCase), "A local hero Mod without a manifest should report its standard fallback scan once.");
Assert(activeCatalog.Issues.Contains(fallbackMessage, StringComparer.OrdinalIgnoreCase), "The trinket and hero fallback reports should use the same deduplicatable message.");
Assert(heroCatalog.Issues.All(issue => !issue.Contains("Hero class 'runtime_hero'", StringComparison.Ordinal)), "Identical hero duplicates should not be reported as conflicts.");
Assert(heroCatalog.Issues.Any(issue => issue.Contains("Effect 'Ambiguous Effect'", StringComparison.Ordinal)), "Semantic-only effect duplicates should be reported.");
Assert(heroCatalog.Issues.All(issue => !issue.Contains("Ordinary Duplicate", StringComparison.Ordinal)), "Effects without disease assignments should not enter runtime-quirk conflict diagnostics.");
Assert(heroCatalog.Issues.Any(issue => issue.Contains("Quirk 'ambiguous_quirk'", StringComparison.Ordinal)), "Semantic-only quirk duplicates should be reported.");
Assert(
    heroCatalog.InitialQuirks.Count(item => item.Id == "ambiguous_quirk") == 2,
    "Semantic-only quirk duplicates should remain in the selection catalog for disabled display.");
var identicalCrossPathQuirk = heroCatalog.InitialQuirks.Single(item => item.Id == "identical_cross_path_quirk");
Assert(
    identicalCrossPathQuirk is { IsPositive: true, WriteStatus: HeroInitialQuirkWriteStatus.Direct } &&
    heroCatalog.Issues.All(issue => !issue.Contains("identical_cross_path_quirk", StringComparison.Ordinal)),
    "Semantically identical quirk definitions at different paths should merge without a conflict.");
Assert(
    heroCatalog.InitialQuirks.Count(item => item.Id == "evolution_conflict_quirk") == 2 &&
    heroCatalog.Issues.Any(issue => issue.Contains("Quirk 'evolution_conflict_quirk'", StringComparison.Ordinal)),
    "Different evolution durations or targets at the same effective priority must remain unresolved.");
Assert(
    heroCatalog.InitialQuirks.Single(item => item.Id == "identical_evolution_quirk") is
    {
        HasEvolution: true,
        Evolution: { DurationMin: 30, DurationMax: 60, TargetQuirkId: "same_target" },
        WriteStatus: HeroInitialQuirkWriteStatus.Direct
    } &&
    heroCatalog.Issues.All(issue => !issue.Contains("identical_evolution_quirk", StringComparison.Ordinal)),
    "Semantically equal evolution metadata should merge even when JSON numbers use integer and decimal spellings.");
var semanticPriorityQuirk = heroCatalog.InitialQuirks.Single(item => item.Id == "semantic_priority_quirk");
Assert(
    semanticPriorityQuirk is { IsPositive: true, Source: "local:Local Test Mod" } &&
    semanticPriorityQuirk.AllSources.Count == 3 &&
    semanticPriorityQuirk.SourceLabel == "原版（当前由 本地 Mod：Local Test Mod 覆盖）" &&
    heroCatalog.Issues.All(issue => !issue.Contains("semantic_priority_quirk", StringComparison.Ordinal)),
    "A genuinely different cross-path quirk definition should follow Mod priority while retaining its original-game provenance.");
var samePathDuplicateQuirk = heroCatalog.InitialQuirks.Single(item => item.Id == "same_path_duplicate");
Assert(
    samePathDuplicateQuirk is { IsPositive: true, RandomChance: 1 },
    "Repeated quirk declarations at one effective path should expose only the last resolved definition.");
Assert(heroCatalog.Issues.Any(issue => issue.Contains("Town event 'ambiguous_recruit'", StringComparison.Ordinal)), "Semantic-only town event duplicates should be reported.");
Assert(heroCatalog.Issues.All(issue => !issue.Contains("identical_recruit", StringComparison.Ordinal)), "Identical town event definitions should not be reported as conflicts.");

Assert(
    heroCatalog.HeroNames.SequenceEqual(["Contract Lenient", "Contract One", "Contract Two"]),
    "The hero_name_* pool should reuse tolerant localization parsing.");
Assert(
    heroCatalog.HeroNames.All(name => name != "Ignored Unused Name") &&
    heroCatalog.Issues.All(issue => !issue.Contains("ignored.string_table.xml", StringComparison.OrdinalIgnoreCase)) &&
    activeCatalog.Issues.All(issue => !issue.Contains("ignored.string_table.xml", StringComparison.OrdinalIgnoreCase)) &&
    heroCatalog.Issues.All(issue => !issue.Contains("ignored_schinese.loc2", StringComparison.OrdinalIgnoreCase)) &&
    activeCatalog.Issues.All(issue => !issue.Contains("ignored_schinese.loc2", StringComparison.OrdinalIgnoreCase)),
    "Unused and platform-specific localization manifest entries must stay outside active XML/LOC2 catalogs.");
var naturalQuirk = heroCatalog.InitialQuirks.Single(item => item.Id == "natural");
Assert(
    naturalQuirk.LocalizedName == new BilingualContentName("契约自然体质", "Contract Natural Constitution"),
    "A quirk should expose distinct Simplified Chinese and English names.");
Assert(naturalQuirk.SourceLabel == "原版", "An untouched base quirk should display its original-game provenance.");
var sourceProbeQuirk = heroCatalog.InitialQuirks.Single(item => item.Id == "source_probe_quirk");
Assert(
    sourceProbeQuirk.Source == "workshop:111" &&
    sourceProbeQuirk.SourceLabel == "原版（当前由 创意工坊 Mod：111 覆盖）" &&
    sourceProbeQuirk.AllSources.Count == 2,
    "A base quirk overridden at the same virtual path should retain and display its full provider provenance.");
Assert(
    naturalQuirk is
    {
        Kind: HeroInitialQuirkKind.Natural,
        IsNaturalRandomEligible: true,
        WriteStatus: HeroInitialQuirkWriteStatus.Direct
    } &&
    naturalQuirk.MaxHpModifier is { Amount: 0.2, RuleType: "no_trinkets" },
    "The original no_trinkets max-HP quirk shape should be supported for an empty-trinket candidate.");
var specialQuirk = heroCatalog.InitialQuirks.Single(item => item.Id == "priority_top_quirk");
Assert(
    specialQuirk is
    {
        Kind: HeroInitialQuirkKind.Special,
        IsNaturalRandomEligible: false,
        WriteStatus: HeroInitialQuirkWriteStatus.Direct
    },
    "A fixed/special quirk with a fully understood save shape should be directly selectable without entering the natural random pool.");
Assert(
    specialQuirk.LocalizedName == new BilingualContentName("编译优先传承", "Compiled Priority Legacy"),
    "A Mod special quirk should retain both compiled localized display names.");
Assert(
    heroCatalog.Issues.Any(issue =>
        issue.Contains("broken_english.loc2", StringComparison.OrdinalIgnoreCase) &&
        issue.Contains("Failed to read localization", StringComparison.Ordinal)),
    "Hero and quirk localization should survive a separate malformed LOC2 file.");
var nonHpConflictQuirk = heroCatalog.InitialQuirks.Single(item => item.Id == "non_hp_conflict_quirk");
Assert(
    nonHpConflictQuirk.WriteStatus == HeroInitialQuirkWriteStatus.Direct,
    "An unresolved non-HP Buff should be left to the game runtime instead of blocking explicit quirk selection.");
var maxHpConflictQuirk = heroCatalog.InitialQuirks.Single(item => item.Id == "max_hp_conflict_quirk");
Assert(
    maxHpConflictQuirk.WriteStatus == HeroInitialQuirkWriteStatus.Unverified &&
    maxHpConflictQuirk.WriteStatusReason.Contains("max_hp", StringComparison.OrdinalIgnoreCase),
    "An unresolved max-HP Buff must still block explicit quirk selection because current_hp cannot be derived safely.");
var contextualQuirk = heroCatalog.InitialQuirks.Single(item => item.Id == "context_special");
Assert(
    contextualQuirk is
    {
        Kind: HeroInitialQuirkKind.Special,
        WriteStatus: HeroInitialQuirkWriteStatus.RequiresSaveContext,
        DefinitionLimit: 1
    } &&
    contextualQuirk.WriteStatusReason.Contains("超限仅警告", StringComparison.Ordinal),
    "A singleton special quirk should expose limit one and remain selectable through preview-time context checking.");
var rosterLimitedQuirk = heroCatalog.InitialQuirks.Single(item => item.Id == "context_roster_limited");
Assert(
    rosterLimitedQuirk is
    {
        WriteStatus: HeroInitialQuirkWriteStatus.Direct,
        DefinitionLimit: null
    },
    "A positive roster_limit must remain directly writable because the game enforces it when the candidate is recruited, not when it is generated in the stagecoach.");
var diseaseQuirk = heroCatalog.InitialQuirks.Single(item => item.Id == "test_disease");
Assert(
    diseaseQuirk is
    {
        Kind: HeroInitialQuirkKind.Disease,
        HasEvolution: false,
        WriteStatus: HeroInitialQuirkWriteStatus.Direct
    },
    "A disease without unknown HP or save-context state should be directly writable and remain separately classified.");
var evolvingQuirk = heroCatalog.InitialQuirks.Single(item => item.Id == "evolving_quirk");
Assert(
    evolvingQuirk is
    {
        HasEvolution: true,
        Evolution:
        {
            DurationMin: 60,
            DurationMax: 60,
            TargetQuirkId: "evolved_quirk",
            CausesDeath: false
        },
        WriteStatus: HeroInitialQuirkWriteStatus.Direct
    },
    "A complete evolution definition should expose its structured duration and target metadata.");
Assert(
    heroCatalog.InitialQuirks.Single(item => item.Id == "evolving_disease") is
    {
        Kind: HeroInitialQuirkKind.Disease,
        HasEvolution: true,
        WriteStatus: HeroInitialQuirkWriteStatus.Direct
    },
    "An evolving disease without an unknown HP rule should use the same initialized-duration contract.");
Assert(
    heroCatalog.InitialQuirks.Single(item => item.Id == "evolving_variable").Evolution is
    {
        DurationMin: 3,
        DurationMax: 15,
        TownProgressionDurationChange: 1,
        TargetQuirkId: "evolved_variable",
        CausesDeath: false
    },
    "A variable Mod evolution range should remain attached to its own quirk definition.");
Assert(
    heroCatalog.InitialQuirks.Single(item => item.Id == "evolving_death") is
    {
        WriteStatus: HeroInitialQuirkWriteStatus.Direct,
        Evolution:
        {
            DurationMin: 61,
            DurationMax: 61,
            TownProgressionDurationChange: 30,
            TargetQuirkId: null,
            CausesDeath: true,
            TownAttemptUseItemDurationThreshold: 61
        }
    },
    "The original-game death evolution shape should be valid without an evolution_class_id.");
Assert(
    heroCatalog.InitialQuirks.Single(item => item.Id == "evolution_missing_max") is
    {
        HasEvolution: false,
        WriteStatus: HeroInitialQuirkWriteStatus.Unverified
    } missingMaximumEvolution &&
    missingMaximumEvolution.WriteStatusReason.Contains("evolution_duration_max", StringComparison.Ordinal),
    "A missing evolution maximum must remain visible but unavailable.");
Assert(
    heroCatalog.InitialQuirks.Single(item => item.Id == "evolution_inverted") is
    {
        HasEvolution: false,
        WriteStatus: HeroInitialQuirkWriteStatus.Unverified
    } invertedEvolution &&
    invertedEvolution.WriteStatusReason.Contains("下限不能大于上限", StringComparison.Ordinal),
    "An inverted evolution range must not silently fall back to zero.");
Assert(
    heroCatalog.InitialQuirks.Single(item => item.Id == "evolution_fractional") is
    {
        HasEvolution: false,
        WriteStatus: HeroInitialQuirkWriteStatus.Unverified
    } fractionalEvolution &&
    fractionalEvolution.WriteStatusReason.Contains("32 位整数", StringComparison.Ordinal),
    "A fractional evolution duration cannot be represented by the integer save field.");
Assert(
    heroCatalog.InitialQuirks.Single(item => item.Id == "evolution_missing_outcome") is
    {
        HasEvolution: false,
        WriteStatus: HeroInitialQuirkWriteStatus.Unverified
    } missingOutcomeEvolution &&
    missingOutcomeEvolution.WriteStatusReason.Contains("evolution_class_id", StringComparison.Ordinal),
    "An evolution definition without a target or death outcome must be rejected.");
Assert(
    heroCatalog.InitialQuirks.Single(item => item.Id == "unknown_hp_disease").WriteStatus ==
    HeroInitialQuirkWriteStatus.Unverified,
    "Disease support must not bypass an unknown max-HP rule.");
Assert(
    heroCatalog.InitialQuirks.Single(item => item.Id == "evolving_unknown_hp") is
    {
        HasEvolution: true,
        WriteStatus: HeroInitialQuirkWriteStatus.Unverified
    },
    "Allowing evolution metadata must not bypass an unknown conditional max-HP rule.");
Assert(
    heroCatalog.InitialQuirks.Single(item => item.Id == "flat_hp_quirk").WriteStatus ==
    HeroInitialQuirkWriteStatus.Unverified,
    "A flat max-HP modifier should remain blocked until the absolute current_hp formula is verified.");
Assert(
    heroCatalog.InitialQuirks.Single(item => item.Id == "multiple_hp_quirk").WriteStatus ==
    HeroInitialQuirkWriteStatus.Unverified,
    "Multiple max-HP Buffs on one quirk should remain blocked until their stacking formula is verified.");
StagecoachHeroCandidateFactory.ValidateInitialQuirkSelection(heroCatalog, localHero, []);
StagecoachHeroCandidateFactory.ValidateInitialQuirkSelection(
    heroCatalog,
    localHero,
    ["natural", "tough"]);
StagecoachHeroCandidateFactory.ValidateInitialQuirkSelection(
    heroCatalog,
    localHero,
    ["same_path_duplicate"]);
StagecoachHeroCandidateFactory.ValidateInitialQuirkSelection(
    heroCatalog,
    localHero,
    ["priority_top_quirk"]);
StagecoachHeroCandidateFactory.ValidateInitialQuirkSelection(
    heroCatalog,
    localHero,
    ["non_hp_conflict_quirk"]);
StagecoachHeroCandidateFactory.ValidateInitialQuirkSelection(
    heroCatalog,
    localHero,
    ["excluded_quirk"]);
StagecoachHeroCandidateFactory.ValidateInitialQuirkSelection(
    heroCatalog,
    localHero,
    ["evolving_quirk"]);
StagecoachHeroCandidateFactory.ValidateInitialQuirkSelection(
    heroCatalog,
    localHero,
    ["test_disease", "test_disease_two", "test_disease_three"]);
StagecoachHeroCandidateFactory.ValidateInitialQuirkSelection(
    heroCatalog,
    localHero,
    ["context_special", "context_roster_limited"]);
AssertInitialQuirkSelectionRejected(heroCatalog, localHero, ["tough", "fragile"], "互斥");
AssertInitialQuirkSelectionRejected(heroCatalog, localHero, ["evolution_missing_max"], "当前不能显式写入");
AssertInitialQuirkSelectionRejected(heroCatalog, localHero, ["evolution_inverted"], "当前不能显式写入");
AssertInitialQuirkSelectionRejected(heroCatalog, localHero, ["evolution_fractional"], "当前不能显式写入");
AssertInitialQuirkSelectionRejected(heroCatalog, localHero, ["evolution_missing_outcome"], "当前不能显式写入");
AssertInitialQuirkSelectionRejected(heroCatalog, localHero, ["unknown_hp_rule"], "当前不能显式写入");
AssertInitialQuirkSelectionRejected(heroCatalog, localHero, ["evolving_unknown_hp"], "当前不能显式写入");
AssertInitialQuirkSelectionRejected(heroCatalog, localHero, ["flat_hp_quirk"], "当前不能显式写入");
AssertInitialQuirkSelectionRejected(heroCatalog, localHero, ["multiple_hp_quirk"], "当前不能显式写入");
AssertInitialQuirkSelectionRejected(heroCatalog, localHero, ["ambiguous_quirk"], "多个未解析定义");
AssertInitialQuirkSelectionRejected(heroCatalog, localHero, ["evolution_conflict_quirk"], "多个未解析定义");
AssertInitialQuirkSelectionRejected(heroCatalog, localHero, ["max_hp_conflict_quirk"], "当前不能显式写入");
AssertInitialQuirkSelectionRejected(heroCatalog, localHero, ["fatal_weakness"], "合计 HP 修正无效");
Assert(
    heroCatalog.InitialQuirks.Single(item => item.Id == "unknown_hp_rule").WriteStatus == HeroInitialQuirkWriteStatus.Unverified,
    "An unverified conditional max-HP rule must not enter the safe random pool.");

var generatedCandidate = StagecoachHeroCandidateFactory.Generate(heroCatalog, localHero, seed: 1729);
var generatedPreview = generatedCandidate.Preview;
Assert(
    generatedPreview.Name is "Contract Lenient" or "Contract One" or "Contract Two",
    "Generated candidate name did not come from the active localization pool.");
Assert(generatedPreview.HeroClass == "local_hero", "Generated candidate has the wrong Mod class ID.");
Assert(
    generatedPreview is { ResolveLevel: 0, ResolveXp: 0, WeaponRank: 0, ArmourRank: 0 },
    "The compatibility overload should continue generating a level-zero candidate.");
Assert(generatedPreview.ColourVariation is >= 0 and < 2, "Generated colour variation is outside the verified A/B range.");
Assert(generatedPreview.PositiveQuirks.Count == 0, "Default console-style generation should not add positive quirks.");
Assert(generatedPreview.NegativeQuirks.Count == 0, "Default console-style generation should not add negative quirks.");
Assert(generatedPreview.Diseases.Count == 0, "Default console-style generation should not add diseases.");
Assert(generatedPreview.CombatSkills.SequenceEqual(["local_skill"]), "The guaranteed level-zero combat skill was not selected.");
Assert(generatedPreview.CampingSkills.Count == 2, "The class template should select one shared and one class camping skill.");
Assert(Math.Abs(generatedPreview.CurrentHp - 20.0) < 0.000001, "A blank candidate should start at the class base HP.");
var generatedActor = generatedCandidate.Candidate["actor"] as JsonObject;
Assert(generatedActor?["buff_group_next_guid"]?.GetValue<int>() == 2, "A generated candidate should use the minimum baseline observed in real saved stagecoach candidates.");
Assert((generatedActor?["buff_group"] as JsonObject)?.Count == 0, "A blank candidate should not contain actor buffs.");
Assert(generatedActor?["current_hp"]?.GetValue<double>() == 20.0, "Candidate JSON current_hp differs from the blank preview.");
Assert(
    generatedCandidate.Candidate["resolveXp"]?.GetValue<int>() == 0 &&
    generatedCandidate.Candidate["weapon_rank"]?.GetValue<int>() == 0 &&
    generatedCandidate.Candidate["armour_rank"]?.GetValue<int>() == 0,
    "A level-zero candidate should write zero XP and equipment ranks.");
var generatedQuirkMap = generatedCandidate.Candidate["quirks"] as JsonObject;
Assert(generatedQuirkMap?.Count == 0, "Default candidate JSON should contain an empty quirk map.");
var expectedCampingTreeIds = localHero.SharedCampingSkillIds
    .Concat(localHero.ClassCampingSkillIds)
    .Select(skillId => $"local_hero.{skillId}")
    .ToHashSet(StringComparer.Ordinal);
Assert(
    generatedCandidate.UpgradePurchases.Count == 6 &&
    generatedCandidate.UpgradePurchases.Count(purchase =>
        (purchase.TreeId == "local_hero.local_skill" ||
         purchase.TreeId == "local_hero.local_skill_two") &&
        purchase.RequirementCode == "a") == 2 &&
    generatedCandidate.UpgradePurchases.Count(purchase =>
        expectedCampingTreeIds.Contains(purchase.TreeId) &&
        purchase.RequirementCode == "0") == 4,
    "A level-zero candidate should unlock every combat tree base requirement and every available camping skill.");
Assert(
    generatedPreview.CampingSkills.Count == 2 &&
    expectedCampingTreeIds.Count == 4,
    "Unlocking every camping skill must not equip every camping skill in the candidate selection map.");

var partialCombatUpgradeHero = localHero with
{
    UpgradeTrees = localHero.UpgradeTrees
        .Where(tree => tree.Id != "local_hero.local_skill_two")
        .ToArray()
};
AssertHeroLevelGenerationRejected(
    heroCatalog,
    partialCombatUpgradeHero,
    0,
    "local_hero.local_skill_two");

var emptyCombatUpgradeHero = localHero with
{
    UpgradeTrees = localHero.UpgradeTrees
        .Select(tree => tree.Id == "local_hero.local_skill_two"
            ? tree with { Requirements = [] }
            : tree)
        .ToArray()
};
AssertHeroLevelGenerationRejected(
    heroCatalog,
    emptyCombatUpgradeHero,
    0,
    "在 0 级没有可用 requirement");

var delayedCombatUpgradeHero = localHero with
{
    UpgradeTrees = localHero.UpgradeTrees
        .Select(tree => tree.Id == "local_hero.local_skill_two"
            ? tree with
            {
                Requirements = [new HeroUpgradeRequirementDefinition("a", 1)]
            }
            : tree)
        .ToArray()
};
AssertHeroLevelGenerationRejected(
    heroCatalog,
    delayedCombatUpgradeHero,
    0,
    "在 0 级没有可用 requirement");

var explicitCandidate = StagecoachHeroCandidateFactory.Generate(
    heroCatalog,
    localHero,
    seed: 1729,
    selectedInitialQuirkIds: ["steady", "eagle_eye", "clumsy"]);
Assert(
    explicitCandidate.Preview.PositiveQuirks.SequenceEqual(["steady", "eagle_eye"]) &&
    explicitCandidate.Preview.NegativeQuirks.SequenceEqual(["clumsy"]),
    "Explicit ordinary quirks should be preserved exactly without random additions.");
Assert(
    Math.Abs(explicitCandidate.Preview.CurrentHp - 20.0) < 0.000001,
    "A non-HP attribute quirk must not be precomputed into current_hp.");
var explicitQuirkMap = explicitCandidate.Candidate["quirks"] as JsonObject;
Assert(explicitQuirkMap?.Count == 3, "Explicit candidate JSON quirk map does not match the selected IDs.");
Assert(
    explicitQuirkMap!.All(pair =>
        pair.Value?["is_new"]?.GetValue<bool>() == true &&
        pair.Value?["mission_count"]?.GetValue<int>() == 0 &&
        pair.Value?["evolution_duration_remaining"]?.GetValue<int>() == 0),
    "Non-evolving initial quirks should retain canonical flags, a zero mission count, and a zero evolution field.");

var specialCandidate = StagecoachHeroCandidateFactory.Generate(
    heroCatalog,
    localHero,
    seed: 1729,
    selectedInitialQuirkIds: ["priority_top_quirk"]);
Assert(
    specialCandidate.Preview.PositiveQuirks.SequenceEqual(["priority_top_quirk"]),
    "A directly writable fixed/special quirk should be preserved exactly in the generated candidate.");

var contextLimitedCandidate = StagecoachHeroCandidateFactory.Generate(
    heroCatalog,
    localHero,
    seed: 1729,
    selectedInitialQuirkIds: ["context_special", "context_roster_limited"]);
Assert(
    contextLimitedCandidate.Preview.PositiveQuirks.SequenceEqual(["context_special"]) &&
    contextLimitedCandidate.Preview.NegativeQuirks.SequenceEqual(["context_roster_limited"]),
    "A singleton or roster-limited quirk should generate normally before its save-context limit is previewed.");

var classExcludedCandidate = StagecoachHeroCandidateFactory.Generate(
    heroCatalog,
    localHero,
    seed: 1729,
    selectedInitialQuirkIds: ["excluded_quirk"]);
Assert(
    classExcludedCandidate.Preview.PositiveQuirks.SequenceEqual(["excluded_quirk"]),
    "Console-style generation should allow any directly writable recognized quirk regardless of the class template's incompatible_class_ids list.");

var evolvingCandidate = StagecoachHeroCandidateFactory.Generate(
    heroCatalog,
    localHero,
    seed: 1729,
    selectedInitialQuirkIds: ["evolving_quirk"]);
var evolvingCandidateQuirk = evolvingCandidate.Candidate["quirks"]?["evolving_quirk"];
Assert(
    evolvingCandidateQuirk?["evolution_duration_remaining"]?.GetValue<int>() == 60 &&
    evolvingCandidate.Preview.Warnings.Any(warning =>
        warning.Contains("evolving_quirk", StringComparison.Ordinal) &&
        warning.Contains("evolving_quirk=60", StringComparison.Ordinal) &&
        warning.Contains("60–60", StringComparison.Ordinal)) &&
    evolvingCandidate.Preview.Warnings.All(warning =>
        !warning.Contains("招募并保存", StringComparison.Ordinal)),
    "A fixed evolution range should be written directly without the disproven game-initialization warning.");

var variableEvolutionCandidate = StagecoachHeroCandidateFactory.Generate(
    heroCatalog,
    localHero,
    seed: 1729,
    selectedInitialQuirkIds: ["evolving_variable"]);
var repeatedVariableEvolutionCandidate = StagecoachHeroCandidateFactory.Generate(
    heroCatalog,
    localHero,
    seed: 1729,
    selectedInitialQuirkIds: ["evolving_variable"]);
var variableEvolutionDuration = variableEvolutionCandidate.Candidate["quirks"]?["evolving_variable"]?
    ["evolution_duration_remaining"]?.GetValue<int>();
var repeatedVariableEvolutionDuration = repeatedVariableEvolutionCandidate.Candidate["quirks"]?["evolving_variable"]?
    ["evolution_duration_remaining"]?.GetValue<int>();
Assert(
    variableEvolutionDuration is >= 3 and <= 15 &&
    repeatedVariableEvolutionDuration == variableEvolutionDuration,
    "A variable evolution duration should be deterministic for one seed and stay inside its own inclusive range.");
Assert(
    variableEvolutionCandidate.Preview.Name == generatedCandidate.Preview.Name &&
    variableEvolutionCandidate.Preview.ColourVariation == generatedCandidate.Preview.ColourVariation &&
    variableEvolutionCandidate.Preview.CombatSkills.SequenceEqual(generatedCandidate.Preview.CombatSkills) &&
    variableEvolutionCandidate.Preview.CampingSkills.SequenceEqual(generatedCandidate.Preview.CampingSkills),
    "Evolution-duration initialization must not shift the existing hero name, skin, or skill random sequence.");
var reversedEvolutionSelectionCandidate = StagecoachHeroCandidateFactory.Generate(
    heroCatalog,
    localHero,
    seed: 1729,
    selectedInitialQuirkIds: ["steady", "evolving_variable"]);
Assert(
    reversedEvolutionSelectionCandidate.Candidate["quirks"]?["evolving_variable"]?
        ["evolution_duration_remaining"]?.GetValue<int>() == variableEvolutionDuration,
    "An evolution duration should depend on the seed and quirk ID, not selection order or neighboring quirks.");

var zeroEvolutionCandidate = StagecoachHeroCandidateFactory.Generate(
    heroCatalog,
    localHero,
    seed: 1729,
    selectedInitialQuirkIds: ["evolving_zero"]);
Assert(
    zeroEvolutionCandidate.Candidate["quirks"]?["evolving_zero"]?
        ["evolution_duration_remaining"]?.GetValue<int>() == 0 &&
    zeroEvolutionCandidate.Preview.Warnings.Any(warning =>
        warning.Contains("配置 0–0", StringComparison.Ordinal)),
    "An author-defined zero-to-zero evolution range should preserve its intentional immediate expiry.");

var deathEvolutionCandidate = StagecoachHeroCandidateFactory.Generate(
    heroCatalog,
    localHero,
    seed: 1729,
    selectedInitialQuirkIds: ["evolving_death"]);
Assert(
    deathEvolutionCandidate.Candidate["quirks"]?["evolving_death"]?
        ["evolution_duration_remaining"]?.GetValue<int>() == 61 &&
    deathEvolutionCandidate.Preview.Warnings.Any(warning =>
        warning.Contains("到期死亡", StringComparison.Ordinal)),
    "A death evolution should initialize its configured duration without requiring a target quirk ID.");

var diseaseCandidate = StagecoachHeroCandidateFactory.Generate(
    heroCatalog,
    localHero,
    seed: 1729,
    selectedInitialQuirkIds: ["test_disease", "test_disease_two", "test_disease_three"]);
Assert(
    diseaseCandidate.Preview.Diseases.SequenceEqual(["test_disease", "test_disease_two", "test_disease_three"]) &&
    diseaseCandidate.Preview.PositiveQuirks.Count == 0 &&
    diseaseCandidate.Preview.NegativeQuirks.Count == 0,
    "Diseases should use their independent preview bucket instead of consuming negative-quirk slots.");
Assert(
    ((JsonObject)diseaseCandidate.Candidate["quirks"]!).Count == 3,
    "Three selected diseases should use the same canonical stagecoach quirk map.");

var hpCandidate = StagecoachHeroCandidateFactory.Generate(
    heroCatalog,
    localHero,
    seed: 1729,
    selectedInitialQuirkIds: ["natural"]);
Assert(Math.Abs(hpCandidate.Preview.CurrentHp - 24.0) < 0.000001, "A selected no_trinkets HP quirk should set full current_hp exactly once.");
Assert(
    ((JsonObject)hpCandidate.Candidate["actor"]!["buff_group"]!).Count == 0,
    "A selected HP quirk must still leave actor.buff_group empty.");

var baseGameStackedHpCandidate = StagecoachHeroCandidateFactory.Generate(
    heroCatalog,
    localHero,
    seed: 1729,
    selectedInitialQuirkIds: ["tough", "soft"]);
Assert(
    Math.Abs(baseGameStackedHpCandidate.Preview.CurrentHp - 21.0) < 0.000001,
    "Compatible base-game max-HP quirks should add their percentages before applying the class base HP.");
Assert(
    baseGameStackedHpCandidate.Candidate["actor"]?["current_hp"]?.GetValue<double>() == 21.0,
    "Candidate JSON current_hp should preserve the summed base-game max-HP result.");

var mixedRuleStackedHpCandidate = StagecoachHeroCandidateFactory.Generate(
    heroCatalog,
    localHero,
    seed: 1729,
    selectedInitialQuirkIds: ["natural", "tough"]);
Assert(
    Math.Abs(mixedRuleStackedHpCandidate.Preview.CurrentHp - 26.0) < 0.000001,
    "An always modifier and an active no_trinkets modifier should add for an empty-trinket candidate.");
Assert(
    ((JsonObject)mixedRuleStackedHpCandidate.Candidate["actor"]!["buff_group"]!).Count == 0,
    "Stacked max-HP quirks must not be duplicated into actor.buff_group.");

var levelFourCandidate = StagecoachHeroCandidateFactory.Generate(
    heroCatalog,
    localHero,
    seed: 1729,
    resolveLevel: 4,
    selectedInitialQuirkIds: ["natural"]);
Assert(
    levelFourCandidate.Preview is { ResolveLevel: 4, ResolveXp: 24, WeaponRank: 3, ArmourRank: 3 } &&
    Math.Abs(levelFourCandidate.Preview.CurrentHp - 38.4) < 0.000001,
    "A level-four candidate should link XP, equipment rank, armour HP, and the selected HP quirk exactly once.");
Assert(
    levelFourCandidate.Candidate["resolveXp"]?.GetValue<int>() == 24 &&
    levelFourCandidate.Candidate["weapon_rank"]?.GetValue<int>() == 3 &&
    levelFourCandidate.Candidate["armour_rank"]?.GetValue<int>() == 3,
    "Level-four candidate JSON does not match its derived progression profile.");
Assert(
    ((JsonObject)levelFourCandidate.Candidate["skills"]!["selected_combat_skills"]!).All(pair => pair.Value?.GetValue<int>() == 0) &&
    ((JsonObject)levelFourCandidate.Candidate["skills"]!["selected_camping_skills"]!).All(pair => pair.Value?.GetValue<int>() == 0),
    "Selected skill maps should retain zero values at non-zero hero levels.");
Assert(
    levelFourCandidate.UpgradePurchases.Count == 18 &&
    levelFourCandidate.UpgradePurchases.Count(purchase => purchase.TreeId == "local_hero.weapon") == 3 &&
    levelFourCandidate.UpgradePurchases.Count(purchase => purchase.TreeId == "local_hero.armour") == 3 &&
    levelFourCandidate.UpgradePurchases.Count(purchase =>
        purchase.TreeId == "local_hero.local_skill" ||
        purchase.TreeId == "local_hero.local_skill_two") == 8 &&
    levelFourCandidate.UpgradePurchases.Count(purchase =>
        expectedCampingTreeIds.Contains(purchase.TreeId) &&
        purchase.RequirementCode == "0") == 4 &&
    levelFourCandidate.UpgradePurchases.Any(purchase =>
        purchase.TreeId == "local_hero.local_skill" &&
        purchase.RequirementCode == "B") &&
    levelFourCandidate.UpgradePurchases.Any(purchase =>
        purchase.TreeId == "local_hero.local_skill" &&
        purchase.RequirementCode == "A") &&
    levelFourCandidate.UpgradePurchases.All(purchase => purchase.RequirementCode != "c"),
    "A level-four candidate should unlock every camping skill and every combat skill through the selected level while preserving case-sensitive custom requirement codes.");

var levelSixCandidate = StagecoachHeroCandidateFactory.Generate(
    heroCatalog,
    localHero,
    seed: 1729,
    resolveLevel: 6,
    selectedInitialQuirkIds: []);
Assert(
    levelSixCandidate.Preview is { ResolveLevel: 6, ResolveXp: 48, WeaponRank: 4, ArmourRank: 4 } &&
    Math.Abs(levelSixCandidate.Preview.CurrentHp - 36.0) < 0.000001,
    "A max-level blank candidate should use the final active equipment template without adding quirks.");

var fivePositiveCandidate = StagecoachHeroCandidateFactory.Generate(
    heroCatalog,
    localHero,
    seed: 1729,
    selectedInitialQuirkIds: ["steady", "eagle_eye", "hard_skinned", "slugger", "warrior_of_light"]);
Assert(fivePositiveCandidate.Preview.PositiveQuirks.Count == 5, "Five positive ordinary quirks should be allowed.");
var fiveNegativeCandidate = StagecoachHeroCandidateFactory.Generate(
    heroCatalog,
    localHero,
    seed: 1729,
    selectedInitialQuirkIds: ["clumsy", "slowdraw", "off_guard", "nervous", "weak_grip"]);
Assert(fiveNegativeCandidate.Preview.NegativeQuirks.Count == 5, "Five negative ordinary quirks should be allowed.");
var fiveNegativeAndThreeDiseasesCandidate = StagecoachHeroCandidateFactory.Generate(
    heroCatalog,
    localHero,
    seed: 1729,
    selectedInitialQuirkIds:
    [
        "clumsy", "slowdraw", "off_guard", "nervous", "weak_grip",
        "test_disease", "test_disease_two", "test_disease_three"
    ]);
Assert(
    fiveNegativeAndThreeDiseasesCandidate.Preview.NegativeQuirks.Count == 5 &&
    fiveNegativeAndThreeDiseasesCandidate.Preview.Diseases.Count == 3,
    "The disease cap should be independent from the five-negative-quirk cap.");

AssertHeroGenerationRejected(
    heroCatalog,
    localHero,
    ["steady", "eagle_eye", "hard_skinned", "slugger", "warrior_of_light", "robust"],
    "最多正面 5 个");
AssertHeroGenerationRejected(
    heroCatalog,
    localHero,
    ["clumsy", "slowdraw", "off_guard", "nervous", "weak_grip", "fearful"],
    "负面 5 个");
AssertHeroGenerationRejected(heroCatalog, localHero, ["tough", "fragile"], "互斥");
AssertHeroGenerationRejected(heroCatalog, localHero, ["unknown_hp_rule"], "当前不能显式写入");
AssertHeroGenerationRejected(heroCatalog, localHero, ["unknown_hp_disease"], "当前不能显式写入");
AssertHeroGenerationRejected(
    heroCatalog,
    localHero,
    ["test_disease", "test_disease_two", "test_disease_three", "test_disease_four"],
    "疾病 3 个");
AssertHeroGenerationRejected(heroCatalog, localHero, ["steady", "STEADY"], "重复选择");
AssertHeroGenerationRejected(heroCatalog, localHero, ["missing_quirk"], "不在当前活动内容目录");
AssertHeroLevelGenerationRejected(heroCatalog, localHero, 7, "0 到 6");

var referenceStagecoachCandidate = JsonNode.Parse(
    """
    {
      "rescued": false,
      "actor": {
        "name": "Contract Recruit",
        "current_hp": 23.4,
        "stunned": 0,
        "combat_ready": false,
        "damage_source_data": 0,
        "damage_source_type": 0,
        "damage_type": 0,
        "colour_variation": 3,
        "enemy_rank_targets": 0,
        "friendly_rank_targets": 0,
        "performing_turn": 0,
        "controlling_actor_guid": 0,
        "controlling_duration": 0,
        "current_mode_id": 0,
        "rounds_in_ranks": 0,
        "check_round_ranks": 0,
        "health_damage_blocks": 0,
        "buff_group_next_guid": 2,
        "buff_group": {},
        "actor_dot": {}
      },
      "heroClass": "hellion",
      "resolveXp": 0,
      "m_Stress": 0.0,
      "is_death_heart_attack_completed": false,
      "visited_deaths_door": false,
      "deaths_door_enter_effect_round_cooldown": 0,
      "has_had_heart_attack": false,
      "backer_hero": false,
      "steps_taken": 0,
      "enemies_killed": 0,
      "weapon_rank": 0,
      "armour_rank": 0,
      "dd_test_survived": 0,
      "affliction_type_id": "",
      "affliction_severity": 0,
      "virtue_type_id": "",
      "provisions_consumed": 0,
      "quirks": {
        "warren_explorer": {
          "is_new": true,
          "is_locked": false,
          "mission_count": 3637668,
          "replaces_quirk": 0,
          "replaces_quirk_viewed": false,
          "evolution_duration_remaining": 0
        },
        "resolution": {
          "is_new": true,
          "is_locked": false,
          "mission_count": 2104304579,
          "replaces_quirk": 0,
          "replaces_quirk_viewed": false,
          "evolution_duration_remaining": 0
        },
        "fragile": {
          "is_new": true,
          "is_locked": false,
          "mission_count": 2104304579,
          "replaces_quirk": 0,
          "replaces_quirk_viewed": false,
          "evolution_duration_remaining": 0
        }
      },
      "skills": {
        "selected_combat_skills": {
          "wicked_hack": 0,
          "iron_swan": 0,
          "barbaric_yawp": 0,
          "bleed_out": 0
        },
        "selected_camping_skills": {
          "first_aid": 0,
          "revel": 0,
          "reject_the_gods": 0
        }
      },
      "trinkets": {
        "items": {}
      },
      "has_item_Tracking": true,
      "item_tracking": {
        "supply": {}
      },
      "number_of_successful_darkest_dungeon_quests": 0,
      "is_from_town_event": false
    }
    """) as JsonObject ?? throw new InvalidDataException("Stagecoach candidate fixture is invalid.");
Assert(
    generatedCandidate.Candidate.Select(pair => pair.Key).Order(StringComparer.Ordinal)
        .SequenceEqual(referenceStagecoachCandidate.Select(pair => pair.Key).Order(StringComparer.Ordinal)),
    "Generated candidate root envelope differs from a complete natural stagecoach candidate.");
Assert(
    (generatedCandidate.Candidate["actor"] as JsonObject)!.Select(pair => pair.Key).Order(StringComparer.Ordinal)
        .SequenceEqual((referenceStagecoachCandidate["actor"] as JsonObject)!.Select(pair => pair.Key).Order(StringComparer.Ordinal)),
    "Generated actor envelope differs from a complete natural stagecoach candidate.");
var stagecoachCandidate = levelFourCandidate.Candidate;
var existingCandidateTown = JsonNode.Parse(
    """
    {
      "base_root": {
        "buildings": {
          "stage_coach": {
            "store": {
              "hero_recruit": {
                "generated": {
                  "100": {
                    "heroClass": "hellion",
                    "actor": {},
                    "quirks": { "context_special": {} }
                  }
                }
              },
              "shard_hero_recruit": {
                "generated": {
                  "200": {
                    "heroClass": "shieldbreaker",
                    "actor": {},
                    "quirks": {
                      "context_special": {},
                      "context_roster_limited": {}
                    }
                  }
                }
              }
            }
          }
        }
      }
    }
    """) as JsonObject ?? throw new InvalidDataException("Stagecoach mutation town fixture is invalid.");
var existingCandidateRoster = JsonNode.Parse(
    """
    {
      "base_root": {
        "nextGuid": 364,
        "heroes": {
          "1": {
            "heroClass": "crusader",
            "hero_file_data": {
              "raw_data": {
                "base_root": {
                  "quirks": {
                    "context_special": {},
                    "context_roster_limited": {}
                  }
                }
              }
            }
          }
        }
      }
    }
    """) as JsonObject ?? throw new InvalidDataException("Stagecoach mutation roster fixture is invalid.");
var existingCandidateUpgrades = JsonNode.Parse(
    """
    {
      "base_root": {
        "version": 1,
        "purchases": {
          "7": {
            "instance_number": 1,
            "tree_id": 123456,
            "requirement_code": "e",
            "is_purchased": true
          }
        }
      }
    }
    """) as JsonObject ?? throw new InvalidDataException("Stagecoach mutation upgrades fixture is invalid.");
var contextualLimitPreviews = StagecoachHeroSaveEditor.AnalyzeQuirkLimits(
    existingCandidateTown,
    existingCandidateRoster,
    contextLimitedCandidate.Candidate,
    heroCatalog.InitialQuirks);
Assert(
    contextualLimitPreviews.Count == 1 &&
    contextualLimitPreviews.All(item => item.QuirkId == "context_special"),
    "roster_limit must not enter stagecoach-generation limit analysis because the game enforces it later during recruitment.");
var singletonLimitPreview = contextualLimitPreviews.Single(item => item.QuirkId == "context_special");
Assert(
    singletonLimitPreview is
    {
        ExistingRosterHeroes: 1,
        ExistingStagecoachCandidates: 2,
        ExistingHeroes: 3,
        ResultingHeroes: 4,
        DefinitionLimit: 1,
        ExceedsDefinitionLimit: true
    },
    "A singleton preview must count the owned roster, ordinary and shard stagecoach candidates before adding one.");
var (mutatedTown, mutatedRoster, mutatedUpgrades, mutationPreview) = StagecoachHeroSaveEditor.AddCandidate(
    existingCandidateTown,
    existingCandidateRoster,
    existingCandidateUpgrades,
    stagecoachCandidate,
    levelFourCandidate.UpgradePurchases);
var mutatedGenerated = mutatedTown["base_root"]?["buildings"]?["stage_coach"]?["store"]?["hero_recruit"]?["generated"] as JsonObject;
var mutatedPurchases = mutatedUpgrades["base_root"]?["purchases"] as JsonObject;
Assert(mutationPreview.CandidateGuid == 364, "Stagecoach candidate should use roster nextGuid without filling holes.");
Assert(
    mutationPreview is { ResolveXp: 24, WeaponRank: 3, ArmourRank: 3, UpgradePurchaseCount: 18 },
    "Stagecoach mutation preview should preserve the candidate progression metadata.");
Assert(mutationPreview.ExistingCandidates == 1 && mutationPreview.ResultingCandidates == 2, "Existing normal recruits must be preserved while appending.");
Assert(mutatedGenerated?.ContainsKey("100") == true && mutatedGenerated.ContainsKey("364"), "Normal recruit append removed an existing candidate or missed the new GUID.");
Assert(mutatedTown["base_root"]?["buildings"]?["stage_coach"]?["store"]?["shard_hero_recruit"]?["generated"]?["200"] is JsonObject, "The shard recruit pool must remain unchanged.");
Assert(mutatedRoster["base_root"]?["nextGuid"]?.GetValue<int>() == 365, "Roster nextGuid should advance exactly once.");
Assert((mutatedRoster["base_root"]?["heroes"] as JsonObject)?.Count == 1, "Stagecoach append must not modify the owned roster.");
Assert(
    JsonNode.DeepEquals(
        mutatedRoster["base_root"]?["heroes"],
        existingCandidateRoster["base_root"]?["heroes"]),
    "Stagecoach append changed the owned roster hero subtree.");
Assert((existingCandidateTown["base_root"]?["buildings"]?["stage_coach"]?["store"]?["hero_recruit"]?["generated"] as JsonObject)?.Count == 1, "Pure mutation changed the input town document.");
Assert(existingCandidateRoster["base_root"]?["nextGuid"]?.GetValue<int>() == 364, "Pure mutation changed the input roster document.");
Assert(
    mutatedPurchases?.Count == 19 &&
    mutatedPurchases.ContainsKey("7") &&
    mutatedPurchases.ContainsKey("8") &&
    mutatedPurchases.Select(pair => pair.Value)
        .OfType<JsonObject>()
        .Count(purchase => purchase["instance_number"]?.GetValue<int>() == 364) == 18,
    "Stagecoach mutation should append every level-derived upgrade purchase after the highest existing key.");
var expectedLocalSkillHash = unchecked((int)HashLoc2Key("local_hero.local_skill"));
Assert(
    mutatedPurchases!.Select(pair => pair.Value).OfType<JsonObject>().Any(purchase =>
        purchase["instance_number"]?.GetValue<int>() == 364 &&
        purchase["tree_id"]?.GetValue<int>() == expectedLocalSkillHash &&
        purchase["requirement_code"]?.GetValue<string>() == "B"),
    "Stagecoach mutation should use the game's existing name hash and preserve a Mod's custom requirement code.");
Assert(
    mutatedPurchases!.Select(pair => pair.Value).OfType<JsonObject>().Where(purchase =>
            purchase["instance_number"]?.GetValue<int>() == 364 &&
            purchase["tree_id"]?.GetValue<int>() == expectedLocalSkillHash)
        .Select(purchase => purchase["requirement_code"]?.GetValue<string>())
        .ToHashSet(StringComparer.Ordinal)
        .IsSupersetOf(["a", "A"]),
    "Requirement codes that differ only by case must remain distinct purchases.");
var expectedCampingSkillHash = unchecked((int)HashLoc2Key("local_hero.local_camp_two"));
Assert(
    mutatedPurchases!.Select(pair => pair.Value).OfType<JsonObject>().Any(purchase =>
        purchase["instance_number"]?.GetValue<int>() == 364 &&
        purchase["tree_id"]?.GetValue<int>() == expectedCampingSkillHash &&
        purchase["requirement_code"]?.GetValue<string>() == "0"),
    "Stagecoach mutation should persist implicit class-prefixed camping unlock trees with requirement code zero.");
Assert(
    (existingCandidateUpgrades["base_root"]?["purchases"] as JsonObject)?.Count == 1,
    "Pure mutation changed the input upgrades document.");

var nonRoundTrippableRequirementBlocked = false;
try
{
    _ = StagecoachHeroSaveEditor.AddCandidate(
        existingCandidateTown,
        existingCandidateRoster,
        existingCandidateUpgrades,
        stagecoachCandidate,
        [new HeroUpgradePurchase("local_hero.local_skill", "too_long")]);
}
catch (InvalidDataException ex) when (ex.Message.Contains("losslessly", StringComparison.Ordinal))
{
    nonRoundTrippableRequirementBlocked = true;
}

Assert(
    nonRoundTrippableRequirementBlocked,
    "A multi-character requirement code must be rejected before DSON can silently truncate it.");

Assert(HashLoc2Key("tree_1e") == HashLoc2Key("tree_20"), "The upgrade collision fixture is invalid.");
var collidingTreeIdsBlocked = false;
try
{
    _ = StagecoachHeroSaveEditor.AddCandidate(
        existingCandidateTown,
        existingCandidateRoster,
        existingCandidateUpgrades,
        stagecoachCandidate,
        [
            new HeroUpgradePurchase("tree_1e", "0"),
            new HeroUpgradePurchase("tree_20", "1")
        ]);
}
catch (InvalidDataException ex) when (ex.Message.Contains("same game hash", StringComparison.Ordinal))
{
    collidingTreeIdsBlocked = true;
}

Assert(
    collidingTreeIdsBlocked,
    "Different upgrade tree ids with the same game hash must be rejected even when their requirement codes differ.");

var invalidGuidRoster = JsonNode.Parse(
    """
    {
      "base_root": {
        "nextGuid": 200,
        "heroes": {}
      }
    }
    """) as JsonObject ?? throw new InvalidDataException("Invalid GUID roster fixture is invalid.");
var invalidGuidBlocked = false;
try
{
    _ = StagecoachHeroSaveEditor.AddCandidate(
        existingCandidateTown,
        invalidGuidRoster,
        existingCandidateUpgrades,
        stagecoachCandidate,
        levelFourCandidate.UpgradePurchases);
}
catch (InvalidDataException ex) when (ex.Message.Contains("highest existing hero GUID", StringComparison.Ordinal))
{
    invalidGuidBlocked = true;
}

Assert(invalidGuidBlocked, "A stale or colliding roster nextGuid must be blocked instead of probing for a hole.");

var stagecoachLocations = new SaveEditorLocations(
    Path.Combine(runRoot, "stagecoach-appdata"),
    Path.Combine(runRoot, "stagecoach-appdata", "workspaces"),
    Path.Combine(runRoot, "stagecoach-appdata", "backups"));
var stagecoachService = new SaveEditService(codec, stagecoachLocations);
var preparedContextLimitedStagecoach = await stagecoachService.PrepareStagecoachHeroEditAsync(
    profile,
    contextLimitedCandidate,
    heroCatalog,
    activeContent);
var preparedSingletonLimit = preparedContextLimitedStagecoach.Preview.QuirkLimits.Single(item =>
    item.QuirkId == "context_special");
Assert(
    preparedSingletonLimit is
    {
        ExistingRosterHeroes: 1,
        ExistingStagecoachCandidates: 0,
        ResultingHeroes: 2,
        DefinitionLimit: 1,
        ExceedsDefinitionLimit: true
    },
    "Prepared hero preview should retain an over-limit singleton warning without blocking the candidate edit.");
Assert(
    !preparedContextLimitedStagecoach.Preview.QuirkLimits.Any(item =>
        item.QuirkId == "context_roster_limited"),
    "A roster_limit quirk must not create a stagecoach preview warning because the editor does not recruit the candidate into the owned roster.");
var preparedStagecoach = await stagecoachService.PrepareStagecoachHeroEditAsync(
    profile,
    levelFourCandidate,
    heroCatalog,
    activeContent);
Assert(preparedStagecoach.Preview.CandidateGuid == 364, "Prepared stagecoach candidate should use save nextGuid 364.");
Assert(
    preparedStagecoach.Preview is { ResolveXp: 24, WeaponRank: 3, ArmourRank: 3, UpgradePurchaseCount: 18 },
    "Prepared stagecoach preview should retain non-zero XP and equipment ranks.");
Assert(preparedStagecoach.Preview.ExistingCandidates == 0 && preparedStagecoach.Preview.ResultingCandidates == 1, "Empty normal recruit pool should receive exactly one candidate.");
Assert(preparedStagecoach.Preview.RosterHeroCount == 36, "A full 36-hero roster should remain valid for stagecoach generation.");
Assert(ReadRevision(preparedStagecoach.TownFile.SourceCopyPath).SequenceEqual(ReadRevision(preparedStagecoach.TownFile.EncodedPath)), "Town revision bytes were not preserved.");
Assert(ReadRevision(preparedStagecoach.RosterFile.SourceCopyPath).SequenceEqual(ReadRevision(preparedStagecoach.RosterFile.EncodedPath)), "Roster revision bytes were not preserved.");
Assert(ReadRevision(preparedStagecoach.UpgradesFile.SourceCopyPath).SequenceEqual(ReadRevision(preparedStagecoach.UpgradesFile.EncodedPath)), "Upgrades revision bytes were not preserved.");

var activeHeroUpgradePath = Path.Combine(localHeroUpgradeRoot, "local_hero.upgrades.json");
var activeHeroUpgradeBytes = File.ReadAllBytes(activeHeroUpgradePath);
var staleHeroTemplateCommitBlocked = false;
try
{
    var changedUpgradeTemplate = File.ReadAllText(activeHeroUpgradePath)
        .Replace(
            "\"code\": \"c\", \"prerequisite_resolve_level\": 5",
            "\"code\": \"c\", \"prerequisite_resolve_level\": 4",
            StringComparison.Ordinal);
    Assert(
        !changedUpgradeTemplate.Equals(
            Encoding.UTF8.GetString(activeHeroUpgradeBytes),
            StringComparison.Ordinal),
        "The stale hero upgrade template fixture did not change.");
    File.WriteAllText(activeHeroUpgradePath, changedUpgradeTemplate, new UTF8Encoding(false));
    try
    {
        _ = await stagecoachService.CommitAsync(preparedStagecoach);
    }
    catch (InvalidOperationException ex) when (ex.Message.Contains(
        "templates changed after the hero preview",
        StringComparison.Ordinal))
    {
        staleHeroTemplateCommitBlocked = true;
    }
}
finally
{
    File.WriteAllBytes(activeHeroUpgradePath, activeHeroUpgradeBytes);
}

Assert(
    staleHeroTemplateCommitBlocked,
    "Stagecoach commit should reject a hero upgrade template changing after preview.");
Assert(
    !Directory.Exists(stagecoachLocations.BackupDirectory),
    "A stale active-content preview should be rejected before creating a profile backup.");

var staleUpgrades = File.ReadAllBytes(upgradesSavePath);
staleUpgrades[^1] ^= 0x01;
File.WriteAllBytes(upgradesSavePath, staleUpgrades);
var staleStagecoachCommitBlocked = false;
try
{
    _ = await stagecoachService.CommitAsync(preparedStagecoach);
}
catch (InvalidOperationException ex) when (ex.Message.Contains("changed after preview", StringComparison.Ordinal))
{
    staleStagecoachCommitBlocked = true;
}

Assert(staleStagecoachCommitBlocked, "Stagecoach commit should reject any live transaction file changing after preview.");
Assert(!Directory.Exists(stagecoachLocations.BackupDirectory), "A stale three-file preview should be rejected before creating a backup.");
File.Copy(preparedStagecoach.UpgradesFile.SourceCopyPath, upgradesSavePath, overwrite: true);

var rollbackObserved = false;
string? rollbackBackupDirectory = null;
using (File.Open(townSavePath, FileMode.Open, FileAccess.Read, FileShare.Read))
{
    try
    {
        _ = await stagecoachService.CommitAsync(preparedStagecoach);
    }
    catch (InvalidOperationException ex) when (ex.Message.Contains("restored", StringComparison.Ordinal))
    {
        rollbackObserved = true;
        rollbackBackupDirectory = Directory.EnumerateDirectories(
                Path.Combine(stagecoachLocations.BackupDirectory, "contract-user", "profile_7"))
            .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
            .First();
    }
}

Assert(rollbackObserved, "A town replacement failure should restore the already-replaced upgrades and roster files.");
Assert(File.ReadAllBytes(rosterSavePath).SequenceEqual(File.ReadAllBytes(preparedStagecoach.RosterFile.SourceCopyPath)), "Roster was not restored byte-for-byte after town replacement failed.");
Assert(File.ReadAllBytes(upgradesSavePath).SequenceEqual(File.ReadAllBytes(preparedStagecoach.UpgradesFile.SourceCopyPath)), "Upgrades were not restored byte-for-byte after town replacement failed.");
Assert(File.ReadAllBytes(townSavePath).SequenceEqual(File.ReadAllBytes(preparedStagecoach.TownFile.SourceCopyPath)), "Unreplaced town file changed during rollback test.");
Assert(rollbackBackupDirectory is not null && File.Exists(Path.Combine(rollbackBackupDirectory, "transaction-state.json")), "Rollback transaction state was not recorded.");
Assert(!File.Exists(Path.Combine(rollbackBackupDirectory!, "commit-result.json")), "A rolled-back transaction must not retain a successful commit result.");
var rollbackState = JsonNode.Parse(File.ReadAllText(Path.Combine(rollbackBackupDirectory!, "transaction-state.json"))) as JsonObject;
Assert(rollbackState?["status"]?.GetValue<string>() == "restored", "Rollback transaction state should finish as restored.");

var stagecoachCommit = await stagecoachService.CommitAsync(preparedStagecoach);
Assert(File.Exists(Path.Combine(stagecoachCommit.BackupDirectory, "persist.town.json")), "Town backup is missing.");
Assert(File.Exists(Path.Combine(stagecoachCommit.BackupDirectory, "persist.roster.json")), "Roster backup is missing.");
Assert(File.Exists(Path.Combine(stagecoachCommit.BackupDirectory, "persist.upgrades.json")), "Upgrades backup is missing.");
Assert(File.Exists(Path.Combine(stagecoachCommit.BackupDirectory, "backup-manifest.json")), "Stagecoach backup manifest is missing.");
var stagecoachBackupManifest = JsonNode.Parse(
    File.ReadAllText(Path.Combine(stagecoachCommit.BackupDirectory, "backup-manifest.json"))) as JsonObject;
Assert(
    stagecoachBackupManifest?["resolveXp"]?.GetValue<int>() == 24 &&
    stagecoachBackupManifest["weaponRank"]?.GetValue<int>() == 3 &&
    stagecoachBackupManifest["armourRank"]?.GetValue<int>() == 3 &&
    stagecoachBackupManifest["upgradePurchaseCount"]?.GetValue<int>() == 18,
    "Stagecoach backup manifest should record the generated level profile metadata.");
Assert(File.Exists(Path.Combine(stagecoachCommit.BackupDirectory, "transaction-state.json")), "Stagecoach transaction state is missing.");
Assert(File.Exists(Path.Combine(stagecoachCommit.BackupDirectory, "commit-result.json")), "Stagecoach commit result is missing.");

var committedTownDecoded = Path.Combine(runRoot, "committed.persist.town.json");
var committedRosterDecoded = Path.Combine(runRoot, "committed.persist.roster.json");
var committedUpgradesDecoded = Path.Combine(runRoot, "committed.persist.upgrades.json");
await codec.DecodeAsync(townSavePath, committedTownDecoded);
await codec.DecodeAsync(rosterSavePath, committedRosterDecoded);
await codec.DecodeAsync(upgradesSavePath, committedUpgradesDecoded);
var committedTownRoot = JsonNode.Parse(File.ReadAllText(committedTownDecoded)) as JsonObject
    ?? throw new InvalidDataException("Committed town did not decode to an object.");
var committedRosterRoot = JsonNode.Parse(File.ReadAllText(committedRosterDecoded)) as JsonObject
    ?? throw new InvalidDataException("Committed roster did not decode to an object.");
var committedUpgradesRoot = JsonNode.Parse(File.ReadAllText(committedUpgradesDecoded)) as JsonObject
    ?? throw new InvalidDataException("Committed upgrades did not decode to an object.");
var committedGenerated = committedTownRoot["base_root"]?["buildings"]?["stage_coach"]?["store"]?["hero_recruit"]?["generated"] as JsonObject;
Assert(committedGenerated?.ContainsKey("364") == true, "Committed town is missing candidate GUID 364.");
Assert(committedRosterRoot["base_root"]?["nextGuid"]?.GetValue<int>() == 365, "Committed roster nextGuid is wrong.");
Assert((committedRosterRoot["base_root"]?["heroes"] as JsonObject)?.Count == 36, "Committed roster heroes were modified.");
Assert(
    JsonNode.DeepEquals(committedRosterRoot["base_root"]?["heroes"], rosterHeroesSeed),
    "Committed roster hero subtree differs from the original 36 heroes.");
var committedPurchases = committedUpgradesRoot["base_root"]?["purchases"] as JsonObject;
Assert(
    committedPurchases?.Select(pair => pair.Value).OfType<JsonObject>().Count(purchase =>
        purchase["instance_number"]?.GetValue<int>() == 364) == 18,
    "Committed upgrades are missing the candidate's complete level-derived purchase plan.");

var unfinishedBackupDirectory = Path.Combine(
    stagecoachLocations.BackupDirectory,
    "contract-user",
    "profile_7",
    "synthetic-unfinished");
Directory.CreateDirectory(unfinishedBackupDirectory);
var unfinishedStatePath = Path.Combine(unfinishedBackupDirectory, "transaction-state.json");
File.WriteAllText(
    unfinishedStatePath,
    """{ "status": "replaced_roster" }""",
    new UTF8Encoding(false));
var unfinishedTransactionBlocked = false;
try
{
    _ = await stagecoachService.PrepareStagecoachHeroEditAsync(
        profile,
        levelFourCandidate,
        heroCatalog,
        activeContent);
}
catch (InvalidOperationException ex) when (ex.Message.Contains("unfinished stagecoach save transaction", StringComparison.Ordinal))
{
    unfinishedTransactionBlocked = true;
}

Assert(unfinishedTransactionBlocked, "A non-terminal prior transaction must block a new stagecoach preview.");
File.WriteAllText(
    unfinishedStatePath,
    """{ "status": "restored" }""",
    new UTF8Encoding(false));

var service = new SaveEditService(codec, locations);
var ambiguousTrinketBlocked = false;
try
{
    _ = await service.PrepareTrinketEditAsync(
        profile,
        ambiguousTrinket,
        1,
        activeCatalog.Storage,
        activeContent);
}
catch (InvalidOperationException ex) when (ex.Message.Contains("unresolved definitions", StringComparison.Ordinal))
{
    ambiguousTrinketBlocked = true;
}

Assert(ambiguousTrinketBlocked, "An unresolved trinket definition must not enter the save preview path.");
var workspaceCountBeforeUnknownCapacity = Directory.Exists(locations.WorkspaceDirectory)
    ? Directory.GetDirectories(locations.WorkspaceDirectory).Length
    : 0;
var unknownStorageCapacityBlocked = false;
try
{
    _ = await service.PrepareTrinketEditAsync(
        profile,
        ordinary,
        1,
        expectedStorage: null,
        activeContent);
}
catch (InvalidOperationException ex) when (ex.Message.Contains("capacity could not be resolved", StringComparison.Ordinal))
{
    unknownStorageCapacityBlocked = true;
}

Assert(unknownStorageCapacityBlocked, "An unresolved active storage capacity must block trinket preview.");
Assert(
    (!Directory.Exists(locations.WorkspaceDirectory) ? 0 : Directory.GetDirectories(locations.WorkspaceDirectory).Length) ==
    workspaceCountBeforeUnknownCapacity,
    "An unresolved storage capacity must be rejected before a trinket edit workspace is created.");
var invalidHighestPriorityCapacityBlocked = false;
try
{
    _ = await service.PrepareTrinketEditAsync(
        profile,
        ordinary,
        1,
        activeCatalog.Storage,
        invalidCapacityContent);
}
catch (InvalidOperationException ex) when (ex.Message.Contains(
    "capacity could not be resolved",
    StringComparison.Ordinal))
{
    invalidHighestPriorityCapacityBlocked = true;
}

Assert(
    invalidHighestPriorityCapacityBlocked,
    "The core preview path must reject an invalid higher-priority capacity instead of trusting the prior catalog value.");
Assert(
    (!Directory.Exists(locations.WorkspaceDirectory) ? 0 : Directory.GetDirectories(locations.WorkspaceDirectory).Length) ==
    workspaceCountBeforeUnknownCapacity,
    "An invalid higher-priority capacity must be rejected before a trinket edit workspace is created.");
var storageCapacityBlocked = false;
try
{
    _ = await service.PrepareTrinketEditAsync(
        profile,
        ordinary,
        3,
        activeCatalog.Storage,
        activeContent);
}
catch (InvalidOperationException ex) when (ex.Message.Contains("exceed the active storage capacity", StringComparison.Ordinal))
{
    storageCapacityBlocked = true;
}

Assert(storageCapacityBlocked, "A trinket edit that exceeds the active total storage capacity must be blocked.");
var unlimitedPrepared = await service.PrepareTrinketEditAsync(
    profile,
    unlimited,
    2,
    activeCatalog.Storage,
    activeContent);
Assert(
    unlimitedPrepared.Preview.DefinitionLimit == 0 &&
    !unlimitedPrepared.Preview.ExceedsDefinitionLimit &&
    unlimitedPrepared.Preview.ResultingInventoryEntries == unlimitedPrepared.Preview.StorageCapacity,
    "Definition limit zero must remain unlimited, and filling exactly to total capacity must be allowed.");
var modDefinitionPrepared = await service.PrepareTrinketEditAsync(
    profile,
    activeWorkshopTrinket,
    1,
    activeCatalog.Storage,
    activeContent);
var prepared = await service.PrepareTrinketEditAsync(
    profile,
    ordinary,
    2,
    activeCatalog.Storage,
    activeContent);

Assert(prepared.Preview.ExistingCopies == 0, "Preview should start with zero focus rings.");
Assert(prepared.Preview.ResultingCopies == 2, "Preview should result in two focus rings.");
Assert(
    prepared.Preview.DefinitionLimit == 1 && prepared.Preview.ExceedsDefinitionLimit,
    "The console-style preview should retain a per-id over-limit write while marking it explicitly.");
Assert(
    prepared.Preview.ExistingInventoryEntries == 1 &&
    prepared.Preview.ResultingInventoryEntries == 3 &&
    prepared.Preview.StorageCapacity == 3 &&
    prepared.Preview.ResultingInventoryEntries == prepared.Preview.StorageCapacity,
    "The preview should report existing, resulting, and maximum storage slots.");
Assert(prepared.OriginalSummary.TrinketCopies == 1, "Seed should have one trinket.");
Assert(prepared.ResultSummary.TrinketCopies == 3, "Result should have three trinkets.");
Assert(ReadRevision(prepared.SourceCopyPath).SequenceEqual(ReadRevision(prepared.EncodedPath)), "Revision bytes were not preserved.");

var selectedModDefinitionBytes = File.ReadAllBytes(activeWorkshopTrinket.SourcePath);
File.Delete(activeWorkshopTrinket.SourcePath);
var missingSelectedModDefinitionBlocked = false;
try
{
    _ = await service.CommitAsync(modDefinitionPrepared);
}
catch (InvalidOperationException ex) when (ex.Message.Contains(
    "selected trinket definition changed after preview",
    StringComparison.OrdinalIgnoreCase))
{
    missingSelectedModDefinitionBlocked = true;
}

Assert(
    missingSelectedModDefinitionBlocked,
    "Removing the selected Mod trinket definition after preview must block commit.");
Assert(
    !Directory.Exists(locations.BackupDirectory),
    "A missing selected Mod definition should be rejected before creating a backup.");
File.WriteAllBytes(activeWorkshopTrinket.SourcePath, selectedModDefinitionBytes);

var activeWorkshopManifestPath = Path.Combine(activeWorkshopRoot, "modfiles.txt");
var originalActiveWorkshopManifestBytes = File.ReadAllBytes(activeWorkshopManifestPath);
File.AppendAllText(
    activeWorkshopManifestPath,
    Environment.NewLine + "// manifest fingerprint probe",
    new UTF8Encoding(false));
var changedManifestBlocked = false;
try
{
    _ = await service.CommitAsync(prepared);
}
catch (InvalidOperationException ex) when (ex.Message.Contains(
    "active Mod manifest changed after preview",
    StringComparison.OrdinalIgnoreCase))
{
    changedManifestBlocked = true;
}

Assert(
    changedManifestBlocked,
    "Any active Mod manifest change after preview must block commit even when effective definitions stay the same.");
Assert(
    !Directory.Exists(locations.BackupDirectory),
    "A changed active Mod manifest should be rejected before creating a backup.");
File.WriteAllBytes(activeWorkshopManifestPath, originalActiveWorkshopManifestBytes);

var originalStorageConfigBytes = File.ReadAllBytes(activeCatalog.Storage!.SourcePath);
File.WriteAllText(
    activeCatalog.Storage.SourcePath,
    """inventory_system_config: .type "trinket_storage" .max_slots 2""",
    new UTF8Encoding(false));
var staleStorageConfigurationBlocked = false;
try
{
    _ = await service.CommitAsync(prepared);
}
catch (InvalidOperationException ex) when (ex.Message.Contains(
    "storage configuration changed after preview",
    StringComparison.Ordinal))
{
    staleStorageConfigurationBlocked = true;
}

Assert(
    staleStorageConfigurationBlocked,
    "A capacity-file change after preview must block commit before touching the live save.");
Assert(
    !Directory.Exists(locations.BackupDirectory),
    "A stale storage configuration should be rejected before creating a backup.");
File.WriteAllBytes(activeCatalog.Storage.SourcePath, originalStorageConfigBytes);

var originalGameSaveBytes = File.ReadAllBytes(gameSavePath);
var changedGameSaveBytes = originalGameSaveBytes.ToArray();
changedGameSaveBytes[^1] ^= 0x01;
File.WriteAllBytes(gameSavePath, changedGameSaveBytes);
var staleModConfigurationBlocked = false;
try
{
    _ = await service.CommitAsync(prepared);
}
catch (InvalidOperationException ex) when (ex.Message.Contains(
    "Mod/DLC configuration changed after preview",
    StringComparison.Ordinal))
{
    staleModConfigurationBlocked = true;
}

Assert(
    staleModConfigurationBlocked,
    "A persist.game.json change after preview must block trinket commit.");
Assert(
    !Directory.Exists(locations.BackupDirectory),
    "A stale Mod/DLC configuration should be rejected before creating a backup.");
File.WriteAllBytes(gameSavePath, originalGameSaveBytes);

var staleEstate = File.ReadAllBytes(estatePath);
staleEstate[^1] ^= 0x01;
File.WriteAllBytes(estatePath, staleEstate);
var staleCommitBlocked = false;
try
{
    _ = await service.CommitAsync(prepared);
}
catch (InvalidOperationException ex) when (ex.Message.Contains("changed after preview", StringComparison.Ordinal))
{
    staleCommitBlocked = true;
}

Assert(staleCommitBlocked, "Commit should reject a live save changed after preview.");
Assert(!Directory.Exists(locations.BackupDirectory), "A stale preview should be rejected before creating a backup.");
File.Copy(prepared.SourceCopyPath, estatePath, overwrite: true);

var estateRaceWrite = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
var estateRaceCommitBlocked = false;
using (var estateRaceWatcher = new FileSystemWatcher(profileRoot)
       {
           Filter = ".persist.estate.json.ddse-*.tmp",
           NotifyFilter = NotifyFilters.FileName
       })
{
    estateRaceWatcher.Created += (_, _) =>
    {
        try
        {
            var changedEstate = File.ReadAllBytes(estatePath);
            changedEstate[^1] ^= 0x01;
            File.WriteAllBytes(estatePath, changedEstate);
            estateRaceWrite.TrySetResult(true);
        }
        catch (Exception ex)
        {
            estateRaceWrite.TrySetException(ex);
        }
    };
    estateRaceWatcher.EnableRaisingEvents = true;
    try
    {
        _ = await service.CommitAsync(prepared);
    }
    catch (InvalidOperationException ex) when (ex.Message.Contains(
        "live estate save changed immediately before replacement",
        StringComparison.OrdinalIgnoreCase))
    {
        estateRaceCommitBlocked = true;
    }
}

Assert(
    await estateRaceWrite.Task.WaitAsync(TimeSpan.FromSeconds(5)),
    "The test writer should change the estate after the prepared temporary file appears.");
Assert(
    estateRaceCommitBlocked,
    "An estate change after the prepared temporary file appears must still block replacement.");
File.Copy(prepared.SourceCopyPath, estatePath, overwrite: true);

var statefulBlocked = false;
try
{
    _ = await service.PrepareTrinketEditAsync(
        profile,
        stateful,
        1,
        activeCatalog.Storage,
        activeContent);
}
catch (InvalidOperationException)
{
    statefulBlocked = true;
}

Assert(statefulBlocked, "Stateful trinket creation should be blocked in phase 1.");

var contentRaceWriteBlocked = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
SaveCommitResult? guardedCommit = null;
var contentRaceWasBlocked = false;
using (var contentRaceWatcher = new FileSystemWatcher(profileRoot)
       {
           Filter = ".persist.estate.json.ddse-*.tmp",
           NotifyFilter = NotifyFilters.FileName
       })
{
    contentRaceWatcher.Created += (_, _) =>
    {
        try
        {
            File.WriteAllText(
                activeWorkshopManifestPath,
                "// concurrent manifest replacement",
                new UTF8Encoding(false));
            contentRaceWriteBlocked.TrySetResult(false);
        }
        catch (IOException)
        {
            contentRaceWriteBlocked.TrySetResult(true);
        }
        catch (UnauthorizedAccessException)
        {
            contentRaceWriteBlocked.TrySetResult(true);
        }
        catch (Exception ex)
        {
            contentRaceWriteBlocked.TrySetException(ex);
        }
    };
    contentRaceWatcher.EnableRaisingEvents = true;
    try
    {
        guardedCommit = await service.CommitAsync(prepared);
        contentRaceWasBlocked = await contentRaceWriteBlocked.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }
    finally
    {
        File.WriteAllBytes(activeWorkshopManifestPath, originalActiveWorkshopManifestBytes);
    }
}

Assert(
    contentRaceWasBlocked,
    "A Mod manifest writer triggered by the prepared temporary file must be blocked until replacement completes.");
var commit = guardedCommit ?? throw new InvalidOperationException("The guarded trinket commit did not complete.");
Assert(File.Exists(Path.Combine(commit.BackupDirectory, "persist.estate.json")), "Estate backup is missing.");
Assert(File.Exists(Path.Combine(commit.BackupDirectory, "backup-manifest.json")), "Backup manifest is missing.");
Assert(File.Exists(Path.Combine(commit.BackupDirectory, "commit-result.json")), "Commit result is missing.");
Assert(ReadRevision(estatePath).SequenceEqual(new byte[] { 0x00, 0x00, 0x4A, 0x66 }), "Committed revision changed.");

var committedDecoded = Path.Combine(runRoot, "committed.persist.estate.json");
await codec.DecodeAsync(estatePath, committedDecoded);
var committedRoot = JsonNode.Parse(File.ReadAllText(committedDecoded)) as JsonObject
    ?? throw new InvalidDataException("Committed estate did not decode to an object.");
Assert(TrinketSaveEditor.CountCopies(committedRoot, "focus_ring") == 2, "Committed save has the wrong focus ring count.");

var loc2ProbeModRoot = Environment.GetEnvironmentVariable("DDSE_LOC2_PROBE_MOD_ROOT");
if (!string.IsNullOrWhiteSpace(loc2ProbeModRoot))
{
    var fullProbeRoot = Path.GetFullPath(loc2ProbeModRoot);
    Assert(Directory.Exists(fullProbeRoot), "DDSE_LOC2_PROBE_MOD_ROOT must point to an existing Mod directory.");
    var probeProfile = new SaveProfile(
        "loc2_probe",
        runRoot,
        Path.Combine(runRoot, "unused.persist.estate.json"),
        "contract-user",
        DateTime.UtcNow);
    var probeSnapshot = new ActiveContentSnapshot(
        probeProfile,
        "base",
        [new ActiveContentSource("local:loc2-probe", "LOC2 Probe", "local", fullProbeRoot, 0)],
        [],
        runRoot,
        string.Empty,
        1,
        string.Empty);
    var probeCatalog = TrinketCatalog.Load(probeSnapshot);
    var expectedProbeIds = Enumerable.Range(1, 9)
        .Select(index => $"EosNyx_Trinket{index}")
        .Append("EosNyx_TrinketC")
        .Concat(["Grandmaster_Trinket1", "Grandmaster_Trinket2"])
        .ToArray();
    Assert(
        expectedProbeIds.All(id => probeCatalog.Trinkets.Any(item =>
            item.Id == id &&
            !string.IsNullOrWhiteSpace(item.LocalizedName.Chinese) &&
            !string.IsNullOrWhiteSpace(item.LocalizedName.English))),
        "The real LOC2 probe should resolve all twelve Eos_Nyx trinkets in both languages.");
    Assert(
        probeCatalog.Trinkets.Single(item => item.Id == "EosNyx_Trinket1").LocalizedName ==
            new BilingualContentName("血循环稳定戒指", "Ring of Circulation Stabilization"),
        "The real LOC2 probe should resolve the known Eos_Nyx trinket name exactly.");
}

var rurutiaProbeModRoot = Environment.GetEnvironmentVariable("DDSE_RURUTIA_LOC2_PROBE_MOD_ROOT");
if (!string.IsNullOrWhiteSpace(rurutiaProbeModRoot))
{
    var fullProbeRoot = Path.GetFullPath(rurutiaProbeModRoot);
    Assert(
        Directory.Exists(fullProbeRoot),
        "DDSE_RURUTIA_LOC2_PROBE_MOD_ROOT must point to an existing Mod directory.");
    var probeProfile = new SaveProfile(
        "rurutia_loc2_probe",
        runRoot,
        Path.Combine(runRoot, "unused.rurutia.persist.estate.json"),
        "contract-user",
        DateTime.UtcNow);
    var probeSnapshot = new ActiveContentSnapshot(
        probeProfile,
        "base",
        [new ActiveContentSource("local:rurutia-loc2-probe", "Rurutia LOC2 Probe", "local", fullProbeRoot, 0)],
        [],
        runRoot,
        string.Empty,
        1,
        string.Empty);
    var probeCatalog = TrinketCatalog.Load(probeSnapshot);
    var expectedProbeIds = Enumerable.Range(1, 9)
        .Select(index => $"Rurutia_{index}")
        .ToArray();
    Assert(
        expectedProbeIds.All(id => probeCatalog.Trinkets.Any(item =>
            item.Id == id && !string.IsNullOrWhiteSpace(item.LocalizedName.Chinese))),
        "The real Rurutia LOC2 probe should resolve all nine Simplified Chinese trinket names.");
    Assert(
        expectedProbeIds.Take(8).All(id => probeCatalog.Trinkets.Any(item =>
            item.Id == id && !string.IsNullOrWhiteSpace(item.LocalizedName.English))),
        "The real Rurutia LOC2 probe should resolve every English trinket name actually supplied by the Mod.");
    Assert(
        probeCatalog.Trinkets.Single(item => item.Id == "Rurutia_1").LocalizedName ==
            new BilingualContentName("黑塔巫师", "Rurutia's Cutlass") &&
        probeCatalog.Trinkets.Single(item => item.Id == "Rurutia_9").LocalizedName ==
            new BilingualContentName("塔", string.Empty),
        "The real Rurutia LOC2 probe should preserve exact supplied names without inventing the missing ninth English name.");
    Assert(
        probeCatalog.Issues.All(issue =>
            !issue.Contains("Failed to read localization", StringComparison.Ordinal)),
        "The real Rurutia LOC2 files should not be rejected for cross-string colour controls.");
}

Console.WriteLine("PASS: active game-mode/Mod catalogs, bilingual names, level 0-max progression, blank/default and explicit natural/special quirks, HP/skill/camping rules, stagecoach GUID/upgrade append, full-roster preservation, town/roster/upgrades stale guards, DSON roundtrips, verified backups, three-file rollback, and trinket commit contracts.");
Console.WriteLine($"Artifacts: {runRoot}");

static TrinketStorageCatalogResult LoadStorageCapacityProbe(
    ActiveContentSnapshot baseline,
    string runRoot,
    string probeName,
    string configText)
{
    var modRoot = Path.Combine(runRoot, probeName);
    var inventoryRoot = Path.Combine(modRoot, "inventory");
    Directory.CreateDirectory(inventoryRoot);
    File.WriteAllText(
        Path.Combine(inventoryRoot, $"{probeName}.inventory.system_configs.darkest"),
        configText,
        new UTF8Encoding(false));
    return TrinketStorageCatalog.Load(baseline with
    {
        Sources = baseline.Sources
            .Append(new ActiveContentSource(
                $"local:{probeName}",
                probeName,
                "local",
                modRoot,
                -1500))
            .ToArray()
    });
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException($"Contract assertion failed: {message}");
    }
}

static (int Width, int Height) ReadPngSize(string path)
{
    var header = File.ReadAllBytes(path).AsSpan();
    Assert(
        header.Length >= 24 &&
        header[0] == 0x89 &&
        header[1] == 0x50 &&
        header[2] == 0x4E &&
        header[3] == 0x47,
        $"Expected a valid PNG file: {path}");
    return (
        BinaryPrimitives.ReadInt32BigEndian(header.Slice(16, 4)),
        BinaryPrimitives.ReadInt32BigEndian(header.Slice(20, 4)));
}

static string ComputeSha256(string path)
{
    using var stream = File.OpenRead(path);
    return Convert.ToHexString(SHA256.HashData(stream));
}

static BitmapSource LoadBgra32(string path)
{
    using var stream = File.OpenRead(path);
    var decoder = new PngBitmapDecoder(
        stream,
        BitmapCreateOptions.PreservePixelFormat,
        BitmapCacheOption.OnLoad);
    var bitmap = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
    bitmap.Freeze();
    return bitmap;
}

static (byte R, byte G, byte B, byte A) ReadPixel(BitmapSource bitmap, int x, int y)
{
    var pixel = new byte[4];
    bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
    return (pixel[2], pixel[1], pixel[0], pixel[3]);
}

static bool IsOpaqueDark((byte R, byte G, byte B, byte A) pixel, byte maximumChannel) =>
    pixel.A >= 250 &&
    pixel.R <= maximumChannel &&
    pixel.G <= maximumChannel &&
    pixel.B <= maximumChannel;

static bool IsStraightAlphaRedEdge((byte R, byte G, byte B, byte A) pixel) =>
    pixel.A is >= 20 and <= 45 &&
    pixel.R >= 235 &&
    pixel.G is >= 35 and <= 50 &&
    pixel.B <= 3;

static bool IsRedOutline(
    (byte R, byte G, byte B, byte A) pixel,
    byte minimumRed,
    byte maximumRed) =>
    pixel.A >= 210 &&
    pixel.R >= minimumRed &&
    pixel.R <= maximumRed &&
    pixel.G is >= 35 and <= 43 &&
    pixel.R >= pixel.G * 3 &&
    pixel.B <= 10;

static void AssertHeroGenerationRejected(
    HeroClassCatalogResult catalog,
    HeroClassDefinition heroClass,
    IReadOnlyCollection<string> selectedInitialQuirkIds,
    string expectedMessage)
{
    var rejected = false;
    try
    {
        _ = StagecoachHeroCandidateFactory.Generate(
            catalog,
            heroClass,
            seed: 1729,
            selectedInitialQuirkIds: selectedInitialQuirkIds);
    }
    catch (InvalidOperationException ex) when (ex.Message.Contains(expectedMessage, StringComparison.Ordinal))
    {
        rejected = true;
    }

    Assert(
        rejected,
        $"Selected initial quirks should be rejected with a message containing '{expectedMessage}'.");
}

static void AssertInitialQuirkSelectionRejected(
    HeroClassCatalogResult catalog,
    HeroClassDefinition heroClass,
    IReadOnlyCollection<string> selectedInitialQuirkIds,
    string expectedMessage)
{
    var rejected = false;
    try
    {
        StagecoachHeroCandidateFactory.ValidateInitialQuirkSelection(
            catalog,
            heroClass,
            selectedInitialQuirkIds);
    }
    catch (InvalidOperationException ex) when (ex.Message.Contains(expectedMessage, StringComparison.Ordinal))
    {
        rejected = true;
    }

    Assert(
        rejected,
        $"Initial quirk validation should reject the selection with a message containing '{expectedMessage}'.");
}

static void AssertHeroLevelGenerationRejected(
    HeroClassCatalogResult catalog,
    HeroClassDefinition heroClass,
    int resolveLevel,
    string expectedMessage)
{
    var rejected = false;
    try
    {
        _ = StagecoachHeroCandidateFactory.Generate(
            catalog,
            heroClass,
            seed: 1729,
            resolveLevel: resolveLevel,
            selectedInitialQuirkIds: []);
    }
    catch (InvalidOperationException ex) when (ex.Message.Contains(expectedMessage, StringComparison.Ordinal))
    {
        rejected = true;
    }

    Assert(rejected, $"Hero level {resolveLevel} should be rejected with a message containing '{expectedMessage}'.");
}

static void SetRevision(string path, ReadOnlySpan<byte> revision)
{
    using var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    stream.Position = 4;
    stream.Write(revision);
    stream.Flush(flushToDisk: true);
}

static byte[] ReadRevision(string path)
{
    using var stream = File.OpenRead(path);
    stream.Position = 4;
    var result = new byte[4];
    stream.ReadExactly(result);
    return result;
}

static void WriteLoc2(string path, IReadOnlyDictionary<string, string> entries)
{
    WriteLoc2Raw(
        path,
        entries.ToDictionary(
            pair => pair.Key,
            pair => Encoding.UTF8.GetBytes(pair.Value),
            StringComparer.Ordinal));
}

static void WriteLoc2Raw(string path, IReadOnlyDictionary<string, byte[]> entries)
{
    const int hashRecordOffset = 12 + 4096;
    const int hashRecordSize = 12;
    const int groupRecordSize = 8;
    const int valueRecordSize = 12;
    var groups = entries
        .GroupBy(pair => HashLoc2Key(pair.Key))
        .OrderBy(group => group.Key)
        .Select(group => (Hash: group.Key, Values: group.ToArray()))
        .ToArray();
    var values = groups.SelectMany(group => group.Values).ToArray();
    var groupTableOffset = hashRecordOffset + groups.Length * hashRecordSize;
    var valueTableOffset = groupTableOffset + groups.Length * groupRecordSize;
    var stringDataOffset = valueTableOffset + values.Length * valueRecordSize;

    using var stream = new MemoryStream();
    using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
    writer.Write(groupTableOffset);
    writer.Write(valueTableOffset);
    writer.Write(stringDataOffset);
    for (var bucket = 0; bucket < 512; bucket++)
    {
        var first = -1;
        var count = 0;
        for (var index = 0; index < groups.Length; index++)
        {
            if ((groups[index].Hash >> 23) != bucket)
            {
                continue;
            }

            first = first < 0 ? index : first;
            count++;
        }

        writer.Write(first < 0 ? 0 : hashRecordOffset + first * hashRecordSize);
        writer.Write(count);
    }

    for (var index = 0; index < groups.Length; index++)
    {
        writer.Write(groups[index].Hash);
        writer.Write(index);
        writer.Write(1);
    }

    var firstValueIndex = 0;
    foreach (var group in groups)
    {
        writer.Write(firstValueIndex);
        writer.Write(group.Values.Length);
        firstValueIndex += group.Values.Length;
    }

    var encodedValues = values.Select(pair => pair.Value).ToArray();
    var relativeStringOffset = 0;
    foreach (var encodedValue in encodedValues)
    {
        writer.Write(relativeStringOffset);
        writer.Write(encodedValue.Length + 1);
        writer.Write(256);
        relativeStringOffset += encodedValue.Length + 1;
    }

    foreach (var encodedValue in encodedValues)
    {
        writer.Write(encodedValue);
        writer.Write((byte)0);
    }

    writer.Flush();
    File.WriteAllBytes(path, stream.ToArray());
}

static byte[] EncodeLoc2Colour(string visibleText)
{
    return ConcatenateBytes(
        Encoding.UTF8.GetBytes("<c>"),
        [0x80, 0x81, 0xFF, 0x00, 0x10, 0x20],
        Encoding.UTF8.GetBytes(visibleText),
        Encoding.UTF8.GetBytes("</c>"));
}

static byte[] EncodeLoc2ColourOpenOnly(string visibleText)
{
    return ConcatenateBytes(
        Encoding.UTF8.GetBytes("<c>"),
        [0x80, 0x81, 0xFF, 0x00, 0x10, 0x20],
        Encoding.UTF8.GetBytes(visibleText));
}

static byte[] ConcatenateBytes(params byte[][] values) =>
    values.SelectMany(value => value).ToArray();

static void CorruptLoc2ValueOffset(string path, string key)
{
    var bytes = File.ReadAllBytes(path);
    var valueRecordOffset = FindLoc2ValueRecordOffset(bytes, key);
    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(valueRecordOffset, sizeof(uint)), uint.MaxValue);
    File.WriteAllBytes(path, bytes);
}

static void SetLoc2Hash(string path, string key, uint hash)
{
    var bytes = File.ReadAllBytes(path);
    var hashRecordOffset = FindLoc2HashRecordOffset(bytes, key);
    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(hashRecordOffset, sizeof(uint)), hash);
    File.WriteAllBytes(path, bytes);
}

static void CorruptLoc2ValueLength(string path, string key, uint byteLength)
{
    var bytes = File.ReadAllBytes(path);
    var valueRecordOffset = FindLoc2ValueRecordOffset(bytes, key);
    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(valueRecordOffset + 4, sizeof(uint)), byteLength);
    File.WriteAllBytes(path, bytes);
}

static void CorruptLoc2ValueUtf8(string path, string key)
{
    var bytes = File.ReadAllBytes(path);
    var valueRecordOffset = FindLoc2ValueRecordOffset(bytes, key);
    var stringDataOffset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8, sizeof(int)));
    var relativeOffset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(valueRecordOffset, sizeof(int)));
    bytes[stringDataOffset + relativeOffset] = 0xFF;
    File.WriteAllBytes(path, bytes);
}

static void CorruptLoc2ValueTerminator(string path, string key)
{
    var bytes = File.ReadAllBytes(path);
    var valueRecordOffset = FindLoc2ValueRecordOffset(bytes, key);
    var stringDataOffset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8, sizeof(int)));
    var relativeOffset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(valueRecordOffset, sizeof(int)));
    var byteLength = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(valueRecordOffset + 4, sizeof(int)));
    bytes[stringDataOffset + relativeOffset + byteLength - 1] = 0x7F;
    File.WriteAllBytes(path, bytes);
}

static int FindLoc2ValueRecordOffset(byte[] bytes, string key)
{
    const int groupRecordSize = 8;
    const int valueRecordSize = 12;
    var groupTableOffset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0, sizeof(int)));
    var valueTableOffset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4, sizeof(int)));
    var hashRecordOffset = FindLoc2HashRecordOffset(bytes, key);
    var groupIndex = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(hashRecordOffset + 4, sizeof(int)));
    var groupOffset = groupTableOffset + groupIndex * groupRecordSize;
    var firstValueIndex = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(groupOffset, sizeof(int)));
    return valueTableOffset + firstValueIndex * valueRecordSize;
}

static int FindLoc2HashRecordOffset(byte[] bytes, string key)
{
    const int hashRecordOffset = 12 + 4096;
    const int hashRecordSize = 12;
    var groupTableOffset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0, sizeof(int)));
    var targetHash = HashLoc2Key(key);
    for (var recordOffset = hashRecordOffset;
         recordOffset < groupTableOffset;
         recordOffset += hashRecordSize)
    {
        if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(recordOffset, sizeof(uint))) != targetHash)
        {
            continue;
        }

        return recordOffset;
    }

    throw new InvalidOperationException($"LOC2 test key '{key}' was not found.");
}

static uint HashLoc2Key(string value)
{
    var hash = 0u;
    foreach (var valueByte in Encoding.UTF8.GetBytes(value))
    {
        unchecked
        {
            hash = hash * 53u + valueByte;
        }
    }

    return hash;
}

static double ContrastRatio(string foregroundHex, string backgroundHex)
{
    static double RelativeLuminance(string hex)
    {
        if (hex.Length is not (7 or 9) || hex[0] != '#')
        {
            throw new InvalidDataException($"Unsupported contract colour '{hex}'.");
        }

        var channelStart = hex.Length == 9 ? 3 : 1;
        var channels = Enumerable.Range(0, 3)
            .Select(index => byte.Parse(
                hex.Substring(channelStart + index * 2, 2),
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture) / 255d)
            .Select(channel => channel <= 0.04045
                ? channel / 12.92
                : Math.Pow((channel + 0.055) / 1.055, 2.4))
            .ToArray();
        return channels[0] * 0.2126 + channels[1] * 0.7152 + channels[2] * 0.0722;
    }

    var foreground = RelativeLuminance(foregroundHex);
    var background = RelativeLuminance(backgroundHex);
    return (Math.Max(foreground, background) + 0.05) /
           (Math.Min(foreground, background) + 0.05);
}
}
catch (Exception error)
{
    Console.Error.WriteLine("FAIL: unhandled contract test exception");
    Console.Error.WriteLine(error);
    Environment.ExitCode = 1;
}
