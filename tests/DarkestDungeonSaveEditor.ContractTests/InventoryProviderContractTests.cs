internal static partial class ContractSuite
{
    private static async Task RunInventoryProviderContractsAsync(string runRoot, DsonSaveCodec codec)
    {
        foreach (var kind in new[] { "base", "local", "workshop" })
            await VerifyWalletDefinitionAggregationAsync(Path.Combine(runRoot, "wallet-definitions", kind), kind, codec);
        foreach (var kind in new[] { "local", "workshop" })
        {
            foreach (var mode in new[] { "high-valid", "high-unlisted", "low-missing", "high-missing" })
                await VerifyMissingInventoryProviderAsync(Path.Combine(runRoot, "inventory-providers", kind, mode), kind, mode, codec);
            await VerifyMissingInventoryGuardsAsync(Path.Combine(runRoot, "inventory-provider-guards", kind), kind, codec);
            VerifyOtherMissingResourceProviders(Path.Combine(runRoot, "other-missing-providers", kind), kind);
        }
    }

    private sealed record InventoryProviderFixture(ActiveContentSnapshot Content, JsonObject Estate, JsonObject Raid,
        SaveEditService Service, string Root);

    private static async Task<InventoryProviderFixture> BuildInventoryProviderProfileAsync(
        string root, IReadOnlyList<ActiveContentSource> sources, bool inRaid, DsonSaveCodec codec)
    {
        var profileRoot = Path.Combine(root, "profile");
        Directory.CreateDirectory(profileRoot);
        var estate = JsonNode.Parse("""
            {"base_root":{"wallet":{"0":{"type":"gold","amount":100,"marker":7},"1":{"type":"shard","amount":3}},
            "estate_items":{"items":{"0":{"type":"estate","id":"probe_token","amount":1},"1":{"type":"estate","id":"residue","amount":1}}},
            "trinkets":{"items":{}}}}
            """)!.AsObject();
        var raid = JsonNode.Parse("""
            {"base_root":{"party":{"inventory":{"items":{"0":{"type":"estate","id":"probe_token","amount":1},
            "1":{"type":"estate","id":"residue","amount":1}}}}}}
            """)!.AsObject();
        var estateSeed = WriteMultiMash(root, "estate.seed.json", estate.ToJsonString());
        var raidSeed = WriteMultiMash(root, "raid.seed.json", raid.ToJsonString());
        var gameSeed = WriteMultiMash(root, "game.seed.json", inRaid
            ? """{"base_root":{"inraid":true,"raiddungeon":"weald"}}"""
            : """{"base_root":{"inraid":false,"raiddungeon":"none"}}""");
        var profile = new SaveProfile("profile", profileRoot, Path.Combine(profileRoot, "persist.estate.json"), "contract-user", DateTime.UtcNow);
        var gameSave = Path.Combine(profileRoot, "persist.game.json");
        await codec.EncodeAsync(estateSeed, profile.EstateSavePath, null);
        await codec.EncodeAsync(raidSeed, profile.RaidSavePath, null);
        await codec.EncodeAsync(gameSeed, gameSave, null);
        var content = new ActiveContentSnapshot(profile, "base", sources, [], root, gameSeed,
            sources.Count(s => s.Kind is "local" or "workshop"), ComputeSha256(gameSave));
        return new(content, estate, raid, new SaveEditService(codec,
            new SaveEditorLocations(root, Path.Combine(root, "work"), Path.Combine(root, "backups"))), root);
    }

    private static async Task VerifyWalletDefinitionAggregationAsync(string root, string kind, DsonSaveCodec codec)
    {
        var sourceRoot = Path.Combine(root, "resources");
        WriteMultiMash(sourceRoot, "inventory/a.inventory.items.darkest", """
            inventory_item: .type gold .id "" .base_stack_limit 2
            inventory_item: .type shard .id "" .base_stack_limit 2
            inventory_item: .type estate .id one .base_stack_limit 2
            inventory_item: .type estate .id two .base_stack_limit 5
            """);
        WriteMultiMash(sourceRoot, "inventory/b.inventory.items.darkest", """
            inventory_item: .type gold .id variant .base_stack_limit 5
            inventory_item: .type gold .id variant .base_stack_limit 9
            inventory_item: .type shard .id variant .base_stack_limit 5
            inventory_item: .type heirloom .id gold .base_stack_limit 8
            """);
        WriteMultiMash(sourceRoot, "inventory/a.inventory.system_configs.darkest",
            "inventory_system_config: .type raid .max_slots 32\n");
        if (kind != "base") WriteFixtureManifest(sourceRoot);
        var f = await BuildInventoryProviderProfileAsync(root, [new(kind, kind, kind, sourceRoot, 0)], false, codec);
        var town = QuantityItemCatalog.Load(f.Content, f.Estate);
        var bag = QuantityItemCatalog.LoadRaid(f.Content, f.Raid);
        foreach (var type in new[] { "gold", "shard" })
        {
            var wallet = town.Items.Single(i => i.StorageKind == QuantityItemStorageKind.Wallet && i.PersistedType == type);
            Assert(!wallet.HasProviderConflict && !wallet.IsSaveOnly && wallet.PersistedId == "" &&
                wallet.CurrentAmount == (type == "gold" ? 100 : 3), "Distinct native IDs must aggregate into an unambiguous wallet target.");
            var raidRows = bag.Items.Where(i => i.InventoryType == type).ToArray();
            Assert(raidRows.Length == 2 && raidRows.All(i => !i.HasProviderConflict) &&
                raidRows.Single(i => i.ItemId == "").BaseStackLimit == 2 &&
                raidRows.Single(i => i.ItemId == "variant").BaseStackLimit == 5,
                "Raid definitions remain separate; repeated exact IDs keep the first definition's limit.");
            await f.Service.CommitAsync(await f.Service.PrepareQuantityItemEditAsync(f.Content.Profile, wallet, 29, f.Content));
        }
        var decoded = Path.Combine(root, "wallet.committed.json");
        await codec.DecodeAsync(f.Content.Profile.EstateSavePath, decoded);
        var saved = JsonSupport.ReadObject(decoded);
        Assert(saved["base_root"]!["wallet"]!.AsObject().Count == 2 &&
            saved["base_root"]!["wallet"]!["0"]!["marker"]!.GetValue<int>() == 7 &&
            saved["base_root"]!["wallet"]!.AsObject().All(p => p.Value!["amount"]!.GetValue<int>() == 29),
            "Wallet writes must update the existing balances and preserve fields without creating definition-ID entries.");
        Assert(town.Items.Count(i => i.InventoryType == "estate" && i.ItemId is "one" or "two") == 2,
            "Different estate item IDs must never be aggregated.");

        // Polynomial hash base 53: 53 * 'a' + 'z' == 53 * 'b' + 'E'.
        WriteMultiMash(sourceRoot, "inventory/c.inventory.items.darkest", """
            inventory_item: .type gold .id az .base_stack_limit 2
            inventory_item: .type gold .id bE .base_stack_limit 5
            """);
        if (kind != "base") WriteFixtureManifest(sourceRoot);
        var collision = QuantityItemCatalog.Load(f.Content, f.Estate);
        var collidedWallet = collision.Items.Single(i => i.StorageKind == QuantityItemStorageKind.Wallet && i.PersistedType == "gold");
        Assert(collidedWallet.HasProviderConflict && collision.Issues.Any(i => i.Contains("native hashes")),
            "Wallet aggregation must not hide a genuine native definition hash collision.");
        var beforeRejection = ComputeSha256(f.Content.Profile.EstateSavePath);
        await RejectProviderEditAsync(() => f.Service.PrepareQuantityItemEditAsync(f.Content.Profile, collidedWallet, 50, f.Content));
        Assert(ComputeSha256(f.Content.Profile.EstateSavePath) == beforeRejection,
            "Native collision rejection must not change saved wallet balances.");
        Console.WriteLine($"PASS: {kind} wallet definition aggregation, exact first match, raid separation, native collisions and DSON persistence.");
    }

    private static async Task RejectProviderEditAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception e) when (e is InvalidOperationException or IOException) { return; }
        throw new InvalidOperationException("An unavailable effective provider passed preflight or commit.");
    }
}
