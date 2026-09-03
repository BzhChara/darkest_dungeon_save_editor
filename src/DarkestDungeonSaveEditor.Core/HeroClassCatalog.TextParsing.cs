using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;

namespace DarkestDungeonSaveEditor.Core;

public static partial class HeroClassCatalog
{
    private static IReadOnlyDictionary<string, IReadOnlyList<string>> ParseAttributes(string value)
    {
        var tokens = Tokenize(value);
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];
            if (!token.StartsWith(".", StringComparison.Ordinal) || token.Length <= 1)
            {
                continue;
            }

            var key = token[1..];
            var values = new List<string>();
            while (index + 1 < tokens.Count && !tokens[index + 1].StartsWith(".", StringComparison.Ordinal))
            {
                index++;
                values.Add(tokens[index]);
            }

            result[key] = values;
        }

        return result;
    }

    private static IReadOnlyList<string> Tokenize(string value)
    {
        var tokens = new List<string>();
        for (var index = 0; index < value.Length;)
        {
            while (index < value.Length && char.IsWhiteSpace(value[index]))
            {
                index++;
            }

            if (index >= value.Length)
            {
                break;
            }

            if (value[index] == '"')
            {
                index++;
                var start = index;
                while (index < value.Length && value[index] != '"')
                {
                    index++;
                }

                tokens.Add(value[start..Math.Min(index, value.Length)]);
                if (index < value.Length)
                {
                    index++;
                }

                continue;
            }

            var tokenStart = index;
            while (index < value.Length && !char.IsWhiteSpace(value[index]) && value[index] != '"')
            {
                index++;
            }

            tokens.Add(value[tokenStart..index]);
        }

        return tokens;
    }

    private static string? ReadString(
        IReadOnlyDictionary<string, IReadOnlyList<string>> attributes,
        string key)
    {
        return attributes.TryGetValue(key, out var values) && values.Count > 0
            ? values[0]
            : null;
    }

    private static int? ReadInt(
        IReadOnlyDictionary<string, IReadOnlyList<string>> attributes,
        string key)
    {
        return ReadString(attributes, key) is { } value &&
               int.TryParse(value.TrimEnd('%'), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static double? ReadDouble(
        IReadOnlyDictionary<string, IReadOnlyList<string>> attributes,
        string key)
    {
        return ReadString(attributes, key) is { } value &&
               double.TryParse(value.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static bool? ReadBooleanFlag(
        IReadOnlyDictionary<string, IReadOnlyList<string>> attributes,
        string key)
    {
        if (!attributes.TryGetValue(key, out var values))
        {
            return null;
        }

        if (values.Count == 0)
        {
            return true;
        }

        if (bool.TryParse(values[0], out var parsed))
        {
            return parsed;
        }

        return values[0] switch
        {
            "1" => true,
            "0" => false,
            _ => null
        };
    }

    private static string ReadJsonString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()?.Trim() ?? string.Empty
            : string.Empty;
    }
    private static double? ReadJsonDouble(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.Number &&
               property.TryGetDouble(out var value)
            ? value
            : null;
    }

    private static int? ReadJsonInt(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.Number &&
               property.TryGetInt32(out var value)
            ? value
            : null;
    }

    private static bool? ReadJsonBoolean(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? property.GetBoolean()
            : null;
    }

    private static IReadOnlyList<string> ReadJsonStringArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return property.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
            .Select(item => item.GetString()!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

}
