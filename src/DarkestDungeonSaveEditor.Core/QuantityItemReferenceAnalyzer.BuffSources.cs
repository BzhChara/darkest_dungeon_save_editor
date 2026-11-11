using System.Text;
using System.Text.Json;

namespace DarkestDungeonSaveEditor.Core;

internal static partial class QuantityItemReferenceAnalyzer
{
    // Potential sources in effective content, not a simulation of the current
    // party, purchased districts or random trait generation.
    private sealed partial class LootBuffSources(ActiveContentSnapshot content, List<string> issues)
    {
        private int failures;
        private readonly IReadOnlyList<string> prefixes = ContentFileOverlay.GetEnabledDlcPrefixes(content.Sources);

        internal bool Read(IReadOnlyList<ScannedContentFile> actors, TrinketHeroDependencies heroes,
            IReadOnlyDictionary<uint, JsonElement> trinkets, bool trinketsComplete, Action<string, bool> buff)
        {
            void Source(Action<Action<string>> read)
            {
                var priorFailures = failures;
                var roots = new List<string>();
                read(roots.Add);
                foreach (var id in roots) buff(id, failures == priorFailures);
            }
            // 0x51B5F0 appends traits; runtime lookup at 0x4833E0 keeps the
            // last matching hash. is_generated only controls the random pool,
            // not application of an already assigned trait.
            Source(add =>
            {
                foreach (var trait in Library("shared/trait", NativeResourceFileRules.IsTraitFile, "traits").Values)
                    foreach (var id in Strings(trait, "buff_ids")) add(id);
            });
            Source(add =>
            {
                if (!trinketsComplete || !heroes.IsComplete) Incomplete("Set admission has incomplete trinket/hero dependencies.");
                ReadSets(heroes, trinkets, add);
            });
            Source(add =>
            {
                if (!heroes.IsComplete) Incomplete("District rewards have incomplete hero dependencies.");
                ReadDistricts(actors, heroes, add);
            });
            return failures == 0;
        }

        private void Incomplete(string reason)
        {
            failures++;
            issues.Add("Effect loot sources could not be verified: " + reason);
        }

        private IReadOnlyList<JsonElement> Documents(string directory,
            Func<string, IReadOnlyList<string>, bool, bool> accepts, bool additive = false)
        {
            var documents = new List<JsonElement>();
            foreach (var file in SourceFiles(directory, "*json", accepts, additive))
            {
                try
                {
                    using var document = JsonDocument.Parse(File.ReadAllBytes(file.Path), new JsonDocumentOptions
                        { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                    if (document.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException("Expected an object.");
                    documents.Add(document.RootElement.Clone());
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
                { Incomplete($"{file.Path}: {error.Message}"); }
            }
            return documents;
        }

        private IReadOnlyList<EffectiveContentFile> SourceFiles(string directory, string pattern,
            Func<string, IReadOnlyList<string>, bool, bool> accepts, bool additive = false)
        {
            var candidates = new List<ContentFileCandidate>();
            foreach (var source in content.Sources)
            {
                if (!Directory.Exists(source.Directory)) Incomplete(source.Directory);
                try
                {
                    var priorIssues = issues.Count;
                    candidates.AddRange(ContentFileDiscovery.EnumerateQuery(source, prefixes, issues, "Effect loot sources",
                        path => accepts(path, prefixes, source.Kind is "local" or "workshop"), new ContentFileRule(directory, pattern))
                        .Select(path => new ContentFileCandidate(source, path)));
                    failures += issues.Count - priorIssues;
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                { Incomplete($"{source.Directory}: {error.Message}"); }
            }
            var resolutionIssues = new List<string>();
            var files = additive
                ? NativeContentFileResolver.ResolveAdditiveFiles(candidates, content.Sources, "Effect loot sources", resolutionIssues)
                : NativeContentFileResolver.Resolve(candidates, content.Sources, "Effect loot sources", resolutionIssues);
            failures += resolutionIssues.Count;
            issues.AddRange(resolutionIssues);
            return files;
        }

        private IEnumerable<JsonElement> Array(JsonElement node, string key, bool required = false)
        {
            if (!NativeJsonReader.TryGetProperty(node, key, out var value))
            {
                if (required) Incomplete($"Missing {key} array.");
                return [];
            }
            if (value.ValueKind == JsonValueKind.Array) return value.EnumerateArray();
            Incomplete($"Invalid {key} array.");
            return [];
        }

        private string Text(JsonElement node, string key, int? bytes = null, bool required = false)
        {
            if (!NativeJsonReader.TryGetProperty(node, key, out var value))
            {
                if (required) Incomplete($"Missing {key} string.");
                return "";
            }
            if (value.ValueKind == JsonValueKind.String) return String(value, bytes);
            Incomplete($"Invalid {key} string.");
            return "";
        }

        private string Bound(string text, int? bytes)
        {
            try { return bytes is { } limit ? NativeJsonReader.BoundedCString(text, limit) : NativeJsonReader.CString(text); }
            catch (Exception error) when (error is EncoderFallbackException or DecoderFallbackException)
            { Incomplete(error.Message); return ""; }
        }

        private string String(JsonElement value, int? bytes)
        {
            // JsonDocument can retain an escaped unpaired surrogate; decoding
            // that string fails later than parsing the document itself.
            try { return Bound(value.GetString()!, bytes); }
            catch (InvalidOperationException error) { Incomplete(error.Message); return ""; }
        }

        private IEnumerable<string> Strings(JsonElement node, string key, int? bytes = null)
        {
            foreach (var value in Array(node, key))
            {
                if (value.ValueKind != JsonValueKind.String) { Incomplete($"Invalid {key} element."); continue; }
                yield return String(value, bytes);
            }
        }

        private Dictionary<uint, JsonElement> Library(string directory,
            Func<string, IReadOnlyList<string>, bool, bool> accepts, string array, int? idBytes = null)
        {
            var result = new Dictionary<uint, JsonElement>();
            foreach (var document in Documents(directory, accepts))
                foreach (var entry in Array(document, array, required: true))
                {
                    var id = Text(entry, "id", idBytes, required: true);
                    if (id.Length > 0) result[NativeResourceIdentity.HashCString(id)] = entry;
                    else Incomplete($"Empty {array} identity.");
                }
            return result;
        }

        private void ReadSets(TrinketHeroDependencies heroes, IReadOnlyDictionary<uint, JsonElement> trinkets, Action<string> buff)
        {
            // 0x501444 replaces the complete hash-keyed set payload, including
            // an omitted/empty Buff list. Entry admission/first-ID selection
            // has already run in the trinket consumer.
            var sets = Library("trinkets", NativeResourceFileRules.IsTrinketSetFile, "sets");
            var members = new Dictionary<uint, List<HashSet<uint>>>();
            foreach (var entry in trinkets.Values)
            {
                var id = NativeResourceIdentity.HashCString(Text(entry, "set_id"));
                if (id == 0) continue; // Native sentinel: no set.
                if (!members.TryGetValue(id, out var list)) members[id] = list = [];
                list.Add(Strings(entry, "hero_class_requirements").Select(NativeResourceIdentity.HashCString).ToHashSet());
            }
            foreach (var (id, set) in sets)
            {
                if (!members.TryGetValue(id, out var list)) continue;
                // 0x481C95 excludes the same trinket identity, then compares
                // set IDs. Two copies of one item cannot activate a set.
                var applicable = heroes.Ids.Any(hero => list.Count(required => required.Count == 0 || required.Contains(hero)) >= 2);
                if (!applicable) continue;
                foreach (var name in Strings(set, "buffs")) buff(name);
            }
        }

        private void ReadDistricts(IReadOnlyList<ScannedContentFile> actors, TrinketHeroDependencies heroes, Action<string> buff)
        {
            var buildings = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var document in Documents("campaign/town/districts", NativeResourceFileRules.IsDistrictFile, additive: true))
            {
                // 0x566293 ORs enabled across files but still loads every
                // building. Existing built districts apply their hero Buffs
                // at 0x5632AB/0x5633F0 without this UI-enable gate.
                foreach (var building in Array(document, "buildings", required: true))
                {
                    // 0x4527B6 bounds the name, strcmp groups it; 0x567030
                    // clears the previous runtime payload before replacing it.
                    var name = Text(building, "name", 127, required: true);
                    if (name.Length > 0) buildings[name] = building;
                    else Incomplete("Empty district building name.");
                }
            }
            var tags = new HashSet<uint>();
            var knownHeroes = new HashSet<uint>();
            foreach (var file in actors.Where(file => file.MountedPath.StartsWith("heroes/", StringComparison.OrdinalIgnoreCase)))
            {
                if (file.ReadFailed) { Incomplete(file.MountedPath); continue; }
                var info = file.MountedPath.EndsWith(".info.darkest", StringComparison.OrdinalIgnoreCase);
                if (!info && !file.MountedPath.EndsWith(".override.darkest", StringComparison.OrdinalIgnoreCase)) continue;
                if (info) knownHeroes.Add(NativeResourceIdentity.HashCString(NativeContentFileResolver.ReadDiscoveredActorId(file.MountedPath)));
                // Hero tags append across info/override declarations (0x4CF09A).
                foreach (var (kind, body) in NativeDarkestReader.ReadRecordsFromText(file.Text))
                    if (kind == "tag") tags.Add(NativeResourceIdentity.HashCString(Bound(NativeDarkestReader.ReadString(body, ".id") ?? "", 63)));
            }
            foreach (var building in buildings.Values)
                foreach (var entry in Array(building, "buff_list"))
                {
                    if (NativeResourceIdentity.HashCString(Text(entry, "type", 511, required: true)) !=
                        NativeResourceIdentity.HashCString("DistrictHeroBuffData")) continue;
                    // Storage tokens are 127 bytes (0x450580); consumers copy
                    // them into 63-byte buffers before tag/Buff hash lookup.
                    var required = Strings(entry, "hero_type_tags", 63).Select(NativeResourceIdentity.HashCString).ToArray();
                    var applicable = heroes.Ids.Count > 0 && (required.Length == 0 || required.Any(tags.Contains));
                    if (!applicable)
                    {
                        if (!heroes.Ids.IsSubsetOf(knownHeroes)) Incomplete("District hero tags have missing canonical definitions.");
                        continue;
                    }
                    foreach (var id in Strings(entry, "buff_list", 63)) buff(id);
                }
        }
    }
}
