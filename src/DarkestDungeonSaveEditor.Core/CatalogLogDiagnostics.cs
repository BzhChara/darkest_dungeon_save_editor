namespace DarkestDungeonSaveEditor.Core;

/// <summary>Presentation-only grouping; the catalogs' original Issues remain authoritative.</summary>
public static partial class CatalogLogDiagnostics
{
    internal const string LocalizationEvidenceMarker = CatalogIssueCode.LocalizationEvidence;

    public static IReadOnlyList<DiagnosticLogEntry> Summarize(
        IEnumerable<(string Module, IReadOnlyList<string> Issues)> inputs)
    {
        var batches = inputs.Select(input => (input.Module, Issues: input.Issues.ToArray())).ToArray();
        var missingPaths = batches.SelectMany(input => input.Issues).Select(ReadMissingFilePath)
            .OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var groups = new Dictionary<string, LogGroup>(StringComparer.Ordinal);
        foreach (var (module, issues) in batches)
        {
            foreach (var raw in issues.Distinct(StringComparer.Ordinal))
            {
                if (AddEncounterSlotNotice(raw, module, groups)) continue;
                var parsed = DescribeFileAccess(raw, missingPaths) ?? Describe(raw);
                if (!groups.TryGetValue(parsed.Key, out var group))
                {
                    group = new LogGroup(parsed.Level, parsed.Message);
                    groups.Add(parsed.Key, group);
                }
                group.Modules.Add(module);
                if (parsed.Evidence is not null)
                {
                    if (!group.Evidence.TryGetValue(parsed.Evidence, out var modules))
                    {
                        modules = new HashSet<string>(StringComparer.Ordinal);
                        group.Evidence.Add(parsed.Evidence, modules);
                    }
                    modules.Add(module);
                }
            }
        }

        return groups.Values.Select(group => new DiagnosticLogEntry(group.Level,
                (group.Level == DiagnosticLogLevel.Information ? EditorText.Get("CatalogLogDiagnostics_001") : EditorText.Get("CatalogLogDiagnostics_002")) +
                group.Message + FormatLocations(group) + EditorText.Get("CatalogLogDiagnostics_003") + string.Join("、", group.Modules.Order(StringComparer.Ordinal)) +
                (group.Evidence.Count == 0 ? string.Empty : "；" + string.Join("；", group.Evidence.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => EditorText.Format("CatalogLogDiagnostics_004", string.Join("、", pair.Value.Order(StringComparer.Ordinal)), pair.Key))))))
            .OrderByDescending(entry => entry.Level)
            .ThenBy(entry => entry.Message, StringComparer.Ordinal)
            .ToArray();
    }

    public static string FormatSourceCounts(ActiveContentSnapshot content)
    {
        var modSources = content.Sources.Count(source => source.Kind is "local" or "workshop");
        var invalidRecords = content.Issues.Count(issue =>
            issue.StartsWith("Ignored malformed applied_ugcs_1_0 entry:", StringComparison.Ordinal) ||
            issue.StartsWith("Ignored incomplete applied_ugcs_1_0 entry:", StringComparison.Ordinal));
        // A gap can also mean an invalid/ambiguous/duplicate entry, not just an uninstalled Mod.
        var unresolved = content.Issues.Count(issue =>
            issue.StartsWith("Enabled Workshop item", StringComparison.Ordinal) ||
            issue.StartsWith("Enabled local Mod", StringComparison.Ordinal) ||
            issue.StartsWith("Unsupported enabled Mod source", StringComparison.Ordinal));
        var duplicates = content.Issues.Count(issue =>
            issue.StartsWith("Enabled content directory appears more than once and was scanned once:", StringComparison.Ordinal));
        return EditorText.Format("CatalogLogDiagnostics_005", (long)content.AppliedModCount + invalidRecords, content.AppliedModCount, invalidRecords) +
            EditorText.Format("CatalogLogDiagnostics_006", modSources) +
            EditorText.Format("CatalogLogDiagnostics_007", unresolved, duplicates) +
            EditorText.Format("CatalogLogDiagnostics_008", content.Sources.Count);
    }

    private static (string Key, DiagnosticLogLevel Level, string Message, string? Evidence) Describe(string raw)
    {
        foreach (var prefix in new[] { "Failed to read localization '", "Failed to read hero names '" })
        {
            if (!raw.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }
            var end = raw.IndexOf("': ", prefix.Length, StringComparison.Ordinal);
            if (end < 0)
            {
                break;
            }
            var path = raw[prefix.Length..end];
            var reasonAndEvidence = raw[(end + 3)..];
            var marker = reasonAndEvidence.IndexOf(LocalizationEvidenceMarker, StringComparison.Ordinal);
            var reason = marker < 0 ? reasonAndEvidence : reasonAndEvidence[..marker];
            var evidence = marker < 0 ? null : Clean(reasonAndEvidence[(marker + LocalizationEvidenceMarker.Length)..]);
            // Same Windows path + exact failure reason. Different reasons are not collapsed.
            return ("localization\0" + path.ToUpperInvariant() + "\0" + reason, DiagnosticLogLevel.Warning,
                EditorText.Format("CatalogLogDiagnostics_009", Clean(path), Clean(reason)) +
                EditorText.Get("CatalogLogDiagnostics_010"),
                evidence);
        }

        const string partialPrefix = CatalogIssueCode.PartialLocalization;
        if (raw.StartsWith(partialPrefix, StringComparison.Ordinal))
        {
            var end = raw.IndexOf("';", partialPrefix.Length, StringComparison.Ordinal);
            if (end >= 0)
            {
                return ("localization-partial\0" + raw[partialPrefix.Length..end].ToUpperInvariant() + "\0" + raw[(end + 2)..],
                    DiagnosticLogLevel.Warning,
                    EditorText.Format("CatalogLogDiagnostics_PartialFile", Clean(raw[partialPrefix.Length..end]), Clean(raw[(end + 2)..])), null);
            }
        }

        if (raw.StartsWith(CatalogIssueCode.TownRaidResidue, StringComparison.Ordinal))
        {
            return (raw, DiagnosticLogLevel.Information, Clean(raw[CatalogIssueCode.TownRaidResidue.Length..]), null);
        }
        if (raw.StartsWith("Enabled content directory appears more than once and was scanned once:", StringComparison.Ordinal))
        {
            return (raw, DiagnosticLogLevel.Information, Clean(raw), null);
        }
        if (raw.StartsWith("Enabled Workshop item is not installed: ", StringComparison.Ordinal))
        {
            return (raw, DiagnosticLogLevel.Warning,
                EditorText.Get("CatalogLogDiagnostics_011") + raw["Enabled Workshop item is not installed: ".Length..] +
                EditorText.Get("CatalogLogDiagnostics_012"), null);
        }
        return (raw, DiagnosticLogLevel.Warning, Clean(raw), null);
    }

    private static string Clean(string value) => value.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');

    private sealed class LogGroup(DiagnosticLogLevel level, string message)
    {
        public DiagnosticLogLevel Level { get; } = level;
        public string Message { get; } = message;
        public HashSet<string> Modules { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, HashSet<string>> Evidence { get; } = new(StringComparer.Ordinal);
        public SortedSet<(int Line, int Record)> Locations { get; } = [];
    }
}
