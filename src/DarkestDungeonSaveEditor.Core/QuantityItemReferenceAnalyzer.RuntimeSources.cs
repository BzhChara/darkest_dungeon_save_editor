using System.Text;
using System.Text.Json;

namespace DarkestDungeonSaveEditor.Core;

internal static partial class QuantityItemReferenceAnalyzer
{
    private sealed partial class LootBuffSources
    {
        private readonly List<NativeEffectReferences.Reference> unverifiedRuntimeRoots = [];
        private readonly HashSet<uint> selectedTorchKeys = [NativeResourceIdentity.HashCString("default")];
        private void UnverifiedRuntimeRoot(NativeEffectReferences.Reference entry) => unverifiedRuntimeRoots.Add(entry);

        internal bool ReadRuntime(IReadOnlyList<ScannedContentFile> roots, TrinketHeroDependencies heroes,
            IReadOnlySet<uint> obtainableQuirkActOuts, bool quirksComplete, IReadOnlyDictionary<uint, JsonElement> buffs,
            Action<NativeEffectReferences.Reference, bool> reference)
        {
            void Source(Action<Action<NativeEffectReferences.Reference>> read)
            {
                var prior = failures;
                var found = new List<NativeEffectReferences.Reference>();
                try { read(found.Add); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or
                    InvalidDataException or EncoderFallbackException or DecoderFallbackException)
                { Incomplete(error.Message); }
                foreach (var entry in found) reference(entry, prior == failures);
            }
            Source(ReadItemUse);
            Source(add =>
            {
                if (!heroes.IsComplete) Incomplete("Flashback admission has incomplete hero dependencies.");
                ReadFlashbacks(heroes, add);
            });
            Source(ReadTorch);
            Source(ReadItemEffects);
            Source(ReadInventoryReplacements);
            Source(ReadQuestModifiers);
            Source(ReadRules);
            Source(add => ReadActivities(buffs, add));
            Source(add => ReadTraitActOuts(heroes, add));
            Source(add =>
            {
                if (!quirksComplete) Incomplete("Quirk act-outs have an incomplete quirk library.");
                ReadQuirkActOuts(obtainableQuirkActOuts, heroes, add);
            });
            Source(add => ReadCurioEffects(roots, add));
            Source(ReadTraps);
            foreach (var entry in unverifiedRuntimeRoots) reference(entry, false);
            return failures == 0;
        }

        private JsonElement Object(JsonElement node, string key, bool required = false)
        {
            if (!NativeJsonReader.TryGetProperty(node, key, out var value))
            {
                if (required) Incomplete($"Missing {key} object.");
                return default;
            }
            if (value.ValueKind == JsonValueKind.Object) return value;
            Incomplete($"Invalid {key} object.");
            return default;
        }

        private IEnumerable<JsonElement> Objects(JsonElement node, string key, bool required = false)
        {
            foreach (var entry in Array(node, key, required))
            {
                if (entry.ValueKind != JsonValueKind.Object) { Incomplete($"Invalid {key} record."); continue; }
                yield return entry;
            }
        }

        private float Number(JsonElement node, string key, float fallback = 0)
        {
            if (!NativeJsonReader.TryGetProperty(node, key, out var value)) return fallback;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetSingle(out var result) && float.IsFinite(result)) return result;
            Incomplete($"Invalid {key} number.");
            return fallback;
        }

        private void BuffList(JsonElement node, string key, Action<NativeEffectReferences.Reference> add, int? bytes = null)
        {
            foreach (var id in Strings(node, key, bytes)) add(new(id, Buff: true));
        }

        private void EffectList(JsonElement node, string key, Action<NativeEffectReferences.Reference> add,
            bool combat, int? bytes = null)
        {
            foreach (var id in Strings(node, key, bytes)) add(new(id, Combat: combat));
        }

        private void ReadTorch(Action<NativeEffectReferences.Reference> add)
        {
            var prior = failures;
            // 0x4C13E0 uses flags 9; 0x4C16E0 replaces the entire map value.
            // The key is bounded to 511 bytes before hashing, not its 63-byte label.
            var settings = new Dictionary<uint, JsonElement>();
            foreach (var document in Documents("scripts", NativeResourceFileRules.IsRaidSettingsFile, additive: true))
                foreach (var entry in Objects(document, "torch_settings_data_table"))
                {
                    var key = Text(entry, "key", 511, required: true);
                    if (key.Length == 0) { Incomplete("Empty torch setting key."); continue; }
                    settings[NativeResourceIdentity.HashCString(key)] = Object(entry, "data", required: true);
                }
            foreach (var (key, data) in settings)
            {
                // Loading a map value does not select it. Other selectors are
                // retained as uncertain named paths until their activation is modeled.
                var selected = selectedTorchKeys.Contains(key);
                var collect = selected ? add : UnverifiedRuntimeRoot;
                var itemEffects = new List<TorchItemEffects>();
                possibleTorchStates.Add(itemEffects);
                if (selected) torchStates.Add(itemEffects);
                BuffList(data, "hero_buff_list", collect, 127);
                BuffList(data, "monster_buff_list", collect, 127);
                EffectList(data, "hero_effect_list", collect, combat: false, 127);
                EffectList(data, "monster_effect_list", collect, combat: false, 127);
                foreach (var entry in Objects(data, "torch_setting_item_buffs"))
                {
                    // 0x430DD2/0x4310E0 only apply the first supply/torch
                    // match; other item entries are not generic use Buffs.
                    if (NativeResourceIdentity.HashCString(Text(entry, "item_type", 127)) != NativeResourceIdentity.HashCString("supply") ||
                        NativeResourceIdentity.HashCString(Text(entry, "item_name", 127)) != NativeResourceIdentity.HashCString("torch")) continue;
                    if (!HasItem(entry, "item_type", "item_name", 127)) continue;
                    BuffList(entry, "hero_buff_list", collect, 127);
                    BuffList(entry, "monster_buff_list", collect, 127);
                    break;
                }
                foreach (var entry in Objects(data, "torch_setting_item_performer_effects"))
                {
                    if (!HasItem(entry, "item_type", "item_name", 127) || !IsUsable(Text(entry, "item_type", 127))) continue;
                    var normal = Strings(entry, "effect_list", 127).ToArray();
                    var sequences = new List<IReadOnlyList<string>>();
                    foreach (var id in normal) collect(new(id));
                    foreach (var sequence in Array(entry, "sequential_effect_list"))
                    {
                        if (sequence.ValueKind != JsonValueKind.Array) { Incomplete("Invalid torch effect sequence."); continue; }
                        var ids = new List<string>();
                        sequences.Add(ids);
                        foreach (var value in sequence.EnumerateArray())
                        {
                            if (value.ValueKind != JsonValueKind.String) { Incomplete("Invalid torch effect sequence element."); continue; }
                            var id = String(value, 127);
                            ids.Add(id);
                        }
                    }
                    itemEffects.Add(new((NativeResourceIdentity.HashCString(Text(entry, "item_type", 127)),
                        NativeResourceIdentity.HashCString(Text(entry, "item_name", 127))), normal, sequences));
                }
            }
            foreach (var item in useItems.Values.Where(item => IsUsable(item.Type)))
            {
                foreach (var includeUnverified in new[] { false, true })
                {
                    var states = ItemTorchStates(item, includeUnverified);
                    var collect = includeUnverified ? UnverifiedRuntimeRoot : add;
                    foreach (var count in ReachableUseCounts(item, states))
                        foreach (var entry in states.SelectMany(state => state))
                            if (entry.Sequence.Count > 0)
                                foreach (var id in entry.Sequence[Math.Min(count, entry.Sequence.Count - 1)]) collect(new(id));
                }
            }
            torchComplete = failures == prior;
        }

        private void ReadRules(Action<NativeEffectReferences.Reference> add)
        {
            // Omission preserves a field; an explicitly present array replaces it
            // (0x44F9F0). This is different from replacing a torch map value.
            var onset = new List<string>();
            var recovery = new List<string>();
            var stall = "";
            foreach (var document in Documents("shared", NativeResourceFileRules.IsSharedRuleFile))
            {
                if (NativeJsonReader.TryGetProperty(document, "stun_onset_buffs", out _)) onset = Strings(document, "stun_onset_buffs", 63).ToList();
                if (NativeJsonReader.TryGetProperty(document, "stun_recovery_buffs", out _)) recovery = Strings(document, "stun_recovery_buffs", 63).ToList();
                if (NativeJsonReader.TryGetProperty(document, "stall_stress_effect_name", out _)) stall = Text(document, "stall_stress_effect_name", 63);
            }
            foreach (var id in onset.Concat(recovery)) add(new(id, Buff: true));
            add(new(stall));
        }

        private void ReadQuestModifiers(Action<NativeEffectReferences.Reference> add)
        {
            // 0x465DB0 bounds IDs to 63 bytes; 0x616320 keeps the last hash.
            // Conditions are
            // potential quest states, not predicates on the currently open save.
            foreach (var entry in Library("campaign/quest", NativeResourceFileRules.IsQuestModifierFile, "modifiers", idBytes: 63).Values)
            {
                var condition = Text(entry, "condition_type", 63, required: true);
                if (condition is not ("always" or "wave_kill_count")) continue;
                BuffList(entry, "hero_buffs", add);
                BuffList(entry, "monster_buffs", add);
            }
        }

        private void ReadFlashbacks(TrinketHeroDependencies heroes, Action<NativeEffectReferences.Reference> add)
        {
            var prior = failures;
            var torchKeys = new HashSet<uint>();
            // Completed-camp Buffs visit every declaration (0x5CB390). Active
            // starting Effects/results instead look up the FIRST bounded ID
            // (0x5A4570/0x5CAD96), after candidate selection from the whole list.
            var entries = Documents("raid/flashback", NativeResourceFileRules.IsFlashbackFile)
                .SelectMany(document => Objects(document, "flashbacks", required: true)).ToArray();
            bool HasRequiredHero(JsonElement entry) => Strings(entry, "required_hero_class_ids").Any(id => heroes.Contains(id) != false);
            foreach (var entry in entries)
                if (HasRequiredHero(entry)) BuffList(entry, "completed_camping_start_buffs", add);
            foreach (var group in entries.GroupBy(entry => NativeResourceIdentity.HashCString(Text(entry, "id", 63, required: true))))
            {
                var entry = group.First();
                // 0x4BD1E0 requires ANY matching class, not every listed class.
                if (group.Key == 0 || !HasRequiredHero(entry) || !group.Any(candidate => HasRequiredHero(candidate) &&
                    Number(candidate, "chance") > 0 && Text(candidate, "start_type", 63) == "camp_finish")) continue;
                var required = Strings(entry, "required_hero_class_ids").ToArray();
                var torchKey = Text(entry, "torch_settings", 63);
                if (torchKey.Length > 0) torchKeys.Add(NativeResourceIdentity.HashCString(torchKey));
                EffectList(entry, "required_heroes_starting_effects", add, combat: false);
                foreach (var result in Objects(entry, "results"))
                {
                    if (Text(result, "type", 63) != "buff") continue;
                    var target = Text(result, "target_type", 63);
                    if (target is not ("hero" or "required_hero" or "not_required_hero")) continue;
                    if (target == "not_required_hero" && heroes.Ids.All(id => required.Any(name => NativeResourceIdentity.HashCString(name) == id))) continue;
                    add(new(Text(result, "string_data", 63), Buff: true));
                }
            }
            if (failures == prior && heroes.IsComplete) selectedTorchKeys.UnionWith(torchKeys);
        }
    }
}
