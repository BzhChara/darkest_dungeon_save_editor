internal static partial class ContractSuite
{
    private static void VerifyHeroQuirkContracts(
        HeroClassCatalogResult heroCatalog,
        HeroClassDefinition localHero)
    {
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
                        Amount: 0.2f,
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
            "A later non-HP Buff definition must not block explicit quirk selection.");
        var maxHpConflictQuirk = heroCatalog.InitialQuirks.Single(item => item.Id == "max_hp_conflict_quirk");
        Assert(
            maxHpConflictQuirk.WriteStatus == HeroInitialQuirkWriteStatus.Direct &&
            maxHpConflictQuirk.MaxHpModifiers is [{ Amount: 0.2f }],
            "Duplicate max-HP Buffs use the last complete native definition rather than becoming ambiguous.");
        Assert(
            heroCatalog.Issues.All(issue =>
                !issue.Contains("Buff 'CONFLICT_ACC'", StringComparison.Ordinal) &&
                !issue.Contains("Buff 'CONFLICT_MAXHP'", StringComparison.Ordinal)),
            "Resolved same-ID Buff replacements must not produce the former ambiguity diagnostic.");
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
        _ = StagecoachHeroCandidateFactory.Generate(heroCatalog, localHero, seed: 1729,
            selectedInitialQuirkIds: ["ambiguous_quirk"]);
        AssertInitialQuirkSelectionRejected(heroCatalog, localHero,
            ["evolution_conflict_quirk"], "进化目标 'evolution_target_b' 缺失");
        var lastBuffCandidate = StagecoachHeroCandidateFactory.Generate(heroCatalog, localHero, seed: 1729,
            selectedInitialQuirkIds: ["max_hp_conflict_quirk"]);
        Assert(Math.Abs(lastBuffCandidate.Preview.CurrentHp - localHero.BaseHp!.Value * (1 + (double)0.2f)) < 1e-9,
            "Explicit quirk selection and generated current_hp must use the winning Buff amount.");
        AssertInitialQuirkSelectionRejected(heroCatalog, localHero, ["fatal_weakness"], "合计 HP 修正无效");
        Assert(
            heroCatalog.InitialQuirks.Single(item => item.Id == "unknown_hp_rule").WriteStatus == HeroInitialQuirkWriteStatus.Unverified,
            "An unverified conditional max-HP rule must not enter the safe random pool.");

    }
}
