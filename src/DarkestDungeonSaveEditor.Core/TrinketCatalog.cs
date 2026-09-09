using System.Text.Json;

namespace DarkestDungeonSaveEditor.Core;

public static class TrinketCatalog
{
    private static readonly string[] StatefulFieldNames =
    [
        "quest_uses",
        "quest_progressive_art",
        "trigger_limit",
        "trigger_progressive_art",
        "transform_on_trigger_limit_exhausted",
        "destroy_on_triggers_exhausted",
        "blocks_other_slot_trigger_expend",
        "blocks_other_slot_quest_uses_expend",
        "on_quest_complete_additional_effects"
    ];

    public static TrinketCatalogResult Load(ActiveContentSnapshot activeContent)
    {
        ArgumentNullException.ThrowIfNull(activeContent);
        var issues = new List<string>();
        var definitions = new Dictionary<string, List<TrinketDefinition>>(StringComparer.Ordinal);
        var fileCandidates = new List<ContentFileCandidate>();
        var heroIds = NativeContentFileResolver.DiscoverActorIds(activeContent.Sources, "heroes", issues);
        var enabledDlcPrefixes = ContentFileOverlay.GetEnabledDlcPrefixes(activeContent.Sources);
        foreach (var source in activeContent.Sources.OrderBy(item => item.LoadOrder))
        {
            var scanRoot = source.Kind is "base" or "mode" or "dlc" or "dlc-package" or "dlc-feature"
                ? Path.Combine(source.Directory, "trinkets")
                : source.Directory;
            foreach (var path in EnumerateTrinketFiles(
                         scanRoot,
                         source.Kind is "workshop" or "local",
                         enabledDlcPrefixes,
                         issues))
            {
                fileCandidates.Add(new ContentFileCandidate(source, path));
            }
        }

        foreach (var file in NativeContentFileResolver.Resolve(fileCandidates, activeContent.Sources, "Trinket definition", issues))
        {
            ScanFile(file, heroIds, definitions, issues);
        }

        var localization = ContentLocalizationCatalog.Load(
            activeContent,
            definitions.Keys.SelectMany(ContentLocalizationCatalog.GetTrinketKeys));
        issues.AddRange(localization.Issues);

        var collisionIds = NativeResourceIdentity.FindCollisions(definitions.Values.SelectMany(group => group).Select(item => item.Id));
        if (collisionIds.Count > 0) issues.Add("Trinket IDs share native hashes and cannot be selected safely: " + string.Join(", ", collisionIds));
        var merged = definitions.Values
            .Select(MergeDefinitions)
            .Select(definition => collisionIds.Contains(definition.Id)
                ? definition with { HasProviderConflict = true, Source = "unresolved" } : definition)
            .Select(definition => definition with
            {
                LocalizedName = localization.GetTrinketName(definition.Id),
                SourceLabel = ContentSourceLabelFormatter.Format(
                    definition.Source,
                    definition.AllSources,
                    activeContent.Sources)
            })
            .OrderBy(definition => definition.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var storageCatalog = TrinketStorageCatalog.Load(activeContent);
        issues.AddRange(storageCatalog.Issues);
        return new TrinketCatalogResult(
            merged,
            storageCatalog.Storage,
            issues.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static void ScanFile(
        EffectiveContentFile file,
        IReadOnlySet<string> heroIds,
        Dictionary<string, List<TrinketDefinition>> definitions,
        List<string> issues)
    {
        var path = file.Path;
        try
        {
            var providerSourcesById = ReadProviderSourcesByTrinketId(file);
            using var document = JsonDocument.Parse(
                File.ReadAllBytes(path),
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip
                });
            if (!document.RootElement.TryGetProperty("entries", out var entries) ||
                entries.ValueKind != JsonValueKind.Array)
            {
                issues.Add($"Trinket file has no entries array: {path}");
                return;
            }

            foreach (var entry in entries.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object ||
                    !entry.TryGetProperty("id", out var idNode) ||
                    idNode.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var id = idNode.GetString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                // Native loader 0x1404F39A0 checks every required class and
                // skips this entry when any class is absent from the actor table.
                if (entry.TryGetProperty("hero_class_requirements", out var requirements) &&
                    requirements.ValueKind == JsonValueKind.Array &&
                    requirements.EnumerateArray().Any(value => value.ValueKind != JsonValueKind.String ||
                        !heroIds.Contains(value.GetString()!)))
                {
                    issues.Add($"Trinket '{id}' requires an unloaded hero class and was ignored: {path}");
                    continue;
                }

                var providerSources = providerSourcesById.TryGetValue(id, out var declaringSources)
                    ? declaringSources
                    : [file.Source.Id];

                var statefulFields = StatefulFieldNames
                    .Where(name => entry.TryGetProperty(name, out _))
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var unsupportedStateFields = new List<string>();
                var questUses = ReadPositiveInstanceCounter(
                    entry,
                    "quest_uses",
                    id,
                    path,
                    unsupportedStateFields,
                    issues);
                var triggerLimit = ReadPositiveInstanceCounter(
                    entry,
                    "trigger_limit",
                    id,
                    path,
                    unsupportedStateFields,
                    issues);
                var definition = new TrinketDefinition(
                    id,
                    ReadString(entry, "rarity"),
                    ReadInt(entry, "limit"),
                    ReadInt(entry, "price"),
                    file.Source.Id,
                    Path.GetFullPath(path),
                    statefulFields.Length > 0,
                    statefulFields,
                    false,
                    providerSources)
                {
                    QuestUses = questUses,
                    TriggerLimit = triggerLimit,
                    UnsupportedStateFields = unsupportedStateFields.ToArray()
                };

                if (!definitions.TryGetValue(id, out var candidates))
                {
                    candidates = [];
                    definitions[id] = candidates;
                }

                candidates.Add(definition);
            }
        }
        catch (Exception ex)
        {
            issues.Add($"Failed to read trinket file '{path}': {ex.Message}");
        }
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> ReadProviderSourcesByTrinketId(
        EffectiveContentFile file)
    {
        var sourcesById = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var provider in file.Providers)
        {
            try
            {
                foreach (var id in ReadTrinketIds(provider.Path))
                {
                    if (!sourcesById.TryGetValue(id, out var sources))
                    {
                        sources = [];
                        sourcesById[id] = sources;
                    }

                    if (!sources.Contains(provider.SourceId, StringComparer.OrdinalIgnoreCase))
                    {
                        sources.Add(provider.SourceId);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                // Provenance is optional evidence. The effective file is parsed separately below;
                // if an overridden provider cannot be inspected, fail closed by not attributing
                // its origin to entries from the winning file.
            }
        }

        return sourcesById.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)pair.Value.ToArray(),
            StringComparer.Ordinal);
    }

    private static IEnumerable<string> ReadTrinketIds(string path)
    {
        using var document = JsonDocument.Parse(
            File.ReadAllBytes(path),
            new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });
        if (!document.RootElement.TryGetProperty("entries", out var entries) ||
            entries.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var entry in entries.EnumerateArray())
        {
            if (entry.ValueKind == JsonValueKind.Object &&
                entry.TryGetProperty("id", out var idNode) &&
                idNode.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(idNode.GetString()))
            {
                yield return idNode.GetString()!;
            }
        }
    }

    private static IReadOnlyList<string> EnumerateTrinketFiles(
        string root,
        bool useModManifest,
        IReadOnlyList<string> enabledDlcPrefixes,
        List<string> issues)
    {
        if (!Directory.Exists(root))
        {
            return [];
        }

        if (!useModManifest)
        {
            return NativeDirectoryDiscovery.EnumerateFiles(root, "*.entries.trinkets.json", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        var manifestPath = Path.Combine(root, "modfiles.txt");
        ModManifestFile.Require(manifestPath);

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in ModManifestFile.ReadEntries(manifestPath, ".entries.trinkets.json"))
        {
            var rawLine = entry.RawLine;
            var relativePath = entry.RelativePath;

            var path = Path.GetFullPath(Path.Combine(root, relativePath));
            var relativeToRoot = Path.GetRelativePath(root, path);
            if (Path.IsPathRooted(relativeToRoot) ||
                relativeToRoot.Equals("..", StringComparison.Ordinal) ||
                relativeToRoot.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                issues.Add($"Ignored trinket manifest path outside its Mod directory: {rawLine.Trim()}");
                continue;
            }

            if (!ContentFileOverlay.IsRootOrEnabledDlcPath(
                    relativeToRoot,
                    "trinkets",
                    enabledDlcPrefixes))
            {
                continue;
            }

            if (!File.Exists(path))
            {
                issues.Add($"Trinket file listed by Mod is missing: {path}");
                continue;
            }

            result.Add(path);
        }

        return result.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static TrinketDefinition MergeDefinitions(IReadOnlyList<TrinketDefinition> candidates)
    {
        // 0x1404F6480: lookup stops at the first ID hash in the loaded vector.
        // Keep instance fields from that entry only; later definitions do not
        // turn an ordinary trinket into a consumable/stateful one.
        var winner = candidates[0];
        return winner with
        {
            AllSources = candidates.SelectMany(candidate => candidate.AllSources)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }

    private static string ReadString(JsonElement value, string name)
    {
        return value.TryGetProperty(name, out var node) && node.ValueKind == JsonValueKind.String
            ? node.GetString()?.Trim() ?? string.Empty
            : string.Empty;
    }

    private static int? ReadInt(JsonElement value, string name)
    {
        return value.TryGetProperty(name, out var node) && node.TryGetInt32(out var result)
            ? result
            : null;
    }

    private static int? ReadPositiveInstanceCounter(
        JsonElement entry,
        string fieldName,
        string trinketId,
        string sourcePath,
        List<string> unsupportedStateFields,
        List<string> issues)
    {
        if (!entry.TryGetProperty(fieldName, out var node))
        {
            return null;
        }

        if (node.ValueKind == JsonValueKind.Number &&
            node.TryGetInt32(out var value) &&
            value > 0)
        {
            return value;
        }

        unsupportedStateFields.Add(fieldName);
        issues.Add(
            $"Trinket '{trinketId}' has an invalid {fieldName} value in '{sourcePath}'; " +
            "pristine instance creation is disabled for this definition.");
        return null;
    }
}
