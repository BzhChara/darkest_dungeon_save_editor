using System.Security.Cryptography;
using System.Text;

namespace DarkestDungeonSaveEditor.Core;

/// <summary>Refresh signal for all catalog pages; never grants permission to write a save.</summary>
public static class ProfileCatalogContentFingerprint
{
    // Include references as well as definitions: e.g. a hero's starting provision
    // can change item classification without changing any inventory definition.
    private static readonly ContentFileRule[] Rules =
        (from directory in new[] { "inventory", "trinkets", "campaign", "curios", "dungeons", "heroes",
             "loot", "monsters", "props", "raid", "rules", "scripts", "shared", "torch", "upgrades", "effects" }
         // Native loot/event/quest/provision/estate/district queries contain
         // unescaped dots before json. Include accepted names such as Xjson.
         // This shared fingerprint is intentionally broader than the item-root
         // consumer whitelist: other catalogs also depend on campaign data.
         from suffix in new[] { "json", ".darkest", ".csv" }
         select new ContentFileRule(directory, "*" + suffix))
        .Concat(new[] { new ContentFileRule("localization", "*.string_table.xml"),
            new ContentFileRule("localization", "*.loc", false), new ContentFileRule("localization", "*.loc2", false) })
        .ToArray();

    public static string Capture(IReadOnlyList<ActiveContentSource> sources,
        CancellationToken cancellationToken = default)
    {
        var prefixes = ContentFileOverlay.GetEnabledDlcPrefixes(sources);
        var issues = new List<string>();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Add(string value) => hash.AppendData(Encoding.UTF8.GetBytes(value + "\n"));
        foreach (var source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Add($"{source.Id}|{source.Kind}|{source.LoadOrder}|{Path.GetFullPath(source.Directory)}|{source.VirtualPathPrefix}");
            var paths = ContentFileDiscovery.Enumerate(source, prefixes, issues, "Catalog refresh", Rules)
                .Concat(NativeContentFileResolver.EnumerateActorOpenFiles(source, prefixes, "monsters", issues))
                .Concat(NativeContentFileResolver.EnumerateActorOpenFiles(source, prefixes, "heroes", issues))
                .Concat(BattleRoomAttachmentCatalog.EnumerateCanonicalPropFiles(source, prefixes, issues).Where(File.Exists))
                .Concat(new[] { "modfiles.txt", "project.xml", ManagedBattleEncounterBridgeService.ManifestFileName }
                    .Select(name => Path.Combine(source.Directory, name)).Where(File.Exists));
            foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var stream = File.OpenRead(path);
                Add($"{Path.GetFullPath(path)}|{Convert.ToHexString(SHA256.HashData(stream))}");
            }
            // Skin eligibility depends on listed textures existing, not their
            // pixel content. Track paths without hashing large image payloads.
            foreach (var path in ContentFileDiscovery.Enumerate(source, prefixes, issues,
                         "Hero skin", new ContentFileRule("heroes", "*.png")))
                Add($"Hero texture: {Path.GetFullPath(path)}");
        }
        foreach (var issue in issues.Distinct().Order(StringComparer.Ordinal)) Add(issue);
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
