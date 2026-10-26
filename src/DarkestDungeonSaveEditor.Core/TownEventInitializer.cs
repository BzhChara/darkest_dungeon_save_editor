using System.Globalization;
using System.Text.Json.Nodes;

namespace DarkestDungeonSaveEditor.Core;

internal sealed partial class TownEventInitializer(ActiveContentSnapshot content, int seed)
{
    private readonly Random _random = new(seed);
    private HeroClassCatalogResult? _heroes;
    private HeroClassCatalogResult Heroes => _heroes ??= HeroClassCatalog.Load(content);
    private static readonly string[] NativeEffectTypes =
    [
        "free_activity", "activity_lock", "activity_cost_change", "bonus_recruit", "dead_recruit", "plot_quest",
        "embark_party_buff", "in_activity_buff", "idle_buff", "idle_resolve_level", "provision_item_type_cost_change",
        "provision_item_type_amount_change", "upgrade_tag_discount", "upgrade_tag_free", "remove_quest_hero_level_restriction",
        "stage_coach_bonus_recruits", "trinket_retention_add_from_storage", "remove_vampire", "districts_unlocked",
        "bonus_currency", "stress_treatment_debuff", "shard_buff", "event_cost", "quirked_recruit", "roster_stress_heal",
        "roster_disease_heal", "trinket_sell_multiplier", "idle_negative_quirk_remove", "heirloom_gain_multiplier",
        "nomad_wagon_trinket_discount", "bonus_recruit_stage_coach_level"
    ];

    internal static string EffectType(TownEventEffect effect) => NativeEffectTypes.FirstOrDefault(type =>
        Hash(type) == Hash(effect.Type)) ?? throw new InvalidOperationException(
            EditorText.Format("TownEvent_UnsupportedEffect", effect.Type));

    internal IReadOnlyList<string> Initialize(IReadOnlyDictionary<string, JsonObject> saves, TownEventDefinition definition, List<string> changes)
    {
        var unavailable = TownEventAvailability.UnavailableReason(definition);
        if (unavailable.Length > 0) throw new InvalidOperationException(unavailable);
        var state = TownEventSaveEditor.Root(saves, "town_event");
        var costs = new Dictionary<uint, (string Type, int Amount)>();
        var upgrades = new Dictionary<uint, int>();
        foreach (var effect in definition.Effects)
        {
            if (!double.IsFinite(effect.NumberData)) throw new InvalidDataException("Non-finite town event effect.");
            var type = EffectType(effect);
            switch (type)
            {
                case "upgrade_tag_free":
                    upgrades[Hash(effect.StringData)] = checked(upgrades.GetValueOrDefault(Hash(effect.StringData)) + Integer(effect.NumberData));
                    break;
                case "event_cost":
                    var currency = ResolveCurrency(saves, effect.StringData);
                    var key = Hash(currency);
                    costs[key] = (currency, checked(costs.GetValueOrDefault(key).Amount + Integer(effect.NumberData)));
                    break;
                case "roster_stress_heal":
                case "roster_disease_heal":
                    state["has_unclaimed_interaction"] = true;
                    break;
                case "heirloom_gain_multiplier":
                    state["heirloom_gain_multiplier"] = TownEventSaveEditor.Float(effect.NumberData);
                    break;
                case "nomad_wagon_trinket_discount":
                    state["nomad_wagon_trinket_discount_available"] = true;
                    state["nomad_wagon_trinket_discount"] = TownEventSaveEditor.Float(effect.NumberData);
                    break;
                case "bonus_recruit":
                case "bonus_recruit_stage_coach_level":
                case "quirked_recruit":
                    AddRecruits(saves, definition, effect, type, changes);
                    break;
                case "dead_recruit":
                    AddRevivalOffers(saves, effect, changes);
                    break;
                case "trinket_retention_add_from_storage":
                    RetainTrinkets(saves, effect, changes);
                    break;
                case "districts_unlocked":
                    UnlockDistricts(saves, changes);
                    break;
                // These are read through the current event on use/departure.
                // Initializing one must not run treatment or another town visit.
                default:
                    break;
            }
        }
        if (costs.Count > 0)
        {
            var cost = new JsonObject { ["count"] = costs.Count };
            var index = 0;
            foreach (var value in costs.Values)
                cost[(index++).ToString(CultureInfo.InvariantCulture)] = new JsonObject
                    { ["type"] = value.Type, ["amount"] = value.Amount };
            state["event_cost"] = cost;
        }
        var tags = new JsonObject();
        foreach (var pair in upgrades.OrderBy(pair => pair.Key))
            tags[tags.Count.ToString(CultureInfo.InvariantCulture)] = new JsonObject
                { ["upgrade_tag"] = unchecked((int)pair.Key), ["amount"] = pair.Value };
        state["free_upgrade_tags"] = tags;
        // ApplyStart precedes the town callbacks. In particular, theft must
        // inspect the old quest board before new event rewards are generated.
        foreach (var effect in definition.Effects.Where(effect => EffectType(effect) == "bonus_currency"))
            GrantCurrency(saves, effect, changes);
        if (definition.Effects.Any(effect => EffectType(effect) == "remove_vampire"))
            RemoveVampire(saves, changes);
        foreach (var effect in definition.Effects.Where(effect => EffectType(effect) == "stage_coach_bonus_recruits"))
            AddRecruits(saves, definition, effect, "stage_coach_bonus_recruits", changes);
        ReserveExistingRecoveryQuests(saves);
        // Event editing only creates explicitly requested plots. The separate
        // weekly recovery-quest scan is not part of starting or replacing an event.
        var warnings = new List<string>();
        AddPlotQuests(saves, definition.Effects.Where(effect => EffectType(effect) == "plot_quest").ToArray(), changes, warnings);
        return warnings.ToArray();
    }

    private string ResolveCurrency(IReadOnlyDictionary<string, JsonObject> saves, string id)
    {
        var catalog = QuantityItemCatalog.Load(content, saves["persist.estate.json"]);
        var match = catalog.Items.FirstOrDefault(item => item.StorageKind == QuantityItemStorageKind.Wallet &&
            Hash(item.PersistedType) == Hash(id) && !item.HasProviderConflict && !item.IsSaveOnly && item.SaveIdentityIssue.Length == 0);
        return match?.PersistedType ?? throw new InvalidDataException(EditorText.Format("TownEvent_MissingCurrency", id));
    }

    private void GrantCurrency(IReadOnlyDictionary<string, JsonObject> saves, TownEventEffect effect, List<string> changes)
    {
        var type = ResolveCurrency(saves, effect.StringData);
        var wallet = JsonSupport.RequireObject(TownEventSaveEditor.Root(saves, "estate"), "wallet");
        var matches = wallet.Select(pair => pair.Value).OfType<JsonObject>()
            .Where(item => NativeInventoryIdentity.HashSavedField(Text(item, "type")) == Hash(type)).ToArray();
        if (matches.Length > 1) throw new InvalidDataException("Duplicate wallet currency identity.");
        var entry = matches.SingleOrDefault();
        var before = entry is null ? 0 : TownEventSaveEditor.RequiredInt(entry, "amount");
        var after = (int)Math.Clamp((long)before + Integer(effect.NumberData), 0, QuantityItemSaveEditor.MaximumWalletAmount);
        if (entry is null)
        {
            entry = new JsonObject { ["type"] = type };
            wallet[NextKey(wallet)] = entry;
        }
        entry["amount"] = after;
        changes.Add(EditorText.Format("TownEvent_CurrencyChange", type, before, after));
    }

    internal static uint Hash(string value) => NativeResourceIdentity.HashCString(value);
    private static int Integer(double value) => checked((int)Math.Truncate(value));
    private static int Count(double value)
    {
        if (value < 0 || value > 4096) throw new InvalidDataException("Town event count must be between 0 and 4096.");
        return checked((int)Math.Ceiling(value));
    }
    internal static string NextKey(JsonObject map) => checked(map.Select(pair =>
        int.TryParse(pair.Key, NumberStyles.None, CultureInfo.InvariantCulture, out var key) && key >= 0 ? key :
            throw new InvalidDataException($"Invalid save entry index: {pair.Key}.")).DefaultIfEmpty(-1).Max() + 1).ToString(CultureInfo.InvariantCulture);
}
