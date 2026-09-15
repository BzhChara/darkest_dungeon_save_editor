using System.Xml;
using System.Xml.Linq;
using System.Text;
using System.Text.RegularExpressions;

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
        var requestedKeySet = requestedKeyArray.ToHashSet(StringComparer.Ordinal);
        var candidates = new List<ContentFileCandidate>();
        var enabledDlcPrefixes = ContentFileOverlay.GetEnabledDlcPrefixes(activeContent.Sources);
        foreach (var source in activeContent.Sources.OrderBy(item => item.LoadOrder))
        {
            foreach (var path in EnumerateLocalizationFiles(source, enabledDlcPrefixes, issues))
            {
                candidates.Add(new ContentFileCandidate(source, path));
            }
        }

        var builders = new Dictionary<string, BilingualNameBuilder>(StringComparer.Ordinal);
        var files = NativeContentFileResolver.Resolve(candidates, activeContent.Sources, "Localization", issues)
            .OrderBy(file => GetLayer(file.Source.Kind))
            .ThenBy(file => file.Source.Kind is "workshop" or "local"
                ? -file.Source.LoadOrder
                : file.Source.LoadOrder)
            .ThenBy(file => GetFormatPriority(file.Path))
            .ThenBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var readFailures = new List<(EffectiveContentFile File, string Reason)>();
        var compiledEvidence = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            try
            {
                var diagnostics = new LocalizationEntryDiagnostics(file.Path);
                var acceptedEntries = 0;
                foreach (var entry in ReadRequestedEntries(file.Path, requestedKeyArray, issues, diagnostics)
                             .Where(entry => IsTargetLanguage(entry.LanguageId)))
                {
                    string displayValue;
                    try
                    {
                        displayValue = NormalizeDisplayName(entry.Value);
                    }
                    catch (RegexMatchTimeoutException)
                    {
                        diagnostics.Skip(entry.Key, "名称格式化超时");
                        continue;
                    }

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
                    acceptedEntries++;
                }

                diagnostics.AppendTo(issues);
                if (acceptedEntries > 0 &&
                    (file.Path.EndsWith(".loc", StringComparison.OrdinalIgnoreCase) ||
                     file.Path.EndsWith(".loc2", StringComparison.OrdinalIgnoreCase)))
                {
                    if (!compiledEvidence.TryGetValue(file.Source.Id, out var paths))
                    {
                        paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        compiledEvidence.Add(file.Source.Id, paths);
                    }
                    paths.Add(file.Path);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException or
                                            RegexMatchTimeoutException or InvalidDataException or
                                            DecoderFallbackException or OverflowException)
            {
                readFailures.Add((file, ex.Message));
            }
        }

        foreach (var (file, reason) in readFailures)
        {
            var evidence = compiledEvidence.TryGetValue(file.Source.Id, out var paths)
                ? $"名称读取器已从同一来源的其他编译表取得有效条目（LOC={paths.Count(path => path.EndsWith(".loc", StringComparison.OrdinalIgnoreCase))}，" +
                  $"LOC2={paths.Count(path => path.EndsWith(".loc2", StringComparison.OrdinalIgnoreCase))}）；不保证覆盖失败文件的所有名称"
                : "本次所请求的名称中，未从同一来源的其他编译表取得有效条目；这不是其他来源或语言全部缺失的结论";
            issues.Add($"Failed to read localization '{file.Path}': {reason}" +
                CatalogLogDiagnostics.LocalizationEvidenceMarker + evidence);
        }

        return new ContentLocalizationCatalog(
            builders.ToDictionary(
                pair => pair.Key,
                pair => new BilingualContentName(pair.Value.Chinese, pair.Value.English),
                StringComparer.Ordinal),
            issues);
    }

    private BilingualContentName Get(string key) =>
        _entries.TryGetValue(key, out var value) ? value : BilingualContentName.Empty;

    private static IReadOnlyList<LocalizedEntry> ReadRequestedEntries(
        string path,
        IReadOnlyCollection<string> requestedKeys,
        ICollection<string> issues,
        LocalizationEntryDiagnostics diagnostics)
    {
        if (!IsCompiledFile(path))
        {
            return ReadLanguageEntries(path, diagnostics);
        }

        var languageId = TryGetCompiledLanguageId(path);
        return languageId is null
            ? []
            : (IsLoc2File(path)
                ? Loc2LocalizationReader.Read(path, requestedKeys, issues)
                : LocLocalizationReader.Read(path, requestedKeys, issues))
                .Select(pair => new LocalizedEntry(languageId, pair.Key, pair.Value))
                .ToArray();
    }

    internal static IReadOnlyList<string> ReadHeroNames(string path, ICollection<string> issues)
    {
        var diagnostics = new LocalizationEntryDiagnostics(path);
        var groups = ReadLanguageEntries(path, diagnostics)
            .Where(entry => entry.Key.StartsWith("hero_name_", StringComparison.OrdinalIgnoreCase))
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Value))
            .GroupBy(entry => entry.LanguageId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        diagnostics.AppendTo(issues);
        var selected = groups.FirstOrDefault(group =>
                group.Key.Equals("english", StringComparison.OrdinalIgnoreCase)) ??
            groups.FirstOrDefault();
        return selected?
                .Select(entry => entry.Value.Trim())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray() ??
            [];
    }

    private static IReadOnlyList<LocalizedEntry> ReadLanguageEntries(
        string path,
        LocalizationEntryDiagnostics diagnostics)
    {
        // Finish strict parsing before exposing any entry: invalid byte encoding,
        // declarations, comments or markup can invalidate the document's boundaries.
        var document = XDocument.Load(path, LoadOptions.None);
        var result = new List<LocalizedEntry>();
        var entryIndex = 0;
        foreach (var language in document.Root?.Elements("language") ?? [])
        {
            var languageId = ((string?)language.Attribute("id"))?.Trim() ?? string.Empty;
            foreach (var entry in language.Descendants("entry"))
            {
                entryIndex++;
                var key = (string?)entry.Attribute("id") ?? string.Empty;
                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(languageId))
                {
                    diagnostics.Skip($"XML 条目 {entryIndex}", string.IsNullOrWhiteSpace(key) ? "缺少 ID" : "缺少语言 ID");
                    continue;
                }

                if (entry.Ancestors("entry").Any() || entry.Descendants("entry").Any() ||
                    entry.Descendants("language").Any() || entry.Ancestors("language").FirstOrDefault() != language)
                {
                    diagnostics.Skip(key, "条目或语言嵌套导致归属不明确");
                    continue;
                }

                result.Add(new LocalizedEntry(languageId, key, entry.Value.Trim()));
            }
        }

        return result;
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
        ModManifestFile.Require(manifestPath);

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in ModManifestFile.ReadEntries(manifestPath, ".string_table.xml", ".loc", ".loc2"))
        {
            var rawLine = entry.RawLine;
            var relativePath = entry.RelativePath;

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

            if (IsCompiledFile(path) &&
                (TryGetCompiledLanguageId(path) is null ||
                 !IsDirectLocalizationFile(relativeToRoot, enabledDlcPrefixes)))
            {
                continue;
            }

            if (!File.Exists(path))
            {
                issues.Add($"Localization file listed by Mod is missing: {path}");
            }

            result.Add(path);
        }

        return result.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IReadOnlyList<string> EnumerateDirectory(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return NativeDirectoryDiscovery.EnumerateFiles(directory, "*.string_table.xml", SearchOption.AllDirectories)
            .Concat(NativeDirectoryDiscovery.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
                .Where(path => IsCompiledFile(path) && TryGetCompiledLanguageId(path) is not null))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsLoc2File(string path) =>
        path.EndsWith(".loc2", StringComparison.OrdinalIgnoreCase);

    private static bool IsCompiledFile(string path) =>
        IsLoc2File(path) || path.EndsWith(".loc", StringComparison.OrdinalIgnoreCase);

    private static int GetFormatPriority(string path) =>
        IsLoc2File(path) ? 2 : IsCompiledFile(path) ? 1 : 0;

    private static string? TryGetCompiledLanguageId(string path)
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
