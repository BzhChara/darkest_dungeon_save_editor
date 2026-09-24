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
    private void InputPath_TextChanged(object sender, TextChangedEventArgs e) => InvalidateCatalog();

    private void InvalidateCatalog()
    {
        StopProfileSync();
        _battleMapLogTracker.Reset();
        InvalidatePreparedEdit();
        _catalogProfileDirectory = null;
        _catalogGameSaveSha256 = null;
        _catalogEstateSaveSha256 = null;
        _catalogQuantitySaveSha256 = null;
        _quantitySaveContext = QuantityItemSaveContext.Town;
        if (ItemTab is not null)
        {
            ItemTab.Header = EditorText.Get("MainWindow_State_001");
        }
        _allItems = [];
        _allTrinkets = [];
        _allHeroes = [];
        _selectedInitialQuirkIds = [];
        _heroCatalog = null;
        _trinketStorage = null;
        _raidInventoryStorage = null;
        _activeContentSnapshot = null;
        _heroLevelChoices.Clear();
        _visibleItems.Clear();
        _visibleTrinkets.Clear();
        _visibleHeroes.Clear();
        if (ItemGrid is not null)
        {
            ItemGrid.SelectedItem = null;
        }
        if (TrinketGrid is not null)
        {
            TrinketGrid.SelectedItem = null;
        }
        if (HeroGrid is not null)
        {
            HeroGrid.SelectedItem = null;
        }
        if (ShowUnusedItemsCheckBox is not null)
        {
            ShowUnusedItemsCheckBox.IsChecked = false;
            ShowUnusedItemsCheckBox.Content = EditorText.Get("MainWindow_State_002");
        }
        if (BattleMapPanel is not null)
        {
            BattleMapPanel.ClearProfile();
        }
        if (PreviewButton is not null)
        {
            PreviewButton.IsEnabled = false;
        }
        UpdateInitialQuirkSelectionSummary();
    }

    private void InvalidatePreparedEdit()
    {
        unchecked
        {
            _editRevision++;
        }
        _preparedQuantityItemEdit = null;
        _preparedTrinketEdit = null;
        _preparedHeroEdit = null;
        _preparedHeroCandidatePreview = null;
        _editService = null;
        if (ApplyButton is not null)
        {
            ApplyButton.IsEnabled = false;
        }
        if (PreviewSummaryTextBlock is not null)
        {
            PreviewSummaryTextBlock.Text = string.Empty;
        }
        if (PreviewWarningTextBlock is not null)
        {
            PreviewWarningTextBlock.Text = string.Empty;
            PreviewWarningTextBlock.Visibility = Visibility.Collapsed;
        }
        UpdateHeroGenerationAvailability();
        UpdateInventoryIdentityWarning();
    }

    private string SelectedInventoryIdentityIssue => CatalogTabs?.SelectedIndex switch
    {
        0 when ItemGrid?.SelectedItem is ItemRow row => row.Definition.SaveIdentityIssue,
        1 when TrinketGrid?.SelectedItem is TrinketRow row => row.Definition.SaveIdentityIssue,
        _ => string.Empty
    };

    private void UpdateInventoryIdentityWarning()
    {
        if (PreviewWarningTextBlock is not null && SelectedInventoryIdentityIssue is { Length: > 0 } issue)
        {
            PreviewWarningTextBlock.Text = issue;
            PreviewWarningTextBlock.Visibility = Visibility.Visible;
        }
    }

    private HeroGenerationAvailability? SelectedHeroGenerationAvailability =>
        HeroGrid?.SelectedItem is HeroRow hero && HeroLevelComboBox?.SelectedItem is HeroLevelChoice level
            ? hero.Definition.GenerationAvailability.SingleOrDefault(item => item.ResolveLevel == level.ResolveLevel)
            : null;

    private void UpdateHeroGenerationAvailability()
    {
        if (CatalogTabs?.SelectedIndex != 2 || PreviewButton is null || PreviewWarningTextBlock is null)
        {
            return;
        }

        var availability = SelectedHeroGenerationAvailability;
        PreviewButton.IsEnabled = CatalogTabs.IsEnabled && CanPreviewCurrentTab();
        if (availability is { CanGenerate: false })
        {
            PreviewWarningTextBlock.Text = availability.UnavailableReason;
            PreviewWarningTextBlock.Visibility = Visibility.Visible;
        }
    }

    private void SetBusy(bool busy)
    {
        if (busy) unchecked { _catalogOperationGeneration++; }
        _busyDepth = busy ? _busyDepth + 1 : Math.Max(0, _busyDepth - 1);
        UpdateEnabledState();
        if (!IsProfileOperationBusy && _syncRequested) _ = DrainProfileSyncAsync();
    }

    private void UpdateEnabledState()
    {
        if (CatalogTabs is null || BattleMapPanel is null) return;
        var busy = IsBusy;
        var isBattleTab = CatalogTabs.SelectedIndex == BattleTabIndex;
        PathInputsBorder.IsEnabled = !busy;
        CatalogTabs.IsEnabled = !busy;
        SearchTextBox.IsEnabled = !busy && !isBattleTab;
        BattleMapPanel.IsEnabled = !busy && _syncReady;
        ShowUnusedItemsCheckBox.IsEnabled = !busy &&
            CatalogTabs.SelectedIndex == 0 && _allItems.Count > 0;
        CopiesTextBox.IsEnabled = !busy && CatalogTabs.SelectedIndex is 0 or 1;
        HeroLevelComboBox.IsEnabled = !busy &&
            CatalogTabs.SelectedIndex == 2 &&
            _heroCatalog is not null &&
            HeroGrid.SelectedItem is HeroRow;
        InitialQuirksButton.IsEnabled = !busy &&
            CatalogTabs.SelectedIndex == 2 &&
            _heroCatalog is not null &&
            HeroGrid.SelectedItem is HeroRow;
        PreviewButton.IsEnabled = !busy && CanPreviewCurrentTab();
        UpdateInventoryIdentityWarning();
        ApplyButton.IsEnabled = !busy && _syncReady &&
            (_preparedQuantityItemEdit is not null ||
             _preparedTrinketEdit is not null ||
             _preparedHeroEdit is not null);
    }

    private void EnsureCatalogMatches(
        SaveProfile profile,
        bool requireCurrentQuantitySnapshot,
        bool requireCurrentEstateSnapshot)
    {
        if (_catalogProfileDirectory is null || _catalogGameSaveSha256 is null ||
            (_allItems.Count == 0 && _allTrinkets.Count == 0 && _heroCatalog is null))
        {
            throw new InvalidOperationException(EditorText.Get("MainWindow_State_003"));
        }

        if (!Path.GetFullPath(profile.ProfileDirectory)
                .Equals(Path.GetFullPath(_catalogProfileDirectory), StringComparison.OrdinalIgnoreCase))
        {
            InvalidateCatalog();
            throw new InvalidOperationException(EditorText.Get("MainWindow_State_004"));
        }

        profile = _activeContentSnapshot?.Profile ?? profile;
        var gameSavePath = Path.Combine(profile.ProfileDirectory, "persist.game.json");
        if (!_syncReady || _catalogFileHashes is null ||
            !ProfileCatalogSnapshotReader.HashesEqual(_catalogFileHashes,
                ProfileCatalogSnapshotReader.CaptureHashes(profile)))
        {
            RequestProfileSync();
            throw new InvalidOperationException(EditorText.Get("MainWindow_State_005"));
        }
        if (!File.Exists(gameSavePath) ||
            !ComputeSha256(gameSavePath).Equals(_catalogGameSaveSha256, StringComparison.OrdinalIgnoreCase))
        {
            RequestProfileSync();
            throw new InvalidOperationException(EditorText.Get("MainWindow_State_006"));
        }

        if (requireCurrentQuantitySnapshot)
        {
            var expectedInRaid = _quantitySaveContext == QuantityItemSaveContext.Raid;
            var quantitySavePath = expectedInRaid ? profile.RaidSavePath : profile.EstateSavePath;
            // The game-save hash above guards the scene resolved at load time.
            // A town profile may legitimately retain an old raid file after force-town.
            if (_catalogQuantitySaveSha256 is null ||
                !File.Exists(quantitySavePath) ||
                !ComputeSha256(quantitySavePath).Equals(
                    _catalogQuantitySaveSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                RequestProfileSync();
                throw new InvalidOperationException(
                    EditorText.Get("MainWindow_State_007"));
            }
        }

        if (requireCurrentEstateSnapshot &&
            (_catalogEstateSaveSha256 is null ||
             !File.Exists(profile.EstateSavePath) ||
             !ComputeSha256(profile.EstateSavePath).Equals(
                 _catalogEstateSaveSha256,
                 StringComparison.OrdinalIgnoreCase)))
        {
            RequestProfileSync();
            throw new InvalidOperationException(EditorText.Get("MainWindow_State_008"));
        }
    }

    private bool CanPreviewCurrentTab()
    {
        if (_catalogProfileDirectory is null || !_syncReady || IsBusy)
        {
            return false;
        }

        return CatalogTabs.SelectedIndex switch
        {
            0 => ItemGrid.SelectedItem is ItemRow && SelectedInventoryIdentityIssue.Length == 0,
            1 => TrinketGrid.SelectedItem is TrinketRow && SelectedInventoryIdentityIssue.Length == 0,
            2 => _heroCatalog is not null &&
                 SelectedHeroGenerationAvailability is { CanGenerate: true },
            _ => false
        };
    }

    private void UpdateCatalogMode()
    {
        if (PreviewButton is null || CopiesTextBox is null || CopiesLabel is null ||
            CatalogToolsPanel is null || CatalogActionPanel is null)
        {
            return;
        }

        var isItemTab = CatalogTabs.SelectedIndex == 0;
        var isHeroTab = CatalogTabs.SelectedIndex == 2;
        var isBattleTab = CatalogTabs.SelectedIndex == BattleTabIndex;
        CatalogToolsPanel.Visibility = isBattleTab ? Visibility.Collapsed : Visibility.Visible;
        CatalogActionPanel.Visibility = isBattleTab ? Visibility.Collapsed : Visibility.Visible;
        PreviewButton.Content = isHeroTab
            ? EditorText.Get("MainWindow_State_009")
            : isItemTab
                ? EditorText.Get("MainWindow_State_010")
                : EditorText.Get("MainWindow_State_011");
        CopiesLabel.Text = isItemTab ? EditorText.Get("MainWindow_State_012") : EditorText.Get("MainWindow_State_013");
        CopiesLabel.Visibility = isHeroTab || isBattleTab ? Visibility.Collapsed : Visibility.Visible;
        CopiesTextBox.Visibility = isHeroTab || isBattleTab ? Visibility.Collapsed : Visibility.Visible;
        HeroLevelLabel.Visibility = isHeroTab ? Visibility.Visible : Visibility.Collapsed;
        HeroLevelComboBox.Visibility = isHeroTab ? Visibility.Visible : Visibility.Collapsed;
        HeroLevelComboBox.IsEnabled = isHeroTab &&
            _heroCatalog is not null &&
            HeroGrid.SelectedItem is HeroRow;
        InitialQuirksButton.Visibility = isHeroTab ? Visibility.Visible : Visibility.Collapsed;
        ShowUnusedItemsCheckBox.Visibility = isItemTab ? Visibility.Visible : Visibility.Collapsed;
        ShowUnusedItemsCheckBox.IsEnabled = isItemTab && _allItems.Count > 0 && CatalogTabs.IsEnabled;
        ShowUnusedItemsCheckBox.Content =
            EditorText.Format("MainWindow_State_014", _allItems.Count(item => item.IsHiddenByDefault));
        InitialQuirkSelectionSummaryTextBlock.Visibility = isHeroTab ? Visibility.Visible : Visibility.Collapsed;
        InitialQuirksButton.IsEnabled = isHeroTab &&
            _heroCatalog is not null &&
            HeroGrid.SelectedItem is HeroRow;
        UpdateInitialQuirkSelectionSummary();
        UpdateEnabledState();
        UpdateHeroGenerationAvailability();
    }

}
