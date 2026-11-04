using System.Windows;

namespace DarkestDungeonSaveEditor.App;

// A selection is valid only while its profile is still active. ShowDialog
// pumps the dispatcher, so invalidation can run before Show returns.
internal sealed class ProfileSelectionDialog
{
    private Window? _open;

    internal bool Show(Window dialog)
    {
        _open = dialog;
        try { return dialog.ShowDialog() == true && ReferenceEquals(_open, dialog); }
        finally
        {
            if (ReferenceEquals(_open, dialog)) _open = null;
            if (dialog.IsVisible) dialog.Close();
        }
    }

    internal void Dismiss()
    {
        var dialog = _open;
        _open = null;
        if (dialog?.IsVisible == true) dialog.Close();
    }
}
