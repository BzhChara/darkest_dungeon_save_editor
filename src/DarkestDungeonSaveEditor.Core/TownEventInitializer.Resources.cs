using System.Text.Json;
using System.Text.Json.Nodes;

namespace DarkestDungeonSaveEditor.Core;

internal sealed partial class TownEventInitializer
{
    private readonly Dictionary<string, JsonObject> _opened = new(StringComparer.Ordinal);

    private JsonObject OpenResource(string relative)
    {
        if (_opened.TryGetValue(relative, out var cached)) return cached;
        var issues = new List<string>();
        var files = NativeContentFileResolver.ResolveOpenedFiles(content.Sources, [relative], "Town event dependency", issues);
        if (issues.Count > 0 || files.Count != 1)
            throw new InvalidDataException($"Town event dependency is unresolved: {relative}. {string.Join("; ", issues)}");
        return _opened[relative] = ReadResource(files[0].Path);
    }

    private IEnumerable<JsonObject> ReadResources(string directory, NativeReferenceJsonKind kind)
        => ReadMatchingResources(directory, (path, isMod, prefixes) =>
            NativeResourceFileRules.ReferenceJsonKind(path, isMod ? prefixes : [], isMod) == kind);

    private IEnumerable<JsonObject> ReadMatchingResources(string directory, string nativeNamePattern)
        => ReadMatchingResources(directory, (path, isMod, prefixes) =>
            NativeResourceFileRules.IsInDirectory(path, directory, isMod ? prefixes : [], isMod) &&
            System.Text.RegularExpressions.Regex.IsMatch(path.Replace('\\', '/'), nativeNamePattern + "\\z",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant));

    private IEnumerable<JsonObject> ReadMatchingResources(string directory,
        Func<string, bool, IReadOnlyList<string>, bool> matches)
    {
        var issues = new List<string>();
        var candidates = new List<ContentFileCandidate>();
        var prefixes = ContentFileOverlay.GetEnabledDlcPrefixes(content.Sources);
        foreach (var source in content.Sources)
        {
            var isMod = source.Kind is "workshop" or "local";
            candidates.AddRange(ContentFileDiscovery.EnumerateQuery(source, prefixes, issues, "Town event dependency",
                path => matches(path, isMod, prefixes),
                new ContentFileRule(directory, "*json")).Select(path => new ContentFileCandidate(source, path)));
        }
        var files = NativeContentFileResolver.Resolve(candidates, content.Sources, "Town event dependency", issues);
        if (issues.Count > 0) throw new InvalidDataException(string.Join("; ", issues));
        return files.Select(file => ReadResource(file.Path)).ToArray();
    }

    private static JsonObject ReadResource(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(path), new JsonDocumentOptions
        {
            AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip
        });
        return FirstMembers(document.RootElement) as JsonObject ?? throw new InvalidDataException($"Invalid resource: {path}.");
    }

    private static JsonNode? FirstMembers(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var result = new JsonObject();
            foreach (var member in element.EnumerateObject())
                if (!result.ContainsKey(member.Name)) result[member.Name] = FirstMembers(member.Value);
            return result;
        }
        if (element.ValueKind == JsonValueKind.Array)
            return new JsonArray(element.EnumerateArray().Select(FirstMembers).ToArray());
        return JsonNode.Parse(element.GetRawText());
    }

    private static IEnumerable<JsonObject> Objects(JsonNode? node) => node switch
    {
        JsonArray array => array.OfType<JsonObject>(),
        JsonObject map => map.Select(pair => pair.Value).OfType<JsonObject>(),
        _ => []
    };

    private static IEnumerable<JsonObject> RosterHeroes(IReadOnlyDictionary<string, JsonObject> saves) =>
        JsonSupport.RequireObject(TownEventSaveEditor.Root(saves, "roster"), "heroes").Select(pair =>
            pair.Value?["hero_file_data"]?["raw_data"]?["base_root"] as JsonObject
            ?? throw new InvalidDataException("Missing roster hero data."));

    private static bool Purchased(IReadOnlyDictionary<string, JsonObject> saves, JsonObject upgrade)
    {
        var tree = JsonSupport.ReadRawString(upgrade, "upgrade_tree_id");
        if (tree.Length == 0) return true;
        var code = JsonSupport.ReadRawString(upgrade, "upgrade_requirement_code");
        return Objects(TownEventSaveEditor.Root(saves, "upgrades")["purchases"]).Any(purchase =>
            JsonSupport.ReadInt(purchase, "instance_number") == 0 &&
            JsonSupport.ReadInt(purchase, "tree_id") == unchecked((int)Hash(tree)) &&
            JsonSupport.ReadRawString(purchase, "requirement_code") == code &&
            purchase["is_purchased"]?.GetValue<bool>() == true);
    }

    private void UnlockDistricts(IReadOnlyDictionary<string, JsonObject> saves, List<string> changes)
    {
        var town = TownEventSaveEditor.Root(saves, "town");
        if (town["districts"] is JsonObject) return;
        // Districts::Load sets the persistent unlock flag from the presence of
        // this block. Missing per-building entries load as unbuilt defaults.
        town["districts"] = new JsonObject { ["buildings"] = new JsonObject() };
        changes.Add(EditorText.Get("TownEvent_DistrictsUnlocked"));
    }
}
