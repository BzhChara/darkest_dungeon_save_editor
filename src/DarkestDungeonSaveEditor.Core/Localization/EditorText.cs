using System.Globalization;
using System.Resources;

namespace DarkestDungeonSaveEditor.Core;

/// <summary>Shared UI and diagnostic text. Resource selection never changes save serialization.</summary>
public static class EditorText
{
    private static readonly ResourceManager Resources = new(
        "DarkestDungeonSaveEditor.Core.Localization.Strings", typeof(EditorText).Assembly);

    private static CultureInfo? _culture;
    public static CultureInfo Culture => Volatile.Read(ref _culture) ?? CultureInfo.CurrentUICulture;
    public static CultureInfo SystemCulture { get; private set; } = CultureInfo.CurrentUICulture;
    public static event EventHandler? CultureChanged;

    public static bool IsChinese => Culture.TwoLetterISOLanguageName == "zh";

    public static string Get(string key) => Get(key, Culture);

    internal static string Get(string key, CultureInfo culture) => Resources.GetString(key, culture)
        ?? throw new MissingManifestResourceException($"Missing editor text resource: {key}");

    public static string Format(string key, params object?[] arguments) =>
        Format(Culture, key, arguments);

    internal static string Format(CultureInfo culture, string key, params object?[] arguments) =>
        string.Format(culture, Get(key, culture), arguments);

    public static CultureInfo ResolveCulture(string preference, CultureInfo systemCulture) =>
        CultureInfo.GetCultureInfo(preference switch
        {
            "zh-CN" => "zh-CN",
            "en" => "en",
            _ => systemCulture.TwoLetterISOLanguageName == "zh" ? "zh-CN" : "en"
        });

    public static void Initialize(string preference, CultureInfo systemCulture)
    {
        var culture = ResolveCulture(preference, systemCulture);
        var previous = Culture;
        SystemCulture = systemCulture;
        // UI culture flows with ExecutionContext. A process-wide selection also reaches
        // callbacks and async operations started before the user changed the language.
        Volatile.Write(ref _culture, culture);
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
        // Do not change CurrentCulture: native file parsing/writing uses its existing invariant rules.
        if (!Equals(previous, culture)) CultureChanged?.Invoke(null, EventArgs.Empty);
    }

    public static string ContentName(BilingualContentName name, string fallbackId) =>
        ContentName(name.Chinese, name.English, fallbackId);

    public static string ContentName(string chinese, string english, string fallbackId)
    {
        var preferred = IsChinese ? chinese : english;
        var alternate = IsChinese ? english : chinese;
        return !string.IsNullOrWhiteSpace(preferred) ? preferred :
            !string.IsNullOrWhiteSpace(alternate) ? alternate : fallbackId;
    }
}
