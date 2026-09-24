internal static partial class ContractSuite
{
    private static void RunUiContracts(string repositoryRoot)
    {
        var appSourceDirectory = Path.Combine(
            repositoryRoot,
            "src",
            "DarkestDungeonSaveEditor.App");
        var mainWindowXamlPath = Path.Combine(
            appSourceDirectory,
            "MainWindow.xaml");
        var mainWindowXaml = LoadLocalizedContractXaml(mainWindowXamlPath);
        var mainWindowCode = string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(
                    appSourceDirectory,
                    "MainWindow*.cs",
                    SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(File.ReadAllText));
        Assert(mainWindowCode.Contains("await StartProfileSyncAsync(activeContent, quantityItems", StringComparison.Ordinal) &&
            mainWindowCode.Contains("confirmationRevision != _editRevision", StringComparison.Ordinal) &&
            mainWindowCode.Contains("RefreshCatalogRowsPreservingInput", StringComparison.Ordinal) &&
            mainWindowCode.Contains("if (_restoringCatalogSelection) return;", StringComparison.Ordinal) &&
            mainWindowCode.Contains("sceneChanged ? null", StringComparison.Ordinal) &&
            mainWindowCode.Contains("generation != _catalogGeneration", StringComparison.Ordinal) &&
            mainWindowCode.Contains("BattleMapPanel.UsesSharedProfileMonitor = true", StringComparison.Ordinal) &&
            mainWindowCode.Contains("!IsProfileOperationBusy && _syncRequested", StringComparison.Ordinal),
            "Profile sync must preserve input, clear cross-scene targets, reject stale dialogs/profiles, and defer until writers finish.");
        Assert(System.Text.RegularExpressions.Regex.IsMatch(mainWindowCode,
                @"UpdateInitialQuirkSelectionSummary\(\);\s+UpdateEnabledState\(\);"),
            "Changing catalog tabs must restore search and input availability after synchronization on the battle tab.");
        var battleMapXamlPath = Path.Combine(
            appSourceDirectory,
            "BattleMapView.xaml");
        var battleMapXaml = LoadLocalizedContractXaml(battleMapXamlPath);
        var battleMapXamlText = File.ReadAllText(battleMapXamlPath);
        var battleMapCode = string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(
                    appSourceDirectory,
                    "BattleMapView*.cs",
                    SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(File.ReadAllText));
        var liveRefreshCode = File.ReadAllText(Path.Combine(appSourceDirectory, "BattleMapView.LiveRefresh.cs"));
        foreach (var handler in new[] { "BattleMapView.Commands.cs", "BattleMapView.ForceTown.cs" })
        {
            var handlerCode = File.ReadAllText(Path.Combine(appSourceDirectory, handler));
            Assert(handlerCode.Contains("CrashDiagnostics.RecordException", StringComparison.Ordinal) &&
                   handlerCode.Contains("prepared?.SessionId", StringComparison.Ordinal) &&
                   handlerCode.Contains("profile.ProfileDirectory", StringComparison.Ordinal) &&
                   handlerCode.Contains("ex is AggregateException", StringComparison.Ordinal),
                "Handled map/force-town failures must record the exception and operation context and disclose incomplete recovery.");
        }
        var battleMapIntegrationCode = File.ReadAllText(Path.Combine(appSourceDirectory, "MainWindow.BattleMapIntegration.cs"));
        Assert(liveRefreshCode.Contains("if (UsesSharedProfileMonitor ||", StringComparison.Ordinal) &&
            battleMapIntegrationCode.Contains("RequestProfileSync(invalidatePreview: false);", StringComparison.Ordinal),
            "Shared profile sync must suppress legacy map-only refreshes and catch up after map operations, including failures.");
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
        var forceTownSaveServiceCode = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "DarkestDungeonSaveEditor.Core",
            "ForceTownSaveService.cs"));
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
        var appXaml = LoadLocalizedContractXaml(appXamlPath);
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
        var themedDialogXaml = LoadLocalizedContractXaml(themedDialogXamlPath);
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
        var battleEncounterDialogPath = Path.Combine(
            appSourceDirectory,
            "BattleEncounterSelectionDialog.xaml");
        var battleEncounterDialog = LoadLocalizedContractXaml(battleEncounterDialogPath);
        var battleEncounterDialogCode = File.ReadAllText(
            battleEncounterDialogPath + ".cs");
        var battleAttachmentDialogPath = Path.Combine(
            appSourceDirectory,
            "BattleRoomAttachmentSelectionDialog.xaml");
        var battleAttachmentDialog = LoadLocalizedContractXaml(battleAttachmentDialogPath);
        var battleAttachmentDialogCode = File.ReadAllText(
            battleAttachmentDialogPath + ".cs");
        var presentationNamespace = mainWindowXaml.Root?.Name.Namespace ??
            throw new InvalidDataException("MainWindow.xaml has no root namespace.");
        var xamlName = System.Xml.Linq.XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml");
        Assert(
            !mainWindowCode.Contains("Loaded += (_, _) => Discover();", StringComparison.Ordinal) &&
            mainWindowCode.Contains("Discover_Click(object sender, RoutedEventArgs e) => Discover();", StringComparison.Ordinal) &&
            mainWindowCode.Contains("WorkshopDirectoryTextBox.Text = game.WorkshopDirectory;", StringComparison.Ordinal) &&
            mainWindowCode.Contains("LocalModDirectoryTextBox.Text = game.DefaultLocalModDirectory;", StringComparison.Ordinal) &&
            mainWindowCode.Contains("MainWindow_Discovery_006", StringComparison.Ordinal) &&
            mainWindowCode.Contains("MainWindow_Discovery_002", StringComparison.Ordinal) &&
            mainWindowCode.Contains("MainWindow_Discovery_004", StringComparison.Ordinal) &&
            mainWindowCode.Contains("MainWindow_Discovery_003", StringComparison.Ordinal) &&
            !mainWindowCode.Contains("自动发现：游戏=", StringComparison.Ordinal),
            "Path discovery must run only after the user clicks the button, fill the derived Workshop/local Mod paths, and report its result in user-facing language instead of terse counters.");
        Assert(
            appCode.Contains("SaveEditorLocations.ResolveLogDirectory(AppContext.BaseDirectory)", StringComparison.Ordinal) &&
            mainWindowCode.Contains("CrashDiagnostics.RecordStatus(message, level);", StringComparison.Ordinal) &&
            mainWindowCode.Contains("CrashDiagnostics.RecordCatalogDiagnostics(diagnosticBatch)", StringComparison.Ordinal) &&
            appCode.Contains("foreach (var entry in batch.Drain())", StringComparison.Ordinal),
            "Project-local logging must persist ordinary UI status and all grouped catalog diagnostics, including those outside the visible-message limit.");
        Assert(
            mainWindowCode.Contains("MainWindow_CatalogLoading_006", StringComparison.Ordinal) &&
            mainWindowCode.Contains("MainWindow_CatalogLoading_007", StringComparison.Ordinal) &&
            mainWindowCode.Contains("persist.game.json SHA-256={activeContent.SourceGameSha256}", StringComparison.Ordinal) &&
            mainWindowCode.Contains("MainWindow_CatalogLoading_015", StringComparison.Ordinal) &&
            mainWindowCode.Contains("MainWindow_CatalogLoading_016", StringComparison.Ordinal) &&
            mainWindowCode.Contains("SHA-256={quantityItems.SourceSaveSha256}", StringComparison.Ordinal) &&
            mainWindowCode.Contains("persist.estate.json SHA-256={estateSaveSha256}", StringComparison.Ordinal),
            "Catalog loading must persist the selected profile path, game hash, active town/raid quantity-save path and hash, plus the estate hash needed alongside a raid catalog.");
        Assert(
            !mainWindowXaml.Descendants()
                .Any(element => element.Attribute(xamlName)?.Value == "CatalogSummaryTextBlock") &&
            !mainWindowCode.Contains("CatalogSummaryTextBlock", StringComparison.Ordinal) &&
            !mainWindowCode.Contains("UpdateCatalogSummary", StringComparison.Ordinal) &&
            mainWindowCode.Contains("MainWindow_CatalogLoading_017", StringComparison.Ordinal) &&
            mainWindowCode.Contains("MainWindow_CatalogLoading_020", StringComparison.Ordinal) &&
            mainWindowCode.Contains("MainWindow_CatalogLoading_021", StringComparison.Ordinal) &&
            mainWindowCode.Contains("MainWindow_CatalogLoading_022", StringComparison.Ordinal) &&
            !mainWindowCode.Contains("Sum(item => item.SavedEntryCount)", StringComparison.Ordinal) &&
            mainWindowCode.Contains("MainWindow_CatalogLoading_024", StringComparison.Ordinal) &&
            mainWindowCode.Contains("MainWindow_CatalogLoading_025", StringComparison.Ordinal) &&
            mainWindowCode.Contains("MainWindow_CatalogLoading_027", StringComparison.Ordinal) &&
            mainWindowCode.Contains("MainWindow_CatalogLoading_028", StringComparison.Ordinal),
            "The crowded top catalog summary must be removed; the runtime log must retain current scene, visible/hidden item, raid-slot, trinket, hero, quirk, name, level, and runtime-signal diagnostics.");
        Assert(
            mainWindowXaml.Descendants(presentationNamespace + "Run")
                .Any(run => run.Attribute("Text")?.Value == "{Binding GenerationMode, Mode=OneWay}") &&
            !mainWindowXaml.Descendants(presentationNamespace + "DataGridCheckBoxColumn").Any() &&
            mainWindowCode.Contains("{ IsEnabled: true } => EditorText.Get(\"MainWindow_RowModels_010\")", StringComparison.Ordinal) &&
            mainWindowCode.Contains("{ IsEnabled: false } => EditorText.Get(\"MainWindow_RowModels_011\")", StringComparison.Ordinal) &&
            mainWindowCode.Contains("_ => EditorText.Get(\"MainWindow_RowModels_012\")", StringComparison.Ordinal),
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
        Assert(
            mainWindowXaml.Descendants(presentationNamespace + "Run")
                .Any(run => run.Attribute("Text")?.Value == "{Binding LimitDisplay, Mode=OneWay}") &&
            mainWindowCode.Contains("0 => EditorText.Get(\"MainWindow_Presentation_009\")", StringComparison.Ordinal) &&
            mainWindowCode.Contains("PreviewWarningTextBlock", StringComparison.Ordinal),
            "The trinket UI must render limit zero as unlimited and expose a dedicated preview warning area.");
        Assert(
            mainWindowCode.Contains(
                "FormatHeroPreviewWarnings(preparedHeroEdit.Preview)",
                StringComparison.Ordinal) &&
            mainWindowCode.Contains("MainWindow_Presentation_007", StringComparison.Ordinal) &&
            mainWindowCode.Contains("MainWindow_Presentation_006", StringComparison.Ordinal) &&
            mainWindowCode.Contains("MainWindow_Presentation_008", StringComparison.Ordinal) &&
            !mainWindowCode.Contains("HeroQuirkLimitKind.RosterLimit", StringComparison.Ordinal),
            "Hero quirk warnings must remain scoped to singleton duplication across the roster and all stagecoach pools.");
        Assert(
            mainWindowCode.Contains("preview.MayRefreshOnTownReturn", StringComparison.Ordinal) &&
            mainWindowCode.Contains("MainWindow_Presentation_005", StringComparison.Ordinal) &&
            mainWindowCode.Contains("FormatHeroQuirkLimitWarnings(preview)", StringComparison.Ordinal) &&
            mainWindowCode.Contains("FormatHeroPreviewWarnings(_preparedHeroEdit.Preview)", StringComparison.Ordinal) &&
            mainWindowCode.Contains("PreviewWarningTextBlock.Text = heroWarning", StringComparison.Ordinal) &&
            mainWindowCode.Contains("MainWindow_EditWorkflow_024", StringComparison.Ordinal),
            "Hero refresh risk must share the existing preview warning, confirmation, and log path without replacing singleton warnings.");
        Assert(
            mainWindowCode.Contains(
                "preparedHeroEdit.Preview.TargetPool == StagecoachRecruitPool.Shard",
                StringComparison.Ordinal) &&
            mainWindowCode.Contains("EditorText.Get(\"MainWindow_EditWorkflow_025\")", StringComparison.Ordinal) &&
            mainWindowCode.Contains("EditorText.Get(\"MainWindow_EditWorkflow_026\")", StringComparison.Ordinal) &&
            !mainWindowCode.Contains("向普通马车加入", StringComparison.Ordinal),
            "Hero preview and confirmation must name the actual ordinary or shard destination selected by the save mutation.");
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
        var itemEnglishColumn = itemGridElement
            .Descendants(presentationNamespace + "DataGridTextColumn")
            .Single(column => column.Attribute("Header")?.Value == "English");
        var heroSourceColumn = heroGridElement
            .Descendants(presentationNamespace + "DataGridTextColumn")
            .Single(column => column.Attribute("Header")?.Value == "来源");
        Assert(
                itemColumnHeaders.SequenceEqual(
                    new[] { "物品 ID", "中文名", "English", "当前数量", "来源" },
                    StringComparer.Ordinal) &&
            trinketColumnHeaders.SequenceEqual(
                new[] { "饰品 ID", "中文名", "English", "来源" },
                StringComparer.Ordinal) &&
            heroColumnHeaders.SequenceEqual(
                new[] { "职业 ID", "中文名", "English", "来源", "等级范围" },
                StringComparer.Ordinal) &&
            heroSourceColumn.Attribute("Width")?.Value == "2*" &&
            heroSourceColumn.Attribute("MinWidth")?.Value == "180" &&
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
                "MainWindow_CatalogLoading_023",
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
            mainWindowRoot.Attribute("FontFamily")?.Value == "{x:Static local:EditorTypography.Body}" &&
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
            ReadStyleSetter(pageTitleStyle, "FontFamily") == "{x:Static local:EditorTypography.Heading}" &&
            ReadStyleSetter(panelHeaderStyle, "FontFamily") == "{x:Static local:EditorTypography.Heading}" &&
            ReadStyleSetter(logTextBoxStyle, "FontFamily") == "{x:Static local:EditorTypography.Body}" &&
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
            mainWindowCode.Contains("MainWindow_State_002", StringComparison.Ordinal) &&
            !mainWindowCode.Contains("显示未使用定义", StringComparison.Ordinal) &&
            loadCatalogRowsStage >= 0 &&
            loadCatalogModeRefresh > loadCatalogRowsStage &&
            loadCatalogModeRefresh < loadCatalogFilterRefresh &&
            mainWindowCode.Contains("IsPresentInSave = resultingEntryCount > 0", StringComparison.Ordinal) &&
            mainWindowCode.Contains("SavedEntryCount = resultingEntryCount", StringComparison.Ordinal) &&
            mainWindowCode.Contains("PrepareQuantityItemEditAsync(", StringComparison.Ordinal) &&
            mainWindowCode.Contains("CopiesLabel.Text = isItemTab ? EditorText.Get(\"MainWindow_State_012\") : EditorText.Get(\"MainWindow_State_013\")", StringComparison.Ordinal) &&
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
            mainWindowCode.Contains("MainWindow_Presentation_015", StringComparison.Ordinal) &&
            mainWindowCode.Contains("MainWindow_Presentation_016", StringComparison.Ordinal) &&
            mainWindowCode.Contains("MainWindow_Presentation_017", StringComparison.Ordinal) &&
            mainWindowCode.Contains("MainWindow_Presentation_019", StringComparison.Ordinal) &&
            mainWindowCode.Contains("MainWindow_Presentation_020", StringComparison.Ordinal) &&
            !mainWindowCode.Contains("不可配给进副本", StringComparison.Ordinal) &&
            !mainWindowCode.Contains("该物品不可从庄园携入远征。", StringComparison.Ordinal) &&
            mainWindowCode.Contains("string.IsNullOrWhiteSpace(itemWarning)", StringComparison.Ordinal) &&
            mainWindowCode.Contains("MainWindow_EditWorkflow_056", StringComparison.Ordinal) &&
            !mainWindowCode.Contains("此功能只修改 persist.estate.json", StringComparison.Ordinal) &&
            !mainWindowCode.Contains("persist.raid.json 不会变化", StringComparison.Ordinal) &&
            !mainWindowCode.Contains("estate 类型能表示庄园计数", StringComparison.Ordinal) &&
            mainWindowCode.Contains("requireCurrentQuantitySnapshot: CatalogTabs.SelectedIndex == 0", StringComparison.Ordinal) &&
            mainWindowCode.Contains("expectedInRaid ? profile.RaidSavePath : profile.EstateSavePath", StringComparison.Ordinal) &&
            mainWindowCode.Contains("MainWindow_CatalogLoading_011", StringComparison.Ordinal),
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
            battleMapNamedElements.Contains("ForceTownButton") &&
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
            battleMapCode.Contains("hasPersistedContent ? EditorText.Get(\"BattleMapView_Commands_001\") : EditorText.Get(\"BattleMapView_Commands_002\")", StringComparison.Ordinal) &&
            battleMapCode.Contains(
                "hasPersistedContent || cell.HasResidualContentBinding",
                StringComparison.Ordinal) &&
            battleMapCode.Contains("BattleMapView_Commands_005", StringComparison.Ordinal) &&
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
            battleMapCode.Contains("BattleMapView_MapConstruction_004", StringComparison.Ordinal) &&
            battleMapCode.Contains("_selectedCell = null;", StringComparison.Ordinal) &&
            battleMapCode.Contains("BuildPrototypeMap();", StringComparison.Ordinal) &&
            battleMapCode.Contains("BattleEncounterCatalog", StringComparison.Ordinal) &&
            battleMapCode.Contains("BattleEncounterSelectionDialog_014", StringComparison.Ordinal) &&
            battleMapCode.Contains("BattleEncounterClassification.Ordinary", StringComparison.Ordinal) &&
            !battleMapCode.Contains("随机（仅界面预览）", StringComparison.Ordinal) &&
            !battleMapCode.Contains("AddDirectEncounterItems", StringComparison.Ordinal) &&
            !battleMapCode.Contains("FormatEncounterHeader", StringComparison.Ordinal) &&
            !battleMapCode.Contains("）…", StringComparison.Ordinal) &&
            battleMapCode.Contains("BattleMapView_Commands_006", StringComparison.Ordinal) &&
            battleMapCode.Contains("BattleEncounterSelectionDialog_005", StringComparison.Ordinal) &&
            battleMapCode.Contains("BattleEncounterClassification.RoamingBoss", StringComparison.Ordinal) &&
            battleMapCode.Contains("BattleEncounterClassification.RoamingEncounter", StringComparison.Ordinal) &&
            battleMapCode.Contains("BattleEncounterClassification.ConditionalOrAdditional", StringComparison.Ordinal) &&
            battleMapCode.Contains("CreateEncounterPickerItem", StringComparison.Ordinal) &&
            battleMapCode.Contains("BattleEncounterCatalog.GetSelectionCandidates", StringComparison.Ordinal) &&
            battleMapCode.Contains("FindDirectlyAddressableEncounter", StringComparison.Ordinal) &&
            !battleMapCode.Contains("生成 Encounter Bridge", StringComparison.Ordinal) &&
            battleMapCode.Contains("BridgeEncounters", StringComparison.Ordinal) &&
            battleMapCode.Contains("OriginDungeonId", StringComparison.Ordinal) &&
            battleMapCode.Contains("ManagedBattleEncounterBridgeService", StringComparison.Ordinal) &&
            battleMapCode.Contains("EnsureEncounterAsync", StringComparison.Ordinal) &&
            battleMapCode.Contains("encounterResolver", StringComparison.Ordinal) &&
            battleMapCode.Contains("SelectAndPlaceEncounterAsync", StringComparison.Ordinal) &&
            battleMapXamlText.Contains("x:Name=\"PART_Popup\"", StringComparison.Ordinal) &&
            battleMapXamlText.Contains("x:Name=\"ItemsPresenter\"", StringComparison.Ordinal) &&
            battleMapXamlText.Contains("PopupAnimation=\"None\"", StringComparison.Ordinal) &&
            battleMapXamlText.Contains("VerticalOffset=\"-3\"", StringComparison.Ordinal) &&
            !battleMapXamlText.Contains("HorizontalOffset=", StringComparison.Ordinal) &&
            !battleMapXamlText.Contains("PlacementTarget=\"{Binding ElementName=ItemChrome}\"", StringComparison.Ordinal) &&
            battleMapCode.Contains("cell.Content == PrototypeContent.Entrance", StringComparison.Ordinal) &&
            battleMapCode.Contains("cell.Content == PrototypeContent.SecretDoor", StringComparison.Ordinal) &&
            battleMapCode.Contains("IsUnsupportedScriptContent", StringComparison.Ordinal) &&
            battleMapCode.Contains(@"panels\panel_map.png", StringComparison.Ordinal) &&
            battleMapCode.Contains(@"panels\icons_map", StringComparison.Ordinal) &&
            battleMapCode.Contains("room_boss.png", StringComparison.Ordinal) &&
            !battleMapCode.Contains("TryLoadMapIcon(\"hall_door.png\")", StringComparison.Ordinal) &&
            battleMapCode.Contains("marker_curio.png", StringComparison.Ordinal) &&
            battleMapCode.Contains("PrototypeContent.Hunger => \"marker_hunger.png\"", StringComparison.Ordinal) &&
            battleMapCode.Contains("BattleMapTileContent.Hunger => PrototypeContent.Hunger", StringComparison.Ordinal) &&
            battleMapCode.Contains("BattleMapTileContent.Hunger => EditorText.Get(\"BattleMapView_CellVisuals_018\")", StringComparison.Ordinal) &&
            battleMapCode.Contains("IsHungerContent", StringComparison.Ordinal) &&
            !battleMapCode.Contains("isHiddenSystemContent", StringComparison.Ordinal) &&
            battleMapCode.Contains("cell.RawContent == (int)BattleMapTileContent.Hunger", StringComparison.Ordinal) &&
            battleMapCode.Contains("if (!isProtectedContent)", StringComparison.Ordinal) &&
            battleMapCode.Contains("marker_secret.png", StringComparison.Ordinal) &&
            battleMapCode.Contains("indicator.png", StringComparison.Ordinal) &&
            battleMapCode.Contains("PrototypeBadgeTextBlock.Text = EditorText.Get(\"BattleMapView_Assets_001\")", StringComparison.Ordinal) &&
            battleMapCode.Contains("PrototypeBadge.ToolTip = _usesOriginalMapAssets", StringComparison.Ordinal) &&
            !battleMapCode.Contains("全局视野 · 预览模式 · 占位素材", StringComparison.Ordinal) &&
            !battleMapCode.Contains("实时只读 · 全局视野 · 操作不写入", StringComparison.Ordinal) &&
            !battleMapCode.Contains("只读监听 · 已同步", StringComparison.Ordinal) &&
            !battleMapCode.Contains("真实地图：", StringComparison.Ordinal) &&
            !battleMapCode.Contains("右击操作仍只作用于界面预览", StringComparison.Ordinal) &&
            new[] { "BattleMapView_Commands_034", "BattleMapView_Commands_103" }.All(key => battleMapCode.Contains(key, StringComparison.Ordinal)) &&
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
            battleMapCode.Contains("BattleMapView_LiveRefresh_004", StringComparison.Ordinal) &&
            battleMapReaderCode.Contains("sourceHashBefore", StringComparison.Ordinal) &&
            battleMapReaderCode.Contains("sourceHashAfter", StringComparison.Ordinal) &&
            battleMapReaderCode.Contains("PairVerificationDelay", StringComparison.Ordinal) &&
            battleMapReaderCode.Contains("structuralIssues", StringComparison.Ordinal) &&
            battleMapReaderCode.Contains("does not belong to the captured map topology", StringComparison.Ordinal) &&
            battleMapReaderCode.Contains("area.Tiles.Count - 1 - savedAreaTile.Value", StringComparison.Ordinal) &&
            battleMapReaderCode.Contains("return area.Tiles[physicalOrdinal].TileIndex", StringComparison.Ordinal) &&
            battleMapReaderCode.Contains("ReadInt(staticTile, \"cur\", 0)", StringComparison.Ordinal) &&
            profileSaveMonitorCode.Contains("FileSystemWatcher", StringComparison.Ordinal) &&
            profileSaveMonitorCode.Contains("PollNow", StringComparison.Ordinal) &&
            profileSaveMonitorCode.Contains("DetectChangesAndSchedule", StringComparison.Ordinal) &&
            battleMapCode.Contains("BattleMapEditService", StringComparison.Ordinal) &&
            battleMapCode.Contains("ForceTownSaveService", StringComparison.Ordinal) &&
            battleMapCode.Contains("ForceTownButton_Click", StringComparison.Ordinal) &&
            battleMapCode.Contains("BattleMapView_ForceTown_003", StringComparison.Ordinal) &&
            !battleMapCode.Contains("if (!committed && generation == _profileGeneration)", StringComparison.Ordinal) &&
            battleMapCode.Contains("PrepareDeleteContentAsync", StringComparison.Ordinal) &&
            battleMapCode.Contains("PrepareMovePartyAsync", StringComparison.Ordinal) &&
            battleMapCode.Contains("PreparePlaceBattleAsync", StringComparison.Ordinal) &&
            battleMapCode.Contains("PreparePlaceContentAsync", StringComparison.Ordinal) &&
            battleMapCode.Contains("preserveBattle: false", StringComparison.Ordinal) &&
            !battleMapCode.Contains("ApplyContentPreview", StringComparison.Ordinal) &&
            battleMapCode.Contains("CreateRegionalContentItem(cell, EditorText.Get(\"BattleMapView_CellVisuals_014\")", StringComparison.Ordinal) &&
            battleMapCode.Contains("CreateRegionalContentItem(cell, EditorText.Get(\"BattleMapView_CellVisuals_017\")", StringComparison.Ordinal) &&
            battleMapCode.Contains("GetRegionalCandidates(kind, _currentSnapshot?.DungeonId", StringComparison.Ordinal) &&
            battleMapCode.Contains("CreateAsyncActionMenuItem(label, icon, () => PlaceRegionalContentAsync(target, kind))", StringComparison.Ordinal) &&
            battleMapCode.Contains("SelectRegionalContent(kind, snapshot.DungeonId, Random.Shared.NextDouble())", StringComparison.Ordinal) &&
            battleMapCode.Contains("await PlaceContentAsync(target, definition);", StringComparison.Ordinal) &&
            battleMapCode.Contains("!definition.IsAvailableInDungeon(snapshot.DungeonId)", StringComparison.Ordinal) &&
            battleMapCode.Contains("BattleMapView_Commands_003", StringComparison.Ordinal) &&
            battleMapCode.Contains("PrepareSetBattleAttachmentAsync", StringComparison.Ordinal) &&
            battleMapCode.Contains("PrepareRemoveBattleAttachmentAsync", StringComparison.Ordinal) &&
            battleMapCode.Contains("ThemedDialog.Confirm", StringComparison.Ordinal) &&
            !battleMapCode.Contains("File.Write", StringComparison.Ordinal) &&
            !battleMapCode.Contains("File.Copy", StringComparison.Ordinal) &&
            !battleMapCode.Contains("File.Move", StringComparison.Ordinal) &&
            !battleMapCode.Contains("File.Delete", StringComparison.Ordinal) &&
            battleMapEditorCode.Contains("dynamicTile[\"content\"] = 0", StringComparison.Ordinal) &&
            battleMapEditorCode.Contains("dynamicTile[\"mash_index\"] = -1", StringComparison.Ordinal) &&
            battleMapEditorCode.Contains("dynamicTile[\"content\"] = (int)BattleMapTileContent.Battle", StringComparison.Ordinal) &&
            battleMapEditorCode.Contains("BattleMapTileContent.GuardedCurio", StringComparison.Ordinal) &&
            battleMapEditorCode.Contains("BattleMapTileContent.GuardedTreasure", StringComparison.Ordinal) &&
            battleMapEditorCode.Contains("staticTile[\"cur\"] = attachment.PropHash", StringComparison.Ordinal) &&
            battleMapEditorCode.Contains("dynamicTile[\"curio_prop\"] = attachment.PropHash", StringComparison.Ordinal) &&
            battleMapEditorCode.Contains("SetScalarToZero(staticTile, \"cur\")", StringComparison.Ordinal) &&
            battleMapEditorCode.Contains("SetScalarToZero(staticTile, \"obstacle\")", StringComparison.Ordinal) &&
            battleMapEditorCode.Contains("SetScalarToZero(dynamicTile, \"curio_prop\")", StringComparison.Ordinal) &&
            battleMapEditorCode.Contains("SetScalarToZero(dynamicTile, \"trap\")", StringComparison.Ordinal) &&
            battleMapEditorCode.Contains("area.Tiles.Count - 1 - physicalOrdinal", StringComparison.Ordinal) &&
            battleMapEditorCode.Contains("ValidateStationaryRaidState", StringComparison.Ordinal) &&
            battleMapEditorCode.Contains("BattleMapSaveEditor_037", StringComparison.Ordinal) &&
            battleMapEditorCode.Contains("BattleMapSaveEditor_001", StringComparison.Ordinal) &&
            battleMapEditServiceCode.Contains("EnsureGameIsNotRunning", StringComparison.Ordinal) &&
            battleMapEditServiceCode.Contains("BattleMapEditService_030", StringComparison.Ordinal) &&
            battleMapEditServiceCode.Contains("ValidateLivePair", StringComparison.Ordinal) &&
            battleMapEditServiceCode.Contains("CreateBackup", StringComparison.Ordinal) &&
            battleMapEditServiceCode.Contains("GuardedSaveReplacement", StringComparison.Ordinal) &&
            battleMapEditServiceCode.Contains("BattleEncounterCatalog.ValidateDirectEncounter", StringComparison.Ordinal) &&
            forceTownSaveServiceCode.Contains("baseRoot[\"inraid\"] = false", StringComparison.Ordinal) &&
            forceTownSaveServiceCode.Contains("baseRoot[\"raiddungeon\"] = \"none\"", StringComparison.Ordinal) &&
            forceTownSaveServiceCode.Contains("ValidateLiveState", StringComparison.Ordinal) &&
            forceTownSaveServiceCode.Contains("EnsureGameIsNotRunning", StringComparison.Ordinal) &&
            forceTownSaveServiceCode.Contains("CreateBackup", StringComparison.Ordinal) &&
            forceTownSaveServiceCode.Contains("RestoreTarget", StringComparison.Ordinal) &&
            forceTownSaveServiceCode.Contains("destinationBackupFileName: displacedTarget", StringComparison.Ordinal) &&
            forceTownSaveServiceCode.Contains("RestoreFileVersion", StringComparison.Ordinal) &&
            forceTownSaveServiceCode.Contains("FileShare.Read", StringComparison.Ordinal) &&
            !forceTownSaveServiceCode.Contains("Process.Start", StringComparison.Ordinal) &&
            !forceTownSaveServiceCode.Contains("-forcetown", StringComparison.Ordinal),
            "The battle workspace must expose a pannable, zoomable real save map rendered from original sprites, keep system/script cells out of ordinary content editing, retain a native PART_Popup/ItemsPresenter menu chain for stable nested hover navigation, limit interaction to rooms and visible corridor tiles in native map orientation, retain previews for unresolved content, route proven battle/delete/move actions through guarded save transactions, and expose a backed-up two-field force-town save repair without launching the game.");
        var forceTownViewCode = File.ReadAllText(Path.Combine(
            appSourceDirectory, "BattleMapView.ForceTown.cs"));
        var forceTownFinally = forceTownViewCode[
            forceTownViewCode.LastIndexOf("finally", StringComparison.Ordinal)..];
        Assert(
            System.Text.RegularExpressions.Regex.IsMatch(
                forceTownFinally,
                @"_isApplyingEdit = false;\s*MapCanvas.IsHitTestVisible = true;\s*SaveEditBusyChanged\?\.Invoke\(false\);") &&
            !forceTownFinally.Contains("if (!committed)", StringComparison.Ordinal) &&
            forceTownFinally.Contains("ForceTownButton.IsEnabled = _currentSnapshot is not null;", StringComparison.Ordinal),
            "Force-town completion must unconditionally release the map input lock before notifying the owner, on success as well as failure; the button must follow current snapshot availability.");
        var battleMapLifecycleCode = File.ReadAllText(Path.Combine(
            appSourceDirectory, "BattleMapView.ProfileLifecycle.cs"));
        var showMapSurfaceCode = battleMapLifecycleCode[
            battleMapLifecycleCode.IndexOf("private void ShowMapSurface()", StringComparison.Ordinal)..battleMapLifecycleCode.IndexOf("private void ShowUnavailableState", StringComparison.Ordinal)];
        Assert(
            showMapSurfaceCode.Contains("MapCanvas.IsHitTestVisible = !_isApplyingEdit;", StringComparison.Ordinal),
            "Showing a new or reloaded map must clear a stale input lock while preserving the lock during an active save edit.");
        var battleEncounterDialogNames = battleEncounterDialog
            .Descendants()
            .Select(element => element.Attribute(xamlName)?.Value)
            .Where(value => value is not null)
            .ToHashSet(StringComparer.Ordinal);
        Assert(
            battleEncounterDialogNames.Contains("SearchTextBox") &&
            battleEncounterDialogNames.Contains("DifficultyComboBox") &&
            battleEncounterDialogNames.Contains("EncounterGrid") &&
            battleEncounterDialogNames.Contains("ConfirmButton") &&
            battleEncounterDialog.Descendants()
                .Any(element => element.Name.LocalName == "TextBlock" &&
                                ((string?)element.Attribute("Text"))?.Contains(
                                    "PreferredComposition",
                                    StringComparison.Ordinal) == true) &&
            !battleEncounterDialog.Descendants()
                .Any(element => (string?)element.Attribute("Header") == "游荡 ID") &&
            !battleEncounterDialog.Descendants()
                .Any(element => element.Name.LocalName == "DataGridTextColumn" &&
                                (string?)element.Attribute("Header") == "难度") &&
            battleEncounterDialogCode.Contains("CollectionViewSource.GetDefaultView", StringComparison.Ordinal) &&
            battleEncounterDialogCode.Contains("choice.Value == currentDifficulty", StringComparison.Ordinal) &&
            battleEncounterDialogCode.Contains("row.Definition.OriginDifficulty != selectedDifficulty.Value", StringComparison.Ordinal) &&
            battleEncounterDialogCode.Contains("row.SearchText.Contains", StringComparison.Ordinal) &&
            battleEncounterDialogCode.Contains("BattleEncounterClassification.FixedBoss", StringComparison.Ordinal) &&
            battleEncounterDialogCode.Contains("BattleEncounterClassification.RoamingBoss", StringComparison.Ordinal) &&
            battleEncounterDialogCode.Contains("BattleEncounterClassification.RoamingEncounter", StringComparison.Ordinal) &&
            battleEncounterDialogCode.Contains("BattleEncounterClassification.ConditionalOrAdditional", StringComparison.Ordinal) &&
            battleEncounterDialogCode.Contains("NativeWindowTheme.ApplyDarkTitleBar", StringComparison.Ordinal) &&
            battleEncounterDialogCode.Contains("DialogResult = true", StringComparison.Ordinal),
            "The global enabled-content encounter catalog must open in a themed picker, default to the current dungeon difficulty in a separate selector, search only that difficulty without repeating it in every row, and return one explicit complete composition for direct or managed placement.");
        var battleAttachmentDialogNames = battleAttachmentDialog
            .Descendants()
            .Select(element => element.Attribute(xamlName)?.Value)
            .Where(value => value is not null)
            .ToHashSet(StringComparer.Ordinal);
        Assert(
            battleAttachmentDialogNames.Contains("SearchTextBox") &&
            battleAttachmentDialogNames.Contains("AttachmentGrid") &&
            battleAttachmentDialogNames.Contains("ConfirmButton") &&
            battleAttachmentDialog.Descendants()
                .Any(element => (string?)element.Attribute("Header") == "中文名") &&
            battleAttachmentDialog.Descendants()
                .Any(element => (string?)element.Attribute("Header") == "English") &&
            battleAttachmentDialog.Descendants()
                .Any(element => (string?)element.Attribute("Header") == "ID") &&
            battleAttachmentDialog.Descendants()
                .Any(element => (string?)element.Attribute("Header") == "来源") &&
            battleAttachmentDialogCode.Contains(
                "CollectionViewSource.GetDefaultView",
                StringComparison.Ordinal) &&
            battleAttachmentDialogCode.Contains(
                "row.SearchText.Contains",
                StringComparison.Ordinal) &&
            battleAttachmentDialogCode.Contains(
                "NativeWindowTheme.ApplyDarkTitleBar",
                StringComparison.Ordinal) &&
            battleAttachmentDialogCode.Contains("DialogResult = true", StringComparison.Ordinal),
            "Room battle attachments must use a themed bilingual searchable picker and return one exact curio or treasure definition without exposing save hashes to the user.");
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
            battleMapDesign.Contains("Both paths preserve persisted knowledge, topology and quest identifiers", StringComparison.Ordinal) &&
            battleMapDesign.Contains("does not read game process memory", StringComparison.OrdinalIgnoreCase) &&
            battleMapDesign.Contains("guarded current-table battle placement", StringComparison.OrdinalIgnoreCase) &&
            battleMapDesign.Contains("Deterministic Encounter Bridge probe", StringComparison.Ordinal) &&
            battleMapDesign.Contains("does not require repeatedly starting expeditions", StringComparison.Ordinal) &&
            battleMapDesign.Contains("direct save-based force-town recovery", StringComparison.OrdinalIgnoreCase) &&
            battleMapDesign.Contains("base_root.inraid", StringComparison.Ordinal) &&
            battleMapDesign.Contains("base_root.raiddungeon", StringComparison.Ordinal) &&
            battleMapDesign.Contains("full-profile backup", StringComparison.OrdinalIgnoreCase),
            "The battle tab must hide catalog-only controls, show and refresh a real map only for a loaded raid, and retain the approved global-vision, arbitrary-boss, normal-tile-only, native-orientation, delete, movement, and direct save-based force-town decisions in durable documentation.");
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
            mainWindowCode.Contains("MainWindow_EditWorkflow_014", StringComparison.Ordinal),
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
        var initialQuirkDialogXaml = LoadLocalizedContractXaml(Path.Combine(
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
            .Where(control => control.Attribute("Content")?.Value == "怪癖目录")
            .Select(control => control.Ancestors(presentationNamespace + "Border").First())
            .ToArray();
        Assert(
            initialQuirkDialogXaml.Root?.Attribute("FontFamily")?.Value == "{x:Static local:EditorTypography.Body}" &&
            initialQuirkDialogXaml.Root?.Attribute("Icon")?.Value == "Assets/save-editor.ico" &&
            initialQuirkDialogXaml.Root?.Attribute("Width")?.Value == "1220" &&
            initialQuirkDialogXaml.Root?.Attribute("MinWidth")?.Value == "900" &&
            initialQuirkDialogXaml.Root?.Attribute("MinHeight")?.Value == "560" &&
            HasGamePanelWindowFrame(initialQuirkDialogXaml) &&
            HasOnlySquareCorners(appXaml, mainWindowXaml, initialQuirkDialogXaml, themedDialogXaml) &&
            mainWindowXaml.Descendants(presentationNamespace + "ContentControl")
                .Count(element => element.Attribute("Style")?.Value.Contains("PanelHeaderStyle", StringComparison.Ordinal) == true) >= 3 &&
            initialQuirkDialogXaml.Descendants(presentationNamespace + "ContentControl")
                .Count(element => element.Attribute("Style")?.Value.Contains("PanelHeaderStyle", StringComparison.Ordinal) == true) >= 1 &&
            initialQuirkDialogXaml.Descendants(presentationNamespace + "Border")
                .Count(border => border.Attribute("Style")?.Value == "{StaticResource OpenPanelStyle}") >= 1 &&
            initialQuirkOpenPanels.Length == 1 &&
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
            initialQuirkDialogCode.Contains(
                "CompactRowReason(definition.WriteStatusReason)",
                StringComparison.Ordinal) &&
            initialQuirkDialogCode.Contains(
                "private static string CompactSingletonReason => EditorText.Get(\"InitialQuirkSelectionDialog_002\")",
                StringComparison.Ordinal) &&
            unknownHpDisplayIndex >= 0 &&
            knownHpDisplayIndex > unknownHpDisplayIndex &&
            initialQuirkDialogCode.Contains("? EditorText.Get(\"InitialQuirkSelectionDialog_015\")", StringComparison.Ordinal) &&
                initialQuirkDialogCode.Contains("? EditorText.Get(\"InitialQuirkSelectionDialog_016\")", StringComparison.Ordinal) &&
                initialQuirkDialogCode.Contains("FormatMaxHpModifier", StringComparison.Ordinal) &&
                initialQuirkDialogCode.Contains("InitialQuirkSelectionDialog_025", StringComparison.Ordinal) &&
                initialQuirkDialogCode.Contains("_resolveLevel", StringComparison.Ordinal) &&
                initialQuirkDialogCode.Contains("if (requestedValue)", StringComparison.Ordinal) &&
                !initialQuirkDialogCode.Contains("row.IsSelected = !requestedValue", StringComparison.Ordinal) &&
                mainWindowCode.Contains("GetSelectedHeroLevel(),", StringComparison.Ordinal),
            "The initial quirk dialog must fully show fixed/special kind text, distinguish absent from unverified HP modifiers, keep context-limit explanations visible, and preserve level-aware selection/removal. Combination availability is exercised by QuirkSelectionInteractionContractTests.");
        var initialQuirkGrid = initialQuirkDialogXaml
            .Descendants(presentationNamespace + "DataGrid")
            .Single(grid => grid.Attribute(xamlName)?.Value == "QuirkGrid");
        var initialQuirkCheckBox = initialQuirkGrid
            .Descendants(presentationNamespace + "CheckBox")
            .Single(checkBox => checkBox.Attribute("Click")?.Value == "QuirkCheckBox_Click");
        Assert(
            initialQuirkCheckBox.Attribute("ToolTip") is null &&
            initialQuirkCheckBox.Attribute("ToolTipService.ShowOnDisabled") is null &&
            initialQuirkCheckBox.Attribute("IsEnabled")?.Value == "{Binding IsSelectable}" &&
            initialQuirkGrid.Descendants(presentationNamespace + "DataGridTextColumn").Any(column =>
                column.Attribute("Header")?.Value == "限制 / 不可用原因" &&
                column.Attribute("Binding")?.Value == "{Binding UnavailableReason}"),
            "Quirk checkboxes must not show hover tooltips; selection guards and the unavailable-reason column must remain intact.");
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

        RunLoggingUiContracts(repositoryRoot);
        RunUiLayoutContracts(repositoryRoot);
    }
}
