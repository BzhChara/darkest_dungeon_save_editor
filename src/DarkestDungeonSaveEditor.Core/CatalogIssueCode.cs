using System.Globalization;

namespace DarkestDungeonSaveEditor.Core;

// Internal issue markers are invariant. Only the presentation after a marker is localized.
// Keep these separate from translated text: log severity/grouping must not depend on UI language.
internal static class CatalogIssueCode
{
    internal const string EncounterSlots = "[DDSE:EncounterSlots]";
    internal const string Record = ";record=";
    internal const string PartialLocalization = "[DDSE:PartialLocalization]'";
    internal const string TownRaidResidue = "[DDSE:TownRaidResidue]";
    internal const string LocalizationEvidence = "\n[DDSE:LocalizationEvidence]";

    internal static string FormatEncounterSlots(string path, int line, int record) =>
        EncounterSlots + path + ":" + line.ToString(CultureInfo.InvariantCulture) +
        Record + record.ToString(CultureInfo.InvariantCulture);
}
