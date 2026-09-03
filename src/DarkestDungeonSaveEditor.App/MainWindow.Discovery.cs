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

            AppendStatus(
                $"自动发现：游戏={snapshot.GameInstallations.Count}，档案={snapshot.Profiles.Count}。" +
                (snapshot.Issues.Count == 0 ? string.Empty : $" 提示={string.Join(" | ", snapshot.Issues)}"));
        }
        catch (Exception ex)
        {
            AppendStatus($"自动发现失败：{ex.Message}");
        }
    }

    private void BrowseGame_Click(object sender, RoutedEventArgs e)
    {
        BrowseInto(GameDirectoryTextBox, "选择 DarkestDungeon 游戏目录");
    }

    private void BrowseWorkshop_Click(object sender, RoutedEventArgs e)
    {
        BrowseInto(WorkshopDirectoryTextBox, "选择 262060 工坊内容目录");
    }

    private void BrowseLocalMods_Click(object sender, RoutedEventArgs e)
    {
        BrowseInto(LocalModDirectoryTextBox, "选择本地 Mod 根目录或单个 Mod 目录");
    }

    private void BrowseProfile_Click(object sender, RoutedEventArgs e)
    {
        BrowseInto(ProfileDirectoryTextBox, "选择 profile_* 存档目录");
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
