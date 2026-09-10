using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;

namespace DarkestDungeonSaveEditor.Core;

public static partial class HeroClassCatalog
{
    // Resource identities are hashed from their original bytes by the game.
    // Display/enum helpers must not normalize an identity before it is resolved.
    private static string ReadJsonIdentity(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object &&
        NativeJsonReader.TryGetProperty(element, propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;

    private static double? ReadJsonDouble(JsonElement element, string propertyName)
    {
        return NativeJsonReader.TryGetProperty(element, propertyName, out var property) &&
               property.ValueKind == JsonValueKind.Number &&
               property.TryGetDouble(out var value)
            ? value
            : null;
    }

    private static int? ReadJsonInt(JsonElement element, string propertyName)
    {
        return NativeJsonReader.TryGetProperty(element, propertyName, out var property) &&
               property.ValueKind == JsonValueKind.Number &&
               property.TryGetInt32(out var value)
            ? value
            : null;
    }

    private static bool? ReadJsonBoolean(JsonElement element, string propertyName)
    {
        return NativeJsonReader.TryGetProperty(element, propertyName, out var property) &&
               property.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? property.GetBoolean()
            : null;
    }

    private static IReadOnlyList<string> ReadJsonStringArray(JsonElement element, string propertyName) =>
        ReadJsonStringList(element, propertyName).Distinct(StringComparer.Ordinal).ToArray();

    private static IReadOnlyList<string> ReadJsonStringList(JsonElement element, string propertyName)
    {
        if (!NativeJsonReader.TryGetProperty(element, propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return property.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(item.GetString()))
            .Select(item => item.GetString()!)
            .ToArray();
    }

}
