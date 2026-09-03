internal static partial class ContractSuite
{
    private static void RunUiContracts(string repositoryRoot)
    {
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
        var battleMapXamlPath = Path.Combine(
            repositoryRoot,
            "src",
            "DarkestDungeonSaveEditor.App",
            "BattleMapView.xaml");
        var battleMapXaml = System.Xml.Linq.XDocument.Load(battleMapXamlPath);
        var battleMapCode = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "DarkestDungeonSaveEditor.App",
            "BattleMapView.xaml.cs"));
        var battleMapReaderCode = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "DarkestDungeonSaveEditor.Core",
            "BattleMapSnapshotReader.cs"));
        var battleMapEditorCode = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "DarkestDungeonSaveEditor.Core",
            "BattleMapSaveEditor.cs"));
        var battleMapEditServiceCode = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "DarkestDungeonSaveEditor.Core",
            "BattleMapEditService.cs"));
        var profileSaveMonitorCode = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "DarkestDungeonSaveEditor.Core",
            "ProfileSaveMonitor.cs"));
        var battleMapDesign = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "docs",
            "battle-map-editor-design.md"));
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
        var themedDialogXamlPath = Path.Combine(
            repositoryRoot,
            "src",
            "DarkestDungeonSaveEditor.App",
            "ThemedDialog.xaml");
        var themedDialogXaml = System.Xml.Linq.XDocument.Load(themedDialogXamlPath);
        var themedDialogCode = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "DarkestDungeonSaveEditor.App",
            "ThemedDialog.xaml.cs"));
        var dialogParchmentPath = Path.Combine(
            repositoryRoot,
            "src",
            "DarkestDungeonSaveEditor.App",
            "Assets",
            "dialog-parchment.png");
        var presentationNamespace = mainWindowXaml.Root?.Name.Namespace ??
            throw new InvalidDataException("MainWindow.xaml has no root namespace.");
        var xamlName = System.Xml.Linq.XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml");
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
            mainWindowCode.Contains("persist.game.json SHA-256={activeContent.SourceGameSha256}", StringComparison.Ordinal) &&
            mainWindowCode.Contains("内容目录数量快照：场景={FormatQuantitySaveContext(_quantitySaveContext)}", StringComparison.Ordinal) &&
            mainWindowCode.Contains("数量来源文件={Path.GetFullPath(quantitySourcePath)}", StringComparison.Ordinal) &&
            mainWindowCode.Contains("SHA-256={quantityItems.SourceSaveSha256}", StringComparison.Ordinal) &&
            mainWindowCode.Contains("persist.estate.json SHA-256={estateSaveSha256}", StringComparison.Ordinal),
            "Catalog loading must persist the selected profile path, game hash, active town/raid quantity-save path and hash, plus the estate hash needed alongside a raid catalog.");
        Assert(
            !mainWindowXaml.Descendants()
                .Any(element => element.Attribute(xamlName)?.Value == "CatalogSummaryTextBlock") &&
            !mainWindowCode.Contains("CatalogSummaryTextBlock", StringComparison.Ordinal) &&
            !mainWindowCode.Contains("UpdateCatalogSummary", StringComparison.Ordinal) &&
            mainWindowCode.Contains("目录加载完成：档案 {profile.ProfileId}；模式 {catalogs.Heroes.GameMode}", StringComparison.Ordinal) &&
            mainWindowCode.Contains("目录统计：{FormatQuantitySaveContext(_quantitySaveContext)}物品", StringComparison.Ordinal) &&
            mainWindowCode.Contains("当前场景隐藏项 {hiddenItemCount} 个", StringComparison.Ordinal) &&
            mainWindowCode.Contains("副本格位 {quantityItems.RaidOccupiedSlots}/", StringComparison.Ordinal) &&
            !mainWindowCode.Contains("Sum(item => item.SavedEntryCount)", StringComparison.Ordinal) &&
            mainWindowCode.Contains("怪癖定义 {catalogs.Heroes.InitialQuirks.Count} 个", StringComparison.Ordinal) &&
            mainWindowCode.Contains("姓名 {catalogs.Heroes.HeroNames.Count} 个", StringComparison.Ordinal) &&
            mainWindowCode.Contains("有招募事件的人物", StringComparison.Ordinal) &&
            mainWindowCode.Contains("有后续玩法怪癖线索的人物", StringComparison.Ordinal),
            "The crowded top catalog summary must be removed; the runtime log must retain current scene, visible/hidden item, raid-slot, trinket, hero, quirk, name, level, and runtime-signal diagnostics.");
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
        var itemGridElement = ReadNamedMainElement("DataGrid", "ItemGrid");
        var trinketGridElement = ReadNamedMainElement("DataGrid", "TrinketGrid");
        var heroGridElement = ReadNamedMainElement("DataGrid", "HeroGrid");
        var dataGridStyle = appXaml
            .Descendants(presentationNamespace + "Style")
            .Single(element => element.Attribute("TargetType")?.Value == "DataGrid");
        var xamlNamespace = System.Xml.Linq.XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml");
        var applicationResourceKeys = appXaml
            .Descendants()
            .Select(element => element.Attribute(xamlNamespace)?.Value)
            .Where(value => value is not null)
            .ToHashSet(StringComparer.Ordinal);
        var itemColumnHeaders = itemGridElement
            .Descendants()
            .Where(element => element.Name == presentationNamespace + "DataGridTextColumn" ||
                              element.Name == presentationNamespace + "DataGridCheckBoxColumn")
            .Select(element => element.Attribute("Header")?.Value)
            .ToArray();
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
        var heroQuirkRangeColumn = heroGridElement
            .Descendants(presentationNamespace + "DataGridTextColumn")
            .Single(column => column.Attribute("Header")?.Value == "自然怪癖范围");
        var itemEnglishColumn = itemGridElement
            .Descendants(presentationNamespace + "DataGridTextColumn")
            .Single(column => column.Attribute("Header")?.Value == "English");
        var heroSourceColumn = heroGridElement
            .Descendants(presentationNamespace + "DataGridTextColumn")
            .Single(column => column.Attribute("Header")?.Value == "来源");
        Assert(
                itemColumnHeaders.SequenceEqual(
                    new[] { "物品 ID", "中文名", "English", "位置 / 类型 / 堆叠", "当前数量", "来源" },
                    StringComparer.Ordinal) &&
            trinketColumnHeaders.SequenceEqual(
                new[] { "饰品 ID", "中文名", "English", "稀有度", "来源", "定义上限" },
                StringComparer.Ordinal) &&
            heroColumnHeaders.SequenceEqual(
                new[] { "职业 ID", "中文名", "English", "来源", "生成方式", "等级范围", "自然怪癖范围" },
                StringComparer.Ordinal) &&
            heroQuirkRangeColumn.Attribute("Width")?.Value == "1.75*" &&
            heroQuirkRangeColumn.Attribute("MinWidth")?.Value == "170" &&
            itemEnglishColumn.Attribute("CellStyle") is null &&
            itemEnglishColumn.Attribute("HeaderStyle") is null &&
            heroSourceColumn.Attribute("CellStyle") is null &&
            heroSourceColumn.Attribute("HeaderStyle") is null &&
            new[] { itemGridElement, trinketGridElement, heroGridElement }
                .All(grid => grid.Attribute("CellStyle") is null && grid.Attribute("RowStyle") is null) &&
            ReadStyleSetter(dataGridStyle, "HorizontalContentAlignment") == "Stretch" &&
            ReadStyleSetter(dataGridStyle, "VerticalContentAlignment") == "Top" &&
                itemGridElement.Descendants(presentationNamespace + "DataGridTextColumn")
                    .All(column => column.Attribute("ElementStyle")?.Value == "{StaticResource DataGridTextElementStyle}") &&
            trinketGridElement.Descendants(presentationNamespace + "DataGridTextColumn")
                .All(column => column.Attribute("ElementStyle")?.Value == "{StaticResource DataGridTextElementStyle}") &&
            heroGridElement.Descendants(presentationNamespace + "DataGridTextColumn")
                .All(column => column.Attribute("ElementStyle")?.Value == "{StaticResource DataGridTextElementStyle}"),
                "The item, trinket, and hero catalogs must keep only user-relevant columns, avoid special-case separators at the two specified boundaries, and use vertically centered ellipsized text.");
        Assert(
            applicationResourceKeys.Contains("CardStyle") &&
            applicationResourceKeys.Contains("PrimaryButtonStyle") &&
            applicationResourceKeys.Contains("GoldButtonStyle") &&
            applicationResourceKeys.Contains("PanelHeaderStyle") &&
            applicationResourceKeys.Contains("RedPanelHeaderStyle") &&
            applicationResourceKeys.Contains("OlivePanelHeaderStyle") &&
            applicationResourceKeys.Contains("LogTextBoxStyle") &&
            mainWindowCode.Contains(
                "仓库槽位 {FormatStorageCapacity(_trinketStorage)}",
                StringComparison.Ordinal),
            "The game-panel application shell and effective storage-capacity diagnostic must remain wired into the runtime log.");
        var mainWindowRoot = mainWindowXaml.Root
            ?? throw new InvalidDataException("MainWindow.xaml has no root element.");
        var appTextureBrush = appXaml
            .Descendants(presentationNamespace + "ImageBrush")
            .Single(element => element.Attribute(xamlNamespace)?.Value == "AppTextureBrush");
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
                appTextureBrush.Attribute("Stretch")?.Value == "UniformToFill" &&
                appTextureBrush.Attribute("TileMode") is null &&
                ReadAppColor("SurfaceColor") == "#D80A0A09" &&
                mainWindowRoot.Element(presentationNamespace + "Grid")?
                    .Element(presentationNamespace + "Border")?
                    .Attribute("Background")?.Value == "#90050505" &&
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
            HasOnlySquareCorners(appXaml, mainWindowXaml, themedDialogXaml),
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
        var dataGridRowStyle = appXaml
            .Descendants(presentationNamespace + "Style")
            .Single(element =>
                element.Attribute("TargetType")?.Value == "DataGridRow" &&
                element.Attribute(xamlNamespace) is null);
        var dataGridCellChrome = dataGridCellStyle
            .Descendants(presentationNamespace + "Border")
            .Single(element => element.Attribute(xamlName)?.Value == "CellChrome");
        var dataGridHeaderStyle = appXaml
            .Descendants(presentationNamespace + "Style")
            .Single(element =>
                element.Attribute("TargetType")?.Value == "DataGridColumnHeader" &&
                element.Attribute(xamlNamespace) is null);
        var dataGridHeaderTemplate = dataGridHeaderStyle
            .Descendants(presentationNamespace + "ControlTemplate")
            .Single(element => element.Attribute("TargetType")?.Value == "DataGridColumnHeader");
        var dataGridHeaderLeadingSeparator = dataGridHeaderTemplate
            .Descendants(presentationNamespace + "Rectangle")
            .Single(element => element.Attribute(xamlName)?.Value == "HeaderLeadingSeparator");
        var dataGridHeaderBottomSeparator = dataGridHeaderTemplate
            .Descendants(presentationNamespace + "Rectangle")
            .Single(element => element.Attribute(xamlName)?.Value == "HeaderBottomSeparator");
        var dataGridHeaderSortArrow = dataGridHeaderTemplate
            .Descendants(presentationNamespace + "Path")
            .Single(element => element.Attribute(xamlName)?.Value == "SortArrow");
        var dataGridHeaderGrippers = dataGridHeaderTemplate
            .Descendants(presentationNamespace + "Thumb")
            .Where(element => element.Attribute(xamlName)?.Value is
                "PART_LeftHeaderGripper" or "PART_RightHeaderGripper")
            .ToArray();
        Assert(
            ReadStyleSetter(dataGridCellStyle, "VerticalContentAlignment") == "Center" &&
            ReadStyleSetter(dataGridStyle, "GridLinesVisibility") == "All" &&
            ReadStyleSetter(dataGridStyle, "UseLayoutRounding") == "True" &&
            ReadStyleSetter(dataGridStyle, "FocusVisualStyle") == "{x:Null}" &&
            !string.IsNullOrWhiteSpace(ReadStyleSetter(dataGridStyle, "VerticalGridLinesBrush")) &&
            ReadStyleSetter(dataGridCellStyle, "Foreground") == "{StaticResource TextPrimaryBrush}" &&
            ReadStyleSetter(dataGridCellStyle, "BorderBrush") == "Transparent" &&
            ReadStyleSetter(dataGridCellStyle, "BorderThickness") == "0" &&
            ReadStyleSetter(dataGridCellStyle, "FocusVisualStyle") == "{x:Null}" &&
            ReadStyleSetter(dataGridRowStyle, "BorderThickness") == "0" &&
            ReadStyleSetter(dataGridRowStyle, "FocusVisualStyle") == "{x:Null}" &&
            dataGridCellChrome.Attribute("BorderBrush") is null &&
            dataGridCellChrome.Attribute("BorderThickness") is null &&
            dataGridCellStyle.Descendants(presentationNamespace + "ContentPresenter")
                .Any(element => element.Attribute("VerticalAlignment")?.Value == "Center") &&
            ReadStyleSetter(dataGridHeaderStyle, "VerticalContentAlignment") == "Center" &&
            ReadStyleSetter(dataGridHeaderStyle, "HorizontalContentAlignment") == "Center" &&
            ReadStyleSetter(dataGridHeaderStyle, "SnapsToDevicePixels") == "True" &&
            ReadStyleSetter(dataGridHeaderStyle, "UseLayoutRounding") == "True" &&
            dataGridHeaderLeadingSeparator.Attribute("Width")?.Value == "1" &&
            dataGridHeaderLeadingSeparator.Attribute("HorizontalAlignment")?.Value == "Left" &&
            dataGridHeaderLeadingSeparator.Attribute("Fill")?.Value == "{TemplateBinding BorderBrush}" &&
            dataGridHeaderBottomSeparator.Attribute("Height")?.Value == "1" &&
            dataGridHeaderBottomSeparator.Attribute("VerticalAlignment")?.Value == "Bottom" &&
            dataGridHeaderSortArrow.Attribute("HorizontalAlignment")?.Value == "Right" &&
            dataGridHeaderSortArrow.Attribute("VerticalAlignment")?.Value == "Top" &&
            dataGridHeaderSortArrow.Attribute("Margin")?.Value == "0,3,3,0" &&
            dataGridHeaderGrippers.Length == 2 &&
            dataGridHeaderGrippers.All(element =>
                element.Attribute("Style")?.Value ==
                "{StaticResource DataGridColumnHeaderGripperStyle}") &&
            dataGridHeaderTemplate.Descendants(presentationNamespace + "Trigger")
                .Count(trigger => trigger.Attribute("Property")?.Value == "SortDirection") == 2 &&
            applicationResourceKeys.Contains("DataGridTextElementStyle") &&
            dataGridCellStyle.Descendants(presentationNamespace + "Trigger")
                .Any(trigger =>
                    trigger.Attribute("Property")?.Value == "IsSelected" &&
                    trigger.Attribute("Value")?.Value == "True" &&
                    trigger.Descendants(presentationNamespace + "Setter")
                        .Any(setter =>
                            setter.Attribute("Property")?.Value == "Foreground" &&
                            setter.Attribute("Value")?.Value == "#FFF8EC")) &&
            dataGridRowStyle.Descendants(presentationNamespace + "Trigger")
                .Any(trigger =>
                    trigger.Attribute("Property")?.Value == "IsSelected" &&
                    trigger.Attribute("Value")?.Value == "True" &&
                    trigger.Descendants(presentationNamespace + "Setter")
                        .Any(setter =>
                            setter.Attribute("Property")?.Value == "Background" &&
                            setter.Attribute("Value")?.Value == "#5D1718")) &&
            !dataGridRowStyle.Descendants(presentationNamespace + "Trigger")
                .Where(trigger => trigger.Attribute("Property")?.Value == "IsSelected")
                .SelectMany(trigger => trigger.Descendants(presentationNamespace + "Setter"))
                .Any(setter => setter.Attribute("Property")?.Value is "BorderBrush" or "BorderThickness") &&
            !dataGridCellStyle.Descendants(presentationNamespace + "Trigger")
                .Any(trigger => trigger.Attribute("Property")?.Value == "IsKeyboardFocusWithin"),
            "Catalog body rows must use automatic DataGrid gridlines without rendering the control's default gray cell border; selection uses only the shared red row fill. Every header draws the same leading separator, text stays centered, and resize/sort behavior remains available.");
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
                catalogTabHeaderPanel.Attribute("Columns")?.Value == "4" &&
            catalogTabHeaderPanel.Attribute("Margin")?.Value == "0,0,0,5" &&
            !tabItemStyle.Descendants(presentationNamespace + "Trigger")
                .Any(trigger => trigger.Attribute("Property")?.Value == "IsKeyboardFocused") &&
            mainWindowXaml.Descendants(presentationNamespace + "TabItem").Take(3)
                .All(tabItem => tabItem.Elements(presentationNamespace + "DataGrid").Count() == 1 &&
                                !tabItem.Elements(presentationNamespace + "Border").Any()) &&
            mainWindowXaml.Descendants(presentationNamespace + "TabItem").Skip(3).Single()
                .Elements().Single().Name.LocalName == "BattleMapView",
                "The three catalogs plus battle workspace must share the full width equally, keep their headers centered, and avoid wrapping the existing catalog grids in nested borders.");
        var catalogTabHeaders = mainWindowXaml
            .Descendants(presentationNamespace + "TabItem")
            .Select(tab => tab.Attribute("Header")?.Value)
            .ToArray();
        var showUnusedItemsCheckBox = ReadNamedMainElement("CheckBox", "ShowUnusedItemsCheckBox");
        var loadCatalogRowsStage = mainWindowCode.IndexOf(
            "CrashDiagnostics.SetStage(\"LoadCatalog: populating visible rows\");",
            StringComparison.Ordinal);
        var loadCatalogModeRefresh = loadCatalogRowsStage < 0
            ? -1
            : mainWindowCode.IndexOf(
                "UpdateCatalogMode();",
                loadCatalogRowsStage,
                StringComparison.Ordinal);
        var loadCatalogFilterRefresh = loadCatalogRowsStage < 0
            ? -1
            : mainWindowCode.IndexOf(
                "ApplyFilter();",
                loadCatalogRowsStage,
                StringComparison.Ordinal);
        Assert(
            catalogTabHeaders.SequenceEqual(
                new[] { "物品  /  ITEMS", "饰品  /  TRINKETS", "人物  /  HEROES", "战斗  /  BATTLE" },
                StringComparer.Ordinal) &&
            mainWindowCode.Contains("QuantityItemCatalog.LoadAsync(activeContent, codec)", StringComparison.Ordinal) &&
            showUnusedItemsCheckBox.Attribute("Checked")?.Value == "ShowUnusedItemsCheckBox_Changed" &&
            showUnusedItemsCheckBox.Attribute("Unchecked")?.Value == "ShowUnusedItemsCheckBox_Changed" &&
            mainWindowCode.Contains("definition.IsHiddenByDefault", StringComparison.Ordinal) &&
            showUnusedItemsCheckBox.Attribute("Content")?.Value == "显示当前场景隐藏项（0）" &&
            showUnusedItemsCheckBox.Attribute("ToolTip")?.Value ==
                "切换为只显示当前场景默认隐藏的物品定义" &&
            mainWindowCode.Contains(
                "definition.IsHiddenByDefault == showHiddenItemsOnly",
                StringComparison.Ordinal) &&
            mainWindowCode.Contains("显示当前场景隐藏项（", StringComparison.Ordinal) &&
            !mainWindowCode.Contains("显示未使用定义", StringComparison.Ordinal) &&
            loadCatalogRowsStage >= 0 &&
            loadCatalogModeRefresh > loadCatalogRowsStage &&
            loadCatalogModeRefresh < loadCatalogFilterRefresh &&
            mainWindowCode.Contains("IsPresentInSave = resultingEntryCount > 0", StringComparison.Ordinal) &&
            mainWindowCode.Contains("SavedEntryCount = resultingEntryCount", StringComparison.Ordinal) &&
            mainWindowCode.Contains("PrepareQuantityItemEditAsync(", StringComparison.Ordinal) &&
            mainWindowCode.Contains("CopiesLabel.Text = isItemTab ? \"目标数量\" : \"添加数量\"", StringComparison.Ordinal) &&
            ReadNamedMainElement("TextBox", "CopiesTextBox").Attribute("TextChanged")?.Value ==
            "CopiesTextBox_TextChanged" &&
            ReadNamedMainElement("TextBox", "CopiesTextBox").Attribute("PreviewMouseLeftButtonDown")?.Value ==
            "SelectAllTextOnFirstClick" &&
            ReadNamedMainElement("TextBox", "CopiesTextBox").Attribute("GotKeyboardFocus")?.Value ==
            "SelectAllTextOnKeyboardFocus" &&
            mainWindowCode.Contains("textBox.IsKeyboardFocusWithin", StringComparison.Ordinal) &&
            mainWindowCode.Contains("textBox.SelectAll();", StringComparison.Ordinal) &&
            mainWindowCode.Contains("ResetQuantityInputForCurrentTab();", StringComparison.Ordinal) &&
            mainWindowCode.Contains("1 => \"1\"", StringComparison.Ordinal) &&
            mainWindowCode.Contains("庄园库存 / 可手动配给", StringComparison.Ordinal) &&
            mainWindowCode.Contains("庄园库存 / 不可手动配给", StringComparison.Ordinal) &&
            mainWindowCode.Contains("庄园库存 / 手动配给未声明", StringComparison.Ordinal) &&
            mainWindowCode.Contains("人物自带或副本中生成的数量不受本次修改影响。", StringComparison.Ordinal) &&
            mainWindowCode.Contains("是否可手动配给未声明，且不会直接修改副本背包。", StringComparison.Ordinal) &&
            !mainWindowCode.Contains("不可配给进副本", StringComparison.Ordinal) &&
            !mainWindowCode.Contains("该物品不可从庄园携入远征。", StringComparison.Ordinal) &&
            mainWindowCode.Contains("string.IsNullOrWhiteSpace(itemWarning)", StringComparison.Ordinal) &&
            mainWindowCode.Contains("程序会先完整备份当前档案。", StringComparison.Ordinal) &&
            !mainWindowCode.Contains("此功能只修改 persist.estate.json", StringComparison.Ordinal) &&
            !mainWindowCode.Contains("persist.raid.json 不会变化", StringComparison.Ordinal) &&
            !mainWindowCode.Contains("estate 类型能表示庄园计数", StringComparison.Ordinal) &&
            mainWindowCode.Contains("requireCurrentQuantitySnapshot: CatalogTabs.SelectedIndex == 0", StringComparison.Ordinal) &&
            mainWindowCode.Contains("expectedInRaid ? profile.RaidSavePath : profile.EstateSavePath", StringComparison.Ordinal) &&
            mainWindowCode.Contains("副本背包  /  RAID ITEMS", StringComparison.Ordinal),
            "The quantity editor must appear immediately left of trinkets, use an absolute target amount, switch between town storage and the current raid inventory, keep only concise user-facing risks, retain persisted unused definitions, invalidate stale previews when input changes, and reject stale displayed quantities or a changed town/raid state.");
        var battleMapRoot = battleMapXaml.Root ??
            throw new InvalidDataException("BattleMapView.xaml has no root element.");
        var battleMapNamedElements = battleMapXaml
            .Descendants()
            .Select(element => element.Attribute(xamlName)?.Value)
            .Where(value => value is not null)
            .ToHashSet(StringComparer.Ordinal);
        Assert(
            battleMapNamedElements.Contains("MapViewport") &&
            battleMapNamedElements.Contains("MapCanvas") &&
            battleMapNamedElements.Contains("EmptyStatePanel") &&
            battleMapNamedElements.Contains("MapSelectionTextBlock") &&
            battleMapNamedElements.Contains("PrototypeBadgeTextBlock") &&
            !battleMapNamedElements.Contains("MapLegend") &&
            battleMapRoot.Attribute("PreviewMouseWheel")?.Value == "BattleMapView_PreviewMouseWheel" &&
            battleMapRoot.Attribute("PreviewMouseRightButtonDown")?.Value ==
                "BattleMapView_PreviewMouseRightButtonDown" &&
            battleMapRoot.Attribute("PreviewMouseRightButtonUp")?.Value ==
                "BattleMapView_PreviewMouseRightButtonUp" &&
            battleMapRoot.Attribute("PreviewMouseMove")?.Value == "BattleMapView_PreviewMouseMove" &&
            battleMapRoot.Attribute("Unloaded") is null &&
            battleMapCode.Contains("PanThreshold = 5", StringComparison.Ordinal) &&
            battleMapCode.Contains("MinimumZoom = 0.45", StringComparison.Ordinal) &&
            battleMapCode.Contains("MaximumZoom = 2.40", StringComparison.Ordinal) &&
            battleMapCode.Contains("FitMapToViewport", StringComparison.Ordinal) &&
            battleMapCode.Contains("hasPersistedContent ? \"替换\" : \"新建\"", StringComparison.Ordinal) &&
            battleMapCode.Contains("移动队伍到此", StringComparison.Ordinal) &&
            !battleMapCode.Contains("PrototypeCellKind.CorridorEndpoint", StringComparison.Ordinal) &&
            !battleMapCode.Contains("PrototypeCellKind.DoorTransition", StringComparison.Ordinal) &&
            !battleMapCode.Contains("EndpointInteractionTarget", StringComparison.Ordinal) &&
            !battleMapCode.Contains("IsEndpointHovered", StringComparison.Ordinal) &&
            battleMapCode.Contains("CloseActiveContextMenu", StringComparison.Ordinal) &&
            battleMapCode.Contains("if (!_cells.Contains(cell))", StringComparison.Ordinal) &&
            battleMapCode.Contains("cell.Kind == PrototypeCellKind.Room ? 20 : 0", StringComparison.Ordinal) &&
            battleMapCode.Contains("area.Kind != BattleMapAreaKind.Corridor || tile.StaticType == 1", StringComparison.Ordinal) &&
            battleMapCode.Contains("tile.MapY - minimumY", StringComparison.Ordinal) &&
            !battleMapCode.Contains("maximumY - tile.MapY", StringComparison.Ordinal) &&
            battleMapCode.Contains("_cells.Count} 个可操作格", StringComparison.Ordinal) &&
            battleMapCode.Contains("_selectedCell = null;", StringComparison.Ordinal) &&
            battleMapCode.Contains("BuildPrototypeMap();", StringComparison.Ordinal) &&
            battleMapCode.Contains("首领遭遇", StringComparison.Ordinal) &&
            battleMapCode.Contains("原版 / BASE", StringComparison.Ordinal) &&
            battleMapCode.Contains("官方 DLC / DLC", StringComparison.Ordinal) &&
            battleMapCode.Contains("Mod / MODS", StringComparison.Ordinal) &&
            battleMapCode.Contains(@"panels\panel_map.png", StringComparison.Ordinal) &&
            battleMapCode.Contains(@"panels\icons_map", StringComparison.Ordinal) &&
            battleMapCode.Contains("room_boss.png", StringComparison.Ordinal) &&
            !battleMapCode.Contains("TryLoadMapIcon(\"hall_door.png\")", StringComparison.Ordinal) &&
            battleMapCode.Contains("marker_curio.png", StringComparison.Ordinal) &&
            !battleMapCode.Contains("marker_hunger.png", StringComparison.Ordinal) &&
            battleMapCode.Contains("IsHiddenSystemContent", StringComparison.Ordinal) &&
            battleMapCode.Contains("cell.RawContent == (int)BattleMapTileContent.Hunger", StringComparison.Ordinal) &&
            battleMapCode.Contains("if (!isHiddenSystemContent)", StringComparison.Ordinal) &&
            battleMapCode.Contains("marker_secret.png", StringComparison.Ordinal) &&
            battleMapCode.Contains("indicator.png", StringComparison.Ordinal) &&
            battleMapCode.Contains("PrototypeBadgeTextBlock.Text = \"全局视野\"", StringComparison.Ordinal) &&
            battleMapCode.Contains("PrototypeBadge.ToolTip = _usesOriginalMapAssets", StringComparison.Ordinal) &&
            !battleMapCode.Contains("全局视野 · 预览模式 · 占位素材", StringComparison.Ordinal) &&
            !battleMapCode.Contains("实时只读 · 全局视野 · 操作不写入", StringComparison.Ordinal) &&
            !battleMapCode.Contains("只读监听 · 已同步", StringComparison.Ordinal) &&
            !battleMapCode.Contains("真实地图：", StringComparison.Ordinal) &&
            !battleMapCode.Contains("右击操作仍只作用于界面预览", StringComparison.Ordinal) &&
            battleMapCode.Split("存档未发生变化。", StringSplitOptions.None).Length - 1 == 3 &&
            battleMapCode.Contains("cell.Knowledge is PrototypeKnowledge.Unknown or PrototypeKnowledge.Scouted", StringComparison.Ordinal) &&
            !battleMapCode.Contains("return TryLoadMapIcon(\"room_unknown.png\")", StringComparison.Ordinal) &&
            !battleMapCode.Contains("return TryLoadMapIcon(\"hall_dark.png\")", StringComparison.Ordinal) &&
            !battleMapCode.Contains("cell.Knowledge != PrototypeKnowledge.Unknown &&", StringComparison.Ordinal) &&
            !battleMapCode.Contains("Knowledge = PrototypeKnowledge.Visited", StringComparison.Ordinal) &&
            battleMapCode.Contains("missingPartyMarker", StringComparison.Ordinal) &&
            battleMapCode.Contains("missingCompletionMarker", StringComparison.Ordinal) &&
            battleMapCode.Contains("BitmapCacheOption.OnLoad", StringComparison.Ordinal) &&
            battleMapCode.Contains("new CroppedBitmap(bitmap, new Int32Rect(12, 18, 648, 326))", StringComparison.Ordinal) &&
            battleMapCode.Contains("ViewportUnits = BrushMappingMode.RelativeToBoundingBox", StringComparison.Ordinal) &&
            battleMapCode.Contains("TileMode = TileMode.None", StringComparison.Ordinal) &&
            battleMapCode.Contains("BattleMapSnapshotReader", StringComparison.Ordinal) &&
            battleMapCode.Contains("ProfileSaveMonitor", StringComparison.Ordinal) &&
            battleMapCode.Contains("persist.map.json", StringComparison.Ordinal) &&
            battleMapCode.Contains("persist.raid.json", StringComparison.Ordinal) &&
            battleMapCode.Contains("BuildSnapshotMap", StringComparison.Ordinal) &&
            battleMapCode.Contains("stagedCells", StringComparison.Ordinal) &&
            battleMapCode.Contains("MapGridTileSize = 24", StringComparison.Ordinal) &&
            battleMapCode.Contains("LoadSnapshotWithRetryAsync", StringComparison.Ordinal) &&
            battleMapCode.Contains("ScheduleRefreshRetry", StringComparison.Ordinal) &&
            battleMapCode.Contains("RetryLiveSnapshotAfterDelayAsync", StringComparison.Ordinal) &&
            battleMapCode.Contains("mapExists != raidExists", StringComparison.Ordinal) &&
            battleMapCode.Contains("保留上一快照 · 等待另一份副本存档", StringComparison.Ordinal) &&
            battleMapReaderCode.Contains("sourceHashBefore", StringComparison.Ordinal) &&
            battleMapReaderCode.Contains("sourceHashAfter", StringComparison.Ordinal) &&
            battleMapReaderCode.Contains("PairVerificationDelay", StringComparison.Ordinal) &&
            battleMapReaderCode.Contains("structuralIssues", StringComparison.Ordinal) &&
            battleMapReaderCode.Contains("does not belong to the captured map topology", StringComparison.Ordinal) &&
            battleMapReaderCode.Contains("area.Tiles.Count - 1 - savedAreaTile.Value", StringComparison.Ordinal) &&
            battleMapReaderCode.Contains("return area.Tiles[physicalOrdinal].TileIndex", StringComparison.Ordinal) &&
            profileSaveMonitorCode.Contains("FileSystemWatcher", StringComparison.Ordinal) &&
            profileSaveMonitorCode.Contains("PollNow", StringComparison.Ordinal) &&
            profileSaveMonitorCode.Contains("DetectChangesAndSchedule", StringComparison.Ordinal) &&
            battleMapCode.Contains("BattleMapEditService", StringComparison.Ordinal) &&
            battleMapCode.Contains("PrepareDeleteContentAsync", StringComparison.Ordinal) &&
            battleMapCode.Contains("PrepareMovePartyAsync", StringComparison.Ordinal) &&
            battleMapCode.Contains("ThemedDialog.Confirm", StringComparison.Ordinal) &&
            !battleMapCode.Contains("File.Write", StringComparison.Ordinal) &&
            !battleMapCode.Contains("File.Copy", StringComparison.Ordinal) &&
            !battleMapCode.Contains("File.Move", StringComparison.Ordinal) &&
            !battleMapCode.Contains("File.Delete", StringComparison.Ordinal) &&
            battleMapEditorCode.Contains("dynamicTile[\"content\"] = 0", StringComparison.Ordinal) &&
            battleMapEditorCode.Contains("dynamicTile[\"mash_index\"] = -1", StringComparison.Ordinal) &&
            battleMapEditorCode.Contains("area.Tiles.Count - 1 - physicalOrdinal", StringComparison.Ordinal) &&
            battleMapEditorCode.Contains("ValidateStationaryRaidState", StringComparison.Ordinal) &&
            battleMapEditorCode.Contains("当前正在战斗，不能修改地图", StringComparison.Ordinal) &&
            battleMapEditorCode.Contains("饥饿节点不属于地图编辑范围", StringComparison.Ordinal) &&
            battleMapEditServiceCode.Contains("EnsureGameIsNotRunning", StringComparison.Ordinal) &&
            battleMapEditServiceCode.Contains("检测到《暗黑地牢》仍在运行", StringComparison.Ordinal) &&
            battleMapEditServiceCode.Contains("ValidateLivePair", StringComparison.Ordinal) &&
            battleMapEditServiceCode.Contains("CreateBackup", StringComparison.Ordinal) &&
            battleMapEditServiceCode.Contains("RestoreTarget", StringComparison.Ordinal),
            "The battle workspace must expose a pannable, zoomable real save map rendered from original sprites, keep native hidden hunger nodes visually and operationally out of content editing, limit interaction to rooms and visible corridor tiles in native map orientation, keep create/replace as previews, and route confirmed delete/move actions through guarded, backed-up save transactions with localized safety errors.");
        Assert(
            mainWindowXaml.Descendants()
                .Any(element => element.Name.LocalName == "BattleMapView" &&
                                element.Attribute(xamlName)?.Value == "BattleMapPanel") &&
            mainWindowCode.Contains("private const int BattleTabIndex = 3;", StringComparison.Ordinal) &&
            mainWindowCode.Contains("CatalogToolsPanel.Visibility = isBattleTab ? Visibility.Collapsed", StringComparison.Ordinal) &&
            mainWindowCode.Contains("CatalogActionPanel.Visibility = isBattleTab ? Visibility.Collapsed", StringComparison.Ordinal) &&
            mainWindowCode.Contains("BattleMapPanel.LoadProfileAsync", StringComparison.Ordinal) &&
            mainWindowCode.Contains("BattleMapPanel.ClearProfile();", StringComparison.Ordinal) &&
            mainWindowCode.Contains("protected override void OnClosed", StringComparison.Ordinal) &&
            mainWindowCode.Contains("BattleMapPanel.SnapshotRefreshed -=", StringComparison.Ordinal) &&
            mainWindowCode.Contains("LoadCatalog: battle map snapshot", StringComparison.Ordinal) &&
            battleMapDesign.Contains("transactional delete and party movement", StringComparison.OrdinalIgnoreCase) &&
            battleMapDesign.Contains("loaded at runtime from the user's selected Darkest Dungeon installation", StringComparison.Ordinal) &&
            battleMapDesign.Contains("Original game artwork is not copied into or redistributed", StringComparison.Ordinal) &&
            battleMapDesign.Contains("boss picker is global", StringComparison.OrdinalIgnoreCase) &&
            battleMapDesign.Contains("does not render, select, right-click, or offer movement", StringComparison.Ordinal) &&
            battleMapDesign.Contains("must not apply a Cartesian Y-axis inversion", StringComparison.Ordinal) &&
            battleMapDesign.Contains("display-only global-vision rule", StringComparison.Ordinal) &&
            battleMapDesign.Contains("does not reveal the map inside the game or write scouting progress", StringComparison.Ordinal) &&
            battleMapDesign.Contains("Content previews must preserve the cell's persisted knowledge state", StringComparison.Ordinal) &&
            battleMapDesign.Contains("does not read game process memory", StringComparison.OrdinalIgnoreCase) &&
            battleMapDesign.Contains("create and replace remain preview-only", StringComparison.OrdinalIgnoreCase) &&
            battleMapDesign.Contains("full-profile backup", StringComparison.OrdinalIgnoreCase),
            "The battle tab must hide catalog-only controls, show and refresh a real map only for a loaded raid, and retain the approved global-vision, arbitrary-boss, normal-tile-only, native-orientation, delete, and movement decisions in durable documentation.");
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
            HasOnlySquareCorners(appXaml, mainWindowXaml, initialQuirkDialogXaml, themedDialogXaml) &&
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
                nativeWindowThemeCode.Contains("DwmwaBorderColor = 34", StringComparison.Ordinal) &&
                nativeWindowThemeCode.Contains("red: 0x3B, green: 0x15, blue: 0x16", StringComparison.Ordinal) &&
                !nativeWindowThemeCode.Contains("red: 0x8B, green: 0x68, blue: 0x36", StringComparison.Ordinal) &&
                initialQuirkDialogXaml.Root?.Element(presentationNamespace + "Grid")?
                    .Element(presentationNamespace + "Border")?
                    .Attribute("Background")?.Value == "#90050505",
                "Both editor windows must request a dark native caption, muted light text, a dark-red system border, and visible full-window texture instead of a bright yellow native frame.");
        var themedDialogCancelButton = themedDialogXaml
            .Descendants(presentationNamespace + "Button")
            .Single(element => element.Attribute(xamlName)?.Value == "CancelButton");
        var themedDialogConfirmButton = themedDialogXaml
            .Descendants(presentationNamespace + "Button")
            .Single(element => element.Attribute(xamlName)?.Value == "ConfirmButton");
        var themedDialogParchmentBrush = themedDialogXaml
            .Descendants(presentationNamespace + "ImageBrush")
            .Single(element => element.Attribute(xamlNamespace)?.Value == "DialogParchmentBrush");
        var themedDialogParchmentSurface = themedDialogXaml
            .Descendants(presentationNamespace + "Border")
            .Single(element => element.Attribute(xamlName)?.Value == "ParchmentSurfaceBorder");
        Assert(
            themedDialogXaml.Root?.Attribute("Icon")?.Value == "Assets/save-editor.ico" &&
            themedDialogXaml.Root?.Attribute("ResizeMode")?.Value == "NoResize" &&
            themedDialogXaml.Root?.Attribute("ShowInTaskbar")?.Value == "False" &&
            themedDialogXaml.Root?.Attribute("WindowStartupLocation")?.Value == "CenterOwner" &&
            themedDialogXaml.Root?.Attribute("SizeToContent")?.Value == "Height" &&
            HasGamePanelWindowFrame(themedDialogXaml) &&
            themedDialogParchmentBrush.Attribute("ImageSource")?.Value == "Assets/dialog-parchment.png" &&
            themedDialogXaml.Descendants(presentationNamespace + "ScrollViewer")
                .Any(element => element.Attribute("MaxHeight")?.Value == "390" &&
                                element.Attribute("VerticalScrollBarVisibility")?.Value == "Auto") &&
            themedDialogCancelButton.Attribute("Style")?.Value == "{StaticResource GhostButtonStyle}" &&
            themedDialogConfirmButton.Attribute("Style")?.Value == "{StaticResource PrimaryButtonStyle}" &&
            themedDialogParchmentSurface.Attribute("BorderThickness")?.Value == "0" &&
            !themedDialogXaml.Descendants()
                .Any(element => element.Attribute(xamlName)?.Value is
                    "KeyboardHintTextBlock" or "KindMarkerBorder") &&
            !themedDialogXaml.Descendants(presentationNamespace + "Border")
                .Any(element => element.Attribute("Grid.Row")?.Value == "2") &&
            File.Exists(dialogParchmentPath) &&
            new FileInfo(dialogParchmentPath).Length > 100_000 &&
            appProjectText.Contains("<Resource Include=\"Assets\\dialog-parchment.png\" />", StringComparison.Ordinal),
            "Application messages must use an unboxed parchment surface, no lower-left helper or framed marker/footer, the dark outer frame, bounded scrolling, and existing themed action buttons.");
        Assert(
            !mainWindowCode.Contains("MessageBox.Show", StringComparison.Ordinal) &&
            mainWindowCode.Contains("ThemedDialog.Confirm(", StringComparison.Ordinal) &&
            mainWindowCode.Contains("ThemedDialogKind.Information", StringComparison.Ordinal) &&
            mainWindowCode.Contains("ThemedDialogKind.Error", StringComparison.Ordinal) &&
            themedDialogCode.Contains("NativeWindowTheme.ApplyDarkTitleBar(this);", StringComparison.Ordinal) &&
            themedDialogCode.Contains("CancelButton.IsDefault = true;", StringComparison.Ordinal) &&
            themedDialogCode.Contains("CancelButton.IsCancel = true;", StringComparison.Ordinal) &&
            themedDialogCode.Contains("ConfirmButton.IsDefault = true;", StringComparison.Ordinal) &&
            themedDialogCode.Contains("ConfirmButton.IsCancel = true;", StringComparison.Ordinal) &&
            themedDialogCode.Contains(".ShowDialog() == true", StringComparison.Ordinal),
            "Save confirmation, success, and failure messages must use the themed modal while preserving safe default-cancel, Enter/Escape, owner, and dark-caption behavior.");
        var initialQuirkBindings = initialQuirkDialogXaml
            .Descendants(presentationNamespace + "DataGridTextColumn")
            .Select(column => column.Attribute("Binding")?.Value)
            .ToArray();
        var unknownHpDisplayIndex = initialQuirkDialogCode.IndexOf(
            "WriteStatusReason.Contains(\"max_hp\"",
            StringComparison.Ordinal);
        var knownHpDisplayIndex = initialQuirkDialogCode.IndexOf(
                "Definition.MaxHpModifiers.Count == 0",
            StringComparison.Ordinal);
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
                .Single(column => column.Attribute("Header")?.Value == "限制 / 不可用原因")
                .Attribute("MinWidth")?.Value == "190" &&
            !initialQuirkDialogXaml.ToString().Contains("职业禁用项", StringComparison.Ordinal) &&
            initialQuirkDialogXaml.ToString().Contains(
                "roster_limit 由游戏在招募进入 roster 时执行，生成马车候选不检查也不警告",
                StringComparison.Ordinal) &&
            !initialQuirkDialogCode.Contains("selectedIds.Append(row.Id)", StringComparison.Ordinal) &&
            initialQuirkDialogCode.Contains("IncompatibleQuirkIds.Contains", StringComparison.Ordinal) &&
            initialQuirkDialogCode.Contains(
                "CombineReasons(row.ContextReason, incompatibilityReason)",
                StringComparison.Ordinal) &&
            initialQuirkDialogCode.Contains("row.SetAvailability(true, row.ContextReason);", StringComparison.Ordinal) &&
            initialQuirkDialogCode.Contains(
                "BaseUnavailableReason = CompactRowReason(baseUnavailableReason)",
                StringComparison.Ordinal) &&
            initialQuirkDialogCode.Contains(
                "CompactRowReason(definition.WriteStatusReason)",
                StringComparison.Ordinal) &&
            initialQuirkDialogCode.Contains(
                "private const string CompactSingletonReason = \"singleton 定义上限 1\"",
                StringComparison.Ordinal) &&
            unknownHpDisplayIndex >= 0 &&
            knownHpDisplayIndex > unknownHpDisplayIndex &&
            initialQuirkDialogCode.Contains("? \"待验证\"", StringComparison.Ordinal) &&
                initialQuirkDialogCode.Contains("? \"无\"", StringComparison.Ordinal) &&
                initialQuirkDialogCode.Contains("FormatMaxHpModifier", StringComparison.Ordinal) &&
                initialQuirkDialogCode.Contains("火光 ≤", StringComparison.Ordinal) &&
                initialQuirkDialogCode.Contains("_resolveLevel", StringComparison.Ordinal) &&
                initialQuirkDialogCode.Contains(
                    "row.SetAvailability(row.IsSelected, row.BaseUnavailableReason)",
                    StringComparison.Ordinal) &&
                initialQuirkDialogCode.Contains("if (requestedValue)", StringComparison.Ordinal) &&
                !initialQuirkDialogCode.Contains("row.IsSelected = !requestedValue", StringComparison.Ordinal) &&
                mainWindowCode.Contains("GetSelectedHeroLevel(),", StringComparison.Ordinal),
            "The initial quirk dialog must fully show fixed/special kind text, distinguish absent from unverified HP modifiers, keep context-limit explanations visible, keep quota feedback in the summary/click validation, and reserve row-level dynamic reasons for actual incompatibilities.");
        var initialQuirkGrid = initialQuirkDialogXaml
            .Descendants(presentationNamespace + "DataGrid")
            .Single(grid => grid.Attribute(xamlName)?.Value == "QuirkGrid");
        var initialQuirkCheckBox = initialQuirkGrid
            .Descendants(presentationNamespace + "CheckBox")
            .Single(checkBox => checkBox.Attribute("Click")?.Value == "QuirkCheckBox_Click");
        Assert(
            initialQuirkGrid.Attribute("IsReadOnly")?.Value == "True" &&
            initialQuirkGrid.Attribute("SelectionUnit")?.Value == "FullRow" &&
            initialQuirkGrid.Attribute("CellStyle") is null &&
            initialQuirkGrid.Attribute("RowStyle") is null &&
            initialQuirkCheckBox.Attribute("IsChecked")?.Value == "{Binding IsSelected, Mode=OneWay}" &&
            initialQuirkDialogCode.Contains("_view is IEditableCollectionView editableView", StringComparison.Ordinal) &&
            initialQuirkDialogCode.Contains("editableView.CommitEdit();", StringComparison.Ordinal) &&
            mainWindowCode.Contains("if (dialog?.IsVisible == true)", StringComparison.Ordinal) &&
            mainWindowCode.Contains("dialog.Close();", StringComparison.Ordinal),
            "The quirk selector must not open DataGrid edit transactions and must close an orphaned dialog if ShowDialog unwinds unexpectedly.");
        var initialQuirkSearchBox = initialQuirkDialogXaml
            .Descendants(presentationNamespace + "TextBox")
            .Single(textBox => textBox.Attribute(xamlName)?.Value == "SearchTextBox");
        Assert(
            initialQuirkSearchBox.Attribute("ToolTip")?.Value ==
                "按怪癖 ID、中英文名、类别、来源或 HP 规则筛选" &&
            initialQuirkDialogCode.Contains(
                "row.Source.Contains(keyword, StringComparison.OrdinalIgnoreCase)",
                StringComparison.Ordinal) &&
            !initialQuirkDialogCode.Contains(
                "row.UnavailableReason.Contains(keyword, StringComparison.OrdinalIgnoreCase)",
                StringComparison.Ordinal),
            "Quirk filtering must use stable catalog fields only; selecting an incompatible quirk must not make its counterpart appear merely because the dynamic unavailable reason names the query quirk.");

        var jarPath = Path.Combine(repositoryRoot, "tools", "DDSaveEditor", "DDSaveEditor.jar");
    }
}
