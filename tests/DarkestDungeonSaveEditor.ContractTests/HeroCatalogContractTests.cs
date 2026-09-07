internal static partial class ContractSuite
{
    private static HeroCatalogContractContext VerifyHeroCatalogContracts(
        ActiveContentSnapshot activeContent,
        TrinketCatalogResult activeCatalog,
        string localModRoot)
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
            runtimeHero.LocalizedName == BilingualContentName.Empty,
            "An unlisted XML authoring table must not supply hero names when a Mod has a manifest.");
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
            "A selectable class with an implicit multilevel skill must still resolve a uniquely compatible upgrade template, preserving exact ID ranking and the single-level distinction.");
        var compatibleLevelZeroCandidate = StagecoachHeroCandidateFactory.Generate(
            heroCatalog,
            compatibleUpgradeHero,
            seed: 1729,
            resolveLevel: 0,
            selectedInitialQuirkIds: []);
        Assert(
            compatibleLevelZeroCandidate.UpgradePurchases.Count == 3 &&
            compatibleLevelZeroCandidate.Preview.CombatSkills.Count == 1 &&
            compatibleLevelZeroCandidate.UpgradePurchases.Contains(
                new HeroUpgradePurchase("compatible_upgrade_hero.scaling_strike", "0")) &&
            compatibleLevelZeroCandidate.UpgradePurchases.Contains(
                new HeroUpgradePurchase("compatible_upgrade_hero.fixed_command", "0")) &&
            compatibleLevelZeroCandidate.UpgradePurchases.Contains(
                new HeroUpgradePurchase("compatible_upgrade_hero.implicit_command", "0")),
            "Single- and multilevel tree-less skills must receive base unlocks independently of the selected skill limit.");
        var compatibleHighLevelCandidate = StagecoachHeroCandidateFactory.Generate(heroCatalog, compatibleUpgradeHero, 1729, 6, []);
        Assert(compatibleUpgradeHero.GenerationAvailability.All(level => level.CanGenerate) &&
               compatibleHighLevelCandidate.UpgradePurchases.Where(purchase => purchase.TreeId == "compatible_upgrade_hero.implicit_command")
                   .Select(purchase => purchase.RequirementCode).SequenceEqual(["0", "1"]),
            "Real catalog resolution, availability, and generation must agree on selectable tree-less skill progression.");
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
        Assert(heroCatalog.Issues.All(issue => !issue.Contains("Quirk 'ambiguous_quirk'", StringComparison.Ordinal)), "Known native duplicate resolution should not be reported as ambiguity.");
        Assert(
            heroCatalog.InitialQuirks.Single(item => item.Id == "ambiguous_quirk") is { IsPositive: false },
            "The last loaded quirk definition supplies the effective properties.");
        var identicalCrossPathQuirk = heroCatalog.InitialQuirks.Single(item => item.Id == "identical_cross_path_quirk");
        Assert(
            identicalCrossPathQuirk is { IsPositive: true, WriteStatus: HeroInitialQuirkWriteStatus.Direct } &&
            heroCatalog.Issues.All(issue => !issue.Contains("identical_cross_path_quirk", StringComparison.Ordinal)),
            "Semantically identical quirk definitions at different paths should merge without a conflict.");
        Assert(
            heroCatalog.InitialQuirks.Single(item => item.Id == "evolution_conflict_quirk").Evolution is
                { DurationMin: 90, DurationMax: 120, TargetQuirkId: "evolution_target_b" },
            "Evolution metadata must come entirely from the last native definition.");
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
            heroCatalog.HeroNames.SequenceEqual(["Contract One", "Contract Two"]) &&
            heroCatalog.Issues.Any(issue => issue.Contains("Failed to read hero names", StringComparison.Ordinal) &&
                                            issue.Contains("lenient.string_table.xml", StringComparison.Ordinal)),
            "Malformed XML must be reported and excluded from the hero_name_* pool without losing valid names.");
        Assert(
            heroCatalog.HeroNames.All(name => name != "Ignored Unused Name") &&
            heroCatalog.Issues.All(issue => !issue.Contains("ignored.string_table.xml", StringComparison.OrdinalIgnoreCase)) &&
            activeCatalog.Issues.All(issue => !issue.Contains("ignored.string_table.xml", StringComparison.OrdinalIgnoreCase)) &&
            heroCatalog.Issues.All(issue => !issue.Contains("ignored_schinese.loc2", StringComparison.OrdinalIgnoreCase)) &&
            activeCatalog.Issues.All(issue => !issue.Contains("ignored_schinese.loc2", StringComparison.OrdinalIgnoreCase)),
            "Unused and platform-specific localization manifest entries must stay outside active XML/LOC2 catalogs.");

        return new HeroCatalogContractContext(heroCatalog, localHero);
    }
}
