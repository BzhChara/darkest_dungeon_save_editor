using System.Xml.Linq;

internal static partial class ContractSuite
{
    private static void RunUiLayoutContracts(string repositoryRoot)
    {
        var appDirectory = Path.Combine(repositoryRoot, "src", "DarkestDungeonSaveEditor.App");
        var mainWindow = XDocument.Load(Path.Combine(appDirectory, "MainWindow.xaml"));
        var quirkDialog = XDocument.Load(Path.Combine(appDirectory, "InitialQuirkSelectionDialog.xaml"));
        var app = XDocument.Load(Path.Combine(appDirectory, "App.xaml"));
        var interaction = File.ReadAllText(Path.Combine(appDirectory, "MainWindow.CatalogInteraction.cs"));
        var stateCode = File.ReadAllText(Path.Combine(appDirectory, "MainWindow.State.cs"));
        foreach (var section in new[] { "filteredTrinkets", "filteredHeroes" })
        {
            var predicateStart = interaction.IndexOf("var " + section, StringComparison.Ordinal);
            var predicateEnd = interaction.IndexOf(";", predicateStart, StringComparison.Ordinal);
            var predicate = interaction[predicateStart..predicateEnd];
            Assert(predicate.Contains("definition.Source.Contains(keyword, StringComparison.OrdinalIgnoreCase)", StringComparison.Ordinal) &&
                   predicate.Contains("definition.SourceLabel.Contains(keyword, StringComparison.OrdinalIgnoreCase)", StringComparison.Ordinal),
                "Hero and trinket search must match both internal and displayed provenance labels.");
        }
        Assert(stateCode.Contains("SelectedHeroGenerationAvailability is { CanGenerate: true }", StringComparison.Ordinal) &&
               stateCode.Contains("PreviewWarningTextBlock.Text = availability.UnavailableReason", StringComparison.Ordinal) &&
               !stateCode.Contains("currentlyInRaid != expectedInRaid", StringComparison.Ordinal),
            "UI guards must use generation preflight and the game-state-guarded quantity scene, not infer town/raid from residue files.");
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        XElement Named(XDocument document, string name) => document.Descendants()
            .Single(element => element.Attribute(xaml + "Name")?.Value == name);

        var loadButton = Named(mainWindow, "LoadCatalogButton");
        var paths = Named(mainWindow, "PathInputsScrollViewer");
        var pathRows = paths.Parent!.Element(presentation + "Grid.RowDefinitions")!
            .Elements(presentation + "RowDefinition").ToArray();
        Assert(
            loadButton.Parent == paths.Parent &&
            loadButton.Attribute("Grid.Row")?.Value == "2" &&
            loadButton.Attribute("Click")?.Value == "LoadCatalog_Click" &&
            !loadButton.Ancestors(presentation + "ScrollViewer").Any() &&
            paths.Attribute("Grid.Row")?.Value == "1" &&
            paths.Attribute("VerticalScrollBarVisibility")?.Value == "Auto" &&
            pathRows.Length == 3 &&
            pathRows[1].Attribute("Height")?.Value == "*" &&
            pathRows[2].Attribute("Height")?.Value == "Auto" &&
            paths.Descendants(presentation + "TextBox").Count() == 4,
            "The load action must stay in a fixed auto-sized footer; only overflowing path inputs may scroll.");

        foreach (var (gridName, fields) in new[]
        {
            ("ItemGrid", new[] { "Location", "InventoryType", "StackSummary" }),
            ("TrinketGrid", new[] { "Rarity", "LimitDisplay" }),
            ("HeroGrid", new[] { "GenerationMode", "QuirkRange" })
        })
        {
            var grid = Named(mainWindow, gridName);
            var details = grid.Element(presentation + "DataGrid.RowDetailsTemplate");
            Assert(
                grid.Attribute("RowDetailsVisibilityMode")?.Value == "VisibleWhenSelected" &&
                grid.Attribute("AreRowDetailsFrozen")?.Value == "True" &&
                grid.Attribute("VirtualizingPanel.ScrollUnit")?.Value == "Pixel" &&
                fields.All(field => details?.Descendants(presentation + "Run")
                    .Any(run => run.Attribute("Text")?.Value == $"{{Binding {field}, Mode=OneWay}}") == true) &&
                !grid.Descendants(presentation + "DataGridTextColumn")
                    .Any(column => fields.Any(field => column.Attribute("Binding")?.Value == $"{{Binding {field}}}")),
                $"{gridName} must preserve secondary information in selected-row details and allow pixel scrolling when a detailed row exceeds the viewport height.");
        }

        var itemDetails = Named(mainWindow, "ItemGrid")
            .Element(presentation + "DataGrid.RowDetailsTemplate")!;
        var itemDetailFields = itemDetails.Descendants(presentation + "WrapPanel")
            .Single().Elements(presentation + "TextBlock").ToArray();
        var itemRowCode = File.ReadAllText(Path.Combine(appDirectory, "MainWindow.RowModels.cs"));
        Assert(
            itemDetailFields.Length == 4 &&
            itemDetailFields.Take(3).Select(field => field.Elements(presentation + "Run")
                    .First().Attribute("Text")?.Value)
                .SequenceEqual(new[] { "位置：", "类型：", "堆叠：" }) &&
            itemDetailFields.All(field => field.Attribute("TextWrapping")?.Value == "Wrap") &&
            itemDetailFields[3].Attribute("Text")?.Value == "{Binding ProvisionSummary}" &&
            itemDetailFields[3].Descendants(presentation + "Trigger")
                .Any(trigger => trigger.Attribute("Property")?.Value == "Text" &&
                                trigger.Attribute("Value")?.Value == "" &&
                                trigger.Elements(presentation + "Setter").Any(setter =>
                                    setter.Attribute("Property")?.Value == "Visibility" &&
                                    setter.Attribute("Value")?.Value == "Collapsed")) &&
            itemRowCode.Contains("Location => FormatItemStorage(Definition.StorageKind)", StringComparison.Ordinal) &&
            itemRowCode.Contains("InventoryType => Definition.InventoryType", StringComparison.Ordinal) &&
            itemRowCode.Contains("StackSummary => Definition.StorageKind != QuantityItemStorageKind.RaidInventory", StringComparison.Ordinal) &&
            itemRowCode.Contains("Definition.BaseStackLimit is > 0", StringComparison.Ordinal) &&
            itemRowCode.Contains("Definition.BaseStackLimit.Value.ToString(CultureInfo.InvariantCulture)", StringComparison.Ordinal) &&
            itemRowCode.Contains("? \"不适用\"", StringComparison.Ordinal) &&
            itemRowCode.Contains(": \"未知\"", StringComparison.Ordinal) &&
            itemRowCode.Contains("ProvisionSummary => Definition.StorageKind != QuantityItemStorageKind.EstateItems", StringComparison.Ordinal) &&
            new[] { "配给：可手动配给", "配给：不可手动配给", "配给：未声明" }
                .All(value => itemRowCode.Contains(value, StringComparison.Ordinal)),
            "Item details must separate location/type/stack into wrapping fields, distinguish non-stackable town counts from unknown raid limits, and retain estate provisioning information.");

        var quirkGrid = Named(quirkDialog, "QuirkGrid");
        var validation = Named(quirkDialog, "ValidationTextBlock");
        var quirkRows = quirkGrid.Parent!.Element(presentation + "Grid.RowDefinitions")!
            .Elements(presentation + "RowDefinition").ToArray();
        Assert(
            !quirkDialog.Descendants(presentation + "Expander").Any() &&
            !quirkDialog.ToString().Contains("选择规则", StringComparison.Ordinal) &&
            !quirkDialog.ToString().Contains("roster_limit", StringComparison.Ordinal) &&
            !app.Descendants(presentation + "Style").Any(style =>
                style.Attribute(xaml + "Key")?.Value is "DisclosureHeaderStyle" or "DisclosureExpanderStyle") &&
            quirkRows.Select(row => row.Attribute("Height")?.Value)
                .SequenceEqual(new[] { "Auto", "*", "Auto", "Auto" }) &&
            quirkGrid.Attribute("Grid.Row")?.Value == "1" &&
            validation.Attribute("Grid.Row")?.Value == "2" &&
            quirkGrid.Parent.Elements(presentation + "Grid")
                .Any(grid => grid.Attribute("Grid.Row")?.Value == "3" &&
                             grid.Descendants(presentation + "Button")
                                 .Any(button => button.Attribute("Click")?.Value == "Ok_Click")) &&
            Named(quirkDialog, "SelectionSummaryTextBlock")
                .Ancestors(presentation + "Border").First().Attribute("Grid.Row")?.Value == "0" &&
            validation.Attribute("Visibility") is null &&
            validation.Descendants(presentation + "Trigger")
                .Any(trigger => trigger.Attribute("Property")?.Value == "Text" &&
                                trigger.Attribute("Value")?.Value == "" &&
                                trigger.Elements(presentation + "Setter")
                                    .Any(setter => setter.Attribute("Property")?.Value == "Visibility" &&
                                                   setter.Attribute("Value")?.Value == "Collapsed")),
            "Static quirk rules and their unused styles must be removed without leaving an empty row; quota, live validation, and confirmation controls must remain.");

        var checkBoxStyle = app.Descendants(presentation + "Style")
            .Single(style => style.Attribute("TargetType")?.Value == "CheckBox");
        var tabItemStyle = app.Descendants(presentation + "Style")
            .Single(style => style.Attribute("TargetType")?.Value == "TabItem");
        Assert(
            tabItemStyle.Descendants(presentation + "Viewbox")
                .Any(viewbox => viewbox.Attribute("Stretch")?.Value == "Uniform" &&
                                viewbox.Attribute("StretchDirection")?.Value == "DownOnly" &&
                                viewbox.Descendants(presentation + "ContentPresenter")
                                    .Any(presenter => presenter.Attribute("ContentSource")?.Value == "Header")),
            "Catalog tab labels must scale down only when needed instead of clipping at the supported minimum width.");
        Assert(
            checkBoxStyle.Descendants(presentation + "ControlTemplate").Any() &&
            checkBoxStyle.Descendants(presentation + "Border")
                .Any(border => border.Attribute(xaml + "Name")?.Value == "CheckBoxChrome" &&
                               border.Attribute("Width")?.Value == "18" && border.Attribute("Height")?.Value == "18") &&
            new[] { "IsChecked", "IsEnabled", "IsKeyboardFocused", "IsMouseOver" }
                .All(property => checkBoxStyle.Descendants(presentation + "Trigger")
                    .Any(trigger => trigger.Attribute("Property")?.Value == property)) &&
            !checkBoxStyle.Descendants(presentation + "ScaleTransform").Any(),
            "The themed checkbox must keep fixed geometry and checked/disabled/keyboard states.");
    }
}
