try
{
    if (args.Length is < 1 or > 2 || (args.Length == 2 && args[1] is not ("--maintenance" or "--manifests" or "--catalogs" or "--semantics" or "--capacities" or "--map-content" or "--raid-paths" or "--queries" or "--quirk-rules" or "--hero-selection" or "--overlay-slots" or "--canonical-resources" or "--encounter-queries" or "--case-identities" or "--inventory-persistence" or "--loot-references")))
    {
        throw new InvalidOperationException("Usage: DarkestDungeonSaveEditor.ContractTests <repository-root> [--maintenance|--manifests|--catalogs|--semantics|--capacities|--map-content|--raid-paths|--queries|--quirk-rules|--hero-selection|--overlay-slots|--canonical-resources|--encounter-queries|--case-identities|--inventory-persistence|--loot-references]");
    }

    if (args.Length == 2 && args[1] == "--loot-references") await ContractSuite.RunLootReferencesOnlyAsync(Path.GetFullPath(args[0]));
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
