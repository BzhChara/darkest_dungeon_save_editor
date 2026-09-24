using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using DarkestDungeonSaveEditor.Core;

namespace DarkestDungeonSaveEditor.App;

public partial class BattleEncounterSelectionDialog : Window
{
    private readonly ObservableCollection<EncounterChoiceRow> _rows = [];
    private readonly IReadOnlyList<DifficultyChoice> _difficultyChoices;
    private ICollectionView? _view;

    public BattleEncounterSelectionDialog(
        IReadOnlyCollection<BattleEncounterDefinition> encounters,
        int currentDifficulty,
        string title)
    {
        ArgumentNullException.ThrowIfNull(encounters);
        InitializeComponent();
        HeaderTitleTextBlock.Text = title;
        foreach (var encounter in encounters)
        {
            _rows.Add(new EncounterChoiceRow(encounter));
        }

        _difficultyChoices = encounters
            .Select(encounter => encounter.OriginDifficulty)
            .Append(currentDifficulty)
            .Distinct()
            .Order()
            .Select(difficulty => new DifficultyChoice(difficulty, difficulty == currentDifficulty))
            .ToArray();
        DifficultyComboBox.ItemsSource = _difficultyChoices;
        DifficultyComboBox.SelectedItem = _difficultyChoices.First(choice => choice.Value == currentDifficulty);
        EncounterGrid.ItemsSource = _rows;
        _view = CollectionViewSource.GetDefaultView(_rows);
        _view.Filter = FilterRow;
        _view.Refresh();
        UpdateCount();
        Loaded += (_, _) => SearchTextBox.Focus();
    }

    public BattleEncounterDefinition? SelectedEncounter { get; private set; }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        NativeWindowTheme.ApplyDarkTitleBar(this);
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        RefreshRows();
    }

    private void DifficultyComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshRows();
    }

    private void RefreshRows()
    {
        _view?.Refresh();
        EncounterGrid.SelectedItem = null;
        UpdateCount();
    }

    private bool FilterRow(object item)
    {
        if (item is not EncounterChoiceRow row)
        {
            return false;
        }

        if (DifficultyComboBox.SelectedItem is not DifficultyChoice selectedDifficulty ||
            row.Definition.OriginDifficulty != selectedDifficulty.Value)
        {
            return false;
        }

        var keyword = SearchTextBox.Text.Trim();
        return string.IsNullOrWhiteSpace(keyword) ||
               row.SearchText.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    private void EncounterGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = EncounterGrid.SelectedItem as EncounterChoiceRow;
        ConfirmButton.IsEnabled = selected is not null;
        SelectionTextBlock.Text = selected is null
            ? EditorText.Get("BattleEncounterSelectionDialog_001")
            : EditorText.Format("BattleEncounterSelectionDialog_002", selected.Composition, selected.DungeonId, selected.Definition.OriginDifficulty, selected.Source);
    }

    private void EncounterGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (EncounterGrid.SelectedItem is EncounterChoiceRow)
        {
            ConfirmSelection();
        }
    }

    private void Confirm_Click(object sender, RoutedEventArgs e) => ConfirmSelection();

    private void ConfirmSelection()
    {
        if (EncounterGrid.SelectedItem is not EncounterChoiceRow selected)
        {
            return;
        }

        SelectedEncounter = selected.Definition;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void UpdateCount()
    {
        var visibleCount = _view?.Cast<object>().Count() ?? _rows.Count;
        var difficultyCount = DifficultyComboBox.SelectedItem is DifficultyChoice selectedDifficulty
            ? _rows.Count(row => row.Definition.OriginDifficulty == selectedDifficulty.Value)
            : 0;
        CountTextBlock.Text = EditorText.Format("BattleEncounterSelectionDialog_003", visibleCount, difficultyCount);
    }

    private sealed record DifficultyChoice(int Value, bool IsCurrent)
    {
        public string DisplayName => IsCurrent ? EditorText.Format("BattleEncounterSelectionDialog_004", Value) : Value.ToString();
    }

    private sealed class EncounterChoiceRow
    {
        public EncounterChoiceRow(BattleEncounterDefinition definition)
        {
            Definition = definition;
            Composition = definition.DisplayName;
            ChineseComposition = definition.ChineseDisplayName;
            EnglishComposition = string.Join(" + ", definition.MonsterNames.Select(name => name.English));
            PreferredComposition = definition.MonsterNames.Count == definition.MonsterIds.Count
                ? string.Join(" + ", definition.MonsterNames.Select((name, index) =>
                    EditorText.ContentName(name, definition.MonsterIds[index])))
                : Composition;
            SourceKind = definition.Classification switch
            {
                BattleEncounterClassification.FixedBoss => EditorText.Get("BattleEncounterSelectionDialog_005"),
                BattleEncounterClassification.RoamingBoss => EditorText.Get("BattleEncounterSelectionDialog_006"),
                BattleEncounterClassification.RoamingEncounter => EditorText.Get("BattleEncounterSelectionDialog_007"),
                BattleEncounterClassification.ConditionalOrAdditional when
                    definition.ContainsBossMonster &&
                    definition.SourceKind == BattleEncounterSourceKind.Conditional => EditorText.Get("BattleEncounterSelectionDialog_008"),
                BattleEncounterClassification.ConditionalOrAdditional when
                    definition.ContainsBossMonster &&
                    definition.SourceKind == BattleEncounterSourceKind.Additional => EditorText.Get("BattleEncounterSelectionDialog_009"),
                BattleEncounterClassification.ConditionalOrAdditional when
                    definition.ContainsBossMonster => EditorText.Get("BattleEncounterSelectionDialog_010"),
                BattleEncounterClassification.ConditionalOrAdditional when
                    definition.SourceKind == BattleEncounterSourceKind.Conditional => EditorText.Get("BattleEncounterSelectionDialog_011"),
                BattleEncounterClassification.ConditionalOrAdditional when
                    definition.SourceKind == BattleEncounterSourceKind.Additional => EditorText.Get("BattleEncounterSelectionDialog_012"),
                BattleEncounterClassification.ConditionalOrAdditional => EditorText.Get("BattleEncounterSelectionDialog_013"),
                _ => EditorText.Get("BattleEncounterSelectionDialog_014")
            };
            TargetKind = definition.MashType switch
            {
                0 => EditorText.Get("BattleEncounterSelectionDialog_015"),
                1 => EditorText.Get("BattleEncounterSelectionDialog_016"),
                2 => EditorText.Get("BattleEncounterSelectionDialog_016"),
                _ => EditorText.Get("BattleEncounterSelectionDialog_017")
            };
            DungeonId = definition.OriginDungeonId;
            Source = definition.SourceLabel;
            CompositionToolTip = $"{PreferredComposition}{Environment.NewLine}{Composition}";
            SearchText = string.Join(
                '\n',
                ChineseComposition,
                EnglishComposition,
                Composition,
                SourceKind,
                definition.Classification,
                definition.ContainsBossMonster,
                definition.SourceKind,
                TargetKind,
                definition.MashType,
                DungeonId,
                definition.OriginDifficulty,
                Source,
                definition.SourceRelativePath);
        }

        public BattleEncounterDefinition Definition { get; }
        public string Composition { get; }
        public string ChineseComposition { get; }
        public string EnglishComposition { get; }
        public string PreferredComposition { get; }
        public string CompositionToolTip { get; }
        public string SourceKind { get; }
        public string TargetKind { get; }
        public string DungeonId { get; }
        public string Source { get; }
        public string SearchText { get; }
    }
}
