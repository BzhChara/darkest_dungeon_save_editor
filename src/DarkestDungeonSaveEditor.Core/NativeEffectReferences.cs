using System.Text.Json;

namespace DarkestDungeonSaveEditor.Core;

// Shared by loot reachability and descriptive hero quirk provenance. These are
// possible runtime paths, not effects to execute when generating a hero.
internal static class NativeEffectReferences
{
    internal readonly record struct Reference(string Id, bool Buff = false, bool Combat = true);

    private static readonly string[] BuffCombatTriggers =
    [
        "attack", "friendly", "move_skill", "attack_crit", "melee_attack", "ranged_attack",
        "riposte", "riposte_crit", "on_dodge", "was_hit", "was_hit_all_heroes", "was_hit_all_monsters",
        "kill_performer", "kill_all_heroes", "kill_all_monsters", "riposte_kill", "was_killed",
        "was_killed_all_heroes", "was_killed_all_monsters", "combat_start", "turn_start", "turn_end",
        "round_end", "battle_finished_successfully"
    ];

    // Trinket enum 0xA7BD0 and loader 0x4FF060 use their own names.
    // Five older triggers include _skill here but not in the Buff library.
    private static readonly string[] TrinketCombatTriggers =
    [
        "friendly_skill", "attack_skill", "melee_attack_skill", "ranged_attack_skill", "attack_crit",
        "riposte_skill", "riposte_kill", "riposte_crit", "kill_performer", "kill_all_heroes",
        "kill_all_monsters", "was_killed_all_heroes", "was_killed_all_monsters", "was_hit",
        "was_hit_all_heroes", "was_hit_all_monsters", "battle_finished_successfully", "turn_end",
        "on_dodge", "was_killed", "round_end", "turn_start", "move_skill", "combat_start"
    ];

    internal static IEnumerable<Reference> BuffTriggers(JsonElement node) =>
        Triggers(node, BuffCombatTriggers, "_additional_effect_ids");

    internal static IEnumerable<Reference> TrinketTriggers(JsonElement node) =>
        Triggers(node, TrinketCombatTriggers, "_additional_effects");

    private static IEnumerable<Reference> Triggers(JsonElement node, IEnumerable<string> combatTriggers, string suffix)
    {
        // Quest-start can install a Buff for later combat. Quest-completion
        // carryover across expeditions is outside this graph's lifetime model.
        foreach (var trigger in combatTriggers.Append("quest_start"))
            foreach (var value in NativeJsonReader.Array(node, trigger + suffix))
                if (value.ValueKind == JsonValueKind.String)
                    yield return new(NativeJsonReader.CString(value.GetString()!), Combat: trigger != "quest_start");
    }

    internal static IEnumerable<Reference> Camping(JsonElement skill)
    {
        foreach (var entry in NativeJsonReader.Array(skill, "effects"))
        {
            var type = NativeResourceIdentity.HashCString(NativeJsonReader.ReadCString(entry, "type"));
            var id = NativeJsonReader.ReadCString(entry, "sub_type");
            if (type == NativeResourceIdentity.HashCString("buff")) yield return new(id, Buff: true);
            if (type == NativeResourceIdentity.HashCString("effect")) yield return new(id, Combat: false);
        }
    }

    internal static IEnumerable<string> List(string body, string field, int count, int bytes = 63)
    {
        foreach (var slot in NativeDarkestReader.ReadRawStringSlots(body, field, count))
        {
            var id = NativeJsonReader.BoundedCString(slot, bytes);
            // Both native actor helpers stop, rather than skip, at these slots.
            if (id.Length == 0 || id[0] == '.') yield break;
            yield return id;
        }
    }

    internal static IEnumerable<Reference> Actor(string kind, string body, bool hero)
    {
        if (kind == (hero ? "combat_skill" : "skill") || kind == "riposte_skill")
        {
            if (string.IsNullOrEmpty(NativeDarkestReader.ReadString(body, ".id"))) yield break;
            foreach (var id in List(body, ".effect", 16)) yield return new(id);
            foreach (var mode in NativeDarkestReader.ReadStringList(body, ".valid_modes", 8, skipEmptyOrFieldTokens: true))
                foreach (var id in List(body, $".{mode}_effects", 12)) yield return new(id);
        }
        // Shared ActorClass loader, build 28385, RVA 0x48B0D0.
        if (kind is "crit" or "hp_reaction" or "death_reaction")
            foreach (var id in List(body, ".effects", kind == "crit" ? 8 : 16)) yield return new(id);
        if (kind == "mode" && NativeDarkestReader.ReadString(body, ".id") is not null)
            foreach (var id in List(body, ".battle_start_effect_ids", 8)) yield return new(id);
        // 0x48CEB0 appends up to 16 tokens per declaration. Runtime hero
        // consumers: 0x482B90 (Effects), 0x483120/0x483230 (Buffs).
        if (hero && kind == "deaths_door")
        {
            foreach (var id in List(body, ".enter_effects", 16)) yield return new(id);
            foreach (var field in new[] { ".buffs", ".recovery_buffs", ".recovery_heart_attack_buffs" })
                foreach (var id in List(body, field, 16)) yield return new(id, Buff: true);
        }
        // 0x5A95E0 applies these to the last living non-corpse actor, once.
        // BattleController calls it for both sides (0x5AEF23/0x5AEF5A).
        if (kind == "last_hero")
            foreach (var id in List(body, ".buffs", 16)) yield return new(id, Buff: true);
        if (hero) yield break;

        // Monster-only helper 0x4DB830 reads FOUR 32-byte tokens, unlike the
        // shared actor helper's 64-byte tokens. Buff helper 0x4DBBD0 reads eight.
        string[] effectFields = kind switch
        {
            "captor_empty" => ["capture_effects"],
            "captor_full" => ["release_effects"],
            "death_class" => ["change_class_effects"],
            "controller" => ["uncontrol_effects"],
            "spawn" => ["effects", "wave_effects"],
            "skill_reaction" => ["was_hit_performer_effects", "was_hit_target_effects",
                "was_killed_other_monsters_effects", "was_killed_by_hero_effects",
                "was_killed_all_heroes_effects", "was_killed_effects"],
            _ => []
        };
        foreach (var field in effectFields)
            foreach (var id in List(body, "." + field, 4, 31)) yield return new(id);
        string[] buffFields = kind switch
        {
            "companion" => ["buffs"],
            "battle_modifier" => ["living_other_enemy_buffs", "living_hero_buff_instance_ids"],
            _ => []
        };
        foreach (var field in buffFields)
            foreach (var id in List(body, "." + field, 8)) yield return new(id, Buff: true);
    }

    // The loaders are verified, but their runtime activation/lifetime is not
    // covered by this graph. Preserve uncertainty only for loot reachable
    // through these named links; arbitrary .buffs text is not a consumer.
    internal static IEnumerable<Reference> UnverifiedActor(string kind, string body, bool hero)
    {
        if (!hero && kind == "deaths_door")
            foreach (var reference in Actor(kind, body, hero: true)) yield return reference;
    }

    internal sealed class Links
    {
        public List<string> Buffs { get; } = [];
        public int? Riposte { get; private set; }
        public string? RiposteEffect { get; private set; }

        public static Links Read(string body)
        {
            var links = new Links
            {
                // The trailing space avoids matching .riposte_effect. The native
                // loader uses this exact key at 0x4F0F15.
                Riposte = ReadRiposte(body),
                RiposteEffect = NativeDarkestReader.ReadString(body, ".riposte_effect") is { } id
                    ? NativeJsonReader.BoundedCString(id, 63) : null
            };
            links.Buffs.AddRange(List(body, ".buff_ids", 8));
            return links;
        }

        private static int? ReadRiposte(string body)
        {
            var start = NativeDarkestReader.FindValue(body, ".riposte ");
            if (start < 0) return null;
            // GetInt (0x36ED50) calls UCRT atoi: explicit overflow saturates,
            // it does not mean omission. Only the sign controls this edge.
            // Keep other consumers' conservative out-of-range policy intact.
            return NativeDarkestReader.ReadIntPrefix(body.AsSpan(start)) ??
                (body[start] == '-' ? int.MinValue : int.MaxValue);
        }

        public void Append(Links update)
        {
            Buffs.AddRange(update.Buffs);
            Riposte = update.Riposte ?? Riposte;
            RiposteEffect = update.RiposteEffect ?? RiposteEffect;
        }

        public IEnumerable<Reference> Targets()
        {
            foreach (var id in Buffs) yield return new(id, Buff: true);
            // ApplyEffect only installs the riposte effect when riposte > 0
            // (0x4C7187); omission retains fields, explicit zero/empty disables.
            if (Riposte > 0 && !string.IsNullOrEmpty(RiposteEffect)) yield return new(RiposteEffect);
        }
    }
}
