using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using DarkestDungeonSaveEditor.Core;

namespace DarkestDungeonSaveEditor.App;

public partial class QuestBoardPreviewDialog : Window
{
    private sealed record Option(int Value, string Label);
    internal sealed record Row(QuestBoardChoice Choice, FrameworkElement Icon)
    {
        public string Name => Choice.Name;
        public string Difficulty => QuestBoardPreviewData.Difficulty(Choice.Difficulty);
        public string Length => QuestBoardPreviewData.Length(Choice.Length);
    }

    private readonly Row[] _rows;
    private readonly ICollectionView _view;

    internal QuestBoardPreviewDialog(string regionId, QuestBoardCategory category, QuestBoardChoice? previous, QuestBoardPreviewAssets assets)
        : this(QuestBoardPreviewData.Regions.First(x => x.Id == regionId).Name, QuestBoardPreviewData.CategoryName(category),
            QuestBoardPreviewData.Choices(regionId).Where(choice => choice.Category == category).ToArray(), previous, assets, true) { }

    internal QuestBoardPreviewDialog(string regionName, string categoryName, IReadOnlyList<QuestBoardChoice> choices,
        QuestBoardChoice? previous, QuestBoardPreviewAssets assets, bool fixtureMode = false)
    {
        InitializeComponent();
        Heading.Text = EditorText.Format(previous is null ? "QuestBoard_AddInRegion" : "QuestBoard_ReplaceInRegion", regionName);
        CategoryText.Text = fixtureMode ? $"{categoryName} · {EditorText.Get("QuestBoard_PreviewLabel")}" : categoryName;
        if (!fixtureMode) ConfirmButton.Content = EditorText.Get("QuestBoard_PreviewEdit");
        _rows = choices
            .Select(choice => new Row(choice, assets.QuestIcon(choice, 48))).ToArray();
        _view = CollectionViewSource.GetDefaultView(_rows);
        _view.Filter = MatchesFilter;
        ChoiceGrid.ItemsSource = _view;
        var levels = _rows.Select(row => row.Choice.Difficulty).Distinct().Order().ToArray();
        DifficultyChoice.ItemsSource = levels.Select(value => new Option(value, QuestBoardPreviewData.Difficulty(value))).ToArray();
        DifficultyChoice.SelectedValue = previous is not null && levels.Contains(previous.Difficulty) ? previous.Difficulty : levels.FirstOrDefault();
        RefreshFilter();
        Loaded += (_, _) => SearchTextBox.Focus();
    }

    internal QuestBoardChoice? SelectedChoice { get; private set; }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        NativeWindowTheme.ApplyDarkTitleBar(this);
    }

    private void ChoiceGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ConfirmButton is null || SelectionTextBlock is null) return;
        var row = ChoiceGrid.SelectedItem as Row;
        ConfirmButton.IsEnabled = row is not null && row.Choice.UnavailableReason.Length == 0;
        SelectionTextBlock.Text = row is null ? EditorText.Get("QuestBoard_SelectionHint")
            : row.Choice.UnavailableReason.Length > 0 ? row.Choice.UnavailableReason : $"{row.Name} · {row.Difficulty} · {row.Length}";
        SelectionTextBlock.ToolTip = SelectionTextBlock.Text;
    }

    private bool MatchesFilter(object item) => item is Row row &&
        DifficultyChoice.SelectedValue is int level && row.Choice.Difficulty == level &&
        $"{row.Name} {row.Choice.Id} {row.Length}".Contains(SearchTextBox.Text.Trim(), StringComparison.OrdinalIgnoreCase);

    private void RefreshFilter()
    {
        if (_view is null) return;
        ChoiceGrid.SelectedItem = null;
        _view.Refresh();
        CountTextBlock.Text = EditorText.Format("QuestBoard_Count", _view.Cast<Row>().Count(),
            _rows.Count(row => row.Choice.Difficulty == (int?)DifficultyChoice.SelectedValue));
    }

    private void DifficultyChoice_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshFilter();
    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshFilter();

    private void ChoiceGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && e.OriginalSource is DependencyObject element &&
            ItemsControl.ContainerFromElement(ChoiceGrid, element) is DataGridRow)
            Confirm_Click(sender, e);
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (ChoiceGrid.SelectedItem is not Row row || !MatchesFilter(row) || row.Choice.UnavailableReason.Length > 0) return;
        SelectedChoice = row.Choice;
        DialogResult = true;
    }
}
