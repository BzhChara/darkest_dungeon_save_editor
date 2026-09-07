using System.Globalization;
using System.Text.RegularExpressions;

namespace DarkestDungeonSaveEditor.Core;

public static partial class BattleEncounterCatalog
{
    private static readonly Regex NativeChanceField = new(
        @"\.chance(?=[\t =])", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex NativeNumberPrefix = new(
        @"^[\t ]*(?<number>[+-]?(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)(?:[eE][+-]?[0-9]+)?)(?<percent>%)?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static double ReadNativeChance(string line)
    {
        // MashGuide uses the last matching .chance and strtod, so 0.7.types
        // still supplies chance 0.7. A missing/unreadable chance defaults to 0.
        var field = NativeChanceField.Matches(line).LastOrDefault();
        if (field is null) return 0;
        var number = NativeNumberPrefix.Match(line[(field.Index + field.Length)..]);
        if (!number.Success || !double.TryParse(number.Groups["number"].Value,
                NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return 0;
        return number.Groups["percent"].Success ? value * 0.01 : value;
    }
}
