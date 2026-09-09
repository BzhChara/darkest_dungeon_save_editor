internal static partial class ContractSuite
{
    private static async Task RunNativeCatalogReadingContractsAsync(string runRoot, DsonSaveCodec codec)
    {
        await VerifyCaseSensitiveMonsterContractsAsync(Path.Combine(runRoot, "monster-id-case"), codec);
        await VerifyNativeInventoryFieldsAsync(Path.Combine(runRoot, "native-inventory-fields"), codec);
    }

    private static async Task VerifyCaseSensitiveMonsterContractsAsync(string root, DsonSaveCodec codec)
    {
        var game = Path.Combine(root, "game");
        var mod = Path.Combine(game, "mods", "lowercase-provider");
        var profile = WriteBattleSafetyProfile(Path.Combine(root, "profile"));
        var gamePath = Path.Combine(profile.ProfileDirectory, "persist.game.json");
        var config = JsonNode.Parse(File.ReadAllText(gamePath))!;
        config["base_root"]!["applied_ugcs_1_0"]!["0"] = new JsonObject
        {
            ["name"] = "Lowercase Provider", ["source"] = "mod_local_source"
        };
        File.WriteAllText(gamePath, config.ToJsonString());
        WriteMultiMash(mod, "project.xml", "<project><Title>Lowercase Provider</Title></project>");
        var raidPath = Path.Combine(profile.ProfileDirectory, "persist.raid.json");
        var raid = JsonNode.Parse(File.ReadAllText(raidPath))!;
        raid["base_root"]!["start_elapsed_time"] = 123456;
        File.WriteAllText(raidPath, raid.ToJsonString());
        WriteMultiMash(game, "monsters/large/large_A/large_A.info.darkest", "display: .size 3\ntag: .id boss\n");
        WriteMultiMash(game, "monsters/tiny/tiny_A/tiny_A.info.darkest", "display: .size 1\n");
        var foreignDefinition = WriteMultiMash(game, "monsters/foreign/foreign_A/foreign_A.info.darkest", "display: .size 1\n");
        var kinds = new[] { "hall", "room", "boss" };
        WriteMultiMash(game, "dungeons/cove/cove.2.mash.darkest", string.Concat(kinds.Select(kind =>
            $"{kind}: .chance 1 .types large_a large_a\n{kind}: .chance 1 .types tiny_A\n{kind}: .chance 1 .types large_A\n")));
        WriteMultiMash(game, "dungeons/ruins/ruins.2.mash.darkest", string.Concat(kinds.Select(kind =>
            $"{kind}: .chance 1 .types foreign_A\n{kind}: .chance 1 .types foreign_a\n")));
        var locations = new SaveEditorLocations(root, Path.Combine(root, "workspaces"), Path.Combine(root, "backups"));
        WriteFixtureManifest(mod);
        var content = await ActiveContentResolver.ResolveAsync(profile, game, null, codec, locations.WorkspaceDirectory);
        var reader = new BattleMapSnapshotReader(codec);
        var snapshot = await reader.LoadAsync(profile.ProfileDirectory);
        var catalog = BattleEncounterCatalog.Load(content, snapshot);
        for (var type = 0; type < kinds.Length; type++)
        {
            var rows = catalog.Encounters.Where(row => row.MashType == type).OrderBy(row => row.SourceLine).ToArray();
            Assert(rows.Length == 3 && rows[0].MashIndex == 0 && !rows[0].CanPlaceDirectly && !rows[0].ContainsBossMonster &&
                   rows[1].MashIndex == 1 && rows[1].CanPlaceDirectly && rows[2].MashIndex == 2 && rows[2].ContainsBossMonster,
                "A wrong-case missing monster must retain its native slot with zero size, never inherit a different ID's size/Boss tag or shift the next valid battle.");
            BattleEncounterCatalog.ValidateDirectEncounter(rows[1]);
            var rejected = false;
            try { BattleEncounterCatalog.ValidateDirectEncounter(rows[1] with { MashIndex = 0 }); }
            catch (InvalidOperationException) { rejected = true; }
            Assert(rejected, "Write validation must reject the formerly accepted incorrect index 0 for tiny_A.");
        }
        Assert(catalog.BridgeEncounters.All(row => !row.MonsterIds.Contains("foreign_a")),
            "A wrong-case foreign ID cannot become a Bridge source through a differently cased definition.");

        var maps = new BattleMapEditService(codec, locations);
        var direct = catalog.DirectEncounters.Single(row => row.MashType == 1 && row.MashIndex == 1);
        await maps.CommitAsync(await maps.PreparePlaceBattleAsync(profile, snapshot, "rooC", "tile0", direct));
        snapshot = await reader.LoadAsync(profile.ProfileDirectory);
        var bridge = new ManagedBattleEncounterBridgeService(codec, locations, () => false);
        for (var type = 0; type < kinds.Length; type++)
        {
            catalog = BattleEncounterCatalog.Load(content, snapshot);
            var source = catalog.BridgeEncounters.Single(row => row.OriginDungeonId == "ruins" &&
                row.MashType == type && row.MonsterIds.SequenceEqual(["foreign_A"]));
            var installed = await bridge.EnsureEncounterAsync(profile, snapshot, content, catalog, source, game, null, null);
            Assert(installed.MashIndex == 3, "Every Bridge type must append after all three native slots, including the wrong-case missing row.");
            content = installed.ActiveContent;
            snapshot = await reader.LoadAsync(profile.ProfileDirectory);
            if (type != 1)
            {
                var area = type == 0 ? "coAB" : "rooB";
                var tile = type == 0 ? "tile1" : "tile0";
                await maps.CommitAsync(await maps.PreparePlaceBattleAsync(profile, snapshot, area, tile, installed.DirectEncounter));
                snapshot = await reader.LoadAsync(profile.ProfileDirectory);
                Assert(snapshot.Areas.Single(a => a.AreaId == area).Tiles.Single(t => t.TileId == tile).MashIndex == 3,
                    "The corrected global Bridge index must survive DSON write and reload.");
            }
        }
        Assert(!(await bridge.ReconcileAsync(content, game, null)).Changed,
            "An unrelated missing-case native row must not invalidate correctly numbered editor battles.");

        // Two separate sources can define IDs differing only in case even on Windows.
        WriteMultiMash(mod, "monsters/large/large_a/large_a.info.darkest", "display: .size 1\n");
        WriteMultiMash(mod, "monsters/foreign/foreign_a/foreign_a.info.darkest", "display: .size 1\n");
        WriteFixtureManifest(mod);
        content = await ActiveContentResolver.ResolveAsync(profile, game, null, codec, locations.WorkspaceDirectory);
        catalog = BattleEncounterCatalog.Load(content, snapshot);
        var sizes = BattleEncounterCatalog.ReadMaintenanceMonsterSizes(content.Sources);
        Assert(sizes["large_A"] == 1 && sizes["large_a"] == 1 &&
               catalog.DirectEncounters.Single(row => row.MashType == 0 && row.MashIndex == 0) is { ContainsBossMonster: false } &&
               catalog.DirectEncounters.Single(row => row.MashType == 0 && row.MashIndex == 2) is { ContainsBossMonster: false },
            "Case-distinct actor IDs remain distinct, while their canonical Windows paths can resolve to the same Mod bytes.");
        var lowerSource = catalog.BridgeEncounters.Single(row => row.OriginDungeonId == "ruins" && row.MashType == 0 &&
            row.MonsterIds.SequenceEqual(["foreign_a"]));
        var lower = await bridge.EnsureEncounterAsync(profile, snapshot, content, catalog, lowerSource, game, null, null);
        Assert(lower.EncounterWasAdded && lower.MashIndex == 4,
            "A case-distinct Bridge formation must get its own global index instead of reusing foreign_A.");
        File.Delete(foreignDefinition);
        var cleanup = await bridge.ReconcileAsync(lower.ActiveContent, game, null);
        Assert(cleanup.Changed && !cleanup.Deferred && cleanup.RemovedCombinations == 3 &&
               cleanup.ReindexedCombinations == 1 && cleanup.ClearedBattles == 3,
            "A surviving lowercase definition must not conceal a removed uppercase dependency; maintenance must prune it and clear recorded direct/Bridge battles.");
        Console.WriteLine("PASS: case-sensitive monster IDs, metadata, all runtime/Bridge indexes, write rejection, and case-distinct maintenance.");
    }

    private static async Task VerifyNativeInventoryFieldsAsync(string root, DsonSaveCodec codec)
    {
        var game = Path.Combine(root, "game");
        var profile = WriteBattleSafetyProfile(Path.Combine(root, "profile"));
        WriteMultiMash(game, "inventory/native.inventory.items.darkest", """
            inventory_item: .type estate .id unquoted .base_stack_limit 3 .estate_can_be_provision true
            inventory_item: .type "gold" .type estate .id "first" .id "second" .base_stack_limit 9 .base_stack_limit 1 .estate_can_be_provision true .estate_can_be_provision false
            inventory_item: .type estate .id prefixed .base_stack_limit +2suffix .estate_can_be_provision "true"
            inventory_item: .type estate .id quoted_number .base_stack_limit 9 .base_stack_limit "4"
            inventory_item: .type estate .id invalid_number .base_stack_limit 9 .base_stack_limit nope
            inventory_item: .type estate .id missing_number
            inventory_item: .type estate .id overflow .base_stack_limit 99999999999999999999999999
            inventory_item: .type estate .id negative .base_stack_limit -3
            inventory_item: .type estate .id removed .id "" .base_stack_limit 1
            inventory_item: .type gold .id "" .base_stack_limit 1750
            inventory_item: .type estate .id stable .base_stack_limit 4 // .id comment .base_stack_limit 99
            unrelated: .type estate .id other .base_stack_limit 99
            /* inventory_item: .type estate .id block_phantom .base_stack_limit 99 */
            inventory_item: .type estate .id block_safe .base_stack_limit 1 /* .id obsolete .base_stack_limit 9 */
            inventory_item: .type estate .id hash_guard .base_stack_limit 9
            # note: the last value belongs to the same declaration
            .base_stack_limit nope
            inventory_item: .type estate .id joined .base_stack_limit 1/* remove without whitespace */2
            inventory_item: .type estate .id multiline
              .base_stack_limit 4
              # note: these fields must still be read
              .base_stack_limit 2 .estate_can_be_provision "yes"
            """);
        var content = await ActiveContentResolver.ResolveAsync(profile, game, null, codec, Path.Combine(root, "workspaces"));
        var town = QuantityItemCatalog.LoadDefinitions(content);
        Assert(town.Single(item => item.ItemId == "unquoted") is { BaseStackLimit: 3, EstateCanBeProvision: true } &&
               town.Single(item => item.ItemId == "second") is { InventoryType: "estate", BaseStackLimit: 1, EstateCanBeProvision: false } &&
               town.Single(item => item.ItemId == "prefixed") is { BaseStackLimit: 2, EstateCanBeProvision: true } &&
               town.Single(item => item.ItemId == "stable").BaseStackLimit == 4 &&
               town.All(item => item.ItemId is not ("first" or "removed" or "comment" or "other")),
            "Native inventory strings accept bare/quoted values and the last field; numbers use a prefix, without falling back or leaking fields across declarations/comments.");
        Assert(town.Single(item => item.ItemId == "quoted_number").BaseStackLimit == 0 &&
               town.Single(item => item.ItemId == "invalid_number").BaseStackLimit == 0 &&
               town.Single(item => item.ItemId == "missing_number").BaseStackLimit is null &&
               town.Single(item => item.ItemId == "overflow").BaseStackLimit is null &&
               town.Single(item => item.ItemId == "negative").BaseStackLimit == -3 &&
               town.Single(item => item.DisplayId == "gold").StorageKind == QuantityItemStorageKind.Wallet,
            "An invalid final integer cannot revive an earlier limit; overflow stays guarded and empty wallet IDs remain valid.");
        Assert(town.Single(item => item.ItemId == "block_safe").BaseStackLimit == 1 &&
               town.Single(item => item.ItemId == "hash_guard").BaseStackLimit == 0 &&
               town.Single(item => item.ItemId == "joined").BaseStackLimit == 12 &&
               town.Single(item => item.ItemId == "multiline") is { BaseStackLimit: 2, EstateCanBeProvision: true } &&
               town.All(item => item.ItemId is not ("block_phantom" or "obsolete")),
            "Native comment handling must remove block definitions/fields, preserve native token concatenation, and ignore hash-comment colons when finding declaration boundaries.");
        var raid = QuantityItemCatalog.LoadDefinitions(content, QuantityItemSaveContext.Raid);
        var selected = raid.Single(item => item.ItemId == "second");
        var raidRoot = JsonNode.Parse("""{"base_root":{"party":{"inventory":{"items":{}}}}}""")!.AsObject();
        var updated = RaidInventorySaveEditor.SetAmount(raidRoot, selected, 3, 3).UpdatedRoot;
        var stacks = updated["base_root"]!["party"]!["inventory"]!["items"]!.AsObject();
        Assert(stacks.Count == 3 && stacks.All(pair => pair.Value!["id"]!.GetValue<string>() == "second" &&
                   pair.Value["amount"]!.GetValue<int>() == 1) &&
               raidRoot["base_root"]!["party"]!["inventory"]!["items"]!.AsObject().Count == 0,
            "Quantity edits must persist the final ID and split by the final stack limit without mutating the original root.");
        var blockUpdated = RaidInventorySaveEditor.SetAmount(raidRoot, raid.Single(item => item.ItemId == "block_safe"), 3, 3).UpdatedRoot;
        Assert(blockUpdated["base_root"]!["party"]!["inventory"]!["items"]!.AsObject()
                .All(pair => pair.Value!["id"]!.GetValue<string>() == "block_safe" && pair.Value["amount"]!.GetValue<int>() == 1),
            "A commented-out ID or larger stack limit must never reach the serialized inventory.");
        foreach (var id in new[] { "quoted_number", "invalid_number", "missing_number", "overflow", "negative", "hash_guard" })
        {
            var rejected = false;
            try { RaidInventorySaveEditor.SetAmount(raidRoot, raid.Single(item => item.ItemId == id), 1, 3); }
            catch (InvalidOperationException) { rejected = true; }
            Assert(rejected, $"Unsafe native stack limit for {id} must prevent creating raid stacks.");
        }
        Console.WriteLine("PASS: native inventory fields, final IDs/limits/provision flags, numeric guards, and real stack allocation.");
    }
}
