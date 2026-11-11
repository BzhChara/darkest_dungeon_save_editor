namespace DarkestDungeonSaveEditor.Core;

public static partial class HeroClassCatalog
{
    private static IEnumerable<HeroRuntimeQuirkSignal> ResolveRuntimeQuirks(
        IReadOnlyList<SkillEffectReference> skills,
        IReadOnlyList<CampingSkillBuilder> camping,
        IReadOnlyDictionary<uint, EffectQuirkAssignment> effects,
        IReadOnlyDictionary<uint, QuirkDefinition> quirks,
        IReadOnlyDictionary<uint, BuffDefinition> buffs)
    {
        var roots = skills.Select(skill => (Origin: skill.SkillId,
                Reference: new NativeEffectReferences.Reference(skill.EffectName)))
            .Concat(camping.SelectMany(skill => skill.References.Select(reference => (Origin: skill.Id, Reference: reference))));
        // The origin stays attached across indirect paths. A visited set per
        // skill keeps two legitimate origins, while terminating Effect/Buff and
        // riposte cycles. This metadata never participates in initial grants.
        foreach (var origin in roots.GroupBy(root => root.Origin, StringComparer.Ordinal))
        {
            var queue = new Queue<(NativeEffectReferences.Reference Reference, IReadOnlyList<string> Sources)>();
            foreach (var root in origin) queue.Enqueue((root.Reference, []));
            var visited = new HashSet<(bool Buff, uint Id)>();
            while (queue.TryDequeue(out var next))
            {
                var hash = NativeResourceIdentity.HashCString(next.Reference.Id);
                if (next.Reference.Id.Length == 0) continue;
                var firstVisit = visited.Add((next.Reference.Buff, hash));
                if (next.Reference.Buff)
                {
                    if (!firstVisit || !buffs.TryGetValue(hash, out var buff)) continue;
                    foreach (var reference in buff.Triggers)
                        queue.Enqueue((reference, next.Sources.Append(buff.Source).ToArray()));
                }
                else if (effects.TryGetValue(hash, out var effect))
                {
                    var sources = next.Sources.Append(effect.Source).ToArray();
                    var reference = new SkillEffectReference(origin.Key, "effect", next.Reference.Id);
                    if (ResolveRuntimeQuirk(reference, effects, quirks) is { } signal)
                    {
                        var quirk = quirks[NativeResourceIdentity.HashCString(signal.QuirkId)];
                        yield return signal with
                        {
                            Source = string.Join(" -> ", sources.Append(quirk.Source).Distinct(StringComparer.OrdinalIgnoreCase))
                        };
                    }
                    // Hash aliases share traversal, but remain separate authored
                    // Effect names in the existing source-description contract.
                    if (firstVisit)
                        foreach (var target in effect.Links.Targets()) queue.Enqueue((target, sources));
                }
            }
        }
    }
}
