using System.Globalization;
using System.Text.Json.Nodes;

namespace DarkestDungeonSaveEditor.Core;

public static partial class StagecoachHeroCandidateFactory
{
    private static IReadOnlyList<HeroInitialQuirkDefinition> ResolveSelectedQuirks(
        HeroClassCatalogResult catalog,
        IReadOnlyCollection<string> selectedIds)
    {
        var uniqueIds = new HashSet<string>(StringComparer.Ordinal);
        var selected = new List<HeroInitialQuirkDefinition>(selectedIds.Count);
        foreach (var rawId in selectedIds)
        {
            var id = rawId;
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new InvalidOperationException(EditorText.Get("StagecoachHeroCandidateFactory_Quirks_001"));
            }
            if (!uniqueIds.Add(id))
            {
                throw new InvalidOperationException(EditorText.Format("StagecoachHeroCandidateFactory_Quirks_002", id));
            }

            var matches = catalog.InitialQuirks
                .Where(quirk => quirk.Id.Equals(id, StringComparison.Ordinal))
                .ToArray();
            if (matches.Length != 1)
            {
                throw new InvalidOperationException(
                    matches.Length == 0
                        ? EditorText.Format("StagecoachHeroCandidateFactory_Quirks_003", id)
                        : EditorText.Format("StagecoachHeroCandidateFactory_Quirks_004", id));
            }

            var quirk = matches[0];
            if (quirk.IsPositive is null)
            {
                throw new InvalidOperationException(EditorText.Format("StagecoachHeroCandidateFactory_Quirks_005", quirk.Id));
            }
            var canWriteWithPreviewLimitCheck =
                quirk.WriteStatus == HeroInitialQuirkWriteStatus.RequiresSaveContext &&
                quirk.DefinitionLimit is > 0;
            if (quirk.WriteStatus != HeroInitialQuirkWriteStatus.Direct &&
                !canWriteWithPreviewLimitCheck)
            {
                throw new InvalidOperationException(
                    EditorText.Format("StagecoachHeroCandidateFactory_Quirks_006", quirk.Id, quirk.WriteStatusReason));
            }
            selected.Add(quirk);
        }

        // Native positive and disease predicates overlap (0x1404E0D50 / 0x1404E0BF0).
        var positiveCount = selected.Count(quirk => quirk.IsPositive == true);
        var negativeCount = selected.Count(quirk => !quirk.IsDisease && quirk.IsPositive == false);
        var diseaseCount = selected.Count(quirk => quirk.IsDisease);
        var limits = catalog.InitialQuirkLimits;
        if ((positiveCount > 0 && limits.Positive is null) ||
            (negativeCount > 0 && limits.Negative is null) ||
            (diseaseCount > 0 && limits.Diseases is null))
            throw new InvalidOperationException(EditorText.Get("StagecoachHeroCandidateFactory_Quirks_007"));
        if (positiveCount > limits.Positive || negativeCount > limits.Negative || diseaseCount > limits.Diseases)
        {
            throw new InvalidOperationException(
                EditorText.Format("StagecoachHeroCandidateFactory_Quirks_008", limits.Positive?.ToString(CultureInfo.InvariantCulture) ?? EditorText.Get("BattleEncounterSelectionDialog_017")) +
                EditorText.Format("StagecoachHeroCandidateFactory_Quirks_009", limits.Negative?.ToString(CultureInfo.InvariantCulture) ?? EditorText.Get("BattleEncounterSelectionDialog_017")) +
                EditorText.Format("StagecoachHeroCandidateFactory_Quirks_010", limits.Diseases?.ToString(CultureInfo.InvariantCulture) ?? EditorText.Get("BattleEncounterSelectionDialog_017")) +
                EditorText.Format("StagecoachHeroCandidateFactory_Quirks_011", positiveCount, negativeCount, diseaseCount));
        }

        for (var leftIndex = 0; leftIndex < selected.Count; leftIndex++)
        {
            for (var rightIndex = leftIndex + 1; rightIndex < selected.Count; rightIndex++)
            {
                var left = selected[leftIndex];
                var right = selected[rightIndex];
                if (left.IncompatibleQuirkIds.Any(id => NativeResourceIdentity.HashCString(id) == NativeResourceIdentity.HashCString(right.Id)) ||
                    right.IncompatibleQuirkIds.Any(id => NativeResourceIdentity.HashCString(id) == NativeResourceIdentity.HashCString(left.Id)))
                {
                    throw new InvalidOperationException(
                        EditorText.Format("StagecoachHeroCandidateFactory_Quirks_012", left.Id, right.Id));
                }
            }
        }

        return selected;
    }

}
