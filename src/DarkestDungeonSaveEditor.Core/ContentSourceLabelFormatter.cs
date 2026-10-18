using System.Globalization;

namespace DarkestDungeonSaveEditor.Core;

// Retain provenance, rather than a translated string, in a loaded catalog.
// Language changes never re-read resources or alter overlay resolution.
internal sealed class ContentSourceLabel(
    string effectiveSourceId,
    IReadOnlyList<string> providerSourceIds,
    IReadOnlyList<ActiveContentSource> activeSources)
{
    // Source lookup can be substantial with many Mods. Resolve at most once per
    // language, rather than on each cell repaint or search keystroke.
    private readonly Lazy<string> _english = new(() => ContentSourceLabelFormatter.Format(
        effectiveSourceId, providerSourceIds, activeSources, CultureInfo.GetCultureInfo("en")));
    private readonly Lazy<string> _chinese = new(() => ContentSourceLabelFormatter.Format(
        effectiveSourceId, providerSourceIds, activeSources, CultureInfo.GetCultureInfo("zh-CN")));
    public override string ToString() => EditorText.IsChinese ? _chinese.Value : _english.Value;
}

internal static class ContentSourceLabelFormatter
{
    public static ContentSourceLabel Create(string effectiveSourceId, IEnumerable<string> providerSourceIds,
        IReadOnlyList<ActiveContentSource> activeSources) => new(effectiveSourceId, providerSourceIds.ToArray(), activeSources);

    public static string Format(
        string effectiveSourceId,
        IEnumerable<string> providerSourceIds,
        IReadOnlyList<ActiveContentSource> activeSources,
        CultureInfo? culture = null)
    {
        culture ??= EditorText.Culture;
        var sourcesById = activeSources.ToDictionary(source => source.Id, StringComparer.OrdinalIgnoreCase);
        var providerIds = providerSourceIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (effectiveSourceId.Equals("unresolved", StringComparison.OrdinalIgnoreCase))
        {
            var labels = providerIds
                .Select(id => sourcesById.TryGetValue(id, out var source) ? FormatSingle(source, culture) : id)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return labels.Length == 0
                ? EditorText.Get("BattleMapLogTracker_017", culture)
                : EditorText.Format(culture, "ContentSourceLabelFormatter_001", string.Join("、", labels));
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
            return FormatSingle(effectiveSource, culture);
        }

        return EditorText.Format(culture, "ContentSourceLabelFormatter_002", FormatSingle(officialOrigin, culture), FormatSingle(effectiveSource, culture));
    }

    private static bool IsOfficial(string kind) => kind is
        "base" or "mode" or "dlc" or "dlc-package" or "dlc-feature";

    private static int GetOfficialOriginOrder(string kind) => kind switch
    {
        "base" => 0,
        "mode" => 1,
        _ => 2
    };

    private static string FormatSingle(ActiveContentSource source, CultureInfo culture) => source.Kind switch
    {
        "base" => EditorText.Get("ContentSourceLabelFormatter_003", culture),
        "mode" => EditorText.Format(culture, "ContentSourceLabelFormatter_004", source.DisplayName),
        "dlc" or "dlc-package" or "dlc-feature" => EditorText.Format(culture, "ContentSourceLabelFormatter_005", source.DisplayName),
        "workshop" => EditorText.Format(culture, "ContentSourceLabelFormatter_006", source.DisplayName),
        "local" => EditorText.Format(culture, "ContentSourceLabelFormatter_007", source.DisplayName),
        _ => source.DisplayName
    };
}
