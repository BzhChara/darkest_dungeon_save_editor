using System.Text.Json;

namespace DarkestDungeonSaveEditor.Core;

internal static partial class QuantityItemReferenceAnalyzer
{
    private static bool ParseUpgradeReferences(
        IReadOnlyList<ScannedContentFile> files, QuantityItemIndex index,
        Dictionary<string, List<string>> activeEvidence,
        Dictionary<string, List<string>> incompleteEvidence, List<string> issues, bool identitiesComplete)
    {
        // 0x1404703D0 retains the last full tree matching its native ID hash.
        // Clone only winning trees so disposed documents cannot escape this pass.
        var trees = new Dictionary<uint, (string Id, JsonElement Node, string Path)>();
        foreach (var file in files.Where(file => file.JsonKind == NativeReferenceJsonKind.Upgrades))
        {
            if (!TryParseJson(file.Text, out var document) || document is null)
            {
                identitiesComplete = false;
                MarkExactIdentities(file.Text, index, incompleteEvidence, $"活动升级 JSON 无法完整解析：{file.File.RelativePath}");
                issues.Add($"Quantity-item reference scan could not parse active upgrade JSON file: {file.File.Path}");
                continue;
            }
            using (document)
            {
                if (!NativeJsonReader.TryGetProperty(document.RootElement, "trees", out var nodes) ||
                    nodes.ValueKind != JsonValueKind.Array)
                {
                    identitiesComplete = false;
                    issues.Add($"Upgrade definition is missing its trees array: {file.File.Path}");
                    continue;
                }
                foreach (var node in nodes.EnumerateArray())
                {
                    string id;
                    try { id = NativeJsonReader.ReadCString(node, "id"); }
                    catch (InvalidOperationException error)
                    {
                        identitiesComplete = false;
                        issues.Add($"Upgrade tree ID could not be decoded: {file.File.Path} ({error.Message})");
                        continue;
                    }
                    if (id.Length == 0)
                    {
                        identitiesComplete = false;
                        issues.Add($"Upgrade tree has no usable ID; item-reference analysis is incomplete: {file.File.Path}");
                        continue;
                    }
                    trees[Loc2LocalizationReader.HashName(id)] = (id, node.Clone(), file.File.RelativePath);
                }
            }
        }

        var complete = identitiesComplete;
        foreach (var tree in trees.Values)
        {
            try
            {
                var requirements = NativeUpgradeRequirements.Read(tree.Node, tree.Id);
                // An unreadable later file may contain replacements of these IDs.
                // Other independent, confirmed item uses remain in activeEvidence.
                var evidence = identitiesComplete ? activeEvidence : incompleteEvidence;
                foreach (var requirement in requirements)
                    foreach (var cost in NativeJsonReader.Array(requirement, "currency_cost"))
                        MarkResolved(index.ResolveIdentityHash(NativeJsonReader.ReadCString(cost, "type")), evidence, tree.Path);
            }
            catch (Exception error) when (error is InvalidDataException or InvalidOperationException)
            {
                complete = false;
                MarkExactIdentities(tree.Node.GetRawText(), index, incompleteEvidence,
                    $"最终升级树无法完整分析：{tree.Path}");
                issues.Add($"Quantity-item upgrade references are incomplete for '{tree.Id}' ({tree.Path}): {error.Message}");
            }
        }
        return complete;
    }
}
