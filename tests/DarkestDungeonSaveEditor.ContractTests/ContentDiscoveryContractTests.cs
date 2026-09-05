internal static partial class ContractSuite
{
    private static void RunContentDiscoveryContracts(ActiveContentSnapshot activeContent, ContractFixture fixture)
    {
        var root = Path.Combine(fixture.RunRoot, "no-manifest-dlc-mod");
        foreach (var file in Directory.EnumerateFiles(fixture.ActiveWorkshopRoot, "*", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(file).Equals("modfiles.txt", StringComparison.OrdinalIgnoreCase) ||
                file.EndsWith(".inventory.system_configs.darkest", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var target = Path.Combine(root, Path.GetRelativePath(fixture.ActiveWorkshopRoot, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }

        void WriteProbe(string relativePath, string text)
        {
            var path = Path.Combine(root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text, new UTF8Encoding(false));
        }

        const string enabled = "dlc/100_feature_pack/features/enabled_feature";
        const string disabled = "dlc/100_feature_pack/features/disabled_feature";
        var localizedProbes = new Dictionary<string, BilingualContentName>
        {
            ["hero_class_name_dlc_shared_hero"] = new("扩展英雄", "DLC Hero"),
            ["str_quirk_name_dlc_top_quirk"] = new("扩展怪癖", "DLC Quirk"),
            ["str_inventory_title_trinketenabled_dlc_trinket"] = new("扩展饰品", "DLC Trinket"),
            ["str_inventory_title_estateenabled_probe"] = new("扩展物品", "DLC Item"),
            ["str_monstername_enabled_probe"] = new("扩展怪物", "DLC Monster")
        };
        foreach (var prefix in new[] { enabled, disabled, "project-backup" })
        {
            var id = prefix == enabled ? "enabled_probe" : "excluded_probe";
            WriteProbe($"{prefix}/inventory/probe.inventory.items.darkest",
                $"inventory_item: .type \"estate\" .id \"{id}\" .base_stack_limit 6 .estate_can_be_provision true");
            WriteProbe($"{prefix}/monsters/{id}/{id}.info.darkest", "tag: .id \"boss\"");
            WriteProbe($"{prefix}/dungeons/probe/probe.1.mash.darkest",
                $"hall: .chance 1 .types {id}\n");
            WriteProbe($"{prefix}/dungeons/probe/probe.props.darkest",
                $"room_curios: .chance 1 .types {id}_curio\n" +
                $"room_treasures: .chance 1 .types {id}_chest\n");
            WriteProbe($"{prefix}/inventory/probe.inventory.system_configs.darkest",
                "inventory_system_config: .type \"trinket_storage\" .max_slots 18\n" +
                "inventory_system_config: .type \"raid\" .max_slots 20 .use_stack_limits true\n");
            WriteProbe($"{prefix}/localization/probe.string_table.xml",
                "<root><language id=\"schinese\">" + string.Concat(localizedProbes.Select(pair =>
                    $"<entry id=\"{pair.Key}\">{(prefix == enabled ? pair.Value.Chinese : "Excluded")}</entry>")) +
                "</language></root>");
            WriteLoc2(Path.Combine(root, prefix, "localization", "probe_english.loc2"),
                localizedProbes.ToDictionary(pair => pair.Key, pair => prefix == enabled ? pair.Value.English : "Excluded"));
        }

        var nestedLocalization = Path.Combine(root, enabled, "localization", "backup");
        Directory.CreateDirectory(nestedLocalization);
        var excludedNames = localizedProbes.ToDictionary(pair => pair.Key, _ => "Excluded");
        WriteLoc2(Path.Combine(nestedLocalization, "probe_english.loc2"), excludedNames);
        WriteLoc2(Path.Combine(root, enabled, "localization", "probe_french.loc2"), excludedNames);

        foreach (var kind in new[] { "workshop", "local" })
        {
            var content = activeContent with
            {
                Sources = activeContent.Sources.Select(source => source.Id == "workshop:111"
                    ? source with { Directory = root, Kind = kind, LoadOrder = -10000 }
                    : source).ToArray()
            };
            var trinkets = TrinketCatalog.Load(content);
            Assert(
                trinkets.Trinkets.Single(item => item.Id == "dlc_shared_trinket").Price == 8800 &&
                trinkets.Trinkets.Single(item => item.Id == "enabled_dlc_trinket").Price == 8200 &&
                trinkets.Trinkets.All(item => item.Id is not ("disabled_mod_trinket" or "backup_trinket")) &&
                trinkets.Storage?.MaxSlots == 18 && RaidInventoryStorageCatalog.Load(content).Storage?.MaxSlots == 20,
                "Manifest-free Mods must discover enabled DLC trinkets and inventory capacities without enabling disabled DLC or backup roots.");
            var heroes = HeroClassCatalog.Load(content);
            var dlcHero = heroes.HeroClasses.Single(hero => hero.Id == "dlc_shared_hero");
            Assert(
                dlcHero.CombatSkillIds.SequenceEqual(["modded_dlc_skill"]) &&
                dlcHero.RuntimeQuirkSignals.Single().QuirkId == "dlc_top_quirk" &&
                dlcHero.RecruitEvents.Single().Count == 6 &&
                heroes.HeroClasses.Single(hero => hero.Id == "enabled_dlc_hero")
                    .CombatSkillIds.SequenceEqual(["modded_enabled_dlc_skill"]) &&
                heroes.HeroClasses.All(hero => hero.Id is not ("disabled_mod_hero" or "backup_hero")),
                "Manifest-free DLC roots must preserve hero, effect, quirk, and town-event overlays.");
            var items = QuantityItemCatalog.Load(content, JsonNode.Parse(File.ReadAllText(fixture.DecodedSeedPath))!.AsObject());
            Assert(items.Items.Any(item => item.ItemId == "enabled_probe") &&
                   items.Items.All(item => item.ItemId != "excluded_probe"),
                "Manifest-free item discovery must scan only root and enabled DLC content directories.");
            var snapshot = new BattleMapSnapshot(
                content.Profile.ProfileDirectory, "", "", "", "", "probe", 1, 1,
                null, null, null, null, null, null, null, null, null, false, [], [], [], DateTime.UtcNow);
            var encounters = BattleEncounterCatalog.Load(content, snapshot);
            var attachments = BattleRoomAttachmentCatalog.Load(content);
            Assert(
                attachments.Curios.Any(item => item.Id == "enabled_probe_curio") &&
                attachments.Treasures.Any(item => item.Id == "enabled_probe_chest") &&
                attachments.Definitions.All(item => item.Id is not ("excluded_probe_curio" or "excluded_probe_chest")),
                "Manifest-free room curios and treasures must include enabled DLC roots without enabling disabled DLC or backup roots.");
            Assert(
                encounters.DirectEncounters.Single().MonsterIds.SequenceEqual(["enabled_probe"]) &&
                encounters.BridgeEncounters.Any(encounter =>
                    encounter.MonsterIds.SequenceEqual(["enabled_probe"]) && encounter.ContainsBossMonster) &&
                encounters.BridgeEncounters.All(encounter => !encounter.MonsterIds.Contains("excluded_probe")),
                "Manifest-free DLC mash files and their monster metadata must be available without scanning disabled/backup encounters.");
            Assert(dlcHero.LocalizedName == localizedProbes["hero_class_name_dlc_shared_hero"] &&
                   heroes.InitialQuirks.Single(quirk => quirk.Id == "dlc_top_quirk").LocalizedName == localizedProbes["str_quirk_name_dlc_top_quirk"] &&
                   trinkets.Trinkets.Single(item => item.Id == "enabled_dlc_trinket").LocalizedName == localizedProbes["str_inventory_title_trinketenabled_dlc_trinket"] &&
                   items.Items.Single(item => item.ItemId == "enabled_probe").LocalizedName == localizedProbes["str_inventory_title_estateenabled_probe"] &&
                   encounters.DirectEncounters.Single().MonsterNames.Single() == localizedProbes["str_monstername_enabled_probe"],
                "Manifest-free enabled DLC XML/loc2 names must reach every catalog while disabled roots, nested loc2 backups, and other languages remain excluded.");
        }

        RunDlcRoomAttachmentOverlayContracts(activeContent, fixture);
    }

    private static void RunDlcRoomAttachmentOverlayContracts(ActiveContentSnapshot activeContent, ContractFixture fixture)
    {
        const string prefix = "dlc/100_feature_pack/features/enabled_feature";
        const string relativePath = "dungeons/probe/probe.props.darkest";
        var root = Path.Combine(fixture.RunRoot, "dlc-room-prop-overlay");
        var builtinRoot = Path.Combine(root, "builtin");
        var modRoot = Path.Combine(root, "mod");
        var builtinPath = Path.Combine(builtinRoot, relativePath);
        var modPath = Path.Combine(modRoot, prefix, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(builtinPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(modPath)!);
        File.WriteAllText(builtinPath, "room_curios: .chance 1 .types original_dlc_curio\n");
        File.WriteAllText(modPath, "room_curios: .chance 1 .types mod_dlc_curio\nroom_treasures: .chance 1 .types mod_dlc_chest\n");
        var content = activeContent with
        {
            Sources =
            [
                new ActiveContentSource("dlc-feature:enabled_feature", "DLC", "dlc-feature", builtinRoot, 0)
                {
                    VirtualPathPrefix = prefix
                },
                new ActiveContentSource("local:prop-probe", "Prop Probe", "local", modRoot, 1)
            ]
        };
        var withoutManifest = BattleRoomAttachmentCatalog.Load(content);
        File.WriteAllText(Path.Combine(modRoot, "modfiles.txt"), $"{prefix}/{relativePath} {new FileInfo(modPath).Length}\n");
        var withManifest = BattleRoomAttachmentCatalog.Load(content);
        Assert(
            withoutManifest.Definitions.Select(item => item.Id).Order().SequenceEqual(["mod_dlc_chest", "mod_dlc_curio"]) &&
            withoutManifest.Definitions.Select(item => item.Id).SequenceEqual(withManifest.Definitions.Select(item => item.Id)) &&
            withoutManifest.Guard.EffectiveFiles.Count == 1 &&
            withoutManifest.Guard.EffectiveFiles.Single().SourceId == "local:prop-probe" &&
            withoutManifest.Guard.Fingerprint == withManifest.Guard.Fingerprint,
            "The same enabled-DLC room prop overlay must replace its builtin provider with or without a manifest, including its write-guard fingerprint.");
    }
}
