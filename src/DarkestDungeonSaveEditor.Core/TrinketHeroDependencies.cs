namespace DarkestDungeonSaveEditor.Core;

// Required classes are checked against discovered actor IDs, not the subset
// whose canonical info files supply usable hero-generation templates.
internal sealed record TrinketHeroDependencies(IReadOnlySet<uint> Ids, bool IsComplete)
{
    internal bool? Contains(string id) => Ids.Contains(NativeResourceIdentity.HashCString(id)) ? true
        : IsComplete ? false : null;

    internal static TrinketHeroDependencies Load(ActiveContentSnapshot content, List<string> issues)
    {
        var prefixes = ContentFileOverlay.GetEnabledDlcPrefixes(content.Sources);
        var complete = !ActiveContentResolver.HasUnresolvedModSources(content);
        var ids = new HashSet<uint>();
        foreach (var source in content.Sources)
        {
            if (!Directory.Exists(source.Directory)) complete = false;
            try
            {
                foreach (var path in NativeContentFileResolver.EnumerateActorInfoFiles(source, prefixes, "heroes", issues))
                    ids.Add(NativeResourceIdentity.HashCString(NativeContentFileResolver.ReadDiscoveredActorId(
                        Path.GetRelativePath(source.Directory, path))));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                complete = false;
                issues.Add($"Failed to discover trinket hero dependencies '{source.Directory}': {error.Message}");
            }
        }
        if (!complete) issues.Add("Trinket hero provider discovery is incomplete; missing references could not be verified.");
        return new(ids, complete);
    }
}
