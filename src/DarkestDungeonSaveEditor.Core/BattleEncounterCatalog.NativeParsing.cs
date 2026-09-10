using System.Text;

namespace DarkestDungeonSaveEditor.Core;

public static partial class BattleEncounterCatalog
{
    private static readonly UTF8Encoding EncounterUtf8 = new(false, true);

    private static string ReadEncounterString(string value, int maximumBytes)
    {
        var bytes = EncounterUtf8.GetBytes(value);
        return EncounterUtf8.GetString(bytes, 0, Math.Min(bytes.Length, maximumBytes));
    }

    // MashGuide uses GetFloat's last-field, native float and percent semantics;
    // only the consumer's absent-field default differs from the shared reader.
    private static double ReadNativeChance(string body) => NativeDarkestReader.ReadFloat(body, ".chance") ?? 0;
}
