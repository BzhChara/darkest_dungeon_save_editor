using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DarkestDungeonSaveEditor.Core;

internal sealed record QuantityItemReferenceAnalysis(
    QuantityItemReferenceStatus Status,
    IReadOnlyList<string> Evidence);

internal static partial class QuantityItemReferenceAnalyzer
{
    private static readonly string[] ContentDirectories =
    [
        "campaign",
        "curios",
        "dungeons",
        "heroes",
        "loot",
        "monsters",
        "props",
        "raid",
        "rules",
        "scripts",
        "shared",
        "torch",
        "upgrades"
    ];

    private static readonly HashSet<string> TextExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".json", ".darkest", ".csv" };

    public static IReadOnlyDictionary<string, QuantityItemReferenceAnalysis> Analyze(
        ActiveContentSnapshot activeContent,
        IReadOnlyList<QuantityItemDefinition> definitions,
        List<string> issues)
    {
        ArgumentNullException.ThrowIfNull(activeContent);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(issues);

        var index = new QuantityItemIndex(definitions);
        var activeEvidence = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var incompleteEvidence = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var lootTables = new Dictionary<string, LootTableNode>(StringComparer.OrdinalIgnoreCase);
        var rootLootEvidence = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var scanComplete = true;
        var files = LoadEffectiveFiles(activeContent, issues, ref scanComplete);

        foreach (var file in files.Where(file => file.IsLootFile))
        {
            scanComplete &= ParseLootFile(file, index, lootTables, incompleteEvidence, issues);
        }

        foreach (var file in files.Where(file => !file.IsLootFile))
        {
            scanComplete &= ParseRootFile(
                file,
                index,
                lootTables.Keys,
                activeEvidence,
                rootLootEvidence,
                incompleteEvidence,
                issues);
        }

        TraverseLootRoots(lootTables, rootLootEvidence, activeEvidence);

        var sourcesById = activeContent.Sources.ToDictionary(
            source => source.Id,
            StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, QuantityItemReferenceAnalysis>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in definitions)
        {
            var hasOfficialOrigin = definition.AllSources.Any(sourceId =>
                sourcesById.TryGetValue(sourceId, out var source) &&
                source.Kind is not ("workshop" or "local"));
            if (hasOfficialOrigin)
            {
                result[definition.CatalogKey] = new QuantityItemReferenceAnalysis(
                    QuantityItemReferenceStatus.OfficialContent,
                    ["原版或官方 DLC 内容定义"]);
                continue;
            }

            if (activeEvidence.TryGetValue(definition.CatalogKey, out var evidence))
            {
                result[definition.CatalogKey] = new QuantityItemReferenceAnalysis(
                    QuantityItemReferenceStatus.ConfirmedActive,
                    evidence.Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray());
                continue;
            }

            if (!scanComplete || incompleteEvidence.TryGetValue(definition.CatalogKey, out evidence))
            {
                result[definition.CatalogKey] = new QuantityItemReferenceAnalysis(
                    QuantityItemReferenceStatus.AnalysisIncomplete,
                    evidence?.Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray() ??
                    ["活动内容引用扫描未完整完成"]);
                continue;
            }

            result[definition.CatalogKey] = new QuantityItemReferenceAnalysis(
                QuantityItemReferenceStatus.SuspectedUnused,
                ["未发现从活动技能、英雄、怪物、任务、事件、建筑、配给或掉落入口可达的引用"]);
        }

        return result;
    }

    private static IReadOnlyList<ScannedContentFile> LoadEffectiveFiles(
        ActiveContentSnapshot activeContent,
        List<string> issues,
        ref bool scanComplete)
    {
        var enabledDlcPrefixes = ContentFileOverlay.GetEnabledDlcPrefixes(activeContent.Sources);
        var candidates = new List<ContentFileCandidate>();
        foreach (var source in activeContent.Sources)
        {
            try
            {
                AuditManifestReferenceFiles(source, enabledDlcPrefixes, issues, ref scanComplete);
                foreach (var path in EnumerateCandidateFiles(source, enabledDlcPrefixes))
                {
                    candidates.Add(new ContentFileCandidate(source, path));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                scanComplete = false;
                issues.Add($"Failed to enumerate quantity-item reference files in '{source.Directory}': {ex.Message}");
            }
        }

        var result = new List<ScannedContentFile>();
        foreach (var file in ContentFileOverlay.Resolve(candidates, "Quantity-item reference", issues))
        {
            try
            {
                if (DsonSaveCodec.IsDson(file.Path))
                {
                    continue;
                }

                result.Add(new ScannedContentFile(
                    file,
                    File.ReadAllText(file.Path, Encoding.UTF8),
                    IsLootPath(file.RelativePath)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
            {
                scanComplete = false;
                issues.Add($"Failed to read quantity-item reference file '{file.Path}': {ex.Message}");
            }
        }

        return result;
    }

    private static void AuditManifestReferenceFiles(
        ActiveContentSource source,
        IReadOnlyList<string> enabledDlcPrefixes,
        List<string> issues,
        ref bool scanComplete)
    {
        var manifestPath = Path.Combine(source.Directory, "modfiles.txt");
        if (source.Kind is not ("workshop" or "local") || !File.Exists(manifestPath))
        {
            return;
        }

        var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadLines(manifestPath))
        {
            var relative = ModManifestPath.Extract(line, ".json", ".darkest", ".csv");
            if (relative is null)
            {
                continue;
            }

            var path = Path.GetFullPath(Path.Combine(source.Directory, relative));
            if (!IsInsideSource(source.Directory, path))
            {
                continue;
            }

            var normalized = ContentFileOverlay.NormalizeRelativePath(source, path);
            if (normalized is null ||
                !ContentDirectories.Any(directory =>
                    ContentFileOverlay.IsRootOrEnabledDlcPath(normalized, directory, enabledDlcPrefixes)) ||
                IsDefinitionOnlyPath(normalized) ||
                File.Exists(path) ||
                !reported.Add(path))
            {
                continue;
            }

            scanComplete = false;
            issues.Add($"Quantity-item reference file listed by active Mod is missing: {path}");
        }
    }

    private static IEnumerable<string> EnumerateCandidateFiles(
        ActiveContentSource source,
        IReadOnlyList<string> enabledDlcPrefixes)
    {
        if (!Directory.Exists(source.Directory))
        {
            yield break;
        }

        IEnumerable<string> paths;
        var manifestPath = Path.Combine(source.Directory, "modfiles.txt");
        if ((source.Kind is "workshop" or "local") && File.Exists(manifestPath))
        {
            paths = File.ReadLines(manifestPath)
                .Select(line => ModManifestPath.Extract(line, ".json", ".darkest", ".csv"))
                .Where(path => path is not null)
                .Select(path => Path.GetFullPath(Path.Combine(source.Directory, path!)))
                .Where(path => IsInsideSource(source.Directory, path) && File.Exists(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        else if (source.Kind is "workshop" or "local")
        {
            paths = Directory.EnumerateFiles(
                source.Directory,
                "*",
                new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.ReparsePoint
                });
        }
        else
        {
            paths = ContentDirectories
                .Select(directory => Path.Combine(source.Directory, directory))
                .Where(Directory.Exists)
                .SelectMany(directory => Directory.EnumerateFiles(
                    directory,
                    "*",
                    new EnumerationOptions
                    {
                        RecurseSubdirectories = true,
                        IgnoreInaccessible = true,
                        AttributesToSkip = FileAttributes.ReparsePoint
                    }));
        }

        foreach (var path in paths)
        {
            if (!TextExtensions.Contains(Path.GetExtension(path)))
            {
                continue;
            }

            var relativePath = ContentFileOverlay.NormalizeRelativePath(source, path);
            if (relativePath is null ||
                !ContentDirectories.Any(directory =>
                    ContentFileOverlay.IsRootOrEnabledDlcPath(relativePath, directory, enabledDlcPrefixes)) ||
                IsDefinitionOnlyPath(relativePath))
            {
                continue;
            }

            yield return path;
        }
    }

    private static bool IsInsideSource(string sourceDirectory, string path)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(sourceDirectory), path);
        return !Path.IsPathRooted(relative) &&
               !relative.Equals("..", StringComparison.Ordinal) &&
               !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
               !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static bool IsDefinitionOnlyPath(string relativePath)
    {
        var normalized = $"/{relativePath.Replace('\\', '/').Trim('/')}";
        return normalized.Contains("/localization/", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("/inventory/", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("/effects/", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("/scripts/starting_save/", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("/shared/buffs/", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("/trinkets/", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith(".effects.darkest", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLootPath(string relativePath)
    {
        var normalized = $"/{relativePath.Replace('\\', '/').Trim('/')}";
        return normalized.Contains("/loot/", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith(".loot.json", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ParseLootFile(
        ScannedContentFile file,
        QuantityItemIndex index,
        Dictionary<string, LootTableNode> lootTables,
        Dictionary<string, List<string>> incompleteEvidence,
        List<string> issues)
    {
        if (!TryParseJson(file.Text, out var document) || document is null)
        {
            MarkLootParseFallback(file, index, incompleteEvidence, issues);
            return false;
        }

        using (document)
        {
            var rootObject = document.RootElement;
            if (rootObject.ValueKind != JsonValueKind.Object ||
                !TryGetProperty(rootObject, "loot_tables", out var tables))
            {
                return true;
            }

            if (tables.ValueKind != JsonValueKind.Array)
            {
                MarkLootParseFallback(file, index, incompleteEvidence, issues);
                return false;
            }

            foreach (var tableNode in tables.EnumerateArray())
            {
                if (tableNode.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var tableId = ReadString(tableNode, "id").Trim();
                if (string.IsNullOrWhiteSpace(tableId))
                {
                    continue;
                }

                if (!lootTables.TryGetValue(tableId, out var table))
                {
                    table = new LootTableNode();
                    lootTables[tableId] = table;
                }

                if (!TryGetProperty(tableNode, "entries", out var entries) ||
                    entries.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var entry in entries.EnumerateArray())
                {
                    if (entry.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var entryType = ReadString(entry, "type");
                    if (!TryGetProperty(entry, "data", out var data) ||
                        data.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    if (entryType.Equals("item", StringComparison.OrdinalIgnoreCase))
                    {
                        var itemType = ReadString(data, "type");
                        var itemId = ReadString(data, "id");
                        foreach (var key in index.Resolve(itemType, itemId))
                        {
                            table.ItemKeys.Add(key);
                        }
                    }
                    else if (entryType.Equals("table", StringComparison.OrdinalIgnoreCase))
                    {
                        var nested = ReadString(data, "table").Trim();
                        if (!string.IsNullOrWhiteSpace(nested))
                        {
                            table.NestedTables.Add(nested);
                        }
                    }
                }
            }
        }

        return true;
    }

    private static void MarkLootParseFallback(
        ScannedContentFile file,
        QuantityItemIndex index,
        Dictionary<string, List<string>> incompleteEvidence,
        List<string> issues)
    {
        _ = MarkExactIdentities(
            file.Text,
            index,
            incompleteEvidence,
            $"掉落文件无法完整解析：{file.File.RelativePath}");
        issues.Add($"Quantity-item reference scan could not parse active loot file: {file.File.Path}");
    }

    private static bool ParseRootFile(
        ScannedContentFile file,
        QuantityItemIndex index,
        IEnumerable<string> knownLootTables,
        Dictionary<string, List<string>> activeEvidence,
        Dictionary<string, List<string>> rootLootEvidence,
        Dictionary<string, List<string>> incompleteEvidence,
        List<string> issues)
    {
        var extension = Path.GetExtension(file.File.Path);
        if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            if (TryParseJson(file.Text, out var document) && document is not null)
            {
                using (document)
                {
                    VisitRootJson(
                        document.RootElement,
                        string.Empty,
                        file.File.RelativePath,
                        index,
                        activeEvidence,
                        rootLootEvidence);
                }

                return true;
            }

            _ = MarkExactIdentities(
                file.Text,
                index,
                incompleteEvidence,
                $"活动 JSON 无法完整解析：{file.File.RelativePath}");
            _ = MarkQuotedLootCodes(
                file.Text,
                knownLootTables,
                file.File.RelativePath,
                rootLootEvidence);
            issues.Add($"Quantity-item reference scan could not parse active JSON file: {file.File.Path}");
            return false;
        }

        if (extension.Equals(".darkest", StringComparison.OrdinalIgnoreCase))
        {
            ParseDarkestRoot(file, index, activeEvidence, rootLootEvidence);
            return true;
        }

        if (extension.Equals(".csv", StringComparison.OrdinalIgnoreCase))
        {
            ParseCsvRoot(file, index, knownLootTables, activeEvidence, rootLootEvidence);
        }

        return true;
    }

    private static void VisitRootJson(
        JsonElement node,
        string parentProperty,
        string relativePath,
        QuantityItemIndex index,
        Dictionary<string, List<string>> activeEvidence,
        Dictionary<string, List<string>> rootLootEvidence)
    {
        if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in node.EnumerateArray())
            {
                VisitRootJson(child, parentProperty, relativePath, index, activeEvidence, rootLootEvidence);
            }

            return;
        }

        if (node.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var type = ReadString(node, "type");
        var id = ReadString(node, "id");
        MarkResolved(index.Resolve(type, id), activeEvidence, relativePath);

        var itemType = ReadString(node, "item_type");
        var itemName = ReadString(node, "item_name");
        MarkResolved(index.Resolve(itemType, itemName), activeEvidence, relativePath);

        if (type.Equals("bonus_currency", StringComparison.OrdinalIgnoreCase))
        {
            MarkResolved(index.ResolveIdentity(ReadString(node, "string_data")), activeEvidence, relativePath);
        }

        if (type.Equals("loot", StringComparison.OrdinalIgnoreCase))
        {
            AddEvidence(rootLootEvidence, ReadString(node, "sub_type"), relativePath);
        }

        foreach (var field in new[] { "loot_table_code", "loot_table", "loot_code" })
        {
            AddEvidence(rootLootEvidence, ReadString(node, field), relativePath);
        }

        foreach (var field in new[] { "item_id", "currency_id" })
        {
            MarkResolved(index.ResolveIdentity(ReadString(node, field)), activeEvidence, relativePath);
        }

        if (parentProperty.Equals("currencies", StringComparison.OrdinalIgnoreCase))
        {
            MarkResolved(index.ResolveIdentity(id), activeEvidence, relativePath);
        }

        foreach (var property in node.EnumerateObject())
        {
            VisitRootJson(
                property.Value,
                property.Name,
                relativePath,
                index,
                activeEvidence,
                rootLootEvidence);
        }
    }

    private static void ParseDarkestRoot(
        ScannedContentFile file,
        QuantityItemIndex index,
        Dictionary<string, List<string>> activeEvidence,
        Dictionary<string, List<string>> rootLootEvidence)
    {
        var text = StripLineComments(file.Text);
        foreach (Match match in DarkestLootCodeRegex().Matches(text))
        {
            AddEvidence(rootLootEvidence, match.Groups["code"].Value, file.File.RelativePath);
        }

        foreach (Match match in DarkestTypeThenIdRegex().Matches(text))
        {
            MarkResolved(
                index.Resolve(match.Groups["type"].Value, match.Groups["id"].Value),
                activeEvidence,
                file.File.RelativePath);
        }

        foreach (Match match in DarkestIdThenTypeRegex().Matches(text))
        {
            MarkResolved(
                index.Resolve(match.Groups["type"].Value, match.Groups["id"].Value),
                activeEvidence,
                file.File.RelativePath);
        }

        foreach (Match match in DarkestItemIdRegex().Matches(text))
        {
            MarkResolved(index.ResolveIdentity(match.Groups["id"].Value), activeEvidence, file.File.RelativePath);
        }
    }

    private static void ParseCsvRoot(
        ScannedContentFile file,
        QuantityItemIndex index,
        IEnumerable<string> knownLootTables,
        Dictionary<string, List<string>> activeEvidence,
        Dictionary<string, List<string>> rootLootEvidence)
    {
        var tableSet = knownLootTables.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var line in file.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Contains("Loot", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var token in line.Split(',').Select(value => value.Trim().Trim('"')))
                {
                    if (tableSet.Contains(token))
                    {
                        AddEvidence(rootLootEvidence, token, file.File.RelativePath);
                    }
                }
            }

            foreach (Match match in CsvTypedItemRegex().Matches(line))
            {
                MarkResolved(
                    index.Resolve(match.Groups["type"].Value, match.Groups["id"].Value),
                    activeEvidence,
                    file.File.RelativePath);
            }
        }
    }

    private static void TraverseLootRoots(
        IReadOnlyDictionary<string, LootTableNode> lootTables,
        IReadOnlyDictionary<string, List<string>> rootLootEvidence,
        Dictionary<string, List<string>> activeEvidence)
    {
        var queue = new Queue<(string TableId, string Evidence)>();
        foreach (var pair in rootLootEvidence)
        {
            foreach (var evidence in pair.Value)
            {
                queue.Enqueue((pair.Key, evidence));
            }
        }

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (queue.Count > 0)
        {
            var (tableId, evidence) = queue.Dequeue();
            if (!visited.Add(tableId) || !lootTables.TryGetValue(tableId, out var table))
            {
                continue;
            }

            var chain = $"{evidence} → 掉落表 {tableId}";
            foreach (var key in table.ItemKeys)
            {
                AddEvidence(activeEvidence, key, chain);
            }

            foreach (var nested in table.NestedTables)
            {
                queue.Enqueue((nested, chain));
            }
        }
    }

    private static bool MarkQuotedLootCodes(
        string text,
        IEnumerable<string> knownLootTables,
        string relativePath,
        Dictionary<string, List<string>> rootLootEvidence)
    {
        var matched = false;
        foreach (var table in knownLootTables)
        {
            if (ContainsQuotedToken(text, table))
            {
                AddEvidence(rootLootEvidence, table, $"无法完整解析但包含掉落表引用：{relativePath}");
                matched = true;
            }
        }

        return matched;
    }

    private static bool MarkExactIdentities(
        string text,
        QuantityItemIndex index,
        Dictionary<string, List<string>> evidence,
        string message)
    {
        var matched = false;
        foreach (var identity in index.Identities)
        {
            if (ContainsQuotedToken(text, identity.Identity))
            {
                MarkResolved(identity.CatalogKeys, evidence, message);
                matched = true;
            }
        }

        return matched;
    }

    private static bool ContainsQuotedToken(string text, string token)
    {
        return text.Contains($"\"{token}\"", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseJson(string text, out JsonDocument? document)
    {
        try
        {
            document = JsonDocument.Parse(
                text,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip
                });
            return true;
        }
        catch (JsonException)
        {
            document = null;
            return false;
        }
    }

    private static string StripLineComments(string text)
    {
        var result = new StringBuilder(text.Length);
        var inQuotes = false;
        var escaped = false;
        for (var index = 0; index < text.Length; index++)
        {
            var current = text[index];
            if (!inQuotes && current == '/' && index + 1 < text.Length && text[index + 1] == '/')
            {
                index += 2;
                while (index < text.Length && text[index] is not ('\r' or '\n'))
                {
                    index++;
                }

                if (index >= text.Length)
                {
                    break;
                }

                current = text[index];
            }

            result.Append(current);
            if (current is '\r' or '\n')
            {
                inQuotes = false;
                escaped = false;
                continue;
            }

            if (inQuotes && current == '\\' && !escaped)
            {
                escaped = true;
                continue;
            }

            if (current == '"' && !escaped)
            {
                inQuotes = !inQuotes;
            }

            escaped = false;
        }

        return result.ToString();
    }

    private static bool TryGetProperty(JsonElement item, string name, out JsonElement value)
    {
        foreach (var property in item.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string ReadString(JsonElement item, string name)
    {
        return TryGetProperty(item, name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static void MarkResolved(
        IEnumerable<string> keys,
        Dictionary<string, List<string>> evidence,
        string message)
    {
        foreach (var key in keys)
        {
            AddEvidence(evidence, key, message);
        }
    }

    private static void AddEvidence(
        Dictionary<string, List<string>> evidence,
        string key,
        string message)
    {
        key = key.Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        if (!evidence.TryGetValue(key, out var values))
        {
            values = [];
            evidence[key] = values;
        }

        if (!values.Contains(message, StringComparer.OrdinalIgnoreCase))
        {
            values.Add(message);
        }
    }

    [GeneratedRegex(
        "(?:^|\\r?\\n)\\s*(?:loot|extra_battle_loot|extra_curio_loot):\\s*\\.code\\s+\"(?<code>[^\"]+)\"",
        RegexOptions.IgnoreCase)]
    private static partial Regex DarkestLootCodeRegex();

    [GeneratedRegex(
        "\\.type\\s+\"(?<type>[^\"]+)\"[^\\r\\n]{0,320}?\\.id\\s+\"(?<id>[^\"]*)\"",
        RegexOptions.IgnoreCase)]
    private static partial Regex DarkestTypeThenIdRegex();

    [GeneratedRegex(
        "\\.id\\s+\"(?<id>[^\"]*)\"[^\\r\\n]{0,320}?\\.type\\s+\"(?<type>[^\"]+)\"",
        RegexOptions.IgnoreCase)]
    private static partial Regex DarkestIdThenTypeRegex();

    [GeneratedRegex("\\.(?:use_item_id|item_id)\\s+\"(?<id>[^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex DarkestItemIdRegex();

    [GeneratedRegex(@"(?<id>[A-Za-z0-9_.-]+)#(?<type>[A-Za-z0-9_.-]+)", RegexOptions.IgnoreCase)]
    private static partial Regex CsvTypedItemRegex();

    private sealed record ScannedContentFile(
        EffectiveContentFile File,
        string Text,
        bool IsLootFile);

    private sealed class LootTableNode
    {
        public HashSet<string> ItemKeys { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> NestedTables { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class QuantityItemIndex
    {
        private readonly IReadOnlyList<QuantityItemDefinition> _definitions;

        public QuantityItemIndex(IReadOnlyList<QuantityItemDefinition> definitions)
        {
            _definitions = definitions;
            Identities = definitions
                .Select(definition => definition.DisplayId)
                .Where(identity => !string.IsNullOrWhiteSpace(identity))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(identity => new ItemIdentity(identity, ResolveIdentity(identity).ToArray()))
                .ToArray();
        }

        public IReadOnlyList<ItemIdentity> Identities { get; }

        public IEnumerable<string> Resolve(string type, string id)
        {
            type = type.Trim();
            id = id.Trim();
            if (string.IsNullOrWhiteSpace(type))
            {
                return [];
            }

            return _definitions
                .Where(definition => Matches(definition, type, id))
                .Select(definition => definition.CatalogKey)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public IEnumerable<string> ResolveIdentity(string identity)
        {
            identity = identity.Trim();
            if (string.IsNullOrWhiteSpace(identity))
            {
                return [];
            }

            return _definitions
                .Where(definition =>
                    definition.DisplayId.Equals(identity, StringComparison.OrdinalIgnoreCase))
                .Select(definition => definition.CatalogKey)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static bool Matches(QuantityItemDefinition definition, string type, string id)
        {
            if (definition.StorageKind == QuantityItemStorageKind.RaidInventory)
            {
                return definition.InventoryType.Equals(type, StringComparison.OrdinalIgnoreCase) &&
                       definition.ItemId.Equals(id, StringComparison.OrdinalIgnoreCase);
            }

            if (definition.StorageKind == QuantityItemStorageKind.EstateItems)
            {
                return definition.InventoryType.Equals(type, StringComparison.OrdinalIgnoreCase) &&
                       definition.ItemId.Equals(id, StringComparison.OrdinalIgnoreCase);
            }

            if (definition.InventoryType.Equals("heirloom", StringComparison.OrdinalIgnoreCase))
            {
                return type.Equals("heirloom", StringComparison.OrdinalIgnoreCase)
                    ? definition.ItemId.Equals(id, StringComparison.OrdinalIgnoreCase)
                    : string.IsNullOrWhiteSpace(id) &&
                      definition.PersistedType.Equals(type, StringComparison.OrdinalIgnoreCase);
            }

            return definition.InventoryType.Equals(type, StringComparison.OrdinalIgnoreCase) &&
                   string.IsNullOrWhiteSpace(id);
        }
    }

    private sealed record ItemIdentity(string Identity, IReadOnlyList<string> CatalogKeys);
}
