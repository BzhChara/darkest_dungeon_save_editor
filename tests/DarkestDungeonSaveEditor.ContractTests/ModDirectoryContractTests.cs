using System.Text.Json.Nodes;

internal static partial class ContractSuite
{
    public static async Task RunModDirectoriesOnlyAsync(string repositoryRoot)
    {
        var root = Path.Combine(repositoryRoot, "artifacts", "mod-directories-" + Guid.NewGuid().ToString("N"));
        var game = Path.Combine(root, "game");
        var mods = Path.Combine(game, "mods");
        var workshop = Path.Combine(root, "workshop");
        var profileDirectory = Path.Combine(root, "profile_0");
        Directory.CreateDirectory(profileDirectory);
        WriteMultiMash(mods, "one/project.xml", "<project><Title>Local One</Title></project>");
        WriteMultiMash(workshop, "123/modfiles.txt", "");
        // A same-title project outside the selected root must not become a
        // second provider, including projects left under the DLC directory.
        WriteMultiMash(game, "dlc/loose-project/project.xml", "<project><Title>Local One</Title></project>");
        WriteMultiMash(root, "external/project.xml", "<project><Title>Local One</Title></project>");
        Directory.CreateDirectory(Path.Combine(game, "dlc", "998_test_dlc"));
        var profile = new SaveProfile("profile_0", profileDirectory, "", "contract", DateTime.UtcNow);
        var document = JsonNode.Parse("""{"base_root":{"inraid":false,"raiddungeon":"none","game_mode":"base","applied_ugcs_1_0":{}}}""")!.AsObject();
        var applied = document["base_root"]!["applied_ugcs_1_0"]!.AsObject();
        ActiveContentSnapshot Resolve(string? workshopRoot, string? localRoot)
        {
            var decoded = Path.Combine(root, "game.decoded.json");
            File.WriteAllText(decoded, document.ToJsonString());
            return ActiveContentResolver.ResolveDecoded(profile, game, workshopRoot, localRoot, root, decoded, ComputeSha256(decoded));
        }

        var vanilla = Resolve(null, null);
        ActiveContentResolver.RequireResolvedModSources(vanilla);
        Assert(vanilla.Sources.Count == 1 && vanilla.Resolution!.LocalModDirectory is null,
            "Read-only source resolution must not discover an implicit local root when none is supplied.");
        var emptyGame = Path.Combine(root, "empty-game");
        var unusedMods = Path.Combine(emptyGame, "mods");
        Directory.CreateDirectory(emptyGame);
        var emptyDecodedGame = WriteMultiMash(root, "empty-game.decoded.json", document.ToJsonString());
        ActiveContentSnapshot ResolveUnusedMods() => ActiveContentResolver.ResolveDecoded(profile, emptyGame, null,
            unusedMods, root, emptyDecodedGame, ComputeSha256(emptyDecodedGame));
        ActiveContentResolver.RequireResolvedModSources(ResolveUnusedMods());
        Assert(!Directory.Exists(unusedMods) && !File.Exists(unusedMods),
            "An unused game-local root need not exist yet; source resolution must not create it.");
        File.WriteAllText(unusedMods, "not a directory");
        Exception? filePathFailure = null;
        try { ResolveUnusedMods(); }
        catch (InvalidOperationException error) { filePathFailure = error; }
        Assert(filePathFailure?.Message == EditorText.Format("LocalModDirectory_NotDirectory", unusedMods) &&
            File.ReadAllText(unusedMods) == "not a directory",
            "A regular file occupying the expected local root must be rejected without altering it.");
        document["base_root"]!["dlc"] = new JsonObject { ["0"] = new JsonObject { ["name"] = "test_dlc" } };
        var dlc = Resolve(null, null);
        ActiveContentResolver.RequireResolvedModSources(dlc);
        Assert(dlc.Sources.Any(source => source.Id == "dlc:test_dlc") && dlc.Sources.All(source => source.Kind != "local"),
            "Official DLC resolution must remain independent of local Mod paths.");
        document["base_root"]!.AsObject().Remove("dlc");

        applied["0"] = ModLoadEntry("Local One");
        var unconfiguredLocal = Resolve(null, null);
        var missingLocal = ModLoadFailure(unconfiguredLocal);
        Assert(missingLocal.Contains(EditorText.Format("CatalogLogDiagnostics_UnavailableLocalDirectory", "Local One")),
            "An enabled local Mod requires the selected directory even when game/mods contains it.");
        var local = Resolve(null, mods + Path.DirectorySeparatorChar);
        ActiveContentResolver.RequireResolvedModSources(local);
        Assert(local.Sources.Count == 2 && local.Sources.Single(source => source.Kind == "local").Directory == Path.Combine(mods, "one"),
            "Only the selected local root contributes; DLC/external same-title copies must not create ambiguity.");
        ActiveContentResolver.ValidateSourceBindings(local.Resolution, local.Sources);

        applied["1"] = ModLoadEntry("123", "Steam");
        var missingWorkshop = ModLoadFailure(Resolve(null, mods));
        Assert(missingWorkshop.Contains(EditorText.Format("CatalogLogDiagnostics_UnavailableWorkshopDirectory", "123")),
            "A Workshop directory is required only when an enabled Workshop entry needs it.");
        var both = Resolve(workshop, mods);
        ActiveContentResolver.RequireResolvedModSources(both);
        Assert(both.Sources.Count == 3, "Configured local and Workshop sources resolve together.");
        applied.Remove("0");
        ActiveContentResolver.RequireResolvedModSources(Resolve(workshop, null));
        applied.Clear();

        foreach (var wrongRoot in new[] { Path.Combine(root, "external"), Path.Combine(mods, "one"), Path.Combine(root, "other-game", "mods") })
        {
            Exception? failure = null;
            try { Resolve(null, wrongRoot); }
            catch (InvalidOperationException error) { failure = error; }
            Assert(failure?.Message == EditorText.Format("LocalModDirectory_WrongRoot", mods),
                "External, individual-Mod and other-installation roots must be rejected before scanning.");
        }

        WriteMultiMash(mods, "duplicate/project.xml", "<project><Title>Local One</Title></project>");
        applied["0"] = ModLoadEntry("Local One");
        Assert(ModLoadFailure(Resolve(null, mods)).Contains("Local One"), "Duplicate titles inside the selected root remain unresolved.");
        applied.Clear();

        var codec = DsonSaveCodec.CreateNative();
        var decodedGame = Path.Combine(root, "game.decoded.json");
        File.WriteAllText(decodedGame, document.ToJsonString());
        var binaryGame = Path.Combine(profileDirectory, "persist.game.json");
        await codec.EncodeAsync(decodedGame, binaryGame, null);
        var sourceHash = ComputeSha256(binaryGame);
        var resolved = await ActiveContentResolver.ResolveAsync(profile, game, null, null, codec, Path.Combine(root, "work"));
        ActiveContentResolver.RequireResolvedModSources(resolved);
        var maintenance = new ManagedBattleEncounterBridgeService(codec,
            new SaveEditorLocations(root, Path.Combine(root, "work"), Path.Combine(root, "backups")), () => false);
        var missingMaintenanceRoot = await CaptureSaveFailureAsync(() => maintenance.ReconcileAsync(resolved, game, null));
        Assert(missingMaintenanceRoot is InvalidOperationException && missingMaintenanceRoot.Message ==
            EditorText.Get("LocalModDirectory_Required") && ComputeSha256(binaryGame) == sourceHash,
            "Missing required configuration must reject maintenance, never return a successful no-op or change the save.");
        Assert(!(await maintenance.ReconcileAsync(resolved, game, mods)).Changed && ComputeSha256(binaryGame) == sourceHash,
            "Supplying the game-local root must resume ordinary maintenance without unnecessary writes.");

        // Existing real-workflow guards check names and UI preflight. The Bridge
        // suite covers actual installation, activation, rollback and maintenance.
        await RunMissingModLoadOnlyAsync(repositoryRoot);
        await RunSourceBindingInventoryContractsAsync(Path.Combine(root, "inventory-bindings"), codec);
        await RunSourceBindingBattleContractsAsync(Path.Combine(root, "battle-bindings"), codec);
        Console.WriteLine("PASS: Explicit Mod roots, required maintenance configuration, independent DLC and missing-source load preflight.");
        Console.WriteLine("Artifacts: " + root);
    }
}
