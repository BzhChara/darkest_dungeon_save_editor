internal static partial class ContractSuite
{
    private static async Task RunDefinitionReadCompletenessContractsAsync(string runRoot, DsonSaveCodec codec)
    {
        foreach (var kind in new[] { "base", "local", "workshop" })
        {
            foreach (var upgrades in new[] { false, true })
                await VerifyHeroDefinitionReadCompletenessAsync(Path.Combine(runRoot, "definition-reads", kind,
                    upgrades ? "upgrades" : "quirks"), kind, upgrades, codec);
            foreach (var item in new[] { false, true })
                await VerifyInventoryDefinitionReadCompletenessAsync(Path.Combine(runRoot, "definition-reads", kind,
                    item ? "items" : "trinkets"), kind, item, codec);
        }
    }

    private static string ReadGuardQuirks(bool updated) => new JsonObject
    {
        ["quirks"] = new JsonArray(new JsonObject { ["id"] = "Az", ["is_positive"] = true },
            new JsonObject
            {
                ["id"] = "other", ["is_positive"] = true,
                ["buffs"] = new JsonArray(updated ? "hp100" : "hp50"),
                ["incompatible_quirks"] = updated ? new JsonArray("Az") : new JsonArray(),
                ["evolution_duration_min"] = updated ? 12 : 0,
                ["evolution_duration_max"] = updated ? 12 : 0,
                ["evolution_class_id"] = updated ? "Az" : ""
            })
    }.ToJsonString();

    private static string ReadGuardUpgradeTrees(int armourLevel) => """
        {"trees":[
        {"id":"progression.weapon","requirements":[{"code":"0","prerequisite_resolve_level":1}]},
        {"id":"progression.armour","requirements":[{"code":"0","prerequisite_resolve_level":ARMOUR_LEVEL}]},
        {"id":"progression.attack","requirements":[{"code":"0","prerequisite_resolve_level":0},{"code":"1","prerequisite_resolve_level":1}]}]}
        """.Replace("ARMOUR_LEVEL", armourLevel.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static async Task WithFailedDefinitionAsync(string path, string failure, Func<Task> check)
    {
        var bytes = File.ReadAllBytes(path);
        if (failure == "locked")
        {
            using var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            await check();
            return;
        }
        // Only files in this run's synthetic fixture are removed or corrupted.
        try
        {
            if (failure == "missing") File.Delete(path);
            else File.WriteAllText(path, "{ incomplete JSON");
            await check();
        }
        finally { File.WriteAllBytes(path, bytes); }
    }

    private static async Task VerifyHeroDefinitionReadCompletenessAsync(string root, string kind, bool upgrades, DsonSaveCodec codec)
    {
        var f = QuirkRuleContent(root, kind);
        var first = upgrades ? Path.Combine(root, "baseline/upgrades/heroes/a.upgrades.json") : f.Quirks;
        var initialText = upgrades ? ReadGuardUpgradeTrees(1) : ReadGuardQuirks(false);
        var updatedText = upgrades ? ReadGuardUpgradeTrees(4) : ReadGuardQuirks(true);
        File.WriteAllText(first, initialText);
        var last = WriteMultiMash(f.Source, upgrades ? "upgrades/z.upgrades.json" : "shared/quirk/z.quirk_library.json",
            upgrades ? "{\"trees\":[]}" : "{\"quirks\":[]}");
        WriteMultiMash(f.Source, "shared/buffs/a.buffs.json", ReferenceHpBuff("hp50", .5));
        WriteMultiMash(f.Source, "shared/buffs/z.buffs.json", ReferenceHpBuff("hp100", 1));
        if (kind != "base") WriteFixtureManifest(f.Source);
        var service = await SeedReferenceHeroProfileAsync(root, f.Content.Profile, codec);
        var paths = new[] { "town", "roster", "upgrades" }.Select(n => Path.Combine(f.Content.Profile.ProfileDirectory, "persist." + n + ".json")).ToArray();
        var original = paths.Select(ComputeSha256).ToArray();
        var initial = HeroClassCatalog.Load(f.Content);
        var generated = upgrades ? GenerateProgressionHero(initial) : GenerateQuirkRuleHero(initial, "other", "Az");
        Assert(generated.Preview.CurrentHp == (upgrades ? 40 : 30), "Initial complete resources establish a healthy preview.");
        var prepared = await service.PrepareStagecoachHeroEditAsync(f.Content.Profile, generated, initial, f.Content);
        File.WriteAllText(last, updatedText);
        foreach (var failure in kind == "base" ? new[] { "locked", "malformed" } : new[] { "locked", "malformed", "missing" })
        {
            await WithFailedDefinitionAsync(last, failure, async () =>
            {
                var partial = HeroClassCatalog.Load(f.Content);
                Assert(await CaptureSaveFailureAsync(() =>
                {
                    _ = upgrades ? GenerateProgressionHero(partial) : GenerateQuirkRuleHero(partial, "other");
                    return Task.CompletedTask;
                }) is InvalidOperationException, "Uncertain last definitions cannot generate a writable hero.");
                Assert(await CaptureSaveFailureAsync(() => service.PrepareStagecoachHeroEditAsync(f.Content.Profile, generated, initial, f.Content)) is InvalidOperationException &&
                    await CaptureSaveFailureAsync(() => service.CommitAsync(prepared)) is InvalidOperationException,
                    "Healthy stale hero previews must reject incomplete definitions at preflight and commit.");
                Assert(paths.Select(ComputeSha256).SequenceEqual(original), "Rejected hero writes preserve all three saves.");
            });
        }
        using (var held = new FileStream(first, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var finalDefinitions = HeroClassCatalog.Load(f.Content);
            var candidate = upgrades ? GenerateProgressionHero(finalDefinitions) : GenerateQuirkRuleHero(finalDefinitions, "other");
            Assert(candidate.Preview.CurrentHp == (upgrades ? 20 : 40), "A complete later definition can recover its own ID after an earlier failed slot.");
        }
        if (upgrades)
        {
            // A missing key in the readable subset is not proof of a tree-less skill.
            File.WriteAllText(first, "{\"trees\":[]}");
            using (var held = new FileStream(last, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var unavailable = HeroClassCatalog.Load(f.Content).HeroClasses.Single(h => h.Id == "progression");
                Assert(unavailable.UpgradeTrees.Any(t => t.Id == "progression.attack" && t.UnsupportedReason.Length > 0),
                    "Unknown tree presence must occupy computed skill purchase targets.");
            }
            File.WriteAllText(first, initialText);
        }
        var restored = HeroClassCatalog.Load(f.Content);
        if (!upgrades) AssertQuirkRuleRejected(restored, ["other", "Az"], "互斥");
        var current = upgrades ? GenerateProgressionHero(restored) : GenerateQuirkRuleHero(restored, "other");
        var transaction = await service.PrepareStagecoachHeroEditAsync(f.Content.Profile, current, restored, f.Content);
        FileStream? heldDuringReplace = null;
        service.AfterTargetReplace = _ => heldDuringReplace ??= new FileStream(last, FileMode.Open, FileAccess.Read, FileShare.None);
        try
        {
            Assert(await CaptureSaveFailureAsync(() => service.CommitAsync(transaction)) is InvalidOperationException,
                "Definition read failure during replacement rejects and restores the hero transaction.");
        }
        finally { service.AfterTargetReplace = null; heldDuringReplace?.Dispose(); }
        Assert(paths.Select(ComputeSha256).SequenceEqual(original), "All hero targets must roll back after a mid-write definition failure.");
        await service.CommitAsync(await service.PrepareStagecoachHeroEditAsync(f.Content.Profile, current, restored, f.Content));
        var decoded = Path.Combine(root, "town.committed.json");
        await codec.DecodeAsync(paths[0], decoded);
        var saved = JsonSupport.ReadObject(decoded)["base_root"]!["buildings"]!["stage_coach"]!["store"]!["hero_recruit"]!["generated"]!["950"]!;
        Assert(saved["actor"]!["current_hp"]!.GetValue<double>() == (upgrades ? 20 : 40), "Recovered final HP persists in DSON.");
        if (upgrades) Assert(saved["armour_rank"]!.GetValue<int>() == 0, "Updated resolve requirement preserves the unupgraded rank.");
        else Assert(saved["quirks"]!["other"]!["evolution_duration_remaining"]!.GetValue<int>() == 12,
            "Recovered evolution countdown is written from the final definition.");
        Console.WriteLine($"PASS: {kind}/{(upgrades ? "upgrade" : "quirk")} ordered failures, recovery, stale preview rejection, mid-write rollback and DSON.");
    }

    private static async Task VerifyInventoryDefinitionReadCompletenessAsync(string root, string kind, bool item, DsonSaveCodec codec)
    {
        var source = Path.Combine(root, "source");
        string Definition(int value) => item
            ? $"inventory_item: .type estate .id probe_token .base_stack_limit {value} .estate_can_be_provision true\n"
            : new JsonObject { ["entries"] = new JsonArray(new JsonObject { ["id"] = "read_trinket", ["quest_uses"] = value }) }.ToJsonString();
        var first = WriteMultiMash(source, item ? "inventory/a.inventory.items.darkest" : "trinkets/a.entries.trinkets.json",
            item ? "" : "{\"entries\":[]}");
        var last = WriteMultiMash(source, item ? "inventory/z.inventory.items.darkest" : "trinkets/z.entries.trinkets.json", Definition(item ? 8 : 7));
        WriteMultiMash(source, "inventory/main.inventory.system_configs.darkest",
            "inventory_system_config: .type raid .max_slots 32\ninventory_system_config: .type trinket_storage .max_slots 100\n");
        if (kind != "base") WriteFixtureManifest(source);
        var f = await BuildInventoryProviderProfileAsync(root, [new(kind, kind, kind, source, 0)], item, codec);
        QuantityItemCatalogResult Items() => QuantityItemCatalog.LoadRaid(f.Content, f.Raid);
        TrinketCatalogResult Trinkets() => TrinketCatalog.Load(f.Content);
        async Task<Func<Task>> PrepareCurrent()
        {
            if (item)
            {
                var p = await f.Service.PrepareQuantityItemEditAsync(f.Content.Profile, Items().Items.Single(i => i.ItemId == "probe_token"), 10, f.Content);
                return async () => { await f.Service.CommitAsync(p); };
            }
            var c = Trinkets();
            var t = await f.Service.PrepareTrinketEditAsync(f.Content.Profile, c.Trinkets.Single(), 1, c.Storage, f.Content);
            return async () => { await f.Service.CommitAsync(t); };
        }
        var oldCommit = await PrepareCurrent();
        var paths = new[] { f.Content.Profile.EstateSavePath, f.Content.Profile.RaidSavePath };
        var original = paths.Select(ComputeSha256).ToArray();
        File.WriteAllText(first, Definition(2));
        var failures = new List<string> { "locked" };
        if (!item) failures.Add("malformed");
        if (kind != "base") failures.Add("missing");
        foreach (var failure in failures)
        {
            await WithFailedDefinitionAsync(first, failure, async () =>
            {
                Assert(item ? Items().Items.Single(i => i.ItemId == "probe_token").HasProviderConflict : Trinkets().Trinkets.Single().HasProviderConflict,
                    "An unknown earlier slot prevents adopting a later first-match definition.");
                if (item)
                {
                    var partial = Items();
                    var refreshed = QuantityItemCatalog.RefreshSavedAmounts(f.Content, partial, f.Raid, "changed-save");
                    Assert(refreshed.Items.Single(i => i.ItemId == "probe_token").HasProviderConflict &&
                        refreshed.Items.Single(i => i.ItemId == "residue").HasProviderConflict,
                        "Incremental refresh preserves uncertainty for defined and saved-only items.");
                }
                Assert(await CaptureSaveFailureAsync(async () => { _ = await PrepareCurrent(); }) is InvalidOperationException &&
                    await CaptureSaveFailureAsync(oldCommit) is InvalidOperationException,
                    "Unknown first definitions must reject both new and healthy old previews.");
                Assert(paths.Select(ComputeSha256).SequenceEqual(original), "Rejected inventory edits leave all targets untouched.");
            });
        }
        using (var held = new FileStream(last, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            if (item)
            {
                var known = Items().Items.Single(i => i.ItemId == "probe_token");
                Assert(!known.HasProviderConflict && known.BaseStackLimit == 2, "Known first item survives an unreadable later definition.");
                var town = QuantityItemCatalog.Load(f.Content, f.Estate).Items.Single(i => i.ItemId == "probe_token");
                Assert(!town.HasProviderConflict, "Town items preserve the same first-match certainty.");
            }
            else Assert(!Trinkets().Trinkets.Single().HasProviderConflict && Trinkets().Trinkets.Single().QuestUses == 2,
                "Known first trinket survives an unreadable later definition.");
            _ = await PrepareCurrent();
        }
        var transaction = await PrepareCurrent();
        // The selected first file is already held read-shared by the transaction.
        // Use a separate earlier empty file so a new unknown slot can appear mid-write.
        var prefix = WriteMultiMash(source, item ? "inventory/0.inventory.items.darkest" : "trinkets/0.entries.trinkets.json", item ? "" : "{\"entries\":[]}");
        if (kind != "base") WriteFixtureManifest(source);
        transaction = await PrepareCurrent();
        FileStream? heldDuringReplace = null;
        f.Service.AfterTargetReplace = _ => heldDuringReplace ??= new FileStream(prefix, FileMode.Open, FileAccess.Read, FileShare.None);
        try
        {
            Assert(await CaptureSaveFailureAsync(transaction) is InvalidOperationException,
                "An earlier definition slot becoming unreadable during replacement must roll back.");
        }
        finally { f.Service.AfterTargetReplace = null; heldDuringReplace?.Dispose(); }
        Assert(paths.Select(ComputeSha256).SequenceEqual(original), "Inventory rollback preserves both saves.");
        await (await PrepareCurrent())();
        var decoded = Path.Combine(root, "inventory.committed.json");
        await codec.DecodeAsync(item ? f.Content.Profile.RaidSavePath : f.Content.Profile.EstateSavePath, decoded);
        var saved = JsonSupport.ReadObject(decoded);
        if (item)
        {
            var stacks = saved["base_root"]!["party"]!["inventory"]!["items"]!.AsObject().Select(p => p.Value!)
                .Where(i => i["id"]!.GetValue<string>() == "probe_token").Select(i => i["amount"]!.GetValue<int>()).ToArray();
            Assert(stacks.SequenceEqual(new[] { 2, 2, 2, 2, 2 }), "Recovered native item limit produces five stacks of two.");
        }
        else Assert(saved["base_root"]!["trinkets"]!["items"]!["0"]!["quest_uses_remaining"]!.GetValue<int>() == 2,
            "Recovered first trinket persists the correct uses.");
        Console.WriteLine($"PASS: {kind}/{(item ? "item" : "trinket")} first-match failures, recovery, refresh, stale preview rejection, rollback and DSON.");
    }
}
