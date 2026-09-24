using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using DarkestDungeonSaveEditor.Core;

namespace DarkestDungeonSaveEditor.App;

public partial class BattleRoomAttachmentSelectionDialog : Window
{
    private readonly ObservableCollection<AttachmentChoiceRow> _rows = [];
    private readonly string _kindLabel;
    private readonly bool _preserveBattle;
    private ICollectionView? _view;

    public BattleRoomAttachmentSelectionDialog(
        IReadOnlyCollection<BattleRoomAttachmentDefinition> definitions,
        string kindLabel,
        bool preserveBattle = true)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentException.ThrowIfNullOrWhiteSpace(kindLabel);
        _kindLabel = kindLabel;
        _preserveBattle = preserveBattle;
        InitializeComponent();
        EditorNameColumns.Apply(AttachmentGrid);
        Title = EditorText.Format("BattleRoomAttachmentSelectionDialog_001", kindLabel);
        HeaderTitleTextBlock.Text = EditorText.Format("BattleRoomAttachmentSelectionDialog_001", kindLabel);
        SelectionTextBlock.Text = SelectionHint;
        ConfirmButton.Content = EditorText.Format("BattleRoomAttachmentSelectionDialog_002", kindLabel);
        foreach (var definition in definitions)
        {
            _rows.Add(new AttachmentChoiceRow(definition));
        }

        AttachmentGrid.ItemsSource = _rows;
        _view = CollectionViewSource.GetDefaultView(_rows);
        _view.Filter = FilterRow;
        _view.Refresh();
        UpdateCount();
        Loaded += (_, _) => SearchTextBox.Focus();
    }

    public BattleRoomAttachmentDefinition? SelectedAttachment { get; private set; }

    private string SelectionHint => _preserveBattle
        ? EditorText.Format("BattleRoomAttachmentSelectionDialog_003", _kindLabel)
        : EditorText.Format("BattleRoomAttachmentSelectionDialog_004", _kindLabel);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        NativeWindowTheme.ApplyDarkTitleBar(this);
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _view?.Refresh();
        AttachmentGrid.SelectedItem = null;
        UpdateCount();
    }

    private bool FilterRow(object item)
    {
        if (item is not AttachmentChoiceRow row)
        {
            return false;
        }

        var keyword = SearchTextBox.Text.Trim();
        return string.IsNullOrWhiteSpace(keyword) ||
               row.SearchText.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    private void AttachmentGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = AttachmentGrid.SelectedItem as AttachmentChoiceRow;
        ConfirmButton.IsEnabled = selected is not null;
        SelectionTextBlock.Text = selected is null
            ? SelectionHint
            : $"{EditorText.ContentName(selected.Definition.LocalizedName, selected.Id)} · {selected.Id} · {selected.Source}";
    }

    private void AttachmentGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (AttachmentGrid.SelectedItem is AttachmentChoiceRow)
        {
            ConfirmSelection();
        }
    }

    private void Confirm_Click(object sender, RoutedEventArgs e) => ConfirmSelection();

    private void ConfirmSelection()
    {
        if (AttachmentGrid.SelectedItem is not AttachmentChoiceRow selected)
        {
            return;
        }

        SelectedAttachment = selected.Definition;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void UpdateCount()
    {
        var visibleCount = _view?.Cast<object>().Count() ?? _rows.Count;
        CountTextBlock.Text = EditorText.Format("BattleRoomAttachmentSelectionDialog_005", visibleCount, _rows.Count);
    }

    private sealed class AttachmentChoiceRow
    {
        public AttachmentChoiceRow(BattleRoomAttachmentDefinition definition)
        {
            Definition = definition;
            Id = definition.Id;
            ChineseName = definition.ChineseName;
            EnglishName = definition.EnglishName;
            Source = definition.SourceLabel;
            SearchText = string.Join(
                '\n',
                Id,
                ChineseName,
                EnglishName,
                Source,
                definition.SourceRelativePath);
        }

        public BattleRoomAttachmentDefinition Definition { get; }
        public string Id { get; }
        public string ChineseName { get; }
        public string EnglishName { get; }
        public string Source { get; }
        public string SearchText { get; }
    }
}
