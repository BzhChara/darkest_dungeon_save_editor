using DarkestDungeonSaveEditor.Core.Dson;

internal static partial class ContractSuite
{
    private static async Task VerifyEventBoundariesAsync(string root)
    {
        var f = await BuildTownEventFixtureAsync(root);
        var catalog = TownEventCatalog.Load(f.Content);
        Dictionary<string, JsonObject> Copies() => f.Saves.ToDictionary(pair => pair.Key, pair => pair.Value.DeepClone().AsObject());
        var selected = catalog.Events.Single(item => item.Id == "test_quirked_recruit");
        var composite = selected with
        {
            Id = "combined_event",
            Effects = [new("event_cost", "gold", 5), new("event_cost", "gold", 7),
                new("upgrade_tag_free", "weapon", 2), new("upgrade_tag_free", "weapon", 3),
                new("roster_stress_heal", "", 50), new("bonus_currency", "gold", 12),
                new("plot_quest", "event_plot", 1), new("stage_coach_bonus_recruits", "", 1),
                new("quirked_recruit", " event_positive ", 1)]
        };
        var saves = Copies();
        TownEventSaveEditor.ReplaceCurrent(saves, composite, new TownEventInitializer(f.Content, 7).Initialize);
        var state = saves["persist.town_event.json"]["base_root"]!;
        Assert(state["event_cost"]!["count"]!.GetValue<int>() == 1 && state["event_cost"]!["0"]!["amount"]!.GetValue<int>() == 12 &&
            state["free_upgrade_tags"]!["0"]!["amount"]!.GetValue<int>() == 5, "Repeated cost/quota effects combine by native identity.");
        Assert(JsonNode.DeepEquals(saves["persist.roster.json"]["base_root"]!["heroes"], f.Saves["persist.roster.json"]["base_root"]!["heroes"]),
            "An unclaimed roster heal must not change heroes during replacement.");
        var delivered = new[] { "persist.town.json", "persist.quest.json", "persist.estate.json" }
            .ToDictionary(name => name, name => saves[name].DeepClone());
        TownEventSaveEditor.ReplaceCurrent(saves, catalog.Events.Single(item => item.Id == "test_free_activity"), new TownEventInitializer(f.Content, 8).Initialize);
        Assert(delivered.All(pair => JsonNode.DeepEquals(pair.Value, saves[pair.Key])) &&
            state["bonus_hero_entries"]!.AsObject().Count == 0 && state["event_cost"]!.AsObject().Count == 0 &&
            state["free_upgrade_tags"]!.AsObject().Count == 0 && !state["has_unclaimed_interaction"]!.GetValue<bool>(),
            "Replacing again retires pending offers and quota but retains delivered heroes, quests and currency.");

        // Event offers must not require a shard shop, even with shard_hungry.
        var direct = Copies();
        direct["persist.town_event.json"]["base_root"]!["bonus_hero_entries"] = new JsonObject();
        direct["persist.town.json"]["base_root"]!["buildings"]!["stage_coach"]!["store"]!.AsObject().Remove("shard_hero_recruit");
        var offer = new JsonObject { ["quirks"] = new JsonObject { ["shard_hungry"] = new JsonObject() } };
        StagecoachHeroSaveEditor.AddEventCandidate(direct["persist.town.json"], direct["persist.roster.json"],
            direct["persist.upgrades.json"], direct["persist.town_event.json"], offer, []);
        Assert(direct["persist.town_event.json"]["base_root"]!["bonus_hero_entries"]!["100"] is JsonObject,
            "Event-owned candidates do not route through a shop.");

        var identities = Copies();
        var savedPrefix = new string('x', NativeInventoryIdentity.MaximumSaveBytes);
        identities["persist.estate.json"]["base_root"]!["trinkets"]!["items"]!["0"]!["id"] = savedPrefix + "storage";
        identities["persist.estate.json"]["base_root"]!["trinkets"]!["items"]!["1"]!["id"] = "old_owned\0suffix";
        identities["persist.quest.json"]["base_root"]!["quests"]!["0"]!["completion_reward"]!["items_definition"]!["items"]!["0"]!["id"] = savedPrefix + "quest";
        TownEventSaveEditor.ReplaceCurrent(identities, catalog.Events.Single(item => item.Id == "test_trinket_retention_add_from_storage"),
            new TownEventInitializer(f.Content, 7).Initialize);
        Assert(identities["persist.estate.json"]["base_root"]!["trinkets"]!["items"]!.AsObject().ContainsKey("0") &&
            identities["persist.quest.json"]["base_root"]!["trinket_retention_ids"]!["items"]!["0"]!["id"]!.GetValue<string>() == "old_owned",
            "Theft and quest protection use bounded saved inventory identities, including NUL aliases.");
        var retainedBeforeReplacement = identities["persist.quest.json"].DeepClone();
        var storageBeforeReplacement = identities["persist.estate.json"].DeepClone();
        TownEventSaveEditor.ReplaceCurrent(identities, catalog.Events.Single(item => item.Id == "test_free_activity"),
            new TownEventInitializer(f.Content, 8).Initialize);
        Assert(JsonNode.DeepEquals(retainedBeforeReplacement, identities["persist.quest.json"]) &&
            JsonNode.DeepEquals(storageBeforeReplacement, identities["persist.estate.json"]),
            "Already stolen trinkets remain recoverable after replacing the event without returning or deleting them.");
        var currency = Copies();
        currency["persist.estate.json"]["base_root"]!["wallet"]!["0"]!["type"] = "gold\0suffix";
        TownEventSaveEditor.ReplaceCurrent(currency, catalog.Events.Single(item => item.Id == "test_bonus_currency"),
            new TownEventInitializer(f.Content, 7).Initialize);
        Assert(currency["persist.estate.json"]["base_root"]!["wallet"]!.AsObject().Count == 1 &&
            currency["persist.estate.json"]["base_root"]!["wallet"]!["0"]!["amount"]!.GetValue<int>() == 125,
            "Currency grants match native saved identities without adding an alias wallet entry.");
        foreach (var (amount, expectedHp) in new[] { (.5, 60.0), (-2.0, 0.0) })
        {
            var revival = Copies();
            var hero = revival["persist.roster.json"]["base_root"]!["heroes"]!["2"]!["hero_file_data"]!["raw_data"]!["base_root"]!;
            hero["actor"]!["buff_group"] = new JsonObject { ["0"] = new JsonObject
            { ["stat_sub_type"] = "max_hp", ["stat_type"] = 3, ["rule"] = 0, ["is_false_rule"] = false, ["amount"] = amount } };
            TownEventSaveEditor.ReplaceCurrent(revival, catalog.Events.Single(item => item.Id == "test_dead_recruit"),
                new TownEventInitializer(f.Content, 7).Initialize);
            Assert(hero["actor"]!["current_hp"]!.GetValue<double>() == expectedHp && hero["armour_rank"]!.GetValue<int>() == 0 &&
                hero["actor"]!["buff_group"]!.AsObject().Count == 0 && hero["roster.status"]!.GetValue<int>() == 3,
                "Revival computes clamped HP with old equipment/buffs before clearing them, without recruiting the dead hero.");
        }
        var repeatedRevival = Copies();
        var deadRoster = repeatedRevival["persist.roster.json"]["base_root"]!["heroes"]!.AsObject();
        deadRoster["3"] = deadRoster["2"]!.DeepClone();
        deadRoster["4"] = deadRoster["2"]!.DeepClone();
        TownEventSaveEditor.ReplaceCurrent(repeatedRevival, selected with { Id = "multiple_revival_rows",
            Effects = Enumerable.Repeat(new TownEventEffect("dead_recruit", "", 1), 4).ToArray() },
            new TownEventInitializer(f.Content, 0).Initialize);
        var revivalIds = repeatedRevival["persist.town_event.json"]["base_root"]!["dead_hero_entries"]!.AsArray()
            .Select(value => value!.GetValue<int>()).ToArray();
        Assert(revivalIds.Length == 4 && revivalIds.Take(3).Distinct().Count() == 3 && revivalIds.Take(3).Contains(revivalIds[3]),
            "Composite revival rows share the draw pool, then refill it only after exhaustion.");

        var prepared = await f.Service.PrepareTownEventEditAsync(f.Content.Profile, selected, f.Content);
        var originals = f.Saves.Keys.ToDictionary(name => name, name => File.ReadAllBytes(Path.Combine(f.Content.Profile.ProfileDirectory, name)));
        async Task MustReject(Func<Task> action, string reason)
        {
            var rejected = false;
            try { await action(); } catch (InvalidOperationException) { rejected = true; }
            Assert(rejected, reason);
        }
        var text = File.ReadAllText(f.EventsPath);
        File.AppendAllText(f.EventsPath, " ");
        await MustReject(() => f.Service.CommitAsync(prepared), "Resource change after preview blocks the event write.");
        File.WriteAllText(f.EventsPath, text);
        foreach (var name in new[] { "persist.game.json", "persist.town_event.json", "persist.quest.json", "persist.progression.json" })
        {
            var path = Path.Combine(f.Content.Profile.ProfileDirectory, name);
            File.AppendAllText(path, " ");
            await MustReject(() => f.Service.CommitAsync(prepared), name + " changed after preview must block writing.");
            File.WriteAllBytes(path, originals[name]);
        }
        Assert(originals.All(pair => File.ReadAllBytes(Path.Combine(f.Content.Profile.ProfileDirectory, pair.Key)).SequenceEqual(pair.Value)),
            "Rejected writes leave all other saves intact.");

        var codec = DsonSaveCodec.CreateNative();
        var raidGame = f.Saves["persist.game.json"].DeepClone().AsObject();
        raidGame["base_root"]!["inraid"] = true;
        raidGame["base_root"]!["raiddungeon"] = "weald";
        await codec.EncodeAsync(WriteMultiMash(root, "raid-game.json", raidGame.ToJsonString()),
            Path.Combine(f.Content.Profile.ProfileDirectory, "persist.game.json"), null);
        var raid = await ActiveContentResolver.ResolveAsync(f.Content.Profile, f.Content.Resolution!.GameDirectory, null, codec, Path.Combine(root, "raid-resolve"));
        await MustReject(() => f.Service.PrepareTownEventEditAsync(raid.Profile, selected, raid), "Event preview is town-only.");
        var heroes = HeroClassCatalog.Load(raid);
        var generated = StagecoachHeroCandidateFactory.Generate(heroes, heroes.HeroClasses.Single(item => item.Id == "progression"), 7, 0, []);
        await MustReject(() => f.Service.PrepareStagecoachHeroEditAsync(raid.Profile, generated, heroes, raid), "Hero generation is town-only.");
        File.WriteAllBytes(Path.Combine(f.Content.Profile.ProfileDirectory, "persist.game.json"), originals["persist.game.json"]);
        // A leftover raid file is not the scene: the canonical game fields are.
        File.WriteAllText(f.Content.Profile.RaidSavePath, "leftover raid file");
        _ = await f.Service.PrepareStagecoachHeroEditAsync(f.Content.Profile, generated, heroes, f.Content);
        File.WriteAllText(f.EventsPath, """
            {"events":[{"id":"Az","data":[{"type":"free_activity","string_data":"abbey"}]},
                       {"id":"BE","data":[{"type":"bonus_currency","string_data":"gold","number_data":500}]}]}
            """);
        var collision = TownEventCatalog.Load(f.Content);
        Assert(collision.Events.Count == 1 && collision.Events[0].Id == "Az" &&
            collision.Events[0].Effects[0].Type == "free_activity" && !collision.Events[0].HasProviderConflict,
            "Event lookup selects the first native hash even for different colliding names.");
        File.WriteAllText(f.EventsPath, """{"events":[{"id":"healthy_prefix"},""" );
        var malformed = TownEventCatalog.Load(f.Content);
        Assert(malformed.Events.Count == 0 && malformed.Issues.Count > 0, "A malformed event file must not leak a parsed prefix.");
        File.WriteAllText(f.EventsPath, text);
        Console.WriteLine("PASS: repeated replacement, pending effects, saved identities, revival HP, town-only operations and stale save/resource guards.");
    }

    private static async Task VerifyEventQuestDependenciesAsync(string root)
    {
        var f = await BuildTownEventFixtureAsync(root);
        var game = f.Content.Resolution!.GameDirectory;
        var path = Path.Combine(game, "campaign", "quest", "event.quest.plot_quests.json");
        var template = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var definition = template["plot_quests"]![0]!.AsObject();
        var effect = new TownEventDefinition("dependency_event", [new("plot_quest", "event_plot", 1)], "base", path, false);
        WriteMultiMash(game, "trinkets/test.rarities.trinkets.json", """{"rarities":[{"id":"event_rarity"},{"id":"common"}]}""");
        WriteMultiMash(game, "campaign/progression/infestation.progression.json", """{"infestation":{"sequence_elements":[{"id":"before"},{"id":"after"}]}}""");
        Dictionary<string, JsonObject> Apply(Action<Dictionary<string, JsonObject>>? arrange = null)
        {
            File.WriteAllText(path, template.ToJsonString());
            var saves = f.Saves.ToDictionary(pair => pair.Key, pair => pair.Value.DeepClone().AsObject());
            arrange?.Invoke(saves);
            TownEventSaveEditor.ReplaceCurrent(saves, effect, new TownEventInitializer(f.Content, 7).Initialize);
            return saves;
        }
        int Count(Dictionary<string, JsonObject> saves) => saves["persist.quest.json"]["base_root"]!["quests"]!.AsObject().Count;
        // Native quest generation reads Roster.highest_resolve_xp (+0x18)
        // at 0x14057661C, rather than recomputing it from current heroes.
        definition["highest_hero_resolve_level_dependency"] = 2;
        Assert(Count(Apply(saves => saves["persist.roster.json"]["base_root"]!["highest_resolve_xp"] = 20)) == 2,
            "The saved highest resolve XP satisfies a plot dependency even when current heroes have lower XP.");
        Assert(Count(Apply(saves => saves["persist.roster.json"]["base_root"]!["highest_resolve_xp"] = 10)) == 1,
            "The plot remains unavailable when the saved highest resolve XP is below its dependency.");
        definition.Remove("highest_hero_resolve_level_dependency");
        definition["infestation_sequence_element_dependency"] = "before";
        void Infestation(Dictionary<string, JsonObject> saves, string id) => saves["persist.progression.json"]["base_root"]!["infestation"] =
            new JsonObject { ["sequence_element_id"] = unchecked((int)HashLoc2Key(id)) };
        Assert(Count(Apply(saves => Infestation(saves, "before"))) == 1 && Count(Apply(saves => Infestation(saves, "after"))) == 2,
            "Infestation dependencies require strictly later sequence progress.");
        definition.Remove("infestation_sequence_element_dependency");
        definition["estate_inventory_dependency"] = JsonNode.Parse("""{"consume_on_attempt":false,"items_definition":{"items":{"0":{"id":"keep","type":"estate","amount":8}}}}""");
        Assert(Count(Apply()) == 1, "Missing estate inventory blocks quest generation without consuming items.");
        definition["estate_inventory_dependency"]!["items_definition"]!["items"]!["0"]!["amount"] = 7;
        var inventoryResult = Apply();
        Assert(Count(inventoryResult) == 2 && JsonNode.DeepEquals(inventoryResult["persist.estate.json"], f.Saves["persist.estate.json"]),
            "An eligible inventory dependency is not consumed during generation.");
        definition.Remove("estate_inventory_dependency");
        definition["trinket_retention_count"] = 2;
        definition["trinket_retention_minimum_rarity"] = "event_rarity";
        void Retained(Dictionary<string, JsonObject> saves, params string[] ids) => saves["persist.quest.json"]["base_root"]!["trinket_retention_ids"] =
            new JsonObject { ["items"] = new JsonObject(ids.Select((id, index) =>
                KeyValuePair.Create<string, JsonNode?>(index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    new JsonObject { ["id"] = id, ["type"] = "trinket", ["amount"] = 1 }))) };
        Assert(Count(Apply(saves => Retained(saves, "old_owned", "protected"))) == 1, "Enough low-rarity trinkets alone cannot satisfy the rarity predicate.");
        var retention = Apply(saves =>
        {
            Retained(saves, "old_owned", "quest_prize");
            // A retained death memento carries the original owner's name.
            saves["persist.quest.json"]["base_root"]!["trinket_retention_ids"]!["items"]!["1"]!["hero_name"] = "Reynauld";
        });
        var rewards = retention["persist.quest.json"]["base_root"]!["quests"]!["1"]!["completion_reward"]!["trinket_retention_ids"]!["items"]!;
        Assert(rewards["0"]!["id"]!.GetValue<string>() == "quest_prize" && rewards.AsObject().Count == 2,
            "Recovery rewards use retained identities in rarity order.");
        Assert(!retention["persist.quest.json"]["base_root"]!["quests"]!["1"]!["completion_reward"]!["items_definition"]!["items"]!
            .AsObject().Any(pair => pair.Value?["id"]?.GetValue<string>() == "quest_prize"),
            "A limited trinket in the retained inventory must not be duplicated as an ordinary additional quest reward.");
        Assert(retention["persist.quest.json"]["base_root"]!["trinket_retention_ids"]!["items"]!.AsObject().Count == 2,
            "Preparing recovery rewards does not consume retained items before quest settlement.");
        var originalRetained = retention["persist.quest.json"]["base_root"]!["trinket_retention_ids"]!["items"]!["1"]!;
        Assert(JsonNode.DeepEquals(rewards["0"], originalRetained) && !ReferenceEquals(rewards["0"], originalRetained),
            "A recovery reward is an independent copy of the retained instance, preserving its saved owner and item state.");
        var roundtrip = Path.Combine(root, "retention-roundtrip");
        Directory.CreateDirectory(roundtrip);
        var restored = await RoundtripDirectoryQueryAsync(roundtrip, "quest", retention["persist.quest.json"], DsonSaveCodec.CreateNative());
        Assert(JsonNode.DeepEquals(retention["persist.quest.json"], restored), "Recovery rewards and top-level retained items both use native inventory objects through DSON roundtrip.");
        Console.WriteLine("PASS: plot quest infestation/inventory/rarity predicates and retained reward DSON shape.");
    }

    private static async Task VerifyOrdinaryEventRecruitRoutingAsync(string root)
    {
        // Synthetic routing boundary, not an installed Mod or a vanilla
        // configuration: ordinary native upgrades do not force shard_hungry.
        var f = await BuildTownEventFixtureAsync(root);
        var game = f.Content.Resolution!.GameDirectory;
        var quirksPath = Path.Combine(game, "shared", "quirk", "event.quirk_library.json");
        var quirks = JsonNode.Parse(File.ReadAllText(quirksPath))!;
        quirks["quirks"]!.AsArray().Add(JsonNode.Parse("""{"id":"shard_hungry","is_positive":false,"random_chance":0,"buffs":[]}"""));
        File.WriteAllText(quirksPath, quirks.ToJsonString());
        var stagecoachPath = Path.Combine(game, "campaign", "town", "buildings", "stage_coach", "stage_coach.building.json");
        var stagecoach = JsonNode.Parse(File.ReadAllText(stagecoachPath))!;
        stagecoach["data"]!["stores"]![0]!["data"]!["upgraded_recruits_upgrades"]![0]!["starting_quirks"] = new JsonArray("shard_hungry");
        File.WriteAllText(stagecoachPath, stagecoach.ToJsonString());
        var saves = f.Saves.ToDictionary(pair => pair.Key, pair => pair.Value.DeepClone().AsObject());
        saves["persist.town.json"]["base_root"]!["buildings"]!["stage_coach"]!["store"]!.AsObject().Remove("shard_hero_recruit");
        var selected = TownEventCatalog.Load(f.Content).Events.Single(item => item.Id == "test_stage_coach_bonus_recruits");
        TownEventSaveEditor.ReplaceCurrent(saves, selected, new TownEventInitializer(f.Content, 7).Initialize);
        var store = saves["persist.town.json"]["base_root"]!["buildings"]!["stage_coach"]!["store"]!;
        var generated = store["hero_recruit"]!["generated"]!.AsObject();
        Assert(generated.Count == 3 && generated["100"]!["quirks"]!["shard_hungry"] is JsonObject &&
            generated["101"]!["quirks"]!["shard_hungry"] is JsonObject && store["shard_hero_recruit"] is null,
            "Ordinary event bonuses stay in the ordinary shop even when upgrades force shard_hungry and no shard shop exists.");
        var manual = StagecoachHeroSaveEditor.AddCandidate(f.Saves["persist.town.json"], f.Saves["persist.roster.json"],
            f.Saves["persist.upgrades.json"], generated["100"]!.AsObject(), []);
        Assert(manual.Preview.TargetPool == StagecoachRecruitPool.Shard,
            "Manual generation keeps its existing quirk-based routing.");
        Console.WriteLine("PASS: ordinary event recruit routing is independent of forced shard quirks; manual routing is preserved.");
    }

    private static async Task VerifyEventRecruitSingletonScopeAsync(string root)
    {
        var f = await BuildTownEventFixtureAsync(root);
        var game = f.Content.Resolution!.GameDirectory;
        var quirksPath = Path.Combine(game, "shared", "quirk", "event.quirk_library.json");
        var quirks = JsonNode.Parse(File.ReadAllText(quirksPath))!;
        quirks["quirks"]![0]!["tags"] = new JsonArray("singleton");
        File.WriteAllText(quirksPath, quirks.ToJsonString());
        File.AppendAllText(Path.Combine(game, "heroes", "progression", "progression.info.darkest"),
            "\ngeneration: .number_of_positive_quirks_min 1 .number_of_positive_quirks_max 1\n");
        var selected = TownEventCatalog.Load(f.Content).Events.Single(item => item.Id == "test_bonus_recruit");

        // This isolates the native scope of GetQuirksToExclude (0x1405C6D90):
        // roster + coach candidates, not unclaimed event offers. The single
        // eligible quirk makes the assertions independent of a random seed.
        JsonObject Offers(Action<Dictionary<string, JsonObject>>? arrange = null)
        {
            var saves = f.Saves.ToDictionary(pair => pair.Key, pair => pair.Value.DeepClone().AsObject());
            arrange?.Invoke(saves);
            TownEventSaveEditor.ReplaceCurrent(saves, selected, new TownEventInitializer(f.Content, 7).Initialize);
            return saves["persist.town_event.json"]["base_root"]!["bonus_hero_entries"]!.AsObject();
        }
        bool HasQuirk(JsonNode? candidate) => candidate?["quirks"]?["event_positive"] is JsonObject;
        var offers = Offers();
        Assert(offers.Count == 2 && offers.All(pair => HasQuirk(pair.Value)),
            "An earlier event offer does not exclude a singleton from the next native recruit draw.");
        offers = Offers(saves => saves["persist.town.json"]["base_root"]!["buildings"]!["stage_coach"]!["store"]!
            ["hero_recruit"]!["generated"]!["50"]!["quirks"]!["event_positive"] = new JsonObject());
        Assert(offers.Count == 2 && offers.All(pair => !HasQuirk(pair.Value)),
            "An existing coach candidate still excludes its singleton from event recruit draws.");
        offers = Offers(saves => saves["persist.roster.json"]["base_root"]!["heroes"]!["1"]!
            ["hero_file_data"]!["raw_data"]!["base_root"]!["quirks"]!["event_positive"] = new JsonObject());
        Assert(offers.Count == 2 && offers.All(pair => !HasQuirk(pair.Value)),
            "An existing roster hero still excludes its singleton from event recruit draws.");
        Console.WriteLine("PASS: native event recruit singleton exclusions use the roster and stagecoach, not event offers.");
    }
}
