using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace DarkestDungeonSaveEditor.App;

public partial class MainWindow : Window
{
    private void RefreshCatalogRowsPreservingInput(bool sceneChanged, bool contentChanged)
    {
        var itemKey = (ItemGrid.SelectedItem as ItemRow)?.Definition.CatalogKey;
        var trinketId = (TrinketGrid.SelectedItem as TrinketRow)?.Id;
        var heroId = (HeroGrid.SelectedItem as HeroRow)?.Id;
        var level = (HeroLevelComboBox.SelectedItem as HeroLevelChoice)?.ResolveLevel;
        var quirks = _selectedInitialQuirkIds;
        var grids = contentChanged ? new[] { ItemGrid, TrinketGrid, HeroGrid } : new[] { ItemGrid };
        var scrolls = grids.Select(grid => FindSyncScrollViewer(grid))
            .Where(scroll => scroll is not null)
            .Select(scroll => (Scroll: scroll!, Vertical: scroll!.VerticalOffset, Horizontal: scroll.HorizontalOffset))
            .ToArray();
        _restoringCatalogSelection = true;
        try
        {
            ApplyFilter(itemsOnly: !contentChanged);
            ItemGrid.SelectedItem = sceneChanged ? null : _visibleItems.FirstOrDefault(row => row.Definition.CatalogKey == itemKey);
            if (contentChanged)
            {
                TrinketGrid.SelectedItem = _visibleTrinkets.FirstOrDefault(row => row.Id == trinketId);
                HeroGrid.SelectedItem = _visibleHeroes.FirstOrDefault(row => row.Id == heroId);
                if (_heroCatalog is not null)
                {
                    PopulateHeroLevels(_heroCatalog);
                    HeroLevelComboBox.SelectedItem = _heroLevelChoices.FirstOrDefault(choice => choice.ResolveLevel == level)
                        ?? _heroLevelChoices.FirstOrDefault();
                    _selectedInitialQuirkIds = HeroGrid.SelectedItem is null ? [] : quirks
                        .Where(id => _heroCatalog.InitialQuirks.Any(quirk => quirk.Id.Equals(id, StringComparison.Ordinal)))
                        .ToArray();
                }
            }
            // Even overlapping town/raid items are distinct operations. Do not carry a town target into a backpack.
            if (sceneChanged && CatalogTabs.SelectedIndex == 0) CopiesTextBox.Text = "0";
        }
        finally
        {
            _restoringCatalogSelection = false;
        }
        UpdateCatalogMode();
        var generation = _catalogGeneration;
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (generation != _catalogGeneration) return;
            foreach (var (scroll, vertical, horizontal) in scrolls)
            {
                scroll.ScrollToVerticalOffset(vertical);
                scroll.ScrollToHorizontalOffset(horizontal);
            }
        }));
    }

    private static ScrollViewer? FindSyncScrollViewer(DependencyObject parent)
    {
        if (parent is ScrollViewer viewer) return viewer;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            if (FindSyncScrollViewer(VisualTreeHelper.GetChild(parent, index)) is { } child) return child;
        }
        return null;
    }
}
