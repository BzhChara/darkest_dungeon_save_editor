internal static partial class ContractSuite
{
    private static string QuirkEvolutionLibrary(string fields) =>
        "{\"quirks\":[{\"id\":\"p0\",\"is_positive\":true" + (fields.Length == 0 ? "" : "," + fields) +
        "},{\"id\":\"p1\",\"is_positive\":true}]}";

    private static async Task VerifyHeroQuirkEvolutionDefaultsAsync(string runRoot, DsonSaveCodec codec)
    {
        foreach (var kind in QuirkRuleSourceKinds)
        {
            var root = Path.Combine(runRoot, "quirk-evolution-defaults", kind);
            var f = QuirkRuleContent(root, kind);
            foreach (var fields in new[]
            {
                "", "\"evolution_note\":{\"text\":\"not a mechanic\"}", "\"EVOLUTION_duration_max\":100",
                "\"evolution_causes_death\":false", "\"evolution_class_id\":\"\"",
                "\"evolution_class_id\":\"\\u0000unused\"",
                "\"evolution_causes_death\":false,\"evolution_causes_death\":true",
                "\"evolution_duration_min\":0,\"evolution_duration_max\":0,\"evolution_class_id\":\"\",\"evolution_causes_death\":false",
                "\"evolution_town_progression_duration_change\":0,\"evolution_town_attempt_use_item_duration_threshold\":0"
            })
            {
                File.WriteAllText(f.Quirks, QuirkEvolutionLibrary(fields));
                var catalog = HeroClassCatalog.Load(f.Content);
                var quirk = catalog.InitialQuirks.Single(q => q.Id == "p0");
                Assert(quirk is { HasEvolution: false, WriteStatus: HeroInitialQuirkWriteStatus.Direct },
                    $"{kind}: defaults and unknown fields must not create evolution: {fields}");
                var generated = GenerateQuirkRuleHero(catalog, "p0");
                Assert(generated.Candidate["quirks"]!["p0"]!["evolution_duration_remaining"]!.GetValue<int>() == 0,
                    "Inactive evolution has no manufactured countdown.");
            }

            const string death = "\"evolution_duration_min\":12,\"evolution_duration_max\":12,\"evolution_causes_death\":true";
            HeroQuirkEvolutionDefinition? ordinaryDeath = null;
            foreach (var extra in new[] { "", ",\"evolution_class_id\":\"\"", ",\"evolution_class_id\":\"\",\"evolution_class_id\":\"missing\"" })
            {
                File.WriteAllText(f.Quirks, QuirkEvolutionLibrary(death + extra));
                var catalog = HeroClassCatalog.Load(f.Content);
                var quirk = catalog.InitialQuirks.Single(q => q.Id == "p0");
                Assert(quirk.Evolution is { DurationMin:12, DurationMax:12, TargetQuirkId:null, CausesDeath:true } &&
                       quirk.WriteStatus == HeroInitialQuirkWriteStatus.Direct, "Empty death targets equal the omitted target.");
                ordinaryDeath ??= quirk.Evolution;
                Assert(quirk.Evolution == ordinaryDeath, "Equivalent native death fields yield identical public evolution data.");
                var generated = GenerateQuirkRuleHero(catalog, "p0");
                Assert(generated.Candidate["quirks"]!["p0"]!["evolution_duration_remaining"]!.GetValue<int>() == 12,
                    "The original countdown is retained with an explicit empty target.");
                if (extra == ",\"evolution_class_id\":\"\"") await VerifyProgressionCandidatePersistenceAsync(root, generated, codec);
            }

            File.WriteAllText(f.Quirks, QuirkEvolutionLibrary("\"evolution_duration_max\":12,\"evolution_class_id\":\"p1\""));
            var minimumDefault = HeroClassCatalog.Load(f.Content);
            Assert(minimumDefault.InitialQuirks.Single(q => q.Id == "p0").Evolution is { DurationMin:0, DurationMax:12 },
                "An omitted minimum uses the native zero default.");
            for (var seed = 0; seed < 12; seed++)
            {
                var generated = StagecoachHeroCandidateFactory.Generate(minimumDefault, minimumDefault.HeroClasses.Single(), seed, 0, ["p0"]);
                Assert(generated.Candidate["quirks"]!["p0"]!["evolution_duration_remaining"]!.GetValue<int>() is >= 0 and <= 12,
                    "Generated countdowns stay inside the authored/defaulted range.");
            }
            File.WriteAllText(f.Quirks, QuirkEvolutionLibrary("\"evolution_duration_min\":6,\"evolution_duration_max\":6,\"evolution_class_id\":\"p0\""));
            GenerateQuirkRuleHero(HeroClassCatalog.Load(f.Content), "p0");

            foreach (var fields in new[]
            {
                "\"evolution_duration_max\":12,\"evolution_class_id\":\"missing\"",
                "\"evolution_duration_min\":3,\"evolution_class_id\":\"p1\"",
                "\"evolution_duration_min\":12,\"evolution_duration_max\":3,\"evolution_causes_death\":true",
                "\"evolution_duration_min\":3.5,\"evolution_duration_max\":12,\"evolution_causes_death\":true",
                "\"evolution_duration_min\":3.0,\"evolution_duration_max\":12,\"evolution_causes_death\":true",
                "\"evolution_duration_max\":-1,\"evolution_causes_death\":true",
                "\"evolution_duration_max\":12,\"evolution_causes_death\":\"true\"",
                "\"evolution_duration_max\":12",
                "\"evolution_class_id\":\" \"",
                "\"evolution_class_id\":null",
                "\"evolution_duration_max\":\"12\",\"evolution_duration_max\":12,\"evolution_causes_death\":true"
            })
            {
                File.WriteAllText(f.Quirks, QuirkEvolutionLibrary(fields));
                var catalog = HeroClassCatalog.Load(f.Content);
                Assert(catalog.InitialQuirks.Single(q => q.Id == "p0").WriteStatus == HeroInitialQuirkWriteStatus.Unverified,
                    $"{kind}: unsafe evolution is still unavailable: {fields}");
                AssertQuirkRuleRejected(catalog, ["p0"], "当前不能显式写入");
            }

            // Acquisition slot_size must not replace the separate load-time record limit.
            var slotQuirks = new JsonArray();
            for (var i = 0; i < 6; i++) slotQuirks.Add(new JsonObject { ["id"] = "p" + i, ["is_positive"] = true, ["slot_size"] = 0 });
            File.WriteAllText(f.Quirks, new JsonObject { ["quirks"] = slotQuirks }.ToJsonString());
            AssertQuirkRuleRejected(HeroClassCatalog.Load(f.Content), Enumerable.Range(0,6).Select(i => "p"+i).ToArray(), "最多");
            for (var i = 0; i < 3; i++) slotQuirks[i]!["slot_size"] = 2;
            File.WriteAllText(f.Quirks, new JsonObject { ["quirks"] = slotQuirks.DeepClone() }.ToJsonString());
            GenerateQuirkRuleHero(HeroClassCatalog.Load(f.Content), "p0", "p1", "p2");
        }
        Console.WriteLine("PASS: quirk evolution defaults, exact members, empty death targets, countdown persistence and retained guards.");
    }
}
