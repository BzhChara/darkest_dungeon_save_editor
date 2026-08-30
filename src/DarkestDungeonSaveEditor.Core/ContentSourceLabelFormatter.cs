namespace DarkestDungeonSaveEditor.Core;

internal static class ContentSourceLabelFormatter
{
    public static string Format(
        string effectiveSourceId,
        IEnumerable<string> providerSourceIds,
        IReadOnlyList<ActiveContentSource> activeSources)
    {
        var sourcesById = activeSources.ToDictionary(source => source.Id, StringComparer.OrdinalIgnoreCase);
        var providerIds = providerSourceIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (effectiveSourceId.Equals("unresolved", StringComparison.OrdinalIgnoreCase))
        {
            var labels = providerIds
                .Select(id => sourcesById.TryGetValue(id, out var source) ? FormatSingle(source) : id)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return labels.Length == 0
                ? "未解析"
                : $"未解析：{string.Join("、", labels)}";
        }

        if (!sourcesById.TryGetValue(effectiveSourceId, out var declaredEffectiveSource))
        {
            return effectiveSourceId;
        }

        var effectiveSource = providerIds
            .Select(id => sourcesById.TryGetValue(id, out var source) ? source : null)
            .Where(source => source is not null)
            .Select(source => source!)
            .Aggregate(
                declaredEffectiveSource,
                (current, candidate) => ContentFileOverlay.ComparePriority(candidate, current) > 0
                    ? candidate
                    : current);

        var officialOrigin = providerIds
            .Select(id => sourcesById.TryGetValue(id, out var source) ? source : null)
            .Where(source => source is not null && IsOfficial(source.Kind))
            .Select(source => source!)
            .OrderBy(source => GetOfficialOriginOrder(source.Kind))
            .ThenBy(source => source.LoadOrder)
            .FirstOrDefault();
        if (officialOrigin is null ||
            officialOrigin.Id.Equals(effectiveSource.Id, StringComparison.OrdinalIgnoreCase))
        {
            return FormatSingle(effectiveSource);
        }

        return $"{FormatSingle(officialOrigin)}（当前由 {FormatSingle(effectiveSource)} 覆盖）";
    }

    private static bool IsOfficial(string kind) => kind is
        "base" or "mode" or "dlc" or "dlc-package" or "dlc-feature";

    private static int GetOfficialOriginOrder(string kind) => kind switch
    {
        "base" => 0,
        "mode" => 1,
        _ => 2
    };

    private static string FormatSingle(ActiveContentSource source) => source.Kind switch
    {
        "base" => "原版",
        "mode" => $"官方模式：{source.DisplayName}",
        "dlc" or "dlc-package" or "dlc-feature" => $"官方 DLC：{source.DisplayName}",
        "workshop" => $"创意工坊 Mod：{source.DisplayName}",
        "local" => $"本地 Mod：{source.DisplayName}",
        _ => source.DisplayName
    };
}
