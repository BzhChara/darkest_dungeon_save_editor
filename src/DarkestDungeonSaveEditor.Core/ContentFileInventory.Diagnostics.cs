namespace DarkestDungeonSaveEditor.Core;

public static partial class ContentFileInventory
{
    public static string FormatSummary(ContentFileInventorySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var extras = snapshot.Mods.SelectMany(mod => mod.Files)
            .Where(file => file.ManifestMatch == ContentManifestMatch.Unlisted).ToArray();
        return EditorText.Format("ContentFileInventory_Diagnostics_001", snapshot.Mods.Count) +
               EditorText.Format("ContentFileInventory_Diagnostics_002", snapshot.Mods.Count(mod => mod.HasManifest)) +
               EditorText.Format("ContentFileInventory_Diagnostics_003", snapshot.Mods.Count(mod => !mod.HasManifest && mod.IsComplete)) +
               EditorText.Format("ContentFileInventory_Diagnostics_004", snapshot.Mods.Sum(mod => mod.Files.Count(file => file.ActualLength.HasValue))) +
               EditorText.Format("ContentFileInventory_Diagnostics_005", extras.Count(file => file.IsContentCandidate)) +
               EditorText.Format("ContentFileInventory_Diagnostics_006", snapshot.Mods.Sum(mod => mod.Files.Count(file => file.ManifestMatch == ContentManifestMatch.Missing))) +
               EditorText.Format("ContentFileInventory_Diagnostics_007", snapshot.Mods.Sum(mod => mod.Files.Count(file => file.ManifestMatch == ContentManifestMatch.Missing && file.IsContentCandidate))) +
               EditorText.Format("ContentFileInventory_Diagnostics_008", snapshot.Mods.Count(mod => !mod.IsComplete));
    }

    public static IEnumerable<string> FormatDetails(ContentFileInventorySnapshot snapshot) =>
        FormatLogDetails(snapshot).Select(entry => entry.Message);

    public static IEnumerable<DiagnosticLogEntry> FormatLogDetails(ContentFileInventorySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        yield return new(DiagnosticLogLevel.Information,
            EditorText.Format("ContentFileInventory_Diagnostics_009", Clean(snapshot.ProfileId), Clean(snapshot.ProfileDirectory)) +
            EditorText.Format("ContentFileInventory_Diagnostics_010", Clean(snapshot.SourceGameSha256), snapshot.ScannedAtUtc));
        yield return new(DiagnosticLogLevel.Information,
            EditorText.Get("ContentFileInventory_Diagnostics_011") +
            EditorText.Get("ContentFileInventory_Diagnostics_012"));
        foreach (var mod in snapshot.Mods)
        {
            var extras = mod.Files.Where(file => file.ManifestMatch == ContentManifestMatch.Unlisted).ToArray();
            var outside = extras.Count(file => !file.IsContentCandidate);
            var state = mod.HasManifest ? EditorText.Get("ContentFileInventory_Diagnostics_013") : mod.IsComplete ? EditorText.Get("ContentFileInventory_Diagnostics_014") : EditorText.Get("ContentFileInventory_Diagnostics_015");
            yield return new(DiagnosticLogLevel.Information,
                         EditorText.Format("ContentFileInventory_Diagnostics_016", Clean(mod.SourceId), Clean(mod.Directory), state) +
                         EditorText.Format("ContentFileInventory_Diagnostics_017", mod.IsComplete, mod.Files.Count(file => file.ActualLength.HasValue)) +
                         EditorText.Format("ContentFileInventory_Diagnostics_018", mod.Files.Count(file => file.ActualLength.HasValue && file.IsContentCandidate)) +
                         EditorText.Format("ContentFileInventory_Diagnostics_019", extras.Length - outside, outside) +
                         EditorText.Format("ContentFileInventory_Diagnostics_020", mod.Files.Count(file => file.ManifestMatch == ContentManifestMatch.Missing)) +
                         EditorText.Format("ContentFileInventory_Diagnostics_021", mod.Files.Count(file => file.HasLengthMismatch)));
            foreach (var file in mod.Files)
            {
                var missingDesktopMetadata = file.ManifestMatch == ContentManifestMatch.Missing &&
                    file.Kind == ContentInventoryFileKind.Other &&
                    Path.GetFileName(file.RelativePath).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase);
                string? message = file.ManifestMatch switch
                {
                    ContentManifestMatch.Unlisted when file.IsContentCandidate => EditorText.Get("ContentFileInventory_Diagnostics_022"),
                    ContentManifestMatch.Missing when missingDesktopMetadata => EditorText.Get("ContentFileInventory_Diagnostics_023"),
                    ContentManifestMatch.Missing => file.IsContentCandidate ? EditorText.Get("ContentFileInventory_Diagnostics_024") : EditorText.Get("ContentFileInventory_Diagnostics_025"),
                    ContentManifestMatch.Unknown when file.IsContentCandidate => EditorText.Get("ContentFileInventory_Diagnostics_026"),
                    _ => null
                };
                if (message is not null)
                {
                    var level = !missingDesktopMetadata && file.ManifestMatch is ContentManifestMatch.Missing or ContentManifestMatch.Unknown
                        ? DiagnosticLogLevel.Warning
                        : DiagnosticLogLevel.Information;
                    yield return new(level, EditorText.Format("ContentFileInventory_Diagnostics_027", message, Clean(mod.SourceId), Clean(file.RelativePath), file.Kind));
                }

                if (file.HasLengthMismatch)
                {
                    yield return new(DiagnosticLogLevel.Information,
                        EditorText.Format("ContentFileInventory_Diagnostics_028", Clean(mod.SourceId), Clean(file.RelativePath)) +
                        EditorText.Format("ContentFileInventory_Diagnostics_029", file.DeclaredLength, file.ActualLength));
                }
            }

            foreach (var issue in mod.Issues)
            {
                yield return new(DiagnosticLogLevel.Warning, EditorText.Format("ContentFileInventory_Diagnostics_030", Clean(mod.SourceId), Clean(issue)));
            }
        }
    }

    private static string Clean(string value) => value.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
}
