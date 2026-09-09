using System.Reflection;
using System.Xml.Linq;

internal static partial class ContractSuite
{
    private static void RunLocalizationPolicyContracts(ActiveContentSnapshot activeContent, ContractFixture fixture)
    {
        const string enabled = "dlc/100_feature_pack/features/enabled_feature";
        const string disabled = "dlc/100_feature_pack/features/disabled_feature";
        string[] keys =
        [
            "hero_class_name_localization_policy", "str_quirk_name_localization_policy",
            "str_inventory_title_estatelocalization_policy", "str_inventory_title_trinketlocalization_policy",
            "str_monstername_localization_policy", "str_curio_title_localization_policy"
        ];
        const string chineseOnly = "str_quirk_name_policy_chinese_only";
        const string unlistedKey = "str_quirk_name_policy_unlisted";
        const string excludedKey = "str_quirk_name_policy_excluded";
        const string dlcKey = "str_quirk_name_policy_dlc";
        const string malformedKey = "str_quirk_name_policy_malformed";
        const string nestedKey = "str_quirk_name_policy_nested_xml";
        var requested = keys.Concat([chineseOnly, unlistedKey, excludedKey, dlcKey, malformedKey, nestedKey]).ToArray();
        var root = Path.Combine(fixture.RunRoot, "localization-policy");
        var modRoot = Path.Combine(root, "manifest");
        var manifest = new List<string>();
        string PathFor(string relative, bool listed = true)
        {
            var path = Path.Combine(modRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (listed)
            {
                manifest.Add(relative);
            }

            return path;
        }

        var english = keys.ToDictionary(key => key, _ => "Legacy English");
        var chinese = keys.ToDictionary(key => key, _ => "旧格式中文");
        chinese[chineseOnly] = "仅有中文";
        WriteLocalizationXml(PathFor("localization/listed.string_table.xml"),
            keys.ToDictionary(key => key, _ => "XML English"), keys.ToDictionary(key => key, _ => "XML 中文"));
        WriteLegacyLoc(PathFor("localization/source_english.loc"), english);
        WriteLegacyLoc(PathFor("localization/source_schinese.loc"), chinese);
        WriteLoc2(PathFor("localization/source_english.loc2"), new Dictionary<string, string> { [keys[0]] = "Modern English" });
        WriteLoc2(PathFor("localization/source_schinese.loc2"), new Dictionary<string, string> { [keys[5]] = "新版奇物" });
        WriteLocalizationXml(PathFor("localization/unlisted.string_table.xml", false),
            new Dictionary<string, string> { [unlistedKey] = "Unlisted XML" }, new Dictionary<string, string>());
        WriteLegacyLoc(PathFor("localization/unlisted_english.loc", false),
            new Dictionary<string, string> { [unlistedKey] = "Unlisted LOC" });
        WriteLoc2(PathFor("localization/unlisted_english.loc2", false),
            new Dictionary<string, string> { [unlistedKey] = "Unlisted LOC2" });
        WriteLocalizationXml(PathFor("localization/nested/names.string_table.xml"),
            new Dictionary<string, string> { [nestedKey] = "Listed Nested XML" }, new Dictionary<string, string>());
        foreach (var prefix in new[] { enabled, disabled, "project-backup" })
        {
            var value = prefix == enabled ? "Enabled DLC" : "Excluded DLC";
            WriteLegacyLoc(PathFor($"{prefix}/localization/dlc_english.loc"),
                new Dictionary<string, string> { [dlcKey] = value });
            WriteLegacyLoc(PathFor($"{prefix}/localization/dlc_schinese.loc"),
                new Dictionary<string, string> { [dlcKey] = value });
            WriteLocalizationXml(PathFor($"{prefix}/localization/unlisted.string_table.xml", false),
                new Dictionary<string, string> { [unlistedKey] = "Unlisted DLC XML" }, new Dictionary<string, string>());
        }

        foreach (var relative in new[]
                 {
                     "localization/unused/hidden_english.loc", "localization/windows/hidden_english.loc",
                     "localization/hidden_french.loc", "localization/hidden_english.loc.unused",
                     $"{enabled}/localization/backup/hidden_english.loc"
                 })
        {
            WriteLegacyLoc(PathFor(relative), new Dictionary<string, string> { [excludedKey] = "Excluded Binary" });
        }

        string[] malformedXml =
        [
            $"<root><language id=\"english\"><entry id=\"{malformedKey}\">Recovered</einntry></language></root>",
            $"<?xml version=\"1.1\"?><root><language id=\"english\"><entry id=\"{malformedKey}\">Recovered</entry></language></root>",
            $"<root><!-- illegal -- comment --><language id=\"english\"><entry id=\"{malformedKey}\">Recovered</entry></language></root>",
            $"<root><language id=\"english\"><entry id=\"{malformedKey}\">Recovered\u0001</entry></language></root>"
        ];
        for (var index = 0; index < malformedXml.Length; index++)
        {
            File.WriteAllText(PathFor($"localization/malformed{index}.string_table.xml"), malformedXml[index]);
        }

        manifest.Add("localization/missing_english.loc");
        var manifestPath = Path.Combine(modRoot, "modfiles.txt");
        File.WriteAllLines(manifestPath, manifest);
        var protectedHashes = Directory.EnumerateFiles(modRoot, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
        var dlcSources = activeContent.Sources.Where(source => source.Kind is "dlc-package" or "dlc-feature").ToArray();
        var source = new ActiveContentSource("local:localization-policy", "Localization Policy", "local", modRoot, 0);
        var content = activeContent with { Sources = [.. dlcSources, source] };
        foreach (var kind in new[] { "local", "workshop" })
        {
            var probe = ReadLocalizationProbe(content with { Sources = [.. dlcSources, source with { Kind = kind }] }, requested);
            for (var index = 0; index < keys.Length; index++)
            {
                Assert(probe.Names[keys[index]] == new BilingualContentName(
                        index == 5 ? "新版奇物" : "旧格式中文", index == 0 ? "Modern English" : "Legacy English"),
                    "All requested name families must use per-language LOC2 > LOC > valid XML precedence within a provider.");
            }

            Assert(probe.Names[chineseOnly] == new BilingualContentName("仅有中文", "") &&
                   probe.Names[unlistedKey] == BilingualContentName.Empty &&
                   probe.Names[excludedKey] == BilingualContentName.Empty &&
                   probe.Names[malformedKey] == BilingualContentName.Empty &&
                   probe.Names[dlcKey] == new BilingualContentName("Enabled DLC", "Enabled DLC") &&
                   probe.Names[nestedKey].English == "Listed Nested XML",
                "Manifest presence must gate every localization format, preserve missing languages and enabled DLC roots, and retain normal listed XML discovery.");
            Assert(Enumerable.Range(0, malformedXml.Length).All(index => probe.Issues.Any(issue =>
                       issue.Contains($"malformed{index}.string_table.xml", StringComparison.Ordinal) &&
                       issue.Contains("Failed to read localization", StringComparison.Ordinal))) &&
                   probe.Issues.Any(issue => issue.Contains("missing_english.loc", StringComparison.Ordinal) &&
                                             issue.Contains("listed by Mod is missing", StringComparison.Ordinal)) &&
                   probe.Issues.All(issue => !issue.Contains("hidden_", StringComparison.Ordinal)),
                "Rejected XML and missing listed LOC files must be diagnosed; excluded binary paths must not be parsed.");
        }

        Assert(protectedHashes.All(pair => pair.Value == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(pair.Key)))),
            "Reading any localization format must not rewrite or repair the source files or manifest.");

        var fallbackRoot = Path.Combine(root, "fallback");
        foreach (var pair in protectedHashes.Where(pair => pair.Key != manifestPath))
        {
            var target = Path.Combine(fallbackRoot, Path.GetRelativePath(modRoot, pair.Key));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(pair.Key, target);
        }

        WriteFixtureManifest(fallbackRoot);
        foreach (var kind in new[] { "local", "workshop" })
        {
            var fallback = ReadLocalizationProbe(content with
            {
                Sources = [.. dlcSources, source with { Kind = kind, Directory = fallbackRoot }]
            }, requested);
            Assert(fallback.Names[unlistedKey].English == "Unlisted LOC2" &&
                   fallback.Names[dlcKey] == new BilingualContentName("Enabled DLC", "Enabled DLC") &&
                   fallback.Names[excludedKey] == BilingualContentName.Empty &&
                   fallback.Names[nestedKey].English == "Listed Nested XML" &&
                   fallback.Names[malformedKey] == BilingualContentName.Empty,
                "After explicitly listing the additional files, both Mod channels must include eligible XML/LOC/LOC2 and still reject disabled roots and malformed XML.");
        }

        var higherRoot = Path.Combine(root, "higher");
        Directory.CreateDirectory(Path.Combine(higherRoot, "localization"));
        WriteLocalizationXml(Path.Combine(higherRoot, "localization/higher.string_table.xml"),
            new Dictionary<string, string> { [keys[0]] = "Higher XML" }, new Dictionary<string, string>());
        File.WriteAllText(Path.Combine(higherRoot, "modfiles.txt"), "localization/higher.string_table.xml");
        var higher = source with { Id = "local:higher", Directory = higherRoot, LoadOrder = -1 };
        var overridden = ReadLocalizationProbe(content with { Sources = [.. content.Sources, higher] }, requested);
        Assert(overridden.Names[keys[0]] == new BilingualContentName("旧格式中文", "Higher XML"),
            "A higher-priority Mod's valid XML must beat a lower provider's LOC2 without cross-language substitution.");

        WriteLegacyLoc(Path.Combine(higherRoot, "localization/source_english.loc"),
            new Dictionary<string, string> { [keys[0]] = "Higher LOC" });
        File.WriteAllText(Path.Combine(higherRoot, "modfiles.txt"), "localization/source_english.loc");
        overridden = ReadLocalizationProbe(content with { Sources = [.. content.Sources, higher] }, requested);
        Assert(overridden.Names[keys[0]].English == "Higher LOC" && overridden.Names[keys[1]].English == "XML English",
            "Exact-path replacement must discard the lower LOC file before key lookup; do not resurrect its omitted values.");

        File.WriteAllText(manifestPath, string.Empty);
        Assert(ReadLocalizationProbe(content, requested).Names.Values.All(name => name == BilingualContentName.Empty),
            "An empty readable manifest must not become permission to load unlisted XML, LOC or LOC2.");
    }

    private static void WriteLocalizationXml(
        string path,
        IReadOnlyDictionary<string, string> english,
        IReadOnlyDictionary<string, string> chinese)
    {
        static XElement Language(string id, IReadOnlyDictionary<string, string> entries) =>
            new("language", new XAttribute("id", id), entries.Select(pair =>
                new XElement("entry", new XAttribute("id", pair.Key), pair.Value)));
        new XDocument(new XElement("root", Language("english", english), Language("schinese", chinese))).Save(path);
    }

    private static (IReadOnlyDictionary<string, BilingualContentName> Names, IReadOnlyList<string> Issues)
        ReadLocalizationProbe(ActiveContentSnapshot content, IReadOnlyCollection<string> keys)
    {
        var type = typeof(TrinketCatalog).Assembly.GetType("DarkestDungeonSaveEditor.Core.ContentLocalizationCatalog")!;
        var catalog = type.GetMethod("Load", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [content, keys])!;
        var get = type.GetMethod("Get", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var names = keys.Distinct().ToDictionary(key => key, key => (BilingualContentName)get.Invoke(catalog, [key])!);
        return (names, (IReadOnlyList<string>)type.GetProperty("Issues")!.GetValue(catalog)!);
    }
}
