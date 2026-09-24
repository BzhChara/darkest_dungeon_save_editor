namespace DarkestDungeonSaveEditor.Core;

internal static class EncounterBridgeRow
{
    internal static string Format(int mashType, IReadOnlyList<string> monsters)
    {
        var kind = mashType switch
        {
            0 => "hall", 1 => "room", 2 => "boss",
            _ => throw new InvalidOperationException(EditorText.Get("EncounterBridgeRow_001"))
        };
        // MashGuide takes four raw tokens after .types; any options written
        // there would themselves become monster IDs in a short formation.
        var options = mashType == 2 ? string.Empty : " .limit 1 .can_be_ambush false";
        return $"{kind}: .chance 0{options} .types {string.Join(' ', monsters.Select(Quote))}" + Environment.NewLine;
    }

    private static string Quote(string id)
    {
        if (id.Contains('"') || id.Contains(':') || id.Contains('/') || id.Contains('\\') ||
            id.Any(character => character is '\r' or '\n' or '\0'))
            throw new InvalidOperationException(EditorText.Get("EncounterBridgeRow_002"));
        return id.Any(NativeDarkestReader.IsWhitespace) ? $"\"{id}\"" : id;
    }
}
