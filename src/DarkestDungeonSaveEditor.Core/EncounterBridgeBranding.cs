namespace DarkestDungeonSaveEditor.Core;

internal static class EncounterBridgeBranding
{
    internal const string PreviewFileName = "preview_icon.png";

    internal static void WritePreview(string packageDirectory)
    {
        using var icon = typeof(EncounterBridgeBranding).Assembly
            .GetManifestResourceStream("DarkestDungeonSaveEditor.BridgePreview.png")
            ?? throw new InvalidOperationException("程序包缺少存档编辑器图标，无法生成 Bridge 封面。");
        using var output = new FileStream(Path.Combine(packageDirectory, PreviewFileName),
            FileMode.Create, FileAccess.Write, FileShare.None);
        icon.CopyTo(output);
        output.Flush(flushToDisk: true);
    }
}
