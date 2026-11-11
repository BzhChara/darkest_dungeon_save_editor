internal static partial class ContractSuite
{
    private static async Task VerifyEffectReferenceChainsAsync(string root)
    {
        VerifyActorLootHooks(Path.Combine(root, "actors"));
        foreach (var kind in new[] { "base", "local", "workshop" })
            await VerifyRuntimeQuirkChainsAsync(Path.Combine(root, kind), kind);
    }

    private static void VerifyActorLootHooks(string root)
    {
        var f = HeroSelectionContent(root, "local", 1);
        SelectionResource(f, "inventory/query.inventory.items.darkest", "inventory_item: .type estate .id query_token .base_stack_limit 2\n");
        SelectionResource(f, "loot/query.loot.json", QueryLootJson);
        var effects = SelectionResource(f, "effects/query.effects.darkest", "effect: .name gain .gain_loot query_loot\n");
        var buffs = SelectionResource(f, "shared/buffs/query.buffs.json",
            """{"buffs":[{"id":"trigger","turn_start_additional_effect_ids":["gain"]}]}""");
        var hero = Path.Combine(f.Source, "heroes/selection/selection.info.darkest");
        var original = File.ReadAllText(hero);
        var monster = SelectionResource(f, "monsters/probe/probe_A/probe_A.info.darkest", "");
        var raid = JsonNode.Parse("""{"base_root":{"party":{"inventory":{"items":{}}}}}""")!.AsObject();
        QuantityItemDefinition Item() => QuantityItemCatalog.LoadRaid(f.Content, raid).Items.Single(i => i.ItemId == "query_token");
        void Check(bool expected, string label)
        {
            var item = Item();
            Assert(item.ReferenceStatus == (expected ? QuantityItemReferenceStatus.ConfirmedActive : QuantityItemReferenceStatus.SuspectedUnused) &&
                item.IsHiddenByDefault != expected, label);
        }
        // Acceptance cases are pinned to the native call sites and buffer sizes,
        // independently of the product's field dispatch table.
        foreach (var (header, field, count) in new[]
        {
            ("crit:", "effects", 8), ("mode: .id normal", "battle_start_effect_ids", 8),
            ("hp_reaction: .hp_ratio 0.5 .is_under true", "effects", 16),
            ("death_reaction: .target_allies true", "effects", 16)
        })
        foreach (var actor in new[] { hero, monster })
        {
            File.WriteAllText(hero, original);
            File.WriteAllText(monster, "");
            var prefix = actor == hero ? original : "";
            File.WriteAllText(actor, prefix + $"{header} .{field} {string.Join(' ', Enumerable.Repeat("absent", count - 1))} gain\n");
            Check(true, $"{header}/{actor}: final native slot reaches loot");
            File.WriteAllText(actor, prefix + $"{header} .{field} {string.Join(' ', Enumerable.Repeat("absent", count))} gain\n");
            Check(false, $"{header}: overflow must not reach loot");
            File.WriteAllText(actor, prefix + $"{header} .{field} \"\" gain\n");
            Check(false, $"{header}: empty token terminates the list");
        }
        File.WriteAllText(hero, original);
        foreach (var (header, field) in new[]
        {
            ("captor_empty:", "capture_effects"), ("captor_full:", "release_effects"),
            ("death_class:", "change_class_effects"), ("controller:", "uncontrol_effects"),
            ("spawn:", "effects"), ("spawn:", "wave_effects"),
            ("skill_reaction:", "was_hit_performer_effects"), ("skill_reaction:", "was_hit_target_effects"),
            ("skill_reaction:", "was_killed_other_monsters_effects"), ("skill_reaction:", "was_killed_by_hero_effects"),
            ("skill_reaction:", "was_killed_all_heroes_effects"), ("skill_reaction:", "was_killed_effects")
        })
        {
            File.WriteAllText(monster, $"{header} .{field} absent absent absent gain\n");
            Check(true, $"{field}: fourth monster Effect slot is consumed");
            File.WriteAllText(monster, $"{header} .{field} absent absent absent absent gain\n");
            Check(false, $"{field}: fifth monster Effect slot is ignored");
        }
        var bounded = new string('x', 31);
        File.AppendAllText(effects, $"effect: .name {bounded} .gain_loot query_loot\n");
        File.WriteAllText(monster, $"skill_reaction: .was_hit_target_effects {bounded}ignored\n");
        Check(true, "Monster reaction references use 31-byte payloads before lookup");
        foreach (var entry in new[] { "companion: .monster_class probe_A .buffs", "battle_modifier: .living_other_enemy_buffs", "battle_modifier: .living_hero_buff_instance_ids" })
        {
            File.WriteAllText(monster, entry + " absent absent absent absent absent absent absent trigger\n");
            Check(true, entry + ": eighth Buff is consumed");
            File.WriteAllText(monster, entry + " absent absent absent absent absent absent absent absent trigger\n");
            Check(false, entry + ": ninth Buff is ignored");
        }
        File.WriteAllText(monster, "battle_stage: .id 1 .buffs trigger\n");
        Check(false, "battle_stage does not load companion Buffs");
        File.WriteAllText(monster, "mode: .battle_start_effect_ids gain\n");
        Check(false, "A mode without an id is not loaded");
        File.WriteAllText(monster, "");
        File.WriteAllText(hero, original + "skill_reaction: .was_hit_target_effects gain\n");
        Check(false, "Monster-only reaction fields are not hero roots");
        File.WriteAllText(hero, original + "combat_skill: .id s0 .effect enable\n");
        const string riposte = "effect: .name gain .gain_loot query_loot\neffect: .name enable .riposte 1 .riposte_effect gain\n";
        File.WriteAllText(effects, riposte);
        Check(true, "A positive riposte installs its effect for later combat");
        File.AppendAllText(effects, "effect: .name enable .chance 50%\n");
        Check(true, "Omitted riposte fields retain the reference");
        File.AppendAllText(effects, "effect: .name enable .riposte 0\n");
        Check(false, "Explicit zero disables the retained riposte effect");
        File.WriteAllText(effects, riposte + "effect: .name enable .riposte_effect \"\"\n");
        Check(false, "Explicit empty clears the riposte effect");
        File.WriteAllText(effects, riposte.Replace(".riposte 1", ".riposte -1"));
        Check(false, "Negative riposte removes a status, not installs its effect");
        File.WriteAllText(effects, riposte.Replace(".riposte 1", ".riposte 2147483648"));
        Check(true, "Positive UCRT atoi overflow remains a positive riposte gate");
        File.WriteAllText(effects, riposte + "effect: .name enable .riposte -2147483649\n");
        Check(false, "Explicit negative overflow disables an earlier positive gate");
        File.WriteAllText(effects, riposte + "effect: .name gain .gain_loot query_loot .buff_ids trigger\n");
        Check(true, "A mixed Effect/Buff cycle terminates and retains the reachable loot");
        File.WriteAllText(buffs, """{"buffs":[]}""");
        File.WriteAllText(hero, original + "combat_skill: .id s0 .effect missing\n");
        Check(false, "A missing named Effect is not an inferred link");
        File.WriteAllText(hero, original + "combat_skill: .id s0 .effect enable\n");
        var unreadable = SelectionResource(f, "effects/unreadable.effects.darkest", "");
        File.Delete(unreadable);
        Assert(Item().ReferenceStatus == QuantityItemReferenceStatus.AnalysisIncomplete && !Item().IsHiddenByDefault,
            "An unseen provider cannot certify an indirect loot chain or hide the item");
        Console.WriteLine("PASS: native actor hooks, distinct list limits, riposte gating/retention, cycles and incomplete loot graphs.");
    }

    private static async Task VerifyRuntimeQuirkChainsAsync(string root, string kind)
    {
        var f = HeroSelectionContent(root, kind, 2);
        SelectionResource(f, "heroes/selection/selection_A/skin.png", "fixture");
        var hero = Path.Combine(f.Source, "heroes/selection/selection.info.darkest");
        var original = File.ReadAllText(hero);
        var quirks = SelectionResource(f, "shared/quirk/query.quirk_library.json",
            """{"quirks":[{"id":"special","is_positive":true,"random_chance":0,"buffs":[]}]}""");
        const string definitions = "effect: .name assign .disease special\neffect: .name apply .buff_ids trigger\neffect: .name counter .riposte 1 .riposte_effect assign\n";
        var effects = SelectionResource(f, "effects/query.effects.darkest", definitions);
        const string triggers = """{"buffs":[{"id":"trigger","turn_start_additional_effect_ids":["assign","apply"]}]}""";
        var buffs = SelectionResource(f, "shared/buffs/query.buffs.json", triggers);
        var camp = SelectionResource(f, "raid/camping/query.camping_skills.json", """{"skills":[]}""");
        HeroClassCatalogResult Catalog() => HeroClassCatalog.Load(f.Content);
        HeroRuntimeQuirkSignal[] Signals() => Catalog().HeroClasses.Single().RuntimeQuirkSignals.ToArray();
        void Check(int count, string label) => Assert(Signals().Length == count, kind + "/" + label);
        Check(0, "Unrooted disease/trigger definitions do not imply a class source");
        File.WriteAllText(hero, original + "combat_skill: .id s0 .effect apply\ncombat_skill: .id s1 .effect apply\n");
        var signals = Signals();
        Assert(signals.Length == 2 && signals.All(s => s.QuirkId == "special" && s.EffectName == "assign") &&
            signals.Select(s => s.SkillId).Order().SequenceEqual(new[] { "s0", "s1" }),
            kind + "/Indirect cyclic chains retain each originating skill and the assigning leaf Effect");
        var catalog = Catalog();
        Assert(catalog.InitialQuirks.Single(q => q.Id == "special").WriteStatus == HeroInitialQuirkWriteStatus.Direct &&
            !GenerateSelectionHero(catalog).Candidate["quirks"]!.AsObject().ContainsKey("special"),
            kind + "/Runtime clues neither restrict an ordinary explicit quirk write nor auto-grant it");
        File.WriteAllText(buffs, triggers[..^2] + ",{\"id\":\"trigger\"}]}");
        Check(0, "A later whole Buff definition clears old trigger arrays");
        File.WriteAllText(buffs, triggers);
        File.AppendAllText(effects, "effect: .name apply .buff_ids \"\"\neffect: .name assign .chance 50%\n");
        Check(2, "Effect omissions and empty appended Buff lists retain prior links and disease");
        File.AppendAllText(effects, "effect: .name assign .disease \"\"\n");
        Check(0, "Explicit disease clearing removes indirect signals");
        File.WriteAllText(effects, definitions);
        File.WriteAllText(hero, original + "combat_skill: .id s0 .effect counter\n");
        Check(1, "A positive riposte chain supplies a special quirk source");
        File.AppendAllText(effects, "effect: .name counter .riposte 0\n");
        Check(0, "Disabled riposte cannot supply a quirk source");
        foreach (var (value, enabled) in new[]
        {
            ("2147483648", true), ("-2147483649", false),
            ("+999999999999999999999999999999", true), ("-999999999999999999999999999999", false),
            ("1suffix", true), ("\"1\"", false), ("nonnumeric", false)
        })
        {
            File.WriteAllText(effects, definitions + $"effect: .name counter .riposte {value}\n");
            Check(enabled ? 1 : 0, "Native integer-prefix/saturation gate: " + value);
        }
        File.WriteAllText(effects, definitions);
        File.WriteAllText(hero, original);
        string Camping(string classes, string payload, string extra = "") =>
            "{\"skills\":[{\"id\":\"camp\",\"hero_classes\":[\"" + classes + "\"],\"effects\":[" + payload + "]}" + extra + "]}";
        const string direct = """{"type":"effect","sub_type":"assign"}""";
        File.WriteAllText(camp, Camping("selection", direct));
        Assert(Signals() is [{ SkillId: "camp", EffectName: "assign", QuirkId: "special" }],
            kind + "/Camping Effect.disease is attributed to the available camping skill");
        File.WriteAllText(camp, Camping("selection", """{"type":"buff","sub_type":"trigger"}"""));
        Check(1, "Camping Buff triggers supply indirect quirk sources");
        File.WriteAllText(camp, Camping("absent", direct));
        Check(0, "Unavailable camping skills do not supply class sources");
        File.WriteAllText(camp, Camping("absent", direct, ",{\"id\":\"camp\",\"hero_classes\":[\"selection\"],\"effects\":[]}"));
        Check(1, "Later class access uses the first camping payload");
        File.WriteAllText(camp, Camping("selection", "", ",{\"id\":\"camp\",\"hero_classes\":[\"selection\"],\"effects\":[" + direct + "]}"));
        Check(0, "A shadowed camping payload cannot create a source");
        File.WriteAllText(camp, Camping("selection", direct));
        File.WriteAllText(quirks, """{"quirks":[{"id":"special","is_positive":true,"random_chance":1,"buffs":[]}]}""");
        Check(0, "Ordinary random quirks are not labeled special runtime quirks");
        File.WriteAllText(quirks, """{"quirks":[{"id":"special","is_positive":true,"random_chance":0,"buffs":[]}]}""");
        File.WriteAllText(camp, """{"skills":[]}""");
        File.WriteAllText(hero, original + "combat_skill: .id s0 .effect apply\n");
        await WithFailedDefinitionAsync(buffs, "locked", () =>
        {
            Check(0, "Unreadable effective Buffs cannot create a source");
            return Task.CompletedTask;
        });
        await WithFailedDefinitionAsync(effects, "locked", () =>
        {
            Check(0, "Unreadable Effect definitions cannot create a source");
            return Task.CompletedTask;
        });
        Check(1, "Restoring definitions restores the indirect source");
        File.WriteAllText(hero, original + "combat_skill: .id s0 .effect az bE\n");
        File.WriteAllText(effects, "effect: .name az .disease special\n");
        Assert(Signals().Select(s => s.EffectName).Order().SequenceEqual(new[] { "az", "bE" }),
            kind + "/Two authored names sharing an Effect retain both source descriptions");
        File.WriteAllText(hero, original + "combat_skill: .id s0 .effect bE\n");
        File.WriteAllText(effects, definitions + "effect: .name az .riposte 1 .riposte_effect assign\n");
        Check(1, "Effect links resolve different names with the same native hash");
        File.AppendAllText(effects, "effect: .name bE .riposte 0\n");
        Check(0, "An alias updates the existing Effect's riposte gate");
        File.AppendAllText(effects, "effect: .name az .riposte 1\n");
        Check(1, "A/B/A Effect declarations retain the target while restoring the gate");
        File.WriteAllText(hero, original + "combat_skill: .id s0 .effect apply\n");
        File.WriteAllText(effects, "effect: .name apply .buff_ids az\neffect: .name assign .disease special\n");
        File.WriteAllText(buffs, """{"buffs":[{"id":"az","turn_start_additional_effect_ids":["assign"]},{"id":"bE"}]}""");
        Check(0, "A hash-alias Buff replaces the complete trigger payload");
        File.WriteAllText(buffs, """{"buffs":[{"id":"az"},{"id":"bE"},{"id":"az","turn_start_additional_effect_ids":["assign"]}]}""");
        Check(1, "A/B/A Buff selection uses the final definition in load order");
        File.WriteAllText(hero, original);
        File.WriteAllText(camp, Camping("selection", direct));
        await WithFailedDefinitionAsync(camp, "locked", () =>
        {
            Check(0, "An unreadable first camping payload cannot supply a source");
            return Task.CompletedTask;
        });
        Console.WriteLine($"PASS: {kind}/indirect special quirk provenance, first camping payload, cycles, certainty and generation isolation.");
    }
}
