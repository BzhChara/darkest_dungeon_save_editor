using System.Reflection;
using System.Text.Json;

internal static partial class ContractSuite
{
    private static async Task VerifyEquipmentProgressionGuardsAsync(string runRoot, DsonSaveCodec codec)
    {
        var root = Path.Combine(runRoot, "equipment-progression-guards");
        var ordinary = new EquipmentCase("guard", "equipment", [null, "a"], [null, "a"], [("a", 1)], 1, 1);
        async Task Reject(ActiveContentSnapshot content, int level, string expected)
        {
            var catalog = HeroClassCatalog.Load(content);
            var hero = catalog.HeroClasses.Single();
            var failure = await CaptureSaveFailureAsync(() =>
            {
                StagecoachHeroCandidateFactory.Generate(catalog, hero, 11, level, []);
                return Task.CompletedTask;
            });
            Assert(!hero.GenerationAvailability.Single(a => a.ResolveLevel == level).CanGenerate &&
                   failure is InvalidOperationException && failure.Message.Contains(expected, StringComparison.Ordinal),
                $"Equipment generation and availability retain the '{expected}' guard; actual: {failure?.Message}");
        }
        foreach (var (name, extra, expectedRank) in new[]
        {
            ("omitted", "", 0),
            ("empty", ".upgradeRequirementCode \t", 1),
            ("wrong-case", ".upgraderequirementcode", 0),
            ("comment", "// .upgradeRequirementCode", 0)
        })
        {
            var fixture = EquipmentContent(Path.Combine(root, name), "local", ordinary);
            File.AppendAllText(fixture.Info, "armour: .name coat1 .hp 41 " + extra + "\n");
            var catalog = HeroClassCatalog.Load(fixture.Content);
            var generated = StagecoachHeroCandidateFactory.Generate(catalog, catalog.HeroClasses.Single(), 11, 0, []);
            Assert(generated.Preview.ArmourRank == expectedRank && generated.Preview.CurrentHp == (expectedRank == 1 ? 41 : 20),
                $"{name}: omitted codes inherit, explicit empty codes clear to NUL, and case/comments preserve native fields.");
        }

        var noXp = EquipmentContent(Path.Combine(root, "no-xp"), "base",
            ordinary with { Weapon = [null, null], Armour = [null, null], Requirements = [] });
        File.WriteAllText(Path.Combine(root, "no-xp", "baseline", "campaign", "roster", "roster.variables.json"), "{}");
        var noXpCatalog = HeroClassCatalog.Load(noXp.Content);
        var noXpHero = noXpCatalog.HeroClasses.Single();
        var zero = StagecoachHeroCandidateFactory.Generate(noXpCatalog, noXpHero, 11, 0, []);
        Assert(noXpHero.LevelProfiles.Count == 1 && zero.Preview.ArmourRank == 1 && zero.Preview.CurrentHp == 40,
            "Missing XP thresholds still resolve free equipment at level zero instead of manufacturing rank zero.");

        var invalidHp = EquipmentContent(Path.Combine(root, "invalid-hp"), "base",
            ordinary with { Armour = [null, null], Requirements = [] });
        File.AppendAllText(invalidHp.Info, "armour: .name coat1 .hp 0\n");
        await Reject(invalidHp.Content, 0, "有效 HP");
        var nonAscii = EquipmentContent(Path.Combine(root, "non-ascii"), "base",
            ordinary with { Armour = [null, "é"] });
        await Reject(nonAscii.Content, 1, "0xC3");

        var malformed = EquipmentContent(Path.Combine(root, "malformed-winner"), "local", ordinary);
        var trees = JsonNode.Parse(File.ReadAllText(malformed.Trees))!.AsObject();
        trees["trees"]!.AsArray().Add(new JsonObject { ["id"] = "equipment.armour", ["requirements"] = "invalid" });
        File.WriteAllText(malformed.Trees, trees.ToJsonString());
        await Reject(malformed.Content, 0, "requirements array");

        var alias = EquipmentContent(Path.Combine(root, "hash-alias"), "base", ordinary);
        Assert(HashLoc2Key("equipment.weapon") == HashLoc2Key("equipment.weapp9"), "Fixture must be a real native hash alias.");
        File.WriteAllText(alias.Trees, File.ReadAllText(alias.Trees).Replace("equipment.weapon", "equipment.weapp9", StringComparison.Ordinal));
        await Reject(alias.Content, 0, "哈希冲突");
        foreach (var length in new[] { 62, 63, 64 })
        {
            var collision = EquipmentContent(Path.Combine(root, "target-" + length), "base",
                ordinary with { HeroId = new string('h', length) });
            await Reject(collision.Content, 0, length > 63 ? "超过 63 字节" : "同一购买目标");
        }
        var skillAlias = EquipmentContent(Path.Combine(root, "skill-alias"), "base", ordinary);
        File.AppendAllText(skillAlias.Info, "combat_skill: .id weapon .level 0\n");
        await Reject(skillAlias.Content, 0, "装备与技能指向同一购买目标");
        var skillHash = EquipmentContent(Path.Combine(root, "skill-hash"), "base",
            ordinary with { TreeMode = "none", Weapon = [null, null], Armour = [null, null], Requirements = [] });
        File.AppendAllText(skillHash.Info, "combat_skill: .id weapp9 .level 0\n");
        await Reject(skillHash.Content, 0, "装备与技能购买编号冲突");

        var refresh = EquipmentContent(Path.Combine(root, "refresh"), "local",
            ordinary with { Requirements = [("a", 1), ("b", 1)] });
        var before = HeroClassCatalog.Load(refresh.Content);
        var beforeFingerprint = ProfileCatalogContentFingerprint.Capture(refresh.Content.Sources);
        var manifest = File.ReadAllBytes(Path.Combine(refresh.Source, "modfiles.txt"));
        File.WriteAllText(refresh.Info, File.ReadAllText(refresh.Info).Replace(".upgradeRequirementCode a", ".upgradeRequirementCode b", StringComparison.Ordinal));
        var after = HeroClassCatalog.Load(refresh.Content);
        Assert(beforeFingerprint != ProfileCatalogContentFingerprint.Capture(refresh.Content.Sources) &&
               manifest.SequenceEqual(File.ReadAllBytes(Path.Combine(refresh.Source, "modfiles.txt"))),
            "Equipment-only changes refresh the catalog without editing the manifest.");
        Assert(JsonSerializer.Serialize(before.HeroClasses) == JsonSerializer.Serialize(after.HeroClasses),
            "Fixture changes only raw equipment requirements while leaving all public computed profiles unchanged.");
        var fingerprintMethod = typeof(SaveEditService).GetMethod("ComputeHeroCatalogSha256", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert(!Equals(fingerprintMethod.Invoke(null, [before]), fingerprintMethod.Invoke(null, [after])),
            "Prepared-save identity must include raw equipment metadata even when displayed ranks are unchanged.");

        var nonfinite = EquipmentContent(Path.Combine(root, "nonfinite-hp"), "local",
            ordinary with { HeroId = "nonfinite" });
        var finiteInfo = File.ReadAllText(nonfinite.Info);
        var invalidFingerprints = new List<string>();
        var invalidPublicDefinitions = new List<string>();
        foreach (var hp in new[] { "1e100", "-1e100" })
        {
            File.WriteAllText(nonfinite.Info, finiteInfo.Replace(".hp 40", ".hp " + hp, StringComparison.Ordinal));
            var limited = HeroClassCatalog.Load(nonfinite.Content).HeroClasses.Single();
            Assert(limited.LevelProfiles.Count == 1 && limited.LevelProfiles[0].ArmourHp == 20 &&
                   !limited.GenerationAvailability.Single(a => a.ResolveLevel == 1).CanGenerate,
                "A nonfinite upper armour HP retains only the valid base profile; invalid HP is not made writable.");
            var combined = after with { HeroClasses = after.HeroClasses.Concat([limited]).ToArray() };
            var unaffected = StagecoachHeroCandidateFactory.Generate(combined, after.HeroClasses.Single(), 11, 1, []);
            Assert(unaffected.Preview.CurrentHp == 40, "An unrelated valid class still generates normally.");
            invalidPublicDefinitions.Add(JsonSerializer.Serialize(combined.HeroClasses));
            invalidFingerprints.Add((string)fingerprintMethod.Invoke(null, [combined])!);
        }
        Assert(invalidPublicDefinitions[0] == invalidPublicDefinitions[1] && invalidFingerprints[0] != invalidFingerprints[1],
            "Nonfinite raw equipment HP must serialize safely and remain distinct in the prepared-save fingerprint.");

        var original = after.HeroClasses.Single();
        var generatedValid = StagecoachHeroCandidateFactory.Generate(after, original, 11, 1, []);
        foreach (var code in new[] { "\"", "\\", "\0", "\u001f", "\u007f", "0", "a", "'", "~" })
        {
            var supported = code is "0" or "a" or "'" or "~";
            var codeRoot = Path.Combine(root, "codec-" + ((int)code[0]).ToString("X2"));
            Directory.CreateDirectory(codeRoot);
            var raw = JsonNode.Parse("""{"base_root":{"purchases":{"0":{"instance_number":950,"tree_id":1,"requirement_code":"0","is_purchased":true}}}}""")!.AsObject();
            raw["base_root"]!["purchases"]!["0"]!["requirement_code"] = code;
            JsonObject? decoded = null;
            JsonException? codecFailure = null;
            try { decoded = await RoundtripDirectoryQueryAsync(codeRoot, "upgrades", raw, codec); }
            catch (JsonException error) { codecFailure = error; }
            Assert(supported ? codecFailure is null && JsonNode.DeepEquals(raw, decoded) :
                    codecFailure is JsonException || (codecFailure is null && !JsonNode.DeepEquals(raw, decoded)),
                "The purchase-code support boundary must match the bundled codec's actual output, not just an ASCII assumption.");
            if (supported) continue;
            var unsafeTree = EquipmentContent(Path.Combine(codeRoot, "catalog"), "base",
                ordinary with { Requirements = [("a", 1), (code, 0)] });
            await Reject(unsafeTree.Content, 0, "DSON codec");
            var writerFailure = await CaptureSaveFailureAsync(() =>
            {
                StagecoachHeroSaveEditor.AddCandidate(
                    JsonNode.Parse("""{"base_root":{"buildings":{"stage_coach":{"store":{"hero_recruit":{"generated":{}}}}}}}""")!.AsObject(),
                    JsonNode.Parse("""{"base_root":{"nextGuid":950,"heroes":{}}}""")!.AsObject(),
                    JsonNode.Parse("""{"base_root":{"purchases":{}}}""")!.AsObject(), generatedValid.Candidate,
                    [new HeroUpgradePurchase("equipment.armour", code)]);
                return Task.CompletedTask;
            });
            Assert(writerFailure is InvalidDataException && writerFailure.Message.Contains("DSON codec", StringComparison.Ordinal),
                "Direct purchase writes reject codec-incompatible bytes before producing an unsavable mutation.");
        }
        var stale = original with
        {
            LevelProfiles = original.LevelProfiles.Select(p => p.ResolveLevel == 1 ? p with { ArmourRank = 0, ArmourHp = 20 } : p).ToArray()
        };
        var staleCatalog = after with { HeroClasses = [stale] };
        var staleGeneration = await CaptureSaveFailureAsync(() =>
        {
            StagecoachHeroCandidateFactory.Generate(staleCatalog, stale, 11, 1, []);
            return Task.CompletedTask;
        });
        Assert(staleGeneration is InvalidOperationException && staleGeneration.Message.Contains("实际购买记录不一致", StringComparison.Ordinal),
            "Candidate creation rejects a profile that disagrees with actual equipment purchases.");
        var staleCandidate = generatedValid with { Preview = generatedValid.Preview with { ArmourRank = 0, CurrentHp = 20 } };
        var validateMethod = typeof(SaveEditService).GetMethod("ValidateGeneratedStagecoachCandidate", BindingFlags.NonPublic | BindingFlags.Static)!;
        var staleSave = await CaptureSaveFailureAsync(() =>
        {
            validateMethod.Invoke(null, [staleCatalog, staleCandidate]);
            return Task.CompletedTask;
        });
        Assert(staleSave is TargetInvocationException { InnerException: InvalidOperationException inner } &&
               inner.Message.Contains("实际购买记录不一致", StringComparison.Ordinal),
            "Save preflight rejects matching stale preview/profile ranks that disagree with native purchases.");
        Console.WriteLine("PASS: equipment code inheritance, identity restrictions, live refresh and save preflight guards.");
    }
}
