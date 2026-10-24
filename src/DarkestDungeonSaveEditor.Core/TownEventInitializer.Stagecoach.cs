using System.Globalization;
using System.Text.Json.Nodes;

namespace DarkestDungeonSaveEditor.Core;

internal sealed partial class TownEventInitializer
{
    private JsonObject? ChooseStagecoachUpgrade(IReadOnlyDictionary<string, JsonObject> saves)
    {
        // Existing candidates and the previous raid's guaranteed replacements
        // have already been delivered. Only roll the newly added event slots.
        var choices = Objects(StagecoachStoreDefinition()["upgraded_recruits_upgrades"])
            .Where(upgrade => Purchased(saves, upgrade)).ToArray();
        var weights = choices.Select(choice => choice["chance"]?.GetValue<double>() ?? 0).ToArray();
        if (weights.Any(weight => !double.IsFinite(weight) || weight < 0))
            throw new InvalidDataException("Invalid stagecoach upgrade chance.");
        var roll = _random.NextDouble() * Math.Max(1, weights.Sum());
        for (var index = 0; index < choices.Length; index++)
        {
            roll -= weights[index];
            if (roll < 0) return choices[index];
        }
        return null;
    }

    private HeroClassDefinition ChooseStagecoachClass(IReadOnlyDictionary<string, JsonObject> saves)
    {
        var history = (JsonArray)Root(saves, "town_event")["result_event_history"]!;
        var eligible = Heroes.HeroClasses.Where(hero => !hero.HasProviderConflict && hero.Generation?.IsEnabled == true &&
            (hero.Generation.TownEventDependency.Length == 0 || history.Any(id => id!.GetValue<int>() ==
                unchecked((int)Hash(hero.Generation.TownEventDependency))))).ToArray();
        if (eligible.Length == 0) throw new InvalidDataException(EditorText.Get("TownEvent_NoRecruitClasses"));
        var deck = JsonSupport.RequireObject(Root(saves, "town"), "buildings", "stage_coach", "store", "hero_recruit", "deck_history_version_0");
        var option = Root(saves, "game")["profile_options"]?["values"]?["deck_based_stage_coach"];
        var useDeck = option switch
        {
            null => true,
            JsonArray { Count: 2 } values => values[0]!.GetValue<bool>(),
            JsonValue value when value.TryGetValue<bool>(out var enabled) => enabled,
            _ => throw new InvalidDataException("Invalid stagecoach deck option.")
        };
        var weights = new double[eligible.Length];
        for (var attempt = 0; attempt < 2; attempt++)
        {
            for (var index = 0; index < eligible.Length; index++)
            {
                var hero = eligible[index];
                var generation = hero.Generation!;
                var cards = generation.CardsInDeck ?? throw new InvalidDataException("Missing hero deck size.");
                foreach (var other in Heroes.HeroClasses.Where(other => other.Id != hero.Id && other.Generation?.IsEnabled == true))
                {
                    var reduction = other.Generation!;
                    // Native 0x140581281 reads the other class's card count
                    // (+0x11D4), not the separately parsed reduction amount.
                    var reductionCards = reduction.CardsInDeck ?? 0;
                    if (Hash(reduction.DeckReductionTarget) == Hash(hero.Id) && cards > reductionCards)
                        cards -= reductionCards;
                }
                var key = Hash(hero.Id).ToString(CultureInfo.InvariantCulture);
                var used = (deck[key] as JsonObject) is { } entry ? TownEventSaveEditor.RequiredInt(entry, "count") : 0;
                var chance = generation.CardChance ?? throw new InvalidDataException("Missing hero deck chance.");
                if (cards < 0 || chance < 0 || !double.IsFinite(chance) || used < 0)
                    throw new InvalidDataException("Invalid hero deck.");
                weights[index] = useDeck ? Math.Max(0, cards - used) * chance : 1;
            }
            if (weights.Sum() > 0) break;
            if (attempt == 0) deck.Clear();
        }
        var total = weights.Sum();
        if (!(total > 0) || !double.IsFinite(total)) throw new InvalidDataException(EditorText.Get("TownEvent_NoRecruitClasses"));
        var roll = _random.NextDouble() * total;
        var choice = eligible.Length - 1;
        for (var index = 0; index < eligible.Length; index++)
        {
            roll -= weights[index];
            if (roll < 0) { choice = index; break; }
        }
        var selected = eligible[choice];
        var selectedKey = Hash(selected.Id).ToString(CultureInfo.InvariantCulture);
        var previous = (deck[selectedKey] as JsonObject) is { } saved ? TownEventSaveEditor.RequiredInt(saved, "count") : 0;
        deck[selectedKey] = new JsonObject { ["count"] = checked(previous + 1) };
        return selected;
    }

    private (IReadOnlyList<string> Combat, IReadOnlyList<string> Camping) AddExtraRecruitSkills(
        HeroClassDefinition hero, JsonObject skills, JsonObject? upgrade)
    {
        if (upgrade is null) return ([], []);
        string[] Pick(IEnumerable<string> all, JsonObject selected, string countKey)
        {
            var count = JsonSupport.ReadInt(upgrade, countKey) ?? 0;
            if (count < 0) throw new InvalidDataException("Negative additional recruit skill count.");
            return all.Where(id => !selected.ContainsKey(id)).Distinct(StringComparer.Ordinal)
                .OrderBy(_ => _random.Next()).Take(count).ToArray();
        }
        var combat = Pick(hero.CombatSkillIds, JsonSupport.RequireObject(skills, "selected_combat_skills"), "number_of_extra_combat_skills");
        var camping = Pick(hero.ClassCampingSkillIds.Concat(hero.SharedCampingSkillIds),
            JsonSupport.RequireObject(skills, "selected_camping_skills"), "number_of_extra_camping_skills");
        // Extra skills are purchased; the native generator does not equip all
        // of them beyond the hero's selected-skill limit.
        return (combat, camping);
    }
}
