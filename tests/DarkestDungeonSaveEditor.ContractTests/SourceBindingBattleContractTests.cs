internal static partial class ContractSuite
{
    private static void RunSourceBindingMountContracts(string runRoot)
    {
        foreach (var kind in new[] { "mode", "dlc", "workshop" })
        {
            var root = Path.Combine(runRoot, "binding-mounts", kind);
            var game = Path.Combine(root, "game");
            Directory.CreateDirectory(game);
            var path = WriteMultiMash(root, "profile/persist.game.json", """
                {"base_root":{"game_mode":"probe_mode","dlc":{"0":{"name":"probe_dlc"}},
                 "applied_ugcs_1_0":{"0":{"name":"1","source":"Steam"}}}}
                """);
            var profile = new SaveProfile("binding", Path.GetDirectoryName(path)!, Path.Combine(root, "profile/persist.estate.json"), "contract", DateTime.UtcNow);
            var content = ActiveContentResolver.ResolveDecoded(profile, game, Path.Combine(root, "workshop"), null, root, path, ComputeSha256(path));
            ActiveContentResolver.ValidateSourceBindings(content.Resolution, content.Sources);
            var mount = kind == "mode" ? Path.Combine(game, "modes/probe_mode") : kind == "dlc"
                ? Path.Combine(game, "dlc/probe_dlc") : Path.Combine(root, "workshop/1");
            Directory.CreateDirectory(mount);
            var rejected = false;
            try { ActiveContentResolver.ValidateSourceBindings(content.Resolution, content.Sources); }
            catch (InvalidOperationException) { rejected = true; }
            Assert(rejected, "Installing an already enabled mount changes the real source mapping: " + kind);
        }
        Console.WriteLine("PASS: newly installed enabled mode, DLC and Workshop sources invalidate prior mappings.");
    }

    private static async Task RunSourceBindingBattleContractsAsync(string runRoot, DsonSaveCodec codec)
    {
        RunSourceBindingMountContracts(runRoot);
        foreach (var scenario in new[] { "unchanged", "description", "rename-after-preview", "remap-before-prepare", "duplicate-after-preview", "manifest-after-preview" })
        {
            var root = Path.Combine(runRoot, scenario);
            var game = Path.Combine(root, "game");
            var mod = Path.Combine(game, "mods", "provider");
            var profile = WriteBattleSafetyProfile(Path.Combine(root, "profile"));
            var gamePath = Path.Combine(profile.ProfileDirectory, "persist.game.json");
            var gameDoc = JsonSupport.ReadObject(gamePath);
            gameDoc["base_root"]!["applied_ugcs_1_0"] = JsonNode.Parse("""{"0":{"name":"Binding Provider","source":"mod_local_source"}}""");
            File.WriteAllText(gamePath, gameDoc.ToJsonString());
            var raid = JsonSupport.ReadObject(profile.RaidSavePath);
            raid["base_root"]!["start_elapsed_time"] = 100;
            File.WriteAllText(profile.RaidSavePath, raid.ToJsonString());
            var binary = Path.Combine(root, "map.binary.json");
            await codec.EncodeAsync(profile.MapSavePath, binary, null);
            File.Copy(binary, profile.MapSavePath, true);
            CreateBattleMonsterDefinitions(game, ["native", "mod_unit"]);
            WriteMultiMash(game, "dungeons/cove/cove.2.mash.darkest", "hall: .chance 1 .types native\n");
            WriteMultiMash(mod, "dungeons/cove/cove.2.mash.darkest", "hall: .chance 1 .types mod_unit\n");
            WriteMultiMash(mod, "dungeons/weald/weald.2.mash.darkest", "hall: .chance 1 .types mod_unit\n");
            WriteMapCurioFixtures(mod, "cove", "binding_curio");
            WriteMultiMash(mod, "dungeons/cove/cove.props.darkest", "room_curios: .chance 1 .types binding_curio\n");
            WriteMultiMash(mod, "project.xml", "<project><Title>Binding Provider</Title></project>");
            WriteFixtureManifest(mod);
            var locations = new SaveEditorLocations(root, Path.Combine(root, "work"), Path.Combine(root, "backups"));
            var content = await ActiveContentResolver.ResolveAsync(profile, game, null, null, codec, locations.WorkspaceDirectory);
            var snapshot = await new BattleMapSnapshotReader(codec).LoadAsync(profile.ProfileDirectory);
            var catalog = BattleEncounterCatalog.Load(content, snapshot);
            var selected = catalog.DirectEncounters.Single(x => x.MashType == 0 && x.MashIndex == 0);
            var bridge = catalog.BridgeEncounters.Single(x => x.OriginDungeonId == "weald" && x.MashType == 0);
            var attachment = BattleRoomAttachmentCatalog.Load(content).Curios.Single(x => x.Id == "binding_curio");
            var service = new BattleMapEditService(codec, locations);
            void Mutate()
            {
                if (scenario == "description") WriteMultiMash(mod, "project.xml", "<project><Title>Binding Provider</Title><Description>changed</Description></project>");
                if (scenario is "rename-after-preview" or "remap-before-prepare")
                {
                    WriteMultiMash(mod, "project.xml", "<project><Title>Retired</Title></project>");
                    if (scenario == "remap-before-prepare")
                    {
                        var replacement = Path.Combine(game, "mods", "replacement");
                        WriteMultiMash(replacement, "project.xml", "<project><Title>Binding Provider</Title></project>");
                        WriteMultiMash(replacement, "modfiles.txt", "");
                    }
                }
                if (scenario == "duplicate-after-preview") WriteMultiMash(Path.Combine(game, "mods", "duplicate"), "project.xml", "<project><Title>Binding Provider</Title></project>");
                if (scenario == "manifest-after-preview") WriteMultiMash(mod, "modfiles.txt", "");
            }
            var original = ComputeSha256(profile.MapSavePath);
            if (scenario == "remap-before-prepare")
            {
                Mutate();
                var fresh = await ActiveContentResolver.ResolveAsync(profile, game, null, null, codec, locations.WorkspaceDirectory);
                var maintenance = await new ManagedBattleEncounterBridgeService(codec, locations).ReconcileAsync(fresh, game, null);
                Assert(!maintenance.Changed && !maintenance.Deferred, "The fresh mapping has no history requiring maintenance.");
            }
            Exception? error = null;
            try
            {
                var prepared = await service.PreparePlaceBattleAsync(profile, snapshot, "coAB", "tile1", selected);
                if (scenario != "remap-before-prepare") Mutate();
                await service.CommitAsync(prepared);
            }
            catch (InvalidOperationException ex) { error = ex; }
            var blocked = scenario is not ("unchanged" or "description");
            Assert((error is not null) == blocked && (!blocked || ComputeSha256(profile.MapSavePath) == original),
                "Direct battle stale bindings must reject before any live map replacement: " + scenario);
            foreach (var validate in new Action[] { () => BattleEncounterCatalog.ValidateBridgeEncounter(bridge),
                         () => BattleRoomAttachmentCatalog.ValidateDefinition(attachment) })
            {
                var rejected = false;
                try { validate(); } catch (InvalidOperationException) { rejected = true; }
                Assert(rejected == blocked, "Bridge and room attachment candidates must use the same binding guard: " + scenario);
            }
            await codec.DecodeAsync(profile.MapSavePath, Path.Combine(root, "final.map.decoded.json"));
            Console.WriteLine("PASS: battle/Bridge/attachment bindings " + scenario);
        }
    }

    private static async Task RunSourceBindingHeroContractsAsync(ContractFixture f)
    {
        var root = Path.Combine(f.RunRoot, "hero-bindings");
        var profile = new SaveProfile("hero_binding", f.ProfileRoot, f.EstatePath, "contract", DateTime.UtcNow);
        var source = Path.Combine(f.LocalModRoot, "project.xml");
        var xml = File.ReadAllText(source);
        var replacement = Path.Combine(f.AdditionalLocalModDirectory, "binding-replacement", "project.xml");
        var originals = new[] { f.TownSavePath, f.RosterSavePath, f.UpgradesSavePath }.ToDictionary(p => p, File.ReadAllBytes);
        foreach (var scenario in new[] { "unchanged", "description", "rename-before-preview", "rename-after-preview", "remap-after-preview", "duplicate-after-preview" })
        {
            try
            {
                var content = await ActiveContentResolver.ResolveAsync(profile, f.GameRoot, f.WorkshopRoot, f.AdditionalLocalModDirectory, f.Codec, root);
                var catalog = HeroClassCatalog.Load(content);
                var hero = StagecoachHeroCandidateFactory.Generate(catalog, catalog.HeroClasses.Single(h => h.Id == "local_hero"), 1729);
                var service = new SaveEditService(f.Codec, new(root, Path.Combine(root, scenario), Path.Combine(root, "backups")));
                void Mutate()
                {
                    if (scenario == "description") File.WriteAllText(source, xml.Replace("</project>", "<Description>changed</Description></project>", StringComparison.Ordinal));
                    if (scenario.StartsWith("rename") || scenario.StartsWith("remap")) File.WriteAllText(source, "<project><Title>Retired hero</Title></project>");
                    if (scenario.StartsWith("remap") || scenario.StartsWith("duplicate"))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(replacement)!);
                        File.WriteAllText(replacement, xml);
                    }
                }
                if (scenario == "rename-before-preview") Mutate();
                var blocked = false;
                try
                {
                    var prepared = await service.PrepareStagecoachHeroEditAsync(profile, hero, catalog, content);
                    if (scenario != "rename-before-preview") Mutate();
                    await service.CommitAsync(prepared);
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("来源映射", StringComparison.Ordinal)) { blocked = true; }
                Assert(blocked == (scenario is not ("unchanged" or "description")) &&
                    (!blocked || originals.All(p => p.Value.SequenceEqual(File.ReadAllBytes(p.Key)))),
                    "Hero binding guards must preserve all three live targets: " + scenario);
                Console.WriteLine("PASS: hero binding " + scenario);
            }
            finally
            {
                File.WriteAllText(source, xml);
                if (File.Exists(replacement)) File.Delete(replacement);
                foreach (var pair in originals) File.WriteAllBytes(pair.Key, pair.Value);
            }
        }
    }
}
