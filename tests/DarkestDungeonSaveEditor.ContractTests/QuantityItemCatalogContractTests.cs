internal static partial class ContractSuite
{
    private sealed record QuantityCatalogContractState(
        QuantityItemDefinition CatalogModEssence,
        JsonObject QuantityRaidRoot);

    private static async Task<QuantityCatalogContractState> RunQuantityItemCatalogContractsAsync(
        ActiveContentSnapshot activeContent,
        string decodedSeedPath,
        string localRaidCampingRoot,
        string localTownEventsRoot,
        string localLootRoot,
        string activeWorkshopRoot,
        string runRoot,
        DsonSaveCodec codec,
        SaveProfile profile,
        string gameRoot,
        string workshopRoot,
        string ambiguousDirectLocalModRoot,
        string additionalLocalModDirectory,
        string estatePath,
        string decodedGameSeedPath,
        string activeWorkshopDistrictRoot)
    {
        VerifyQuestItemReachabilityContracts(activeContent, runRoot);
        var quantityEstateRoot = JsonNode.Parse(File.ReadAllText(decodedSeedPath)) as JsonObject
            ?? throw new InvalidDataException("Quantity-item estate seed is invalid.");
        var quantityCatalog = QuantityItemCatalog.Load(activeContent, quantityEstateRoot, "contract-estate-sha");
        Assert(
            quantityCatalog.SourceEstateSha256 == "contract-estate-sha" &&
            quantityCatalog.Items.Count == 11 &&
            quantityCatalog.Items.All(item => item.DisplayId != "raid_only_gem") &&
            quantityCatalog.Items.All(item => item.DisplayId != "local_raid_gem") &&
            quantityCatalog.Items.All(item => item.DisplayId != "disabled_mod_item"),
            "The quantity-item catalog must include only wallet/estate persisted definitions and ignore raid-only or disabled content.");
        var catalogGold = quantityCatalog.Items.Single(item => item.DisplayId == "gold");
        var catalogBlueprint = quantityCatalog.Items.Single(item => item.DisplayId == "blueprint");
        var catalogBlood = quantityCatalog.Items.Single(item => item.DisplayId == "the_blood");
        var catalogModEssence = quantityCatalog.Items.Single(item => item.DisplayId == "local_mod_essence");
        var catalogProvisionableModEssence = quantityCatalog.Items.Single(item =>
            item.DisplayId == "provisionable_mod_essence");
        var catalogTownOnlyHeirloom = quantityCatalog.Items.Single(item =>
            item.DisplayId == "town_only_heirloom");
        var catalogEventCostOnlyHeirloom = quantityCatalog.Items.Single(item =>
            item.DisplayId == "event_cost_only_heirloom");
        var catalogOrphanModEssence = quantityCatalog.Items.Single(item => item.DisplayId == "orphan_mod_essence");
        var catalogStoredOrphanEssence = quantityCatalog.Items.Single(item => item.DisplayId == "stored_orphan_essence");
        var catalogHeroStarterEssence = quantityCatalog.Items.Single(item =>
            item.DisplayId == "hero_starter_essence");
        var catalogSaveOnly = quantityCatalog.Items.Single(item => item.DisplayId == "save_only_relic");
        Assert(
            catalogGold.StorageKind == QuantityItemStorageKind.Wallet &&
            catalogGold.CurrentAmount == 1250 &&
            catalogGold.Source == "workshop:111" &&
            catalogGold.SourceLabel.Contains("原版（当前由 创意工坊 Mod", StringComparison.Ordinal) &&
            catalogGold.LocalizedName == new BilingualContentName("金币", "Gold") &&
            catalogBlueprint is { StorageKind: QuantityItemStorageKind.Wallet, CurrentAmount: 2 } &&
            catalogBlueprint.LocalizedName == new BilingualContentName("建筑图纸", "Blueprint") &&
            catalogBlood is
            {
                StorageKind: QuantityItemStorageKind.EstateItems,
                EstateCanBeProvision: true,
                CurrentAmount: 3
            } &&
            catalogBlood.LocalizedName == new BilingualContentName("血酿", "The Blood"),
            "Wallet and estate items must merge live amounts, bilingual names, and original/current-provider provenance.");
        Assert(
            catalogModEssence is
            {
                StorageKind: QuantityItemStorageKind.EstateItems,
                EstateCanBeProvision: false,
                CurrentAmount: 0
            } &&
            catalogModEssence.Source == "local:Local Test Mod" &&
            catalogModEssence.LocalizedName == new BilingualContentName("本地精华", "Local Essence") &&
            catalogModEssence.ReferenceStatus == QuantityItemReferenceStatus.ConfirmedActive &&
            catalogModEssence.ReferenceEvidence.Any(evidence =>
                evidence.Contains("LOCAL_ACTIVE_LOOT", StringComparison.Ordinal) &&
                evidence.Contains("LOCAL_NESTED_LOOT", StringComparison.Ordinal)) &&
            !catalogModEssence.IsHiddenByDefault &&
            catalogProvisionableModEssence is
            {
                ReferenceStatus: QuantityItemReferenceStatus.ConfirmedActive,
                IsHiddenByDefault: false
            } &&
            catalogProvisionableModEssence.ReferenceEvidence.Any(evidence =>
                evidence.Contains("允许从庄园库存手动配给", StringComparison.Ordinal)) &&
            catalogTownOnlyHeirloom is
            {
                StorageKind: QuantityItemStorageKind.Wallet,
                ReferenceStatus: QuantityItemReferenceStatus.ConfirmedActive,
                IsHiddenByDefault: false
            } &&
            catalogEventCostOnlyHeirloom is
            {
                StorageKind: QuantityItemStorageKind.Wallet,
                ReferenceStatus: QuantityItemReferenceStatus.ConfirmedActive,
                IsHiddenByDefault: false
            } &&
            catalogEventCostOnlyHeirloom.ReferenceEvidence.Any(evidence =>
                evidence.Contains("quantity_reference.events.json", StringComparison.OrdinalIgnoreCase)) &&
            catalogOrphanModEssence.ReferenceStatus == QuantityItemReferenceStatus.SuspectedUnused &&
            catalogOrphanModEssence.IsHiddenByDefault &&
            catalogStoredOrphanEssence.ReferenceStatus == QuantityItemReferenceStatus.SuspectedUnused &&
            catalogStoredOrphanEssence is { IsPresentInSave: true, CurrentAmount: 0, IsHiddenByDefault: false } &&
            catalogHeroStarterEssence is
            {
                EstateCanBeProvision: false,
                ReferenceStatus: QuantityItemReferenceStatus.SuspectedUnused,
                IsHiddenByDefault: true
            } &&
            catalogSaveOnly.IsSaveOnly &&
            catalogSaveOnly.ReferenceStatus == QuantityItemReferenceStatus.SaveOnly &&
            catalogSaveOnly.IsPresentInSave &&
            catalogSaveOnly.CurrentAmount == 7 &&
            catalogSaveOnly.LocalizedName == new BilingualContentName("存档遗物", "Save Relic"),
            "Town-reachable Mod items, hidden orphan definitions, definition-backed save residues, and save-only entries must remain distinguishable and editable.");

        var malformedRaidJsonTownOverridePath = Path.Combine(
            localRaidCampingRoot,
            "malformed_town_override.json");
        QuantityItemCatalogResult malformedRaidJsonTownOverrideCatalog;
        File.WriteAllText(malformedRaidJsonTownOverridePath, "{ invalid", new UTF8Encoding(false));
        try
        {
            malformedRaidJsonTownOverrideCatalog = QuantityItemCatalog.Load(
                activeContent,
                quantityEstateRoot,
                "contract-estate-sha");
        }
        finally
        {
            File.Delete(malformedRaidJsonTownOverridePath);
        }

        Assert(
            malformedRaidJsonTownOverrideCatalog.Items.Single(item =>
                item.DisplayId == "orphan_mod_essence") is
            {
                ReferenceStatus: QuantityItemReferenceStatus.AnalysisIncomplete,
                IsHiddenByDefault: false
            } &&
            malformedRaidJsonTownOverrideCatalog.Issues.Any(issue =>
                issue.Contains("could not parse active JSON file", StringComparison.OrdinalIgnoreCase) &&
                issue.Contains("malformed_town_override.json", StringComparison.OrdinalIgnoreCase)),
            "A malformed JSON under a raid-default path must fail open for the town catalog because its nodes could target estate or wallet storage.");

        var malformedReferencePath = Path.Combine(localTownEventsRoot, "malformed_reference.json");
        QuantityItemCatalogResult malformedReferenceCatalog;
        File.WriteAllText(malformedReferencePath, "{ invalid", new UTF8Encoding(false));
        try
        {
            malformedReferenceCatalog = QuantityItemCatalog.Load(
                activeContent,
                quantityEstateRoot,
                "contract-estate-sha");
        }
        finally
        {
            File.Delete(malformedReferencePath);
        }

        Assert(
            malformedReferenceCatalog.Items.Single(item => item.DisplayId == "orphan_mod_essence") is
            {
                ReferenceStatus: QuantityItemReferenceStatus.AnalysisIncomplete,
                IsHiddenByDefault: false
            } &&
            malformedReferenceCatalog.Issues.Any(issue =>
                issue.Contains("could not parse active JSON file", StringComparison.OrdinalIgnoreCase) &&
                issue.Contains("malformed_reference.json", StringComparison.OrdinalIgnoreCase)),
            "An unreadable active reference file must fail open: unresolved Mod items stay visible as analysis-incomplete.");

        var malformedLootPath = Path.Combine(localLootRoot, "malformed_reference.loot.json");
        QuantityItemCatalogResult malformedLootCatalog;
        File.WriteAllText(malformedLootPath, "{ \"loot_tables\": [", new UTF8Encoding(false));
        try
        {
            malformedLootCatalog = QuantityItemCatalog.Load(
                activeContent,
                quantityEstateRoot,
                "contract-estate-sha");
        }
        finally
        {
            File.Delete(malformedLootPath);
        }

        Assert(
            malformedLootCatalog.Items.Single(item => item.DisplayId == "orphan_mod_essence") is
            {
                ReferenceStatus: QuantityItemReferenceStatus.AnalysisIncomplete,
                IsHiddenByDefault: false
            } &&
            malformedLootCatalog.Issues.Any(issue =>
                issue.Contains("could not parse active loot file", StringComparison.OrdinalIgnoreCase) &&
                issue.Contains("malformed_reference.loot.json", StringComparison.OrdinalIgnoreCase)),
            "An unreadable active loot graph must fail open instead of hiding items behind a lost nested-table edge.");

        var quantityReferenceManifestPath = Path.Combine(activeWorkshopRoot, "modfiles.txt");
        var quantityReferenceManifestBytes = File.ReadAllBytes(quantityReferenceManifestPath);
        QuantityItemCatalogResult missingReferenceCatalog;
        File.AppendAllText(
            quantityReferenceManifestPath,
            Environment.NewLine + "campaign/town_events/missing_quantity_reference.json 100",
            new UTF8Encoding(false));
        try
        {
            missingReferenceCatalog = QuantityItemCatalog.Load(
                activeContent,
                quantityEstateRoot,
                "contract-estate-sha");
        }
        finally
        {
            File.WriteAllBytes(quantityReferenceManifestPath, quantityReferenceManifestBytes);
        }

        Assert(
            missingReferenceCatalog.Items.Single(item => item.DisplayId == "orphan_mod_essence") is
            {
                ReferenceStatus: QuantityItemReferenceStatus.AnalysisIncomplete,
                IsHiddenByDefault: false
            } &&
            missingReferenceCatalog.Issues.Any(issue =>
                issue.Contains("reference file listed by active Mod is missing", StringComparison.OrdinalIgnoreCase) &&
                issue.Contains("missing_quantity_reference.json", StringComparison.OrdinalIgnoreCase)),
            "A missing active manifest reference file must keep unresolved Mod items visible instead of claiming they are unused.");

        var nonTownReferenceManifestBytes = File.ReadAllBytes(quantityReferenceManifestPath);
        QuantityItemCatalogResult missingRaidJsonTownOverrideCatalog;
        File.AppendAllText(
            quantityReferenceManifestPath,
            Environment.NewLine + "raid/camping/missing_town_override.json 100",
            new UTF8Encoding(false));
        try
        {
            missingRaidJsonTownOverrideCatalog = QuantityItemCatalog.Load(
                activeContent,
                quantityEstateRoot,
                "contract-estate-sha");
        }
        finally
        {
            File.WriteAllBytes(quantityReferenceManifestPath, nonTownReferenceManifestBytes);
        }

        Assert(
            missingRaidJsonTownOverrideCatalog.Items.Single(item =>
                item.DisplayId == "orphan_mod_essence") is
            {
                ReferenceStatus: QuantityItemReferenceStatus.AnalysisIncomplete,
                IsHiddenByDefault: false
            } &&
            missingRaidJsonTownOverrideCatalog.Issues.Any(issue =>
                issue.Contains("reference file listed by active Mod is missing", StringComparison.OrdinalIgnoreCase) &&
                issue.Contains("missing_town_override.json", StringComparison.OrdinalIgnoreCase)),
            "A missing manifest-listed JSON under a raid-default path must fail open for the town catalog because it could have contained a town inventory override.");

        var (goldAdjustedRoot, goldPreview) = QuantityItemSaveEditor.SetAmount(quantityEstateRoot, catalogGold, 5000);
        Assert(
            goldPreview is { ExistingAmount: 1250, TargetAmount: 5000, CreatedEntry: false } &&
            QuantityItemSaveEditor.CountAmount(goldAdjustedRoot, catalogGold) == 5000 &&
            QuantityItemSaveEditor.CountAmount(quantityEstateRoot, catalogGold) == 1250,
            "Wallet amount edits must be absolute, clone the input, and preserve the source document.");
        var (essenceAddedRoot, essencePreview) = QuantityItemSaveEditor.SetAmount(
            quantityEstateRoot,
            catalogModEssence,
            11);
        var createdEssence = ((JsonObject)((JsonObject)((JsonObject)essenceAddedRoot["base_root"]!)["estate_items"]!)["items"]!)
            .Select(pair => pair.Value as JsonObject)
            .Single(item => item is not null && item["id"]?.GetValue<string>() == "local_mod_essence")!;
        Assert(
            essencePreview is { ExistingAmount: 0, TargetAmount: 11, CreatedEntry: true } &&
            QuantityItemSaveEditor.CountAmount(essenceAddedRoot, catalogModEssence) == 11 &&
            createdEssence["added_buffs"]?.GetValue<int>() == 0 &&
            createdEssence["hero_name"]?.GetValue<string>() == string.Empty &&
            createdEssence["did_transform"]?.GetValue<bool>() == false &&
            createdEssence["trinkets_gained_count"]?.GetValue<int>() == 0,
            "Adding an absent Mod estate item must use the complete game-compatible default saved-item shape.");
        var (bloodZeroRoot, bloodPreview) = QuantityItemSaveEditor.SetAmount(quantityEstateRoot, catalogBlood, 0);
        Assert(
            bloodPreview is { ExistingAmount: 3, TargetAmount: 0, CreatedEntry: false } &&
            QuantityItemSaveEditor.CountAmount(bloodZeroRoot, catalogBlood) == 0,
            "Existing estate items must support an absolute target quantity of zero without deleting unrelated fields.");

        var duplicateQuantityRoot = quantityEstateRoot.DeepClone() as JsonObject
            ?? throw new InvalidDataException("Failed to clone the duplicate quantity-item fixture.");
        var duplicateBaseRoot = (JsonObject)duplicateQuantityRoot["base_root"]!;
        var duplicateWallet = (JsonObject)duplicateBaseRoot["wallet"]!;
        duplicateWallet["99"] = new JsonObject
        {
            ["amount"] = 250,
            ["type"] = "gold",
            ["contract_sentinel"] = "wallet-preserved"
        };
        var duplicateEstateItems = (JsonObject)((JsonObject)duplicateBaseRoot["estate_items"]!)["items"]!;
        duplicateEstateItems["99"] = new JsonObject
        {
            ["id"] = "the_blood",
            ["type"] = "estate",
            ["amount"] = 4,
            ["added_buffs"] = 17,
            ["hero_name"] = "estate-preserved",
            ["previous_trinket_id"] = "contract_previous",
            ["did_transform"] = true,
            ["trinkets_gained_count"] = 23
        };
        var (duplicateLoweredRoot, duplicateLoweredPreview) = QuantityItemSaveEditor.SetAmount(
            duplicateQuantityRoot,
            catalogGold,
            1000);
        var loweredDuplicateWallet = (JsonObject)((JsonObject)duplicateLoweredRoot["base_root"]!)["wallet"]!;
        Assert(
            duplicateLoweredPreview is { ExistingAmount: 1500, TargetAmount: 1000, MatchingEntries: 2 } &&
            QuantityItemSaveEditor.CountAmount(duplicateLoweredRoot, catalogGold) == 1000 &&
            ((JsonObject)loweredDuplicateWallet["99"]!)["contract_sentinel"]?.GetValue<string>() ==
            "wallet-preserved" &&
            ((JsonObject)duplicateWallet["99"]!)["amount"]?.GetValue<int>() == 250,
            "Lowering a duplicated wallet quantity must distribute the absolute target without mutating the source or unrelated fields.");
        var (duplicateRaisedRoot, duplicateRaisedPreview) = QuantityItemSaveEditor.SetAmount(
            duplicateQuantityRoot,
            catalogBlood,
            12);
        var raisedDuplicateEstateItems = (JsonObject)((JsonObject)((JsonObject)duplicateRaisedRoot["base_root"]!)["estate_items"]!)["items"]!;
        var raisedDuplicateBlood = (JsonObject)raisedDuplicateEstateItems["99"]!;
        Assert(
            duplicateRaisedPreview is { ExistingAmount: 7, TargetAmount: 12, MatchingEntries: 2 } &&
            QuantityItemSaveEditor.CountAmount(duplicateRaisedRoot, catalogBlood) == 12 &&
            raisedDuplicateBlood["amount"]?.GetValue<int>() == 4 &&
            raisedDuplicateBlood["added_buffs"]?.GetValue<int>() == 17 &&
            raisedDuplicateBlood["hero_name"]?.GetValue<string>() == "estate-preserved" &&
            raisedDuplicateBlood["previous_trinket_id"]?.GetValue<string>() == "contract_previous" &&
            raisedDuplicateBlood["did_transform"]?.GetValue<bool>() == true &&
            raisedDuplicateBlood["trinkets_gained_count"]?.GetValue<int>() == 23,
            "Raising duplicated estate-item quantities must preserve every non-amount field on all existing entries.");
        var (maxQuantityRoot, maxQuantityPreview) = QuantityItemSaveEditor.SetAmount(
            quantityEstateRoot,
            catalogGold,
            int.MaxValue);
        Assert(
            maxQuantityPreview.TargetAmount == int.MaxValue &&
            QuantityItemSaveEditor.CountAmount(maxQuantityRoot, catalogGold) == int.MaxValue,
            "An existing positive quantity must support Int32.MaxValue without intermediate arithmetic overflow.");

        var quantityRaidRoot = JsonNode.Parse(
            """
        {
          "base_root": {
            "party": {
              "inventory": {
                "items": {
                  "0": { "id": "torch", "type": "supply", "amount": 6 },
                  "2": { "id": "", "type": "gold", "amount": 2500 },
                  "3": { "id": "raid_only_gem", "type": "gem", "amount": 5 }
                }
              }
            }
          }
        }
        """) as JsonObject
            ?? throw new InvalidDataException("Quantity-item raid seed is invalid.");
        var conflictingRaidItemModRootA = Path.Combine(runRoot, "conflicting_raid_item_mod_a");
        var conflictingRaidItemModRootB = Path.Combine(runRoot, "conflicting_raid_item_mod_b");
        var conflictingRaidItemLowerModRoot = Path.Combine(runRoot, "conflicting_raid_item_lower_mod");
        var conflictingRaidItemInventoryRootA = Path.Combine(conflictingRaidItemModRootA, "inventory");
        var conflictingRaidItemInventoryRootB = Path.Combine(conflictingRaidItemModRootB, "inventory");
        var conflictingRaidItemLowerInventoryRoot = Path.Combine(conflictingRaidItemLowerModRoot, "inventory");
        Directory.CreateDirectory(conflictingRaidItemInventoryRootA);
        Directory.CreateDirectory(conflictingRaidItemInventoryRootB);
        Directory.CreateDirectory(conflictingRaidItemLowerInventoryRoot);
        File.WriteAllText(
            Path.Combine(conflictingRaidItemInventoryRootA, "shared.inventory.items.darkest"),
            """inventory_item: .type "supply" .id "conflicting_raid_item" .base_stack_limit 4""",
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(conflictingRaidItemInventoryRootB, "shared.inventory.items.darkest"),
            """inventory_item: .type "supply" .id "conflicting_raid_item" .base_stack_limit 8""",
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(conflictingRaidItemLowerInventoryRoot, "lower.inventory.items.darkest"),
            """inventory_item: .type "supply" .id "conflicting_raid_item" .base_stack_limit 99""",
            new UTF8Encoding(false));
        var conflictingRaidItemContent = activeContent with
        {
            Sources = activeContent.Sources
                .Concat([
                    new ActiveContentSource(
                    "local:raid-item-conflict-a",
                    "Raid Item Conflict A",
                    "local",
                    conflictingRaidItemModRootA,
                    -3000),
                new ActiveContentSource(
                    "local:raid-item-conflict-b",
                    "Raid Item Conflict B",
                    "local",
                    conflictingRaidItemModRootB,
                    -3000),
                new ActiveContentSource(
                    "local:raid-item-conflict-lower",
                    "Raid Item Conflict Lower",
                    "local",
                    conflictingRaidItemLowerModRoot,
                    -2000)
                ])
                .ToArray()
        };
        var conflictingRaidItemCatalog = QuantityItemCatalog.LoadRaid(
            conflictingRaidItemContent,
            quantityRaidRoot,
            "contract-raid-conflict-sha");
        var conflictingRaidItem = conflictingRaidItemCatalog.Items.Single(item =>
            item.InventoryType == "supply" && item.ItemId == "conflicting_raid_item");
        var conflictingRaidItemBlocked = false;
        try
        {
            var conflictProbeService = new SaveEditService(
                codec,
                new SaveEditorLocations(
                    Path.Combine(runRoot, "raid-item-conflict-appdata"),
                    Path.Combine(runRoot, "raid-item-conflict-appdata", "workspaces"),
                    Path.Combine(runRoot, "raid-item-conflict-appdata", "backups")));
            _ = await conflictProbeService.PrepareQuantityItemEditAsync(
                activeContent.Profile,
                conflictingRaidItem,
                1,
                conflictingRaidItemContent);
        }
        catch (InvalidOperationException error) when (error.Message.Contains(
            "unresolved definitions",
            StringComparison.OrdinalIgnoreCase))
        {
            conflictingRaidItemBlocked = true;
        }

        Assert(
            conflictingRaidItem.HasProviderConflict &&
            !conflictingRaidItem.IsSaveOnly &&
            conflictingRaidItem.BaseStackLimit != 99 &&
            conflictingRaidItemBlocked,
            "A raid item from different same-virtual-path, same-priority providers must remain visible but read-only; it must not disappear into a writable save-only fallback or use a lower/arbitrary stack limit.");
        var raidStorageCatalog = RaidInventoryStorageCatalog.Load(activeContent);
        var raidQuantityCatalog = QuantityItemCatalog.LoadRaid(
            activeContent,
            quantityRaidRoot,
            "contract-raid-sha");
        var raidWithCarriedTrinketRoot = quantityRaidRoot.DeepClone() as JsonObject
            ?? throw new InvalidDataException("Failed to clone the raid quantity fixture.");
        var raidWithCarriedTrinketItems = (JsonObject)((JsonObject)((JsonObject)((JsonObject)
            raidWithCarriedTrinketRoot["base_root"]!)["party"]!)["inventory"]!)["items"]!;
        raidWithCarriedTrinketItems["1"] = new JsonObject
        {
            ["id"] = "Livia_6",
            ["type"] = "trinket",
            ["amount"] = 1
        };
        var raidWithCarriedTrinketCatalog = QuantityItemCatalog.LoadRaid(
            activeContent,
            raidWithCarriedTrinketRoot,
            "contract-raid-trinket-sha");
        var raidWithoutTorchRoot = quantityRaidRoot.DeepClone() as JsonObject
            ?? throw new InvalidDataException("Failed to clone the no-torch raid fixture.");
        var raidWithoutTorchItems = (JsonObject)((JsonObject)((JsonObject)((JsonObject)
            raidWithoutTorchRoot["base_root"]!)["party"]!)["inventory"]!)["items"]!;
        _ = raidWithoutTorchItems.Remove("0");
        var raidWithoutTorchCatalog = QuantityItemCatalog.LoadRaid(
            activeContent,
            raidWithoutTorchRoot,
            "contract-raid-no-torch-sha");
        var raidTorch = raidQuantityCatalog.Items.Single(item =>
            item.InventoryType == "supply" && item.ItemId == "torch");
        var raidBandage = raidQuantityCatalog.Items.Single(item =>
            item.InventoryType == "supply" && item.ItemId == "bandage");
        var raidGold = raidQuantityCatalog.Items.Single(item =>
            item.InventoryType == "gold" && item.ItemId == string.Empty);
        var raidGem = raidQuantityCatalog.Items.Single(item => item.DisplayId == "raid_only_gem");
        var raidBlood = raidQuantityCatalog.Items.Single(item =>
            item.InventoryType == "estate" && item.ItemId == "the_blood");
        var raidModEssence = raidQuantityCatalog.Items.Single(item =>
            item.InventoryType == "estate" && item.ItemId == "local_mod_essence");
        var raidProvisionableModEssence = raidQuantityCatalog.Items.Single(item =>
            item.InventoryType == "estate" && item.ItemId == "provisionable_mod_essence");
        var raidTownOnlyHeirloom = raidQuantityCatalog.Items.Single(item =>
            item.InventoryType == "heirloom" && item.ItemId == "town_only_heirloom");
        var raidLocalGem = raidQuantityCatalog.Items.Single(item =>
            item.InventoryType == "gem" && item.ItemId == "local_raid_gem");
        var raidHeroStarterEssence = raidQuantityCatalog.Items.Single(item =>
            item.InventoryType == "estate" && item.ItemId == "hero_starter_essence");
        var absentRaidTorch = raidWithoutTorchCatalog.Items.Single(item =>
            item.InventoryType == "supply" && item.ItemId == "torch");
        Assert(
            raidQuantityCatalog is
            {
                SaveContext: QuantityItemSaveContext.Raid,
                SourceSaveSha256: "contract-raid-sha",
                RaidOccupiedSlots: 3
            } &&
            raidWithCarriedTrinketCatalog.RaidOccupiedSlots == 4 &&
            raidWithoutTorchCatalog.RaidOccupiedSlots == 2 &&
            raidQuantityCatalog.RaidStorage?.MaxSlots == 4 &&
            raidStorageCatalog.Storage?.MaxSlots == 4 &&
            raidQuantityCatalog.Items.All(item => item.StorageKind == QuantityItemStorageKind.RaidInventory) &&
            raidWithCarriedTrinketCatalog.Items.All(item =>
                !item.InventoryType.Equals("trinket", StringComparison.OrdinalIgnoreCase)) &&
            raidTorch is
            {
                StorageKind: QuantityItemStorageKind.RaidInventory,
                BaseStackLimit: 8,
                CurrentAmount: 6,
                SavedEntryCount: 1
            } &&
            raidTorch.LocalizedName == new BilingualContentName("火把", "Torch") &&
            absentRaidTorch is
            {
                CurrentAmount: 0,
                IsPresentInSave: false,
                ReferenceStatus: QuantityItemReferenceStatus.OfficialContent,
                IsHiddenByDefault: false
            } &&
            raidGold is { CurrentAmount: 2500, BaseStackLimit: 2500 } &&
            catalogGold.CurrentAmount == 1250 &&
            raidGem is { CurrentAmount: 5, BaseStackLimit: 5 } &&
            catalogBlood is { StorageKind: QuantityItemStorageKind.EstateItems, CurrentAmount: 3 } &&
            raidBlood is { StorageKind: QuantityItemStorageKind.RaidInventory, CurrentAmount: 0 } &&
            raidModEssence is
            {
                StorageKind: QuantityItemStorageKind.RaidInventory,
                ReferenceStatus: QuantityItemReferenceStatus.SuspectedUnused,
                IsHiddenByDefault: true
            } &&
            raidProvisionableModEssence is
            {
                StorageKind: QuantityItemStorageKind.RaidInventory,
                ReferenceStatus: QuantityItemReferenceStatus.ConfirmedActive,
                IsHiddenByDefault: false
            } &&
            raidProvisionableModEssence.ReferenceEvidence.Any(evidence =>
                evidence.Contains("允许从庄园配给", StringComparison.Ordinal)) &&
            raidTownOnlyHeirloom is
            {
                ReferenceStatus: QuantityItemReferenceStatus.SuspectedUnused,
                IsHiddenByDefault: true
            } &&
            raidLocalGem is
            {
                ReferenceStatus: QuantityItemReferenceStatus.ConfirmedActive,
                IsHiddenByDefault: false
            } &&
            raidLocalGem.ReferenceEvidence.Any(evidence =>
                evidence.Contains("quantity_reference_probe", StringComparison.OrdinalIgnoreCase) &&
                evidence.Contains("LOCAL_RAID_LOOT", StringComparison.OrdinalIgnoreCase)),
            "An active expedition must resolve rooted raid loot independently from town-only references.");
        Assert(
            raidHeroStarterEssence is
            {
                ReferenceStatus: QuantityItemReferenceStatus.ConfirmedActive,
                IsHiddenByDefault: false
            } &&
            raidHeroStarterEssence.ReferenceEvidence.Any(evidence =>
                evidence.Contains("hero_starter.provision.json", StringComparison.OrdinalIgnoreCase)),
            "An estate item with estate_can_be_provision=false must remain raid-visible when a hero-starting-item list injects it independently of town stock.");
        Assert(
            raidQuantityCatalog.Items.All(item => item.StorageKind == QuantityItemStorageKind.RaidInventory),
            "An active expedition must use raid reachability rather than town-only evidence: explicit provisioning and rooted raid loot remain visible, while town-only currency and estate rewards hide by default. Carried trinkets remain outside the quantity workflow.");

        var malformedTownOnlyReferencePath = Path.Combine(
            localTownEventsRoot,
            "malformed_town_only_reference.json");
        QuantityItemCatalogResult raidWithMalformedTownOnlyReference;
        File.WriteAllText(malformedTownOnlyReferencePath, "{ invalid", new UTF8Encoding(false));
        try
        {
            raidWithMalformedTownOnlyReference = QuantityItemCatalog.LoadRaid(
                activeContent,
                quantityRaidRoot,
                "contract-raid-malformed-town-sha");
        }
        finally
        {
            File.Delete(malformedTownOnlyReferencePath);
        }

        Assert(
            raidWithMalformedTownOnlyReference.Items.Single(item =>
                item.InventoryType == "heirloom" && item.ItemId == "town_only_heirloom") is
            {
                ReferenceStatus: QuantityItemReferenceStatus.SuspectedUnused,
                IsHiddenByDefault: true
            } &&
            raidWithMalformedTownOnlyReference.Issues.All(issue =>
                !issue.Contains("malformed_town_only_reference.json", StringComparison.OrdinalIgnoreCase)),
            "A malformed town-only reference file must not make unrelated raid reachability incomplete or keep town-only definitions visible in the raid catalog.");

        var mixedDistrictReferencePath = Path.Combine(
            activeWorkshopDistrictRoot,
            "quantity_reference.districts.json");
        var mixedDistrictReferenceBytes = File.ReadAllBytes(mixedDistrictReferencePath);
        QuantityItemCatalogResult raidWithMalformedMixedDistrictReference;
        File.WriteAllText(mixedDistrictReferencePath, "{ invalid", new UTF8Encoding(false));
        try
        {
            raidWithMalformedMixedDistrictReference = QuantityItemCatalog.LoadRaid(
                activeContent,
                quantityRaidRoot,
                "contract-raid-malformed-district-sha");
        }
        finally
        {
            File.WriteAllBytes(mixedDistrictReferencePath, mixedDistrictReferenceBytes);
        }

        Assert(
            raidWithMalformedMixedDistrictReference.Items.Single(item =>
                item.InventoryType == "heirloom" && item.ItemId == "town_only_heirloom") is
            {
                ReferenceStatus: QuantityItemReferenceStatus.AnalysisIncomplete,
                IsHiddenByDefault: false
            } &&
            raidWithMalformedMixedDistrictReference.Issues.Any(issue =>
                issue.Contains("could not parse active JSON file", StringComparison.OrdinalIgnoreCase) &&
                issue.Contains("quantity_reference.districts.json", StringComparison.OrdinalIgnoreCase)),
            "An unreadable district JSON must fail open in raid mode because a town district can contain provision-target or inventory-replacement nodes.");

        var raidReferenceManifestBytes = File.ReadAllBytes(quantityReferenceManifestPath);
        QuantityItemCatalogResult raidWithMissingMixedDistrictReference;
        File.AppendAllText(
            quantityReferenceManifestPath,
            Environment.NewLine + "campaign/town/districts/missing_raid_supply_reference.json 100",
            new UTF8Encoding(false));
        try
        {
            raidWithMissingMixedDistrictReference = QuantityItemCatalog.LoadRaid(
                activeContent,
                quantityRaidRoot,
                "contract-raid-missing-district-sha");
        }
        finally
        {
            File.WriteAllBytes(quantityReferenceManifestPath, raidReferenceManifestBytes);
        }

        Assert(
            raidWithMissingMixedDistrictReference.Items.Single(item =>
                item.InventoryType == "heirloom" && item.ItemId == "town_only_heirloom") is
            {
                ReferenceStatus: QuantityItemReferenceStatus.AnalysisIncomplete,
                IsHiddenByDefault: false
            } &&
            raidWithMissingMixedDistrictReference.Issues.Any(issue =>
                issue.Contains("reference file listed by active Mod is missing", StringComparison.OrdinalIgnoreCase) &&
                issue.Contains("missing_raid_supply_reference.json", StringComparison.OrdinalIgnoreCase)),
            "A missing manifest-listed district JSON must fail open in raid mode because its content could have supplied the expedition inventory.");

        var raidWithZeroSaveOnlyStackRoot = quantityRaidRoot.DeepClone() as JsonObject
            ?? throw new InvalidDataException("Failed to clone the zero-stack raid fixture.");
        var raidWithZeroSaveOnlyStackItems = (JsonObject)((JsonObject)((JsonObject)((JsonObject)
            raidWithZeroSaveOnlyStackRoot["base_root"]!)["party"]!)["inventory"]!)["items"]!;
        raidWithZeroSaveOnlyStackItems["1"] = new JsonObject
        {
            ["id"] = "orphan_zero_stack",
            ["type"] = "supply",
            ["amount"] = 0
        };
        var zeroSaveOnlyCatalog = QuantityItemCatalog.LoadRaid(
            activeContent,
            raidWithZeroSaveOnlyStackRoot,
            "contract-raid-zero-sha");
        var zeroSaveOnlyItem = zeroSaveOnlyCatalog.Items.Single(item =>
            item.InventoryType == "supply" && item.ItemId == "orphan_zero_stack");
        var (zeroStackRemovedRoot, zeroStackRemovalPreview) = RaidInventorySaveEditor.SetAmount(
            raidWithZeroSaveOnlyStackRoot,
            zeroSaveOnlyItem,
            0,
            raidStorageCatalog.Storage!.MaxSlots);
        var zeroStackRemovedItems = (JsonObject)((JsonObject)((JsonObject)((JsonObject)
            zeroStackRemovedRoot["base_root"]!)["party"]!)["inventory"]!)["items"]!;
        Assert(
            zeroSaveOnlyItem is
            {
                IsSaveOnly: true,
                IsPresentInSave: true,
                CurrentAmount: 0,
                SavedEntryCount: 1
            } &&
            zeroStackRemovalPreview is
            {
                ExistingAmount: 0,
                TargetAmount: 0,
                ExistingInventoryEntries: 4,
                ResultingInventoryEntries: 3,
                RemovedEntries: 1,
                ResultingMatchingEntries: 0
            } &&
            !zeroStackRemovedItems.ContainsKey("1"),
            "A target of zero must remove a physically present zero-amount save-only stack and release its raid slot.");

        var raidWithTownOnlyResidueRoot = quantityRaidRoot.DeepClone() as JsonObject
            ?? throw new InvalidDataException("Failed to clone the town-only residue raid fixture.");
        var raidWithTownOnlyResidueItems = (JsonObject)((JsonObject)((JsonObject)((JsonObject)
            raidWithTownOnlyResidueRoot["base_root"]!)["party"]!)["inventory"]!)["items"]!;
        raidWithTownOnlyResidueItems["1"] = new JsonObject
        {
            ["id"] = "town_only_heirloom",
            ["type"] = "heirloom",
            ["amount"] = 0
        };
        var raidWithTownOnlyResidueCatalog = QuantityItemCatalog.LoadRaid(
            activeContent,
            raidWithTownOnlyResidueRoot,
            "contract-raid-town-residue-sha");
        Assert(
            raidWithTownOnlyResidueCatalog.Items.Single(item =>
                item.InventoryType == "heirloom" && item.ItemId == "town_only_heirloom") is
            {
                ReferenceStatus: QuantityItemReferenceStatus.SuspectedUnused,
                IsPresentInSave: true,
                CurrentAmount: 0,
                IsHiddenByDefault: false
            },
            "A town-only definition that is already present in the raid save must remain visible even at amount zero so the residue can be inspected or removed.");

        var (torchAdjustedRoot, torchPreview) = RaidInventorySaveEditor.SetAmount(
            quantityRaidRoot,
            raidTorch,
            10,
            raidStorageCatalog.Storage!.MaxSlots);
        var adjustedRaidItems = (JsonObject)((JsonObject)((JsonObject)((JsonObject)
            torchAdjustedRoot["base_root"]!)["party"]!)["inventory"]!)["items"]!;
        Assert(
            torchPreview is
            {
                ExistingAmount: 6,
                TargetAmount: 10,
                MatchingEntries: 1,
                ResultingMatchingEntries: 2,
                ExistingInventoryEntries: 3,
                ResultingInventoryEntries: 4,
                CreatedEntries: 1,
                InventoryCapacity: 4
            } &&
            ((JsonObject)adjustedRaidItems["0"]!)["amount"]?.GetValue<int>() == 8 &&
            ((JsonObject)adjustedRaidItems["1"]!)["amount"]?.GetValue<int>() == 2 &&
            RaidInventorySaveEditor.CountAmount(torchAdjustedRoot, raidTorch) == 10 &&
            RaidInventorySaveEditor.CountAmount(quantityRaidRoot, raidTorch) == 6,
            "Increasing a raid item must fill its existing stack, occupy the first empty slot, respect stack limits, and leave the source document unchanged.");

        var fullBagRejected = false;
        try
        {
            _ = RaidInventorySaveEditor.SetAmount(
                torchAdjustedRoot,
                raidBandage,
                1,
                raidStorageCatalog.Storage.MaxSlots);
        }
        catch (InvalidOperationException error)
        {
            fullBagRejected = error.Message.Contains("不会被覆盖", StringComparison.Ordinal);
        }

        Assert(fullBagRejected, "A full expedition inventory must reject a new item instead of replacing an occupied slot.");
        var (torchRemovedRoot, torchRemovedPreview) = RaidInventorySaveEditor.SetAmount(
            torchAdjustedRoot,
            raidTorch,
            0,
            raidStorageCatalog.Storage.MaxSlots);
        Assert(
            torchRemovedPreview is
            {
                TargetAmount: 0,
                RemovedEntries: 2,
                ResultingMatchingEntries: 0,
                ResultingInventoryEntries: 2
            } &&
            RaidInventorySaveEditor.CountAmount(torchRemovedRoot, raidTorch) == 0,
            "Setting a raid item to zero must remove only its matching stacks and free their slots.");

        var defaultLocalContent = await ActiveContentResolver.ResolveAsync(
            profile,
            gameRoot,
            workshopRoot,
            codec,
            Path.Combine(runRoot, "default-local-workspaces"));
        Assert(
            defaultLocalContent.Sources.Any(source => source.Id == "local:Local Test Mod") &&
            defaultLocalContent.Sources.All(source => source.Id != "local:External Test Mod") &&
            defaultLocalContent.Issues.Any(issue => issue.Contains("External Test Mod", StringComparison.Ordinal)),
            "The compatible resolver overload should keep scanning default local roots and report an external Mod when no extra root is selected.");

        var ambiguousLocalContent = await ActiveContentResolver.ResolveAsync(
            profile,
            gameRoot,
            workshopRoot,
            ambiguousDirectLocalModRoot,
            codec,
            Path.Combine(runRoot, "ambiguous-local-workspaces"));
        Assert(
            ambiguousLocalContent.Sources.All(source => source.Id != "local:Local Test Mod") &&
            ambiguousLocalContent.Issues.Any(issue =>
                issue.Contains("Local Test Mod", StringComparison.Ordinal) &&
                issue.Contains("ambiguous", StringComparison.OrdinalIgnoreCase)),
            "Selecting a single Mod project directory must detect a duplicate project title across local roots and refuse to guess.");

        var radiantProfileRoot = Path.Combine(runRoot, "profile_radiant");
        Directory.CreateDirectory(radiantProfileRoot);
        var radiantEstatePath = Path.Combine(radiantProfileRoot, "persist.estate.json");
        var radiantGamePath = Path.Combine(radiantProfileRoot, "persist.game.json");
        var radiantDecodedGamePath = Path.Combine(runRoot, "seed.persist.game.radiant.json");
        File.Copy(estatePath, radiantEstatePath, overwrite: false);
        var radiantGameRoot = JsonNode.Parse(File.ReadAllText(decodedGameSeedPath)) as JsonObject
            ?? throw new InvalidDataException("Radiant game seed is invalid.");
        ((JsonObject)radiantGameRoot["base_root"]!)["game_mode"] = "radiant";
        File.WriteAllText(radiantDecodedGamePath, radiantGameRoot.ToJsonString(), new UTF8Encoding(false));
        await codec.EncodeAsync(radiantDecodedGamePath, radiantGamePath, originalBinaryPath: null);
        var radiantProfile = new SaveProfile(
            "profile_radiant",
            radiantProfileRoot,
            radiantEstatePath,
            "contract-user",
            File.GetLastWriteTimeUtc(radiantEstatePath));
        var radiantContent = await ActiveContentResolver.ResolveAsync(
            radiantProfile,
            gameRoot,
            workshopRoot,
            additionalLocalModDirectory,
            codec,
            Path.Combine(runRoot, "radiant-workspaces"));
        Assert(
            radiantContent.GameMode == "radiant" &&
            radiantContent.Sources.Any(source => source is { Id: "mode:radiant", Kind: "mode" }),
            "A non-base profile should activate its game-mode content source.");
        var radiantHeroCatalog = HeroClassCatalog.Load(radiantContent);
        Assert(
            radiantHeroCatalog.ResolveLevelThresholds.SequenceEqual([0, 1, 6, 12, 20, 30, 42]),
            "The active game mode should override base resolve XP thresholds.");
        Assert(
            radiantHeroCatalog.HeroClasses.Single(hero => hero.Id == "local_hero")
                .LevelProfiles.Select(profile => profile.ResolveXp)
                .SequenceEqual([0, 1, 6, 12, 20, 30, 42]),
            "A Mod hero should reuse the selected game mode's resolve progression.");


        return new QuantityCatalogContractState(catalogModEssence, quantityRaidRoot);
    }
}
