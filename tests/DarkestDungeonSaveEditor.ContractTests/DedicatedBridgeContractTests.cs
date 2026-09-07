using System.Xml.Linq;
using System.Text.Json;

internal static partial class ContractSuite
{
    private static async Task RunInheritedLegacyBridgeContractsAsync(string runRoot, DsonSaveCodec codec)
    {
        var root = Path.Combine(runRoot, "inherited-legacy-bridge");
        var game = Path.Combine(root, "game");
        var mods = Path.Combine(game, "mods");
        var oldPackage = Path.Combine(mods, "renamed-old-package");
        const string oldTitle = "DDSE Managed Encounter Bridge - profile_other-oldowner";
        const string relativePath = "dungeons/cove/cove.2.mash.darkest";
        WriteMultiMash(game, relativePath, "hall: .chance 1 .types native\nroom: .chance 1 .types native\n");
        WriteMultiMash(game, "dungeons/ruins/ruins.2.mash.darkest", "hall: .chance 1 .types valid\n");
        var oldMash = WriteMultiMash(oldPackage, relativePath,
            "hall: .chance 1 .types native\nroom: .chance 1 .types native\nhall: .chance 0 .types historical\n");
        CreateBattleMonsterDefinitions(game, ["native", "valid", "historical"]);
        File.WriteAllText(Path.Combine(oldPackage, "project.xml"), $"<project><Title>{oldTitle}</Title></project>");
        File.WriteAllText(Path.Combine(oldPackage, "modfiles.txt"), $"{relativePath} {new FileInfo(oldMash).Length}\n");
        File.WriteAllText(Path.Combine(oldPackage, ManagedBattleEncounterBridgeService.ManifestFileName),
            JsonSerializer.Serialize(new
            {
                Version = 3, ProjectTitle = oldTitle, ProfileId = "profile_other", SteamUserId = "old-owner",
                Tables = new[] { new { DungeonId = "cove", Difficulty = 2, RelativeMashPath = relativePath,
                    GeneratedMashSha256 = ComputeSha256(oldMash), ContentFingerprint = "old-sources",
                    Entries = new[] { new { MashType = 0, MashIndex = 1, MonsterIds = new[] { "historical" } } } } }
            }));
        var profile = WriteBattleSafetyProfile(Path.Combine(root, "profile_inherited"));
        var gamePath = Path.Combine(profile.ProfileDirectory, "persist.game.json");
        var configuration = JsonNode.Parse(File.ReadAllText(gamePath))!;
        configuration["base_root"]!["applied_ugcs_1_0"] = new JsonObject
        {
            ["0"] = new JsonObject { ["name"] = oldTitle, ["source"] = "mod_local_source" }
        };
        File.WriteAllText(gamePath, configuration.ToJsonString());
        var mapPath = Path.Combine(profile.ProfileDirectory, "persist.map.json");
        var gameHash = ComputeSha256(gamePath);
        var mapHash = ComputeSha256(mapPath);
        var oldHash = ComputeSha256(oldMash);
        var snapshot = await new BattleMapSnapshotReader(codec).LoadAsync(profile.ProfileDirectory);
        var locations = new SaveEditorLocations(root, Path.Combine(root, "workspaces"), Path.Combine(root, "backups"));
        var content = await ActiveContentResolver.ResolveAsync(profile, game, null, mods, codec, locations.WorkspaceDirectory);
        var catalog = BattleEncounterCatalog.Load(content, snapshot);
        var source = catalog.BridgeEncounters.Single(row => row.OriginDungeonId == "ruins" && row.MashType == 0);
        var service = new ManagedBattleEncounterBridgeService(codec, locations, () => false);
        var error = await CaptureSaveFailureAsync(() => service.EnsureEncounterAsync(profile, snapshot, content, catalog,
            source, game, null, mods));
        Assert(error is InvalidOperationException && error.Message.Contains("启用的托管 Bridge", StringComparison.Ordinal) &&
               ComputeSha256(gamePath) == gameHash && ComputeSha256(mapPath) == mapHash && ComputeSha256(oldMash) == oldHash &&
               !Directory.Exists(Path.Combine(mods, ManagedBattleEncounterBridgeService.GetProjectTitle(profile))),
            "An inherited foreign-owner v3 carrier at a noncanonical folder must block creation before a new index can depend on its copied rows.");
        Console.WriteLine("PASS: inherited or relocated legacy Bridge rejection before package/save writes.");
    }

    private static async Task RunDedicatedBridgeContractsAsync(string runRoot, DsonSaveCodec codec, string repositoryRoot)
    {
        var root = Path.Combine(runRoot, "dedicated-bridge");
        var game = Path.Combine(root, "game");
        var mods = Path.Combine(game, "mods");
        Directory.CreateDirectory(mods);
        var profile = WriteBattleSafetyProfile(Path.Combine(root, "profile"));
        var nativePath = WriteMultiMash(game, "dungeons/cove/cove.2.mash.darkest",
            "hall: .chance 1 .types native\nroom: .chance 1 .types native\nboss: .chance 1 .types native\n");
        var foreignPath = WriteMultiMash(game, "dungeons/ruins/ruins.2.mash.darkest",
            "hall: .chance 1 .types historical lost\nhall: .chance 1 .types valid\n" +
            "room: .chance 1 .types valid\nboss: .chance 1 .types valid\n");
        CreateBattleMonsterDefinitions(game, ["native", "historical", "lost", "valid", "mod_added"]);
        var nativeHash = ComputeSha256(nativePath);
        var foreignHash = ComputeSha256(foreignPath);
        var locations = new SaveEditorLocations(root, Path.Combine(root, "workspaces"), Path.Combine(root, "backups"));
        var snapshot = await new BattleMapSnapshotReader(codec).LoadAsync(profile.ProfileDirectory);
        var content = await ActiveContentResolver.ResolveAsync(profile, game, null, mods, codec, locations.WorkspaceDirectory);
        var catalog = BattleEncounterCatalog.Load(content, snapshot);
        var service = new ManagedBattleEncounterBridgeService(codec, locations, () => false);
        var historical = catalog.BridgeEncounters.Single(row => row.OriginDungeonId == "ruins" && row.MonsterIds[0] == "historical");
        var installed = await service.EnsureEncounterAsync(profile, snapshot, content, catalog, historical, game, null, mods);
        var dedicatedPath = installed.MashFilePath;
        Assert(installed.MashIndex == 1 && File.ReadAllLines(dedicatedPath).Length == 1 &&
               installed.ProjectTitle == "DDSE_Managed_Encounter_Bridge（profile - contract-user）" &&
               Path.GetFileName(installed.PackageDirectory) == installed.ProjectTitle &&
               installed.ActiveContent.Sources.Any(source => source.DisplayName == installed.ProjectTitle &&
                   source.Directory == installed.PackageDirectory),
            "The first dedicated entry must contain only its selected formation at the global tail.");

        // A previously usable monster disappears. Native retains this row's slot.
        File.Delete(Directory.EnumerateFiles(Path.Combine(game, "monsters"), "lost.info.darkest", SearchOption.AllDirectories).Single());
        content = installed.ActiveContent;
        catalog = BattleEncounterCatalog.Load(content, snapshot);
        Assert(catalog.Encounters.Single(row => row.SourcePath == dedicatedPath).MashIndex == 1 &&
               !catalog.Encounters.Single(row => row.SourcePath == dedicatedPath).CanPlaceDirectly,
            "An unavailable historical formation must retain its numeric slot without remaining placeable.");

        foreach (var mashType in new[] { 0, 1, 2 })
        {
            var source = catalog.BridgeEncounters.Single(row => row.OriginDungeonId == "ruins" &&
                row.MashType == mashType && row.MonsterIds.SequenceEqual(["valid"]));
            installed = await service.EnsureEncounterAsync(profile, snapshot, content, catalog, source, game, null, mods);
            Assert(installed.MashFilePath == dedicatedPath && installed.MashIndex == (mashType == 0 ? 2 : 1) &&
                   installed.DirectEncounter.Weight == 0,
                "A missing historical unit must not block a valid new hall, room or boss in the same dedicated file.");
            content = installed.ActiveContent;
            catalog = installed.Catalog;
        }
        var manifest = JsonNode.Parse(File.ReadAllText(installed.ManifestPath))!;
        var previewPath = Path.Combine(installed.PackageDirectory, "preview_icon.png");
        var project = XDocument.Load(Path.Combine(installed.PackageDirectory, "project.xml"));
        Assert(File.Exists(previewPath) && ComputeSha256(previewPath) == ComputeSha256(Path.Combine(repositoryRoot,
                   "src", "DarkestDungeonSaveEditor.App", "Assets", "save-editor-icon.png")) &&
               project.Root!.Element("Title")!.Value == installed.ProjectTitle &&
               project.Root!.Element("PreviewIconFile")!.Value.Replace('/', Path.DirectorySeparatorChar) == previewPath &&
               File.ReadAllLines(Path.Combine(installed.PackageDirectory, "modfiles.txt"))
                   .Contains($"preview_icon.png {new FileInfo(previewPath).Length}"),
            "Generated and refreshed packages must embed the exact application icon, reference its installed path and list its actual size.");
        Assert(manifest["Version"]!.GetValue<int>() == 4 && manifest["Tables"]!.AsArray().Count == 1 &&
               Directory.EnumerateFiles(installed.PackageDirectory, "*.mash.darkest", SearchOption.AllDirectories).Count() == 1 &&
               File.ReadAllLines(dedicatedPath).Length == 4 &&
               catalog.Encounters.Where(row => row.SourcePath == dedicatedPath).All(row => row.Weight == 0) &&
               ComputeSha256(nativePath) == nativeHash && ComputeSha256(foreignPath) == foreignHash,
            "One region/difficulty file must contain only zero-weight Bridge rows, leaving every original source untouched.");
        var valid = catalog.BridgeEncounters.Single(row => row.OriginDungeonId == "ruins" &&
            row.MashType == 0 && row.MonsterIds.SequenceEqual(["valid"]));
        var manifestHash = ComputeSha256(installed.ManifestPath);
        var mashHash = ComputeSha256(dedicatedPath);
        var reused = await service.EnsureEncounterAsync(profile, snapshot, content, catalog, valid, game, null, mods);
        Assert(!reused.EncounterWasAdded && reused.MashIndex == 2 &&
               ComputeSha256(installed.ManifestPath) == manifestHash && ComputeSha256(dedicatedPath) == mashHash,
            "Reusing a valid formation must not duplicate rows or rewrite metadata when another historical entry is unavailable.");
        var mapService = new BattleMapEditService(codec, locations);
        var placement = await mapService.PreparePlaceBattleAsync(profile, snapshot, "coAB", "tile1", reused.DirectEncounter);
        await mapService.CommitAsync(placement);
        snapshot = await new BattleMapSnapshotReader(codec).LoadAsync(profile.ProfileDirectory);
        content = reused.ActiveContent;
        catalog = reused.Catalog;
        var mapPath = Path.Combine(profile.ProfileDirectory, "persist.map.json");
        var gamePath = Path.Combine(profile.ProfileDirectory, "persist.game.json");
        var mapHash = ComputeSha256(mapPath);
        var gameHash = ComputeSha256(gamePath);

        // Even a shadowed lower-priority provider can reserve the dedicated path's slot.
        var collision = WriteMultiMash(game, BattleEncounterCatalog.DedicatedMashPath("cove", 2),
            "hall: .chance 1 .types native\n");
        var collidedCatalog = BattleEncounterCatalog.Load(content, snapshot);
        var collisionError = await CaptureSaveFailureAsync(() => service.EnsureEncounterAsync(profile, snapshot,
            content, collidedCatalog, collidedCatalog.BridgeEncounters.Single(row => row.OriginDungeonId == "ruins" &&
                row.MashType == 0 && row.MonsterIds.SequenceEqual(["valid"])), game, null, mods));
        Assert(collisionError is InvalidOperationException && collisionError.Message.Contains("路径已被其他来源", StringComparison.Ordinal),
            "A shadowed same-path provider must be rejected instead of silently moving the Bridge into an earlier native slot.");
        File.Delete(collision);

        var addedMod = Path.Combine(root, "added-mod");
        WriteMultiMash(addedMod, "dungeons/cove/added.cove.2.mash.darkest", "hall: .chance 1 .types mod_added\n");
        var managedOrder = content.Sources.Single(source => source.Directory == installed.PackageDirectory).LoadOrder;
        var shiftedContent = content with { Sources = [.. content.Sources,
            new ActiveContentSource("local:Added", "Added", "local", addedMod, managedOrder + 1)] };
        var shiftedCatalog = BattleEncounterCatalog.Load(shiftedContent, snapshot);
        var shiftedRow = shiftedCatalog.Encounters.Single(row => row.SourcePath == dedicatedPath && row.MashType == 0 &&
            row.MonsterIds.SequenceEqual(["valid"]));
        var shiftedError = await CaptureSaveFailureAsync(() => service.EnsureEncounterAsync(profile, snapshot,
            shiftedContent, shiftedCatalog, shiftedCatalog.BridgeEncounters.Single(row => row.OriginDungeonId == "ruins" &&
                row.MashType == 0 && row.MonsterIds.SequenceEqual(["valid"])), game, null, mods));
        Assert(shiftedRow.MashIndex == 3 && shiftedError is InvalidOperationException &&
               shiftedError.Message.Contains("已有索引", StringComparison.Ordinal) &&
               snapshot.Areas.Single(area => area.AreaId == "coAB").Tiles.Single(tile => tile.TileId == "tile1").MashIndex == 2,
            "A newly prepended Mod changes the runtime index while the saved tile stays numeric; a stale manifest must block reuse without rewriting that tile.");

        var laterContent = shiftedContent with { Sources = [.. content.Sources,
            new ActiveContentSource("local:Added", "Added", "local", addedMod, managedOrder - 1)] };
        var laterCatalog = BattleEncounterCatalog.Load(laterContent, snapshot);
        var tailError = await CaptureSaveFailureAsync(() =>
        {
            BattleEncounterCatalog.ResolveAppendTarget(laterCatalog, 0, installed.PackageDirectory);
            return Task.CompletedTask;
        });
        Assert(tailError is InvalidOperationException && tailError.Message.Contains("末尾", StringComparison.Ordinal) &&
               ComputeSha256(mapPath) == mapHash && ComputeSha256(gamePath) == gameHash &&
               ComputeSha256(installed.ManifestPath) == manifestHash && ComputeSha256(dedicatedPath) == mashHash,
            "A later provider must prevent an in-place append that would shift its rows; all failed checks must leave save and package bytes unchanged.");

        var otherProfile = WriteBattleSafetyProfile(Path.Combine(root, "other-account", "profile")) with
            { SteamUserId = "another-user" };
        var otherSnapshot = await new BattleMapSnapshotReader(codec).LoadAsync(otherProfile.ProfileDirectory);
        var otherContent = await ActiveContentResolver.ResolveAsync(otherProfile, game, null, mods, codec, locations.WorkspaceDirectory);
        var otherCatalog = BattleEncounterCatalog.Load(otherContent, otherSnapshot);
        var otherSource = otherCatalog.BridgeEncounters.First(row => row.OriginDungeonId == "ruins" &&
            row.MashType == 0 && row.MonsterIds.SequenceEqual(["valid"]));
        var other = await service.EnsureEncounterAsync(otherProfile, otherSnapshot, otherContent, otherCatalog, otherSource, game, null, mods);
        Assert(other.ProjectTitle == "DDSE_Managed_Encounter_Bridge（profile - another-user）" &&
               other.PackageDirectory != installed.PackageDirectory &&
               other.ActiveContent.Sources.All(source => source.Directory != installed.PackageDirectory) &&
               ComputeSha256(installed.ManifestPath) == manifestHash && ComputeSha256(dedicatedPath) == mashHash,
            "Readable names must keep the same profile number in different Steam accounts independently enabled without modifying the other package.");

        manifest["Version"] = 3;
        File.WriteAllText(installed.ManifestPath, manifest.ToJsonString());
        var legacyHash = ComputeSha256(installed.ManifestPath);
        var versionError = await CaptureSaveFailureAsync(() => service.EnsureEncounterAsync(profile, snapshot,
            content, catalog, valid, game, null, mods));
        Assert(versionError is InvalidDataException && versionError.Message.Contains("版本不兼容", StringComparison.Ordinal) &&
               ComputeSha256(installed.ManifestPath) == legacyHash && ComputeSha256(dedicatedPath) == mashHash &&
               ComputeSha256(gamePath) == gameHash && ComputeSha256(mapPath) == mapHash,
            "An old copied-file manifest requires explicit cleanup and must not be silently mixed with version-4 dedicated files.");
        Console.WriteLine("PASS: dedicated Bridge files, unavailable historical slots, zero weights, collisions, tail guards, Mod index drift and explicit version boundaries.");
    }
}
