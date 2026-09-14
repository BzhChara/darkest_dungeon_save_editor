internal static partial class ContractSuite
{
    private static void VerifyJsonReferenceConsumers(ActiveContentSnapshot original, string runRoot)
    {
        var root = Path.Combine(runRoot, "json-reference-consumers");
        var definitions = Path.Combine(root, "definitions");
        WriteMultiMash(definitions, "inventory/jc.inventory.items.darkest",
            "inventory_item: .type estate .id jc_token .base_stack_limit 6 .estate_can_be_provision false\n");
        WriteFixtureManifest(definitions);
        var definitionSource = new ActiveContentSource("local:jc-definitions", "Reference definitions", "local", definitions, -2700);
        var town = JsonNode.Parse("""{"base_root":{"wallet":{},"estate_items":{"items":{}}}}""")!.AsObject();
        var raid = JsonNode.Parse("""{"base_root":{"party":{"inventory":{"items":{}}}}}""")!.AsObject();
        const string item = """{"type":"estate","id":"jc_token","amount":1}""";
        var inventory = "{\"items\":{\"0\":" + item + "}}";
        var plot = "{\"plot_quests\":[{\"quest\":{\"completion_reward\":{\"items_definition\":" + inventory + "}}}]}";
        var provision = "{\"raid_starting_hero_class_item_lists\":[{\"hero_class\":\"test\",\"item_lists\":[" + item + "]}]}";
        var eventText = """{"events":[{"id":"jc_event","data":[{"type":"bonus_currency","string_data":"jc_token"}]}]}""";
        var cases = new (string Name, string Path, string Text, bool Town, bool Raid, bool Uncertain)[]
        {
            ("quest-notes-file", "campaign/quest/notes.json", plot, false, false, false),
            ("quest-root-notes", "campaign/quest/jc.quest.plot_quests.json", "{\"plot_quests\":[],\"notes\":" + item + "}", false, false, false),
            ("quest-child-notes", "campaign/quest/jc.quest.plot_quests.json", "{\"plot_quests\":[{\"notes\":" + item + "}]}", false, false, false),
            ("quest-reward", "campaign/quest/nested/jc.quest.plot_quests.json", plot, true, false, false),
            ("quest-native-regex", "campaign/quest/jc.questXplot_questsXjson", plot, true, false, false),
            ("quest-wrong-root", "heroes/jc.quest.plot_quests.json", plot, false, false, false),
            ("quest-first-array", "campaign/quest/jc.quest.plot_quests.json", "{\"plot_quests\":[]," + plot[1..], false, false, false),
            ("quest-first-null", "campaign/quest/jc.quest.plot_quests.json", "{\"plot_quests\":null," + plot[1..], false, false, false),
            ("quest-field-notes", "campaign/quest/jc.quest.plot_quests.json", "{\"plot_quests\":[{\"quest\":{\"completion_reward\":" + item + "}}]}", false, false, false),
            ("quest-slot-notes", "campaign/quest/jc.quest.plot_quests.json", plot.Replace("\"0\":", "\"notes\":"), false, false, false),
            ("quest-provisions", "campaign/quest/jc.quest.plot_quests.json", "{\"plot_quests\":[{\"additional_provisions\":" + inventory + "}]}", false, true, false),
            ("quest-goal-start", "campaign/quest/jc.quest.types.json", "{\"goals\":[{\"starting_items\":[" + item + "]}]}", false, true, false),
            ("quest-generation", "campaign/quest/jc.quest.generation.json", "{\"generation\":{\"rewards\":{\"item_table\":[[[" + item + "]]]}}}", true, false, false),
            ("provision-hero", "campaign/provision/jc.provision.json", provision, false, true, false),
            ("provision-native-regex", "campaign/provision/jcXprovisionXjson", provision, false, true, false),
            ("provision-unconsumed", "campaign/provision/notes.json", provision, false, false, false),
            ("provision-notes", "campaign/provision/jc.provision.json", "{\"notes\":" + item + "}", false, false, false),
            ("event-notes", "campaign/town_events/jc.town_events.events.json", "{\"events\":[],\"notes\":" + item + "}", false, false, false),
            ("event-first-array", "campaign/town_events/jc.town_events.events.json", "{\"events\":[]," + eventText[1..], false, false, false),
            ("event-good", "campaign/town_events/jc.town_events.events.json", eventText, true, false, false),
            ("event-cost", "campaign/town_events/jc.town_events.events.json", eventText.Replace("bonus_currency", "event_cost"), true, false, false),
            ("event-upper", "campaign/town_events/jc.town_events.events.json", eventText.Replace("bonus_currency", "BONUS_CURRENCY"), false, false, false),
            ("event-space", "campaign/town_events/jc.town_events.events.json", eventText.Replace("bonus_currency", " bonus_currency "), false, false, false),
            ("event-space-id", "campaign/town_events/jc.town_events.events.json", eventText.Replace("jc_token", " jc_token "), false, false, false),
            ("event-nul", "campaign/town_events/jc.town_events.events.json", eventText.Replace("bonus_currency", "bonus_currency\\u0000ignored").Replace("jc_token", "jc_token\\u0000ignored"), true, false, false),
            ("event-first-type", "campaign/town_events/jc.town_events.events.json", eventText.Replace("\"type\":", "\"type\":null,\"type\":"), false, false, false),
            ("event-first-result", "campaign/town_events/jc.town_events.events.json", eventText.Replace("\"events\":[", "\"events\":[{\"id\":\"jc_event\",\"data\":[]},"), false, false, false),
            ("estate-currency", "campaign/estate/jc.estate.json", """{"currencies":[{"id":"jc_token"}]}""", true, false, false),
            ("upgrade-cost", "upgrades/building/jc.upgrades.json", """{"trees":[{"requirements":[{"currency_cost":[{"type":"jc_token","amount":1}]}]}]}""", true, false, false),
            ("district-estate", "campaign/town/districts/jc.districts.json", """{"buildings":[{"buff_list":[{"type":"DistrictSupplyBuffData","target_inventory":"estate","item_type":"estate","item_name":"jc_token","range_min":1,"range_max":1}]}]}""", true, false, false),
            ("district-provision", "campaign/town/districts/jc.districts.json", """{"buildings":[{"buff_list":[{"type":"DistrictSupplyBuffData","target_inventory":"provision","item_type":"estate","item_name":"jc_token","range_min":1,"range_max":1}]}]}""", false, true, false),
            ("district-replacement", "campaign/town/districts/jc.districts.json", """{"buildings":[{"buff_list":[{"type":"DistrictReplacementInventoryEffectBuffData","item_type":"estate","item_id":"jc_token"}]}]}""", false, true, false),
            ("district-notes", "campaign/town/districts/jc.districts.json", "{\"buildings\":[],\"notes\":" + item + "}", false, false, false),
            ("building-unknown-data", "campaign/town/buildings/stage_coach/jc.stage_coach.building.json", "{\"data\":{\"unsupported_item_consumer\":" + item + "}}", false, false, true),
            ("building-escaped-data", "campaign/town/buildings/stage_coach/jc.stage_coach.building.json", "{\"data\":{\"unsupported_item_consumer\":" + item.Replace("jc_token", "jc_\\u0074oken") + "}}", false, false, true)
        };
        foreach (var entry in cases)
        {
            var resources = Path.Combine(root, entry.Name);
            WriteMultiMash(resources, entry.Path, entry.Text); WriteFixtureManifest(resources);
            var sources = new[] { definitionSource, new ActiveContentSource("local:jc", "Reference roots", "local", resources, 0) };
            var content = original with { Sources = sources };
            foreach (var isTown in new[] { true, false })
            {
                var result = isTown ? QuantityItemCatalog.Load(content, town) : QuantityItemCatalog.LoadRaid(content, raid);
                var row = result.Items.Single(i => i.ItemId == "jc_token");
                var active = isTown ? entry.Town : entry.Raid;
                var expected = active ? QuantityItemReferenceStatus.ConfirmedActive : entry.Uncertain && isTown
                    ? QuantityItemReferenceStatus.AnalysisIncomplete : QuantityItemReferenceStatus.SuspectedUnused;
                Assert(row.ReferenceStatus == expected && row.IsHiddenByDefault == (expected == QuantityItemReferenceStatus.SuspectedUnused),
                    $"{entry.Name}/{(isTown ? "town" : "raid")}: expected {expected}, found {row.ReferenceStatus} ({string.Join("; ", row.ReferenceEvidence)}).");
            }
            if (entry.Path.EndsWith("Xjson", StringComparison.Ordinal))
            {
                var fingerprint = ProfileCatalogContentFingerprint.Capture(sources);
                File.AppendAllText(Path.Combine(resources, entry.Path), " ");
                Assert(ProfileCatalogContentFingerprint.Capture(sources) != fingerprint,
                    "Newly recognized native query names must invalidate the shared catalog refresh fingerprint.");
            }
        }

        // Keep graph traversal covered with a real actor consumer, rather than
        // the old fictitious DistrictSupplyBuffData.loot_table_code fixture.
        var graph = Path.Combine(root, "loot-graph");
        WriteMultiMash(graph, "loot/jc.loot.json", """
            {"loot_tables":[{"id":"jc_root","entries":[{"type":"table","chances":1,"data":{"table":"jc_nested"}}]},
              {"id":"jc_nested","entries":[{"type":"item","chances":1,"data":{"type":"estate","id":"jc_token"}}]}]}
            """);
        WriteMultiMash(graph, "heroes/jc/jc.info.darkest", "extra_battle_loot: .code jc_root\n");
        WriteFixtureManifest(graph);
        var graphContent = original with { Sources = [definitionSource, new ActiveContentSource("local:jc-graph", "Reference graph", "local", graph, 0)] };
        var graphItem = QuantityItemCatalog.LoadRaid(graphContent, raid).Items.Single(i => i.ItemId == "jc_token");
        Assert(graphItem.ReferenceStatus == QuantityItemReferenceStatus.ConfirmedActive &&
               graphItem.ReferenceEvidence.Any(e => e.Contains("jc_root") && e.Contains("jc_nested")),
            "Real consumer roots must still traverse nested loot tables and retain the source chain.");
        WriteMultiMash(graph, "campaign/town/buildings/stage_coach/jc.stage_coach.building.json", """{"data":{"unverified_loot":"jc_\u0072oot"}}""");
        WriteFixtureManifest(graph);
        var unknownGraph = QuantityItemCatalog.Load(graphContent, town).Items.Single(i => i.ItemId == "jc_token");
        Assert(unknownGraph.ReferenceStatus == QuantityItemReferenceStatus.AnalysisIncomplete &&
               unknownGraph.ReferenceEvidence.Any(e => e.Contains("jc_root") && e.Contains("jc_nested")),
            "Escaped codes in valid unverified JSON must preserve uncertainty through nested loot without becoming confirmed roots.");
        Console.WriteLine($"PASS: {cases.Length * 2} JSON consumer/context cases, nonliteral-name refresh and real actor-to-nested-loot reachability.");
    }
}
