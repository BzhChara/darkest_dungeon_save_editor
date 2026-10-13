// Format knowledge and field paths derived from robojumper/DarkestDungeonSaveEditor
// v0.0.70. Copyright (c) 2018 robojumper. See LICENSE in this directory (MIT).
namespace DarkestDungeonSaveEditor.Core.Dson;

internal enum DsonKind
{
    Object, Boolean, Character, Integer, Float, String, Embedded,
    Integers, Strings, Floats, TwoIntegers, TwoBooleans, Opaque
}

internal static class DsonTypes
{
    private static readonly (DsonKind Kind, string[][] Paths)[] Rules =
    [
        (DsonKind.Character, [["requirement_code"]]),
        (DsonKind.Float, [["current_hp"], ["m_Stress"], ["actor", "buff_group", "*", "amount"],
            ["chapters", "*", "*", "percent"], ["non_rolled_additional_chances", "*", "chance"]]),
        (DsonKind.Integers, [["read_page_indexes"], ["raid_read_page_indexes"], ["raid_unread_page_indexes"],
            ["dungeons_unlocked"], ["played_video_list"], ["trinket_retention_ids"], ["last_party_guids"],
            ["dungeon_history"], ["buff_group_guids"], ["result_event_history"], ["dead_hero_entries"],
            ["additional_mash_disabled_infestation_monster_class_ids"], ["mash", "valid_additional_mash_entry_indexes"],
            ["party", "heroes"], ["skill_cooldown_keys"], ["skill_cooldown_values"], ["bufferedSpawningSlotsAvailable"],
            ["curioGroups", "*", "curios"], ["curioGroups", "*", "curio_table_entries"],
            ["raid_finish_quirk_monster_class_ids"], ["narration_audio_event_queue_tags"], ["dispatched_events"],
            ["backer_heroes", "*", "combat_skills"], ["backer_heroes", "*", "camping_skills"],
            ["backer_heroes", "*", "quirks"]]),
        (DsonKind.Strings, [["goal_ids"], ["roaming_dungeon_2_ids", "*", "s"], ["quirk_group"],
            ["backgroundNames"], ["backgroundGroups", "*", "backgrounds"],
            ["backgroundGroups", "*", "background_table_entries"]]),
        (DsonKind.Floats, [["map", "bounds"], ["areas", "*", "bounds"],
            ["areas", "*", "tiles", "*", "mappos"], ["areas", "*", "tiles", "*", "sidepos"]]),
        (DsonKind.TwoIntegers, [["killRange"]])
    ];

    internal static DsonKind? KnownKind(IReadOnlyList<string> path)
    {
        foreach (var (kind, patterns) in Rules)
        foreach (var pattern in patterns)
        {
            if (pattern.Length > path.Count) continue;
            var match = true;
            for (var i = 0; i < pattern.Length; i++)
                if (pattern[i] != "*" && pattern[i] != path[path.Count - pattern.Length + i])
                { match = false; break; }
            if (match) return kind;
        }
        return null;
    }

    internal static int Hash(ReadOnlySpan<byte> name)
    {
        var hash = 0;
        foreach (var value in name) hash = unchecked(hash * 53 + value);
        return hash;
    }

    internal static int Padding(int position) => (-position) & 3;
}
