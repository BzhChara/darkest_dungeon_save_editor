using System.Globalization;
using System.Text.Json.Nodes;

namespace DarkestDungeonSaveEditor.Core;

public static partial class StagecoachHeroCandidateFactory
{
    private static JsonObject BuildCandidate(
        string heroClass,
        string name,
        int resolveXp,
        int weaponRank,
        int armourRank,
        double currentHp,
        int colourVariation,
        IEnumerable<InitialQuirkPersistenceState> quirks,
        IEnumerable<string> combatSkills,
        IEnumerable<string> campingSkills)
    {
        var quirkMap = new JsonObject();
        foreach (var quirk in quirks)
        {
            quirkMap[quirk.Definition.Id] = new JsonObject
            {
                ["is_new"] = true,
                ["is_locked"] = false,
                ["mission_count"] = 0,
                ["replaces_quirk"] = 0,
                ["replaces_quirk_viewed"] = false,
                ["evolution_duration_remaining"] = quirk.EvolutionDurationRemaining
            };
        }

        return new JsonObject
        {
            ["rescued"] = false,
            ["actor"] = new JsonObject
            {
                ["name"] = name,
                ["current_hp"] = CreateFloat(currentHp),
                ["stunned"] = 0,
                ["combat_ready"] = false,
                ["damage_source_data"] = 0,
                ["damage_source_type"] = 0,
                ["damage_type"] = 0,
                ["colour_variation"] = colourVariation,
                ["enemy_rank_targets"] = 0,
                ["friendly_rank_targets"] = 0,
                ["performing_turn"] = 0,
                ["controlling_actor_guid"] = 0,
                ["controlling_duration"] = 0,
                ["current_mode_id"] = 0,
                ["rounds_in_ranks"] = 0,
                ["check_round_ranks"] = 0,
                ["health_damage_blocks"] = 0,
                ["buff_group_next_guid"] = 2,
                ["buff_group"] = new JsonObject(),
                ["actor_dot"] = new JsonObject()
            },
            ["heroClass"] = heroClass,
            ["resolveXp"] = resolveXp,
            ["m_Stress"] = CreateFloat(0),
            ["is_death_heart_attack_completed"] = false,
            ["visited_deaths_door"] = false,
            ["deaths_door_enter_effect_round_cooldown"] = 0,
            ["has_had_heart_attack"] = false,
            ["backer_hero"] = false,
            ["steps_taken"] = 0,
            ["enemies_killed"] = 0,
            ["weapon_rank"] = weaponRank,
            ["armour_rank"] = armourRank,
            ["dd_test_survived"] = 0,
            ["affliction_type_id"] = string.Empty,
            ["affliction_severity"] = 0,
            ["virtue_type_id"] = string.Empty,
            ["provisions_consumed"] = 0,
            ["quirks"] = quirkMap,
            ["skills"] = new JsonObject
            {
                ["selected_combat_skills"] = CreateZeroMap(combatSkills),
                ["selected_camping_skills"] = CreateZeroMap(campingSkills)
            },
            ["trinkets"] = new JsonObject { ["items"] = new JsonObject() },
            ["has_item_Tracking"] = true,
            ["item_tracking"] = new JsonObject { ["supply"] = new JsonObject() },
            ["number_of_successful_darkest_dungeon_quests"] = 0,
            ["is_from_town_event"] = false
        };
    }

    private static JsonObject CreateZeroMap(IEnumerable<string> ids)
    {
        var result = new JsonObject();
        foreach (var id in ids)
        {
            result[id] = 0;
        }

        return result;
    }

    private static JsonNode CreateFloat(double value)
    {
        return JsonNode.Parse(value.ToString("0.0###############", CultureInfo.InvariantCulture))!;
    }

    private static IReadOnlyList<string> TakeRandom(
        IReadOnlyList<string> source,
        int count,
        Random random)
    {
        var values = source.ToList();
        Shuffle(values, random);
        return values.Take(count).ToArray();
    }

    private static void Shuffle<T>(IList<T> values, Random random)
    {
        for (var index = values.Count - 1; index > 0; index--)
        {
            var replacement = random.Next(index + 1);
            (values[index], values[replacement]) = (values[replacement], values[index]);
        }
    }

    private sealed record InitialQuirkPersistenceState(
        HeroInitialQuirkDefinition Definition,
        int EvolutionDurationRemaining);
}
