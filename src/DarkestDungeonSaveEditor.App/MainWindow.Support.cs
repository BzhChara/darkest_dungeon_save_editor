using System.Windows;

namespace DarkestDungeonSaveEditor.App;

public partial class MainWindow
{
    private void BuyCoffee_Click(object sender, RoutedEventArgs e) => new SupportDialog(this).ShowDialog();
}
