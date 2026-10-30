using System.Windows;
using System.Windows.Controls;
using DarkestDungeonSaveEditor.Core;

namespace DarkestDungeonSaveEditor.App;

public partial class MainWindow
{
    private void UpdateQuestBoardMode()
    {
        if (QuestBoardPanel is null || BattleTab is null || GameDirectoryTextBox is null) return;
        // Publish the board only through the same stable profile synchronization as the other pages.
        var showBoard = _quantitySaveContext == QuantityItemSaveContext.Town;
        QuestBoardPanel.SetProfileLoaded(_catalogProfileDirectory is not null);
        QuestBoardPanel.Configure(GameDirectoryTextBox.Text);
        QuestBoardPanel.Visibility = showBoard ? Visibility.Visible : Visibility.Collapsed;
        BattleMapPanel.Visibility = showBoard ? Visibility.Collapsed : Visibility.Visible;
        LocalizedUi.Set(BattleTab, HeaderedContentControl.HeaderProperty,
            () => EditorText.Get(showBoard ? "QuestBoard_Tab" : "MainWindow_042"));
    }

    private void QuestBoardPanel_PreviewActionApplied(string message) =>
        AppendStatusSafely(message, "QuestBoard: preview action");

    private async Task QuestBoardPanel_SaveEditRequested(QuestBoardEditRequest request)
    {
        if (!await CheckProfileBeforeOperationAsync(requireUnchanged: true) || _activeContentSnapshot is not { } content) return;
        var committed = false;
        string? backupDirectory = null;
        try
        {
            SetBusy(true);
            InvalidatePreparedEdit();
            if (_quantitySaveContext != QuantityItemSaveContext.Town)
                throw new InvalidOperationException(EditorText.Get("TownOnlyOperation"));
            var service = new SaveEditService(_codecSession.Codec, _saveLocations);
            var prepared = await Task.Run(() => service.PrepareQuestBoardEditAsync(content.Profile, request, content));
            var summary = FormatQuestBoardEdit(prepared.Preview);
            if (!ThemedDialog.Confirm(this, summary + Environment.NewLine + Environment.NewLine +
                    EditorText.Get("QuestBoard_WriteConfirmation"), EditorText.Get("QuestBoard_Title"))) return;
            var result = await Task.Run(() => service.CommitAsync(prepared));
            committed = true;
            backupDirectory = result.BackupDirectory;
            AppendStatusSafely(summary + Environment.NewLine + EditorText.Format("QuestBoard_Backup", result.BackupDirectory),
                "QuestBoard: applied save edit");
        }
        catch (Exception error)
        {
            CrashDiagnostics.RecordException("QuestBoard: " + (committed ? "post-commit UI" : "edit"), error);
            AppendStatusSafely(EditorText.Format("QuestBoard_EditFailed", error.Message), "QuestBoard: failed edit", DiagnosticLogLevel.Error);
            ThemedDialog.ShowMessage(this, error.Message + (backupDirectory is null ? string.Empty :
                Environment.NewLine + EditorText.Format("QuestBoard_Backup", backupDirectory)),
                EditorText.Get("QuestBoard_Title"), ThemedDialogKind.Error);
        }
        finally { await CompleteProfileEditAsync(); }
    }

    private static string FormatQuestBoardEdit(QuestBoardMutationPreview preview)
    {
        var lines = new List<string> { preview.Kind switch
        {
            QuestBoardEditKind.Create => EditorText.Format("QuestBoard_CreateSummary", preview.Result!.Name),
            QuestBoardEditKind.Replace => EditorText.Format("QuestBoard_ReplaceSummary", preview.Previous!.Name, preview.Result!.Name),
            _ => EditorText.Format("QuestBoard_DeleteSummary", preview.Previous!.Name)
        } };
        if (preview.Result is { } quest)
        {
            lines.Add($"{quest.Id} · {quest.Dungeon} · {QuestBoardPreviewData.Difficulty(quest.Difficulty)} · {QuestBoardPreviewData.Length(quest.Length)}");
            if (quest.Objective.Length > 0) lines.Add(quest.Objective);
            if (quest.Rewards.Count > 0)
                lines.Add(EditorText.Get("QuestBoard_Rewards") + ": " + string.Join(" · ", quest.Rewards.Select(reward =>
                    $"{EditorText.ContentName(reward.LocalizedName, reward.Id.Length == 0 ? reward.Type : reward.Id)} ×{reward.Amount}")));
        }
        lines.AddRange(preview.Warnings);
        return string.Join(Environment.NewLine, lines);
    }
}
