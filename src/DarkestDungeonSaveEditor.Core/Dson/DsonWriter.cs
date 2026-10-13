// DSON layout/type rules derived from robojumper/DarkestDungeonSaveEditor v0.0.70.
// Copyright (c) 2018 robojumper. See LICENSE in this directory (MIT).
using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DarkestDungeonSaveEditor.Core.Dson;

internal sealed class DsonWriter(CancellationToken token)
{
    private readonly MemoryStream _data = new();
    private readonly List<ObjectEntry> _objects = [];
    private readonly List<(int Hash, int Offset, uint Info)> _fields = [];
    private readonly List<string> _path = [];

    internal static byte[] Encode(JsonObject desired, DsonDocument? source, CancellationToken token, int depth = 0)
    {
        token.ThrowIfCancellationRequested();
        if (depth >= DsonDocument.MaxDepth) throw DsonReader.Error("Nesting is too deep.");
        // A JSON view cannot represent every native detail. An unchanged view must
        // keep the entire original file, including physical order and padding.
        if (source is not null && DsonDocument.ViewEquals(source.ToJson(token), desired)) return source.Original;
        var writer = new DsonWriter(token);
        using (writer._data)
        {
            writer.WriteObject(desired, source?.Fields, -1, depth);
            return writer.Finish(source?.Header);
        }
    }

    private void WriteObject(JsonObject desired, List<DsonField>? originals, int parent, int depth)
    {
        token.ThrowIfCancellationRequested();
        if (depth >= DsonDocument.MaxDepth) throw DsonReader.Error("Nesting is too deep.");
        var consumed = new HashSet<string>(StringComparer.Ordinal);
        var groups = originals?.ToLookup(field => field.Name, StringComparer.Ordinal);
        if (originals is not null)
        foreach (var source in originals)
        {
            token.ThrowIfCancellationRequested();
            var exists = desired.TryGetPropertyValue(source.Name, out var value);
            if (!exists && source.Kind == DsonKind.Opaque)
                throw DsonReader.Error($"Cannot remove opaque native data at '{Location(source.Name)}'.");
            var group = groups![source.Name];
            if (group.Skip(1).Any())
            {
                if (!exists || !DsonDocument.ViewEquals(group.First().ToJson(token), value))
                    throw DsonReader.Error($"Cannot edit or remove duplicate field '{Location(source.Name)}' unambiguously.");
                // Preserve every occurrence, even when later occurrences have a
                // different value from the first occurrence exposed in the JSON view.
                value = source.ToJson(token);
            }
            consumed.Add(source.Name);
            if (exists) WriteField(source.Name, value, source, parent, depth);
        }
        foreach (var pair in desired)
            if (!consumed.Contains(pair.Key)) WriteField(pair.Key, pair.Value, null, parent, depth);
    }

    private void WriteField(string name, JsonNode? value, DsonField? source, int parent, int depth)
    {
        token.ThrowIfCancellationRequested();
        var nameBytes = DsonDocument.Utf8.GetBytes(name);
        if (nameBytes.Length > 510) throw DsonReader.Error($"Field name exceeds 510 UTF-8 bytes: '{Location(name)}'.");
        _path.Add(name);
        var kind = source?.Kind ?? InferKind(value, name);
        if (parent == -1 && kind != DsonKind.Object) throw DsonReader.Error("A root field must be an object.");
        var offset = checked((int)_data.Position);
        var fieldIndex = _fields.Count;
        var info = ((source?.Info ?? 0) & ~0x7FDU) | ((uint)(nameBytes.Length + 1) << 2);
        var objectIndex = _objects.Count;
        if (kind == DsonKind.Object)
        {
            if (objectIndex > 0xFFFFF) throw DsonReader.Error("Too many objects for the native index field.");
            info = (info & 0x800007FEU) | 1U | ((uint)objectIndex << 11);
        }
        _fields.Add((DsonTypes.Hash(nameBytes), offset, info));
        _data.Write(nameBytes);
        _data.WriteByte(0);
        if (kind == DsonKind.Object)
        {
            if (value is not JsonObject obj) throw InvalidValue("object");
            var entry = new ObjectEntry(parent, fieldIndex);
            _objects.Add(entry);
            if (source is not null) CopyUnaligned(source);
            var before = _fields.Count;
            WriteObject(obj, source?.Children, objectIndex, depth + 1);
            entry.Descendants = _fields.Count - before;
            // Direct scalar children have no parent in meta2; count from the
            // emitted preorder, skipping each object's entire subtree.
            entry.Children = 0;
            for (var i = before; i < _fields.Count; i++)
            {
                entry.Children++;
                if ((_fields[i].Info & 1) != 0)
                    i += _objects[(int)((_fields[i].Info >> 11) & 0xFFFFF)].Descendants;
            }
        }
        else if (source is not null && DsonDocument.ViewEquals(source.ToJson(token), value))
        {
            if (kind is DsonKind.Boolean or DsonKind.Character or DsonKind.Opaque) CopyUnaligned(source);
            else
            {
                Align(source);
                _data.Write(source.Raw.AsSpan(source.Padding));
            }
        }
        else if (kind == DsonKind.Opaque)
            throw DsonReader.Error($"Cannot edit opaque native data at '{Location()}'.");
        else if (kind == DsonKind.Embedded)
        {
            if (value is not JsonObject embedded) throw InvalidValue("embedded object");
            var bytes = Encode(embedded, source?.Embedded, token, depth + 1);
            Align(source); Int(bytes.Length); _data.Write(bytes);
        }
        else WriteValue(kind, value, source);
        _path.RemoveAt(_path.Count - 1);
    }

    private DsonKind InferKind(JsonNode? value, string name)
    {
        if (value is JsonObject marker && marker.Count == 1 && marker.ContainsKey("$dsonOpaque"))
            throw DsonReader.Error($"An opaque view requires its original native field at '{Location()}'; it cannot be created or moved as JSON.");
        if (value is JsonObject) return name is "raw_data" or "static_save" ? DsonKind.Embedded : DsonKind.Object;
        if (DsonTypes.KnownKind(_path) is { } known) return known;
        return value?.GetValueKind() switch
        {
            JsonValueKind.True or JsonValueKind.False => DsonKind.Boolean,
            JsonValueKind.String => DsonKind.String,
            JsonValueKind.Number => DsonKind.Integer,
            JsonValueKind.Array => DsonKind.TwoBooleans,
            _ => throw InvalidValue("a supported DSON value (null is not a native type)")
        };
    }

    private void WriteValue(DsonKind kind, JsonNode? value, DsonField? source)
    {
        if (kind is not (DsonKind.Boolean or DsonKind.Character)) Align(source);
        switch (kind)
        {
            case DsonKind.Boolean: _data.WriteByte(Bool(value) ? (byte)1 : (byte)0); break;
            case DsonKind.Character:
                var text = Text(value);
                if (text.Length != 1 || text[0] > 0x7F ||
                    (DsonTypes.KnownKind(_path) != DsonKind.Character && text[0] is < ' ' or > '~'))
                    throw InvalidValue("one ASCII character recognizable at this native field path");
                _data.WriteByte((byte)text[0]);
                break;
            case DsonKind.Integer: Int(Integer(value)); break;
            case DsonKind.Float: Float(value); break;
            case DsonKind.String: String(value); break;
            case DsonKind.Integers:
            case DsonKind.Strings:
            case DsonKind.Floats:
            case DsonKind.TwoIntegers:
            case DsonKind.TwoBooleans:
                if (value is not JsonArray array) throw InvalidValue("array");
                if (kind is DsonKind.TwoIntegers or DsonKind.TwoBooleans && array.Count != 2)
                    throw InvalidValue("exactly two array elements");
                if (kind is DsonKind.Integers or DsonKind.Strings) Int(array.Count);
                foreach (var item in array)
                {
                    token.ThrowIfCancellationRequested();
                    switch (kind)
                    {
                        case DsonKind.Integers:
                        case DsonKind.TwoIntegers: Int(Integer(item)); break;
                        case DsonKind.Floats: Float(item); break;
                        case DsonKind.TwoBooleans: Int(Bool(item) ? 1 : 0); break;
                        case DsonKind.Strings: Align(null); String(item); break;
                    }
                }
                break;
            default: throw InvalidValue("supported native type");
        }
    }

    private void CopyUnaligned(DsonField source)
    {
        if (source.Kind is DsonKind.Opaque or DsonKind.Object && source.Raw.Length > 0 &&
            (_data.Position & 3) != (source.DataStart & 3))
            throw DsonReader.Error($"Cannot relocate opaque data with unknown alignment at '{Location()}'.");
        _data.Write(source.Raw);
    }

    private void Align(DsonField? source)
    {
        var padding = DsonTypes.Padding(checked((int)_data.Position));
        if (source is not null && source.Padding == padding) _data.Write(source.Raw.AsSpan(0, padding));
        else for (var i = 0; i < padding; i++) _data.WriteByte(0);
    }

    private void String(JsonNode? value)
    {
        // Hash values are numeric in the editor. A literal ### prefix is text,
        // never an instruction to turn a string into a different native type.
        var bytes = DsonDocument.Utf8.GetBytes(Text(value));
        Int(checked(bytes.Length + 1)); _data.Write(bytes); _data.WriteByte(0);
    }

    private static string Text(JsonNode? value) => value?.GetValueKind() == JsonValueKind.String
        ? value.GetValue<string>() : throw DsonReader.Error("Expected a string.");

    private bool Bool(JsonNode? value) => value?.GetValueKind() switch
    {
        JsonValueKind.True => true, JsonValueKind.False => false, _ => throw InvalidValue("boolean")
    };

    private int Integer(JsonNode? value)
    {
        if (value?.GetValueKind() == JsonValueKind.Number &&
            JsonSerializer.SerializeToElement(value).TryGetInt32(out var number)) return number;
        throw InvalidValue("32-bit integer; new float fields require a known DSON type");
    }

    private void Float(JsonNode? value)
    {
        if (value?.GetValueKind() != JsonValueKind.Number ||
            !JsonSerializer.SerializeToElement(value).TryGetSingle(out var number) || !float.IsFinite(number))
            throw InvalidValue("finite 32-bit float");
        Int(BitConverter.SingleToInt32Bits(number));
    }

    private void Int(int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value); _data.Write(bytes);
    }

    private byte[] Finish(byte[]? originalHeader)
    {
        token.ThrowIfCancellationRequested();
        var header = originalHeader is null ? new byte[64] : (byte[])originalHeader.Clone();
        var meta2 = checked(header.Length + _objects.Count * 16);
        var dataOffset = checked(meta2 + _fields.Count * 12);
        var dataLength = checked((int)_data.Length);
        var result = new byte[checked(dataOffset + dataLength)];
        header.CopyTo(result, 0);
        Put(0, 0xB101); Put(8, header.Length); Put(16, _objects.Count * 16);
        Put(20, _objects.Count); Put(24, header.Length); Put(44, _fields.Count);
        Put(48, meta2); Put(56, dataLength); Put(60, dataOffset);
        for (var i = 0; i < _objects.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var entry = _objects[i]; var offset = header.Length + i * 16;
            Put(offset, entry.Parent); Put(offset + 4, entry.Field);
            Put(offset + 8, entry.Children); Put(offset + 12, entry.Descendants);
        }
        for (var i = 0; i < _fields.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var entry = _fields[i]; var offset = meta2 + i * 12;
            Put(offset, entry.Hash); Put(offset + 4, entry.Offset); Put(offset + 8, unchecked((int)entry.Info));
        }
        _data.GetBuffer().AsSpan(0, dataLength).CopyTo(result.AsSpan(dataOffset));
        return result;
        void Put(int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(offset), value);
    }

    private string Location(string? name = null) => string.Join('/', name is null ? _path : _path.Append(name));
    private InvalidDataException InvalidValue(string expected) => DsonReader.Error($"Expected {expected} at '{Location()}'.");
    private sealed class ObjectEntry(int parent, int field)
    {
        internal int Parent { get; } = parent;
        internal int Field { get; } = field;
        internal int Children { get; set; }
        internal int Descendants { get; set; }
    }
}
