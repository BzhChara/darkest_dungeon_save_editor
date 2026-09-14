internal static partial class ContractSuite
{
    private sealed record TrinketCatalogContractState(
        TrinketCatalogResult ActiveCatalog,
        TrinketDefinition ActiveWorkshopTrinket,
        TrinketDefinition AmbiguousTrinket,
        TrinketDefinition DefinitionDrivenStateful,
        TrinketDefinition DualCounterStateful,
        ActiveContentSnapshot InvalidCapacityContent,
        TrinketDefinition ZeroCounterStateful,
        TrinketDefinition Ordinary,
        TrinketDefinition Stateful,
        TrinketDefinition TriggerStateful,
        TrinketDefinition Unlimited);

    private static TrinketCatalogContractState RunTrinketCatalogContracts(
        ActiveContentSnapshot activeContent,
        string runRoot,
        string activeWorkshopInventoryRoot)
    {
        var activeCatalog = TrinketCatalog.Load(activeContent);
        var noTrinketDlcRoot = Path.Combine(runRoot, "empty_enabled_dlc");
        Directory.CreateDirectory(noTrinketDlcRoot);
        var catalogWithEmptyDlc = TrinketCatalog.Load(activeContent with
        {
            Sources = activeContent.Sources
                .Append(new ActiveContentSource("dlc:empty", "empty", "dlc", noTrinketDlcRoot, 999))
                .ToArray()
        });
        Assert(
            catalogWithEmptyDlc.Trinkets.Count == activeCatalog.Trinkets.Count,
            "An enabled DLC source without a trinkets directory should be ignored without failing the catalog.");
        Assert(activeCatalog.Trinkets.Count == 16, "Active catalog should contain base, stateful fixtures, enabled DLC package/feature, active Workshop, default/extra local Mods, provenance probes, and two unresolved native-hash collisions.");
        var activeWorkshopTrinket = activeCatalog.Trinkets.Single(item => item.Id == "active_workshop_trinket");
        Assert(
            activeWorkshopTrinket.LocalizedName == new BilingualContentName("编译工坊饰品", "Compiled Workshop Trinket"),
            "A manifest-listed LOC2 pair should provide bilingual Workshop trinket names.");
        Assert(activeCatalog.Trinkets.Any(item => item.Id == "local_mod_trinket"), "Enabled local Mod trinket is missing.");
        var externalLocalTrinket = activeCatalog.Trinkets.Single(item => item.Id == "external_local_trinket");
        Assert(
            externalLocalTrinket.Source == "local:External Test Mod" &&
            externalLocalTrinket.LocalizedName == new BilingualContentName("外部契约饰品", "External Contract Trinket"),
            "The selected extra local Mod directory should feed content and bilingual localization catalogs.");
        Assert(activeCatalog.Trinkets.Any(item => item.Id == "dlc_shared_trinket"), "Enabled DLC package root trinket is missing.");
        Assert(activeCatalog.Trinkets.Any(item => item.Id == "enabled_dlc_trinket"), "Enabled DLC feature trinket is missing.");
        Assert(activeCatalog.Trinkets.All(item => item.Id != "disabled_dlc_trinket"), "A disabled DLC feature trinket must not be scanned.");
        Assert(activeCatalog.Trinkets.All(item => item.Id != "disabled_workshop_trinket"), "Persistent history must not enable a disabled Workshop Mod.");
        Assert(activeCatalog.Trinkets.All(item => item.Id != "backup_trinket"), "A manifest backup path must not enter the active trinket catalog.");
        Assert(activeCatalog.Trinkets.All(item => item.Id != "disabled_mod_trinket"), "A normalized Mod path under a disabled DLC feature must not enter the active trinket catalog.");
        Assert(activeCatalog.Issues.All(issue => !issue.Contains("no modfiles.txt", StringComparison.Ordinal)), "Prepared local Mods must use their explicit manifests.");
        Assert(
            activeCatalog.Storage is { MaxSlots: 3, Source: "workshop:111" } &&
            !string.IsNullOrWhiteSpace(activeCatalog.Storage.SourceSha256) &&
            activeCatalog.Storage.SourcePath.Equals(
                Path.GetFullPath(Path.Combine(activeWorkshopInventoryRoot, "base.inventory.system_configs.darkest")),
                StringComparison.OrdinalIgnoreCase),
            "The effective inventory config at this native file slot should define trinket storage capacity without reading commented max_slots values.");
        var invalidCapacityModRoot = Path.Combine(runRoot, "invalid_capacity_mod");
        var invalidCapacityInventoryRoot = Path.Combine(invalidCapacityModRoot, "inventory");
        Directory.CreateDirectory(invalidCapacityInventoryRoot);
        File.WriteAllText(
            Path.Combine(invalidCapacityInventoryRoot, "broken.inventory.system_configs.darkest"),
            """
    inventory_system_config: .type "trinket_storage" .max_slots invalid
    """,
            new UTF8Encoding(false));
        WriteFixtureManifest(invalidCapacityModRoot);
        var invalidCapacityContent = activeContent with
        {
            Sources = activeContent.Sources
                .Append(new ActiveContentSource(
                    "local:invalid-capacity",
                    "Invalid Capacity",
                    "local",
                    invalidCapacityModRoot,
                    -1000))
                .ToArray()
        };
        var invalidCapacityCatalog = TrinketStorageCatalog.Load(invalidCapacityContent);
        Assert(
            invalidCapacityCatalog.Storage is null &&
            invalidCapacityCatalog.Issues.Any(issue =>
                issue.Contains("instead of falling back", StringComparison.Ordinal)),
            "An invalid final storage assignment must fail closed instead of falling back to an earlier capacity value.");
        var duplicateCapacityCatalog = LoadStorageCapacityProbe(
            activeContent,
            runRoot,
            "duplicate_capacity",
            """inventory_system_config: .type "trinket_storage" .max_slots 3 .max_slots 1""");
        Assert(
            duplicateCapacityCatalog.Storage is { MaxSlots: 1 },
            "A storage entry with duplicate max_slots fields must use the last native field value.");
        var malformedCapacityTokenCatalog = LoadStorageCapacityProbe(
            activeContent,
            runRoot,
            "malformed_capacity_token",
            """inventory_system_config: .type "trinket_storage" .max_slots 3oops""");
        Assert(
            malformedCapacityTokenCatalog.Storage is { MaxSlots: 3 },
            "A storage max_slots field must use the native integer prefix.");
        var conflictingCapacityModRootA = Path.Combine(runRoot, "conflicting_capacity_mod_a");
        var conflictingCapacityModRootB = Path.Combine(runRoot, "conflicting_capacity_mod_b");
        var conflictingCapacityInventoryRootA = Path.Combine(conflictingCapacityModRootA, "inventory");
        var conflictingCapacityInventoryRootB = Path.Combine(conflictingCapacityModRootB, "inventory");
        Directory.CreateDirectory(conflictingCapacityInventoryRootA);
        Directory.CreateDirectory(conflictingCapacityInventoryRootB);
        File.WriteAllText(
            Path.Combine(conflictingCapacityInventoryRootA, "shared.inventory.system_configs.darkest"),
            """
    inventory_system_config: .type "trinket_storage" .max_slots 4
    inventory_system_config: .type "raid" .max_slots 20
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(conflictingCapacityInventoryRootB, "shared.inventory.system_configs.darkest"),
            """
    inventory_system_config: .type "trinket_storage" .max_slots 5
    inventory_system_config: .type "raid" .max_slots 24
    """,
            new UTF8Encoding(false));
        WriteFixtureManifest(conflictingCapacityModRootA);
        WriteFixtureManifest(conflictingCapacityModRootB);
        var conflictingCapacityContent = activeContent with
        {
            Sources = activeContent.Sources
                .Concat([
                    new ActiveContentSource(
                "local:capacity-conflict-a",
                "Capacity Conflict A",
                "local",
                conflictingCapacityModRootA,
                -2000),
            new ActiveContentSource(
                "local:capacity-conflict-b",
                "Capacity Conflict B",
                "local",
                conflictingCapacityModRootB,
                -2000)
                ])
                .ToArray()
        };
        var conflictingCapacityCatalog = TrinketStorageCatalog.Load(conflictingCapacityContent);
        var conflictingRaidCapacityCatalog = RaidInventoryStorageCatalog.Load(conflictingCapacityContent);
        Assert(
            conflictingCapacityCatalog.Storage is null &&
            conflictingRaidCapacityCatalog.Storage is null &&
            conflictingCapacityCatalog.Issues.Any(issue =>
                issue.Contains("multiple providers", StringComparison.Ordinal)) &&
            conflictingRaidCapacityCatalog.Issues.Any(issue =>
                issue.Contains("multiple providers", StringComparison.Ordinal)),
            "Same-priority town-storage or raid-capacity providers for one virtual path must fail closed instead of falling back to a lower-priority value.");
        var ordinary = activeCatalog.Trinkets.Single(item => item.Id == "focus_ring");
        var unlimited = activeCatalog.Trinkets.Single(item => item.Id == "unlimited_probe");
        var stateful = activeCatalog.Trinkets.Single(item => item.Id == "fire_probe");
        var triggerStateful = activeCatalog.Trinkets.Single(item => item.Id == "trigger_probe");
        var dualCounterStateful = activeCatalog.Trinkets.Single(item => item.Id == "dual_counter_probe");
        var definitionDrivenStateful = activeCatalog.Trinkets.Single(item => item.Id == "definition_driven_probe");
        var zeroCounterStateful = activeCatalog.Trinkets.Single(item => item.Id == "zero_counter_probe");
        var prioritizedTrinket = activeCatalog.Trinkets.Single(item => item.Id == "local_mod_trinket");
        var originProbeTrinket = activeCatalog.Trinkets.Single(item => item.Id == "origin_probe_trinket");
        var modOnlyOverrideFileTrinket = activeCatalog.Trinkets.Single(item =>
            item.Id == "mod_only_in_overridden_trinket_file");
        var overriddenDlcTrinket = activeCatalog.Trinkets.Single(item => item.Id == "dlc_shared_trinket");
        var overriddenDlcFeatureTrinket = activeCatalog.Trinkets.Single(item => item.Id == "enabled_dlc_trinket");
        var ambiguousTrinket = activeCatalog.Trinkets.Single(item => item.Id == "ambiguous_trinketAz");
        Assert(!ordinary.IsStateful, "focus_ring should be writable.");
        Assert(ordinary.Limit == 1, "focus_ring should retain its per-id definition limit independently of storage capacity.");
        Assert(ordinary.SourceLabel == "原版", "An untouched base trinket should display its original-game provenance.");
        Assert(
            ordinary.LocalizedName == new BilingualContentName("契约专注戒指", "Contract Focus Ring"),
            "Trinket catalog should expose distinct Simplified Chinese and English names.");
        Assert(stateful.IsStateful && stateful.StatefulFields.Contains("quest_uses"), "fire_probe should be stateful.");
        Assert(
            stateful.QuestUses == 3 &&
            stateful.TriggerLimit is null,
            "A valid quest-use counter should be available for pristine instance construction.");
        Assert(
            triggerStateful.TriggerLimit == 4 &&
            triggerStateful.QuestUses is null,
            "A valid trigger counter should be available for pristine instance construction.");
        Assert(
            dualCounterStateful is { QuestUses: 2, TriggerLimit: 3 },
            "A definition carrying both supported counters should remain writable.");
        Assert(
            definitionDrivenStateful.IsStateful &&
            definitionDrivenStateful.QuestUses is null &&
            definitionDrivenStateful.TriggerLimit is null,
            "Definition-driven lifecycle fields must not block pristine creation.");
        Assert(
            zeroCounterStateful is { QuestUses: 0, TriggerLimit: null },
            "Zero is an explicit initial count, not an unsupported definition or an absent counter.");
        Assert(
            stateful.LocalizedName == BilingualContentName.Empty &&
            activeCatalog.Issues.Any(issue => issue.Contains("Failed to read localization", StringComparison.Ordinal) &&
                                             issue.Contains("lenient.string_table.xml", StringComparison.Ordinal)),
            "Malformed Mod XML must be reported and must not supply recovered display names.");
        Assert(
            prioritizedTrinket.Source == "local:Local Test Mod" &&
            prioritizedTrinket.Rarity == "uncommon" &&
            prioritizedTrinket.AllSources.Count == 2 &&
            !prioritizedTrinket.HasProviderConflict &&
            prioritizedTrinket.LocalizedName == new BilingualContentName("编译本地饰品", "Compiled Local Trinket"),
            "The top Mod should win an exact-relative-path trinket override without becoming a semantic conflict.");
        Assert(
            originProbeTrinket.Source == "workshop:111" &&
            originProbeTrinket.SourceLabel == "原版（当前由 创意工坊 Mod：111 覆盖）",
            "A base trinket overridden by a Mod should keep its original-game provenance while naming the effective provider.");
        Assert(
            modOnlyOverrideFileTrinket.Source == "workshop:111" &&
            modOnlyOverrideFileTrinket.AllSources.SequenceEqual(["workshop:111"]) &&
            modOnlyOverrideFileTrinket.SourceLabel == "创意工坊 Mod：111",
            "A Mod-only trinket added inside an overridden base file must not inherit original-game provenance from unrelated entries in that file.");
        Assert(
            activeCatalog.Issues.Any(issue =>
                issue.Contains("broken_english.loc2", StringComparison.OrdinalIgnoreCase) &&
                issue.Contains("Failed to read localization", StringComparison.Ordinal)),
            "A malformed LOC2 file should be reported without preventing other localized names from loading.");
        Assert(
            new[]
            {
        "partially_broken_bounds_english.loc2",
        "partially_broken_nul_english.loc2",
        "partially_broken_zero_length_english.loc2"
            }.All(fileName => activeCatalog.Issues.Any(issue =>
                issue.Contains(fileName, StringComparison.OrdinalIgnoreCase) &&
                issue.Contains("Failed to read localization", StringComparison.Ordinal))),
            "A LOC2 file must be rejected when any unrequested value has invalid bounds or termination.");
        Assert(activeCatalog.Issues.Any(issue =>
                   issue.Contains("partially_broken_utf8_english.loc2", StringComparison.OrdinalIgnoreCase) &&
                   issue.Contains("本地化部分读取", StringComparison.Ordinal) && issue.Contains("跳过 1 个", StringComparison.Ordinal)) &&
               activeCatalog.Issues.All(issue =>
                   !issue.Contains("partially_broken_utf8_english.loc2", StringComparison.OrdinalIgnoreCase) ||
                   !issue.Contains("Failed to read localization", StringComparison.Ordinal)),
            "An unrequested LOC2 text encoding error must be aggregated as a skipped item while keeping valid names.");
        Assert(
            activeCatalog.Issues.All(issue =>
                !issue.Contains("legal_duplicate_zero_english.loc2", StringComparison.OrdinalIgnoreCase)),
            "Repeated zero-hash compiler sentinel records must not reject an otherwise valid LOC2 file.");
        Assert(
            overriddenDlcTrinket.Source == "workshop:111" &&
            overriddenDlcTrinket.Price == 8800 &&
            overriddenDlcTrinket.AllSources.Count == 2 &&
            !overriddenDlcTrinket.HasProviderConflict,
            "A Mod should override a DLC file through its game-root virtual relative path.");
        Assert(
            overriddenDlcFeatureTrinket.Source == "workshop:111" &&
            overriddenDlcFeatureTrinket.Price == 8200 &&
            overriddenDlcFeatureTrinket.AllSources.Count == 2 &&
            !overriddenDlcFeatureTrinket.HasProviderConflict,
            "A Mod should override an enabled DLC feature trinket through its full virtual path.");
        Assert(
            ambiguousTrinket.HasProviderConflict && ambiguousTrinket.Source == "unresolved",
            "Trinket IDs sharing a native hash must remain unavailable.");
        Assert(
            activeCatalog.Issues.Any(issue => issue.Contains("Trinket IDs share native hashes", StringComparison.Ordinal)),
            "An unresolved native trinket identity should be reported.");


        return new TrinketCatalogContractState(
            activeCatalog,
            activeWorkshopTrinket,
            ambiguousTrinket,
            definitionDrivenStateful,
            dualCounterStateful,
            invalidCapacityContent,
            zeroCounterStateful,
            ordinary,
            stateful,
            triggerStateful,
            unlimited);
    }
}
