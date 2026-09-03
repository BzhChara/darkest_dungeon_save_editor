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
    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void ShowUnusedItemsCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        InvalidatePreparedEdit();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var keyword = SearchTextBox.Text.Trim();
        var showHiddenItemsOnly = ShowUnusedItemsCheckBox.IsChecked == true;
        var filteredItems = _allItems.Where(definition =>
            definition.IsHiddenByDefault == showHiddenItemsOnly &&
            (string.IsNullOrWhiteSpace(keyword) ||
             definition.DisplayId.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
             definition.InventoryType.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
             definition.LocalizedName.Chinese.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
             definition.LocalizedName.English.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
             definition.Source.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
             definition.SourceLabel.Contains(keyword, StringComparison.OrdinalIgnoreCase)));

        _visibleItems.Clear();
        foreach (var definition in filteredItems)
        {
            _visibleItems.Add(new ItemRow(definition));
        }

        var filteredTrinkets = _allTrinkets.Where(definition =>
            string.IsNullOrWhiteSpace(keyword) ||
            definition.Id.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
            definition.LocalizedName.Chinese.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
            definition.LocalizedName.English.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
            definition.Rarity.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
            definition.Source.Contains(keyword, StringComparison.OrdinalIgnoreCase));

        _visibleTrinkets.Clear();
        foreach (var definition in filteredTrinkets)
        {
            _visibleTrinkets.Add(new TrinketRow(definition));
        }

        var filteredHeroes = _allHeroes.Where(definition =>
            string.IsNullOrWhiteSpace(keyword) ||
            definition.Id.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
            definition.LocalizedName.Chinese.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
            definition.LocalizedName.English.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
            definition.Source.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
            definition.RecruitEvents.Any(item => item.Id.Contains(keyword, StringComparison.OrdinalIgnoreCase)) ||
            definition.RuntimeQuirkSignals.Any(item => item.QuirkId.Contains(keyword, StringComparison.OrdinalIgnoreCase)));

        _visibleHeroes.Clear();
        foreach (var definition in filteredHeroes)
        {
            _visibleHeroes.Add(new HeroRow(definition));
        }
    }

    private void ItemGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        InvalidatePreparedEdit();
        if (CatalogTabs.SelectedIndex == 0 && ItemGrid.SelectedItem is ItemRow selected)
        {
            CopiesTextBox.Text = selected.CurrentAmount.ToString(CultureInfo.InvariantCulture);
        }
    }

    private void TrinketGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        InvalidatePreparedEdit();
        if (CatalogTabs.SelectedIndex == 1 && TrinketGrid.SelectedItem is TrinketRow)
        {
            CopiesTextBox.Text = "1";
        }
    }

    private void CopiesTextBox_TextChanged(object sender, TextChangedEventArgs e) => InvalidatePreparedEdit();

    private void SelectAllTextOnFirstClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TextBox textBox || textBox.IsKeyboardFocusWithin)
        {
            return;
        }

        e.Handled = true;
        _ = textBox.Focus();
        textBox.SelectAll();
    }

    private void SelectAllTextOnKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            textBox.SelectAll();
        }
    }

    private void HeroGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        InvalidatePreparedEdit();
        _selectedInitialQuirkIds = [];
        UpdateInitialQuirkSelectionSummary();
        UpdateCatalogMode();
    }

    private void HeroLevelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        InvalidatePreparedEdit();

    private void SelectInitialQuirks_Click(object sender, RoutedEventArgs e)
    {
        if (HeroGrid.SelectedItem is not HeroRow selectedHero || _heroCatalog is null)
        {
            AppendStatus("请先选择一个人物职业。");
            return;
        }

        InitialQuirkSelectionDialog? dialog = null;
        try
        {
            dialog = new InitialQuirkSelectionDialog(
                _heroCatalog,
                selectedHero.Definition,
                GetSelectedHeroLevel(),
                _selectedInitialQuirkIds)
            {
                Owner = this
            };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            var selectedIds = dialog.SelectedQuirkIds.ToArray();
            if (_selectedInitialQuirkIds.SequenceEqual(selectedIds, StringComparer.OrdinalIgnoreCase))
            {
                return;
            }

            _selectedInitialQuirkIds = selectedIds;
            InvalidatePreparedEdit();
            UpdateInitialQuirkSelectionSummary();
            AppendStatus($"已选择初始怪癖：{FormatSelectedQuirks(_selectedInitialQuirkIds)}。");
        }
        catch (Exception ex)
        {
            if (dialog?.IsVisible == true)
            {
                dialog.Close();
            }

            AppendStatus($"打开初始怪癖选择失败：{ex.Message}");
        }
    }

    private void CatalogTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, CatalogTabs))
        {
            return;
        }

        InvalidatePreparedEdit();
        UpdateCatalogMode();
        ResetQuantityInputForCurrentTab();
    }

    private void ResetQuantityInputForCurrentTab()
    {
        if (CopiesTextBox is null)
        {
            return;
        }

        CopiesTextBox.Text = CatalogTabs.SelectedIndex switch
        {
            0 when ItemGrid?.SelectedItem is ItemRow selected =>
                selected.CurrentAmount.ToString(CultureInfo.InvariantCulture),
            0 => "0",
            1 => "1",
            _ => CopiesTextBox.Text
        };
    }

}
