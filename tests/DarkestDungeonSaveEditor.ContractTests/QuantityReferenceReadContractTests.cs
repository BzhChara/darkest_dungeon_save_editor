using System.Text.Json;

internal static partial class ContractSuite
{
    private static async Task RunQuantityReferenceReadContractsAsync(string runRoot, DsonSaveCodec codec)
    {
        var town = JsonNode.Parse("""{"base_root":{"wallet":{},"estate_items":{"items":{}}}}""")!.AsObject();
        var raid = JsonNode.Parse("""{"base_root":{"party":{"inventory":{"items":{}}}}}""")!.AsObject();
        const string emptyEvent = "{\"events\":[]}";
        string Event(string id, string? item = null) => JsonSerializer.Serialize(new
        {
            id, data = item is null ? [] : new[] { new { type = "bonus_currency", string_data = item } }
        });
        string Events(params string[] events) => "{\"events\":[" + string.Join(',', events) + "]}";
        foreach (var kind in new[] { "base", "mode", "dlc-feature", "local", "workshop", "dlc-mod" })
        foreach (var family in new[] { "event", "loot" })
        {
            var root = Path.Combine(runRoot, "reference-read", kind, family);
            var definitions = Path.Combine(root, "definitions");
            var resources = Path.Combine(root, "resources");
            var prefix = kind == "dlc-mod" ? "dlc/rq_feature/" : "";
            var type = family == "event" ? "heirloom" : "estate";
            WriteMultiMash(definitions, "inventory/a.inventory.items.darkest", string.Join('\n',
                new[] { "early", "late", "nested", "shadowed", "provision" }.Select(id =>
                    $"inventory_item: .type {type} .id {id} .base_stack_limit 2 .estate_can_be_provision {(id == "provision" ? "true" : "false")}")));
            WriteMultiMash(definitions, "inventory/a.inventory.system_configs.darkest", QueryCapacity(8));
            if (family == "loot") WriteMultiMash(definitions, "heroes/audit/audit.info.darkest",
                "extra_battle_loot: .code audit_root\nextra_curio_loot: .code empty_root\n");
            WriteFixtureManifest(definitions);
            string Relative(string stem) => prefix + (family == "event" ? $"campaign/town_events/{stem}.town_events.events.json" : $"loot/{stem}.loot.json");
            var first = WriteMultiMash(resources, Relative("a"), family == "event"
                ? Events(Event("known", "early"), Event("empty"))
                : LootFile(LootTable("audit_root", LootItem("early"), "\"difficulty\":1"),
                    LootTable("child", LootItem("nested")), LootTable("empty_root", "")));
            var middle = WriteMultiMash(resources, Relative("m"), family == "event" ? emptyEvent : LootFile());
            WriteMultiMash(resources, Relative("z"), family == "event"
                ? Events(Event("known", "shadowed"), Event("empty", "shadowed"), Event("new", "late"))
                : LootFile(LootTable("audit_root", LootItem("late") + "," + LootChild("child")),
                    LootTable("empty_root", LootItem("shadowed"))));
            WriteFixtureManifest(resources);
            var sources = QuerySources(resources, kind).Append(new ActiveContentSource("defs", "Definitions", "local", definitions, -1000)).ToArray();
            var content = QueryContent(root, sources);
            QuantityItemCatalogResult Load() => family == "event" ? QuantityItemCatalog.Load(content, town) : QuantityItemCatalog.LoadRaid(content, raid);
            var healthy = Load();
            Assert(healthy.Items.Single(i => i.DisplayId == "early").ReferenceStatus == QuantityItemReferenceStatus.ConfirmedActive &&
                healthy.Items.Single(i => i.DisplayId == "late").ReferenceStatus == QuantityItemReferenceStatus.ConfirmedActive &&
                healthy.Items.Single(i => i.DisplayId == "shadowed").IsHiddenByDefault,
                $"{kind}/{family}: invalid ordered reference fixture.");
            var original = File.ReadAllBytes(middle);
            var faults = new List<string> { "locked", "malformed" };
            if (kind is "local" or "workshop" or "dlc-mod") faults.Add("missing");
            if (family == "event") faults.Add("decode-failure");
            foreach (var fault in faults)
            {
                QuantityItemCatalogResult failed;
                try
                {
                    if (fault == "missing") File.Delete(middle);
                    if (fault == "malformed") File.WriteAllText(middle, "{ broken JSON");
                    if (fault == "decode-failure") File.WriteAllText(middle, Events(Event("partial", "nested"),
                        JsonSerializer.Serialize(new { id = "broken", data = new[] { new { type = new string('x', 62) + "中" } } })));
                    using var held = fault == "locked" ? File.Open(middle, FileMode.Open, FileAccess.Read, FileShare.None) : null;
                    failed = Load();
                    Assert(failed.Items.Single(i => i.DisplayId == "early").ReferenceStatus == QuantityItemReferenceStatus.ConfirmedActive &&
                        failed.Items.Single(i => i.DisplayId == "provision").ReferenceStatus == QuantityItemReferenceStatus.ConfirmedActive,
                        "Previously confirmed winners and independent provision must survive a later failure.");
                    var late = failed.Items.Single(i => i.DisplayId == "late");
                    Assert(late.ReferenceStatus == QuantityItemReferenceStatus.AnalysisIncomplete && late.ReferenceEvidence.Any(e => e.Contains("无法确认", StringComparison.Ordinal)),
                        $"{kind}/{family}/{fault}: later evidence must explain its uncertain selection.");
                    if (family == "loot")
                        Assert(failed.Items.Single(i => i.DisplayId == "nested").ReferenceStatus == QuantityItemReferenceStatus.AnalysisIncomplete,
                            "An earlier readable child remains uncertain when its only parent is after a failed slot.");
                    else if (fault == "decode-failure")
                        Assert(failed.Items.Single(i => i.DisplayId == "nested").ReferenceStatus == QuantityItemReferenceStatus.AnalysisIncomplete,
                            "A valid event before a string decode failure in the same file must not publish partial evidence.");
                    Assert(failed.Items.Single(i => i.DisplayId == "shadowed").ReferenceStatus != QuantityItemReferenceStatus.ConfirmedActive &&
                        failed.Items.Single(i => i.DisplayId == "shadowed").ReferenceEvidence.All(e => !e.Contains("z.", StringComparison.Ordinal)),
                        "An earlier empty winner must still suppress the later duplicate, including under a failed scan.");
                    var updated = family == "event" ? QuantityItemSaveEditor.SetAmount(town, late, 5).UpdatedRoot : RaidInventorySaveEditor.SetAmount(raid, late, 5, 8).UpdatedRoot;
                    var refreshed = QuantityItemCatalog.RefreshSavedAmounts(content, failed, updated, "reference-read-refresh").Items.Single(i => i.DisplayId == "late");
                    Assert(refreshed.CurrentAmount == 5 && refreshed.ReferenceStatus == QuantityItemReferenceStatus.AnalysisIncomplete && refreshed.ReferenceEvidence.SequenceEqual(late.ReferenceEvidence),
                        "Amount-only refresh must preserve uncertain evidence while adopting the new quantity.");
                    if (family == "loot")
                    {
                        var slots = JsonSupport.RequireObject(updated, "base_root", "party", "inventory", "items");
                        Assert(slots.Count == 3 && slots.Sum(p => p.Value!["amount"]!.GetValue<int>()) == 5 &&
                            slots.All(p => p.Value!["amount"]!.GetValue<int>() <= 2), "Uncertainty must not alter 2/2/1 stack allocation.");
                    }
                    else
                        Assert(QuantityItemCatalog.LoadRaid(content, raid).Items.Where(i => i.DisplayId != "provision").All(i => i.IsHiddenByDefault),
                            "Town-event failures must not contaminate the raid reference context.");
                }
                finally { if (fault != "locked") File.WriteAllBytes(middle, original); }
                Assert(Load().Items.Select(i => i.ReferenceStatus).SequenceEqual(healthy.Items.Select(i => i.ReferenceStatus)), "Restoring bytes must restore complete reference classification.");
            }
            // A wholly shadowed provider never occupies a selected read slot.
            var overlay = Path.Combine(root, "overlay");
            WriteMultiMash(overlay, Relative("a"), File.ReadAllText(first));
            WriteFixtureManifest(overlay);
            var overridden = content with { Sources = sources.Append(new ActiveContentSource("overlay", "Overlay", "local", overlay, -2000)).ToArray() };
            using (var held = File.Open(first, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var catalog = family == "event" ? QuantityItemCatalog.Load(overridden, town) : QuantityItemCatalog.LoadRaid(overridden, raid);
                Assert(catalog.Items.Select(i => i.ReferenceStatus).SequenceEqual(healthy.Items.Select(i => i.ReferenceStatus)),
                    "An unreadable shadowed file must not invalidate its complete selected replacement.");
            }
            Console.WriteLine($"PASS: {kind}/{family} ordered reference failures, earlier winners, nested conditions, independent provision, stacking and recovery.");
        }
        await VerifyQuantityReferenceReadSyncAsync(Path.Combine(runRoot, "reference-read-sync"), codec);
    }

    private static async Task VerifyQuantityReferenceReadSyncAsync(string root, DsonSaveCodec codec)
    {
        var gameRoot = Path.Combine(root, "game");
        var mod = Path.Combine(gameRoot, "mods", "reference");
        WriteMultiMash(mod, "project.xml", "<project><Title>Reference Sync</Title></project>");
        WriteMultiMash(mod, "inventory/a.inventory.items.darkest", "inventory_item: .type heirloom .id sync_token .base_stack_limit 2 .estate_can_be_provision false\n");
        var first = WriteMultiMash(mod, "campaign/town_events/a.town_events.events.json", """{"events":[{"id":"sync_event","data":[]}]}""");
        WriteMultiMash(mod, "campaign/town_events/z.town_events.events.json", """{"events":[{"id":"sync_event","data":[{"type":"event_cost","string_data":"sync_token"}]}]}""");
        WriteFixtureManifest(mod);
        var manifestHash = ComputeSha256(Path.Combine(mod, "modfiles.txt"));
        var profileRoot = Path.Combine(root, "profile");
        WriteMultiMash(profileRoot, "persist.game.json", """{"base_root":{"game_mode":"base","inraid":false,"raiddungeon":"none","applied_ugcs_1_0":{"0":{"name":"Reference Sync","source":"mod_local_source"}}}}""");
        WriteMultiMash(profileRoot, "persist.estate.json", """{"base_root":{"wallet":{},"estate_items":{"items":{}}}}""");
        foreach (var name in new[] { "roster", "town", "upgrades" }) WriteMultiMash(profileRoot, $"persist.{name}.json", "{\"base_root\":{}}");
        var profile = new SaveProfile("reference-sync", profileRoot, Path.Combine(profileRoot, "persist.estate.json"), "contract", DateTime.UtcNow);
        var originalHashes = ProfileCatalogSnapshotReader.CaptureHashes(profile);
        var content = await ActiveContentResolver.ResolveAsync(profile, gameRoot, null, codec, root);
        var initial = await QuantityItemCatalog.LoadAsync(content, codec);
        var reader = new ProfileCatalogSnapshotReader(content, initial, codec, gameRoot, null, null, root);
        var healthy = await reader.ReadAsync();
        Assert(healthy.QuantityItems.Items.Single().IsHiddenByDefault, "Healthy empty first event must suppress later cost reference.");
        var original = File.ReadAllBytes(first);
        foreach (var fault in new[] { "missing", "malformed", "locked" })
        {
            try
            {
                if (fault == "missing") File.Delete(first);
                if (fault == "malformed") File.WriteAllText(first, "{ incomplete JSON");
                using var held = fault == "locked" ? File.Open(first, FileMode.Open, FileAccess.Read, FileShare.None) : null;
                if (fault == "locked")
                    Assert(await CaptureSaveFailureAsync(async () => { _ = await reader.ReadAsync(refreshContent: true); }) is IOException,
                        "Locked resource fingerprints must reject speculative snapshots.");
                else
                {
                    var snapshot = await reader.ReadAsync(refreshContent: true);
                    Assert(snapshot.QuantityItems.Items.Single().ReferenceStatus == QuantityItemReferenceStatus.AnalysisIncomplete && snapshot.ContentFingerprint != healthy.ContentFingerprint,
                        "Shared resource refresh must publish uncertainty, not a later event winner.");
                }
            }
            finally { if (fault != "locked") File.WriteAllBytes(first, original); }
            var restored = await reader.ReadAsync(refreshContent: true);
            Assert(restored.QuantityItems.Items.Single().IsHiddenByDefault && ProfileCatalogSnapshotReader.HashesEqual(originalHashes, restored.FileHashes) &&
                manifestHash == ComputeSha256(Path.Combine(mod, "modfiles.txt")), "Recovery must not require save or manifest edits.");
            var idle = await reader.ReadAsync(refreshContent: true);
            Assert(ReferenceEquals(restored.QuantityItems, idle.QuantityItems), "Idle polling must still reuse the restored catalog.");
        }
        Console.WriteLine("PASS: reference read failure snapshots, unchanged-manifest recovery and idle cache reuse.");
    }
}
