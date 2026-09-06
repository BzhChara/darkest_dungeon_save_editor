using System.Globalization;

namespace DarkestDungeonSaveEditor.Core;

internal sealed record RegionalMapContentChoice(
    BattleRoomAttachmentKind Kind, string DungeonId, string Id, double Weight);

public sealed partial record BattleRoomAttachmentCatalogResult
{
    internal IReadOnlyList<RegionalMapContentChoice> RegionalPool { get; init; } = [];

    public IReadOnlyList<BattleRoomAttachmentDefinition> GetRegionalCandidates(
        BattleRoomAttachmentKind kind, string dungeonId) => GetRegionalPool(kind, dungeonId)
        .Select(choice => choice.Definition)
        .DistinctBy(definition => definition.Id, StringComparer.Ordinal)
        .ToArray();

    public BattleRoomAttachmentDefinition? SelectRegionalContent(
        BattleRoomAttachmentKind kind, string dungeonId, double sample)
    {
        if (!double.IsFinite(sample) || sample < 0 || sample >= 1)
        {
            throw new ArgumentOutOfRangeException(nameof(sample));
        }
        var pool = GetRegionalPool(kind, dungeonId).ToArray();
        if (pool.Length == 0)
        {
            return null;
        }

        // Normalize before summing so even large finite authored weights cannot overflow.
        // Duplicate eligible rows retain their contributions; no removed row is rolled.
        var scale = pool.Max(choice => choice.Weight);
        var roll = sample * pool.Sum(choice => choice.Weight / scale);
        foreach (var choice in pool)
        {
            var weight = choice.Weight / scale;
            if (roll < weight)
            {
                return choice.Definition;
            }
            roll -= weight;
        }
        return pool[^1].Definition;
    }

    private IEnumerable<(BattleRoomAttachmentDefinition Definition, double Weight)> GetRegionalPool(
        BattleRoomAttachmentKind kind, string dungeonId)
    {
        if (kind is not (BattleRoomAttachmentKind.Trap or BattleRoomAttachmentKind.Obstacle))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }
        // Resolve IDs through the current definitions: profile synchronization rebinds
        // their guards without rebuilding the unchanged pool. Never retain old guards here.
        var definitions = GetCandidates(kind, dungeonId).ToDictionary(item => item.Id, StringComparer.Ordinal);
        return RegionalPool.Where(choice => choice.Kind == kind &&
                choice.DungeonId.Equals(dungeonId, StringComparison.OrdinalIgnoreCase) &&
                double.IsFinite(choice.Weight) && choice.Weight > 0 && definitions.ContainsKey(choice.Id))
            .Select(choice => (definitions[choice.Id], choice.Weight));
    }
}

public static partial class BattleRoomAttachmentCatalog
{
    private static string DefinitionKey(BattleRoomAttachmentDefinition definition) =>
        $"{definition.Kind}\n{definition.Id}\n" +
        (definition.IsRegionBound ? definition.OriginDungeonId.ToLowerInvariant() : string.Empty);

    private static IReadOnlyList<RegionalMapContentChoice> BuildRegionalPool(
        IReadOnlyList<ParsedAttachment> parsed,
        IReadOnlyList<ParsedAttachment> selected,
        IReadOnlyList<BattleRoomAttachmentDefinition> definitions)
    {
        var selectedByKey = selected.ToDictionary(item => DefinitionKey(item.Definition), StringComparer.Ordinal);
        var acceptedByKey = definitions.Where(item => item.IsRegionBound)
            .ToDictionary(DefinitionKey, StringComparer.Ordinal);
        var pool = new List<RegionalMapContentChoice>();
        foreach (var row in parsed.Where(item => item.RegionalWeight > 0))
        {
            var key = DefinitionKey(row.Definition);
            if (acceptedByKey.TryGetValue(key, out var definition) &&
                ContentFileOverlay.ComparePriority(row.Source, selectedByKey[key].Source) == 0)
            {
                pool.Add(new RegionalMapContentChoice(
                    definition.Kind, definition.OriginDungeonId, definition.Id, row.RegionalWeight));
            }
        }
        return pool;
    }

    private static double ReadRegionalWeight(string[] tokens, string path, int line, List<string> issues)
    {
        var indexes = Enumerable.Range(0, tokens.Length)
            .Where(index => tokens[index].Equals(".chance", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (indexes.Length == 1 && indexes[0] + 1 < tokens.Length &&
            double.TryParse(tokens[indexes[0] + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var weight) &&
            double.IsFinite(weight) && weight >= 0)
        {
            return weight;
        }
        issues.Add($"地图区域资源未参与自动选择：.chance 权重缺失、重复或无效；文件={path}:{line}");
        return 0;
    }
}
