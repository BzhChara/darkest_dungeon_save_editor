using System.Globalization;
using System.Text.Json.Nodes;

namespace DarkestDungeonSaveEditor.Core;

public static class StagecoachHeroSaveEditor
{
    public static (
        JsonObject UpdatedTown,
        JsonObject UpdatedRoster,
        JsonObject UpdatedUpgrades,
        StagecoachHeroMutationPreview Preview) AddCandidate(
            JsonObject townRoot,
            JsonObject rosterRoot,
            JsonObject upgradesRoot,
            JsonObject candidate,
            IReadOnlyList<HeroUpgradePurchase> upgradePurchases)
    {
        ArgumentNullException.ThrowIfNull(townRoot);
        ArgumentNullException.ThrowIfNull(rosterRoot);
        ArgumentNullException.ThrowIfNull(upgradesRoot);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(upgradePurchases);

        var heroClass = JsonSupport.ReadString(candidate, "heroClass");
        if (string.IsNullOrWhiteSpace(heroClass) || candidate["actor"] is not JsonObject)
        {
            throw new InvalidDataException("A stagecoach candidate must contain heroClass and actor objects.");
        }

        var resolveXp = ReadRequiredNonNegativeInt(candidate, "resolveXp");
        var weaponRank = ReadRequiredNonNegativeInt(candidate, "weapon_rank");
        var armourRank = ReadRequiredNonNegativeInt(candidate, "armour_rank");

        var updatedTown = townRoot.DeepClone() as JsonObject
            ?? throw new InvalidDataException("Failed to clone decoded town save.");
        var updatedRoster = rosterRoot.DeepClone() as JsonObject
            ?? throw new InvalidDataException("Failed to clone decoded roster save.");
        var updatedUpgrades = upgradesRoot.DeepClone() as JsonObject
            ?? throw new InvalidDataException("Failed to clone decoded upgrades save.");

        var rosterBase = JsonSupport.RequireObject(updatedRoster, "base_root");
        var heroes = JsonSupport.RequireObject(rosterBase, "heroes");
        var nextGuid = JsonSupport.ReadInt(rosterBase, "nextGuid")
            ?? throw new InvalidDataException("Required roster value is missing or invalid: base_root.nextGuid");
        if (nextGuid < 0 || nextGuid == int.MaxValue)
        {
            throw new InvalidDataException($"Roster nextGuid is outside the supported range: {nextGuid}.");
        }

        var store = JsonSupport.RequireObject(
            updatedTown,
            "base_root",
            "buildings",
            "stage_coach",
            "store");
        var normalRecruit = JsonSupport.RequireObject(store, "hero_recruit");
        var generated = JsonSupport.RequireObject(normalRecruit, "generated");
        ValidateNextGuidInvariant(nextGuid, heroes, store);

        var candidateKey = nextGuid.ToString(CultureInfo.InvariantCulture);
        if (heroes.ContainsKey(candidateKey) || ContainsGeneratedGuid(store, candidateKey))
        {
            throw new InvalidDataException(
                $"Roster nextGuid '{candidateKey}' already exists in the roster or a stagecoach pool.");
        }

        var existingCandidates = generated.Count;
        generated[candidateKey] = candidate.DeepClone();
        rosterBase["nextGuid"] = nextGuid + 1;
        AddUpgradePurchases(updatedUpgrades, nextGuid, upgradePurchases);

        return (
            updatedTown,
            updatedRoster,
            updatedUpgrades,
            new StagecoachHeroMutationPreview(
                nextGuid,
                heroClass,
                resolveXp,
                weaponRank,
                armourRank,
                upgradePurchases.Count,
                existingCandidates,
                generated.Count,
                nextGuid,
                nextGuid + 1,
                heroes.Count));
    }

    public static IReadOnlyList<HeroQuirkLimitPreview> AnalyzeQuirkLimits(
        JsonObject townRoot,
        JsonObject rosterRoot,
        JsonObject candidate,
        IReadOnlyList<HeroInitialQuirkDefinition> initialQuirks)
    {
        ArgumentNullException.ThrowIfNull(townRoot);
        ArgumentNullException.ThrowIfNull(rosterRoot);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(initialQuirks);

        if (candidate["quirks"] is not JsonObject candidateQuirks || candidateQuirks.Count == 0)
        {
            return [];
        }

        var rosterHeroes = JsonSupport.RequireObject(rosterRoot, "base_root", "heroes");
        var stagecoachStore = JsonSupport.RequireObject(
            townRoot,
            "base_root",
            "buildings",
            "stage_coach",
            "store");
        var previews = new List<HeroQuirkLimitPreview>();

        foreach (var candidateQuirkId in candidateQuirks
                     .Select(pair => pair.Key)
                     .Order(StringComparer.OrdinalIgnoreCase))
        {
            var matchingDefinitions = initialQuirks
                .Where(quirk =>
                    quirk.Id.Equals(candidateQuirkId, StringComparison.OrdinalIgnoreCase) &&
                    quirk.DefinitionLimit is > 0)
                .ToArray();
            if (matchingDefinitions.Length == 0)
            {
                continue;
            }
            if (matchingDefinitions.Length != 1)
            {
                throw new InvalidDataException(
                    $"Limited quirk '{candidateQuirkId}' has multiple unresolved active definitions.");
            }

            var definition = matchingDefinitions[0];
            var existingRosterHeroes = rosterHeroes
                .Select(pair => pair.Value)
                .OfType<JsonObject>()
                .Count(hero => ContainsQuirk(hero, definition.Id));
            var existingStagecoachCandidates = stagecoachStore
                .Select(pair => pair.Value)
                .OfType<JsonObject>()
                .Select(pool => pool["generated"])
                .OfType<JsonObject>()
                .SelectMany(pool => pool.Select(pair => pair.Value))
                .OfType<JsonObject>()
                .Count(hero => ContainsQuirk(hero, definition.Id));
            previews.Add(new HeroQuirkLimitPreview(
                definition.Id,
                existingRosterHeroes,
                existingStagecoachCandidates,
                existingRosterHeroes + existingStagecoachCandidates + 1,
                definition.DefinitionLimit!.Value));
        }

        return previews;
    }

    private static void AddUpgradePurchases(
        JsonObject upgradesRoot,
        int candidateGuid,
        IReadOnlyList<HeroUpgradePurchase> requestedPurchases)
    {
        var purchases = JsonSupport.RequireObject(upgradesRoot, "base_root", "purchases");
        if (purchases.Any(pair =>
                pair.Value is JsonObject existing &&
                JsonSupport.ReadInt(existing, "instance_number") == candidateGuid))
        {
            throw new InvalidDataException(
                $"Upgrade purchases already exist for unused roster nextGuid '{candidateGuid}'.");
        }

        var normalized = requestedPurchases
            .Select(purchase =>
            {
                if (string.IsNullOrWhiteSpace(purchase.TreeId) ||
                    string.IsNullOrWhiteSpace(purchase.RequirementCode))
                {
                    throw new InvalidDataException(
                        "A stagecoach upgrade purchase must contain a tree id and requirement code.");
                }
                if (purchase.RequirementCode.Length != 1 || purchase.RequirementCode[0] > 0x7F)
                {
                    throw new InvalidDataException(
                        $"Upgrade requirement code '{purchase.RequirementCode}' cannot be written losslessly; " +
                        "persist.upgrades requires one ASCII character.");
                }

                return new
                {
                    TreeId = purchase.TreeId.Trim(),
                    RequirementCode = purchase.RequirementCode.Trim(),
                    TreeHash = unchecked((int)Loc2LocalizationReader.HashName(purchase.TreeId.Trim()))
                };
            })
            .ToArray();
        var treeHashCollision = normalized
            .GroupBy(purchase => purchase.TreeHash)
            .FirstOrDefault(group =>
                group.Select(purchase => purchase.TreeId)
                    .Distinct(StringComparer.Ordinal)
                    .Skip(1)
                    .Any());
        if (treeHashCollision is not null)
        {
            var treeIds = treeHashCollision
                .Select(item => item.TreeId)
                .Distinct(StringComparer.Ordinal);
            throw new InvalidDataException(
                "The stagecoach upgrade plan contains different tree ids with the same game hash: " +
                $"{string.Join(", ", treeIds)}.");
        }

        var duplicate = normalized
            .GroupBy(purchase => new { purchase.TreeId, purchase.RequirementCode })
            .FirstOrDefault(group => group.Skip(1).Any());
        if (duplicate is not null)
        {
            throw new InvalidDataException(
                "The stagecoach upgrade plan contains a duplicate purchase: " +
                $"{duplicate.First().TreeId}/{duplicate.First().RequirementCode}.");
        }

        var highestPurchaseKey = FindHighestNumericKey(purchases);
        if ((long)highestPurchaseKey + normalized.Length > int.MaxValue)
        {
            throw new InvalidDataException("Upgrade purchase keys are outside the supported range.");
        }

        var nextPurchaseKey = highestPurchaseKey + 1;
        foreach (var purchase in normalized)
        {
            var purchaseKey = nextPurchaseKey.ToString(CultureInfo.InvariantCulture);
            if (purchases.ContainsKey(purchaseKey))
            {
                throw new InvalidDataException($"Upgrade purchase key '{purchaseKey}' already exists.");
            }

            purchases[purchaseKey] = new JsonObject
            {
                ["instance_number"] = candidateGuid,
                ["tree_id"] = purchase.TreeHash,
                ["requirement_code"] = purchase.RequirementCode,
                ["is_purchased"] = true
            };
            nextPurchaseKey++;
        }
    }

    private static bool ContainsQuirk(JsonObject hero, string quirkId)
    {
        var quirks = hero["quirks"] as JsonObject ??
                     hero["hero_file_data"]?["raw_data"]?["base_root"]?["quirks"] as JsonObject;
        return quirks?.Any(pair => pair.Key.Equals(quirkId, StringComparison.OrdinalIgnoreCase)) == true;
    }

    private static int ReadRequiredNonNegativeInt(JsonObject candidate, string propertyName)
    {
        var value = JsonSupport.ReadInt(candidate, propertyName)
            ?? throw new InvalidDataException(
                $"A stagecoach candidate must contain an integer {propertyName} value.");
        if (value < 0)
        {
            throw new InvalidDataException(
                $"A stagecoach candidate {propertyName} value cannot be negative: {value}.");
        }

        return value;
    }

    private static void ValidateNextGuidInvariant(int nextGuid, JsonObject heroes, JsonObject store)
    {
        var highestUsedGuid = FindHighestNumericKey(heroes);
        foreach (var pair in store)
        {
            if (pair.Value is JsonObject recruitPool && recruitPool["generated"] is JsonObject pool)
            {
                highestUsedGuid = Math.Max(highestUsedGuid, FindHighestNumericKey(pool));
            }
        }

        if (highestUsedGuid >= nextGuid)
        {
            throw new InvalidDataException(
                $"Roster nextGuid '{nextGuid}' is not greater than the highest existing hero GUID '{highestUsedGuid}'.");
        }
    }

    private static bool ContainsGeneratedGuid(JsonObject store, string candidateKey)
    {
        return store.Any(pair =>
            pair.Value is JsonObject recruitPool &&
            recruitPool["generated"] is JsonObject generated &&
            generated.ContainsKey(candidateKey));
    }

    private static int FindHighestNumericKey(JsonObject values)
    {
        var highest = -1;
        foreach (var key in values.Select(pair => pair.Key))
        {
            if (int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value >= 0)
            {
                highest = Math.Max(highest, value);
            }
        }

        return highest;
    }
}
