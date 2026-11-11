using System.Text.Json;

namespace DarkestDungeonSaveEditor.Core;

internal static partial class QuantityItemReferenceAnalyzer
{
    private sealed partial class LootBuffSources
    {
        private static readonly HashSet<string> TurnEffectActOuts = new(StringComparer.Ordinal)
        {
            "attack_friendly", "attack_self", "mark_self", "stress_heal_self", "stress_heal_party",
            "buff_random_party_member", "buff_party"
        };
        private static readonly HashSet<string> CombatReactions = new(StringComparer.Ordinal)
        {
            "block_move", "block_heal", "block_buff", "block_item", "block_combat_retreat",
            "comment_self_hit", "comment_self_missed", "comment_ally_hit", "comment_ally_missed",
            "comment_ally_attack_hit", "comment_ally_attack_missed", "comment_move", "block_effect"
        };
        private static readonly HashSet<string> ExplorationReactions = new(StringComparer.Ordinal)
        {
            "comment_curio_interaction", "comment_trap_triggered", "block_camping_meal",
            "block_camping_skill_performer", "block_camping_skill_target"
        };

        private void ReadActOuts(JsonElement owner, TrinketHeroDependencies heroes, Action<NativeEffectReferences.Reference> add)
        {
            if (!heroes.IsComplete) Incomplete("Act-out hero restrictions have an incomplete class library.");
            foreach (var entry in Objects(owner, "combat_start_turn_act_outs"))
            {
                if (Number(entry, "chance") <= 0) continue;
                var allowed = Strings(entry, "valid_hero_class_ids").ToArray();
                if (allowed.Length > 0 && !allowed.Any(id => heroes.Contains(id) != false)) continue;
                if (!TurnEffectActOuts.Contains(Text(entry, "id", 63))) continue;
                add(new(Text(Object(entry, "data", required: true), "string_value", 63)));
            }
            foreach (var entry in Objects(owner, "reaction_act_outs"))
            {
                if (Number(entry, "chance") <= 0) continue;
                var id = Text(entry, "id", 63);
                var combat = CombatReactions.Contains(id);
                if (!combat && !ExplorationReactions.Contains(id)) continue;
                add(new(Text(Object(entry, "data", required: true), "effect", 63), Combat: combat));
            }
        }

        private void ReadTraitActOuts(TrinketHeroDependencies heroes, Action<NativeEffectReferences.Reference> add)
        {
            foreach (var trait in Library("shared/trait", NativeResourceFileRules.IsTraitFile, "traits").Values) ReadActOuts(trait, heroes, add);
        }

        private void ReadQuirkActOuts(IReadOnlySet<uint> obtainableQuirkActOuts, TrinketHeroDependencies heroes,
            Action<NativeEffectReferences.Reference> add)
        {
            // 0x4EB901 binds the FIRST matching object and appends its lists.
            // Hero::GetActOuts (0x5D7F10) uses the owned definition pointer.
            // A shadowed, nonrandom first object is never that owned instance.
            foreach (var document in Documents("shared/quirk", NativeResourceFileRules.IsQuirkActOutFile))
                foreach (var entry in Objects(document, "quirk_act_outs", required: true))
                    if (obtainableQuirkActOuts.Contains(NativeResourceIdentity.HashCString(Text(entry, "quirk_id", required: true)))) ReadActOuts(entry, heroes, add);
        }
    }
}
