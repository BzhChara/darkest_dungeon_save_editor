using System.Globalization;
using System.Text.Json.Nodes;

namespace DarkestDungeonSaveEditor.Core;

internal sealed partial class TownEventInitializer
{
    private void AddRecruits(IReadOnlyDictionary<string, JsonObject> saves, TownEventDefinition definition,
        TownEventEffect effect, string type, List<string> changes)
    {
        var ordinary = type == "stage_coach_bonus_recruits";
        var count = type == "quirked_recruit" ? 1 : ordinary ? Math.Max(0, Integer(effect.NumberData)) : Count(effect.NumberData);
        if (count > 4096) throw new InvalidDataException("Too many event recruits.");
        var level = type == "quirked_recruit" ? Integer(effect.NumberData) :
            type == "bonus_recruit_stage_coach_level" ? HighestStagecoachLevel(saves) : 0;
        if (level < 0 || level >= Heroes.ResolveLevelThresholds.Count)
            throw new InvalidDataException($"Invalid event recruit level: {level}.");
        for (var index = 0; index < count; index++)
        {
            var upgrade = ordinary ? ChooseStagecoachUpgrade(saves) : null;
            var recruitLevel = upgrade is null ? level : TownEventSaveEditor.RequiredInt(upgrade, "level");
            var hero = ordinary ? ChooseStagecoachClass(saves) : type == "quirked_recruit"
                ? ChooseRecruitClass(saves)
                : Heroes.HeroClasses.FirstOrDefault(hero => Hash(hero.Id) == Hash(effect.StringData))
                    ?? throw new InvalidDataException(EditorText.Format("TownEvent_MissingHero", effect.StringData));
            // The event retains the full string hash, but its comma-separated
            // quirk list is read from a separate 64-byte character buffer.
            var forcedIds = type == "quirked_recruit" ? NativeJsonReader.BoundedCString(effect.StringData, 63).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) :
                upgrade?["starting_quirks"] is JsonArray initial ? initial.Select(id => id!.GetValue<string>()).ToArray() : [];
            var forced = forcedIds
                .Select(id => Heroes.InitialQuirks.FirstOrDefault(quirk => Hash(quirk.Id) == Hash(id))?.Id
                    ?? throw new InvalidDataException($"Unresolved event quirk: {id}.")).ToArray();
            var quirks = ChooseNaturalQuirks(saves, hero, forced, upgrade);
            // Native AddResolveXp calls 0x1405CB4A0: purchases eligible equipment
            // and combat upgrades, refreshes ranks, then fills the new maximum HP.
            var generated = StagecoachHeroCandidateFactory.Generate(Heroes, hero, _random.Next(), recruitLevel, quirks);
            generated.Candidate["is_from_town_event"] = !ordinary;
            var skills = JsonSupport.RequireObject(generated.Candidate, "skills");
            var extraPurchases = AddExtraRecruitSkills(hero, skills, upgrade);
            var allowedTrees = HeroSkillPurchaseTargets.Equipment(hero.Id).Values
                .Concat(HeroSkillPurchaseTargets.Combat(hero.Id,
                    recruitLevel > 0 ? hero.CombatSkillIds :
                    JsonSupport.RequireObject(skills, "selected_combat_skills").Select(pair => pair.Key).Concat(extraPurchases.Combat)).Values)
                .Concat(HeroSkillPurchaseTargets.Camping(hero.Id,
                    JsonSupport.RequireObject(skills, "selected_camping_skills").Select(pair => pair.Key).Concat(extraPurchases.Camping)).Values)
                .ToHashSet(StringComparer.Ordinal);
            var purchases = generated.UpgradePurchases.Where(purchase => allowedTrees.Contains(purchase.TreeId)).ToArray();
            if (!ordinary)
            {
                StagecoachHeroSaveEditor.AddEventCandidate(saves["persist.town.json"], saves["persist.roster.json"],
                    saves["persist.upgrades.json"], saves["persist.town_event.json"], generated.Candidate, purchases);
            }
            else
            {
                // Native ordinary-store generation inserts into that store
                // before upgrade quirks are applied. A forced shard_hungry
                // must not reroute an event bonus into the shard shop.
                var result = StagecoachHeroSaveEditor.AddCandidateToPool(saves["persist.town.json"], saves["persist.roster.json"],
                    saves["persist.upgrades.json"], generated.Candidate, purchases, StagecoachRecruitPool.Ordinary);
                CopyContents(saves["persist.town.json"], result.UpdatedTown);
                CopyContents(saves["persist.roster.json"], result.UpdatedRoster);
                CopyContents(saves["persist.upgrades.json"], result.UpdatedUpgrades);
            }
        }
        changes.Add(EditorText.Format(ordinary ? "TownEvent_StagecoachCandidates" : "TownEvent_EventCandidates", count));
    }

    private HeroClassDefinition ChooseRecruitClass(IReadOnlyDictionary<string, JsonObject> saves)
    {
        var history = (TownEventSaveEditor.Root(saves, "town_event")["result_event_history"] as JsonArray)!
            .Select(node => node!.GetValue<int>()).ToHashSet();
        var eligible = Heroes.HeroClasses.Where(hero => !hero.HasProviderConflict && hero.Generation?.IsEnabled == true &&
            (hero.Generation.TownEventDependency.Length == 0 || history.Contains(unchecked((int)Hash(hero.Generation.TownEventDependency)))))
            .ToArray();
        if (eligible.Length == 0) throw new InvalidDataException(EditorText.Get("TownEvent_NoRecruitClasses"));
        // Event quirked_recruit passes no deck-history pointer: the native
        // generator uses one equal-weight entry per eligible class in that case.
        return eligible[_random.Next(eligible.Length)];
    }

    private IReadOnlyCollection<string> ChooseNaturalQuirks(IReadOnlyDictionary<string, JsonObject> saves,
        HeroClassDefinition hero, IReadOnlyCollection<string> forced, JsonObject? upgrade)
    {
        var selected = new List<string>();
        var generation = hero.Generation ?? throw new InvalidDataException("Missing hero generation rules.");
        // Native GetQuirksToExclude (0x1405C6D90) queries the roster and
        // stagecoach. Event-owned offers do not enter either query; counting
        // them here would add a singleton restriction absent from the game.
        var used = RosterHeroes(saves)
            .Concat(Objects(JsonSupport.RequireObject(TownEventSaveEditor.Root(saves, "town"), "buildings", "stage_coach", "store"))
                .SelectMany(pool => Objects(pool["generated"]))).ToArray();
        foreach (var positive in new[] { true, false })
        {
            var min = positive ? generation.PositiveQuirksMin : generation.NegativeQuirksMin;
            var max = positive ? generation.PositiveQuirksMax : generation.NegativeQuirksMax;
            if (min is null || max is null || min < 0 || max < min || max > 4096)
                throw new InvalidDataException("Invalid natural recruit quirk counts.");
            var limit = (positive ? Heroes.InitialQuirkLimits.Positive : Heroes.InitialQuirkLimits.Negative)
                ?? throw new InvalidDataException("Unknown recruit quirk limit.");
            var existing = selected.Count(id => Heroes.InitialQuirks.Single(q => q.Id == id).IsPositive == positive);
            var count = Math.Min(_random.Next(min.Value, max.Value + 1), Math.Max(0, limit - existing));
            AddRandom(positive, count);
        }
        foreach (var id in forced)
        {
            var quirk = Heroes.InitialQuirks.Single(item => item.Id == id);
            if (selected.Contains(id)) continue;
            selected.RemoveAll(chosen => quirk.IncompatibleQuirkIds.Any(other => Hash(other) == Hash(chosen)) ||
                Heroes.InitialQuirks.Single(item => item.Id == chosen).IncompatibleQuirkIds.Any(other => Hash(other) == Hash(id)));
            var sameKind = selected.Where(chosen =>
            {
                var q = Heroes.InitialQuirks.Single(item => item.Id == chosen);
                return q.IsDisease == quirk.IsDisease && q.IsPositive == quirk.IsPositive;
            }).ToArray();
            var limit = quirk.IsDisease ? Heroes.InitialQuirkLimits.Diseases :
                quirk.IsPositive == true ? Heroes.InitialQuirkLimits.Positive : Heroes.InitialQuirkLimits.Negative;
            if (limit is null or <= 0) throw new InvalidDataException("Unknown event quirk limit.");
            if (sameKind.Length >= limit) selected.Remove(sameKind[_random.Next(sameKind.Length)]);
            selected.Add(id);
        }
        if (upgrade is not null)
        {
            foreach (var positive in new[] { true, false })
            {
                var extra = JsonSupport.ReadInt(upgrade,
                    positive ? "number_of_extra_positive_quirks" : "number_of_extra_negative_quirks") ?? 0;
                if (extra < 0 || extra > 4096) throw new InvalidDataException("Invalid extra recruit quirk count.");
                var limit = positive ? Heroes.InitialQuirkLimits.Positive : Heroes.InitialQuirkLimits.Negative;
                var existing = selected.Count(id => Heroes.InitialQuirks.Single(q => q.Id == id).IsPositive == positive);
                AddRandom(positive, Math.Min(extra, Math.Max(0, (limit ?? 0) - existing)));
            }
        }
        return selected;

        void AddRandom(bool positive, int count)
        {
            for (var index = 0; index < count; index++)
            {
                // RequiresSaveContext is not a native exclusion: singleton
                // quirks (for example twilight_dreamer) remain random draws
                // when the roster/coach context below permits them.
                var candidates = Heroes.InitialQuirks.Where(q => q.Kind == HeroInitialQuirkKind.Natural &&
                    q.WriteStatus is HeroInitialQuirkWriteStatus.Direct or HeroInitialQuirkWriteStatus.RequiresSaveContext && q.IsPositive == positive &&
                    !q.IsDisease && q.RandomChance > 0 && !selected.Contains(q.Id) &&
                    !hero.IncompatibleInitialQuirkIds.Any(id => Hash(id) == Hash(q.Id)) &&
                    !q.IncompatibleQuirkIds.Any(id => selected.Any(chosen => Hash(chosen) == Hash(id))) &&
                    !selected.Any(id => Heroes.InitialQuirks.Single(chosen => chosen.Id == id).IncompatibleQuirkIds.Any(other => Hash(other) == Hash(q.Id))) &&
                    (q.DefinitionLimit is not > 0 || used.Count(candidate => candidate["quirks"] is JsonObject qs &&
                        qs.Any(pair => Hash(pair.Key) == Hash(q.Id))) < q.DefinitionLimit)).ToArray();
                if (candidates.Length == 0) break;
                var roll = _random.NextDouble() * candidates.Sum(q => q.RandomChance!.Value);
                var chosen = candidates[^1];
                foreach (var candidate in candidates)
                {
                    roll -= candidate.RandomChance!.Value;
                    if (roll < 0) { chosen = candidate; break; }
                }
                selected.Add(chosen.Id);
            }
        }
    }

    private JsonObject StagecoachStoreDefinition() => Objects(OpenResource("campaign/town/buildings/stage_coach/stage_coach.building.json")["data"]?["stores"])
        .Single(store => Hash(JsonSupport.ReadRawString(store, "id")) == Hash("hero_recruit"))["data"]!.AsObject();

    private int HighestStagecoachLevel(IReadOnlyDictionary<string, JsonObject> saves) =>
        Objects(StagecoachStoreDefinition()["upgraded_recruits_upgrades"]).Where(upgrade => Purchased(saves, upgrade))
            .Select(upgrade => TownEventSaveEditor.RequiredInt(upgrade, "level")).DefaultIfEmpty(0).Max();

    private static void CopyContents(JsonObject target, JsonObject source)
    {
        target.Clear();
        foreach (var pair in source) target[pair.Key] = pair.Value?.DeepClone();
    }
}
