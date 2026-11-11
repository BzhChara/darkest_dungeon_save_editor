internal static partial class ContractSuite
{
    private sealed class LootSourceFixture
    {
        internal SelectionFixture Files { get; }
        internal string Hero { get; }
        internal string OriginalHero { get; }
        internal string Effects { get; }
        internal string Buffs { get; }
        private readonly JsonObject raid = JsonNode.Parse("""{"base_root":{"party":{"inventory":{"items":{}}}}}""")!.AsObject();

        internal LootSourceFixture(string root, string kind)
        {
            Files = HeroSelectionContent(root, kind, 1);
            Write("inventory/query.inventory.items.darkest",
                "inventory_item: .type estate .id query_token .base_stack_limit 2\ninventory_item: .type estate .id orphan_token .base_stack_limit 2\n");
            Write("loot/query.loot.json", QueryLootJson);
            Effects = Write("effects/query.effects.darkest", "effect: .name gain .gain_loot query_loot\neffect: .name install .buff_ids trigger\n");
            Buffs = Write("shared/buffs/query.buffs.json", """{"buffs":[{"id":"trigger","turn_start_additional_effect_ids":["gain"]}]}""");
            Hero = Path.Combine(Files.Source, "heroes/selection/selection.info.darkest");
            OriginalHero = File.ReadAllText(Hero);
        }

        internal string Write(string relative, string text) => SelectionResource(
            relative.StartsWith("heroes/", StringComparison.Ordinal) || relative.StartsWith("monsters/", StringComparison.Ordinal)
                ? Files with { Prefix = "" } : Files, relative, text);
        internal void Actor(string text) => File.WriteAllText(Hero, OriginalHero + text);
        internal void Check(QuantityItemReferenceStatus expected, string label, bool globalIncomplete = false)
        {
            var items = QuantityItemCatalog.LoadRaid(Files.Content, raid).Items;
            var item = items.Single(i => i.ItemId == "query_token");
            Assert(item.ReferenceStatus == expected && item.IsHiddenByDefault == (expected == QuantityItemReferenceStatus.SuspectedUnused),
                $"{Files.Source}: {label}; expected {expected}, got {item.ReferenceStatus}, hidden={item.IsHiddenByDefault}");
            var orphan = items.Single(i => i.ItemId == "orphan_token");
            Assert(orphan.ReferenceStatus == (globalIncomplete ? QuantityItemReferenceStatus.AnalysisIncomplete : QuantityItemReferenceStatus.SuspectedUnused),
                label + ": unrelated orphan retains the appropriate completeness policy");
        }
    }

    private static void VerifyNativeLootBuffSources(string root)
    {
        foreach (var kind in new[] { "local", "workshop", "dlc-mod" })
        {
            VerifyDeathDoorLootSources(Path.Combine(root, kind, "death-door"), kind);
            VerifyTraitLootSources(Path.Combine(root, kind, "traits"), kind);
            VerifyDistrictLootSources(Path.Combine(root, kind, "districts"), kind);
            VerifyTrinketSetLootSources(Path.Combine(root, kind, "sets"), kind);
            VerifyLootSourceCompleteness(Path.Combine(root, kind, "completeness"), kind);
        }
        VerifyLootSourceProviderRules(Path.Combine(root, "providers"));
    }

    private static void VerifyDeathDoorLootSources(string root, string kind)
    {
        var f = new LootSourceFixture(root, kind);
        foreach (var (field, target) in new[]
        {
            ("enter_effects", "gain"), ("enter_effects", "install"), ("buffs", "trigger"),
            ("recovery_buffs", "trigger"), ("recovery_heart_attack_buffs", "trigger")
        })
        {
            f.Actor($"deaths_door: .{field} {string.Join(' ', Enumerable.Repeat("absent", 15))} {target}\n");
            f.Check(QuantityItemReferenceStatus.ConfirmedActive, field + ": sixteenth native slot is consumed");
            f.Actor($"deaths_door: .{field} {string.Join(' ', Enumerable.Repeat("absent", 16))} {target}\n");
            f.Check(QuantityItemReferenceStatus.SuspectedUnused, field + ": seventeenth slot is ignored");
            f.Actor($"deaths_door: .{field} \"\" {target}\n");
            f.Check(QuantityItemReferenceStatus.SuspectedUnused, field + ": empty token terminates the list");
        }
        var bounded = new string('b', 63);
        File.AppendAllText(f.Effects, $"effect: .name {bounded} .gain_loot query_loot\n");
        f.Actor($"deaths_door: .enter_effects {bounded}tail\n");
        f.Check(QuantityItemReferenceStatus.ConfirmedActive, "Death-door references use a 63-byte payload");
        f.Actor("deaths_door: .buffs trigger\ndeaths_door: .buffs \"\"\n");
        f.Write("heroes/selection/selection.override.darkest", "deaths_door: .buffs \"\"\n");
        f.Check(QuantityItemReferenceStatus.ConfirmedActive, "Repeated records and override files append, without clearing earlier buffs");
        f.Actor("");
        f.Write("heroes/selection/selection.art.darkest", "deaths_door: .buffs trigger\n");
        f.Check(QuantityItemReferenceStatus.SuspectedUnused, "Art files do not load actor-class death-door records");
        f.Actor("unknown_record: .buffs trigger\n");
        f.Check(QuantityItemReferenceStatus.SuspectedUnused, "Arbitrary Buff-looking text is not an actor consumer");
        Console.WriteLine($"PASS: {kind} death-door Effect/Buff roots, native bounds, append semantics and canonical files.");
    }

    private static void VerifyTraitLootSources(string root, string kind)
    {
        var f = new LootSourceFixture(root, kind);
        var file = f.Write("shared/trait/query.trait_library.json", "");
        const string active = """{"id":"courage","overstress_type":"virtue","is_generated":false,"buff_ids":["trigger"]}""";
        const string empty = """{"id":"courage","overstress_type":"virtue","buff_ids":[]}""";
        File.WriteAllText(file, "{\"traits\":[" + active + "]}");
        f.Check(QuantityItemReferenceStatus.ConfirmedActive, "An assigned non-generated virtue still applies Buffs");
        File.WriteAllText(file, "{\"traits\":[" + active.Replace("virtue", "affliction") + "]}");
        f.Check(QuantityItemReferenceStatus.ConfirmedActive, "Afflictions share the trait Buff consumer");
        File.WriteAllText(file, "{\"traits\":[" + active + "," + empty + "]}");
        f.Check(QuantityItemReferenceStatus.SuspectedUnused, "The last matching trait hash replaces the Buff payload");
        File.WriteAllText(file, "{\"traits\":[" + empty + "," + active + "]}");
        f.Check(QuantityItemReferenceStatus.ConfirmedActive, "A later trait can supply the runtime Buff payload");
        var prefix = new string('i', 63);
        File.WriteAllText(file, "{\"traits\":[" + active.Replace("courage", prefix + "A") + "," + empty.Replace("courage", prefix + "B") + "]}");
        f.Check(QuantityItemReferenceStatus.ConfirmedActive, "Trait identity hashes the full C string, not its bounded display name");
        File.WriteAllText(file, "{\"traits\":[],\"traits\":[" + active + "]}");
        f.Check(QuantityItemReferenceStatus.SuspectedUnused, "Native JSON lookup uses the first duplicate member");
        File.WriteAllText(file, """{"traits":[{"id":"courage","buffs":["trigger"]}]}""");
        f.Check(QuantityItemReferenceStatus.SuspectedUnused, "Trinket-style buffs is not a trait buff_ids alias");
        File.WriteAllText(file, "{\"traits\":[]}");
        var network = f.Write("shared/trait/prefix_network_trait_library.json", "{\"traits\":[" + active + "]}");
        f.Check(QuantityItemReferenceStatus.SuspectedUnused, "The network_trait_library substring routes into the multiplayer library");
        File.WriteAllText(network, "{");
        f.Check(QuantityItemReferenceStatus.SuspectedUnused, "Unreadable multiplayer-only payload does not poison campaign analysis");
        f.Write("shared/trait/nested/queryXtrait_library.json", "{\"traits\":[" + active + "]}");
        f.Check(QuantityItemReferenceStatus.ConfirmedActive, "Trait discovery is recursive and does not require a dot before trait_library");
        Console.WriteLine($"PASS: {kind} affliction/virtue roots, last-hash lookup, first JSON members and network exclusion.");
    }

    private static string DistrictDocument(string tags = "[]", string buffs = "[\"trigger\"]", string name = "test",
        string type = "DistrictHeroBuffData", bool enabled = true) =>
        "{\"enabled\":" + (enabled ? "true" : "false") + ",\"buildings\":[{\"name\":\"" + name +
        "\",\"buff_list\":[{\"type\":\"" + type + "\",\"hero_type_tags\":" + tags + ",\"buff_list\":" + buffs + "}]}]}";

    private static void VerifyDistrictLootSources(string root, string kind)
    {
        var f = new LootSourceFixture(root, kind);
        var file = f.Write("campaign/town/districts/query.districts.json", DistrictDocument());
        f.Check(QuantityItemReferenceStatus.ConfirmedActive, "Empty district tags accept heroes");
        File.WriteAllText(file, DistrictDocument(enabled: false));
        f.Check(QuantityItemReferenceStatus.ConfirmedActive, "Disabled UI does not erase potential already-built district rewards");
        File.WriteAllText(file, DistrictDocument("[\"religious\",\"heavy\"]"));
        f.Check(QuantityItemReferenceStatus.SuspectedUnused, "No matching hero tag means no district Buff source");
        f.Actor("tag: .id heavy\n");
        f.Check(QuantityItemReferenceStatus.ConfirmedActive, "District tags match ANY tag, not all tags");
        f.Actor("");
        f.Write("heroes/selection/selection.override.darkest", "tag: .id religious\n");
        f.Check(QuantityItemReferenceStatus.ConfirmedActive, "Hero tags append through the canonical override");
        File.WriteAllText(file, DistrictDocument(type: "DistrictBankData"));
        f.Check(QuantityItemReferenceStatus.SuspectedUnused, "Other district data classes do not load hero Buff lists");
        var longId = new string('t', 63);
        f.Actor("tag: .id " + longId + "tail\n");
        File.WriteAllText(f.Buffs, "{\"buffs\":[{\"id\":\"" + longId + "\",\"turn_start_additional_effect_ids\":[\"gain\"]}]}");
        File.WriteAllText(file, DistrictDocument("[\"" + longId + "different\"]", "[\"" + longId + "suffix\"]"));
        f.Check(QuantityItemReferenceStatus.ConfirmedActive, "District runtime bounds both tag and Buff lookups to 63 bytes");
        var document = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
        document["buildings"]!.AsArray().Add(new JsonObject { ["name"] = "test" });
        File.WriteAllText(file, document.ToJsonString());
        f.Check(QuantityItemReferenceStatus.SuspectedUnused, "Later duplicate building clears earlier Buff payload even when omitted");
        document["buildings"]!.AsArray()[1]!["name"] = "Test";
        File.WriteAllText(file, document.ToJsonString());
        f.Check(QuantityItemReferenceStatus.ConfirmedActive, "Building names use case-sensitive string identity");
        Console.WriteLine($"PASS: {kind} district hero Buffs, any-tag filtering, bounded lookup and full duplicate replacement.");
    }

    private static void VerifyTrinketSetLootSources(string root, string kind)
    {
        var f = new LootSourceFixture(root, kind);
        var sets = f.Write("trinkets/query.sets.trinkets.json", """{"sets":[{"id":"pair","buffs":["trigger"]}]}""");
        const string a = """{"id":"a","set_id":"pair","buffs":[],"hero_class_requirements":["selection"]}""";
        const string b = """{"id":"b","set_id":"pair","buffs":[],"hero_class_requirements":["selection"]}""";
        var entries = f.Write("trinkets/query.entries.trinkets.json", "{\"entries\":[" + a + "]}");
        f.Check(QuantityItemReferenceStatus.SuspectedUnused, "A set definition and one member cannot activate its Buffs");
        File.WriteAllText(entries, "{\"entries\":[" + a + "," + a + "]}");
        f.Check(QuantityItemReferenceStatus.SuspectedUnused, "Duplicate item IDs cannot form the two distinct native members");
        File.WriteAllText(entries, "{\"entries\":[" + a + "," + b + "]}");
        f.Check(QuantityItemReferenceStatus.ConfirmedActive, "Two valid compatible members activate the set Buffs");
        File.WriteAllText(entries, "{\"entries\":[" + a + "," + b.Replace("[]", "[\"missing_buff\"]") + "]}");
        f.Check(QuantityItemReferenceStatus.SuspectedUnused, "A member rejected by native Buff admission cannot activate a set");
        File.WriteAllText(entries, "{\"entries\":[" + a + "," + b.Replace("[]", "[\"missing_buff\"]") + "," + b + "]}");
        f.Check(QuantityItemReferenceStatus.ConfirmedActive, "Rejected earlier IDs do not shadow a valid later member");
        f.Write("heroes/other/other.info.darkest", "weapon: .name blade\narmour: .name coat .hp 20\n");
        File.WriteAllText(entries, "{\"entries\":[" + a + "," + b.Replace("selection", "other") + "]}");
        f.Check(QuantityItemReferenceStatus.SuspectedUnused, "Two valid but incompatible hero restrictions cannot form a pair");
        File.WriteAllText(entries, "{\"entries\":[" + a + "," + b.Replace("[\"selection\"]", "[]") + "]}");
        f.Check(QuantityItemReferenceStatus.ConfirmedActive, "An unrestricted member can pair with a class-restricted member");
        File.WriteAllText(sets, """{"sets":[{"id":"pair","buffs":["trigger"]},{"id":"pair"}]}""");
        f.Check(QuantityItemReferenceStatus.SuspectedUnused, "A later set ID clears earlier Buffs when its list is omitted");
        File.WriteAllText(sets, """{"sets":[{"id":"pair"},{"id":"pair","buffs":["missing_buff","trigger"]}]}""");
        f.Check(QuantityItemReferenceStatus.ConfirmedActive, "Set loading skips a missing Buff without rejecting its other Buffs");
        File.WriteAllText(sets, """{"sets":[{"id":"pair","buffs":[],"buffs":["trigger"]}]}""");
        f.Check(QuantityItemReferenceStatus.SuspectedUnused, "The first JSON Buff member wins inside a set");
        var prefix = new string('s', 63);
        File.WriteAllText(entries, ("{\"entries\":[" + a + "," + b + "]}").Replace("pair", prefix + "A"));
        File.WriteAllText(sets, "{\"sets\":[{\"id\":\"" + prefix + "A\",\"buffs\":[\"trigger\"]},{\"id\":\"" + prefix + "B\",\"buffs\":[]}]}");
        f.Check(QuantityItemReferenceStatus.ConfirmedActive, "Set and member lookup use full-string hashes beyond the display-name bound");
        Console.WriteLine($"PASS: {kind} set library, distinct compatible members, entry admission and last-set replacement.");
    }

    private static void VerifyLootSourceCompleteness(string root, string kind)
    {
        var f = new LootSourceFixture(root, kind);
        f.Actor("last_hero: .buffs trigger\n");
        f.Check(QuantityItemReferenceStatus.ConfirmedActive, "The last living actor has a verified native Buff application path");
        f.Actor("last_hero: .buffs trigger\ncrit: .effects gain\n");
        f.Check(QuantityItemReferenceStatus.ConfirmedActive, "An uncertain route must not suppress independent verified evidence");
        f.Actor("");
        f.Write("monsters/probe/probe_A/probe_A.info.darkest", "deaths_door: .enter_effects install\n");
        f.Check(QuantityItemReferenceStatus.AnalysisIncomplete, "Unverified monster death-door activation propagates through Effect/Buff links");
        f.Write("monsters/probe/probe_A/probe_A.info.darkest", "");
        foreach (var (path, broken, restored) in new[]
        {
            ("shared/trait/query.trait_library.json", "{", "{\"traits\":[]}"),
            ("shared/trait/query.trait_library.json", "{\"traits\":[{\"id\":\"x\",\"buff_ids\":{}}]}", "{\"traits\":[]}"),
            ("shared/trait/query.trait_library.json", """{"traits":[{"id":"x","buff_ids":["\uD800"]}]}""", "{\"traits\":[]}"),
            ("shared/trait/query.trait_library.json", """{"traits":[{"id":"\uD800","buff_ids":["trigger"]}]}""", "{\"traits\":[]}"),
            ("trinkets/query.sets.trinkets.json", "{\"sets\":{}}", "{\"sets\":[]}"),
            ("campaign/town/districts/query.districts.json", DistrictDocument(tags: "{}"), "{\"buildings\":[]}")
        })
        {
            var file = f.Write(path, broken);
            f.Check(QuantityItemReferenceStatus.AnalysisIncomplete, "Malformed source cannot certify an unused item: " + path, globalIncomplete: true);
            f.Actor("deaths_door: .enter_effects gain\n");
            f.Check(QuantityItemReferenceStatus.ConfirmedActive, "An independent death-door path survives an unrelated broken library: " + path, globalIncomplete: true);
            f.Actor("");
            File.WriteAllText(file, restored);
            f.Check(QuantityItemReferenceStatus.SuspectedUnused, "Restoring source completeness restores orphan filtering: " + path);
        }
        var missing = f.Write("shared/trait/missing.trait_library.json", "{\"traits\":[]}");
        File.Delete(missing);
        f.Check(QuantityItemReferenceStatus.AnalysisIncomplete, "Missing manifest-listed source cannot certify graph completeness", globalIncomplete: true);
        File.WriteAllText(missing, "{\"traits\":[]}");
        var trait = f.Write("shared/trait/query.trait_library.json", """{"traits":[{"id":"courage","buff_ids":["trigger"]}]}""");
        var sets = f.Write("trinkets/query.sets.trinkets.json", "{");
        f.Check(QuantityItemReferenceStatus.ConfirmedActive, "An unreadable unrelated set library cannot revoke a verified trait route", globalIncomplete: true);
        File.WriteAllText(trait, "{");
        File.WriteAllText(sets, """{"sets":[{"id":"pair","buffs":["trigger"]}]}""");
        f.Write("trinkets/query.entries.trinkets.json", """{"entries":[{"id":"a","set_id":"pair","buffs":[]},{"id":"b","set_id":"pair","buffs":[]}]}""");
        f.Check(QuantityItemReferenceStatus.ConfirmedActive, "An earlier unreadable trait library cannot revoke a later verified set route", globalIncomplete: true);
        var anotherSet = f.Write("trinkets/unreadable.sets.trinkets.json", "{");
        File.WriteAllText(trait, "{\"traits\":[]}");
        f.Check(QuantityItemReferenceStatus.AnalysisIncomplete, "Unreadable definitions in the SAME set library may replace the candidate route", globalIncomplete: true);
        File.WriteAllText(anotherSet, "{\"sets\":[]}");
        File.WriteAllText(sets, "{\"sets\":[]}");
        // A manifest-ineligible physical payload is not a source, even if broken.
        WriteMultiMash(f.Files.Source, f.Files.Prefix + "shared/trait/unlisted.trait_library.json", "{");
        f.Check(QuantityItemReferenceStatus.SuspectedUnused, "Unlisted Mod files do not taint graph completeness");
        Console.WriteLine($"PASS: {kind} loot graph uncertainty, verified-route precedence, read failures and recovery.");
    }

    private static void VerifyLootSourceProviderRules(string root)
    {
        foreach (var kind in new[] { "local", "workshop" })
        {
            var f = new LootSourceFixture(Path.Combine(root, kind), kind);
            var baseline = f.Files.Content.Sources.Single(s => s.Id == "base").Directory;
            const string trait = """{"traits":[{"id":"test","buff_ids":["trigger"]}]}""";
            var lowTrait = WriteMultiMash(baseline, "shared/trait/query.trait_library.json", trait);
            var highTrait = f.Write("shared/trait/query.trait_library.json", "{\"traits\":[]}");
            f.Check(QuantityItemReferenceStatus.SuspectedUnused, "Same-path Mod trait provider replaces base before library parsing");
            File.WriteAllText(lowTrait, "{");
            File.WriteAllText(highTrait, trait);
            f.Check(QuantityItemReferenceStatus.ConfirmedActive, "A malformed shadowed provider cannot downgrade the effective trait");
            var lateTrait = f.Write("shared/trait/z.trait_library.json", """{"traits":[{"id":"test","buff_ids":[]}]}""");
            f.Check(QuantityItemReferenceStatus.SuspectedUnused, "Trait hash replacement also applies across ordered files");
            File.WriteAllText(lateTrait, "{\"traits\":[]}");
            File.WriteAllText(highTrait, "{\"traits\":[]}");
            var lowDistrict = WriteMultiMash(baseline, "campaign/town/districts/query.districts.json", DistrictDocument());
            var highDistrict = f.Write("campaign/town/districts/query.districts.json", "{\"buildings\":[]}");
            f.Check(QuantityItemReferenceStatus.ConfirmedActive, "District flags-9 retains base definitions behind the same Mod path");
            File.WriteAllText(highDistrict, """{"buildings":[{"name":"test","buff_list":[]}]}""");
            f.Check(QuantityItemReferenceStatus.SuspectedUnused, "A later district with the same name replaces the entire lower payload");
            File.WriteAllText(lowDistrict, "{\"buildings\":[]}");
            f.Write("trinkets/query.entries.trinkets.json", """{"entries":[{"id":"a","set_id":"pair","buffs":[]},{"id":"b","set_id":"pair","buffs":[]}]}""");
            var lowSet = WriteMultiMash(baseline, "trinkets/query.sets.trinkets.json", """{"sets":[{"id":"pair","buffs":["trigger"]}]}""");
            var highSet = f.Write("trinkets/query.sets.trinkets.json", "{\"sets\":[]}");
            f.Check(QuantityItemReferenceStatus.SuspectedUnused, "Set flags-0 replaces the same-path base provider");
            File.WriteAllText(lowSet, "{");
            File.WriteAllText(highSet, """{"sets":[{"id":"pair","buffs":["trigger"]}]}""");
            f.Check(QuantityItemReferenceStatus.ConfirmedActive, "Malformed shadowed sets do not taint the effective graph");
            File.WriteAllText(highSet, "{\"sets\":[]}");
            f.Write("trinkets/queryXsets.trinkets.json", """{"sets":[{"id":"pair","buffs":["trigger"]}]}""");
            f.Check(QuantityItemReferenceStatus.SuspectedUnused, "The first dot in the native set suffix is literal");
            f.Write("trinkets/nested/query.setsXtrinketsXjson", """{"sets":[{"id":"pair","buffs":["trigger"]}]}""");
            f.Check(QuantityItemReferenceStatus.ConfirmedActive, "Other set suffix dots retain native regex wildcard semantics and recursion");
        }
        Console.WriteLine("PASS: trait/set replacement versus additive districts, shadowed failures, cross-file identities and native set queries.");
    }
}
