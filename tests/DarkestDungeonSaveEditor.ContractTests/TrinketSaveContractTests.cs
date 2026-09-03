internal static partial class ContractSuite
{
    private static async Task RunTrinketSaveContractsAsync(
        SaveProfile profile,
        ActiveContentSnapshot activeContent,
        SaveEditorLocations locations,
        DsonSaveCodec codec,
        TrinketCatalogContractState state,
        string activeWorkshopRoot,
        string gameSavePath,
        string estatePath,
        string profileRoot,
        string decodedSeedPath,
        string runRoot)
    {
        var activeCatalog = state.ActiveCatalog;
        var activeWorkshopTrinket = state.ActiveWorkshopTrinket;
        var ambiguousTrinket = state.AmbiguousTrinket;
        var definitionDrivenStateful = state.DefinitionDrivenStateful;
        var dualCounterStateful = state.DualCounterStateful;
        var invalidCapacityContent = state.InvalidCapacityContent;
        var invalidCounterStateful = state.InvalidCounterStateful;
        var ordinary = state.Ordinary;
        var stateful = state.Stateful;
        var triggerStateful = state.TriggerStateful;
        var unlimited = state.Unlimited;
        var service = new SaveEditService(codec, locations);
        var ambiguousTrinketBlocked = false;
        try
        {
            _ = await service.PrepareTrinketEditAsync(
                profile,
                ambiguousTrinket,
                1,
                activeCatalog.Storage,
                activeContent);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("unresolved definitions", StringComparison.Ordinal))
        {
            ambiguousTrinketBlocked = true;
        }

        Assert(ambiguousTrinketBlocked, "An unresolved trinket definition must not enter the save preview path.");
        var workspaceCountBeforeUnknownCapacity = Directory.Exists(locations.WorkspaceDirectory)
            ? Directory.GetDirectories(locations.WorkspaceDirectory).Length
            : 0;
        var unknownStorageCapacityBlocked = false;
        try
        {
            _ = await service.PrepareTrinketEditAsync(
                profile,
                ordinary,
                1,
                expectedStorage: null,
                activeContent);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("capacity could not be resolved", StringComparison.Ordinal))
        {
            unknownStorageCapacityBlocked = true;
        }

        Assert(unknownStorageCapacityBlocked, "An unresolved active storage capacity must block trinket preview.");
        Assert(
            (!Directory.Exists(locations.WorkspaceDirectory) ? 0 : Directory.GetDirectories(locations.WorkspaceDirectory).Length) ==
            workspaceCountBeforeUnknownCapacity,
            "An unresolved storage capacity must be rejected before a trinket edit workspace is created.");
        var invalidHighestPriorityCapacityBlocked = false;
        try
        {
            _ = await service.PrepareTrinketEditAsync(
                profile,
                ordinary,
                1,
                activeCatalog.Storage,
                invalidCapacityContent);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains(
            "capacity could not be resolved",
            StringComparison.Ordinal))
        {
            invalidHighestPriorityCapacityBlocked = true;
        }

        Assert(
            invalidHighestPriorityCapacityBlocked,
            "The core preview path must reject an invalid higher-priority capacity instead of trusting the prior catalog value.");
        Assert(
            (!Directory.Exists(locations.WorkspaceDirectory) ? 0 : Directory.GetDirectories(locations.WorkspaceDirectory).Length) ==
            workspaceCountBeforeUnknownCapacity,
            "An invalid higher-priority capacity must be rejected before a trinket edit workspace is created.");
        var storageCapacityBlocked = false;
        try
        {
            _ = await service.PrepareTrinketEditAsync(
                profile,
                ordinary,
                3,
                activeCatalog.Storage,
                activeContent);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("exceed the active storage capacity", StringComparison.Ordinal))
        {
            storageCapacityBlocked = true;
        }

        Assert(storageCapacityBlocked, "A trinket edit that exceeds the active total storage capacity must be blocked.");
        var unlimitedPrepared = await service.PrepareTrinketEditAsync(
            profile,
            unlimited,
            2,
            activeCatalog.Storage,
            activeContent);
        Assert(
            unlimitedPrepared.Preview.DefinitionLimit == 0 &&
            !unlimitedPrepared.Preview.ExceedsDefinitionLimit &&
            unlimitedPrepared.Preview.ResultingInventoryEntries == unlimitedPrepared.Preview.StorageCapacity,
            "Definition limit zero must remain unlimited, and filling exactly to total capacity must be allowed.");
        var modDefinitionPrepared = await service.PrepareTrinketEditAsync(
            profile,
            activeWorkshopTrinket,
            1,
            activeCatalog.Storage,
            activeContent);
        var prepared = await service.PrepareTrinketEditAsync(
            profile,
            ordinary,
            2,
            activeCatalog.Storage,
            activeContent);

        Assert(prepared.Preview.ExistingCopies == 0, "Preview should start with zero focus rings.");
        Assert(prepared.Preview.ResultingCopies == 2, "Preview should result in two focus rings.");
        Assert(
            prepared.Preview.DefinitionLimit == 1 && prepared.Preview.ExceedsDefinitionLimit,
            "The console-style preview should retain a per-id over-limit write while marking it explicitly.");
        Assert(
            prepared.Preview.ExistingInventoryEntries == 1 &&
            prepared.Preview.ResultingInventoryEntries == 3 &&
            prepared.Preview.StorageCapacity == 3 &&
            prepared.Preview.ResultingInventoryEntries == prepared.Preview.StorageCapacity,
            "The preview should report existing, resulting, and maximum storage slots.");
        Assert(prepared.OriginalSummary.TrinketCopies == 1, "Seed should have one trinket.");
        Assert(prepared.ResultSummary.TrinketCopies == 3, "Result should have three trinkets.");
        Assert(ReadRevision(prepared.SourceCopyPath).SequenceEqual(ReadRevision(prepared.EncodedPath)), "Revision bytes were not preserved.");
        var ordinaryPreviewRoot = JsonNode.Parse(File.ReadAllText(prepared.ProposedDecodedPath)) as JsonObject
            ?? throw new InvalidDataException("The ordinary trinket preview did not contain an object.");
        var ordinaryPreviewItems = ordinaryPreviewRoot["base_root"]?["trinkets"]?["items"] as JsonObject
            ?? throw new InvalidDataException("The ordinary trinket preview did not contain an inventory.");
        var ordinaryPreviewCopies = ordinaryPreviewItems
            .Select(pair => pair.Value)
            .OfType<JsonObject>()
            .Where(item => string.Equals(
                item["id"]?.GetValue<string>(),
                "focus_ring",
                StringComparison.Ordinal))
            .ToArray();
        Assert(
            ordinaryPreviewCopies.Length == 2 &&
            ordinaryPreviewCopies.All(item =>
                item["added_buffs"]?.GetValue<int>() == 0 &&
                item["hero_name"]?.GetValue<string>() == string.Empty &&
                item["previous_trinket_id"]?.GetValue<string>() == string.Empty &&
                item["did_transform"]?.GetValue<bool>() == false &&
                item["trinkets_gained_count"]?.GetValue<int>() == 0 &&
                !item.ContainsKey("quest_uses_remaining") &&
                !item.ContainsKey("triggers_remaining")),
            "Ordinary trinkets must use the same complete pristine instance shape without invented counters.");

        var selectedModDefinitionBytes = File.ReadAllBytes(activeWorkshopTrinket.SourcePath);
        File.Delete(activeWorkshopTrinket.SourcePath);
        var missingSelectedModDefinitionBlocked = false;
        try
        {
            _ = await service.CommitAsync(modDefinitionPrepared);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains(
            "selected trinket definition changed after preview",
            StringComparison.OrdinalIgnoreCase))
        {
            missingSelectedModDefinitionBlocked = true;
        }

        Assert(
            missingSelectedModDefinitionBlocked,
            "Removing the selected Mod trinket definition after preview must block commit.");
        Assert(
            !Directory.Exists(locations.BackupDirectory),
            "A missing selected Mod definition should be rejected before creating a backup.");
        File.WriteAllBytes(activeWorkshopTrinket.SourcePath, selectedModDefinitionBytes);

        var activeWorkshopManifestPath = Path.Combine(activeWorkshopRoot, "modfiles.txt");
        var originalActiveWorkshopManifestBytes = File.ReadAllBytes(activeWorkshopManifestPath);
        File.AppendAllText(
            activeWorkshopManifestPath,
            Environment.NewLine + "// manifest fingerprint probe",
            new UTF8Encoding(false));
        var changedManifestBlocked = false;
        try
        {
            _ = await service.CommitAsync(prepared);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains(
            "active Mod manifest changed after preview",
            StringComparison.OrdinalIgnoreCase))
        {
            changedManifestBlocked = true;
        }

        Assert(
            changedManifestBlocked,
            "Any active Mod manifest change after preview must block commit even when effective definitions stay the same.");
        Assert(
            !Directory.Exists(locations.BackupDirectory),
            "A changed active Mod manifest should be rejected before creating a backup.");
        File.WriteAllBytes(activeWorkshopManifestPath, originalActiveWorkshopManifestBytes);

        var originalStorageConfigBytes = File.ReadAllBytes(activeCatalog.Storage!.SourcePath);
        File.WriteAllText(
            activeCatalog.Storage.SourcePath,
            """inventory_system_config: .type "trinket_storage" .max_slots 2""",
            new UTF8Encoding(false));
        var staleStorageConfigurationBlocked = false;
        try
        {
            _ = await service.CommitAsync(prepared);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains(
            "storage configuration changed after preview",
            StringComparison.Ordinal))
        {
            staleStorageConfigurationBlocked = true;
        }

        Assert(
            staleStorageConfigurationBlocked,
            "A capacity-file change after preview must block commit before touching the live save.");
        Assert(
            !Directory.Exists(locations.BackupDirectory),
            "A stale storage configuration should be rejected before creating a backup.");
        File.WriteAllBytes(activeCatalog.Storage.SourcePath, originalStorageConfigBytes);

        var originalGameSaveBytes = File.ReadAllBytes(gameSavePath);
        var changedGameSaveBytes = originalGameSaveBytes.ToArray();
        changedGameSaveBytes[^1] ^= 0x01;
        File.WriteAllBytes(gameSavePath, changedGameSaveBytes);
        var staleModConfigurationBlocked = false;
        try
        {
            _ = await service.CommitAsync(prepared);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains(
            "Mod/DLC configuration changed after preview",
            StringComparison.Ordinal))
        {
            staleModConfigurationBlocked = true;
        }

        Assert(
            staleModConfigurationBlocked,
            "A persist.game.json change after preview must block trinket commit.");
        Assert(
            !Directory.Exists(locations.BackupDirectory),
            "A stale Mod/DLC configuration should be rejected before creating a backup.");
        File.WriteAllBytes(gameSavePath, originalGameSaveBytes);

        var staleEstate = File.ReadAllBytes(estatePath);
        staleEstate[^1] ^= 0x01;
        File.WriteAllBytes(estatePath, staleEstate);
        var staleCommitBlocked = false;
        try
        {
            _ = await service.CommitAsync(prepared);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("changed after preview", StringComparison.Ordinal))
        {
            staleCommitBlocked = true;
        }

        Assert(staleCommitBlocked, "Commit should reject a live save changed after preview.");
        Assert(!Directory.Exists(locations.BackupDirectory), "A stale preview should be rejected before creating a backup.");
        File.Copy(prepared.SourceCopyPath, estatePath, overwrite: true);

        var estateRaceWrite = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var estateRaceCommitBlocked = false;
        using (var estateRaceWatcher = new FileSystemWatcher(profileRoot)
        {
            Filter = ".persist.estate.json.ddse-*.tmp",
            NotifyFilter = NotifyFilters.FileName
        })
        {
            estateRaceWatcher.Created += (_, _) =>
            {
                try
                {
                    var changedEstate = File.ReadAllBytes(estatePath);
                    changedEstate[^1] ^= 0x01;
                    File.WriteAllBytes(estatePath, changedEstate);
                    estateRaceWrite.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    estateRaceWrite.TrySetException(ex);
                }
            };
            estateRaceWatcher.EnableRaisingEvents = true;
            try
            {
                _ = await service.CommitAsync(prepared);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains(
                "live estate save changed immediately before replacement",
                StringComparison.OrdinalIgnoreCase))
            {
                estateRaceCommitBlocked = true;
            }
        }

        Assert(
            await estateRaceWrite.Task.WaitAsync(TimeSpan.FromSeconds(5)),
            "The test writer should change the estate after the prepared temporary file appears.");
        Assert(
            estateRaceCommitBlocked,
            "An estate change after the prepared temporary file appears must still block replacement.");
        File.Copy(prepared.SourceCopyPath, estatePath, overwrite: true);

        var preparedStateful = await service.PrepareTrinketEditAsync(
            profile,
            stateful,
            2,
            activeCatalog.Storage,
            activeContent);
        var preparedStatefulRoot = JsonNode.Parse(File.ReadAllText(preparedStateful.ProposedDecodedPath)) as JsonObject
            ?? throw new InvalidDataException("The stateful trinket preview did not contain an object.");
        var preparedStatefulItems = preparedStatefulRoot["base_root"]?["trinkets"]?["items"] as JsonObject
            ?? throw new InvalidDataException("The stateful trinket preview did not contain an inventory.");
        var preparedStatefulCopies = preparedStatefulItems
            .Select(pair => pair.Value)
            .OfType<JsonObject>()
            .Where(item => string.Equals(
                item["id"]?.GetValue<string>(),
                "fire_probe",
                StringComparison.Ordinal))
            .ToArray();
        Assert(
            preparedStatefulCopies.Length == 2 &&
            preparedStatefulCopies.All(item =>
                item["quest_uses_remaining"]?.GetValue<int>() == 3 &&
                item["used_during_quest"]?.GetValue<bool>() == false &&
                item["added_buffs"]?.GetValue<int>() == 0 &&
                item["hero_name"]?.GetValue<string>() == string.Empty &&
                item["previous_trinket_id"]?.GetValue<string>() == string.Empty &&
                item["did_transform"]?.GetValue<bool>() == false &&
                item["trinkets_gained_count"]?.GetValue<int>() == 0 &&
                !item.ContainsKey("triggers_remaining")),
            "Every quest-use copy must roundtrip as an independent, fully initialized pristine instance.");

        var statefulShapeSeed = JsonNode.Parse(File.ReadAllText(decodedSeedPath)) as JsonObject
            ?? throw new InvalidDataException("The stateful shape seed did not contain an object.");
        var (triggerShapeRoot, _) = TrinketSaveEditor.AddCopies(
            statefulShapeSeed,
            triggerStateful,
            1,
            20);
        var (dualShapeRoot, _) = TrinketSaveEditor.AddCopies(
            statefulShapeSeed,
            dualCounterStateful,
            1,
            20);
        var (definitionDrivenShapeRoot, _) = TrinketSaveEditor.AddCopies(
            statefulShapeSeed,
            definitionDrivenStateful,
            1,
            20);
        static JsonObject FindTrinketInstance(JsonObject root, string id) =>
            (root["base_root"]?["trinkets"]?["items"] as JsonObject
                ?? throw new InvalidDataException("The trinket shape fixture did not contain an inventory."))
                .Select(pair => pair.Value)
                .OfType<JsonObject>()
                .Single(item => string.Equals(item["id"]?.GetValue<string>(), id, StringComparison.Ordinal));
        var triggerShape = FindTrinketInstance(triggerShapeRoot, "trigger_probe");
        var dualShape = FindTrinketInstance(dualShapeRoot, "dual_counter_probe");
        var definitionDrivenShape = FindTrinketInstance(definitionDrivenShapeRoot, "definition_driven_probe");
        Assert(
            triggerShape["triggers_remaining"]?.GetValue<int>() == 4 &&
            !triggerShape.ContainsKey("quest_uses_remaining") &&
            triggerShape["did_transform"]?.GetValue<bool>() == false,
            "Trigger-limited trinkets must start at their complete trigger limit and an untransformed state.");
        Assert(
            dualShape["quest_uses_remaining"]?.GetValue<int>() == 2 &&
            dualShape["triggers_remaining"]?.GetValue<int>() == 3 &&
            dualShape["used_during_quest"]?.GetValue<bool>() == false,
            "A dual-counter trinket must initialize both counters to the complete definition values.");
        Assert(
            !definitionDrivenShape.ContainsKey("quest_uses_remaining") &&
            !definitionDrivenShape.ContainsKey("triggers_remaining") &&
            definitionDrivenShape["did_transform"]?.GetValue<bool>() == false,
            "Definition-only lifecycle behavior must create a normal pristine instance without invented counters.");

        var invalidStatefulBlocked = false;
        try
        {
            _ = await service.PrepareTrinketEditAsync(
                profile,
                invalidCounterStateful,
                1,
                activeCatalog.Storage,
                activeContent);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("次数定义无效", StringComparison.Ordinal))
        {
            invalidStatefulBlocked = true;
        }

        Assert(invalidStatefulBlocked, "An invalid state counter must fail closed before creating an instance.");

        var contentRaceWriteBlocked = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        SaveCommitResult? guardedCommit = null;
        var contentRaceWasBlocked = false;
        using (var contentRaceWatcher = new FileSystemWatcher(profileRoot)
        {
            Filter = ".persist.estate.json.ddse-*.tmp",
            NotifyFilter = NotifyFilters.FileName
        })
        {
            contentRaceWatcher.Created += (_, _) =>
            {
                try
                {
                    File.WriteAllText(
                        activeWorkshopManifestPath,
                        "// concurrent manifest replacement",
                        new UTF8Encoding(false));
                    contentRaceWriteBlocked.TrySetResult(false);
                }
                catch (IOException)
                {
                    contentRaceWriteBlocked.TrySetResult(true);
                }
                catch (UnauthorizedAccessException)
                {
                    contentRaceWriteBlocked.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    contentRaceWriteBlocked.TrySetException(ex);
                }
            };
            contentRaceWatcher.EnableRaisingEvents = true;
            try
            {
                guardedCommit = await service.CommitAsync(prepared);
                contentRaceWasBlocked = await contentRaceWriteBlocked.Task.WaitAsync(TimeSpan.FromSeconds(5));
            }
            finally
            {
                File.WriteAllBytes(activeWorkshopManifestPath, originalActiveWorkshopManifestBytes);
            }
        }

        Assert(
            contentRaceWasBlocked,
            "A Mod manifest writer triggered by the prepared temporary file must be blocked until replacement completes.");
        var commit = guardedCommit ?? throw new InvalidOperationException("The guarded trinket commit did not complete.");
        Assert(File.Exists(Path.Combine(commit.BackupDirectory, "persist.estate.json")), "Estate backup is missing.");
        Assert(File.Exists(Path.Combine(commit.BackupDirectory, "backup-manifest.json")), "Backup manifest is missing.");
        Assert(File.Exists(Path.Combine(commit.BackupDirectory, "commit-result.json")), "Commit result is missing.");
        Assert(ReadRevision(estatePath).SequenceEqual(new byte[] { 0x00, 0x00, 0x4A, 0x66 }), "Committed revision changed.");

        var committedDecoded = Path.Combine(runRoot, "committed.persist.estate.json");
        await codec.DecodeAsync(estatePath, committedDecoded);
        var committedRoot = JsonNode.Parse(File.ReadAllText(committedDecoded)) as JsonObject
            ?? throw new InvalidDataException("Committed estate did not decode to an object.");
        Assert(TrinketSaveEditor.CountCopies(committedRoot, "focus_ring") == 2, "Committed save has the wrong focus ring count.");

    }
}
