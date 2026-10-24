using System.Globalization;
using System.Text.Json.Nodes;

namespace DarkestDungeonSaveEditor.Core;

internal sealed partial class TownEventInitializer
{
    private List<(string Key, JsonObject Hero)>? _revivalCandidates;

    private void AddRevivalOffers(IReadOnlyDictionary<string, JsonObject> saves, TownEventEffect effect, List<string> changes)
    {
        // ApplyStart shares one draw pool across dead_recruit rows. It refills
        // only when a later row starts with an exhausted pool (0x14058E5B0).
        if (_revivalCandidates is not { Count: > 0 })
        {
            var roster = JsonSupport.RequireObject(Root(saves, "roster"), "heroes");
            _revivalCandidates = roster.Select(pair => (Key: pair.Key,
                    Hero: pair.Value?["hero_file_data"]?["raw_data"]?["base_root"] as JsonObject))
                .Where(pair => pair.Hero is not null && JsonSupport.ReadInt(pair.Hero, "roster.status") == 3)
                .Select(pair => (pair.Key, pair.Hero!)).ToList();
        }
        var dead = _revivalCandidates;
        var offers = (JsonArray)Root(saves, "town_event")["dead_hero_entries"]!;
        var count = Math.Min(Count(effect.NumberData), dead.Count);
        var resetQuirks = QuirkIdsWithTag("vampire");
        resetQuirks.UnionWith(QuirkIdsWithTag("singleton"));
        for (var index = 0; index < count; index++)
        {
            var choice = _random.Next(dead.Count);
            var (key, value) = dead[choice];
            dead.RemoveAt(choice);
            var hero = value!;
            var hp = RevivalHitPoints(hero);
            var actor = JsonSupport.RequireObject(hero, "actor");
            hero["is_from_town_event"] = true;
            actor["current_hp"] = TownEventSaveEditor.Float(hp);
            hero["is_death_heart_attack_completed"] = false;
            hero["m_Stress"] = TownEventSaveEditor.Float(0);
            hero["affliction_type_id"] = string.Empty;
            hero["affliction_severity"] = 0;
            hero["virtue_type_id"] = string.Empty;
            // Native revival clears every persisted buff source (source-info
            // flag at 0x142AA62E4), not just affliction/virtue sources 3 and 4.
            actor["buff_group"] = new JsonObject();
            hero["weapon_rank"] = 0;
            hero["armour_rank"] = 0;
            RemoveQuirks(hero, resetQuirks);
            offers.Add(int.Parse(key, CultureInfo.InvariantCulture));
            // Keep roster.status = dead. Selecting an offer in game performs
            // recruitment; replacing an event never recruits a hero itself.
        }
        changes.Add(EditorText.Format("TownEvent_RevivalOffers", count));
    }

    private double RevivalHitPoints(JsonObject hero)
    {
        var id = JsonSupport.ReadRawString(hero, "heroClass");
        var definition = Heroes.HeroClasses.FirstOrDefault(item => Hash(item.Id) == Hash(id))
            ?? throw new InvalidDataException(EditorText.Format("TownEvent_MissingHero", id));
        if (definition.HasProviderConflict) throw new InvalidDataException(EditorText.Get("TownEvent_Unresolved"));
        var rank = TownEventSaveEditor.RequiredInt(hero, "armour_rank");
        var baseHp = definition.Equipment?.Armour.SingleOrDefault(item => item.Rank == rank)?.Hp
            ?? definition.LevelProfiles.FirstOrDefault(item => item.ArmourRank == rank)?.ArmourHp
            ?? throw new InvalidDataException($"Cannot resolve revival armour HP for {id} / {rank}.");
        if (Objects(hero["trinkets"]?["items"]).Any())
            throw new InvalidDataException(EditorText.Get("TownEvent_RevivalTrinkets"));
        var afflicted = JsonSupport.ReadRawString(hero, "affliction_type_id").Length > 0;
        float flat = 0, multiplier = 0;
        foreach (var quirk in JsonSupport.RequireObject(hero, "quirks"))
        {
            var rule = Heroes.InitialQuirks.FirstOrDefault(item => Hash(item.Id) == Hash(quirk.Key))
                ?? throw new InvalidDataException($"Unresolved revival quirk: {quirk.Key}.");
            if (rule.WriteStatus is HeroInitialQuirkWriteStatus.Unverified or HeroInitialQuirkWriteStatus.Unsupported)
                throw new InvalidDataException($"Unresolved revival quirk: {quirk.Key}. {rule.WriteStatusReason}");
            foreach (var buff in rule.MaxHpModifiers)
            {
                bool? active = buff.RuleType switch { "always" => true, "no_trinkets" => true,
                    "afflicted" => afflicted, "in_mode" or "lightabove" => null,
                    _ => throw new InvalidDataException($"Unresolved revival HP rule: {buff.RuleType}.") };
                if (active is null || (buff.IsFalseRule ? !active.Value : active.Value) == false) continue;
                if (buff.Kind == HeroMaxHpModifierKind.Flat) flat += (float)buff.Amount; else multiplier += (float)buff.Amount;
            }
        }
        foreach (var buff in Objects(hero["actor"]?["buff_group"]))
        {
            if (Hash(JsonSupport.ReadRawString(buff, "stat_sub_type")) != Hash("max_hp")) continue;
            if (JsonSupport.ReadInt(buff, "rule") != 0 || buff["is_false_rule"]?.GetValue<bool>() == true)
                throw new InvalidDataException(EditorText.Get("TownEvent_RevivalConditionalHp"));
            var amount = buff["amount"]?.GetValue<double>() ?? throw new InvalidDataException("Missing revival HP buff amount.");
            switch (JsonSupport.ReadInt(buff, "stat_type"))
            {
                case 2: flat += (float)amount; break;
                case 3: multiplier += (float)amount; break;
                default: throw new InvalidDataException("Unknown revival HP stat type.");
            }
        }
        var additive = (float)baseHp + flat;
        var hp = additive * multiplier + additive;
        if (float.IsNaN(hp) || !float.IsFinite(flat) || !float.IsFinite(multiplier))
            throw new InvalidDataException("Invalid revival HP.");
        // Actor's max_hp stat bounds are 0..FLT_MAX (0x140474617);
        // its clamped stat query is used before revival clears old buffs.
        return Math.Clamp(hp, 0, float.MaxValue);
    }

    private void RemoveVampire(IReadOnlyDictionary<string, JsonObject> saves, List<string> changes)
    {
        var curseQuirks = QuirkIdsWithTag("vampire");
        var count = RosterHeroes(saves).Count(hero => RemoveQuirks(hero, curseQuirks));
        changes.Add(EditorText.Format("TownEvent_CuredVampires", count));
    }

    private HashSet<uint> QuirkIdsWithTag(string tag)
    {
        // Native removal (0x1405C6B00 / predicate 0x1405CF5F0) matches
        // definition tags, not IDs such as disease_vampire_passive.
        var tagHash = Hash(tag);
        return Heroes.InitialQuirks.Where(quirk => quirk.Tags.Any(value => Hash(value) == tagHash))
            .Select(quirk => Hash(quirk.Id)).ToHashSet();
    }

    private static bool RemoveQuirks(JsonObject hero, IReadOnlySet<uint> ids)
    {
        var quirks = JsonSupport.RequireObject(hero, "quirks");
        var keys = quirks.Select(pair => pair.Key).Where(key => ids.Contains(Hash(key))).ToArray();
        foreach (var key in keys) quirks.Remove(key);
        return keys.Length > 0;
    }

    private static JsonObject Root(IReadOnlyDictionary<string, JsonObject> saves, string name) => TownEventSaveEditor.Root(saves, name);
}
