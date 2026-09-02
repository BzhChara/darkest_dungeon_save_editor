using System.Windows;
using System.Windows.Media;

namespace DarkestDungeonSaveEditor.App;

internal enum ThemedDialogKind
{
    Information,
    Warning,
    Error
}

public partial class ThemedDialog : Window
{
    private readonly bool _isConfirmation;

    private ThemedDialog(
        Window owner,
        string message,
        string title,
        ThemedDialogKind kind,
        bool isConfirmation)
    {
        ArgumentNullException.ThrowIfNull(owner);

        InitializeComponent();
        Owner = owner;
        Title = title;
        HeaderTitleTextBlock.Text = title;
        MessageTextBlock.Text = message;
        _isConfirmation = isConfirmation;

        ConfigureKind(kind);
        ConfigureButtons();
        Loaded += ThemedDialog_Loaded;
    }

    internal static bool Confirm(Window owner, string message, string title) =>
        new ThemedDialog(owner, message, title, ThemedDialogKind.Warning, isConfirmation: true)
            .ShowDialog() == true;

    internal static void ShowMessage(
        Window owner,
        string message,
        string title,
        ThemedDialogKind kind) =>
        _ = new ThemedDialog(owner, message, title, kind, isConfirmation: false).ShowDialog();

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        NativeWindowTheme.ApplyDarkTitleBar(this);
    }

    private void ConfigureKind(ThemedDialogKind kind)
    {
        var (headerBrushKey, marker, label, markerBrush) = kind switch
        {
            ThemedDialogKind.Information =>
                ("OliveBandBrush", "i", "信息  /  INFORMATION", FindResource("SuccessBrush") as Brush),
            ThemedDialogKind.Error =>
                ("RedBandBrush", "×", "错误  /  ERROR", FindResource("DangerBrush") as Brush),
            _ =>
                ("RedBandBrush", "!", "警示  /  WARNING", FindResource("WarningBrush") as Brush)
        };

        HeaderBackdrop.Background = FindResource(headerBrushKey) as Brush;
        KindMarkerTextBlock.Text = marker;
        KindLabelTextBlock.Text = label;
        KindMarkerTextBlock.Foreground = markerBrush ?? Brushes.White;
    }

    private void ConfigureButtons()
    {
        if (_isConfirmation)
        {
            CancelButton.Visibility = Visibility.Visible;
            CancelButton.IsCancel = true;
            CancelButton.IsDefault = true;
            ConfirmButton.IsDefault = false;
            ConfirmButton.IsCancel = false;
            return;
        }

        CancelButton.Visibility = Visibility.Collapsed;
        CancelButton.IsCancel = false;
        ConfirmButton.Content = "知道了";
        ConfirmButton.IsDefault = true;
        ConfirmButton.IsCancel = true;
    }

    private void ThemedDialog_Loaded(object sender, RoutedEventArgs e)
    {
        if (_isConfirmation)
        {
            CancelButton.Focus();
        }
        else
        {
            ConfirmButton.Focus();
        }
    }

    private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
