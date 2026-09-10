internal static partial class ContractSuite
{
    private static void VerifyReferenceFileEligibility(ActiveContentSnapshot original, HeroClassDefinition originalHero, string runRoot)
    {
        var root = Path.Combine(runRoot, "reference-file-eligibility");
        var definitions = Path.Combine(root, "definitions");
        WriteMultiMash(definitions, "inventory/rf.inventory.items.darkest",
            "inventory_item: .type estate .id rf_token .base_stack_limit 3 .estate_can_be_provision false\n");
        WriteMultiMash(definitions, "inventory/rf.inventory.system_configs.darkest", "inventory_system_config: .type raid .max_slots 16\n");
        WriteFixtureManifest(definitions);
        var definitionsSource = new ActiveContentSource("local:rf-defs", "Reference definitions", "local", definitions, -2300);
        var raid = JsonNode.Parse("""{"base_root":{"party":{"inventory":{"items":{}}}}}""")!.AsObject();
        var town = JsonNode.Parse("""{"base_root":{"wallet":{},"estate_items":{"items":{}}}}""")!.AsObject();
        const string lootText = """{"loot_tables":[{"id":"rf_loot","entries":[{"type":"item","chances":1,"data":{"type":"estate","id":"rf_token","amount":1}}]}]}""";
        var eventText = new JsonObject { ["events"] = new JsonArray(new JsonObject
        {
            ["id"] = "rf_event", ["data"] = new JsonArray(
                new JsonObject { ["type"] = "bonus_recruit", ["string_data"] = originalHero.Id, ["number_data"] = 1 },
                new JsonObject { ["type"] = "bonus_currency", ["string_data"] = "rf_token", ["number_data"] = 1 })
        }) }.ToJsonString();
        foreach (var kind in new[] { "local", "base", "dlc", "dlc-mod" })
        foreach (var (path, isEvent, active) in new[]
        {
            ("loot/rf.loot.json", false, true), ("loot/nested/rfloot.json", false, true),
            ("loot/rf.lootXjson", false, true),
            ("loot/notes.json", false, false), ("heroes/rf.loot.json", false, false),
            ("campaign/town_events/rf.town_events.events.json", true, true),
            ("campaign/town_events/nested/rf.town_eventsXevents.json", true, true),
            ("campaign/town_events/rf.town_events.eventsXjson", true, true),
            ("campaign/town_events/rf.events.json", true, false)
        })
        {
            var resourceRoot = Path.Combine(root, kind, path.Replace('/', '_'));
            var prefix = kind == "dlc-mod" ? "dlc/rf_feature/" : "";
            WriteMultiMash(resourceRoot, prefix + path, isEvent ? eventText : lootText);
            if (!isEvent) WriteMultiMash(resourceRoot, prefix + "heroes/rf/rf.info.darkest", "extra_battle_loot: .code rf_loot\n");
            if (kind is "local" or "dlc-mod") WriteFixtureManifest(resourceRoot);
            var resourceSource = new ActiveContentSource("resource:rf", "Reference resources", kind == "dlc-mod" ? "local" : kind, resourceRoot, -2301)
            { VirtualPathPrefix = kind == "dlc" ? "dlc/rf_feature" : "" };
            var sources = new List<ActiveContentSource> { definitionsSource, resourceSource };
            if (kind == "dlc-mod")
            {
                var emptyDlc = Path.Combine(resourceRoot, "enabled-feature"); Directory.CreateDirectory(emptyDlc);
                sources.Add(new ActiveContentSource("dlc:rf", "Reference DLC", "dlc", emptyDlc, 0) { VirtualPathPrefix = "dlc/rf_feature" });
            }
            var content = original with { Sources = sources };
            if (kind == "local" && path.EndsWith("Xjson", StringComparison.Ordinal))
            {
                var before = ProfileCatalogContentFingerprint.Capture(sources);
                File.AppendAllText(Path.Combine(resourceRoot, path), " ");
                Assert(ProfileCatalogContentFingerprint.Capture(sources) != before,
                    "Native-accepted nonliteral JSON suffixes must participate in automatic catalog refresh.");
            }
            var catalog = isEvent ? QuantityItemCatalog.Load(content, town) : QuantityItemCatalog.LoadRaid(content, raid);
            var item = catalog.Items.Single(i => i.ItemId == "rf_token");
            Assert(item.ReferenceStatus == (active ? QuantityItemReferenceStatus.ConfirmedActive : QuantityItemReferenceStatus.SuspectedUnused) &&
                   item.IsHiddenByDefault == !active,
                $"{kind}/{path}: only native-consumed resource files may prove item reachability.");
            if (isEvent)
            {
                var heroContent = original with { Sources = original.Sources.Concat(sources.Skip(1)).ToArray() };
                Assert(HeroClassCatalog.Load(heroContent).RecruitEvents.Any(e => e.Id == "rf_event") == active,
                    $"{kind}/{path}: hero sources and item references must agree about event-file eligibility.");
            }
        }

        foreach (var eligible in new[] { false, true })
        {
            var missingRoot = Path.Combine(root, "missing-" + eligible); Directory.CreateDirectory(missingRoot);
            File.WriteAllText(Path.Combine(missingRoot, "modfiles.txt"), eligible ? "loot/missing.loot.json\n" : "loot/notes.json\nheroes/missing.loot.json\ncampaign/town_events/missing.events.json\n");
            var content = original with { Sources = [definitionsSource,
                new ActiveContentSource("local:rf-missing", "Missing references", "local", missingRoot, 0)] };
            var catalog = QuantityItemCatalog.LoadRaid(content, raid);
            Assert(catalog.Items.Single(i => i.ItemId == "rf_token").ReferenceStatus ==
                   (eligible ? QuantityItemReferenceStatus.AnalysisIncomplete : QuantityItemReferenceStatus.SuspectedUnused),
                "Missing unconsumed manifest files must not poison analysis; a missing consumed loot file must still report incomplete.");
            Assert(catalog.Issues.Any(i => i.Contains("reference file listed by active Mod is missing")) == eligible,
                "Missing-reference diagnostics must use the same consumer eligibility as discovery.");
        }

        var uncertain = Path.Combine(root, "uncertain");
        WriteMultiMash(uncertain, "inventory/u.inventory.items.darkest", string.Join('\n', new[] { "rf_token", "rf_known" }.Select(id =>
            $"inventory_item: .type estate .id {id} .base_stack_limit 3 .estate_can_be_provision false")));
        WriteMultiMash(uncertain, "inventory/u.inventory.system_configs.darkest", "inventory_system_config: .type raid .max_slots 16\n");
        WriteMultiMash(uncertain, "loot/u.loot.json", """
            {"loot_tables":[
              {"id":"rf_root","entries":[{"type":"table","data":{"table":"rf_nested"}}]},
              {"id":"rf_nested","entries":[{"type":"item","data":{"type":"estate","id":"rf_token"}},{"type":"table","data":{"table":"rf_root"}}]},
              {"id":"rf_known","entries":[{"type":"item","data":{"type":"estate","id":"rf_known"}}]}
            ]}
            """);
        WriteMultiMash(uncertain, "campaign/quest/quest.plot_quests.json", "{ broken_json \"rf_root\" \"rf_known\" ");
        WriteMultiMash(uncertain, "heroes/rf/rf.info.darkest", "extra_battle_loot: .code rf_known\n");
        WriteFixtureManifest(uncertain);
        var uncertainContent = original with { Sources = [new ActiveContentSource("local:rf-u", "Uncertain references", "local", uncertain, 0)] };
        var result = QuantityItemCatalog.LoadRaid(uncertainContent, raid);
        var unknown = result.Items.Single(i => i.ItemId == "rf_token");
        Assert(unknown.ReferenceStatus == QuantityItemReferenceStatus.AnalysisIncomplete && !unknown.IsHiddenByDefault &&
               unknown.ReferenceEvidence.Any(e => e.Contains("无法完整解析") && e.Contains("rf_nested")),
            "Quoted fallback roots must remain uncertain through nested/cyclic loot tables and retain their diagnostic evidence.");
        Assert(result.Items.Single(i => i.ItemId == "rf_known").ReferenceStatus == QuantityItemReferenceStatus.ConfirmedActive,
            "An independent proven root must remain confirmed even when the same table also has an uncertain root.");
        Console.WriteLine("PASS: native loot/event eligibility across Mod/base/DLC, missing-file diagnostics and uncertain reference propagation.");
    }
}
