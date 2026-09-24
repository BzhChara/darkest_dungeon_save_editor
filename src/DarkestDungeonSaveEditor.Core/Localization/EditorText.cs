using System.Globalization;
using System.Resources;

namespace DarkestDungeonSaveEditor.Core;

/// <summary>Shared UI and diagnostic text. Resource selection never changes save serialization.</summary>
public static class EditorText
{
    private static readonly ResourceManager Resources = new(
        "DarkestDungeonSaveEditor.Core.Localization.Strings", typeof(EditorText).Assembly);

    public static bool IsChinese => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh";

    public static string Get(string key) => Resources.GetString(key, CultureInfo.CurrentUICulture)
        ?? throw new MissingManifestResourceException($"Missing editor text resource: {key}");

    public static string Format(string key, params object?[] arguments) =>
        string.Format(CultureInfo.CurrentUICulture, Get(key), arguments);

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
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
        // Do not change CurrentCulture: native file parsing/writing uses its existing invariant rules.
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
