using System.IO.Enumeration;

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
    // game_support_DarkestDungeon_20261016_004446: these aliases use the layer index.
    private static readonly ReorderRule[] ReorderRules =
    [
        new("campaign/town/provision", "*layout.darkest", true),
        new("inventory", "*inventory.system_configs.darkest", true),
        new("effects", "*.effects.darkest", true),
        new("localization", "*.loc2", true),
        new("localization", "*.loc", true),
        new("shared", "*rules.json", true),
        new("shared/quirk", "*quirk_library.json", false),
        new("shared/buffs", "*buffs.json", true)
    ];
    private static readonly (string Directory, string Pattern)[] MergeRules =
    [
        ("trinkets", "*entries.trinkets.json"), ("scripts", "*raid_settings.json"),
        ("raid/ai", "*monster_brains.json"), ("campaign/quest", "*quest.plot_quests.json"),
        ("loot", "*loot.json"), ("shared/quirk", "*quirk_library.json"),
        ("shared/quirk", "*quirk_act_outs.json"), ("colours", "*.colours.darkest"),
        ("inventory", "*inventory.items.darkest"), ("inventory", "*inventory.extra_stack_limits.darkest"),
        ("trinkets", "*rarities.trinkets.json")
    ];

    internal static Dictionary<string, Mo2Provider> Build(IReadOnlyList<Mo2Layer> layers, CancellationToken token)
    {
        var result = new Dictionary<string, Mo2Provider>(StringComparer.OrdinalIgnoreCase);
        foreach (var layer in layers)
        {
            token.ThrowIfCancellationRequested();
            foreach (var file in layer.Files) result[file.Key] = file.Value;
        }
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
            token.ThrowIfCancellationRequested();
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
        // Rarity files now keep their original paths. MO2 merges same-path rarities into
        // overwrite, including dependency ordering and duplicate-ID selection. Consume
        // those bytes directly; do not parse or globally reorder the input rarity files.
        // Only names in the final manifest are exposed by Mo2ContentPlan.
        foreach (var alias in aliases) result.Add(alias.Key, alias.Value);
        return result;
    }

    private static string Alias(string relative, int index) =>
        relative[..(relative.LastIndexOf('/') + 1)] + "999" + index.ToString("D3", System.Globalization.CultureInfo.InvariantCulture) + Path.GetFileName(relative);

    private static bool Matches(string relative, string directory, string pattern) =>
        relative[..Math.Max(0, relative.LastIndexOf('/'))].Equals(directory, StringComparison.OrdinalIgnoreCase) &&
        FileSystemName.MatchesSimpleExpression(pattern, Path.GetFileName(relative), ignoreCase: true);
}
