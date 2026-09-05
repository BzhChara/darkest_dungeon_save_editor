namespace DarkestDungeonSaveEditor.Core;

public static partial class BattleEncounterCatalog
{
    public static IReadOnlyList<BattleEncounterDefinition> GetSelectionCandidates(
        BattleEncounterCatalogResult catalog,
        int mashType,
        IReadOnlyCollection<BattleEncounterClassification> classifications)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(classifications);
        var fixedBossOnly = classifications.Count == 1 &&
                            classifications.Contains(BattleEncounterClassification.FixedBoss);
        var ordinaryOnly = classifications.Count == 1 &&
                           classifications.Contains(BattleEncounterClassification.Ordinary);
        var direct = catalog.DirectEncounters.Where(encounter =>
            encounter.HasKnownClassification && encounter.MashType == mashType &&
            (fixedBossOnly
                ? encounter.Classification == BattleEncounterClassification.FixedBoss
                : ordinaryOnly
                    ? encounter.Classification == BattleEncounterClassification.Ordinary
                    : encounter.Weight is <= 0));

        return direct.Concat(catalog.BridgeEncounters)
            .Where(encounter => encounter.HasKnownClassification &&
                                encounter.MashType == mashType &&
                                classifications.Contains(encounter.Classification))
            // Filter by purpose before deduplication. An identical party can legitimately
            // appear in both ordinary and conditional tables, and at several difficulties.
            .GroupBy(
                encounter => $"{encounter.MashType}\n{encounter.OriginDifficulty}\n{string.Join('\n', encounter.MonsterIds)}",
                StringComparer.Ordinal)
            .Select(group => group
                .OrderBy(encounter => SelectionClassificationOrder(encounter.Classification))
                .ThenBy(encounter => encounter.CanPlaceDirectly)
                .ThenByDescending(encounter => !string.IsNullOrWhiteSpace(encounter.RoamingId))
                .ThenBy(encounter => encounter.SourceKind)
                .First())
            .OrderBy(encounter => encounter.OriginDungeonId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(encounter => encounter.OriginDifficulty)
            .ThenBy(encounter => encounter.SourceKind)
            .ThenBy(encounter => encounter.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static int SelectionClassificationOrder(BattleEncounterClassification classification) => classification switch
    {
        BattleEncounterClassification.FixedBoss => 0,
        BattleEncounterClassification.RoamingBoss => 1,
        BattleEncounterClassification.RoamingEncounter => 2,
        BattleEncounterClassification.ConditionalOrAdditional => 3,
        _ => 4
    };
}
