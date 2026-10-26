using DarkestDungeonSaveEditor.Core.Dson;

internal static partial class ContractSuite
{
    private static async Task VerifyRepeatedEventTransactionsAsync(string root)
    {
        var f = await BuildTownEventFixtureAsync(root);
        var codec = DsonSaveCodec.CreateNative();
        var profile = f.Content.Profile;
        var events = JsonNode.Parse(File.ReadAllText(f.EventsPath))!;
        events["events"]!.AsArray().OfType<JsonObject>().Single(row => row["id"]!.GetValue<string>() == "test_dead_recruit")
            ["data"]![0]!["number_data"] = 3;
        File.WriteAllText(f.EventsPath, events.ToJsonString());
        var catalog = TownEventCatalog.Load(f.Content);
        var revival = catalog.Events.Single(row => row.Id == "test_dead_recruit");
        var roster = f.Saves["persist.roster.json"].DeepClone().AsObject();
        for (var id = 3; id <= 5; id++) roster["base_root"]!["heroes"]![id.ToString()] = roster["base_root"]!["heroes"]!["2"]!.DeepClone();

        async Task Write(string name, JsonObject save) => await codec.EncodeAsync(
            WriteMultiMash(root, "input/" + name, save.ToJsonString()), Path.Combine(profile.ProfileDirectory, name), null);
        async Task<JsonObject> Read(string name)
        {
            var decoded = Path.Combine(root, "observed", name);
            Directory.CreateDirectory(Path.GetDirectoryName(decoded)!);
            await codec.DecodeAsync(Path.Combine(profile.ProfileDirectory, name), decoded);
            return JsonNode.Parse(File.ReadAllText(decoded))!.AsObject();
        }
        JsonObject Hero(JsonObject save, int id) => save["base_root"]!["heroes"]![id.ToString()]!["hero_file_data"]!["raw_data"]!["base_root"]!.AsObject();
        int[] Offers(JsonObject save) => save["base_root"]!["dead_hero_entries"]!.AsArray().Select(value => value!.GetValue<int>()).ToArray();
        void CheckAnchors(PreparedTownEventEdit edit)
        {
            Assert(edit.Files.Any(file => file.FileName == "persist.campaign_log.json") &&
                edit.Files[^1].FileName == "persist.town_event.json",
                "Repeated events retain both required transaction files and publish the event last.");
        }

        await Write("persist.roster.json", roster);
        await f.Service.CommitAsync(await f.Service.PrepareTownEventEditAsync(profile, revival, f.Content));
        var firstState = await Read("persist.town_event.json");
        var originalLog = await Read("persist.campaign_log.json");
        var originalHistory = firstState["base_root"]!["result_event_history"]!.DeepClone();
        Assert(Offers(firstState).Distinct().Count() == 3, "The fixture offers three distinct dead heroes before repeat testing.");

        var pendingRefresh = await f.Service.PrepareTownEventEditAsync(profile, revival, f.Content);
        CheckAnchors(pendingRefresh);
        Assert(pendingRefresh.Preview.OldEventId == revival.NativeId && pendingRefresh.Preview.RemovedRevivalOffers == 3,
            "A fresh preview can replace the same event and retires its pending revival offers.");
        var historyFile = pendingRefresh.Files.Single(file => file.FileName == "persist.campaign_log.json");
        Assert(JsonNode.DeepEquals(originalLog, JsonNode.Parse(File.ReadAllText(historyFile.ProposedDecodedPath))),
            "The required campaign-log transaction file has no additional occurrence or week.");
        var before = f.Saves.Keys.ToDictionary(name => name, name => ComputeSha256(Path.Combine(profile.ProfileDirectory, name)));
        foreach (var failureIndex in Enumerable.Range(1, pendingRefresh.Files.Count))
        {
            var writes = 0;
            f.Service.AfterTargetReplace = _ => { if (++writes == failureIndex) throw new IOException("injected same-event write failure"); };
            var failed = false;
            try { await f.Service.CommitAsync(pendingRefresh); } catch (InvalidOperationException) { failed = true; }
            Assert(failed && before.All(pair => ComputeSha256(Path.Combine(profile.ProfileDirectory, pair.Key)) == pair.Value),
                "A repeated-event transaction must restore every original byte after each injected write failure.");
        }
        f.Service.AfterTargetReplace = null;
        var committed = await f.Service.CommitAsync(pendingRefresh);
        Assert(before.All(pair => ComputeSha256(Path.Combine(committed.BackupDirectory, pair.Key)) == pair.Value),
            "Repeating an event keeps the existing complete-backup contract.");
        Assert(Offers(await Read("persist.town_event.json")).Distinct().Count() == 3,
            "Repeating an unclaimed revival event refreshes three offers instead of appending three more.");

        var revived = new Dictionary<int, JsonObject>();
        for (var claim = 0; claim < 2; claim++)
        {
            var state = await Read("persist.town_event.json");
            roster = await Read("persist.roster.json");
            var id = Offers(state)[0];
            // Arrange the recruitment result relevant to the editor: an active
            // hero and cleared offers. This is not a simulation of game settlement.
            Hero(roster, id)["roster.status"] = 0;
            revived[id] = Hero(roster, id).DeepClone().AsObject();
            state["base_root"]!["dead_hero_entries"] = new JsonArray();
            await Write("persist.roster.json", roster);
            await Write("persist.town_event.json", state);
            var refresh = await f.Service.PrepareTownEventEditAsync(profile, revival, f.Content);
            CheckAnchors(refresh);
            await f.Service.CommitAsync(refresh);
            var after = await Read("persist.town_event.json");
            var afterRoster = await Read("persist.roster.json");
            var offered = Offers(after);
            Assert(offered.Length == 3 - claim && offered.Distinct().Count() == offered.Length &&
                offered.All(candidate => !revived.ContainsKey(candidate) && Hero(afterRoster, candidate)["roster.status"]!.GetValue<int>() == 3),
                "Each repeat offers remaining dead heroes, excluding heroes already revived, even when fewer than three remain.");
            Assert(revived.All(pair => JsonNode.DeepEquals(pair.Value, Hero(afterRoster, pair.Key))),
                "Already recruited heroes must remain unchanged through later revival previews and commits.");
            Assert(JsonNode.DeepEquals(originalHistory, after["base_root"]!["result_event_history"]) &&
                JsonNode.DeepEquals(originalLog, await Read("persist.campaign_log.json")),
                "Repeated revival commits keep the original history occurrence and week counters unchanged.");
        }

        // Also exercise commits where the event-owned state itself is unchanged:
        // a repeated currency grant, a rule-only event, and an already-present quest.
        foreach (var eventId in new[] { "test_bonus_currency", "test_free_activity", "test_plot_quest" })
        {
            var selected = catalog.Events.Single(row => row.Id == eventId);
            await f.Service.CommitAsync(await f.Service.PrepareTownEventEditAsync(profile, selected, f.Content));
            var oldState = await Read("persist.town_event.json");
            var oldLog = await Read("persist.campaign_log.json");
            var oldQuest = await Read("persist.quest.json");
            var repeat = await f.Service.PrepareTownEventEditAsync(profile, selected, f.Content);
            CheckAnchors(repeat);
            var eventFile = repeat.Files.Single(file => file.FileName == "persist.town_event.json");
            Assert(JsonNode.DeepEquals(oldState, JsonNode.Parse(File.ReadAllText(eventFile.ProposedDecodedPath))),
                eventId + ": an unchanged event file still belongs to a valid repeat transaction.");
            await f.Service.CommitAsync(repeat);
            Assert(JsonNode.DeepEquals(oldLog, await Read("persist.campaign_log.json")) &&
                JsonNode.DeepEquals(oldQuest, await Read("persist.quest.json")),
                eventId + ": repeat commits preserve history and already generated quests.");
            if (eventId == "test_bonus_currency")
                Assert((await Read("persist.estate.json"))["base_root"]!["wallet"]!["0"]!["amount"]!.GetValue<int>() == 150,
                    "An unchanged event ID does not prevent its repeated currency grant from committing.");
        }
        Console.WriteLine("PASS: repeated revival after recruitment, unchanged event/history transactions, backup/rollback and other repeated event commits.");
    }
}
