internal static partial class ContractSuite
{
    private const string RuntimeCurioHeader = ",,ID STRING,,RESULT TYPES\n,,runtime_curio,,Nothing\n";
    private const string RuntimeCurioItems = ",,Item Interactions,,ITEM\n";
    private static string RuntimeTorch(string fields, string key = "default") =>
        "{\"torch_settings_data_table\":[{\"key\":\"" + key + "\",\"data\":{" + fields + "}}]}";
    private static string RuntimeModifier(string fields, string condition = "always") =>
        "{\"modifiers\":[{\"id\":\"runtime_modifier\",\"condition_type\":\"" + condition + "\"," + fields + "}]}";
    private static string RuntimeFlashback(string fields, string required = "selection") =>
        "{\"flashbacks\":[{\"id\":\"runtime_flashback\",\"chance\":1,\"start_type\":\"camp_finish\",\"required_hero_class_ids\":[\"" + required + "\"]," + fields + "}]}";
    private static string RuntimeActivity(string buff = "trigger", string id = "bar", int chance = 1) =>
        "{\"data\":{\"activities\":[{\"id\":\"" + id + "\",\"data\":{\"side_effects\":{\"chance\":" + chance +
        ",\"results\":[{\"type\":\"apply_buff\",\"chance\":1,\"data\":[{\"chance\":1,\"buff_library_ids\":[\"" + buff + "\"]}]}]}}}]}}";
    private static string RuntimeActOut(string id = "buff_random_party_member", string effect = "gain", bool reaction = false, int chance = 1) =>
        "\"" + (reaction ? "reaction_act_outs" : "combat_start_turn_act_outs") + "\":[{\"id\":\"" + id +
        "\",\"chance\":" + chance + ",\"data\":{\"" + (reaction ? "effect" : "string_value") + "\":\"" + effect + "\"}}]";
    private static string RuntimeTrap(string fields, string variants = "", string name = "runtime_trap") =>
        "{\"props\":[{\"name\":\"" + name + "\",\"default_data\":{\"instance_type\":\"trap\"," + fields + "}" + variants + "}]}";

    private static void VerifyNativeLootRuntimeSources(string root)
    {
        foreach (var kind in new[] { "local", "workshop", "dlc-mod" })
        {
            VerifyRuntimeRootFamilies(Path.Combine(root, kind, "families"), kind);
            VerifyRuntimeSourceFailures(Path.Combine(root, kind, "failures"), kind);
            VerifyLastLivingActor(Path.Combine(root, kind, "last-actor"), kind);
        }
        VerifyRuntimeSourceSemantics(Path.Combine(root, "semantics"));
        VerifyRuntimeProviderRules(Path.Combine(root, "providers"));
        VerifyRuntimeSourceBoundaries(Path.Combine(root, "boundaries"));
    }

    private static void VerifyRuntimeRootFamilies(string root, string kind)
    {
        var cases = new List<(string Name, string Path, string Text)>();
        foreach (var field in new[] { "hero_buff_list", "monster_buff_list", "hero_effect_list", "monster_effect_list" })
            cases.Add(("torch-" + field, "scripts/raid_settings.json", RuntimeTorch($"\"{field}\":[\"{(field.Contains("buff") ? "trigger" : "install")}\"]")));
        foreach (var field in new[] { "hero_buffs", "monster_buffs" })
            cases.Add(("modifier-" + field, "campaign/quest/query.quest.modifiers.json", RuntimeModifier($"\"{field}\":[\"trigger\"]", "wave_kill_count")));
        foreach (var field in new[] { "stun_onset_buffs", "stun_recovery_buffs" })
            cases.Add((field, "shared/query.rules.json", $"{{\"{field}\":[\"trigger\"]}}"));
        cases.Add(("stall", "shared/query.rules.json", """{"stall_stress_effect_name":"gain"}"""));
        cases.Add(("curio", "curios/query.curio_type_library.csv", RuntimeCurioHeader + ",,REGION FOUND,,Effect,1,100%,install,1\n" + RuntimeCurioItems));
        cases.Add(("flashback-result", "raid/flashback/query.flashbacks.json", RuntimeFlashback("\"results\":[{\"type\":\"buff\",\"target_type\":\"required_hero\",\"string_data\":\"trigger\",\"is_on_complete\":true}]")));
        cases.Add(("flashback-camp", "raid/flashback/query.flashbacks.json", RuntimeFlashback("\"completed_camping_start_buffs\":[\"trigger\"]")));
        cases.Add(("flashback-start", "raid/flashback/query.flashbacks.json", RuntimeFlashback("\"required_heroes_starting_effects\":[\"install\"]")));
        cases.Add(("activity", "campaign/town/buildings/tavern/tavern.building.json", RuntimeActivity()));
        cases.Add(("trait", "shared/trait/query.trait_library.json", "{\"traits\":[{\"id\":\"test_trait\"," + RuntimeActOut() + "}]}"));
        cases.Add(("quirk", "shared/quirk/query.quirk_act_outs.json", "{\"quirk_act_outs\":[{\"quirk_id\":\"test_quirk\"," + RuntimeActOut() + "}]}"));
        foreach (var field in new[] { "success_effects", "fail_effects" })
            cases.Add((field, "props/crypts/trap_definitions.json", RuntimeTrap($"\"{field}\":[\"install\"]")));
        foreach (var (name, path, text) in cases)
        {
            var f = new LootSourceFixture(Path.Combine(root, name), kind);
            f.Write("shared/quirk/query.quirk_library.json", """{"quirks":[{"id":"test_quirk","buffs":[]}]}""");
            var file = f.Write(path, text);
            f.Check(QuantityItemReferenceStatus.ConfirmedActive, name + ": native consumer reaches combat loot through Effect/Buff links");
            File.WriteAllText(file, text.Replace("trigger", "absent").Replace("install", "absent").Replace("\"gain\"", "\"absent\""));
            f.Check(QuantityItemReferenceStatus.SuspectedUnused, name + ": unrelated definitions are not roots");
        }
        foreach (var (name, effect) in new[] { ("direct", ".gain_loot query_loot"), ("buff", ".buff_ids trigger") })
        {
            var f = new LootSourceFixture(Path.Combine(root, "item-" + name), kind);
            f.Write("inventory/use.inventory.items.darkest", "inventory_item: .type supply .id bandage .base_stack_limit 6\n");
            File.AppendAllText(f.Effects, "effect: .name bandage .item 1 " + effect + "\n");
            f.Check(QuantityItemReferenceStatus.ConfirmedActive, "Item use " + name);
        }
        Console.WriteLine($"PASS: {kind} nine native runtime source families, direct item effects and non-combat Buff bridges.");
    }

    private static void VerifyRuntimeSourceFailures(string root, string kind)
    {
        foreach (var (name, path, invalid, restored) in new[]
        {
            ("torch", "scripts/raid_settings.json", RuntimeTorch("\"hero_buff_list\":{}"), RuntimeTorch("")),
            ("modifier", "campaign/quest/query.quest.modifiers.json", RuntimeModifier("\"hero_buffs\":[{}]"), "{\"modifiers\":[]}"),
            ("rules", "shared/query.rules.json", "{\"stun_onset_buffs\":{}}", "{}"),
            ("flashback", "raid/flashback/query.flashbacks.json", "{\"flashbacks\":[42]}", "{\"flashbacks\":[]}"),
            ("activity", "campaign/town/buildings/tavern/tavern.building.json", RuntimeActivity().Replace("[\"trigger\"]", "{}"), "{\"data\":{\"activities\":[]}}"),
            ("actout", "shared/trait/query.trait_library.json", "{\"traits\":[{\"id\":\"test\",\"reaction_act_outs\":{}}]}", "{\"traits\":[]}"),
            ("trap", "props/crypts/trap_definitions.json", RuntimeTrap("\"fail_effects\":{}"), "{\"props\":[]}")
        })
        {
            var f = new LootSourceFixture(Path.Combine(root, name), kind);
            var file = f.Write(path, invalid);
            f.Check(QuantityItemReferenceStatus.AnalysisIncomplete, name + ": invalid consumed structure prevents unused verdicts", globalIncomplete: true);
            f.Actor("crit: .effects gain\n");
            f.Check(QuantityItemReferenceStatus.ConfirmedActive, name + ": source failure preserves independent evidence", globalIncomplete: true);
            f.Actor("");
            File.WriteAllText(file, "{");
            f.Check(QuantityItemReferenceStatus.AnalysisIncomplete, name + ": unreadable effective source", globalIncomplete: true);
            File.WriteAllText(file, restored);
            f.Check(QuantityItemReferenceStatus.SuspectedUnused, name + ": repaired source restores completeness");
        }
        var item = new LootSourceFixture(Path.Combine(root, "item"), kind);
        var itemFile = item.Write("inventory/use.inventory.items.darkest", "inventory_item: .type supply .id bandage\n");
        using (File.Open(itemFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            item.Check(QuantityItemReferenceStatus.AnalysisIncomplete, "Unreadable inventory prevents unused verdict", globalIncomplete: true);
        item.Check(QuantityItemReferenceStatus.SuspectedUnused, "Inventory read recovery");
        var curio = new LootSourceFixture(Path.Combine(root, "curio"), kind);
        var curioFile = curio.Write("curios/query.curio_type_library.csv", RuntimeCurioHeader + ",,REGION FOUND,,Effect,1,100%," + new string('x', 512) + ",1\n" + RuntimeCurioItems);
        curio.Check(QuantityItemReferenceStatus.AnalysisIncomplete, "Curio field exceeds the native CSV buffer", globalIncomplete: true);
        File.WriteAllText(curioFile, RuntimeCurioHeader + RuntimeCurioItems);
        curio.Check(QuantityItemReferenceStatus.SuspectedUnused, "Curio read recovery");
        Console.WriteLine($"PASS: {kind} runtime source completeness, independent evidence, unreadable files and recovery.");
    }

    private static void VerifyLastLivingActor(string root, string kind)
    {
        foreach (var hero in new[] { true, false })
        {
            var f = new LootSourceFixture(Path.Combine(root, hero ? "hero" : "monster"), kind);
            void Actor(string record)
            {
                if (hero) f.Actor(record);
                else f.Write("monsters/probe/probe_A/probe_A.info.darkest", record);
            }
            Actor("last_hero: .buffs trigger\n");
            f.Check(QuantityItemReferenceStatus.ConfirmedActive, "Last living actor applies Buffs on either side");
            Actor("last_hero: .buffs " + string.Join(' ', Enumerable.Repeat("absent", 16)) + " trigger\n");
            f.Check(QuantityItemReferenceStatus.SuspectedUnused, "Last actor seventeenth token is ignored");
            Actor("last_hero: .buffs \"\" trigger\n");
            f.Check(QuantityItemReferenceStatus.SuspectedUnused, "Last actor empty slot stops reading");
            Actor("last_hero: .buffs trigger\nlast_hero: .buffs \"\"\n");
            f.Check(QuantityItemReferenceStatus.ConfirmedActive, "Last actor declarations append");
        }
        Console.WriteLine($"PASS: {kind} verified last_hero activation for heroes and monsters with native slot semantics.");
    }

    private static void VerifyRuntimeSourceSemantics(string root)
    {
        var f = new LootSourceFixture(Path.Combine(root, "torch"), "local");
        var path = f.Write("scripts/raid_settings.json", RuntimeTorch("\"hero_buff_list\":[\"trigger\"]"));
        File.WriteAllText(path, """{"torch_settings_data_table":[{"key":"default","data":{"hero_buff_list":["trigger"]}},{"key":"default","data":{}}]}""");
        f.Check(QuantityItemReferenceStatus.SuspectedUnused, "Duplicate torch key replaces the complete value");
        File.WriteAllText(path, RuntimeTorch("\"hero_effect_list\":[\"gain\"]"));
        f.Check(QuantityItemReferenceStatus.SuspectedUnused, "Torch transition cannot directly gain loot outside combat");
        f.Write("inventory/use.inventory.items.darkest", "inventory_item: .type supply .id bandage\n");
        foreach (var data in new[]
        {
            "\"torch_setting_item_performer_effects\":[{\"item_type\":\"supply\",\"item_name\":\"bandage\",\"effect_list\":[\"gain\"]}]",
            "\"torch_setting_item_performer_effects\":[{\"item_type\":\"supply\",\"item_name\":\"bandage\",\"sequential_effect_list\":[[\"absent\"],[\"gain\"]]}]"
        })
        {
            File.WriteAllText(path, RuntimeTorch(data));
            f.Check(QuantityItemReferenceStatus.ConfirmedActive, "Torch performer and sequential effects are executable sources");
            File.WriteAllText(path, RuntimeTorch(data.Replace("bandage", "missing_item")));
            f.Check(QuantityItemReferenceStatus.SuspectedUnused, "Torch item source requires the matching inventory identity");
        }

        f = new LootSourceFixture(Path.Combine(root, "items"), "local");
        path = f.Write("inventory/use.inventory.items.darkest", "inventory_item: .type supply .id bandage .sequential_use_effects gain\n");
        f.Check(QuantityItemReferenceStatus.ConfirmedActive, "Sequential inventory use reaches its Effect");
        File.AppendAllText(f.Effects, "effect: .name bandage .gain_loot query_loot\n");
        File.WriteAllText(path, "inventory_item: .type supply .id bandage .sequential_use_effects absent\n");
        f.Check(QuantityItemReferenceStatus.SuspectedUnused, "A sequence replaces the same-name Effect fallback");
        File.WriteAllText(path, "inventory_item: .type food .id bandage\n");
        f.Check(QuantityItemReferenceStatus.SuspectedUnused, "Food does not execute the supply Effect callback");
        File.WriteAllText(path, "inventory_item: .type supply .id bandage .sequential_use_effects absent\ninventory_item: .type supply .id bandage .sequential_use_effects gain\n");
        f.Check(QuantityItemReferenceStatus.SuspectedUnused, "Item callback uses the first definition of a type/id");
        File.WriteAllText(path, "inventory_item: .type supply .id bandage\n");
        File.WriteAllText(f.Effects, "effect: .name gain .gain_loot query_loot\n");
        f.Write("campaign/town/districts/query.districts.json", """{"buildings":[{"name":"runtime","buff_list":[{"type":"DistrictReplacementInventoryEffectBuffData","item_type":"supply","item_id":"bandage","effect":"gain"}]}]}""");
        f.Check(QuantityItemReferenceStatus.ConfirmedActive, "Built district can replace an item Effect by its typed identity");

        f = new LootSourceFixture(Path.Combine(root, "rules"), "local");
        f.Write("shared/a.rules.json", """{"stun_onset_buffs":["trigger"]}""");
        path = f.Write("shared/z.rules.json", "{}");
        f.Check(QuantityItemReferenceStatus.ConfirmedActive, "Omitted rule list preserves previous value");
        File.WriteAllText(path, """{"stun_onset_buffs":[]}""");
        f.Check(QuantityItemReferenceStatus.SuspectedUnused, "Present empty rule list replaces previous value");

        f = new LootSourceFixture(Path.Combine(root, "traps"), "local");
        path = f.Write("props/trap_definitions.json", RuntimeTrap("\"success_effects\":[\"install\"]"));
        f.Check(QuantityItemReferenceStatus.SuspectedUnused, "Base-only root open ignores Mod root trap definitions");
        path = f.Write("props/crypts/trap_definitions.json", RuntimeTrap("\"success_effects\":[\"install\"]", ",\"difficulty_variations\":[{\"level\":1,\"success_effects\":[]}]"));
        f.Check(QuantityItemReferenceStatus.ConfirmedActive, "Other difficulty copies retain the inherited Effect list");
        File.WriteAllText(path, RuntimeTrap("\"success_effects\":[\"install\"]", ",\"difficulty_variations\":[" + string.Join(',', Enumerable.Range(1, 7).Select(level => $"{{\"level\":{level},\"success_effects\":[]}}")) + "]"));
        f.Check(QuantityItemReferenceStatus.SuspectedUnused, "Every effective difficulty clears the trap Effect list");
        File.WriteAllText(path, """{"props":[{"name":"parent","default_data":{"instance_type":"curio","fail_effects":["install"]}},{"name":"child","default_data":{"inherits_from":{"prop_type_name":"parent"},"instance_type":"trap"}}]}""");
        f.Check(QuantityItemReferenceStatus.ConfirmedActive, "Trap inherits an already loaded parent's named Effect list");
        File.WriteAllText(path, """{"props":[{"name":"parent","default_data":{"instance_type":"curio","fail_effects":["install"]}},{"name":"child","default_data":{"inherits_from":{"prop_type_name":"parent"},"instance_type":"trap","fail_effects":[]}}]}""");
        f.Check(QuantityItemReferenceStatus.SuspectedUnused, "Child's empty list replaces inherited effects");
        File.WriteAllText(path, RuntimeTrap("\"success_effects\":[\"gain\"]"));
        f.Check(QuantityItemReferenceStatus.SuspectedUnused, "Trap success cannot directly gain loot outside combat");

        f = new LootSourceFixture(Path.Combine(root, "actouts"), "local");
        path = f.Write("shared/trait/query.trait_library.json", "");
        foreach (var (id, combat) in new[] { ("comment_self_hit", true), ("block_item", true), ("comment_curio_interaction", false), ("block_camping_skill_target", false) })
        {
            File.WriteAllText(path, "{\"traits\":[{\"id\":\"test\"," + RuntimeActOut(id, "gain", reaction: true) + "}]}");
            f.Check(combat ? QuantityItemReferenceStatus.ConfirmedActive : QuantityItemReferenceStatus.SuspectedUnused, "Reaction context: " + id);
            File.WriteAllText(path, "{\"traits\":[{\"id\":\"test\"," + RuntimeActOut(id, "install", reaction: true) + "}]}");
            f.Check(QuantityItemReferenceStatus.ConfirmedActive, "Reaction installs a combat-triggered Buff: " + id);
        }
        foreach (var id in new[] { "buff_self", "heal_self", "consume_item", "nothing" })
        {
            File.WriteAllText(path, "{\"traits\":[{\"id\":\"test\"," + RuntimeActOut(id) + "}]}");
            f.Check(QuantityItemReferenceStatus.SuspectedUnused, "Invalid/non-Effect act-out kind: " + id);
        }
        File.WriteAllText(path, "{\"traits\":[{\"id\":\"test\"," + RuntimeActOut(chance: 0) + "}]}");
        f.Check(QuantityItemReferenceStatus.SuspectedUnused, "Zero-chance act-out does not activate");
        Console.WriteLine("PASS: runtime duplicate/replace/append rules, contexts, typed item routes, trap inheritance and act-out consumers.");
    }

    private static void VerifyRuntimeProviderRules(string root)
    {
        // Same physical relative path: ordinary flag-0 rule query selects the
        // higher provider, while torch's flag-9 query consumes both in load order.
        foreach (var kind in new[] { "local", "workshop", "dlc-mod" })
        {
            var f = new LootSourceFixture(Path.Combine(root, kind), kind);
            f.Write("scripts/nested/queryXraid_settingsXjson", RuntimeTorch("\"hero_buff_list\":[\"trigger\"]"));
            f.Check(QuantityItemReferenceStatus.ConfirmedActive, "Torch query preserves native wildcard dots and nested paths");
            f.Write("scripts/nested/queryXraid_settingsXjson", "{}");
            f.Write("scripts/notes.json", RuntimeTorch("\"hero_buff_list\":[\"trigger\"]"));
            f.Check(QuantityItemReferenceStatus.SuspectedUnused, "Nonmatching source filename is not a consumer");
            f.Write("raid/flashback/queryXflashbacksXjson", RuntimeFlashback("\"completed_camping_start_buffs\":[\"trigger\"]", "absent_class"));
            f.Check(QuantityItemReferenceStatus.SuspectedUnused, "Missing required flashback class cannot install Buffs");
            var baseline = f.Files.Content.Sources.Single(s => s.Id == "base").Directory;
            var lowRule = WriteMultiMash(baseline, "shared/overlay.rules.json", """{"stun_onset_buffs":["trigger"]}""");
            var highRule = f.Write("shared/overlay.rules.json", "{}");
            f.Check(QuantityItemReferenceStatus.SuspectedUnused, "Rule flag-0 query replaces the same-path base provider");
            File.WriteAllText(lowRule, "{");
            File.WriteAllText(highRule, """{"stun_onset_buffs":["trigger"]}""");
            f.Check(QuantityItemReferenceStatus.ConfirmedActive, "Shadowed unreadable rule provider cannot taint effective roots");
            File.WriteAllText(highRule, "{}");
            var lowTorch = WriteMultiMash(baseline, "scripts/raid_settings.json", RuntimeTorch("\"hero_buff_list\":[\"trigger\"]"));
            var highTorch = f.Write("scripts/raid_settings.json", "{}");
            f.Check(QuantityItemReferenceStatus.ConfirmedActive, "Torch flag-9 query retains the same-path base settings");
            File.WriteAllText(highTorch, RuntimeTorch(""));
            f.Check(QuantityItemReferenceStatus.SuspectedUnused, "Later same-key torch payload replaces the base payload");
            File.WriteAllText(lowTorch, "{");
            f.Check(QuantityItemReferenceStatus.AnalysisIncomplete, "Unreadable additive torch provider prevents an unused verdict", globalIncomplete: true);
        }
        Console.WriteLine("PASS: runtime source query names, manifest/DLC mounting and missing class admission.");
    }
}
