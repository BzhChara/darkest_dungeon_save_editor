namespace DarkestDungeonSaveEditor.Core;

public sealed record HeroGenerationAvailability(int ResolveLevel, string UnavailableReason)
{
    public bool CanGenerate => string.IsNullOrEmpty(UnavailableReason);
}

public static partial class StagecoachHeroCandidateFactory
{
    public static IReadOnlyList<HeroGenerationAvailability> GetGenerationAvailability(
        HeroClassCatalogResult catalog,
        HeroClassDefinition heroClass)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(heroClass);
        return Enumerable.Range(0, Math.Max(1, catalog.ResolveLevelThresholds.Count))
            .Select(level =>
            {
                try
                {
                    // Pure in-memory preflight: use the complete generation path so
                    // UI availability cannot drift from skill/upgrade/HP validation.
                    // User-selected quirks are still checked by the actual preview.
                    _ = Generate(catalog, heroClass, seed: 0, level, []);
                    return new HeroGenerationAvailability(level, string.Empty);
                }
                catch (Exception error) when (error is InvalidOperationException or ArgumentException)
                {
                    return new HeroGenerationAvailability(level, error.Message);
                }
            })
            .ToArray();
    }
}
