internal static partial class ContractSuite
{
    private static async Task RunHeroContractsAsync(
        ActiveContentSnapshot activeContent,
        TrinketCatalogResult activeCatalog,
        SaveProfile profile,
        DsonSaveCodec codec,
        string runRoot,
        string localModRoot,
        string localHeroUpgradeRoot,
        string upgradesSavePath,
        string townSavePath,
        string rosterSavePath,
        JsonObject rosterHeroesSeed)
    {
        var heroCatalog = HeroClassCatalog.Load(activeContent);
        Assert(activeContent.GameMode == "base" && heroCatalog.GameMode == "base", "The profile game mode should flow into the hero catalog.");
        Assert(
            heroCatalog.ResolveLevelThresholds.SequenceEqual([0, 2, 8, 14, 24, 36, 48]),
            "Base resolve XP thresholds were not loaded from the effective roster variables.");
        Assert(heroCatalog.HeroClasses.Count == 8, "Hero catalog should contain base replacement/patch, enabled DLC package/feature, active Workshop, and local classes.");
        Assert(heroCatalog.RecruitEvents.Count == 4, "Resolved Workshop, DLC-overlay, local, and identical duplicate bonus_recruit events should be active.");
        Assert(heroCatalog.HeroClasses.All(item => item.Id != "disabled_hero"), "Persistent history must not enable a disabled hero class.");
        Assert(heroCatalog.HeroClasses.Any(item => item.Id == "dlc_shared_hero"), "Enabled DLC package root hero is missing.");
        var officialOverrideHero = heroCatalog.HeroClasses.Single(item => item.Id == "official_override_hero");
        Assert(
            officialOverrideHero.Generation?.IsEnabled == true &&
            officialOverrideHero.CombatSkillIds.SequenceEqual(["official_override_skill"]),
            "An official-style hero .override.darkest file must patch its active base .info.darkest definition without replacing the rest of the class template.");
        Assert(
            officialOverrideHero.SourceLabel.Contains("官方 DLC：", StringComparison.Ordinal) &&
            officialOverrideHero.SourceLabel.Contains("创意工坊 Mod：111", StringComparison.Ordinal),
            "A Mod that supplies only an .override.darkest file must be named as the current provider without hiding the hero's official origin.");
        Assert(heroCatalog.HeroClasses.Any(item => item.Id == "enabled_dlc_hero"), "Enabled DLC feature hero is missing.");
        Assert(heroCatalog.HeroClasses.All(item => item.Id != "disabled_dlc_hero"), "A disabled DLC feature hero must not be scanned.");
        Assert(heroCatalog.HeroClasses.All(item => item.Id != "backup_hero"), "A manifest backup path must not enter the active hero catalog.");
        Assert(heroCatalog.HeroClasses.All(item => item.Id != "disabled_mod_hero"), "A Mod path under a disabled DLC feature must not enter the active hero catalog.");
        Assert(heroCatalog.RecruitEvents.All(item => item.HeroClass != "disabled_hero"), "Persistent history must not enable a disabled recruit event.");

        var overriddenDlcHero = heroCatalog.HeroClasses.Single(item => item.Id == "dlc_shared_hero");
        var overriddenDlcFeatureHero = heroCatalog.HeroClasses.Single(item => item.Id == "enabled_dlc_hero");
        Assert(
            overriddenDlcHero.Source == "workshop:111" &&
            overriddenDlcHero.AllSources.Count == 2 &&
            !overriddenDlcHero.HasProviderConflict &&
            overriddenDlcHero.CombatSkillIds.SequenceEqual(["modded_dlc_skill"]),
            "A Mod manifest DLC path should override the matching DLC hero file.");
        Assert(
            overriddenDlcHero.RuntimeQuirkSignals.Single().QuirkId == "dlc_top_quirk",
            "Mod manifest DLC paths should be classified for effect and quirk overlays.");
        Assert(
            overriddenDlcHero.RecruitEvents.Single().Count == 6.0,
            "Mod manifest DLC paths should be classified for town-event overlays.");
        Assert(
            overriddenDlcFeatureHero.Source == "workshop:111" &&
            overriddenDlcFeatureHero.AllSources.Count == 2 &&
            !overriddenDlcFeatureHero.HasProviderConflict &&
            overriddenDlcFeatureHero.CombatSkillIds.SequenceEqual(["modded_enabled_dlc_skill"]),
            "A Mod should override an enabled DLC feature hero through its full virtual path.");

        var overriddenHero = heroCatalog.HeroClasses.Single(item => item.Id == "base_hero");
        Assert(overriddenHero.Source == "workshop:111", "A Mod should override the base game at the same relative hero path.");
        Assert(!overriddenHero.HasProviderConflict && overriddenHero.AllSources.Count == 2, "A verified same-path override should retain its provider chain without becoming ambiguous.");
        Assert(
            overriddenHero.SourceLabel == "原版（当前由 创意工坊 Mod：111 覆盖）",
            "A base hero overridden by a Mod should still display as original content and identify its current override provider.");
        Assert(overriddenHero.CombatSkillIds.SequenceEqual(["overridden_strike"]), "Effective hero definition should come from the active override.");
        Assert(
            overriddenHero.Generation?.PositiveQuirksMin == 4,
            "A manifest-listed hero .override.darkest file must be applied after its selected .info.darkest template.");

        var runtimeHero = heroCatalog.HeroClasses.Single(item => item.Id == "runtime_hero");
        Assert(
            !runtimeHero.HasProviderConflict &&
            runtimeHero.Source == "workshop:111" &&
            runtimeHero.CombatSkillIds.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(["runtime_strike", "runtime_guard"]) &&
            runtimeHero.GuaranteedCombatSkillIds.SequenceEqual(["runtime_strike"]) &&
            runtimeHero.IncompatibleInitialQuirkIds.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(["runtime_excluded_a", "runtime_excluded_b"]),
            "Hero definitions that only reorder set-like fields should merge, while an explicit false override must remove a previously guaranteed skill.");
        Assert(
            runtimeHero.LocalizedName == new BilingualContentName("未列清单英雄", "Unlisted Runtime Hero"),
            "A direct authoring string table should supply hero names when a Mod manifest lists only an unreadable legacy .loc file or omits the XML source.");
        Assert(runtimeHero.RecruitEvents.Single().Id == "recruit_runtime_hero", "Workshop bonus_recruit event was not linked to its hero class.");
        Assert(
            runtimeHero.RuntimeQuirkSignals.Count == 2 &&
            runtimeHero.RuntimeQuirkSignals.Single(signal => signal.SkillId == "runtime_strike").QuirkId == "runtime_fixed_quirk" &&
            runtimeHero.RuntimeQuirkSignals.Single(signal => signal.SkillId == "runtime_guard").QuirkId == "priority_top_quirk" &&
            runtimeHero.RuntimeQuirkSignals.All(signal => signal.EffectName != "Grant Runtime Quirk" || signal.SkillId != "runtime_guard"),
            "A skill override must replace only the named effect field, clear an explicitly empty field, and retain untouched *_effects fields.");
        Assert(
            string.IsNullOrWhiteSpace(runtimeHero.ProgressionUnsupportedReason) &&
            runtimeHero.LevelProfiles.Select(profile => profile.WeaponRank).SequenceEqual([0, 0, 1, 1, 1, 1, 1]) &&
            runtimeHero.LevelProfiles.Select(profile => profile.ArmourHp).SequenceEqual([18d, 18d, 25d, 25d, 25d, 25d, 25d]),
            "A partial equipment override must retain omitted HP/upgrade codes and update only the supplied rank field without breaking 0-max progression.");

        var priorityHero = heroCatalog.HeroClasses.Single(item => item.Id == "priority_hero");
        Assert(
            !priorityHero.HasProviderConflict &&
            priorityHero.Source == "local:Local Test Mod" &&
            priorityHero.AllSources.Count == 2 &&
            priorityHero.CombatSkillIds.SequenceEqual(["local_priority_skill"]) &&
            priorityHero.ColourVariationCount == 2,
            "A top-listed Mod should win a genuinely different hero definition while inheriting lower-layer art that it does not replace.");

        var localHero = heroCatalog.HeroClasses.Single(item => item.Id == "local_hero");
        Assert(
            localHero.Source == "local:Local Test Mod" &&
            localHero.AllSources.Count == 2 &&
            !localHero.HasProviderConflict,
            "The top Mod should win an exact-relative-path hero override.");
        Assert(localHero.Generation?.PositiveQuirksMin == 1 && localHero.Generation.PositiveQuirksMax == 3, "Repeated generation lines should merge instead of replacing earlier fields.");
        Assert(localHero.Generation?.NegativeQuirksMin == 0 && localHero.Generation.NegativeQuirksMax == 1, "Split negative quirk bounds were not merged.");
        Assert(localHero.GuaranteedCombatSkillIds.Contains("local_skill"), "Local fallback hero skill flags were not parsed.");
        Assert(localHero.BaseHp == 20, "The level-zero armour HP was not parsed from the effective Mod hero template.");
        Assert(
            localHero.LocalizedName == new BilingualContentName("编译本地英雄", "Compiled Local Hero"),
            "A Mod hero should prefer the compiled LOC2 names that the game loads over its XML authoring source.");
        Assert(
            string.IsNullOrWhiteSpace(localHero.ProgressionUnsupportedReason) &&
            localHero.LevelProfiles.Select(profile => profile.ResolveXp).SequenceEqual([0, 2, 8, 14, 24, 36, 48]) &&
            localHero.LevelProfiles.Select(profile => profile.WeaponRank).SequenceEqual([0, 1, 2, 3, 3, 4, 4]) &&
            localHero.LevelProfiles.Select(profile => profile.ArmourRank).SequenceEqual([0, 1, 2, 3, 3, 4, 4]) &&
            localHero.LevelProfiles.Select(profile => profile.ArmourHp).SequenceEqual([20d, 24d, 28d, 32d, 32d, 36d, 36d]),
            "The Mod hero level profiles should be derived from active XP and upgrade templates.");
        Assert(
            localHero.UpgradeTrees.Count == 4 &&
            localHero.UpgradeTrees.Count(tree => tree.Kind == HeroUpgradeTreeKind.CombatSkill) == 2 &&
            localHero.UpgradeTrees.Single(tree => tree.Id == "local_hero.local_skill")
                .Requirements.Select(requirement => requirement.Code)
                .SequenceEqual(["a", "A", "b", "B", "c"]),
            "The active hero upgrade template should retain exact, case-sensitive tree ids and custom requirement codes for save purchases.");
        Assert(
            heroCatalog.Issues.Any(issue =>
                issue.Contains("Hero upgrade 'case_probe_hero'", StringComparison.Ordinal) &&
                issue.Contains("conflicting definitions", StringComparison.Ordinal)),
            "Same-priority hero upgrade templates that differ only by requirement-code case must remain an explicit conflict.");
        var compatibleUpgradeHero = heroCatalog.HeroClasses.Single(item => item.Id == "compatible_upgrade_hero");
        Assert(
            string.IsNullOrWhiteSpace(compatibleUpgradeHero.ProgressionUnsupportedReason) &&
            compatibleUpgradeHero.UpgradeTrees.Any(tree =>
                tree.Id == "compatible_upgrade_hero.scaling_strike") &&
            compatibleUpgradeHero.UpgradeTrees.All(tree =>
                tree.Id != "compatible_upgrade_hero.legacy_strike_name") &&
            compatibleUpgradeHero.SingleLevelCombatSkillIds.SequenceEqual(["fixed_command"]) &&
            heroCatalog.Issues.All(issue =>
                !issue.Contains("Hero upgrade 'compatible_upgrade_hero'", StringComparison.Ordinal)),
            "A unique same-priority upgrade template compatible with the active hero skill ids should win, while a truly level-zero-only skill remains explicit.");
        var compatibleLevelZeroCandidate = StagecoachHeroCandidateFactory.Generate(
            heroCatalog,
            compatibleUpgradeHero,
            seed: 1729,
            resolveLevel: 0,
            selectedInitialQuirkIds: []);
        Assert(
            compatibleLevelZeroCandidate.UpgradePurchases.Count == 2 &&
            compatibleLevelZeroCandidate.UpgradePurchases.Contains(
                new HeroUpgradePurchase("compatible_upgrade_hero.scaling_strike", "0")) &&
            compatibleLevelZeroCandidate.UpgradePurchases.Contains(
                new HeroUpgradePurchase("compatible_upgrade_hero.fixed_command", "0")),
            "A level-zero-only combat skill omitted from the upgrade JSON should receive the proven code-0 purchase without weakening multilevel missing-tree validation.");
        Assert(localHero.ColourVariationCount == 2, "Only the continuous A/B skin directories should be available for random colour selection.");
        Assert(localHero.ClassCampingSkillIds.Count == 2 && localHero.SharedCampingSkillIds.Count == 2, "Class and shared camping skills were not separated by the camping configuration.");
        Assert(localHero.IncompatibleInitialQuirkIds.Contains("excluded_quirk"), "Class-level incompatible initial quirks were not parsed.");
        Assert(localHero.RecruitEvents.Single().Count == 2.0, "Local bonus_recruit count was not parsed.");
        Assert(localHero.RuntimeQuirkSignals.Single().QuirkId == "priority_top_quirk", "Top-priority same-path effect and quirk files should supply the non-initial runtime signal.");
        Assert(heroCatalog.RecruitEvents.All(item => item.Id != "ambiguous_recruit"), "A same-priority town event conflict must not select a winner.");
        Assert(heroCatalog.RecruitEvents.Single(item => item.Id == "identical_recruit").HeroClass == "event_only_hero", "Identical town event definitions should merge.");
        var fallbackMessage = $"Mod has no modfiles.txt; standard fallback scan used: {Path.GetFullPath(localModRoot)}";
        Assert(heroCatalog.Issues.Contains(fallbackMessage, StringComparer.OrdinalIgnoreCase), "A local hero Mod without a manifest should report its standard fallback scan once.");
        Assert(activeCatalog.Issues.Contains(fallbackMessage, StringComparer.OrdinalIgnoreCase), "The trinket and hero fallback reports should use the same deduplicatable message.");
        Assert(heroCatalog.Issues.All(issue => !issue.Contains("Hero class 'runtime_hero'", StringComparison.Ordinal)), "Identical hero duplicates should not be reported as conflicts.");
        Assert(heroCatalog.Issues.Any(issue => issue.Contains("Effect 'Ambiguous Effect'", StringComparison.Ordinal)), "Semantic-only effect duplicates should be reported.");
        Assert(heroCatalog.Issues.All(issue => !issue.Contains("Ordinary Duplicate", StringComparison.Ordinal)), "Effects without disease assignments should not enter runtime-quirk conflict diagnostics.");
        Assert(heroCatalog.Issues.Any(issue => issue.Contains("Quirk 'ambiguous_quirk'", StringComparison.Ordinal)), "Semantic-only quirk duplicates should be reported.");
        Assert(
            heroCatalog.InitialQuirks.Count(item => item.Id == "ambiguous_quirk") == 2,
            "Semantic-only quirk duplicates should remain in the selection catalog for disabled display.");
        var identicalCrossPathQuirk = heroCatalog.InitialQuirks.Single(item => item.Id == "identical_cross_path_quirk");
        Assert(
            identicalCrossPathQuirk is { IsPositive: true, WriteStatus: HeroInitialQuirkWriteStatus.Direct } &&
            heroCatalog.Issues.All(issue => !issue.Contains("identical_cross_path_quirk", StringComparison.Ordinal)),
            "Semantically identical quirk definitions at different paths should merge without a conflict.");
        Assert(
            heroCatalog.InitialQuirks.Count(item => item.Id == "evolution_conflict_quirk") == 2 &&
            heroCatalog.Issues.Any(issue => issue.Contains("Quirk 'evolution_conflict_quirk'", StringComparison.Ordinal)),
            "Different evolution durations or targets at the same effective priority must remain unresolved.");
        Assert(
            heroCatalog.InitialQuirks.Single(item => item.Id == "identical_evolution_quirk") is
            {
                HasEvolution: true,
                Evolution: { DurationMin: 30, DurationMax: 60, TargetQuirkId: "same_target" },
                WriteStatus: HeroInitialQuirkWriteStatus.Direct
            } &&
            heroCatalog.Issues.All(issue => !issue.Contains("identical_evolution_quirk", StringComparison.Ordinal)),
            "Semantically equal evolution metadata should merge even when JSON numbers use integer and decimal spellings.");
        var semanticPriorityQuirk = heroCatalog.InitialQuirks.Single(item => item.Id == "semantic_priority_quirk");
        Assert(
            semanticPriorityQuirk is { IsPositive: true, Source: "local:Local Test Mod" } &&
            semanticPriorityQuirk.AllSources.Count == 3 &&
            semanticPriorityQuirk.SourceLabel == "原版（当前由 本地 Mod：Local Test Mod 覆盖）" &&
            heroCatalog.Issues.All(issue => !issue.Contains("semantic_priority_quirk", StringComparison.Ordinal)),
            "A genuinely different cross-path quirk definition should follow Mod priority while retaining its original-game provenance.");
        var samePathDuplicateQuirk = heroCatalog.InitialQuirks.Single(item => item.Id == "same_path_duplicate");
        Assert(
            samePathDuplicateQuirk is { IsPositive: true, RandomChance: 1 },
            "Repeated quirk declarations at one effective path should expose only the last resolved definition.");
        Assert(heroCatalog.Issues.Any(issue => issue.Contains("Town event 'ambiguous_recruit'", StringComparison.Ordinal)), "Semantic-only town event duplicates should be reported.");
        Assert(heroCatalog.Issues.All(issue => !issue.Contains("identical_recruit", StringComparison.Ordinal)), "Identical town event definitions should not be reported as conflicts.");

        Assert(
            heroCatalog.HeroNames.SequenceEqual(["Contract Lenient", "Contract One", "Contract Two"]),
            "The hero_name_* pool should reuse tolerant localization parsing.");
        Assert(
            heroCatalog.HeroNames.All(name => name != "Ignored Unused Name") &&
            heroCatalog.Issues.All(issue => !issue.Contains("ignored.string_table.xml", StringComparison.OrdinalIgnoreCase)) &&
            activeCatalog.Issues.All(issue => !issue.Contains("ignored.string_table.xml", StringComparison.OrdinalIgnoreCase)) &&
            heroCatalog.Issues.All(issue => !issue.Contains("ignored_schinese.loc2", StringComparison.OrdinalIgnoreCase)) &&
            activeCatalog.Issues.All(issue => !issue.Contains("ignored_schinese.loc2", StringComparison.OrdinalIgnoreCase)),
            "Unused and platform-specific localization manifest entries must stay outside active XML/LOC2 catalogs.");
        var naturalQuirk = heroCatalog.InitialQuirks.Single(item => item.Id == "natural");
        Assert(
            naturalQuirk.LocalizedName == new BilingualContentName("契约自然体质", "Contract Natural Constitution"),
            "A quirk should expose distinct Simplified Chinese and English names.");
        Assert(naturalQuirk.SourceLabel == "原版", "An untouched base quirk should display its original-game provenance.");
        var sourceProbeQuirk = heroCatalog.InitialQuirks.Single(item => item.Id == "source_probe_quirk");
        var modOnlyOverrideFileQuirk = heroCatalog.InitialQuirks.Single(item =>
            item.Id == "mod_only_in_overridden_quirk_file");
        Assert(
            sourceProbeQuirk.Source == "workshop:111" &&
            sourceProbeQuirk.SourceLabel == "原版（当前由 创意工坊 Mod：111 覆盖）" &&
            sourceProbeQuirk.AllSources.Count == 2,
            "A base quirk overridden at the same virtual path should retain and display its full provider provenance.");
        Assert(
            modOnlyOverrideFileQuirk.Source == "workshop:111" &&
            modOnlyOverrideFileQuirk.AllSources.SequenceEqual(["workshop:111"]) &&
            modOnlyOverrideFileQuirk.SourceLabel == "创意工坊 Mod：111",
            "A Mod-only quirk added inside an overridden base file must not inherit original-game provenance from unrelated entries in that file.");
        Assert(
            naturalQuirk is
            {
                Kind: HeroInitialQuirkKind.Natural,
                IsNaturalRandomEligible: true,
                WriteStatus: HeroInitialQuirkWriteStatus.Direct
            } &&
                naturalQuirk.MaxHpModifiers is
                [
                    {
                        Kind: HeroMaxHpModifierKind.Percentage,
                        Amount: 0.2,
                        RuleType: "no_trinkets",
                        IsFalseRule: false
                    }
                ],
            "The original no_trinkets max-HP quirk shape should be supported for an empty-trinket candidate.");
        var specialQuirk = heroCatalog.InitialQuirks.Single(item => item.Id == "priority_top_quirk");
        Assert(
            specialQuirk is
            {
                Kind: HeroInitialQuirkKind.Special,
                IsNaturalRandomEligible: false,
                WriteStatus: HeroInitialQuirkWriteStatus.Direct
            },
            "A fixed/special quirk with a fully understood save shape should be directly selectable without entering the natural random pool.");
        Assert(
            specialQuirk.LocalizedName == new BilingualContentName("编译优先传承", "Compiled Priority Legacy"),
            "A Mod special quirk should retain both compiled localized display names.");
        Assert(
            heroCatalog.Issues.Any(issue =>
                issue.Contains("broken_english.loc2", StringComparison.OrdinalIgnoreCase) &&
                issue.Contains("Failed to read localization", StringComparison.Ordinal)),
            "Hero and quirk localization should survive a separate malformed LOC2 file.");
        var nonHpConflictQuirk = heroCatalog.InitialQuirks.Single(item => item.Id == "non_hp_conflict_quirk");
        Assert(
            nonHpConflictQuirk.WriteStatus == HeroInitialQuirkWriteStatus.Direct,
            "An unresolved non-HP Buff should be left to the game runtime instead of blocking explicit quirk selection.");
        var maxHpConflictQuirk = heroCatalog.InitialQuirks.Single(item => item.Id == "max_hp_conflict_quirk");
        Assert(
            maxHpConflictQuirk.WriteStatus == HeroInitialQuirkWriteStatus.Unverified &&
            maxHpConflictQuirk.WriteStatusReason.Contains("max_hp", StringComparison.OrdinalIgnoreCase),
            "An unresolved max-HP Buff must still block explicit quirk selection because current_hp cannot be derived safely.");
        Assert(
            heroCatalog.Issues.All(issue =>
                !issue.Contains("Buff 'CONFLICT_ACC'", StringComparison.Ordinal) &&
                !issue.Contains("Buff 'CONFLICT_MAXHP'", StringComparison.Ordinal)),
            "Raw Buff conflicts should not pollute the catalog log: only a referenced max-HP conflict belongs to the affected quirk's write status.");
        var contextualQuirk = heroCatalog.InitialQuirks.Single(item => item.Id == "context_special");
        Assert(
            contextualQuirk is
            {
                Kind: HeroInitialQuirkKind.Special,
                WriteStatus: HeroInitialQuirkWriteStatus.RequiresSaveContext,
                DefinitionLimit: 1
            } &&
            contextualQuirk.WriteStatusReason.Contains("超限仅警告", StringComparison.Ordinal),
            "A singleton special quirk should expose limit one and remain selectable through preview-time context checking.");
        var unverifiedSingletonQuirk = heroCatalog.InitialQuirks.Single(item =>
            item.Id == "context_unverified_singleton");
        Assert(
            unverifiedSingletonQuirk is
            {
                WriteStatus: HeroInitialQuirkWriteStatus.Unverified,
                DefinitionLimit: 1
            } &&
            unverifiedSingletonQuirk.WriteStatusReason.Contains("max_hp", StringComparison.OrdinalIgnoreCase) &&
            unverifiedSingletonQuirk.WriteStatusReason.Contains("预览统计 roster", StringComparison.Ordinal),
            "A singleton with an independent unsafe HP rule must retain both its blocking core reason and full preview-context detail; only the table display compacts the latter.");
        var rosterLimitedQuirk = heroCatalog.InitialQuirks.Single(item => item.Id == "context_roster_limited");
        Assert(
            rosterLimitedQuirk is
            {
                WriteStatus: HeroInitialQuirkWriteStatus.Direct,
                DefinitionLimit: null
            },
            "A positive roster_limit must remain directly writable because the game enforces it when the candidate is recruited, not when it is generated in the stagecoach.");
        var diseaseQuirk = heroCatalog.InitialQuirks.Single(item => item.Id == "test_disease");
        Assert(
            diseaseQuirk is
            {
                Kind: HeroInitialQuirkKind.Disease,
                HasEvolution: false,
                WriteStatus: HeroInitialQuirkWriteStatus.Direct
            },
            "A disease without unknown HP or save-context state should be directly writable and remain separately classified.");
        var evolvingQuirk = heroCatalog.InitialQuirks.Single(item => item.Id == "evolving_quirk");
        Assert(
            evolvingQuirk is
            {
                HasEvolution: true,
                Evolution:
                {
                    DurationMin: 60,
                    DurationMax: 60,
                    TargetQuirkId: "evolved_quirk",
                    CausesDeath: false
                },
                WriteStatus: HeroInitialQuirkWriteStatus.Direct
            },
            "A complete evolution definition should expose its structured duration and target metadata.");
        Assert(
            heroCatalog.InitialQuirks.Single(item => item.Id == "evolving_disease") is
            {
                Kind: HeroInitialQuirkKind.Disease,
                HasEvolution: true,
                WriteStatus: HeroInitialQuirkWriteStatus.Direct
            },
            "An evolving disease without an unknown HP rule should use the same initialized-duration contract.");
        Assert(
            heroCatalog.InitialQuirks.Single(item => item.Id == "evolving_variable").Evolution is
            {
                DurationMin: 3,
                DurationMax: 15,
                TownProgressionDurationChange: 1,
                TargetQuirkId: "evolved_variable",
                CausesDeath: false
            },
            "A variable Mod evolution range should remain attached to its own quirk definition.");
        Assert(
            heroCatalog.InitialQuirks.Single(item => item.Id == "evolving_death") is
            {
                WriteStatus: HeroInitialQuirkWriteStatus.Direct,
                Evolution:
                {
                    DurationMin: 61,
                    DurationMax: 61,
                    TownProgressionDurationChange: 30,
                    TargetQuirkId: null,
                    CausesDeath: true,
                    TownAttemptUseItemDurationThreshold: 61
                }
            },
            "The original-game death evolution shape should be valid without an evolution_class_id.");
        Assert(
            heroCatalog.InitialQuirks.Single(item => item.Id == "evolution_missing_max") is
            {
                HasEvolution: false,
                WriteStatus: HeroInitialQuirkWriteStatus.Unverified
            } missingMaximumEvolution &&
            missingMaximumEvolution.WriteStatusReason.Contains("evolution_duration_max", StringComparison.Ordinal),
            "A missing evolution maximum must remain visible but unavailable.");
        Assert(
            heroCatalog.InitialQuirks.Single(item => item.Id == "evolution_inverted") is
            {
                HasEvolution: false,
                WriteStatus: HeroInitialQuirkWriteStatus.Unverified
            } invertedEvolution &&
            invertedEvolution.WriteStatusReason.Contains("下限不能大于上限", StringComparison.Ordinal),
            "An inverted evolution range must not silently fall back to zero.");
        Assert(
            heroCatalog.InitialQuirks.Single(item => item.Id == "evolution_fractional") is
            {
                HasEvolution: false,
                WriteStatus: HeroInitialQuirkWriteStatus.Unverified
            } fractionalEvolution &&
            fractionalEvolution.WriteStatusReason.Contains("32 位整数", StringComparison.Ordinal),
            "A fractional evolution duration cannot be represented by the integer save field.");
        Assert(
            heroCatalog.InitialQuirks.Single(item => item.Id == "evolution_missing_outcome") is
            {
                HasEvolution: false,
                WriteStatus: HeroInitialQuirkWriteStatus.Unverified
            } missingOutcomeEvolution &&
            missingOutcomeEvolution.WriteStatusReason.Contains("evolution_class_id", StringComparison.Ordinal),
            "An evolution definition without a target or death outcome must be rejected.");
        Assert(
            heroCatalog.InitialQuirks.Single(item => item.Id == "unknown_hp_disease").WriteStatus ==
            HeroInitialQuirkWriteStatus.Unverified,
            "Disease support must not bypass an unknown max-HP rule.");
        Assert(
            heroCatalog.InitialQuirks.Single(item => item.Id == "evolving_unknown_hp") is
            {
                HasEvolution: true,
                WriteStatus: HeroInitialQuirkWriteStatus.Unverified
            },
            "Allowing evolution metadata must not bypass an unknown conditional max-HP rule.");
        var flatHpQuirk = heroCatalog.InitialQuirks.Single(item => item.Id == "flat_hp_quirk");
        Assert(
                flatHpQuirk.WriteStatus == HeroInitialQuirkWriteStatus.Direct &&
                flatHpQuirk.MaxHpModifiers is
                [
                    { Kind: HeroMaxHpModifierKind.Flat, Amount: 4, RuleType: "always" }
                ],
                "A known flat max-HP modifier should enter the additive term of the verified formula.");
        var multipleHpQuirk = heroCatalog.InitialQuirks.Single(item => item.Id == "multiple_hp_quirk");
        Assert(
            multipleHpQuirk.WriteStatus == HeroInitialQuirkWriteStatus.Direct &&
            multipleHpQuirk.MaxHpModifiers.Count == 2 &&
            multipleHpQuirk.MaxHpModifiers.All(modifier =>
                modifier.Kind == HeroMaxHpModifierKind.Percentage),
            "Multiple known max-HP Buffs on one quirk should remain individually modeled.");
        var otherModeHpQuirk = heroCatalog.InitialQuirks.Single(item => item.Id == "other_mode_hp_quirk");
        Assert(
            otherModeHpQuirk.WriteStatus == HeroInitialQuirkWriteStatus.Direct &&
            otherModeHpQuirk.MaxHpModifiers is
            [
                {
                    Kind: HeroMaxHpModifierKind.Flat,
                    Amount: 20,
                    RuleType: "in_mode",
                    IsFalseRule: true,
                    RuleString: "ContractModeA"
                }
            ],
            "A recognized inverted in_mode max-HP Buff should preserve its rule_data string.");
        var lightHpQuirk = heroCatalog.InitialQuirks.Single(item => item.Id == "light_hp_quirk");
        Assert(
                lightHpQuirk.WriteStatus == HeroInitialQuirkWriteStatus.Direct &&
                lightHpQuirk.MaxHpModifiers is
                [
                    {
                        Kind: HeroMaxHpModifierKind.Percentage,
                        Amount: -0.5,
                        RuleType: "lightabove",
                        RuleFloat: 1
                    }
                ],
                "A recognized lightabove max-HP Buff should preserve its rule_data threshold.");
        StagecoachHeroCandidateFactory.ValidateInitialQuirkSelection(heroCatalog, localHero, []);
        StagecoachHeroCandidateFactory.ValidateInitialQuirkSelection(
            heroCatalog,
            localHero,
            ["natural", "tough"]);
        StagecoachHeroCandidateFactory.ValidateInitialQuirkSelection(
            heroCatalog,
            localHero,
            ["same_path_duplicate"]);
        StagecoachHeroCandidateFactory.ValidateInitialQuirkSelection(
            heroCatalog,
            localHero,
            ["priority_top_quirk"]);
        StagecoachHeroCandidateFactory.ValidateInitialQuirkSelection(
            heroCatalog,
            localHero,
            ["non_hp_conflict_quirk"]);
        StagecoachHeroCandidateFactory.ValidateInitialQuirkSelection(
            heroCatalog,
            localHero,
            ["excluded_quirk"]);
        StagecoachHeroCandidateFactory.ValidateInitialQuirkSelection(
            heroCatalog,
            localHero,
            ["evolving_quirk"]);
        StagecoachHeroCandidateFactory.ValidateInitialQuirkSelection(
            heroCatalog,
            localHero,
            ["test_disease", "test_disease_two", "test_disease_three"]);
        StagecoachHeroCandidateFactory.ValidateInitialQuirkSelection(
            heroCatalog,
            localHero,
            ["context_special", "context_roster_limited"]);
        StagecoachHeroCandidateFactory.ValidateInitialQuirkSelection(
            heroCatalog,
            localHero,
            ["flat_hp_quirk", "multiple_hp_quirk"]);
        StagecoachHeroCandidateFactory.ValidateInitialQuirkSelection(
            heroCatalog,
            localHero,
            ["afflicted_hp_quirk", "other_mode_hp_quirk", "light_hp_quirk"]);
        StagecoachHeroCandidateFactory.ValidateInitialQuirkSelection(
            heroCatalog,
            localHero,
            ["mode_a_weakness", "mode_b_weakness"]);
        StagecoachHeroCandidateFactory.ValidateInitialQuirkSelection(
            heroCatalog,
            localHero,
            resolveLevel: 4,
            selectedInitialQuirkIds: ["flat_level_boundary"]);
        AssertInitialQuirkSelectionRejected(heroCatalog, localHero, ["tough", "fragile"], "互斥");
        AssertInitialQuirkSelectionRejected(heroCatalog, localHero, ["evolution_missing_max"], "当前不能显式写入");
        AssertInitialQuirkSelectionRejected(heroCatalog, localHero, ["evolution_inverted"], "当前不能显式写入");
        AssertInitialQuirkSelectionRejected(heroCatalog, localHero, ["evolution_fractional"], "当前不能显式写入");
        AssertInitialQuirkSelectionRejected(heroCatalog, localHero, ["evolution_missing_outcome"], "当前不能显式写入");
        AssertInitialQuirkSelectionRejected(heroCatalog, localHero, ["unknown_hp_rule"], "当前不能显式写入");
        AssertInitialQuirkSelectionRejected(heroCatalog, localHero, ["evolving_unknown_hp"], "当前不能显式写入");
        AssertInitialQuirkSelectionRejected(heroCatalog, localHero, ["flat_level_boundary"], "可达条件");
        AssertInitialQuirkSelectionRejected(
            heroCatalog,
            localHero,
            ["half_weakness", "light_hp_quirk"],
            "可达条件");
        AssertInitialQuirkSelectionRejected(
            heroCatalog,
            localHero,
            ["half_weakness", "afflicted_half_weakness"],
            "可达条件");
        AssertInitialQuirkSelectionRejected(
            heroCatalog,
            localHero,
            ["half_weakness", "other_mode_half_weakness"],
            "可达条件");
        AssertInitialQuirkSelectionRejected(
            heroCatalog,
            localHero,
            ["light_hp_quirk", "rounding_weakness_a", "rounding_weakness_b", "rounding_weakness_c"],
            "可达条件");
        AssertInitialQuirkSelectionRejected(heroCatalog, localHero, ["ambiguous_quirk"], "多个未解析定义");
        AssertInitialQuirkSelectionRejected(heroCatalog, localHero, ["evolution_conflict_quirk"], "多个未解析定义");
        AssertInitialQuirkSelectionRejected(heroCatalog, localHero, ["max_hp_conflict_quirk"], "当前不能显式写入");
        AssertInitialQuirkSelectionRejected(heroCatalog, localHero, ["fatal_weakness"], "合计 HP 修正无效");
        Assert(
            heroCatalog.InitialQuirks.Single(item => item.Id == "unknown_hp_rule").WriteStatus == HeroInitialQuirkWriteStatus.Unverified,
            "An unverified conditional max-HP rule must not enter the safe random pool.");

        var generatedCandidate = StagecoachHeroCandidateFactory.Generate(heroCatalog, localHero, seed: 1729);
        var generatedPreview = generatedCandidate.Preview;
        Assert(
            generatedPreview.Name is "Contract Lenient" or "Contract One" or "Contract Two",
            "Generated candidate name did not come from the active localization pool.");
        Assert(generatedPreview.HeroClass == "local_hero", "Generated candidate has the wrong Mod class ID.");
        Assert(
            generatedPreview is { ResolveLevel: 0, ResolveXp: 0, WeaponRank: 0, ArmourRank: 0 },
            "The compatibility overload should continue generating a level-zero candidate.");
        Assert(generatedPreview.ColourVariation is >= 0 and < 2, "Generated colour variation is outside the verified A/B range.");
        Assert(generatedPreview.PositiveQuirks.Count == 0, "Default console-style generation should not add positive quirks.");
        Assert(generatedPreview.NegativeQuirks.Count == 0, "Default console-style generation should not add negative quirks.");
        Assert(generatedPreview.Diseases.Count == 0, "Default console-style generation should not add diseases.");
        Assert(generatedPreview.CombatSkills.SequenceEqual(["local_skill"]), "The guaranteed level-zero combat skill was not selected.");
        Assert(generatedPreview.CampingSkills.Count == 2, "The class template should select one shared and one class camping skill.");
        Assert(Math.Abs(generatedPreview.CurrentHp - 20.0) < 0.000001, "A blank candidate should start at the class base HP.");
        var generatedActor = generatedCandidate.Candidate["actor"] as JsonObject;
        Assert(generatedActor?["buff_group_next_guid"]?.GetValue<int>() == 2, "A generated candidate should use the minimum baseline observed in real saved stagecoach candidates.");
        Assert((generatedActor?["buff_group"] as JsonObject)?.Count == 0, "A blank candidate should not contain actor buffs.");
        Assert(generatedActor?["current_hp"]?.GetValue<double>() == 20.0, "Candidate JSON current_hp differs from the blank preview.");
        Assert(
            generatedCandidate.Candidate["resolveXp"]?.GetValue<int>() == 0 &&
            generatedCandidate.Candidate["weapon_rank"]?.GetValue<int>() == 0 &&
            generatedCandidate.Candidate["armour_rank"]?.GetValue<int>() == 0,
            "A level-zero candidate should write zero XP and equipment ranks.");
        var generatedQuirkMap = generatedCandidate.Candidate["quirks"] as JsonObject;
        Assert(generatedQuirkMap?.Count == 0, "Default candidate JSON should contain an empty quirk map.");
        var expectedCampingTreeIds = localHero.SharedCampingSkillIds
            .Concat(localHero.ClassCampingSkillIds)
            .Select(skillId => $"local_hero.{skillId}")
            .ToHashSet(StringComparer.Ordinal);
        Assert(
            generatedCandidate.UpgradePurchases.Count == 6 &&
            generatedCandidate.UpgradePurchases.Count(purchase =>
                (purchase.TreeId == "local_hero.local_skill" ||
                 purchase.TreeId == "local_hero.local_skill_two") &&
                purchase.RequirementCode == "a") == 2 &&
            generatedCandidate.UpgradePurchases.Count(purchase =>
                expectedCampingTreeIds.Contains(purchase.TreeId) &&
                purchase.RequirementCode == "0") == 4,
            "A level-zero candidate should unlock every combat tree base requirement and every available camping skill.");
        Assert(
            generatedPreview.CampingSkills.Count == 2 &&
            expectedCampingTreeIds.Count == 4,
            "Unlocking every camping skill must not equip every camping skill in the candidate selection map.");

        var partialCombatUpgradeHero = localHero with
        {
            UpgradeTrees = localHero.UpgradeTrees
                .Where(tree => tree.Id != "local_hero.local_skill_two")
                .ToArray()
        };
        AssertHeroLevelGenerationRejected(
            heroCatalog,
            partialCombatUpgradeHero,
            0,
            "local_hero.local_skill_two");

        var emptyCombatUpgradeHero = localHero with
        {
            UpgradeTrees = localHero.UpgradeTrees
                .Select(tree => tree.Id == "local_hero.local_skill_two"
                    ? tree with { Requirements = [] }
                    : tree)
                .ToArray()
        };
        AssertHeroLevelGenerationRejected(
            heroCatalog,
            emptyCombatUpgradeHero,
            0,
            "在 0 级没有可用 requirement");

        var delayedCombatUpgradeHero = localHero with
        {
            UpgradeTrees = localHero.UpgradeTrees
                .Select(tree => tree.Id == "local_hero.local_skill_two"
                    ? tree with
                    {
                        Requirements = [new HeroUpgradeRequirementDefinition("a", 1)]
                    }
                    : tree)
                .ToArray()
        };
        AssertHeroLevelGenerationRejected(
            heroCatalog,
            delayedCombatUpgradeHero,
            0,
            "在 0 级没有可用 requirement");

        var explicitCandidate = StagecoachHeroCandidateFactory.Generate(
            heroCatalog,
            localHero,
            seed: 1729,
            selectedInitialQuirkIds: ["steady", "eagle_eye", "clumsy"]);
        Assert(
            explicitCandidate.Preview.PositiveQuirks.SequenceEqual(["steady", "eagle_eye"]) &&
            explicitCandidate.Preview.NegativeQuirks.SequenceEqual(["clumsy"]),
            "Explicit ordinary quirks should be preserved exactly without random additions.");
        Assert(
            Math.Abs(explicitCandidate.Preview.CurrentHp - 20.0) < 0.000001,
            "A non-HP attribute quirk must not be precomputed into current_hp.");
        var explicitQuirkMap = explicitCandidate.Candidate["quirks"] as JsonObject;
        Assert(explicitQuirkMap?.Count == 3, "Explicit candidate JSON quirk map does not match the selected IDs.");
        Assert(
            explicitQuirkMap!.All(pair =>
                pair.Value?["is_new"]?.GetValue<bool>() == true &&
                pair.Value?["mission_count"]?.GetValue<int>() == 0 &&
                pair.Value?["evolution_duration_remaining"]?.GetValue<int>() == 0),
            "Non-evolving initial quirks should retain canonical flags, a zero mission count, and a zero evolution field.");

        var specialCandidate = StagecoachHeroCandidateFactory.Generate(
            heroCatalog,
            localHero,
            seed: 1729,
            selectedInitialQuirkIds: ["priority_top_quirk"]);
        Assert(
            specialCandidate.Preview.PositiveQuirks.SequenceEqual(["priority_top_quirk"]),
            "A directly writable fixed/special quirk should be preserved exactly in the generated candidate.");

        var contextLimitedCandidate = StagecoachHeroCandidateFactory.Generate(
            heroCatalog,
            localHero,
            seed: 1729,
            selectedInitialQuirkIds: ["context_special", "context_roster_limited"]);
        Assert(
            contextLimitedCandidate.Preview.PositiveQuirks.SequenceEqual(["context_special"]) &&
            contextLimitedCandidate.Preview.NegativeQuirks.SequenceEqual(["context_roster_limited"]),
            "A singleton or roster-limited quirk should generate normally before its save-context limit is previewed.");

        var classExcludedCandidate = StagecoachHeroCandidateFactory.Generate(
            heroCatalog,
            localHero,
            seed: 1729,
            selectedInitialQuirkIds: ["excluded_quirk"]);
        Assert(
            classExcludedCandidate.Preview.PositiveQuirks.SequenceEqual(["excluded_quirk"]),
            "Console-style generation should allow any directly writable recognized quirk regardless of the class template's incompatible_class_ids list.");

        var evolvingCandidate = StagecoachHeroCandidateFactory.Generate(
            heroCatalog,
            localHero,
            seed: 1729,
            selectedInitialQuirkIds: ["evolving_quirk"]);
        var evolvingCandidateQuirk = evolvingCandidate.Candidate["quirks"]?["evolving_quirk"];
        Assert(
            evolvingCandidateQuirk?["evolution_duration_remaining"]?.GetValue<int>() == 60 &&
            evolvingCandidate.Preview.Warnings.Any(warning =>
                warning.Contains("evolving_quirk", StringComparison.Ordinal) &&
                warning.Contains("evolving_quirk=60", StringComparison.Ordinal) &&
                warning.Contains("60–60", StringComparison.Ordinal)) &&
            evolvingCandidate.Preview.Warnings.All(warning =>
                !warning.Contains("招募并保存", StringComparison.Ordinal)),
            "A fixed evolution range should be written directly without the disproven game-initialization warning.");

        var variableEvolutionCandidate = StagecoachHeroCandidateFactory.Generate(
            heroCatalog,
            localHero,
            seed: 1729,
            selectedInitialQuirkIds: ["evolving_variable"]);
        var repeatedVariableEvolutionCandidate = StagecoachHeroCandidateFactory.Generate(
            heroCatalog,
            localHero,
            seed: 1729,
            selectedInitialQuirkIds: ["evolving_variable"]);
        var variableEvolutionDuration = variableEvolutionCandidate.Candidate["quirks"]?["evolving_variable"]?
            ["evolution_duration_remaining"]?.GetValue<int>();
        var repeatedVariableEvolutionDuration = repeatedVariableEvolutionCandidate.Candidate["quirks"]?["evolving_variable"]?
            ["evolution_duration_remaining"]?.GetValue<int>();
        Assert(
            variableEvolutionDuration is >= 3 and <= 15 &&
            repeatedVariableEvolutionDuration == variableEvolutionDuration,
            "A variable evolution duration should be deterministic for one seed and stay inside its own inclusive range.");
        Assert(
            variableEvolutionCandidate.Preview.Name == generatedCandidate.Preview.Name &&
            variableEvolutionCandidate.Preview.ColourVariation == generatedCandidate.Preview.ColourVariation &&
            variableEvolutionCandidate.Preview.CombatSkills.SequenceEqual(generatedCandidate.Preview.CombatSkills) &&
            variableEvolutionCandidate.Preview.CampingSkills.SequenceEqual(generatedCandidate.Preview.CampingSkills),
            "Evolution-duration initialization must not shift the existing hero name, skin, or skill random sequence.");
        var reversedEvolutionSelectionCandidate = StagecoachHeroCandidateFactory.Generate(
            heroCatalog,
            localHero,
            seed: 1729,
            selectedInitialQuirkIds: ["steady", "evolving_variable"]);
        Assert(
            reversedEvolutionSelectionCandidate.Candidate["quirks"]?["evolving_variable"]?
                ["evolution_duration_remaining"]?.GetValue<int>() == variableEvolutionDuration,
            "An evolution duration should depend on the seed and quirk ID, not selection order or neighboring quirks.");

        var zeroEvolutionCandidate = StagecoachHeroCandidateFactory.Generate(
            heroCatalog,
            localHero,
            seed: 1729,
            selectedInitialQuirkIds: ["evolving_zero"]);
        Assert(
            zeroEvolutionCandidate.Candidate["quirks"]?["evolving_zero"]?
                ["evolution_duration_remaining"]?.GetValue<int>() == 0 &&
            zeroEvolutionCandidate.Preview.Warnings.Any(warning =>
                warning.Contains("配置 0–0", StringComparison.Ordinal)),
            "An author-defined zero-to-zero evolution range should preserve its intentional immediate expiry.");

        var deathEvolutionCandidate = StagecoachHeroCandidateFactory.Generate(
            heroCatalog,
            localHero,
            seed: 1729,
            selectedInitialQuirkIds: ["evolving_death"]);
        Assert(
            deathEvolutionCandidate.Candidate["quirks"]?["evolving_death"]?
                ["evolution_duration_remaining"]?.GetValue<int>() == 61 &&
            deathEvolutionCandidate.Preview.Warnings.Any(warning =>
                warning.Contains("到期死亡", StringComparison.Ordinal)),
            "A death evolution should initialize its configured duration without requiring a target quirk ID.");

        var diseaseCandidate = StagecoachHeroCandidateFactory.Generate(
            heroCatalog,
            localHero,
            seed: 1729,
            selectedInitialQuirkIds: ["test_disease", "test_disease_two", "test_disease_three"]);
        Assert(
            diseaseCandidate.Preview.Diseases.SequenceEqual(["test_disease", "test_disease_two", "test_disease_three"]) &&
            diseaseCandidate.Preview.PositiveQuirks.Count == 0 &&
            diseaseCandidate.Preview.NegativeQuirks.Count == 0,
            "Diseases should use their independent preview bucket instead of consuming negative-quirk slots.");
        Assert(
            ((JsonObject)diseaseCandidate.Candidate["quirks"]!).Count == 3,
            "Three selected diseases should use the same canonical stagecoach quirk map.");

        var hpCandidate = StagecoachHeroCandidateFactory.Generate(
            heroCatalog,
            localHero,
            seed: 1729,
            selectedInitialQuirkIds: ["natural"]);
        Assert(Math.Abs(hpCandidate.Preview.CurrentHp - 24.0) < 0.000001, "A selected no_trinkets HP quirk should set full current_hp exactly once.");
        Assert(
            ((JsonObject)hpCandidate.Candidate["actor"]!["buff_group"]!).Count == 0,
            "A selected HP quirk must still leave actor.buff_group empty.");

        var baseGameStackedHpCandidate = StagecoachHeroCandidateFactory.Generate(
            heroCatalog,
            localHero,
            seed: 1729,
            selectedInitialQuirkIds: ["tough", "soft"]);
        Assert(
            Math.Abs(baseGameStackedHpCandidate.Preview.CurrentHp - 21.0) < 0.000001,
            "Compatible base-game max-HP quirks should add their percentages before applying the class base HP.");
        Assert(
            baseGameStackedHpCandidate.Candidate["actor"]?["current_hp"]?.GetValue<double>() == 21.0,
            "Candidate JSON current_hp should preserve the summed base-game max-HP result.");

        var mixedRuleStackedHpCandidate = StagecoachHeroCandidateFactory.Generate(
            heroCatalog,
            localHero,
            seed: 1729,
            selectedInitialQuirkIds: ["natural", "tough"]);
        Assert(
            Math.Abs(mixedRuleStackedHpCandidate.Preview.CurrentHp - 26.0) < 0.000001,
            "An always modifier and an active no_trinkets modifier should add for an empty-trinket candidate.");
        Assert(
            ((JsonObject)mixedRuleStackedHpCandidate.Candidate["actor"]!["buff_group"]!).Count == 0,
            "Stacked max-HP quirks must not be duplicated into actor.buff_group.");

        var flatHpCandidate = StagecoachHeroCandidateFactory.Generate(
            heroCatalog,
            localHero,
            seed: 1729,
            selectedInitialQuirkIds: ["flat_hp_quirk"]);
        Assert(
            Math.Abs(flatHpCandidate.Preview.CurrentHp - 24.0) < 0.000001,
            "A constant flat max-HP modifier should be added to the selected armour base HP.");

        var multipleHpCandidate = StagecoachHeroCandidateFactory.Generate(
            heroCatalog,
            localHero,
            seed: 1729,
            selectedInitialQuirkIds: ["multiple_hp_quirk"]);
        Assert(
            Math.Abs(multipleHpCandidate.Preview.CurrentHp - 21.0) < 0.000001,
            "Multiple percentage modifiers declared by one quirk should be summed before multiplication.");

        var mixedFlatPercentageCandidate = StagecoachHeroCandidateFactory.Generate(
            heroCatalog,
            localHero,
            seed: 1729,
            selectedInitialQuirkIds: ["mixed_hp_quirk"]);
        Assert(
            Math.Abs(mixedFlatPercentageCandidate.Preview.CurrentHp - 24.96) < 0.000001,
            "Mixed HP modifiers should use (base + flat) * (1 + percentage), not flat-after-percentage ordering.");

        var runtimeConditionalHpCandidate = StagecoachHeroCandidateFactory.Generate(
            heroCatalog,
            localHero,
            seed: 1729,
            selectedInitialQuirkIds: ["afflicted_hp_quirk", "other_mode_hp_quirk", "light_hp_quirk"]);
        Assert(
            Math.Abs(runtimeConditionalHpCandidate.Preview.CurrentHp - 20.0) < 0.000001 &&
            ((JsonObject)runtimeConditionalHpCandidate.Candidate["actor"]!["buff_group"]!).Count == 0,
            "Runtime-only affliction, mode, and light modifiers should not be pre-applied to a stagecoach candidate.");

        var mutuallyExclusiveModeCandidate = StagecoachHeroCandidateFactory.Generate(
            heroCatalog,
            localHero,
            seed: 1729,
            selectedInitialQuirkIds: ["mode_a_weakness", "mode_b_weakness"]);
        Assert(
            Math.Abs(mutuallyExclusiveModeCandidate.Preview.CurrentHp - 20.0) < 0.000001,
            "Different in_mode conditions should be evaluated as mutually exclusive runtime states, not arbitrary independent Buff subsets.");

        var levelFourFlatBoundaryCandidate = StagecoachHeroCandidateFactory.Generate(
            heroCatalog,
            localHero,
            seed: 1729,
            resolveLevel: 4,
            selectedInitialQuirkIds: ["flat_level_boundary"]);
        Assert(
            Math.Abs(levelFourFlatBoundaryCandidate.Preview.CurrentHp - 8.0) < 0.000001,
            "HP safety and current_hp calculation should use the selected level's armour HP, not level zero.");

        var levelFourCandidate = StagecoachHeroCandidateFactory.Generate(
            heroCatalog,
            localHero,
            seed: 1729,
            resolveLevel: 4,
            selectedInitialQuirkIds: ["natural"]);
        Assert(
            levelFourCandidate.Preview is { ResolveLevel: 4, ResolveXp: 24, WeaponRank: 3, ArmourRank: 3 } &&
            Math.Abs(levelFourCandidate.Preview.CurrentHp - 38.4) < 0.000001,
            "A level-four candidate should link XP, equipment rank, armour HP, and the selected HP quirk exactly once.");
        Assert(
            levelFourCandidate.Candidate["resolveXp"]?.GetValue<int>() == 24 &&
            levelFourCandidate.Candidate["weapon_rank"]?.GetValue<int>() == 3 &&
            levelFourCandidate.Candidate["armour_rank"]?.GetValue<int>() == 3,
            "Level-four candidate JSON does not match its derived progression profile.");
        Assert(
            ((JsonObject)levelFourCandidate.Candidate["skills"]!["selected_combat_skills"]!).All(pair => pair.Value?.GetValue<int>() == 0) &&
            ((JsonObject)levelFourCandidate.Candidate["skills"]!["selected_camping_skills"]!).All(pair => pair.Value?.GetValue<int>() == 0),
            "Selected skill maps should retain zero values at non-zero hero levels.");
        Assert(
            levelFourCandidate.UpgradePurchases.Count == 18 &&
            levelFourCandidate.UpgradePurchases.Count(purchase => purchase.TreeId == "local_hero.weapon") == 3 &&
            levelFourCandidate.UpgradePurchases.Count(purchase => purchase.TreeId == "local_hero.armour") == 3 &&
            levelFourCandidate.UpgradePurchases.Count(purchase =>
                purchase.TreeId == "local_hero.local_skill" ||
                purchase.TreeId == "local_hero.local_skill_two") == 8 &&
            levelFourCandidate.UpgradePurchases.Count(purchase =>
                expectedCampingTreeIds.Contains(purchase.TreeId) &&
                purchase.RequirementCode == "0") == 4 &&
            levelFourCandidate.UpgradePurchases.Any(purchase =>
                purchase.TreeId == "local_hero.local_skill" &&
                purchase.RequirementCode == "B") &&
            levelFourCandidate.UpgradePurchases.Any(purchase =>
                purchase.TreeId == "local_hero.local_skill" &&
                purchase.RequirementCode == "A") &&
            levelFourCandidate.UpgradePurchases.All(purchase => purchase.RequirementCode != "c"),
            "A level-four candidate should unlock every camping skill and every combat skill through the selected level while preserving case-sensitive custom requirement codes.");

        var levelSixCandidate = StagecoachHeroCandidateFactory.Generate(
            heroCatalog,
            localHero,
            seed: 1729,
            resolveLevel: 6,
            selectedInitialQuirkIds: []);
        Assert(
            levelSixCandidate.Preview is { ResolveLevel: 6, ResolveXp: 48, WeaponRank: 4, ArmourRank: 4 } &&
            Math.Abs(levelSixCandidate.Preview.CurrentHp - 36.0) < 0.000001,
            "A max-level blank candidate should use the final active equipment template without adding quirks.");

        var fivePositiveCandidate = StagecoachHeroCandidateFactory.Generate(
            heroCatalog,
            localHero,
            seed: 1729,
            selectedInitialQuirkIds: ["steady", "eagle_eye", "hard_skinned", "slugger", "warrior_of_light"]);
        Assert(fivePositiveCandidate.Preview.PositiveQuirks.Count == 5, "Five positive ordinary quirks should be allowed.");
        var fiveNegativeCandidate = StagecoachHeroCandidateFactory.Generate(
            heroCatalog,
            localHero,
            seed: 1729,
            selectedInitialQuirkIds: ["clumsy", "slowdraw", "off_guard", "nervous", "weak_grip"]);
        Assert(fiveNegativeCandidate.Preview.NegativeQuirks.Count == 5, "Five negative ordinary quirks should be allowed.");
        var fiveNegativeAndThreeDiseasesCandidate = StagecoachHeroCandidateFactory.Generate(
            heroCatalog,
            localHero,
            seed: 1729,
            selectedInitialQuirkIds:
            [
                "clumsy", "slowdraw", "off_guard", "nervous", "weak_grip",
        "test_disease", "test_disease_two", "test_disease_three"
            ]);
        Assert(
            fiveNegativeAndThreeDiseasesCandidate.Preview.NegativeQuirks.Count == 5 &&
            fiveNegativeAndThreeDiseasesCandidate.Preview.Diseases.Count == 3,
            "The disease cap should be independent from the five-negative-quirk cap.");

        AssertHeroGenerationRejected(
            heroCatalog,
            localHero,
            ["steady", "eagle_eye", "hard_skinned", "slugger", "warrior_of_light", "robust"],
            "最多正面 5 个");
        AssertHeroGenerationRejected(
            heroCatalog,
            localHero,
            ["clumsy", "slowdraw", "off_guard", "nervous", "weak_grip", "fearful"],
            "负面 5 个");
        AssertHeroGenerationRejected(heroCatalog, localHero, ["tough", "fragile"], "互斥");
        AssertHeroGenerationRejected(heroCatalog, localHero, ["unknown_hp_rule"], "当前不能显式写入");
        AssertHeroGenerationRejected(heroCatalog, localHero, ["unknown_hp_disease"], "当前不能显式写入");
        AssertHeroGenerationRejected(
            heroCatalog,
            localHero,
            ["half_weakness", "light_hp_quirk"],
            "可达条件");
        AssertHeroGenerationRejected(
            heroCatalog,
            localHero,
            ["light_hp_quirk", "rounding_weakness_a", "rounding_weakness_b", "rounding_weakness_c"],
            "可达条件");
        AssertHeroGenerationRejected(
            heroCatalog,
            localHero,
            ["test_disease", "test_disease_two", "test_disease_three", "test_disease_four"],
            "疾病 3 个");
        AssertHeroGenerationRejected(heroCatalog, localHero, ["steady", "STEADY"], "重复选择");
        AssertHeroGenerationRejected(heroCatalog, localHero, ["missing_quirk"], "不在当前活动内容目录");
        AssertHeroLevelGenerationRejected(heroCatalog, localHero, 7, "0 到 6");

        var referenceStagecoachCandidate = JsonNode.Parse(
            """
    {
      "rescued": false,
      "actor": {
        "name": "Contract Recruit",
        "current_hp": 23.4,
        "stunned": 0,
        "combat_ready": false,
        "damage_source_data": 0,
        "damage_source_type": 0,
        "damage_type": 0,
        "colour_variation": 3,
        "enemy_rank_targets": 0,
        "friendly_rank_targets": 0,
        "performing_turn": 0,
        "controlling_actor_guid": 0,
        "controlling_duration": 0,
        "current_mode_id": 0,
        "rounds_in_ranks": 0,
        "check_round_ranks": 0,
        "health_damage_blocks": 0,
        "buff_group_next_guid": 2,
        "buff_group": {},
        "actor_dot": {}
      },
      "heroClass": "hellion",
      "resolveXp": 0,
      "m_Stress": 0.0,
      "is_death_heart_attack_completed": false,
      "visited_deaths_door": false,
      "deaths_door_enter_effect_round_cooldown": 0,
      "has_had_heart_attack": false,
      "backer_hero": false,
      "steps_taken": 0,
      "enemies_killed": 0,
      "weapon_rank": 0,
      "armour_rank": 0,
      "dd_test_survived": 0,
      "affliction_type_id": "",
      "affliction_severity": 0,
      "virtue_type_id": "",
      "provisions_consumed": 0,
      "quirks": {
        "warren_explorer": {
          "is_new": true,
          "is_locked": false,
          "mission_count": 3637668,
          "replaces_quirk": 0,
          "replaces_quirk_viewed": false,
          "evolution_duration_remaining": 0
        },
        "resolution": {
          "is_new": true,
          "is_locked": false,
          "mission_count": 2104304579,
          "replaces_quirk": 0,
          "replaces_quirk_viewed": false,
          "evolution_duration_remaining": 0
        },
        "fragile": {
          "is_new": true,
          "is_locked": false,
          "mission_count": 2104304579,
          "replaces_quirk": 0,
          "replaces_quirk_viewed": false,
          "evolution_duration_remaining": 0
        }
      },
      "skills": {
        "selected_combat_skills": {
          "wicked_hack": 0,
          "iron_swan": 0,
          "barbaric_yawp": 0,
          "bleed_out": 0
        },
        "selected_camping_skills": {
          "first_aid": 0,
          "revel": 0,
          "reject_the_gods": 0
        }
      },
      "trinkets": {
        "items": {}
      },
      "has_item_Tracking": true,
      "item_tracking": {
        "supply": {}
      },
      "number_of_successful_darkest_dungeon_quests": 0,
      "is_from_town_event": false
    }
    """) as JsonObject ?? throw new InvalidDataException("Stagecoach candidate fixture is invalid.");
        Assert(
            generatedCandidate.Candidate.Select(pair => pair.Key).Order(StringComparer.Ordinal)
                .SequenceEqual(referenceStagecoachCandidate.Select(pair => pair.Key).Order(StringComparer.Ordinal)),
            "Generated candidate root envelope differs from a complete natural stagecoach candidate.");
        Assert(
            (generatedCandidate.Candidate["actor"] as JsonObject)!.Select(pair => pair.Key).Order(StringComparer.Ordinal)
                .SequenceEqual((referenceStagecoachCandidate["actor"] as JsonObject)!.Select(pair => pair.Key).Order(StringComparer.Ordinal)),
            "Generated actor envelope differs from a complete natural stagecoach candidate.");
        var stagecoachCandidate = levelFourCandidate.Candidate;
        var existingCandidateTown = JsonNode.Parse(
            """
    {
      "base_root": {
        "buildings": {
          "stage_coach": {
            "store": {
              "hero_recruit": {
                "generated": {
                  "100": {
                    "heroClass": "hellion",
                    "actor": {},
                    "quirks": { "context_special": {} }
                  }
                }
              },
              "shard_hero_recruit": {
                "generated": {
                  "200": {
                    "heroClass": "shieldbreaker",
                    "actor": {},
                    "quirks": {
                      "context_special": {},
                      "context_roster_limited": {}
                    }
                  }
                }
              }
            }
          }
        }
      }
    }
    """) as JsonObject ?? throw new InvalidDataException("Stagecoach mutation town fixture is invalid.");
        var existingCandidateRoster = JsonNode.Parse(
            """
    {
      "base_root": {
        "nextGuid": 364,
        "heroes": {
          "1": {
            "heroClass": "crusader",
            "hero_file_data": {
              "raw_data": {
                "base_root": {
                  "quirks": {
                    "context_special": {},
                    "context_roster_limited": {}
                  }
                }
              }
            }
          }
        }
      }
    }
    """) as JsonObject ?? throw new InvalidDataException("Stagecoach mutation roster fixture is invalid.");
        var existingCandidateUpgrades = JsonNode.Parse(
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
    """) as JsonObject ?? throw new InvalidDataException("Stagecoach mutation upgrades fixture is invalid.");
        var contextualLimitPreviews = StagecoachHeroSaveEditor.AnalyzeQuirkLimits(
            existingCandidateTown,
            existingCandidateRoster,
            contextLimitedCandidate.Candidate,
            heroCatalog.InitialQuirks);
        Assert(
            contextualLimitPreviews.Count == 1 &&
            contextualLimitPreviews.All(item => item.QuirkId == "context_special"),
            "roster_limit must not enter stagecoach-generation limit analysis because the game enforces it later during recruitment.");
        var singletonLimitPreview = contextualLimitPreviews.Single(item => item.QuirkId == "context_special");
        Assert(
            singletonLimitPreview is
            {
                ExistingRosterHeroes: 1,
                ExistingStagecoachCandidates: 2,
                ExistingHeroes: 3,
                ResultingHeroes: 4,
                DefinitionLimit: 1,
                ExceedsDefinitionLimit: true
            },
            "A singleton preview must count the owned roster, ordinary and shard stagecoach candidates before adding one.");
        var (mutatedTown, mutatedRoster, mutatedUpgrades, mutationPreview) = StagecoachHeroSaveEditor.AddCandidate(
            existingCandidateTown,
            existingCandidateRoster,
            existingCandidateUpgrades,
            stagecoachCandidate,
            levelFourCandidate.UpgradePurchases);
        var mutatedGenerated = mutatedTown["base_root"]?["buildings"]?["stage_coach"]?["store"]?["hero_recruit"]?["generated"] as JsonObject;
        var mutatedPurchases = mutatedUpgrades["base_root"]?["purchases"] as JsonObject;
        Assert(mutationPreview.CandidateGuid == 364, "Stagecoach candidate should use roster nextGuid without filling holes.");
        Assert(
            mutationPreview is { ResolveXp: 24, WeaponRank: 3, ArmourRank: 3, UpgradePurchaseCount: 18 },
            "Stagecoach mutation preview should preserve the candidate progression metadata.");
        Assert(mutationPreview.ExistingCandidates == 1 && mutationPreview.ResultingCandidates == 2, "Existing normal recruits must be preserved while appending.");
        Assert(mutatedGenerated?.ContainsKey("100") == true && mutatedGenerated.ContainsKey("364"), "Normal recruit append removed an existing candidate or missed the new GUID.");
        Assert(mutatedTown["base_root"]?["buildings"]?["stage_coach"]?["store"]?["shard_hero_recruit"]?["generated"]?["200"] is JsonObject, "The shard recruit pool must remain unchanged.");
        Assert(mutatedRoster["base_root"]?["nextGuid"]?.GetValue<int>() == 365, "Roster nextGuid should advance exactly once.");
        Assert((mutatedRoster["base_root"]?["heroes"] as JsonObject)?.Count == 1, "Stagecoach append must not modify the owned roster.");
        Assert(
            JsonNode.DeepEquals(
                mutatedRoster["base_root"]?["heroes"],
                existingCandidateRoster["base_root"]?["heroes"]),
            "Stagecoach append changed the owned roster hero subtree.");
        Assert((existingCandidateTown["base_root"]?["buildings"]?["stage_coach"]?["store"]?["hero_recruit"]?["generated"] as JsonObject)?.Count == 1, "Pure mutation changed the input town document.");
        Assert(existingCandidateRoster["base_root"]?["nextGuid"]?.GetValue<int>() == 364, "Pure mutation changed the input roster document.");
        Assert(
            mutatedPurchases?.Count == 19 &&
            mutatedPurchases.ContainsKey("7") &&
            mutatedPurchases.ContainsKey("8") &&
            mutatedPurchases.Select(pair => pair.Value)
                .OfType<JsonObject>()
                .Count(purchase => purchase["instance_number"]?.GetValue<int>() == 364) == 18,
            "Stagecoach mutation should append every level-derived upgrade purchase after the highest existing key.");
        var expectedLocalSkillHash = unchecked((int)HashLoc2Key("local_hero.local_skill"));
        Assert(
            mutatedPurchases!.Select(pair => pair.Value).OfType<JsonObject>().Any(purchase =>
                purchase["instance_number"]?.GetValue<int>() == 364 &&
                purchase["tree_id"]?.GetValue<int>() == expectedLocalSkillHash &&
                purchase["requirement_code"]?.GetValue<string>() == "B"),
            "Stagecoach mutation should use the game's existing name hash and preserve a Mod's custom requirement code.");
        Assert(
            mutatedPurchases!.Select(pair => pair.Value).OfType<JsonObject>().Where(purchase =>
                    purchase["instance_number"]?.GetValue<int>() == 364 &&
                    purchase["tree_id"]?.GetValue<int>() == expectedLocalSkillHash)
                .Select(purchase => purchase["requirement_code"]?.GetValue<string>())
                .ToHashSet(StringComparer.Ordinal)
                .IsSupersetOf(["a", "A"]),
            "Requirement codes that differ only by case must remain distinct purchases.");
        var expectedCampingSkillHash = unchecked((int)HashLoc2Key("local_hero.local_camp_two"));
        Assert(
            mutatedPurchases!.Select(pair => pair.Value).OfType<JsonObject>().Any(purchase =>
                purchase["instance_number"]?.GetValue<int>() == 364 &&
                purchase["tree_id"]?.GetValue<int>() == expectedCampingSkillHash &&
                purchase["requirement_code"]?.GetValue<string>() == "0"),
            "Stagecoach mutation should persist implicit class-prefixed camping unlock trees with requirement code zero.");
        Assert(
            (existingCandidateUpgrades["base_root"]?["purchases"] as JsonObject)?.Count == 1,
            "Pure mutation changed the input upgrades document.");

        var nonRoundTrippableRequirementBlocked = false;
        try
        {
            _ = StagecoachHeroSaveEditor.AddCandidate(
                existingCandidateTown,
                existingCandidateRoster,
                existingCandidateUpgrades,
                stagecoachCandidate,
                [new HeroUpgradePurchase("local_hero.local_skill", "too_long")]);
        }
        catch (InvalidDataException ex) when (ex.Message.Contains("losslessly", StringComparison.Ordinal))
        {
            nonRoundTrippableRequirementBlocked = true;
        }

        Assert(
            nonRoundTrippableRequirementBlocked,
            "A multi-character requirement code must be rejected before DSON can silently truncate it.");

        Assert(HashLoc2Key("tree_1e") == HashLoc2Key("tree_20"), "The upgrade collision fixture is invalid.");
        var collidingTreeIdsBlocked = false;
        try
        {
            _ = StagecoachHeroSaveEditor.AddCandidate(
                existingCandidateTown,
                existingCandidateRoster,
                existingCandidateUpgrades,
                stagecoachCandidate,
                [
                    new HeroUpgradePurchase("tree_1e", "0"),
            new HeroUpgradePurchase("tree_20", "1")
                ]);
        }
        catch (InvalidDataException ex) when (ex.Message.Contains("same game hash", StringComparison.Ordinal))
        {
            collidingTreeIdsBlocked = true;
        }

        Assert(
            collidingTreeIdsBlocked,
            "Different upgrade tree ids with the same game hash must be rejected even when their requirement codes differ.");

        var invalidGuidRoster = JsonNode.Parse(
            """
    {
      "base_root": {
        "nextGuid": 200,
        "heroes": {}
      }
    }
    """) as JsonObject ?? throw new InvalidDataException("Invalid GUID roster fixture is invalid.");
        var invalidGuidBlocked = false;
        try
        {
            _ = StagecoachHeroSaveEditor.AddCandidate(
                existingCandidateTown,
                invalidGuidRoster,
                existingCandidateUpgrades,
                stagecoachCandidate,
                levelFourCandidate.UpgradePurchases);
        }
        catch (InvalidDataException ex) when (ex.Message.Contains("highest existing hero GUID", StringComparison.Ordinal))
        {
            invalidGuidBlocked = true;
        }

        Assert(invalidGuidBlocked, "A stale or colliding roster nextGuid must be blocked instead of probing for a hole.");

        var stagecoachLocations = new SaveEditorLocations(
            Path.Combine(runRoot, "stagecoach-appdata"),
            Path.Combine(runRoot, "stagecoach-appdata", "workspaces"),
            Path.Combine(runRoot, "stagecoach-appdata", "backups"));
        var stagecoachService = new SaveEditService(codec, stagecoachLocations);
        var preparedContextLimitedStagecoach = await stagecoachService.PrepareStagecoachHeroEditAsync(
            profile,
            contextLimitedCandidate,
            heroCatalog,
            activeContent);
        var preparedSingletonLimit = preparedContextLimitedStagecoach.Preview.QuirkLimits.Single(item =>
            item.QuirkId == "context_special");
        Assert(
            preparedSingletonLimit is
            {
                ExistingRosterHeroes: 1,
                ExistingStagecoachCandidates: 0,
                ResultingHeroes: 2,
                DefinitionLimit: 1,
                ExceedsDefinitionLimit: true
            },
            "Prepared hero preview should retain an over-limit singleton warning without blocking the candidate edit.");
        Assert(
            !preparedContextLimitedStagecoach.Preview.QuirkLimits.Any(item =>
                item.QuirkId == "context_roster_limited"),
            "A roster_limit quirk must not create a stagecoach preview warning because the editor does not recruit the candidate into the owned roster.");
        var preparedStagecoach = await stagecoachService.PrepareStagecoachHeroEditAsync(
            profile,
            levelFourCandidate,
            heroCatalog,
            activeContent);
        Assert(preparedStagecoach.Preview.CandidateGuid == 364, "Prepared stagecoach candidate should use save nextGuid 364.");
        Assert(
            preparedStagecoach.Preview is { ResolveXp: 24, WeaponRank: 3, ArmourRank: 3, UpgradePurchaseCount: 18 },
            "Prepared stagecoach preview should retain non-zero XP and equipment ranks.");
        Assert(preparedStagecoach.Preview.ExistingCandidates == 0 && preparedStagecoach.Preview.ResultingCandidates == 1, "Empty normal recruit pool should receive exactly one candidate.");
        Assert(preparedStagecoach.Preview.RosterHeroCount == 36, "A full 36-hero roster should remain valid for stagecoach generation.");
        Assert(ReadRevision(preparedStagecoach.TownFile.SourceCopyPath).SequenceEqual(ReadRevision(preparedStagecoach.TownFile.EncodedPath)), "Town revision bytes were not preserved.");
        Assert(ReadRevision(preparedStagecoach.RosterFile.SourceCopyPath).SequenceEqual(ReadRevision(preparedStagecoach.RosterFile.EncodedPath)), "Roster revision bytes were not preserved.");
        Assert(ReadRevision(preparedStagecoach.UpgradesFile.SourceCopyPath).SequenceEqual(ReadRevision(preparedStagecoach.UpgradesFile.EncodedPath)), "Upgrades revision bytes were not preserved.");

        var activeHeroUpgradePath = Path.Combine(localHeroUpgradeRoot, "local_hero.upgrades.json");
        var activeHeroUpgradeBytes = File.ReadAllBytes(activeHeroUpgradePath);
        var staleHeroTemplateCommitBlocked = false;
        try
        {
            var changedUpgradeTemplate = File.ReadAllText(activeHeroUpgradePath)
                .Replace(
                    "\"code\": \"c\", \"prerequisite_resolve_level\": 5",
                    "\"code\": \"c\", \"prerequisite_resolve_level\": 4",
                    StringComparison.Ordinal);
            Assert(
                !changedUpgradeTemplate.Equals(
                    Encoding.UTF8.GetString(activeHeroUpgradeBytes),
                    StringComparison.Ordinal),
                "The stale hero upgrade template fixture did not change.");
            File.WriteAllText(activeHeroUpgradePath, changedUpgradeTemplate, new UTF8Encoding(false));
            try
            {
                _ = await stagecoachService.CommitAsync(preparedStagecoach);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains(
                "templates changed after the hero preview",
                StringComparison.Ordinal))
            {
                staleHeroTemplateCommitBlocked = true;
            }
        }
        finally
        {
            File.WriteAllBytes(activeHeroUpgradePath, activeHeroUpgradeBytes);
        }

        Assert(
            staleHeroTemplateCommitBlocked,
            "Stagecoach commit should reject a hero upgrade template changing after preview.");
        Assert(
            !Directory.Exists(stagecoachLocations.BackupDirectory),
            "A stale active-content preview should be rejected before creating a profile backup.");

        var staleUpgrades = File.ReadAllBytes(upgradesSavePath);
        staleUpgrades[^1] ^= 0x01;
        File.WriteAllBytes(upgradesSavePath, staleUpgrades);
        var staleStagecoachCommitBlocked = false;
        try
        {
            _ = await stagecoachService.CommitAsync(preparedStagecoach);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("changed after preview", StringComparison.Ordinal))
        {
            staleStagecoachCommitBlocked = true;
        }

        Assert(staleStagecoachCommitBlocked, "Stagecoach commit should reject any live transaction file changing after preview.");
        Assert(!Directory.Exists(stagecoachLocations.BackupDirectory), "A stale three-file preview should be rejected before creating a backup.");
        File.Copy(preparedStagecoach.UpgradesFile.SourceCopyPath, upgradesSavePath, overwrite: true);

        var rollbackObserved = false;
        string? rollbackBackupDirectory = null;
        using (File.Open(townSavePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            try
            {
                _ = await stagecoachService.CommitAsync(preparedStagecoach);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("restored", StringComparison.Ordinal))
            {
                rollbackObserved = true;
                rollbackBackupDirectory = Directory.EnumerateDirectories(
                        Path.Combine(stagecoachLocations.BackupDirectory, "contract-user", "profile_7"))
                    .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
                    .First();
            }
        }

        Assert(rollbackObserved, "A town replacement failure should restore the already-replaced upgrades and roster files.");
        Assert(File.ReadAllBytes(rosterSavePath).SequenceEqual(File.ReadAllBytes(preparedStagecoach.RosterFile.SourceCopyPath)), "Roster was not restored byte-for-byte after town replacement failed.");
        Assert(File.ReadAllBytes(upgradesSavePath).SequenceEqual(File.ReadAllBytes(preparedStagecoach.UpgradesFile.SourceCopyPath)), "Upgrades were not restored byte-for-byte after town replacement failed.");
        Assert(File.ReadAllBytes(townSavePath).SequenceEqual(File.ReadAllBytes(preparedStagecoach.TownFile.SourceCopyPath)), "Unreplaced town file changed during rollback test.");
        Assert(rollbackBackupDirectory is not null && File.Exists(Path.Combine(rollbackBackupDirectory, "transaction-state.json")), "Rollback transaction state was not recorded.");
        Assert(!File.Exists(Path.Combine(rollbackBackupDirectory!, "commit-result.json")), "A rolled-back transaction must not retain a successful commit result.");
        var rollbackState = JsonNode.Parse(File.ReadAllText(Path.Combine(rollbackBackupDirectory!, "transaction-state.json"))) as JsonObject;
        Assert(rollbackState?["status"]?.GetValue<string>() == "restored", "Rollback transaction state should finish as restored.");

        var stagecoachCommit = await stagecoachService.CommitAsync(preparedStagecoach);
        Assert(File.Exists(Path.Combine(stagecoachCommit.BackupDirectory, "persist.town.json")), "Town backup is missing.");
        Assert(File.Exists(Path.Combine(stagecoachCommit.BackupDirectory, "persist.roster.json")), "Roster backup is missing.");
        Assert(File.Exists(Path.Combine(stagecoachCommit.BackupDirectory, "persist.upgrades.json")), "Upgrades backup is missing.");
        Assert(File.Exists(Path.Combine(stagecoachCommit.BackupDirectory, "backup-manifest.json")), "Stagecoach backup manifest is missing.");
        var stagecoachBackupManifest = JsonNode.Parse(
            File.ReadAllText(Path.Combine(stagecoachCommit.BackupDirectory, "backup-manifest.json"))) as JsonObject;
        Assert(
            stagecoachBackupManifest?["resolveXp"]?.GetValue<int>() == 24 &&
            stagecoachBackupManifest["weaponRank"]?.GetValue<int>() == 3 &&
            stagecoachBackupManifest["armourRank"]?.GetValue<int>() == 3 &&
            stagecoachBackupManifest["upgradePurchaseCount"]?.GetValue<int>() == 18,
            "Stagecoach backup manifest should record the generated level profile metadata.");
        Assert(File.Exists(Path.Combine(stagecoachCommit.BackupDirectory, "transaction-state.json")), "Stagecoach transaction state is missing.");
        Assert(File.Exists(Path.Combine(stagecoachCommit.BackupDirectory, "commit-result.json")), "Stagecoach commit result is missing.");

        var committedTownDecoded = Path.Combine(runRoot, "committed.persist.town.json");
        var committedRosterDecoded = Path.Combine(runRoot, "committed.persist.roster.json");
        var committedUpgradesDecoded = Path.Combine(runRoot, "committed.persist.upgrades.json");
        await codec.DecodeAsync(townSavePath, committedTownDecoded);
        await codec.DecodeAsync(rosterSavePath, committedRosterDecoded);
        await codec.DecodeAsync(upgradesSavePath, committedUpgradesDecoded);
        var committedTownRoot = JsonNode.Parse(File.ReadAllText(committedTownDecoded)) as JsonObject
            ?? throw new InvalidDataException("Committed town did not decode to an object.");
        var committedRosterRoot = JsonNode.Parse(File.ReadAllText(committedRosterDecoded)) as JsonObject
            ?? throw new InvalidDataException("Committed roster did not decode to an object.");
        var committedUpgradesRoot = JsonNode.Parse(File.ReadAllText(committedUpgradesDecoded)) as JsonObject
            ?? throw new InvalidDataException("Committed upgrades did not decode to an object.");
        var committedGenerated = committedTownRoot["base_root"]?["buildings"]?["stage_coach"]?["store"]?["hero_recruit"]?["generated"] as JsonObject;
        Assert(committedGenerated?.ContainsKey("364") == true, "Committed town is missing candidate GUID 364.");
        Assert(committedRosterRoot["base_root"]?["nextGuid"]?.GetValue<int>() == 365, "Committed roster nextGuid is wrong.");
        Assert((committedRosterRoot["base_root"]?["heroes"] as JsonObject)?.Count == 36, "Committed roster heroes were modified.");
        Assert(
            JsonNode.DeepEquals(committedRosterRoot["base_root"]?["heroes"], rosterHeroesSeed),
            "Committed roster hero subtree differs from the original 36 heroes.");
        var committedPurchases = committedUpgradesRoot["base_root"]?["purchases"] as JsonObject;
        Assert(
            committedPurchases?.Select(pair => pair.Value).OfType<JsonObject>().Count(purchase =>
                purchase["instance_number"]?.GetValue<int>() == 364) == 18,
            "Committed upgrades are missing the candidate's complete level-derived purchase plan.");

        var unfinishedBackupDirectory = Path.Combine(
            stagecoachLocations.BackupDirectory,
            "contract-user",
            "profile_7",
            "synthetic-unfinished");
        Directory.CreateDirectory(unfinishedBackupDirectory);
        var unfinishedStatePath = Path.Combine(unfinishedBackupDirectory, "transaction-state.json");
        File.WriteAllText(
            unfinishedStatePath,
            """{ "status": "replaced_roster" }""",
            new UTF8Encoding(false));
        var unfinishedTransactionBlocked = false;
        try
        {
            _ = await stagecoachService.PrepareStagecoachHeroEditAsync(
                profile,
                levelFourCandidate,
                heroCatalog,
                activeContent);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("unfinished stagecoach save transaction", StringComparison.Ordinal))
        {
            unfinishedTransactionBlocked = true;
        }

        Assert(unfinishedTransactionBlocked, "A non-terminal prior transaction must block a new stagecoach preview.");
        File.WriteAllText(
            unfinishedStatePath,
            """{ "status": "restored" }""",
            new UTF8Encoding(false));

    }
}
