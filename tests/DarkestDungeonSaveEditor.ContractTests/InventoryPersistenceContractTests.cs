using System.Text.Json;

internal static partial class ContractSuite
{
    public static async Task RunInventoryPersistenceOnlyAsync(string repositoryRoot)
    {
        var fixture = BuildContractFixture(repositoryRoot);
        await RunInventoryPersistenceContractsAsync(fixture.RunRoot, fixture.Codec);
        await RunInventoryProviderContractsAsync(fixture.RunRoot, fixture.Codec);
        Console.WriteLine($"Artifacts: {fixture.RunRoot}");
    }

    private static async Task RunInventoryPersistenceContractsAsync(string runRoot, DsonSaveCodec codec)
    {
        foreach (var kind in new[] { "base", "local", "workshop" })
            await VerifyInventoryPersistenceAsync(Path.Combine(runRoot, "inventory-persistence", kind), kind, codec);
    }

    private static async Task VerifyInventoryPersistenceAsync(string root, string kind, DsonSaveCodec codec)
    {
        var contentRoot = Path.Combine(root, "content");
        var profileRoot = Path.Combine(root, "profile");
        Directory.CreateDirectory(profileRoot);
        var ascii = new string('a', 63);
        var chinese = new string('饰', 21);
        var supplementary = string.Concat(Enumerable.Repeat("😀", 15)) + "abc";
        var validIds = new[] { ascii, chinese, supplementary, " spaced " };
        var invalidIds = new[] { ascii + "b", chinese + "b", supplementary + "b", new string('s', 62) + "饰" };
        var type63 = new string('t', 63);
        var type64 = type63 + "u";
        const string definitionsPath = "trinkets/persistence.entries.trinkets.json";
        var counterCases = new (string Id, string Fields, int? Quest, int? Trigger)[]
        {
            ("zero", "\"quest_uses\":0,\"trigger_limit\":0", 0, 0),
            ("positive", "\"quest_uses\":2,\"trigger_limit\":3", 2, 3),
            ("absent", "\"price\":10", null, null),
            ("null_first", "\"quest_uses\":null,\"quest_uses\":5,\"trigger_limit\":null,\"trigger_limit\":5", null, null),
            ("string_first", "\"quest_uses\":\"2\",\"quest_uses\":5,\"trigger_limit\":\"0\"", null, null),
            ("boolean", "\"quest_uses\":false,\"trigger_limit\":true", null, null),
            ("containers", "\"quest_uses\":[],\"trigger_limit\":{}", null, null),
            ("negative", "\"quest_uses\":-2,\"trigger_limit\":-1", -2, -1),
            ("min_int", "\"quest_uses\":-2147483648,\"trigger_limit\":-2147483648", int.MinValue, int.MinValue),
            ("max_int", "\"quest_uses\":2147483647,\"trigger_limit\":2147483647", int.MaxValue, int.MaxValue),
            ("overflow", "\"quest_uses\":2147483648,\"trigger_limit\":-2147483649", null, null),
            ("floating", "\"quest_uses\":0.0,\"trigger_limit\":2e0", null, null),
            ("first_zero", "\"quest_uses\":0,\"quest_uses\":5,\"trigger_limit\":0,\"trigger_limit\":5", 0, 0),
            ("first_positive", "\"quest_uses\":2,\"quest_uses\":0,\"trigger_limit\":3,\"trigger_limit\":0", 2, 3)
        };
        var trinketEntries = validIds.Concat(invalidIds).Select(id => JsonSerializer.Serialize(new { id, price = validIds.Contains(id) ? 10 : 99 }))
            .Concat(counterCases.Select(c => "{\"id\":\"" + c.Id + "\"," + c.Fields + "}"))
            .Append("{\"id\":\"zero\",\"quest_uses\":9,\"trigger_limit\":9}")
            .Append("{\"id\":\"nul_alias\"}")
            .Append("{\"id\":\"nul_alias\\u0000suffix\"}");
        var trinketJson = "{\"entries\":[" + string.Join(',', trinketEntries) + "]}";
        WriteMultiMash(contentRoot, definitionsPath, trinketJson);
        WriteMultiMash(contentRoot, "inventory/persistence.inventory.items.darkest",
            string.Join('\n', validIds.Concat(invalidIds).Select(id =>
                $"inventory_item: .type estate .id \"{id}\" .base_stack_limit {(validIds.Contains(id) ? 2 : 9)}")) +
            $"\ninventory_item: .type {type63} .id typed .base_stack_limit 2" +
            $"\ninventory_item: .type {type64} .id typed .base_stack_limit 9" +
            $"\ninventory_item: .type heirloom .id {ascii} .base_stack_limit 2" +
            $"\ninventory_item: .type heirloom .id {ascii}b .base_stack_limit 9" +
            $"\ninventory_item: .type gold .id {ascii}b .base_stack_limit 9\n");
        WriteMultiMash(contentRoot, "inventory/persistence.inventory.system_configs.darkest",
            "inventory_system_config: .type raid .max_slots 40\ninventory_system_config: .type trinket_storage .max_slots 100\n");
        if (kind != "base") WriteFixtureManifest(contentRoot);
        var estate = JsonNode.Parse("""{"base_root":{"version":1,"wallet":{},"estate_items":{"items":{}},"trinkets":{"items":{}}}}""")!.AsObject();
        var raid = JsonNode.Parse("""{"base_root":{"party":{"inventory":{"items":{}}}}}""")!.AsObject();
        var estateDecoded = Path.Combine(root, "estate.json");
        var gameDecoded = Path.Combine(root, "game.json");
        var raidDecoded = Path.Combine(root, "raid.json");
        File.WriteAllText(estateDecoded, estate.ToJsonString());
        File.WriteAllText(gameDecoded, """{"base_root":{"inraid":false,"raiddungeon":"none"}}""");
        File.WriteAllText(raidDecoded, raid.ToJsonString());
        var profile = new SaveProfile("profile", profileRoot, Path.Combine(profileRoot, "persist.estate.json"), "contract-user", DateTime.UtcNow);
        var gameSave = Path.Combine(profileRoot, "persist.game.json");
        await codec.EncodeAsync(estateDecoded, profile.EstateSavePath, null);
        await codec.EncodeAsync(gameDecoded, gameSave, null);
        await codec.EncodeAsync(raidDecoded, profile.RaidSavePath, null);
        var content = new ActiveContentSnapshot(profile, "base", [new("persistence", "Persistence", kind, contentRoot, 0)],
            [], root, gameDecoded, 0, ComputeSha256(gameSave));
        var service = new SaveEditService(codec, new SaveEditorLocations(root, Path.Combine(root, "work"), Path.Combine(root, "backups")));
        var town = QuantityItemCatalog.Load(content, estate);
        var raidCatalog = QuantityItemCatalog.LoadRaid(content, raid);
        var trinkets = TrinketCatalog.Load(content);

        foreach (var id in validIds.Concat(invalidIds))
        {
            var expectedWritable = validIds.Contains(id);
            var item = town.Items.Single(i => i.StorageKind == QuantityItemStorageKind.EstateItems && i.ItemId == id);
            var bagItem = raidCatalog.Items.Single(i => i.InventoryType == "estate" && i.ItemId == id);
            var trinket = trinkets.Trinkets.Single(i => i.Id == id);
            Assert((item.SaveIdentityIssue.Length == 0) == expectedWritable &&
                   (bagItem.SaveIdentityIssue.Length == 0) == expectedWritable &&
                   (trinket.SaveIdentityIssue.Length == 0) == expectedWritable &&
                   !item.HasProviderConflict && !trinket.HasProviderConflict &&
                   trinket.Price == (expectedWritable ? 10 : 99),
                "Catalogs must retain full distinct IDs and apply the UTF-8 save boundary without renaming or merging them.");
            if (!expectedWritable)
            {
                RejectIdentity(() => QuantityItemSaveEditor.SetAmount(estate, item, 5));
                RejectIdentity(() => RaidInventorySaveEditor.SetAmount(raid, bagItem, 5, 40));
                RejectIdentity(() => TrinketSaveEditor.AddCopies(estate, trinket, 1, 100));
                await RejectIdentityAsync(() => service.PrepareQuantityItemEditAsync(profile, item, 5, content));
                await RejectIdentityAsync(() => service.PrepareTrinketEditAsync(profile, trinket, 1, trinkets.Storage, content));
            }
        }
        Assert(town.Issues.Any(s => s.Contains("UTF-8", StringComparison.Ordinal)) &&
               trinkets.Issues.Any(s => s.Contains("UTF-8", StringComparison.Ordinal)),
            "Unavailable save identities must carry a catalog diagnostic.");
        Assert(trinkets.Trinkets.Where(t => t.Id.StartsWith("nul_alias", StringComparison.Ordinal)).All(t => t.HasProviderConflict),
            "A NUL-terminated definition must not evade native hash collision checks and leave its short alias writable.");
        RejectIdentity(() => TrinketSaveEditor.AddCopies(estate, trinkets.Trinkets.Single(t => t.Id.Contains('\0')), 1, 100));
        RejectIdentity(() => TrinketSaveEditor.AddCopies(estate, trinkets.Trinkets.First() with { Id = "bad\ud800" }, 1, 100));

        var longWallet = town.Items.Single(i => i.InventoryType == "heirloom" && i.ItemId == ascii + "b");
        // Gold persists only its type. The authored item ID is not part of its wallet identity.
        var gold = town.Items.Single(i => i.InventoryType == "gold");
        Assert(gold.SaveIdentityIssue.Length == 0 && longWallet.SaveIdentityIssue.Contains("type", StringComparison.Ordinal),
            "Wallet validation must follow persisted type (including heirloom ID mapping), not blindly validate unused definition fields.");
        RejectIdentity(() => QuantityItemSaveEditor.SetAmount(estate, longWallet, 5));
        await RejectIdentityAsync(() => service.PrepareQuantityItemEditAsync(profile, longWallet, 5, content));
        var goldPreview = await service.PrepareQuantityItemEditAsync(profile, gold, 5, content);
        Assert(goldPreview.Preview.TargetAmount == 5, "Gold with an unused long definition ID must remain writable in the wallet.");

        var savedOnlyRoot = estate.DeepClone().AsObject();
        savedOnlyRoot["base_root"]!["estate_items"]!["items"]!["0"] = new JsonObject
            { ["type"] = "estate", ["id"] = new string('r', 64), ["amount"] = 3 };
        var savedOnly = QuantityItemCatalog.Load(content, savedOnlyRoot).Items.Single(i => i.IsSaveOnly);
        Assert(savedOnly.CurrentAmount == 3 && savedOnly.SaveIdentityIssue.Length > 0 &&
               QuantityItemCatalog.RefreshSavedAmounts(content, town, savedOnlyRoot, "refresh").Items.Single(i => i.IsSaveOnly).SaveIdentityIssue.Length > 0,
            "Loading and refreshing must preserve and explain an unreadable save-only ID without truncating it.");
        RejectIdentity(() => QuantityItemSaveEditor.SetAmount(savedOnlyRoot, savedOnly, 0));
        await RejectIdentityAsync(() => service.PrepareQuantityItemEditAsync(profile, savedOnly, 0, content));

        var counterRoot = estate;
        foreach (var c in counterCases)
        {
            var definition = trinkets.Trinkets.Single(t => t.Id == c.Id);
            Assert(definition.QuestUses == c.Quest && definition.TriggerLimit == c.Trigger,
                $"First-member Int32 counter semantics differ for {c.Id} ({kind}).");
            counterRoot = TrinketSaveEditor.AddCopies(counterRoot, definition, 2, 100).UpdatedRoot;
        }
        var counterJson = Path.Combine(root, "counters.json");
        var counterDson = Path.Combine(root, "counters.dson");
        var counterDecoded = Path.Combine(root, "counters.restored.json");
        File.WriteAllText(counterJson, counterRoot.ToJsonString());
        await codec.EncodeAsync(counterJson, counterDson, null);
        await codec.DecodeAsync(counterDson, counterDecoded);
        var restored = JsonSupport.ReadObject(counterDecoded)["base_root"]!["trinkets"]!["items"]!.AsObject();
        foreach (var c in counterCases)
        {
            var copies = restored.Select(p => p.Value!.AsObject()).Where(i => i["id"]!.GetValue<string>() == c.Id).ToArray();
            Assert(copies.Length == 2 && copies.All(i =>
                (c.Quest is >= 0 ? i["quest_uses_remaining"]?.GetValue<int>() == c.Quest && i["used_during_quest"]?.GetValue<bool>() == false :
                    !i.ContainsKey("quest_uses_remaining") && !i.ContainsKey("used_during_quest")) &&
                (c.Trigger is >= 0 ? i["triggers_remaining"]?.GetValue<int>() == c.Trigger : !i.ContainsKey("triggers_remaining"))),
                $"New copies must round-trip native counter presence and values for {c.Id} ({kind}).");
        }
        var zero = trinkets.Trinkets.Single(t => t.Id == "zero");
        var zeroPreview = await service.PrepareTrinketEditAsync(profile, zero, 1, trinkets.Storage, content);
        await service.CommitAsync(zeroPreview);
        await codec.DecodeAsync(profile.EstateSavePath, estateDecoded);
        Assert(JsonSupport.ReadObject(estateDecoded)["base_root"]!["trinkets"]!["items"]!["0"]!["quest_uses_remaining"]!.GetValue<int>() == 0,
            "A real zero-counter save commit must retain zero through binary restore.");
        await service.PrepareTrinketEditAsync(profile, trinkets.Trinkets.Single(t => t.Id == "null_first"), 1, trinkets.Storage, content);

        // A counter change after preview must still invalidate the prepared write.
        var pinned = await service.PrepareTrinketEditAsync(profile, zero, 1, trinkets.Storage, content);
        var before = ComputeSha256(profile.EstateSavePath);
        WriteMultiMash(contentRoot, definitionsPath, trinketJson.Replace("\"quest_uses\":0", "\"quest_uses\":1", StringComparison.Ordinal));
        var staleRejected = false;
        try { await service.CommitAsync(pinned); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("changed", StringComparison.OrdinalIgnoreCase)) { staleRejected = true; }
        Assert(staleRejected && ComputeSha256(profile.EstateSavePath) == before,
            "Counter/default support must not weaken definition-change guards or overwrite a guarded save.");
        WriteMultiMash(contentRoot, definitionsPath, trinketJson);

        foreach (var id in new[] { ascii, chinese })
        {
            var item = town.Items.Single(i => i.StorageKind == QuantityItemStorageKind.EstateItems && i.ItemId == id);
            var p = await service.PrepareQuantityItemEditAsync(profile, item, 3, content);
            await service.CommitAsync(p);
            var t = await service.PrepareTrinketEditAsync(profile, trinkets.Trinkets.Single(i => i.Id == id), 1, trinkets.Storage, content);
            await service.CommitAsync(t);
        }
        await codec.DecodeAsync(profile.EstateSavePath, estateDecoded);
        var saved = JsonSupport.ReadObject(estateDecoded);
        Assert(new[] { ascii, chinese }.All(id => TrinketSaveEditor.CountCopies(saved, id) == 1 &&
               QuantityItemSaveEditor.CountAmount(saved, town.Items.Single(i => i.StorageKind == QuantityItemStorageKind.EstateItems && i.ItemId == id)) == 3),
            "63-byte ASCII and multibyte IDs must commit unchanged to both town inventories.");

        File.WriteAllText(gameDecoded, """{"base_root":{"inraid":true,"raiddungeon":"weald"}}""");
        await codec.EncodeAsync(gameDecoded, gameSave, null);
        content = content with { SourceGameSha256 = ComputeSha256(gameSave) };
        foreach (var item in raidCatalog.Items.Where(i => i.SaveIdentityIssue.Length > 0))
            await RejectIdentityAsync(() => service.PrepareQuantityItemEditAsync(profile, item, 5, content));
        var longTypeItem = raidCatalog.Items.Single(i => i.InventoryType == type64);
        RejectIdentity(() => RaidInventorySaveEditor.SetAmount(raid, longTypeItem, 5, 40));
        foreach (var item in raidCatalog.Items.Where(i => (i.InventoryType == "estate" && (i.ItemId == ascii || i.ItemId == chinese)) || i.InventoryType == type63))
        {
            var p = await service.PrepareQuantityItemEditAsync(profile, item, 5, content);
            Assert(p.Preview.ResultingMatchingEntries == 3, "A valid bounded ID must use its own stack limit of 2, not the longer sibling's 9.");
            await service.CommitAsync(p);
        }
        await codec.DecodeAsync(profile.RaidSavePath, raidDecoded);
        var bag = JsonSupport.ReadObject(raidDecoded)["base_root"]!["party"]!["inventory"]!["items"]!.AsObject();
        Assert(bag.Count == 9 && bag.All(p => p.Value!["amount"]!.GetValue<int>() is 1 or 2),
            "Raid commits must preserve exact bounded identities and distribute 5 items across three native-sized stacks.");
        Assert(JsonNode.DeepEquals(estate, JsonNode.Parse("""{"base_root":{"version":1,"wallet":{},"estate_items":{"items":{}},"trinkets":{"items":{}}}}""")),
            "Identity rejection and pristine construction must never mutate caller-owned input roots.");
        Console.WriteLine($"PASS: {kind} inventory save identities, 63/64-byte UTF-8 boundaries, wallet mapping, counters, DSON commits and content guards.");
    }

    private static void RejectIdentity(Action action)
    {
        try { action(); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("UTF-8", StringComparison.Ordinal) || ex.Message.Contains("NUL", StringComparison.Ordinal)) { return; }
        throw new InvalidOperationException("An unrepresentable inventory identity was allowed to mutate a save.");
    }

    private static async Task RejectIdentityAsync(Func<Task> action)
    {
        try { await action(); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("UTF-8", StringComparison.Ordinal) || ex.Message.Contains("NUL", StringComparison.Ordinal)) { return; }
        throw new InvalidOperationException("An unrepresentable inventory identity passed save preflight.");
    }
}
