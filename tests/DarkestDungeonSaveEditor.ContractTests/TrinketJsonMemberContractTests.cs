internal static partial class ContractSuite
{
    private static async Task VerifyTrinketJsonMembersAsync(ActiveContentSnapshot original, string runRoot, DsonSaveCodec codec)
    {
        var root = Path.Combine(runRoot, "trinket-json-members");
        var high = Path.Combine(root, "high");
        var low = Path.Combine(root, "low");
        const string path = "trinkets/members.entries.trinkets.json";
        WriteMultiMash(high, path, """
            {"entries":[
              {"id":"tj_counter","id":"tj_wrong_id","rarity":"common","rarity":"rare",
               "limit":1,"limit":9,"price":100,"price":900,
               "quest_uses":2,"quest_uses":7,"trigger_limit":3,"trigger_limit":8,
               "hero_class_requirements":[],"hero_class_requirements":["absent_hero"]},
              {"id":"tj_counter","quest_uses":99,"trigger_limit":99},
              {"id":"tj_bad_counter","quest_uses":null,"quest_uses":2,"trigger_limit":-1,"trigger_limit":3},
              {"id":"tj_bad_number","limit":null,"limit":9,"price":"invalid","price":100},
              {"id":null,"id":"tj_bad_id"},
              {"id":"tj_bad_requirement","hero_class_requirements":["absent_hero"],"hero_class_requirements":[]}
            ],"entries":[{"id":"tj_wrong_root"}]}
            """);
        // The losing provider does not declare tj_counter in its FIRST root array.
        WriteMultiMash(low, path, """{"entries":[{"id":"tj_low"}],"entries":[{"id":"tj_counter"}]}""");
        WriteFixtureManifest(high);
        var content = original with { Sources = [
            new("base", "Base", "base", low, 0), new("local:tj", "Trinket members", "local", high, 1000)] };
        var catalog = TrinketCatalog.Load(content);
        var counter = catalog.Trinkets.Single(t => t.Id == "tj_counter");
        Assert(counter is { Limit: 1, Price: 100, Rarity: "common", QuestUses: 2, TriggerLimit: 3 } &&
               counter.AllSources.SequenceEqual(["local:tj"]) &&
               catalog.Trinkets.Select(t => t.Id).Order().SequenceEqual(new[] { "tj_bad_counter", "tj_bad_number", "tj_counter" }),
            "Trinket root arrays, IDs, requirements, numbers and provenance must take first JSON members; repeated definition IDs still select the first entry.");
        Assert(catalog.Trinkets.Single(t => t.Id == "tj_bad_number") is { Limit: null, Price: null } &&
               catalog.Trinkets.Single(t => t.Id == "tj_bad_counter") is { QuestUses: null, TriggerLimit: -1 },
            "Wrong-typed first fields keep the default and negative integers are retained; later valid members must not replace them.");

        var estate = JsonNode.Parse("""{"base_root":{"version":1,"trinkets":{"items":{}}}}""")!.AsObject();
        var (updated, _) = TrinketSaveEditor.AddCopies(estate, counter, 2, 100);
        var proposed = Path.Combine(root, "estate.proposed.json");
        var binary = Path.Combine(root, "estate.dson");
        var restored = Path.Combine(root, "estate.roundtrip.json");
        File.WriteAllText(proposed, updated.ToJsonString());
        await codec.EncodeAsync(proposed, binary, null);
        await codec.DecodeAsync(binary, restored);
        var items = JsonNode.Parse(File.ReadAllText(restored))!["base_root"]!["trinkets"]!["items"]!.AsObject();
        Assert(items.Count == 2 && items.All(pair => pair.Value!["id"]!.GetValue<string>() == "tj_counter" &&
               pair.Value["quest_uses_remaining"]!.GetValue<int>() == 2 && pair.Value["triggers_remaining"]!.GetValue<int>() == 3),
            "Every created trinket instance must persist first-field counters through actual DSON encoding and decoding.");
        var defaulted = TrinketSaveEditor.AddCopies(estate, catalog.Trinkets.Single(t => t.Id == "tj_bad_counter"), 1, 100).UpdatedRoot;
        var instance = defaulted["base_root"]!["trinkets"]!["items"]!["0"]!.AsObject();
        Assert(!instance.ContainsKey("quest_uses_remaining") && !instance.ContainsKey("triggers_remaining"),
            "Optional fields ignored by the loader and negative counters must not block creation or invent saved counters.");

        WriteMultiMash(high, path, """{"entries":null,"entries":[{"id":"tj_later_root"}]}""");
        Assert(TrinketCatalog.Load(content).Trinkets.Count == 0,
            "A wrong-typed first root array must not fall back to another array or the shadowed provider.");
        Console.WriteLine("PASS: first trinket JSON members, invalid first values, provider provenance and instance-counter DSON roundtrip.");
    }
}
