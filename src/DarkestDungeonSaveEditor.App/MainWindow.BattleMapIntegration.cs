using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DarkestDungeonSaveEditor.Core;
using Microsoft.Win32;

namespace DarkestDungeonSaveEditor.App;

public partial class MainWindow : Window
{
    private static void BattleMapPanel_SnapshotRefreshed(BattleMapSnapshot snapshot)
    {
        CrashDiagnostics.RecordStatus(
            $"战斗地图快照：档案目录={snapshot.ProfileDirectory}；" +
            $"地区={snapshot.DungeonId}；难度={snapshot.Difficulty}；长度={snapshot.Length}；" +
            $"房间={snapshot.RoomCount}；走廊={snapshot.CorridorCount}；格子={snapshot.TileCount}；" +
            $"队伍位置={snapshot.PartyAreaId ?? "未解析"}/tile{snapshot.PartyTileIndex?.ToString(CultureInfo.InvariantCulture) ?? "?"}；" +
            $"persist.map.json SHA-256={snapshot.MapSha256}；" +
            $"persist.raid.json SHA-256={snapshot.RaidSha256}；提示={snapshot.Issues.Count}");
        foreach (var issue in snapshot.Issues)
        {
            CrashDiagnostics.RecordStatus($"战斗地图解析提示：{issue}");
        }
    }

    private void BattleMapPanel_ActiveContentChanged(ActiveContentSnapshot activeContent)
    {
        _activeContentSnapshot = activeContent;
        _catalogProfileDirectory = activeContent.Profile.ProfileDirectory;
        _catalogGameSaveSha256 = activeContent.SourceGameSha256;
        CrashDiagnostics.RecordStatus(
            $"托管遭遇 Bridge 已同步活动内容：档案={activeContent.Profile.ProfileId}；" +
            $"活动来源={activeContent.Sources.Count}；启用 Mod={activeContent.AppliedModCount}；" +
            $"persist.game.json SHA-256={activeContent.SourceGameSha256}");
    }

    private void BattleMapPanel_SaveEditApplied(string message) =>
        AppendStatusSafely(message, "BattleMap: applied save edit");

    private void BattleMapPanel_SaveEditBusyChanged(bool isBusy) => SetBusy(isBusy);

    private void AppendStatus(string message, bool persist = true)
    {
        if (persist)
        {
            CrashDiagnostics.RecordStatus(message);
        }

        StatusTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        StatusTextBox.ScrollToEnd();
    }

    private void AppendStatusSafely(string message, string diagnosticSource)
    {
        try
        {
            AppendStatus(message);
        }
        catch (Exception ex)
        {
            CrashDiagnostics.RecordException(diagnosticSource, ex, message);
        }
    }

}
