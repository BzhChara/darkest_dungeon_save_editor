internal static partial class ContractSuite
{
    private const string ProviderItemPath = "inventory/new_mod.inventory.items.darkest";
    private const string ProviderTrinketPath = "trinkets/new_mod.entries.trinkets.json";

    private static void WriteProviderDefinitions(string root, int limit)
    {
        WriteMultiMash(root, ProviderItemPath, $"inventory_item: .type estate .id probe_token .base_stack_limit {limit}\n");
        WriteMultiMash(root, ProviderTrinketPath,
            $$"""{"entries":[{"id":"probe_trinket","quest_uses":{{limit}},"trigger_limit":{{limit}}}]}""");
    }

    private static IReadOnlyList<ActiveContentSource> BuildProviderSources(string root, string kind, string mode)
    {
        var baseline = Path.Combine(root, "base");
        var low = Path.Combine(root, "low");
        var high = Path.Combine(root, "high");
        WriteMultiMash(baseline, "inventory/base.inventory.system_configs.darkest",
            "inventory_system_config: .type raid .max_slots 32\ninventory_system_config: .type trinket_storage .max_slots 100\n");
        if (mode != "low-missing") WriteProviderDefinitions(low, 2);
        if (mode != "high-missing") WriteProviderDefinitions(high, 5);
        WriteMultiMash(low, "modfiles.txt", ProviderItemPath + "\n" + ProviderTrinketPath + "\n");
        WriteMultiMash(high, "modfiles.txt", mode == "high-unlisted" ? "" : ProviderItemPath + "\n" + ProviderTrinketPath + "\n");
        return [new("base", "Base", "base", baseline, 0), new("low", "Lower", kind, low, 1001), new("high", "Higher", kind, high, 1000)];
    }

    private static async Task VerifyMissingInventoryProviderAsync(string root, string kind, string mode, DsonSaveCodec codec)
    {
        var sources = BuildProviderSources(root, kind, mode);
        var f = await BuildInventoryProviderProfileAsync(root, sources, true, codec);
        var bag = QuantityItemCatalog.LoadRaid(f.Content, f.Raid);
        var town = QuantityItemCatalog.Load(f.Content, f.Estate);
        var trinkets = TrinketCatalog.Load(f.Content);
        var item = bag.Items.Single(i => i.ItemId == "probe_token");
        var oldSaveHash = ComputeSha256(f.Content.Profile.RaidSavePath);
        if (mode == "high-missing")
        {
            Assert(item.IsSaveOnly && item.HasProviderConflict && item.CurrentAmount == 1 &&
                bag.DefinitionReadFailures.Count > 0 && trinkets.Trinkets.Count == 0,
                "A missing winner must not revive low Mod definitions or turn saved inventory into a writable fallback.");
            Assert(town.Items.Single(i => i.ItemId == "probe_token").HasProviderConflict &&
                town.DefinitionReadFailures.Count > 0, "Town residue must retain the same incomplete-definition protection.");
            await RejectProviderEditAsync(() => f.Service.PrepareQuantityItemEditAsync(f.Content.Profile, item, 10, f.Content));
            var refreshed = QuantityItemCatalog.RefreshSavedAmounts(f.Content, bag, f.Raid, "fresh");
            Assert(refreshed.Items.Single(i => i.ItemId == "probe_token").HasProviderConflict &&
                refreshed.DefinitionReadFailures.Count > 0, "Quantity-only refresh must not clear a known read failure.");
            var added = f.Raid.DeepClone().AsObject();
            added["base_root"]!["party"]!["inventory"]!["items"]!["2"] =
                new JsonObject { ["type"] = "estate", ["id"] = "new_residue", ["amount"] = 1 };
            var newlySeen = QuantityItemCatalog.RefreshSavedAmounts(f.Content, bag, added, "fresh2");
            Assert(newlySeen.Items.Single(i => i.ItemId == "new_residue").HasProviderConflict,
                "New save-only rows discovered by synchronization must also retain incomplete-read protection.");
            Assert(ComputeSha256(f.Content.Profile.RaidSavePath) == oldSaveHash, "Rejected edits must preserve raid bytes.");
        }
        else
        {
            var expectedSource = mode == "high-unlisted" ? "low" : "high";
            var limit = mode == "high-unlisted" ? 2 : 5;
            Assert(!item.HasProviderConflict && !item.IsSaveOnly && item.Source == expectedSource &&
                item.BaseStackLimit == limit && bag.DefinitionReadFailures.Count == 0,
                "Unlisted high files and shadowed missing low files must retain the readable winning provider.");
            var trinket = trinkets.Trinkets.Single();
            Assert(trinket.Source == expectedSource && trinket.QuestUses == limit, "Trinket instance fields must come from the winner.");
            await f.Service.CommitAsync(await f.Service.PrepareQuantityItemEditAsync(f.Content.Profile, item, 10, f.Content));
            await f.Service.CommitAsync(await f.Service.PrepareTrinketEditAsync(f.Content.Profile, trinket, 1, trinkets.Storage, f.Content));
            var bagPath = Path.Combine(root, "bag.committed.json");
            var estatePath = Path.Combine(root, "estate.committed.json");
            await codec.DecodeAsync(f.Content.Profile.RaidSavePath, bagPath);
            await codec.DecodeAsync(f.Content.Profile.EstateSavePath, estatePath);
            var entries = JsonSupport.ReadObject(bagPath)["base_root"]!["party"]!["inventory"]!["items"]!.AsObject()
                .Select(p => p.Value!).Where(i => i["id"]!.GetValue<string>() == "probe_token").ToArray();
            var copy = JsonSupport.ReadObject(estatePath)["base_root"]!["trinkets"]!["items"]!.AsObject().Single().Value!;
            Assert(entries.Sum(i => i["amount"]!.GetValue<int>()) == 10 && entries.All(i => i["amount"]!.GetValue<int>() <= limit) &&
                copy["quest_uses_remaining"]!.GetValue<int>() == limit && copy["triggers_remaining"]!.GetValue<int>() == limit,
                "DSON writes must use the selected item limit and both trinket counters.");
        }
        Assert(ComputeSha256(Path.Combine(f.Content.Profile.ProfileDirectory, "persist.game.json")) == f.Content.SourceGameSha256,
            "Provider verification must not change the Mod configuration.");
        Console.WriteLine($"PASS: {kind}/{mode} provider selection, town/raid residue and DSON write behavior.");
    }

    private static async Task VerifyMissingInventoryGuardsAsync(string root, string kind, DsonSaveCodec codec)
    {
        foreach (var inRaid in new[] { false, true })
        {
            var sceneRoot = Path.Combine(root, inRaid ? "raid" : "town");
            var sources = BuildProviderSources(sceneRoot, kind, "high-valid");
            var f = await BuildInventoryProviderProfileAsync(sceneRoot, sources, inRaid, codec);
            var quantities = inRaid ? QuantityItemCatalog.LoadRaid(f.Content, f.Raid) : QuantityItemCatalog.Load(f.Content, f.Estate);
            var item = quantities.Items.Single(i => i.ItemId == "probe_token");
            var residue = quantities.Items.Single(i => i.ItemId == "residue");
            Assert(residue.IsSaveOnly && !residue.HasProviderConflict, "A complete scan still permits genuine saved-only quantities.");
            var itemEdit = await f.Service.PrepareQuantityItemEditAsync(f.Content.Profile, item, 8, f.Content);
            // Increasing a raid save-only item lacks a known stack limit; reduction remains supported.
            var residueEdit = await f.Service.PrepareQuantityItemEditAsync(f.Content.Profile, residue, 0, f.Content);
            var trinkets = TrinketCatalog.Load(f.Content);
            var trinketEdit = await f.Service.PrepareTrinketEditAsync(f.Content.Profile, trinkets.Trinkets.Single(), 1, trinkets.Storage, f.Content);
            var raidHash = ComputeSha256(f.Content.Profile.RaidSavePath);
            var estateHash = ComputeSha256(f.Content.Profile.EstateSavePath);
            var high = sources.Single(s => s.Id == "high").Directory;
            // These are isolated fixture files under the generated contract run.
            File.Delete(Path.Combine(high, ProviderItemPath));
            File.Delete(Path.Combine(high, ProviderTrinketPath));
            await RejectProviderEditAsync(() => f.Service.CommitAsync(itemEdit));
            await RejectProviderEditAsync(() => f.Service.CommitAsync(residueEdit));
            await RejectProviderEditAsync(() => f.Service.CommitAsync(trinketEdit));
            await RejectProviderEditAsync(() => f.Service.PrepareQuantityItemEditAsync(f.Content.Profile, item, 9, f.Content));
            await RejectProviderEditAsync(() => f.Service.PrepareQuantityItemEditAsync(f.Content.Profile, residue, 0, f.Content));
            Assert(ComputeSha256(f.Content.Profile.RaidSavePath) == raidHash &&
                ComputeSha256(f.Content.Profile.EstateSavePath) == estateHash, "Missing-provider guards must reject before replacing either save.");
            WriteProviderDefinitions(high, 5);
            // Restoration allows a fresh preview; there is no persistent blacklist or compatibility migration.
            await f.Service.CommitAsync(await f.Service.PrepareQuantityItemEditAsync(f.Content.Profile, residue, 0, f.Content));
            var final = Path.Combine(sceneRoot, "residue.committed.json");
            await codec.DecodeAsync(inRaid ? f.Content.Profile.RaidSavePath : f.Content.Profile.EstateSavePath, final);
            var saved = JsonSupport.ReadObject(final);
            Assert((inRaid ? RaidInventorySaveEditor.CountAmount(saved, residue) : QuantityItemSaveEditor.CountAmount(saved, residue)) == 0,
                "A restored complete catalog must allow the genuine residue operation.");
        }
        Console.WriteLine($"PASS: {kind} preflight/commit deletion guards, town/raid saved-only checks and restored-provider recovery.");
    }
}
