using System.Windows.Controls;
using System.Windows;
using DarkestDungeonSaveEditor.Core;

namespace DarkestDungeonSaveEditor.App;

public partial class MainWindow
{
    private readonly string _languageSettingsPath;
    private string _savedLanguagePreference = "system";

    private void InitializeLanguageSelection()
    {
        _savedLanguagePreference = EditorLanguageSettings.Load(_languageSettingsPath, out _);
        UpdateLanguageMenuChecks();
    }

    private void LanguageButton_Click(object sender, RoutedEventArgs e)
    {
        LanguageButton.ContextMenu.PlacementTarget = LanguageButton;
        LanguageButton.ContextMenu.IsOpen = true;
    }

    private void UpdateLanguageMenuChecks()
    {
        foreach (var item in LanguageButton.ContextMenu.Items.OfType<MenuItem>())
            item.IsChecked = item.Tag is string preference && preference == _savedLanguagePreference;
    }

    private void LanguageMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string preference }) return;
        if (preference == _savedLanguagePreference)
        {
            UpdateLanguageMenuChecks();
            return;
        }
        try
        {
            EditorLanguageSettings.Save(_languageSettingsPath, preference);
            _savedLanguagePreference = preference;
            UpdateLanguageMenuChecks();
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            UpdateLanguageMenuChecks();
            CrashDiagnostics.RecordException("Language preference: save", ex);
            ThemedDialog.ShowMessage(this, EditorText.Format("Language_SaveFailed", ex.Message),
                EditorText.Get("Language_SaveFailedTitle"), ThemedDialogKind.Error);
        }
    }
}
