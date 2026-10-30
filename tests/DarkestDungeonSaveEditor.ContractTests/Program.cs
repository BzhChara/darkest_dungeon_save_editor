try
{
    // Legacy contracts intentionally assert the existing Simplified Chinese messages.
    // The localization group switches both cultures explicitly and restores this baseline.
    DarkestDungeonSaveEditor.Core.EditorText.Initialize("zh-CN", System.Globalization.CultureInfo.CurrentUICulture);
    if (await ContractSuite.TryRunCodecProcessProbeAsync(args)) return;
    if (args.Length == 2 && args[1] == "--managed-bridge")
    {
        await ContractSuite.RunManagedBridgeOnlyAsync(Path.GetFullPath(args[0]));
        return;
    }
    if (args.Length == 3 && args[1] == "--town-events-native")
    {
        await ContractSuite.RunTownEventsNativeAsync(Path.GetFullPath(args[0]), Path.GetFullPath(args[2]));
        return;
    }
    if (args.Length == 2 && args[1] == "--town-events")
    {
        await ContractSuite.RunTownEventsOnlyAsync(Path.GetFullPath(args[0]));
        return;
    }
    if (args.Length == 2 && args[1] == "--quantity-native")
    {
        await ContractSuite.RunQuantityNativeOnlyAsync(Path.GetFullPath(args[0]));
        return;
    }
    if (args.Length == 2 && args[1] == "--native-identities")
    {
        await ContractSuite.RunNativeCatalogHashesOnlyAsync(Path.GetFullPath(args[0]));
        return;
    }
    if (args.Length == 2 && args[1] == "--buff-collisions")
    {
        await ContractSuite.RunBuffHashCollisionsOnlyAsync(Path.GetFullPath(args[0]));
        return;
    }
    if (args.Length == 2 && args[1] == "--mod-load-preflight")
    {
        await ContractSuite.RunMissingModLoadOnlyAsync(Path.GetFullPath(args[0]));
        return;
    }
    if (args.Length == 2 && args[1] == "--selection-menu")
    {
        await ContractSuite.RunSelectionAndMenuOnlyAsync(Path.GetFullPath(args[0]));
        return;
    }
    if (args.Length == 2 && args[1] == "--operation-sync")
    {
        await ContractSuite.RunOperationSyncOnlyAsync(Path.GetFullPath(args[0]));
        return;
    }
    if (args.Length == 2 && args[1] == "--native-desktop")
    {
        await ContractSuite.RunDesktopCodecOnlyAsync(Path.GetFullPath(args[0]));
        return;
    }
    if (args.Length == 3 && args[1] == "--native-save-regression")
    {
        await ContractSuite.RunNativeSaveRegressionAsync(Path.GetFullPath(args[0]), Path.GetFullPath(args[2]));
        return;
    }
    if (args.Length == 2 && args[1] == "--native-codec")
    {
        await ContractSuite.RunNativeCodecOnlyAsync(Path.GetFullPath(args[0]));
        return;
    }
    if (args.Length == 2 && args[1] == "--native-all")
    {
        await ContractSuite.RunNativeAllAsync(Path.GetFullPath(args[0]));
        return;
    }
    if (args.Length == 2 && args[1] == "--native-equipment")
    {
        await ContractSuite.RunNativeEquipmentOnlyAsync(Path.GetFullPath(args[0]));
        return;
    }
    if (args.Length == 3 && args[1] == "--native-upload-regression")
    {
        await ContractSuite.RunNativeUploadRegressionAsync(Path.GetFullPath(args[0]), Path.GetFullPath(args[2]));
        return;
    }
    if (args.Length == 2 && args[1] == "--ui-localization")
    {
        await ContractSuite.RunEditorLocalizationOnlyAsync(Path.GetFullPath(args[0]));
        return;
    }
    if (args.Length is < 1 or > 2 || (args.Length == 2 && args[1] is not ("--maintenance" or "--manifests" or "--catalogs" or "--semantics" or "--capacities" or "--map-content" or "--raid-paths" or "--queries" or "--quirk-rules" or "--hero-selection" or "--overlay-slots" or "--canonical-resources" or "--encounter-queries" or "--case-identities" or "--inventory-persistence" or "--loot-references" or "--upgrade-references" or "--reference-consumers" or "--content-sync" or "--quirk-selection" or "--trinket-dependencies" or "--codec-cancellation" or "--codec-json-escaping" or "--save-replacement" or "--catalog-diagnostics")))
    {
        throw new InvalidOperationException("Usage: DarkestDungeonSaveEditor.ContractTests <repository-root> [--maintenance|--manifests|--catalogs|--semantics|--capacities|--map-content|--raid-paths|--queries|--quirk-rules|--hero-selection|--overlay-slots|--canonical-resources|--encounter-queries|--case-identities|--inventory-persistence|--loot-references|--upgrade-references|--reference-consumers|--content-sync|--quirk-selection|--trinket-dependencies|--codec-cancellation|--codec-json-escaping|--save-replacement|--catalog-diagnostics]");
    }

    if (args.Length == 2 && args[1] == "--catalog-diagnostics") await ContractSuite.RunCatalogDiagnosticsOnlyAsync(Path.GetFullPath(args[0]));
    else if (args.Length == 2 && args[1] == "--save-replacement") ContractSuite.RunSaveReplacementOnly(Path.GetFullPath(args[0]));
    else if (args.Length == 2 && args[1] == "--codec-cancellation") await ContractSuite.RunCodecCancellationOnlyAsync(Path.GetFullPath(args[0]));
    else if (args.Length == 2 && args[1] == "--codec-json-escaping") await ContractSuite.RunCodecJsonEscapingOnlyAsync(Path.GetFullPath(args[0]));
    else if (args.Length == 2 && args[1] == "--trinket-dependencies") await ContractSuite.RunTrinketDependenciesOnlyAsync(Path.GetFullPath(args[0]));
    else if (args.Length == 2 && args[1] == "--quirk-selection") await ContractSuite.RunQuirkSelectionOnlyAsync(Path.GetFullPath(args[0]));
    else if (args.Length == 2 && args[1] == "--content-sync") await ContractSuite.RunContentSyncOnlyAsync(Path.GetFullPath(args[0]));
    else if (args.Length == 2 && args[1] == "--reference-consumers") await ContractSuite.RunReferenceConsumersOnlyAsync(Path.GetFullPath(args[0]));
    else if (args.Length == 2 && args[1] == "--upgrade-references") await ContractSuite.RunUpgradeReferencesOnlyAsync(Path.GetFullPath(args[0]));
    else if (args.Length == 2 && args[1] == "--loot-references") await ContractSuite.RunLootReferencesOnlyAsync(Path.GetFullPath(args[0]));
    else if (args.Length == 2 && args[1] == "--inventory-persistence") await ContractSuite.RunInventoryPersistenceOnlyAsync(Path.GetFullPath(args[0]));
    else if (args.Length == 2 && args[1] == "--case-identities") await ContractSuite.RunCaseIdentitiesOnlyAsync(Path.GetFullPath(args[0]));
    else if (args.Length == 2 && args[1] == "--encounter-queries") await ContractSuite.RunEncounterQueriesOnlyAsync(Path.GetFullPath(args[0]));
    else if (args.Length == 2 && args[1] == "--canonical-resources") await ContractSuite.RunCanonicalResourcesOnlyAsync(Path.GetFullPath(args[0]));
    else if (args.Length == 2 && args[1] == "--overlay-slots") await ContractSuite.RunResourceOverlaySlotsOnlyAsync(Path.GetFullPath(args[0]));
    else if (args.Length == 2 && args[1] == "--hero-selection") await ContractSuite.RunHeroSelectionOnlyAsync(Path.GetFullPath(args[0]));
    else if (args.Length == 2 && args[1] == "--quirk-rules") await ContractSuite.RunQuirkRulesOnlyAsync(Path.GetFullPath(args[0]));
    else if (args.Length == 2 && args[1] == "--queries") await ContractSuite.RunFileQueriesOnlyAsync(Path.GetFullPath(args[0]));
    else if (args.Length == 2 && args[1] == "--raid-paths") await ContractSuite.RunNestedRaidSavesOnlyAsync(Path.GetFullPath(args[0]));
    else if (args.Length == 2 && args[1] == "--capacities") ContractSuite.RunInventoryCapacitiesOnly(Path.GetFullPath(args[0]));
    else if (args.Length == 2 && args[1] == "--map-content") await ContractSuite.RunMapContentOnlyAsync(Path.GetFullPath(args[0]));
    else if (args.Length == 2 && args[1] == "--manifests") await ContractSuite.RunManifestsOnlyAsync(Path.GetFullPath(args[0]));
    else if (args.Length == 2 && args[1] == "--semantics") await ContractSuite.RunResourceSemanticsOnlyAsync(Path.GetFullPath(args[0]));
    else if (args.Length == 2 && args[1] == "--catalogs") await ContractSuite.RunAsync(Path.GetFullPath(args[0]), includeBattle: false);
    else if (args.Length == 2) await ContractSuite.RunMaintenanceOnlyAsync(Path.GetFullPath(args[0]));
    else await ContractSuite.RunAsync(Path.GetFullPath(args[0]));
}
catch (Exception error)
{
    Console.Error.WriteLine("FAIL: unhandled contract test exception");
    Console.Error.WriteLine(error);
    Environment.ExitCode = 1;
}
