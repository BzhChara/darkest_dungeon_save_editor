using System.Text;
using System.Text.Json;

namespace DarkestDungeonSaveEditor.Core;

internal static partial class QuantityItemReferenceAnalyzer
{
    private sealed class LootEffect
    {
        public string Loot { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public NativeEffectReferences.Links Links { get; } = new();
    }

    private static bool ParseEffectLootReferences(ActiveContentSnapshot content, IReadOnlyList<ScannedContentFile> rootFiles,
        Dictionary<string, List<string>> lootRoots, Dictionary<string, List<string>> uncertainRoots, List<string> issues)
    {
        var prefixes = ContentFileOverlay.GetEnabledDlcPrefixes(content.Sources);
        var graphIssues = new List<string>();
        var complete = true;
        var foundLoot = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var unverifiedLoot = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        IReadOnlyList<EffectiveContentFile> Files(string directory, string pattern,
            Func<string, IReadOnlyList<string>, bool, bool> accepts, bool effects = false)
        {
            var candidates = new List<ContentFileCandidate>();
            foreach (var source in content.Sources)
            {
                if (!Directory.Exists(source.Directory)) complete = false;
                try
                {
                    var priorIssues = graphIssues.Count;
                    candidates.AddRange(ContentFileDiscovery.EnumerateQuery(source, prefixes, graphIssues, "Effect loot",
                        path => accepts(path, prefixes, source.Kind is "local" or "workshop"), new ContentFileRule(directory, pattern))
                        .Select(path => new ContentFileCandidate(source, path)));
                    complete &= graphIssues.Count == priorIssues;
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                { complete = false; graphIssues.Add($"Effect loot discovery failed: {source.Directory}: {error.Message}"); }
            }
            var resolutionIssues = new List<string>();
            var result = effects ? NativeContentFileResolver.ResolveEffectFiles(candidates, content.Sources, "Effect loot", resolutionIssues)
                : NativeContentFileResolver.Resolve(candidates, content.Sources, "Effect loot", resolutionIssues);
            complete &= resolutionIssues.Count == 0;
            graphIssues.AddRange(resolutionIssues);
            return result;
        }
        void Read(EffectiveContentFile file, Action action)
        {
            try { action(); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or EncoderFallbackException or DecoderFallbackException)
            { complete = false; graphIssues.Add($"Effect loot references could not be read: {file.Path}: {error.Message}"); }
        }
        Dictionary<uint, JsonElement> JsonLibrary(string directory, Func<string, IReadOnlyList<string>, bool, bool> accepts,
            string arrayName, bool first, out bool libraryComplete, Func<JsonElement, bool>? admitted = null,
            List<JsonElement>? declarations = null)
        {
            var priorComplete = complete;
            complete = true;
            var entries = new Dictionary<uint, JsonElement>();
            foreach (var file in Files(directory, "*json", accepts)) Read(file, () =>
            {
                using var doc = JsonDocument.Parse(File.ReadAllBytes(file.Path), new JsonDocumentOptions
                    { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                if (!NativeJsonReader.TryGetProperty(doc.RootElement, arrayName, out var array) || array.ValueKind != JsonValueKind.Array)
                    throw new JsonException($"Missing {arrayName} array.");
                foreach (var entry in array.EnumerateArray())
                {
                    var id = NativeJsonReader.ReadCString(entry, "id");
                    if (id.Length == 0) continue;
                    if (admitted is not null && !admitted(entry)) continue;
                    var hash = NativeResourceIdentity.HashCString(id);
                    var definition = entry.Clone();
                    declarations?.Add(definition);
                    if (!first || !entries.ContainsKey(hash)) entries[hash] = definition;
                }
            });
            libraryComplete = complete;
            complete &= priorComplete;
            return entries;
        }
        static IEnumerable<string> Ids(JsonElement node, string key) => NativeJsonReader.Array(node, key)
            .Where(value => value.ValueKind == JsonValueKind.String).Select(value => NativeJsonReader.CString(value.GetString()!));
        var queue = new Queue<(bool Buff, uint Id, bool Combat, bool Verified)>();
        void Buff(string id, bool verified = true) { if (id.Length > 0) queue.Enqueue((true, NativeResourceIdentity.HashCString(id), true, verified)); }
        void Reference(NativeEffectReferences.Reference reference, bool verified = true)
        {
            if (reference.Id.Length > 0)
                queue.Enqueue((reference.Buff, NativeResourceIdentity.HashCString(reference.Id), reference.Buff || reference.Combat, verified));
        }
        var effects = new Dictionary<uint, LootEffect>();
        foreach (var file in Files("effects", "*darkest", NativeResourceFileRules.IsEffectFile, effects: true)) Read(file, () =>
        {
            foreach (var (kind, body) in NativeDarkestReader.ReadRecords(file.Path))
            {
                if (kind != "effect") continue;
                var id = NativeJsonReader.BoundedCString(NativeDarkestReader.ReadString(body, ".name") ?? "", 63);
                if (id.Length == 0) continue;
                var hash = NativeResourceIdentity.HashCString(id);
                if (!effects.TryGetValue(hash, out var effect)) effects[hash] = effect = new();
                // RVA 0x4F0D38 clears this buffer before every record, including
                // duplicate IDs. Omitted and explicit empty gain_loot both clear.
                effect.Loot = NativeJsonReader.BoundedCString(NativeDarkestReader.ReadString(body, ".gain_loot") ?? "", 31);
                effect.Source = file.RelativePath;
                effect.Links.Append(NativeEffectReferences.Links.Read(body));
            }
        });
        // No gain_loot exists in readable effective content: retain the prior
        // consumer result unless unreadable definitions could contain one.
        if (complete && effects.Values.All(effect => effect.Loot.Length == 0))
        {
            issues.AddRange(graphIssues);
            return true;
        }
        // Preserve the existing quantity policy when no effective gain_loot
        // participates. An unresolved Mod can invalidate an observed graph,
        // but must not change unrelated legacy-only catalogs.
        complete &= !ActiveContentResolver.HasUnresolvedModSources(content);
        var buffs = JsonLibrary("shared/buffs", NativeResourceFileRules.IsBuffFile, "buffs", first: false, out var buffsComplete);
        var sharedComplete = complete;
        var heroes = TrinketHeroDependencies.Load(content, graphIssues);
        complete &= heroes.IsComplete;
        var quirkDeclarations = new List<JsonElement>();
        _ = JsonLibrary("shared/quirk", NativeResourceFileRules.IsQuirkFile, "quirks", first: false,
            out var quirksComplete, declarations: quirkDeclarations);
        var quirkGroups = quirkDeclarations.GroupBy(entry =>
            NativeResourceIdentity.HashCString(NativeJsonReader.ReadCString(entry, "id"))).ToArray();
        static bool RandomQuirk(JsonElement entry) => NativeJsonReader.TryGetProperty(entry, "random_chance", out var chance) &&
            chance.ValueKind == JsonValueKind.Number && chance.TryGetSingle(out var value) && value > 0 && float.IsFinite(value);
        // Random acquisition returns the actual definition pointer. ID-based
        // save/effect/evolution lookups select the last object instead.
        var obtainableQuirkActOuts = quirkGroups.Where(group => group.Count() == 1 || RandomQuirk(group.First()))
            .Select(group => group.Key).ToHashSet();
        var townEvents = JsonLibrary("campaign/town_events", NativeResourceFileRules.IsTownEventFile, "events", first: true, out var eventsComplete);
        var trinkets = JsonLibrary("trinkets", NativeResourceFileRules.IsTrinketFile, "entries", first: true, out var trinketsComplete, entry =>
        {
            // Native admission precedes first-ID selection. A discarded earlier
            // entry must not shadow a later valid one.
            if (NativeJsonReader.Array(entry, "hero_class_requirements").Any(value =>
                    value.ValueKind != JsonValueKind.String || heroes.Contains(value.GetString()!) == false)) return false;
            var required = NativeJsonReader.Array(entry, "buffs").ToArray();
            if (required.Any(value => value.ValueKind != JsonValueKind.String)) complete = false;
            return !buffsComplete || !Ids(entry, "buffs").Any(id => !buffs.ContainsKey(NativeResourceIdentity.HashCString(id)));
        });
        var availableCamping = new HashSet<uint>();
        var camping = JsonLibrary("raid/camping", NativeResourceFileRules.IsCampingSkillFile, "skills", first: true, out var campingComplete, skill =>
        {
            // Each declaration grants class access, but runtime lookup uses the
            // first stored skill record (the same rule as HeroClassCatalog).
            if (Ids(skill, "hero_classes").Any(id => heroes.Contains(id) != false))
                availableCamping.Add(NativeResourceIdentity.HashCString(NativeJsonReader.ReadCString(skill, "id")));
            return true;
        });
        foreach (var file in rootFiles.Where(file => !file.ReadFailed &&
                     (file.MountedPath.EndsWith(".info.darkest", StringComparison.OrdinalIgnoreCase) ||
                      file.MountedPath.EndsWith(".override.darkest", StringComparison.OrdinalIgnoreCase)) &&
                     (file.MountedPath.StartsWith("heroes/", StringComparison.OrdinalIgnoreCase) ||
                      file.MountedPath.StartsWith("monsters/", StringComparison.OrdinalIgnoreCase))))
        {
            try
            {
                var hero = file.MountedPath.StartsWith("heroes/", StringComparison.OrdinalIgnoreCase);
                foreach (var (kind, body) in NativeDarkestReader.ReadRecordsFromText(file.Text))
                {
                    foreach (var reference in NativeEffectReferences.Actor(kind, body, hero)) Reference(reference);
                    foreach (var reference in NativeEffectReferences.UnverifiedActor(kind, body, hero)) Reference(reference, verified: false);
                }
            }
            catch (Exception error) when (error is IOException or EncoderFallbackException or DecoderFallbackException)
            {
                complete = false;
                graphIssues.Add($"Actor Effect references could not be read: {file.MountedPath}: {error.Message}");
            }
        }
        foreach (var entry in quirkGroups.SelectMany(group => group.Where(RandomQuirk).Append(group.Last())))
            foreach (var id in Ids(entry, "buffs")) Buff(id, quirksComplete);
        foreach (var townEvent in townEvents.Values)
            foreach (var entry in NativeJsonReader.Array(townEvent, "data"))
                if (NativeResourceIdentity.HashCString(NativeJsonReader.ReadBoundedString(entry, "type", 63)) ==
                    NativeResourceIdentity.HashCString("embark_party_buff"))
                    Buff(NativeJsonReader.ReadCString(entry, "string_data"), eventsComplete);
        foreach (var entry in trinkets.Values)
        {
            foreach (var id in Ids(entry, "buffs")) Buff(id, trinketsComplete && heroes.IsComplete);
            foreach (var reference in NativeEffectReferences.TrinketTriggers(entry)) Reference(reference, trinketsComplete && heroes.IsComplete);
        }
        // A failed source library prevents an unused-item verdict, but cannot
        // invalidate independent roots. Each new source family carries its
        // own certainty; shared Effect/Buff provider failures still affect all.
        var sources = new LootBuffSources(content, graphIssues);
        var sourcesComplete = sources.Read(rootFiles, heroes, trinkets, trinketsComplete, Buff);
        sourcesComplete &= sources.ReadRuntime(rootFiles, heroes, obtainableQuirkActOuts, quirksComplete, buffs, Reference);
        foreach (var skill in camping.Values)
        {
            if (!availableCamping.Contains(NativeResourceIdentity.HashCString(NativeJsonReader.ReadCString(skill, "id")))) continue;
            foreach (var reference in NativeEffectReferences.Camping(skill)) Reference(reference, campingComplete && heroes.IsComplete);
        }
        var visited = new HashSet<(bool Buff, uint Id, bool Combat, bool Verified)>();
        while (queue.TryDequeue(out var next))
        {
            if (!visited.Add(next)) continue;
            if (next.Buff)
            {
                if (buffs.TryGetValue(next.Id, out var buff))
                    foreach (var reference in NativeEffectReferences.BuffTriggers(buff)) Reference(reference, next.Verified);
            }
            else if (effects.TryGetValue(next.Id, out var effect))
            {
                if (next.Combat && effect.Loot.Length > 0)
                    AddEvidence(next.Verified ? foundLoot : unverifiedLoot, effect.Loot,
                        $"{effect.Source}: gain_loot {effect.Loot}" + (next.Verified ? "" : " (unverified source or activation)"));
                foreach (var reference in effect.Links.Targets()) Reference(reference, next.Verified);
            }
        }
        issues.AddRange(graphIssues);
        foreach (var pair in foundLoot)
            foreach (var evidence in pair.Value) AddEvidence(sharedComplete ? lootRoots : uncertainRoots, pair.Key, evidence);
        foreach (var pair in unverifiedLoot)
            foreach (var evidence in pair.Value) AddEvidence(uncertainRoots, pair.Key, evidence);
        return complete && sourcesComplete;
    }
}
