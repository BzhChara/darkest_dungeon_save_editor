internal static partial class ContractSuite
{
    private static async Task VerifyInventoryIdentitiesAsync(ActiveContentSnapshot original, string runRoot, DsonSaveCodec codec)
    {
        var root = Path.Combine(runRoot, "inventory-identities");
        void Write(string path, string text) => WriteMultiMash(root, path, text);
        Write("inventory/identity.inventory.items.darkest", """
            inventory_item: .type estate .id ni_case .base_stack_limit 3
            inventory_item: .type estate .id NI_CASE .base_stack_limit 7
            inventory_item: .type estate .id " ni_case " .base_stack_limit 9
            inventory_item: .type estate .id ni_case .base_stack_limit 99
            inventory_item: .type Estate .id ni_type .base_stack_limit 4
            """);
        Write("trinkets/identity.entries.trinkets.json", """
            {"entries":[
              {"id":"ni_case","rarity":"common","price":11},
              {"id":"NI_CASE","rarity":"rare","price":22},
              {"id":" ni_case ","rarity":"very_rare","price":33},
              {"id":"ni_case","rarity":"common","price":99}
            ]}
            """);
        Write("inventory/identity.inventory.system_configs.darkest", """
            inventory_system_config: .type raid .max_slots 16
            inventory_system_config: .type trinket_storage .max_slots 16
            """);
        Write("campaign/provision/identity.provision.json", """
            {"default_store_inventory_item_lists":[[{"type":"estate","id":"ni_case","amount":1}]]}
            """);
        Directory.CreateDirectory(Path.Combine(root, "localization"));
        WriteLoc2(Path.Combine(root, "localization", "identity_english.loc2"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["str_inventory_title_estateni_case"] = "Lower item",
                ["str_inventory_title_estateNI_CASE"] = "Upper item",
                ["str_inventory_title_trinketni_case"] = "Lower trinket",
                ["str_inventory_title_trinketNI_CASE"] = "Upper trinket"
            });
        WriteLegacyLoc(Path.Combine(root, "localization", "identity_schinese.loc"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["str_inventory_title_estateni_case"] = "小写物品",
                ["str_inventory_title_estateNI_CASE"] = "大写物品"
            });
        Write("localization/identity.string_table.xml", """
            <root><language id="english">
              <entry id="str_inventory_title_estate ni_case ">Spaced item</entry>
              <entry id="str_inventory_title_trinket ni_case ">Spaced trinket</entry>
            </language></root>
            """);
        WriteFixtureManifest(root);
        var content = original with { Sources =
            [new ActiveContentSource("local:inventory-identities", "Inventory identities", "local", root, -2000)] };
        var estate = new JsonObject { ["base_root"] = new JsonObject
        {
            ["wallet"] = new JsonObject(),
            ["estate_items"] = new JsonObject { ["items"] = IdentityItems("estate") },
            ["trinkets"] = new JsonObject { ["items"] = IdentityItems("trinket", amount: 1) }
        } };
        var raid = new JsonObject { ["base_root"] = new JsonObject
        { ["party"] = new JsonObject { ["inventory"] = new JsonObject { ["items"] = IdentityItems("estate") } } } };
        var failures = new List<string>();
        void Check(bool condition, string message) { if (!condition) failures.Add(message); }
        var definition = new QuantityItemDefinition("estate", "ni_case", QuantityItemStorageKind.EstateItems,
            3, false, 0, "probe", "probe", false, []);
        var raidDefinition = definition with { StorageKind = QuantityItemStorageKind.RaidInventory };
        var residueRoot = Path.Combine(runRoot, "inventory-identity-residue");
        WriteMultiMash(residueRoot, "inventory/residue.inventory.items.darkest",
            "inventory_item: .type estate .id ni_case .base_stack_limit 3\n");
        WriteFixtureManifest(residueRoot);
        var residueContent = content with { Sources =
            [new ActiveContentSource("local:identity-residue", "Identity residue", "local", residueRoot, 0)] };
        var residueCatalog = QuantityItemCatalog.Load(residueContent, estate);
        Check(residueCatalog.Items.SingleOrDefault(row => row.ItemId == "ni_case") is { HasProviderConflict: false, CurrentAmount: 2 } &&
              residueCatalog.Items.SingleOrDefault(row => row.ItemId == "NI_CASE") is { IsSaveOnly: true, CurrentAmount: 5 } &&
              residueCatalog.Items.SingleOrDefault(row => row.ItemId == " ni_case ") is { IsSaveOnly: true, CurrentAmount: 8 },
            "A writable sole definition absorbed case/space-distinct save-only residue.");
        Check(Loc2LocalizationReader.HashName("ni_case") != Loc2LocalizationReader.HashName("NI_CASE"),
            "The native identity hash must distinguish case.");
        Check(QuantityItemSaveEditor.CountAmount(estate, definition) == 2, "Town count merged distinct IDs.");
        var reducedTown = QuantityItemSaveEditor.SetAmount(estate, definition, 0).UpdatedRoot;
        Check(reducedTown["base_root"]!["estate_items"]!["items"]!["1"]!["amount"]!.GetValue<int>() == 5 &&
              reducedTown["base_root"]!["estate_items"]!["items"]!["2"]!["amount"]!.GetValue<int>() == 8,
            "Town reduction changed a case/space-distinct ID.");
        Check(RaidInventorySaveEditor.CountAmount(raid, raidDefinition) == 2, "Raid count merged distinct IDs.");
        var reducedRaid = RaidInventorySaveEditor.SetAmount(raid, raidDefinition, 0, 16).UpdatedRoot;
        var raidItems = (JsonObject)reducedRaid["base_root"]!["party"]!["inventory"]!["items"]!;
        Check(!raidItems.ContainsKey("0") && raidItems.ContainsKey("1") && raidItems.ContainsKey("2"),
            "Raid deletion removed another ID's stacks.");
        Check(TrinketSaveEditor.CountCopies(estate, "ni_case") == 1 &&
              TrinketSaveEditor.Summarize(estate).UniqueTrinketIds == 3,
            "Trinket counts merged distinct IDs.");

        var townCatalog = QuantityItemCatalog.Load(content, estate);
        var raidCatalog = QuantityItemCatalog.LoadRaid(content, raid);
        var trinkets = TrinketCatalog.Load(content);
        foreach (var (id, amount, limit) in new[] { ("ni_case", 2, 3), ("NI_CASE", 5, 7), (" ni_case ", 8, 9) })
        {
            var item = townCatalog.Items.SingleOrDefault(row => row.ItemId == id);
            Check(item is not null && !item.IsSaveOnly && !item.HasProviderConflict &&
                  item.CurrentAmount == amount && item.BaseStackLimit == limit, $"Town catalog lost identity '{id}'.");
            var raidItem = raidCatalog.Items.SingleOrDefault(row => row.ItemId == id);
            Check(raidItem is not null && !raidItem.HasProviderConflict && raidItem.CurrentAmount == amount &&
                  raidItem.BaseStackLimit == limit, $"Raid catalog lost identity '{id}'.");
            var trinket = trinkets.Trinkets.SingleOrDefault(row => row.Id == id);
            Check(trinket is not null && !trinket.HasProviderConflict && trinket.Price == (id == "ni_case" ? 11 : id == "NI_CASE" ? 22 : 33),
                $"Trinket catalog lost identity '{id}'.");
        }
        Check(townCatalog.Items.All(row => row.ItemId != "ni_type") &&
              raidCatalog.Items.Any(row => row.InventoryType == "Estate" && row.ItemId == "ni_type"),
            "A custom case-distinct inventory type was misclassified as town estate storage.");
        Check(raidCatalog.Items.SingleOrDefault(row => row.ItemId == "ni_case")?.ReferenceStatus == QuantityItemReferenceStatus.ConfirmedActive &&
              raidCatalog.Items.SingleOrDefault(row => row.ItemId == "NI_CASE")?.ReferenceStatus == QuantityItemReferenceStatus.SuspectedUnused &&
              raidCatalog.Items.SingleOrDefault(row => row.ItemId == " ni_case ")?.ReferenceStatus == QuantityItemReferenceStatus.SuspectedUnused,
            "Item reference identities: " + string.Join("; ", raidCatalog.Items.Where(row => row.ItemId.Contains("case", StringComparison.OrdinalIgnoreCase))
                .Select(row => $"'{row.ItemId}'={row.ReferenceStatus} [{string.Join(", ", row.ReferenceEvidence)}]")));
        Check(townCatalog.Items.SingleOrDefault(row => row.ItemId == "ni_case")?.LocalizedName.English == "Lower item" &&
              townCatalog.Items.SingleOrDefault(row => row.ItemId == "NI_CASE")?.LocalizedName.English == "Upper item" &&
              trinkets.Trinkets.SingleOrDefault(row => row.Id == "ni_case")?.LocalizedName.English == "Lower trinket" &&
              trinkets.Trinkets.SingleOrDefault(row => row.Id == "NI_CASE")?.LocalizedName.English == "Upper trinket",
            "Localization cache mixed different identity hashes.");
        Check(townCatalog.Items.Single(row => row.ItemId == "ni_case").LocalizedName.Chinese == "小写物品" &&
              townCatalog.Items.Single(row => row.ItemId == "NI_CASE").LocalizedName.Chinese == "大写物品" &&
              townCatalog.Items.Single(row => row.ItemId == " ni_case ").LocalizedName.English == "Spaced item" &&
              trinkets.Trinkets.Single(row => row.Id == " ni_case ").LocalizedName.English == "Spaced trinket",
            "Legacy LOC or XML key normalization changed resource identities.");
        var refreshed = QuantityItemCatalog.RefreshSavedAmounts(content, townCatalog, reducedTown, "identity-refresh");
        Check(refreshed.Items.SingleOrDefault(row => row.ItemId == "ni_case")?.CurrentAmount == 0 &&
              refreshed.Items.SingleOrDefault(row => row.ItemId == "NI_CASE")?.CurrentAmount == 5,
            "Saved-amount refresh mixed identities.");
        Check((definition with { InventoryType = "custom:a", ItemId = "b" }).CatalogKey !=
              (definition with { InventoryType = "custom", ItemId = "a:b" }).CatalogKey,
            "Catalog key separators made two different type/ID pairs alias.");
        Assert(failures.Count == 0, "Inventory identity regressions:\n" + string.Join('\n', failures));
        await VerifyInventoryIdentityWritesAsync(content, residueContent, estate, raid, townCatalog, trinkets, codec, runRoot);
        Console.WriteLine("PASS: exact inventory/trinket IDs, untouched sibling stacks, reference reachability, localization and saved-amount refresh.");
    }

    private static async Task VerifyInventoryIdentityWritesAsync(ActiveContentSnapshot content, ActiveContentSnapshot residueContent, JsonObject estate, JsonObject raid,
        QuantityItemCatalogResult townCatalog, TrinketCatalogResult trinkets, DsonSaveCodec codec, string runRoot)
    {
        var root = Path.Combine(runRoot, "inventory-identity-writes");
        var profileRoot = Path.Combine(root, "profile");
        Directory.CreateDirectory(profileRoot);
        var estateDecoded = Path.Combine(root, "estate.json");
        File.WriteAllText(estateDecoded, estate.ToJsonString());
        var estatePath = Path.Combine(profileRoot, "persist.estate.json");
        await codec.EncodeAsync(estateDecoded, estatePath, originalBinaryPath: null);
        var gameDecoded = Path.Combine(root, "game.json");
        File.WriteAllText(gameDecoded, """{"base_root":{"inraid":false,"raiddungeon":"none"}}""");
        var gamePath = Path.Combine(profileRoot, "persist.game.json");
        await codec.EncodeAsync(gameDecoded, gamePath, originalBinaryPath: null);
        var profile = new SaveProfile("profile", profileRoot, estatePath, "contract-user", DateTime.UtcNow);
        var snapshot = content with { Profile = profile, DecodedGamePath = gameDecoded,
            SourceGameSha256 = ComputeSha256(gamePath), WorkspaceDirectory = root };
        var service = new SaveEditService(codec, new SaveEditorLocations(root,
            Path.Combine(root, "workspace"), Path.Combine(root, "backups")));
        var residueEdit = await service.PrepareQuantityItemEditAsync(profile,
            QuantityItemCatalog.Load(residueContent, estate).Items.Single(item => item.ItemId == "ni_case"), 0,
            snapshot with { Sources = residueContent.Sources });
        Assert(residueEdit.Preview.ExistingAmount == 2 && residueEdit.Preview.MatchingEntries == 1,
            "The real preview path must not absorb save-only siblings when only one active definition exists.");
        var upper = townCatalog.Items.Single(item => item.ItemId == "NI_CASE");
        var prepared = await service.PrepareQuantityItemEditAsync(profile, upper, 6, snapshot);
        Assert(prepared.Preview.ExistingAmount == 5, "Quantity preview must bind the exact uppercase definition.");
        await service.CommitAsync(prepared);
        var decoded = Path.Combine(root, "roundtrip-estate.json");
        await codec.DecodeAsync(estatePath, decoded);
        var saved = JsonSupport.ReadObject(decoded);
        var items = (JsonObject)saved["base_root"]!["estate_items"]!["items"]!;
        Assert(items["0"]!["amount"]!.GetValue<int>() == 2 && items["1"]!["amount"]!.GetValue<int>() == 6 &&
               items["2"]!["id"]!.GetValue<string>() == " ni_case " && items["2"]!["amount"]!.GetValue<int>() == 8,
            "Guarded town write and DSON round trip must preserve sibling IDs and amounts exactly.");

        var upperTrinket = trinkets.Trinkets.Single(item => item.Id == "NI_CASE");
        var trinketEdit = await service.PrepareTrinketEditAsync(profile, upperTrinket, 1, trinkets.Storage, snapshot);
        Assert(trinketEdit.Preview.ExistingCopies == 1 && trinketEdit.Trinket.Price == 22,
            "Trinket preview must use the selected ID's count and complete first definition.");
        await service.CommitAsync(trinketEdit);
        await codec.DecodeAsync(estatePath, decoded);
        saved = JsonSupport.ReadObject(decoded);
        Assert(TrinketSaveEditor.CountCopies(saved, "NI_CASE") == 2 && TrinketSaveEditor.CountCopies(saved, "ni_case") == 1 &&
               TrinketSaveEditor.CountCopies(saved, " ni_case ") == 1, "Trinket commit mixed identity counts.");

        // A spelling which was never defined must not borrow a differently cased provider.
        var rejected = false;
        try { await service.PrepareTrinketEditAsync(profile, upperTrinket with { Id = "Ni_Case" }, 1, trinkets.Storage, snapshot); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("definition changed", StringComparison.Ordinal)) { rejected = true; }
        Assert(rejected, "A wrong-case trinket request must be rejected before writing.");

        File.WriteAllText(gameDecoded, """{"base_root":{"inraid":true,"raiddungeon":"weald"}}""");
        await codec.EncodeAsync(gameDecoded, gamePath, originalBinaryPath: null);
        var raidDecoded = Path.Combine(root, "raid.json");
        File.WriteAllText(raidDecoded, raid.ToJsonString());
        await codec.EncodeAsync(raidDecoded, profile.RaidSavePath, originalBinaryPath: null);
        snapshot = snapshot with { SourceGameSha256 = ComputeSha256(gamePath) };
        var raidCatalog = QuantityItemCatalog.LoadRaid(snapshot, raid);
        var spaced = raidCatalog.Items.Single(item => item.ItemId == " ni_case ");
        var raidEdit = await service.PrepareQuantityItemEditAsync(profile, spaced, 0, snapshot);
        Assert(raidEdit.Preview.ExistingAmount == 8, "Raid preview must retain whitespace in the selected resource ID.");
        await service.CommitAsync(raidEdit);
        await codec.DecodeAsync(profile.RaidSavePath, raidDecoded);
        items = (JsonObject)JsonSupport.ReadObject(raidDecoded)["base_root"]!["party"]!["inventory"]!["items"]!;
        Assert(items.Count == 2 && items["0"]!["amount"]!.GetValue<int>() == 2 &&
               items["1"]!["amount"]!.GetValue<int>() == 5, "Guarded raid deletion must remove only the selected exact ID.");
    }

    private static JsonObject IdentityItems(string type, int? amount = null) => new()
    {
        ["0"] = new JsonObject { ["type"] = type, ["id"] = "ni_case", ["amount"] = amount ?? 2 },
        ["1"] = new JsonObject { ["type"] = type, ["id"] = "NI_CASE", ["amount"] = amount ?? 5 },
        ["2"] = new JsonObject { ["type"] = type, ["id"] = " ni_case ", ["amount"] = amount ?? 8 }
    };
}
