using System.Globalization;
using System.Text.Json.Nodes;

namespace DarkestDungeonSaveEditor.Core;

public static partial class StagecoachHeroCandidateFactory
{
    private static int ResolveEvolutionDuration(int seed, HeroInitialQuirkDefinition quirk)
    {
        if (quirk.Evolution is not { } evolution)
        {
            return 0;
        }

        if (evolution.DurationMin == evolution.DurationMax)
        {
            return evolution.DurationMin;
        }

        var span = (ulong)((long)evolution.DurationMax - evolution.DurationMin + 1L);
        var offset = (int)(ComputeStableEvolutionHash(seed, quirk.Id) % span);
        return evolution.DurationMin + offset;
    }

    private static ulong ComputeStableEvolutionHash(int seed, string quirkId)
    {
        const ulong offsetBasis = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        var hash = offsetBasis;
        var seedBits = unchecked((uint)seed);
        for (var shift = 0; shift < 32; shift += 8)
        {
            hash ^= (byte)(seedBits >> shift);
            hash *= prime;
        }

        foreach (var character in quirkId)
        {
            hash ^= (byte)character;
            hash *= prime;
            hash ^= (byte)(character >> 8);
            hash *= prime;
        }

        return hash;
    }

    private static string FormatEvolutionSummary(InitialQuirkPersistenceState state)
    {
        var evolution = state.Definition.Evolution!;
        var outcome = evolution.CausesDeath
            ? string.IsNullOrWhiteSpace(evolution.TargetQuirkId)
                ? EditorText.Get("StagecoachHeroCandidateFactory_Evolution_001")
                : EditorText.Format("StagecoachHeroCandidateFactory_Evolution_002", evolution.TargetQuirkId)
            : $"→ {evolution.TargetQuirkId}";
        return $"{state.Definition.Id}={state.EvolutionDurationRemaining}" +
               EditorText.Format("StagecoachHeroCandidateFactory_Evolution_003", evolution.DurationMin, evolution.DurationMax, outcome);
    }

}
