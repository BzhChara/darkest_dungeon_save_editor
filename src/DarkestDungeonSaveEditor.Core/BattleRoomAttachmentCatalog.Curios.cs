using System.Text;
using Microsoft.VisualBasic.FileIO;

namespace DarkestDungeonSaveEditor.Core;

public static partial class BattleRoomAttachmentCatalog
{
    private static IReadOnlyList<EffectiveContentFile> ResolveEffectiveCurioFiles(
        IReadOnlyList<ActiveContentSource> sources, List<string> issues)
    {
        var enabledDlcPrefixes = ContentFileOverlay.GetEnabledDlcPrefixes(sources);
        return ContentFileOverlay.Resolve(sources.SelectMany(source =>
                EnumeratePropFiles(source, enabledDlcPrefixes, issues, "curios", "*.csv", ".csv")
                    .Where(path => path.EndsWith("curio_props.csv", StringComparison.OrdinalIgnoreCase) ||
                        path.EndsWith("curio_type_library.csv", StringComparison.OrdinalIgnoreCase))
                    .Select(path => new ContentFileCandidate(source, path))),
            "Curio resource", issues);
    }

    private static CurioResources ReadCurioResources(
        IReadOnlyList<EffectiveContentFile> files, List<string> issues)
    {
        // Save resource hashes are case-sensitive; only filesystem paths are not.
        var types = new HashSet<string>(StringComparer.Ordinal);
        var props = new Dictionary<string, CurioProp>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            var isTypeLibrary = file.Path.EndsWith("curio_type_library.csv", StringComparison.OrdinalIgnoreCase);
            var expectsTypeId = false;
            foreach (var row in ReadCurioCsv(file.Path, issues))
            {
                var fields = row.Fields;
                if (isTypeLibrary)
                {
                    // The native library is a series of blocks, not a flat CSV table.
                    // Its ID is column 3 on the row immediately after ID STRING.
                    if (expectsTypeId)
                    {
                        if (fields.Length > 4 && !string.IsNullOrWhiteSpace(fields[2]) &&
                            (string.IsNullOrWhiteSpace(fields[4]) ||
                             fields[4].Equals("Nothing", StringComparison.OrdinalIgnoreCase)))
                        {
                            types.Add(fields[2]);
                        }
                        else
                        {
                            issues.Add($"奇物互动类型缺少有效 ID 行，已跳过该块：{file.Path}:{row.Line}");
                        }
                    }
                    expectsTypeId = fields.Length > 2 && fields[2].Equals("ID STRING", StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (fields.Length == 0 || string.IsNullOrWhiteSpace(fields[0]) ||
                    fields[0].Equals("Curio Prop Name", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (fields.Length < 5)
                {
                    issues.Add($"奇物道具映射列数不足，已跳过该行：{file.Path}:{row.Line}");
                    continue;
                }
                var prop = new CurioProp(file.Source, fields[1], fields[2], fields[3], false);
                if (!props.TryGetValue(fields[0], out var previous) ||
                    ContentFileOverlay.ComparePriority(file.Source, previous.Source) > 0)
                {
                    props[fields[0]] = prop;
                }
                else if (ContentFileOverlay.ComparePriority(file.Source, previous.Source) == 0 &&
                    (prop.SpriteId != previous.SpriteId || prop.TypeId != previous.TypeId || prop.NameId != previous.NameId))
                {
                    props[fields[0]] = previous with { IsAmbiguous = true };
                }
            }
            if (expectsTypeId)
            {
                issues.Add($"奇物互动类型文件末尾缺少 ID 行：{file.Path}");
            }
        }
        return new CurioResources(types, props);
    }

    private static IReadOnlyList<CurioCsvRow> ReadCurioCsv(string path, List<string> issues)
    {
        var rows = new List<CurioCsvRow>();
        try
        {
            // Part of the .NET runtime; handles quoted commas and multiline CSV fields.
            using var parser = new TextFieldParser(path, Encoding.UTF8, detectEncoding: true)
            {
                TextFieldType = FieldType.Delimited,
                Delimiters = [","],
                HasFieldsEnclosedInQuotes = true,
                TrimWhiteSpace = true
            };
            while (!parser.EndOfData)
            {
                var line = parser.LineNumber;
                try
                {
                    rows.Add(new CurioCsvRow(line, parser.ReadFields() ?? []));
                }
                catch (MalformedLineException)
                {
                    issues.Add($"奇物 CSV 行格式无效，已跳过：{path}:{parser.ErrorLineNumber}");
                    // Keep the failed row boundary: never take the next block's label as an ID.
                    rows.Add(new CurioCsvRow(line, []));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            issues.Add($"奇物 CSV 读取失败：{path}；{ex.Message}");
            return [];
        }
        return rows;
    }

    private static string GetCurioNameId(BattleRoomAttachmentDefinition definition, CurioResources resources) =>
        !definition.IsRegionBound && resources.Props.TryGetValue(definition.Id, out var prop)
            ? prop.NameId
            : definition.Id;

    private sealed record CurioResources(
        IReadOnlySet<string> TypeIds, IReadOnlyDictionary<string, CurioProp> Props);
    private sealed record CurioProp(
        ActiveContentSource Source, string SpriteId, string TypeId, string NameId, bool IsAmbiguous);
    private sealed record CurioCsvRow(long Line, string[] Fields);
}
