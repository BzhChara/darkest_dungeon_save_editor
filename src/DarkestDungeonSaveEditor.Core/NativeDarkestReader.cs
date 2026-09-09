using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DarkestDungeonSaveEditor.Core;

internal static partial class NativeDarkestReader
{
    [GeneratedRegex(
        @"(?<kind>[A-Za-z_]+):(?<body>.*?)(?=[A-Za-z_]+:|\z)",
        RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex EntryRegex();

    internal static IEnumerable<(string Kind, string Body)> ReadRecords(string path)
        => ReadRecordsFromText(File.ReadAllText(path, Encoding.UTF8));

    internal static IEnumerable<(string Kind, string Body)> ReadRecordsFromText(string content)
    {
        var text = StripComments(content);
        foreach (Match entry in EntryRegex().Matches(MaskHashComments(text)))
        {
            var body = entry.Groups["body"];
            yield return (entry.Groups["kind"].Value, text.Substring(body.Index, body.Length));
        }
    }

    private static string StripComments(string text)
    {
        var result = new StringBuilder(text.Length);
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
                    index = end;
                    continue;
                }
                if (text[index + 1] == '*')
                {
                    var end = text.IndexOf("*/", index + 2, StringComparison.Ordinal);
                    if (end < 0) break;
                    index = end + 1;
                    continue;
                }
            }
            result.Append(current);
        }
        return result.ToString();
    }

    internal static string MaskHashComments(string text)
    {
        // Hash comments suppress declaration boundaries in ReadNextLine, but
        // remain in the native copied body. Mask only the header-search text;
        // field reads must use the original body at these same offsets.
        var masked = text.ToCharArray();
        for (var index = 0; index < masked.Length; index++)
        {
            if (masked[index] != '#') continue;
            while (index < masked.Length && masked[index] != '\n') masked[index++] = ' ';
        }
        return new string(masked);
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
        if (start < 0) return null;
        var end = start;
        if (end < body.Length && body[end] is '+' or '-') end++;
        var digitStart = end;
        while (end < body.Length && body[end] is >= '0' and <= '9') end++;
        // atoi reads an integer prefix. Quotes or a nonnumeric final value
        // yield zero; neither permits reviving an earlier, usable limit.
        if (end == digitStart) return 0;
        // Out-of-range native conversion is not a proven usable stack limit.
        return int.TryParse(body.AsSpan(start, end - start), NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out var value) ? value : null;
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
