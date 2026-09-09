internal static partial class ContractSuite
{
    private static async Task RunHeroContractsAsync(
        ActiveContentSnapshot activeContent,
        TrinketCatalogResult activeCatalog,
        SaveProfile profile,
        DsonSaveCodec codec,
        string runRoot,
        string localModRoot,
        string localHeroUpgradeRoot,
        string upgradesSavePath,
        string townSavePath,
        string rosterSavePath,
        JsonObject rosterHeroesSeed)
    {
        var catalog = VerifyHeroCatalogContracts(activeContent, activeCatalog, localModRoot);
        await VerifyHeroUpgradeTreeResolutionAsync(activeContent, codec, runRoot);
        VerifyHeroDefinitionSafetyContracts(activeContent, catalog.LocalHero, runRoot);
        VerifyHeroNativeSemantics(activeContent, catalog.LocalHero, runRoot);
        VerifyNativeItemReferences(activeContent, runRoot);
        await VerifyInventoryIdentitiesAsync(activeContent, runRoot, codec);
        VerifyResourceDuplicateSemantics(activeContent, catalog.LocalHero, runRoot);
        VerifyHeroAvailabilityContracts(catalog.HeroCatalog, catalog.LocalHero);
        await VerifyImplicitSkillProgressionContractsAsync(catalog.HeroCatalog, catalog.LocalHero, codec, runRoot);
        VerifyHeroQuirkContracts(catalog.HeroCatalog, catalog.LocalHero);
        var candidates = VerifyHeroCandidateContracts(catalog.HeroCatalog, catalog.LocalHero);
        await VerifyStagecoachRefreshWarningContractsAsync(activeContent, profile, codec, runRoot, catalog.HeroCatalog, candidates);
        await VerifyStagecoachHeroSaveContractsAsync(
            activeContent,
            profile,
            codec,
            runRoot,
            localHeroUpgradeRoot,
            upgradesSavePath,
            townSavePath,
            rosterSavePath,
            rosterHeroesSeed,
            catalog.HeroCatalog,
            candidates);
    }

    private sealed record HeroCatalogContractContext(
        HeroClassCatalogResult HeroCatalog,
        HeroClassDefinition LocalHero);

    private sealed record HeroCandidateContractContext(
        GeneratedStagecoachHeroCandidate LevelFourCandidate,
        GeneratedStagecoachHeroCandidate ContextLimitedCandidate,
        GeneratedStagecoachHeroCandidate ShardCandidate);
}
