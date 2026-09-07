try
{
    if (args.Length is < 1 or > 2 || (args.Length == 2 && args[1] != "--maintenance"))
    {
        throw new InvalidOperationException("Usage: DarkestDungeonSaveEditor.ContractTests <repository-root> [--maintenance]");
    }

    if (args.Length == 2) await ContractSuite.RunMaintenanceOnlyAsync(Path.GetFullPath(args[0]));
    else await ContractSuite.RunAsync(Path.GetFullPath(args[0]));
}
catch (Exception error)
{
    Console.Error.WriteLine("FAIL: unhandled contract test exception");
    Console.Error.WriteLine(error);
    Environment.ExitCode = 1;
}
