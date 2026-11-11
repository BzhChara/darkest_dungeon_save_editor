internal static partial class ContractSuite
{
    private static void VerifyRuntimeSourceBoundaries(string root)
    {
        var f = new LootSourceFixture(Path.Combine(root, "torch-item"), "local");
        f.Write("inventory/use.inventory.items.darkest", "inventory_item: .type supply .id torch\ninventory_item: .type supply .id bandage\n");
        const string torchBuff = """{"item_type":"supply","item_name":"torch","hero_buff_list":["trigger"]}""";
        var path = f.Write("scripts/raid_settings.json", RuntimeTorch("\"torch_setting_item_buffs\":[" + torchBuff + "]"));
        f.Check(QuantityItemReferenceStatus.ConfirmedActive, "Using a torch applies its configured Buffs");
        File.WriteAllText(path, RuntimeTorch("\"torch_setting_item_buffs\":[" + torchBuff.Replace("torch", "bandage") + "]"));
        f.Check(QuantityItemReferenceStatus.SuspectedUnused, "Generic items do not consume torch item Buffs");
        File.WriteAllText(path, RuntimeTorch("\"torch_setting_item_buffs\":[" + torchBuff.Replace("[\"trigger\"]", "[]") + "," + torchBuff + "]"));
        f.Check(QuantityItemReferenceStatus.SuspectedUnused, "Torch item Buff lookup stops at the first typed identity");
        File.WriteAllText(path, RuntimeTorch("""
            "torch_setting_item_performer_effects":[
              {"item_type":"supply","item_name":"bandage","effect_list":["gain"],"sequential_effect_list":[["absent"]]},
              {"item_type":"supply","item_name":"bandage","effect_list":[]}]
            """));
        f.Check(QuantityItemReferenceStatus.ConfirmedActive, "Torch performer sequence does not replace its normal effects");
        File.WriteAllText(path, RuntimeTorch("""
            "torch_setting_item_performer_effects":[
              {"item_type":"supply","item_name":"bandage","effect_list":[]},
              {"item_type":"supply","item_name":"bandage","effect_list":["gain"]}]
            """));
        f.Check(QuantityItemReferenceStatus.ConfirmedActive, "All matching torch performer entries contribute effects");
        File.AppendAllText(f.Effects, "effect: .name bandage .gain_loot query_loot\n");
        File.WriteAllText(path, RuntimeTorch("\"torch_setting_item_performer_effects\":[{\"item_type\":\"supply\",\"item_name\":\"bandage\",\"effect_list\":[\"absent\"]}]"));
        f.Check(QuantityItemReferenceStatus.SuspectedUnused, "Torch performer callback overrides the same-name item Effect");
        var itemFile = f.Write("inventory/use.inventory.items.darkest", "inventory_item: .type supply .id bandage .sequential_use_effects gain absent\n");
        File.WriteAllText(path, RuntimeTorch("\"torch_setting_item_performer_effects\":[{\"item_type\":\"supply\",\"item_name\":\"bandage\",\"sequential_effect_list\":[[\"absent\"],[]]}]"));
        f.Check(QuantityItemReferenceStatus.SuspectedUnused, "Torch callback shadows the matching item-sequence step");
        File.WriteAllText(itemFile, "inventory_item: .type supply .id bandage .sequential_use_effects absent gain\n");
        f.Check(QuantityItemReferenceStatus.ConfirmedActive, "An empty later torch sequence allows the corresponding item step");
        File.WriteAllText(f.Effects, "effect: .name gain .gain_loot query_loot\n");
        File.WriteAllText(itemFile, "inventory_item: .type supply .id bandage\n");
        File.WriteAllText(path, RuntimeTorch("\"torch_setting_item_performer_effects\":[{\"item_type\":\"supply\",\"item_name\":\"bandage\",\"sequential_effect_list\":[[],[\"gain\"]]}]"));
        f.Check(QuantityItemReferenceStatus.SuspectedUnused, "An empty first torch step and ordinary fallback cannot advance the use counter");
        File.WriteAllText(itemFile, "inventory_item: .type supply .id bandage .sequential_use_effects absent\n");
        f.Check(QuantityItemReferenceStatus.ConfirmedActive, "Generic item sequence advances through an empty first torch step");
        File.WriteAllText(path, RuntimeTorch("\"torch_setting_item_performer_effects\":[{\"item_type\":\"supply\",\"item_name\":\"bandage\",\"effect_list\":[\"absent\"],\"sequential_effect_list\":[[],[\"gain\"]]}]"));
        f.Check(QuantityItemReferenceStatus.SuspectedUnused, "A normal torch callback neither advances nor permits the generic sequence");
        f.Write("scripts/other.raid_settings.json", RuntimeTorch("", "other"));
        f.Check(QuantityItemReferenceStatus.AnalysisIncomplete, "An unverified torch selector cannot prove counter advancement");
        f.Write("raid/flashback/query.flashbacks.json", RuntimeFlashback("\"torch_settings\":\"other\""));
        f.Check(QuantityItemReferenceStatus.ConfirmedActive, "Another torch state can permit the generic sequence to advance the shared counter");

        f = new LootSourceFixture(Path.Combine(root, "torch-activation"), "local");
        f.Write("scripts/raid_settings.json", RuntimeTorch(""));
        f.Write("scripts/other.raid_settings.json", RuntimeTorch("\"hero_buff_list\":[\"trigger\"]", "other"));
        f.Check(QuantityItemReferenceStatus.AnalysisIncomplete, "A loaded custom torch key alone is not a verified activation path");
        path = f.Write("raid/flashback/query.flashbacks.json", RuntimeFlashback("\"torch_settings\":\"other\""));
        f.Check(QuantityItemReferenceStatus.ConfirmedActive, "An admitted flashback selects its authored torch setting");
        File.WriteAllText(path, "{");
        f.Check(QuantityItemReferenceStatus.AnalysisIncomplete, "Failed selector input cannot confirm a custom torch key", globalIncomplete: true);
        f.Write("scripts/raid_settings.json", RuntimeTorch("\"hero_buff_list\":[\"trigger\"]"));
        f.Check(QuantityItemReferenceStatus.ConfirmedActive, "Default torch evidence survives an unrelated unresolved selector", globalIncomplete: true);

        f = new LootSourceFixture(Path.Combine(root, "item-special"), "local");
        path = f.Write("inventory/use.inventory.items.darkest", "");
        foreach (var id in new[] { "torch", "the_blood" })
        {
            File.WriteAllText(path, "inventory_item: .type supply .id " + id + "\n");
            File.WriteAllText(f.Effects, "effect: .name " + id + " .gain_loot query_loot\n");
            f.Check(QuantityItemReferenceStatus.SuspectedUnused, "Dedicated item callback excludes same-name fallback: " + id);
            File.AppendAllText(f.Effects, "effect: .name gain .gain_loot query_loot\n");
            File.WriteAllText(path, "inventory_item: .type supply .id " + id + " .sequential_use_effects gain\n");
            f.Check(QuantityItemReferenceStatus.ConfirmedActive, "Sequential callback precedes dedicated item behavior: " + id);
        }
        File.WriteAllText(f.Effects, "effect: .name gain .gain_loot query_loot\n");
        f.Write("campaign/town/districts/query.districts.json", """{"buildings":[{"name":"runtime","buff_list":[{"type":"DistrictReplacementInventoryEffectBuffData","item_type":"supply","item_id":"bandage","effect":"gain"}]}]}""");
        File.WriteAllText(path, "inventory_item: .type supply .id bandage .sequential_use_effects absent\n");
        f.Check(QuantityItemReferenceStatus.SuspectedUnused, "Sequential callback bypasses district replacement of the fallback");
        File.WriteAllText(path, "inventory_item: .type supply .id bandage .sequential_use_effects " + string.Join(' ', Enumerable.Repeat("absent", 16)) + " gain\n");
        f.Check(QuantityItemReferenceStatus.SuspectedUnused, "Inventory sequence consumes at most sixteen tokens");

        f = new LootSourceFixture(Path.Combine(root, "flashback"), "local");
        const string start = "\"required_heroes_starting_effects\":[\"install\"]";
        path = f.Write("raid/flashback/query.flashbacks.json", RuntimeFlashback(start).Replace("[\"selection\"]", "[\"missing\",\"selection\"]"));
        f.Check(QuantityItemReferenceStatus.ConfirmedActive, "Flashback admission needs any required class, not every class");
        File.WriteAllText(path, RuntimeFlashback(start).Replace("[\"selection\"]", "[]"));
        f.Check(QuantityItemReferenceStatus.SuspectedUnused, "Empty required-class list cannot admit a flashback");
        File.WriteAllText(path, RuntimeFlashback(start.Replace("install", "gain")));
        f.Check(QuantityItemReferenceStatus.SuspectedUnused, "Flashback start is noncombat and cannot directly gain loot");
        var first = System.Text.Json.Nodes.JsonNode.Parse(RuntimeFlashback("\"results\":[]"))!["flashbacks"]![0]!.DeepClone();
        var second = System.Text.Json.Nodes.JsonNode.Parse(RuntimeFlashback(start))!["flashbacks"]![0]!.DeepClone();
        var entries = new System.Text.Json.Nodes.JsonArray(first, second);
        var document = new System.Text.Json.Nodes.JsonObject { ["flashbacks"] = entries };
        File.WriteAllText(path, document.ToJsonString());
        f.Check(QuantityItemReferenceStatus.SuspectedUnused, "Flashback active payload uses the first matching ID");
        second["completed_camping_start_buffs"] = new System.Text.Json.Nodes.JsonArray("trigger");
        second["chance"] = 0;
        File.WriteAllText(path, document.ToJsonString());
        f.Check(QuantityItemReferenceStatus.ConfirmedActive, "Completed-camp Buffs visit all declarations independently of new-start chance");
        File.WriteAllText(path, RuntimeFlashback("\"results\":[{\"type\":\"buff\",\"target_type\":\"not_required_hero\",\"string_data\":\"trigger\"}]"));
        f.Check(QuantityItemReferenceStatus.SuspectedUnused, "Flashback result cannot target a nonexistent non-required class");
        f.Write("heroes/other/other.info.darkest", "weapon: .name blade\narmour: .name coat .hp 20\n");
        f.Check(QuantityItemReferenceStatus.ConfirmedActive, "Available non-required class admits the result target");
        var longBuff = new string('f', 63) + "tail";
        File.WriteAllText(path, RuntimeFlashback("\"completed_camping_start_buffs\":[\"" + longBuff + "\"]"));
        File.WriteAllText(f.Buffs, "{\"buffs\":[{\"id\":\"" + longBuff + "\",\"turn_start_additional_effect_ids\":[\"gain\"]}]}");
        f.Check(QuantityItemReferenceStatus.ConfirmedActive, "Completed-camp Buffs hash the full ID without a display-token truncation");
        File.WriteAllText(f.Buffs, File.ReadAllText(f.Buffs).Replace(longBuff, longBuff[..63]));
        f.Check(QuantityItemReferenceStatus.SuspectedUnused, "Completed-camp Buffs do not match a shorter prefix ID");

        f = new LootSourceFixture(Path.Combine(root, "modifier-id"), "local");
        var modifierPrefix = new string('m', 63);
        path = f.Write("campaign/quest/query.quest.modifiers.json", "{\"modifiers\":[{\"id\":\"" + modifierPrefix +
            "A\",\"condition_type\":\"always\",\"hero_buffs\":[\"trigger\"]},{\"id\":\"" + modifierPrefix + "B\",\"condition_type\":\"always\"}]}");
        f.Check(QuantityItemReferenceStatus.SuspectedUnused, "Modifier IDs collide after native 63-byte truncation and keep the last payload");

        f = new LootSourceFixture(Path.Combine(root, "independent-libraries"), "local");
        var torch = f.Write("scripts/raid_settings.json", RuntimeTorch("\"hero_buff_list\":[\"trigger\"]"));
        foreach (var (library, restored) in new[]
        {
            ("shared/quirk/broken.quirk_library.json", "{\"quirks\":[]}"),
            ("trinkets/broken.entries.trinkets.json", "{\"entries\":[]}"),
            ("raid/camping/broken.camping_skills.json", "{\"skills\":[]}"),
            ("campaign/town_events/broken.town_events.events.json", "{\"events\":[]}")
        })
        {
            var broken = f.Write(library, "{");
            f.Check(QuantityItemReferenceStatus.ConfirmedActive, "An unrelated source library cannot revoke verified torch loot: " + library, globalIncomplete: true);
            File.WriteAllText(torch, "{}");
            f.Check(QuantityItemReferenceStatus.AnalysisIncomplete, "Unknown source contents still prevent an unused verdict: " + library, globalIncomplete: true);
            File.WriteAllText(torch, RuntimeTorch("\"hero_buff_list\":[\"trigger\"]"));
            File.WriteAllText(broken, restored);
            f.Check(QuantityItemReferenceStatus.ConfirmedActive, "Source-library read recovery: " + library);
        }

        f = new LootSourceFixture(Path.Combine(root, "town"), "local");
        path = f.Write("campaign/town/buildings/tavern/tavern.building.json", RuntimeActivity(id: "invented"));
        f.Check(QuantityItemReferenceStatus.SuspectedUnused, "Native activity factory ignores arbitrary IDs");
        File.WriteAllText(path, RuntimeActivity(chance: 0));
        f.Check(QuantityItemReferenceStatus.SuspectedUnused, "Zero-chance side effects cannot apply a Buff");
        File.WriteAllText(path, RuntimeActivity().Replace("apply_buff", "invented"));
        f.Check(QuantityItemReferenceStatus.SuspectedUnused, "Only ApplyBuff side effects consume the Buff list");

        f = new LootSourceFixture(Path.Combine(root, "trap-structure"), "local");
        path = f.Write("props/crypts/trap_definitions.json", "{}");
        foreach (var invalid in new[] { "{\"props\":{}}", "{\"props\":[42]}", "{\"default_data\":{\"success_effects\":{}}}", """{"props":[{"name":"\uD800"}]}""" })
        {
            File.WriteAllText(path, invalid);
            f.Check(QuantityItemReferenceStatus.AnalysisIncomplete, "Malformed prop records must not certify an unused item", globalIncomplete: true);
        }
        File.WriteAllText(path, "{}");
        f.Check(QuantityItemReferenceStatus.SuspectedUnused, "Repairing prop structure restores completeness");

        f = new LootSourceFixture(Path.Combine(root, "bounded"), "local");
        var prefix = new string('b', 127);
        File.WriteAllText(f.Buffs, "{\"buffs\":[{\"id\":\"" + prefix + "\",\"turn_start_additional_effect_ids\":[\"gain\"]}]}");
        f.Write("scripts/raid_settings.json", RuntimeTorch("\"hero_buff_list\":[\"" + prefix + "suffix\"]"));
        f.Check(QuantityItemReferenceStatus.ConfirmedActive, "Torch Buff IDs are bounded to 127 bytes before lookup");
        f.Write("scripts/raid_settings.json", "{}");
        prefix = new string('e', 63);
        File.WriteAllText(f.Effects, "effect: .name " + prefix + " .gain_loot query_loot\n");
        f.Write("shared/trait/query.trait_library.json", "{\"traits\":[{\"id\":\"test\"," + RuntimeActOut(effect: prefix + "suffix") + "}]}");
        f.Check(QuantityItemReferenceStatus.ConfirmedActive, "Act-out Effect IDs are bounded to 63 bytes before lookup");
        Console.WriteLine("PASS: native runtime admission, first/append behavior, callback priority and byte/slot boundaries.");
    }
}
