using System.Buffers.Binary;
using System.Text;

namespace DarkestDungeonSaveEditor.Core;

internal static class Loc2LocalizationReader
{
    private const int HeaderSize = 12;
    private const int BucketTableSize = 4096;
    private const int HashRecordOffset = HeaderSize + BucketTableSize;
    private const int HashRecordSize = 12;
    private const int GroupRecordSize = 8;
    private const int ValueRecordSize = 12;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static IReadOnlyDictionary<string, string> Read(
        string path,
        IReadOnlyCollection<string> requestedKeys)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(requestedKeys);

        var keysByHash = requestedKeys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.Ordinal)
            .GroupBy(HashName)
            .ToDictionary(group => group.Key, group => group.ToArray());
        if (keysByHash.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var bytes = File.ReadAllBytes(path);
        var layout = ReadLayout(bytes, path);
        var decodedValues = ReadAndValidateValues(bytes, path, layout);
        var index = ReadAndValidateIndex(bytes, path, layout);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in keysByHash)
        {
            if (!index.GroupByHash.TryGetValue(pair.Key, out var groupIndex))
            {
                continue;
            }

            var value = ReadFirstNonEmptyValue(decodedValues, index.Groups[groupIndex]);
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            foreach (var key in pair.Value)
            {
                values[key] = value;
            }
        }

        return values;
    }

    internal static uint HashName(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var hash = 0u;
        foreach (var valueByte in Encoding.UTF8.GetBytes(value))
        {
            unchecked
            {
                hash = hash * 53u + valueByte;
            }
        }

        return hash;
    }

    private static Loc2Layout ReadLayout(byte[] bytes, string path)
    {
        if (bytes.Length < HashRecordOffset)
        {
            throw new InvalidDataException(
                $"LOC2 '{path}' is too short for its fixed header and bucket table.");
        }

        var groupTableOffset = ReadOffset(bytes, 0, path, "group table");
        var valueTableOffset = ReadOffset(bytes, 4, path, "value table");
        var stringDataOffset = ReadOffset(bytes, 8, path, "string data");
        if (groupTableOffset < HashRecordOffset ||
            groupTableOffset > valueTableOffset ||
            valueTableOffset > stringDataOffset ||
            stringDataOffset > bytes.Length)
        {
            throw new InvalidDataException(
                $"LOC2 '{path}' has invalid table offsets " +
                $"{groupTableOffset}, {valueTableOffset}, {stringDataOffset} for length {bytes.Length}.");
        }

        var hashBytes = groupTableOffset - HashRecordOffset;
        var groupBytes = valueTableOffset - groupTableOffset;
        var valueBytes = stringDataOffset - valueTableOffset;
        if (hashBytes % HashRecordSize != 0 ||
            groupBytes % GroupRecordSize != 0 ||
            valueBytes % ValueRecordSize != 0)
        {
            throw new InvalidDataException($"LOC2 '{path}' has misaligned table lengths.");
        }

        var hashRecordCount = hashBytes / HashRecordSize;
        var groupRecordCount = groupBytes / GroupRecordSize;
        if (hashRecordCount != groupRecordCount)
        {
            throw new InvalidDataException(
                $"LOC2 '{path}' has {hashRecordCount} hash records but {groupRecordCount} value groups.");
        }

        return new Loc2Layout(
            groupTableOffset,
            valueTableOffset,
            stringDataOffset,
            hashRecordCount,
            groupRecordCount,
            valueBytes / ValueRecordSize);
    }

    private static string[] ReadAndValidateValues(
        byte[] bytes,
        string path,
        Loc2Layout layout)
    {
        var values = new string[layout.ValueRecordCount];
        for (var valueIndex = 0; valueIndex < layout.ValueRecordCount; valueIndex++)
        {
            var recordOffset = checked(layout.ValueTableOffset + valueIndex * ValueRecordSize);
            var relativeOffset = ReadUInt32(bytes, recordOffset);
            var byteLength = ReadUInt32(bytes, recordOffset + 4);
            if ((ulong)relativeOffset + byteLength > (ulong)(bytes.Length - layout.StringDataOffset))
            {
                throw new InvalidDataException(
                    $"LOC2 '{path}' string {valueIndex} points outside the string data.");
            }

            if (byteLength == 0)
            {
                throw new InvalidDataException(
                    $"LOC2 '{path}' string {valueIndex} has no NUL terminator.");
            }

            var absoluteOffset = checked(layout.StringDataOffset + (int)relativeOffset);
            var length = checked((int)byteLength);
            if (bytes[absoluteOffset + length - 1] != 0)
            {
                throw new InvalidDataException(
                    $"LOC2 '{path}' string {valueIndex} is not NUL-terminated.");
            }

            values[valueIndex] = DecodeValue(
                bytes.AsSpan(absoluteOffset, length - 1),
                path,
                (uint)valueIndex);
        }

        return values;
    }

    private static Loc2Index ReadAndValidateIndex(
        byte[] bytes,
        string path,
        Loc2Layout layout)
    {
        var groups = new ValueGroup[layout.GroupRecordCount];
        for (var groupIndex = 0; groupIndex < layout.GroupRecordCount; groupIndex++)
        {
            var groupOffset = checked(layout.GroupTableOffset + groupIndex * GroupRecordSize);
            var firstValueIndex = ReadUInt32(bytes, groupOffset);
            var valueCount = ReadUInt32(bytes, groupOffset + 4);
            if ((ulong)firstValueIndex + valueCount > (ulong)layout.ValueRecordCount)
            {
                throw new InvalidDataException(
                    $"LOC2 '{path}' value range {firstValueIndex}+{valueCount} is outside the value table.");
            }

            groups[groupIndex] = new ValueGroup((int)firstValueIndex, (int)valueCount);
        }

        var groupByHash = new Dictionary<uint, int>();
        for (var recordIndex = 0; recordIndex < layout.HashRecordCount; recordIndex++)
        {
            var recordOffset = HashRecordOffset + recordIndex * HashRecordSize;
            var hash = ReadUInt32(bytes, recordOffset);
            var groupIndex = ReadUInt32(bytes, recordOffset + 4);
            if (groupIndex >= layout.GroupRecordCount)
            {
                throw new InvalidDataException(
                    $"LOC2 '{path}' hash group {groupIndex} is outside the group table.");
            }

            if (!groupByHash.TryAdd(hash, (int)groupIndex) && hash != 0)
            {
                throw new InvalidDataException(
                    $"LOC2 '{path}' contains duplicate hash records for 0x{hash:X8}.");
            }
        }

        return new Loc2Index(groups, groupByHash);
    }

    private static string ReadFirstNonEmptyValue(
        IReadOnlyList<string> values,
        ValueGroup group)
    {
        for (var offset = 0; offset < group.ValueCount; offset++)
        {
            var value = values[group.FirstValueIndex + offset];
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    private static string DecodeValue(
        ReadOnlySpan<byte> valueBytes,
        string path,
        uint valueIndex)
    {
        ReadOnlySpan<byte> colourOpen = "<c>"u8;
        ReadOnlySpan<byte> colourClose = "</c>"u8;
        var filtered = new List<byte>(valueBytes.Length);
        for (var index = 0; index < valueBytes.Length;)
        {
            var remaining = valueBytes[index..];
            if (remaining.StartsWith(colourOpen))
            {
                const int compiledColourTagLength = 3 + 6;
                if (remaining.Length < compiledColourTagLength)
                {
                    throw new InvalidDataException(
                        $"LOC2 '{path}' string {valueIndex} has a truncated compiled colour tag.");
                }

                index += compiledColourTagLength;
                continue;
            }

            if (remaining.StartsWith(colourClose))
            {
                index += colourClose.Length;
                continue;
            }

            filtered.Add(valueBytes[index]);
            index++;
        }

        return StrictUtf8.GetString(filtered.ToArray());
    }

    private static int ReadOffset(byte[] bytes, int offset, string path, string label)
    {
        var value = ReadUInt32(bytes, offset);
        if (value > int.MaxValue)
        {
            throw new InvalidDataException($"LOC2 '{path}' {label} offset is too large: {value}.");
        }

        return (int)value;
    }

    private static uint ReadUInt32(byte[] bytes, int offset)
    {
        if (offset < 0 || offset > bytes.Length - sizeof(uint))
        {
            throw new InvalidDataException($"LOC2 read at offset {offset} is outside the file.");
        }

        return BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, sizeof(uint)));
    }

    private sealed record Loc2Layout(
        int GroupTableOffset,
        int ValueTableOffset,
        int StringDataOffset,
        int HashRecordCount,
        int GroupRecordCount,
        int ValueRecordCount);

    private sealed record Loc2Index(
        IReadOnlyList<ValueGroup> Groups,
        IReadOnlyDictionary<uint, int> GroupByHash);

    private readonly record struct ValueGroup(int FirstValueIndex, int ValueCount);
}
