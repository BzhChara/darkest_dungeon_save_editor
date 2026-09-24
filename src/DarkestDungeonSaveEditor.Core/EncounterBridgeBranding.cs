namespace DarkestDungeonSaveEditor.Core;

internal static class EncounterBridgeBranding
{
    internal const string PreviewFileName = "preview_icon.png";

    internal static void WritePreview(string packageDirectory)
    {
        using var icon = typeof(EncounterBridgeBranding).Assembly
            .GetManifestResourceStream("DarkestDungeonSaveEditor.BridgePreview.png")
            ?? throw new InvalidOperationException(EditorText.Get("EncounterBridgeBranding_001"));
        using var output = new FileStream(Path.Combine(packageDirectory, PreviewFileName),
            FileMode.Create, FileAccess.Write, FileShare.None);
        icon.CopyTo(output);
        output.Flush(flushToDisk: true);
    }
}
