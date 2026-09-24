using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using DarkestDungeonSaveEditor.Core;

namespace DarkestDungeonSaveEditor.App;

public partial class InitialQuirkSelectionDialog : Window
{
    private static string DetailedSingletonReason =>
        EditorText.Get("InitialQuirkSelectionDialog_001");
    private static string CompactSingletonReason => EditorText.Get("InitialQuirkSelectionDialog_002");
    private readonly HeroClassCatalogResult _catalog;
    private readonly HeroClassDefinition _heroClass;
    private readonly int _resolveLevel;
    private readonly ObservableCollection<QuirkChoiceRow> _rows = [];
    private ICollectionView? _view;

    public InitialQuirkSelectionDialog(
        HeroClassCatalogResult catalog,
        HeroClassDefinition heroClass,
        int resolveLevel,
        IReadOnlyCollection<string> selectedQuirkIds)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(heroClass);
        ArgumentNullException.ThrowIfNull(selectedQuirkIds);

        _catalog = catalog;
        _heroClass = heroClass;
        _resolveLevel = resolveLevel;

        InitializeComponent();
        EditorNameColumns.Apply(QuirkGrid);
        var selectedIds = selectedQuirkIds.ToHashSet(StringComparer.Ordinal);
        foreach (var group in catalog.InitialQuirks
                     .GroupBy(quirk => quirk.Id, StringComparer.Ordinal)
                     .OrderBy(group => group.First().Kind)
                     .ThenBy(group => group.First().IsPositive switch
                     {
                         true => 0,
                         false => 1,
                         _ => 2
                     })
                     .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            var definition = group.First();
            var sources = group
                .Select(quirk => string.IsNullOrWhiteSpace(quirk.SourceLabel)
                    ? quirk.Source
                    : quirk.SourceLabel)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var source = sources.Length == 1
                ? sources[0]
                : EditorText.Format("InitialQuirkSelectionDialog_003", string.Join(", ", sources));
            _rows.Add(new QuirkChoiceRow(
                definition,
                source,
                selectedIds.Contains(definition.Id)));
        }

        QuirkGrid.ItemsSource = _rows;
        _view = CollectionViewSource.GetDefaultView(_rows);
        _view.Filter = FilterRow;
        RefreshAvailability();
        UpdateSelectionSummary();
        try
        {
            ValidateCurrentSelection();
        }
        catch (InvalidOperationException ex)
        {
            ValidationTextBlock.Text = ex.Message;
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        NativeWindowTheme.ApplyDarkTitleBar(this);
    }

    public IReadOnlyList<string> SelectedQuirkIds { get; private set; } = [];

    private string GetUnavailableReason(IReadOnlyCollection<string> quirkIds)
    {
        try
        {
            StagecoachHeroCandidateFactory.ValidateInitialQuirkSelection(
                _catalog,
                _heroClass,
                _resolveLevel,
                quirkIds);
            return string.Empty;
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message;
        }
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshView();

    private bool FilterRow(object item)
    {
        if (item is not QuirkChoiceRow row)
        {
            return false;
        }

        var keyword = SearchTextBox.Text.Trim();
        return string.IsNullOrWhiteSpace(keyword) ||
               row.Id.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               row.ChineseName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               row.EnglishName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               row.Kind.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               row.Polarity.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               row.WriteStatus.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               row.MaxHpSummary.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               row.Source.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    private void QuirkCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { DataContext: QuirkChoiceRow row } checkBox)
        {
            return;
        }

        var requestedValue = checkBox.IsChecked == true;
        row.IsSelected = requestedValue;
        try
        {
            ValidateCurrentSelection();
            ValidationTextBlock.Text = string.Empty;
        }
        catch (InvalidOperationException ex)
        {
            // Removing one invalid legacy selection must remain possible even when
            // another invalid selection is still present after a level change.
            if (requestedValue)
            {
                row.IsSelected = false;
                checkBox.IsChecked = false;
            }
            ValidationTextBlock.Text = ex.Message;
        }

        RefreshAvailability();
        UpdateSelectionSummary();
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in _rows)
        {
            row.IsSelected = false;
        }

        ValidationTextBlock.Text = string.Empty;
        RefreshAvailability();
        UpdateSelectionSummary();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var selectedIds = GetSelectedIds();
        try
        {
            StagecoachHeroCandidateFactory.ValidateInitialQuirkSelection(
                _catalog,
                _heroClass,
                _resolveLevel,
                selectedIds);
        }
        catch (InvalidOperationException ex)
        {
            ValidationTextBlock.Text = ex.Message;
            return;
        }

        SelectedQuirkIds = selectedIds;
        DialogResult = true;
    }

    private void ValidateCurrentSelection()
    {
        StagecoachHeroCandidateFactory.ValidateInitialQuirkSelection(
            _catalog,
            _heroClass,
            _resolveLevel,
            GetSelectedIds());
    }

    private string[] GetSelectedIds()
    {
        return _rows
            .Where(row => row.IsSelected)
            .Select(row => row.Id)
            .ToArray();
    }

    private void RefreshAvailability()
    {
        var selectedIds = GetSelectedIds();
        var selectionUnavailableReason = GetUnavailableReason(selectedIds);
        foreach (var row in _rows)
        {
            // HP modifiers can compensate each other; validate the proposed combination.
            // Selected rows stay enabled so an invalid selection can always be removed.
            var reason = row.IsSelected
                ? selectionUnavailableReason
                : GetUnavailableReason([.. selectedIds, row.Id]);
            row.SetAvailability(
                row.IsSelected || string.IsNullOrWhiteSpace(reason),
                CompactRowReason(CombineReasons(row.ContextReason, reason)));
        }

        RefreshView();
    }

    private static string CombineReasons(string first, string second)
    {
        if (string.IsNullOrWhiteSpace(first))
        {
            return second;
        }

        return string.IsNullOrWhiteSpace(second)
            ? first
            : $"{first}；{second}";
    }

    private static string CompactRowReason(string reason) =>
        reason.Replace(
            DetailedSingletonReason,
            CompactSingletonReason,
            StringComparison.Ordinal);

    private void RefreshView()
    {
        if (_view is IEditableCollectionView editableView)
        {
            if (editableView.IsAddingNew)
            {
                editableView.CommitNew();
            }

            if (editableView.IsEditingItem)
            {
                editableView.CommitEdit();
            }
        }

        _view?.Refresh();
    }

    private void UpdateSelectionSummary()
    {
        var positiveCount = _rows.Count(row =>
            row.IsSelected && row.Definition.IsPositive == true);
        var negativeCount = _rows.Count(row =>
            row.IsSelected && !row.Definition.IsDisease && row.Definition.IsPositive == false);
        var diseaseCount = _rows.Count(row => row.IsSelected && row.Definition.IsDisease);
        var limits = _catalog.InitialQuirkLimits;
        static string Limit(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? EditorText.Get("BattleEncounterSelectionDialog_017");
        SelectionSummaryTextBlock.Text =
            EditorText.Format("InitialQuirkSelectionDialog_004", positiveCount, Limit(limits.Positive)) +
            $"-{negativeCount}/{Limit(limits.Negative)} " +
            EditorText.Format("InitialQuirkSelectionDialog_005", diseaseCount, Limit(limits.Diseases));
    }

    private sealed class QuirkChoiceRow : INotifyPropertyChanged
    {
        private bool _isSelected;
        private bool _isSelectable;
        private string _unavailableReason;

        public QuirkChoiceRow(
            HeroInitialQuirkDefinition definition,
            string source,
            bool isSelected)
        {
            Definition = definition;
            Source = source;
            ContextReason = definition.WriteStatus == HeroInitialQuirkWriteStatus.RequiresSaveContext
                ? CompactRowReason(definition.WriteStatusReason)
                : string.Empty;
            _unavailableReason = ContextReason;
            _isSelectable = true;
            _isSelected = isSelected;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public HeroInitialQuirkDefinition Definition { get; }
        public string Id => Definition.Id;
        public string ChineseName => FormatLocalizedName(Definition.LocalizedName.Chinese);
        public string EnglishName => FormatLocalizedName(Definition.LocalizedName.English);
        public string Kind => Definition.Kind switch
        {
            HeroInitialQuirkKind.Natural => EditorText.Get("InitialQuirkSelectionDialog_006"),
            HeroInitialQuirkKind.Special => EditorText.Get("InitialQuirkSelectionDialog_007"),
            HeroInitialQuirkKind.Disease => EditorText.Get("InitialQuirkSelectionDialog_008"),
            _ => EditorText.Get("BattleEncounterSelectionDialog_017")
        };
        public string Polarity => Definition.IsDisease
            ? EditorText.Get("InitialQuirkSelectionDialog_008")
            : Definition.IsPositive switch
            {
                true => EditorText.Get("InitialQuirkSelectionDialog_009"),
                false => EditorText.Get("InitialQuirkSelectionDialog_010"),
                _ => EditorText.Get("BattleEncounterSelectionDialog_017")
            };
        public string WriteStatus => Definition.WriteStatus switch
        {
            HeroInitialQuirkWriteStatus.Direct => EditorText.Get("InitialQuirkSelectionDialog_011"),
            HeroInitialQuirkWriteStatus.RequiresSaveContext => EditorText.Get("InitialQuirkSelectionDialog_012"),
            HeroInitialQuirkWriteStatus.Unverified => EditorText.Get("InitialQuirkSelectionDialog_013"),
            HeroInitialQuirkWriteStatus.Unsupported => EditorText.Get("InitialQuirkSelectionDialog_014"),
            _ => EditorText.Get("BattleEncounterSelectionDialog_017")
        };
        public string MaxHpSummary =>
            Definition.WriteStatusReason.Contains("max_hp", StringComparison.OrdinalIgnoreCase)
                ? EditorText.Get("InitialQuirkSelectionDialog_015")
                : Definition.MaxHpModifiers.Count == 0
                    ? EditorText.Get("InitialQuirkSelectionDialog_016")
                    : string.Join("；", Definition.MaxHpModifiers.Select(FormatMaxHpModifier));
        public string Source { get; }
        public string ContextReason { get; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                {
                    return;
                }

                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public bool IsSelectable
        {
            get => _isSelectable;
            private set
            {
                if (_isSelectable == value)
                {
                    return;
                }

                _isSelectable = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelectable)));
            }
        }

        public string UnavailableReason
        {
            get => _unavailableReason;
            private set
            {
                if (_unavailableReason.Equals(value, StringComparison.Ordinal))
                {
                    return;
                }

                _unavailableReason = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UnavailableReason)));
            }
        }

        public void SetAvailability(bool isSelectable, string unavailableReason)
        {
            IsSelectable = isSelectable;
            UnavailableReason = unavailableReason;
        }

        private static string FormatSignedPercent(double amount)
        {
            var percent = (amount * 100).ToString("0.##", CultureInfo.InvariantCulture);
            return amount > 0 ? $"+{percent}%" : $"{percent}%";
        }

        private static string FormatMaxHpModifier(HeroMaxHpModifier modifier)
        {
            var amount = modifier.Kind == HeroMaxHpModifierKind.Percentage
                ? FormatSignedPercent(modifier.Amount)
                : modifier.Amount.ToString("+0.###;-0.###;0", CultureInfo.InvariantCulture);
            var condition = modifier.RuleType switch
            {
                "always" => modifier.IsFalseRule ? EditorText.Get("InitialQuirkSelectionDialog_017") : EditorText.Get("InitialQuirkSelectionDialog_018"),
                "no_trinkets" => modifier.IsFalseRule ? EditorText.Get("InitialQuirkSelectionDialog_019") : EditorText.Get("InitialQuirkSelectionDialog_020"),
                "afflicted" => modifier.IsFalseRule ? EditorText.Get("InitialQuirkSelectionDialog_021") : EditorText.Get("InitialQuirkSelectionDialog_022"),
                "in_mode" => modifier.IsFalseRule
                    ? EditorText.Format("InitialQuirkSelectionDialog_023", modifier.RuleString)
                    : EditorText.Format("InitialQuirkSelectionDialog_024", modifier.RuleString),
                "lightabove" => modifier.IsFalseRule
                    ? EditorText.Format("InitialQuirkSelectionDialog_025", FormatRuleNumber(modifier.RuleFloat))
                    : EditorText.Format("InitialQuirkSelectionDialog_026", FormatRuleNumber(modifier.RuleFloat)),
                _ => modifier.RuleType
            };
            return $"{amount} / {condition}";
        }

        private static string FormatRuleNumber(double? value) =>
            value?.ToString("0.###", CultureInfo.InvariantCulture) ?? "?";

        private static string FormatLocalizedName(string value) =>
            string.IsNullOrWhiteSpace(value) ? "—" : value;
    }
}
