using System.Globalization;
using System.Text.Json.Nodes;

namespace DarkestDungeonSaveEditor.Core;

internal sealed partial class TownEventInitializer
{
    private readonly HashSet<uint> _generatedProgressionDungeons = [];
    private readonly HashSet<uint> _generatedRetentionDungeons = [];

    private void AddPlotQuests(IReadOnlyDictionary<string, JsonObject> saves, IReadOnlyList<TownEventEffect> effects, List<string> changes)
    {
        if (effects.Count == 0) return;
        var definitions = ReadResources("campaign/quest", NativeReferenceJsonKind.PlotQuests)
            .SelectMany(root => Objects(root["plot_quests"]))
            .GroupBy(item => Hash(Text(item, "id"))).Select(group => group.First()).ToArray();
        var requested = effects.Select(effect => Hash(effect.StringData)).ToHashSet();
        foreach (var effect in effects)
            if (!definitions.Any(item => Hash(Text(item, "id")) == Hash(effect.StringData)))
                throw new InvalidDataException($"Unresolved event quest: {effect.StringData}.");
        // The native quest callback walks definitions, not effect rows. A
        // duplicate effect must not create a second copy or change precedence.
        foreach (var definition in definitions.Where(item => requested.Contains(Hash(Text(item, "id")))))
            AddPlotQuest(saves, definition, changes);
    }

    private void AddPlotQuest(IReadOnlyDictionary<string, JsonObject> saves, JsonObject definition, List<string> changes)
    {
        var id = Text(definition, "id");
        var board = JsonSupport.RequireObject(Root(saves, "quest"), "quests");
        if (Objects(board).Any(quest => Hash(Text(quest, "id")) == Hash(id)))
        {
            changes.Add(EditorText.Format("TownEvent_QuestAlreadyPresent", id));
            return;
        }
        if (!PlotQuestEligible(saves, definition))
        {
            changes.Add(EditorText.Format("TownEvent_QuestNotEligible", id));
            return;
        }
        var template = definition["quest"] as JsonObject ?? throw new InvalidDataException($"Missing quest template: {id}.");
        var quest = new JsonObject
        {
            ["id"] = id,
            ["map_name"] = Text(template, "map_name"),
            ["torch_setting"] = Text(template, "torch_setting"),
            ["raid_rules_override"] = Text(template, "raid_rules_override"),
            ["is_plot_quest"] = Flag(template, "is_plot_quest", true),
            ["type"] = Text(template, "type"),
            ["dungeon"] = Text(template, "dungeon"),
            ["difficulty"] = TownEventSaveEditor.RequiredInt(template, "difficulty"),
            ["length"] = TownEventSaveEditor.RequiredInt(template, "length"),
            ["counted_in_generation"] = Flag(template, "counted_in_generation", true),
            ["goal_ids"] = StringArray(template["goal_ids"]),
            ["progression_goal_ids"] = StringArray(template["progression_goal_ids"]),
            ["use_default_progression_goals"] = Flag(definition, "is_town_progression_goals_enabled", true),
            ["completion_threshold"] = JsonSupport.ReadInt(template, "completion_threshold") ?? 0,
            // CreatePlotQuest (0x1405771C1) explicitly clears this flag even
            // for plots selected by the current event's generation predicate.
            ["is_from_town_event"] = false
        };
        var owned = CountOwnedTrinkets(saves);
        var reward = BuildQuestReward(template["completion_reward"] as JsonObject ?? new JsonObject(), owned);
        var items = JsonSupport.RequireObject(reward, "items_definition", "items");
        foreach (var additional in Objects(definition["additional_trinket_completion_rewards"]))
        {
            var rarity = Hash(Text(additional, "rarity"));
            var dungeon = Hash(Text(template, "dungeon"));
            var eligible = EventTrinkets.Values.Where(trinket => !trinket.Definition.HasProviderConflict &&
                (rarity == 0 || Hash(trinket.Definition.Rarity) == 0 || Hash(trinket.Definition.Rarity) == rarity) &&
                (dungeon == 0 || Hash(trinket.Dungeon) == 0 || Hash(trinket.Dungeon) == dungeon) &&
                !TrinketLimitReached(trinket.Definition, owned)).ToArray();
            if (eligible.Length == 0) continue;
            // The native loop at 0x140576E90 advances one 8-byte rarity/amount
            // record per draw, but creates exactly one item (0x1405770FF).
            // Do not interpret its unused amount field as an extra draw loop.
            var chosen = eligible[_random.Next(eligible.Length)].Definition;
            NativeInventoryIdentity.RequireWritable(chosen.SaveIdentityIssue);
            items[NextKey(items)] = TrinketSaveEditor.CreatePristineInstance(chosen);
        }
        var retentionCount = JsonSupport.ReadInt(definition, "trinket_retention_count") ?? 0;
        if (retentionCount < 0) throw new InvalidDataException("Negative quest trinket retention count.");
        if (retentionCount > 0)
        {
            var rewards = new JsonObject();
            foreach (var idHash in OrderedRetainedTrinkets(saves).Take(retentionCount))
            {
                var trinket = RequireEventTrinket(idHash);
                rewards[NextKey(rewards)] = TrinketSaveEditor.CreatePristineInstance(trinket);
            }
            // The top-level retention list remains unchanged until settlement.
            if (rewards.Count > 0) reward["trinket_retention_ids"] = new JsonObject { ["items"] = rewards };
        }
        quest["completion_reward"] = reward;
        var thresholds = new JsonObject();
        foreach (var pair in Indexed(template["threshold_rewards"]))
            thresholds[pair.Key] = BuildQuestReward(pair.Value.AsObject(), owned);
        quest["threshold_rewards"] = thresholds;
        var requestedModifiers = Strings(definition["potential_quest_modifiers"]).ToArray();
        if (requestedModifiers.Length > 0)
        {
            var modifiers = ReadMatchingResources("campaign/quest", "quest.modifiers.json")
                .SelectMany(root => Objects(root["modifiers"])).GroupBy(item => Hash(Text(item, "id")))
                .ToDictionary(group => group.Key, group => group.Last());
            var eligible = requestedModifiers.Where(value => modifiers.ContainsKey(Hash(value))).ToArray();
            if (eligible.Length > 0) quest["modifier"] = Text(modifiers[Hash(eligible[_random.Next(eligible.Length)])], "id");
        }
        board[NextKey(board)] = quest;
        var dungeonHash = Hash(Text(template, "dungeon"));
        if (Flag(definition, "is_progression", false)) _generatedProgressionDungeons.Add(dungeonHash);
        if (retentionCount > 0) _generatedRetentionDungeons.Add(dungeonHash);
        changes.Add(EditorText.Format("TownEvent_QuestCreated", id));
    }

    private bool PlotQuestEligible(IReadOnlyDictionary<string, JsonObject> saves, JsonObject definition)
    {
        if (!Flag(definition, "is_enabled", true)) return false;
        var dungeonHash = Hash(Text(definition["quest"]!.AsObject(), "dungeon"));
        if (Flag(definition, "is_progression", false) && _generatedProgressionDungeons.Contains(dungeonHash)) return false;
        var progression = Root(saves, "progression");
        var completed = Objects(progression["completed_plot_quests_data"])
            .Select(item => unchecked((uint)TownEventSaveEditor.RequiredInt(item, "plot_quest_id"))).ToHashSet();
        var id = Hash(Text(definition, "id"));
        if (completed.Contains(id) && (!Flag(definition, "is_repeatable", false) || Flag(definition, "is_progression", false)))
            return false;
        var dependency = Hash(Text(definition, "plot_quest_dependency"));
        if (dependency != 0 && !completed.Contains(dependency)) return false;
        var requiredLevel = JsonSupport.ReadInt(definition, "dungeon_level") ?? 0;
        if (requiredLevel > 0)
        {
            var dungeon = Text(definition["quest"]!.AsObject(), "dungeon");
            var dungeonState = (progression["dungeon"] as JsonObject)?.FirstOrDefault(pair => Hash(pair.Key) == Hash(dungeon)).Value as JsonObject;
            var xp = dungeonState is null ? 0 : JsonSupport.ReadInt(dungeonState, "xp") ?? 0;
            var thresholds = OpenResource("campaign/progression/progression.json")["dungeon"]?["level_threshold_table"] as JsonArray
                ?? throw new InvalidDataException("Missing dungeon level thresholds.");
            var level = thresholds.Count(value => value!.GetValue<int>() <= xp) - 1;
            if (level < requiredLevel) return false;
        }
        var requiredHeroLevel = JsonSupport.ReadInt(definition, "highest_hero_resolve_level_dependency") ?? 0;
        if (requiredHeroLevel > 0)
        {
            // Native quest generation (0x14057661C) converts the saved roster
            // high-water XP to a level. Recomputing from living heroes can
            // incorrectly revoke a dependency after a death or dismissal.
            var highestXp = TownEventSaveEditor.RequiredInt(Root(saves, "roster"), "highest_resolve_xp");
            var highestLevel = Math.Max(0, Heroes.ResolveLevelThresholds.Count(threshold => threshold <= highestXp) - 1);
            if (highestLevel < requiredHeroLevel) return false;
        }
        var retained = Root(saves, "quest")["trinket_retention_ids"] as JsonArray;
        var requiredRetained = JsonSupport.ReadInt(definition, "trinket_retention_count") ?? 0;
        if (requiredRetained > (retained?.Count ?? 0)) return false;
        if (requiredRetained > 0)
        {
            if (_generatedRetentionDungeons.Contains(dungeonHash)) return false;
            var threshold = RarityRank(Text(definition, "trinket_retention_minimum_rarity"));
            // Lower rarity indices are more valuable. The native predicate
            // requires at least one retained trinket at the minimum rarity.
            if (!OrderedRetainedTrinkets(saves).Any(value => RarityRank(RequireEventTrinket(value).Rarity) <= threshold)) return false;
        }
        var infestationDependency = Hash(Text(definition, "infestation_sequence_element_dependency"));
        if (infestationDependency != 0)
        {
            var sequence = Objects(OpenResource("campaign/progression/infestation.progression.json")["infestation"]?["sequence_elements"])
                .Select(item => Hash(Text(item, "id"))).ToArray();
            var current = unchecked((uint)(JsonSupport.ReadInt(progression["infestation"] as JsonObject ?? new JsonObject(), "sequence_element_id") ?? 0));
            var dependencyIndex = Array.LastIndexOf(sequence, infestationDependency);
            if (dependencyIndex < 0 || Array.LastIndexOf(sequence, current) <= dependencyIndex) return false;
        }
        if (definition["estate_inventory_dependency"] is JsonObject inventory && !Flag(inventory, "consume_on_attempt", false))
        {
            var required = InventoryAmounts(inventory["items_definition"]?["items"]);
            var available = InventoryAmounts(Root(saves, "estate")["estate_items"]?["items"]);
            if (required.Any(pair => available.GetValueOrDefault(pair.Key) < pair.Value)) return false;
        }
        return true;
    }

    private static Dictionary<(uint Type, uint Id), long> InventoryAmounts(JsonNode? items) => Objects(items)
        .Where(item => JsonSupport.ReadInt(item, "amount") > 0)
        .GroupBy(item => (NativeInventoryIdentity.HashSavedField(Text(item, "type")), NativeInventoryIdentity.HashSavedField(Text(item, "id"))))
        .ToDictionary(group => group.Key, group => group.Sum(item => (long)TownEventSaveEditor.RequiredInt(item, "amount")));

    private JsonObject BuildQuestReward(JsonObject template, IReadOnlyDictionary<uint, int> owned)
    {
        var items = new JsonObject();
        foreach (var item in Objects(template["items_definition"]?["items"]))
        {
            var id = Text(item, "id");
            var type = Text(item, "type");
            var amount = TownEventSaveEditor.RequiredInt(item, "amount");
            if (amount <= 0) continue;
            JsonObject saved;
            if (Hash(type) == Hash("trinket"))
            {
                var trinket = RequireEventTrinket(Hash(id));
                if (TrinketLimitReached(trinket, owned)) continue;
                saved = TrinketSaveEditor.CreatePristineInstance(trinket);
            }
            else
            {
                NativeInventoryIdentity.RequireWritable(NativeInventoryIdentity.GetSaveIssue(type, id));
                saved = new JsonObject { ["id"] = id, ["type"] = type, ["added_buffs"] = 0,
                    ["hero_name"] = "", ["previous_trinket_id"] = "", ["did_transform"] = false, ["trinkets_gained_count"] = 0 };
            }
            saved["amount"] = amount;
            items[NextKey(items)] = saved;
        }
        var reward = new JsonObject
        {
            ["resolve_xp"] = JsonSupport.ReadInt(template, "resolve_xp") ?? 0,
            ["resolve_xp_per_wave_kill"] = TownEventSaveEditor.Float(template["resolve_xp_per_wave_kill"]?.GetValue<double>() ?? 0),
            ["items_definition"] = new JsonObject { ["items"] = items },
            ["max_times_dungeon_xp_awarded"] = JsonSupport.ReadInt(template, "max_times_dungeon_xp_awarded") ?? 0
        };
        var additional = new JsonObject();
        foreach (var pair in Indexed(template["additional_threshold_trinket_rewards"]))
        {
            var row = pair.Value.AsObject();
            var rarities = new JsonObject();
            foreach (var rarity in Indexed(row["rarity_table"]))
                rarities[rarity.Key] = new JsonObject { ["chance"] = TownEventSaveEditor.Float(rarity.Value["chance"]!.GetValue<double>()),
                    ["rarity"] = Text(rarity.Value.AsObject(), "rarity") };
            additional[pair.Key] = new JsonObject { ["chance_of_loot"] = TownEventSaveEditor.Float(row["chance_of_loot"]!.GetValue<double>()),
                ["rarity_table"] = rarities };
        }
        reward["additional_threshold_trinket_rewards"] = additional;
        if (template["other_dungeon_xp"] is JsonObject xp && xp.Count > 0) reward["other_dungeon_xp"] = xp.DeepClone();
        return reward;
    }

    private static bool Flag(JsonObject node, string key, bool fallback) => node[key]?.GetValue<bool>() ?? fallback;
    private static string Text(JsonObject node, string key) => NativeJsonReader.CString(JsonSupport.ReadRawString(node, key));
    private static IEnumerable<string> Strings(JsonNode? node) => node is JsonArray array
        ? array.Select(value => NativeJsonReader.CString(value!.GetValue<string>())) : [];
    private static JsonArray StringArray(JsonNode? node) => new(Strings(node).Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());
    private static IEnumerable<KeyValuePair<string, JsonNode>> Indexed(JsonNode? node) => node switch
    {
        JsonArray array => array.Select((value, index) => new KeyValuePair<string, JsonNode>(index.ToString(CultureInfo.InvariantCulture), value!)),
        JsonObject map => map.Select(pair => new KeyValuePair<string, JsonNode>(pair.Key, pair.Value!)),
        _ => []
    };
}
