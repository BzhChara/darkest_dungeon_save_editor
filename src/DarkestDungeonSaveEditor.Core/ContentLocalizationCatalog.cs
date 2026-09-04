using System.Xml;
using System.Xml.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Net;

namespace DarkestDungeonSaveEditor.Core;

internal sealed class ContentLocalizationCatalog
{
    private const string HeroClassPrefix = "hero_class_name_";
    private const string QuirkPrefix = "str_quirk_name_";
    private const string InventoryTitlePrefix = "str_inventory_title_";
    private const string TrinketPrefix = "str_inventory_title_trinket";
    private const string MonsterNamePrefix = "str_monstername_";
    private const string CurioTitlePrefix = "str_curio_title_";
    private readonly IReadOnlyDictionary<string, BilingualContentName> _entries;

    private ContentLocalizationCatalog(
        IReadOnlyDictionary<string, BilingualContentName> entries,
        IReadOnlyList<string> issues)
    {
        _entries = entries;
        Issues = issues;
    }

    public IReadOnlyList<string> Issues { get; }

    public BilingualContentName GetHeroClassName(string id) => Get(HeroClassPrefix + id);

    public BilingualContentName GetQuirkName(string id) => Get(QuirkPrefix + id);

    public BilingualContentName GetTrinketName(string id)
    {
        var canonical = Get(TrinketPrefix + id);
        var alternate = Get(TrinketPrefix + "_" + id);
        return new BilingualContentName(
            string.IsNullOrWhiteSpace(canonical.Chinese) ? alternate.Chinese : canonical.Chinese,
            string.IsNullOrWhiteSpace(canonical.English) ? alternate.English : canonical.English);
    }

    public BilingualContentName GetInventoryItemName(string type, string id)
    {
        var keys = GetInventoryItemKeys(type, id);
        var result = BilingualContentName.Empty;
        foreach (var key in keys)
        {
            var candidate = Get(key);
            result = new BilingualContentName(
                string.IsNullOrWhiteSpace(result.Chinese) ? candidate.Chinese : result.Chinese,
                string.IsNullOrWhiteSpace(result.English) ? candidate.English : result.English);
        }

        return result;
    }

    public BilingualContentName GetMonsterName(string id) => Get(MonsterNamePrefix + id);

    public BilingualContentName GetCurioTitle(string id) => Get(CurioTitlePrefix + id);

    internal static string GetHeroClassKey(string id) => HeroClassPrefix + id;

    internal static string GetQuirkKey(string id) => QuirkPrefix + id;

    internal static IReadOnlyList<string> GetTrinketKeys(string id) =>
        [TrinketPrefix + id, TrinketPrefix + "_" + id];

    internal static IReadOnlyList<string> GetInventoryItemKeys(string type, string id)
    {
        var canonical = InventoryTitlePrefix + type + id;
        return string.IsNullOrWhiteSpace(id)
            ? [canonical]
            : [canonical, InventoryTitlePrefix + type + "_" + id];
    }

    internal static string GetMonsterNameKey(string id) => MonsterNamePrefix + id;

    internal static string GetCurioTitleKey(string id) => CurioTitlePrefix + id;

    public static ContentLocalizationCatalog Load(
        ActiveContentSnapshot activeContent,
        IEnumerable<string> requestedKeys)
    {
        ArgumentNullException.ThrowIfNull(activeContent);
        ArgumentNullException.ThrowIfNull(requestedKeys);
        var issues = new List<string>();
        var requestedKeyArray = requestedKeys
            .Where(key => !string.IsNullOrWhiteSpace(key) && IsSupportedKey(key))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var requestedKeySet = requestedKeyArray.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidates = new List<ContentFileCandidate>();
        var enabledDlcPrefixes = ContentFileOverlay.GetEnabledDlcPrefixes(activeContent.Sources);
        foreach (var source in activeContent.Sources.OrderBy(item => item.LoadOrder))
        {
            foreach (var path in EnumerateLocalizationFiles(source, enabledDlcPrefixes, issues))
            {
                candidates.Add(new ContentFileCandidate(source, path));
            }
        }

        var builders = new Dictionary<string, BilingualNameBuilder>(StringComparer.OrdinalIgnoreCase);
        var files = ContentFileOverlay.Resolve(candidates, "Localization", issues)
            .OrderBy(file => GetLayer(file.Source.Kind))
            .ThenBy(file => file.Source.Kind is "workshop" or "local"
                ? -file.Source.LoadOrder
                : file.Source.LoadOrder)
            .ThenBy(file => IsLoc2File(file.Path) ? 1 : 0)
            .ThenBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (var file in files)
        {
            try
            {
                foreach (var entry in ReadRequestedEntries(file.Path, requestedKeyArray)
                             .Where(entry => IsTargetLanguage(entry.LanguageId)))
                {
                    var displayValue = NormalizeDisplayName(entry.Value);
                    if (string.IsNullOrWhiteSpace(entry.Key) || string.IsNullOrWhiteSpace(displayValue) ||
                        !requestedKeySet.Contains(entry.Key))
                    {
                        continue;
                    }

                    if (!builders.TryGetValue(entry.Key, out var builder))
                    {
                        builder = new BilingualNameBuilder();
                        builders[entry.Key] = builder;
                    }

                    if (entry.LanguageId.Equals("english", StringComparison.OrdinalIgnoreCase))
                    {
                        builder.English = displayValue;
                    }
                    else
                    {
                        builder.Chinese = displayValue;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException or
                                            RegexMatchTimeoutException or InvalidDataException or
                                            DecoderFallbackException or OverflowException)
            {
                issues.Add($"Failed to read localization '{file.Path}': {ex.Message}");
            }
        }

        return new ContentLocalizationCatalog(
            builders.ToDictionary(
                pair => pair.Key,
                pair => new BilingualContentName(pair.Value.Chinese, pair.Value.English),
                StringComparer.OrdinalIgnoreCase),
            issues);
    }

    private BilingualContentName Get(string key) =>
        _entries.TryGetValue(key, out var value) ? value : BilingualContentName.Empty;

    private static IReadOnlyList<LocalizedEntry> ReadRequestedEntries(
        string path,
        IReadOnlyCollection<string> requestedKeys)
    {
        if (!IsLoc2File(path))
        {
            return ReadLanguageEntries(path);
        }

        var languageId = TryGetLoc2LanguageId(path);
        return languageId is null
            ? []
            : Loc2LocalizationReader.Read(path, requestedKeys)
                .Select(pair => new LocalizedEntry(languageId, pair.Key, pair.Value))
                .ToArray();
    }

    internal static IReadOnlyList<string> ReadHeroNames(string path)
    {
        var groups = ReadLanguageEntries(path)
            .Where(entry => entry.Key.StartsWith("hero_name_", StringComparison.OrdinalIgnoreCase))
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Value))
            .GroupBy(entry => entry.LanguageId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var selected = groups.FirstOrDefault(group =>
                group.Key.Equals("english", StringComparison.OrdinalIgnoreCase)) ??
            groups.FirstOrDefault();
        return selected?
                .Select(entry => entry.Value.Trim())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray() ??
            [];
    }

    private static IReadOnlyList<LocalizedEntry> ReadLanguageEntries(string path)
    {
        try
        {
            var document = LoadDocument(path);
            return (document.Root?.Elements("language") ?? [])
                .SelectMany(language =>
                {
                    var languageId = ((string?)language.Attribute("id"))?.Trim() ?? string.Empty;
                    return language.Descendants("entry").Select(entry => new LocalizedEntry(
                            languageId,
                            ((string?)entry.Attribute("id"))?.Trim() ?? string.Empty,
                            entry.Value.Trim()));
                })
                .ToArray();
        }
        catch (XmlException)
        {
            var text = ReadSanitizedText(path);
            var result = new List<LocalizedEntry>();
            var languagePattern = new Regex(
                @"<language\b[^>]*\bid\s*=\s*(?<quote>[""'])(?<language>.*?)\k<quote>[^>]*>(?<body>.*?)</language\s*>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline,
                TimeSpan.FromSeconds(2));
            var entryPattern = new Regex(
                @"<entry\b[^>]*\bid\s*=\s*(?<quote>[""'])(?<key>.*?)\k<quote>[^>]*>(?<value>.*?)</[^>]+>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline,
                TimeSpan.FromSeconds(2));
            foreach (Match languageMatch in languagePattern.Matches(text))
            {
                var languageId = languageMatch.Groups["language"].Value.Trim();
                foreach (Match entryMatch in entryPattern.Matches(languageMatch.Groups["body"].Value))
                {
                    var value = entryMatch.Groups["value"].Value.Trim();
                    if (value.StartsWith("<![CDATA[", StringComparison.Ordinal) &&
                        value.EndsWith("]]>", StringComparison.Ordinal))
                    {
                        value = value[9..^3];
                    }
                    else
                    {
                        value = WebUtility.HtmlDecode(value);
                    }

                    result.Add(new LocalizedEntry(
                        languageId,
                        entryMatch.Groups["key"].Value.Trim(),
                        value.Trim()));
                }
            }

            return result;
        }
    }

    private static XDocument LoadDocument(string path)
    {
        try
        {
            return XDocument.Load(path, LoadOptions.None);
        }
        catch (XmlException)
        {
            return XDocument.Parse(ReadSanitizedText(path), LoadOptions.None);
        }
    }

    private static string ReadSanitizedText(string path)
    {
        var text = File.ReadAllText(path, new UTF8Encoding(false, false));
        text = Regex.Replace(
            text,
            @"<\?xml[^>]*\?>",
            string.Empty,
            RegexOptions.IgnoreCase,
            TimeSpan.FromSeconds(2));
        text = Regex.Replace(
            text,
            @"<!--.*?-->",
            string.Empty,
            RegexOptions.Singleline,
            TimeSpan.FromSeconds(2));
        return new string(text.Where(character =>
                character is '\t' or '\n' or '\r' || character >= ' ')
            .ToArray());
    }

    private static bool IsTargetLanguage(string languageId) =>
        languageId.Equals("english", StringComparison.OrdinalIgnoreCase) ||
        languageId.Equals("schinese", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeDisplayName(string value)
    {
        value = Regex.Replace(
            value,
            @"\{colour_(?:start(?:\|[^}]*)?|end)\}",
            string.Empty,
            RegexOptions.IgnoreCase,
            TimeSpan.FromSeconds(2));
        return Regex.Replace(
                value,
                @"\s+",
                " ",
                RegexOptions.None,
                TimeSpan.FromSeconds(2))
            .Trim();
    }

    private static bool IsSupportedKey(string key) =>
        key.StartsWith(HeroClassPrefix, StringComparison.OrdinalIgnoreCase) ||
        key.StartsWith(QuirkPrefix, StringComparison.OrdinalIgnoreCase) ||
        key.StartsWith(InventoryTitlePrefix, StringComparison.OrdinalIgnoreCase) ||
        key.StartsWith(MonsterNamePrefix, StringComparison.OrdinalIgnoreCase) ||
        key.StartsWith(CurioTitlePrefix, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> EnumerateLocalizationFiles(
        ActiveContentSource source,
        IReadOnlyList<string> enabledDlcPrefixes,
        List<string> issues)
    {
        if (source.Kind is not ("workshop" or "local"))
        {
            return EnumerateDirectory(Path.Combine(source.Directory, "localization"));
        }

        var manifestPath = Path.Combine(source.Directory, "modfiles.txt");
        if (!File.Exists(manifestPath))
        {
            return EnumerateDirectory(Path.Combine(source.Directory, "localization"));
        }

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddDirectAuthoringStringTables(
            result,
            Path.Combine(source.Directory, "localization"));
        foreach (var dlcPrefix in enabledDlcPrefixes)
        {
            AddDirectAuthoringStringTables(
                result,
                Path.Combine(
                    source.Directory,
                    dlcPrefix.Replace('/', Path.DirectorySeparatorChar),
                    "localization"));
        }

        foreach (var rawLine in File.ReadLines(manifestPath))
        {
            var relativePath = ModManifestPath.Extract(rawLine, ".string_table.xml", ".loc2");
            if (relativePath is null)
            {
                continue;
            }

            var path = Path.GetFullPath(Path.Combine(source.Directory, relativePath));
            var relativeToRoot = Path.GetRelativePath(source.Directory, path);
            if (Path.IsPathRooted(relativeToRoot) ||
                relativeToRoot.Equals("..", StringComparison.Ordinal) ||
                relativeToRoot.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                issues.Add($"Ignored localization manifest path outside its Mod directory: {rawLine.Trim()}");
                continue;
            }

            if (!ContentFileOverlay.IsRootOrEnabledDlcPath(
                    relativeToRoot,
                    "localization",
                    enabledDlcPrefixes))
            {
                continue;
            }

            if (IsLoc2File(path) &&
                (TryGetLoc2LanguageId(path) is null ||
                 !IsDirectLocalizationFile(relativeToRoot, enabledDlcPrefixes)))
            {
                continue;
            }

            if (!File.Exists(path))
            {
                issues.Add($"Localization file listed by Mod is missing: {path}");
                continue;
            }

            result.Add(path);
        }

        return result.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void AddDirectAuthoringStringTables(HashSet<string> result, string directory)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var path in Directory.EnumerateFiles(
                     directory,
                     "*.string_table.xml",
                     SearchOption.TopDirectoryOnly))
        {
            result.Add(Path.GetFullPath(path));
        }
    }

    private static IReadOnlyList<string> EnumerateDirectory(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return Directory.EnumerateFiles(directory, "*.string_table.xml", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(directory, "*.loc2", SearchOption.TopDirectoryOnly)
                .Where(path => TryGetLoc2LanguageId(path) is not null))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsLoc2File(string path) =>
        path.EndsWith(".loc2", StringComparison.OrdinalIgnoreCase);

    private static string? TryGetLoc2LanguageId(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        if (name.Equals("english", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("_english", StringComparison.OrdinalIgnoreCase))
        {
            return "english";
        }

        if (name.Equals("schinese", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("_schinese", StringComparison.OrdinalIgnoreCase))
        {
            return "schinese";
        }

        return null;
    }

    private static bool IsDirectLocalizationFile(
        string relativePath,
        IReadOnlyList<string> enabledDlcPrefixes)
    {
        var normalized = relativePath.Replace('\\', '/');
        var separator = normalized.LastIndexOf('/');
        if (separator < 0)
        {
            return false;
        }

        var directory = normalized[..separator];
        return directory.Equals("localization", StringComparison.OrdinalIgnoreCase) ||
               enabledDlcPrefixes.Any(prefix => directory.Equals(
                   $"{prefix.TrimEnd('/')}/localization",
                   StringComparison.OrdinalIgnoreCase));
    }

    private static int GetLayer(string kind) => kind switch
    {
        "base" or "mode" => 0,
        "dlc" or "dlc-package" or "dlc-feature" => 1,
        "workshop" or "local" => 2,
        _ => -1
    };

    private sealed class BilingualNameBuilder
    {
        public string Chinese { get; set; } = string.Empty;
        public string English { get; set; } = string.Empty;
    }

    private sealed record LocalizedEntry(string LanguageId, string Key, string Value);
}
