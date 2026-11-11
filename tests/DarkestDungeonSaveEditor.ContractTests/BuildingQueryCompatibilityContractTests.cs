internal static partial class ContractSuite
{
    private static void VerifyNativeBuildingQueries(string root)
    {
        var town = JsonNode.Parse("""{"base_root":{"wallet":{},"estate_items":{"items":{}}}}""")!.AsObject();
        string Activity(string id)
        {
            var data = JsonNode.Parse(RuntimeActivity(id: id))!;
            data["data"]!["activities"]![0]!["data"]!["side_effects"]!["results"]!.AsArray().Add(JsonNode.Parse(
                """{"type":"add_currency","chance":1,"data":[{"chance":1,"type":"query_token","amount":1}]}"""));
            return data.ToJsonString();
        }
        void Check(LootSourceFixture f, QuantityItemReferenceStatus expected, string label)
        {
            f.Check(expected, label, globalIncomplete: expected == QuantityItemReferenceStatus.AnalysisIncomplete);
            var item = QuantityItemCatalog.Load(f.Files.Content, town).Items.Single(i => i.ItemId == "query_token");
            Assert(item.ReferenceStatus == expected && item.IsHiddenByDefault == (expected == QuantityItemReferenceStatus.SuspectedUnused),
                label + ": the town currency consumer must use the same building query and identity as the loot consumer");
            Assert(!item.IsPresentInSave && item.CurrentAmount == 0, label + ": analysis must not invent inventory quantities");
        }
        foreach (var kind in new[] { "local", "workshop", "dlc-mod" })
        {
            foreach (var (building, activity) in new[] { ("tavern", "bar"), ("abbey", "meditation"), ("sanitarium", "treatment") })
            foreach (var test in new[] { "standard", "nested", "prefix", "wildcards", "wrong-name", "wrong-directory", "wrong-case", "missing", "malformed", "unlisted" })
            {
                var f = new LootSourceFixture(Path.Combine(root, kind, building, test), kind);
                var relative = test switch
                {
                    "standard" => $"campaign/town/buildings/{building}/{building}.building.json",
                    "prefix" => $"campaign/town/buildings/custom_{building}/prefix{building}.building.json",
                    "wildcards" => $"campaign/town/buildings/custom/{building}/nested/prefix{building}XbuildingYjson",
                    "wrong-name" => $"campaign/town/buildings/custom/{building}/unrelated.building.json",
                    "wrong-directory" => $"campaign/town/buildings/unrelated/{building}.building.json",
                    "wrong-case" => $"campaign/town/buildings/custom/{building.ToUpperInvariant()}/{building}.building.json",
                    _ => $"campaign/town/buildings/custom/{building}/{building}.building.json"
                };
                var text = test == "malformed" ? "{" : Activity(activity);
                var file = test == "unlisted" ? WriteMultiMash(f.Files.Source, f.Files.Prefix + relative, text) : f.Write(relative, text);
                if (test == "missing") File.Delete(file);
                var expected = test is "wrong-name" or "wrong-directory" or "wrong-case" or "unlisted"
                    ? QuantityItemReferenceStatus.SuspectedUnused
                    : test is "missing" or "malformed" ? QuantityItemReferenceStatus.AnalysisIncomplete
                    : QuantityItemReferenceStatus.ConfirmedActive;
                Check(f, expected, kind + "/" + building + "/" + test);
            }
            var overlay = new LootSourceFixture(Path.Combine(root, kind, "overlay"), kind);
            const string path = "campaign/town/buildings/custom/tavern/tavern.building.json";
            var low = WriteMultiMash(overlay.Files.Content.Sources.Single(s => s.Id == "base").Directory, path, Activity("bar"));
            var high = overlay.Write(path, "{}");
            Check(overlay, QuantityItemReferenceStatus.SuspectedUnused, "Flags-0 building query replaces the whole same-path provider");
            File.WriteAllText(low, "{");
            File.WriteAllText(high, Activity("bar"));
            Check(overlay, QuantityItemReferenceStatus.ConfirmedActive, "A malformed shadowed building does not taint a readable winner");
            File.WriteAllText(high, "{");
            Check(overlay, QuantityItemReferenceStatus.AnalysisIncomplete, "A malformed effective building prevents an unused verdict");
            File.WriteAllText(high, "{}");
            Check(overlay, QuantityItemReferenceStatus.SuspectedUnused, "A repaired empty building restores completeness");
            // Different paths participate in the same consumer query; a nested
            // directory must not invent a separate building or overwrite another path.
            overlay.Write("campaign/town/buildings/other_tavern/tavern.building.json", Activity("bar"));
            Check(overlay, QuantityItemReferenceStatus.ConfirmedActive, "Distinct matching paths both feed the tavern consumer");
            Console.WriteLine($"PASS: {kind} building query paths, native identity, currency/loot consumers, overlays and failed providers.");
        }
    }
}
