using System.Text;

namespace DarkestDungeonSaveEditor.Core;

// Inventory::Item restore (0x1405D0910) and estate wallet restore
// (0x14055BD53) read identity fields into 64-byte C string buffers.
// Definitions may hash a longer input. Never truncate a selected definition
// into another item merely to make its save representation fit.
internal static class NativeInventoryIdentity
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    internal const int MaximumSaveBytes = 63;

    internal static string GetSaveIssue(string type, string id) =>
        GetFieldIssue(type, "type") is { Length: > 0 } typeIssue
            ? typeIssue : GetFieldIssue(id, "id");

    private static string GetFieldIssue(string value, string field)
    {
        if (value.Contains('\0'))
            return EditorText.Format("NativeInventoryIdentity_001", field);
        try
        {
            var bytes = Utf8.GetByteCount(value);
            return bytes > MaximumSaveBytes
                ? EditorText.Format("NativeInventoryIdentity_002", field, bytes, MaximumSaveBytes)
                : string.Empty;
        }
        catch (EncoderFallbackException)
        {
            return EditorText.Format("NativeInventoryIdentity_003", field);
        }
    }

    internal static void RequireWritable(string issue)
    {
        if (issue.Length > 0) throw new InvalidOperationException(issue);
    }
}
