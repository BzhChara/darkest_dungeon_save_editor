using DarkestDungeonSaveEditor.Core.Dson;

internal static partial class ContractSuite
{
    private static async Task VerifyTownEventTriggerRulesAsync(string root)
    {
        var f = await BuildTownEventFixtureAsync(root);
        // Reduced native revival metadata; the surrounding save is synthetic.
        File.WriteAllText(f.EventsPath, """
            {"events":[{"id":"dead_recruit","cooldown":3,
              "requirements":{"minimum_week":15,"dead_heroes":3},
              "data":[{"type":"dead_recruit","number_data":3}]}]}
            """);
        var revival = TownEventCatalog.Load(f.Content).Events.Single();
        Assert(revival.TriggerRules.MinimumWeek == 15 && revival.TriggerRules.DeadHeroes == 3 &&
            revival.TriggerRules.CooldownEvents == 3 && !revival.TriggerRules.IsUnique,
            "Native revival requirements and repeat restrictions are display metadata.");
        // The fixture is week 4 with one dead hero. Natural thresholds must not
        // become new manual-preview or commit restrictions.
        var prepared = await f.Service.PrepareTownEventEditAsync(f.Content.Profile, revival, f.Content);
        Assert(prepared.Preview.Week == 4 && prepared.Preview.NewEventId == "dead_recruit",
            "Manual replacement remains available before natural week/dead-hero thresholds.");
        await f.Service.CommitAsync(prepared);
        var proposedLog = JsonSupport.ReadObject(Path.Combine(prepared.WorkspaceDirectory, "proposed", "persist.campaign_log.json"));
        Assert(proposedLog["base_root"]!["total_weeks"]!.GetValue<int>() == 5 &&
            prepared.Files.All(file => file.FileName != "persist.game.json"),
            "Trigger-condition display must not change the replacement transaction's scope.");
        var noEvent = f.Saves.ToDictionary(pair => pair.Key, pair => pair.Value.DeepClone().AsObject());
        noEvent["persist.town_event.json"]["base_root"]!["current_result_event_id"] = 0;
        var insertion = TownEventSaveEditor.ReplaceCurrent(noEvent, revival, new TownEventInitializer(f.Content, 7).Initialize);
        Assert(insertion.OldEventId == 0 && insertion.Week == 4 &&
            noEvent["persist.campaign_log.json"]["base_root"]!["total_weeks"]!.GetValue<int>() == 5,
            "Adding an event to an empty slot also bypasses natural thresholds without advancing the week.");

        // Authored Mod fixture: exercises engine-supported fields, not a claim
        // that this exact combination exists in an installed Mod.
        var game = f.Content.Resolution!.GameDirectory;
        var modRoot = Path.Combine(game, "mods", "trigger_rules");
        WriteMultiMash(modRoot, "project.xml", "<project><Title>Trigger Rules Fixture</Title></project>");
        var modPath = WriteMultiMash(modRoot, "campaign/town_events/event.town_events.events.json", """
            {"events":[{"id":"Az","is_unique":true,"cooldown":4,
              "requirements":{"minimum_week":40,"minimum_week":1,"dead_heroes":3,
                "hero_level_counts":[{"level":5,"count":4}],
                "upgrades_purchased":[{"tree_id":"blacksmith.armour","requirement_code":"a"}],
                "trinket_storage_count":8,"town_events":["earlier_a","earlier_b"],
                "finished_plot_quests":["plot_a","plot_b"],"available_plot_quest":"plot_open",
                "infestation_sequences":["infestation_1_2","infestation_2_2"],
                "minimum_number_of_district_buildings":1,"building_unlocked":"camping_trainer"},
              "data":[{"type":"free_activity","string_data":"abbey"}]},
              {"id":"BE","requirements":{"minimum_week":999},"data":[]},
              {"id":"unrestricted","data":[]}]}
            """);
        WriteFixtureManifest(modRoot);
        var gameSave = f.Saves["persist.game.json"];
        gameSave["base_root"]!["applied_ugcs_1_0"]!["0"] = new JsonObject
            { ["name"] = "Trigger Rules Fixture", ["source"] = "mod_local_source" };
        var codec = DsonSaveCodec.CreateNative();
        await codec.EncodeAsync(WriteMultiMash(root, "enabled-game.json", gameSave.ToJsonString()),
            Path.Combine(f.Content.Profile.ProfileDirectory, "persist.game.json"), null);
        var content = await ActiveContentResolver.ResolveAsync(f.Content.Profile, game, null, codec, Path.Combine(root, "mod-resolve"));
        var catalog = TownEventCatalog.Load(content);
        var effective = catalog.Events.Single(item => item.Id == "Az");
        var rules = effective.TriggerRules;
        Assert(catalog.Events.Count == 2 && effective.SourcePath == modPath && !effective.HasProviderConflict &&
            rules.MinimumWeek == 40 && effective.Effects.Single().Type == "free_activity",
            "Same-path Mod overlay, first native hash and first JSON member govern conditions exactly as effects.");
        Assert(rules.HeroLevelCounts.SequenceEqual([new TownEventHeroCountRequirement(5, 4)]) &&
            rules.UpgradesPurchased.SequenceEqual([new TownEventUpgradeRequirement("blacksmith.armour", "a")]) &&
            rules.TownEvents.SequenceEqual(["earlier_a", "earlier_b"]) &&
            rules.FinishedPlotQuests.SequenceEqual(["plot_a", "plot_b"]) && rules.AvailablePlotQuest == "plot_open" &&
            rules.InfestationSequences.SequenceEqual(["infestation_1_2", "infestation_2_2"]) &&
            rules.MinimumDistrictDefinitions == 1 && rules.BuildingUnlocked == "camping_trainer" &&
            rules.TrinketStorageCount == 8 && rules.DeadHeroes == 3 && rules.IsUnique && rules.CooldownEvents == 4,
            "All native requirement fields retain their configured values and Mod identifiers.");
        var original = EditorText.Culture.Name;
        try
        {
            foreach (var language in new[] { "en", "zh-CN" })
            {
                EditorText.Initialize(language, EditorText.SystemCulture);
                var display = rules.Describe();
                Assert(display.Contains("earlier_a, earlier_b", StringComparison.Ordinal) &&
                    display.Contains(EditorText.Format("TownEvent_RequirementCooldown", 4), StringComparison.Ordinal) &&
                    display.Contains(EditorText.Format("TownEvent_RequirementDistricts", 1), StringComparison.Ordinal) &&
                    !display.Contains("TownEvent_", StringComparison.Ordinal) &&
                    catalog.Events.Single(item => item.Id == "unrestricted").TriggerRules.Describe() == EditorText.Get("TownEvent_RequirementsNone"),
                    "Both languages describe repeat/district semantics and retain custom dependency IDs.");
            }
        }
        finally { EditorText.Initialize(original, EditorText.SystemCulture); }
        // Constructed parser boundaries, not observed installed-Mod defects.
        File.WriteAllText(modPath, new JsonObject { ["events"] = new JsonArray(
            new JsonObject
            {
                ["id"] = "display_boundary",
                ["requirements"] = new JsonObject
                {
                    ["available_plot_quest"] = new string('x', 254) + "中",
                    ["hero_level_counts"] = new JsonArray(
                        new JsonObject { ["level"] = 5, ["count"] = 4 },
                        new JsonObject { ["level"] = 5, ["count"] = 1 },
                        new JsonObject { ["level"] = 5, ["count"] = 0 })
                },
                ["data"] = new JsonArray(new JsonObject { ["type"] = "free_activity", ["string_data"] = "abbey" })
            },
            new JsonObject { ["id"] = "unrelated_event", ["data"] = new JsonArray() }) }.ToJsonString());
        WriteFixtureManifest(modRoot);
        var boundary = TownEventCatalog.Load(content);
        var boundaryEvent = boundary.Events.Single(item => item.Id == "display_boundary");
        Assert(boundary.Events.Count == 2 && boundary.Events.All(item => !item.HasProviderConflict) &&
            TownEventAvailability.UnavailableReason(boundaryEvent).Length == 0 &&
            boundaryEvent.TriggerRules.AvailablePlotQuest == new string('x', 254) + "\uFFFD" &&
            boundaryEvent.TriggerRules.HeroLevelCounts.SequenceEqual([new TownEventHeroCountRequirement(5, 1)]),
            "Advisory reference decoding must not reject events; duplicate hero levels use the last nonzero native count.");
        Console.WriteLine("PASS: event trigger metadata, effective Mod definitions, bilingual conditions and unrestricted manual replacement.");
    }

    private static void VerifyInstalledEventTriggerRules(TownEventCatalogResult catalog)
    {
        TownEventTriggerRules Rules(string id) => catalog.Events.Single(item => item.Id == id).TriggerRules;
        var revival = Rules("dead_recruit");
        var theft = Rules("trinket_retention_add_from_storage");
        var invasion = Rules("plot_quest_town_invasion_0");
        Assert(revival.MinimumWeek == 15 && revival.DeadHeroes == 3 && revival.CooldownEvents == 3 &&
            theft.MinimumWeek == 42 && theft.TrinketStorageCount == 8 && theft.IsUnique &&
            invasion.MinimumWeek == 40 && invasion.HeroLevelCounts.SequenceEqual([new TownEventHeroCountRequirement(5, 4)]) &&
            Rules("upgrade_tag_free_armour").UpgradesPurchased.SequenceEqual([new TownEventUpgradeRequirement("blacksmith.armour", "a")]),
            "Installed base-game event conditions match the inspected native resource values.");
        Assert(Rules("infestation_repeat").AvailablePlotQuest == "plot_crimson_court_0" &&
            Rules("return_from_CCQ1").FinishedPlotQuests.SequenceEqual(["plot_crimson_court_0"]) &&
            Rules("fanatic_intro_0").InfestationSequences.SequenceEqual(["infestation_1_2"]) &&
            Rules("cc_districts_unlock").MinimumDistrictDefinitions == 1,
            "Installed DLC event prerequisites remain distinct from associated quest-generation requirements.");
        Console.WriteLine("PASS native resources: base/DLC event prerequisites and repeat conditions.");
    }
}
