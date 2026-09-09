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
        QuantityItemSaveContext saveContext,
        List<string> issues)
    {
        ArgumentNullException.ThrowIfNull(activeContent);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(issues);

        var index = new QuantityItemIndex(definitions);
        var activeEvidence = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var incompleteEvidence = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var lootTables = new Dictionary<string, LootTableNode>(StringComparer.Ordinal);
        var rootLootEvidence = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var scanComplete = true;
        var files = LoadEffectiveFiles(activeContent, saveContext, issues, ref scanComplete);
        var eventIds = new Dictionary<uint, string>();
        var dlcPrefixes = ContentFileOverlay.GetEnabledDlcPrefixes(activeContent.Sources);

        foreach (var definition in definitions.Where(definition => definition.EstateCanBeProvision == true))
        {
            AddEvidence(
                activeEvidence,
                definition.CatalogKey,
                saveContext == QuantityItemSaveContext.Town
                    ? "物品定义明确允许从庄园库存手动配给"
                    : "物品定义明确允许从庄园配给进副本");
        }

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
                saveContext,
                activeEvidence,
                rootLootEvidence,
                incompleteEvidence,
                issues,
                file.File.RelativePath.EndsWith(".events.json", StringComparison.OrdinalIgnoreCase) &&
                ContentFileOverlay.IsRootOrEnabledDlcPath(file.File.RelativePath, "campaign/town_events", dlcPrefixes)
                    ? eventIds : null);
        }

        TraverseLootRoots(lootTables, rootLootEvidence, activeEvidence);

        var sourcesById = activeContent.Sources.ToDictionary(
            source => source.Id,
            StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, QuantityItemReferenceAnalysis>(StringComparer.Ordinal);
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
                [saveContext == QuantityItemSaveContext.Raid
                    ? "未发现从活动配给、技能、英雄、怪物、任务、场景或掉落入口进入副本的引用"
                    : "未发现从活动事件、建筑、配给、任务、掉落或其他小镇持久化入口可达的引用"]);
        }

        return result;
    }
}
