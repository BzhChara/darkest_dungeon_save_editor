using System.Text.Json.Nodes;

namespace DarkestDungeonSaveEditor.Core;

internal static class TownEventHistory
{
    private static readonly int EventRecordType = unchecked((int)NativeResourceIdentity.HashCString("TownEventStartedLogItem"));

    internal static int SetCurrent(JsonObject state, JsonObject log, int oldId, int newId)
    {
        var history = state["result_event_history"] as JsonArray
            ?? throw new InvalidDataException("Missing town event history.");
        var chapters = JsonSupport.RequireObject(log, "chapters");
        var totalWeeks = TownEventSaveEditor.RequiredInt(log, "total_weeks");
        var ordered = chapters.Select(pair => pair.Value as JsonObject ?? throw Mismatch()).ToArray();
        // Native load retains serialized chapter order. Save keys are assigned
        // 1, 2, ... independently of chapterIndex (0x1405507C1–0x14055088D).
        var latest = ordered.LastOrDefault();
        var week = latest is null ? totalWeeks : TownEventSaveEditor.RequiredInt(latest, "chapterIndex");
        if (week < 0 || (totalWeeks != week && (long)totalWeeks != (long)week + 1))
            throw new InvalidOperationException(EditorText.Get("TownEvent_StaleWeek"));

        if (oldId != 0)
        {
            var records = ordered.SelectMany(chapter => chapter.Select(pair => pair.Value).OfType<JsonObject>()
                .Where(record => record.ContainsKey("town_event_result_id"))
                .Select(record => (Chapter: chapter, Record: record))).ToArray();
            if (records.Any(item => JsonSupport.ReadInt(item.Record, "rtti") != EventRecordType)) throw Mismatch();
            var last = records.LastOrDefault();
            if (TownEventSaveEditor.RequiredInt(state, "last_town_event_week") != week)
                throw new InvalidOperationException(EditorText.Get("TownEvent_StaleWeek"));
            if (last.Record is null || history.Count == 0 || history[^1]?.GetValue<int>() != oldId ||
                JsonSupport.ReadInt(last.Record, "town_event_result_id") != oldId ||
                JsonSupport.ReadInt(last.Chapter, "chapterIndex") != week) throw Mismatch();
            // The last occurrence owns the active event, even when an earlier
            // occurrence in the same week has the same ID. Never rewrite it too.
            history[^1] = newId;
            last.Record["town_event_result_id"] = newId;
        }
        else
        {
            // Native StartEvent uses the last chapter's week (0x14058B899–B8C1)
            // and appends TownEventStartedLogItem to that chapter (0x14058EF64–EFBF).
            // An empty log creates its first chapter using total_weeks.
            // Earlier event log items may have been pruned by the native log size
            // limit (0x140550C60–CA5); insertion does not depend on their retention.
            latest ??= new JsonObject { ["chapterIndex"] = week };
            var entries = new JsonObject(latest.Where(pair => pair.Key != "chapterIndex")
                .Select(pair => new KeyValuePair<string, JsonNode?>(pair.Key, null)));
            var key = TownEventInitializer.NextKey(entries);
            if (chapters.Count == 0) chapters["1"] = latest;
            latest[key] = new JsonObject { ["rtti"] = EventRecordType, ["town_event_result_id"] = newId };
            history.Add(newId);
            state["last_town_event_week"] = week;
        }
        return week;
    }

    private static InvalidDataException Mismatch() => new(EditorText.Get("TownEvent_HistoryMismatch"));
}
