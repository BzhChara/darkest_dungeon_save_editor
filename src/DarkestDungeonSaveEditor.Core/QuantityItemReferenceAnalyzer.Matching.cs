using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DarkestDungeonSaveEditor.Core;

internal static partial class QuantityItemReferenceAnalyzer
{
    private static bool MarkQuotedLootCodes(
        string text,
        IEnumerable<string> knownLootTables,
        string relativePath,
        Dictionary<string, List<string>> rootLootEvidence)
    {
        var matched = false;
        foreach (var table in knownLootTables)
        {
            if (ContainsQuotedToken(text, table))
            {
                AddEvidence(rootLootEvidence, table, EditorText.Format("QuantityItemReferenceAnalyzer_Matching_001", relativePath));
                matched = true;
            }
        }

        return matched;
    }

    private static bool MarkExactIdentities(
        string text,
        QuantityItemIndex index,
        Dictionary<string, List<string>> evidence,
        string message)
    {
        var matched = false;
        foreach (var identity in index.Identities)
        {
            if (ContainsQuotedToken(text, identity.Identity))
            {
                MarkResolved(identity.CatalogKeys, evidence, message);
                matched = true;
            }
        }

        return matched;
    }

    private static bool ContainsQuotedToken(string text, string token)
    {
        return text.Contains($"\"{token}\"", StringComparison.Ordinal);
    }

    private static bool TryParseJson(string text, out JsonDocument? document)
    {
        try
        {
            document = JsonDocument.Parse(
                text,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip
                });
            return true;
        }
        catch (JsonException)
        {
            document = null;
            return false;
        }
    }

    private static bool TryGetProperty(JsonElement item, string name, out JsonElement value)
    {
        return NativeJsonReader.TryGetProperty(item, name, out value);
    }

    private static string ReadString(JsonElement item, string name)
    {
        return TryGetProperty(item, name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static void MarkResolved(
        IEnumerable<string> keys,
        Dictionary<string, List<string>> evidence,
        string message)
    {
        foreach (var key in keys)
        {
            AddEvidence(evidence, key, message);
        }
    }

    private static void AddEvidence(
        Dictionary<string, List<string>> evidence,
        string key,
        string message)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        if (!evidence.TryGetValue(key, out var values))
        {
            values = [];
            evidence[key] = values;
        }

        if (!values.Contains(message, StringComparer.OrdinalIgnoreCase))
        {
            values.Add(message);
        }
    }

}
