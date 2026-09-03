using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DarkestDungeonSaveEditor.Core;

internal static partial class QuantityItemReferenceAnalyzer
{
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

}
