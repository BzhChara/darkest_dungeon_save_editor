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
        var heroIds = NativeContentFileResolver.DiscoverActorIds(activeContent.Sources, "heroes", issues)
            .Select(id => Loc2LocalizationReader.HashName(NativeJsonReader.CString(id))).ToHashSet();
        var buffs = TrinketBuffDependencies.Load(activeContent, issues);
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
            ScanFile(file, heroIds, buffs, definitions, issues);
        }

        var localization = ContentLocalizationCatalog.Load(
            activeContent,
            definitions.Keys.SelectMany(ContentLocalizationCatalog.GetTrinketKeys));
        issues.AddRange(localization.Issues);

        var collisionIds = NativeResourceIdentity.FindCollisions(definitions.Values.SelectMany(group => group),
            item => item.Id, item => Loc2LocalizationReader.HashName(NativeJsonReader.CString(item.Id)));
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
        foreach (var definition in merged.Where(item => item.SaveIdentityIssue.Length > 0))
            issues.Add($"Trinket '{definition.Id}' ({definition.SourcePath}): {definition.SaveIdentityIssue}");
        var storageCatalog = TrinketStorageCatalog.Load(activeContent);
        issues.AddRange(storageCatalog.Issues);
        return new TrinketCatalogResult(
            merged,
            storageCatalog.Storage,
            issues.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static void ScanFile(
        EffectiveContentFile file,
        IReadOnlySet<uint> heroIds,
        TrinketBuffDependencies buffs,
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
            if (!NativeJsonReader.TryGetProperty(document.RootElement, "entries", out var entries) ||
                entries.ValueKind != JsonValueKind.Array)
            {
                issues.Add($"Trinket file has no entries array: {path}");
                return;
            }

            foreach (var entry in entries.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object ||
                    !NativeJsonReader.TryGetProperty(entry, "id", out var idNode) ||
                    idNode.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var id = idNode.GetString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                // Native references use full C-string hashes, not literal names.
                // Every required class must be present in the actor table.
                if (NativeJsonReader.TryGetProperty(entry, "hero_class_requirements", out var requirements) &&
                    requirements.ValueKind == JsonValueKind.Array &&
                    requirements.EnumerateArray().Any(value => value.ValueKind != JsonValueKind.String ||
                        !heroIds.Contains(Loc2LocalizationReader.HashName(NativeJsonReader.CString(value.GetString()!)))))
                {
                    issues.Add($"Trinket '{id}' requires an unloaded hero class and was ignored: {path}");
                    continue;
                }

                // The native loader discards entries with missing ordinary Buffs
                // before first-ID selection. An unreadable dependency is unknown,
                // so retain the candidate as read-only instead of choosing a later ID.
                var buffStates = NativeJsonReader.Array(entry, "buffs")
                    .Select(value => value.ValueKind == JsonValueKind.String ? buffs.Contains(value.GetString()!) : null)
                    .ToArray();
                if (buffStates.Contains(false))
                {
                    issues.Add($"Trinket '{id}' references a missing Buff and was ignored: {path}");
                    continue;
                }
                var unresolvedBuffs = buffStates.Contains(null);
                if (unresolvedBuffs)
                    issues.Add($"Trinket '{id}' Buff dependencies could not be verified and are read-only: {path}");

                var providerSources = providerSourcesById.TryGetValue(id, out var declaringSources)
                    ? declaringSources
                    : [file.Source.Id];

                var statefulFields = StatefulFieldNames
                    .Where(name => NativeJsonReader.TryGetProperty(entry, name, out _))
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                // 0x1404F3D7D / 0x1404F4012: optional signed Int32 fields.
                // A wrong-typed first member leaves the native default (-1);
                // zero is a real count, and later duplicate members are ignored.
                var questUses = ReadInt(entry, "quest_uses");
                var triggerLimit = ReadInt(entry, "trigger_limit");
                var definition = new TrinketDefinition(
                    id,
                    ReadString(entry, "rarity"),
                    ReadInt(entry, "limit"),
                    ReadInt(entry, "price"),
                    unresolvedBuffs ? "unresolved" : file.Source.Id,
                    Path.GetFullPath(path),
                    statefulFields.Length > 0,
                    statefulFields,
                    unresolvedBuffs,
                    providerSources)
                {
                    QuestUses = questUses,
                    TriggerLimit = triggerLimit
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
        if (!NativeJsonReader.TryGetProperty(document.RootElement, "entries", out var entries) ||
            entries.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var entry in entries.EnumerateArray())
        {
            if (entry.ValueKind == JsonValueKind.Object &&
                NativeJsonReader.TryGetProperty(entry, "id", out var idNode) &&
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
            return NativeDirectoryDiscovery.EnumerateFiles(root, "*json", SearchOption.AllDirectories)
                .Where(path => NativeResourceFileRules.IsTrinketFile("trinkets/" + Path.GetRelativePath(root, path), []))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        var manifestPath = Path.Combine(root, "modfiles.txt");
        ModManifestFile.Require(manifestPath);

        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in ModManifestFile.ReadEntries(manifestPath, "json"))
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

            if (!NativeResourceFileRules.IsTrinketFile(relativeToRoot, enabledDlcPrefixes, manifestDirectory: true))
            {
                continue;
            }

            if (!File.Exists(path))
            {
                issues.Add($"Trinket file listed by Mod is missing: {path}");
            }

            // Resolve the winning manifest entry before attempting to read its bytes.
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
        return NativeJsonReader.TryGetProperty(value, name, out var node) && node.ValueKind == JsonValueKind.String
            ? node.GetString()?.Trim() ?? string.Empty
            : string.Empty;
    }

    private static int? ReadInt(JsonElement value, string name)
    {
        return NativeJsonReader.TryGetProperty(value, name, out var node) &&
               node.ValueKind == JsonValueKind.Number && node.TryGetInt32(out var result)
            ? result
            : null;
    }

}
