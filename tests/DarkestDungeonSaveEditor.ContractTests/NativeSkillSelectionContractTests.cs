internal static partial class ContractSuite
{
    private static async Task VerifyOrdinaryNativeSkillSelectionAsync(string root)
    {
        var f = HeroSelectionContent(Path.Combine(root, "ordinary"), "local", skills: 6, target: 6);
        SelectionResource(f, "heroes/selection/selection_A/skin.png", "fixture");
        var catalog = HeroClassCatalog.Load(f.Content);
        var hero = catalog.HeroClasses.Single();
        Assert(hero.CombatSkillSelection.Values.All(value => value == new HeroCombatSkillSelection()),
            "Ordinary-skill cases must not enter behavior only enabled by new selection flags.");
        var all = Enumerable.Range(0, 6).Select(index => "s" + index).ToArray();
        for (var seed = 0; seed < 64; seed++)
        {
            var candidate = GenerateSelectionHero(catalog, seed);
            // Native 0x5D5C40 purchases the six draws; 0x5D5190 subsequently
            // equips the first four in class order, independently of draw order.
            Assert(candidate.InitiallyUnlockedCombatSkills!.SequenceEqual(all) &&
                candidate.Preview.CombatSkills.SequenceEqual(all.Take(4)),
                "Ordinary heroes unlock six skills before equipping the first four in class order.");
            Assert(candidate.UpgradePurchases.Count(p => all.Any(id => p.TreeId == "selection." + id)) == 6,
                "Manual generation retains its existing purchase-all-visible-skills policy.");

            var lateGuaranteed = StagecoachHeroCandidateFactory.Generate(catalog, hero with
            {
                Generation = hero.Generation! with { RandomCombatSkills = 5 }, GuaranteedCombatSkillIds = ["s5"]
            }, seed);
            Assert(lateGuaranteed.InitiallyUnlockedCombatSkills!.Count == 5 &&
                lateGuaranteed.InitiallyUnlockedCombatSkills.Contains("s5") &&
                lateGuaranteed.Preview.CombatSkills.SequenceEqual(lateGuaranteed.InitiallyUnlockedCombatSkills.Take(4)) &&
                !lateGuaranteed.Preview.CombatSkills.Contains("s5"),
                "Generation-guaranteed constrains unlocks; it must not displace an earlier equipped skill after the cap.");
        }
        await VerifyHeroSelectionPersistenceAsync(f.Root, GenerateSelectionHero(catalog), DsonSaveCodec.CreateNative());
        var info = Path.Combine(f.Source, "heroes/selection/selection.info.darkest");
        File.AppendAllText(info, "\ngeneration: .number_of_random_combat_skills 0\ncombat_skill: .id s5 .level 0 .generation_guaranteed true\n");
        var zeroCatalog = HeroClassCatalog.Load(f.Content);
        Assert(zeroCatalog.HeroClasses.Single().GenerationAvailability.All(level => level.CanGenerate),
            "An ordinary zero-draw hero with a guaranteed skill passes all generation-level preflights.");
        var zero = GenerateSelectionHero(zeroCatalog);
        Assert(zero.InitiallyUnlockedCombatSkills!.SequenceEqual(["s5"]) && zero.Preview.CombatSkills.SequenceEqual(["s5"]),
            "Native zero-draw guarantee still unlocks and equips one eligible skill.");
        var zeroDirectory = Directory.CreateDirectory(Path.Combine(f.Root, "zero")).FullName;
        await VerifyHeroSelectionPersistenceAsync(zeroDirectory, zero, DsonSaveCodec.CreateNative());
        Console.WriteLine("PASS: ordinary native unlock/equip phases, class-order caps, unlock-only guarantees and zero-draw persistence.");
    }

    private static async Task VerifyOrdinaryEventSkillPurchasesAsync(string root)
    {
        var f = await BuildTownEventFixtureAsync(root);
        var source = f.Content.Sources.Single(s => s.Kind == "base").Directory;
        var info = Path.Combine(source, "heroes/progression/progression.info.darkest");
        File.AppendAllText(info, "\nskill_selection: .can_select_combat_skills true .number_of_selected_combat_skills_max 4\n" +
            "generation: .number_of_random_combat_skills 6\n" +
            string.Concat(Enumerable.Range(1, 5).Select(index => $"combat_skill: .id other{index} .level 0\n")));
        var ids = new[] { "attack", "other1", "other2", "other3", "other4", "other5" };
        var expectedTrees = ids.Select(id => unchecked((int)HashLoc2Key("progression." + id))).ToHashSet();
        foreach (var type in new[] { "specific_recruit", "bonus_recruit", "stage_coach_bonus_recruits" })
        {
            // Keep ordinary coach recruits at level zero too, without relying
            // on the level-up path that unlocks all eligible combat upgrades.
            File.WriteAllText(Path.Combine(source, "campaign/town/buildings/stage_coach/stage_coach.building.json"),
                """{"data":{"stores":[{"id":"hero_recruit","data":{"upgraded_recruits_upgrades":[]}}]}}""");
            File.WriteAllText(f.EventsPath, new JsonObject { ["events"] = new JsonArray(new JsonObject
            {
                ["id"] = "ordinary", ["data"] = new JsonArray(new JsonObject
                {
                    ["type"] = type, ["string_data"] = type == "stage_coach_bonus_recruits" ? "" : "progression",
                    ["number_data"] = type == "specific_recruit" ? 0 : 1
                })
            }) }.ToJsonString());
            var saves = f.Saves.ToDictionary(pair => pair.Key, pair => pair.Value.DeepClone().AsObject());
            var definition = TownEventCatalog.Load(f.Content).Events.Single();
            TownEventSaveEditor.ReplaceCurrent(saves, definition, new TownEventInitializer(f.Content, 42).Initialize);
            var candidates = type == "stage_coach_bonus_recruits"
                ? saves["persist.town.json"]["base_root"]!["buildings"]!["stage_coach"]!["store"]!["hero_recruit"]!["generated"]!.AsObject()
                : saves["persist.town_event.json"]["base_root"]!["bonus_hero_entries"]!.AsObject();
            var offer = candidates.Single(pair => pair.Key != "50");
            Assert(offer.Value!["resolveXp"]!.GetValue<int>() == 0 &&
                offer.Value["skills"]!["selected_combat_skills"]!.AsObject().Select(pair => pair.Key).SequenceEqual(ids.Take(4)),
                $"{type}: the level-zero candidate equips the first four ordinary skills.");
            var purchased = saves["persist.upgrades.json"]["base_root"]!["purchases"]!.AsObject()
                .Where(pair => pair.Value!["instance_number"]!.GetValue<int>() == int.Parse(offer.Key))
                .Select(pair => pair.Value!["tree_id"]!.GetValue<int>()).Where(expectedTrees.Contains).ToHashSet();
            Assert(purchased.SetEquals(expectedTrees), $"{type}: all six initial combat unlocks must survive event purchase filtering.");
            foreach (var name in new[] { "town", "town_event", "upgrades" })
            {
                var value = saves["persist." + name + ".json"];
                Assert(JsonNode.DeepEquals(value, await RoundtripDirectoryQueryAsync(root, type + "-" + name, value, DsonSaveCodec.CreateNative())),
                    $"{type}: separate unlock/equipment sets survive DSON persistence.");
            }
        }
        Console.WriteLine("PASS: ordinary level-zero specific, bonus and coach recruit purchase retention with DSON persistence.");
    }
}
