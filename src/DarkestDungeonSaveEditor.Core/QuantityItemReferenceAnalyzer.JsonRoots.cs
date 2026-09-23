using System.Globalization;
using System.Text.Json;

namespace DarkestDungeonSaveEditor.Core;

internal static partial class QuantityItemReferenceAnalyzer
{
    private static bool VisitReferenceJson(JsonElement root, ScannedContentFile file, QuantityItemIndex index,
        IEnumerable<string> knownLootTables, QuantityItemSaveContext context,
        Dictionary<string, List<string>> evidence, Dictionary<string, List<string>> incompleteEvidence,
        Dictionary<string, List<string>> uncertainLootEvidence, Dictionary<uint, string>? eventIds,
        OrderedDefinitionReadState? eventReads, List<string> issues)
    {
        var path = file.File.RelativePath;
        var town = context == QuantityItemSaveContext.Town;
        static JsonElement Child(JsonElement node, string key) =>
            NativeJsonReader.TryGetProperty(node, key, out var child) ? child : default;
        static IEnumerable<JsonElement> Array(JsonElement node) =>
            node.ValueKind == JsonValueKind.Array ? node.EnumerateArray() : [];
        static IEnumerable<JsonElement> List(JsonElement node, string key) => NativeJsonReader.Array(node, key);
        static string String(JsonElement node, string key) => NativeJsonReader.ReadCString(node, key);
        void Item(JsonElement node) => MarkResolved(index.Resolve(String(node, "type"), String(node, "id")), evidence, path);
        void Items(IEnumerable<JsonElement> nodes) { foreach (var node in nodes) Item(node); }
        void Currencies(JsonElement node)
        {
            foreach (var cost in List(node, "currency_cost"))
                MarkResolved(index.ResolveCurrencyHash(String(cost, "type")), evidence, path);
        }
        // Serialized inventory entries are numeric slots, not arbitrary child objects.
        // Use lookup for each distinct slot so a repeated JSON member is first-match.
        static IEnumerable<JsonElement> Slots(JsonElement node)
        {
            if (node.ValueKind != JsonValueKind.Object) yield break;
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var member in node.EnumerateObject())
                if (int.TryParse(member.Name, NumberStyles.None, CultureInfo.InvariantCulture, out var slot) &&
                    slot >= 0 && member.Name == slot.ToString(CultureInfo.InvariantCulture) && keys.Add(member.Name))
                    yield return member.Value;
        }
        void Inventory(JsonElement node) => Items(Slots(Child(node, "items")));
        void Reward(JsonElement node)
        {
            Inventory(Child(node, "items_definition"));
            Inventory(Child(node, "threshold_loot"));
        }
        void Uncertain(JsonElement node)
        {
            if (node.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ||
                !ReferencePathCanAffectContext(file.MountedPath, context)) return;
            // This is valid parsed JSON: equivalent escaped spellings must
            // provide the same uncertainty. Raw-text recovery is only for
            // malformed files whose JSON strings cannot be decoded reliably.
            var tokens = new HashSet<string>(StringComparer.Ordinal);
            void Collect(JsonElement value)
            {
                if (value.ValueKind == JsonValueKind.String) tokens.Add(value.GetString()!);
                else if (value.ValueKind == JsonValueKind.Array)
                    foreach (var child in value.EnumerateArray()) Collect(child);
                else if (value.ValueKind == JsonValueKind.Object)
                {
                    var members = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var member in value.EnumerateObject())
                        if (members.Add(member.Name)) { tokens.Add(member.Name); Collect(member.Value); }
                }
            }
            Collect(node);
            var matched = false;
            foreach (var identity in index.Identities.Where(identity => tokens.Contains(identity.Identity)))
            {
                MarkResolved(identity.CatalogKeys, incompleteEvidence, $"有效资源的此结构尚未确认物品消费规则：{path}");
                matched = true;
            }
            foreach (var table in knownLootTables.Where(tokens.Contains))
            {
                AddEvidence(uncertainLootEvidence, table, $"有效资源中尚未确认的掉落引用：{path}");
                matched = true;
            }
            if (matched) issues.Add($"Quantity-item references remain unverified in a consumed JSON structure: {path}");
        }

        switch (file.JsonKind)
        {
            case NativeReferenceJsonKind.TownEvents:
                if (!town) return true;
                var complete = true;
                // Decode the whole file before publishing first-result identities
                // or evidence, so a later string decode failure cannot leave a partial winner.
                var events = List(root, "events").Select(node => (
                    Id: String(node, "id"),
                    ItemKeys: List(node, "data").Where(data =>
                    {
                        var type = Loc2LocalizationReader.HashName(NativeJsonReader.ReadBoundedString(data, "type", 63));
                        return type == Loc2LocalizationReader.HashName("bonus_currency") || type == Loc2LocalizationReader.HashName("event_cost");
                    }).SelectMany(data => index.ResolveCurrencyHash(String(data, "string_data"))).ToArray())).ToArray();
                foreach (var entry in events)
                {
                    var id = entry.Id;
                    if (id.Length == 0)
                    {
                        complete = false;
                        issues.Add($"Town event has no usable ID; item-reference analysis is incomplete: {path}");
                        continue;
                    }
                    var hash = Loc2LocalizationReader.HashName(id);
                    if (eventIds is not null && !eventIds.TryAdd(hash, id))
                    {
                        if (eventIds[hash] != id)
                        {
                            complete = false;
                            issues.Add($"Town event IDs share a native hash; item-reference analysis is incomplete: {eventIds[hash]} / {id}");
                        }
                        continue;
                    }
                    // Only the first event result's data is executed. The native
                    // event parser has no generic cost/loot/item/notes object walker.
                    eventReads?.RecordDefinition(id);
                    var verified = eventReads is null || eventReads.IsVerified(id);
                    MarkResolved(entry.ItemKeys, verified ? evidence : incompleteEvidence,
                        verified ? path : $"事件首条结果无法确认：{path}");
                }
                return complete;

            case NativeReferenceJsonKind.Provision:
                if (town) break;
                foreach (var key in new[] { "raid_starting_length_inventory_item_lists", "default_store_inventory_item_lists" })
                    foreach (var list in List(root, key)) Items(Array(list));
                foreach (var hero in List(root, "raid_starting_hero_class_item_lists")) Items(List(hero, "item_lists"));
                break;

            case NativeReferenceJsonKind.Estate:
                if (town)
                    foreach (var currency in List(root, "currencies"))
                        MarkResolved(index.ResolveCurrencyHash(String(currency, "id")), evidence, path);
                break;

            case NativeReferenceJsonKind.PlotQuests:
                foreach (var plot in List(root, "plot_quests"))
                {
                    if (!town) Inventory(Child(plot, "additional_provisions"));
                    else
                    {
                        Inventory(Child(Child(plot, "estate_inventory_dependency"), "items_definition"));
                        var quest = Child(plot, "quest");
                        Reward(Child(quest, "completion_reward"));
                        foreach (var reward in Slots(Child(quest, "threshold_rewards"))) Reward(reward);
                    }
                }
                break;

            case NativeReferenceJsonKind.QuestGeneration:
                if (town)
                {
                    var rewards = Child(Child(root, "generation"), "rewards");
                    foreach (var difficulty in List(rewards, "item_table"))
                        foreach (var length in Array(difficulty)) Items(Array(length));
                    foreach (var heirloom in List(rewards, "heirloom_amount_table"))
                        MarkResolved(index.ResolveCurrencyHash(String(heirloom, "type")), evidence, path);
                }
                break;

            case NativeReferenceJsonKind.QuestTypes:
                if (!town)
                    foreach (var goal in List(root, "goals"))
                    {
                        Items(List(goal, "starting_items"));
                        // Goal-specific data uses separate virtual consumers. It
                        // cannot certify an item merely because data contains an ID.
                        Uncertain(Child(goal, "data"));
                    }
                break;

            case NativeReferenceJsonKind.Districts:
                foreach (var building in List(root, "buildings"))
                {
                    if (town) Currencies(building);
                    foreach (var buff in List(building, "buff_list"))
                    {
                        var type = String(buff, "type");
                        if (type == "DistrictSupplyBuffData")
                        {
                            var target = String(buff, "target_inventory");
                            if (town && target == "estate" || !town && target == "provision")
                                MarkResolved(index.Resolve(String(buff, "item_type"), String(buff, "item_name")), evidence, path);
                            else if (target is not ("estate" or "provision")) Uncertain(buff);
                        }
                        else if (type == "DistrictReplacementInventoryEffectBuffData" && !town)
                            MarkResolved(index.Resolve(String(buff, "item_type"), String(buff, "item_id")), evidence, path);
                    }
                }
                break;

            case NativeReferenceJsonKind.Building:
                // Per-building data is interpreted by different LoadInternal
                // implementations. Preserve uncertainty rather than invent a
                // universal item/currency consumer from arbitrary nested fields.
                Uncertain(Child(root, "data"));
                Uncertain(Child(root, "requirements"));
                break;
            case NativeReferenceJsonKind.Unverified:
                Uncertain(root);
                break;
        }
        return true;
    }
}
