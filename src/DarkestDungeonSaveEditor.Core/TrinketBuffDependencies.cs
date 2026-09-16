using System.Text.Json;

namespace DarkestDungeonSaveEditor.Core;

// Ordinary trinket Buff references only ask whether the native hash exists.
// Attribute selection (last whole Buff definition) remains the Buff consumer's job.
internal sealed record TrinketBuffDependencies(
    IReadOnlySet<uint> Ids, bool IsComplete, bool HasUnresolvedProviders)
{
    internal bool? Contains(string id) => HasUnresolvedProviders ? null
        : Ids.Contains(Loc2LocalizationReader.HashName(NativeJsonReader.CString(id))) ? true
        : IsComplete ? false : null;

    internal static TrinketBuffDependencies Load(ActiveContentSnapshot content, List<string> issues)
    {
        var prefixes = ContentFileOverlay.GetEnabledDlcPrefixes(content.Sources);
        var candidates = new List<ContentFileCandidate>();
        var complete = true;
        var unresolvedProviders = ActiveContentResolver.HasUnresolvedModSources(content);
        if (unresolvedProviders)
            issues.Add("Trinket Buff provider discovery is incomplete: enabled Mods could not all be resolved.");
        foreach (var source in content.Sources)
        {
            try
            {
                if (!Directory.Exists(source.Directory)) unresolvedProviders = true;
                candidates.AddRange(ContentFileDiscovery.EnumerateQuery(source, prefixes, issues, "Trinket Buff",
                    path => NativeResourceFileRules.IsBuffFile(path, prefixes, source.Kind is "local" or "workshop"),
                    new ContentFileRule("shared/buffs", "*json"))
                    .Select(path => new ContentFileCandidate(source, path)));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                complete = false;
                unresolvedProviders = true;
                issues.Add($"Failed to enumerate trinket Buff files '{source.Directory}': {error.Message}");
            }
        }

        var resolutionIssues = new List<string>();
        var files = NativeContentFileResolver.Resolve(candidates, content.Sources, "Trinket Buff", resolutionIssues);
        issues.AddRange(resolutionIssues);
        var ids = new HashSet<uint>();
        foreach (var file in files)
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllBytes(file.Path), new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip
                });
                foreach (var buff in NativeJsonReader.Array(document.RootElement, "buffs"))
                    if (NativeJsonReader.TryGetProperty(buff, "id", out var id) && id.ValueKind == JsonValueKind.String)
                        ids.Add(Loc2LocalizationReader.HashName(NativeJsonReader.CString(id.GetString()!)));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
            {
                complete = false;
                issues.Add($"Failed to read trinket Buff definitions '{file.Path}': {error.Message}");
            }
        }
        // Missing/failed effective files cannot establish absence. A shadowed
        // provider's failure does not matter: only resolved winners are opened.
        return new(ids, complete, unresolvedProviders || resolutionIssues.Count > 0);
    }
}
