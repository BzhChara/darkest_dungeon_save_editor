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
    private readonly BattleMapLogTracker _battleMapLogTracker = new();

    private void BattleMapPanel_SnapshotRefreshed(BattleMapSnapshot snapshot)
    {
        try
        {
            foreach (var entry in _battleMapLogTracker.Observe(snapshot))
            {
                CrashDiagnostics.RecordStatus(entry.Message, entry.Level);
            }
        }
        catch (Exception ex)
        {
            CrashDiagnostics.RecordException("BattleMap: snapshot diagnostics", ex,
                EditorText.Format("MainWindow_BattleMapIntegration_001", snapshot.ProfileDirectory, snapshot.MapSavePath, snapshot.RaidSavePath) +
                $"persist.map.json SHA-256={snapshot.MapSha256}；persist.raid.json SHA-256={snapshot.RaidSha256}");
        }
    }

    private void BattleMapPanel_ActiveContentChanged(ActiveContentSnapshot activeContent)
    {
        // A managed Bridge changes active sources. Publish all catalogs together after the write.
        RequestProfileSync(refreshContent: true);
        CrashDiagnostics.RecordStatus(
            EditorText.Format("MainWindow_BattleMapIntegration_002", activeContent.Profile.ProfileId) +
            CatalogLogDiagnostics.FormatSourceCounts(activeContent) + "；" +
            $"persist.game.json SHA-256={activeContent.SourceGameSha256}");
    }

    private void BattleMapPanel_SaveEditApplied(string message) =>
        AppendStatusSafely(message, "BattleMap: applied save edit");

    private void BattleMapPanel_SaveEditBusyChanged(bool isBusy)
    {
        if (!isBusy)
        {
            RequestProfileSync(invalidatePreview: false);
        }
        SetBusy(isBusy);
    }

    private void AppendStatus(
        string message,
        bool persist = true,
        DiagnosticLogLevel level = DiagnosticLogLevel.Information)
    {
        if (persist)
        {
            CrashDiagnostics.RecordStatus(message, level);
        }

        StatusTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        StatusTextBox.ScrollToEnd();
    }

    private void AppendStatusSafely(
        string message,
        string diagnosticSource,
        DiagnosticLogLevel level = DiagnosticLogLevel.Information)
    {
        try
        {
            AppendStatus(message, level: level);
        }
        catch (Exception ex)
        {
            CrashDiagnostics.RecordException(diagnosticSource, ex, message);
        }
    }

}
