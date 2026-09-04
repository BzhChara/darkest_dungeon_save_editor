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
            MapSelectionTextBlock.Text = "当前没有可用于强制返回城镇的完整副本快照。";
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
                $"程序将完整备份 {profile.ProfileId}，再把该档案的下次读档入口强制设置为城镇。" +
                Environment.NewLine +
                "这不是正常撤退结算：当前副本的未结算进度和战利品可能丢失，但人物、任务、周数和背包不会由编辑器额外改写。" +
                Environment.NewLine +
                "写入后请正常启动游戏并载入这个档案，让游戏完成回城。",
                "确认强制返回城镇"))
        {
            return;
        }

        var generation = _profileGeneration;
        var gateTaken = false;
        var committed = false;
        _isApplyingEdit = true;
        SaveEditBusyChanged?.Invoke(true);
        CloseActiveContextMenu();
        CancelScheduledRefreshRetry();
        StopProfileMonitoring();
        MapCanvas.IsHitTestVisible = false;
        ForceTownButton.IsEnabled = false;
        MapSelectionTextBlock.Text = "正在验证并备份当前档案，然后写入回城状态……";

        try
        {
            await _refreshGate.WaitAsync();
            gateTaken = true;
            if (generation != _profileGeneration ||
                !ReferenceEquals(snapshot, _currentSnapshot) ||
                !ReferenceEquals(profile, _profile))
            {
                throw new InvalidOperationException("地图已刷新；请在最新地图上重新执行强制返回城镇。");
            }

            var prepared = await service.PrepareAsync(profile, snapshot);
            var result = await service.CommitAsync(prepared);
            committed = true;
            SaveEditApplied?.Invoke(
                $"强制回城状态已写入：档案={profile.ProfileId}；" +
                $"原副本={prepared.Preview.PreviousRaidDungeon}；备份={result.BackupDirectory}");
            ShowUnavailableState(
                $"已将 {profile.ProfileId} 的下次读档入口设置为城镇。现在请启动游戏并载入该档案。",
                "回城状态已写入");
        }
        catch (Exception ex)
        {
            MapSelectionTextBlock.Text = committed
                ? "回城状态已写入，但界面未能切换；请重新加载内容目录。"
                : "强制回城未应用；存档保持原状或已自动恢复。";
            ThemedDialog.ShowMessage(
                owner,
                ex.Message,
                committed ? "界面刷新失败" : "强制返回城镇失败",
                ThemedDialogKind.Error);
        }
        finally
        {
            if (gateTaken)
            {
                _refreshGate.Release();
            }
            _isApplyingEdit = false;
            SaveEditBusyChanged?.Invoke(false);
            if (generation == _profileGeneration)
            {
                if (!committed)
                {
                    MapCanvas.IsHitTestVisible = true;
                    ForceTownButton.IsEnabled = _currentSnapshot is not null;
                }
                StartProfileMonitoring();
            }
        }
    }
}
