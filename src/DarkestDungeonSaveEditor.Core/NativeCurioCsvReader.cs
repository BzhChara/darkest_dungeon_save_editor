using System.Text;

namespace DarkestDungeonSaveEditor.Core;

// Build 27890: 0x14038DCA0, with the physical-line readers in 0x1404D9CF0/0x1404D8DC0.
// This is deliberately not RFC CSV: quotes toggle, only trailing ASCII spaces are
// removed, and an omitted column can retain its previous buffer in the mapping reader.
internal static class NativeCurioCsvReader
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal sealed record Row(int Line, int Count, string[] Fields);

    public static IReadOnlyList<Row> Read(string path, int columns, bool mapping)
        => Read(File.ReadAllBytes(path), path, columns, mapping);

    internal static IReadOnlyList<Row> ReadFromText(string text, string path, int columns, bool mapping)
        => Read(StrictUtf8.GetBytes(text), path, columns, mapping);

    internal static IEnumerable<IReadOnlyList<Row>> TypeBlocks(IEnumerable<Row> rows)
    {
        List<Row>? block = null;
        var itemSection = false;
        foreach (var row in rows)
        {
            if (block is null)
            {
                if (row.Fields[2] == "ID STRING") { block = []; itemSection = false; }
                continue;
            }
            if (itemSection && row.Fields[1].Length > 0)
            {
                yield return block;
                block = null; // The separator itself is consumed by the native block reader.
                continue;
            }
            block.Add(row);
            if (row.Fields[4] == "ITEM") itemSection = true;
        }
        if (block is not null) yield return block;
    }

    private static IReadOnlyList<Row> Read(byte[] bytes, string path, int columns, bool mapping)
    {
        var rows = new List<Row>();
        var fields = Enumerable.Repeat(string.Empty, columns).ToArray();
        var offset = 0;
        var line = 1;
        var first = true;
        while (offset < bytes.Length)
        {
            var start = offset;
            var sourceLine = line;
            while (offset < bytes.Length && offset - start < 4095)
                if (bytes[offset++] == '\n') { line++; break; }
            // Curio mappings discard the first physical chunk unconditionally.
            if (mapping && first) { first = false; continue; }
            first = false;
            if (!mapping) Array.Fill(fields, string.Empty);
            var count = 0;
            var quoted = false;
            var field = new List<byte>();
            void Emit()
            {
                if (field.Count > 511)
                    throw new InvalidDataException($"奇物 CSV 字段超过原生缓冲区：{path}:{sourceLine}");
                while (field.Count > 0 && field[^1] == 0x20) field.RemoveAt(field.Count - 1);
                try { fields[count++] = StrictUtf8.GetString(field.ToArray()); }
                catch (DecoderFallbackException error)
                { throw new InvalidDataException($"奇物 CSV 字段不是完整 UTF-8：{path}:{sourceLine}", error); }
                field.Clear();
            }
            for (var index = start; index < offset; index++)
            {
                var value = bytes[index];
                if (value is 0 or (byte)'\r' or (byte)'\n') break;
                if (value == '"') { quoted = !quoted; continue; }
                if (value == ',' && !quoted)
                {
                    Emit();
                    if (count == columns) break;
                }
                else field.Add(value);
            }
            if (count < columns) Emit();
            rows.Add(new Row(sourceLine, count, fields.ToArray()));
        }
        return rows;
    }
}
