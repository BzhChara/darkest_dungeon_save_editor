internal static partial class ContractSuite
{
    private static async Task VerifyHeroQuirkClassificationGuardsAsync(string runRoot, DsonSaveCodec codec)
    {
        foreach (var change in new[] { "positive-disease", "probability" })
        {
            var root = Path.Combine(runRoot, "quirk-classification-save-guards", change);
            var f = QuirkRuleContent(root, "local");
            const string quirkJson = """{"quirks":[{"id":"p0","is_positive":true,"is_disease":false,"random_chance":0.0}]}""";
            File.WriteAllText(f.Quirks, quirkJson);
            var rulePath = WriteMultiMash(f.Source, "shared/rules.json", """{"quirks_max_positive":1}""");
            WriteFixtureManifest(f.Source);
            var profile = f.Content.Profile;
            WriteMultiMash(profile.ProfileDirectory, "persist.estate.json", """{"base_root":{}}""");
            foreach (var (name, json) in new[]
            {
                ("town", """{"base_root":{"buildings":{"stage_coach":{"store":{"hero_recruit":{"generated":{}}}}}}}"""),
                ("roster", """{"base_root":{"nextGuid":950,"heroes":{}}}"""),
                ("upgrades", """{"base_root":{"purchases":{}}}""")
            })
                await codec.EncodeAsync(WriteMultiMash(root, name + ".seed.json", json), Path.Combine(profile.ProfileDirectory, "persist." + name + ".json"), null);
            var originals = new[] { "town", "roster", "upgrades" }.ToDictionary(n => n,
                n => ComputeSha256(Path.Combine(profile.ProfileDirectory, "persist." + n + ".json")));
            void Unchanged() => Assert(originals.All(p => ComputeSha256(Path.Combine(profile.ProfileDirectory, "persist." + p.Key + ".json")) == p.Value),
                "All target saves remain byte-for-byte unchanged after preflight or a rejected commit.");
            var service = new SaveEditService(codec, new SaveEditorLocations(Path.Combine(root, "data"), Path.Combine(root, "work"), Path.Combine(root, "backups")));
            var before = HeroClassCatalog.Load(f.Content);
            var generated = GenerateQuirkRuleHero(before, "p0");
            var prepared = await service.PrepareStagecoachHeroEditAsync(profile, generated, before, f.Content);
            if (change == "positive-disease")
            {
                File.WriteAllText(f.Quirks, quirkJson.Replace("\"is_disease\":false", "\"is_disease\":true", StringComparison.Ordinal));
                File.WriteAllText(rulePath, """{"quirks_max_positive":0}""");
                var current = HeroClassCatalog.Load(f.Content);
                var staleCommit = await CaptureSaveFailureAsync(() => service.CommitAsync(prepared));
                var stalePrepare = await CaptureSaveFailureAsync(() => service.PrepareStagecoachHeroEditAsync(profile, generated, before, f.Content));
                Assert(staleCommit.Message.Contains("templates changed", StringComparison.Ordinal) && stalePrepare.Message.Contains("templates changed", StringComparison.Ordinal),
                    "Existing content guards reject changed quirk flags and limits.");
                var failure = await CaptureSaveFailureAsync(() => service.PrepareStagecoachHeroEditAsync(profile, generated, current, f.Content));
                Assert(failure is InvalidOperationException && failure.Message.Contains("最多", StringComparison.Ordinal),
                    "The actual save preparation path enforces the positive quota for diseases.");
                Unchanged();
                File.WriteAllText(rulePath, """{"quirks_max_positive":1}""");
                current = HeroClassCatalog.Load(f.Content);
                generated = GenerateQuirkRuleHero(current, "p0");
                prepared = await service.PrepareStagecoachHeroEditAsync(profile, generated, current, f.Content);
            }
            else
            {
                var signal = ProfileCatalogContentFingerprint.Capture(f.Content.Sources);
                File.WriteAllText(f.Quirks, quirkJson.Replace("0.0", "1e-50", StringComparison.Ordinal));
                Assert(signal != ProfileCatalogContentFingerprint.Capture(f.Content.Sources), "Probability-only file changes still trigger content refresh.");
                Unchanged();
                // Native-equivalent weights keep the existing semantic guard valid.
            }
            await service.CommitAsync(prepared);
            var decoded = Path.Combine(root, "town.committed.json");
            await codec.DecodeAsync(Path.Combine(profile.ProfileDirectory, "persist.town.json"), decoded);
            var saved = JsonNode.Parse(File.ReadAllText(decoded))!["base_root"]!["buildings"]!["stage_coach"]!["store"]!["hero_recruit"]!["generated"]!["950"]!;
            Assert(saved["quirks"]!.AsObject().Select(p => p.Key).SequenceEqual(["p0"]) &&
                   saved["actor"]!["current_hp"]!.GetValue<double>() == generated.Preview.CurrentHp,
                "Successful three-file DSON commit stores one quirk record and preserves generated HP.");
        }
        Console.WriteLine("PASS: positive disease save quotas, content refresh, native-equivalent probability guards and full DSON commits.");
    }
}
