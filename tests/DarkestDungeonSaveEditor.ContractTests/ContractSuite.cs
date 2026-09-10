internal static partial class ContractSuite
{
    public static async Task RunMapContentOnlyAsync(string repositoryRoot)
    {
        var fixture = BuildContractFixture(repositoryRoot);
        await RunBattleMapContractsAsync(fixture.RunRoot, fixture.Codec);
        Console.WriteLine($"Artifacts: {fixture.RunRoot}");
    }

    public static async Task RunAsync(string repositoryRoot, bool includeBattle = true)
    {
        RunUiContracts(repositoryRoot);
        var fixture = BuildContractFixture(repositoryRoot);
        var runRoot = fixture.RunRoot;
        var gameRoot = fixture.GameRoot;
        var workshopRoot = fixture.WorkshopRoot;
        var additionalLocalModDirectory = fixture.AdditionalLocalModDirectory;
        var ambiguousDirectLocalModRoot = fixture.AmbiguousDirectLocalModRoot;
        var activeWorkshopRoot = fixture.ActiveWorkshopRoot;
        var activeWorkshopDistrictRoot = fixture.ActiveWorkshopDistrictRoot;
        var activeWorkshopInventoryRoot = fixture.ActiveWorkshopInventoryRoot;
        var externalLocalModRoot = fixture.ExternalLocalModRoot;
        var localHeroUpgradeRoot = fixture.LocalHeroUpgradeRoot;
        var localLootRoot = fixture.LocalLootRoot;
        var localModRoot = fixture.LocalModRoot;
        var localRaidCampingRoot = fixture.LocalRaidCampingRoot;
        var localTownEventsRoot = fixture.LocalTownEventsRoot;
        var profileRoot = fixture.ProfileRoot;
        var decodedSeedPath = fixture.DecodedSeedPath;
        var decodedGameSeedPath = fixture.DecodedGameSeedPath;
        var decodedTownSeedPath = fixture.DecodedTownSeedPath;
        var decodedRosterSeedPath = fixture.DecodedRosterSeedPath;
        var decodedUpgradesSeedPath = fixture.DecodedUpgradesSeedPath;
        var estatePath = fixture.EstatePath;
        var gameSavePath = fixture.GameSavePath;
        var townSavePath = fixture.TownSavePath;
        var rosterSavePath = fixture.RosterSavePath;
        var upgradesSavePath = fixture.UpgradesSavePath;
        var rosterHeroesSeed = fixture.RosterHeroesSeed;
        var codec = fixture.Codec;

        RunInventoryCapacityContracts(runRoot);

        if (includeBattle)
        {
            await RunNestedRaidSaveContractsAsync(fixture);
            await RunEncounterMaintenanceContractsAsync(runRoot, codec, nested: true);
            await RunModManifestPreparationContractsAsync(runRoot);
            await RunNativeResourceResolutionContractsAsync(runRoot, codec);
            await RunNativeCatalogReadingContractsAsync(runRoot, codec);
            RunSaveReplacementContracts(runRoot);
            await RunEncounterMaintenanceContractsAsync(runRoot, codec);
            await RunInheritedLegacyBridgeContractsAsync(runRoot, codec);
            await RunBattleMapWriteSafetyContractsAsync(runRoot, codec);
            await RunBattleMapContractsAsync(runRoot, codec);
            await RunBridgeClassificationContractsAsync(runRoot, codec);
            await RunMultiFileEncounterContractsAsync(runRoot, codec);
            await RunEmptyEncounterSlotContractsAsync(runRoot, codec);
            await RunEncounterRecordContractsAsync(runRoot, codec);
            await RunDlcNativeEncounterContractsAsync(runRoot, codec);
            await RunDedicatedBridgeContractsAsync(runRoot, codec, repositoryRoot);
        }
        await codec.EncodeAsync(decodedSeedPath, estatePath, originalBinaryPath: null);
        await codec.EncodeAsync(decodedGameSeedPath, gameSavePath, originalBinaryPath: null);
        await codec.EncodeAsync(decodedTownSeedPath, townSavePath, originalBinaryPath: null);
        await codec.EncodeAsync(decodedRosterSeedPath, rosterSavePath, originalBinaryPath: null);
        await codec.EncodeAsync(decodedUpgradesSeedPath, upgradesSavePath, originalBinaryPath: null);
        SetRevision(estatePath, [0x00, 0x00, 0x4A, 0x66]);
        SetRevision(townSavePath, [0x00, 0x00, 0x4A, 0x67]);
        SetRevision(rosterSavePath, [0x00, 0x00, 0x4A, 0x68]);
        SetRevision(upgradesSavePath, [0x00, 0x00, 0x4A, 0x69]);

        var profile = new SaveProfile(
            "profile_7",
            profileRoot,
            estatePath,
            "contract-user",
            File.GetLastWriteTimeUtc(estatePath));
        var locations = new SaveEditorLocations(
            Path.Combine(runRoot, "appdata"),
            Path.Combine(runRoot, "appdata", "workspaces"),
            Path.Combine(runRoot, "appdata", "backups"));
        var activeContent = await ActiveContentResolver.ResolveAsync(
            profile,
            gameRoot,
            workshopRoot,
            additionalLocalModDirectory,
            codec,
            locations.WorkspaceDirectory);
        var activeModSources = activeContent.Sources
            .Where(source => source.Kind is "local" or "workshop")
            .ToArray();
        Assert(activeContent.AppliedModCount == 4, "Expected four applied Mod records, including one missing Workshop item.");
        Assert(activeModSources.Length == 3, "Expected two resolved local Mods and one resolved Workshop Mod.");
        Assert(activeModSources[0].Id == "local:Local Test Mod", "Applied Mod keys should be sorted numerically.");
        Assert(activeModSources[1].Id == "local:External Test Mod", "The selected extra local Mod root should participate in numeric applied order.");
        Assert(
            activeModSources[1].Directory.Equals(Path.GetFullPath(externalLocalModRoot), StringComparison.OrdinalIgnoreCase),
            "The enabled external local Mod should resolve from the selected additional directory.");
        Assert(activeModSources[2].Id == "workshop:111", "Workshop load order should follow the numeric applied key.");
        Assert(activeContent.Issues.Any(issue => issue.Contains("333", StringComparison.Ordinal)), "Missing enabled Workshop content should be reported.");
        Assert(activeContent.Sources.Any(source => source.Id == "dlc-package:feature_pack"), "An enabled DLC feature should activate its package root content.");
        Assert(activeContent.Sources.Any(source => source.Id == "dlc-feature:enabled_feature"), "The explicitly enabled DLC feature is missing.");
        Assert(activeContent.Sources.All(source => source.Id != "dlc-feature:disabled_feature"), "A disabled DLC feature must not become an active source.");
        Assert(
            activeContent.Sources.Single(source => source.Id == "dlc-package:feature_pack").VirtualPathPrefix == "dlc/100_feature_pack",
            "A DLC package source must preserve its game-root virtual path prefix.");
        Assert(
            activeContent.Sources.Single(source => source.Id == "dlc-feature:enabled_feature").VirtualPathPrefix == "dlc/100_feature_pack/features/enabled_feature",
            "A DLC feature source must preserve its game-root virtual path prefix.");


        await RunProfileSyncContractsAsync(fixture);
        RunContentDiscoveryContracts(activeContent, fixture);
        RunManifestDiscoveryContracts(activeContent, fixture);
        RunLocalizationPolicyContracts(activeContent, fixture);
        RunLegacyLocalizationContracts(activeContent, fixture);
        RunLocalizationEntryIsolationContracts(activeContent, fixture);
        RunContentInventoryContracts(activeContent, fixture, repositoryRoot);
        RunLoggingContracts(activeContent, runRoot);
        var quantityState = await RunQuantityItemCatalogContractsAsync(
            activeContent,
            decodedSeedPath,
            localRaidCampingRoot,
            localTownEventsRoot,
            localLootRoot,
            activeWorkshopRoot,
            runRoot,
            codec,
            profile,
            gameRoot,
            workshopRoot,
            ambiguousDirectLocalModRoot,
            additionalLocalModDirectory,
            estatePath,
            decodedGameSeedPath,
            activeWorkshopDistrictRoot);
        var trinketState = RunTrinketCatalogContracts(
            activeContent,
            runRoot,
            activeWorkshopInventoryRoot);
        await VerifyTrinketJsonMembersAsync(activeContent, runRoot, codec);
        await RunHeroContractsAsync(
            activeContent,
            trinketState.ActiveCatalog,
            profile,
            codec,
            runRoot,
            localModRoot,
            localHeroUpgradeRoot,
            upgradesSavePath,
            townSavePath,
            rosterSavePath,
            rosterHeroesSeed);
        await RunTrinketSaveContractsAsync(
            profile,
            activeContent,
            locations,
            codec,
            trinketState,
            activeWorkshopRoot,
            gameSavePath,
            estatePath,
            profileRoot,
            decodedSeedPath,
            runRoot);
        await RunQuantitySaveContractsAsync(
            runRoot,
            profileRoot,
            profile,
            activeContent,
            codec,
            quantityState);
        RunRealModLocalizationContracts(runRoot);

        if (!includeBattle)
        {
            Console.WriteLine("PASS: catalog, localization, synchronization, hero, inventory and save contracts (battle group not selected).");
            Console.WriteLine($"Artifacts: {runRoot}");
            return;
        }

        Console.WriteLine("PASS: active game-mode/Mod catalogs, bilingual names, town/raid quantity edits with stack and slot guards, pristine ordinary/stateful trinket construction, level 0-max progression, blank/default and explicit natural/special quirks, HP/skill/camping rules, ordinary/shard stagecoach routing with GUID/upgrade append, real battle-map snapshot/live-monitor plus guarded delete/move/battle placement and room battle attachments, global enabled-content encounter discovery and persistent managed Bridge append/reuse, force-town contracts, full-roster preservation, stale guards, DSON roundtrips, verified backups, three-file rollback, and trinket/quantity commit contracts.");
        Console.WriteLine($"Artifacts: {runRoot}");
    }
}
