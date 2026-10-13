// DSON layout/type rules derived from robojumper/DarkestDungeonSaveEditor v0.0.70.
// Copyright (c) 2018 robojumper. See LICENSE in this directory (MIT).
using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace DarkestDungeonSaveEditor.Core.Dson;

internal static class DsonReader
{
    internal static DsonDocument Read(byte[] bytes, CancellationToken token, int depth = 0)
    {
        token.ThrowIfCancellationRequested();
        if (depth >= DsonDocument.MaxDepth) throw Error("Nesting is too deep.");
        if (bytes.Length < 64 || U32(bytes, 0) != 0xB101) throw Error("Invalid or truncated header.");
        var headerLength = I32(bytes, 8);
        var objectSize = I32(bytes, 16);
        var objectCount = I32(bytes, 20);
        var objectOffset = I32(bytes, 24);
        var fieldCount = I32(bytes, 44);
        var fieldOffset = I32(bytes, 48);
        var dataLength = I32(bytes, 56);
        var dataOffset = I32(bytes, 60);
        if (headerLength < 64 || objectOffset != headerLength || objectOffset > bytes.Length ||
            objectCount < 0 || fieldCount < 0 || objectCount > fieldCount || dataLength < 0 ||
            objectSize != (long)objectCount * 16 || fieldOffset != (long)objectOffset + objectSize ||
            dataOffset != (long)fieldOffset + (long)fieldCount * 12 ||
            bytes.Length != (long)dataOffset + dataLength)
            throw Error("Inconsistent header lengths, counts or offsets.");

        var offsets = new int[fieldCount];
        for (var i = 0; i < fieldCount; i++)
        {
            token.ThrowIfCancellationRequested();
            offsets[i] = I32(bytes, fieldOffset + i * 12 + 4);
            if (offsets[i] < 0 || offsets[i] >= dataLength) throw Error("Field offset is out of range.");
        }
        Array.Sort(offsets);
        var ends = new Dictionary<int, int>(fieldCount);
        for (var i = 0; i < fieldCount; i++)
            if (!ends.TryAdd(offsets[i], i + 1 == fieldCount ? dataLength : offsets[i + 1]))
                throw Error("Two fields share a data offset.");
        if ((fieldCount == 0 && dataLength != 0) || (fieldCount > 0 && offsets[0] != 0))
            throw Error("Unreferenced data prefix.");

        var seenObjects = new HashSet<int>();
        var cursor = 0;
        var path = new List<string>();
        var roots = new List<DsonField>();
        while (cursor < fieldCount) roots.Add(ReadField(-1, depth));
        if (seenObjects.Count != objectCount) throw Error("Unreferenced object metadata.");
        return new DsonDocument(bytes, bytes[..objectOffset], roots);

        DsonField ReadField(int parent, int nesting)
        {
            token.ThrowIfCancellationRequested();
            if (nesting >= DsonDocument.MaxDepth || cursor >= fieldCount)
                throw Error("Incomplete or excessively nested object.");
            var fieldIndex = cursor++;
            var meta = fieldOffset + fieldIndex * 12;
            var offset = I32(bytes, meta + 4);
            var info = U32(bytes, meta + 8);
            var nameLength = (int)((info >> 2) & 0x1FF);
            var end = ends[offset];
            if (nameLength == 0 || nameLength > end - offset || bytes[dataOffset + offset + nameLength - 1] != 0)
                throw Error($"Invalid field name length at field {fieldIndex}.");
            var nameBytes = bytes.AsSpan(dataOffset + offset, nameLength - 1);
            string name;
            try { name = DsonDocument.Utf8.GetString(nameBytes); }
            catch (DecoderFallbackException ex) { throw Error("Invalid UTF-8 field name.", ex); }
            if (DsonTypes.Hash(nameBytes) != I32(bytes, meta)) throw Error($"Wrong name hash for '{name}'.");
            var start = offset + nameLength;
            var field = new DsonField
            {
                Name = name, Info = info, DataStart = start,
                Raw = bytes.AsSpan(dataOffset + start, end - start).ToArray()
            };
            path.Add(name);
            if ((info & 1) != 0)
            {
                field.Kind = DsonKind.Object;
                var objectIndex = (int)((info >> 11) & 0xFFFFF);
                if (objectIndex >= objectCount || !seenObjects.Add(objectIndex))
                    throw Error($"Invalid object index for '{name}'.");
                var obj = objectOffset + objectIndex * 16;
                var children = I32(bytes, obj + 8);
                var descendants = I32(bytes, obj + 12);
                if (I32(bytes, obj) != parent || I32(bytes, obj + 4) != fieldIndex ||
                    children < 0 || descendants < children || descendants > fieldCount - cursor)
                    throw Error($"Inconsistent object hierarchy at '{name}'.");
                var before = cursor;
                for (var i = 0; i < children; i++) field.Children.Add(ReadField(objectIndex, nesting + 1));
                if (cursor - before != descendants) throw Error($"Wrong descendant count for '{name}'.");
            }
            else
            {
                if (parent == -1) throw Error("A root field must be an object.");
                ReadValue(field, path, token, nesting);
            }
            path.RemoveAt(path.Count - 1);
            return field;
        }
    }

    private static void ReadValue(DsonField field, List<string> path, CancellationToken token, int depth)
    {
        var raw = field.Raw.AsSpan();
        var data = raw.Length >= field.Padding ? raw[field.Padding..] : ReadOnlySpan<byte>.Empty;
        var known = DsonTypes.KnownKind(path);
        if (known is DsonKind.Character)
        {
            if (raw.Length != 1 || raw[0] > 0x7F) { Opaque(field); return; }
            field.Kind = DsonKind.Character;
            field.Value = JsonValue.Create(((char)raw[0]).ToString());
        }
        else if (known is not null)
        {
            field.Kind = known.Value;
            var values = new JsonArray();
            var position = 0;
            switch (known)
            {
                case DsonKind.Float:
                    if (data.Length != 4) throw Error($"Invalid float length: {string.Join('/', path)}.");
                    var scalar = Single(data);
                    if (!float.IsFinite(scalar)) { Opaque(field); return; }
                    field.Value = FloatJson(scalar);
                    return;
                case DsonKind.Floats:
                    if (data.Length % 4 != 0) throw Error("Invalid float array length.");
                    for (; position < data.Length; position += 4)
                    {
                        token.ThrowIfCancellationRequested();
                        var number = Single(data[position..]);
                        if (!float.IsFinite(number)) { Opaque(field); return; }
                        values.Add(FloatJson(number));
                    }
                    break;
                case DsonKind.Integers:
                    if (data.Length < 4 || I32(data, 0) < 0 || (long)I32(data, 0) * 4 + 4 != data.Length)
                        throw Error("Invalid integer vector length.");
                    for (position = 4; position < data.Length; position += 4)
                    { token.ThrowIfCancellationRequested(); values.Add(I32(data, position)); }
                    break;
                case DsonKind.TwoIntegers:
                    if (data.Length != 8) throw Error("Invalid integer pair length.");
                    values.Add(I32(data, 0)); values.Add(I32(data, 4));
                    break;
                case DsonKind.Strings:
                    if (data.Length < 4) throw Error("Truncated string vector.");
                    var count = I32(data, 0);
                    if (count < 0 || count > (data.Length - 4) / 5) throw Error("Invalid string vector count.");
                    position = 4;
                    for (var i = 0; i < count; i++)
                    {
                        token.ThrowIfCancellationRequested();
                        position += DsonTypes.Padding(position);
                        if (position > data.Length - 4) throw Error("Truncated string vector entry.");
                        var length = I32(data, position);
                        position += 4;
                        if (length < 1 || length > data.Length - position || data[position + length - 1] != 0)
                            throw Error("Invalid string vector entry length.");
                        try { values.Add(DsonDocument.Utf8.GetString(data.Slice(position, length - 1))); }
                        catch (DecoderFallbackException) { Opaque(field); return; }
                        position += length;
                    }
                    if (position != data.Length) throw Error("Unconsumed string vector bytes.");
                    break;
            }
            field.Value = values;
        }
        else if (raw.Length == 1)
        {
            if (raw[0] is 0 or 1)
            { field.Kind = DsonKind.Boolean; field.Value = JsonValue.Create(raw[0] != 0); }
            else if (raw[0] is >= 0x20 and <= 0x7E)
            { field.Kind = DsonKind.Character; field.Value = JsonValue.Create(((char)raw[0]).ToString()); }
            else Opaque(field);
        }
        else if (data.Length == 8 && I32(data, 0) is 0 or 1 && I32(data, 4) is 0 or 1)
        {
            field.Kind = DsonKind.TwoBooleans;
            field.Value = new JsonArray(JsonValue.Create(I32(data, 0) != 0), JsonValue.Create(I32(data, 4) != 0));
        }
        else if (data.Length == 4)
        { field.Kind = DsonKind.Integer; field.Value = JsonValue.Create(I32(data, 0)); }
        else if (data.Length >= 4 && I32(data, 0) == data.Length - 4)
        {
            var payload = data[4..];
            if (payload.Length >= 4 && U32(payload, 0) == 0xB101)
            {
                field.Kind = DsonKind.Embedded;
                field.Embedded = Read(payload.ToArray(), token, depth + 1);
            }
            else if (payload.Length > 0 && payload[^1] == 0)
            {
                try
                {
                    field.Value = JsonValue.Create(DsonDocument.Utf8.GetString(payload[..^1]));
                    field.Kind = DsonKind.String;
                }
                catch (DecoderFallbackException) { Opaque(field); }
            }
            else Opaque(field);
        }
        else Opaque(field);
    }

    private static void Opaque(DsonField field)
    {
        field.Kind = DsonKind.Opaque;
        field.Value = new JsonObject { ["$dsonOpaque"] = Convert.ToHexString(field.Raw) };
    }

    private static float Single(ReadOnlySpan<byte> data) => BitConverter.Int32BitsToSingle(I32(data, 0));
    // Existing editing workflows preserve decimal tokens for native float fields.
    private static JsonNode FloatJson(float value)
    {
        var text = value.ToString("R", CultureInfo.InvariantCulture);
        if (!text.Contains('.') && !text.Contains('E')) text += ".0";
        return JsonNode.Parse(text)!;
    }
    internal static int I32(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadInt32LittleEndian(data[offset..]);
    internal static uint U32(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
    internal static InvalidDataException Error(string message, Exception? inner = null) => new("DSON: " + message, inner);
}
