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
            ? "选择一个完整敌方组合；确认后会直接写入当前地图格。"
            : $"{selected.Composition} · {selected.DungeonId} · 难度 {selected.Definition.OriginDifficulty} · {selected.Source}";
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
        CountTextBlock.Text = $"显示 {visibleCount} / {difficultyCount} 个遭遇";
    }

    private sealed record DifficultyChoice(int Value, bool IsCurrent)
    {
        public string DisplayName => IsCurrent ? $"{Value}（当前副本）" : Value.ToString();
    }

    private sealed class EncounterChoiceRow
    {
        public EncounterChoiceRow(BattleEncounterDefinition definition)
        {
            Definition = definition;
            Composition = definition.DisplayName;
            ChineseComposition = definition.ChineseDisplayName;
            SourceKind = definition.Classification switch
            {
                BattleEncounterClassification.FixedBoss => "固定首领",
                BattleEncounterClassification.RoamingBoss => "游荡首领",
                BattleEncounterClassification.RoamingEncounter => "游荡遭遇",
                BattleEncounterClassification.ConditionalOrAdditional when
                    definition.ContainsBossMonster &&
                    definition.SourceKind == BattleEncounterSourceKind.Conditional => "条件首领",
                BattleEncounterClassification.ConditionalOrAdditional when
                    definition.ContainsBossMonster &&
                    definition.SourceKind == BattleEncounterSourceKind.Additional => "额外首领",
                BattleEncounterClassification.ConditionalOrAdditional when
                    definition.ContainsBossMonster => "特殊首领",
                BattleEncounterClassification.ConditionalOrAdditional when
                    definition.SourceKind == BattleEncounterSourceKind.Conditional => "条件战斗",
                BattleEncounterClassification.ConditionalOrAdditional when
                    definition.SourceKind == BattleEncounterSourceKind.Additional => "额外战斗",
                BattleEncounterClassification.ConditionalOrAdditional => "特殊战斗",
                _ => "普通战斗"
            };
            TargetKind = definition.MashType switch
            {
                0 => "走廊",
                1 => "房间",
                2 => "房间",
                _ => "未知"
            };
            DungeonId = definition.OriginDungeonId;
            Source = definition.SourceLabel;
            CompositionToolTip = string.IsNullOrWhiteSpace(ChineseComposition)
                ? Composition
                : $"{ChineseComposition}{Environment.NewLine}{Composition}";
            SearchText = string.Join(
                '\n',
                ChineseComposition,
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
        public string CompositionToolTip { get; }
        public string SourceKind { get; }
        public string TargetKind { get; }
        public string DungeonId { get; }
        public string Source { get; }
        public string SearchText { get; }
    }
}
