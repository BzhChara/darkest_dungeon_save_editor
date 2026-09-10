using System.Text;
using System.Text.Json;

namespace DarkestDungeonSaveEditor.Core;

// Resource JSON member lookup, x64 build 27890: 0x14028E980 returns the
// first member, including a wrong-typed first member. Save JSON is separate.
internal static class NativeJsonReader
{
    private static readonly UTF8Encoding Utf8 = new(false, true);

    internal static bool TryGetProperty(JsonElement node, string name, out JsonElement value)
    {
        if (node.ValueKind == JsonValueKind.Object)
            foreach (var member in node.EnumerateObject())
                if (member.NameEquals(name))
                {
                    value = member.Value;
                    return true;
                }
        value = default;
        return false;
    }

    internal static string ReadString(JsonElement node, string name) =>
        TryGetProperty(node, name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty : string.Empty;

    internal static string CString(string value)
    {
        var end = value.IndexOf('\0');
        return end < 0 ? value : value[..end];
    }

    internal static string ReadCString(JsonElement node, string name) => CString(ReadString(node, name));

    internal static string ReadBoundedString(JsonElement node, string name, int payloadBytes)
    {
        var bytes = Utf8.GetBytes(ReadCString(node, name));
        return Utf8.GetString(bytes, 0, Math.Min(bytes.Length, payloadBytes));
    }

    internal static double? ReadFloat(JsonElement node, string name) =>
        TryGetProperty(node, name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)
            ? (double)(float)number : null;

    internal static IEnumerable<JsonElement> Array(JsonElement node, string name) =>
        TryGetProperty(node, name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray() : [];
}
