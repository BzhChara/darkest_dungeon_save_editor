internal static partial class ContractSuite
{
    private sealed record ContractFixture(
        string RunRoot,
        string GameRoot,
        string WorkshopRoot,
        string AdditionalLocalModDirectory,
        string AmbiguousDirectLocalModRoot,
        string ActiveWorkshopRoot,
        string ActiveWorkshopDistrictRoot,
        string ActiveWorkshopInventoryRoot,
        string ExternalLocalModRoot,
        string LocalHeroUpgradeRoot,
        string LocalLootRoot,
        string LocalModRoot,
        string LocalRaidCampingRoot,
        string LocalTownEventsRoot,
        string ProfileRoot,
        string DecodedSeedPath,
        string DecodedGameSeedPath,
        string DecodedTownSeedPath,
        string DecodedRosterSeedPath,
        string DecodedUpgradesSeedPath,
        string EstatePath,
        string GameSavePath,
        string TownSavePath,
        string RosterSavePath,
        string UpgradesSavePath,
        JsonObject RosterHeroesSeed,
        DsonSaveCodec Codec);

    private static ContractFixture BuildContractFixture(string repositoryRoot)
    {
        var jarPath = Path.Combine(
            repositoryRoot,
            "tools",
            "DDSaveEditor",
            "DDSaveEditor.jar");
        var runRoot = Path.Combine(
            repositoryRoot,
            "workspaces",
            "contract_tests",
            $"{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}");
        var syntheticProjectRoot = Path.Combine(runRoot, "synthetic-log-project");
        var syntheticApplicationDirectory = Path.Combine(
            syntheticProjectRoot,
            "src",
            "DarkestDungeonSaveEditor.App",
            "bin",
            "Release",
            "net8.0-windows");
        Directory.CreateDirectory(syntheticApplicationDirectory);
        File.WriteAllText(
            Path.Combine(syntheticProjectRoot, "DarkestDungeonSaveEditor.sln"),
            string.Empty,
            new UTF8Encoding(false));
        Assert(
            SaveEditorLocations.ResolveLogDirectory(syntheticApplicationDirectory)
                .Equals(Path.Combine(syntheticProjectRoot, "logs"), StringComparison.OrdinalIgnoreCase),
            "Development logging should resolve to the nearest project root logs directory.");
        var standaloneApplicationDirectory = Path.Combine(
            Path.GetTempPath(),
            $"ddse-standalone-log-contract-{Guid.NewGuid():N}");
        Assert(
            SaveEditorLocations.ResolveLogDirectory(standaloneApplicationDirectory)
                .Equals(Path.Combine(standaloneApplicationDirectory, "logs"), StringComparison.OrdinalIgnoreCase),
            "Standalone logging should fall back to a logs directory beside the application.");
        var gameRoot = Path.Combine(runRoot, "game");
        var workshopRoot = Path.Combine(runRoot, "workshop");
        var activeWorkshopRoot = Path.Combine(workshopRoot, "111");
        var disabledWorkshopRoot = Path.Combine(workshopRoot, "222");
        var localModRoot = Path.Combine(gameRoot, "mods", "LocalTestMod");
        var localBackupRoot = Path.Combine(localModRoot, "project-backup");
        var additionalLocalModDirectory = Path.Combine(runRoot, "external-local-mods");
        var externalLocalModRoot = Path.Combine(additionalLocalModDirectory, "ExternalTestMod");
        var externalLocalLocalizationRoot = Path.Combine(externalLocalModRoot, "localization");
        var ambiguousDirectLocalModRoot = Path.Combine(runRoot, "ambiguous-direct-local-mod");
        var baseHeroRoot = Path.Combine(gameRoot, "heroes", "base_hero");
        var activeRuntimeHeroRoot = Path.Combine(activeWorkshopRoot, "heroes", "runtime_hero");
        var activeRuntimeHeroDuplicateRoot = Path.Combine(activeWorkshopRoot, "heroes", "runtime_hero_patch");
        var activeOverrideHeroRoot = Path.Combine(activeWorkshopRoot, "heroes", "base_hero");
        var activeWorkshopLocalHeroRoot = Path.Combine(activeWorkshopRoot, "heroes", "local_hero");
        var activeWorkshopPriorityHeroRoot = Path.Combine(activeWorkshopRoot, "heroes", "priority_hero");
        var activeWorkshopUpgradeRoot = Path.Combine(activeWorkshopRoot, "upgrades");
        var activeWorkshopBuildingUpgradeRoot = Path.Combine(activeWorkshopUpgradeRoot, "building");
        var activeWorkshopBuffRoot = Path.Combine(activeWorkshopRoot, "shared", "buffs");
        var activeWorkshopLocalizationRoot = Path.Combine(activeWorkshopRoot, "localization");
        var activeWorkshopDistrictRoot = Path.Combine(activeWorkshopRoot, "campaign", "town", "districts");
        var baseInventoryRoot = Path.Combine(gameRoot, "inventory");
        var activeWorkshopInventoryRoot = Path.Combine(activeWorkshopRoot, "inventory");
        var disabledWorkshopInventoryRoot = Path.Combine(disabledWorkshopRoot, "inventory");
        var localInventoryRoot = Path.Combine(localModRoot, "inventory");
        var localLootRoot = Path.Combine(localModRoot, "loot");
        var localRulesRoot = Path.Combine(localModRoot, "rules");
        var localProvisionRoot = Path.Combine(localModRoot, "campaign", "provision");
        var localTownEventsRoot = Path.Combine(localModRoot, "campaign", "town_events");
        var localMonsterRoot = Path.Combine(localModRoot, "monsters", "quantity_reference_probe", "quantity_reference_probe_A");
        var localRaidCampingRoot = Path.Combine(localModRoot, "raid", "camping");
        var activeWorkshopDlcRoot = Path.Combine(activeWorkshopRoot, "dlc", "100_feature_pack");
        var activeWorkshopDlcTrinketRoot = Path.Combine(activeWorkshopDlcRoot, "trinkets");
        var activeWorkshopOfficialOverrideHeroRoot = Path.Combine(
            activeWorkshopDlcRoot,
            "heroes",
            "official_override_hero");
        var activeWorkshopEnabledDlcFeatureRoot = Path.Combine(
            activeWorkshopDlcRoot,
            "features",
            "enabled_feature");
        var ignoredBackupRoot = Path.Combine(activeWorkshopRoot, "backup");
        var ignoredModeRoot = Path.Combine(activeWorkshopRoot, "modes", "bloodmoon");
        var ignoredDisabledFeatureHeroRoot = Path.Combine(
            activeWorkshopDlcRoot,
            "features",
            "disabled_feature",
            "heroes",
            "disabled_mod_hero");
        var ignoredDisabledFeatureTrinketRoot = Path.Combine(
            activeWorkshopDlcRoot,
            "features",
            "disabled_feature",
            "trinkets");
        var disabledHeroRoot = Path.Combine(disabledWorkshopRoot, "heroes", "disabled_hero");
        var localHeroRoot = Path.Combine(localModRoot, "heroes", "local_hero");
        var compatibleUpgradeHeroRoot = Path.Combine(localModRoot, "heroes", "compatible_upgrade_hero");
        var localPriorityHeroRoot = Path.Combine(localModRoot, "heroes", "priority_hero");
        var localHeroUpgradeRoot = Path.Combine(localModRoot, "upgrades");
        var localNestedHeroUpgradeRoot = Path.Combine(localHeroUpgradeRoot, "heroes");
        var localBuildingUpgradeRoot = Path.Combine(localHeroUpgradeRoot, "building");
        var localLocalizationRoot = Path.Combine(localModRoot, "localization");
        var baseBuffRoot = Path.Combine(gameRoot, "shared", "buffs");
        var baseQuirkRoot = Path.Combine(gameRoot, "shared", "quirk");
        var baseCampingRoot = Path.Combine(gameRoot, "raid", "camping");
        var baseLocalizationRoot = Path.Combine(gameRoot, "localization");
        var baseRosterConfigurationRoot = Path.Combine(gameRoot, "campaign", "roster");
        var radiantRosterConfigurationRoot = Path.Combine(gameRoot, "modes", "radiant", "campaign", "roster");
        var dlcPackageRoot = Path.Combine(gameRoot, "dlc", "100_feature_pack");
        var dlcOverrideHeroRoot = Path.Combine(dlcPackageRoot, "heroes", "official_override_hero");
        var enabledDlcFeatureRoot = Path.Combine(dlcPackageRoot, "features", "enabled_feature");
        var disabledDlcFeatureRoot = Path.Combine(dlcPackageRoot, "features", "disabled_feature");
        var profileRoot = Path.Combine(runRoot, "profile_7");
        var decodedSeedPath = Path.Combine(runRoot, "seed.persist.estate.json");
        var decodedGameSeedPath = Path.Combine(runRoot, "seed.persist.game.json");
        var decodedTownSeedPath = Path.Combine(runRoot, "seed.persist.town.json");
        var decodedRosterSeedPath = Path.Combine(runRoot, "seed.persist.roster.json");
        var decodedUpgradesSeedPath = Path.Combine(runRoot, "seed.persist.upgrades.json");
        var estatePath = Path.Combine(profileRoot, "persist.estate.json");
        var gameSavePath = Path.Combine(profileRoot, "persist.game.json");
        var townSavePath = Path.Combine(profileRoot, "persist.town.json");
        var rosterSavePath = Path.Combine(profileRoot, "persist.roster.json");
        var upgradesSavePath = Path.Combine(profileRoot, "persist.upgrades.json");
        Directory.CreateDirectory(Path.Combine(gameRoot, "trinkets"));
        Directory.CreateDirectory(Path.Combine(activeWorkshopRoot, "trinkets"));
        Directory.CreateDirectory(Path.Combine(disabledWorkshopRoot, "trinkets"));
        Directory.CreateDirectory(Path.Combine(localModRoot, "trinkets"));
        Directory.CreateDirectory(localBackupRoot);
        Directory.CreateDirectory(Path.Combine(externalLocalModRoot, "trinkets"));
        Directory.CreateDirectory(externalLocalLocalizationRoot);
        Directory.CreateDirectory(ambiguousDirectLocalModRoot);
        Directory.CreateDirectory(baseHeroRoot);
        Directory.CreateDirectory(activeRuntimeHeroRoot);
        Directory.CreateDirectory(activeRuntimeHeroDuplicateRoot);
        Directory.CreateDirectory(activeOverrideHeroRoot);
        Directory.CreateDirectory(activeWorkshopLocalHeroRoot);
        Directory.CreateDirectory(activeWorkshopPriorityHeroRoot);
        Directory.CreateDirectory(Path.Combine(activeWorkshopPriorityHeroRoot, "priority_hero_A"));
        Directory.CreateDirectory(Path.Combine(activeWorkshopPriorityHeroRoot, "priority_hero_B"));
        File.WriteAllBytes(Path.Combine(activeWorkshopPriorityHeroRoot, "priority_hero_A", "skin.png"), [1]);
        File.WriteAllBytes(Path.Combine(activeWorkshopPriorityHeroRoot, "priority_hero_B", "skin.png"), [2]);
        Directory.CreateDirectory(activeWorkshopUpgradeRoot);
        Directory.CreateDirectory(activeWorkshopBuildingUpgradeRoot);
        Directory.CreateDirectory(activeWorkshopBuffRoot);
        Directory.CreateDirectory(activeWorkshopLocalizationRoot);
        Directory.CreateDirectory(activeWorkshopDistrictRoot);
        Directory.CreateDirectory(baseInventoryRoot);
        Directory.CreateDirectory(activeWorkshopInventoryRoot);
        Directory.CreateDirectory(disabledWorkshopInventoryRoot);
        Directory.CreateDirectory(localInventoryRoot);
        Directory.CreateDirectory(localLootRoot);
        Directory.CreateDirectory(localRulesRoot);
        Directory.CreateDirectory(localProvisionRoot);
        Directory.CreateDirectory(localTownEventsRoot);
        Directory.CreateDirectory(localMonsterRoot);
        Directory.CreateDirectory(localRaidCampingRoot);
        Directory.CreateDirectory(activeWorkshopDlcTrinketRoot);
        Directory.CreateDirectory(activeWorkshopOfficialOverrideHeroRoot);
        Directory.CreateDirectory(Path.Combine(activeWorkshopDlcRoot, "heroes", "dlc_shared_hero"));
        Directory.CreateDirectory(Path.Combine(activeWorkshopDlcRoot, "effects"));
        Directory.CreateDirectory(Path.Combine(activeWorkshopDlcRoot, "shared", "quirk"));
        Directory.CreateDirectory(Path.Combine(activeWorkshopDlcRoot, "campaign", "town_events"));
        Directory.CreateDirectory(Path.Combine(activeWorkshopEnabledDlcFeatureRoot, "trinkets"));
        Directory.CreateDirectory(Path.Combine(activeWorkshopEnabledDlcFeatureRoot, "heroes", "enabled_dlc_hero"));
        Directory.CreateDirectory(Path.Combine(ignoredBackupRoot, "heroes", "backup_hero"));
        Directory.CreateDirectory(Path.Combine(ignoredBackupRoot, "trinkets"));
        Directory.CreateDirectory(Path.Combine(ignoredModeRoot, "effects"));
        Directory.CreateDirectory(Path.Combine(ignoredModeRoot, "shared", "quirk"));
        Directory.CreateDirectory(Path.Combine(ignoredModeRoot, "campaign", "town_events"));
        Directory.CreateDirectory(ignoredDisabledFeatureHeroRoot);
        Directory.CreateDirectory(ignoredDisabledFeatureTrinketRoot);
        Directory.CreateDirectory(disabledHeroRoot);
        Directory.CreateDirectory(localHeroRoot);
        Directory.CreateDirectory(compatibleUpgradeHeroRoot);
        Directory.CreateDirectory(Path.Combine(compatibleUpgradeHeroRoot, "compatible_upgrade_hero_A"));
        File.WriteAllBytes(Path.Combine(compatibleUpgradeHeroRoot, "compatible_upgrade_hero_A", "skin.png"), [1]);
        Directory.CreateDirectory(localPriorityHeroRoot);
        Directory.CreateDirectory(localHeroUpgradeRoot);
        Directory.CreateDirectory(localNestedHeroUpgradeRoot);
        Directory.CreateDirectory(localBuildingUpgradeRoot);
        Directory.CreateDirectory(localLocalizationRoot);
        Directory.CreateDirectory(Path.Combine(localHeroRoot, "local_hero_A"));
        Directory.CreateDirectory(Path.Combine(localHeroRoot, "local_hero_B"));
        File.WriteAllBytes(Path.Combine(localHeroRoot, "local_hero_A", "skin.png"), [1]);
        File.WriteAllBytes(Path.Combine(localHeroRoot, "local_hero_B", "skin.png"), [2]);
        Directory.CreateDirectory(baseBuffRoot);
        Directory.CreateDirectory(baseQuirkRoot);
        Directory.CreateDirectory(baseCampingRoot);
        Directory.CreateDirectory(baseLocalizationRoot);
        Directory.CreateDirectory(baseRosterConfigurationRoot);
        Directory.CreateDirectory(radiantRosterConfigurationRoot);
        Directory.CreateDirectory(Path.Combine(activeWorkshopRoot, "effects"));
        Directory.CreateDirectory(Path.Combine(activeWorkshopRoot, "shared", "quirk"));
        Directory.CreateDirectory(Path.Combine(activeWorkshopRoot, "campaign", "town_events"));
        Directory.CreateDirectory(Path.Combine(disabledWorkshopRoot, "campaign", "town_events"));
        Directory.CreateDirectory(Path.Combine(localModRoot, "campaign", "town_events"));
        Directory.CreateDirectory(Path.Combine(localModRoot, "effects"));
        Directory.CreateDirectory(Path.Combine(localModRoot, "shared", "quirk"));
        Directory.CreateDirectory(Path.Combine(dlcPackageRoot, "trinkets"));
        Directory.CreateDirectory(Path.Combine(dlcPackageRoot, "heroes", "dlc_shared_hero"));
        Directory.CreateDirectory(dlcOverrideHeroRoot);
        Directory.CreateDirectory(Path.Combine(dlcPackageRoot, "effects"));
        Directory.CreateDirectory(Path.Combine(dlcPackageRoot, "shared", "quirk"));
        Directory.CreateDirectory(Path.Combine(dlcPackageRoot, "campaign", "town_events"));
        Directory.CreateDirectory(Path.Combine(enabledDlcFeatureRoot, "trinkets"));
        Directory.CreateDirectory(Path.Combine(enabledDlcFeatureRoot, "heroes", "enabled_dlc_hero"));
        Directory.CreateDirectory(Path.Combine(disabledDlcFeatureRoot, "trinkets"));
        Directory.CreateDirectory(Path.Combine(disabledDlcFeatureRoot, "heroes", "disabled_dlc_hero"));
        Directory.CreateDirectory(profileRoot);

        File.WriteAllText(
            Path.Combine(baseBuffRoot, "generation.buffs.json"),
            """
    {
      "buffs": [
        { "id": "MAXHP10", "stat_type": "combat_stat_multiply", "stat_sub_type": "max_hp", "amount": 0.1, "rule_type": "always", "is_false_rule": false },
        { "id": "MAXHP20_NO_TRINKETS", "stat_type": "combat_stat_multiply", "stat_sub_type": "max_hp", "amount": 0.2, "rule_type": "no_trinkets", "is_false_rule": false },
        { "id": "MAXHP-5", "stat_type": "combat_stat_multiply", "stat_sub_type": "max_hp", "amount": -0.05, "rule_type": "always", "is_false_rule": false },
        { "id": "MAXHP-10", "stat_type": "combat_stat_multiply", "stat_sub_type": "max_hp", "amount": -0.1, "rule_type": "always", "is_false_rule": false },
        { "id": "MAXHP-100", "stat_type": "combat_stat_multiply", "stat_sub_type": "max_hp", "amount": -1.0, "rule_type": "always", "is_false_rule": false },
        { "id": "MAXHP_CONDITIONAL", "stat_type": "combat_stat_multiply", "stat_sub_type": "max_hp", "amount": 0.5, "rule_type": "in_rank", "is_false_rule": false },
        { "id": "MAXHP_FLAT", "stat_type": "combat_stat_add", "stat_sub_type": "max_hp", "amount": 4, "rule_type": "always", "is_false_rule": false },
        { "id": "MAXHP_FLAT_NEG24", "stat_type": "combat_stat_add", "stat_sub_type": "max_hp", "amount": -24, "rule_type": "always", "is_false_rule": false },
        { "id": "MAXHP4_PERCENT", "stat_type": "combat_stat_multiply", "stat_sub_type": "max_hp", "amount": 0.04, "rule_type": "always", "is_false_rule": false },
        { "id": "MAXHP-50", "stat_type": "combat_stat_multiply", "stat_sub_type": "max_hp", "amount": -0.5, "rule_type": "always", "is_false_rule": false },
        { "id": "MAXHP_AFFLICTED", "stat_type": "combat_stat_multiply", "stat_sub_type": "max_hp", "amount": 0.05, "rule_type": "afflicted", "is_false_rule": false },
        { "id": "MAXHP_AFFLICTED_NEG", "stat_type": "combat_stat_multiply", "stat_sub_type": "max_hp", "amount": -0.5, "rule_type": "afflicted", "is_false_rule": false },
        { "id": "MAXHP_OTHER_MODE", "stat_type": "combat_stat_add", "stat_sub_type": "max_hp", "amount": 20, "rule_type": "in_mode", "is_false_rule": true, "rule_data": { "float": 0, "string": "ContractModeA" } },
        { "id": "MAXHP_OTHER_MODE_NEG", "stat_type": "combat_stat_multiply", "stat_sub_type": "max_hp", "amount": -0.5, "rule_type": "in_mode", "is_false_rule": true, "rule_data": { "float": 0, "string": "ContractModeA" } },
        { "id": "MAXHP_MODE_A_NEG60", "stat_type": "combat_stat_multiply", "stat_sub_type": "max_hp", "amount": -0.6, "rule_type": "in_mode", "is_false_rule": false, "rule_data": { "float": 0, "string": "ContractModeA" } },
        { "id": "MAXHP_MODE_B_NEG60", "stat_type": "combat_stat_multiply", "stat_sub_type": "max_hp", "amount": -0.6, "rule_type": "in_mode", "is_false_rule": false, "rule_data": { "float": 0, "string": "ContractModeB" } },
        { "id": "MAXHP_LIGHT_ABOVE", "stat_type": "combat_stat_multiply", "stat_sub_type": "max_hp", "amount": -0.5, "rule_type": "lightabove", "is_false_rule": false, "rule_data": { "float": 1, "string": "" } },
        { "id": "MAXHP_ROUNDING_NEG20_A", "stat_type": "combat_stat_multiply", "stat_sub_type": "max_hp", "amount": -0.2, "rule_type": "always", "is_false_rule": false },
        { "id": "MAXHP_ROUNDING_NEG20_B", "stat_type": "combat_stat_multiply", "stat_sub_type": "max_hp", "amount": -0.2, "rule_type": "always", "is_false_rule": false },
        { "id": "MAXHP_ROUNDING_NEG10", "stat_type": "combat_stat_multiply", "stat_sub_type": "max_hp", "amount": -0.1, "rule_type": "always", "is_false_rule": false },
        { "id": "ACC5", "stat_type": "combat_stat_add", "stat_sub_type": "attack_rating", "amount": 5, "rule_type": "always", "is_false_rule": false }
      ]
    }
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(baseQuirkRoot, "generation.quirk_library.json"),
            """
    {
      "quirks": [
        { "id": "tough", "random_chance": 1, "is_positive": true, "is_disease": false, "incompatible_quirks": ["fragile"], "buffs": ["MAXHP10"] },
        { "id": "natural", "random_chance": 1, "is_positive": true, "is_disease": false, "buffs": ["MAXHP20_NO_TRINKETS"] },
        { "id": "steady", "random_chance": 1, "is_positive": true, "is_disease": false, "buffs": [] },
        { "id": "eagle_eye", "random_chance": 1, "is_positive": true, "is_disease": false, "buffs": ["ACC5"] },
        { "id": "hard_skinned", "random_chance": 1, "is_positive": true, "is_disease": false, "buffs": [] },
        { "id": "slugger", "random_chance": 1, "is_positive": true, "is_disease": false, "buffs": [] },
        { "id": "warrior_of_light", "random_chance": 1, "is_positive": true, "is_disease": false, "buffs": [] },
        { "id": "robust", "random_chance": 1, "is_positive": true, "is_disease": false, "buffs": [] },
        { "id": "same_path_duplicate", "random_chance": 0, "is_positive": false, "is_disease": false, "buffs": [] },
        { "id": "same_path_duplicate", "random_chance": 1, "is_positive": true, "is_disease": false, "buffs": [] },
        { "id": "context_special", "random_chance": 0, "is_positive": true, "is_disease": false, "tags": ["singleton"], "buffs": [] },
        { "id": "context_unverified_singleton", "random_chance": 0, "is_positive": true, "is_disease": false, "tags": ["singleton"], "buffs": ["MAXHP_CONDITIONAL"] },
        { "id": "context_roster_limited", "random_chance": 0, "is_positive": false, "is_disease": false, "roster_limit": 2, "buffs": [] },
        { "id": "shard_hungry", "random_chance": 0, "is_positive": false, "is_disease": false, "roster_limit": 6, "buffs": [] },
        { "id": "excluded_quirk", "random_chance": 100, "is_positive": true, "is_disease": false, "buffs": [] },
        { "id": "semantic_priority_quirk", "random_chance": 1, "is_positive": false, "is_disease": false, "buffs": [] },
        { "id": "unknown_hp_rule", "random_chance": 100, "is_positive": true, "is_disease": false, "buffs": ["MAXHP_CONDITIONAL"] },
        { "id": "evolving_quirk", "random_chance": 100, "is_positive": true, "is_disease": false, "buffs": [], "evolution_duration_min": 60, "evolution_duration_max": 60, "evolution_class_id": "evolved_quirk" },
        { "id": "evolving_variable", "random_chance": 100, "is_positive": true, "is_disease": false, "buffs": [], "evolution_duration_min": 3, "evolution_duration_max": 15, "evolution_town_progression_duration_change": 1, "evolution_class_id": "evolved_variable" },
        { "id": "evolved_quirk", "random_chance": 0, "is_positive": true, "is_disease": false, "buffs": [] },
        { "id": "evolved_variable", "random_chance": 0, "is_positive": true, "is_disease": false, "buffs": [] },
        { "id": "evolved_immediately", "random_chance": 0, "is_positive": true, "is_disease": false, "buffs": [] },
        { "id": "evolved_unknown_hp", "random_chance": 0, "is_positive": true, "is_disease": false, "buffs": [] },
        { "id": "evolved_disease", "random_chance": 0, "is_positive": false, "is_disease": true, "buffs": [] },
        { "id": "same_target", "random_chance": 0, "is_positive": true, "is_disease": false, "buffs": [] },
        { "id": "evolving_zero", "random_chance": 100, "is_positive": true, "is_disease": false, "buffs": [], "evolution_duration_min": 0, "evolution_duration_max": 0, "evolution_class_id": "evolved_immediately" },
        { "id": "evolving_death", "random_chance": 100, "is_positive": false, "is_disease": true, "buffs": [], "evolution_duration_min": 61, "evolution_duration_max": 61, "evolution_town_progression_duration_change": 30, "evolution_town_attempt_use_item_duration_threshold": 61, "evolution_causes_death": true },
        { "id": "evolution_missing_max", "random_chance": 100, "is_positive": false, "is_disease": false, "buffs": [], "evolution_duration_min": 3, "evolution_class_id": "broken_target" },
        { "id": "evolution_inverted", "random_chance": 100, "is_positive": false, "is_disease": false, "buffs": [], "evolution_duration_min": 15, "evolution_duration_max": 3, "evolution_class_id": "broken_target" },
        { "id": "evolution_fractional", "random_chance": 100, "is_positive": false, "is_disease": false, "buffs": [], "evolution_duration_min": 3.5, "evolution_duration_max": 15, "evolution_class_id": "broken_target" },
        { "id": "evolution_missing_outcome", "random_chance": 100, "is_positive": false, "is_disease": false, "buffs": [], "evolution_duration_min": 3, "evolution_duration_max": 15 },
        { "id": "evolving_unknown_hp", "random_chance": 100, "is_positive": true, "is_disease": false, "buffs": ["MAXHP_CONDITIONAL"], "evolution_duration_min": 30, "evolution_duration_max": 60, "evolution_class_id": "evolved_unknown_hp" },
        { "id": "flat_hp_quirk", "random_chance": 1, "is_positive": true, "is_disease": false, "buffs": ["MAXHP_FLAT"] },
        { "id": "multiple_hp_quirk", "random_chance": 1, "is_positive": true, "is_disease": false, "buffs": ["MAXHP10", "MAXHP-5"] },
        { "id": "mixed_hp_quirk", "random_chance": 1, "is_positive": true, "is_disease": false, "buffs": ["MAXHP_FLAT", "MAXHP4_PERCENT"] },
        { "id": "afflicted_hp_quirk", "random_chance": 1, "is_positive": true, "is_disease": false, "buffs": ["MAXHP_AFFLICTED"] },
        { "id": "afflicted_half_weakness", "random_chance": 0, "is_positive": false, "is_disease": false, "buffs": ["MAXHP_AFFLICTED_NEG"] },
        { "id": "other_mode_hp_quirk", "random_chance": 0, "is_positive": true, "is_disease": false, "buffs": ["MAXHP_OTHER_MODE"] },
        { "id": "other_mode_half_weakness", "random_chance": 0, "is_positive": false, "is_disease": false, "buffs": ["MAXHP_OTHER_MODE_NEG"] },
        { "id": "mode_a_weakness", "random_chance": 0, "is_positive": false, "is_disease": false, "buffs": ["MAXHP_MODE_A_NEG60"] },
        { "id": "mode_b_weakness", "random_chance": 0, "is_positive": false, "is_disease": false, "buffs": ["MAXHP_MODE_B_NEG60"] },
        { "id": "light_hp_quirk", "random_chance": 0, "is_positive": false, "is_disease": false, "buffs": ["MAXHP_LIGHT_ABOVE"] },
        { "id": "rounding_weakness_a", "random_chance": 0, "is_positive": false, "is_disease": false, "buffs": ["MAXHP_ROUNDING_NEG20_A"] },
        { "id": "rounding_weakness_b", "random_chance": 0, "is_positive": false, "is_disease": false, "buffs": ["MAXHP_ROUNDING_NEG20_B"] },
        { "id": "rounding_weakness_c", "random_chance": 0, "is_positive": false, "is_disease": false, "buffs": ["MAXHP_ROUNDING_NEG10"] },
        { "id": "half_weakness", "random_chance": 1, "is_positive": false, "is_disease": false, "buffs": ["MAXHP-50"] },
        { "id": "flat_level_boundary", "random_chance": 0, "is_positive": false, "is_disease": false, "buffs": ["MAXHP_FLAT_NEG24"] },
        { "id": "fragile", "random_chance": 1, "is_positive": false, "is_disease": false, "incompatible_quirks": ["tough"], "buffs": ["MAXHP-10"] },
        { "id": "soft", "random_chance": 1, "is_positive": false, "is_disease": false, "incompatible_quirks": ["hard_skinned"], "buffs": ["MAXHP-5"] },
        { "id": "fatal_weakness", "random_chance": 1, "is_positive": false, "is_disease": false, "buffs": ["MAXHP-100"] },
        { "id": "clumsy", "random_chance": 1, "is_positive": false, "is_disease": false, "buffs": [] },
        { "id": "slowdraw", "random_chance": 1, "is_positive": false, "is_disease": false, "buffs": [] },
        { "id": "off_guard", "random_chance": 1, "is_positive": false, "is_disease": false, "buffs": [] },
        { "id": "nervous", "random_chance": 1, "is_positive": false, "is_disease": false, "buffs": [] },
        { "id": "weak_grip", "random_chance": 1, "is_positive": false, "is_disease": false, "buffs": [] },
        { "id": "fearful", "random_chance": 1, "is_positive": false, "is_disease": false, "buffs": [] },
        { "id": "test_disease", "random_chance": 1, "is_positive": false, "is_disease": true, "buffs": [] },
        { "id": "test_disease_two", "random_chance": 1, "is_positive": false, "is_disease": true, "buffs": [] },
        { "id": "test_disease_three", "random_chance": 1, "is_positive": false, "is_disease": true, "buffs": [] },
        { "id": "test_disease_four", "random_chance": 1, "is_positive": false, "is_disease": true, "buffs": [] },
        { "id": "evolving_disease", "random_chance": 1, "is_positive": false, "is_disease": true, "buffs": [], "evolution_duration_min": 30, "evolution_duration_max": 60, "evolution_class_id": "evolved_disease" },
        { "id": "unknown_hp_disease", "random_chance": 1, "is_positive": false, "is_disease": true, "buffs": ["MAXHP_CONDITIONAL"] }
      ]
    }
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(baseQuirkRoot, "source_probe.quirk_library.json"),
            """
    {
      "quirks": [
        { "id": "source_probe_quirk", "random_chance": 1, "is_positive": true, "is_disease": false, "buffs": [] }
      ]
    }
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(baseCampingRoot, "default.camping_skills.json"),
            """
    {
      "configuration": { "class_specific_number_of_classes_threshold": 4 },
      "skills": [
        { "id": "encourage", "hero_classes": ["base_hero", "local_hero", "runtime_hero", "dlc_shared_hero", "enabled_dlc_hero"] },
        { "id": "first_aid", "hero_classes": ["base_hero", "local_hero", "runtime_hero", "dlc_shared_hero", "enabled_dlc_hero"] },
        { "id": "local_camp_one", "hero_classes": ["local_hero"] },
        { "id": "local_camp_two", "hero_classes": ["local_hero"] }
      ]
    }
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(baseLocalizationRoot, "names.string_table.xml"),
            """
    <?xml version="1.0" encoding="UTF-8"?>
    <root>
      <language id="english">
        <entry id="str_inventory_title_trinketfocus_ring"><![CDATA[Contract Focus Ring]]></entry>
        <entry id="str_inventory_title_gold"><![CDATA[Gold]]></entry>
        <entry id="str_inventory_title_heirloomblueprint"><![CDATA[Blueprint]]></entry>
        <entry id="str_inventory_title_supplytorch"><![CDATA[Torch]]></entry>
        <entry id="str_inventory_title_supplybandage"><![CDATA[Bandage]]></entry>
        <entry id="str_inventory_title_gemraid_only_gem"><![CDATA[Raid Gem]]></entry>
        <entry id="str_inventory_title_estatethe_blood"><![CDATA[The Blood]]></entry>
        <entry id="str_inventory_title_estatesave_only_relic"><![CDATA[Save Relic]]></entry>
        <entry id="str_quirk_name_natural"><![CDATA[Contract Natural Constitution]]></entry>
      </language>
      <language id="schinese">
        <entry id="hero_name_0"><![CDATA[Contract One]]></entry>
        <entry id="hero_name_1"><![CDATA[Contract Two]]></entry>
        <entry id="str_inventory_title_trinketfocus_ring"><![CDATA[契约专注戒指]]></entry>
        <entry id="str_inventory_title_gold"><![CDATA[金币]]></entry>
        <entry id="str_inventory_title_heirloomblueprint"><![CDATA[建筑图纸]]></entry>
        <entry id="str_inventory_title_supplytorch"><![CDATA[火把]]></entry>
        <entry id="str_inventory_title_supplybandage"><![CDATA[绷带]]></entry>
        <entry id="str_inventory_title_gemraid_only_gem"><![CDATA[副本宝石]]></entry>
        <entry id="str_inventory_title_estatethe_blood"><![CDATA[血酿]]></entry>
        <entry id="str_inventory_title_estatesave_only_relic"><![CDATA[存档遗物]]></entry>
        <entry id="str_quirk_name_natural"><![CDATA[契约自然体质]]></entry>
      </language>
    </root>
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(baseRosterConfigurationRoot, "roster.variables.json"),
            """
    { "resolve_level_thresholds": [0, 2, 8, 14, 24, 36, 48] }
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(radiantRosterConfigurationRoot, "roster.variables.json"),
            """
    { "resolve_level_thresholds": [0, 1, 6, 12, 20, 30, 42] }
    """,
            new UTF8Encoding(false));

        File.WriteAllText(
            Path.Combine(gameRoot, "trinkets", "fixture.entries.trinkets.json"),
            """
    {
      "entries": [
        { "id": "focus_ring", "rarity": "very_rare", "limit": 1, "price": 7500 },
        { "id": "unlimited_probe", "rarity": "common", "limit": 0, "price": 100 },
        { "id": "fire_probe", "rarity": "rare", "quest_uses": 3, "price": 2500 },
        {
          "id": "trigger_probe", "rarity": "rare", "trigger_limit": 4,
          "trigger_progressive_art": true, "transform_on_trigger_limit_exhausted": "spent_probe"
        },
        {
          "id": "dual_counter_probe", "rarity": "rare", "quest_uses": 2, "trigger_limit": 3,
          "blocks_other_slot_quest_uses_expend": true, "destroy_on_triggers_exhausted": true
        },
        {
          "id": "definition_driven_probe", "rarity": "rare",
          "on_quest_complete_additional_effects": ["contract_effect"]
        },
        { "id": "zero_counter_probe", "rarity": "rare", "quest_uses": 0 },
        { "id": "ambiguous_trinketAz", "rarity": "common", "price": 100 }
      ]
    }
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(gameRoot, "trinkets", "origin_probe.entries.trinkets.json"),
            """
    { "entries": [ { "id": "origin_probe_trinket", "rarity": "common", "price": 100 } ] }
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(baseInventoryRoot, "base.inventory.system_configs.darkest"),
            """
    inventory_system_config: .type "trinket_storage" .max_slots 9999 .use_stack_limits true
    inventory_system_config: .type "hero_equipped_trinkets" .max_slots 2 .use_stack_limits true
    """,
                new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(baseInventoryRoot, "raid.inventory.system_configs.darkest"),
            """
    inventory_system_config: .type "raid" .max_slots 4 .use_stack_limits true
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(baseInventoryRoot, "base.currency.inventory.items.darkest"),
            """
    inventory_item: .type "gold" .id "" .base_stack_limit 1750 .purchase_gold_value 0 .sell_gold_value 0
    inventory_item: .type "heirloom" .id "blueprint" .base_stack_limit 1 .purchase_gold_value 0 .sell_gold_value 0
    inventory_item: .type "gem" .id "raid_only_gem" .base_stack_limit 5 .purchase_gold_value 0 .sell_gold_value 500
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(baseInventoryRoot, "base.supply.inventory.items.darkest"),
            """
    inventory_item: .type "supply" .id "torch" .base_stack_limit 8 .purchase_gold_value 75 .sell_gold_value 5
    inventory_item: .type "supply" .id "bandage" .base_stack_limit 6 .purchase_gold_value 150 .sell_gold_value 15
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(baseInventoryRoot, "base.estate.inventory.items.darkest"),
            """
    inventory_item: .type "estate" .id "the_blood" .base_stack_limit 6 .purchase_gold_value 0 .sell_gold_value 0 .estate_can_be_provision true
    """,
        new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(activeWorkshopInventoryRoot, "base.inventory.system_configs.darkest"),
            """
    // inventory_system_config: .type "trinket_storage" .max_slots 9999
    inventory_system_config: .type "trinket_storage" // .max_slots 9999 must stay ignored
        .max_slots 3 .use_stack_limits true
    """,
                new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(activeWorkshopInventoryRoot, "active.currency.inventory.items.darkest"),
            """
    inventory_item: .type "gold" .id "" .base_stack_limit 2500 .purchase_gold_value 0 .sell_gold_value 0
    """,
        new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(disabledWorkshopInventoryRoot, "base.inventory.system_configs.darkest"),
            """
    inventory_system_config: .type "trinket_storage" .max_slots 1 .use_stack_limits true
    """,
                new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(disabledWorkshopInventoryRoot, "disabled.estate.inventory.items.darkest"),
            """
    inventory_item: .type "estate" .id "disabled_mod_item" .base_stack_limit 99
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(localInventoryRoot, "local.estate.inventory.items.darkest"),
            """
    inventory_item: .type "estate" .id "local_mod_essence" .base_stack_limit 12 .purchase_gold_value 0 .sell_gold_value 10 .estate_can_be_provision false
    inventory_item: .type "estate" .id "provisionable_mod_essence" .base_stack_limit 4 .purchase_gold_value 0 .sell_gold_value 0 .estate_can_be_provision true
    inventory_item: .type "estate" .id "orphan_mod_essence" .base_stack_limit 1 .purchase_gold_value 0 .sell_gold_value 0 .estate_can_be_provision false
    inventory_item: .type "estate" .id "stored_orphan_essence" .base_stack_limit 1 .purchase_gold_value 0 .sell_gold_value 0 .estate_can_be_provision false
    inventory_item: .type "estate" .id "hero_starter_essence" .base_stack_limit 6 .purchase_gold_value 0 .sell_gold_value 0 .estate_can_be_provision false
    inventory_item: .type "heirloom" .id "town_only_heirloom" .base_stack_limit 1 .purchase_gold_value 0 .sell_gold_value 0
    inventory_item: .type "heirloom" .id "event_cost_only_heirloom" .base_stack_limit 1 .purchase_gold_value 0 .sell_gold_value 0
    inventory_item: .type "gem" .id "local_raid_gem" .base_stack_limit 5 .purchase_gold_value 0 .sell_gold_value 500
    """,
        new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(localProvisionRoot, "hero_starter.provision.json"),
            """
    {
      "raid_starting_hero_class_item_lists": [
        {
          "hero_class": "quantity_reference_probe",
          "item_lists": [
            { "type": "estate", "id": "hero_starter_essence", "amount": 3 }
          ]
        }
      ]
    }
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(localLootRoot, "local.loot.json"),
            """
    {
      "loot_tables": [
        {
          "id": "LOCAL_ACTIVE_LOOT",
          "entries": [
            { "type": "table", "chances": 1, "data": { "table": "LOCAL_NESTED_LOOT" } }
          ]
        },
        {
          "id": "LOCAL_NESTED_LOOT",
          "entries": [
            { "type": "item", "chances": 1, "data": { "type": "estate", "id": "local_mod_essence", "amount": 1 } }
          ]
        },
        {
          "id": "LOCAL_RAID_LOOT",
          "entries": [
            { "type": "item", "chances": 1, "data": { "type": "gem", "id": "local_raid_gem", "amount": 1 } }
          ]
        },
        {
          "id": "LOCAL_ORPHAN_LOOT",
          "entries": [
            { "type": "item", "chances": 1, "data": { "type": "estate", "id": "orphan_mod_essence", "amount": 1 } },
            { "type": "item", "chances": 1, "data": { "type": "estate", "id": "stored_orphan_essence", "amount": 1 } }
          ]
        }
      ]
    }
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(localTownEventsRoot, "quantity_reference.town_events.events.json"),
            """
    {
      "events": [
        {
          "id": "town_currency_probe",
          "data": [
            { "type": "bonus_currency", "string_data": "town_only_heirloom", "number_data": 1 },
            { "type": "event_cost", "string_data": "event_cost_only_heirloom", "number_data": 1 }
          ]
        }
      ]
    }
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(localMonsterRoot, "quantity_reference_probe_A.info.darkest"),
            """
    monster: .id "quantity_reference_probe"
    loot: .code "LOCAL_RAID_LOOT" .count 1
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(localRulesRoot, "comment_only_reference.darkest"),
            "// reward: .type \"estate\" .id \"orphan_mod_essence\"",
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(activeWorkshopDistrictRoot, "quantity_reference.districts.json"),
            """
    {
      "buildings": [
        {
          "name": "cross_mod_item_building",
          "buff_list": [
            { "type": "DistrictSupplyBuffData", "target_inventory": "estate", "item_type": "estate", "item_name": "local_mod_essence", "range_min": 1, "range_max": 1 }
          ]
        }
      ]
    }
    """,
            new UTF8Encoding(false));

        File.WriteAllText(
            Path.Combine(activeWorkshopRoot, "trinkets", "active.entries.trinkets.json"),
            """
    {
      "entries": [
        { "id": "active_workshop_trinket", "rarity": "rare", "price": 3000 },
        { "id": "ambiguous_trinketBE", "rarity": "rare", "price": 9000 }
      ]
    }
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(activeWorkshopRoot, "modfiles.txt"),
            """
    trinkets/active.entries.trinkets.json 100
    trinkets/local.entries.trinkets.json 100
    trinkets/origin_probe.entries.trinkets.json 100
    inventory/base.inventory.system_configs.darkest 100
    inventory/active.currency.inventory.items.darkest 100
    campaign/town/districts/quantity_reference.districts.json 100
    dlc/100_feature_pack/trinkets/shared.entries.trinkets.json 100
    dlc/100_feature_pack/heroes/dlc_shared_hero/dlc_shared_hero.info.darkest 100
    dlc/100_feature_pack/heroes/official_override_hero/official_override_hero.override.darkest 100
    dlc/100_feature_pack/effects/dlc_priority.effects.darkest 100
    dlc/100_feature_pack/shared/quirk/dlc_priority.quirk_library.json 100
    dlc/100_feature_pack/campaign/town_events/dlc_priority.town_events.events.json 100
    dlc/100_feature_pack/features/enabled_feature/trinkets/enabled.entries.trinkets.json 100
    dlc/100_feature_pack/features/enabled_feature/heroes/enabled_dlc_hero/enabled_dlc_hero.info.darkest 100
    backup/heroes/backup_hero/backup_hero.info.darkest 100
    backup/trinkets/backup.entries.trinkets.json 100
    heroes/../backup/heroes/backup_hero/backup_hero.info.darkest 100
    trinkets/../backup/trinkets/backup.entries.trinkets.json 100
    modes/bloodmoon/effects/ignored.effects.darkest 100
    modes/bloodmoon/shared/quirk/ignored.quirk_library.json 100
    modes/bloodmoon/campaign/town_events/ignored.town_events.events.json 100
    dlc/100_feature_pack/features/disabled_feature/heroes/disabled_mod_hero/disabled_mod_hero.info.darkest 100
    dlc/100_feature_pack/features/enabled_feature/heroes/../../disabled_feature/heroes/disabled_mod_hero/disabled_mod_hero.info.darkest 100
    dlc/100_feature_pack/features/enabled_feature/trinkets/../../disabled_feature/trinkets/disabled.entries.trinkets.json 100
    heroes/runtime_hero/runtime_hero.info.darkest 100
    heroes/runtime_hero/runtime_hero.override.darkest 100
    heroes/runtime_hero_patch/runtime_hero.info.darkest 100
    heroes/base_hero/base_hero.info.darkest 100
    heroes/base_hero/base_hero.override.darkest 100
    heroes/local_hero/local_hero.info.darkest 100
    heroes/priority_hero/priority_hero.info.darkest 100
    heroes/priority_hero/priority_hero_A/skin.png 1
    heroes/priority_hero/priority_hero_B/skin.png 1
    upgrades/runtime_hero.upgrades.json 100
    upgrades/building/runtime_hero.upgrades.json 100
    effects/runtime_hero.effects.darkest 100
    effects/priority.effects.darkest 100
    effects/ambiguous_a.effects.darkest 100
    effects/ambiguous_b.effects.darkest 100
    shared/quirk/runtime_hero.quirk_library.json 100
    shared/quirk/source_probe.quirk_library.json 100
    shared/quirk/priority.quirk_library.json 100
    shared/quirk/buff_conflicts.quirk_library.json 100
    shared/quirk/semantic_priority_bottom.quirk_library.json 100
    shared/quirk/ambiguous_a.quirk_library.json 100
    shared/quirk/ambiguous_b.quirk_library.json 100
    shared/buffs/non_hp_a.buffs.json 100
    shared/buffs/non_hp_b.buffs.json 100
    shared/buffs/max_hp_a.buffs.json 100
    shared/buffs/max_hp_b.buffs.json 100
    campaign/town_events/runtime_hero.town_events.events.json 100
    campaign/town_events/local_hero.town_events.events.json 100
    campaign/town_events/ambiguous_a.town_events.events.json 100
    campaign/town_events/ambiguous_b.town_events.events.json 100
    localization/111_english.loc2 100
    localization/111_schinese.loc2 100
    localization/ignored_schinese.loc2.unused 100
    localization/windows/ignored_schinese.loc2 100
    localization/ignored.string_table.xml.unused 100
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(activeWorkshopRoot, "trinkets", "local.entries.trinkets.json"),
            """
    {
      "entries": [
        { "id": "local_mod_trinket", "rarity": "very_rare", "price": 9900 }
      ]
    }
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(activeWorkshopRoot, "trinkets", "origin_probe.entries.trinkets.json"),
            """
    {
      "entries": [
        { "id": "origin_probe_trinket", "rarity": "rare", "price": 500 },
        { "id": "mod_only_in_overridden_trinket_file", "rarity": "common", "price": 250 }
      ]
    }
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(activeWorkshopDlcTrinketRoot, "shared.entries.trinkets.json"),
            """
    {
      "entries": [
        { "id": "dlc_shared_trinket", "rarity": "very_rare", "price": 8800 }
      ]
    }
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(activeWorkshopDlcRoot, "heroes", "dlc_shared_hero", "dlc_shared_hero.info.darkest"),
            """
    combat_skill: .id "modded_dlc_skill" .level 0 .effect "DLC Priority Effect" .generation_guaranteed true
    generation: .is_generation_enabled true .number_of_random_combat_skills 1
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(activeWorkshopOfficialOverrideHeroRoot, "official_override_hero.override.darkest"),
            """
    generation: .is_generation_enabled true
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(activeWorkshopDlcRoot, "effects", "dlc_priority.effects.darkest"),
            """
    effect: .name "DLC Priority Effect" .target "performer" .chance 100% .disease "dlc_top_quirk" .on_hit true
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(activeWorkshopDlcRoot, "shared", "quirk", "dlc_priority.quirk_library.json"),
            """
    {
      "quirks": [
        { "id": "dlc_top_quirk", "random_chance": 0, "is_positive": true }
      ]
    }
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(activeWorkshopDlcRoot, "campaign", "town_events", "dlc_priority.town_events.events.json"),
            """
    {
      "events": [
        {
          "id": "recruit_dlc_shared_hero",
          "data": [
            { "type": "bonus_recruit", "string_data": "dlc_shared_hero", "number_data": 6.0 }
          ]
        }
      ]
    }
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(activeWorkshopEnabledDlcFeatureRoot, "trinkets", "enabled.entries.trinkets.json"),
            """
    { "entries": [ { "id": "enabled_dlc_trinket", "rarity": "very_rare", "price": 8200 } ] }
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(activeWorkshopEnabledDlcFeatureRoot, "heroes", "enabled_dlc_hero", "enabled_dlc_hero.info.darkest"),
            """
    combat_skill: .id "modded_enabled_dlc_skill" .level 0
    generation: .is_generation_enabled true .number_of_random_combat_skills 1
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(ignoredBackupRoot, "heroes", "backup_hero", "backup_hero.info.darkest"),
            """
    combat_skill: .id "backup_skill" .level 0
    generation: .is_generation_enabled true .number_of_random_combat_skills 1
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(ignoredBackupRoot, "trinkets", "backup.entries.trinkets.json"),
            """
    { "entries": [ { "id": "backup_trinket", "rarity": "common", "price": 1 } ] }
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(ignoredModeRoot, "effects", "ignored.effects.darkest"),
            """
    effect: .name "Priority Effect" .target "performer" .chance 100% .disease "ignored_mode_quirk" .on_hit true
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(ignoredModeRoot, "shared", "quirk", "ignored.quirk_library.json"),
            """
    {
      "quirks": [
        { "id": "priority_top_quirk", "random_chance": 0, "is_positive": false }
      ]
    }
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(ignoredModeRoot, "campaign", "town_events", "ignored.town_events.events.json"),
            """
    {
      "events": [
        {
          "id": "recruit_local_hero",
          "data": [
            { "type": "bonus_recruit", "string_data": "local_hero", "number_data": 99.0 }
          ]
        }
      ]
    }
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(ignoredDisabledFeatureHeroRoot, "disabled_mod_hero.info.darkest"),
            """
    combat_skill: .id "disabled_mod_skill" .level 0
    generation: .is_generation_enabled true .number_of_random_combat_skills 1
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(ignoredDisabledFeatureTrinketRoot, "disabled.entries.trinkets.json"),
            """
    { "entries": [ { "id": "disabled_mod_trinket", "rarity": "common", "price": 1 } ] }
    """,
            new UTF8Encoding(false));

        File.WriteAllText(
            Path.Combine(disabledWorkshopRoot, "trinkets", "disabled.entries.trinkets.json"),
            """
    {
      "entries": [
        { "id": "disabled_workshop_trinket", "rarity": "rare", "price": 3000 }
      ]
    }
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(disabledWorkshopRoot, "modfiles.txt"),
            """
    trinkets/disabled.entries.trinkets.json 100
    inventory/base.inventory.system_configs.darkest 100
    heroes/disabled_hero/disabled_hero.info.darkest 100
    campaign/town_events/disabled_hero.town_events.events.json 100
    """,
            new UTF8Encoding(false));

        File.WriteAllText(
            Path.Combine(localModRoot, "project.xml"),
            """
    <?xml version="1.0" encoding="UTF-8"?>
    <project>
      <Title>Local Test Mod</Title>
    </project>
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(localModRoot, "trinkets", "local.entries.trinkets.json"),
            """
    {
      "entries": [
        { "id": "local_mod_trinket", "rarity": "uncommon", "price": 1500 }
      ]
    }
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(localBackupRoot, "project.xml"),
            """
    <?xml version="1.0" encoding="UTF-8"?>
    <project>
      <Title>Local Test Mod</Title>
    </project>
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(externalLocalModRoot, "project.xml"),
            """
    <?xml version="1.0" encoding="UTF-8"?>
    <project>
      <Title>External Test Mod</Title>
    </project>
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(externalLocalModRoot, "trinkets", "external.entries.trinkets.json"),
            """
    {
      "entries": [
        { "id": "external_local_trinket", "rarity": "rare", "price": 2750 }
      ]
    }
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(externalLocalLocalizationRoot, "external.string_table.xml"),
            """
    <?xml version="1.0" encoding="UTF-8"?>
    <root>
      <language id="english">
        <entry id="str_inventory_title_trinketexternal_local_trinket"><![CDATA[External Contract Trinket]]></entry>
      </language>
      <language id="schinese">
        <entry id="str_inventory_title_trinketexternal_local_trinket"><![CDATA[外部契约饰品]]></entry>
      </language>
    </root>
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(ambiguousDirectLocalModRoot, "project.xml"),
            """
    <?xml version="1.0" encoding="UTF-8"?>
    <project>
      <Title>Local Test Mod</Title>
    </project>
    """,
            new UTF8Encoding(false));

        File.WriteAllText(
            Path.Combine(baseHeroRoot, "base_hero.info.darkest"),
            """
    combat_skill: .id "base_strike" .level 0
    skill_selection: .can_select_combat_skills true .number_of_selected_combat_skills_max 4
    generation: .is_generation_enabled true .number_of_positive_quirks_min 1 .number_of_positive_quirks_max 2 .number_of_negative_quirks_min 1 .number_of_negative_quirks_max 2 .number_of_random_combat_skills 4
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(activeRuntimeHeroRoot, "runtime_hero.info.darkest"),
            """
    weapon: .name "runtime_hero_weapon_0" .dmg 4 8
    weapon: .name "runtime_hero_weapon_1" .dmg 5 9 .upgradeRequirementCode 0
    armour: .name "runtime_hero_armour_0" .def 0% .prot 0 .hp 18 .spd 0
    armour: .name "runtime_hero_armour_1" .def 5% .prot 0 .hp 22 .spd 0 .upgradeRequirementCode 0
    combat_skill: .id "runtime_strike" .level 0 .effect "Grant Runtime Quirk" .generation_guaranteed true
    combat_skill: .id "runtime_guard" .level 0 .effect "Grant Runtime Quirk" .valid_modes target .target_effects "Priority Effect" .generation_guaranteed true
    quirk_modifier: .incompatible_class_ids runtime_excluded_a runtime_excluded_b
    skill_selection: .can_select_combat_skills true .number_of_selected_combat_skills_max 2
    generation: .is_generation_enabled true .number_of_positive_quirks_min 0 .number_of_positive_quirks_max 0 .number_of_negative_quirks_min 0 .number_of_negative_quirks_max 0 .number_of_random_combat_skills 2
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(activeRuntimeHeroRoot, "runtime_hero.override.darkest"),
            """
    weapon: .name "runtime_hero_weapon_1" .dmg 99 101
    armour: .name "runtime_hero_armour_0" .def 10%
    armour: .name "runtime_hero_armour_1" .hp 25
    combat_skill: .id "runtime_guard" .level 0 .effect .generation_guaranteed false
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(activeRuntimeHeroDuplicateRoot, "runtime_hero.info.darkest"),
            """
    weapon: .name "runtime_hero_weapon_0" .dmg 4 8
    weapon: .name "runtime_hero_weapon_1" .dmg 5 9 .upgradeRequirementCode 0
    armour: .name "runtime_hero_armour_0" .def 0% .prot 0 .hp 18 .spd 0
    armour: .name "runtime_hero_armour_1" .def 5% .prot 0 .hp 22 .spd 0 .upgradeRequirementCode 0
    combat_skill: .id "runtime_guard" .level 0 .effect "Grant Runtime Quirk" .valid_modes target .target_effects "Priority Effect" .generation_guaranteed true
    combat_skill: .id "runtime_strike" .level 0 .effect "Grant Runtime Quirk" .generation_guaranteed true
    quirk_modifier: .incompatible_class_ids runtime_excluded_b runtime_excluded_a
    skill_selection: .can_select_combat_skills true .number_of_selected_combat_skills_max 2
    generation: .is_generation_enabled true .number_of_positive_quirks_min 0 .number_of_positive_quirks_max 0 .number_of_negative_quirks_min 0 .number_of_negative_quirks_max 0 .number_of_random_combat_skills 2
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(activeWorkshopUpgradeRoot, "runtime_hero.upgrades.json"),
            """
    {
      "trees": [
        {
          "id": "runtime_hero.weapon",
          "tags": ["weapon", "first_level_not_upgrade"],
          "requirements": [
            { "code": "0", "prerequisite_resolve_level": 2 }
          ]
        },
        {
          "id": "runtime_hero.armour",
          "tags": ["armour", "first_level_not_upgrade"],
          "requirements": [
            { "code": "0", "prerequisite_resolve_level": 2 }
          ]
        }
      ]
    }
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(activeWorkshopBuildingUpgradeRoot, "runtime_hero.upgrades.json"),
            """
    {
      "trees": [
        {
          "id": "runtime_hero.weapon",
          "tags": ["weapon"],
          "requirements": [
            { "code": "0", "prerequisite_resolve_level": 6 }
          ]
        }
      ]
    }
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(activeOverrideHeroRoot, "base_hero.info.darkest"),
            """
    combat_skill: .id "overridden_strike" .level 0
    skill_selection: .can_select_combat_skills true .number_of_selected_combat_skills_max 1
    generation: .is_generation_enabled true .number_of_positive_quirks_min 0 .number_of_positive_quirks_max 0 .number_of_negative_quirks_min 0 .number_of_negative_quirks_max 0 .number_of_random_combat_skills 1
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(activeOverrideHeroRoot, "base_hero.override.darkest"),
            """
    generation: .number_of_positive_quirks_min 4
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(activeWorkshopPriorityHeroRoot, "priority_hero.info.darkest"),
            """
    armour: .name "priority_hero_armour_0" .def 0% .prot 0 .hp 17 .spd 0
    combat_skill: .id "workshop_priority_skill" .level 0
    generation: .is_generation_enabled true .number_of_random_combat_skills 1
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(localPriorityHeroRoot, "priority_hero.info.darkest"),
            """
    armour: .name "priority_hero_armour_0" .def 0% .prot 0 .hp 21 .spd 0
    combat_skill: .id "local_priority_skill" .level 0
    generation: .is_generation_enabled true .number_of_random_combat_skills 1
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(activeWorkshopLocalHeroRoot, "local_hero.info.darkest"),
            """
    combat_skill: .id "workshop_lower_priority_skill" .level 0 .effect "Priority Effect" .generation_guaranteed true
    skill_selection: .can_select_combat_skills true .number_of_selected_combat_skills_max 1
    generation: .is_generation_enabled true .number_of_positive_quirks_min 0 .number_of_positive_quirks_max 0 .number_of_negative_quirks_min 0 .number_of_negative_quirks_max 0 .number_of_random_combat_skills 1
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(activeWorkshopRoot, "effects", "runtime_hero.effects.darkest"),
            """
    effect: .name "Grant Runtime Quirk" .target "performer" .chance 100% .disease "runtime_fixed_quirk" .on_hit true
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(activeWorkshopRoot, "effects", "priority.effects.darkest"),
            """
    effect: .name "Priority Effect" .target "performer" .chance 100% .disease "priority_bottom_quirk" .on_hit true
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(activeWorkshopRoot, "effects", "ambiguous_a.effects.darkest"),
            """
    effect: .name "Ambiguous Effect" .target "performer" .chance 100% .disease "ambiguous_quirk" .on_hit true
    effect: .name "Ordinary Duplicate" .target "target" .chance 100% .health_damage 1 .on_hit true
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(activeWorkshopRoot, "effects", "ambiguous_b.effects.darkest"),
            """
    effect: .name "Ambiguous Effect" .target "performer" .chance 100% .disease "runtime_fixed_quirk" .on_hit true
    effect: .name "Ordinary Duplicate" .target "target_group" .chance 100% .health_damage 2 .on_hit true
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(activeWorkshopRoot, "shared", "quirk", "runtime_hero.quirk_library.json"),
            """
    {
      "quirks": [
        { "id": "runtime_fixed_quirk", "random_chance": 0, "is_positive": true }
      ]
    }
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(activeWorkshopRoot, "shared", "quirk", "source_probe.quirk_library.json"),
            """
    {
      "quirks": [
        { "id": "source_probe_quirk", "random_chance": 1, "is_positive": true, "is_disease": false, "buffs": [] },
        { "id": "mod_only_in_overridden_quirk_file", "random_chance": 0, "is_positive": true, "is_disease": false, "buffs": [] }
      ]
    }
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(activeWorkshopRoot, "shared", "quirk", "priority.quirk_library.json"),
            """
    {
      "quirks": [
        { "id": "priority_bottom_quirk", "random_chance": 0, "is_positive": true },
        { "id": "non_hp_conflict_quirk", "random_chance": 1, "is_positive": true, "buffs": ["CONFLICT_ACC"] },
        { "id": "max_hp_conflict_quirk", "random_chance": 1, "is_positive": true, "buffs": ["CONFLICT_MAXHP"] }
      ]
    }
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(activeWorkshopRoot, "shared", "quirk", "buff_conflicts.quirk_library.json"),
            """
    {
      "quirks": [
        { "id": "non_hp_conflict_quirk", "random_chance": 1, "is_positive": true, "buffs": ["CONFLICT_ACC"] },
        { "id": "max_hp_conflict_quirk", "random_chance": 1, "is_positive": true, "buffs": ["CONFLICT_MAXHP"] }
      ]
    }
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(activeWorkshopRoot, "shared", "quirk", "semantic_priority_bottom.quirk_library.json"),
            """
    {
      "quirks": [
        { "id": "semantic_priority_quirk", "random_chance": 1, "is_positive": false }
      ]
    }
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(localModRoot, "shared", "quirk", "semantic_priority_top.quirk_library.json"),
            """
    {
      "quirks": [
        { "id": "semantic_priority_quirk", "random_chance": 1, "is_positive": true }
      ]
    }
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(activeWorkshopRoot, "shared", "quirk", "ambiguous_a.quirk_library.json"),
            """
    {
      "quirks": [
        { "id": "ambiguous_quirk", "random_chance": 0, "is_positive": true },
        { "id": "identical_cross_path_quirk", "random_chance": 1, "is_positive": true, "buffs": [] },
        { "id": "evolution_conflict_quirk", "random_chance": 1, "is_positive": false, "is_disease": true, "buffs": [], "evolution_duration_min": 30, "evolution_duration_max": 60, "evolution_class_id": "evolution_target_a" },
        { "id": "identical_evolution_quirk", "random_chance": 1, "is_positive": false, "is_disease": true, "buffs": [], "evolution_duration_min": 30, "evolution_duration_max": 60, "evolution_class_id": "same_target" }
      ]
    }
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(activeWorkshopRoot, "shared", "quirk", "ambiguous_b.quirk_library.json"),
            """
    {
      "quirks": [
        { "id": "ambiguous_quirk", "random_chance": 0, "is_positive": false },
        { "id": "identical_cross_path_quirk", "random_chance": 1, "is_positive": true, "buffs": [] },
        { "id": "evolution_conflict_quirk", "random_chance": 1, "is_positive": false, "is_disease": true, "buffs": [], "evolution_duration_min": 90, "evolution_duration_max": 120, "evolution_class_id": "evolution_target_b" },
        { "id": "identical_evolution_quirk", "random_chance": 1, "is_positive": false, "is_disease": true, "buffs": [], "evolution_duration_min": 30.0, "evolution_duration_max": 60.0, "evolution_class_id": "same_target" }
      ]
    }
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(activeWorkshopBuffRoot, "non_hp_a.buffs.json"),
            """
    { "buffs": [
      { "id": "CONFLICT_ACC", "stat_type": "combat_stat_add", "stat_sub_type": "attack_rating", "amount": 3, "rule_type": "always", "is_false_rule": false }
    ] }
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(activeWorkshopBuffRoot, "non_hp_b.buffs.json"),
            """
    { "buffs": [
      { "id": "CONFLICT_ACC", "stat_type": "combat_stat_add", "stat_sub_type": "attack_rating", "amount": 5, "rule_type": "always", "is_false_rule": false }
    ] }
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(activeWorkshopBuffRoot, "max_hp_a.buffs.json"),
            """
    { "buffs": [
      { "id": "CONFLICT_MAXHP", "stat_type": "combat_stat_multiply", "stat_sub_type": "max_hp", "amount": 0.1, "rule_type": "always", "is_false_rule": false }
    ] }
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(activeWorkshopBuffRoot, "max_hp_b.buffs.json"),
            """
    { "buffs": [
      { "id": "CONFLICT_MAXHP", "stat_type": "combat_stat_multiply", "stat_sub_type": "max_hp", "amount": 0.2, "rule_type": "always", "is_false_rule": false }
    ] }
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(activeWorkshopRoot, "campaign", "town_events", "runtime_hero.town_events.events.json"),
            """
    {
      "events": [
        {
          "id": "recruit_runtime_hero",
          "data": [
            { "type": "bonus_recruit", "string_data": "runtime_hero", "number_data": 1.0 }
          ]
        }
      ]
    }
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(activeWorkshopRoot, "campaign", "town_events", "local_hero.town_events.events.json"),
            """
    {
      "events": [
        {
          "id": "recruit_local_hero",
          "data": [
            { "type": "bonus_recruit", "string_data": "local_hero", "number_data": 9.0 }
          ]
        }
      ]
    }
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(activeWorkshopRoot, "campaign", "town_events", "ambiguous_a.town_events.events.json"),
            """
    {
      "events": [
        {
          "id": "ambiguous_recruit",
          "data": [
            { "type": "bonus_recruit", "string_data": "runtime_hero", "number_data": 1.0 }
          ]
        },
        {
          "id": "identical_recruit",
          "data": [
            { "type": "bonus_recruit", "string_data": "event_only_hero", "number_data": 1.0 }
          ]
        }
      ]
    }
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(activeWorkshopRoot, "campaign", "town_events", "ambiguous_b.town_events.events.json"),
            """
    {
      "events": [
        {
          "id": "ambiguous_recruit",
          "data": [
            { "type": "bonus_recruit", "string_data": "local_hero", "number_data": 7.0 }
          ]
        },
        {
          "id": "identical_recruit",
          "data": [
            { "type": "bonus_recruit", "string_data": "event_only_hero", "number_data": 1.0 }
          ]
        }
      ]
    }
    """,
            new UTF8Encoding(false));

        File.WriteAllText(
            Path.Combine(disabledHeroRoot, "disabled_hero.info.darkest"),
            """
    combat_skill: .id "disabled_strike" .level 0
    generation: .is_generation_enabled true .number_of_random_combat_skills 1
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(disabledWorkshopRoot, "campaign", "town_events", "disabled_hero.town_events.events.json"),
            """
    {
      "events": [
        {
          "id": "recruit_disabled_hero",
          "data": [
            { "type": "bonus_recruit", "string_data": "disabled_hero", "number_data": 1.0 }
          ]
        }
      ]
    }
    """,
            new UTF8Encoding(false));

        File.WriteAllText(
            Path.Combine(localHeroRoot, "local_hero.info.darkest"),
            """
    weapon: .name "local_hero_weapon_0" .dmg 4 8
    weapon: .name "local_hero_weapon_1" .dmg 5 9 .upgradeRequirementCode 0
    weapon: .name "local_hero_weapon_2" .dmg 6 10 .upgradeRequirementCode 1
    weapon: .name "local_hero_weapon_3" .dmg 7 11 .upgradeRequirementCode 2
    weapon: .name "local_hero_weapon_4" .dmg 8 12 .upgradeRequirementCode 3
    armour: .name "local_hero_armour_0" .def 0% .prot 0 .hp 20 .spd 0
    armour: .name "local_hero_armour_1" .def 5% .prot 0 .hp 24 .spd 0 .upgradeRequirementCode 0
    armour: .name "local_hero_armour_2" .def 10% .prot 0 .hp 28 .spd 0 .upgradeRequirementCode 1
    armour: .name "local_hero_armour_3" .def 15% .prot 0 .hp 32 .spd 0 .upgradeRequirementCode 2
    armour: .name "local_hero_armour_4" .def 20% .prot 0 .hp 36 .spd 0 .upgradeRequirementCode 3
    combat_skill: .id "local_skill" .level 0 .effect "Priority Effect" .generation_guaranteed true
    combat_skill: .id "local_skill" .level 1
    combat_skill: .id "local_skill_two" .level 0
    combat_skill: .id "local_skill_two" .level 1
    skill_selection: .can_select_combat_skills true
    skill_selection: .number_of_selected_combat_skills_max 1
    generation: .is_generation_enabled true .number_of_positive_quirks_min 1
    generation: .number_of_positive_quirks_max 3 .number_of_negative_quirks_min 0
    generation: .number_of_negative_quirks_max 1 .number_of_class_specific_camping_skills 1 .number_of_shared_camping_skills 1 .number_of_random_combat_skills 2
    quirk_modifier: .incompatible_class_ids excluded_quirk
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(localHeroUpgradeRoot, "local_hero.upgrades.json"),
            """
    {
      "trees": [
        {
          "id": "local_hero.weapon",
          "tags": ["weapon", "first_level_not_upgrade"],
          "requirements": [
            { "code": "0", "prerequisite_resolve_level": 1 },
            { "code": "1", "prerequisite_resolve_level": 2 },
            { "code": "2", "prerequisite_resolve_level": 3 },
            { "code": "3", "prerequisite_resolve_level": 5 }
          ]
        },
        {
          "id": "local_hero.armour",
          "tags": ["armour", "first_level_not_upgrade"],
          "requirements": [
            { "code": "0", "prerequisite_resolve_level": 1 },
            { "code": "1", "prerequisite_resolve_level": 2 },
            { "code": "2", "prerequisite_resolve_level": 3 },
            { "code": "3", "prerequisite_resolve_level": 5 }
          ]
        },
        {
          "id": "local_hero.local_skill",
          "tags": ["combat_skill"],
          "requirements": [
            { "code": "0", "prerequisite_resolve_level": 0 },
            { "code": "A", "prerequisite_resolve_level": 1 },
            { "code": "b", "prerequisite_resolve_level": 2 },
            { "code": "B", "prerequisite_resolve_level": 3 },
            { "code": "c", "prerequisite_resolve_level": 5 }
          ]
        },
        {
          "id": "local_hero.local_skill_two",
          "tags": ["combat_skill"],
          "requirements": [
            { "code": "0", "prerequisite_resolve_level": 0 },
            { "code": "A", "prerequisite_resolve_level": 1 },
            { "code": "b", "prerequisite_resolve_level": 2 },
            { "code": "B", "prerequisite_resolve_level": 3 },
            { "code": "c", "prerequisite_resolve_level": 5 }
          ]
        }
      ]
    }
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(localBuildingUpgradeRoot, "local_hero.upgrades.json"),
            """
    {
      "trees": [
        {
          "id": "local_hero.weapon",
          "tags": ["weapon"],
          "requirements": [
            { "code": "0", "prerequisite_resolve_level": 6 }
          ]
        }
      ]
    }
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(compatibleUpgradeHeroRoot, "compatible_upgrade_hero.info.darkest"),
            """
    weapon: .name "compatible_upgrade_hero_weapon_0" .dmg 4 8
    weapon: .name "compatible_upgrade_hero_weapon_1" .dmg 5 9 .upgradeRequirementCode 0
    armour: .name "compatible_upgrade_hero_armour_0" .def 0% .prot 0 .hp 19 .spd 0
    armour: .name "compatible_upgrade_hero_armour_1" .def 5% .prot 0 .hp 23 .spd 0 .upgradeRequirementCode 0
    combat_skill: .id "scaling_strike" .level 0
    combat_skill: .id "scaling_strike" .level 1
    combat_skill: .id "fixed_command" .level 0
    combat_skill: .id "implicit_command" .level 0
    combat_skill: .id "implicit_command" .level 1
    skill_selection: .can_select_combat_skills true .number_of_selected_combat_skills_max 1
    generation: .is_generation_enabled true .number_of_positive_quirks_min 0 .number_of_positive_quirks_max 0 .number_of_negative_quirks_min 0 .number_of_negative_quirks_max 0 .number_of_random_combat_skills 2
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(localHeroUpgradeRoot, "compatible_upgrade_hero.upgrades.json"),
            """
    {
      "trees": [
        {
          "id": "compatible_upgrade_hero.weapon",
          "tags": ["weapon", "first_level_not_upgrade"],
          "requirements": [
            { "code": "0", "prerequisite_resolve_level": 1 }
          ]
        },
        {
          "id": "compatible_upgrade_hero.armour",
          "tags": ["armour", "first_level_not_upgrade"],
          "requirements": [
            { "code": "0", "prerequisite_resolve_level": 1 }
          ]
        },
        {
          "id": "compatible_upgrade_hero.scaling_strike",
          "tags": ["combat_skill"],
          "requirements": [
            { "code": "0", "prerequisite_resolve_level": 0 },
            { "code": "1", "prerequisite_resolve_level": 1 }
          ]
        }
      ]
    }
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(localNestedHeroUpgradeRoot, "compatible_upgrade_hero.upgrades.json"),
            """
    {
      "trees": [
        {
          "id": "compatible_upgrade_hero.weapon",
          "tags": ["weapon", "first_level_not_upgrade"],
          "requirements": [
            { "code": "0", "prerequisite_resolve_level": 1 }
          ]
        },
        {
          "id": "compatible_upgrade_hero.armour",
          "tags": ["armour", "first_level_not_upgrade"],
          "requirements": [
            { "code": "0", "prerequisite_resolve_level": 1 }
          ]
        },
        {
          "id": "compatible_upgrade_hero.legacy_strike_name",
          "tags": ["combat_skill"],
          "requirements": [
            { "code": "0", "prerequisite_resolve_level": 0 },
            { "code": "1", "prerequisite_resolve_level": 1 }
          ]
        }
      ]
    }
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(localHeroUpgradeRoot, "case_probe_hero.upgrades.json"),
            """
    {
      "trees": [
        {
          "id": "case_probe_hero.case_skill",
          "tags": ["combat_skill"],
          "requirements": [
            { "code": "a", "prerequisite_resolve_level": 0 }
          ]
        }
      ]
    }
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(localNestedHeroUpgradeRoot, "case_probe_hero.upgrades.json"),
            """
    {
      "trees": [
        {
          "id": "case_probe_hero.case_skill",
          "tags": ["combat_skill"],
          "requirements": [
            { "code": "A", "prerequisite_resolve_level": 0 }
          ]
        }
      ]
    }
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(localLocalizationRoot, "local_hero.string_table.xml"),
            """
    <?xml version="1.0" encoding="UTF-8"?>
    <root>
      <language id="english">
        <entry id="hero_class_name_local_hero"><![CDATA[Local Contract Hero]]></entry>
        <entry id="str_inventory_title_estatelocal_mod_essence"><![CDATA[Local Essence]]></entry>
        <entry id="str_quirk_name_priority_top_quirk"><![CDATA[Priority Legacy]]></entry>
      </language>
      <language id="schinese">
        <entry id="hero_class_name_local_hero"><![CDATA[{colour_start|G2}本地契约英雄{colour_end}]]></entry>
        <entry id="str_inventory_title_estatelocal_mod_essence"><![CDATA[本地精华]]></entry>
        <entry id="str_quirk_name_priority_top_quirk"><![CDATA[优先传承]]></entry>
      </language>
    </root>
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(localLocalizationRoot, "lenient.string_table.xml"),
            """
    <?xml version="1.1" encoding="UTF-8"?>
    <root>
      <!-- a Mod-style illegal -- comment -->
      <language id="english">
        <entry id="hero_name_99"><![CDATA[Contract Lenient]]></entry>
        <entry id="str_inventory_title_trinketfire_probe"><![CDATA[Lenient Fire Probe]]></entry>
      </language>
      <language id="schinese">
        <entry id="str_inventory_title_trinketfire_probe"><![CDATA[容错火焰探针]]></einntry>
      </language>
    </root>
    """,
            new UTF8Encoding(false));
        WriteLoc2(
            Path.Combine(localLocalizationRoot, "LocalTest_english.loc2"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["str_inventory_title_trinketlocal_mod_trinket"] = "Compiled Local Trinket",
                ["hero_class_name_local_hero"] = "Compiled Local Hero",
                ["str_quirk_name_priority_top_quirk"] = "Compiled Priority Legacy"
            });
        var duplicateZeroHashPath = Path.Combine(localLocalizationRoot, "legal_duplicate_zero_english.loc2");
        WriteLoc2(
            duplicateZeroHashPath,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["str_inventory_title_trinketlocal_mod_trinket"] = "Compiled Local Trinket",
                ["duplicate_zero_sentinel_one"] = "sentinel one",
                ["duplicate_zero_sentinel_two"] = "sentinel two"
            });
        SetLoc2Hash(duplicateZeroHashPath, "duplicate_zero_sentinel_one", 0);
        SetLoc2Hash(duplicateZeroHashPath, "duplicate_zero_sentinel_two", 0);
        var partiallyBrokenBoundsPath = Path.Combine(localLocalizationRoot, "partially_broken_bounds_english.loc2");
        WriteLoc2(
            partiallyBrokenBoundsPath,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["str_inventory_title_trinketlocal_mod_trinket"] = "Corrupt File Must Be Ignored",
                ["unrequested_corrupt_bounds"] = "broken"
            });
        CorruptLoc2ValueOffset(partiallyBrokenBoundsPath, "unrequested_corrupt_bounds");
        var partiallyBrokenUtf8Path = Path.Combine(localLocalizationRoot, "partially_broken_utf8_english.loc2");
        WriteLoc2(
            partiallyBrokenUtf8Path,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["str_inventory_title_trinketlocal_mod_trinket"] = "Compiled Local Trinket",
                ["unrequested_corrupt_utf8"] = "broken"
            });
        CorruptLoc2ValueUtf8(partiallyBrokenUtf8Path, "unrequested_corrupt_utf8");
        var partiallyBrokenNulPath = Path.Combine(localLocalizationRoot, "partially_broken_nul_english.loc2");
        WriteLoc2(
            partiallyBrokenNulPath,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["str_inventory_title_trinketlocal_mod_trinket"] = "Corrupt File Must Be Ignored",
                ["unrequested_corrupt_nul"] = "broken"
            });
        CorruptLoc2ValueTerminator(partiallyBrokenNulPath, "unrequested_corrupt_nul");
        var partiallyBrokenZeroLengthPath = Path.Combine(localLocalizationRoot, "partially_broken_zero_length_english.loc2");
        WriteLoc2(
            partiallyBrokenZeroLengthPath,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["str_inventory_title_trinketlocal_mod_trinket"] = "Corrupt File Must Be Ignored",
                ["unrequested_corrupt_zero_length"] = "broken"
            });
        CorruptLoc2ValueLength(partiallyBrokenZeroLengthPath, "unrequested_corrupt_zero_length", 0);
        WriteLoc2Raw(
            Path.Combine(localLocalizationRoot, "LocalTest_schinese.loc2"),
            new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                ["str_inventory_title_trinketlocal_mod_trinket"] = EncodeLoc2ColourOpenOnly("编译本地饰品"),
                ["hero_class_name_local_hero"] = ConcatenateBytes(
                    Encoding.UTF8.GetBytes("</c>"),
                    Encoding.UTF8.GetBytes("编译本地英雄")),
                ["str_quirk_name_priority_top_quirk"] = Encoding.UTF8.GetBytes("编译优先传承")
            });
        File.WriteAllBytes(
            Path.Combine(localLocalizationRoot, "broken_english.loc2"),
            [0x01, 0x02, 0x03]);
        WriteLoc2Raw(
            Path.Combine(activeWorkshopLocalizationRoot, "111_english.loc2"),
            new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                ["str_inventory_title_trinketactive_workshop_trinket"] = ConcatenateBytes(
                    Encoding.UTF8.GetBytes("Compiled "),
                    EncodeLoc2Colour("Workshop"),
                    Encoding.UTF8.GetBytes(" Trinket"))
            });
        WriteLoc2(
            Path.Combine(activeWorkshopLocalizationRoot, "111_schinese.loc2"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["str_inventory_title_trinket_active_workshop_trinket"] = "编译工坊饰品"
            });
        File.WriteAllText(
            Path.Combine(activeWorkshopLocalizationRoot, "unlisted_authoring.string_table.xml"),
            """
    <?xml version="1.0" encoding="UTF-8"?>
    <root>
      <language id="english">
        <entry id="hero_class_name_runtime_hero"><![CDATA[Unlisted Runtime Hero]]></entry>
      </language>
      <language id="schinese">
        <entry id="hero_class_name_runtime_hero"><![CDATA[未列清单英雄]]></entry>
      </language>
    </root>
    """,
            new UTF8Encoding(false));
        File.WriteAllBytes(
            Path.Combine(activeWorkshopLocalizationRoot, "ignored_schinese.loc2.unused"),
            [0x01, 0x02, 0x03]);
        var ignoredPlatformLocalizationRoot = Path.Combine(activeWorkshopLocalizationRoot, "windows");
        Directory.CreateDirectory(ignoredPlatformLocalizationRoot);
        WriteLoc2(
            Path.Combine(ignoredPlatformLocalizationRoot, "ignored_schinese.loc2"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["str_inventory_title_trinketactive_workshop_trinket"] = "不应读取的平台名称"
            });
        File.WriteAllText(
            Path.Combine(activeWorkshopLocalizationRoot, "ignored.string_table.xml.unused"),
            """
    <?xml version="1.0" encoding="UTF-8"?>
    <root>
      <language id="english">
        <entry id="hero_name_unused"><![CDATA[Ignored Unused Name]]></entry>
      </language>
    </root>
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(localModRoot, "effects", "priority.effects.darkest"),
            """
    effect: .name "Priority Effect" .target "performer" .chance 100% .disease "priority_top_quirk" .on_hit true
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(localModRoot, "shared", "quirk", "priority.quirk_library.json"),
            """
    {
      "quirks": [
        { "id": "priority_top_quirk", "random_chance": 0, "is_positive": true }
      ]
    }
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(localModRoot, "campaign", "town_events", "local_hero.town_events.events.json"),
            """
    {
      "events": [
        {
          "id": "recruit_local_hero",
          "data": [
            { "type": "bonus_recruit", "string_data": "local_hero", "number_data": 2.0 }
          ]
        }
      ]
    }
    """,
            new UTF8Encoding(false));

        File.WriteAllText(
            Path.Combine(dlcPackageRoot, "trinkets", "shared.entries.trinkets.json"),
            """
    { "entries": [ { "id": "dlc_shared_trinket", "rarity": "rare", "price": 2000 } ] }
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(dlcPackageRoot, "heroes", "dlc_shared_hero", "dlc_shared_hero.info.darkest"),
            """
    combat_skill: .id "shared_dlc_skill" .level 0 .effect "DLC Priority Effect" .generation_guaranteed true
    generation: .is_generation_enabled true .number_of_random_combat_skills 1
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(dlcOverrideHeroRoot, "official_override_hero.info.darkest"),
            """
    combat_skill: .id "official_override_skill" .level 0
    generation: .is_generation_enabled false .number_of_random_combat_skills 1
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(dlcOverrideHeroRoot, "official_override_hero.override.darkest"),
            """
    generation: .is_generation_enabled true
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(dlcPackageRoot, "effects", "dlc_priority.effects.darkest"),
            """
    effect: .name "DLC Priority Effect" .target "performer" .chance 100% .disease "dlc_bottom_quirk" .on_hit true
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(dlcPackageRoot, "shared", "quirk", "dlc_priority.quirk_library.json"),
            """
    {
      "quirks": [
        { "id": "dlc_bottom_quirk", "random_chance": 0, "is_positive": true }
      ]
    }
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(dlcPackageRoot, "campaign", "town_events", "dlc_priority.town_events.events.json"),
            """
    {
      "events": [
        {
          "id": "recruit_dlc_shared_hero",
          "data": [
            { "type": "bonus_recruit", "string_data": "dlc_shared_hero", "number_data": 1.0 }
          ]
        }
      ]
    }
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(enabledDlcFeatureRoot, "trinkets", "enabled.entries.trinkets.json"),
            """
    { "entries": [ { "id": "enabled_dlc_trinket", "rarity": "rare", "price": 2000 } ] }
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(enabledDlcFeatureRoot, "heroes", "enabled_dlc_hero", "enabled_dlc_hero.info.darkest"),
            """
    combat_skill: .id "enabled_dlc_skill" .level 0
    generation: .is_generation_enabled true .number_of_random_combat_skills 1
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(disabledDlcFeatureRoot, "trinkets", "disabled.entries.trinkets.json"),
            """
    { "entries": [ { "id": "disabled_dlc_trinket", "rarity": "rare", "price": 2000 } ] }
    """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(disabledDlcFeatureRoot, "heroes", "disabled_dlc_hero", "disabled_dlc_hero.info.darkest"),
            """
    combat_skill: .id "disabled_dlc_skill" .level 0
    generation: .is_generation_enabled true .number_of_random_combat_skills 1
    """,
            new UTF8Encoding(false));

        File.WriteAllText(
            decodedSeedPath,
            """
    {
      "base_root": {
        "version": 34,
        "wallet": {
          "0": { "amount": 1250, "type": "gold" },
          "1": { "amount": 2, "type": "blueprint" }
        },
        "trinkets": {
          "items": {
            "0": { "id": "bleed_charm", "type": "trinket", "amount": 1 }
          }
        },
        "darkest_dungeon_trinket_unlocks": {},
        "estate_items": {
          "items": {
            "0": {
              "id": "the_blood", "type": "estate", "amount": 3,
              "added_buffs": 0, "hero_name": "", "previous_trinket_id": "",
              "did_transform": false, "trinkets_gained_count": 0
            },
            "1": {
              "id": "save_only_relic", "type": "estate", "amount": 7,
              "added_buffs": 0, "hero_name": "", "previous_trinket_id": "",
              "did_transform": false, "trinkets_gained_count": 0
            },
            "2": {
              "id": "stored_orphan_essence", "type": "estate", "amount": 0,
              "added_buffs": 0, "hero_name": "", "previous_trinket_id": "",
              "did_transform": false, "trinkets_gained_count": 0
            }
          }
        }
      }
    }
    """,
            new UTF8Encoding(false));

        File.WriteAllText(
            decodedGameSeedPath,
            """
    {
      "base_root": {
        "version": 2,
        "game_mode": "base",
        "inraid": false,
        "raiddungeon": "none",
        "applied_ugcs_1_0": {
          "10": { "name": "111", "source": "Steam" },
          "2": { "name": "Local Test Mod", "source": "mod_local_source" },
          "3": { "name": "External Test Mod", "source": "mod_local_source" },
          "11": { "name": "333", "source": "Steam" }
        },
        "persistent_ugcs": {
          "applied_ugcs_1_0": {
            "0": { "name": "222", "source": "Steam" }
          }
        },
        "dlc": {
          "0": { "name": "enabled_feature" }
        }
      }
    }
    """,
            new UTF8Encoding(false));

        File.WriteAllText(
            decodedTownSeedPath,
            """
    {
      "base_root": {
        "version": 513,
        "buildings": {
          "stage_coach": {
            "store": {
              "hero_recruit": {
                "generated": {},
                "deck_history_version_0": {}
              },
              "shard_hero_recruit": {
                "generated": {
                  "300": {
                    "heroClass": "shieldbreaker",
                    "actor": {},
                    "quirks": {
                      "context_roster_limited": {}
                    }
                  }
                },
                "deck_history_version_0": {}
              }
            }
          }
        }
      }
    }
    """,
            new UTF8Encoding(false));

        var rosterHeroesSeed = new JsonObject();
        for (var index = 0; index < 36; index++)
        {
            rosterHeroesSeed[index.ToString()] = new JsonObject
            {
                ["heroClass"] = "crusader"
            };
        }

        ((JsonObject)rosterHeroesSeed["0"]!)["hero_file_data"] = new JsonObject
        {
            ["raw_data"] = new JsonObject
            {
                ["base_root"] = new JsonObject
                {
                    ["quirks"] = new JsonObject
                    {
                        ["context_special"] = new JsonObject(),
                        ["context_roster_limited"] = new JsonObject()
                    }
                }
            }
        };

        var rosterSeed = new JsonObject
        {
            ["base_root"] = new JsonObject
            {
                ["version"] = 513,
                ["nextGuid"] = 364,
                ["heroes"] = rosterHeroesSeed
            }
        };
        File.WriteAllText(decodedRosterSeedPath, rosterSeed.ToJsonString(), new UTF8Encoding(false));

        File.WriteAllText(
            decodedUpgradesSeedPath,
            """
    {
      "base_root": {
        "version": 1,
        "purchases": {
          "7": {
            "instance_number": 1,
            "tree_id": 123456,
            "requirement_code": "e",
            "is_purchased": true
          }
        }
      }
    }
    """,
            new UTF8Encoding(false));

        var codec = new DsonSaveCodec(jarPath);

        WriteFixtureManifest(localModRoot);
        WriteFixtureManifest(externalLocalModRoot);

        return new ContractFixture(
            runRoot,
            gameRoot,
            workshopRoot,
            additionalLocalModDirectory,
            ambiguousDirectLocalModRoot,
            activeWorkshopRoot,
            activeWorkshopDistrictRoot,
            activeWorkshopInventoryRoot,
            externalLocalModRoot,
            localHeroUpgradeRoot,
            localLootRoot,
            localModRoot,
            localRaidCampingRoot,
            localTownEventsRoot,
            profileRoot,
            decodedSeedPath,
            decodedGameSeedPath,
            decodedTownSeedPath,
            decodedRosterSeedPath,
            decodedUpgradesSeedPath,
            estatePath,
            gameSavePath,
            townSavePath,
            rosterSavePath,
            upgradesSavePath,
            rosterHeroesSeed,
            codec);
    }
}
