using System.Text.Json;

namespace DarkestDungeonSaveEditor.Core;

internal static partial class QuantityItemReferenceAnalyzer
{
    private static void VisitBuildingReferences(JsonElement data, string buildingId,
        Action<JsonElement, string> currency, Action<JsonElement, string, string?> uncertain)
    {
        // Activity factories are building-specific. IDs and currency types use
        // complete C-string hashes; JSON members retain first-match lookup.
        // See resource-duplicate-semantics.md for versioned native evidence.
        string[] activityIds = buildingId switch
        {
            "abbey" => ["meditation", "prayer", "flagellation"],
            "tavern" => ["bar", "gambling", "brothel"],
            "sanitarium" => ["treatment", "disease_treatment"],
            _ => []
        };
        if (activityIds.Length == 0)
        {
            uncertain(data, "$.data", null);
            return;
        }

        static JsonElement Child(JsonElement node, string key) =>
            NativeJsonReader.TryGetProperty(node, key, out var child) ? child : default;
        static bool HasId(JsonElement node, string key, IEnumerable<string> ids)
        {
            var hash = Loc2LocalizationReader.HashName(NativeJsonReader.ReadCString(node, key));
            return ids.Any(id => Loc2LocalizationReader.HashName(id) == hash);
        }
        // Keep unknown extensions visible without recertifying arbitrary ID
        // strings as consumers. Do not revisit shadowed duplicate members.
        void OtherMembers(JsonElement node, string location, params string[] consumed)
        {
            if (node.ValueKind != JsonValueKind.Object) return;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var member in node.EnumerateObject())
                if (seen.Add(member.Name) && !consumed.Contains(member.Name, StringComparer.Ordinal))
                    uncertain(member.Value, location + "[" + JsonSerializer.Serialize(member.Name) + "]", member.Name);
        }

        var activityIndex = 0;
        foreach (var activity in NativeJsonReader.Array(data, "activities"))
        {
            var activityPath = $"$.data.activities[{activityIndex++}]";
            if (!HasId(activity, "id", activityIds))
            {
                uncertain(activity, activityPath, null);
                continue;
            }
            var activityData = Child(activity, "data");
            var dataPath = activityPath + ".data";
            // Both sanitarium activities use QuirkTreatment::LoadInternal,
            // which reads all four lists, including lists absent in vanilla.
            string[] costLists = buildingId == "sanitarium"
                ? ["positive_quirk_cost_upgrades", "negative_quirk_cost_upgrades",
                    "disease_quirk_cost_upgrades", "permanent_negative_quirk_cost_upgrades"]
                : ["cost_upgrades"];
            foreach (var key in costLists)
            {
                var costIndex = 0;
                foreach (var cost in NativeJsonReader.Array(activityData, key))
                    currency(Child(cost, "cost_currency"), $"{dataPath}.{key}[{costIndex++}].cost_currency");
            }

            // The shared Activity loader dispatches add/remove_currency to
            // ChangeCurrency. Each result's data is an array of currency objects.
            var sideEffects = Child(activityData, "side_effects");
            var resultIndex = 0;
            foreach (var result in NativeJsonReader.Array(sideEffects, "results"))
            {
                var resultPath = $"{dataPath}.side_effects.results[{resultIndex++}]";
                if (HasId(result, "type", ["add_currency", "remove_currency"]))
                {
                    var currencyIndex = 0;
                    foreach (var entry in NativeJsonReader.Array(result, "data"))
                        currency(entry, $"{resultPath}.data[{currencyIndex++}]");
                }
                else uncertain(result, resultPath, null);
            }
            OtherMembers(sideEffects, dataPath + ".side_effects", "chance", "results");
            OtherMembers(activityData, dataPath, [.. costLists, "side_effects"]);
            OtherMembers(activity, activityPath, "id", "data");
        }
        OtherMembers(data, "$.data", "activities");
    }
}
