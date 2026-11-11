internal static partial class ContractSuite
{
    private static void VerifyNativeTrinketTriggerReferences(string root)
    {
        // Acceptance vocabulary: current native trinket enum 0xA7BD0 and
        // loader 0x4FF060 (26 keys), independent of the product's Buff names.
        var combatTriggers = new[]
        {
            "friendly_skill", "attack_skill", "melee_attack_skill", "ranged_attack_skill", "attack_crit",
            "riposte_skill", "riposte_kill", "riposte_crit", "kill_performer", "kill_all_heroes",
            "kill_all_monsters", "was_killed_all_heroes", "was_killed_all_monsters", "was_hit",
            "was_hit_all_heroes", "was_hit_all_monsters", "battle_finished_successfully", "turn_end",
            "on_dodge", "was_killed", "round_end", "turn_start", "move_skill", "combat_start"
        };
        foreach (var kind in new[] { "local", "workshop" })
        {
            var f = HeroSelectionContent(Path.Combine(root, kind), kind, 1);
            SelectionResource(f, "inventory/query.inventory.items.darkest", "inventory_item: .type estate .id query_token .base_stack_limit 2\n");
            SelectionResource(f, "loot/query.loot.json", QueryLootJson);
            SelectionResource(f, "effects/query.effects.darkest", "effect: .name gain .gain_loot query_loot\neffect: .name install .buff_ids trigger\n");
            var buffs = SelectionResource(f, "shared/buffs/query.buffs.json",
                """{"buffs":[{"id":"trigger","turn_start_additional_effect_ids":["gain"]}]}""");
            var trinket = SelectionResource(f, "trinkets/query.entries.trinkets.json", "");
            var raid = JsonNode.Parse("""{"base_root":{"party":{"inventory":{"items":{}}}}}""")!.AsObject();
            void Check(bool expected, string label)
            {
                var item = QuantityItemCatalog.LoadRaid(f.Content, raid).Items.Single(item => item.ItemId == "query_token");
                Assert(item.ReferenceStatus == (expected ? QuantityItemReferenceStatus.ConfirmedActive : QuantityItemReferenceStatus.SuspectedUnused) &&
                    item.IsHiddenByDefault != expected, kind + "/" + label);
            }
            void Trinket(string field, string effect) => File.WriteAllText(trinket, new JsonObject
            {
                ["entries"] = new JsonArray(new JsonObject { ["id"] = "query", ["buffs"] = new JsonArray(), [field] = new JsonArray(effect) })
            }.ToJsonString());
            foreach (var trigger in combatTriggers)
            foreach (var effect in new[] { "gain", "install" })
            {
                Trinket(trigger + "_additional_effects", effect);
                Check(true, $"{trigger}/{effect}: a native combat trigger reaches direct or Buff-triggered loot");
            }
            foreach (var trigger in new[] { "quest_start", "on_quest_complete" })
            {
                Trinket(trigger + "_additional_effects", "gain");
                Check(false, trigger + ": direct gain_loot is excluded outside combat");
            }
            Trinket("quest_start_additional_effects", "install");
            Check(true, "A quest-start Buff can trigger loot in later combat");
            foreach (var wrong in new[] { "attack", "friendly", "melee_attack", "ranged_attack", "riposte" })
            foreach (var effect in new[] { "gain", "install" })
            {
                Trinket(wrong + "_additional_effects", effect);
                Check(false, wrong + ": Buff-style names are not native trinket trigger aliases");
            }
            Trinket("attack_skill_additional_effect_ids", "gain");
            Check(false, "Trinket fields require the native additional_effects suffix");

            File.WriteAllText(trinket, """{"entries":[{"id":"query","buffs":["trigger"]}]}""");
            foreach (var prefix in new[] { "attack", "friendly", "melee_attack", "ranged_attack", "riposte" })
            foreach (var wrongSuffix in new[] { false, true })
            {
                var field = prefix + (wrongSuffix ? "_skill" : "") + "_additional_effect_ids";
                File.WriteAllText(buffs, new JsonObject { ["buffs"] = new JsonArray(new JsonObject
                    { ["id"] = "trigger", [field] = new JsonArray("gain") }) }.ToJsonString());
                Check(!wrongSuffix, field + ": Buffs retain their own exact trigger vocabulary");
            }
        }
        Console.WriteLine("PASS: all native trinket trigger keys, five invalid aliases, separate Buff names and direct/indirect loot visibility.");
    }
}
