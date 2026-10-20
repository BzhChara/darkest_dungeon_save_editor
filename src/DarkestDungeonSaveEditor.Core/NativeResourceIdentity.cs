namespace DarkestDungeonSaveEditor.Core;

internal static class NativeResourceIdentity
{
    // JSON Buff/quirk identities and references stop at the first NUL, then
    // hash the full UTF-8 string (independent of their display buffer sizes).
    public static uint HashCString(string id) => Loc2LocalizationReader.HashName(NativeJsonReader.CString(id));

    // Reuse the game's UTF-8 polynomial hash. Different strings can address
    // the same native definition, so a string-only editor row is not safe.
    public static IReadOnlySet<string> FindCollisions<T, TKey>(IEnumerable<T> values,
        Func<T, string> identity, Func<T, TKey> nativeKey) where TKey : notnull =>
        values.GroupBy(nativeKey)
            .Select(group => group.Select(identity).Distinct(StringComparer.Ordinal).ToArray())
            .Where(ids => ids.Length > 1).SelectMany(ids => ids).ToHashSet(StringComparer.Ordinal);

    public static IReadOnlySet<string> FindCollisions(IEnumerable<string> ids) =>
        FindCollisions(ids, id => id, Loc2LocalizationReader.HashName);

    internal static string? DescribeHashResolution<T>(
        IGrouping<uint, T> group, T winner, Func<T, string> id, Func<T, string> path, string messageKey)
    {
        if (!group.Select(id).Distinct(StringComparer.Ordinal).Skip(1).Any()) return null;
        var providers = string.Join("; ", group.Select(value => (Id: id(value), Path: path(value)))
            .Distinct().Select(value => $"'{value.Id}' ('{value.Path}')"));
        return CatalogIssueCode.NativeHashResolution + EditorText.Format(messageKey,
            group.Key.ToString(System.Globalization.CultureInfo.InvariantCulture), providers, id(winner), path(winner));
    }
}
