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
            return $"存档物品的 {field} 包含 NUL，游戏读取时会提前结束，无法安全修改。";
        try
        {
            var bytes = Utf8.GetByteCount(value);
            return bytes > MaximumSaveBytes
                ? $"存档物品的 {field} 为 {bytes} 个 UTF-8 字节，超过游戏可读取的 {MaximumSaveBytes} 字节，无法安全修改。"
                : string.Empty;
        }
        catch (EncoderFallbackException)
        {
            return $"存档物品的 {field} 无法完整编码为 UTF-8，无法安全修改。";
        }
    }

    internal static void RequireWritable(string issue)
    {
        if (issue.Length > 0) throw new InvalidOperationException(issue);
    }
}
