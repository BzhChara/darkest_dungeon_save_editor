using System.Buffers.Binary;

namespace DarkestDungeonSaveEditor.Core;

internal static class LocLocalizationReader
{
    private const int HashRecordOffset = 8 + 4096;
    private const int RecordSize = 12;

    public static IReadOnlyDictionary<string, string> Read(
        string path,
        IReadOnlyCollection<string> requestedKeys,
        ICollection<string> issues)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(requestedKeys);
        ArgumentNullException.ThrowIfNull(issues);
        var keysByHash = requestedKeys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.Ordinal)
            .GroupBy(Loc2LocalizationReader.HashName)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (keysByHash.Count == 0)
        {
            return result;
        }

        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < HashRecordOffset)
        {
            throw new InvalidDataException($"LOC '{path}' is too short for its fixed header and bucket table.");
        }

        var valueTableOffset = ReadOffset(bytes, 0, path);
        var stringDataOffset = ReadOffset(bytes, 4, path);
        if (valueTableOffset < HashRecordOffset || valueTableOffset > stringDataOffset ||
            stringDataOffset > bytes.Length)
        {
            throw new InvalidDataException($"LOC '{path}' has invalid table offsets {valueTableOffset}, {stringDataOffset}.");
        }

        if ((valueTableOffset - HashRecordOffset) % RecordSize != 0 ||
            (stringDataOffset - valueTableOffset) % RecordSize != 0)
        {
            throw new InvalidDataException($"LOC '{path}' has misaligned table lengths.");
        }

        var valueCount = (stringDataOffset - valueTableOffset) / RecordSize;
        var groups = ReadAndValidateIndex(bytes, path, valueTableOffset, valueCount);
        var diagnostics = new LocalizationEntryDiagnostics(path);
        var values = CompiledLocalizationValueReader.Read(
            bytes, path, "LOC", valueTableOffset, stringDataOffset, valueCount, diagnostics);
        foreach (var pair in keysByHash)
        {
            if (!groups.TryGetValue(pair.Key, out var group))
            {
                continue;
            }

            for (var index = 0; index < group.Count; index++)
            {
                var value = values[group.First + index];
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                foreach (var key in pair.Value)
                {
                    result[key] = value;
                }

                break;
            }
        }

        diagnostics.AppendTo(issues);
        return result;
    }

    private static IReadOnlyDictionary<uint, ValueGroup> ReadAndValidateIndex(
        byte[] bytes,
        string path,
        int valueTableOffset,
        int valueCount)
    {
        var groups = new Dictionary<uint, ValueGroup>();
        for (var offset = HashRecordOffset; offset < valueTableOffset; offset += RecordSize)
        {
            // Unlike LOC2, LOC stores each hash's value count and first index here,
            // without a separate group table. The bucket table is not needed for lookup.
            var hash = ReadUInt32(bytes, offset);
            var count = ReadUInt32(bytes, offset + 4);
            var first = ReadUInt32(bytes, offset + 8);
            if ((ulong)first + count > (ulong)valueCount)
            {
                throw new InvalidDataException($"LOC '{path}' value range {first}+{count} is outside the value table.");
            }

            if (!groups.TryAdd(hash, new ValueGroup((int)first, (int)count)) && hash != 0)
            {
                throw new InvalidDataException($"LOC '{path}' contains duplicate hash records for 0x{hash:X8}.");
            }
        }

        return groups;
    }

    private static int ReadOffset(byte[] bytes, int offset, string path)
    {
        var value = ReadUInt32(bytes, offset);
        if (value > int.MaxValue)
        {
            throw new InvalidDataException($"LOC '{path}' table offset is too large: {value}.");
        }

        return (int)value;
    }

    private static uint ReadUInt32(byte[] bytes, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, sizeof(uint)));

    private readonly record struct ValueGroup(int First, int Count);
}
