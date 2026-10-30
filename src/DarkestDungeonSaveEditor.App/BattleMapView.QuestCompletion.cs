using System.Windows;
using System.Windows.Controls;
using DarkestDungeonSaveEditor.Core;

namespace DarkestDungeonSaveEditor.App;

public partial class BattleMapView : UserControl
{
    private async void ForceCompleteQuestButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanBrowseProfile() || !CanCompleteQuest() || Window.GetWindow(this) is not { } owner) return;
        if (!await CheckProfileEditAsync() || !CanCompleteQuest()) return;
        var profile = _profile!;
        var snapshot = _currentSnapshot!;
        var content = _activeContentSnapshot!;
        var service = new SaveEditService(_codec!);
        var generation = _profileGeneration;
        var gateTaken = false;
        var committed = false;
        string? backup = null;
        _isApplyingEdit = true;
        CloseActiveContextMenu();
        CancelScheduledRefreshRetry();
        StopProfileMonitoring();
        MapCanvas.IsHitTestVisible = false;
        ForceTownButton.IsEnabled = false;
        ForceCompleteQuestButton.IsEnabled = false;
        try
        {
            await NotifySaveEditBusyAsync(true);
            await _refreshGate.WaitAsync();
            gateTaken = true;
            if (generation != _profileGeneration || !ReferenceEquals(snapshot, _currentSnapshot))
                throw new InvalidOperationException(EditorText.Get("QuestCompletion_Changed"));
            LocalizedUi.Set(MapSelectionTextBlock, TextBlock.TextProperty, () => EditorText.Get("QuestCompletion_Preparing"));
            var prepared = await Task.Run(() => service.PrepareQuestCompletionAsync(profile, snapshot, content));
            if (generation != _profileGeneration || !ReferenceEquals(snapshot, _currentSnapshot))
                throw new InvalidOperationException(EditorText.Get("QuestCompletion_Changed"));
            if (!ThemedDialog.Confirm(owner, prepared.Preview.Summary + Environment.NewLine + Environment.NewLine +
                    EditorText.Get("QuestCompletion_Confirm"), EditorText.Get("QuestCompletion_Button")))
            {
                LocalizedUi.Set(MapSelectionTextBlock, TextBlock.TextProperty, () => EditorText.Get("QuestCompletion_Cancelled"));
                return;
            }
            var result = await Task.Run(() => service.CommitAsync(prepared));
            committed = true;
            backup = result.BackupDirectory;
            SaveEditApplied?.Invoke(EditorText.Format("QuestCompletion_Log", profile.ProfileId, backup) + " " + prepared.Preview.Summary);
            if (generation == _profileGeneration && _snapshotReader is not null)
            {
                var refreshed = await LoadSnapshotWithRetryAsync(_snapshotReader, profile.ProfileDirectory, CancellationToken.None);
                if (generation == _profileGeneration) RenderSnapshot(refreshed, fitToView: false);
            }
            LocalizedUi.Set(MapSelectionTextBlock, TextBlock.TextProperty, () => EditorText.Get("QuestCompletion_Done"));
        }
        catch (Exception error)
        {
            CrashDiagnostics.RecordException("Complete quest objectives", error,
                $"Profile={profile.ProfileDirectory}; committed={committed}; backup={backup}");
            LocalizedUi.Set(MapSelectionTextBlock, TextBlock.TextProperty, () => EditorText.Get(
                committed ? "QuestCompletion_RefreshFailed" : "QuestCompletion_Failed"));
            ThemedDialog.ShowMessage(owner, error.Message, EditorText.Get(
                committed ? "QuestCompletion_RefreshFailed" : "QuestCompletion_Failed"), ThemedDialogKind.Error);
        }
        finally
        {
            if (gateTaken) _refreshGate.Release();
            try { await NotifySaveEditBusyAsync(false); }
            finally
            {
                _isApplyingEdit = false;
                MapCanvas.IsHitTestVisible = true;
                if (generation == _profileGeneration)
                {
                    ForceTownButton.IsEnabled = _currentSnapshot is { IsTutorialRaid: false };
                    StartProfileMonitoring();
                }
            }
        }
    }
}
