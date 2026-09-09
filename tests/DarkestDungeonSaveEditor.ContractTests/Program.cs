try
{
    if (args.Length is < 1 or > 2 || (args.Length == 2 && args[1] is not ("--maintenance" or "--manifests" or "--catalogs" or "--semantics" or "--capacities")))
    {
        throw new InvalidOperationException("Usage: DarkestDungeonSaveEditor.ContractTests <repository-root> [--maintenance|--manifests|--catalogs|--semantics|--capacities]");
    }

    if (args.Length == 2 && args[1] == "--capacities") ContractSuite.RunInventoryCapacitiesOnly(Path.GetFullPath(args[0]));
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
