internal static partial class ContractSuite
{
    public static async Task RunNestedRaidSavesOnlyAsync(string repositoryRoot)
    {
        var fixture = BuildContractFixture(repositoryRoot);
        await RunNestedRaidSaveContractsAsync(fixture);
        await RunEncounterMaintenanceContractsAsync(fixture.RunRoot, fixture.Codec, nested: true);
        Console.WriteLine($"Artifacts: {fixture.RunRoot}");
    }

    private static SaveProfile MoveRaidToSubdirectory(SaveProfile profile, string relative)
    {
        var gamePath = Path.Combine(profile.ProfileDirectory, "persist.game.json");
        var game = JsonNode.Parse(File.ReadAllText(gamePath))!.AsObject();
        game["base_root"]!["raid_save"] = relative;
        var location = RaidSaveLocation.FromGame(profile.ProfileDirectory, game);
        Directory.CreateDirectory(Path.GetDirectoryName(location.MapPath)!);
        File.Move(profile.MapSavePath, location.MapPath);
        File.Move(profile.RaidSavePath, location.RaidPath);
        File.WriteAllText(gamePath, game.ToJsonString());
        return location.Bind(profile);
    }

    private static async Task RunNestedRaidSaveContractsAsync(ContractFixture fixture)
    {
        var root = Path.Combine(fixture.RunRoot, "nested-raid");
        var profile = WriteBattleSafetyProfile(Path.Combine(root, "profile"));
        var codec = fixture.Codec;
        foreach (var pair in new[] { (fixture.DecodedSeedPath, "persist.estate.json"),
            (fixture.DecodedTownSeedPath, "persist.town.json"), (fixture.DecodedRosterSeedPath, "persist.roster.json"),
            (fixture.DecodedUpgradesSeedPath, "persist.upgrades.json") })
            File.Copy(pair.Item1, Path.Combine(profile.ProfileDirectory, pair.Item2), overwrite: true);
        var gamePath = Path.Combine(profile.ProfileDirectory, "persist.game.json");
        var raid = JsonNode.Parse(File.ReadAllText(profile.RaidSavePath))!.AsObject();
        raid["base_root"]!["start_elapsed_time"] = 123;
        raid["base_root"]!["party"]!["inventory"] = JsonNode.Parse("""{"items":{"0":{"id":"","type":"gold","amount":25}}}""");
        File.WriteAllText(profile.RaidSavePath, raid.ToJsonString());
        var rootMapBytes = File.ReadAllBytes(profile.MapSavePath);
        var rootRaidBytes = File.ReadAllBytes(profile.RaidSavePath);
        profile = MoveRaidToSubdirectory(profile, "plot_crimson_court_1/");
        var basicProfile = profile with { RaidSaveRelativeDirectory = string.Empty };
        // Same-name leftovers must not override the explicit active expedition.
        File.WriteAllBytes(basicProfile.MapSavePath, rootMapBytes);
        File.WriteAllBytes(basicProfile.RaidSavePath, rootRaidBytes);
        foreach (var path in new[] { gamePath, profile.RaidSavePath, profile.MapSavePath })
        {
            var binary = path + ".encoded";
            await codec.EncodeAsync(path, binary, null);
            File.Copy(binary, path, overwrite: true);
        }
        var locations = new SaveEditorLocations(root, Path.Combine(root, "workspaces"), Path.Combine(root, "backups"));
        async Task<ActiveContentSnapshot> Content() => await ActiveContentResolver.ResolveAsync(basicProfile,
            fixture.GameRoot, null, codec, locations.WorkspaceDirectory);
        var content = await Content();
        var quantities = await QuantityItemCatalog.LoadAsync(content, codec);
        Assert(content.Profile.RaidSavePath == profile.RaidSavePath && quantities.SaveContext == QuantityItemSaveContext.Raid &&
            quantities.Items.Single(item => item.DisplayId == "gold").CurrentAmount == 25,
            "DSON raid_save must select the nested expedition despite same-name root leftovers.");
        var sync = new ProfileCatalogSnapshotReader(content, quantities, codec, fixture.GameRoot, null, null, root);
        var first = await sync.ReadAsync();
        Assert(string.Equals(first.FileHashes["persist.raid.json"], ComputeSha256(profile.RaidSavePath), StringComparison.OrdinalIgnoreCase),
            "Catalog snapshots must hash the selected expedition, not the root leftover.");
        var service = new SaveEditService(codec, locations);
        var gold = quantities.Items.Single(item => item.DisplayId == "gold");
        var quantityEdit = await service.PrepareQuantityItemEditAsync(basicProfile, gold, 35, content);
        var quantityResult = await service.CommitAsync(quantityEdit);
        Assert(quantityResult.TargetPath == profile.RaidSavePath &&
            File.ReadAllBytes(basicProfile.RaidSavePath).SequenceEqual(rootRaidBytes) &&
            File.Exists(Path.Combine(quantityResult.BackupDirectory, "plot_crimson_court_1", "persist.raid.json")),
            "Quantity commit and backup must target the selected child folder and preserve root data.");
        var syncedQuantity = await sync.ReadAsync();
        Assert(syncedQuantity.QuantityItems.Items.Single(item => item.DisplayId == "gold").CurrentAmount == 35,
            "Nested inventory changes must refresh displayed quantities.");

        using (var monitor = new ProfileSaveMonitor(profile.ProfileDirectory, ProfileCatalogSnapshotReader.WatchedFileNames,
            TimeSpan.FromHours(1), TimeSpan.FromHours(1)))
        {
            var changed = new HashSet<string>();
            monitor.Changed += (_, args) => changed.UnionWith(args.FileNames);
            monitor.Start();
            File.SetLastWriteTimeUtc(profile.MapSavePath, File.GetLastWriteTimeUtc(profile.MapSavePath).AddSeconds(3));
            monitor.PollNow();
            Assert(changed.Contains("persist.map.json"), "Fallback polling must detect nested map writes without relying on OS events.");
        }
        // A fresh watcher keeps delayed events from the preceding map write out of this negative case.
        using (var monitor = new ProfileSaveMonitor(profile.ProfileDirectory, ProfileCatalogSnapshotReader.WatchedFileNames,
            TimeSpan.FromHours(1), TimeSpan.FromHours(1)))
        {
            var changed = new HashSet<string>();
            monitor.Changed += (_, args) => changed.UnionWith(args.FileNames);
            monitor.Start();
            var backup = Path.Combine(profile.ProfileDirectory, "backup");
            Directory.CreateDirectory(backup);
            File.WriteAllText(Path.Combine(backup, "persist.map.json"), "ignored backup copy");
            monitor.PollNow();
            Assert(changed.Count == 0, "Game backup folders must not look like active map saves.");
        }

        var reader = new BattleMapSnapshotReader(codec);
        var map = await reader.LoadAsync(profile.ProfileDirectory);
        VerifyBattleMapLogContracts(map);
        var mapService = new BattleMapEditService(codec, locations);
        var townService = new ForceTownSaveService(codec, locations);
        var staleDelete = await mapService.PrepareDeleteContentAsync(basicProfile, map, "coAB", "tile1");
        var staleTown = await townService.PrepareAsync(basicProfile, map);
        var staleQuantity = await service.PrepareQuantityItemEditAsync(basicProfile, gold, 40, content);
        var gameBytes = File.ReadAllBytes(gamePath);
        var game = JsonNode.Parse(File.ReadAllText(content.DecodedGamePath))!.AsObject();
        game["base_root"]!["raid_save"] = "mod_region/episode_2/";
        var other = RaidSaveLocation.FromGame(profile.ProfileDirectory, game);
        Directory.CreateDirectory(Path.GetDirectoryName(other.MapPath)!);
        File.Copy(profile.MapSavePath, other.MapPath);
        File.Copy(profile.RaidSavePath, other.RaidPath);
        File.WriteAllText(gamePath, game.ToJsonString());
        var switched = await sync.ReadAsync();
        Assert(switched.FileHashes["persist.map.json"] == syncedQuantity.FileHashes["persist.map.json"] &&
            switched.Content.Profile.MapSavePath == other.MapPath &&
            !ProfileCatalogSnapshotReader.HashesEqual(switched.FileHashes, syncedQuantity.FileHashes),
            "A directory switch is a new snapshot even when both expedition files are byte-identical.");
        _ = await CaptureSaveFailureAsync(() => mapService.CommitAsync(staleDelete));
        _ = await CaptureSaveFailureAsync(() => townService.CommitAsync(staleTown));
        _ = await CaptureSaveFailureAsync(() => service.CommitAsync(staleQuantity));
        _ = await CaptureSaveFailureAsync(async () => { _ = await mapService.PrepareDeleteContentAsync(basicProfile, map, "coAB", "tile1"); });
        Assert(ComputeSha256(other.MapPath).Equals(map.MapSha256, StringComparison.OrdinalIgnoreCase) &&
            ComputeSha256(profile.MapSavePath).Equals(map.MapSha256, StringComparison.OrdinalIgnoreCase),
            "Stale previews may not write either expedition after raid_save changes.");
        File.WriteAllBytes(gamePath, gameBytes);

        await VerifyServiceReplacementRacesAsync(staleDelete.TargetFile.TargetPath, async (before, after) =>
        {
            mapService.BeforeTargetReplace = before;
            mapService.AfterTargetReplace = after;
            try { await mapService.CommitAsync(staleDelete); }
            finally { mapService.BeforeTargetReplace = mapService.AfterTargetReplace = null; }
        });
        var deleted = await mapService.CommitAsync(staleDelete);
        Assert(deleted.TargetPath == profile.MapSavePath && DsonSaveCodec.IsDson(profile.MapSavePath) &&
            File.ReadAllBytes(basicProfile.MapSavePath).SequenceEqual(rootMapBytes),
            "Map delete must preserve nested DSON format and leave the root map untouched.");
        var markerPath = Path.Combine(deleted.BackupDirectory, "commit-result.json");
        var markerBytes = File.ReadAllBytes(markerPath);
        var marker = JsonNode.Parse(markerBytes)!.AsObject();
        Assert(SaveCommitMarker.IsComplete(markerPath, profile.ProfileDirectory), "Nested map commit markers must be recognized as complete.");
        foreach (var target in new[] { Path.Combine(root, "outside", "persist.map.json"),
            Path.Combine(profile.ProfileDirectory, "backup", "persist.map.json"),
            Path.Combine(profile.ProfileDirectory, "mod_region", "persist.estate.json") })
        {
            marker["TargetPath"] = target;
            File.WriteAllText(markerPath, marker.ToJsonString());
            Assert(!SaveCommitMarker.IsComplete(markerPath, profile.ProfileDirectory),
                "Allowing nested expeditions must not allow outside, backup, or nested town-file targets.");
        }
        File.WriteAllBytes(markerPath, markerBytes);
        map = await reader.LoadAsync(profile.ProfileDirectory);
        var moved = await mapService.CommitAsync(await mapService.PrepareMovePartyAsync(basicProfile, map, "rooB", "tile0"));
        Assert(moved.TargetPath == profile.RaidSavePath, "Party movement must update the active nested raid.");
        map = await reader.LoadAsync(profile.ProfileDirectory);
        var raidHash = ComputeSha256(profile.RaidSavePath);
        var mapHash = ComputeSha256(profile.MapSavePath);
        var townResult = await townService.CommitAsync(await townService.PrepareAsync(basicProfile, map));
        var decodedTown = Path.Combine(root, "town-game.json");
        await codec.DecodeAsync(gamePath, decodedTown);
        var townGame = JsonNode.Parse(File.ReadAllText(decodedTown))!;
        Assert(townGame["base_root"]!["inraid"]!.GetValue<bool>() == false &&
            townGame["base_root"]!["raid_save"]!.GetValue<string>() == "plot_crimson_court_1/" &&
            ComputeSha256(profile.RaidSavePath) == raidHash && ComputeSha256(profile.MapSavePath) == mapHash &&
            File.Exists(Path.Combine(townResult.BackupDirectory, "plot_crimson_court_1", "persist.map.json")) &&
            File.Exists(Path.Combine(townResult.BackupDirectory, "mod_region", "episode_2", "persist.raid.json")) &&
            !Directory.Exists(Path.Combine(townResult.BackupDirectory, "backup")),
            "Force-town must preserve persistent expedition files and raid_save; full backups retain relative paths, excluding backup copies.");
        Assert((await sync.ReadAsync()).QuantityItems.SaveContext == QuantityItemSaveContext.Town,
            "Force-town with nested residue must synchronize back to town quantities.");

        foreach (var invalid in new[] { "../escape/", "C:/outside/", "/root/", "\\\\server\\share", "a/../b", "a//b", "a:stream", "a./", "a /" })
        {
            game["base_root"]!["raid_save"] = invalid;
            var rejected = false;
            try { _ = RaidSaveLocation.FromGame(profile.ProfileDirectory, game); }
            catch (InvalidDataException) { rejected = true; }
            Assert(rejected, $"Unsafe raid_save must be rejected: {invalid}");
        }
        game["base_root"]!["raid_save"] = 1;
        var wrongType = false;
        try { _ = RaidSaveLocation.FromGame(profile.ProfileDirectory, game); }
        catch (InvalidDataException) { wrongType = true; }
        Assert(wrongType, "Wrong-typed raid_save must not fall back to the root.");
        game["base_root"]!["raid_save"] = "missing/";
        File.WriteAllText(gamePath, game.ToJsonString());
        var missingContent = await Content();
        _ = await CaptureSaveFailureAsync(() => QuantityItemCatalog.LoadAsync(missingContent, codec));
        _ = await CaptureSaveFailureAsync(() => reader.LoadAsync(profile.ProfileDirectory));
        game["base_root"]!["raid_save"] = "linked/";
        var link = Path.Combine(profile.ProfileDirectory, "linked");
        Directory.CreateSymbolicLink(link, Path.GetDirectoryName(other.MapPath)!);
        var linked = false;
        try { _ = RaidSaveLocation.FromGame(profile.ProfileDirectory, game); }
        catch (InvalidDataException) { linked = true; }
        Assert(linked, "A directory link cannot redirect expedition writes even when its target is inside the profile.");
        Directory.Delete(link);
        Console.WriteLine("PASS: persistent raid directories, DSON inventory/map writes, rollback, stale-route guards, sync, backup, force-town and unsafe paths.");
    }
}
