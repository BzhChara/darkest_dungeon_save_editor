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
        List<string> issues)
    {
        var defaultReachability = GetDefaultReachability(file.File.RelativePath);
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
                ParseCsvRoot(file, index, knownLootTables, activeEvidence, rootLootEvidence);
            }
        }

        return true;
    }

    private static void VisitRootJson(
        JsonElement node,
        string parentProperty,
        string relativePath,
        QuantityItemIndex index,
        QuantityItemSaveContext saveContext,
        ReferenceReachability inheritedReachability,
        Dictionary<string, List<string>> activeEvidence,
        Dictionary<string, List<string>> rootLootEvidence)
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

}
