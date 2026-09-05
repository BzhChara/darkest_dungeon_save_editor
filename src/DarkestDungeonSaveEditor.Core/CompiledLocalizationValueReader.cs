using System.Buffers.Binary;
using System.Text;

namespace DarkestDungeonSaveEditor.Core;

internal static class CompiledLocalizationValueReader
{
    // Both binary formats use the same value records after validating their distinct layouts.
    public static string[] Read(
        byte[] bytes,
        string path,
        string format,
        int valueTableOffset,
        int stringDataOffset,
        int valueCount,
        LocalizationEntryDiagnostics diagnostics)
    {
        var values = new string[valueCount];
        for (var index = 0; index < valueCount; index++)
        {
            var recordOffset = checked(valueTableOffset + index * 12);
            var offset = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(recordOffset, 4));
            var length = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(recordOffset + 4, 4));
            if ((ulong)offset + length > (ulong)(bytes.Length - stringDataOffset))
            {
                throw new InvalidDataException($"{format} '{path}' string {index} points outside the string data.");
            }

            if (length == 0)
            {
                throw new InvalidDataException($"{format} '{path}' string {index} has no NUL terminator.");
            }

            var absoluteOffset = checked(stringDataOffset + (int)offset);
            var byteLength = checked((int)length);
            if (bytes[absoluteOffset + byteLength - 1] != 0)
            {
                throw new InvalidDataException($"{format} '{path}' string {index} is not NUL-terminated.");
            }

            // Catch only value decoding failures, never table/index/boundary errors.
            try
            {
                values[index] = Loc2LocalizationReader.DecodeValue(
                    bytes.AsSpan(absoluteOffset, byteLength - 1), path, (uint)index, format);
            }
            catch (DecoderFallbackException)
            {
                values[index] = string.Empty;
                diagnostics.Skip($"值索引 {index}", "UTF-8 编码无效");
            }
            catch (InvalidDataException)
            {
                values[index] = string.Empty;
                diagnostics.Skip($"值索引 {index}", "编译色码不完整");
            }
        }

        return values;
    }
}
