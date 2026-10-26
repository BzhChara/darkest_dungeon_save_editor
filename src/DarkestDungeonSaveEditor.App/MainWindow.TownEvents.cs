using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using DarkestDungeonSaveEditor.Core;

namespace DarkestDungeonSaveEditor.App;

public partial class MainWindow
{
    private readonly ObservableCollection<TownEventRow> _visibleEvents = [];
    private IReadOnlyList<TownEventDefinition> _allEvents = [];
    private PreparedTownEventEdit? _preparedTownEventEdit;

    private sealed class TownEventRow(TownEventDefinition definition) : LocalizedRow
    {
        public TownEventDefinition Definition { get; } = definition;
        public string Id => Definition.Id;
        public string ChineseName => FormatLocalizedName(Definition.LocalizedName.Chinese);
        public string EnglishName => FormatLocalizedName(Definition.LocalizedName.English);
        public string Source => Definition.SourceLabel;
        public string Description => EditorText.ContentName(Definition.LocalizedDescription, Definition.Id);
        public string TriggerConditions => Definition.TriggerRules.Describe();
    }

    private void FilterEvents(string keyword)
    {
        _visibleEvents.Clear();
        foreach (var definition in _allEvents.Where(item => string.IsNullOrWhiteSpace(keyword) ||
                     new[] { item.Id, item.LocalizedName.Chinese, item.LocalizedName.English, item.SourceLabel }
                         .Any(text => text.Contains(keyword, StringComparison.OrdinalIgnoreCase))))
            _visibleEvents.Add(new TownEventRow(definition));
    }

    private void EventGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_restoringCatalogSelection) return;
        InvalidatePreparedEdit();
        UpdateEnabledState();
    }

    private void UpdateEventAvailability()
    {
        if (CatalogTabs?.SelectedIndex != EventTabIndex || PreviewWarningTextBlock is null) return;
        var warning = _quantitySaveContext != QuantityItemSaveContext.Town ? EditorText.Get("TownOnlyOperation") :
            EventGrid?.SelectedItem is TownEventRow row
                ? TownEventAvailability.UnavailableReason(row.Definition) : string.Empty;
        if (warning.Length == 0 && _preparedTownEventEdit is { } prepared)
            warning = string.Join(Environment.NewLine, prepared.Preview.Warnings);
        PreviewWarningTextBlock.Text = warning;
        PreviewWarningTextBlock.Visibility = warning.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateTownEventPreview()
    {
        if (_preparedTownEventEdit is not { } prepared) return;
        PreviewSummaryTextBlock.Text = FormatTownEventSummary(prepared.Preview, includeWarnings: false);
        UpdateEventAvailability();
    }

    private string FormatTownEventPreview(TownEventMutationPreview preview) =>
        FormatTownEventSummary(preview, includeWarnings: true);

    private string FormatTownEventSummary(TownEventMutationPreview preview, bool includeWarnings)
    {
        var old = _allEvents.FirstOrDefault(item => item.NativeId == preview.OldEventId);
        var next = _allEvents.FirstOrDefault(item => item.Id == preview.NewEventId);
        var nextName = next is null ? preview.NewEventId : EditorText.ContentName(next.LocalizedName, next.Id);
        var summary = preview.OldEventId == 0
            ? EditorText.Format("TownEvent_Addition", nextName, preview.Week)
            : EditorText.Format("TownEvent_Replacement", old is null ? preview.OldEventId.ToString() :
                EditorText.ContentName(old.LocalizedName, old.Id), nextName, preview.Week);
        var lines = new List<string> { summary };
        lines.AddRange(includeWarnings ? preview.Changes :
            preview.Changes.Where(change => !preview.Warnings.Contains(change, StringComparer.Ordinal)));
        return string.Join(Environment.NewLine, lines);
    }
}
