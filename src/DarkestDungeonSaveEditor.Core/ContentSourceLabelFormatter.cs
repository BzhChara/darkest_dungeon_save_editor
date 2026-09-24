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
                ? EditorText.Get("BattleMapLogTracker_017")
                : EditorText.Format("ContentSourceLabelFormatter_001", string.Join("、", labels));
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

        return EditorText.Format("ContentSourceLabelFormatter_002", FormatSingle(officialOrigin), FormatSingle(effectiveSource));
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
        "base" => EditorText.Get("ContentSourceLabelFormatter_003"),
        "mode" => EditorText.Format("ContentSourceLabelFormatter_004", source.DisplayName),
        "dlc" or "dlc-package" or "dlc-feature" => EditorText.Format("ContentSourceLabelFormatter_005", source.DisplayName),
        "workshop" => EditorText.Format("ContentSourceLabelFormatter_006", source.DisplayName),
        "local" => EditorText.Format("ContentSourceLabelFormatter_007", source.DisplayName),
        _ => source.DisplayName
    };
}
