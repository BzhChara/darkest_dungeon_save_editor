internal static partial class ContractSuite
{
    public static async Task RunQuantityNativeOnlyAsync(string repositoryRoot)
    {
        var root = Path.Combine(repositoryRoot, "artifacts", "quantity-native-contracts", Guid.NewGuid().ToString("N"));
        await RunQuantityNativeContractsAsync(root, DsonSaveCodec.CreateNative());
        Console.WriteLine($"Artifacts: {root}");
    }

    private static async Task RunQuantityNativeContractsAsync(string root, DsonSaveCodec codec)
    {
        Assert(HashLoc2Key("Az") == HashLoc2Key("BE") &&
               HashLoc2Key("estate") == HashLoc2Key("f>tate") &&
               HashLoc2Key("supply") == HashLoc2Key("t@pply") &&
               HashLoc2Key("gold") == HashLoc2Key("h:ld"),
            "Independent fixture names have native hash collisions.");
        foreach (var scene in new[] { "estate", "wallet", "raid" })
        {
            foreach (var form in new[] { "exact", "hash", "type-hash", "nul-suffix", "ascii-bound", "utf8-bound" })
                await VerifyQuantitySavedIdentityAsync(Path.Combine(root, scene, form), scene, form, codec);
            await VerifyQuantitySaveOnlyIdentityAsync(Path.Combine(root, scene, "save-only"), scene, codec);
        }
        await VerifyNativeWalletCapAsync(Path.Combine(root, "wallet-cap"), codec);
        await VerifySharedWalletAliasBalanceAsync(Path.Combine(root, "wallet-alias-definitions"), codec);
        await VerifyQuantityProvenanceAsync(Path.Combine(root, "provenance"), codec);
        await VerifyQuantityTrinketExclusionAsync(Path.Combine(root, "trinket-exclusion"), codec);
    }

    private sealed record QuantityNativeFixture(
        ActiveContentSnapshot Content, SaveEditService Service, JsonObject Save, string DefinitionPath);

    private static JsonObject QuantityNativeSave(string scene) => JsonNode.Parse(scene == "raid"
        ? """{"base_root":{"party":{"inventory":{"items":{}}}}}"""
        : """{"base_root":{"wallet":{},"estate_items":{"items":{}},"trinkets":{"items":{}}}}""")!.AsObject();

    private static JsonObject QuantityNativeItems(JsonObject save, string scene) => scene switch
    {
        "wallet" => save["base_root"]!["wallet"]!.AsObject(),
        "estate" => save["base_root"]!["estate_items"]!["items"]!.AsObject(),
        _ => save["base_root"]!["party"]!["inventory"]!["items"]!.AsObject()
    };

    private static JsonObject QuantityNativeItem(string scene, string type, string id, int amount) =>
        scene == "wallet"
            ? new JsonObject { ["type"] = id, ["amount"] = amount, ["marker"] = "preserve" }
            : new JsonObject { ["type"] = type, ["id"] = id, ["amount"] = amount, ["marker"] = "preserve" };

    private static string QuantityNativeType(string scene) => scene switch
    {
        "wallet" => "heirloom", "estate" => "estate", _ => "supply"
    };

    private static async Task<QuantityNativeFixture> BuildQuantityNativeFixtureAsync(
        string root, string scene, string definitions, JsonObject save, DsonSaveCodec codec)
    {
        var game = Path.Combine(root, "game");
        var definitionPath = WriteMultiMash(game, "inventory/a.inventory.items.darkest", definitions);
        WriteMultiMash(game, "inventory/a.inventory.system_configs.darkest",
            "inventory_system_config: .type raid .max_slots 2\n");
        var profileRoot = Path.Combine(root, "profile_7");
        Directory.CreateDirectory(profileRoot);
        var profile = new SaveProfile("profile_7", profileRoot, Path.Combine(profileRoot, "persist.estate.json"),
            "quantity-native-contract", DateTime.UtcNow);
        var gameSave = new JsonObject { ["base_root"] = new JsonObject
        {
            ["game_mode"] = "base", ["inraid"] = scene == "raid",
            ["raiddungeon"] = scene == "raid" ? "ruins" : "none",
            ["applied_ugcs_1_0"] = new JsonObject()
        }};
        await codec.EncodeAsync(WriteMultiMash(root, "game.json", gameSave.ToJsonString()),
            Path.Combine(profileRoot, "persist.game.json"), null);
        await codec.EncodeAsync(WriteMultiMash(root, "estate.json",
            (scene == "raid" ? QuantityNativeSave("estate") : save).ToJsonString()), profile.EstateSavePath, null);
        if (scene == "raid")
            await codec.EncodeAsync(WriteMultiMash(root, "raid.json", save.ToJsonString()), profile.RaidSavePath, null);
        var content = await ActiveContentResolver.ResolveAsync(profile, game, null, codec, Path.Combine(root, "resolve"));
        ActiveContentResolver.RequireResolvedModSources(content);
        return new(content, new SaveEditService(codec,
            new SaveEditorLocations(root, Path.Combine(root, "work"), Path.Combine(root, "backups"))), save, definitionPath);
    }

    private static QuantityItemCatalogResult LoadQuantityNative(QuantityNativeFixture f, string scene) =>
        scene == "raid" ? QuantityItemCatalog.LoadRaid(f.Content, f.Save) : QuantityItemCatalog.Load(f.Content, f.Save);

    private static async Task VerifyQuantitySavedIdentityAsync(
        string root, string scene, string form, DsonSaveCodec codec)
    {
        var type = QuantityNativeType(scene);
        var id = form switch { "ascii-bound" => new string('x', 63), "utf8-bound" => new string('界', 21), _ => "Az" };
        var savedId = form switch
        {
            "hash" or "type-hash" => "BE", "nul-suffix" => "BE\0ignored",
            "ascii-bound" or "utf8-bound" => id + "ignored", _ => id
        };
        var savedType = form == "type-hash" && scene != "wallet"
            ? scene == "estate" ? "f>tate" : "t@pply" : form == "nul-suffix" ? type + "\0ignored" : type;
        var save = QuantityNativeSave(scene);
        var items = QuantityNativeItems(save, scene);
        items["0"] = QuantityNativeItem(scene, savedType, savedId, 3);
        items["1"] = QuantityNativeItem(scene, type, "az", 7);
        var originalAlias = items["0"]!.DeepClone().AsObject();
        var unrelated = items["1"]!.DeepClone();
        var f = await BuildQuantityNativeFixtureAsync(root, scene,
            $"inventory_item: .type {type} .id {id} .base_stack_limit 10\n", save, codec);
        var catalog = await QuantityItemCatalog.LoadAsync(f.Content, codec);
        var selected = catalog.Items.Single(item => !item.IsSaveOnly);
        Assert(selected.CurrentAmount == 3 && selected.SavedEntryCount == 1 && selected.IsPresentInSave &&
               catalog.Items.Count == 2 && catalog.Items.Single(item => item.IsSaveOnly).ItemId is "az" or "",
            $"{scene}/{form}: native aliases join the active row; a case-distinct identity remains separate.");
        var refreshed = QuantityItemCatalog.RefreshSavedAmounts(f.Content, catalog, save, "fresh");
        Assert(refreshed.Items.Single(item => !item.IsSaveOnly).CurrentAmount == 3 &&
               refreshed.Items.Count == catalog.Items.Count,
            $"{scene}/{form}: refresh uses the same identity as initial loading.");

        var target = scene == "raid" ? f.Content.Profile.RaidSavePath : f.Content.Profile.EstateSavePath;
        var originalHash = ComputeSha256(target);
        var prepared = await f.Service.PrepareQuantityItemEditAsync(f.Content.Profile, selected, 5, f.Content);
        Assert(prepared.Preview is { ExistingAmount: 3, TargetAmount: 5, MatchingEntries: 1, CreatedEntry: false } &&
               ComputeSha256(target) == originalHash,
            $"{scene}/{form}: preview edits the existing alias, preserves the input, and needs no extra slot.");
        await f.Service.CommitAsync(prepared);
        var decoded = Path.Combine(root, "committed.json");
        await codec.DecodeAsync(target, decoded);
        var after = JsonSupport.ReadObject(decoded);
        var afterItems = QuantityNativeItems(after, scene);
        originalAlias["amount"] = 5;
        Assert(afterItems.Count == 2 && JsonNode.DeepEquals(afterItems["0"], originalAlias) &&
               JsonNode.DeepEquals(afterItems["1"], unrelated),
            $"{scene}/{form}: actual DSON commit preserves raw alias identity, metadata and unrelated entries.");
        Assert(QuantityItemCatalog.RefreshSavedAmounts(f.Content, catalog, after, "committed")
                   .Items.Single(item => !item.IsSaveOnly).CurrentAmount == 5,
            $"{scene}/{form}: post-write refresh agrees with the target.");
        var cleared = scene == "raid"
            ? RaidInventorySaveEditor.SetAmount(after, selected, 0, 2).UpdatedRoot
            : QuantityItemSaveEditor.SetAmount(after, selected, 0).UpdatedRoot;
        var clearedItems = QuantityNativeItems(cleared, scene);
        Assert(JsonNode.DeepEquals(clearedItems["1"], unrelated) &&
               (scene == "raid" ? !clearedItems.ContainsKey("0") : clearedItems["0"]!["amount"]!.GetValue<int>() == 0),
            $"{scene}/{form}: clearing removes only matching raid stacks and zeroes matching town entries.");
        Console.WriteLine($"PASS: quantity {scene}/{form} catalog, refresh, preview, full-bag edit, DSON commit and clear.");
    }

    private static async Task VerifyQuantitySaveOnlyIdentityAsync(string root, string scene, DsonSaveCodec codec)
    {
        var type = QuantityNativeType(scene);
        var save = QuantityNativeSave(scene);
        var items = QuantityNativeItems(save, scene);
        items["0"] = QuantityNativeItem(scene, type, "Az", 3);
        items["1"] = QuantityNativeItem(scene, type, "BE", 2);
        var f = await BuildQuantityNativeFixtureAsync(root, scene, "", save, codec);
        var catalog = LoadQuantityNative(f, scene);
        var selected = catalog.Items.Single();
        Assert(selected.IsSaveOnly && selected.CurrentAmount == 5 && selected.SavedEntryCount == 2,
            "Unresolved saved aliases form one quantity row without inventing a definition.");
        var refreshed = QuantityItemCatalog.RefreshSavedAmounts(f.Content, catalog, save, "same");
        Assert(refreshed.Items.Single().CurrentAmount == 5, "Save-only aliases remain one row after refresh.");
        var prepared = await f.Service.PrepareQuantityItemEditAsync(f.Content.Profile, selected, 1, f.Content);
        Assert(prepared.Preview is { ExistingAmount: 5, TargetAmount: 1, MatchingEntries: 2 },
            "Save-only reductions count every native-equivalent entry.");
        File.WriteAllText(f.DefinitionPath, $"inventory_item: .type {type} .id BE .base_stack_limit 10\n");
        await ExpectQuantityDefinitionRebindAsync(() =>
            f.Service.PrepareQuantityItemEditAsync(f.Content.Profile, selected, 1, f.Content));
        await ExpectQuantityDefinitionRebindAsync(() => f.Service.CommitAsync(prepared));
        Console.WriteLine($"PASS: quantity {scene} save-only aliases, duplicate totals and native-identity rebind guards.");
    }

    private static async Task ExpectQuantityDefinitionRebindAsync(Func<Task> action)
    {
        try { await action(); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("gained an active content definition", StringComparison.Ordinal))
        { return; }
        throw new InvalidOperationException("A save-only selection must be invalidated when a native-equivalent definition appears.");
    }

    private static async Task VerifyNativeWalletCapAsync(string root, DsonSaveCodec codec)
    {
        var save = QuantityNativeSave("wallet");
        var items = QuantityNativeItems(save, "wallet");
        items["0"] = QuantityNativeItem("wallet", "", "gold", 3);
        var f = await BuildQuantityNativeFixtureAsync(root, "wallet",
            "inventory_item: .type gold .base_stack_limit 1750\n", save, codec);
        var catalog = LoadQuantityNative(f, "wallet");
        var gold = catalog.Items.Single();
        foreach (var invalid in new[] { 2_000_000_001, int.MaxValue })
        {
            try { QuantityItemSaveEditor.SetAmount(save, gold, invalid); throw new Exception("Wallet limit was not enforced."); }
            catch (ArgumentOutOfRangeException) { }
            try { await f.Service.PrepareQuantityItemEditAsync(f.Content.Profile, gold, invalid, f.Content); throw new Exception("Preview admitted an invalid wallet target."); }
            catch (ArgumentOutOfRangeException) { }
        }
        var prepared = await f.Service.PrepareQuantityItemEditAsync(f.Content.Profile, gold, 2_000_000_000, f.Content);
        await f.Service.CommitAsync(prepared);
        var decoded = Path.Combine(root, "committed.json");
        await codec.DecodeAsync(f.Content.Profile.EstateSavePath, decoded);
        Assert(JsonSupport.ReadObject(decoded)["base_root"]!["wallet"]!["0"]!["amount"]!.GetValue<int>() == 2_000_000_000,
            "The native wallet maximum survives an actual DSON commit.");

        items["0"]!["amount"] = int.MaxValue;
        items["1"] = QuantityNativeItem("wallet", "", "h:ld", int.MaxValue);
        Assert(LoadQuantityNative(f, "wallet").Items.Single().CurrentAmount == 2_000_000_000 &&
               QuantityItemCatalog.RefreshSavedAmounts(f.Content, catalog, save, "large").Items.Single().CurrentAmount == 2_000_000_000 &&
               QuantityItemSaveEditor.CountAmount(save, gold) == 2_000_000_000,
            "Initial load, refresh and mutation counts clamp duplicate wallet totals without overflowing Int32.");
        var lowered = QuantityItemSaveEditor.SetAmount(save, gold, 1);
        Assert(lowered.Preview.ExistingAmount == 2_000_000_000 &&
               lowered.UpdatedRoot["base_root"]!["wallet"]!["0"]!["amount"]!.GetValue<int>() == 1 &&
               lowered.UpdatedRoot["base_root"]!["wallet"]!["1"]!["amount"]!.GetValue<int>() == 0 &&
               lowered.UpdatedRoot["base_root"]!["wallet"]!["1"]!["type"]!.GetValue<string>() == "h:ld",
            "Reducing an over-cap wallet removes hidden excess while preserving duplicate identities.");
        var estate = QuantityNativeSave("estate");
        var estateItem = gold with { InventoryType = "estate", ItemId = "token", StorageKind = QuantityItemStorageKind.EstateItems };
        Assert(QuantityItemSaveEditor.SetAmount(estate, estateItem, int.MaxValue).Preview.TargetAmount == int.MaxValue,
            "The wallet cap does not impose an unsupported cap on estate items.");
        var raidItem = estateItem with { InventoryType = "supply", StorageKind = QuantityItemStorageKind.RaidInventory, BaseStackLimit = int.MaxValue };
        Assert(RaidInventorySaveEditor.SetAmount(QuantityNativeSave("raid"), raidItem, int.MaxValue, 1).Preview.TargetAmount == int.MaxValue,
            "The wallet cap does not impose an unsupported cap on raid stacks.");
        Console.WriteLine("PASS: native wallet cap, over-cap rejection, duplicate clamping, reduction, DSON persistence and non-wallet controls.");
    }

    private static async Task VerifyQuantityProvenanceAsync(string root, DsonSaveCodec codec)
    {
        var f = await BuildQuantityNativeFixtureAsync(root, "estate",
            "inventory_item: .type estate .id token .base_stack_limit 2\n", QuantityNativeSave("estate"), codec);
        var game = Path.Combine(root, "game");
        var mod = Path.Combine(game, "mods", "High");
        WriteMultiMash(mod, "project.xml", "<project><Title>Higher priority Mod</Title></project>");
        var modFile = WriteMultiMash(mod, "inventory/z.inventory.items.darkest",
            "inventory_item: .type estate .id token .base_stack_limit 9\n");
        WriteMultiMash(mod, "modfiles.txt", "inventory/z.inventory.items.darkest\n");
        var gameJson = JsonNode.Parse("""{"base_root":{"game_mode":"base","inraid":false,"raiddungeon":"none","applied_ugcs_1_0":{"0":{"name":"Higher priority Mod","source":"mod_local_source"}}}}""")!.AsObject();
        await codec.EncodeAsync(WriteMultiMash(root, "enabled.json", gameJson.ToJsonString()),
            Path.Combine(f.Content.Profile.ProfileDirectory, "persist.game.json"), null);
        var content = await ActiveContentResolver.ResolveAsync(f.Content.Profile, game, null, codec, Path.Combine(root, "enabled"));
        ActiveContentResolver.RequireResolvedModSources(content);
        foreach (var samePath in new[] { false, true })
        {
            if (samePath)
            {
                WriteMultiMash(mod, "inventory/a.inventory.items.darkest", File.ReadAllText(modFile));
                File.WriteAllText(Path.Combine(mod, "modfiles.txt"), "inventory/a.inventory.items.darkest\n");
            }
            foreach (var raid in new[] { false, true })
            {
                var catalog = raid ? QuantityItemCatalog.LoadRaid(content, QuantityNativeSave("raid"))
                    : QuantityItemCatalog.Load(content, f.Save);
                var item = catalog.Items.Single();
                Assert(item.Source == (samePath ? "local:Higher priority Mod" : "base") &&
                       item.BaseStackLimit == (samePath ? 9 : 2),
                    "Different paths retain native first-definition order; a same-path Mod replaces the base file.");
                var previousCulture = EditorText.Culture;
                var previousSystemCulture = EditorText.SystemCulture;
                try
                {
                    foreach (var language in new[] { "en", "zh-CN" })
                    {
                        EditorText.Initialize(language, previousSystemCulture);
                        Assert(item.SourceLabel.Contains("Higher priority Mod", StringComparison.Ordinal) == samePath,
                            "Both languages show the actual effective quantity definition, not the maximum Mod priority.");
                    }
                }
                finally { EditorText.Initialize(previousCulture.Name, previousSystemCulture); }
            }
        }
        Console.WriteLine("PASS: town/raid quantity provenance, distinct-path first match, same-path override and both UI languages.");
    }

    private static async Task VerifySharedWalletAliasBalanceAsync(string root, DsonSaveCodec codec)
    {
        var save = QuantityNativeSave("wallet");
        QuantityNativeItems(save, "wallet")["0"] = QuantityNativeItem("wallet", "", "gold", 3);
        var f = await BuildQuantityNativeFixtureAsync(root, "wallet",
            "inventory_item: .type gold .id \"\" .base_stack_limit 10\n" +
            "inventory_item: .type heirloom .id h:ld .base_stack_limit 10\n", save, codec);
        var catalog = LoadQuantityNative(f, "wallet");
        Assert(catalog.Items.Count > 0 && catalog.Items.All(item => !item.IsSaveOnly &&
                   item.CurrentAmount == 3 && item.SavedEntryCount == 1 &&
                   QuantityItemSaveEditor.CountAmount(save, item) == 3),
            "Definitions addressing the same native wallet balance must not consume it from one another's display.");
        QuantityNativeItems(save, "wallet")["0"]!["amount"] = 5;
        var refreshed = QuantityItemCatalog.RefreshSavedAmounts(f.Content, catalog, save, "updated");
        Assert(refreshed.Items.All(item => item.CurrentAmount == 5 && item.SavedEntryCount == 1),
            "Shared native wallet balance remains consistent after refresh.");
        Console.WriteLine("PASS: shared native-wallet definition aliases agree across catalog, count and refresh.");
    }

    private static async Task VerifyQuantityTrinketExclusionAsync(string root, DsonSaveCodec codec)
    {
        Assert(HashLoc2Key("u=inket") == HashLoc2Key("trinket"), "Fixture type aliases the native trinket type.");
        var save = QuantityNativeSave("raid");
        var items = QuantityNativeItems(save, "raid");
        items["0"] = QuantityNativeItem("raid", "u=inket", "one", 1);
        items["1"] = QuantityNativeItem("raid", "trinket\0ignored", "two", 1);
        var f = await BuildQuantityNativeFixtureAsync(root, "raid",
            "inventory_item: .type u=inket .id one .base_stack_limit 99\n", save, codec);
        var catalog = LoadQuantityNative(f, "raid");
        Assert(catalog.Items.Count == 0 && catalog.RaidOccupiedSlots == 2 &&
               QuantityItemCatalog.RefreshSavedAmounts(f.Content, catalog, save, "same").Items.Count == 0,
            "Native-equivalent trinket types remain instance-based and occupy slots without entering quantity editing.");
        Console.WriteLine("PASS: hash/NUL trinket types stay outside raid quantity rows and remain occupied slots.");
    }
}
