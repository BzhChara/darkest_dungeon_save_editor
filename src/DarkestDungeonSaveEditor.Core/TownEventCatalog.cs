using System.Text.Json;

namespace DarkestDungeonSaveEditor.Core;

public static class TownEventCatalog
{
    // Event lookup is first matching native hash (x64 build 27890,
    // 0x14058DB51). File overlays must be resolved before this selection.
    public static TownEventCatalogResult Load(ActiveContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var issues = new List<string>();
        var prefixes = ContentFileOverlay.GetEnabledDlcPrefixes(content.Sources);
        var candidates = new List<ContentFileCandidate>();
        foreach (var source in content.Sources.OrderBy(source => source.LoadOrder))
        {
            var isMod = source.Kind is "workshop" or "local";
            foreach (var path in ContentFileDiscovery.EnumerateQuery(source, prefixes, issues, "Town event",
                         relative => NativeResourceFileRules.IsTownEventFile(relative, isMod ? prefixes : [], isMod),
                         new ContentFileRule("campaign/town_events", "*")))
                candidates.Add(new ContentFileCandidate(source, path));
        }

        var resolutionIssues = new List<string>();
        var files = NativeContentFileResolver.Resolve(candidates, content.Sources, "Town event", resolutionIssues);
        issues.AddRange(resolutionIssues);
        var reads = new OrderedDefinitionReadState<int>(firstMatch: true, orderKnown: resolutionIssues.Count == 0);
        var definitions = new List<TownEventDefinition>();
        foreach (var file in files)
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllBytes(file.Path), new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip
                });
                if (!NativeJsonReader.TryGetProperty(document.RootElement, "events", out var events) ||
                    events.ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException("Missing events array.");

                var parsed = new List<TownEventDefinition>();
                foreach (var entry in events.EnumerateArray())
                {
                    var id = NativeJsonReader.ReadCString(entry, "id");
                    if (id.Length == 0) continue;
                    var effects = NativeJsonReader.Array(entry, "data").Select(effect => new TownEventEffect(
                        NativeJsonReader.ReadBoundedString(effect, "type", 63),
                        NativeJsonReader.ReadCString(effect, "string_data"),
                        NativeJsonReader.ReadFloat(effect, "number_data") ?? 0)).ToArray();
                    parsed.Add(new TownEventDefinition(id, effects, file.Source.Id, file.Path, false));
                }
                foreach (var definition in parsed)
                {
                    definitions.Add(definition);
                    reads.RecordDefinition(definition.NativeId);
                }
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException)
            {
                reads.RecordFailure();
                issues.Add($"Town event file could not be read: {file.Path}: {error.Message}");
            }
        }

        var localization = ContentLocalizationCatalog.Load(content,
            definitions.SelectMany(definition => ContentLocalizationCatalog.GetTownEventKeys(definition.Id)));
        issues.AddRange(localization.Issues);
        var result = definitions.GroupBy(definition => definition.NativeId).Select(group =>
        {
            var winner = group.First();
            return winner with
            {
                HasProviderConflict = !reads.IsVerified(group.Key),
                LocalizedName = localization.GetTownEventName(winner.Id),
                LocalizedDescription = localization.GetTownEventDescription(winner.Id),
                LocalizedSource = ContentSourceLabelFormatter.Create(
                    reads.IsVerified(group.Key) ? winner.Source : "unresolved",
                    group.Select(definition => definition.Source), content.Sources, useDeclaredEffectiveSource: true)
            };
        }).OrderBy(definition => definition.Id, StringComparer.Ordinal).ToArray();
        return new TownEventCatalogResult(result, issues.Distinct(StringComparer.Ordinal).ToArray());
    }
}
