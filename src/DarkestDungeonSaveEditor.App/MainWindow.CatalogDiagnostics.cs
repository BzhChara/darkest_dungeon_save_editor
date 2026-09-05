using DarkestDungeonSaveEditor.Core;

namespace DarkestDungeonSaveEditor.App;

public partial class MainWindow
{
    private static ContentFileInventorySnapshot? ScanContentFilesForDiagnostics(ActiveContentSnapshot activeContent)
    {
        try
        {
            return ContentFileInventory.Scan(activeContent);
        }
        catch (Exception ex)
        {
            // Diagnostic discovery must not replace the existing catalog failure/availability rules.
            CrashDiagnostics.RecordException("LoadCatalog: file inventory diagnostics", ex);
            return null;
        }
    }

    private async Task RecordContentFileDiagnosticsAsync(ContentFileInventorySnapshot? inventory)
    {
        if (inventory is null)
        {
            AppendStatus("文件清点未完成，原因已记录到完整日志；业务目录仍按现有规则加载。");
            return;
        }

        AppendStatus(ContentFileInventory.FormatSummary(inventory));
        await Task.Run(() =>
        {
            foreach (var line in ContentFileInventory.FormatDetails(inventory))
            {
                CrashDiagnostics.RecordStatus(line);
            }
        });
    }
}
