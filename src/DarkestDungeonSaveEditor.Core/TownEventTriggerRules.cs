using System.Text;
using System.Text.Json;

namespace DarkestDungeonSaveEditor.Core;

public sealed record TownEventHeroCountRequirement(uint Level, uint Count);
public sealed record TownEventUpgradeRequirement(string TreeId, string RequirementCode);

// Natural selection metadata, not restrictions on the editor's manual replacement.
// Read from the same effective event definition as its effects and description.
public sealed record TownEventTriggerRules
{
    public uint MinimumWeek { get; init; }
    public uint DeadHeroes { get; init; }
    public IReadOnlyList<TownEventHeroCountRequirement> HeroLevelCounts { get; init; } = [];
    public IReadOnlyList<TownEventUpgradeRequirement> UpgradesPurchased { get; init; } = [];
    public uint TrinketStorageCount { get; init; }
    public IReadOnlyList<string> TownEvents { get; init; } = [];
    public IReadOnlyList<string> FinishedPlotQuests { get; init; } = [];
    public string AvailablePlotQuest { get; init; } = string.Empty;
    public IReadOnlyList<string> InfestationSequences { get; init; } = [];
    public uint MinimumDistrictDefinitions { get; init; }
    public string BuildingUnlocked { get; init; } = string.Empty;
    public bool IsUnique { get; init; }
    public uint CooldownEvents { get; init; }

    internal static TownEventTriggerRules Read(JsonElement entry)
    {
        NativeJsonReader.TryGetProperty(entry, "requirements", out var requirements);
        return new TownEventTriggerRules
        {
            MinimumWeek = UInt(requirements, "minimum_week"),
            DeadHeroes = UInt(requirements, "dead_heroes"),
            HeroLevelCounts = NativeJsonReader.Array(requirements, "hero_level_counts")
                .Where(row => row.ValueKind == JsonValueKind.Object)
                .Select(row => new TownEventHeroCountRequirement(UInt(row, "level"), UInt(row, "count")))
                // Native 0x14046E65B-0x14046E675: zero counts are ignored;
                // each later nonzero entry replaces that level's map value.
                .Where(row => row.Count > 0).GroupBy(row => row.Level)
                .OrderBy(group => group.Key).Select(group => group.Last()).ToArray(),
            UpgradesPurchased = NativeJsonReader.Array(requirements, "upgrades_purchased")
                .Where(row => row.ValueKind == JsonValueKind.Object)
                .Select(row => new TownEventUpgradeRequirement(NativeJsonReader.ReadCString(row, "tree_id"),
                    NativeJsonReader.ReadCString(row, "requirement_code"))).ToArray(),
            TrinketStorageCount = UInt(requirements, "trinket_storage_count"),
            TownEvents = Strings(requirements, "town_events"),
            FinishedPlotQuests = Strings(requirements, "finished_plot_quests"),
            AvailablePlotQuest = ReadAvailablePlotReference(requirements),
            InfestationSequences = Strings(requirements, "infestation_sequences"),
            MinimumDistrictDefinitions = UInt(requirements, "minimum_number_of_district_buildings"),
            BuildingUnlocked = NativeJsonReader.ReadCString(requirements, "building_unlocked"),
            IsUnique = NativeJsonReader.TryGetProperty(entry, "is_unique", out var unique) && unique.ValueKind == JsonValueKind.True,
            CooldownEvents = UInt(entry, "cooldown")
        };
    }

    public string Describe()
    {
        var lines = new List<string>();
        if (MinimumWeek > 0) lines.Add(EditorText.Format("TownEvent_RequirementWeek", MinimumWeek));
        if (DeadHeroes > 0) lines.Add(EditorText.Format("TownEvent_RequirementDead", DeadHeroes));
        foreach (var row in HeroLevelCounts.Where(row => row.Count > 0))
            lines.Add(EditorText.Format("TownEvent_RequirementHeroes", row.Level, row.Count));
        foreach (var row in UpgradesPurchased)
            lines.Add(EditorText.Format("TownEvent_RequirementUpgrade", row.TreeId, row.RequirementCode));
        if (TrinketStorageCount > 0) lines.Add(EditorText.Format("TownEvent_RequirementTrinkets", TrinketStorageCount));
        if (TownEvents.Count > 0) lines.Add(EditorText.Format("TownEvent_RequirementEvents", string.Join(", ", TownEvents)));
        if (FinishedPlotQuests.Count > 0) lines.Add(EditorText.Format("TownEvent_RequirementFinishedPlots", string.Join(", ", FinishedPlotQuests)));
        if (AvailablePlotQuest.Length > 0) lines.Add(EditorText.Format("TownEvent_RequirementAvailablePlot", AvailablePlotQuest));
        if (InfestationSequences.Count > 0) lines.Add(EditorText.Format("TownEvent_RequirementInfestation", string.Join(", ", InfestationSequences)));
        if (MinimumDistrictDefinitions > 0) lines.Add(EditorText.Format("TownEvent_RequirementDistricts", MinimumDistrictDefinitions));
        if (BuildingUnlocked.Length > 0) lines.Add(EditorText.Format("TownEvent_RequirementBuilding", BuildingUnlocked));
        if (IsUnique) lines.Add(EditorText.Get("TownEvent_RequirementUnique"));
        // Native 0x14058D79F-0x14058D7DB scans the last N result events, not weeks.
        if (CooldownEvents > 0) lines.Add(EditorText.Format("TownEvent_RequirementCooldown", CooldownEvents));
        return lines.Count == 0 ? EditorText.Get("TownEvent_RequirementsNone") : string.Join(" · ", lines);
    }

    private static uint UInt(JsonElement node, string name)
    {
        if (!NativeJsonReader.TryGetProperty(node, name, out var value) || value.ValueKind != JsonValueKind.Number) return 0;
        if (value.TryGetUInt32(out var unsigned)) return unsigned;
        return value.TryGetInt32(out var signed) ? unchecked((uint)signed) : 0;
    }

    private static string[] Strings(JsonElement node, string name) => NativeJsonReader.Array(node, name)
        .Where(value => value.ValueKind == JsonValueKind.String)
        .Select(value => NativeJsonReader.CString(value.GetString()!)).ToArray();

    private static string ReadAvailablePlotReference(JsonElement requirements)
    {
        var bytes = Encoding.UTF8.GetBytes(NativeJsonReader.ReadCString(requirements, "available_plot_quest"));
        // Native storage keeps at most 255 bytes. A partial UTF-8 character is
        // rendered as a replacement glyph here; advisory text must not reject
        // the event file. This string is never used for quest lookup or writes.
        return Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 255));
    }
}
