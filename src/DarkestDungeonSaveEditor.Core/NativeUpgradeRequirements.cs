using System.Text;
using System.Text.Json;

namespace DarkestDungeonSaveEditor.Core;

internal static class NativeUpgradeRequirements
{
    private static readonly UTF8Encoding Utf8 = new(false, true);

    // 0x140470460 / 0x140470CD0, build 27890: each row assigns to the
    // first-byte code's map entry. Resolve winners before reading their fields.
    // Returned elements belong to the caller's JSON document.
    internal static IReadOnlyList<JsonElement> Read(JsonElement tree, string treeId)
    {
        if (!NativeJsonReader.TryGetProperty(tree, "requirements", out var requirements) ||
            requirements.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"Upgrade tree '{treeId}' is missing its requirements array.");

        var result = new Dictionary<byte, JsonElement>();
        foreach (var requirement in requirements.EnumerateArray())
        {
            if (requirement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException($"Upgrade tree '{treeId}' contains a non-object requirement.");
            try
            {
                var code = NativeJsonReader.ReadString(requirement, "code");
                if (code.Length == 0)
                    throw new InvalidDataException($"Upgrade tree '{treeId}' requirement is missing code.");
                result[Utf8.GetBytes(code)[0]] = requirement;
            }
            catch (Exception error) when (error is EncoderFallbackException or InvalidOperationException)
            {
                throw new InvalidDataException($"Upgrade tree '{treeId}' has an invalid UTF-8 requirement code.", error);
            }
        }
        return result.Values.ToArray();
    }
}
