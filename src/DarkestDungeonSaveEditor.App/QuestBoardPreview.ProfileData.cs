using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using DarkestDungeonSaveEditor.Core;

namespace DarkestDungeonSaveEditor.App;

public partial class QuestBoardPreview
{
    private QuestBoardEditCatalog? _editCatalog;

    internal void SetEditCatalog(QuestBoardEditCatalog? catalog)
    {
        _editCatalog = catalog;
        UpdateResolvedImages();
    }

    private void UpdateResolvedImages()
    {
        var images = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in _editCatalog?.ImagePaths ?? new Dictionary<string, string>()) images[pair.Key] = pair.Value;
        foreach (var pair in _snapshot?.ImagePaths ?? new Dictionary<string, string>()) images[pair.Key] = pair.Value;
        _assets.SetResolvedImages(images);
    }

    private QuestBoardChoice[] EditChoices(string regionId) => _fixtureMode ? QuestBoardPreviewData.Choices(regionId).ToArray() :
        _editCatalog?.Templates.Where(item => item.Quest.Dungeon == regionId).Select(item =>
            new QuestBoardChoice(item.Quest.Type, item.Quest.Difficulty, item.Quest.Length, item.Quest.IsPlotQuest ? item.Quest.Id : null)
            { SavedQuest = item.Quest, TemplateKey = item.Key, UnavailableReason = item.UnavailableReason }).ToArray() ?? [];

    // Fixture interactions are exercised explicitly by the UI contracts, never by a profile load.
    internal void UseSampleData()
    {
        _fixtureMode = true;
        _snapshot = null;
        _samples = QuestBoardPreviewData.Samples();
        _regions = QuestBoardPreviewData.Regions;
        _selected = _samples[0].Key;
        _assets.SetResolvedImages(null);
        RebuildBoard();
    }

    internal void SetSnapshot(QuestBoardSnapshot? snapshot)
    {
        if (ReferenceEquals(_snapshot, snapshot) && !_fixtureMode) return;
        var previousProfile = _snapshot?.ProfileDirectory;
        if (!string.Equals(previousProfile, snapshot?.ProfileDirectory, StringComparison.OrdinalIgnoreCase)) _regionChoices.Clear();
        _fixtureMode = false;
        _snapshot = snapshot;
        if (snapshot is null) _editCatalog = null;
        UpdateResolvedImages();
        _regions = snapshot?.Regions.Select(region => new QuestBoardRegion(region.Id, region.X, region.Y)
        {
            LocalizedName = region.LocalizedName, QuestsPerRow = region.QuestsPerRow, HasLayout = region.HasLayout,
            HasVisibleProgression = region.HasVisibleProgression, ProgressionLevel = region.ProgressionLevel,
            ProgressionFraction = region.ProgressionFraction
        }).ToArray() ?? [];
        _samples = snapshot?.Quests.Select(quest => new QuestBoardSample(QuestKey(snapshot.ProfileDirectory, quest),
            quest.Dungeon, new QuestBoardChoice(quest.Type, quest.Difficulty, quest.Length, quest.IsPlotQuest ? quest.Id : null)
            { SavedQuest = quest })).ToList() ?? [];
        if (!string.Equals(previousProfile, snapshot?.ProfileDirectory, StringComparison.OrdinalIgnoreCase) ||
            !_samples.Any(entry => entry.Key == _selected)) _selected = _samples.FirstOrDefault()?.Key;
        RewardsScroll.ScrollToTop();
        RewardInventoryScroll.ScrollToTop();
        RebuildBoard();
    }

    private static Guid QuestKey(string profile, QuestBoardQuest quest)
    {
        var key = System.Text.Json.JsonSerializer.Serialize(new[] { profile.ToUpperInvariant(), quest.SaveKey,
            quest.Id, quest.Type, quest.Dungeon, quest.Difficulty.ToString(), quest.Length.ToString(), string.Join("\0", quest.GoalIds) });
        return new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(key)).AsSpan(0, 16));
    }

    private void UpdateBoardState()
    {
        var available = _profileLoaded && (_fixtureMode || _snapshot is not null);
        BoardBody.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
        ProfileRequiredPanel.Visibility = available ? Visibility.Collapsed : Visibility.Visible;
        LocalizedUi.Set(BoardEmptyText, TextBlock.TextProperty, () => EditorText.Get(_profileLoaded ? "QuestBoard_NoBoard" : "QuestBoard_LoadFirst"));
        LocalizedUi.Set(BoardStatusText, TextBlock.TextProperty, () => _fixtureMode ? EditorText.Get("QuestBoard_PreviewLabel") :
            _snapshot is null ? EditorText.Get("QuestBoard_Title") : EditorText.Format("QuestBoard_TaskCount", _samples.Count));
        BoardStatusText.ToolTip = BoardStatusText.Text;
    }

    private void RefreshUnpositionedQuests()
    {
        _updatingUnpositioned = true;
        try
        {
            var unknown = _regions.Where(region => !region.HasLayout).Select(region => region.Id).ToHashSet(StringComparer.Ordinal);
            var rows = _samples.Where(entry => unknown.Contains(entry.RegionId))
                .Select(entry => new { entry.Key, Label = $"{entry.RegionId} · {FormatSample(entry)}" }).ToArray();
            UnpositionedQuests.ItemsSource = rows;
            UnpositionedQuests.SelectedValue = _selected;
            UnpositionedQuests.Visibility = rows.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        finally { _updatingUnpositioned = false; }
    }

    private void UnpositionedQuests_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_updatingUnpositioned && UnpositionedQuests.SelectedValue is Guid key) SelectSample(key);
    }
}
