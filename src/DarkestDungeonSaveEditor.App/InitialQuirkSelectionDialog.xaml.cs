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
    private const string DetailedSingletonReason =
        "singleton 定义上限 1；预览统计 roster 与全部马车池，超限仅警告";
    private const string CompactSingletonReason = "singleton 定义上限 1";
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
                : $"多个定义：{string.Join(", ", sources)}";
            var baseUnavailableReason = GetUnavailableReason(definition.Id);
            _rows.Add(new QuirkChoiceRow(
                definition,
                source,
                baseUnavailableReason,
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

    private string GetUnavailableReason(string quirkId)
    {
        try
        {
            StagecoachHeroCandidateFactory.ValidateInitialQuirkSelection(
                _catalog,
                _heroClass,
                _resolveLevel,
                [quirkId]);
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
        var selectedDefinitions = _rows
            .Where(row => row.IsSelected)
            .Select(row => row.Definition)
            .ToArray();
        foreach (var row in _rows)
        {
            if (!string.IsNullOrWhiteSpace(row.BaseUnavailableReason))
            {
                // A level change can make a previously valid flat-HP selection invalid.
                // Keep an already selected row enabled so the user can remove it.
                row.SetAvailability(row.IsSelected, row.BaseUnavailableReason);
                continue;
            }

            if (row.IsSelected)
            {
                row.SetAvailability(true, row.ContextReason);
                continue;
            }

            var incompatible = selectedDefinitions.FirstOrDefault(selected =>
                selected.IncompatibleQuirkIds.Contains(row.Id, StringComparer.Ordinal) ||
                row.Definition.IncompatibleQuirkIds.Contains(selected.Id, StringComparer.Ordinal));
            if (incompatible is not null)
            {
                var incompatibilityReason =
                    $"初始怪癖 '{incompatible.Id}' 与 '{row.Id}' 互斥，不能同时选择。";
                row.SetAvailability(
                    false,
                    CombineReasons(row.ContextReason, incompatibilityReason));
                continue;
            }

            // The counters above the grid already communicate the three independent quotas.
            // Keep row-level dynamic reasons for actual quirk incompatibilities only; a sixth
            // positive/negative quirk or fourth disease is rejected when the user clicks it.
            row.SetAvailability(true, row.ContextReason);
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
            row.IsSelected && !row.Definition.IsDisease && row.Definition.IsPositive == true);
        var negativeCount = _rows.Count(row =>
            row.IsSelected && !row.Definition.IsDisease && row.Definition.IsPositive == false);
        var diseaseCount = _rows.Count(row => row.IsSelected && row.Definition.IsDisease);
        SelectionSummaryTextBlock.Text =
            $"已选 +{positiveCount}/{StagecoachHeroCandidateFactory.MaximumPositiveInitialQuirks} " +
            $"-{negativeCount}/{StagecoachHeroCandidateFactory.MaximumNegativeInitialQuirks} " +
            $"疾病 {diseaseCount}/{StagecoachHeroCandidateFactory.MaximumInitialDiseases}";
    }

    private sealed class QuirkChoiceRow : INotifyPropertyChanged
    {
        private bool _isSelected;
        private bool _isSelectable;
        private string _unavailableReason;

        public QuirkChoiceRow(
            HeroInitialQuirkDefinition definition,
            string source,
            string baseUnavailableReason,
            bool isSelected)
        {
            Definition = definition;
            Source = source;
            BaseUnavailableReason = CompactRowReason(baseUnavailableReason);
            ContextReason = definition.WriteStatus == HeroInitialQuirkWriteStatus.RequiresSaveContext
                ? CompactRowReason(definition.WriteStatusReason)
                : string.Empty;
            _unavailableReason = string.IsNullOrWhiteSpace(BaseUnavailableReason)
                ? ContextReason
                : BaseUnavailableReason;
            _isSelectable = string.IsNullOrWhiteSpace(BaseUnavailableReason);
            _isSelected = isSelected;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public HeroInitialQuirkDefinition Definition { get; }
        public string Id => Definition.Id;
        public string ChineseName => FormatLocalizedName(Definition.LocalizedName.Chinese);
        public string EnglishName => FormatLocalizedName(Definition.LocalizedName.English);
        public string Kind => Definition.Kind switch
        {
            HeroInitialQuirkKind.Natural => "自然随机",
            HeroInitialQuirkKind.Special => "固定/特殊",
            HeroInitialQuirkKind.Disease => "疾病",
            _ => "未知"
        };
        public string Polarity => Definition.IsDisease
            ? "疾病"
            : Definition.IsPositive switch
            {
                true => "正面",
                false => "负面",
                _ => "未知"
            };
        public string WriteStatus => Definition.WriteStatus switch
        {
            HeroInitialQuirkWriteStatus.Direct => "可直接写入",
            HeroInitialQuirkWriteStatus.RequiresSaveContext => "需存档上下文",
            HeroInitialQuirkWriteStatus.Unverified => "未验证",
            HeroInitialQuirkWriteStatus.Unsupported => "暂不支持",
            _ => "未知"
        };
        public string MaxHpSummary =>
            Definition.WriteStatusReason.Contains("max_hp", StringComparison.OrdinalIgnoreCase)
                ? "待验证"
                : Definition.MaxHpModifiers.Count == 0
                    ? "无"
                    : string.Join("；", Definition.MaxHpModifiers.Select(FormatMaxHpModifier));
        public string Source { get; }
        public string BaseUnavailableReason { get; }
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
            var condition = modifier.RuleType.ToLowerInvariant() switch
            {
                "always" => modifier.IsFalseRule ? "永不生效" : "常驻",
                "no_trinkets" => modifier.IsFalseRule ? "有饰品时" : "无饰品时",
                "afflicted" => modifier.IsFalseRule ? "未折磨时" : "折磨时",
                "in_mode" => modifier.IsFalseRule
                    ? $"非 {modifier.RuleString} 模式"
                    : $"{modifier.RuleString} 模式",
                "lightabove" => modifier.IsFalseRule
                    ? $"火光 ≤ {FormatRuleNumber(modifier.RuleFloat)}"
                    : $"火光 > {FormatRuleNumber(modifier.RuleFloat)}",
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
