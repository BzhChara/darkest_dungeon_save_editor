namespace DarkestDungeonSaveEditor.Core;

internal static class NativeResourceIdentity
{
    // Reuse the game's UTF-8 polynomial hash. Different strings can address
    // the same native definition, so a string-only editor row is not safe.
    public static IReadOnlySet<string> FindCollisions<T, TKey>(IEnumerable<T> values,
        Func<T, string> identity, Func<T, TKey> nativeKey) where TKey : notnull =>
        values.GroupBy(nativeKey)
            .Select(group => group.Select(identity).Distinct(StringComparer.Ordinal).ToArray())
            .Where(ids => ids.Length > 1).SelectMany(ids => ids).ToHashSet(StringComparer.Ordinal);

    public static IReadOnlySet<string> FindCollisions(IEnumerable<string> ids) =>
        FindCollisions(ids, id => id, Loc2LocalizationReader.HashName);
}
