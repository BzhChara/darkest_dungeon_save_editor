using DarkestDungeonSaveEditor.Core.Dson;

internal static partial class ContractSuite
{
    // Opt-in integration check: the installation is read-only. All save files
    // are synthetic and live beneath the test artifact directory.
    public static async Task RunTownEventsNativeAsync(string repositoryRoot, string gameDirectory)
    {
        var root = Path.Combine(repositoryRoot, "artifacts", "town-event-native", Guid.NewGuid().ToString("N"));
        var fixture = await BuildTownEventFixtureAsync(root);
        var codec = DsonSaveCodec.CreateNative();
        var game = fixture.Saves["persist.game.json"];
        var dlc = new JsonObject();
        foreach (var name in new[] { "crimson_court", "districts", "flagellant", "musketeer", "shieldbreaker", "color_of_madness" })
            dlc[dlc.Count.ToString()] = new JsonObject { ["name"] = name };
        game["base_root"]!["dlc"] = dlc;
        await codec.EncodeAsync(WriteMultiMash(root, "native-game.json", game.ToJsonString()),
            Path.Combine(fixture.Content.Profile.ProfileDirectory, "persist.game.json"), null);
        var content = await ActiveContentResolver.ResolveAsync(fixture.Content.Profile, gameDirectory, null, codec, Path.Combine(root, "native-resolve"));
        var events = TownEventCatalog.Load(content);
        var heroes = HeroClassCatalog.Load(content);
        var crusader = heroes.HeroClasses.Single(hero => hero.Id == "crusader");
        var candidate = StagecoachHeroCandidateFactory.Generate(heroes, crusader, 42, 6, []).Candidate;
        fixture.Saves["persist.roster.json"]["base_root"]!["highest_resolve_xp"] = candidate["resolveXp"]!.DeepClone();
        var roster = fixture.Saves["persist.roster.json"]["base_root"]!["heroes"]!.AsObject();
        roster.Clear();
        var curseIds = new[] { "disease_vampire_passive", "disease_vampire_crave", "disease_vampire_wasting", "disease_vampire_blood_lust" };
        for (var index = 1; index <= 4; index++)
        {
            var hero = candidate.DeepClone().AsObject();
            hero["roster.status"] = index == 1 ? 0 : 3;
            hero["actor"]!["current_hp"] = TownEventSaveEditor.Float(index == 1 ? 61 : 0);
            hero["quirks"]![curseIds[index - 1]] = new JsonObject { ["is_locked"] = false };
            if (index == 2)
                hero["quirks"]!["twilight_dreamer"] = new JsonObject { ["is_locked"] = false };
            roster[index.ToString()] = new JsonObject { ["hero_file_data"] = new JsonObject { ["raw_data"] = new JsonObject { ["base_root"] = hero } } };
        }
        fixture.Saves["persist.town.json"]["base_root"]!["buildings"]!["stage_coach"]!["store"]!["hero_recruit"]!["generated"] = new JsonObject();
        var estate = fixture.Saves["persist.estate.json"]["base_root"]!;
        estate["trinkets"]!["items"]!["0"]!["id"] = "accuracy_stone";
        estate["trinkets"]!["items"]!["1"]!["id"] = "speed_stone";
        fixture.Saves["persist.quest.json"]["base_root"]!["quests"]!["0"]!["completion_reward"]!["items_definition"]!["items"]!["0"]!["id"] = "speed_stone";
        var failures = new List<string>();
        var curseRemovalCases = 0;
        var revivalCases = 0;
        foreach (var selected in events.Events)
        {
            try
            {
                var saves = fixture.Saves.ToDictionary(pair => pair.Key, pair => pair.Value.DeepClone().AsObject());
                TownEventSaveEditor.ReplaceCurrent(saves, selected, new TownEventInitializer(content, 7).Initialize);
                var updatedRoster = saves["persist.roster.json"]["base_root"]!["heroes"]!;
                JsonObject HeldQuirks(string id) => updatedRoster[id]!["hero_file_data"]!["raw_data"]!["base_root"]!["quirks"]!.AsObject();
                if (selected.Effects.Any(effect => effect.Type == "remove_vampire"))
                {
                    Assert(Enumerable.Range(1, 4).All(id => !HeldQuirks(id.ToString()).Any(quirk => curseIds.Contains(quirk.Key))),
                        $"Native event {selected.Id}: all four real Crimson Curse states are removed by tag.");
                    Assert(HeldQuirks("2")["twilight_dreamer"] is JsonObject,
                        $"Native event {selected.Id}: curse removal retains the unrelated singleton quirk.");
                    curseRemovalCases++;
                }
                if (selected.Effects.Any(effect => effect.Type == "dead_recruit"))
                {
                    var offers = saves["persist.town_event.json"]["base_root"]!["dead_hero_entries"]!.AsArray();
                    Assert(offers.Count == 3 && offers.All(id => !HeldQuirks(id!.GetValue<int>().ToString())
                            .Any(quirk => curseIds.Contains(quirk.Key) || quirk.Key == "twilight_dreamer")),
                        $"Native event {selected.Id}: revival clears native vampire and singleton tags from all offered heroes.");
                    Assert(HeldQuirks("1")[curseIds[0]] is JsonObject,
                        $"Native event {selected.Id}: revival leaves the living roster hero unchanged.");
                    revivalCases++;
                }
                var path = Path.Combine(root, "roundtrip", selected.Id);
                Directory.CreateDirectory(path);
                foreach (var pair in saves)
                {
                    var restored = await RoundtripDirectoryQueryAsync(path, pair.Key, pair.Value, codec);
                    Assert(JsonNode.DeepEquals(pair.Value, restored), $"Native event {selected.Id}: DSON roundtrip for {pair.Key}.");
                }
                Console.WriteLine("PASS native resources: " + selected.Id);
            }
            catch (Exception error)
            {
                failures.Add(selected.Id + ": " + error.Message);
                Console.WriteLine("FAIL native resources: " + failures[^1]);
            }
        }
        File.WriteAllText(Path.Combine(root, "result.json"), System.Text.Json.JsonSerializer.Serialize(new
        {
            gameDirectory, eventCount = events.Events.Count, curseRemovalCases, revivalCases, failures, catalogIssues = events.Issues,
            validation = "Synthetic private saves against installed native definitions; not an in-game play test."
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Artifacts: {root}");
        Assert(events.Events.Count > 0 && failures.Count == 0, "All native event resource initialization cases must pass: " + string.Join("; ", failures));
        Assert(curseRemovalCases > 0 && revivalCases > 0, "Installed-resource checks must exercise native curse removal and revival.");
    }
}
