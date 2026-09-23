internal static partial class ContractSuite
{
    private static async Task RunHeroSupportingReadContractsAsync(string runRoot, DsonSaveCodec codec)
    {
        foreach (var kind in QuirkRuleSourceKinds)
            await VerifyHeroClueReadOrderAsync(Path.Combine(runRoot, "hero-clue-reads", kind), kind);
        foreach (var kind in new[] { "base", "local", "workshop" })
            await VerifyCampingReadGuardsAsync(Path.Combine(runRoot, "camping-read-guards", kind), kind, codec);
        VerifyCampingShadowedFailure(Path.Combine(runRoot, "camping-shadowed-failure"));
    }

    private static string SupportingCamping(params string[] ids) => new JsonObject
    {
        ["configuration"] = new JsonObject { ["class_specific_number_of_classes_threshold"] = 1 },
        ["skills"] = new JsonArray(ids.Select(id => (JsonNode)new JsonObject
        {
            ["id"] = id, ["hero_classes"] = new JsonArray("progression")
        }).ToArray())
    }.ToJsonString();

    private static string SupportingEvents(params (string Id, int Count)[] entries) => new JsonObject
    {
        ["events"] = new JsonArray(entries.Select(entry => (JsonNode)new JsonObject
        {
            ["id"] = entry.Id,
            ["data"] = entry.Count == 0 ? new JsonArray() : new JsonArray(new JsonObject
            {
                ["type"] = "bonus_recruit", ["string_data"] = "progression", ["number_data"] = entry.Count
            })
        }).ToArray())
    }.ToJsonString();

    private static async Task VerifyHeroClueReadOrderAsync(string root, string kind)
    {
        var f = QuirkRuleContent(root, kind);
        File.WriteAllText(f.Quirks, """
            {"quirks":[{"id":"p0","is_positive":true,"random_chance":0},
                       {"id":"p1","is_positive":true,"random_chance":0}]}
            """);
        File.AppendAllText(Path.Combine(root, "baseline/heroes/progression/progression.info.darkest"),
            "combat_skill: .id attack .level 0 .effect probe_effect independent_effect\n");
        var a = WriteMultiMash(f.Source, f.Prefix + "effects/a.effects.darkest", "effect: .name probe_effect .disease p0\n");
        var m = WriteMultiMash(f.Source, f.Prefix + "effects/m.effects.darkest", "effect: .name probe_effect .disease \"\"\n");
        var z = WriteMultiMash(f.Source, f.Prefix + "effects/z.effects.darkest", "effect: .name probe_effect .duration 3\n");
        var ea = WriteMultiMash(f.Source, f.Prefix + "campaign/town_events/a.town_events.events.json",
            SupportingEvents(("same", 0), ("known", 1)));
        var em = WriteMultiMash(f.Source, f.Prefix + "campaign/town_events/m.town_events.events.json", SupportingEvents());
        var ez = WriteMultiMash(f.Source, f.Prefix + "campaign/town_events/z.town_events.events.json",
            SupportingEvents(("same", 2), ("known", 3), ("new", 4)));
        var mod = kind is "local" or "workshop" or "dlc-mod";
        if (mod) WriteFixtureManifest(f.Source);
        HeroClassDefinition Hero() => HeroClassCatalog.Load(f.Content).HeroClasses.Single(h => h.Id == "progression");
        Assert(Hero().RuntimeQuirkSignals.Count == 0, "Explicit disease clearing survives a later omitted field.");
        var healthyEvents = Hero().RecruitEvents;
        Assert(healthyEvents.Count == 2 && healthyEvents.Single(e => e.Id == "known").Count == 1 &&
            healthyEvents.Single(e => e.Id == "new").Count == 4, "Healthy first events include empty first results.");
        foreach (var failure in mod ? new[] { "locked", "missing" } : new[] { "locked" })
        {
            await WithFailedDefinitionAsync(m, failure, () =>
            {
                Assert(Hero().RuntimeQuirkSignals.Count == 0, "An omitted field after a failed slot cannot resurrect the old disease.");
                File.WriteAllText(z, "effect: .name probe_effect .duration 3\neffect: .name independent_effect .disease p1\n");
                var independent = Hero().RuntimeQuirkSignals;
                Assert(independent.Count == 1 && independent[0].EffectName == "independent_effect" && independent[0].QuirkId == "p1",
                    "A later explicit assignment restores only its own Effect ID.");
                File.WriteAllText(z, "effect: .name probe_effect .disease p1\n");
                Assert(Hero().RuntimeQuirkSignals.Single().QuirkId == "p1", "Explicit later disease values recover their own field.");
                File.WriteAllText(z, "effect: .name probe_effect .disease \"\"\n");
                Assert(Hero().RuntimeQuirkSignals.Count == 0, "Explicit later empty values recover a cleared field.");
                File.WriteAllText(z, "effect: .name probe_effect .duration 3\n");
                return Task.CompletedTask;
            });
        }
        File.WriteAllText(m, "");
        Assert(Hero().RuntimeQuirkSignals.Single().QuirkId == "p0", "Healthy omissions retain the earlier assignment.");
        File.WriteAllText(m, "effect: .name probe_effect .disease \"\"\n");
        await WithFailedDefinitionAsync(a, "locked", () =>
        {
            Assert(Hero().RuntimeQuirkSignals.Count == 0, "Readable clearing after an earlier failure remains authoritative.");
            return Task.CompletedTask;
        });
        foreach (var failure in mod ? new[] { "locked", "malformed", "missing" } : new[] { "locked", "malformed" })
        {
            await WithFailedDefinitionAsync(em, failure, () =>
            {
                var events = Hero().RecruitEvents;
                Assert(events.Count == 1 && events[0].Id == "known" && events[0].Count == 1,
                    "Earlier first event results survive, empty first results stay empty, and unseen later IDs remain unknown.");
                return Task.CompletedTask;
            });
            await WithFailedDefinitionAsync(ea, failure, () =>
            {
                Assert(Hero().RecruitEvents.Count == 0, "No later event may pose as an unknown first result.");
                // These clues do not initialize HP or write event state.
                Assert(GenerateProgressionHero(HeroClassCatalog.Load(f.Content)).Preview.CurrentHp == 40,
                    "Uncertain recruitment metadata alone does not disable otherwise valid generation.");
                return Task.CompletedTask;
            });
        }
        await WithFailedDefinitionAsync(ez, "locked", () =>
        {
            var events = Hero().RecruitEvents;
            Assert(events.Count == 1 && events[0].Id == "known" && events[0].Count == 1,
                "A failed later event file does not erase a proved first result.");
            return Task.CompletedTask;
        });
        Assert(Hero().RuntimeQuirkSignals.Count == 0 && Hero().RecruitEvents.SequenceEqual(healthyEvents),
            "Restored reads recover the exact healthy metadata.");
        Console.WriteLine($"PASS: {kind}/Effect field certainty, event first-result certainty, recovery and independent hero generation.");
    }

    private static async Task VerifyCampingReadGuardsAsync(string root, string kind, DsonSaveCodec codec)
    {
        var f = QuirkRuleContent(root, kind);
        WriteMultiMash(f.Source, "raid/camping/a.camping_skills.json", SupportingCamping("rest"));
        var last = WriteMultiMash(f.Source, "raid/camping/z.camping_skills.json", SupportingCamping());
        if (kind != "base") WriteFixtureManifest(f.Source);
        var service = await SeedReferenceHeroProfileAsync(root, f.Content.Profile, codec);
        var paths = new[] { "town", "roster", "upgrades" }.Select(n => Path.Combine(f.Content.Profile.ProfileDirectory, "persist." + n + ".json")).ToArray();
        var original = paths.Select(ComputeSha256).ToArray();
        var initial = HeroClassCatalog.Load(f.Content);
        var generated = GenerateProgressionHero(initial);
        Assert(generated.Candidate["skills"]!["selected_camping_skills"]!.AsObject().Count == 0 &&
            generated.UpgradePurchases.Any(p => p.TreeId == "progression.rest"), "Zero equipped camping skills still unlock the complete available pool.");
        var prepared = await service.PrepareStagecoachHeroEditAsync(f.Content.Profile, generated, initial, f.Content);
        File.WriteAllText(last, SupportingCamping("extra"));
        foreach (var failure in kind == "base" ? new[] { "locked", "malformed" } : new[] { "locked", "malformed", "missing" })
        {
            await WithFailedDefinitionAsync(last, failure, async () =>
            {
                var partial = HeroClassCatalog.Load(f.Content);
                var hero = partial.HeroClasses.Single(h => h.Id == "progression");
                Assert(hero.ClassCampingSkillIds.SequenceEqual(new[] { "rest" }) && !hero.CampingSkillsComplete,
                    "Unchanged visible skill IDs cannot prove that the entire unlock pool is complete.");
                var failureToGenerate = await CaptureSaveFailureAsync(() => { GenerateProgressionHero(partial); return Task.CompletedTask; });
                Assert(failureToGenerate is InvalidOperationException && failureToGenerate.Message.Contains("露营技能定义读取不完整", StringComparison.Ordinal),
                    "Even zero requested camping skills must reject an incomplete unlock plan.");
                Assert(hero.GenerationAvailability.Count > 0 && hero.GenerationAvailability.All(g => !g.CanGenerate),
                    "UI availability follows the same camping guard.");
                Assert(await CaptureSaveFailureAsync(() => service.PrepareStagecoachHeroEditAsync(f.Content.Profile, generated, initial, f.Content)) is InvalidOperationException &&
                    await CaptureSaveFailureAsync(() => service.CommitAsync(prepared)) is InvalidOperationException,
                    "A healthy old preview is rejected at both preflight and commit when only completeness changes.");
                Assert(paths.Select(ComputeSha256).SequenceEqual(original), "Rejected camping edits preserve every save byte.");
            });
        }
        var restored = HeroClassCatalog.Load(f.Content);
        var current = GenerateProgressionHero(restored);
        Assert(restored.HeroClasses.Single().CampingSkillsComplete && current.UpgradePurchases.Any(p => p.TreeId == "progression.extra"),
            "Recovery unlocks the previously unreadable new skill.");
        var transaction = await service.PrepareStagecoachHeroEditAsync(f.Content.Profile, current, restored, f.Content);
        FileStream? heldDuringReplace = null;
        service.AfterTargetReplace = _ => heldDuringReplace ??= new FileStream(last, FileMode.Open, FileAccess.Read, FileShare.None);
        try
        {
            Assert(await CaptureSaveFailureAsync(() => service.CommitAsync(transaction)) is InvalidOperationException,
                "A camping file failing during replacement rejects the transaction.");
        }
        finally { service.AfterTargetReplace = null; heldDuringReplace?.Dispose(); }
        Assert(paths.Select(ComputeSha256).SequenceEqual(original), "Mid-write camping failures restore town, roster and upgrades.");
        await service.CommitAsync(await service.PrepareStagecoachHeroEditAsync(f.Content.Profile, current, restored, f.Content));
        var decoded = Path.Combine(root, "upgrades.committed.json");
        await codec.DecodeAsync(paths[2], decoded);
        var purchases = JsonSupport.ReadObject(decoded)["base_root"]!["purchases"]!.AsObject();
        foreach (var id in new[] { "rest", "extra" })
            Assert(purchases.Any(p => p.Value!["tree_id"]!.GetValue<int>() == unchecked((int)HashLoc2Key("progression." + id)) &&
                p.Value!["requirement_code"]!.GetValue<string>() == "0"), "Both camping unlocks persist with the native code in DSON.");
        Console.WriteLine($"PASS: {kind}/camping complete pool, zero selection, preflight, commit, mid-write rollback and recovered DSON.");
    }

    private static void VerifyCampingShadowedFailure(string root)
    {
        var f = QuirkRuleContent(root, "local");
        var low = WriteMultiMash(Path.Combine(root, "baseline"), "raid/camping/a.camping_skills.json", SupportingCamping("wrong"));
        WriteMultiMash(f.Source, "raid/camping/a.camping_skills.json", SupportingCamping("rest"));
        WriteFixtureManifest(f.Source);
        using var held = new FileStream(low, FileMode.Open, FileAccess.Read, FileShare.None);
        var catalog = HeroClassCatalog.Load(f.Content);
        var hero = catalog.HeroClasses.Single();
        Assert(hero.CampingSkillsComplete && hero.ClassCampingSkillIds.SequenceEqual(new[] { "rest" }) &&
            GenerateProgressionHero(catalog).UpgradePurchases.Any(p => p.TreeId == "progression.rest"),
            "A failed fully replaced camping provider does not poison the effective readable file.");
        Console.WriteLine("PASS: shadowed camping failures preserve the readable winning pool and generation.");
    }
}
