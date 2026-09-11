internal static partial class ContractSuite
{
    private static async Task VerifyActorDiscoveryQueriesAsync(string runRoot)
    {
        foreach (var kind in new[] { "base", "mode", "dlc-feature", "local", "workshop", "dlc-mod" })
        foreach (var variant in new[] { "normal", "extension-case", "info-case", "directory-case", "physical-alias" })
        {
            var root = Path.Combine(runRoot, "actor-discovery-query", kind, variant);
            var source = Path.Combine(root, "source");
            var prefix = kind == "dlc-mod" ? "dlc/rq_feature/" : "";
            var isMod = kind is "local" or "workshop" or "dlc-mod";
            var suffix = variant == "extension-case" ? ".info.DARKEST" : variant == "info-case" ? ".INFO.darkest" : ".info.darkest";
            var heroRoot = variant is "directory-case" or "physical-alias" ? "Heroes" : "heroes";
            var monsterRoot = variant is "directory-case" or "physical-alias" ? "Monsters" : "monsters";
            WriteMultiMash(source, prefix + $"{heroRoot}/query/query{suffix}", "armour: .name coat .hp 20\nextra_battle_loot: .code query_loot\n");
            WriteMultiMash(source, prefix + $"{monsterRoot}/alpha/alpha_A/alpha_A{suffix}", "display: .size 3\nloot: .code query_loot\n");
            WriteMultiMash(source, prefix + "monsters/bravo/bravo_A/bravo_A.info.darkest", "display: .size 1\n");
            WriteMultiMash(source, prefix + "inventory/query.inventory.items.darkest", "inventory_item: .type estate .id query_token .base_stack_limit 2\n");
            WriteMultiMash(source, prefix + "loot/query.loot.json", QueryLootJson);
            WriteMultiMash(source, prefix + "trinkets/query.entries.trinkets.json",
                """{"entries":[{"id":"query_restricted","hero_class_requirements":["query"]}]}""");
            const string mash = "dungeons/cove/a.cove.2.mash.darkest";
            WriteMultiMash(source, prefix + mash, QueryMashes("alpha_A alpha_A") + QueryMashes("bravo_A"));
            var sources = QuerySources(source, kind);
            if (kind == "dlc-mod") WriteMultiMash(sources[1].Directory, mash, QueryMashes("bravo_A"));
            if (isMod)
            {
                WriteFixtureManifest(source);
                if (variant == "physical-alias")
                {
                    var manifest = Path.Combine(source, "modfiles.txt");
                    File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("Heroes/", "heroes/", StringComparison.Ordinal)
                        .Replace("Monsters/", "monsters/", StringComparison.Ordinal));
                }
            }
            // On Windows, the second write shares the capitalized physical
            // Monsters folder. Give bravo a correctly cased manifest query.
            if (isMod)
            {
                var manifest = Path.Combine(source, "modfiles.txt");
                File.WriteAllText(manifest, File.ReadAllText(manifest)
                    .Replace("Monsters/bravo/", "monsters/bravo/", StringComparison.Ordinal));
            }
            var content = QueryContent(root, sources);
            var snapshot = new BattleMapSnapshot(content.Profile.ProfileDirectory, "", "", "", "", "cove", 2, 1,
                null, null, null, null, null, null, null, null, null, false, [], [], [], DateTime.UtcNow);
            var accepted = variant is not ("extension-case" or "info-case") && (!isMod || variant != "directory-case");
            var catalog = BattleEncounterCatalog.Load(content, snapshot);
            Assert(HeroClassCatalog.Load(content).HeroClasses.Any(h => h.Id == "query") == accepted &&
                   TrinketCatalog.Load(content).Trinkets.Any(t => t.Id == "query_restricted") == accepted,
                $"{kind}/{variant}: hero generation and trinket requirements must share native actor registration.");
            var raid = JsonNode.Parse("""{"base_root":{"party":{"inventory":{"items":{}}}}}""")!.AsObject();
            if (isMod)
                Assert(QuantityItemCatalog.LoadRaid(content, raid).Items.Single().ReferenceStatus ==
                       (accepted ? QuantityItemReferenceStatus.ConfirmedActive : QuantityItemReferenceStatus.SuspectedUnused),
                    $"{kind}/{variant}: unregistered actors must not establish item/loot references.");
            foreach (var type in new[] { 0, 1, 2 })
            {
                var bravo = catalog.Encounters.Single(row => row.MashType == type && row.MonsterIds.SequenceEqual(["bravo_A"]));
                Assert(bravo.MashIndex == (accepted ? 0 : 1),
                    $"{kind}/{variant}/{type}: missing alpha occupies index zero; only registered oversized alpha may remove the slot.");
                BattleEncounterCatalog.ValidateDirectEncounter(bravo);
                Assert(BattleEncounterCatalog.ResolveAppendTarget(catalog, type).NextMashIndex == (accepted ? 1 : 2) &&
                       BattleEncounterCatalog.ReadMaintenanceTable(content, "cove", 2, type).Count == (accepted ? 1 : 2),
                    "Direct writes, Bridge append and maintenance must agree on actor registration and slot counting.");
            }
            if (isMod && variant == "extension-case")
            {
                var fingerprint = BattleEncounterCatalog.CaptureContentFingerprint(sources);
                var manifest = Path.Combine(source, "modfiles.txt");
                File.WriteAllText(manifest, File.ReadAllText(manifest).Replace(".info.DARKEST", ".info.darkest", StringComparison.Ordinal));
                Assert(BattleEncounterCatalog.CaptureContentFingerprint(sources) != fingerprint &&
                       BattleEncounterCatalog.ReadMaintenanceTable(content, "cove", 2, 0).Count == 1 &&
                       HeroClassCatalog.Load(content).HeroClasses.Any(h => h.Id == "query"),
                    "Lowercase manifest names must discover physical uppercase filename aliases and refresh registration.");
                Assert(await CaptureSaveFailureAsync(() => {
                    BattleEncounterCatalog.ValidateDirectEncounter(catalog.DirectEncounters.Last()); return Task.CompletedTask;
                }) is InvalidOperationException, "Actor registration changes must invalidate old battle selections.");
            }
        }

        foreach (var kind in new[] { "base", "local", "workshop" })
        {
            var root = Path.Combine(runRoot, "actor-wildcard-discovery", kind);
            WriteMultiMash(root, "heroes/seed/query.seed.infoXdarkest", "// registration seed\n");
            // This canonical file cannot register its own ID: the physical AND
            // manifest suffix are uppercase. Only the wildcard seed registers query.
            WriteMultiMash(root, "heroes/query/query.info.DARKEST", "armour: .name coat .hp 27\nextra_battle_loot: .code query_loot\n");
            WriteMultiMash(root, "monsters/seed/alpha_A.seed.infoXdarkest", "display: .size 1\n");
            WriteMultiMash(root, "trinkets/query.entries.trinkets.json", """{"entries":[{"id":"restricted","hero_class_requirements":["query"]}]}""");
            if (kind != "base") WriteFixtureManifest(root);
            var content = QueryContent(Path.Combine(root, "context"), QuerySources(root, kind));
            Assert(HeroClassCatalog.Load(content).HeroClasses.Single().Id == "query" && TrinketCatalog.Load(content).Trinkets.Count == 1,
                "Hero discovery must honor the native wildcard dot and two-dot ID extraction, followed by canonical Windows open.");
            Assert(!NativeContentFileResolver.DiscoverActorIds(content.Sources, "monsters", []).Contains("alpha_A"),
                "The monster query escapes both dots; hero wildcard semantics must not leak into monster registration.");
        }
        Console.WriteLine("PASS: actor query case and wildcard rules across six sources, item references, all battle types, append indexes and refresh.");
    }
}
