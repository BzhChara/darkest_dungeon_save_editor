namespace DarkestDungeonSaveEditor.Core;

public static partial class ContentFileInventory
{
    public static string FormatSummary(ContentFileInventorySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var extras = snapshot.Mods.SelectMany(mod => mod.Files)
            .Where(file => file.ManifestMatch == ContentManifestMatch.Unlisted).ToArray();
        return $"文件清点：已定位 Mod 来源 {snapshot.Mods.Count} 个（不含无法定位的启用记录）；" +
               $"有清单 {snapshot.Mods.Count(mod => mod.HasManifest)} 个；" +
               $"确认无清单 {snapshot.Mods.Count(mod => !mod.HasManifest && mod.IsComplete)} 个；" +
               $"实际文件 {snapshot.Mods.Sum(mod => mod.Files.Count(file => file.ActualLength.HasValue))} 个；" +
               $"清单外数据/译文文件 {extras.Count(file => file.IsContentCandidate)} 个（仅盘点、未加载）；" +
               $"清单引用但文件缺失 {snapshot.Mods.Sum(mod => mod.Files.Count(file => file.ManifestMatch == ContentManifestMatch.Missing))} 个" +
               $"（当前内容范围内 {snapshot.Mods.Sum(mod => mod.Files.Count(file => file.ManifestMatch == ContentManifestMatch.Missing && file.IsContentCandidate))} 个）；" +
               $"扫描不完整 {snapshot.Mods.Count(mod => !mod.IsComplete)} 个。差异明细已写入完整日志，未改变内容纳入规则。";
    }

    public static IEnumerable<string> FormatDetails(ContentFileInventorySnapshot snapshot) =>
        FormatLogDetails(snapshot).Select(entry => entry.Message);

    public static IEnumerable<DiagnosticLogEntry> FormatLogDetails(ContentFileInventorySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        yield return new(DiagnosticLogLevel.Information,
            $"文件清点上下文：档案={Clean(snapshot.ProfileId)}；目录={Clean(snapshot.ProfileDirectory)}；" +
            $"游戏存档 SHA-256={Clean(snapshot.SourceGameSha256)}；清点完成 UTC={snapshot.ScannedAtUtc:O}。");
        yield return new(DiagnosticLogLevel.Information,
            "文件清点说明：候选只表示文件类型和目录范围，不代表已解析、游戏已加载或允许写入；" +
            "清单外数据/译文均不参与内容加载；不以相同内容或无引用判旧，不执行脚本、不改写清单。");
        foreach (var mod in snapshot.Mods)
        {
            var extras = mod.Files.Where(file => file.ManifestMatch == ContentManifestMatch.Unlisted).ToArray();
            var outside = extras.Count(file => !file.IsContentCandidate);
            var state = mod.HasManifest ? "有清单" : mod.IsComplete ? "无清单" : "清单状态未确认";
            yield return new(DiagnosticLogLevel.Information,
                         $"文件清点来源：{Clean(mod.SourceId)}；目录={Clean(mod.Directory)}；{state}；" +
                         $"完整={mod.IsComplete}；实际文件={mod.Files.Count(file => file.ActualLength.HasValue)}；" +
                         $"范围内数据/译文={mod.Files.Count(file => file.ActualLength.HasValue && file.IsContentCandidate)}；" +
                         $"清单外数据/译文文件={extras.Length - outside}（仅盘点、未加载）；清单外其他文件={outside}；" +
                         $"清单缺失={mod.Files.Count(file => file.ManifestMatch == ContentManifestMatch.Missing)}；" +
                         $"长度不同={mod.Files.Count(file => file.HasLengthMismatch)}。");
            foreach (var file in mod.Files)
            {
                var missingDesktopMetadata = file.ManifestMatch == ContentManifestMatch.Missing &&
                    file.Kind == ContentInventoryFileKind.Other &&
                    Path.GetFileName(file.RelativePath).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase);
                string? message = file.ManifestMatch switch
                {
                    ContentManifestMatch.Unlisted when file.IsContentCandidate => "清单外数据/译文文件：仅盘点、未加载",
                    ContentManifestMatch.Missing when missingDesktopMetadata => "清单缺失：Windows 文件夹设置 desktop.ini，不影响游戏内容",
                    ContentManifestMatch.Missing => file.IsContentCandidate ? "清单缺失：内容目录候选" : "清单缺失：当前内容范围外",
                    ContentManifestMatch.Unknown when file.IsContentCandidate => "文件对应关系未确认：清点不完整",
                    _ => null
                };
                if (message is not null)
                {
                    var level = !missingDesktopMetadata && file.ManifestMatch is ContentManifestMatch.Missing or ContentManifestMatch.Unknown
                        ? DiagnosticLogLevel.Warning
                        : DiagnosticLogLevel.Information;
                    yield return new(level, $"{message}；来源={Clean(mod.SourceId)}；路径={Clean(file.RelativePath)}；类型={file.Kind}。");
                }

                if (file.HasLengthMismatch)
                {
                    yield return new(DiagnosticLogLevel.Information,
                        $"清单长度不同：来源={Clean(mod.SourceId)}；路径={Clean(file.RelativePath)}；" +
                        $"清单={file.DeclaredLength}；实际={file.ActualLength}；仅记录差异，不据此判定损坏或过时。");
                }
            }

            foreach (var issue in mod.Issues)
            {
                yield return new(DiagnosticLogLevel.Warning, $"文件清点提示：来源={Clean(mod.SourceId)}；{Clean(issue)}");
            }
        }
    }

    private static string Clean(string value) => value.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
}
