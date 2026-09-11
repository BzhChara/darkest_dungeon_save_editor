internal static partial class ContractSuite
{
    private static HeroCandidateContractContext VerifyHeroCandidateContracts(
        HeroClassCatalogResult heroCatalog,
        HeroClassDefinition localHero)
    {
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
                purchase.RequirementCode == "0") == 2 &&
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
        var partialCombatCandidate = StagecoachHeroCandidateFactory.Generate(heroCatalog, partialCombatUpgradeHero, 1729, 0, []);
        Assert(partialCombatCandidate.UpgradePurchases.Contains(new HeroUpgradePurchase("local_hero.local_skill_two", "0")) &&
               partialCombatCandidate.Preview.Warnings.Any(warning => warning.Contains("仅做基础解锁", StringComparison.Ordinal)),
            "A selectable missing-tree skill must receive a base unlock and explain why no valid numeric reference could establish higher tiers.");

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
            "基础购买码 '0'");

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
            "基础购买码 '0'");

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
        var shardCandidate = StagecoachHeroCandidateFactory.Generate(
            heroCatalog,
            localHero,
            seed: 1729,
            selectedInitialQuirkIds: ["shard_hungry"]);
        Assert(
            shardCandidate.Preview.NegativeQuirks.SequenceEqual(["shard_hungry"]) &&
            shardCandidate.Candidate["quirks"]?["shard_hungry"] is JsonObject,
            "The shard mercenary quirk should be serialized normally before the save editor selects its target pool.");

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
            (float)baseGameStackedHpCandidate.Candidate["actor"]!["current_hp"]!.GetValue<double>() == (float)baseGameStackedHpCandidate.Preview.CurrentHp,
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
            "A level-four candidate should unlock every skill's base and preserve all eligible case-sensitive authored codes; extra letters do not imply combat tiers.");

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
        AssertHeroGenerationRejected(heroCatalog, localHero, ["steady", "steady"], "重复选择");
        AssertHeroGenerationRejected(heroCatalog, localHero, ["steady", "STEADY"], "不在当前活动内容目录");
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

        return new HeroCandidateContractContext(levelFourCandidate, contextLimitedCandidate, shardCandidate);
    }
}
