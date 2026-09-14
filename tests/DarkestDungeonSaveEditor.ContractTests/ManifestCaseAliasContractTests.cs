internal static partial class ContractSuite
{
    private static async Task VerifySameManifestCaseAliasesAsync(string runRoot, DsonSaveCodec codec)
    {
        foreach (var kind in new[] { "local", "workshop" })
        foreach (var upperFirst in new[] { false, true })
        {
            var root = Path.Combine(runRoot, "same-manifest-case", kind, upperFirst ? "upper-first" : "lower-first");
            var f = HeroSelectionContent(root, kind, skills: 1);
            var aliases = new List<(string Lower, string Upper)>();
            void Alias(string lower, string upper, string text)
            {
                SelectionResource(f, lower, text);
                aliases.Add((lower, upper));
            }
            var info = File.ReadAllText(Path.Combine(f.Source, "heroes/selection/selection.info.darkest"));
            Alias("heroes/selection/selection.info.darkest", "heroes/Selection/Selection.info.darkest", info);
            Alias("heroes/selection/selection_A/skin.png", "heroes/Selection/Selection_A/skin.png", "fixture");
            Alias("monsters/alias/alias_A/alias_A.info.darkest", "monsters/ALIAS/ALIAS_A/ALIAS_A.info.darkest", "display: .size 1\n");
            Alias("dungeons/cove/a.cove.2.mash.darkest", "dungeons/Cove/a.Cove.2.mash.darkest", QueryMashes("ALIAS_A"));
            Alias("dungeons/cove/cove.props.darkest", "dungeons/Cove/Cove.props.darkest", "traps: .chance 1 .types alias_trap\n");
            WriteMultiMash(f.Content.Sources.Single(s => s.Kind == "base").Directory, "props/trap_definitions.json",
                """{"props":[{"name":"alias_trap","default_data":{"instance_type":"trap"}}]}""");
            SelectionResource(f, "inventory/a.inventory.items.darkest", "inventory_item: .type provision .id alias_food .base_stack_limit 5\n");
            Alias("inventory/b.inventory.items.darkest", "inventory/B.inventory.items.darkest",
                "inventory_item: .type provision .id alias_food .base_stack_limit 9\n");
            SelectionResource(f, "inventory/a.inventory.system_configs.darkest", QueryCapacity(5));
            Alias("inventory/b.inventory.system_configs.darkest", "inventory/B.inventory.system_configs.darkest", QueryCapacity(9));
            SelectionResource(f, "trinkets/a.entries.trinkets.json", """{"entries":[{"id":"alias_ring","quest_uses":5}]}""");
            Alias("trinkets/b.entries.trinkets.json", "trinkets/B.entries.trinkets.json", """{"entries":[{"id":"alias_ring","quest_uses":9}]}""");
            SelectionResource(f, "shared/buffs/a.buffs.json", QueryBuff(.25));
            Alias("shared/buffs/b.buffs.json", "shared/buffs/B.buffs.json", QueryBuff(.75));
            SelectionResource(f, "shared/quirk/a.quirk_library.json",
                """{"quirks":[{"id":"rq_hp","is_positive":true,"buffs":["rq_hp"]},{"id":"alias_q","is_positive":true}]}""");
            Alias("shared/quirk/b.quirk_library.json", "shared/quirk/B.quirk_library.json",
                """{"quirks":[{"id":"alias_q","is_positive":false}]}""");
            var manifest = Path.Combine(f.Source, "modfiles.txt");
            var aliasedPaths = aliases.SelectMany(pair => new[] { pair.Lower, pair.Upper }).ToHashSet(StringComparer.Ordinal);
            var otherPaths = File.ReadAllLines(manifest).Where(path => !aliasedPaths.Contains(path)).Distinct(StringComparer.Ordinal);
            File.WriteAllLines(manifest, otherPaths.Concat(aliases.SelectMany(pair => upperFirst
                ? new[] { pair.Upper, pair.Lower } : new[] { pair.Lower, pair.Upper })));

            // These are two native requests and one Windows file, not two test
            // files whose paths accidentally overwrite one another.
            Assert(aliases.All(pair => ComputeSha256(Path.Combine(f.Source, pair.Lower)) == ComputeSha256(Path.Combine(f.Source, pair.Upper))),
                "Both manifest spellings must open the same fixture bytes.");
            var heroes = HeroClassCatalog.Load(f.Content);
            Assert(heroes.HeroClasses.Select(h => h.Id).ToHashSet(StringComparer.Ordinal).SetEquals(["selection", "Selection"]),
                $"{kind}/{upperFirst}: one manifest must retain both registered hero identities.");
            var item = QuantityItemCatalog.LoadDefinitions(f.Content, QuantityItemSaveContext.Raid).Single(i => i.ItemId == "alias_food");
            var ring = TrinketCatalog.Load(f.Content).Trinkets.Single(t => t.Id == "alias_ring");
            var raidCapacity = RaidInventoryStorageCatalog.Load(f.Content).Storage?.MaxSlots;
            var trinketCapacity = TrinketStorageCatalog.Load(f.Content).Storage?.MaxSlots;
            var hpModifier = heroes.InitialQuirks.Single(q => q.Id == "rq_hp").MaxHpModifiers.Single().Amount;
            var quirkPositive = heroes.InitialQuirks.Single(q => q.Id == "alias_q").IsPositive;
            Assert(item.BaseStackLimit == 9 && ring.QuestUses == 9 &&
                   raidCapacity == 9 && trinketCapacity == 9 && hpModifier == .75 && quirkPositive == false,
                $"{kind}/{upperFirst}: retain B,a,b native slots for first-match and last-update consumers; " +
                $"item={item.BaseStackLimit}, ring={ring.QuestUses}, capacities={raidCapacity}/{trinketCapacity}, hp={hpModifier}, positive={quirkPositive}.");
            foreach (var hero in heroes.HeroClasses)
            {
                var generated = StagecoachHeroCandidateFactory.Generate(heroes, hero, 17, 0, ["rq_hp"]);
                Assert(generated.Preview.CurrentHp == 35, "Both manifest actor requests use the same effective HP Buff.");
                var heroRoot = Path.Combine(root, hero.Id == "selection" ? "lower-hero" : "upper-hero");
                Directory.CreateDirectory(heroRoot);
                await VerifyHeroSelectionPersistenceAsync(heroRoot, generated, codec);
            }
            var props = BattleRoomAttachmentCatalog.Load(f.Content);
            foreach (var dungeon in new[] { "cove", "Cove" })
            {
                Assert(props.GetCandidates(BattleRoomAttachmentKind.Trap, dungeon).Count == 1,
                    "Both canonical region pools survive same-manifest alias discovery.");
                var catalog = BattleEncounterCatalog.Load(f.Content, EncounterQueryMap(f.Content, dungeon));
                foreach (var type in new[] { 0, 1, 2 })
                {
                    var row = catalog.Encounters.Single(r => r.MashType == type);
                    Assert(row.MashIndex == 0 && row.CanPlaceDirectly && row.MonsterIds.SequenceEqual(["ALIAS_A"]),
                        "Each manifest region retains its own row and case-distinct registered monster.");
                    BattleEncounterCatalog.ValidateDirectEncounter(row);
                    Assert(BattleEncounterCatalog.ResolveAppendTarget(catalog, type).NextMashIndex == 1 &&
                           BattleEncounterCatalog.ReadMaintenanceTable(f.Content, dungeon, 2, type).Count == 1,
                        "Append and maintenance cannot skip a manifest alias before computing the next index.");
                }
            }
        }
        Console.WriteLine("PASS: same-Mod raw-case manifest aliases, both line orders, hero/monster registration, prop regions, first/last file slots, HP DSON and all-type append counts.");
    }
}
