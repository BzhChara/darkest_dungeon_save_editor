using DarkestDungeonSaveEditor.Core.Dson;

internal static partial class ContractSuite
{
    private static async Task VerifyDuplicateRecruitQuirksAsync(string root)
    {
        foreach (var kind in new[] { "local", "workshop", "dlc-mod" })
        {
            var f = await BuildTownEventFixtureAsync(Path.Combine(root, kind));
            var game = f.Content.Sources.First(s => s.Kind == "base").Directory;
            var info = Path.Combine(game, "heroes/progression/progression.info.darkest");
            var original = File.ReadAllText(info);
            void Counts(int positive, int negative = 0) => File.WriteAllText(info, original +
                $"\ngeneration: .number_of_positive_quirks_min {positive} .number_of_positive_quirks_max {positive}" +
                $" .number_of_negative_quirks_min {negative} .number_of_negative_quirks_max {negative}\n");
            Counts(1);
            var mod = Path.Combine(root, kind, "quirk-mod");
            Directory.CreateDirectory(mod);
            var content = f.Content with { Sources = f.Content.Sources.Concat(QuerySources(mod, kind)).ToArray() };
            var files = new SelectionFixture(root, mod, kind == "dlc-mod" ? "dlc/rq_feature/" : "", true, content);
            File.WriteAllText(Path.Combine(mod, "modfiles.txt"), "");
            var path = SelectionResource(files, "shared/quirk/event.quirk_library.json", "{\"quirks\":[]}");
            SelectionResource(files, "shared/buffs/duplicate.buffs.json", """
                {"buffs":[
                  {"id":"hp7","stat_type":"combat_stat_add","stat_sub_type":"max_hp","amount":7,
                   "rule_type":"always","is_false_rule":false,"rule_data":{"float":0,"string":""}},
                  {"id":"hp11","stat_type":"combat_stat_add","stat_sub_type":"max_hp","amount":11,
                   "rule_type":"always","is_false_rule":false,"rule_data":{"float":0,"string":""}}]}
                """);
            JsonObject Definition(string id, double weight, int duration, string buff, bool positive = true)
            {
                var q = DuplicateQuirk(id, weight, positive: positive);
                q["buffs"] = new JsonArray(buff);
                q["evolution_duration_min"] = duration;
                q["evolution_duration_max"] = duration;
                q["evolution_duration_type"] = duration == 3 ? "quest_end" : "combat_end";
                q["evolution_causes_death"] = true;
                return q;
            }
            void Library(params JsonObject[] entries) => File.WriteAllText(path, QuirkLibrary(entries));
            (JsonObject Hero, Dictionary<string, JsonObject> Saves) Generate(string type = "specific_recruit", int seed = 7,
                string text = "progression", Dictionary<string, JsonObject>? input = null)
            {
                File.WriteAllText(f.EventsPath, new JsonObject { ["events"] = new JsonArray(new JsonObject
                {
                    ["id"] = "duplicate_probe", ["data"] = new JsonArray(new JsonObject
                    { ["type"] = type, ["string_data"] = text, ["number_data"] = type == "stage_coach_bonus_recruits" ? 1 : 0 })
                }) }.ToJsonString());
                var saves = (input ?? f.Saves).ToDictionary(p => p.Key, p => p.Value.DeepClone().AsObject());
                TownEventSaveEditor.ReplaceCurrent(saves, TownEventCatalog.Load(content).Events.Single(),
                    new TownEventInitializer(content, seed).Initialize);
                var hero = type == "stage_coach_bonus_recruits"
                    ? saves["persist.town.json"]["base_root"]!["buildings"]!["stage_coach"]!["store"]!["hero_recruit"]!["generated"]!.AsObject()
                        .Single(p => p.Key != "50").Value!.AsObject()
                    : saves["persist.town_event.json"]["base_root"]!["bonus_hero_entries"]!.AsObject().Single().Value!.AsObject();
                return (hero, saves);
            }
            void Instance(JsonObject hero, string id, int hp, int duration)
            {
                Assert(hero["quirks"]!.AsObject().ContainsKey(id) && hero["actor"]!["current_hp"]!.GetValue<double>() == hp &&
                    hero["quirks"]![id]!["evolution_duration_remaining"]!.GetValue<int>() == duration,
                    kind + ": recruit persistence must use the randomly selected definition's name, HP and evolution duration");
            }
            foreach (var (first, last, expected) in new[] { (1, 0, 1), (0, 1, 1), (0, 0, 0) })
            {
                Library(DuplicateQuirk("probe", first), DuplicateQuirk("probe", last));
                Assert(Generate().Hero["quirks"]!.AsObject().Count == expected, "Positive earlier duplicates must remain in the native random pool");
            }
            // Same hash, different spelling/classification/data. Generation
            // must keep the first instance; manual ID selection keeps the last.
            Library(Definition("aZ", 1, 3, "hp7"), Definition("b%", 0, 9, "hp11", positive: false));
            var catalog = HeroClassCatalog.Load(content);
            Assert(catalog.InitialQuirks.Single().Id == "b%" && catalog.InitialQuirks.Single().IsPositive == false &&
                catalog.RandomInitialQuirks.Count == 2 && catalog.RandomInitialQuirks[0].Evolution!.DurationType == HeroQuirkEvolutionDurationType.QuestEnd,
                "The selectable ID catalog and the complete random-definition sequence have different consumers");
            var manual = StagecoachHeroCandidateFactory.Generate(catalog, catalog.HeroClasses.Single(), 7, 0, ["b%"]);
            Instance(manual.Candidate, "b%", 31, 9);
            Assert(manual.Preview.NegativeQuirks.Single() == "b%", "Manual selection retains the last definition's classification");
            var generated = Generate();
            Instance(generated.Hero, "aZ", 27, 3);
            var restored = await RoundtripDirectoryQueryAsync(root, kind + "-duplicate-event", generated.Saves["persist.town_event.json"], DsonSaveCodec.CreateNative());
            Assert(JsonNode.DeepEquals(restored, generated.Saves["persist.town_event.json"]), "Event random-instance fields survive native DSON round-trip");
            Instance(Generate("quirked_recruit", text: "b%").Hero, "aZ", 27, 3);
            Counts(0);
            Instance(Generate("quirked_recruit", text: "aZ").Hero, "b%", 31, 9);
            Counts(1);
            var ordinary = Generate("stage_coach_bonus_recruits");
            Instance(ordinary.Hero, "aZ", 47, 3);
            Assert(!ordinary.Hero["is_from_town_event"]!.GetValue<bool>() &&
                ordinary.Saves["persist.town.json"]["base_root"]!["buildings"]!["stage_coach"]!["store"]!["hero_recruit"]!["generated"]!["50"]!["marker"]!.GetValue<string>() == "old coach",
                "Ordinary bonus recruits keep their pool and do not overwrite old offers");

            // Each duplicate has its own random weight and payload. Exercise
            // enough deterministic seeds to observe both objects and a third ID.
            Library(Definition("aZ", 2, 3, "hp7"), Definition("b%", 5, 9, "hp11"), DuplicateQuirk("other", 3));
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var seed = 0; seed < 32; seed++)
            {
                var hero = Generate(seed: seed).Hero;
                var id = hero["quirks"]!.AsObject().Single().Key;
                seen.Add(id);
                if (id != "other") Instance(hero, id, id == "aZ" ? 27 : 31, id == "aZ" ? 3 : 9);
            }
            Assert(seen.SetEquals(["aZ", "b%", "other"]), "Every positive-weight definition must be drawable independently");
            Counts(2);
            var two = Generate().Hero["quirks"]!.AsObject();
            Assert(two.Count == 2 && two.ContainsKey("other") && two.Count(pair => pair.Key is "aZ" or "b%") == 1,
                "A hero cannot receive both hash aliases, even though both definitions contribute random weight");

            Counts(1, 1);
            var incompatible = Definition("aZ", 1, 3, "hp7");
            incompatible["incompatible_quirks"] = new JsonArray("negative");
            Library(incompatible, DuplicateQuirk("b%", 0), DuplicateQuirk("negative", 1, positive: false));
            Assert(Generate().Hero["quirks"]!.AsObject().Count == 1, "Subsequent draws respect the chosen instance's exclusions");
            Counts(1);
            var singleton = DuplicateQuirk("aZ", 1);
            singleton["tags"] = new JsonArray("singleton");
            Library(singleton, DuplicateQuirk("b%", 0));
            var occupied = f.Saves.ToDictionary(p => p.Key, p => p.Value.DeepClone().AsObject());
            occupied["persist.town.json"]["base_root"]!["buildings"]!["stage_coach"]!["store"]!["hero_recruit"]!["generated"]!["50"]!["quirks"]!["b%"] = new JsonObject();
            Assert(Generate(input: occupied).Hero["quirks"]!.AsObject().Count == 0, "An earlier singleton's tag and saved hash aliases constrain its own draw");

            foreach (var rawId in new[] { "\0hidden", " \0hidden" })
            {
                Library(DuplicateQuirk(rawId, 1));
                foreach (var type in new[] { "specific_recruit", "stage_coach_bonus_recruits" })
                {
                    var rejected = false;
                    try { Generate(type); }
                    catch (InvalidOperationException error)
                    { rejected = error.Message == EditorText.Get("StagecoachHeroCandidateFactory_Quirks_001"); }
                    Assert(rejected, "Both random recruit paths must retain the manual generator's empty/whitespace C-string ID guard");
                }
            }

            // Last-ID certainty can recover after an unknown slot; the full
            // random pool cannot recover until that slot is repaired.
            Library(DuplicateQuirk("probe", 1));
            var missing = SelectionResource(files, "shared/quirk/missing.quirk_library.json", "{");
            SelectionResource(files, "shared/quirk/z.quirk_library.json", QuirkLibrary(DuplicateQuirk("probe", 0)));
            catalog = HeroClassCatalog.Load(content);
            Assert(catalog.InitialQuirks.Single().WriteStatus == HeroInitialQuirkWriteStatus.Direct && !catalog.RandomInitialQuirksComplete,
                "A later ID definition restores explicit selection but cannot invent unknown random entries");
            var failed = false;
            try { Generate(); } catch (InvalidDataException error) { failed = error.Message.Contains("Random recruit quirk", StringComparison.Ordinal); }
            Assert(failed, "Recruit generation must not silently draw from a partial random pool");
            File.WriteAllText(missing, "{\"quirks\":[]}");
            Assert(Generate().Hero["quirks"]!.AsObject().Count == 1, "Repair restores the earlier random definition despite a later zero-weight ID match");
            Console.WriteLine($"PASS: {kind} duplicate recruit weights, owned definition data, forced/manual lookup, singleton/exclusion guards and DSON persistence.");
        }
    }
}
