using DarkestDungeonSaveEditor.Core.Dson;

internal static partial class ContractSuite
{
    public static async Task RunOfficialModUpdateOnlyAsync(string repositoryRoot)
    {
        var root = Path.Combine(repositoryRoot, "artifacts", "official-mod-update-contracts", Guid.NewGuid().ToString("N"));
        await RunOfficialModUpdateContractsAsync(root);
        Console.WriteLine($"Artifacts: {root}");
    }

    private static async Task RunOfficialModUpdateContractsAsync(string root)
    {
        await VerifyPaidBuffAndSpecificRecruitsAsync(Path.Combine(root, "events"));
        await VerifyModSkillFlagsAsync(Path.Combine(root, "skills"));
        await VerifyEventForcedSkillUnlocksAsync(Path.Combine(root, "event-skills"));
        await VerifyOrdinaryNativeSkillSelectionAsync(Path.Combine(root, "ordinary-skills"));
        await VerifyOrdinaryEventSkillPurchasesAsync(Path.Combine(root, "ordinary-event-skills"));
        await VerifyModEvolutionPeriodsAsync(Path.Combine(root, "quirks"));
        VerifyDifficultyPropAdditions(Path.Combine(root, "props"));
        VerifyEffectLootPaths(Path.Combine(root, "loot"));
        VerifyNativeTrinketTriggerReferences(Path.Combine(root, "trinket-triggers"));
        VerifyNativeLootBuffSources(Path.Combine(root, "buff-sources"));
        VerifyNativeLootRuntimeSources(Path.Combine(root, "runtime-sources"));
        VerifyNativeBuildingQueries(Path.Combine(root, "building-queries"));
        VerifyDuplicateQuirkSources(Path.Combine(root, "duplicate-quirk-sources"));
        await VerifyDuplicateRecruitQuirksAsync(Path.Combine(root, "duplicate-recruit-quirks"));
        await VerifyEffectReferenceChainsAsync(Path.Combine(root, "reference-chains"));
        VerifyDazeFieldPreservation();
    }

    private static async Task VerifyPaidBuffAndSpecificRecruitsAsync(string root)
    {
        var f = await BuildTownEventFixtureAsync(root);
        foreach (var (texture, buff, cost, healing, expected) in new[]
        {
            ("claim", true, true, false, true), ("", true, true, false, false),
            ("\0claim", true, true, false, false), (" ", true, true, false, true),
            ("claim", false, true, false, false), ("claim", true, false, false, false),
            (new string('a', 62) + "界", true, true, false, true),
            ("", false, false, true, true)
        })
        {
            var data = new JsonArray();
            void Add(string type, string text = "", double number = 0) => data.Add(new JsonObject
                { ["type"] = type, ["string_data"] = text, ["number_data"] = number });
            if (buff) Add("embark_party_buff", "event_buff", 1);
            if (cost) Add("event_cost", "gold", 0); // Zero cost still enables the interaction.
            if (healing) Add("roster_stress_heal", "", 20);
            File.WriteAllText(f.EventsPath, new JsonObject { ["events"] = new JsonArray(new JsonObject
                { ["id"] = "paid", ["interaction_texture_id"] = texture, ["data"] = data }) }.ToJsonString());
            var definition = TownEventCatalog.Load(f.Content).Events.Single();
            var saves = f.Saves.ToDictionary(p => p.Key, p => p.Value.DeepClone().AsObject());
            TownEventSaveEditor.ReplaceCurrent(saves, definition, new TownEventInitializer(f.Content, 1).Initialize);
            Assert(saves["persist.town_event.json"]["base_root"]!["has_unclaimed_interaction"]!.GetValue<bool>() == expected,
                "Paid buff interaction matches the native three-way gate and existing healing interactions.");
            Assert(JsonNode.DeepEquals(saves["persist.estate.json"], f.Saves["persist.estate.json"]), "Initializing an offer must not pay its cost.");
            var restored = await RoundtripDirectoryQueryAsync(root, "paid-" + Guid.NewGuid().ToString("N"), saves["persist.town_event.json"], DsonSaveCodec.CreateNative());
            Assert(JsonNode.DeepEquals(restored, saves["persist.town_event.json"]), "Pending paid interaction persists.");
        }
        File.WriteAllText(f.EventsPath, """{"events":[{"id":"specific","data":[{"type":"specific_recruit","string_data":"progression","number_data":0},{"type":"specific_recruit","string_data":"progression","number_data":4.75}]}]}""");
        var selected = TownEventCatalog.Load(f.Content).Events.Single();
        Assert(TownEventAvailability.UnavailableReason(selected).Length == 0, "specific_recruit is a supported native effect.");
        var changed = f.Saves.ToDictionary(p => p.Key, p => p.Value.DeepClone().AsObject());
        TownEventSaveEditor.ReplaceCurrent(changed, selected, new TownEventInitializer(f.Content, 7).Initialize);
        var offers = changed["persist.town_event.json"]["base_root"]!["bonus_hero_entries"]!.AsObject().Select(p => p.Value!).ToArray();
        Assert(offers.Length == 2 && offers.Select(h => h["resolveXp"]!.GetValue<int>()).Order().SequenceEqual([0, 40]) &&
            offers.All(h => h["heroClass"]!.GetValue<string>() == "progression" && h["is_from_town_event"]!.GetValue<bool>()),
            "Each specific_recruit effect creates exactly one event-owned hero at its requested level.");
        var heroes = HeroClassCatalog.Load(f.Content);
        var source = heroes.HeroClasses.Single(h => h.Id == "progression").RecruitEvents;
        Assert(source.Count == 2 && source.All(e => e.Count == 1) && source.Select(e => e.ResolveLevel).Order().SequenceEqual(new int?[] { 0, 4 }),
            "Recruit-source metadata must distinguish level from count.");
        foreach (var pair in changed)
        {
            var restored = await RoundtripDirectoryQueryAsync(root, "specific-" + pair.Key, pair.Value, DsonSaveCodec.CreateNative());
            Assert(JsonNode.DeepEquals(restored, pair.Value), "Specific recruits, purchases and event state survive DSON persistence.");
        }
        Console.WriteLine("PASS: paid-buff interaction matrix and mixed-level specific recruits with persistence.");
    }

    private static async Task VerifyModSkillFlagsAsync(string root)
    {
        var f = HeroSelectionContent(root, "local", 7, target: 2);
        SelectionResource(f, "heroes/selection/selection_A/skin.png", "fixture");
        var info = Path.Combine(f.Source, "heroes/selection/selection.info.darkest");
        File.WriteAllText(info, File.ReadAllText(info).Replace(".id s0 .level 0", ".id s0 .level 0 .force_selection true")
            .Replace(".id s1 .level 0", ".id s1 .level 0 .force_selection true .counts_towards_skill_limit false")
            .Replace(".id s2 .level 0", ".id s2 .level 0 .skill_visible false")
            .Replace(".id s3 .level 0", ".id s3 .level 0 .skill_visible false")
            .Replace(".number_of_selected_combat_skills_max 4", ".number_of_selected_combat_skills_max 2"));
        File.AppendAllText(info, "\ncombat_skill: .id s2 .level 1\ncombat_skill: .id s2 .level 2\ncombat_skill: .id s2 .level 3\n");
        SelectionResource(f, "upgrades/selection.upgrades.json", """{"trees":[{"id":"selection.s0","requirements":[{"code":"0","prerequisite_resolve_level":99}]},{"id":"selection.s2","requirements":[{"code":"0","prerequisite_resolve_level":99},{"code":"1","prerequisite_resolve_level":0},{"code":"3","prerequisite_resolve_level":0}]}]}""");
        var catalog = HeroClassCatalog.Load(f.Content);
        for (var seed = 0; seed < 64; seed++)
        {
            var generated = GenerateSelectionHero(catalog, seed);
            var equipped = generated.Preview.CombatSkills;
            Assert(generated.InitiallyUnlockedCombatSkills!.Count == 5,
                "Random unlocks remain distinct from the smaller equipment set after forced selection.");
            Assert(generated.UpgradePurchases.Where(p => p.TreeId == "selection.s2").Select(p => p.RequirementCode).SequenceEqual(["0", "1"]),
                "Hidden base unlock ignores resolve requirements, and missing numeric codes stop later auto-upgrades.");
            Assert(equipped.Contains("s0") && equipped.Contains("s1") && equipped.Contains("s2") && !equipped.Contains("s3") &&
                equipped.Count(id => id is not ("s1" or "s2")) == 2,
                "Forced visible and authored hidden skills are equipped, without consuming exempt slots; tree-less hidden skills remain locked.");
            if (seed == 0) await VerifyHeroSelectionPersistenceAsync(root, generated, DsonSaveCodec.CreateNative());
        }
        var beforeZeroDraw = File.ReadAllText(info);
        File.AppendAllText(info, "\ngeneration: .number_of_random_combat_skills 0\ncombat_skill: .id s4 .level 0 .generation_guaranteed true\n");
        var guaranteedAtZero = GenerateSelectionHero(HeroClassCatalog.Load(f.Content), 42);
        Assert(guaranteedAtZero.Preview.CombatSkills.Contains("s4") && guaranteedAtZero.InitiallyUnlockedCombatSkills!.Contains("s4"),
            "A zero random-count request still unlocks one eligible generation-guaranteed skill.");
        File.WriteAllText(info, beforeZeroDraw);
        SelectionResource(f, "heroes/selection/selection.override.darkest", "combat_skill: .id s1 .level 0 .force_selection false .counts_towards_skill_limit true\ncombat_skill: .id s3 .level 0 .skill_visible true\n");
        var overridden = HeroClassCatalog.Load(f.Content).HeroClasses.Single();
        Assert(overridden.CombatSkillSelection["s1"] is { ForceSelection: false, CountsTowardsLimit: true } &&
            overridden.CombatSkillSelection["s3"].Visible && !overridden.CombatSkillSelection["s2"].Visible,
            "Partial overrides clear explicit flags and retain omitted flags.");
        Console.WriteLine("PASS: native skill visibility, authored hidden unlocks, forced equipment, slot counts and partial overrides.");
    }

    private static async Task VerifyEventForcedSkillUnlocksAsync(string root)
    {
        var f = await BuildTownEventFixtureAsync(root);
        var source = f.Content.Sources.Single(s => s.Kind == "base").Directory;
        var info = Path.Combine(source, "heroes/progression/progression.info.darkest");
        File.AppendAllText(info, "\nskill_selection: .can_select_combat_skills true .number_of_selected_combat_skills_max 2\n" +
            "generation: .number_of_random_combat_skills 2\ncombat_skill: .id attack .level 0 .force_selection true\n" +
            "combat_skill: .id ordinary_a .level 0\ncombat_skill: .id ordinary_b .level 0\n");
        var upgradePath = Path.Combine(source, "upgrades/heroes/a.upgrades.json");
        var upgrades = JsonNode.Parse(File.ReadAllText(upgradePath))!.AsObject();
        upgrades["trees"]!.AsArray().Single(node => node!["id"]!.GetValue<string>() == "progression.attack")!
            ["requirements"]![0]!["prerequisite_resolve_level"] = 99;
        File.WriteAllText(upgradePath, upgrades.ToJsonString());
        File.WriteAllText(f.EventsPath, """{"events":[{"id":"forced","data":[{"type":"specific_recruit","string_data":"progression","number_data":0}]}]}""");
        var definition = TownEventCatalog.Load(f.Content).Events.Single();
        var saves = f.Saves.ToDictionary(pair => pair.Key, pair => pair.Value.DeepClone().AsObject());
        TownEventSaveEditor.ReplaceCurrent(saves, definition, new TownEventInitializer(f.Content, 7).Initialize);
        var offer = saves["persist.town_event.json"]["base_root"]!["bonus_hero_entries"]!.AsObject().Single();
        var equipped = offer.Value!["skills"]!["selected_combat_skills"]!.AsObject();
        Assert(equipped.Count == 2 && equipped.ContainsKey("attack"), "Forced visible base unlock ignores its resolve prerequisite and consumes one slot.");
        var purchases = saves["persist.upgrades.json"]["base_root"]!["purchases"]!.AsObject()
            .Where(pair => pair.Value!["instance_number"]!.GetValue<int>() == int.Parse(offer.Key))
            .Select(pair => pair.Value!["tree_id"]!.GetValue<int>()).ToHashSet();
        Assert(new[] { "progression.attack", "progression.ordinary_a", "progression.ordinary_b" }
            .All(id => purchases.Contains(unchecked((int)HashLoc2Key(id)))),
            "A level-zero event recruit retains both random unlocks even when only one can be equipped alongside a forced skill.");
        foreach (var name in new[] { "town_event", "upgrades" })
        {
            var value = saves["persist." + name + ".json"];
            Assert(JsonNode.DeepEquals(value, await RoundtripDirectoryQueryAsync(root, "forced-" + name, value, DsonSaveCodec.CreateNative())),
                "Forced skills and unselected random unlocks survive DSON persistence.");
        }
        Console.WriteLine("PASS: forced base unlock prerequisites and event purchase retention beyond equipped slots.");
    }

    private static async Task VerifyModEvolutionPeriodsAsync(string root)
    {
        var f = QuirkRuleContent(root, "local");
        foreach (var (value, expected) in new[] { ("\"combat_end\"", HeroQuirkEvolutionDurationType.CombatEnd),
                     ("\"quest_end\"", HeroQuirkEvolutionDurationType.QuestEnd), ("\"quest_end\\u0000ignored\"", HeroQuirkEvolutionDurationType.QuestEnd),
                     ("\"turns\"", HeroQuirkEvolutionDurationType.Turns), ("null", HeroQuirkEvolutionDurationType.Turns),
                     ("\"unsupported\"", HeroQuirkEvolutionDurationType.Turns), ("3", HeroQuirkEvolutionDurationType.Turns) })
        {
            File.WriteAllText(f.Quirks, QuirkEvolutionLibrary("\"evolution_duration_min\":3,\"evolution_duration_max\":3,\"evolution_class_id\":\"p1\",\"evolution_duration_type\":" + value));
            var catalog = HeroClassCatalog.Load(f.Content);
            Assert(catalog.InitialQuirks.Single(q => q.Id == "p0").Evolution!.DurationType == expected,
                "Evolution period follows the native accepted enums and ignored-value default.");
            var candidate = GenerateQuirkRuleHero(catalog, "p0");
            var state = candidate.Candidate["quirks"]!["p0"]!.AsObject();
            Assert(state["evolution_duration_remaining"]!.GetValue<int>() == 3 && !state.ContainsKey("evolution_duration_type"),
                "The period belongs to content, while the instance retains the existing counter schema.");
            var output = Path.Combine(root, expected.ToString(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(output);
            await VerifyProgressionCandidatePersistenceAsync(output, candidate, DsonSaveCodec.CreateNative());
        }
        Console.WriteLine("PASS: quirk evolution periods, invalid-value fallback and unchanged instance schema.");
    }

    private static void VerifyDifficultyPropAdditions(string root)
    {
        var source = Path.Combine(root, "base");
        WriteMultiMash(source, "dungeons/cove/cove.props.darkest", "traps: .chance 1 .types alpha\n");
        var added = WriteMultiMash(source, "dungeons/cove/a.cove.1.props.darkest", "traps: .chance 3 .types beta\n");
        WriteMultiMash(source, "dungeons/cove/b.cove.3.props.darkest", "traps: .chance 1 .types gamma\n");
        WriteMultiMash(source, "props/prop_definitions.json", """{"props":[{"name":"trap","default_data":{"instance_type":"trap"}}]}""");
        WriteMultiMash(source, "props/trap_definitions.json", """{"props":[{"name":"alpha","default_data":{"inherits_from":{"prop_type_name":"trap"}}},{"name":"beta","default_data":{"inherits_from":{"prop_type_name":"trap"}}},{"name":"gamma","default_data":{"inherits_from":{"prop_type_name":"trap"}}}]}""");
        var content = QueryContent(root, [new("base", "Base", "base", source, 0)]);
        var one = BattleRoomAttachmentCatalog.Load(content, "cove", 1);
        var three = BattleRoomAttachmentCatalog.Load(content, "cove", 3);
        Assert(BattleRoomAttachmentCatalog.Load(content, "cove", 7).Traps.Select(t => t.Id).SequenceEqual(["alpha"]),
            "A difficulty outside native registered keys retains the default pool.");
        Assert(one.Traps.Select(t => t.Id).Order().SequenceEqual(["alpha", "beta"]) &&
            three.Traps.Select(t => t.Id).Order().SequenceEqual(["alpha", "gamma"]), "Each difficulty appends only its own pool to the default pool.");
        Assert(one.SelectRegionalContent(BattleRoomAttachmentKind.Trap, "cove", .24)!.Id == "alpha" &&
            one.SelectRegionalContent(BattleRoomAttachmentKind.Trap, "cove", .25)!.Id == "beta", "Authored weights survive native pool concatenation.");
        Assert(!one.Traps.Single(t => t.Id == "beta").IsAvailableInDungeon("cove", 3), "A previous difficulty selection cannot be applied after a context change.");
        File.WriteAllText(added, "traps: .chance 4 .types beta\n");
        var rejected = false;
        try { BattleRoomAttachmentCatalog.ValidateGuard(one.Guard); } catch (InvalidOperationException) { rejected = true; }
        Assert(rejected, "Changing an additive pool invalidates the old write guard.");
        WriteMultiMash(source, "dungeons/cove/aXcoveY1ZpropsQdarkest", "traps: .chance 1 .types gamma\n");
        Assert(BattleRoomAttachmentCatalog.Load(content, "cove", 1).Traps.Any(t => t.Id == "gamma"),
            "Native additive queries retain their unescaped regex separators.");
        var mod = Path.Combine(root, "local");
        WriteMultiMash(mod, "dungeons/cove/a.cove.1.props.darkest", "traps: .chance 1 .types gamma\n");
        WriteMultiMash(mod, "dungeons/cove/unlisted.cove.1.props.darkest", "traps: .chance 1 .types beta\n");
        WriteMultiMash(mod, "modfiles.txt", "dungeons/cove/a.cove.1.props.darkest\n");
        var modded = QueryContent(root, [new("base", "Base", "base", source, 0), new("local", "Local", "local", mod, 1)]);
        var moddedPool = BattleRoomAttachmentCatalog.Load(modded, "cove", 1);
        Assert(!moddedPool.Traps.Any(t => t.Id == "beta") && moddedPool.Traps.Any(t => t.Id == "alpha"),
            "Same-path Mod additions override their file, retain the base pool, and exclude unlisted files.");
        WriteMultiMash(source, "dungeons/ruins/ruins.props.darkest", "");
        WriteMultiMash(source, "dungeons/ruins/ruins.1.props.darkest", "traps: .chance 1 .types gamma\n");
        Assert(BattleRoomAttachmentCatalog.Load(content, "ruins", 1).GetCandidates(BattleRoomAttachmentKind.Trap, "ruins")
            .Select(t => t.Id).SequenceEqual(["gamma"]), "The native regex matches the full path, including the slash before a prefix-free basename.");
        Console.WriteLine("PASS: difficulty-specific additive pools, weights and stale write guards.");
    }

    private static void VerifyEffectLootPaths(string root)
    {
        var f = HeroSelectionContent(root, "local", 1);
        SelectionResource(f, "inventory/query.inventory.items.darkest", "inventory_item: .type estate .id query_token .base_stack_limit 2\n");
        SelectionResource(f, "loot/query.loot.json", QueryLootJson);
        var effect = SelectionResource(f, "effects/query.effects.darkest", "effect: .name gain .gain_loot query_loot\n");
        var hero = Path.Combine(f.Source, "heroes/selection/selection.info.darkest");
        var original = File.ReadAllText(hero);
        var raid = JsonNode.Parse("""{"base_root":{"party":{"inventory":{"items":{}}}}}""")!.AsObject();
        File.WriteAllText(effect, "effect: .name gain\n");
        var unresolved = f.Content with
        {
            Resolution = new(f.Source, null, f.Source,
                """{"applied_ugcs_1_0":{"0":{"name":"selection","source":"local"},"1":{"name":"missing","source":"Steam"}}}""")
        };
        Assert(QuantityItemCatalog.LoadRaid(unresolved, raid).Items.Single(i => i.ItemId == "query_token").ReferenceStatus ==
            QuantityItemReferenceStatus.SuspectedUnused, "Without gain_loot, an unrelated unresolved Mod retains the legacy quantity policy.");
        File.WriteAllText(effect, "effect: .name gain .gain_loot query_loot\n");
        void Check(bool expected, string label)
        {
            var item = QuantityItemCatalog.LoadRaid(f.Content, raid).Items.Single(i => i.ItemId == "query_token");
            Assert(item.ReferenceStatus == (expected ? QuantityItemReferenceStatus.ConfirmedActive : QuantityItemReferenceStatus.SuspectedUnused) &&
                item.IsHiddenByDefault != expected, label + ": reachability and visibility must agree.");
        }
        Check(false, "An unused effect definition is not a root");
        File.WriteAllText(hero, original + "skill: .id unused .effect gain\n");
        Check(false, "Monster skill syntax is not a hero skill root");
        File.WriteAllText(hero, original + "combat_skill: .id s0 .effect gain\n");
        Check(true, "An active actor skill reaches gain_loot");
        File.AppendAllText(effect, "effect: .name gain .chance 50%\n");
        Check(false, "An omitted duplicate gain_loot field clears the native value");
        File.AppendAllText(effect, "effect: .name gain .gain_loot query_loot\n");
        Check(true, "A later duplicate can restore gain_loot");
        File.AppendAllText(effect, "effect: .name gain .gain_loot \"\"\n");
        Check(false, "An explicit empty field clears the native value");
        File.WriteAllText(effect, "effect: .name gain .gain_loot query_loot\neffect: .name apply .buff_ids trigger\n");
        File.WriteAllText(hero, original);
        var trinket = SelectionResource(f, "trinkets/query.entries.trinkets.json", """{"entries":[{"id":"query","buffs":[],"quest_start_additional_effects":["gain"]}]}""");
        Check(false, "Quest-start gain_loot is outside combat");
        File.WriteAllText(trinket, """{"entries":[{"id":"query","buffs":[],"combat_start_additional_effects":["gain"]}]}""");
        Check(true, "A combat trinket trigger reaches loot");
        File.WriteAllText(trinket, """{"entries":[{"id":"query","buffs":[],"hero_class_requirements":["absent"],"combat_start_additional_effects":["gain"]}]}""");
        Check(false, "Missing hero dependencies discard a trinket before its triggers become roots");
        File.WriteAllText(trinket, """{"entries":[{"id":"query","buffs":["missing"]},{"id":"query","buffs":[],"combat_start_additional_effects":["gain"]}]}""");
        Check(true, "A discarded first duplicate does not shadow a valid later trinket");
        File.WriteAllText(trinket, """{"entries":[]}""");
        SelectionResource(f, "shared/buffs/query.buffs.json", """{"buffs":[{"id":"trigger","attack_additional_effect_ids":["gain"]}]}""");
        var camp = SelectionResource(f, "raid/camping/query.camping_skills.json", """{"skills":[{"id":"camp","hero_classes":["selection"],"effects":[{"type":"effect","sub_type":"gain"}]}]}""");
        Check(false, "Direct camping loot is outside combat");
        File.WriteAllText(camp, """{"skills":[{"id":"camp","hero_classes":["selection"],"effects":[{"type":"effect","sub_type":"apply"}]}]}""");
        Check(true, "A camping effect can apply a later combat-triggered Buff");
        File.WriteAllText(camp, """{"skills":[{"id":"camp","hero_classes":["absent"],"effects":[{"type":"effect","sub_type":"apply"}]}]}""");
        Check(false, "A camping skill with no registered hero is not a root");
        File.WriteAllText(camp, """{"skills":[{"id":"camp","hero_classes":["absent"],"effects":[{"type":"effect","sub_type":"apply"}]},{"id":"camp","hero_classes":["selection"],"effects":[]}]}""");
        Check(true, "A later camping declaration grants access to the first stored effect payload");
        File.WriteAllText(camp, """{"skills":[{"id":"camp","hero_classes":["selection"],"effects":[]},{"id":"camp","hero_classes":["selection"],"effects":[{"type":"effect","sub_type":"apply"}]}]}""");
        Check(false, "A later camping payload cannot replace the first skill");
        var quirk = SelectionResource(f, "shared/quirk/loot.quirk_library.json", """{"quirks":[{"id":"loot_quirk","buffs":["trigger"]}]}""");
        Check(true, "A quirk Buff can trigger combat loot");
        File.WriteAllText(quirk, """{"quirks":[{"id":"loot_quirk","buffs":[]}]}""");
        var monster = SelectionResource(f, "monsters/probe/probe_A/probe_A.info.darkest", "skill: .id hit .effect gain\n");
        Check(true, "A registered canonical monster skill reaches loot");
        File.WriteAllText(monster, "combat_skill: .id hit .effect gain\n");
        Check(false, "Hero skill syntax is not a monster skill root");
        var events = SelectionResource(f, "campaign/town_events/loot.town_events.events.json",
            """{"events":[{"id":"embark","data":[{"type":"embark_party_buff","string_data":"trigger","number_data":1}]}]}""");
        Check(true, "An embark event Buff can trigger combat loot");
        File.WriteAllText(events, """{"events":[{"id":"embark","data":[]},{"id":"embark","data":[{"type":"embark_party_buff","string_data":"trigger","number_data":1}]}]}""");
        Check(false, "A shadowed duplicate event cannot add a Buff root");
        File.WriteAllText(hero, original + "combat_skill: .id s0 .effect apply\n");
        File.WriteAllText(effect, "effect: .name gain .gain_loot query_loot\neffect: .name apply .buff_ids " +
            string.Join(' ', Enumerable.Repeat("absent", 7)) + " trigger\n");
        Check(true, "The eighth Buff reference is consumed");
        File.WriteAllText(effect, "effect: .name gain .gain_loot query_loot\neffect: .name apply .buff_ids " +
            string.Join(' ', Enumerable.Repeat("absent", 8)) + " trigger\n");
        Check(false, "The ninth Buff reference is outside the native buffer");
        File.AppendAllText(effect, "effect: .name apply .buff_ids trigger\n");
        Check(true, "A repeated effect appends its own bounded Buff list");
        File.WriteAllText(hero, original + "combat_skill: .id s0 .effect gain\n");
        SelectionResource(f, "effects/unreadable.effects.darkest", "effect: .name another\n");
        File.Delete(Path.Combine(f.Source, "effects/unreadable.effects.darkest"));
        Assert(QuantityItemCatalog.LoadRaid(f.Content, raid).Items.Single(i => i.ItemId == "query_token").ReferenceStatus ==
            QuantityItemReferenceStatus.AnalysisIncomplete, "An unseen winning effect definition cannot certify reachability.");
        Console.WriteLine("PASS: effect-to-loot reachability, duplicate resets, bounded Buff append, trinket triggers and camping context.");
    }

    private static void VerifyDazeFieldPreservation()
    {
        foreach (var deferred in new byte[] { 0, 1 })
        {
            var bytes = NativeScalarFixture(("is_daze_deferred", new[] { deferred }, false), ("other_value", NativeInt(5), true));
            var original = DsonReader.Read(bytes, default);
            var json = original.ToJson(default);
            Assert(json["base_root"]!["is_daze_deferred"]!.GetValue<bool>() == (deferred != 0) &&
                DsonWriter.Encode(json, original, default).SequenceEqual(bytes), "Official Daze bool payload is decoded without altering no-op bytes.");
            json["base_root"]!["other_value"] = 9;
            var restored = DsonReader.Read(DsonWriter.Encode(json, original, default), default).ToJson(default);
            Assert(restored["base_root"]!["is_daze_deferred"]!.GetValue<bool>() == (deferred != 0), "An unrelated edit preserves the Daze turn-order flag.");
        }
        Console.WriteLine("PASS: independently authored Daze boolean payloads, exact no-op and unrelated-edit preservation.");
    }
}
