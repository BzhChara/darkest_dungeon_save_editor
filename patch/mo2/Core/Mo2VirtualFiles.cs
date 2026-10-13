using System.IO.Enumeration;
using System.Text;
using System.Text.Json;

namespace DarkestDungeonSaveEditor.Core;

internal sealed record Mo2Provider(string SourcePath);

internal sealed record Mo2Layer(string Name, string RootDirectory,
    IReadOnlyDictionary<string, Mo2Provider> Files)
{
    internal static Mo2Layer Read(string name, string root, CancellationToken token)
    {
        root = SafePath.AbsoluteDirectory(root);
        var files = new Dictionary<string, Mo2Provider>(StringComparer.OrdinalIgnoreCase);
        void Walk(string directory)
        {
            // Retain the Windows directory enumeration order used by Python Path.glob in
            // the supplied mapper. Rarity topological-sort ties depend on this order.
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                token.ThrowIfCancellationRequested();
                if (Path.GetFileName(path).EndsWith(".mohidden", StringComparison.OrdinalIgnoreCase) ||
                    Path.GetFileName(path).Equals(".git", StringComparison.OrdinalIgnoreCase)) continue;
                var relative = SafePath.Relative(Path.GetRelativePath(root, path));
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    throw Mo2Compatibility.Error($"Linked/reparse paths are unsupported: {path}.");
                if (Directory.Exists(path)) { Walk(path); continue; }
                if (relative is "meta.ini" or "modfiles.txt") continue;
                if (!files.TryAdd(relative, new(path)))
                    throw Mo2Compatibility.Error($"Ambiguous paths inside MO2 Mod '{name}': {relative}.");
            }
        }
        Walk(root);
        return new(name, root, files);
    }
}

internal static class Mo2VirtualFiles
{
    private sealed record ReorderRule(string Directory, string Pattern, bool All);
    private static readonly ReorderRule[] ReorderRules =
    [
        new("campaign/town/provision", "*layout.darkest", true),
        new("inventory", "*inventory.system_configs.darkest", true),
        new("effects", "*.effects.darkest", true),
        new("localization", "*.loc2", true),
        new("shared", "*.rules.json", true),
        new("shared/quirk", "*quirk_library.json", false)
    ];
    private static readonly (string Directory, string Pattern)[] MergeRules =
    [
        ("trinkets", "*entries.trinkets.json"), ("scripts", "*raid_settings.json"),
        ("raid/ai", "*monster_brains.json"), ("campaign/quest", "*quest.plot_quests.json"),
        ("loot", "*loot.json"), ("shared/quirk", "*quirk_library.json"),
        ("shared/buffs", "*buffs.json"), ("shared/quirk", "*quirk_act_outs.json"),
        ("colours", "*.colours.darkest")
    ];

    internal static Dictionary<string, Mo2Provider> Build(IReadOnlyList<Mo2Layer> layers, CancellationToken token)
    {
        var result = new Dictionary<string, Mo2Provider>(StringComparer.OrdinalIgnoreCase);
        foreach (var layer in layers)
            foreach (var file in layer.Files) result[file.Key] = file.Value;
        var overwrite = layers[^1];
        // Never silently replace a missing generated merge with one of its inputs.
        foreach (var group in layers.Take(layers.Count - 1).SelectMany(layer => layer.Files.Keys)
                     .Where(path => MergeRules.Any(rule => Matches(path, rule.Directory, rule.Pattern)))
                     .GroupBy(path => path, StringComparer.OrdinalIgnoreCase))
            if (group.Count() > 1 && !overwrite.Files.ContainsKey(group.Key))
                throw Mo2Compatibility.Error($"Generated merged file is missing from overwrite: {group.Key}. Run the configured MO2 profile to regenerate it.");

        var aliases = new Dictionary<string, Mo2Provider>(StringComparer.OrdinalIgnoreCase);
        void AddAlias(string alias, Mo2Provider provider)
        {
            if (result.ContainsKey(alias) || !aliases.TryAdd(alias, provider))
                throw Mo2Compatibility.Error($"Conflicting virtual alias: {alias}.");
        }
        foreach (var rule in ReorderRules)
        {
            var groups = layers.SelectMany((layer, index) => layer.Files
                    .Where(file => Matches(file.Key, rule.Directory, rule.Pattern))
                    .Select(file => (file.Key, file.Value, Index: index)))
                .GroupBy(file => file.Key, StringComparer.OrdinalIgnoreCase);
            foreach (var group in groups)
            {
                foreach (var file in rule.All ? group : group.TakeLast(1))
                    AddAlias(Alias(file.Key, file.Index), file.Value);
            }
        }
        var rarityFiles = layers.Take(layers.Count - 1)
            .SelectMany(layer => layer.Files.Where(file => Matches(file.Key, "trinkets", "*rarities.trinkets.json")))
            .ToArray();
        var sorted = SortRarities(rarityFiles.Select(file => file.Value).ToArray(), token);
        for (var index = 0; index < sorted.Count; index++)
        {
            var file = rarityFiles[sorted[index]];
            AddAlias(Alias(file.Key, index), file.Value);
        }
        // Retain original mappings as well. In the supplied Windows mapper, the rarity
        // exclusion uses '/' while the scanner stores '\\', so some final manifests list
        // both originals and aliases. Only the final whitelist decides which are exposed.
        foreach (var alias in aliases) result.Add(alias.Key, alias.Value);
        return result;
    }

    private static string Alias(string relative, int index) =>
        relative[..(relative.LastIndexOf('/') + 1)] + "999" + index.ToString("D3", System.Globalization.CultureInfo.InvariantCulture) + Path.GetFileName(relative);

    private static bool Matches(string relative, string directory, string pattern) =>
        relative[..Math.Max(0, relative.LastIndexOf('/'))].Equals(directory, StringComparison.OrdinalIgnoreCase) &&
        FileSystemName.MatchesSimpleExpression(pattern, Path.GetFileName(relative), ignoreCase: true);

    // Match the supplied game plugin's dependency sort. The final manifest remains the
    // authority: only aliases actually listed there will be copied/visible to the editor.
    internal static IReadOnlyList<int> SortRarities(IReadOnlyList<Mo2Provider> files, CancellationToken token)
    {
        var baseIds = new HashSet<string>(["darkest_dungeon", "trophy", "ancestral_shambler", "ancestral",
            "crow", "courtier", "collector", "madman", "very_rare", "rare", "uncommon", "common", "very_common",
            "kickstarter", "arena", "comet", "mildred", "thing", "shieldbreaker", "crimson_court"], StringComparer.Ordinal);
        var ids = new List<HashSet<string>>();
        var references = new List<HashSet<string>>();
        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();
            using var reader = new StreamReader(file.SourcePath, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
            using var json = JsonDocument.Parse(reader.ReadToEnd());
            var own = new HashSet<string>(StringComparer.Ordinal);
            var refs = new HashSet<string>(StringComparer.Ordinal);
            foreach (var rarity in json.RootElement.GetProperty("rarities").EnumerateArray())
            {
                if (rarity.TryGetProperty("id", out var id)) own.Add(id.GetString()!);
                if (rarity.TryGetProperty("insert_before", out var before)) refs.Add(before.GetString()!);
            }
            ids.Add(own); references.Add(refs);
        }
        var edges = files.Select(_ => new List<int>()).ToArray();
        var incoming = new int[files.Count];
        for (var file = 0; file < files.Count; file++)
        {
            var required = new HashSet<string>(references[file], StringComparer.Ordinal);
            required.ExceptWith(baseIds);
            required.ExceptWith(ids[file]);
            for (var target = 0; target < files.Count; target++)
                if (required.Overlaps(ids[target]))
                {
                    edges[target].Add(file);
                    incoming[file]++;
                    required.ExceptWith(ids[target]);
                }
        }
        var queue = new Queue<int>(Enumerable.Range(0, files.Count).Where(index => incoming[index] == 0));
        var sorted = new List<int>();
        while (queue.TryDequeue(out var next))
        {
            sorted.Add(next);
            foreach (var dependent in edges[next]) if (--incoming[dependent] == 0) queue.Enqueue(dependent);
        }
        if (sorted.Count != files.Count) throw Mo2Compatibility.Error("Trinket rarity dependencies contain a cycle. Fix it in MO2 before loading.");
        return sorted;
    }
}
