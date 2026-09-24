namespace DarkestDungeonSaveEditor.Core;

public static partial class BattleEncounterCatalog
{
    // These suffixes are observed native mash fields, not repair rules. A suffix
    // is only a diagnostic clue when the complete ID is missing and its prefix exists.
    private static readonly string[] GluedMonsterFieldSuffixes = [".limit", ".quirk_tag"];

    // Observed MashGuide fields are diagnostic clues only. Native actor slots
    // do not stop at these strings; never use this set to repair a formation.
    private static readonly HashSet<string> EncounterFieldSlotClues = new(new[]
    {
        ".name", ".chance", ".dungeon_length_range", ".loading_screen", ".flashback_id",
        ".darkness_range", ".inventory_valid_item_percent_range", ".infestation_sequence_element",
        ".infestation_activity_level", ".min_vampire_heroes", ".max_vampire_heroes",
        ".quirk_tag", ".min_quirk_tags", ".max_quirk_tags", ".completed_flashback_id",
        ".limit", ".can_be_ambush", ".plot_quest_requirement", ".random_dungeon_roaming_id"
    // All field literals are ASCII; avoid depending on another partial file's
    // static UTF-8 decoder during type initialization.
    }.Select(field => field[..Math.Min(field.Length, 31)]), StringComparer.Ordinal);

    private sealed record UnresolvedEncounterDiagnostic(
        BattleEncounterDefinition Encounter,
        string[] UnresolvedTokens,
        string[] GluedFieldTokens,
        string[] FieldSlotTokens);

    private static UnresolvedEncounterDiagnostic CreateUnresolvedEncounterDiagnostic(
        BattleEncounterDefinition encounter,
        string[] unresolvedTokens,
        IReadOnlySet<string> availableIds) => new(
            encounter,
            unresolvedTokens,
            unresolvedTokens.Where(token => GluedMonsterFieldSuffixes.Any(suffix =>
                token.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) &&
                availableIds.Contains(token[..^suffix.Length]))).ToArray(),
            unresolvedTokens.Where(EncounterFieldSlotClues.Contains).ToArray());

    private static void AddUnresolvedEncounterIssues(
        IReadOnlyList<BattleEncounterDefinition> candidates,
        IReadOnlySet<string> availableIds,
        List<string> issues)
    {
        var rows = candidates
            .Select(encounter => CreateUnresolvedEncounterDiagnostic(
                encounter, GetMissingMonsterIds(encounter, availableIds), availableIds))
            .Where(row => row.UnresolvedTokens.Length > 0)
            .OrderBy(row => row.Encounter.SourcePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.Encounter.SourceLine)
            .ThenBy(row => row.Encounter.SourceRecordIndex)
            .ToArray();
        if (rows.Length == 0)
        {
            return;
        }

        var gluedRows = rows.Count(row => row.GluedFieldTokens.Length > 0);
        var fieldSlotRows = rows.Count(row => row.GluedFieldTokens.Length == 0 && row.FieldSlotTokens.Length > 0);
        var unresolvedTokenCount = rows.SelectMany(row => row.UnresolvedTokens)
            .Distinct(StringComparer.Ordinal).Count();
        issues.Add(
            EditorText.Format("BattleEncounterCatalog_Diagnostics_001", rows.Length) +
            EditorText.Format("BattleEncounterCatalog_Diagnostics_002", gluedRows, fieldSlotRows) +
            EditorText.Format("BattleEncounterCatalog_Diagnostics_003", rows.Length - gluedRows - fieldSlotRows) +
            EditorText.Format("BattleEncounterCatalog_Diagnostics_004", unresolvedTokenCount) +
            EditorText.Get("BattleEncounterCatalog_Diagnostics_005"));
        foreach (var row in rows)
        {
            issues.Add(EditorText.Get("BattleEncounterCatalog_Diagnostics_006") + FormatUnresolvedEncounter(row));
        }
    }

    private static string FormatUnresolvedEncounter(UnresolvedEncounterDiagnostic row)
    {
        var encounter = row.Encounter;
        var location = encounter.MashType switch
        {
            0 => EditorText.Get("BattleEncounterCatalog_Diagnostics_007"),
            1 => EditorText.Get("BattleEncounterCatalog_Diagnostics_008"),
            _ => EditorText.Get("BattleEncounterCatalog_Diagnostics_009")
        };
        var reason = row.GluedFieldTokens.Length > 0
            ? EditorText.Get("BattleEncounterCatalog_Diagnostics_010") + string.Join(", ", row.GluedFieldTokens) +
              EditorText.Get("BattleEncounterCatalog_Diagnostics_011") +
              EditorText.Get("BattleEncounterCatalog_Diagnostics_012")
            : row.FieldSlotTokens.Length > 0
            ? EditorText.Get("BattleEncounterCatalog_Diagnostics_013") + string.Join(", ", row.FieldSlotTokens) +
              EditorText.Get("BattleEncounterCatalog_Diagnostics_014") +
              EditorText.Get("BattleEncounterCatalog_Diagnostics_015")
            : EditorText.Get("BattleEncounterCatalog_Diagnostics_016");
        return EditorText.Format("BattleEncounterCatalog_Diagnostics_017", reason, encounter.SourceLabel) +
            EditorText.Format("BattleEncounterCatalog_Diagnostics_018", encounter.SourcePath, encounter.SourceLine, encounter.SourceRecordIndex) +
            EditorText.Format("BattleEncounterCatalog_Diagnostics_019", encounter.OriginDungeonId, encounter.OriginDifficulty, location) +
            EditorText.Format("BattleEncounterCatalog_Diagnostics_020", string.Join(", ", encounter.MonsterIds)) +
            EditorText.Format("BattleEncounterCatalog_Diagnostics_021", string.Join(", ", row.UnresolvedTokens));
    }
}
