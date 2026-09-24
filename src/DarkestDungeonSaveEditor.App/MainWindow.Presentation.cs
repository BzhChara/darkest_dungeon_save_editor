using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DarkestDungeonSaveEditor.Core;
using Microsoft.Win32;

namespace DarkestDungeonSaveEditor.App;

public partial class MainWindow : Window
{
    private void PopulateHeroLevels(HeroClassCatalogResult catalog)
    {
        _heroLevelChoices.Clear();
        var thresholds = catalog.ResolveLevelThresholds.Count > 0
            ? catalog.ResolveLevelThresholds
            : new[] { 0 };
        for (var level = 0; level < thresholds.Count; level++)
        {
            _heroLevelChoices.Add(new HeroLevelChoice(level, thresholds[level]));
        }

        HeroLevelComboBox.SelectedIndex = _heroLevelChoices.Count > 0 ? 0 : -1;
    }

    private int GetSelectedHeroLevel()
    {
        return HeroLevelComboBox.SelectedItem is HeroLevelChoice choice
            ? choice.ResolveLevel
            : throw new InvalidOperationException(EditorText.Get("MainWindow_Presentation_001"));
    }

    private void UpdateInitialQuirkSelectionSummary()
    {
        if (InitialQuirksButton is null || InitialQuirkSelectionSummaryTextBlock is null)
        {
            return;
        }

        InitialQuirksButton.Content = EditorText.Format("MainWindow_Presentation_002", _selectedInitialQuirkIds.Count);
        if (HeroGrid is null || HeroGrid.SelectedItem is not HeroRow)
        {
            InitialQuirksButton.IsEnabled = false;
            InitialQuirkSelectionSummaryTextBlock.Text = EditorText.Get("MainWindow_Presentation_003");
            return;
        }

        InitialQuirksButton.IsEnabled = CatalogTabs is { IsEnabled: true, SelectedIndex: 2 } &&
            _heroCatalog is not null;

        var positiveCount = 0;
        var negativeCount = 0;
        var diseaseCount = 0;
        if (_heroCatalog is not null)
        {
            foreach (var id in _selectedInitialQuirkIds)
            {
                var definition = _heroCatalog.InitialQuirks.FirstOrDefault(
                    quirk => quirk.Id.Equals(id, StringComparison.Ordinal));
                diseaseCount += definition?.IsDisease == true ? 1 : 0;
                positiveCount += definition is { IsPositive: true } ? 1 : 0;
                negativeCount += definition is { IsDisease: false, IsPositive: false } ? 1 : 0;
            }
        }

        InitialQuirkSelectionSummaryTextBlock.Text =
            EditorText.Format("MainWindow_Presentation_004", positiveCount, negativeCount, diseaseCount) +
            FormatSelectedQuirks(_selectedInitialQuirkIds);
    }

    private static string FormatSelectedQuirks(StagecoachHeroCandidatePreview? preview)
    {
        return preview is null
            ? EditorText.Get("BattleEncounterSelectionDialog_017")
            : FormatSelectedQuirks(
                preview.PositiveQuirks.Concat(preview.NegativeQuirks).Concat(preview.Diseases));
    }

    private static string FormatSelectedQuirks(IEnumerable<string> quirkIds)
    {
        var ids = quirkIds.Distinct(StringComparer.Ordinal).ToArray();
        return ids.Length == 0 ? EditorText.Get("BattleMapView_CellVisuals_010") : string.Join(", ", ids);
    }

    private static string FormatHeroPreviewWarnings(StagecoachHeroMutationPreview preview)
    {
        var warnings = new List<string>();
        if (preview.MayRefreshOnTownReturn)
        {
            warnings.Add(EditorText.Get("MainWindow_Presentation_005"));
        }

        var quirkLimitWarnings = FormatHeroQuirkLimitWarnings(preview);
        if (!string.IsNullOrWhiteSpace(quirkLimitWarnings))
        {
            warnings.Add(quirkLimitWarnings);
        }

        return string.Join(Environment.NewLine, warnings);
    }

    private static string FormatHeroQuirkLimitWarnings(StagecoachHeroMutationPreview preview)
    {
        return string.Join(
            Environment.NewLine,
            preview.QuirkLimits
                .Where(limit => limit.ExceedsDefinitionLimit)
                .Select(limit =>
                    EditorText.Format("MainWindow_Presentation_006", limit.QuirkId, limit.ExistingRosterHeroes) +
                    EditorText.Format("MainWindow_Presentation_007", limit.ExistingStagecoachCandidates, limit.ResultingHeroes) +
                    EditorText.Format("MainWindow_Presentation_008", limit.DefinitionLimit)));
    }

    private static string FormatDefinitionLimit(int? limit)
    {
        return limit switch
        {
            0 => EditorText.Get("MainWindow_Presentation_009"),
            null => EditorText.Get("BattleEncounterSelectionDialog_017"),
            _ => limit.Value.ToString(CultureInfo.InvariantCulture)
        };
    }

    private static string FormatItemStorage(QuantityItemStorageKind storageKind) => storageKind switch
    {
        QuantityItemStorageKind.Wallet => EditorText.Get("MainWindow_Presentation_010"),
        QuantityItemStorageKind.EstateItems => EditorText.Get("MainWindow_Presentation_011"),
        QuantityItemStorageKind.RaidInventory => EditorText.Get("MainWindow_Presentation_012"),
        _ => storageKind.ToString()
    };

    private static string FormatItemStorage(QuantityItemDefinition definition)
    {
        if (definition.StorageKind == QuantityItemStorageKind.RaidInventory)
        {
            return definition.BaseStackLimit is > 0
                ? EditorText.Format("MainWindow_Presentation_013", definition.InventoryType) +
                  definition.BaseStackLimit.Value.ToString(CultureInfo.InvariantCulture)
                : EditorText.Format("MainWindow_Presentation_014", definition.InventoryType);
        }

        if (definition.StorageKind != QuantityItemStorageKind.EstateItems)
        {
            return FormatItemStorage(definition.StorageKind);
        }

        return definition.EstateCanBeProvision switch
        {
            true => EditorText.Get("MainWindow_Presentation_015"),
            false => EditorText.Get("MainWindow_Presentation_016"),
            null => EditorText.Get("MainWindow_Presentation_017")
        };
    }

    private static string FormatQuantityItemBoundaryWarning(QuantityItemDefinition definition)
    {
        if (definition.StorageKind == QuantityItemStorageKind.RaidInventory)
        {
            return definition.InventoryType.Equals("quest_item", StringComparison.Ordinal)
                ? EditorText.Get("MainWindow_Presentation_018")
                : string.Empty;
        }

        if (definition.StorageKind != QuantityItemStorageKind.EstateItems)
        {
            return string.Empty;
        }

        return definition.EstateCanBeProvision switch
        {
            false => EditorText.Get("MainWindow_Presentation_019"),
            null => EditorText.Get("MainWindow_Presentation_020"),
            true => string.Empty
        };
    }

    private static string FormatStorageCapacity(TrinketStorageDefinition? storage)
    {
        return storage is null
            ? EditorText.Get("BattleEncounterSelectionDialog_017")
            : $"{storage.MaxSlots.ToString(CultureInfo.InvariantCulture)}（{storage.ContentSource.DisplayName}）";
    }

    private static string FormatStorageCapacity(int? capacity)
    {
        return capacity?.ToString(CultureInfo.InvariantCulture) ?? EditorText.Get("BattleEncounterSelectionDialog_017");
    }

    private static string FormatRaidInventoryCapacity(RaidInventoryStorageDefinition? storage) =>
        storage?.MaxSlots.ToString(CultureInfo.InvariantCulture) ?? EditorText.Get("BattleEncounterSelectionDialog_017");

    private static string FormatQuantitySaveContext(QuantityItemSaveContext saveContext) =>
        saveContext == QuantityItemSaveContext.Raid ? EditorText.Get("MainWindow_Presentation_021") : EditorText.Get("MainWindow_Presentation_022");

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string RequireDirectory(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value) || !Directory.Exists(value.Trim()))
        {
            throw new DirectoryNotFoundException(EditorText.Format("MainWindow_Presentation_023", label, value));
        }

        return Path.GetFullPath(value.Trim());
    }

}
