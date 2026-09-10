internal static partial class ContractSuite
{
    private static async Task VerifyHeroExactIdentitiesAsync(ActiveContentSnapshot original,
        HeroClassDefinition originalHero, string runRoot, DsonSaveCodec codec)
    {
        var root = Path.Combine(runRoot, "hero-exact-identities");
        void Write(string path, string text) => WriteMultiMash(root, path, text);
        Write("shared/buffs/exact.buffs.json", """
            {"buffs":[
              {"id":"NI_HP","stat_type":"combat_stat_multiply","stat_sub_type":"max_hp","amount":0.1,"rule_type":"always","is_false_rule":false},
              {"id":" NI_HP ","stat_type":"combat_stat_multiply","stat_sub_type":"max_hp","amount":0.5,"rule_type":"always","is_false_rule":false},
              {"id":"ni_hp","stat_type":"combat_stat_multiply","stat_sub_type":"max_hp","amount":0.2,"rule_type":"always","is_false_rule":false},
              {"id":"NI_COLLIDE_Az","stat_type":"combat_stat_add","stat_sub_type":"speed_rating","amount":1,"rule_type":"always","is_false_rule":false},
              {"id":"NI_COLLIDE_BE","stat_type":"combat_stat_multiply","stat_sub_type":"max_hp","amount":0.5,"rule_type":"always","is_false_rule":false}
            ]}
            """);
        Write("shared/quirk/exact.quirk_library.json", """
            {"quirks":[
              {"id":"ni_select","is_positive":true},
              {"id":" ni_select ","is_positive":true,"buffs":[" NI_HP "]},
              {"id":"NI_SELECT","is_positive":true,"buffs":["NI_HP"]},
              {"id":"ni_both_buffs","is_positive":true,"buffs":["NI_HP","ni_hp"]},
              {"id":"ni_missing_buff","is_positive":true,"buffs":["Ni_HP"]},
              {"id":"ni_blank_buff","is_positive":true,"buffs":[" "]},
              {"id":"ni_collision","is_positive":true,"buffs":["NI_COLLIDE_Az"]},
              {"id":"ni_excludes","is_positive":false,"incompatible_quirks":[" ni_select ","NI_SELECT"]},
              {"id":"ni_limited","is_positive":true,"tags":["singleton"]},
              {"id":"NI_LIMITED","is_positive":true,"tags":["singleton"]},
              {"id":"ni_evolving","is_positive":false,"evolution_duration_min":1,"evolution_duration_max":1,"evolution_class_id":" ni_select "},
              {"id":"ni_chain","is_positive":false,"evolution_duration_min":1,"evolution_duration_max":1,"evolution_class_id":"NI_CHAIN"},
              {"id":"NI_CHAIN","is_positive":false,"evolution_duration_min":1,"evolution_duration_max":1,"evolution_class_id":"ni_no_target"},
              {"id":"SHARD_HUNGRY","is_positive":true},
              {"id":"shard_hungry","is_positive":true}
            ]}
            """);
        WriteFixtureManifest(root);
        var content = original with { Sources = original.Sources.Append(
            new ActiveContentSource("local:hero-exact", "Exact hero IDs", "local", root, -1850)).ToArray() };
        var catalog = HeroClassCatalog.Load(content);
        var hero = catalog.HeroClasses.Single(row => row.Id == originalHero.Id);
        GeneratedStagecoachHeroCandidate Generate(params string[] ids) => StagecoachHeroCandidateFactory.Generate(catalog, hero, 1729, ids);
        var baseline = Generate().Preview.CurrentHp;
        Assert(baseline == 20, "The independent HP fixture must retain its known base HP.");
        foreach (var (id, expected) in new[] { ("ni_select", 20d), (" ni_select ", 30d), ("NI_SELECT", 22d), ("ni_both_buffs", 26d) })
        {
            var generated = Generate(id);
            var serialized = JsonNode.Parse(generated.Candidate.ToJsonString())!.AsObject();
            Assert(generated.Preview.CurrentHp == expected && serialized["actor"]!["current_hp"]!.GetValue<double>() == expected &&
                   serialized["quirks"]!.AsObject().Select(pair => pair.Key).SequenceEqual([id]),
                "Raw Buff definitions, references and selected quirk IDs must reach both preview and actual candidate JSON without aliasing.");
        }
        Assert(Generate("ni_select", "NI_SELECT").Candidate["quirks"]!.AsObject().Count == 2,
            "Case-distinct quirks must remain separately selectable.");
        void Reject(string[] ids)
        {
            var rejected = false;
            try { Generate(ids); } catch (InvalidOperationException) { rejected = true; }
            Assert(rejected, "Invalid exact identities, duplicate choices and exact incompatibilities must remain rejected.");
        }
        Reject(["Ni_Select"]);
        Reject(["ni_select", "ni_select"]);
        Reject(["ni_excludes", " ni_select "]);
        Reject(["ni_excludes", "NI_SELECT"]);
        Reject(["ni_missing_buff"]);
        Reject(["ni_collision"]);
        Reject(["ni_blank_buff"]);
        Generate("ni_excludes", "ni_select");
        Assert(catalog.InitialQuirks.Single(q => q.Id == "ni_evolving").Evolution?.TargetQuirkId == " ni_select " &&
               catalog.InitialQuirks.Single(q => q.Id == "ni_evolving").WriteStatus == HeroInitialQuirkWriteStatus.Direct &&
               catalog.InitialQuirks.Single(q => q.Id == "ni_chain").WriteStatus == HeroInitialQuirkWriteStatus.Unverified,
            "Evolution targets must retain spaces, and case-distinct chain steps must not become a false cycle hiding a missing target.");

        var town = JsonNode.Parse("""
            {"base_root":{"buildings":{"stage_coach":{"store":{
              "hero_recruit":{"generated":{"3":{"quirks":{"NI_LIMITED":{}}}}},
              "shard_hero_recruit":{"generated":{"4":{"quirks":{"ni_limited":{}}}}}
            }}}}}
            """)!.AsObject();
        var roster = JsonNode.Parse("""
            {"base_root":{"nextGuid":10,"heroes":{
              "1":{"quirks":{"ni_limited":{}}},"2":{"quirks":{"NI_LIMITED":{}}}
            }}}
            """)!.AsObject();
        var limited = Generate("ni_limited", "NI_LIMITED");
        var limits = StagecoachHeroSaveEditor.AnalyzeQuirkLimits(town, roster, limited.Candidate, catalog.InitialQuirks);
        Assert(limits.Count == 2 && limits.All(row => row.ExistingRosterHeroes == 1 && row.ExistingStagecoachCandidates == 1),
            "Exact singleton identities must remain separate in roster and every stagecoach pool.");
        var upgrades = JsonNode.Parse("""{"base_root":{"purchases":{}}}""")!.AsObject();
        foreach (var (id, pool) in new[] { ("SHARD_HUNGRY", StagecoachRecruitPool.Ordinary), ("shard_hungry", StagecoachRecruitPool.Shard) })
        {
            var generated = Generate(id);
            Assert(StagecoachHeroSaveEditor.AddCandidate(town, roster, upgrades, generated.Candidate, generated.UpgradePurchases)
                .Preview.TargetPool == pool, "Only the exact shard_hungry identity may route a candidate into the shard pool.");
        }
        var selected = Generate(" ni_select ", "NI_SELECT");
        var added = StagecoachHeroSaveEditor.AddCandidate(town, roster, upgrades, selected.Candidate, selected.UpgradePurchases);
        var decoded = Path.Combine(root, "town.decoded.json");
        var binary = Path.Combine(root, "persist.town.json");
        var roundtrip = Path.Combine(root, "town.roundtrip.json");
        File.WriteAllText(decoded, added.UpdatedTown.ToJsonString());
        await codec.EncodeAsync(decoded, binary, originalBinaryPath: null);
        await codec.DecodeAsync(binary, roundtrip);
        var restored = JsonNode.Parse(File.ReadAllText(roundtrip))!["base_root"]!["buildings"]!["stage_coach"]!["store"]!["hero_recruit"]!["generated"]!["10"]!;
        Assert(restored["quirks"]!.AsObject().Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal).SetEquals([" ni_select ", "NI_SELECT"]) &&
               restored["actor"]!["current_hp"]!.GetValue<double>() == 32,
            "Exact selected IDs and HP must survive actual stagecoach insertion and DSON encoding/decoding.");
        Console.WriteLine("PASS: exact Buff/quirk identities, HP, exclusions, evolution, limits, pool routing and DSON stagecoach write.");
    }
}
