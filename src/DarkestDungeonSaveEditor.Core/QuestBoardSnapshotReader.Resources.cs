using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DarkestDungeonSaveEditor.Core;

public static partial class QuestBoardSnapshotReader
{
    internal static IReadOnlyDictionary<uint, JsonElement> ReadGoalDefinitions(ActiveContentSnapshot content)
    {
        var issues = new List<string>();
        var goals = new QuestBoardResources(content, issues).Definitions(NativeReferenceJsonKind.QuestTypes, "goals");
        if (issues.Count > 0)
            throw new InvalidDataException(EditorText.Format("QuestCompletion_Definitions", string.Join(Environment.NewLine, issues)));
        return goals;
    }

    private sealed class QuestBoardResources(ActiveContentSnapshot content, List<string> issues)
    {
        private readonly Dictionary<NativeReferenceJsonKind, JsonElement[]> _json = [];

        private IReadOnlyList<EffectiveContentFile> Files(string directory, Func<string, bool, IReadOnlyList<string>, bool> accepts)
        {
            var prefixes = ContentFileOverlay.GetEnabledDlcPrefixes(content.Sources);
            var candidates = content.Sources.OrderBy(source => source.LoadOrder).SelectMany(source =>
                ContentFileDiscovery.EnumerateQuery(source, prefixes, issues, "Quest board",
                    path => accepts(path, source.Kind is "local" or "workshop", prefixes),
                    new ContentFileRule(directory, "*")).Select(path => new ContentFileCandidate(source, path))).ToArray();
            return NativeContentFileResolver.Resolve(candidates, content.Sources, "Quest board", issues);
        }

        private IEnumerable<JsonElement> ReadJson(IEnumerable<EffectiveContentFile> files)
        {
            foreach (var file in files)
            {
                JsonElement? root = null;
                try
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(file.Path), new JsonDocumentOptions
                    { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                    if (document.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Expected a resource object.");
                    root = document.RootElement.Clone();
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
                {
                    issues.Add($"Quest board resource could not be read: {file.Path}: {error.Message}");
                }
                if (root.HasValue) yield return root.Value;
            }
        }

        private JsonElement[] Json(NativeReferenceJsonKind kind)
        {
            if (_json.TryGetValue(kind, out var cached)) return cached;
            var directory = kind == NativeReferenceJsonKind.Provision ? "campaign/provision" : "campaign/quest";
            return _json[kind] = ReadJson(Files(directory, (path, mod, prefixes) =>
                NativeResourceFileRules.ReferenceJsonKind(path, mod ? prefixes : [], mod) == kind)).ToArray();
        }

        internal Dictionary<uint, JsonElement> Definitions(NativeReferenceJsonKind kind, string array, bool lastMatch = false) =>
            Json(kind).SelectMany(root => NativeJsonReader.Array(root, array))
                .Where(value => Text(value, "id").Length > 0)
                .GroupBy(value => NativeResourceIdentity.HashCString(Text(value, "id")))
                .ToDictionary(group => group.Key, group => lastMatch ? group.Last() : group.First());

        internal Dictionary<uint, JsonElement> Trinkets() =>
            ReadJson(Files("trinkets", (path, mod, prefixes) => NativeResourceFileRules.IsTrinketFile(path, mod ? prefixes : [], mod)))
                .SelectMany(root => NativeJsonReader.Array(root, "entries"))
                .Where(value => Text(value, "id").Length > 0)
                .GroupBy(value => NativeResourceIdentity.HashCString(Text(value, "id")))
                .ToDictionary(group => group.Key, group => group.First());

        internal IReadOnlyDictionary<uint, uint> MonsterSharedHealth(IEnumerable<string> requestedIds)
        {
            var requested = requestedIds.Select(NativeResourceIdentity.HashCString).ToHashSet();
            var result = new Dictionary<uint, uint>();
            if (requested.Count == 0) return result;
            var ids = NativeContentFileResolver.DiscoverActorIds(content.Sources, "monsters", issues)
                .Where(id => id.Length >= 2 && requested.Contains(HashSaved(id)))
                .DistinctBy(HashSaved).ToArray();
            var files = NativeContentFileResolver.ResolveOpenRequests(content.Sources,
                ids.SelectMany(id => NativeContentFileResolver.ActorOpenPaths("monsters", id)), "Quest monsters", issues);
            foreach (var id in ids)
            {
                uint shared = 0;
                // Native actor loading opens canonical info, then art. A later record
                // with this field replaces the earlier value, using the normal parser.
                foreach (var path in NativeContentFileResolver.ActorOpenPaths("monsters", id))
                {
                    if (!files.TryGetValue(path, out var file)) continue;
                    try
                    {
                        foreach (var (kind, body) in NativeDarkestReader.ReadRecords(file.Path))
                            if (kind == "shared_health" && NativeDarkestReader.ReadString(body, ".id") is { } group)
                                shared = NativeResourceIdentity.HashCString(group);
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                    {
                        issues.Add($"Quest monster definition could not be read: {file.Path}: {error.Message}");
                    }
                }
                result[HashSaved(id)] = shared;
            }
            return result;
        }

        internal IReadOnlyList<QuestBoardRegionSnapshot> Regions(IReadOnlyList<string> savedRegions)
        {
            var layouts = new Dictionary<string, QuestBoardRegionSnapshot>(StringComparer.Ordinal);
            foreach (var file in Files("campaign/town/quest_select", (path, mod, prefixes) =>
                         NativeResourceFileRules.IsInDirectory(path, "campaign/town/quest_select", mod ? prefixes : [], mod) &&
                         path.EndsWith("quest_select.layout.darkest", StringComparison.Ordinal)))
            {
                try
                {
                    foreach (var (kind, body) in NativeDarkestReader.ReadRecords(file.Path))
                    {
                        const string prefix = "quest_select_dungeon_layout_";
                        if (!kind.StartsWith(prefix, StringComparison.Ordinal)) continue;
                        var coordinates = NativeDarkestReader.ReadRawStringSlots(body, ".quest_map_pos", 2).ToArray();
                        if (coordinates.Length != 2 || !double.TryParse(coordinates[0], CultureInfo.InvariantCulture, out var x) ||
                            !double.TryParse(coordinates[1], CultureInfo.InvariantCulture, out var y) || !double.IsFinite(x) || !double.IsFinite(y)) continue;
                        var id = kind[prefix.Length..];
                        var columns = NativeDarkestReader.ReadInt(body, ".quest_number_of_quests_in_row") ?? 4;
                        layouts[id] = new(id, x, y, Math.Max(1, columns), BilingualContentName.Empty)
                        { HasVisibleProgression = NativeDarkestReader.ReadInt(body, ".has_visible_progression") is not 0 and not null };
                    }
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    issues.Add($"Quest map layout could not be read: {file.Path}: {error.Message}");
                }
            }
            foreach (var id in savedRegions.Where(id => !layouts.ContainsKey(id)))
            {
                layouts[id] = new(id, 0, 0, 4, BilingualContentName.Empty) { HasLayout = false };
                issues.Add($"Quest board region has no readable map layout: {id}. Its quests remain available in the unpositioned quest selector.");
            }
            // Manual editing exposes empty quest destinations, but not internal wave
            // scenes or unused layout placeholders. Saved quests always take precedence.
            var visible = savedRegions.ToHashSet(StringComparer.Ordinal);
            var destinations = QuestDestinations();
            var requests = layouts.Keys.Where(id => !visible.Contains(id) && id.Length > 0 &&
                    id is not ("." or "..") && id.IndexOfAny(Path.GetInvalidFileNameChars()) < 0)
                .ToDictionary(id => $"dungeons/{id}/{id}.dungeon.json", id => id, StringComparer.Ordinal);
            var definitions = NativeContentFileResolver.ResolveOpenRequests(content.Sources, requests.Keys,
                "Quest board region", issues);
            foreach (var (path, file) in definitions)
            {
                foreach (var definition in ReadJson([file]))
                {
                    var requiresQuest = Child(definition, "requires_quest_to_display");
                    if (requiresQuest.ValueKind is not (JsonValueKind.False or JsonValueKind.True))
                        issues.Add($"Quest board region has no valid requires_quest_to_display flag: {file.Path}.");
                    else if (requiresQuest.ValueKind is JsonValueKind.False ||
                             destinations.Contains(NativeResourceIdentity.HashCString(requests[path])))
                        visible.Add(requests[path]);
                }
            }
            return layouts.Values.Where(region => visible.Contains(region.Id)).ToArray();
        }

        private HashSet<uint> QuestDestinations()
        {
            // Match the quest constructor's first native plot identity, rather than
            // the last-match runtime lookup used separately for plot provisions.
            var plots = Definitions(NativeReferenceJsonKind.PlotQuests, "plot_quests").Values
                .Select(plot => Text(Child(plot, "quest"), "dungeon"));
            var generated = Json(NativeReferenceJsonKind.QuestGeneration)
                .SelectMany(root => NativeJsonReader.Array(Child(Child(root, "generation"), "type"), "available_quests_table"))
                .Where(region => NativeJsonReader.Array(region, "generated_quest_table")
                    .Where(table => table.ValueKind == JsonValueKind.Array)
                    .SelectMany(table => table.EnumerateArray()).Any(quest => Text(quest, "type").Length > 0))
                .Select(region => Text(region, "dungeon"));
            return plots.Concat(generated).Where(id => id.Length > 0)
                .Select(NativeResourceIdentity.HashCString).ToHashSet();
        }

        internal IReadOnlyList<QuestBoardRegionSnapshot> Progression(IReadOnlyList<QuestBoardRegionSnapshot> regions, JsonObject? document)
        {
            if (document is null || !regions.Any(region => region.HasVisibleProgression)) return regions;
            try
            {
                var saved = JsonSupport.RequireObject(document, "base_root", "dungeon");
                const string path = "campaign/progression/progression.json";
                var files = NativeContentFileResolver.ResolveOpenRequests(content.Sources, [path], "Quest board progression", issues);
                if (!files.TryGetValue(path, out var file)) throw new InvalidDataException("Missing dungeon progression thresholds.");
                var root = ReadJson([file]).Single();
                var thresholds = NativeJsonReader.Array(Child(root, "dungeon"), "level_threshold_table")
                    .Select(value => unchecked((uint)value.GetInt32())).ToArray();
                if (thresholds.Length == 0) throw new InvalidDataException("Empty dungeon progression thresholds.");
                return regions.Select(region =>
                {
                    if (!region.HasVisibleProgression) return region;
                    var entry = saved.LastOrDefault(pair => HashSaved(pair.Key) == HashSaved(region.Id)).Value as JsonObject;
                    var xp = entry is null ? 0u : unchecked((uint)RequiredInt(entry, "xp"));
                    // Native 0x140564F70 stops at the first greater threshold.
                    var level = 0;
                    for (var index = 0; index < thresholds.Length && xp >= thresholds[index]; index++) level = index;
                    var span = level + 1 < thresholds.Length ? (double)thresholds[level + 1] - thresholds[level] : 0;
                    var fraction = span <= 0 ? 1 : Math.Clamp(((double)xp - thresholds[level]) / span, 0, 1);
                    return region with { ProgressionLevel = level, ProgressionFraction = fraction };
                }).ToArray();
            }
            catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException or ArgumentException)
            {
                issues.Add("Quest board progression could not be read: " + error.Message);
                return regions;
            }
        }

        internal (int Count, bool IsRandom) Camping(string type, int length, bool isPlot, JsonElement plot,
            IEnumerable<JsonElement> goals)
        {
            var goalList = goals.ToArray();
            var isRandom = isPlot && HashSaved(type) == NativeResourceIdentity.HashCString("wave") &&
                goalList.Any(goal => Text(goal, "type") == "survival" && ItemAmount(Child(goal, "data")) == 0);
            // The quest-select UI appends every effective length list, plot provisions and
            // goal starting items, then removes disabled identities. It passes no heroes.
            // See docs/quest-board-preview.md for the native consumers and evidence.
            bool IsFirewood(JsonElement item, string typeField = "type", string idField = "id") =>
                HashSaved(Text(item, typeField)) == NativeResourceIdentity.HashCString("supply") &&
                HashSaved(Text(item, idField)) == NativeResourceIdentity.HashCString("firewood");
            if (isPlot && NativeJsonReader.Array(plot, "disabled_provisions")
                    .Any(item => IsFirewood(item, "item_type", "item_id"))) return (0, isRandom);

            var lengthItems = Json(NativeReferenceJsonKind.Provision).Select(root => Child(root, "raid_starting_length_inventory_item_lists"))
                .Where(value => value.ValueKind == JsonValueKind.Array && length >= 0 && length < value.GetArrayLength())
                .Select(value => value[length]).Where(value => value.ValueKind == JsonValueKind.Array)
                .SelectMany(value => value.EnumerateArray());
            var additional = Child(Child(plot, "additional_provisions"), "items");
            var plotItems = isPlot && additional.ValueKind == JsonValueKind.Object
                ? additional.EnumerateObject().Select(pair => pair.Value) : [];
            var extraItems = plotItems.Concat(goalList.SelectMany(goal => NativeJsonReader.Array(goal, "starting_items")))
                .Where(item => ItemAmount(item) > 0);
            var count = 0;
            foreach (var item in lengthItems.Concat(extraItems).Where(item => IsFirewood(item)))
                count = unchecked(count + ItemAmount(item));
            return (count, isRandom);
        }

        private static int ItemAmount(JsonElement item) => Child(item, "amount") is { ValueKind: JsonValueKind.Number } value &&
            value.TryGetInt32(out var amount) ? amount : 0;
    }
}
