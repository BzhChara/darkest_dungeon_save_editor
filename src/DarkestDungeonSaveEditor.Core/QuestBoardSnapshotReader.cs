using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace DarkestDungeonSaveEditor.Core;

/// <summary>Reads the already-generated board. Never generates rewards or changes a save.</summary>
public static partial class QuestBoardSnapshotReader
{
    private const string QuestArt = "campaign/town/quest_select/";

    public static QuestBoardSnapshot Read(ActiveContentSnapshot content, JsonObject document, string sourceSha256,
        JsonObject? progression = null)
    {
        var issues = new List<string>();
        var board = JsonSupport.RequireObject(document, "base_root", "quests");
        var entries = board.Select(pair => (Key: pair.Key, Value: pair.Value as JsonObject
            ?? throw new InvalidDataException($"Invalid quest entry: {pair.Key}."))).ToArray();
        var resources = new QuestBoardResources(content, issues);
        var goals = resources.Definitions(NativeReferenceJsonKind.QuestTypes, "goals");
        // Runtime provision lookup scans the entire plot vector and retains its last match.
        var plots = resources.Definitions(NativeReferenceJsonKind.PlotQuests, "plot_quests", lastMatch: true);
        var trinkets = resources.Trinkets();
        var monsterSharedHealth = resources.MonsterSharedHealth(entries.SelectMany(pair => SavedStrings(pair.Value, "goal_ids"))
            .Where(id => goals.ContainsKey(HashSaved(id))).Select(id => goals[HashSaved(id)])
            .SelectMany(goal => NativeJsonReader.Array(Child(goal, "data"), "monster_class_ids"))
            .Select(value => value.GetString()!));
        var regions = resources.Progression(resources.Regions(entries.Select(pair => RequiredText(pair.Value, "dungeon")).Distinct().ToArray()), progression);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var camping = entries.ToDictionary(pair => pair.Key, pair => resources.Camping(
            RequiredText(pair.Value, "type"), RequiredInt(pair.Value, "length"), RequiredBool(pair.Value, "is_plot_quest"),
            plots.GetValueOrDefault(HashSaved(RequiredText(pair.Value, "id"))),
            SavedStrings(pair.Value, "goal_ids").Where(id => goals.ContainsKey(HashSaved(id))).Select(id => goals[HashSaved(id)])));
        string CampingKey((int Count, bool IsRandom) camp) => "town_quest_number_of_camps" +
            (camp.IsRandom ? "_random" : camp.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var camp in camping.Values) keys.Add(CampingKey(camp));
        foreach (var region in regions) keys.Add("dungeon_name_" + region.Id);
        foreach (var (_, quest) in entries)
        {
            var identity = LocalizationIdentity(quest);
            keys.Add("town_quest_name_" + identity);
            keys.Add("town_quest_description_" + identity);
            foreach (var reward in RewardItems(quest))
            {
                var type = RequiredText(reward.Item, "type");
                var id = JsonSupport.ReadRawString(reward.Item, "id");
                if (type == "trinket" && trinkets.TryGetValue(HashSaved(id), out var trinket)) id = Text(trinket, "id");
                foreach (var key in ContentLocalizationCatalog.GetInventoryItemKeys(type, id)) keys.Add(key);
            }
            foreach (var goalId in SavedStrings(quest, "goal_ids"))
                if (goals.TryGetValue(HashSaved(goalId), out var goal)) AddGoalKeys(goal, keys);
        }
        var localization = ContentLocalizationCatalog.Load(content, keys);
        issues.AddRange(localization.Issues);
        var result = new List<QuestBoardQuest>();
        foreach (var (key, quest) in entries)
        {
            var id = RequiredText(quest, "id");
            var type = RequiredText(quest, "type");
            var dungeon = RequiredText(quest, "dungeon");
            var level = RequiredInt(quest, "difficulty");
            var length = RequiredInt(quest, "length");
            var isPlot = RequiredBool(quest, "is_plot_quest");
            var goalIds = SavedStrings(quest, "goal_ids");
            var objectives = goalIds.Select(goalId => goals.TryGetValue(HashSaved(goalId), out var goal)
                ? DescribeGoal(goalId, goal, localization, monsterSharedHealth)
                : new BilingualContentName(goalId, goalId)).ToArray();
            foreach (var missing in goalIds.Where(goalId => !goals.ContainsKey(HashSaved(goalId))))
                issues.Add($"Quest '{id}' has an unresolved goal definition: {missing}.");
            var rewards = RewardItems(quest).Select(saved =>
            {
                var rewardType = RequiredText(saved.Item, "type");
                var rewardId = JsonSupport.ReadRawString(saved.Item, "id");
                var rarity = string.Empty;
                if (rewardType == "trinket" && trinkets.TryGetValue(HashSaved(rewardId), out var trinket))
                {
                    rewardId = Text(trinket, "id");
                    rarity = Text(trinket, "rarity");
                }
                var amount = RequiredInt(saved.Item, "amount");
                var icon = $"panels/icons_equip/{rewardType}/inv_{rewardType}+{rewardId}.png";
                if (rewardType == "gold" && rewardId.Length == 0) icon = "panels/icons_equip/gold/inv_gold+_3.png";
                return new QuestBoardReward(rewardType, rewardId, amount, icon, rarity,
                    localization.GetInventoryItemName(rewardType, rewardId), saved.Recovery);
            }).Where(reward => reward.Amount > 0).ToArray();
            var identity = LocalizationIdentity(quest);
            result.Add(new(key, id, type, dungeon, level, length, isPlot, goalIds,
                localization.GetQuestText("town_quest_name_" + identity),
                localization.GetQuestText("town_quest_description_" + identity),
                new(string.Join("\n", objectives.Select(text => text.Chinese)),
                    string.Join("\n", objectives.Select(text => text.English))), rewards)
            {
                Firewood = camping[key].Count,
                HasRandomCamping = camping[key].IsRandom,
                LocalizedCampingTooltip = localization.GetQuestText(CampingKey(camping[key])),
                IconPath = QuestArt + $"quest_select_{type}_{level}.png",
                LengthIconPath = QuestArt + $"quest_select_length_{(isPlot ? "plot" : "generated")}_{length}.png"
            });
        }
        var requests = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in new[] { "quest_select.background.png", "quest_select.questverbose_bg.png", "quest_select.icon.png",
                     "dungeon_no_progressionbar.png", "dungeon_progressionbar.png", "quest_select_selected.png" }) requests.Add(QuestArt + file);
        foreach (var file in new[] { "marker_battle.png", "room_boss.png", "marker_curio.png", "room_treasure.png",
                     "marker_trap.png", "marker_obstacle.png", "indicator.png" }) requests.Add("panels/icons_map/" + file);
        foreach (var quest in result)
        {
            requests.Add(quest.IconPath);
            requests.Add(quest.LengthIconPath);
            if (quest.IsPlotQuest) requests.Add(QuestArt + $"quest_select_{quest.Id}.png");
            foreach (var reward in quest.Rewards)
            {
                requests.Add(reward.IconPath);
                if (reward.Rarity.Length > 0) requests.Add($"panels/icons_equip/trinket/rarity_{reward.Rarity}.png");
            }
        }
        var safeRequests = requests.Where(path => !Path.IsPathRooted(path) &&
            !path.Replace('\\', '/').Split('/').Any(part => part is ".." or ".")).ToArray();
        var images = NativeContentFileResolver.ResolveOpenRequests(content.Sources, safeRequests, "Quest board artwork", issues)
            .ToDictionary(pair => pair.Key, pair => pair.Value.Path, StringComparer.Ordinal);
        // A plot can supply a dedicated icon; ordinary plots use their saved type and difficulty.
        result = result.Select(quest => quest.IsPlotQuest && images.ContainsKey(QuestArt + $"quest_select_{quest.Id}.png")
            ? quest with { IconPath = QuestArt + $"quest_select_{quest.Id}.png" } : quest).ToList();
        return new(content.Profile.ProfileDirectory, sourceSha256, result,
            regions.Select(region => region with { LocalizedName = localization.GetQuestText("dungeon_name_" + region.Id) }).ToArray(),
            images, issues.Distinct(StringComparer.Ordinal).ToArray());
    }

    private static string LocalizationIdentity(JsonObject quest) => RequiredBool(quest, "is_plot_quest")
        ? RequiredText(quest, "id")
        : string.Join("+", new[] { RequiredText(quest, "type"), RequiredInt(quest, "length").ToString(CultureInfo.InvariantCulture),
            RequiredText(quest, "dungeon") }.Concat(SavedStrings(quest, "goal_ids")));

    private static IEnumerable<(JsonObject Item, bool Recovery)> RewardItems(JsonObject quest)
    {
        var reward = quest["completion_reward"] as JsonObject;
        foreach (var item in SavedObjects(reward?["items_definition"]?["items"])) yield return (item, false);
        foreach (var item in SavedObjects(reward?["trinket_retention_ids"]?["items"])) yield return (item, true);
    }

    private static IEnumerable<JsonObject> SavedObjects(JsonNode? node) => node switch
    {
        JsonObject map => map.Select(pair => pair.Value as JsonObject ?? throw new InvalidDataException("Invalid quest reward entry.")),
        null => [],
        _ => throw new InvalidDataException("Invalid quest reward inventory.")
    };
    private static string RequiredText(JsonObject node, string name) => node[name]?.GetValue<string>()
        ?? throw new InvalidDataException($"Quest field '{name}' is missing.");
    private static int RequiredInt(JsonObject node, string name) => JsonSupport.ReadInt(node, name)
        ?? throw new InvalidDataException($"Quest field '{name}' is not an integer.");
    private static bool RequiredBool(JsonObject node, string name) => node[name]?.GetValue<bool>()
        ?? throw new InvalidDataException($"Quest field '{name}' is missing.");
    private static string[] SavedStrings(JsonObject node, string name) => node[name] is JsonArray list
        ? list.Select(value => value?.GetValue<string>() ?? throw new InvalidDataException($"Invalid quest {name} entry.")).ToArray()
        : throw new InvalidDataException($"Quest field '{name}' is not an array.");
    private static uint HashSaved(string id) => NativeInventoryIdentity.HashSavedField(id);
    private static string Text(JsonElement node, string name) => NativeJsonReader.ReadCString(node, name);
    private static JsonElement Child(JsonElement node, string name) => NativeJsonReader.TryGetProperty(node, name, out var child) ? child : default;

    private static void AddGoalKeys(JsonElement goal, HashSet<string> keys)
    {
        var type = Text(goal, "type");
        keys.Add("town_quest_goal_start_plural_" + type);
        keys.Add("town_quest_goal_start_single_" + type);
        var data = Child(goal, "data");
        foreach (var monster in NativeJsonReader.Array(data, "monster_class_ids")) keys.Add(ContentLocalizationCatalog.GetMonsterNameKey(monster.GetString()!));
        keys.Add(ContentLocalizationCatalog.GetCurioTitleKey(Text(data, "curio_name")));
        var item = Child(data, "item");
        foreach (var key in ContentLocalizationCatalog.GetInventoryItemKeys(Text(item, "type"), Text(item, "id"))) keys.Add(key);
    }

    private static BilingualContentName DescribeGoal(string id, JsonElement goal, ContentLocalizationCatalog localization,
        IReadOnlyDictionary<uint, uint> monsterSharedHealth)
    {
        var type = Text(goal, "type");
        var data = Child(goal, "data");
        var amount = NativeJsonReader.ReadFloat(data, "amount") ?? 0;
        BilingualContentName target = BilingualContentName.Empty;
        if (type == "gather")
        {
            var item = Child(data, "item");
            amount = NativeJsonReader.ReadFloat(item, "amount") ?? 0;
            target = localization.GetInventoryItemName(Text(item, "type"), Text(item, "id"));
            target = WithFallback(target, Text(item, "id"));
        }
        else if (type == "activate") target = WithFallback(localization.GetCurioTitle(Text(data, "curio_name")), Text(data, "curio_name"));
        else if (type == "kill_monster")
        {
            var targets = NativeJsonReader.Array(data, "monster_class_ids").Select(value => value.GetString()!).ToArray();
            var groups = targets.Select(NativeResourceIdentity.HashCString).Where(monsterSharedHealth.ContainsKey)
                .Select(hash => monsterSharedHealth[hash]).ToArray();
            // Native includes zero in the set, then additionally counts every
            // ungrouped monster, and caps the requested amount at that total.
            var count = groups.Distinct().Count() + groups.Count(group => group == 0);
            amount = Math.Min(unchecked((uint)(int)amount), (uint)count);
            if (targets.LastOrDefault() is { } monster)
                target = WithFallback(localization.GetMonsterName(monster), monster);
        }
        // Survival's single/plural keys mean finite/endless, not one/many enemies.
        var single = type != "kill_monster" && (type == "survival" ? amount != 0 : amount == 1);
        var template = localization.GetQuestText($"town_quest_goal_start_{(single ? "single" : "plural")}_{type}");
        var plural = localization.GetQuestText("town_quest_goal_start_plural_" + type);
        var number = type is "explore_room" or "battle_room" ? (NativeJsonReader.ReadFloat(data, "percentage") ?? 0) * 100 : amount;
        string Format(string text, string alternate, string targetText)
        {
            if (text.Length == 0) text = alternate;
            if (text.Length == 0) return id;
            text = Regex.Replace(text, @"\{\?[^}]*\}", string.Empty);
            text = Regex.Replace(text, @"%%|%[-+0-9.]*[dfs]", match => match.Value == "%%" ? "%" :
                match.Value.EndsWith('s') ? targetText : number.ToString("0", CultureInfo.InvariantCulture));
            return text;
        }
        return new(Format(template.Chinese, plural.Chinese, target.Chinese), Format(template.English, plural.English, target.English));
    }

    private static BilingualContentName WithFallback(BilingualContentName name, string id) =>
        new(name.Chinese.Length > 0 ? name.Chinese : name.English.Length > 0 ? name.English : id,
            name.English.Length > 0 ? name.English : name.Chinese.Length > 0 ? name.Chinese : id);
}
