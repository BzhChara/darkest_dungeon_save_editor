using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DarkestDungeonSaveEditor.Core;

internal static partial class NativeDarkestReader
{
    internal sealed record Record(string Kind, string Body, int SourceLine, int RecordIndex);

    internal static IEnumerable<(string Kind, string Body)> ReadRecords(string path)
        => ReadRecordsFromText(File.ReadAllText(path, Encoding.UTF8));

    internal static IEnumerable<(string Kind, string Body)> ReadRecordsFromText(string content)
        => ReadRecordsCore(content, includeSourceLines: false).Select(record => (record.Kind, record.Body));

    internal static IEnumerable<(string Kind, string Body, int SourceLine)> ReadRecordsWithSourceLinesFromText(string content)
        => ReadRecordsWithLocationsFromText(content).Select(record => (record.Kind, record.Body, record.SourceLine));

    internal static IEnumerable<Record> ReadRecordsWithLocationsFromText(string content)
        => ReadRecordsCore(content, includeSourceLines: true);

    private static IEnumerable<Record> ReadRecordsCore(
        string content, bool includeSourceLines)
    {
        // LineReader::ReadNextLine stops at the first NUL. In particular,
        // definitions after it cannot override a preceding usable value.
        var nul = content.IndexOf('\0');
        if (nul >= 0) content = content[..nul];
        var sourceLines = includeSourceLines ? new List<int>(content.Length) : null;
        var text = StripComments(content, sourceLines);
        var cursor = 0;
        var ordinal = 0;
        while (cursor < text.Length)
        {
            while (cursor < text.Length)
            {
                if (IsWhitespace(text[cursor])) { cursor++; continue; }
                if (text[cursor] != '#') break;
                while (cursor < text.Length && text[cursor] != '\n') cursor++;
            }
            if (cursor == text.Length) yield break;
            var start = cursor;
            while (cursor < text.Length && IsHeaderCharacter(text[cursor])) cursor++;
            // ReadNextLine returns false at an invalid current header. It does
            // not search past arbitrary text for another plausible declaration.
            if (cursor == text.Length || text[cursor] != ':') yield break;
            var headerEnd = cursor++;
            var bodyStart = cursor;
            while (cursor < text.Length)
            {
                if (text[cursor] == '#')
                {
                    while (cursor < text.Length && text[cursor] != '\n') cursor++;
                    continue;
                }
                if (text[cursor] == ':')
                {
                    // Native backs up over the next header to its preceding
                    // separator; that separator is consumed on the next read.
                    do { cursor--; }
                    while (cursor >= bodyStart && IsHeaderCharacter(text[cursor]));
                    if (cursor < bodyStart)
                        throw new InvalidDataException("Darkest declarations have no advancing record boundary.");
                    break;
                }
                cursor++;
            }
            yield return new Record(text[start..headerEnd], text[bodyStart..cursor],
                sourceLines?[start] ?? 0, ordinal++);
        }
    }

    private static bool IsHeaderCharacter(char value) => char.IsAsciiLetter(value) || value == '_';

    private static string StripComments(string text, List<int>? sourceLines)
    {
        var result = new StringBuilder(text.Length);
        var sourceLine = 1;
        for (var index = 0; index < text.Length; index++)
        {
            var current = text[index];
            // Native LineReader removes slash comments before parsing strings.
            // It does not track quotes or replace removed text with whitespace.
            if (current == '/' && index + 1 < text.Length)
            {
                if (text[index + 1] == '/')
                {
                    var end = text.IndexOf('\n', index + 2);
                    if (end < 0) break;
                    sourceLine++;
                    index = end;
                    continue;
                }
                if (text[index + 1] == '*')
                {
                    var end = text.IndexOf("*/", index + 2, StringComparison.Ordinal);
                    if (end < 0) break;
                    for (var skipped = index; skipped <= end + 1; skipped++)
                        if (text[skipped] == '\n') sourceLine++;
                    index = end + 1;
                    continue;
                }
            }
            result.Append(current);
            sourceLines?.Add(sourceLine);
            if (current == '\n') sourceLine++;
        }
        return result.ToString();
    }

    internal static int FindValue(string body, string field)
    {
        // Native field helpers use the last case-sensitive substring, not the
        // first regex match or a dictionary of case-folded attribute tokens.
        var index = body.LastIndexOf(field, StringComparison.Ordinal);
        if (index < 0) return -1;
        index += field.Length;
        while (index < body.Length && IsWhitespace(body[index])) index++;
        return index;
    }

    internal static string? ReadString(string body, string field)
    {
        var start = FindValue(body, field);
        if (start < 0) return null;
        if (start < body.Length && body[start] == '"')
        {
            start++;
            var closingQuote = body.IndexOf('"', start);
            return body[start..(closingQuote < 0 ? body.Length : closingQuote)];
        }
        var end = start;
        while (end < body.Length && !IsWhitespace(body[end])) end++;
        return body[start..end];
    }

    internal static int? ReadInt(string body, string field)
    {
        var start = FindValue(body, field);
        return start < 0 ? null : ReadIntPrefix(body.AsSpan(start));
    }

    internal static int? ReadIntPrefix(ReadOnlySpan<char> text)
    {
        var start = 0;
        while (start < text.Length && IsWhitespace(text[start])) start++;
        var end = start;
        if (end < text.Length && text[end] is '+' or '-') end++;
        var digitStart = end;
        while (end < text.Length && text[end] is >= '0' and <= '9') end++;
        // atoi reads an integer prefix. Quotes or a nonnumeric final value
        // yield zero; neither permits reviving an earlier, usable limit.
        if (end == digitStart) return 0;
        // Out-of-range native conversion is not a proven usable stack limit.
        return int.TryParse(text.Slice(start, end - start), NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    [GeneratedRegex(@"^[\t\n\v\f\r ]*(?<number>[+-]?(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)(?:[eE][+-]?[0-9]+)?)(?<percent>%)?",
        RegexOptions.CultureInvariant)]
    private static partial Regex FloatPrefixRegex();

    internal static double? ReadFloat(string body, string field)
    {
        // GetFloat has a different field boundary from GetString/GetInt:
        // only NUL, TAB, SPACE or '=' immediately after the key qualifies.
        var start = -1;
        for (var offset = 0; offset < body.Length;)
        {
            var found = body.IndexOf(field, offset, StringComparison.Ordinal);
            if (found < 0) break;
            var end = found + field.Length;
            if (end == body.Length || body[end] is '\t' or ' ' or '=') start = end;
            offset = found + 1;
        }
        if (start < 0) return null;
        var number = FloatPrefixRegex().Match(body[start..]);
        if (!number.Success) return 0;
        if (!double.TryParse(number.Groups["number"].Value, NumberStyles.Float,
                CultureInfo.InvariantCulture, out var value)) return double.NaN;
        var result = (float)value;
        return number.Groups["percent"].Success ? result * 0.01f : result;
    }

    internal static IReadOnlyList<string> ReadStringList(string body, string field, int maximumCount,
        bool skipEmptyOrFieldTokens = false)
    {
        var values = new List<string>();
        // Effects and quirk exclusions stop at empty/dot-prefixed tokens;
        // valid_modes skips those slots but continues within its fixed limit.
        // Neither policy is the encounter raw-position policy.
        foreach (var value in ReadRawStringSlots(body, field, maximumCount))
        {
            if (value.Length == 0 || value[0] == '.')
            {
                if (!skipEmptyOrFieldTokens) break;
                continue;
            }
            // These callers use 64-byte native token buffers (63 plus NUL).
            var bytes = Encoding.UTF8.GetBytes(value);
            values.Add(bytes.Length < 64 ? value : Encoding.UTF8.GetString(bytes, 0, 63));
        }
        return values;
    }

    // Leave each consumer's empty/dot policy and native buffer handling separate.
    // Prop pools stop at an empty slot, but dot-prefixed strings are ordinary IDs.
    internal static IEnumerable<string> ReadRawStringSlots(string body, string field, int maximumCount)
    {
        var start = FindValue(body, field);
        for (var count = 0; start >= 0 && start < body.Length && count < maximumCount; count++)
        {
            while (start < body.Length && IsWhitespace(body[start])) start++;
            if (start == body.Length) break;
            int end;
            int next;
            if (body[start] == '"')
            {
                start++;
                end = body.IndexOf('"', start);
                // Native fixed-list helper uses strlen(body)-1 on an
                // unmatched quote, unlike the scalar-string helper.
                if (end < 0) end = Math.Max(start, body.Length - 1);
                next = end + 1;
            }
            else
            {
                end = start;
                while (end < body.Length && !IsWhitespace(body[end])) end++;
                next = end;
            }
            yield return body[start..end];
            start = next;
        }
    }

    internal static bool? ReadBoolean(string body, string field)
    {
        var value = ReadString(body, field);
        if (value is null) return null;
        // The game's StringToBool recognizes this explicit set. Other values
        // (including differently mixed case) are false rather than a fallback.
        return value is "t" or "T" or "true" or "True" or "TRUE" or "1" or
            "y" or "Y" or "yes" or "Yes" or "YES" or "on" or "On" or "ON";
    }

    internal static bool IsWhitespace(char value) => value is ' ' or '\t' or '\r' or '\n' or '\v' or '\f';
}
