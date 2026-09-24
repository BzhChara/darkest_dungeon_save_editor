using System.Globalization;

namespace DarkestDungeonSaveEditor.Core;

public static partial class CatalogLogDiagnostics
{
    private static bool AddEncounterSlotNotice(string raw, string module, Dictionary<string, LogGroup> groups)
    {
        const string prefix = CatalogIssueCode.EncounterSlots;
        const string recordMarker = CatalogIssueCode.Record;
        if (!raw.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var recordStart = raw.LastIndexOf(recordMarker, StringComparison.Ordinal);
        if (recordStart < prefix.Length) return false;
        var lineStart = raw.LastIndexOf(':', recordStart);
        if (lineStart <= prefix.Length ||
            !int.TryParse(raw.AsSpan(lineStart + 1, recordStart - lineStart - 1), NumberStyles.None,
                CultureInfo.InvariantCulture, out var line) ||
            !int.TryParse(raw.AsSpan(recordStart + recordMarker.Length), NumberStyles.None,
                CultureInfo.InvariantCulture, out var record)) return false;

        var path = raw[prefix.Length..lineStart];
        var key = "encounter-slots\0" + path.ToUpperInvariant();
        if (!groups.TryGetValue(key, out var group))
        {
            group = new LogGroup(DiagnosticLogLevel.Information,
                EditorText.Format("CatalogLogDiagnostics_FileIssues_001", Clean(path)) +
                EditorText.Get("CatalogLogDiagnostics_FileIssues_002"));
            groups.Add(key, group);
        }
        group.Modules.Add(module);
        group.Locations.Add((line, record));
        return true;
    }

    private static string FormatLocations(LogGroup group) => group.Locations.Count == 0 ? string.Empty :
        EditorText.Format("CatalogLogDiagnostics_FileIssues_003", group.Locations.Count) +
        string.Join(", ", group.Locations.Select(location => $"{location.Line}/{location.Record}"));

    private static string? ReadMissingFilePath(string raw)
    {
        foreach (var marker in new[] { " listed by Mod is missing: ", " listed by active Mod is missing: " })
        {
            var index = raw.IndexOf(marker, StringComparison.Ordinal);
            if (index > 0 && index + marker.Length < raw.Length) return raw[(index + marker.Length)..];
        }
        return null;
    }

    private static (string Key, DiagnosticLogLevel Level, string Message, string? Evidence)? DescribeFileAccess(
        string raw, IReadOnlySet<string> missingPaths)
    {
        var path = ReadMissingFilePath(raw);
        if (path is null)
        {
            const string quantityPrefix = "Failed to read quantity-item reference file '";
            const string monsterPrefix = "Monster definition could not be read for boss classification: ";
            if (raw.StartsWith(quantityPrefix, StringComparison.Ordinal))
            {
                var end = raw.IndexOf("': ", quantityPrefix.Length, StringComparison.Ordinal);
                if (end > quantityPrefix.Length) path = raw[quantityPrefix.Length..end];
            }
            else if (raw.StartsWith(monsterPrefix, StringComparison.Ordinal))
            {
                // Match a reported missing path, not parentheses inside its filename.
                path = missingPaths.OrderByDescending(candidate => candidate.Length).FirstOrDefault(candidate =>
                    raw.AsSpan(monsterPrefix.Length).StartsWith(candidate + " (", StringComparison.OrdinalIgnoreCase));
            }
            if (path is null || !missingPaths.Contains(path)) return null;
        }

        // Only an explicit missing-file report joins dependent read failures.
        // Keep every original reason/module; unrelated read errors stay separate.
        return ("missing-file\0" + path.ToUpperInvariant(), DiagnosticLogLevel.Warning,
            EditorText.Format("CatalogLogDiagnostics_FileIssues_004", Clean(path)) +
            EditorText.Get("CatalogLogDiagnostics_FileIssues_005"), Clean(raw));
    }
}
