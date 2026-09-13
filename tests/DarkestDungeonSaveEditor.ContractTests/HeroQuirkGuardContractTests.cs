using System.Reflection;

internal static partial class ContractSuite
{
    private static async Task VerifyHeroQuirkRuleGuardsAsync(string runRoot, DsonSaveCodec codec)
    {
        var root = Path.Combine(runRoot, "quirk-rule-save-guards");
        var f = QuirkRuleContent(root, "local");
        var rulePath = WriteMultiMash(f.Source, "shared/rules.json", """{"quirks_max_positive":2}""");
        WriteFixtureManifest(f.Source);
        var profile = f.Content.Profile;
        WriteMultiMash(profile.ProfileDirectory, "persist.estate.json", "{\"base_root\":{}}");
        var sourceText = new Dictionary<string,string>
        {
            ["town"] = """{"base_root":{"buildings":{"stage_coach":{"store":{"hero_recruit":{"generated":{}}}}}}}""",
            ["roster"] = """{"base_root":{"nextGuid":950,"heroes":{}}}""",
            ["upgrades"] = """{"base_root":{"purchases":{}}}"""
        };
        foreach (var (name, value) in sourceText)
        {
            var decoded = WriteMultiMash(root, name + ".seed.json", value);
            await codec.EncodeAsync(decoded, Path.Combine(profile.ProfileDirectory, "persist." + name + ".json"), null);
        }
        var service = new SaveEditService(codec, new SaveEditorLocations(Path.Combine(root,"data"), Path.Combine(root,"work"), Path.Combine(root,"backups")));
        var catalog = HeroClassCatalog.Load(f.Content);
        var generated = GenerateQuirkRuleHero(catalog, "p0", "p1");
        var prepared = await service.PrepareStagecoachHeroEditAsync(profile, generated, catalog, f.Content);
        var originals = sourceText.Keys.ToDictionary(name => name, name => ComputeSha256(Path.Combine(profile.ProfileDirectory, "persist." + name + ".json")));
        var originalManifest = File.ReadAllBytes(Path.Combine(f.Source, "modfiles.txt"));
        var signal = ProfileCatalogContentFingerprint.Capture(f.Content.Sources);
        var ruleTime = File.GetLastWriteTimeUtc(rulePath);
        File.WriteAllText(rulePath, """{"quirks_max_positive":1}""");
        File.SetLastWriteTimeUtc(rulePath, ruleTime);
        var refreshed = HeroClassCatalog.Load(f.Content);
        Assert(refreshed.InitialQuirkLimits.Positive == 1 && signal != ProfileCatalogContentFingerprint.Capture(f.Content.Sources) &&
               originalManifest.SequenceEqual(File.ReadAllBytes(Path.Combine(f.Source, "modfiles.txt"))),
            "Rule-only changes refresh with identical size/timestamp/manifest.");
        var staleCommit = await CaptureSaveFailureAsync(async () => { await service.CommitAsync(prepared); });
        Assert(staleCommit is InvalidOperationException && staleCommit.Message.Contains("templates changed", StringComparison.Ordinal),
            $"Changed quirk limits reject the actual prepared commit; got {staleCommit}");
        var staleCatalog = await CaptureSaveFailureAsync(async () => { await service.PrepareStagecoachHeroEditAsync(profile, generated, catalog, f.Content); });
        Assert(staleCatalog is InvalidOperationException && staleCatalog.Message.Contains("templates changed", StringComparison.Ordinal),
            "A stale catalog also fails before preparing new save files.");
        var reboundCandidate = await CaptureSaveFailureAsync(async () => { await service.PrepareStagecoachHeroEditAsync(profile, generated, refreshed, f.Content); });
        Assert(reboundCandidate is InvalidOperationException && reboundCandidate.Message.Contains("最多", StringComparison.Ordinal),
            "Pairing the old candidate with the fresh catalog cannot bypass current quirk limits.");
        Assert(originals.All(p => ComputeSha256(Path.Combine(profile.ProfileDirectory, "persist." + p.Key + ".json")) == p.Value),
            "Every rejected prepare/commit leaves all target saves byte-for-byte unchanged.");

        const string fixedDeath = "\"evolution_duration_min\":12,\"evolution_duration_max\":12,\"evolution_causes_death\":true";
        foreach (var (previous, current) in new[]
        {
            ("", fixedDeath),
            (fixedDeath, "\"evolution_duration_min\":1,\"evolution_duration_max\":11,\"evolution_causes_death\":true"),
            (fixedDeath, "")
        })
        {
            File.WriteAllText(f.Quirks, QuirkEvolutionLibrary(previous));
            var oldCatalog = HeroClassCatalog.Load(f.Content);
            var oldCandidate = GenerateQuirkRuleHero(oldCatalog, "p0");
            File.WriteAllText(f.Quirks, QuirkEvolutionLibrary(current));
            var currentCatalog = HeroClassCatalog.Load(f.Content);
            StagecoachHeroCandidateFactory.ValidateInitialQuirkSelection(currentCatalog, currentCatalog.HeroClasses.Single(), 0, ["p0"]);
            var staleEvolution = await CaptureSaveFailureAsync(async () =>
            {
                await service.PrepareStagecoachHeroEditAsync(profile, oldCandidate, currentCatalog, f.Content);
            });
            Assert(staleEvolution is InvalidOperationException && staleEvolution.Message.Contains("进化", StringComparison.Ordinal),
                $"Rebinding a candidate cannot retain a countdown outside the active evolution range; got {staleEvolution}");
        }
        File.WriteAllText(f.Quirks, QuirkEvolutionLibrary(fixedDeath));
        var compatibleOld = GenerateQuirkRuleHero(HeroClassCatalog.Load(f.Content), "p0");
        File.WriteAllText(f.Quirks, QuirkEvolutionLibrary("\"evolution_duration_min\":10,\"evolution_duration_max\":14,\"evolution_causes_death\":true"));
        await service.PrepareStagecoachHeroEditAsync(profile, compatibleOld, HeroClassCatalog.Load(f.Content), f.Content);
        Assert(originals.All(p => ComputeSha256(Path.Combine(profile.ProfileDirectory, "persist." + p.Key + ".json")) == p.Value),
            "Evolution preflight rejects incompatible state and accepts a compatible range without mutating target saves.");

        // Meaning-preserving evolution edits refresh discovery without invalidating
        // an otherwise unchanged candidate. This also exercises a successful commit.
        File.WriteAllText(f.Quirks, QuirkEvolutionLibrary(""));
        var beforeNote = HeroClassCatalog.Load(f.Content);
        var neutral = GenerateQuirkRuleHero(beforeNote, "p0");
        var neutralPrepared = await service.PrepareStagecoachHeroEditAsync(profile, neutral, beforeNote, f.Content);
        var fingerprintMethod = typeof(SaveEditService).GetMethod("ComputeHeroCatalogSha256", BindingFlags.NonPublic | BindingFlags.Static)!;
        string Fingerprint(HeroClassCatalogResult value) => (string)fingerprintMethod.Invoke(null, [value])!;
        var beforeHash = Fingerprint(beforeNote);
        File.WriteAllText(f.Quirks, QuirkEvolutionLibrary("\"evolution_note\":\"ignored\",\"evolution_causes_death\":false,\"evolution_class_id\":\"\""));
        Assert(Fingerprint(HeroClassCatalog.Load(f.Content)) == beforeHash,
            "Default/ignored evolution fields must not make a semantically stale preview.");
        await service.CommitAsync(neutralPrepared);
        var committed = Path.Combine(root, "town.committed.json");
        await codec.DecodeAsync(Path.Combine(profile.ProfileDirectory, "persist.town.json"), committed);
        var hero = JsonNode.Parse(File.ReadAllText(committed))!["base_root"]!["buildings"]!["stage_coach"]!["store"]!["hero_recruit"]!["generated"]!["950"]!;
        Assert(hero["quirks"]!.AsObject().Count == 1 && hero["quirks"]!["p0"]!["evolution_duration_remaining"]!.GetValue<int>() == 0,
            "Successful guarded DSON commit retains the selected ordinary quirk with zero countdown.");
        Console.WriteLine("PASS: quirk-rule refresh, stale prepare/commit rejection, candidate rebinding and semantic evolution save guards.");
    }
}
