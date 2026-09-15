using System.Text.Json;
using ReferenceStatus = DarkestDungeonSaveEditor.Core.QuantityItemReferenceStatus;

internal static partial class ContractSuite
{
    public static async Task RunReferenceConsumersOnlyAsync(string repositoryRoot)
    {
        var fixture = BuildContractFixture(repositoryRoot);
        await RunNonActorReferenceContractsAsync(fixture.RunRoot, fixture.Codec);
        Console.WriteLine($"Artifacts: {fixture.RunRoot}");
    }

    private static async Task RunNonActorReferenceContractsAsync(string runRoot, DsonSaveCodec codec)
    {
        var run = Path.Combine(runRoot, "nonactor-references");
        var definitions = Path.Combine(run, "definitions");
        var ids = new[] { "na_pair", "na_loot", "na_unused" };
        WriteMultiMash(definitions, "inventory/a.inventory.items.darkest", string.Join('\n', ids.Select(id =>
            $"inventory_item: .type estate .id {id} .base_stack_limit 2 .estate_can_be_provision false")));
        WriteMultiMash(definitions, "inventory/a.inventory.system_configs.darkest", QueryCapacity(8));
        WriteMultiMash(definitions, "loot/a.loot.json", """
            {"loot_tables":[
              {"id":"na_root","entries":[{"type":"table","chances":1,"data":{"table":"na_nested"}}]},
              {"id":"na_nested","entries":[{"type":"item","chances":1,"data":{"type":"estate","id":"na_loot","amount":1}}]},
              {"id":"na_trusted","entries":[{"type":"item","chances":1,"data":{"type":"estate","id":"na_pair","amount":1}}]}]}
            """);
        WriteFixtureManifest(definitions);
        var definitionsSource = new ActiveContentSource("local:na-defs", "Reference definitions", "local", definitions, -4000);
        var cases = 0;
        var observations = new List<object>();
        var roundtrips = new List<object>();
        JsonObject Empty(bool town) => JsonNode.Parse(town
            ? """{"base_root":{"wallet":{},"estate_items":{"items":{}}}}"""
            : """{"base_root":{"party":{"inventory":{"items":{}}}}}""")!.AsObject();
        (ActiveContentSnapshot Content, string Path, string Source) Create(string kind, string path, string text)
        {
            var root = Path.Combine(run, (++cases).ToString());
            var source = Path.Combine(root, "source");
            var prefix = kind == "dlc-mod" && !path.StartsWith("heroes/") && !path.StartsWith("monsters/")
                ? "dlc/rq_feature/" : "";
            var absolute = WriteMultiMash(source, prefix + path, text);
            WriteFixtureManifest(source);
            var content = QueryContent(root, QuerySources(source, kind).Append(definitionsSource).ToArray());
            return (content, absolute, source);
        }
        void Check(ActiveContentSnapshot content, string label, Func<bool, string, ReferenceStatus> expected,
            Func<bool, bool>? warnings = null)
        {
            foreach (var town in new[] { true, false })
            {
                var catalog = town ? QuantityItemCatalog.Load(content, Empty(true)) : QuantityItemCatalog.LoadRaid(content, Empty(false));
                foreach (var id in ids)
                {
                    var item = catalog.Items.Single(i => i.ItemId == id);
                    var status = expected(town, id);
                    Assert(item.ReferenceStatus == status && item.IsHiddenByDefault == (status == ReferenceStatus.SuspectedUnused),
                        $"{label}/{town}/{id}: expected {status}, got {item.ReferenceStatus} ({string.Join("; ", item.ReferenceEvidence)}).");
                    Assert(!item.IsPresentInSave && item.CurrentAmount == 0 && item.BaseStackLimit == 2,
                        $"{label}: reference classification must not change quantities or stack limits.");
                }
                Assert(catalog.Issues.Any() == (warnings?.Invoke(town) ?? false),
                    $"{label}/{town}: unexpected diagnostics: {string.Join("; ", catalog.Issues)}.");
                observations.Add(new { Label = label, Town = town, Items = catalog.Items.Select(i => new
                { i.ItemId, Status = i.ReferenceStatus.ToString(), i.IsHiddenByDefault, i.ReferenceEvidence }).ToArray(), catalog.Issues });
            }
        }
        static ReferenceStatus Unused(bool town, string id) => ReferenceStatus.SuspectedUnused;
        const string noise = "notes: .type estate .id na_pair\nnotes: .item_id na_pair\nnotes: .use_item_id na_pair\nloot: .code na_root\n";
        foreach (var kind in new[] { "base", "mode", "dlc-feature", "local", "workshop", "dlc-mod" })
        {
            // Neither a listed file nor a missing manifest entry can bypass a
            // known consumer's filename query, including case and nested paths.
            foreach (var folder in new[] { "campaign/provision", "campaign/town_events", "campaign/estate", "campaign/quest",
                         "campaign/town/districts", "campaign/town/buildings/audit", "upgrades", "curios" })
            foreach (var name in new[] { "notes.darkest", "nested/arbitrary.DARKEST" })
            {
                var path = folder + "/" + name;
                var f = Create(kind, path, noise);
                Check(f.Content, $"{kind}/ignored/{path}", Unused);
                File.Delete(f.Path); // Only this test's generated file; keep its manifest entry.
                Check(f.Content, $"{kind}/missing-ignored/{path}", Unused);
            }
            foreach (var folder in new[] { "campaign/Provision", "campaign/Town_events" })
            {
                var f = Create(kind, folder + "/notes.darkest", noise);
                Check(f.Content, $"{kind}/case-directory/{folder}", Unused);
                File.Delete(f.Path);
                Check(f.Content, $"{kind}/missing-case-directory/{folder}", Unused);
            }

            var native = new (string Path, string Text, bool Town, bool Raid, string[] Items)[]
            {
                ("campaign/provision/nested/a.provisionXjson", """{"default_store_inventory_item_lists":[[{"type":"estate","id":"na_pair","amount":1}]]}""", false, true, ["na_pair"]),
                ("campaign/town_events/a.town_events.events.json", """{"events":[{"id":"na_event","data":[{"type":"bonus_currency","string_data":"na_pair","number_data":1}]}]}""", true, false, ["na_pair"]),
                ("campaign/estate/a.estate.json", """{"currencies":[{"id":"na_pair"}]}""", true, false, ["na_pair"]),
                ("upgrades/a.upgrades.json", """{"trees":[{"id":"na_tree","requirements":[{"code":"0","currency_cost":[{"type":"na_pair","amount":1}]}]}]}""", true, false, ["na_pair"]),
                ("curios/nested/a_curio_type_library.csv", ",,ID STRING,,RESULT TYPES\n,,na_curio,,Nothing\n,,REGION FOUND,,Loot,1,100%,na_root,1\n,,Item Interactions,,ITEM\n,,,,na_pair#estate,Nothing\n", false, true, ["na_pair", "na_loot"]),
                ("heroes/na/na.info.darkest", "extra_battle_loot: .code na_root\nextra_curio_loot: .code na_trusted\n", false, true, ["na_pair", "na_loot"]),
                ("monsters/na/na_A/na_A.info.darkest", "loot: .code na_root\n", false, true, ["na_loot"])
            };
            foreach (var entry in native)
            {
                var f = Create(kind, entry.Path, entry.Text);
                Check(f.Content, $"{kind}/native/{entry.Path}", (town, id) =>
                    (town ? entry.Town : entry.Raid) && entry.Items.Contains(id) ? ReferenceStatus.ConfirmedActive : ReferenceStatus.SuspectedUnused);
                if (entry.Path.StartsWith("heroes/") || entry.Path.StartsWith("monsters/"))
                {
                    File.WriteAllText(f.Path, noise.Replace("loot: .code", "notes: .code", StringComparison.Ordinal));
                    Check(f.Content, $"{kind}/actor-unknown-records/{entry.Path}", Unused);
                    continue;
                }
                var fingerprint = ProfileCatalogContentFingerprint.Capture(f.Content.Sources);
                File.AppendAllText(f.Path, "\n");
                Assert(ProfileCatalogContentFingerprint.Capture(f.Content.Sources) != fingerprint,
                    "A consumed resource content change must still invalidate catalog refresh.");
                if (entry.Path.Contains("provision") || entry.Path.Contains("town_events"))
                {
                    var town = entry.Town;
                    var original = Empty(town);
                    var originalText = original.ToJsonString();
                    var catalog = town ? QuantityItemCatalog.Load(f.Content, original) : QuantityItemCatalog.LoadRaid(f.Content, original);
                    var item = catalog.Items.Single(i => i.ItemId == "na_pair");
                    var mutation = town ? QuantityItemSaveEditor.SetAmount(original, item, 5)
                        : RaidInventorySaveEditor.SetAmount(original, item, 5, catalog.RaidStorage!.MaxSlots);
                    Assert(original.ToJsonString() == originalText, "A quantity preview must not mutate its input.");
                    var proposed = WriteMultiMash(f.Content.WorkspaceDirectory, "proposed.json", mutation.UpdatedRoot.ToJsonString());
                    var encoded = Path.Combine(f.Content.WorkspaceDirectory, "encoded.dson");
                    var decoded = Path.Combine(f.Content.WorkspaceDirectory, "decoded.json");
                    await codec.EncodeAsync(proposed, encoded, null);
                    await codec.DecodeAsync(encoded, decoded);
                    var saved = JsonNode.Parse(File.ReadAllText(decoded))!.AsObject();
                    var reloaded = town ? QuantityItemCatalog.Load(f.Content, saved) : QuantityItemCatalog.LoadRaid(f.Content, saved);
                    Assert(reloaded.Items.Single(i => i.ItemId == "na_pair").CurrentAmount == 5 && JsonNode.DeepEquals(saved, mutation.UpdatedRoot),
                        "DSON must preserve the full quantity mutation and its item identity.");
                    var items = town ? saved["base_root"]!["estate_items"]!["items"]!.AsObject()
                        : saved["base_root"]!["party"]!["inventory"]!["items"]!.AsObject();
                    var amounts = items.Select(p => p.Value!["amount"]!.GetValue<int>()).OrderDescending().ToArray();
                    Assert(amounts.SequenceEqual(town ? [5] : new[] { 2, 2, 1 }), "Wrong quantity stack distribution.");
                    roundtrips.Add(new { Kind = kind, Town = town, Amounts = amounts, Proposed = proposed, Encoded = encoded, Decoded = decoded });
                }
                File.Delete(f.Path);
                var manifest = kind is "local" or "workshop" or "dlc-mod";
                Check(f.Content, $"{kind}/missing-native/{entry.Path}", (town, id) =>
                    manifest && (town ? entry.Town : entry.Raid) ? ReferenceStatus.AnalysisIncomplete : ReferenceStatus.SuspectedUnused,
                    town => manifest && (town ? entry.Town : entry.Raid));
            }

            // Keep unknown non-actor consumers conservative, without claiming
            // native item use or contaminating unrelated definitions.
            foreach (var town in new[] { false, true })
            foreach (var text in new[] { "notes: .type estate .id na_pair", "notes: .item_id na_pair", "notes: .use_item_id na_pair",
                         "loot: .code na_root", "notes: .type estate .id na_unused .id na_pair", "// notes: .item_id na_pair" })
            {
                var f = Create(kind, town ? "campaign/town/custom/unknown.darkest" : "dungeons/ruins/campaign/provision/unknown.darkest", text + "\n");
                var comment = text.StartsWith("//");
                var target = text.StartsWith("loot:") ? "na_loot" : "na_pair";
                Check(f.Content, $"{kind}/unverified/{town}/{text}", (context, id) =>
                    context == town && id == target && !comment ? ReferenceStatus.AnalysisIncomplete : ReferenceStatus.SuspectedUnused,
                    context => context == town && !comment);
                if (comment) continue;
                var before = ProfileCatalogContentFingerprint.Capture(f.Content.Sources);
                File.WriteAllText(f.Path, "// " + text + "\n");
                Assert(ProfileCatalogContentFingerprint.Capture(f.Content.Sources) != before, "Unverified reference edits must refresh classification.");
                Check(f.Content, $"{kind}/unverified-commented/{town}/{text}", Unused);
            }

            // The same ID can have both a proven root and an uncertain root.
            var mixed = Create(kind, "dungeons/ruins/unknown.darkest", noise);
            WriteMultiMash(mixed.Source, "heroes/na/na.info.darkest", "extra_battle_loot: .code na_trusted\n");
            WriteFixtureManifest(mixed.Source);
            Check(mixed.Content, $"{kind}/mixed", (town, id) => town || id == "na_unused" ? ReferenceStatus.SuspectedUnused
                : id == "na_pair" ? ReferenceStatus.ConfirmedActive : ReferenceStatus.AnalysisIncomplete, town => !town);
        }
        File.WriteAllText(Path.Combine(run, "results.json"), JsonSerializer.Serialize(new { Cases = cases, Observations = observations, Roundtrips = roundtrips },
            new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"PASS: non-actor reference consumers: {cases} cases, {observations.Count} catalog checks, {roundtrips.Count} DSON roundtrips; ignored files, uncertainty, actor/JSON/Curio controls, diagnostics and refresh.");
    }
}
