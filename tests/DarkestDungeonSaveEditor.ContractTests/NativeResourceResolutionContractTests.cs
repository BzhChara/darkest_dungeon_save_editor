internal static partial class ContractSuite
{
    private static async Task RunNativeResourceResolutionContractsAsync(string runRoot, DsonSaveCodec codec)
    {
        var root = Path.Combine(runRoot, "native-resource-resolution");
        var game = Path.Combine(root, "game");
        var profile = WriteBattleSafetyProfile(Path.Combine(root, "profile"));
        var mod = Path.Combine(game, "mods", "native-provider");
        WriteMultiMash(mod, "project.xml", "<project><Title>Native Provider</Title></project>");
        var gameSave = Path.Combine(profile.ProfileDirectory, "persist.game.json");
        var config = JsonNode.Parse(File.ReadAllText(gameSave))!;
        config["base_root"]!["applied_ugcs_1_0"]!["0"] = new JsonObject
        {
            ["name"] = "Native Provider", ["source"] = "mod_local_source"
        };
        File.WriteAllText(gameSave, config.ToJsonString());
        WriteMultiMash(game, "inventory/z.inventory.items.darkest",
            "inventory_item: .type estate .id shared .base_stack_limit 2\n");
        WriteMultiMash(mod, "inventory/z.inventory.items.darkest", """
            inventory_item: .type estate .id shared .base_stack_limit 3 .estate_can_be_provision false
            inventory_item: .type estate .id shared .base_stack_limit 7 .estate_can_be_provision true
            """);
        WriteMultiMash(mod, "inventory/a.inventory.items.darkest",
            "inventory_item: .type estate .id shared .base_stack_limit 9\n");
        WriteMultiMash(game, "shared/quirk/z.quirk_library.json",
            """{"quirks":[{"id":"clash","is_positive":true,"random_chance":1}]}""");
        WriteMultiMash(mod, "shared/quirk/a.quirk_library.json", """
            {"quirks":[{"id":"clash","is_positive":false,"random_chance":0,
              "evolution_duration_min":4,"evolution_duration_max":8,"evolution_class_id":"target"},
              {"id":"target","is_positive":true,"random_chance":0}]}
            """);
        WriteMultiMash(game, "heroes/good/good.info.darkest", "armour: .name good_armour_0 .hp 20\n");
        WriteMultiMash(game, "heroes/good/good.override.darkest", "armour: .name good_armour_0 .hp 25\n");
        WriteMultiMash(mod, "heroes/good/good.info.darkest", "armour: .name good_armour_0 .hp 30\n");
        WriteMultiMash(mod, "heroes/archive/good.info.darkest", "armour: .name good_armour_0 .hp 999\n");
        WriteMultiMash(game, "monsters/real/real_A/real_A.info.darkest", "display: .size 1\n");
        WriteMultiMash(mod, "monsters/archive/real_A.info.darkest", "display: .size 4\ntag: .id boss\n");
        WriteMultiMash(mod, "monsters/real/real_A/real_A.art.darkest", """
            /* display: .size 4 */
            display:
              .size 3 .size +2suffix
            """);
        WriteMultiMash(mod, "monsters/archive/ghost_A.info.darkest", "display: .size 4\n");
        WriteMultiMash(game, "monsters/ghost/ghost_A/ghost_A.info.darkest", "display: .size 2\n");
        WriteMultiMash(mod, "monsters/ghost/ghost_A/ghost_A.info.darkest", "display: .size 1\n");
        WriteMultiMash(mod, "monsters/archive/missing_A.info.darkest", "display: .size 4\n");
        WriteMultiMash(game, "trinkets/z.entries.trinkets.json", """
            {"entries":[{"id":"duplicate","price":10},{"id":"duplicate","price":99,"quest_uses":2}]}
            """);
        WriteMultiMash(mod, "trinkets/a.entries.trinkets.json", """
            {"entries":[{"id":"duplicate","price":1000,"trigger_limit":3},
             {"id":"class_ok","hero_class_requirements":["good"]},
             {"id":"class_missing","hero_class_requirements":["good","not_installed"]}]}
            """);
        // A listed decoy does not authorize an unlisted canonical override.
        // The lower, eligible canonical definition must remain effective.
        File.WriteAllLines(Path.Combine(mod, "modfiles.txt"), Directory.EnumerateFiles(mod, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(mod, path).Replace('\\', '/'))
            .Where(path => path != "monsters/ghost/ghost_A/ghost_A.info.darkest"));
        var content = await ActiveContentResolver.ResolveAsync(profile, game, null, codec, Path.Combine(root, "workspace"));
        VerifyNativeDirectoryDiscovery(root, content);
        await VerifyNativeHashCollisionsAsync(root, content, codec);
        var items = QuantityItemCatalog.LoadDefinitions(content, QuantityItemSaveContext.Raid);
        var shared = items.Single(item => item.ItemId == "shared");
        Assert(shared is { BaseStackLimit: 3, EstateCanBeProvision: false, HasProviderConflict: false },
            "Path replacement retains the base slot; native first-match lookup must not pick a later lexical filename or duplicate declaration.");
        var raid = JsonNode.Parse("""{"base_root":{"party":{"inventory":{"items":{}}}}}""")!.AsObject();
        var changed = RaidInventorySaveEditor.SetAmount(raid, shared, 7, 4).UpdatedRoot;
        Assert(changed["base_root"]!["party"]!["inventory"]!["items"]!.AsObject().Select(pair =>
                pair.Value!["amount"]!.GetValue<int>()).SequenceEqual([3, 3, 1]),
            "The native effective stack limit must reach actual serialized stack allocation.");
        var heroes = HeroClassCatalog.Load(content);
        Assert(heroes.HeroClasses.Single(hero => hero.Id == "good").BaseHp == 25,
            "A discovered decoy must not become the hero template; a lower-priority canonical override applies after info.");
        Assert(heroes.InitialQuirks.Single(quirk => quirk.Id == "clash") is
            { IsPositive: false, Evolution: { DurationMin: 4, DurationMax: 8, TargetQuirkId: "target" } },
            "Quirk lookup uses the last loaded definition and its complete evolution metadata.");
        var sizes = BattleEncounterCatalog.ReadMaintenanceMonsterSizes(content.Sources);
        Assert(sizes["real_A"] == 2 && sizes["ghost_A"] == 2 && sizes["missing_A"] == 0,
            "Monster metadata must use canonical info/art, native comments and final integer fields, with zero for an uninitialized native class.");
        WriteMultiMash(game, "monsters/zero/zero_A/zero_A.info.darkest", "display: .size 0\n");
        WriteMultiMash(game, "dungeons/cove/cove.2.mash.darkest", """
            hall: .chance 1 .types missing_A
            hall: .chance 1 .types real_A
            hall: .chance 1 .types zero_A
            """);
        var nativeRows = BattleEncounterCatalog.ReadMaintenanceTable(content, "cove", 2, 0);
        Assert(nativeRows.Select(row => row.MashIndex).SequenceEqual([0, 1, 2]) &&
            nativeRows.Select(row => row.CanPlaceDirectly).SequenceEqual([false, true, true]) &&
            !BattleEncounterCatalog.ReadMaintenanceMonsterSizes(content.Sources, usableOnly: true).ContainsKey("missing_A"),
            "Uninitialized native classes retain their zero-size index slots but are unavailable to placement/maintenance; an authored zero-size class remains usable.");
        var map = await new BattleMapSnapshotReader(codec).LoadAsync(profile.ProfileDirectory);
        var encounters = BattleEncounterCatalog.Load(content, map);
        Assert(encounters.BridgeEncounters.All(row => !row.MonsterIds.Contains("missing_A")) &&
            encounters.DirectEncounters.Any(row => row.MashIndex == 1 && row.MonsterIds.SequenceEqual(["real_A"])),
            "An unusable class must not enter the Bridge source list or block a later valid native entry.");
        var trinkets = TrinketCatalog.Load(content).Trinkets;
        Assert(trinkets.Single(item => item.Id == "duplicate") is
            { Price: 10, IsStateful: false, QuestUses: null, TriggerLimit: null, HasProviderConflict: false } &&
            trinkets.Any(item => item.Id == "class_ok") && trinkets.All(item => item.Id != "class_missing"),
            "Trinket first-match lookup must not union later state fields, and all required hero classes must exist.");
        Console.WriteLine("PASS: native file slots, item/trinket first lookup, quirk last lookup, canonical actors and class requirements.");
    }

    private static void VerifyNativeDirectoryDiscovery(string root, ActiveContentSnapshot content)
    {
        var mod = Path.Combine(root, "directory-device");
        var cases = new[] { ("normal", "normal"), ("_ordinary", "underscore"),
            ("中文", "unicode"), ("copy_template", "template"), (".hidden", "hidden") };
        foreach (var (folder, id) in cases)
        {
            WriteMultiMash(mod, $"inventory/{folder}/a.inventory.items.darkest",
                $"inventory_item: .type estate .id {id} .base_stack_limit 2\n");
            WriteMultiMash(mod, $"trinkets/{folder}/a.entries.trinkets.json",
                "{\"entries\":[{\"id\":\"" + id + "\"}]}");
            WriteMultiMash(mod, $"shared/quirk/{folder}/a.quirk_library.json",
                "{\"quirks\":[{\"id\":\"" + id + "\",\"is_positive\":true,\"random_chance\":0}]}");
            WriteMultiMash(mod, $"monsters/{folder}/{id}_A.info.darkest", "display: .size 4\n");
        }
        var source = new ActiveContentSource("base", "directory-device", "base", mod, 0);
        var snapshot = content with { Sources = [source] };
        var expected = new[] { "normal", "underscore" }.ToHashSet(StringComparer.Ordinal);
        Assert(QuantityItemCatalog.LoadDefinitions(snapshot, QuantityItemSaveContext.Raid)
                .Select(item => item.ItemId).ToHashSet().SetEquals(expected) &&
            TrinketCatalog.Load(snapshot).Trinkets.Select(item => item.Id).ToHashSet().SetEquals(expected) &&
            HeroClassCatalog.Load(snapshot).InitialQuirks.Select(item => item.Id).ToHashSet().SetEquals(expected) &&
            BattleEncounterCatalog.ReadMaintenanceMonsterSizes(snapshot.Sources).Keys.ToHashSet()
                .SetEquals(expected.Select(id => id + "_A")),
            "Physical discovery must omit C-locale conversion failures, dot names and _template components across catalogs, while keeping ordinary underscore paths.");
        Assert(NativeDirectoryDiscovery.IsDiscovered(mod, Path.Combine(mod, "é", "file.json")),
            "The native C locale conversion is a byte-range rule, not an ASCII-only rule.");
        // Manifest discovery does not go through the directory-device conversion.
        // A canonical definition must still be an eligible manifest provider.
        snapshot = snapshot with { Sources = [source with { Id = "local:directory-device", Kind = "local", LoadOrder = 1000 }] };
        WriteMultiMash(mod, "monsters/unicode/unicode_A/unicode_A.info.darkest", "display: .size 2\n");
        File.WriteAllText(Path.Combine(mod, "modfiles.txt"), "monsters/中文/unicode_A.info.darkest 20\n");
        var listed = BattleEncounterCatalog.ReadMaintenanceMonsterSizes(snapshot.Sources);
        Assert(listed.Count == 1 && listed["unicode_A"] == 0,
            "Manifest-listed Unicode paths discover IDs but cannot expose an unlisted canonical override.");
        File.AppendAllText(Path.Combine(mod, "modfiles.txt"), "monsters/unicode/unicode_A/unicode_A.info.darkest 20\n");
        Assert(BattleEncounterCatalog.ReadMaintenanceMonsterSizes(snapshot.Sources)["unicode_A"] == 2,
            "Listing the canonical definition makes its native metadata available.");
    }

    private static async Task VerifyNativeHashCollisionsAsync(string root, ActiveContentSnapshot content, DsonSaveCodec codec)
    {
        Assert(Loc2LocalizationReader.HashName("Az") == 3567 && Loc2LocalizationReader.HashName("BE") == 3567,
            "The collision fixture must exercise actual native hashes.");
        var directory = Path.Combine(root, "hash-collisions");
        WriteMultiMash(directory, "trinkets/hash.entries.trinkets.json",
            """{"entries":[{"id":"Az","quest_uses":2},{"id":"BE"},{"id":"safe"}]}""");
        WriteMultiMash(directory, "inventory/hash.inventory.items.darkest", """
            inventory_item: .type estate .id Az .base_stack_limit 2
            inventory_item: .type estate .id BE .base_stack_limit 9
            inventory_item: .type estate .id safe .base_stack_limit 1
            """);
        WriteMultiMash(directory, "shared/quirk/hash.quirk_library.json", """
            {"quirks":[{"id":"Az","is_positive":true,"random_chance":1},
              {"id":"BE","is_positive":false,"random_chance":1},
              {"id":"safe","is_positive":true,"random_chance":0},
              {"id":"evolves","is_positive":true,"random_chance":0,
               "evolution_duration_min":1,"evolution_duration_max":2,"evolution_class_id":"BE"}]}
            """);
        foreach (var id in new[] { "Az", "BE" })
        {
            WriteMultiMash(directory, $"heroes/{id}/{id}.info.darkest", "armour: .name a .hp 10\n");
            WriteMultiMash(directory, $"monsters/{id}/{id}_A/{id}_A.info.darkest", "display: .size 1\n");
        }
        var snapshot = content with { Sources = [new ActiveContentSource("base", "base", "base", directory, 0)] };
        var items = QuantityItemCatalog.LoadDefinitions(snapshot, QuantityItemSaveContext.Raid);
        var trinkets = TrinketCatalog.Load(snapshot).Trinkets;
        var heroes = HeroClassCatalog.Load(snapshot);
        Assert(items.Where(item => item.ItemId != "safe").All(item => item.HasProviderConflict) &&
            trinkets.Where(item => item.Id != "safe").All(item => item.HasProviderConflict) &&
            !items.Single(item => item.ItemId == "safe").HasProviderConflict &&
            !trinkets.Single(item => item.Id == "safe").HasProviderConflict &&
            heroes.InitialQuirks.Where(quirk => quirk.Id != "safe").All(quirk =>
                quirk.WriteStatus == HeroInitialQuirkWriteStatus.Unverified && !quirk.IsNaturalRandomEligible) &&
            heroes.HeroClasses.All(hero => hero.HasProviderConflict),
            "Hash collisions must block only affected definitions and evolution references, without suppressing unrelated resources.");
        var sizes = BattleEncounterCatalog.ReadMaintenanceMonsterSizes(snapshot.Sources);
        Assert(sizes["Az_A"] is null && sizes["BE_A"] is null,
            "Colliding monster hashes must defer index calculations instead of using independent string-keyed sizes.");
        var locations = new SaveEditorLocations(directory, Path.Combine(directory, "workspaces"), Path.Combine(directory, "backups"));
        var service = new SaveEditService(codec, locations);
        var itemRejected = false;
        var trinketRejected = false;
        try { await service.PrepareQuantityItemEditAsync(content.Profile, items.Single(item => item.ItemId == "BE"), 3, snapshot); }
        catch (InvalidOperationException error) when (error.Message.Contains("unresolved definitions", StringComparison.Ordinal)) { itemRejected = true; }
        try { await service.PrepareTrinketEditAsync(content.Profile, trinkets.Single(item => item.Id == "BE"), 1, null, snapshot); }
        catch (InvalidOperationException error) when (error.Message.Contains("unresolved definitions", StringComparison.Ordinal)) { trinketRejected = true; }
        Assert(itemRejected && trinketRejected && !Directory.Exists(locations.BackupDirectory),
            "Native hash collisions must be rejected by the real preview services before any save mutation or backup.");
    }
}
