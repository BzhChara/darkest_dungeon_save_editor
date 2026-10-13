// DSON format derived from robojumper/DarkestDungeonSaveEditor v0.0.70.
// Copyright (c) 2018 robojumper. See LICENSE in this directory (MIT).
using System.Text;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DarkestDungeonSaveEditor.Core.Dson;

internal sealed class DsonDocument(byte[] original, byte[] header, List<DsonField> fields)
{
    internal const int MaxDepth = 64;
    internal static readonly UTF8Encoding Utf8 = new(false, true);
    internal byte[] Original { get; } = original;
    internal byte[] Header { get; } = header;
    internal List<DsonField> Fields { get; } = fields;

    internal JsonObject ToJson(CancellationToken token) => ObjectView(Fields, token);

    internal static bool ViewEquals(JsonNode? left, JsonNode? right)
    {
        if (left is JsonObject a && right is JsonObject b)
            return a.Count == b.Count && a.All(pair => b.TryGetPropertyValue(pair.Key, out var value) && ViewEquals(pair.Value, value));
        if (left is JsonArray x && right is JsonArray y)
            return x.Count == y.Count && x.Zip(y).All(pair => ViewEquals(pair.First, pair.Second));
        if (left?.GetValueKind() == JsonValueKind.Number && right?.GetValueKind() == JsonValueKind.Number)
        {
            var aNumber = NormalizeNumber(JsonSerializer.SerializeToElement(left).GetRawText());
            var bNumber = NormalizeNumber(JsonSerializer.SerializeToElement(right).GetRawText());
            return aNumber is not null && aNumber == bNumber;
        }
        return JsonNode.DeepEquals(left, right);
    }

    // Compare numeric spelling exactly without rounding through double/decimal;
    // otherwise an edit such as integer 0 -> 1e-99 could be mistaken for a no-op.
    private static (bool Negative, string Digits, long Exponent)? NormalizeNumber(string text)
    {
        var split = text.IndexOfAny(['e', 'E']);
        var exponent = 0;
        if (split >= 0 && !int.TryParse(text.AsSpan(split + 1), NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out exponent)) return null;
        var mantissa = split < 0 ? text : text[..split];
        var negative = mantissa.StartsWith('-');
        if (negative) mantissa = mantissa[1..];
        var dot = mantissa.IndexOf('.');
        var fraction = dot < 0 ? 0 : mantissa.Length - dot - 1;
        var digits = mantissa.Replace(".", "", StringComparison.Ordinal).TrimStart('0');
        if (digits.Length == 0) return (false, "0", 0);
        var trimmed = digits.TrimEnd('0');
        return (negative, trimmed, (long)exponent - fraction + digits.Length - trimmed.Length);
    }

    internal static JsonObject ObjectView(List<DsonField> fields, CancellationToken token)
    {
        var result = new JsonObject();
        foreach (var field in fields)
        {
            token.ThrowIfCancellationRequested();
            // Existing editor workflows use unique JSON property names. All native
            // occurrences remain in the document; the writer rejects ambiguous edits.
            if (!result.ContainsKey(field.Name)) result.Add(field.Name, field.ToJson(token));
        }
        return result;
    }

    internal static JsonObject ReadJson(string text)
    {
        using var document = JsonDocument.Parse(text, new JsonDocumentOptions
        {
            AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip, MaxDepth = MaxDepth
        });
        ValidateNames(document.RootElement);
        return JsonNode.Parse(document.RootElement.GetRawText()) as JsonObject
            ?? throw new InvalidDataException("DSON input must be a JSON object.");
    }

    private static void ValidateNames(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new InvalidDataException($"Duplicate JSON property '{property.Name}' is ambiguous.");
                ValidateNames(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) ValidateNames(item);
    }
}

internal sealed class DsonField
{
    internal required string Name { get; init; }
    internal required uint Info { get; init; }
    internal required int DataStart { get; init; }
    internal required byte[] Raw { get; init; }
    internal DsonKind Kind { get; set; }
    internal JsonNode? Value { get; set; }
    internal List<DsonField> Children { get; } = [];
    internal DsonDocument? Embedded { get; set; }
    internal int Padding => DsonTypes.Padding(DataStart);

    internal JsonNode? ToJson(CancellationToken token) => Kind switch
    {
        DsonKind.Object => DsonDocument.ObjectView(Children, token),
        DsonKind.Embedded => Embedded!.ToJson(token),
        _ => Value?.DeepClone()
    };
}
