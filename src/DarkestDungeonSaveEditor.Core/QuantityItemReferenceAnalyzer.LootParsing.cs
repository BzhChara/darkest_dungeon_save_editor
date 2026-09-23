using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DarkestDungeonSaveEditor.Core;

internal static partial class QuantityItemReferenceAnalyzer
{
    private static readonly uint LootItemEntryHash = Loc2LocalizationReader.HashName("item");
    private static readonly uint LootTableEntryHash = Loc2LocalizationReader.HashName("table");

    private static bool ParseLootFile(
        ScannedContentFile file,
        QuantityItemIndex index,
        LootTableLibrary lootTables,
        Dictionary<string, List<string>> incompleteEvidence,
        List<string> issues,
        bool selectionVerified)
    {
        if (!TryParseJson(file.Text, out var document) || document is null)
        {
            MarkLootParseFallback(file, index, incompleteEvidence, issues);
            return false;
        }

        using (document)
        {
            var rootObject = document.RootElement;
            if (rootObject.ValueKind != JsonValueKind.Object ||
                !TryGetProperty(rootObject, "loot_tables", out var tables))
            {
                return true;
            }

            if (tables.ValueKind != JsonValueKind.Array)
            {
                MarkLootParseFallback(file, index, incompleteEvidence, issues);
                return false;
            }

            foreach (var tableNode in tables.EnumerateArray())
            {
                if (tableNode.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                // Native tables keep ordered variants per hash. Their name buffer
                // is 64 bytes, and the hash is calculated after that copy.
                var rawId = NativeJsonReader.ReadString(tableNode, "id");
                var code = ReadLootCode(rawId, 63);
                lootTables.Codes.Add(rawId);
                lootTables.Codes.Add(code.Name);
                if (!lootTables.Tables.TryGetValue(code.Hash, out var variants))
                    lootTables.Tables[code.Hash] = variants = [];
                var table = new LootTableVariant(ReadLootContext(tableNode), file.File.RelativePath, selectionVerified);
                // Even an empty/zero-weight first variant can shadow later ones.
                variants.Add(table);

                if (!TryGetProperty(tableNode, "entries", out var entries) ||
                    entries.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var entry in entries.EnumerateArray())
                {
                    if (entry.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var usable = ReadLootWeight(entry);
                    if (usable == false) continue;
                    var entryType = ReadLootCode(ReadString(entry, "type")).Hash;
                    if (!TryGetProperty(entry, "data", out var data) ||
                        data.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    if (entryType == LootItemEntryHash)
                    {
                        // ItemEntry::LoadFromJsonValue calls Item's JSON reader:
                        // copy each identity to a 64-byte C buffer, then hash
                        // those bytes. Keep the selected definition unchanged.
                        var itemType = ReadLootCode(ReadString(data, "type"), 63).Hash;
                        var itemId = ReadLootCode(ReadString(data, "id"), 63).Hash;
                        foreach (var definition in index.ResolveLootItemHashes(itemType, itemId))
                        {
                            if (definition.HasProviderConflict) table.ConflictedItemKeys.Add(definition.CatalogKey);
                            if (usable != true) table.UncertainItemKeys.Add(definition.CatalogKey);
                            else if (!definition.HasProviderConflict) table.ItemKeys.Add(definition.CatalogKey);
                        }
                    }
                    else if (entryType == LootTableEntryHash)
                    {
                        var nested = ReadString(data, "table");
                        if (!string.IsNullOrWhiteSpace(nested))
                        {
                            (usable == true ? table.NestedTables : table.UncertainNestedTables).Add(ReadLootCode(nested, 63));
                        }
                    }
                }
            }
        }

        return true;
    }

    private static void MarkLootParseFallback(
        ScannedContentFile file,
        QuantityItemIndex index,
        Dictionary<string, List<string>> incompleteEvidence,
        List<string> issues)
    {
        _ = MarkExactIdentities(
            file.Text,
            index,
            incompleteEvidence,
            $"掉落文件无法完整解析：{file.File.RelativePath}");
        issues.Add($"Quantity-item reference scan could not parse active loot file: {file.File.Path}");
    }

    private static bool? ReadLootWeight(JsonElement entry)
    {
        // 0x14044333B..0x1404433DA: first member, number -> float32,
        // skip missing/nonpositive weights before creating an entry.
        if (!NativeJsonReader.TryGetProperty(entry, "chances", out _)) return false;
        var weight = NativeJsonReader.ReadFloat(entry, "chances");
        if (weight is not { } number) return null;
        // Negative overflow becomes -infinity and still takes the native skip
        // branch. Positive overflow remains insufficient evidence of a roll.
        if (number <= 0) return false;
        return double.IsFinite(number) ? true : null;
    }

    private static LootContext ReadLootContext(JsonElement table)
    {
        // Unsigned fields retain these native constructor defaults when absent
        // or of another JSON type (including floating-point spellings).
        // The native integer parser tags -0 as unsigned zero as well; a direct
        // TryGetUInt32 would reject that spelling and retain the wrong default.
        static uint UInt(JsonElement node, string key, uint defaultValue = 0) =>
            NativeJsonReader.TryGetProperty(node, key, out var value) &&
            value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) && number is >= 0 and <= uint.MaxValue
                ? (uint)number : defaultValue;
        static LootRange Filter(uint value) => value == 0 ? LootRange.Any : new(value, value);
        return new(Filter(UInt(table, "difficulty")),
            Filter(ReadLootCode(NativeJsonReader.ReadCString(table, "dungeon")).Hash),
            Filter(ReadLootCode(NativeJsonReader.ReadCString(table, "infestation_sequence_element")).Hash),
            new(UInt(table, "week_min"), UInt(table, "week_max", uint.MaxValue)));
    }

    private static LootCode ReadLootCode(string value, int maximumBytes = int.MaxValue)
    {
        var bytes = Encoding.UTF8.GetBytes(NativeJsonReader.CString(value));
        var count = Math.Min(bytes.Length, maximumBytes);
        var hash = 0u;
        for (var i = 0; i < count; i++) hash = unchecked(hash * 53u + bytes[i]);
        // Hash raw bytes even if a native truncation splits a UTF-8 character.
        // Name is diagnostic text only; it is never used for native lookup.
        return new(hash, Encoding.UTF8.GetString(bytes, 0, count));
    }

}
