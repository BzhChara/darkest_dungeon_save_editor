try
{
    if (args.Length != 1)
    {
        throw new InvalidOperationException("Usage: DarkestDungeonSaveEditor.ContractTests <repository-root>");
    }

    await ContractSuite.RunAsync(Path.GetFullPath(args[0]));
}
catch (Exception error)
{
    Console.Error.WriteLine("FAIL: unhandled contract test exception");
    Console.Error.WriteLine(error);
    Environment.ExitCode = 1;
}
