using DarkestDungeonSaveEditor.Core;

namespace DarkestDungeonSaveEditor.App;

public partial class MainWindow
{
    private static ContentFileInventorySnapshot? ScanContentFilesForDiagnostics(ActiveContentSnapshot activeContent, CancellationToken token)
    {
        try
        {
            return ContentFileInventory.Scan(activeContent, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            // Diagnostic discovery must not replace the existing catalog failure/availability rules.
            CrashDiagnostics.RecordException("LoadCatalog: file inventory diagnostics", ex);
            return null;
        }
    }

    private async Task RecordContentFileDiagnosticsAsync(ContentFileInventorySnapshot? inventory, CancellationToken token)
    {
        if (inventory is null)
        {
            AppendStatus(EditorText.Get("MainWindow_CatalogDiagnostics_001"), level: DiagnosticLogLevel.Warning);
            return;
        }

        AppendStatus(ContentFileInventory.FormatSummary(inventory),
            level: inventory.Mods.Any(mod => !mod.IsComplete) ? DiagnosticLogLevel.Warning : DiagnosticLogLevel.Information);
        await Task.Run(() =>
        {
            foreach (var entry in ContentFileInventory.FormatLogDetails(inventory))
            {
                token.ThrowIfCancellationRequested();
                CrashDiagnostics.RecordStatus(entry.Message, entry.Level);
            }
        }, token);
    }
}
