internal static partial class ContractSuite
{
    private static void VerifyNativeItemReferences(ActiveContentSnapshot original, string runRoot)
    {
        var root = Path.Combine(runRoot, "native-item-references");
        void Write(string path, string text) => WriteMultiMash(root, path, text);
        var ids = new[] { "nr_good", "nr_bad_monster", "nr_bad_hero", "nr_commented", "nr_superseded", "nr_inline" };
        Write("inventory/native.inventory.items.darkest", string.Join('\n', ids.Select(id =>
            $"inventory_item: .type estate .id {id} .base_stack_limit 3 .estate_can_be_provision false")));
        Write("loot/native.loot.json", new JsonObject
        {
            ["loot_tables"] = new JsonArray(ids.Select(id => (JsonNode)new JsonObject
            {
                ["id"] = id, ["entries"] = new JsonArray(new JsonObject
                {
                    ["type"] = "item", ["chances"] = 1,
                    ["data"] = new JsonObject { ["type"] = "estate", ["id"] = id, ["amount"] = 1 }
                })
            }).ToArray())
        }.ToJsonString());
        Write("monsters/sorting/native_ref_A.info.darkest", "loot: .code \"nr_bad_monster\"\n");
        Write("heroes/sorting/native_ref.info.darkest", "extra_battle_loot: .code \"nr_bad_hero\"\n");
        Write("monsters/native_ref/native_ref_A/native_ref_A.info.darkest", """
            stats: .hp 10
            /* loot: .code "nr_commented" */
            loot:
              .code "nr_superseded" .code nr_good
            """);
        Write("heroes/native_ref/native_ref.info.darkest", "stats: .hp 20 extra_battle_loot: .code nr_inline\n");
        WriteFixtureManifest(root);
        var content = original with { Sources = original.Sources.Append(
            new ActiveContentSource("local:native-references", "Native references", "local", root, -1900)).ToArray() };
        var raid = new JsonObject { ["base_root"] = new JsonObject
        { ["party"] = new JsonObject { ["inventory"] = new JsonObject { ["items"] = new JsonObject() } } } };
        var catalog = QuantityItemCatalog.LoadRaid(content, raid);
        foreach (var id in ids)
        {
            var item = catalog.Items.Single(row => row.ItemId == id);
            var expected = id is "nr_good" or "nr_inline"
                ? QuantityItemReferenceStatus.ConfirmedActive : QuantityItemReferenceStatus.SuspectedUnused;
            Assert(item.ReferenceStatus == expected, $"Native item reference {id} must be {expected}, found {item.ReferenceStatus}.");
        }
        Console.WriteLine("PASS: canonical actor reference paths and native comment/record/last-field item reachability.");
    }
}
