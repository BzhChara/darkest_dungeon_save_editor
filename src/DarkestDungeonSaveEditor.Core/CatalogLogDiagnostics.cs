namespace DarkestDungeonSaveEditor.Core;

/// <summary>Presentation-only grouping; the catalogs' original Issues remain authoritative.</summary>
public static class CatalogLogDiagnostics
{
    internal const string LocalizationEvidenceMarker = "\n本地化读取补充：";

    public static IReadOnlyList<DiagnosticLogEntry> Summarize(
        IEnumerable<(string Module, IReadOnlyList<string> Issues)> inputs)
    {
        var groups = new Dictionary<string, LogGroup>(StringComparer.Ordinal);
        foreach (var (module, issues) in inputs)
        {
            foreach (var raw in issues.Distinct(StringComparer.Ordinal))
            {
                var parsed = Describe(raw);
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
                (group.Level == DiagnosticLogLevel.Information ? "目录说明：" : "目录警告：") +
                group.Message + "；报告模块=" + string.Join("、", group.Modules.Order(StringComparer.Ordinal)) +
                (group.Evidence.Count == 0 ? string.Empty : "；" + string.Join("；", group.Evidence.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => $"读取范围[{string.Join("、", pair.Value.Order(StringComparer.Ordinal))}]：{pair.Key}")))))
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
        return $"存档启用记录 {(long)content.AppliedModCount + invalidRecords} 条（已解析 {content.AppliedModCount}，格式无效 {invalidRecords}）；" +
            $"已定位 Mod 来源 {modSources} 个；" +
            $"无法定位/识别记录 {unresolved} 条；重复来源合并 {duplicates} 条；" +
            $"活动来源共 {content.Sources.Count} 个（含本体、模式和 DLC）";
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
                $"本地化文件读取失败；文件={Clean(path)}；原因={Clean(reason)}；" +
                "其他有效译文仍按既定规则使用，不代表所有名称缺失；人物随机姓名候选另由 XML 提供",
                evidence);
        }

        const string partialPrefix = "本地化部分读取：'";
        if (raw.StartsWith(partialPrefix, StringComparison.Ordinal))
        {
            var end = raw.IndexOf("'；", partialPrefix.Length, StringComparison.Ordinal);
            if (end >= 0)
            {
                return ("localization-partial\0" + raw[partialPrefix.Length..end].ToUpperInvariant() + "\0" + raw[(end + 2)..],
                    DiagnosticLogLevel.Warning, Clean(raw), null);
            }
        }

        if (raw.StartsWith("当前为小镇状态；残留副本文件已忽略", StringComparison.Ordinal) ||
            raw.StartsWith("Enabled content directory appears more than once and was scanned once:", StringComparison.Ordinal))
        {
            return (raw, DiagnosticLogLevel.Information, Clean(raw), null);
        }
        if (raw.StartsWith("Enabled Workshop item is not installed: ", StringComparison.Ordinal))
        {
            return (raw, DiagnosticLogLevel.Warning,
                "存档仍启用该工坊项目，但本机未找到安装目录；项目=" + raw["Enabled Workshop item is not installed: ".Length..] +
                "；该来源未加载，请核对订阅/安装状态；其余来源继续加载", null);
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
    }
}
