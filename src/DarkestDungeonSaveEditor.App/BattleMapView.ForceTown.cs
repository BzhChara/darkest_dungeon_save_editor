using System.Windows;
using System.Windows.Controls;
using DarkestDungeonSaveEditor.Core;

namespace DarkestDungeonSaveEditor.App;

public partial class BattleMapView : UserControl
{
    private async void ForceTownButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentSnapshot is null ||
            _profile is null ||
            _forceTownSaveService is null ||
            _isApplyingEdit)
        {
            MapSelectionTextBlock.Text = EditorText.Get("BattleMapView_ForceTown_001");
            return;
        }

        var owner = Window.GetWindow(this);
        if (owner is null)
        {
            return;
        }

        var profile = _profile;
        var snapshot = _currentSnapshot;
        var service = _forceTownSaveService;
        if (!ThemedDialog.Confirm(
                owner,
                EditorText.Format("BattleMapView_ForceTown_002", profile.ProfileId) +
                Environment.NewLine +
                EditorText.Get("BattleMapView_ForceTown_003") +
                Environment.NewLine +
                EditorText.Get("BattleMapView_ForceTown_004"),
                EditorText.Get("BattleMapView_ForceTown_005")))
        {
            return;
        }

        var generation = _profileGeneration;
        var gateTaken = false;
        var committed = false;
        PreparedForceTownEdit? prepared = null;
        string? backupDirectory = null;
        _isApplyingEdit = true;
        SaveEditBusyChanged?.Invoke(true);
        CloseActiveContextMenu();
        CancelScheduledRefreshRetry();
        StopProfileMonitoring();
        MapCanvas.IsHitTestVisible = false;
        ForceTownButton.IsEnabled = false;
        MapSelectionTextBlock.Text = EditorText.Get("BattleMapView_ForceTown_006");

        try
        {
            await _refreshGate.WaitAsync();
            gateTaken = true;
            if (generation != _profileGeneration ||
                !ReferenceEquals(snapshot, _currentSnapshot) ||
                !ReferenceEquals(profile, _profile))
            {
                throw new InvalidOperationException(EditorText.Get("BattleMapView_ForceTown_007"));
            }

            prepared = await service.PrepareAsync(profile, snapshot);
            var result = await service.CommitAsync(prepared);
            backupDirectory = result.BackupDirectory;
            committed = true;
            SaveEditApplied?.Invoke(
                EditorText.Format("BattleMapView_ForceTown_008", profile.ProfileId) +
                EditorText.Format("BattleMapView_ForceTown_009", profile.ProfileDirectory, prepared.SessionId) +
                EditorText.Format("BattleMapView_ForceTown_010", prepared.Preview.PreviousRaidDungeon, result.BackupDirectory));
            ShowUnavailableState(
                EditorText.Format("BattleMapView_ForceTown_011", profile.ProfileId),
                EditorText.Get("BattleMapView_ForceTown_012"));
        }
        catch (Exception ex)
        {
            CrashDiagnostics.RecordException("Force town: " + (committed ? "post-commit UI" : "edit"), ex,
                EditorText.Format("BattleMapView_ForceTown_013", prepared?.SessionId ?? EditorText.Get("BattleMapView_Commands_092"), profile.ProfileId) +
                EditorText.Format("BattleMapView_ForceTown_014", profile.ProfileDirectory, committed, backupDirectory ?? EditorText.Get("BattleMapView_Commands_095")));
            MapSelectionTextBlock.Text = committed
                ? EditorText.Get("BattleMapView_ForceTown_015")
                : ex is AggregateException
                    ? EditorText.Get("BattleMapView_ForceTown_016")
                    : EditorText.Get("BattleMapView_ForceTown_017");
            ThemedDialog.ShowMessage(
                owner,
                ex.Message,
                committed ? EditorText.Get("BattleMapView_ForceTown_018") : EditorText.Get("BattleMapView_ForceTown_019"),
                ThemedDialogKind.Error);
        }
        finally
        {
            if (gateTaken)
            {
                _refreshGate.Release();
            }
            // A successful return hides the map, but the same canvas is reused on the next raid.
            _isApplyingEdit = false;
            MapCanvas.IsHitTestVisible = true;
            SaveEditBusyChanged?.Invoke(false);
            if (generation == _profileGeneration)
            {
                ForceTownButton.IsEnabled = _currentSnapshot is not null;
                StartProfileMonitoring();
            }
        }
    }
}
