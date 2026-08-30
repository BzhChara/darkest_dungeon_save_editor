using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DarkestDungeonSaveEditor.Core;

internal static class JsonSupport
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    internal static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    internal static JsonObject ReadObject(string path)
    {
        var node = JsonNode.Parse(
            File.ReadAllText(path, Encoding.UTF8),
            documentOptions: new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });

        return node as JsonObject
            ?? throw new InvalidDataException($"Expected a JSON object: {path}");
    }

    internal static void WriteObject(string path, JsonObject value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException($"Output path has no directory: {path}"));
        File.WriteAllText(path, value.ToJsonString(SerializerOptions), Utf8NoBom);
    }

    internal static JsonObject RequireObject(JsonObject root, params string[] segments)
    {
        JsonObject current = root;
        foreach (var segment in segments)
        {
            current = current[segment] as JsonObject
                ?? throw new InvalidDataException($"Required save object is missing: {string.Join('.', segments)}");
        }

        return current;
    }

    internal static string ReadString(JsonObject value, string name)
    {
        return value[name]?.GetValue<string>()?.Trim() ?? string.Empty;
    }

    internal static int? ReadInt(JsonObject value, string name)
    {
        if (value[name] is not JsonValue node)
        {
            return null;
        }

        if (node.TryGetValue<int>(out var integer))
        {
            return integer;
        }

        return node.TryGetValue<long>(out var longValue) && longValue is >= int.MinValue and <= int.MaxValue
            ? (int)longValue
            : null;
    }
}
