internal static partial class ContractSuite
{
    private static void VerifyOtherMissingResourceProviders(string root, string kind)
    {
        var baseline = Path.Combine(root, "base");
        var low = Path.Combine(root, "low");
        var high = Path.Combine(root, "high");
        const string quirkPath = "shared/quirk/probe.quirk_library.json";
        const string buffPath = "shared/buffs/probe.buffs.json";
        const string namePath = "localization/probe.string_table.xml";
        const string mashPath = "dungeons/weald/probe.weald.1.mash.darkest";
        const string propPath = "props/probe/trap_definitions.json";
        const string lootPath = "loot/probe.loot.json";
        WriteMultiMash(baseline, "shared/quirk/reference.quirk_library.json",
            """{"quirks":[{"id":"buff_reference","is_positive":true,"random_chance":1,"buffs":["provider_hp"]}]}""");
        foreach (var actor in new[] { "alpha", "beta" })
            WriteMultiMash(baseline, $"monsters/{actor}/{actor}_A/{actor}_A.info.darkest", "display: .size 1\nloot: .code provider_loot\n");
        WriteMultiMash(baseline, "dungeons/weald/weald.props.darkest", "traps: .chance 1 .types low_trap high_trap\n");
        foreach (var (directory, isHigh) in new[] { (low, false), (high, true) })
        {
            WriteMultiMash(directory, quirkPath,
                $$"""{"quirks":[{"id":"provider_quirk","is_positive":{{(isHigh ? "true" : "false")}},"random_chance":1}]}""");
            WriteMultiMash(directory, buffPath,
                $$"""{"buffs":[{"id":"provider_hp","stat_type":"combat_stat_add","stat_sub_type":"max_hp","amount":{{(isHigh ? 5 : 2)}},"rule_type":"always","is_false_rule":false}]}""");
            WriteMultiMash(directory, namePath,
                $"<root><language id=\"english\"><entry id=\"str_inventory_title_estateprobe_token\">{(isHigh ? "High" : "Low")}</entry></language></root>");
            WriteMultiMash(directory, mashPath, $"hall: .chance 1 .types {(isHigh ? "beta_A" : "alpha_A")}\n");
            WriteMultiMash(directory, propPath,
                $$$"""{"props":[{"name":"{{{(isHigh ? "high_trap" : "low_trap")}}}","default_data":{"instance_type":"trap"}}]}""");
            WriteMultiMash(directory, lootPath,
                """{"loot_tables":[{"id":"provider_loot","entries":[{"type":"item","chances":1,"data":{"type":"estate","id":"reference_token","amount":1}}]}]}""");
            WriteMultiMash(directory, "inventory/reference.inventory.items.darkest",
                "inventory_item: .type estate .id reference_token .base_stack_limit 5 .estate_can_be_provision false\n");
            WriteFixtureManifest(directory);
        }
        var sources = new ActiveContentSource[] { new("base", "Base", "base", baseline, 0),
            new("low", "Low", kind, low, 1001), new("high", "High", kind, high, 1000) };
        var content = QueryContent(root, sources);
        var heroes = HeroClassCatalog.Load(content);
        Assert(heroes.InitialQuirks.Single(q => q.Id == "provider_quirk").IsPositive == true &&
            heroes.InitialQuirks.Single(q => q.Id == "buff_reference").MaxHpModifiers.Single().Amount == 5,
            "Quirk and Buff controls must initially use the readable high provider.");
        var names = ContentLocalizationCatalog.Load(content, ["str_inventory_title_estateprobe_token"]);
        Assert(names.GetInventoryItemName("estate", "probe_token").English == "High", "Localization control must load the high provider.");
        var map = new BattleMapSnapshot(content.Profile.ProfileDirectory, "", "", "", "", "weald", 1, 1,
            null, null, null, null, null, null, null, null, null, false, [], [], [], DateTime.UtcNow);
        var battles = BattleEncounterCatalog.Load(content, map);
        Assert(battles.DirectEncounters.Single().MonsterIds.SequenceEqual(["beta_A"]) &&
            BattleEncounterCatalog.ResolveAppendTarget(battles, 0).NextMashIndex == 1,
            "Battle control must use the high formation and its append index.");
        var props = BattleRoomAttachmentCatalog.Load(content, "weald");
        Assert(props.GetCandidates(BattleRoomAttachmentKind.Trap, "weald").Count == 2,
            "Additive prop definition queries retain both explicit Mod providers.");
        var emptyRaid = JsonNode.Parse("""{"base_root":{"party":{"inventory":{"items":{}}}}}""")!.AsObject();
        Assert(QuantityItemCatalog.LoadRaid(content, emptyRaid).Items.Single().ReferenceStatus == QuantityItemReferenceStatus.ConfirmedActive,
            "A readable provider loot table must confirm the Mod item through the actual quantity-catalog consumer.");

        var originalProfileFingerprint = ProfileCatalogContentFingerprint.Capture(sources);
        var originalBattleFingerprint = BattleEncounterCatalog.CaptureContentFingerprint(sources);
        var removedFiles = new[] { quirkPath, buffPath, namePath, mashPath, propPath, lootPath }
            .ToDictionary(path => Path.Combine(high, path), path => File.ReadAllBytes(Path.Combine(high, path)));
        foreach (var path in removedFiles.Keys) File.Delete(path);
        var missingProfileFingerprint = ProfileCatalogContentFingerprint.Capture(sources);
        var missingBattleFingerprint = BattleEncounterCatalog.CaptureContentFingerprint(sources);
        var missingReferences = QuantityItemCatalog.LoadRaid(content, emptyRaid);
        Assert(missingReferences.Items.Single().ReferenceStatus == QuantityItemReferenceStatus.AnalysisIncomplete &&
            missingReferences.DefinitionReadFailures.Count == 0,
            "A missing high loot table must not revive low-Mod active evidence; the item definition itself remains readable.");
        Assert(missingProfileFingerprint != originalProfileFingerprint && missingBattleFingerprint != originalBattleFingerprint &&
            missingProfileFingerprint == ProfileCatalogContentFingerprint.Capture(sources) &&
            missingBattleFingerprint == BattleEncounterCatalog.CaptureContentFingerprint(sources),
            "Missing resources must produce changed but stable profile and battle refresh fingerprints.");
        heroes = HeroClassCatalog.Load(content);
        Assert(heroes.InitialQuirks.All(q => q.Id != "provider_quirk") &&
            heroes.InitialQuirks.Single(q => q.Id == "buff_reference").WriteStatus == HeroInitialQuirkWriteStatus.Unverified &&
            heroes.Issues.Any(i => i.Contains("Failed to read buff")),
            "Missing high quirk/Buff files must not restore low definitions or low HP values.");
        names = ContentLocalizationCatalog.Load(content, ["str_inventory_title_estateprobe_token"]);
        Assert(names.GetInventoryItemName("estate", "probe_token").English != "Low" &&
            names.Issues.Any(i => i.Contains("Failed to read localization")),
            "Missing high localization must not silently present the shadowed low label.");
        var unavailable = BattleEncounterCatalog.Load(content, map);
        Assert(unavailable.DirectEncounters.Count == 0 && unavailable.Issues.Any(i => i.Contains("could not be read")),
            "A missing effective mash keeps an unavailable catalog instead of loading the low formation.");
        ExpectMissingProviderRejection(() => BattleEncounterCatalog.ResolveAppendTarget(unavailable, 0));
        ExpectMissingProviderRejection(() => BattleEncounterCatalog.ValidateDirectEncounter(battles.DirectEncounters.Single()));
        ExpectMissingProviderRejection(() => BattleEncounterCatalog.ReadMaintenanceTable(content, "weald", 1, 0));
        // Additive props cannot silently drop an unreadable explicit provider either.
        ExpectMissingProviderRejection(() => BattleRoomAttachmentCatalog.Load(content, "weald"));
        var diagnostics = new List<string>();
        var files = sources.SelectMany(s => ContentFileDiscovery.EnumerateQuery(s, [], diagnostics, "Loot",
                p => NativeResourceFileRules.IsLootFile(p, [], s.Kind is "local" or "workshop"),
                new ContentFileRule("loot", "*json")).Select(p => new ContentFileCandidate(s, p))).ToArray();
        var effective = NativeContentFileResolver.Resolve(files, sources, "Loot", diagnostics).Single();
        Assert(effective.Source.Id == "high" && !File.Exists(effective.Path),
            "Shared query discovery must preserve a missing manifest slot until native provider resolution.");
        foreach (var (path, bytes) in removedFiles) File.WriteAllBytes(path, bytes);
        Assert(ProfileCatalogContentFingerprint.Capture(sources) == originalProfileFingerprint &&
            BattleEncounterCatalog.CaptureContentFingerprint(sources) == originalBattleFingerprint,
            "Restoring file bytes without changing manifests must restore the original refresh fingerprints.");
        Assert(QuantityItemCatalog.LoadRaid(content, emptyRaid).Items.Single().ReferenceStatus == QuantityItemReferenceStatus.ConfirmedActive,
            "Restoring the loot provider must restore confirmed reference classification.");
        Console.WriteLine($"PASS: {kind} missing quirk/Buff/name/mash/prop/shared-query providers, battle append and maintenance guards.");
    }

    private static void ExpectMissingProviderRejection(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException) { return; }
        throw new InvalidOperationException("An unreadable winning provider was treated as available.");
    }
}
