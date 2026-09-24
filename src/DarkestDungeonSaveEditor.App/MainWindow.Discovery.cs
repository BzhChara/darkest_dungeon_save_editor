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
    private void Discover_Click(object sender, RoutedEventArgs e) => Discover();

    private void Discover()
    {
        try
        {
            var snapshot = SteamDiscovery.Discover();
            var game = snapshot.GameInstallations.FirstOrDefault();
            if (game is not null)
            {
                GameDirectoryTextBox.Text = game.GameDirectory;
                WorkshopDirectoryTextBox.Text = game.WorkshopDirectory;
                LocalModDirectoryTextBox.Text = game.DefaultLocalModDirectory;
            }

            var profile = SteamDiscovery.SelectDefaultProfile(snapshot.Profiles);
            if (profile is not null)
            {
                ProfileDirectoryTextBox.Text = profile.ProfileDirectory;
            }

            var discoverySummary = (game is not null, profile) switch
            {
                (true, { } selectedProfile) =>
                    EditorText.Format("MainWindow_Discovery_001", snapshot.GameInstallations.Count) +
                    EditorText.Format("MainWindow_Discovery_002", snapshot.Profiles.Count, selectedProfile.ProfileId),
                (true, null) =>
                    EditorText.Format("MainWindow_Discovery_003", snapshot.GameInstallations.Count),
                (false, { } selectedProfile) =>
                    EditorText.Format("MainWindow_Discovery_004", snapshot.Profiles.Count, selectedProfile.ProfileId),
                _ => EditorText.Get("MainWindow_Discovery_005")
            };
            AppendStatus(
                EditorText.Format("MainWindow_Discovery_006", discoverySummary) +
                (snapshot.Issues.Count == 0
                    ? string.Empty
                    : EditorText.Format("MainWindow_Discovery_007", string.Join(" | ", snapshot.Issues))),
                level: snapshot.Issues.Count == 0 ? DiagnosticLogLevel.Information : DiagnosticLogLevel.Warning);
        }
        catch (Exception ex)
        {
            CrashDiagnostics.RecordException("Discover: handled exception", ex);
            AppendStatusSafely(EditorText.Format("MainWindow_Discovery_008", ex.Message), "Discover: failure status", DiagnosticLogLevel.Error);
        }
    }

    private void BrowseGame_Click(object sender, RoutedEventArgs e)
    {
        BrowseInto(GameDirectoryTextBox, EditorText.Get("MainWindow_Discovery_009"));
    }

    private void BrowseWorkshop_Click(object sender, RoutedEventArgs e)
    {
        BrowseInto(WorkshopDirectoryTextBox, EditorText.Get("MainWindow_Discovery_010"));
    }

    private void BrowseLocalMods_Click(object sender, RoutedEventArgs e)
    {
        BrowseInto(LocalModDirectoryTextBox, EditorText.Get("MainWindow_Discovery_011"));
    }

    private void BrowseProfile_Click(object sender, RoutedEventArgs e)
    {
        BrowseInto(ProfileDirectoryTextBox, EditorText.Get("MainWindow_Discovery_012"));
    }

    private static void BrowseInto(TextBox target, string title)
    {
        var dialog = new OpenFolderDialog
        {
            Title = title,
            Multiselect = false
        };
        if (Directory.Exists(target.Text))
        {
            dialog.InitialDirectory = target.Text;
        }

        if (dialog.ShowDialog() == true)
        {
            target.Text = dialog.FolderName;
        }
    }

}
