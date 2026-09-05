internal static partial class ContractSuite
{
    private static async Task RunQuantitySaveContractsAsync(
        string runRoot,
        string profileRoot,
        SaveProfile profile,
        ActiveContentSnapshot activeContent,
        DsonSaveCodec codec,
        QuantityCatalogContractState state)
    {
        var catalogModEssence = state.CatalogModEssence;
        var quantityRaidRoot = state.QuantityRaidRoot;
        var quantityProfileRoot = Path.Combine(runRoot, "profile_quantity_items");
        Directory.CreateDirectory(quantityProfileRoot);
        foreach (var sourcePath in Directory.EnumerateFiles(profileRoot, "persist*.json", SearchOption.TopDirectoryOnly))
        {
            File.Copy(sourcePath, Path.Combine(quantityProfileRoot, Path.GetFileName(sourcePath)), overwrite: false);
        }

        var quantityEstatePath = Path.Combine(quantityProfileRoot, "persist.estate.json");
        var quantityGamePath = Path.Combine(quantityProfileRoot, "persist.game.json");
        var quantityRaidPath = Path.Combine(quantityProfileRoot, "persist.raid.json");
        var quantityProfile = new SaveProfile(
            "profile_quantity_items",
            quantityProfileRoot,
            quantityEstatePath,
            "contract-user",
            File.GetLastWriteTimeUtc(quantityEstatePath));
        var quantityActiveContent = activeContent with
        {
            Profile = quantityProfile,
            SourceGameSha256 = ComputeSha256(quantityGamePath)
        };
        var quantityLocations = new SaveEditorLocations(
            Path.Combine(runRoot, "quantity-appdata"),
            Path.Combine(runRoot, "quantity-appdata", "workspaces"),
            Path.Combine(runRoot, "quantity-appdata", "backups"));
        var quantityService = new SaveEditService(codec, quantityLocations);
        var preparedQuantity = await quantityService.PrepareQuantityItemEditAsync(
            quantityProfile,
            catalogModEssence,
            11,
            quantityActiveContent);
        Assert(
            preparedQuantity.Preview is
            {
                ExistingAmount: 0,
                TargetAmount: 11,
                CreatedEntry: true,
                StorageKind: QuantityItemStorageKind.EstateItems
            } &&
            ReadRevision(preparedQuantity.SourceCopyPath).SequenceEqual(ReadRevision(preparedQuantity.EncodedPath)),
            "The service preview must roundtrip a zero-held Mod estate item and preserve DSON revision bytes.");

        var quantityDefinitionBytes = File.ReadAllBytes(catalogModEssence.SourcePath);
        File.AppendAllText(catalogModEssence.SourcePath, Environment.NewLine + "// stale quantity definition", new UTF8Encoding(false));
        var staleQuantityDefinitionBlocked = false;
        try
        {
            _ = await quantityService.CommitAsync(preparedQuantity);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains(
            "quantity-item definition changed after preview",
            StringComparison.OrdinalIgnoreCase))
        {
            staleQuantityDefinitionBlocked = true;
        }
        finally
        {
            File.WriteAllBytes(catalogModEssence.SourcePath, quantityDefinitionBytes);
        }

        Assert(
            staleQuantityDefinitionBlocked && !Directory.Exists(quantityLocations.BackupDirectory),
            "Quantity-item commit must reject a changed selected Mod definition before creating a backup.");
        preparedQuantity = await quantityService.PrepareQuantityItemEditAsync(
            quantityProfile,
            catalogModEssence,
            11,
            quantityActiveContent);
        var quantityCommit = await quantityService.CommitAsync(preparedQuantity);
        Assert(
            File.Exists(Path.Combine(quantityCommit.BackupDirectory, "persist.estate.json")) &&
            !File.Exists(Path.Combine(quantityCommit.BackupDirectory, "persist.raid.json")) &&
            File.Exists(Path.Combine(quantityCommit.BackupDirectory, "backup-manifest.json")) &&
            File.Exists(Path.Combine(quantityCommit.BackupDirectory, "commit-result.json")),
            "A town quantity-item commit must preserve the same complete profile backup and result evidence as trinket writes without inventing persist.raid.json.");
        var quantityCommittedDecodedPath = Path.Combine(runRoot, "quantity.committed.persist.estate.json");
        await codec.DecodeAsync(quantityEstatePath, quantityCommittedDecodedPath);
        var quantityCommittedRoot = JsonNode.Parse(File.ReadAllText(quantityCommittedDecodedPath)) as JsonObject
            ?? throw new InvalidDataException("Committed quantity-item save did not decode to an object.");
        Assert(
            QuantityItemSaveEditor.CountAmount(quantityCommittedRoot, catalogModEssence) == 11 &&
            TrinketSaveEditor.CountCopies(quantityCommittedRoot, "focus_ring") == 2,
            "Quantity-item commit must write the requested Mod amount without changing the existing trinket inventory.");

        var quantityDecodedRaidSeedPath = Path.Combine(runRoot, "quantity.seed.persist.raid.json");
        File.WriteAllText(quantityDecodedRaidSeedPath, quantityRaidRoot.ToJsonString(), new UTF8Encoding(false));
        await codec.EncodeAsync(quantityDecodedRaidSeedPath, quantityRaidPath, originalBinaryPath: null);
        SetRevision(quantityRaidPath, [0x00, 0x00, 0x4A, 0x70]);
        var townWithRaidResidue = await QuantityItemCatalog.LoadAsync(quantityActiveContent with
        {
            WorkspaceDirectory = Path.Combine(runRoot, "quantity-town-residue-catalog")
        }, codec);
        Assert(townWithRaidResidue.SaveContext == QuantityItemSaveContext.Town &&
               townWithRaidResidue.Issues.Any(issue => issue.Contains("残留副本", StringComparison.Ordinal)),
            "A town game state with an old raid save must expose town quantities, not the stale raid backpack.");
        var townResidueEdit = await quantityService.PrepareQuantityItemEditAsync(
            quantityProfile, catalogModEssence, 12, quantityActiveContent);
        var residueHash = ComputeSha256(quantityRaidPath);
        var townResidueCommit = await quantityService.CommitAsync(townResidueEdit);
        Assert(Path.GetFileName(townResidueCommit.TargetPath) == "persist.estate.json" &&
               ComputeSha256(quantityRaidPath) == residueHash,
            "Editing town quantities must remain available with raid residue and must preserve the old backpack.");

        var quantityGameRoot = JsonNode.Parse(File.ReadAllText(quantityActiveContent.DecodedGamePath))!.AsObject();
        var quantityDecodedGamePath = Path.Combine(runRoot, "quantity.scene.persist.game.json");
        quantityGameRoot["base_root"]!["inraid"] = true;
        quantityGameRoot["base_root"]!["raiddungeon"] = "cove";
        File.WriteAllText(quantityDecodedGamePath, quantityGameRoot.ToJsonString(), new UTF8Encoding(false));
        await codec.EncodeAsync(quantityDecodedGamePath, quantityGamePath, originalBinaryPath: null);
        quantityActiveContent = quantityActiveContent with
        {
            DecodedGamePath = quantityDecodedGamePath,
            SourceGameSha256 = ComputeSha256(quantityGamePath)
        };
        var quantityEstateBytesBeforeRaidEdit = File.ReadAllBytes(quantityEstatePath);
        var autoRaidCatalog = await QuantityItemCatalog.LoadAsync(
            quantityActiveContent with
            {
                WorkspaceDirectory = Path.Combine(runRoot, "quantity-raid-catalog-workspace")
            },
            codec);
        var serviceRaidTorch = autoRaidCatalog.Items.Single(item =>
            item.InventoryType == "supply" && item.ItemId == "torch");
        Assert(
            autoRaidCatalog.SaveContext == QuantityItemSaveContext.Raid &&
            autoRaidCatalog.RaidStorage?.MaxSlots == 4 &&
            serviceRaidTorch.CurrentAmount == 6,
            "The asynchronous quantity catalog must switch to persist.raid.json whenever the selected profile is in an expedition.");

        var townItemBlockedDuringRaid = false;
        try
        {
            _ = await quantityService.PrepareQuantityItemEditAsync(
                quantityProfile,
                catalogModEssence,
                12,
                quantityActiveContent);
        }
        catch (InvalidOperationException error) when (error.Message.Contains("已经进入副本", StringComparison.Ordinal))
        {
            townItemBlockedDuringRaid = true;
        }

        Assert(
            townItemBlockedDuringRaid,
            "Once the game state enters an expedition, a stale town item row must be rejected and the user must reload into raid mode.");
        var preparedRaidQuantity = await quantityService.PrepareQuantityItemEditAsync(
            quantityProfile,
            serviceRaidTorch,
            10,
            quantityActiveContent);
        Assert(
            preparedRaidQuantity.ContentGuard.SaveContext == QuantityItemSaveContext.Raid &&
            preparedRaidQuantity.Preview is
            {
                ExistingAmount: 6,
                TargetAmount: 10,
                ExistingInventoryEntries: 3,
                ResultingInventoryEntries: 4,
                InventoryCapacity: 4
            } &&
            ReadRevision(preparedRaidQuantity.SourceCopyPath)
                .SequenceEqual(ReadRevision(preparedRaidQuantity.EncodedPath)),
            "A raid quantity preview must roundtrip persist.raid.json, preserve its revision, and expose its slot impact.");

        var raidStoragePath = autoRaidCatalog.RaidStorage!.SourcePath;
        var raidStorageBytes = File.ReadAllBytes(raidStoragePath);
        File.AppendAllText(raidStoragePath, Environment.NewLine + "// stale raid capacity", new UTF8Encoding(false));
        var staleRaidCapacityBlocked = false;
        try
        {
            _ = await quantityService.CommitAsync(preparedRaidQuantity);
        }
        catch (InvalidOperationException error) when (error.Message.Contains(
            "inventory capacity changed after preview",
            StringComparison.OrdinalIgnoreCase))
        {
            staleRaidCapacityBlocked = true;
        }
        finally
        {
            File.WriteAllBytes(raidStoragePath, raidStorageBytes);
        }

        Assert(
            staleRaidCapacityBlocked,
            "A raid capacity configuration change after preview must block commit before touching the live inventory.");
        var parkedRaidPath = Path.Combine(quantityProfileRoot, "persist.raid.contract-parked");
        File.Move(quantityRaidPath, parkedRaidPath);
        var endedRaidBlocked = false;
        try
        {
            _ = await quantityService.CommitAsync(preparedRaidQuantity);
        }
        catch (InvalidOperationException error) when (error.Message.Contains("已不在副本", StringComparison.Ordinal))
        {
            endedRaidBlocked = true;
        }
        finally
        {
            File.Move(parkedRaidPath, quantityRaidPath);
        }

        Assert(
            endedRaidBlocked,
            "If the expedition ends after preview, the prepared raid edit must be rejected instead of being redirected to town storage.");
        var raidQuantityCommit = await quantityService.CommitAsync(preparedRaidQuantity);
        Assert(
            Path.GetFileName(raidQuantityCommit.TargetPath) == "persist.raid.json" &&
            File.Exists(Path.Combine(raidQuantityCommit.BackupDirectory, "persist.raid.json")) &&
            File.Exists(Path.Combine(raidQuantityCommit.BackupDirectory, "persist.estate.json")) &&
            File.ReadAllBytes(quantityEstatePath).SequenceEqual(quantityEstateBytesBeforeRaidEdit),
            "A raid quantity commit must replace only persist.raid.json while backing up the complete current profile.");
        var quantityCommittedRaidDecodedPath = Path.Combine(runRoot, "quantity.committed.persist.raid.json");
        await codec.DecodeAsync(quantityRaidPath, quantityCommittedRaidDecodedPath);
        var quantityCommittedRaidRoot = JsonNode.Parse(File.ReadAllText(quantityCommittedRaidDecodedPath)) as JsonObject
            ?? throw new InvalidDataException("Committed raid quantity save did not decode to an object.");
        Assert(
            RaidInventorySaveEditor.CountAmount(quantityCommittedRaidRoot, serviceRaidTorch) == 10 &&
            ReadRevision(quantityRaidPath).SequenceEqual(new byte[] { 0x00, 0x00, 0x4A, 0x70 }),
            "A raid quantity commit must write the requested stacks and preserve the DSON revision.");

        var beforeSceneChange = await quantityService.PrepareQuantityItemEditAsync(
            quantityProfile, serviceRaidTorch, 11, quantityActiveContent);
        quantityGameRoot["base_root"]!["inraid"] = false;
        quantityGameRoot["base_root"]!["raiddungeon"] = "none";
        File.WriteAllText(quantityDecodedGamePath, quantityGameRoot.ToJsonString(), new UTF8Encoding(false));
        await codec.EncodeAsync(quantityDecodedGamePath, quantityGamePath, originalBinaryPath: null);
        var oldRaidHash = ComputeSha256(quantityRaidPath);
        var changedSceneBlocked = false;
        try
        {
            _ = await quantityService.CommitAsync(beforeSceneChange);
        }
        catch (InvalidOperationException)
        {
            changedSceneBlocked = true;
        }
        Assert(changedSceneBlocked && ComputeSha256(quantityRaidPath) == oldRaidHash,
            "A force-town flag change after raid preview must reject commit even while persist.raid.json remains present.");
        quantityActiveContent = quantityActiveContent with { SourceGameSha256 = ComputeSha256(quantityGamePath) };
        var staleRaidRowBlocked = false;
        try
        {
            _ = await quantityService.PrepareQuantityItemEditAsync(quantityProfile, serviceRaidTorch, 11, quantityActiveContent);
        }
        catch (InvalidOperationException error) when (error.Message.Contains("已不在副本", StringComparison.Ordinal))
        {
            staleRaidRowBlocked = true;
        }
        Assert(staleRaidRowBlocked, "Town with residue must not permit an old raid row to create a new raid preview.");

        foreach (var invalidState in new[] { "missing-flag", "contradictory", "missing-raid" })
        {
            if (invalidState == "missing-flag")
            {
                quantityGameRoot["base_root"]!.AsObject().Remove("inraid");
            }
            else
            {
                quantityGameRoot["base_root"]!["inraid"] = true;
            }
            quantityGameRoot["base_root"]!["raiddungeon"] = invalidState == "contradictory" ? "none" : "cove";
            File.WriteAllText(quantityDecodedGamePath, quantityGameRoot.ToJsonString(), new UTF8Encoding(false));
            await codec.EncodeAsync(quantityDecodedGamePath, quantityGamePath, originalBinaryPath: null);
            quantityActiveContent = quantityActiveContent with
            {
                SourceGameSha256 = ComputeSha256(quantityGamePath),
                WorkspaceDirectory = Path.Combine(runRoot, "quantity-scene-" + invalidState)
            };
            if (invalidState == "missing-raid")
            {
                File.Move(quantityRaidPath, parkedRaidPath);
            }
            var invalidSceneBlocked = false;
            try
            {
                _ = await QuantityItemCatalog.LoadAsync(quantityActiveContent, codec);
            }
            catch (Exception error) when (error is InvalidDataException or InvalidOperationException)
            {
                invalidSceneBlocked = true;
            }
            finally
            {
                if (invalidState == "missing-raid")
                {
                    File.Move(parkedRaidPath, quantityRaidPath);
                }
            }
            Assert(invalidSceneBlocked && File.ReadAllBytes(quantityEstatePath).SequenceEqual(quantityEstateBytesBeforeRaidEdit),
                $"An invalid scene ({invalidState}) must fail closed without falling back to town or changing estate data.");
        }

    }
}
