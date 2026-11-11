using System.Text;
using System.Text.Json;

namespace DarkestDungeonSaveEditor.Core;

internal static partial class QuantityItemReferenceAnalyzer
{
    private sealed partial class LootBuffSources
    {
        private sealed record UseItem(string Type, string Id, IReadOnlyList<string> Sequence);
        private readonly Dictionary<(uint Type, uint Id), UseItem> useItems = [];
        private bool itemsComplete;
        private sealed record TorchItemEffects((uint Type, uint Id) Item, IReadOnlyList<string> Normal,
            IReadOnlyList<IReadOnlyList<string>> Sequence);
        private readonly List<IReadOnlyList<TorchItemEffects>> torchStates = [];
        private readonly List<IReadOnlyList<TorchItemEffects>> possibleTorchStates = [];
        private bool torchComplete;

        private void ReadItemUse(Action<NativeEffectReferences.Reference> add)
        {
            var prior = failures;
            foreach (var file in SourceFiles("inventory", "*darkest", NativeResourceFileRules.IsInventoryItemFile))
            {
                try
                {
                    foreach (var (kind, body) in NativeDarkestReader.ReadRecordsFromText(File.ReadAllText(file.Path, new UTF8Encoding(false, true))))
                    {
                        if (kind != "inventory_item") continue;
                        // 0x4D3C46/0x4D3CB8 hash a separate 512-byte buffer.
                        var type = Bound(NativeDarkestReader.ReadString(body, ".type") ?? "", 511);
                        var id = Bound(NativeDarkestReader.ReadString(body, ".id") ?? "", 511);
                        if (type.Length == 0 || id.Length == 0) { Incomplete("Invalid inventory item identity."); continue; }
                        var key = (NativeResourceIdentity.HashCString(type), NativeResourceIdentity.HashCString(id));
                        // HeroUseItem (0x429870) selects the FIRST type/id match.
                        if (useItems.ContainsKey(key)) continue;
                        var sequence = NativeEffectReferences.List(body, ".sequential_use_effects", 16).ToArray();
                        useItems.Add(key, new(type, id, sequence));
                    }
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or EncoderFallbackException or DecoderFallbackException)
                { Incomplete($"{file.Path}: {error.Message}"); }
            }
            itemsComplete = failures == prior;
        }

        private void ReadItemEffects(Action<NativeEffectReferences.Reference> add)
        {
            if (!itemsComplete || !torchComplete) Incomplete("Item callbacks have an incomplete inventory/torch dependency.");
            foreach (var item in useItems.Values)
            {
                // Food, journals, quest items and other inventory classes have
                // separate consumers. Only these two classes reach 0x429F3D.
                if (!IsUsable(item.Type)) continue;
                foreach (var includeUnverified in new[] { false, true })
                foreach (var count in UnshadowedUseCounts(item, includeUnverified))
                {
                    var collect = includeUnverified ? UnverifiedRuntimeRoot : add;
                    // 0x42A315 selects the sequential callback instead of the
                    // same-name fallback; the hero's use count selects a step.
                    if (item.Sequence.Count > 0) collect(new(item.Sequence[Math.Min(count, item.Sequence.Count - 1)]));
                    else if (HasItemFallback(item)) { collect(new(item.Id)); break; }
                }
            }
        }

        private TorchItemEffects[][] ItemTorchStates(UseItem item, bool includeUnverified = false)
        {
            var key = (NativeResourceIdentity.HashCString(item.Type), NativeResourceIdentity.HashCString(item.Id));
            var states = includeUnverified ? possibleTorchStates : torchStates;
            return states.Select(state => state.Where(entry => entry.Item == key).ToArray()).ToArray();
        }

        private static bool HasSequenceStep(TorchItemEffects entry, int count) => entry.Sequence.Count > 0 &&
            entry.Sequence[Math.Min(count, entry.Sequence.Count - 1)].Count > 0;

        private static bool HasPerformerCallback(TorchItemEffects[] state, int count) =>
            state.Any(entry => entry.Normal.Count > 0 || HasSequenceStep(entry, count));

        private static IEnumerable<int> ReachableUseCounts(UseItem item, TorchItemEffects[][] states)
        {
            // Both native callbacks index the same typed use counter and clamp
            // to the last step. This finite prefix therefore also covers its tail.
            var limit = Math.Max(1, Math.Max(item.Sequence.Count,
                states.SelectMany(state => state).Select(entry => entry.Sequence.Count).DefaultIfEmpty().Max()));
            for (var count = 0; count < limit; count++)
            {
                yield return count;
                // A normal performer callback or same-name fallback does not
                // advance the counter (0x4FD429/0x42A940). Generic sequences
                // always advance it (0x5D755A), even if their Effect is missing.
                var advances = states.Length == 0 ? item.Sequence.Count > 0 : states.Any(state =>
                    state.Any(entry => HasSequenceStep(entry, count)) ||
                    (!HasPerformerCallback(state, count) && item.Sequence.Count > 0));
                if (!advances) yield break;
            }
        }

        private IEnumerable<int> UnshadowedUseCounts(UseItem item, bool includeUnverified = false)
        {
            var states = ItemTorchStates(item, includeUnverified);
            return ReachableUseCounts(item, states).Where(count => states.Length == 0 ||
                states.Any(state => !HasPerformerCallback(state, count)));
        }

        private static bool IsUsable(string type) => NativeResourceIdentity.HashCString(type) is var hash &&
            (hash == NativeResourceIdentity.HashCString("supply") || hash == NativeResourceIdentity.HashCString("estate"));

        private static bool HasItemFallback(UseItem item)
        {
            // 0x42A3ED/0x42A463: these identities have dedicated callbacks.
            var hash = NativeResourceIdentity.HashCString(item.Id);
            return IsUsable(item.Type) && item.Sequence.Count == 0 &&
                hash != NativeResourceIdentity.HashCString("torch") && hash != NativeResourceIdentity.HashCString("the_blood");
        }

        private bool HasItem(JsonElement entry, string typeKey, string idKey, int bytes)
        {
            var type = Text(entry, typeKey, bytes, required: true);
            var id = Text(entry, idKey, bytes, required: true);
            if (!itemsComplete) Incomplete("Item-dependent loot source has an incomplete inventory library.");
            return useItems.ContainsKey((NativeResourceIdentity.HashCString(type), NativeResourceIdentity.HashCString(id)));
        }

        private void ReadInventoryReplacements(Action<NativeEffectReferences.Reference> add)
        {
            if (!itemsComplete || !torchComplete) Incomplete("Item replacement callbacks have an incomplete inventory/torch dependency.");
            var buildings = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var document in Documents("campaign/town/districts", NativeResourceFileRules.IsDistrictFile, additive: true))
                foreach (var building in Array(document, "buildings", required: true))
                    buildings[Text(building, "name", 127, required: true)] = building;
            foreach (var building in buildings.Values)
                foreach (var entry in Array(building, "buff_list"))
                {
                    if (Text(entry, "type", 511) != "DistrictReplacementInventoryEffectBuffData") continue;
                    if (!HasItem(entry, "item_type", "item_id", 63)) continue;
                    var key = (NativeResourceIdentity.HashCString(Text(entry, "item_type", 63)),
                        NativeResourceIdentity.HashCString(Text(entry, "item_id", 63)));
                    if (!HasItemFallback(useItems[key])) continue;
                    var verified = UnshadowedUseCounts(useItems[key]).Any();
                    if (!verified && !UnshadowedUseCounts(useItems[key], includeUnverified: true).Any()) continue;
                    // 0x451760 bounds all three fields before hashing. The
                    // district listener replaces the same-name item Effect;
                    // an unbuilt district still permits the normal route.
                    var effect = new NativeEffectReferences.Reference(Text(entry, "effect", 63, required: true));
                    if (verified) add(effect); else UnverifiedRuntimeRoot(effect);
                }
        }
    }
}
