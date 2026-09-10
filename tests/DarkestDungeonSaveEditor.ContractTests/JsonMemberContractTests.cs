internal static partial class ContractSuite
{
    private static async Task VerifyJsonMembersAndBuffReferencesAsync(ActiveContentSnapshot original,
        HeroClassDefinition originalHero, string runRoot, DsonSaveCodec codec)
    {
        var root = Path.Combine(runRoot, "json-members-buff-references");
        void Write(string path, string text) => WriteMultiMash(root, path, text);
        Write($"heroes/{originalHero.Id}/{originalHero.Id}.info.darkest", File.ReadAllText(originalHero.SourcePath));
        var buffCases = new[]
        {
            ("jm_plus", "\"amount\":0.25"), ("jm_minus", "\"amount\":-0.75"),
            ("jm_first_bad", "\"amount\":-1,\"amount\":0.25"),
            ("jm_first_good", "\"amount\":0.25,\"amount\":-1"),
            ("jm_first_null", "\"amount\":null,\"amount\":0.25")
        };
        var buffs = string.Join(',', buffCases.Select(entry =>
            $$$"""{"id":"{{{entry.Item1}}}","stat_type":"combat_stat_multiply","stat_sub_type":"max_hp",{{{entry.Item2}}},"rule_type":"always","is_false_rule":false,"rule_data":{"float":0,"string":""}}"""));
        Write("shared/buffs/jm.buffs.json", "{\"buffs\":[" + buffs + "],\"buffs\":[]}");
        Write("shared/quirk/jm.quirk_library.json", """
            {"quirks":[
              {"id":"jm_once","is_positive":true,"buffs":["jm_plus"]},
              {"id":"jm_twice","is_positive":true,"buffs":["jm_plus","jm_plus"]},
              {"id":"jm_negative_twice","is_positive":true,"buffs":["jm_minus","jm_minus"]},
              {"id":"jm_first_bad","is_positive":true,"buffs":["jm_first_bad"]},
              {"id":"jm_first_good","is_positive":true,"buffs":["jm_first_good"]},
              {"id":"jm_first_null","is_positive":true,"buffs":["jm_first_null"]},
              {"id":"jm_fields","id":"jm_ignored","is_positive":true,"is_positive":false,
               "random_chance":100,"random_chance":0,"buffs":["jm_plus"],"buffs":["jm_minus"],
               "incompatible_quirks":["jm_once","jm_once"]},
              {"id":"jm_evolution","is_positive":false,"buffs":[],
               "evolution_duration_min":3,"evolution_duration_min":100,"evolution_duration_max":3,
               "evolution_causes_death":true,"evolution_causes_death":false}
            ],"quirks":[{"id":"jm_ignored_root","is_positive":true,"buffs":[]}]}
            """);
        WriteFixtureManifest(root);
        var content = original with { Sources = original.Sources.Append(
            new ActiveContentSource("local:json-members", "JSON members", "local", root, -2500)).ToArray() };
        var catalog = HeroClassCatalog.Load(content);
        var hero = catalog.HeroClasses.Single(h => h.Id == originalHero.Id);
        HeroInitialQuirkDefinition Quirk(string id) => catalog.InitialQuirks.Single(q => q.Id == id);
        GeneratedStagecoachHeroCandidate Generate(string id) => StagecoachHeroCandidateFactory.Generate(catalog, hero, 1729, [id]);
        Assert(Quirk("jm_once").MaxHpModifiers.Count == 1 && Generate("jm_once").Preview.CurrentHp == 25 &&
               Quirk("jm_twice").MaxHpModifiers.Count == 2 && Generate("jm_twice").Preview.CurrentHp == 30,
            "Every occurrence in a Buff reference array must contribute to HP, while each reference still uses its effective definition.");
        foreach (var id in new[] { "jm_negative_twice", "jm_first_bad", "jm_first_null" })
        {
            var rejected = false;
            try { Generate(id); } catch (InvalidOperationException) { rejected = true; }
            Assert(rejected, $"{id}: repeated harmful references or an unsafe/wrong-typed FIRST JSON member must block generation.");
        }
        Assert(Generate("jm_first_good").Preview.CurrentHp == 25 && Generate("jm_fields").Preview.CurrentHp == 25 &&
               Quirk("jm_fields").IsPositive == true && Quirk("jm_fields").RandomChance == 100 &&
               Quirk("jm_fields").IncompatibleQuirkIds.SequenceEqual(["jm_once"]) &&
               Quirk("jm_evolution").Evolution is { DurationMin: 3, DurationMax: 3, CausesDeath: true } &&
               catalog.InitialQuirks.All(q => q.Id is not ("jm_ignored" or "jm_ignored_root")),
            "First JSON fields must govern IDs, root/reference arrays, numbers and flags; exclusion lists remain sets.");

        var town = JsonNode.Parse("""{"base_root":{"buildings":{"stage_coach":{"store":{"hero_recruit":{"generated":{}}}}}}}""")!.AsObject();
        var roster = JsonNode.Parse("""{"base_root":{"nextGuid":900,"heroes":{}}}""")!.AsObject();
        var upgrades = JsonNode.Parse("""{"base_root":{"purchases":{}}}""")!.AsObject();
        var candidate = Generate("jm_twice");
        var mutation = StagecoachHeroSaveEditor.AddCandidate(town, roster, upgrades, candidate.Candidate, candidate.UpgradePurchases);
        var decoded = Path.Combine(root, "town.decoded.json"); var binary = Path.Combine(root, "town.dson");
        var restored = Path.Combine(root, "town.roundtrip.json");
        File.WriteAllText(decoded, mutation.UpdatedTown.ToJsonString());
        await codec.EncodeAsync(decoded, binary, null); await codec.DecodeAsync(binary, restored);
        var restoredTown = JsonNode.Parse(File.ReadAllText(restored))!.AsObject();
        Assert(SaveEditService.HasEquivalentStagecoachTownRoundTrip(mutation.UpdatedTown, restoredTown, mutation.Preview) &&
               restoredTown["base_root"]!["buildings"]!["stage_coach"]!["store"]!["hero_recruit"]!["generated"]!["900"]!
                   ["actor"]!["current_hp"]!.GetValue<double>() == 30,
            "The repeated Buff's HP must reach the actual town DSON, not merely the catalog or candidate preview.");

        // Exercise direct property reads outside the Buff helper as well.
        Write("upgrades/heroes/jm.upgrades.json", $$"""
            {"trees":[{"id":"{{originalHero.Id}}.weapon","id":"ignored.weapon",
              "requirements":[{"code":"0","code":"x","prerequisite_resolve_level":1,"prerequisite_resolve_level":6}],
              "requirements":[]}],"trees":[]}
            """);
        var events = new JsonArray();
        foreach (var (id, type, target, expected) in new[]
        {
            ("jm_recruit", "bonus_recruit", originalHero.Id, true),
            ("jm_upper_type", "BONUS_RECRUIT", originalHero.Id, false),
            ("jm_space_type", " bonus_recruit ", originalHero.Id, false),
            ("jm_space_class", "bonus_recruit", " " + originalHero.Id + " ", false),
            ("jm_upper_class", "bonus_recruit", originalHero.Id.ToUpperInvariant(), false),
            ("jm_nul", "bonus_recruit\0ignored", originalHero.Id + "\0ignored", true)
        })
        {
            events.Add(new JsonObject { ["id"] = id, ["data"] = new JsonArray(new JsonObject
            { ["type"] = type, ["string_data"] = target, ["number_data"] = 2 }) });
        }
        events.Add(new JsonObject { ["id"] = "jm_blocked\0ignored", ["data"] = new JsonArray() });
        events.Add(new JsonObject { ["id"] = "jm_blocked", ["data"] = new JsonArray(new JsonObject
        { ["type"] = "bonus_recruit", ["string_data"] = originalHero.Id, ["number_data"] = 2 }) });
        var eventText = new JsonObject { ["events"] = events }.ToJsonString();
        Write("campaign/town_events/jm.town_events.events.json", eventText.Replace("\"number_data\":2", "\"number_data\":2,\"number_data\":9"));
        WriteFixtureManifest(root);
        var updated = HeroClassCatalog.Load(content);
        var updatedHero = updated.HeroClasses.Single(h => h.Id == hero.Id);
        Assert(updatedHero.UpgradeTrees.Single(t => t.Id == hero.Id + ".weapon").Requirements.SequenceEqual([new HeroUpgradeRequirementDefinition("0", 1)]),
            "Upgrade root arrays, tree IDs and requirement fields must use first JSON members without changing last-tree resolution.");
        Assert(updatedHero.RecruitEvents.Where(e => e.Id.StartsWith("jm_", StringComparison.Ordinal)).Select(e => e.Id).Order()
                   .SequenceEqual(new[] { "jm_nul", "jm_recruit" }) &&
               updatedHero.RecruitEvents.Where(e => e.Id.StartsWith("jm_", StringComparison.Ordinal)).All(e => e.Count == 2),
            "Recruit payloads use raw case-sensitive hashes, stop at NUL, and take the first numeric field, including class binding after parsing.");
        Write("shared/buffs/jm.buffs.json", "{\"buffs\":null,\"buffs\":[" + buffs + "]}");
        Assert(HeroClassCatalog.Load(content).InitialQuirks.Single(q => q.Id == "jm_twice").WriteStatus == HeroInitialQuirkWriteStatus.Unverified,
            "A wrong-typed first resource array must not fall through to a later valid array.");
        Console.WriteLine("PASS: repeated Buff references, first JSON members across hero resources, actual HP DSON and raw recruit payloads.");
    }
}
