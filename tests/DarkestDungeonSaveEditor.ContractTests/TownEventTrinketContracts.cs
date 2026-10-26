internal static partial class ContractSuite
{
    private static async Task VerifyEventTrinketDefinitionSelectionAsync(string root)
    {
        // Authored duplicate entries, not a claim about an installed Mod:
        // native trinket admission filters missing class/Buff dependencies
        // before first-hash selection (resource-duplicate-semantics, 30–31).
        foreach (var sourceKind in new[] { "base", "local", "workshop" })
        foreach (var dependency in new[] { "hero_class_requirements", "buffs" })
        {
            var f = await BuildTownEventFixtureAsync(Path.Combine(root, sourceKind, dependency));
            var path = Path.Combine(f.Content.Resolution!.GameDirectory, "trinkets", "event.entries.trinkets.json");
            var resource = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            var entries = resource["entries"]!.AsArray();
            var effective = entries.OfType<JsonObject>().Single(entry => entry["id"]!.GetValue<string>() == "quest_prize");
            var discarded = effective.DeepClone().AsObject();
            discarded[dependency] = new JsonArray("unloaded_dependency");
            discarded["price"] = 111;
            effective["price"] = 222;
            entries.Insert(entries.IndexOf(effective), discarded);
            var content = f.Content;
            var eventId = "test_plot_quest";
            string? modRoot = null;
            if (sourceKind != "base")
            {
                var game = content.Resolution!.GameDirectory;
                var workshop = Path.Combine(f.Root, "workshop");
                modRoot = sourceKind == "local" ? Path.Combine(game, "mods", "event_mod") : Path.Combine(workshop, "123456");
                WriteMultiMash(modRoot, "project.xml", "<project><Title>Event Reward Mod</Title></project>");
                var quests = JsonNode.Parse(File.ReadAllText(Path.Combine(game, "campaign", "quest", "event.quest.plot_quests.json")))!.AsObject();
                quests["plot_quests"]![0]!["id"] = "mod_event_plot";
                WriteMultiMash(modRoot, "campaign/quest/mod.quest.plot_quests.json", quests.ToJsonString());
                eventId = "mod_quest_reward";
                WriteMultiMash(modRoot, "campaign/town_events/mod.town_events.events.json", """
                    {"events":[{"id":"mod_quest_reward","data":[{"type":"plot_quest","string_data":"mod_event_plot","number_data":1}]}]}
                    """);
                path = WriteMultiMash(modRoot, "trinkets/event.entries.trinkets.json", resource.ToJsonString());
                WriteFixtureManifest(modRoot);
                var gameSave = f.Saves["persist.game.json"];
                gameSave["base_root"]!["applied_ugcs_1_0"]!["0"] = new JsonObject
                {
                    ["name"] = sourceKind == "local" ? "Event Reward Mod" : "123456",
                    ["source"] = sourceKind == "local" ? "mod_local_source" : "Steam"
                };
                var codec = DsonSaveCodec.CreateNative();
                await codec.EncodeAsync(WriteMultiMash(f.Root, "enabled-mod-game.json", gameSave.ToJsonString()),
                    Path.Combine(content.Profile.ProfileDirectory, "persist.game.json"), null);
                content = await ActiveContentResolver.ResolveAsync(content.Profile, game, workshop, codec, Path.Combine(f.Root, "mod-resolve"));
                Assert(content.Sources.Any(source => source.Kind == sourceKind && source.Directory == modRoot),
                    sourceKind + ": the enabled fixture Mod must be resolved from the profile.");
            }
            var selected = TownEventCatalog.Load(content).Events.Single(item => item.Id == eventId);
            Assert(!selected.HasProviderConflict && (modRoot is null || selected.SourcePath.StartsWith(modRoot + Path.DirectorySeparatorChar)),
                sourceKind + ": custom events must be discovered through their Mod manifest.");

            void CheckReward(bool expected, string reason)
            {
                File.WriteAllText(path, resource.ToJsonString());
                if (modRoot is not null) WriteFixtureManifest(modRoot);
                var definition = TrinketCatalog.Load(content).Trinkets.Single(item => item.Id == "quest_prize");
                Assert(!definition.HasProviderConflict && definition.Price == 222,
                    sourceKind + "/" + dependency + ": the later admitted definition must win before event initialization.");
                var saves = f.Saves.ToDictionary(pair => pair.Key, pair => pair.Value.DeepClone().AsObject());
                TownEventSaveEditor.ReplaceCurrent(saves, selected, new TownEventInitializer(content, 7).Initialize);
                var items = saves["persist.quest.json"]["base_root"]!["quests"]!["1"]!["completion_reward"]!["items_definition"]!["items"]!.AsObject();
                var rewarded = items.Any(pair => pair.Value?["id"]?.GetValue<string>() == "quest_prize");
                Assert(rewarded == expected, sourceKind + "/" + dependency + ": " + reason);
            }

            // The fixture's plot takes place in the Weald.
            discarded["origin_dungeon"] = "crypts";
            effective["origin_dungeon"] = "weald";
            CheckReward(true, "A discarded entry's dungeon must not exclude the effective quest reward.");
            discarded["origin_dungeon"] = "weald";
            effective["origin_dungeon"] = "crypts";
            CheckReward(false, "The effective entry's dungeon restriction must still exclude the reward.");
            effective["origin_dungeon"] = "weald";

            // old_owned is already in storage, so a real transform-family link
            // reaches quest_prize's limit of one; a discarded link must not.
            foreach (var transform in new[] { "transform_on_quest_complete", "transform_on_wearer_killed", "transform_on_trigger_limit_exhausted" })
            {
                discarded[transform] = "old_owned";
                CheckReward(true, "A discarded " + transform + " link must not count toward the ownership limit.");
                discarded.Remove(transform);
                effective[transform] = "old_owned";
                CheckReward(false, "An effective " + transform + " link must count toward the ownership limit.");
                effective.Remove(transform);
            }
        }
        Console.WriteLine("PASS: Base/local/Workshop event quest rewards use admitted trinket dungeon restrictions and all three transform-family links.");
    }
}
