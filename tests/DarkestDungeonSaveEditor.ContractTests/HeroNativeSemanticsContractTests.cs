internal static partial class ContractSuite
{
    private static void VerifyHeroNativeSemantics(ActiveContentSnapshot original,
        HeroClassDefinition originalHero, string runRoot)
    {
        var root = Path.Combine(runRoot, "hero-native-semantics");
        var stem = $"heroes/{originalHero.Id}/{originalHero.Id}";
        void Write(string path, string text) => WriteMultiMash(root, path, text);
        var text = File.ReadAllText(originalHero.SourcePath);
        var armourNames = new[] { "worn_coat", "replacement_armour_0", "Coat", "coat", "master_coat" };
        for (var rank = 0; rank < 5; rank++)
        {
            text = text.Replace($"local_hero_armour_{rank}", armourNames[rank], StringComparison.Ordinal)
                .Replace($"local_hero_weapon_{rank}", $"blade_{4 - rank}", StringComparison.Ordinal);
        }
        Write(stem + ".info.darkest", text + "\n" + """
            armour: .name worn_coat .hp 3100% generation: .is_generation_enabled yes
              .number_of_positive_quirks_min 0 .number_of_positive_quirks_max 0
              .number_of_negative_quirks_min 0 .number_of_negative_quirks_max 0
              .number_of_random_combat_skills 2suffix .card_chance 25%
            skill_selection: .can_select_combat_skills on .number_of_selected_combat_skills_max 2junk
            combat_skill: .id native_signal .level 0 .effect FIRST "" AFTER_EMPTY
            combat_skill: .id native_signal .level 0 .effect ".LeadingDot" AFTER_DOT
            combat_skill: .id native_signal .level 0 .effect "Comment // hidden" .effect FALSE_SIGNAL
            combat_skill: .id native_mode .level 0 .valid_modes form .form_effects FIRST
            combat_skill: .id native_mode .level 0 .valid_modes form .form_effects SECOND
            combat_skill: .id native_mode .level 0 .valid_modes form .form_effects ""
            combat_skill: .id native_mode .level 0 .orphan_effects FALSE_SIGNAL
            combat_skill: .id native_mode_empty .level 0 .valid_modes "" form .form_effects FIRST
            combat_skill: .id native_mode_dot .level 0 .valid_modes ".skip" form .form_effects FIRST
            combat_skill: .id native_mode_limit .level 0 .valid_modes "" "" "" "" "" "" "" "" form .form_effects FIRST
            combat_skill: .id native_boolean .level 0 .generation_guaranteed true
            combat_skill: .id native_boolean .level 0 .generation_guaranteed
            combat_skill: .id native_yes .level 0 .generation_guaranteed yes
            """ + "\ncombat_skill: .id native_limit .level 0 .effect " +
            string.Join(' ', Enumerable.Range(0, 17).Select(i => $"LIMIT_{i}")));
        Write(stem + ".override.darkest", "armour: .name worn_coat .hp 3/*joined*/3junk\n");
        Write("effects/native.effects.darkest", """
            effect: .name FIRST
              .disease priority_top_quirk effect: .name SECOND .disease priority_top_quirk
            effect: .name AFTER_EMPTY .disease priority_top_quirk
            effect: .name AFTER_DOT .disease priority_top_quirk
            effect: .name ".LeadingDot" .disease priority_top_quirk
            effect: .name FALSE_SIGNAL .disease priority_top_quirk
            """ + "\n" + string.Join('\n', Enumerable.Range(0, 17).Select(i =>
                $"effect: .name LIMIT_{i} .disease priority_top_quirk")));
        // Same-path replacement leaves these as the first native records.
        Write("raid/camping/default.camping_skills.json", """
            {"configuration":{"class_specific_number_of_classes_threshold":4},"skills":[
              {"id":"encourage","hero_classes":["local_hero"]},
              {"id":"native_duplicate","hero_classes":["local_hero"]},
              {"id":"native_repeated_classes","hero_classes":["local_hero","local_hero","local_hero","local_hero","local_hero"]}
            ]}
            """);
        Write("raid/camping/z.native.camping_skills.json", """
            {"skills":[
              {"id":"native_default","hero_classes":["local_hero"]},
              {"id":"native_duplicate","hero_classes":["local_hero","base_hero"]}
            ]}
            """);
        WriteFixtureManifest(root);
        var content = original with { Sources = original.Sources.Append(
            new ActiveContentSource("local:hero-native", "Native hero", "local", root, -1800)).ToArray() };
        var catalog = HeroClassCatalog.Load(content);
        var hero = catalog.HeroClasses.Single(row => row.Id == originalHero.Id);
        Assert(hero.Generation?.IsEnabled == true && hero.Generation.RandomCombatSkills == 2 &&
               hero.Generation.CardChance == 0.25 && hero.CanSelectCombatSkills == true && hero.SelectedCombatSkillsMax == 2,
            "Native hero records must span physical lines, accept integer prefixes and StringToBool values, and scale percentages.");
        Assert(!hero.GuaranteedCombatSkillIds.Contains("native_boolean") && hero.GuaranteedCombatSkillIds.Contains("native_yes"),
            "An explicitly empty guarantee must clear true; yes must enable it.");
        Assert(hero.BaseHp == 33 && string.IsNullOrEmpty(hero.ProgressionUnsupportedReason) &&
               hero.LevelProfiles[0].ArmourHp == 33 && hero.LevelProfiles.Max(p => p.WeaponRank) == 4,
            "Equipment slots follow first declaration of exact names, not numeric suffixes; overrides reuse those slots.");
        Assert(hero.RuntimeQuirkSignals.Where(s => s.SkillId == "native_signal").Select(s => s.EffectName).SequenceEqual(["FIRST"]),
            "Native effect lists stop on empty or leading-dot tokens, and slash comments are removed before quoted strings.");
        Assert(hero.RuntimeQuirkSignals.Where(s => s.SkillId == "native_mode").Select(s => s.EffectName)
                   .ToHashSet(StringComparer.Ordinal).SetEquals(["FIRST", "SECOND"]),
            "Only explicitly declared valid_modes supply mode effects; repeats append and empty lists retain earlier effects.");
        Assert(hero.RuntimeQuirkSignals.Any(s => s.SkillId == "native_mode_empty" && s.EffectName == "FIRST") &&
               hero.RuntimeQuirkSignals.Any(s => s.SkillId == "native_mode_dot" && s.EffectName == "FIRST") &&
               !hero.RuntimeQuirkSignals.Any(s => s.SkillId == "native_mode_limit"),
            "Empty/dot mode slots are skipped, not terminal; they still consume the native eight-slot limit.");
        Assert(hero.RuntimeQuirkSignals.Count(s => s.SkillId == "native_limit") == 16 &&
               !hero.RuntimeQuirkSignals.Any(s => s.SkillId == "native_limit" && s.EffectName == "LIMIT_16"),
            "A native skill reads at most sixteen ordinary effects from one declaration.");
        Assert(hero.ClassCampingSkillIds.Contains("encourage") && hero.ClassCampingSkillIds.Contains("native_duplicate") &&
               hero.SharedCampingSkillIds.Contains("native_default") && hero.SharedCampingSkillIds.Contains("native_repeated_classes") &&
               !hero.SharedCampingSkillIds.Contains("native_duplicate"),
            "Camping classification uses raw class count and per-file default zero; same-ID later records cannot OR over the first classification.");
        var baseHero = catalog.HeroClasses.Single(row => row.Id == "base_hero");
        Assert(baseHero.ClassCampingSkillIds.Contains("native_duplicate"),
            "Later camping records may grant access to another hero, which still uses the first record's classification.");

        Write(stem + ".override.darkest", "armour: .name worn_coat .hp 34\0armour: .name worn_coat .hp 999\n");
        Assert(HeroClassCatalog.Load(content).HeroClasses.Single(row => row.Id == originalHero.Id).BaseHp == 34,
            "Text following native LineReader's NUL terminator must not contribute a new armor definition.");

        Write(stem + ".override.darkest", """
            generation: .is_generation_enabled .number_of_random_combat_skills "7"
            skill_selection: .can_select_combat_skills TrUe .number_of_selected_combat_skills_max invalid
            armour: .name worn_coat .hp invalid
            """);
        var invalid = HeroClassCatalog.Load(content).HeroClasses.Single(row => row.Id == originalHero.Id);
        Assert(invalid.Generation?.IsEnabled == false && invalid.Generation.RandomCombatSkills == 0 &&
               invalid.CanSelectCombatSkills == false && invalid.SelectedCombatSkillsMax == 0 && invalid.BaseHp == 0 &&
               !string.IsNullOrEmpty(invalid.ProgressionUnsupportedReason),
            "Present invalid/quoted numeric fields and false boolean tokens overwrite prior values instead of reviving them.");

        var fullInfo = File.ReadAllText(originalHero.SourcePath);
        var namePairs = new[]
        {
            (new string('a', 63) + "0", new string('a', 63) + "1", true),
            (new string('甲', 21) + "0", new string('甲', 21) + "1", true),
            (new string('a', 62) + "甲", new string('a', 62) + "界", true),
            (new string('a', 62) + "丈", new string('a', 62) + "甲", false)
        };
        Write(stem + ".override.darkest", "");
        foreach (var (first, second, sameSlot) in namePairs)
        {
            Write(stem + ".info.darkest", fullInfo.Replace("local_hero_armour_0", first, StringComparison.Ordinal)
                .Replace("local_hero_armour_1", second, StringComparison.Ordinal));
            var bounded = HeroClassCatalog.Load(content).HeroClasses.Single(row => row.Id == originalHero.Id);
            Assert(bounded.BaseHp == (sameSlot ? 24 : 20),
                "Equipment comparison uses the first 63 UTF-8 bytes, including truncated multibyte sequences, not full names or decoded replacement characters.");
            if (sameSlot)
                Assert(bounded.LevelProfiles.All(p => p.ArmourRank <= 3) &&
                       !string.IsNullOrEmpty(bounded.ProgressionUnsupportedReason),
                    "Collapsing native equipment names must never expose a nonexistent fifth armor slot; unmatched upgrade codes remain guarded.");
        }
        Console.WriteLine("PASS: native hero text, equipment slots, mode effects and camping classification.");
    }
}
