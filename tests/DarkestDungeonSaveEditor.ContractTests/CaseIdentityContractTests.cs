internal static partial class ContractSuite
{
    public static async Task RunCaseIdentitiesOnlyAsync(string repositoryRoot)
    {
        var fixture = BuildContractFixture(repositoryRoot);
        RunInventoryCapacityContracts(fixture.RunRoot);
        await RunCaseIdentityContractsAsync(fixture.RunRoot, fixture.Codec);
        Console.WriteLine($"Artifacts: {fixture.RunRoot}");
    }

    private static async Task RunCaseIdentityContractsAsync(string root, DsonSaveCodec codec)
    {
        await VerifySameManifestCaseAliasesAsync(root, codec);
        await VerifyHeroCaseIdentitiesAsync(root, codec);
        await VerifyMapCaseIdentitiesAsync(root, codec);
        await VerifyBridgeCaseIdentitiesAsync(root, codec);
        await VerifyTerminatedCapacityWritesAsync(root, codec);
    }

    private static async Task VerifyHeroCaseIdentitiesAsync(string runRoot, DsonSaveCodec codec)
    {
        foreach (var kind in new[] { "local", "workshop" })
        {
            var root = Path.Combine(runRoot, "case-hero", kind);
            var f = HeroSelectionContent(root, kind, skills: 1);
            SelectionResource(f, "heroes/selection/selection_A/skin.png", "fixture");
            SelectionResource(f, "heroes/selection/selection.art.darkest",
                "armour: .name coat .hp 21\nquirk_modifier: .incompatible_class_ids ban Ban\n");
            SelectionResource(f, "shared/quirk/case.quirk_library.json",
                """{"quirks":[{"id":"ban","is_positive":true},{"id":"Ban","is_positive":true},{"id":"ok","is_positive":true}]}""");
            var upper = Path.Combine(root, "upper");
            WriteMultiMash(upper, "heroes/Selection/Selection.info.darkest",
                File.ReadAllText(Path.Combine(f.Source, "heroes/selection/selection.info.darkest")));
            WriteMultiMash(upper, "heroes/Selection/Selection.art.darkest", "armour: .name coat .hp 31\n");
            WriteMultiMash(upper, "heroes/Selection/Selection_A/skin.png", "fixture");
            WriteFixtureManifest(upper);
            var content = f.Content with { Sources = f.Content.Sources.Append(new ActiveContentSource(kind + ":upper", "Upper", kind, upper, -1000)).ToArray() };
            var catalog = HeroClassCatalog.Load(content);
            Assert(catalog.HeroClasses.Count == 2 && catalog.HeroClasses.All(h => !h.HasProviderConflict),
                "Canonical case-distinct hero requests must remain separate catalog definitions.");
            var lowerHero = catalog.HeroClasses.Single(h => h.Id == "selection");
            var upperHero = catalog.HeroClasses.Single(h => h.Id == "Selection");
            Assert(lowerHero.BaseHp == 21 && upperHero.BaseHp == 31 && lowerHero.IncompatibleInitialQuirkIds.ToHashSet(StringComparer.Ordinal).SetEquals(["ban", "Ban"]) &&
                upperHero.IncompatibleInitialQuirkIds.Count == 0, "Each class must receive its own art override and exact quirk exclusions.");
            StagecoachHeroCandidateFactory.ValidateInitialQuirkSelection(catalog, lowerHero, 0, ["ok"]);
            foreach (var id in new[] { "ban", "Ban" })
            {
                // Manual console-style assignment intentionally ignores a
                // class's natural-generation exclusion list.
                StagecoachHeroCandidateFactory.ValidateInitialQuirkSelection(catalog, lowerHero, 0, [id]);
                StagecoachHeroCandidateFactory.ValidateInitialQuirkSelection(catalog, upperHero, 0, [id]);
            }
            foreach (var hero in new[] { lowerHero, upperHero })
            {
                var generated = StagecoachHeroCandidateFactory.Generate(catalog, hero, 17, 0, ["ok"]);
                Assert(generated.Candidate["heroClass"]!.GetValue<string>() == hero.Id,
                    "Generation must keep the selected class spelling in the persisted ID.");
                var saveRoot = Path.Combine(root, hero.Id == "selection" ? "lower-save" : "upper-save");
                Directory.CreateDirectory(saveRoot);
                await VerifyProgressionCandidatePersistenceAsync(saveRoot, generated, codec);
            }
        }
        Console.WriteLine("PASS: case-distinct hero catalogs, canonical art overrides, quirk exclusions and generated DSON identities.");
    }

    private static async Task VerifyMapCaseIdentitiesAsync(string runRoot, DsonSaveCodec codec)
    {
        var root = Path.Combine(runRoot, "case-map");
        var profile = WriteBattleSafetyProfile(Path.Combine(root, "profile"));
        var reader = new BattleMapSnapshotReader(codec);
        var snapshot = await reader.LoadAsync(profile.ProfileDirectory);
        var original = JsonSupport.ReadObject(profile.MapSavePath);
        var raid = JsonSupport.ReadObject(profile.RaidSavePath);
        foreach (var (area, tile) in new[] { ("ROOC", "tile0"), ("rooC", "Tile0") })
        {
            var copy = (JsonObject)original.DeepClone();
            Assert(await CaptureSaveFailureAsync(() => {
                BattleMapSaveEditor.DeleteContent(copy, raid, snapshot, area, tile);
                return Task.CompletedTask;
            }) is InvalidOperationException && JsonNode.DeepEquals(copy, original),
                "A differently cased JSON selector cannot borrow an existing map area or tile.");
        }
        var sibling = (JsonObject)original.DeepClone();
        var dynamicAreas = sibling["base_root"]!["map"]!["static_dynamic"]!["areas"]!.AsObject();
        var staticAreas = sibling["base_root"]!["map"]!["static_dynamic"]!["static_save"]!["base_root"]!["areas"]!.AsObject();
        dynamicAreas["rooC"]!["tiles"]!["Tile0"] = dynamicAreas["rooC"]!["tiles"]!["tile0"]!.DeepClone();
        staticAreas["rooC"]!["tiles"]!["Tile0"] = staticAreas["rooC"]!["tiles"]!["tile0"]!.DeepClone();
        dynamicAreas["ROOC"] = dynamicAreas["rooC"]!.DeepClone();
        staticAreas["ROOC"] = staticAreas["rooC"]!.DeepClone();
        staticAreas["ROOC"]!["id"] = 251;
        File.WriteAllText(profile.MapSavePath, sibling.ToJsonString());
        var siblings = await reader.LoadAsync(profile.ProfileDirectory);
        BattleMapSaveEditor.DeleteContent(sibling, raid, siblings, "rooC", "tile0");
        Assert(dynamicAreas["rooC"]!["tiles"]!["tile0"]!["content"]!.GetValue<int>() == 0 &&
            dynamicAreas["rooC"]!["tiles"]!["Tile0"]!["content"]!.GetValue<int>() == 10 &&
            dynamicAreas["ROOC"]!["tiles"]!["tile0"]!["content"]!.GetValue<int>() == 10,
            "An exact map edit must preserve case-distinct sibling areas and tiles.");
        File.WriteAllText(Path.Combine(profile.ProfileDirectory, "persist.game.json"),
            """{"base_root":{"inraid":true,"raiddungeon":"Cove"}}""");
        Assert(await CaptureSaveFailureAsync(async () => {
            using var guard = await BattleMapWriteGuard.LoadAsync(profile, siblings, codec, Path.Combine(root, "guard"), default);
        }) is InvalidOperationException, "Game/raid dungeon case differences must fail the write guard.");
        Console.WriteLine("PASS: exact map JSON selectors, sibling preservation and game/raid region identity guard.");
    }

    private static async Task VerifyTerminatedCapacityWritesAsync(string runRoot, DsonSaveCodec codec)
    {
        foreach (var kind in new[] { "base", "local", "workshop" })
        {
            var root = Path.Combine(runRoot, "case-capacity", kind);
            var resources = Path.Combine(root, "resources");
            string Config(int slots) => $"inventory_system_config: .type raid .max_slots {slots}\ninventory_system_config: .type trinket_storage .max_slots {slots}\n";
            var config = WriteMultiMash(resources, "inventory/a.inventory.system_configs.darkest", Config(2) + "\0" + Config(100));
            WriteMultiMash(resources, "inventory/a.inventory.items.darkest", "inventory_item: .type provision .id food .base_stack_limit 2\n");
            WriteMultiMash(resources, "trinkets/a.entries.trinkets.json", """{"entries":[{"id":"case_ring","rarity":"common","price":100}]}""");
            if (kind != "base") WriteFixtureManifest(resources);
            foreach (var raidScene in new[] { false, true })
            {
                var sceneRoot = Path.Combine(root, raidScene ? "raid" : "town");
                var content = QueryContent(sceneRoot, [new ActiveContentSource(kind, kind, kind, resources, 0)]);
                var profile = content.Profile;
                var gameDecoded = WriteMultiMash(sceneRoot, "game.json", raidScene ?
                    """{"base_root":{"inraid":true,"raiddungeon":"None"}}""" : """{"base_root":{"inraid":false,"raiddungeon":"none"}}""");
                var estate = WriteMultiMash(sceneRoot, "estate.json", """{"base_root":{"version":1,"trinkets":{"items":{}},"estate_items":{"items":{}}}}""");
                var raidSeed = WriteMultiMash(sceneRoot, "raid.json", """{"base_root":{"raid_instance":{"dungeon":"None"},"inbattle":false,"party":{"inventory":{"items":{}}}}}""");
                var gamePath = Path.Combine(profile.ProfileDirectory, "persist.game.json");
                await codec.EncodeAsync(gameDecoded, gamePath, originalBinaryPath: null);
                await codec.EncodeAsync(estate, profile.EstateSavePath, originalBinaryPath: null);
                await codec.EncodeAsync(raidSeed, profile.RaidSavePath, originalBinaryPath: null);
                content = content with { DecodedGamePath = gameDecoded, SourceGameSha256 = ComputeSha256(gamePath) };
                Assert(RaidInventoryStorageCatalog.Load(content).Storage?.MaxSlots == 2 && TrinketStorageCatalog.Load(content).Storage?.MaxSlots == 2,
                    "All source kinds retain capacity before NUL.");
                var service = new SaveEditService(codec, new SaveEditorLocations(sceneRoot, Path.Combine(sceneRoot, "workspaces"), Path.Combine(sceneRoot, "backups")));
                if (raidScene)
                {
                    var food = QuantityItemCatalog.LoadDefinitions(content, QuantityItemSaveContext.Raid).Single(i => i.ItemId == "food");
                    var prepared = await service.PrepareQuantityItemEditAsync(profile, food, 3, content);
                    Assert(await CaptureSaveFailureAsync(() => service.PrepareQuantityItemEditAsync(profile, food, 5, content)) is InvalidOperationException,
                        "An ignored post-NUL larger capacity must not allow extra item stacks.");
                    await service.CommitAsync(prepared);
                    await codec.DecodeAsync(profile.RaidSavePath, raidSeed);
                    Assert(RaidInventorySaveEditor.CountAmount(JsonSupport.ReadObject(raidSeed), food) == 3, "NUL-aware raid quantity survives DSON commit.");
                }
                else
                {
                    var trinkets = TrinketCatalog.Load(content);
                    var ring = trinkets.Trinkets.Single(t => t.Id == "case_ring");
                    var prepared = await service.PrepareTrinketEditAsync(profile, ring, 2, trinkets.Storage, content);
                    Assert(await CaptureSaveFailureAsync(() => service.PrepareTrinketEditAsync(profile, ring, 3, trinkets.Storage, content)) is InvalidOperationException,
                        "An ignored post-NUL capacity must not allow extra trinkets.");
                    await service.CommitAsync(prepared);
                    await codec.DecodeAsync(profile.EstateSavePath, estate);
                    Assert(TrinketSaveEditor.CountCopies(JsonSupport.ReadObject(estate), ring.Id) == 2, "NUL-aware trinket capacity survives DSON commit.");
                }
            }
        }
        Console.WriteLine("PASS: NUL capacity across base/local/workshop, six DSON commits, over-capacity rejection and exact none sentinel.");
    }
}
