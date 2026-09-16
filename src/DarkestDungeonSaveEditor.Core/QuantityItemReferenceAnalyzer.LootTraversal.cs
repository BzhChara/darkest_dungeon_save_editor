namespace DarkestDungeonSaveEditor.Core;

internal static partial class QuantityItemReferenceAnalyzer
{
    private readonly record struct LootRange(uint Min, uint Max)
    {
        public static LootRange Any => new(0, uint.MaxValue);
        public bool IsEmpty => Min > Max;
        public LootRange Intersect(LootRange other) => new(Math.Max(Min, other.Min), Math.Min(Max, other.Max));
    }

    // Four independent native selection fields. Intervals also represent the
    // complement of an exact dungeon/difficulty/infestation filter, without
    // enumerating guessed game IDs or restricting the catalog to today's raid.
    private readonly record struct LootContext(LootRange Difficulty, LootRange Dungeon, LootRange Infestation, LootRange Week)
    {
        public static LootContext Any => new(LootRange.Any, LootRange.Any, LootRange.Any, LootRange.Any);
        public bool IsEmpty => Difficulty.IsEmpty || Dungeon.IsEmpty || Infestation.IsEmpty || Week.IsEmpty;
        public LootContext Intersect(LootContext other) => new(Difficulty.Intersect(other.Difficulty),
            Dungeon.Intersect(other.Dungeon), Infestation.Intersect(other.Infestation), Week.Intersect(other.Week));
        private LootRange At(int dimension) => dimension switch
        {
            0 => Difficulty, 1 => Dungeon, 2 => Infestation, _ => Week
        };
        private LootContext With(int dimension, LootRange range) => dimension switch
        {
            0 => this with { Difficulty = range }, 1 => this with { Dungeon = range },
            2 => this with { Infestation = range }, _ => this with { Week = range }
        };

        // Split this box around the overlap; the pieces are disjoint and
        // exactly preserve every context not consumed by an earlier variant.
        public IEnumerable<LootContext> Except(LootContext other)
        {
            if (IsEmpty) yield break;
            var intersection = Intersect(other);
            if (intersection.IsEmpty) { yield return this; yield break; }
            var remainder = this;
            for (var dimension = 0; dimension < 4; dimension++)
            {
                var range = remainder.At(dimension);
                var cut = intersection.At(dimension);
                if (range.Min < cut.Min)
                    yield return remainder.With(dimension, new(range.Min, cut.Min - 1));
                remainder = remainder.With(dimension, new(cut.Min, range.Max));
                if (range.Max > cut.Max)
                    yield return remainder.With(dimension, new(cut.Max + 1, range.Max));
                remainder = remainder.With(dimension, cut);
            }
        }
    }

    private static void TraverseLootRoots(LootTableLibrary library,
        IReadOnlyDictionary<string, List<string>> roots, IReadOnlyDictionary<string, List<string>> uncertainRoots,
        Dictionary<string, List<string>> activeEvidence, Dictionary<string, List<string>> incompleteEvidence)
    {
        var queue = new Queue<(LootCode Code, LootContext Context, bool Uncertain, string Evidence)>();
        void Roots(IReadOnlyDictionary<string, List<string>> evidence, bool uncertain)
        {
            foreach (var pair in evidence)
            foreach (var source in pair.Value)
                queue.Enqueue((ReadLootCode(pair.Key), LootContext.Any, uncertain, source));
        }
        Roots(roots, false);
        Roots(uncertainRoots, true);
        var visited = new Dictionary<(uint Hash, bool Uncertain), List<LootContext>>();
        while (queue.TryDequeue(out var state))
        {
            if (!library.Tables.TryGetValue(state.Code.Hash, out var variants)) continue;
            var key = (state.Code.Hash, state.Uncertain);
            if (!visited.TryGetValue(key, out var seen)) visited[key] = seen = [];
            var remaining = new List<LootContext> { state.Context };
            foreach (var prior in seen)
                remaining = remaining.SelectMany(context => context.Except(prior)).ToList();
            if (remaining.Count == 0) continue;
            seen.AddRange(remaining);

            foreach (var variant in variants)
            {
                var selected = remaining.Select(context => context.Intersect(variant.Context)).Where(context => !context.IsEmpty).ToArray();
                if (selected.Length == 0) continue;
                var chain = $"{state.Evidence} → 掉落表 {state.Code.Name}（{variant.Source}）";
                var uncertainChain = chain + "（掉落权重无法确认）";
                var evidence = state.Uncertain ? incompleteEvidence : activeEvidence;
                foreach (var item in variant.ItemKeys) AddEvidence(evidence, item, chain);
                foreach (var item in variant.UncertainItemKeys) AddEvidence(incompleteEvidence, item, uncertainChain);
                foreach (var item in variant.ConflictedItemKeys) AddEvidence(incompleteEvidence, item, chain + "（物品定义冲突，无法确认）");
                foreach (var context in selected)
                {
                    foreach (var nested in variant.NestedTables)
                        queue.Enqueue((nested, context, state.Uncertain, chain));
                    foreach (var nested in variant.UncertainNestedTables)
                        queue.Enqueue((nested, context, true, uncertainChain));
                }
                // Selection precedes entry rolling: a matched empty table also
                // removes its contexts, rather than falling through to another.
                remaining = remaining.SelectMany(context => context.Except(variant.Context)).ToList();
                if (remaining.Count == 0) break;
            }
        }
    }
}
