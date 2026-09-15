internal static partial class ContractSuite
{
    private static async Task RunProfileSyncContractsAsync(ContractFixture fixture)
    {
        var root = Path.Combine(fixture.RunRoot, "profile-sync");
        var profileRoot = Path.Combine(root, "profile_8");
        Directory.CreateDirectory(profileRoot);
        foreach (var name in ProfileCatalogSnapshotReader.WatchedFileNames)
        {
            var source = Path.Combine(fixture.ProfileRoot, name);
            if (File.Exists(source)) File.Copy(source, Path.Combine(profileRoot, name));
        }
        var profile = new SaveProfile("profile_8", profileRoot, Path.Combine(profileRoot, "persist.estate.json"),
            "contract", DateTime.UtcNow);
        var content = await ActiveContentResolver.ResolveAsync(profile, fixture.GameRoot, fixture.WorkshopRoot,
            fixture.AdditionalLocalModDirectory, fixture.Codec, root);
        var initial = await QuantityItemCatalog.LoadAsync(content, fixture.Codec);
        var reader = new ProfileCatalogSnapshotReader(content, initial, fixture.Codec,
            fixture.GameRoot, fixture.WorkshopRoot, fixture.AdditionalLocalModDirectory, root);
        var first = await reader.ReadAsync();
        Assert(first.Content.SourceGameSha256 == content.SourceGameSha256 &&
            first.FileHashes["persist.game.json"] == content.SourceGameSha256,
            "The shared reader must use the same hash representation as existing content/map readers.");
        Assert(ProfileCatalogSnapshotReader.HashesEqual(first.FileHashes,
            first.FileHashes.ToDictionary(pair => pair.Key, pair => pair.Value?.ToUpperInvariant())),
            "Equivalent upper/lower-case SHA-256 strings are the same snapshot, not perpetual partial writes.");
        var unchanged = await reader.ReadAsync();
        Assert(ReferenceEquals(first.Content, unchanged.Content) && ReferenceEquals(first.QuantityItems, unchanged.QuantityItems),
            "A catch-up poll with unchanged hashes must reuse the complete snapshot and definitions.");
        var scratch = Path.GetDirectoryName(first.Content.WorkspaceDirectory)!;
        var scratchFiles = Directory.EnumerateFiles(scratch, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.GetLastWriteTimeUtc);
        for (var poll = 0; poll < 3; poll++)
        {
            var quiet = await reader.ReadAsync(refreshContent: true);
            Assert(ReferenceEquals(first.Content, quiet.Content) && ReferenceEquals(first.QuantityItems, quiet.QuantityItems) &&
                scratchFiles.Count == Directory.EnumerateFiles(scratch, "*", SearchOption.AllDirectories).Count() &&
                scratchFiles.All(file => File.GetLastWriteTimeUtc(file.Key) == file.Value),
                "Idle resource polling must not decode or rewrite scratch copies of unchanged saves.");
        }
        var hotUpdatePath = WriteMultiMash(fixture.GameRoot, "dungeons/cove/maintenance_hot_update.1.mash.darkest",
            "hall: .chance 1 .types hot_updated_monster\n");
        var hotUpdated = await reader.ReadAsync(refreshContent: true);
        Assert(hotUpdated.ContentFingerprint != first.ContentFingerprint &&
            ProfileCatalogSnapshotReader.HashesEqual(first.FileHashes, hotUpdated.FileHashes) &&
            !ReferenceEquals(hotUpdated.QuantityItems, first.QuantityItems),
            "An in-place encounter update with no save writes must refresh the shared content snapshot.");
        File.Delete(hotUpdatePath);
        first = await reader.ReadAsync(refreshContent: true);

        var itemUpdatePath = WriteMultiMash(fixture.GameRoot, "inventory/hot.inventory.items.darkest",
            "inventory_item: .type estate .id hot_catalog_quantity .base_stack_limit 3\n");
        var itemUpdate = await reader.ReadAsync(refreshContent: true);
        var originalTime = File.GetLastWriteTimeUtc(itemUpdatePath);
        File.WriteAllText(itemUpdatePath, "inventory_item: .type estate .id hot_catalog_quantity .base_stack_limit 4\n");
        File.SetLastWriteTimeUtc(itemUpdatePath, originalTime);
        var repeatedItemUpdate = await reader.ReadAsync(refreshContent: true);
        Assert(itemUpdate.QuantityItems.Items.Single(item => item.ItemId == "hot_catalog_quantity").BaseStackLimit == 3 &&
            repeatedItemUpdate.QuantityItems.Items.Single(item => item.ItemId == "hot_catalog_quantity").BaseStackLimit == 4 &&
            repeatedItemUpdate.ContentFingerprint != itemUpdate.ContentFingerprint &&
            ProfileCatalogSnapshotReader.HashesEqual(first.FileHashes, repeatedItemUpdate.FileHashes),
            "Same-size, same-timestamp definition changes must refresh cached quantities without save or manifest edits.");
        var quirkUpdatePath = WriteMultiMash(fixture.GameRoot, "shared/quirk/hot.quirk_library.json",
            """{"quirks":[{"id":"hot_refresh_quirk","is_positive":true,"random_chance":0}]}""");
        var quirkUpdate = await reader.ReadAsync(refreshContent: true);
        Assert(quirkUpdate.ContentFingerprint != repeatedItemUpdate.ContentFingerprint &&
            HeroClassCatalog.Load(quirkUpdate.Content).InitialQuirks.Any(quirk => quirk.Id == "hot_refresh_quirk"),
            "Nonbattle definition edits must invalidate the shared page refresh signal.");
        File.Delete(itemUpdatePath);
        File.Delete(quirkUpdatePath);
        first = await reader.ReadAsync(refreshContent: true);

        var game = (JsonObject)JsonNode.Parse(File.ReadAllText(fixture.DecodedGameSeedPath))!;
        var gameBase = (JsonObject)game["base_root"]!;
        gameBase["week"] = 88;
        var gamePath = Path.Combine(profileRoot, "persist.game.json");
        File.WriteAllText(gamePath, game.ToJsonString());
        var progress = await reader.ReadAsync();
        Assert(progress.ConfigurationKey == first.ConfigurationKey &&
            progress.Content.SourceGameSha256 != first.Content.SourceGameSha256 &&
            ReferenceEquals(progress.QuantityItems, first.QuantityItems),
            "Ordinary game progress must update the game guard without rescanning item definitions.");

        var estate = (JsonObject)JsonNode.Parse(File.ReadAllText(fixture.DecodedSeedPath))!;
        var gold = initial.Items.Single(item => item.DisplayId == "gold");
        estate = QuantityItemSaveEditor.SetAmount(estate, gold, 5432).UpdatedRoot;
        File.WriteAllText(profile.EstateSavePath, estate.ToJsonString());
        var quantities = await reader.ReadAsync();
        var newGold = quantities.QuantityItems.Items.Single(item => item.DisplayId == "gold");
        Assert(newGold.CurrentAmount == 5432 && newGold.LocalizedName == gold.LocalizedName &&
            newGold.SourceLabel == gold.SourceLabel,
            "Quantity refresh must adopt live amounts without changing bilingual names or provenance.");
        var validGame = File.ReadAllBytes(gamePath);
        var cachedGame = quantities.Content.DecodedGamePath;
        var cachedBytes = File.ReadAllBytes(cachedGame);
        File.WriteAllText(gamePath, "{partial-game");
        var cachedPartialRejected = false;
        try { await reader.ReadAsync(refreshContent: true); }
        catch (Exception error) when (error is not OperationCanceledException) { cachedPartialRejected = true; }
        Assert(cachedPartialRejected && File.Exists(cachedGame) && cachedBytes.SequenceEqual(File.ReadAllBytes(cachedGame)),
            "Game -> quantity -> partial game must never delete the still-published decoded game cache.");
        File.WriteAllBytes(gamePath, validGame);
        var restoredGame = await reader.ReadAsync(refreshContent: true);
        Assert(restoredGame.Content.SourceGameSha256 == quantities.Content.SourceGameSha256 &&
            restoredGame.QuantityItems.Items.Single(item => item.DisplayId == "gold").CurrentAmount == 5432,
            "Restoring the prior game bytes after a failed speculative decode must recover without reloading the profile.");
        var orphan = initial.Items.Single(item => item.DisplayId == "orphan_mod_essence");
        estate = QuantityItemSaveEditor.SetAmount(estate, orphan, 1).UpdatedRoot;
        File.WriteAllText(profile.EstateSavePath, estate.ToJsonString());
        var residue = await reader.ReadAsync();
        Assert(!residue.QuantityItems.Items.Single(item => item.DisplayId == orphan.DisplayId).IsHiddenByDefault,
            "An unused definition newly present in a save must leave the hidden-only view.");

        using (var locked = File.Open(gamePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var rejected = false;
            try { await reader.ReadAsync(); } catch (IOException) { rejected = true; }
            Assert(rejected, "An exclusively locked game save must not yield a fresh snapshot.");
        }
        File.WriteAllText(profile.EstateSavePath, "{\"base_root\":");
        var partialRejected = false;
        try { await reader.ReadAsync(); } catch (System.Text.Json.JsonException) { partialRejected = true; }
        Assert(partialRejected, "Partial save JSON must not replace the last complete cached catalog.");
        File.WriteAllText(profile.EstateSavePath, estate.ToJsonString());
        Assert((await reader.ReadAsync()).QuantityItems.Items.Single(item => item.DisplayId == "gold").CurrentAmount == 5432,
            "Reading must recover after a partial save without a manual catalog reload.");

        var raidPath = Path.Combine(profileRoot, "persist.raid.json");
        var raid = (JsonObject)JsonNode.Parse("""
            {"base_root":{"party":{"inventory":{"items":{
              "0":{"id":"torch","type":"supply","amount":6},
              "1":{"id":"","type":"gold","amount":2500}
            }}}}}
            """)!;
        File.WriteAllText(raidPath, raid.ToJsonString());
        File.WriteAllText(Path.Combine(profileRoot, "persist.map.json"), "{\"base_root\":{}}");
        gameBase["inraid"] = true;
        gameBase["raiddungeon"] = "cove";
        File.WriteAllText(gamePath, game.ToJsonString());
        var inRaid = await reader.ReadAsync();
        Assert(inRaid.QuantityItems.SaveContext == QuantityItemSaveContext.Raid &&
            inRaid.QuantityItems.RaidOccupiedSlots == 2 &&
            inRaid.QuantityItems.Items.Single(item => item.DisplayId == "gold").StorageKind == QuantityItemStorageKind.RaidInventory,
            "Scene changes must build the raid catalog, preserve overlapping resources, and count occupied slots.");
        ((JsonObject)raid["base_root"]!["party"]!["inventory"]!["items"]!).Remove("0");
        File.WriteAllText(raidPath, raid.ToJsonString());
        var torchRemoved = await reader.ReadAsync();
        var remainingTorch = torchRemoved.QuantityItems.Items.Single(item => item.DisplayId == "torch");
        Assert(remainingTorch.CurrentAmount == 0 && !remainingTorch.IsHiddenByDefault && torchRemoved.QuantityItems.RaidOccupiedSlots == 1,
            "A used-up torch remains creatable and raid slot counts refresh independently of town rules.");

        gameBase["inraid"] = false;
        gameBase["raiddungeon"] = "none";
        File.WriteAllText(gamePath, game.ToJsonString());
        var backInTown = await reader.ReadAsync();
        Assert(backInTown.QuantityItems.SaveContext == QuantityItemSaveContext.Town &&
            backInTown.QuantityItems.Items.Single(item => item.DisplayId == "gold").CurrentAmount == 5432,
            "Returning to town must ignore residual raid files and reuse the town classification cache.");
        gameBase["game_mode"] = "radiant";
        File.WriteAllText(gamePath, game.ToJsonString());
        var changedContent = await reader.ReadAsync();
        Assert(changedContent.ConfigurationKey != backInTown.ConfigurationKey && changedContent.Content.GameMode == "radiant" &&
            !ReferenceEquals(changedContent.QuantityItems, backInTown.QuantityItems),
            "Actual content configuration changes must resolve sources and invalidate both scene caches.");

        var orderA = (JsonObject)JsonNode.Parse("""{"base_root":{"game_mode":"base","applied_ugcs_1_0":{"0":{"name":"a"},"1":{"name":"b"}}}}""")!;
        var orderB = (JsonObject)JsonNode.Parse("""{"base_root":{"applied_ugcs_1_0":{"1":{"name":"b"},"0":{"name":"a"}},"game_mode":"base","week":9}}""")!;
        Assert(ProfileContentConfiguration.GetKey(orderA) == ProfileContentConfiguration.GetKey(orderB),
            "JSON property order and progress are not a Mod load-order change.");
        orderB["base_root"]!["applied_ugcs_1_0"]!["0"]!["name"] = "b";
        Assert(ProfileContentConfiguration.GetKey(orderA) != ProfileContentConfiguration.GetKey(orderB),
            "A changed numbered Mod entry must invalidate the configuration fingerprint.");

        var beforeRosterChange = changedContent.FileHashes;
        File.AppendAllText(Path.Combine(profileRoot, "persist.roster.json"), " ");
        Assert(!ProfileCatalogSnapshotReader.HashesEqual(beforeRosterChange, ProfileCatalogSnapshotReader.CaptureHashes(profileRoot)),
            "Roster-only changes must invalidate hero/quirk previews even when game and estate saves are unchanged.");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = false;
        try { await reader.ReadAsync(cancellation.Token); } catch (OperationCanceledException) { cancelled = true; }
        Assert(cancelled, "A stopped profile reader must honor cancellation instead of publishing another profile's state.");

        Assert(Directory.GetDirectories(Path.Combine(root, "profile_sync")).Length == 1 &&
            Directory.GetDirectories(Directory.GetDirectories(Path.Combine(root, "profile_sync"))[0]).Length == 2,
            "Continuous synchronization must reuse two scratch slots rather than grow an archive on every save.");
        Console.WriteLine("PASS: profile synchronization, scene caches, stale guards, locks, partial writes and configuration identity");
    }
}
