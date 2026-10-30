using System.Globalization;
using System.Text.Json.Nodes;

namespace DarkestDungeonSaveEditor.Core;

// The board and events use the same quest constructor and inventory-instance rules.
// Only the board's explicit manual operation bypasses natural eligibility.
internal sealed partial class TownEventInitializer
{
    private sealed record BoardTemplate(string Key, JsonObject Definition, bool Generated);
    private JsonObject[]? _boardGeneration;
    private JsonObject[] BoardGeneration => _boardGeneration ??= ReadResources("campaign/quest", NativeReferenceJsonKind.QuestGeneration)
        .Select(root => root["generation"] as JsonObject).OfType<JsonObject>().ToArray();
    private JsonObject[]? _boardTypes;
    private JsonObject[] BoardTypes => _boardTypes ??= ReadResources("campaign/quest", NativeReferenceJsonKind.QuestTypes).ToArray();
    private BoardTemplate[]? _boardTemplates;

    internal QuestBoardEditCatalog ReadQuestBoardCatalog()
    {
        var templates = BoardTemplates();
        var board = new JsonObject();
        var errors = new Dictionary<string, string>();
        foreach (var entry in templates)
        {
            var template = entry.Definition["quest"]!.AsObject();
            try { ValidateBoardTemplate(entry.Definition); }
            catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException or ArgumentException)
            { errors[entry.Key] = error.Message; }
            var display = (JsonObject)template.DeepClone();
            display["id"] = Text(entry.Definition, "id");
            display["is_plot_quest"] = !entry.Generated;
            // Invalid definitions stay inspectable, but these display fallbacks
            // are never used by the initializer or written to a save.
            display["type"] = Text(template, "type");
            display["dungeon"] = Text(template, "dungeon");
            display["difficulty"] = JsonSupport.ReadInt(template, "difficulty") ?? 0;
            display["length"] = JsonSupport.ReadInt(template, "length") ?? 0;
            display["goal_ids"] = StringArray(template["goal_ids"]);
            // Rewards are rolled only in a guarded preview, never while browsing.
            display["completion_reward"] = new JsonObject();
            board[entry.Key] = display;
        }
        var snapshot = QuestBoardSnapshotReader.Read(content, new JsonObject { ["base_root"] = new JsonObject { ["quests"] = board } }, "");
        return new(snapshot.Quests.Select(quest => new QuestBoardTemplate(quest.SaveKey, quest,
            errors.GetValueOrDefault(quest.SaveKey, ""))).ToArray(), snapshot.ImagePaths, snapshot.Issues)
        { Regions = snapshot.Regions };
    }

    internal JsonObject CreateBoardQuest(IReadOnlyDictionary<string, JsonObject> saves, string key)
    {
        var entry = BoardTemplates().SingleOrDefault(item => item.Key == key)
            ?? throw new InvalidOperationException(EditorText.Get("QuestBoard_DefinitionChanged"));
        var definition = (JsonObject)entry.Definition.DeepClone();
        var template = definition["quest"]!.AsObject();
        ValidateBoardTemplate(definition);
        var board = JsonSupport.RequireObject(Root(saves, "quest"), "quests");
        if (entry.Generated)
        {
            // Native uses generated_N. Keep existing IDs stable and allocate the
            // first unused identity, including native-hash aliases.
            var identities = Objects(board).Select(item => Hash(Text(item, "id"))).ToHashSet();
            var index = 0;
            while (identities.Contains(Hash("generated_" + index.ToString(CultureInfo.InvariantCulture)))) index = checked(index + 1);
            definition["id"] = "generated_" + index.ToString(CultureInfo.InvariantCulture);
            template["completion_reward"] = BoardGeneratedReward(saves, template);
        }
        var id = Text(definition, "id");
        if (Objects(board).Any(quest => Hash(Text(quest, "id")) == Hash(id)))
            throw new InvalidOperationException(EditorText.Format("QuestBoard_Duplicate", id));
        var slot = NextKey(board);
        AddPlotQuest(saves, definition, [], [], checkNaturalEligibility: false);
        return board[slot]?.AsObject() ?? throw new InvalidDataException("Quest initialization produced no entry.");
    }

    private BoardTemplate[] BoardTemplates()
    {
        if (_boardTemplates is not null) return _boardTemplates;
        var result = new List<BoardTemplate>();
        foreach (var definition in ReadPlotQuestDefinitions())
            if (definition["quest"] is JsonObject)
                result.Add(new("plot:" + Hash(Text(definition, "id")).ToString("X8"), definition, false));

        var levels = BoardGeneration.SelectMany(root => Objects(root["difficulty"]?["generated_resolve_level_difficulties"]))
            .Select(row => TownEventSaveEditor.RequiredInt(row, "difficulty")).Distinct().ToArray();
        var types = BoardTypes.SelectMany(root => Objects(root["types"]))
            .GroupBy(row => Hash(Text(row, "id"))).ToDictionary(group => group.Key, group => group.First());
        var ordinal = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var region in BoardGeneration.SelectMany(root => Objects(root["type"]?["available_quests_table"])))
        foreach (var table in region["generated_quest_table"] as JsonArray ?? [])
        foreach (var row in Objects(table))
        {
            var type = Text(row, "type");
            var dungeon = Text(region, "dungeon");
            // Native 0x140574A25 applies only a nonzero override.
            var overrideLevel = JsonSupport.ReadInt(row, "difficulty_override") ?? 0;
            foreach (var difficulty in overrideLevel != 0 ? new[] { overrideLevel } : levels)
            {
                var template = new JsonObject
                {
                    ["is_plot_quest"] = false, ["type"] = type, ["dungeon"] = dungeon,
                    ["difficulty"] = difficulty, ["length"] = row["length"]?.DeepClone(),
                    ["torch_setting"] = Text(row, "torch_setting"), ["raid_rules_override"] = Text(row, "raid_rules_override"),
                    ["goal_ids"] = new JsonArray()
                };
                // GetGoalIds (0x140456900) concatenates every all-region list
                // and every matching region list; it does not randomly choose one.
                if (types.TryGetValue(Hash(type), out var typeDefinition))
                {
                    var goals = Objects(typeDefinition["goal_lists"])
                        .Where(list => Text(list, "dungeon") == "all" || Hash(Text(list, "dungeon")) == Hash(dungeon))
                        .OrderBy(list => Text(list, "dungeon") == "all" ? 0 : 1)
                        .SelectMany(list => list["goals"] as JsonArray ?? [])
                        .SelectMany(Strings).ToArray();
                    template["goal_ids"] = new JsonArray(goals.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());
                }
                if (!seen.Add(template.ToJsonString())) continue;
                var key = "generated:" + ordinal++.ToString(CultureInfo.InvariantCulture);
                result.Add(new(key, new JsonObject { ["id"] = key, ["quest"] = template }, true));
            }
        }
        return _boardTemplates = result.ToArray();
    }

    private void ValidateBoardTemplate(JsonObject definition)
    {
        var template = definition["quest"]!.AsObject();
        var type = Text(template, "type");
        var dungeon = Text(template, "dungeon");
        if (!BoardTypes.SelectMany(root => Objects(root["types"])).Any(row => Hash(Text(row, "id")) == Hash(type)))
            throw new InvalidDataException(EditorText.Format("QuestBoard_MissingType", type));
        if (dungeon.Length == 0 || dungeon.IndexOfAny(['/', '\\', ':']) >= 0 || dungeon is "." or "..")
            throw new InvalidDataException(EditorText.Format("QuestBoard_MissingDungeon", dungeon));
        _ = OpenResource($"dungeons/{dungeon}/{dungeon}.dungeon.json");
        foreach (var value in new[] { Text(definition, "id"), type, dungeon, Text(template, "map_name"), Text(template, "torch_setting"), Text(template, "raid_rules_override") })
            NativeInventoryIdentity.RequireWritable(NativeInventoryIdentity.GetSaveIssue("quest", value));
        if (TownEventSaveEditor.RequiredInt(template, "difficulty") < 0 || TownEventSaveEditor.RequiredInt(template, "length") < 0)
            throw new InvalidDataException("Negative quest difficulty or length.");
        var goals = BoardTypes.SelectMany(root => Objects(root["goals"])).Select(row => Hash(Text(row, "id"))).ToHashSet();
        if (!Strings(template["goal_ids"]).Any()) throw new InvalidDataException(EditorText.Get("QuestBoard_NoGoals"));
        foreach (var goal in Strings(template["goal_ids"]).Concat(Strings(template["progression_goal_ids"])))
        {
            NativeInventoryIdentity.RequireWritable(NativeInventoryIdentity.GetSaveIssue("quest", goal));
            if (!goals.Contains(Hash(goal))) throw new InvalidDataException(EditorText.Format("QuestBoard_MissingGoal", goal));
        }
        foreach (var reward in Objects(template["threshold_rewards"]).Prepend(template["completion_reward"] as JsonObject ?? new JsonObject()))
        foreach (var item in Objects(reward["items_definition"]?["items"]).Where(item => JsonSupport.ReadInt(item, "amount") > 0))
        {
            var id = Text(item, "id");
            var itemType = Text(item, "type");
            NativeInventoryIdentity.RequireWritable(NativeInventoryIdentity.GetSaveIssue(itemType, id));
            if (Hash(itemType) != Hash("trinket")) continue;
            if (!EventTrinkets.TryGetValue(Hash(id), out var trinket) || trinket.Definition.HasProviderConflict)
                throw new InvalidDataException(EditorText.Format("QuestBoard_MissingTrinket", id));
            NativeInventoryIdentity.RequireWritable(trinket.Definition.SaveIdentityIssue);
        }
    }

    private JsonObject BoardGeneratedReward(IReadOnlyDictionary<string, JsonObject> saves, JsonObject quest)
    {
        var difficulty = TownEventSaveEditor.RequiredInt(quest, "difficulty");
        var length = TownEventSaveEditor.RequiredInt(quest, "length");
        var dungeon = Hash(Text(quest, "dungeon"));
        var rewards = BoardGeneration.Select(root => root["rewards"] as JsonObject).OfType<JsonObject>().ToArray();
        // Native loader appends array rows in file-query order. It does not
        // replace the entire generation table with the last resource file.
        var itemRows = rewards.SelectMany(root => root["item_table"] as JsonArray ?? []).ToArray();
        var xpRows = rewards.SelectMany(root => root["resolve_xp_table"] as JsonArray ?? []).ToArray();
        var items = new JsonObject();
        if (Cell(itemRows, difficulty, length) is JsonArray itemList)
            foreach (var item in Objects(itemList)) items[NextKey(items)] = item.DeepClone();
        var heirlooms = rewards.SelectMany(root => Objects(root["heirloom_type_map"]))
            .Where(row => Hash(Text(row, "dungeon")) == dungeon).SelectMany(row => Strings(row["types"])).ToArray();
        if (heirlooms.Length > 0)
        {
            var id = heirlooms[_random.Next(heirlooms.Length)];
            var rows = rewards.SelectMany(root => Objects(root["heirloom_amount_table"]))
                .Where(row => Hash(Text(row, "type")) == Hash(id))
                .SelectMany(row => row["amounts"] as JsonArray ?? []).ToArray();
            var amount = Cell(rows, difficulty, length)?.GetValue<int>() ?? 0;
            if (amount > 0) items[NextKey(items)] = new JsonObject { ["type"] = "heirloom", ["id"] = id, ["amount"] = amount };
        }
        var owned = CountOwnedTrinkets(saves);
        var draws = new List<(TrinketDefinition Trinket, float Chance)>();
        var rarities = ReadMatchingResources("trinkets", "\\.rarities.trinkets.json").SelectMany(root => Objects(root["rarities"]))
            .GroupBy(row => Hash(Text(row, "id"))).Select(group => group.First()).ToArray();
        foreach (var row in rewards.SelectMany(root => Objects(root["trinket_chance_table"])))
        {
            if (Hash(Text(row, "dungeon")) is var restriction && restriction != 0 && restriction != dungeon) continue;
            var chance = (float)(Cell((row["chances"] as JsonArray ?? []).ToArray(), difficulty, length)?.GetValue<double>() ?? 0);
            if (!float.IsFinite(chance)) throw new InvalidDataException("Invalid quest trinket chance.");
            if (chance <= 0) continue;
            var rarityIndex = Array.FindIndex(rarities, item => Hash(Text(item, "id")) == Hash(Text(row, "rarity")));
            if (rarityIndex < 0) throw new InvalidDataException(EditorText.Format("QuestBoard_MissingRarity", Text(row, "rarity")));
            var category = Text(rarities[rarityIndex], "award_category");
            for (var index = rarityIndex; index < rarities.Length; index++)
            {
                if (index != rarityIndex && Text(rarities[index], "award_category") != category) continue;
                var rarity = Hash(Text(rarities[index], "id"));
                var eligible = EventTrinkets.Values.Where(item => !item.Definition.HasProviderConflict &&
                    (Hash(item.Definition.Rarity) == 0 || Hash(item.Definition.Rarity) == rarity) &&
                    (Hash(item.Dungeon) == 0 || Hash(item.Dungeon) == dungeon) && !TrinketLimitReached(item.Definition, owned)).ToArray();
                if (eligible.Length == 0) continue;
                draws.Add((eligible[_random.Next(eligible.Length)].Definition, chance));
                break;
            }
        }
        if (draws.Count > 0)
        {
            var total = draws.Aggregate(0f, (sum, draw) => sum + draw.Chance);
            if (!float.IsFinite(total)) throw new InvalidDataException("Invalid quest trinket chance sum.");
            var roll = (float)_random.NextDouble() * total;
            var sum = 0f;
            var chosen = draws[^1].Trinket;
            foreach (var draw in draws) { sum += draw.Chance; if (sum > roll) { chosen = draw.Trinket; break; } }
            items[NextKey(items)] = new JsonObject { ["type"] = "trinket", ["id"] = chosen.Id, ["amount"] = 1 };
        }
        return new JsonObject { ["items_definition"] = new JsonObject { ["items"] = items },
            ["resolve_xp"] = Cell(xpRows, difficulty, length)?.GetValue<int>() ?? 0 };
    }

    private static JsonNode? Cell(JsonNode?[] rows, int row, int column) => row >= 0 && row < rows.Length &&
        rows[row] is JsonArray cells && column >= 0 && column < cells.Count ? cells[column] : null;
}
