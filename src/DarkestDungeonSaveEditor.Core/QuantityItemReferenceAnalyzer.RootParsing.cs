using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DarkestDungeonSaveEditor.Core;

internal static partial class QuantityItemReferenceAnalyzer
{
    private static bool ParseRootFile(
        ScannedContentFile file,
        QuantityItemIndex index,
        IEnumerable<string> knownLootTables,
        QuantityItemSaveContext saveContext,
        Dictionary<string, List<string>> activeEvidence,
        Dictionary<string, List<string>> rootLootEvidence,
        Dictionary<string, List<string>> incompleteEvidence,
        List<string> issues,
        Dictionary<uint, string>? eventIds = null)
    {
        var defaultReachability = GetDefaultReachability(file.File.RelativePath);
        var extension = Path.GetExtension(file.File.Path);
        if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            if (TryParseJson(file.Text, out var document) && document is not null)
            {
                using (document)
                {
                    if (eventIds is not null && document.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        return VisitTownEventJson(document.RootElement, file.File.RelativePath, index, saveContext,
                            defaultReachability, activeEvidence, rootLootEvidence, eventIds, issues);
                    }
                    VisitRootJson(
                        document.RootElement,
                        string.Empty,
                        file.File.RelativePath,
                        index,
                        saveContext,
                        defaultReachability,
                        activeEvidence,
                        rootLootEvidence);
                }

                return true;
            }

            if (!ReferencePathCanAffectContext(file.File.RelativePath, saveContext))
            {
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
            if (IsReachableInContext(defaultReachability, saveContext))
            {
                ParseDarkestRoot(file, index, activeEvidence, rootLootEvidence);
            }

            return true;
        }

        if (extension.Equals(".csv", StringComparison.OrdinalIgnoreCase))
        {
            if (IsReachableInContext(defaultReachability, saveContext))
            {
                try { ParseCsvRoot(file, index, activeEvidence, rootLootEvidence); }
                catch (Exception error) when (error is InvalidDataException or DecoderFallbackException or EncoderFallbackException)
                {
                    issues.Add($"Quantity-item curio references could not be parsed: {file.File.Path} ({error.Message})");
                    return false;
                }
            }
        }

        return true;
    }

    private static bool VisitTownEventJson(
        JsonElement root,
        string relativePath,
        QuantityItemIndex index,
        QuantityItemSaveContext saveContext,
        ReferenceReachability reachability,
        Dictionary<string, List<string>> activeEvidence,
        Dictionary<string, List<string>> rootLootEvidence,
        Dictionary<uint, string> eventIds,
        List<string> issues)
    {
        var complete = true;
        foreach (var property in root.EnumerateObject())
        {
            if (property.Name != "events" || property.Value.ValueKind != JsonValueKind.Array)
            {
                VisitRootJson(property.Value, property.Name, relativePath, index, saveContext,
                    reachability, activeEvidence, rootLootEvidence);
                continue;
            }
            foreach (var eventNode in property.Value.EnumerateArray())
            {
                var id = ReadString(eventNode, "id");
                var first = true;
                if (!string.IsNullOrWhiteSpace(id))
                {
                    var hash = Loc2LocalizationReader.HashName(id);
                    first = eventIds.TryAdd(hash, id);
                    if (!first && !eventIds[hash].Equals(id, StringComparison.Ordinal))
                    {
                        complete = false;
                        issues.Add($"Town event IDs share a native hash; item-reference analysis is incomplete: {eventIds[hash]} / {id}");
                    }
                }
                else
                {
                    complete = false;
                    issues.Add($"Town event has no usable ID; item-reference analysis is incomplete: {relativePath}");
                }
                // Event result execution looks up the FIRST record by ID,
                // even if its data is absent/empty. Do not deduplicate the
                // eligibility/cost/other fields of the random candidate pool.
                VisitRootJson(eventNode, "events", relativePath, index, saveContext,
                    reachability, activeEvidence, rootLootEvidence, skipTownEventData: !first);
            }
        }
        return complete;
    }

    private static void VisitRootJson(
        JsonElement node,
        string parentProperty,
        string relativePath,
        QuantityItemIndex index,
        QuantityItemSaveContext saveContext,
        ReferenceReachability inheritedReachability,
        Dictionary<string, List<string>> activeEvidence,
        Dictionary<string, List<string>> rootLootEvidence,
        bool skipTownEventData = false)
    {
        if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in node.EnumerateArray())
            {
                VisitRootJson(
                    child,
                    parentProperty,
                    relativePath,
                    index,
                    saveContext,
                    inheritedReachability,
                    activeEvidence,
                    rootLootEvidence);
            }

            return;
        }

        if (node.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var reachability = GetJsonReachability(node, parentProperty, inheritedReachability);
        if (IsReachableInContext(reachability, saveContext))
        {
            var type = ReadString(node, "type");
            var id = ReadString(node, "id");
            MarkResolved(index.Resolve(type, id), activeEvidence, relativePath);

            var itemType = ReadString(node, "item_type");
            var itemName = ReadString(node, "item_name");
            MarkResolved(index.Resolve(itemType, itemName), activeEvidence, relativePath);

            if (type.Equals("bonus_currency", StringComparison.OrdinalIgnoreCase) ||
                type.Equals("event_cost", StringComparison.OrdinalIgnoreCase))
            {
                MarkResolved(
                    index.ResolveIdentity(ReadString(node, "string_data")),
                    activeEvidence,
                    relativePath);
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
        }

        foreach (var property in node.EnumerateObject())
        {
            if (skipTownEventData && property.Name == "data") continue;
            VisitRootJson(
                property.Value,
                property.Name,
                relativePath,
                index,
                saveContext,
                reachability,
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
        foreach (var (kind, body) in NativeDarkestReader.ReadRecordsFromText(file.Text))
        {
            if (kind is "loot" or "extra_battle_loot" or "extra_curio_loot" &&
                NativeDarkestReader.ReadString(body, ".code") is { Length: > 0 } code)
                AddEvidence(rootLootEvidence, code, file.File.RelativePath);
            if (NativeDarkestReader.ReadString(body, ".type") is { } type &&
                NativeDarkestReader.ReadString(body, ".id") is { } id)
                MarkResolved(index.Resolve(type, id), activeEvidence, file.File.RelativePath);
            foreach (var field in new[] { ".use_item_id", ".item_id" })
                if (NativeDarkestReader.ReadString(body, field) is { Length: > 0 } itemId)
                    MarkResolved(index.ResolveIdentity(itemId), activeEvidence, file.File.RelativePath);
        }
    }

    private static void ParseCsvRoot(
        ScannedContentFile file,
        QuantityItemIndex index,
        Dictionary<string, List<string>> activeEvidence,
        Dictionary<string, List<string>> rootLootEvidence)
    {
        // Only the effective type-library files reach this consumer. Physical
        // rows, block boundaries and columns are shared with the map catalog.
        var rows = NativeCurioCsvReader.ReadFromText(file.Text, file.File.Path, 24, mapping: false);
        foreach (var block in NativeCurioCsvReader.TypeBlocks(rows))
        {
            if (block.Count == 0 || block[0].Count < 3 || block[0].Fields[2].Length == 0)
                throw new InvalidDataException("奇物互动类型缺少有效 ID 行。");
            var itemSection = false;
            foreach (var row in block)
            {
                var fields = row.Fields;
                if (!itemSection && fields[4] == "ITEM") { itemSection = true; continue; }
                if (!itemSection)
                {
                    // The native default-result loop requires a nonzero atoi weight.
                    if (fields[4] == "Loot")
                    {
                        var weight = NativeDarkestReader.ReadIntPrefix(fields[5])
                            ?? throw new InvalidDataException("奇物默认掉落权重超出可确认范围。");
                        if (weight != 0) AddCurioLootReferences(row, file.File.RelativePath, rootLootEvidence);
                    }
                    continue;
                }
                if (fields[4].Length == 0 || fields[5].Length == 0) continue;
                // BeginItemInteraction consumes column 5; no # suffix means supply.
                var item = CurioString(fields[4], 255);
                var separator = item.IndexOf('#');
                var id = separator < 0 ? item : item[..separator];
                var type = separator < 0 ? "supply" : item[(separator + 1)..];
                MarkResolved(index.Resolve(type, id), activeEvidence, file.File.RelativePath);
                if (fields[5] == "Loot") AddCurioLootReferences(row, file.File.RelativePath, rootLootEvidence);
            }
        }
    }

    private static string CurioString(string text, int maximumBytes)
    {
        var encoding = new UTF8Encoding(false, true);
        var bytes = encoding.GetBytes(text);
        return encoding.GetString(bytes, 0, Math.Min(bytes.Length, maximumBytes));
    }

    private static void AddCurioLootReferences(NativeCurioCsvReader.Row row, string path,
        Dictionary<string, List<string>> evidence)
    {
        // Native Loot consumes columns 8/11/14. Notes and localization text
        // are not references. The first code is copied even when its count is 0.
        var firstCount = NativeDarkestReader.ReadIntPrefix(row.Fields[8])
            ?? throw new InvalidDataException("奇物掉落次数超出可确认范围。");
        var codes = new List<(string Code, int Count)> { (row.Fields[7], Math.Max(1, firstCount)) };
        foreach (var column in new[] { 10, 13 })
        {
            if (column + 2 >= row.Count || row.Fields[column].Length == 0) continue;
            var count = NativeDarkestReader.ReadIntPrefix(row.Fields[column + 1])
                ?? throw new InvalidDataException("奇物掉落次数超出可确认范围。");
            if (count > 0) codes.Add((row.Fields[column], count));
        }
        // ReadPropLootResultTypePossibleResults packs all repeated codes into
        // ONE 64-byte buffer. Do not confirm untruncated column IDs if that
        // combined value would overflow; leave this file's analysis incomplete.
        var bytes = codes.Sum(entry => ((long)Encoding.UTF8.GetByteCount(entry.Code) + 1) * entry.Count) - 1;
        if (bytes > 63) throw new InvalidDataException("奇物掉落代码合并后超过原生缓冲区，无法确认引用。");
        // CurioInteractionLootResult construction (0x1404A835F) tokenizes the
        // packed string on '&', skips empty tokens, then copies each to 32 bytes.
        var consumed = codes.SelectMany(entry => entry.Code.Split('&', StringSplitOptions.RemoveEmptyEntries))
            .Select(code => CurioString(code, 31)).ToArray();
        foreach (var code in consumed) AddEvidence(evidence, code, path);
    }

}
