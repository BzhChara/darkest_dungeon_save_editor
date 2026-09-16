using System.Text.Json;

internal static partial class ContractSuite
{
    private static string ReferenceHpBuff(string id, double amount) => new JsonObject
    {
        ["buffs"] = new JsonArray(new JsonObject
        {
            ["id"] = id, ["stat_type"] = "combat_stat_multiply", ["stat_sub_type"] = "max_hp",
            ["amount"] = amount, ["rule_type"] = "always", ["is_false_rule"] = false
        })
    }.ToJsonString();

    private static string ReferenceQuirks(string field, params string[] references)
    {
        var other = new JsonObject { ["id"] = "other", ["is_positive"] = true };
        if (field == "evolution_class_id")
        {
            other[field] = references.Single();
            other["evolution_duration_min"] = 4;
            other["evolution_duration_max"] = 4;
        }
        else other[field] = JsonSerializer.SerializeToNode(references);
        return new JsonObject { ["quirks"] = new JsonArray(new JsonObject { ["id"] = "Az", ["is_positive"] = true }, other) }.ToJsonString();
    }

    private static async Task RunHeroReferenceDependencyContractsAsync(string runRoot, DsonSaveCodec codec)
    {
        foreach (var kind in QuirkRuleSourceKinds)
        {
            var root = Path.Combine(runRoot, "hero-native-references", kind);
            var f = QuirkRuleContent(root, kind);
            var buffPath = WriteMultiMash(f.Source, f.Prefix + "shared/buffs/a.buffs.json", ReferenceHpBuff("Az", 0.5));
            if (kind is "local" or "workshop" or "dlc-mod") WriteFixtureManifest(f.Source);
            foreach (var field in new[] { "buffs", "evolution_class_id", "incompatible_quirks" })
            foreach (var (reference, resolves) in new[]
            {
                ("Az", true), ("BE", true), ("Az\0tail", true), ("BE\0tail", true),
                ("az", false), (" Az ", false), ("missingZZ", false)
            })
            {
                // Az and BE independently hash to 3567 with unsigned base-53 UTF-8.
                File.WriteAllText(f.Quirks, ReferenceQuirks(field, reference));
                var catalog = HeroClassCatalog.Load(f.Content);
                if (field == "incompatible_quirks")
                {
                    if (resolves)
                    {
                        AssertQuirkRuleRejected(catalog, ["other", "Az"], "互斥");
                        AssertQuirkRuleRejected(catalog, ["Az", "other"], "互斥");
                    }
                    else GenerateQuirkRuleHero(catalog, "other", "Az");
                }
                else if (!resolves) AssertQuirkRuleRejected(catalog, ["other"], field == "buffs" ? "Buff" : "进化目标");
                else
                {
                    var generated = GenerateQuirkRuleHero(catalog, "other");
                    Assert(generated.Preview.CurrentHp == (field == "buffs" ? 30 : 20), "Native reference alias selects the actual HP definition.");
                    Assert(generated.Candidate["quirks"]!.AsObject().ContainsKey("other"), "Reference aliases cannot rename the saved quirk ID.");
                    if (field == "evolution_class_id")
                        Assert(catalog.InitialQuirks.Single(q => q.Id == "other").Evolution?.TargetQuirkId == "Az" &&
                            generated.Candidate["quirks"]!["other"]!["evolution_duration_remaining"]!.GetValue<int>() == 4,
                            "Resolve an alias to the actual evolution target while preserving its countdown.");
                }
            }
            File.WriteAllText(f.Quirks, ReferenceQuirks("buffs", "BE", "Az\0tail"));
            var repeated = GenerateQuirkRuleHero(HeroClassCatalog.Load(f.Content), "other");
            Assert(repeated.Preview.CurrentHp == 40, "Repeated native Buff references each contribute, even with different spellings.");
            await VerifyProgressionCandidatePersistenceAsync(root, repeated, codec);
            var cycle = JsonNode.Parse(ReferenceQuirks("evolution_class_id", "BE"))!.AsObject();
            cycle["quirks"]![0]!["evolution_class_id"] = "other";
            File.WriteAllText(f.Quirks, cycle.ToJsonString());
            GenerateQuirkRuleHero(HeroClassCatalog.Load(f.Content), "other");

            foreach (var collision in new[] { "BE", "Az\0tail" })
            {
                var definitions = JsonNode.Parse(ReferenceHpBuff("Az", 0.5))!.AsObject();
                definitions["buffs"]!.AsArray().Add(JsonNode.Parse(ReferenceHpBuff(collision, 1))!["buffs"]![0]!.DeepClone());
                File.WriteAllText(buffPath, definitions.ToJsonString());
                File.WriteAllText(f.Quirks, ReferenceQuirks("buffs", "Az"));
                AssertQuirkRuleRejected(HeroClassCatalog.Load(f.Content), ["other"], "Buff");
                File.WriteAllText(buffPath, ReferenceHpBuff("Az", 0.5));
                var quirks = JsonNode.Parse(ReferenceQuirks("evolution_class_id", "Az"))!.AsObject();
                quirks["quirks"]!.AsArray().Add(new JsonObject { ["id"] = collision, ["is_positive"] = true });
                File.WriteAllText(f.Quirks, quirks.ToJsonString());
                var ambiguous = HeroClassCatalog.Load(f.Content);
                AssertQuirkRuleRejected(ambiguous, ["other"], "进化目标");
                AssertQuirkRuleRejected(ambiguous, ["Az"], "未能唯一解析");
            }
            Console.WriteLine($"PASS: {kind} quirk/Buff reference hashes, NUL, case/space controls, cycles, multiplicity, true definition collisions and DSON.");
        }
        foreach (var kind in new[] { "base", "local", "workshop" })
            await VerifyHeroBuffReadGuardsAsync(Path.Combine(runRoot, "hero-buff-read-guards", kind), kind, codec);
        await VerifyUnmappedHeroBuffSourceAsync(Path.Combine(runRoot, "hero-buff-unmapped"), codec);
    }

    private static async Task<SaveEditService> SeedReferenceHeroProfileAsync(string root, SaveProfile profile, DsonSaveCodec codec)
    {
        WriteMultiMash(profile.ProfileDirectory, "persist.estate.json", "{\"base_root\":{}}");
        foreach (var (name, value) in new[]
        {
            ("town", """{"base_root":{"buildings":{"stage_coach":{"store":{"hero_recruit":{"generated":{}}}}}}}"""),
            ("roster", """{"base_root":{"nextGuid":950,"heroes":{}}}"""),
            ("upgrades", """{"base_root":{"purchases":{}}}""")
        })
            await codec.EncodeAsync(WriteMultiMash(root, name + ".seed.json", value), Path.Combine(profile.ProfileDirectory, "persist." + name + ".json"), null);
        return new SaveEditService(codec, new SaveEditorLocations(Path.Combine(root, "data"), Path.Combine(root, "work"), Path.Combine(root, "backups")));
    }

    private static async Task VerifyHeroBuffReadGuardsAsync(string root, string kind, DsonSaveCodec codec)
    {
        var f = QuirkRuleContent(root, kind);
        File.WriteAllText(f.Quirks, ReferenceQuirks("buffs", "Az"));
        var first = WriteMultiMash(f.Source, "shared/buffs/a.buffs.json", ReferenceHpBuff("Az", 0.5));
        var last = WriteMultiMash(f.Source, "shared/buffs/z.buffs.json", "{\"buffs\":[]}");
        if (kind != "base") WriteFixtureManifest(f.Source);
        var service = await SeedReferenceHeroProfileAsync(root, f.Content.Profile, codec);
        var paths = new[] { "town", "roster", "upgrades" }.Select(name => Path.Combine(f.Content.Profile.ProfileDirectory, "persist." + name + ".json")).ToArray();
        var originalHashes = paths.Select(ComputeSha256).ToArray();
        var initial = HeroClassCatalog.Load(f.Content);
        var generated = GenerateQuirkRuleHero(initial, "other");
        Assert(generated.Preview.CurrentHp == 30, "Healthy first definition establishes the initial preview.");
        var prepared = await service.PrepareStagecoachHeroEditAsync(f.Content.Profile, generated, initial, f.Content);
        var before = ProfileCatalogContentFingerprint.Capture(f.Content.Sources);
        File.WriteAllText(last, ReferenceHpBuff("Az", 1));
        using (var held = new FileStream(last, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var partial = HeroClassCatalog.Load(f.Content);
            AssertQuirkRuleRejected(partial, ["other"], "Buff");
            Assert(GenerateQuirkRuleHero(partial, "Az").Preview.CurrentHp == 20, "A quirk without Buff dependencies remains usable.");
            Assert(await CaptureSaveFailureAsync(() => service.PrepareStagecoachHeroEditAsync(f.Content.Profile, generated, initial, f.Content)) is InvalidOperationException,
                "A healthy stale preview cannot reuse an earlier Buff when its later replacement is locked.");
            Assert(await CaptureSaveFailureAsync(() => service.CommitAsync(prepared)) is InvalidOperationException, "Commit rechecks dependency completeness.");
        }
        Assert(paths.Select(ComputeSha256).SequenceEqual(originalHashes), "Read failures leave all three saves unchanged.");
        Assert(before != ProfileCatalogContentFingerprint.Capture(f.Content.Sources), "Recovered changed resource bytes invalidate the sync fingerprint.");
        Assert(GenerateQuirkRuleHero(HeroClassCatalog.Load(f.Content), "other").Preview.CurrentHp == 40, "Releasing the file restores its real whole definition.");
        using (var held = new FileStream(first, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert(GenerateQuirkRuleHero(HeroClassCatalog.Load(f.Content), "other").Preview.CurrentHp == 40,
                "A readable later whole definition re-establishes the ID after an earlier read failure.");
        File.WriteAllText(last, "invalid JSON");
        AssertQuirkRuleRejected(HeroClassCatalog.Load(f.Content), ["other"], "Buff");
        File.WriteAllText(last, ReferenceHpBuff("Az", 1));
        if (kind != "base")
        {
            // Establish both native result slots in order. If the baseline only
            // contains z, the Mod-only a is appended after it and can restore Az.
            WriteMultiMash(Path.Combine(root, "baseline"), "shared/buffs/a.buffs.json", ReferenceHpBuff("Az", 2));
            var hidden = WriteMultiMash(Path.Combine(root, "baseline"), "shared/buffs/z.buffs.json", ReferenceHpBuff("Az", 3));
            File.Delete(last);
            AssertQuirkRuleRejected(HeroClassCatalog.Load(f.Content), ["other"], "Buff");
            File.WriteAllText(last, ReferenceHpBuff("Az", 1));
            File.Delete(hidden);
            Assert(GenerateQuirkRuleHero(HeroClassCatalog.Load(f.Content), "other").Preview.CurrentHp == 40,
                "Neither a missing winning manifest slot nor a missing shadowed file revives the lower definition.");
        }
        var current = HeroClassCatalog.Load(f.Content);
        var fresh = GenerateQuirkRuleHero(current, "other");
        var during = await service.PrepareStagecoachHeroEditAsync(f.Content.Profile, fresh, current, f.Content);
        FileStream? replacementLock = null;
        service.AfterTargetReplace = _ => replacementLock ??= new FileStream(last, FileMode.Open, FileAccess.Read, FileShare.None);
        try
        {
            Assert(await CaptureSaveFailureAsync(() => service.CommitAsync(during)) is InvalidOperationException,
                "A dependency failure after replacement must reject the transaction.");
        }
        finally { service.AfterTargetReplace = null; replacementLock?.Dispose(); }
        Assert(paths.Select(ComputeSha256).SequenceEqual(originalHashes), "Dependency failure during replacement recovers every save.");
        await service.CommitAsync(await service.PrepareStagecoachHeroEditAsync(f.Content.Profile, fresh, current, f.Content));
        var restored = Path.Combine(root, "town.committed.json");
        await codec.DecodeAsync(paths[0], restored);
        Assert(JsonSupport.ReadObject(restored)["base_root"]!["buildings"]!["stage_coach"]!["store"]!["hero_recruit"]!["generated"]!["950"]!["actor"]!["current_hp"]!.GetValue<double>() == 40,
            "Recovered full definition persists the correct HP in a real service commit.");
        Console.WriteLine($"PASS: {kind} ordered Buff read failures, unknown/independent quirks, stale prepare/commit, transaction recovery and correct DSON HP.");
    }

    private static async Task VerifyUnmappedHeroBuffSourceAsync(string root, DsonSaveCodec codec)
    {
        var f = QuirkRuleContent(root, "base");
        var game = Path.Combine(root, "baseline");
        WriteMultiMash(game, "shared/buffs/a.buffs.json", ReferenceHpBuff("Az", 0.5));
        WriteMultiMash(game, "shared/quirk/a.quirk_library.json", ReferenceQuirks("buffs", "Az"));
        WriteMultiMash(f.Content.Profile.ProfileDirectory, "persist.game.json", """
            {"base_root":{"inraid":false,"game_mode":"base","applied_ugcs_1_0":{
                "0":{"name":"Unavailable Buff Provider","source":"mod_local_source"}}}}
            """);
        var service = await SeedReferenceHeroProfileAsync(root, f.Content.Profile, codec);
        var content = await ActiveContentResolver.ResolveAsync(f.Content.Profile, game, null, codec, Path.Combine(root, "resolve"));
        var catalog = HeroClassCatalog.Load(content);
        AssertQuirkRuleRejected(catalog, ["other"], "Buff");
        var hypothetical = content with { Sources = content.Sources };
        var assumed = HeroClassCatalog.Load(hypothetical);
        var generated = GenerateQuirkRuleHero(assumed, "other");
        var prepared = await service.PrepareStagecoachHeroEditAsync(content.Profile, generated, assumed, hypothetical);
        prepared = prepared with { ContentGuard = prepared.ContentGuard with { Resolution = content.Resolution } };
        var paths = new[] { "town", "roster", "upgrades" }.Select(name => Path.Combine(content.Profile.ProfileDirectory, "persist." + name + ".json")).ToArray();
        var before = paths.Select(ComputeSha256).ToArray();
        Assert(await CaptureSaveFailureAsync(() => service.PrepareStagecoachHeroEditAsync(content.Profile, generated, assumed, content)) is InvalidOperationException &&
            await CaptureSaveFailureAsync(() => service.CommitAsync(prepared)) is InvalidOperationException,
            "Unknown enabled Buff providers must remain unknown through preflight and reconstructed commit context.");
        Assert(paths.Select(ComputeSha256).SequenceEqual(before), "Incomplete-source rejection must not touch saves.");
        var independent = GenerateQuirkRuleHero(catalog, "Az");
        await service.CommitAsync(await service.PrepareStagecoachHeroEditAsync(content.Profile, independent, catalog, content));
        Console.WriteLine("PASS: unresolved enabled Buff provider, preview/commit resolution context and independent quirk commit.");
    }
}
