internal static partial class ContractSuite
{
    public static async Task RunBuffHashCollisionsOnlyAsync(string repositoryRoot)
    {
        var root = Path.Combine(repositoryRoot, "artifacts", "buff-hash-contracts", Guid.NewGuid().ToString("N"));
        var codec = DsonSaveCodec.CreateNative();
        await RunHeroReferenceDependencyContractsAsync(root, codec);
        await RunBuffEnumContractsAsync(root, codec);
        Console.WriteLine($"Artifacts: {root}");
    }

    private static async Task VerifyBuffHashCollisionsAsync(string runRoot, DsonSaveCodec codec)
    {
        JsonObject Buff(string id, double amount = 0.5, string stat = "max_hp") => new()
        {
            ["id"] = id, ["stat_type"] = "combat_stat_multiply", ["stat_sub_type"] = stat,
            ["amount"] = amount, ["rule_type"] = "always", ["is_false_rule"] = false
        };
        void WriteBuffs(string path, params JsonObject[] buffs) =>
            WriteMultiMash(Path.GetDirectoryName(path)!, Path.GetFileName(path),
                new JsonObject { ["buffs"] = new JsonArray(buffs.Select(buff => buff.DeepClone()).ToArray()) }.ToJsonString());

        foreach (var kind in QuirkRuleSourceKinds)
        {
            var root = Path.Combine(runRoot, "buff-hash-collisions", kind);
            var f = QuirkRuleContent(root, kind);
            var first = Path.GetFullPath(Path.Combine(f.Source, f.Prefix + "shared/buffs/a.buffs.json"));
            var last = Path.GetFullPath(Path.Combine(f.Source, f.Prefix + "shared/buffs/z.buffs.json"));
            WriteBuffs(first);
            WriteBuffs(last);
            if (kind is "local" or "workshop" or "dlc-mod") WriteFixtureManifest(f.Source);

            HeroClassCatalogResult Load(params string[] references)
            {
                File.WriteAllText(f.Quirks, ReferenceQuirks("buffs", references));
                return HeroClassCatalog.Load(f.Content);
            }
            void Expect(double hp, string winner, params string[] references)
            {
                var catalog = Load(references);
                var quirk = catalog.InitialQuirks.Single(q => q.Id == "other");
                Assert(quirk.WriteStatus == HeroInitialQuirkWriteStatus.Direct &&
                    quirk.MaxHpModifiers.All(modifier => modifier.BuffId == winner) &&
                    GenerateQuirkRuleHero(catalog, "other").Preview.CurrentHp == hp,
                    $"{kind}: {string.Join(", ", references)} must resolve to complete Buff {winner}, HP {hp}.");
                Assert(!catalog.Issues.Any(issue => issue.Contains("conflicting native identities", StringComparison.Ordinal)),
                    "A proven Buff winner must not retain the former unresolved-identity warning.");
            }

            foreach (var (a, b, hash) in new[]
            {
                ("Az", "BE", 3567u),
                ("EosScourge_tooltip_2f", "EosScourge_tooltip_31", 2244466516u)
            })
            {
                Assert(HashLoc2Key(a) == hash && HashLoc2Key(b) == hash, "Independent hash fixture must collide.");
                WriteBuffs(first, Buff(a), Buff(b, 1, "speed_rating"));
                Expect(20, b, a);
                Expect(20, b, b);
                WriteBuffs(first, Buff(b, 1, "speed_rating"), Buff(a));
                Expect(30, a, a);
                Expect(30, a, b);
                WriteBuffs(first, Buff(a), Buff(b, 1, "speed_rating"), Buff(a, 1));
                Expect(40, a, b); // Grouping by textual ID first would incorrectly select B.
                WriteBuffs(first, Buff(a));
                WriteBuffs(last, Buff(b, 1));
                Expect(40, b, a);
                Expect(40, b, b);
                WriteBuffs(first, Buff(b, 1));
                WriteBuffs(last, Buff(a));
                Expect(30, a, b);
                WriteBuffs(first, Buff(a, 1, "speed_rating"));
                WriteBuffs(last, Buff(b, 0.25));
                Expect(35, b, a, b, a + "\0tail"); // Every reference occurrence contributes.
                var persistenceRoot = Path.Combine(root, a);
                Directory.CreateDirectory(persistenceRoot);
                await VerifyProgressionCandidatePersistenceAsync(persistenceRoot,
                    GenerateQuirkRuleHero(Load(a, b, a + "\0tail"), "other"), codec);

                var originalLanguage = EditorText.Culture.Name;
                var systemCulture = EditorText.SystemCulture;
                try
                {
                    foreach (var language in new[] { "en", "zh-CN" })
                    {
                        EditorText.Initialize(language, systemCulture);
                        var catalog = Load(a);
                        var notice = catalog.Issues.Single(issue => issue.StartsWith("[DDSE:BuffHashOverride]", StringComparison.Ordinal));
                        var entries = CatalogLogDiagnostics.Summarize([("Heroes", new[] { notice })]);
                        Assert(entries.Count == 1 && entries[0].Level == DiagnosticLogLevel.Information &&
                            !entries[0].Message.Contains("[DDSE:", StringComparison.Ordinal) &&
                            entries[0].Message.Contains(a, StringComparison.Ordinal) &&
                            entries[0].Message.Contains(b, StringComparison.Ordinal) &&
                            entries[0].Message.Contains(first, StringComparison.Ordinal) &&
                            entries[0].Message.Contains(last, StringComparison.Ordinal) &&
                            entries[0].Message.Contains(language == "en" ? $"definition '{b}'" : $"定义「{b}」", StringComparison.Ordinal),
                            "Resolved collision diagnostics must name both definitions, paths and final winner as information in either language.");
                    }
                }
                finally { EditorText.Initialize(originalLanguage, systemCulture); }

                foreach (var invalid in new[]
                {
                    new JsonObject { ["id"] = b, ["stat_type"] = "combat_stat_multiply", ["stat_sub_type"] = "max_hp",
                        ["rule_type"] = "always", ["is_false_rule"] = false }, // No inherited amount.
                    Buff(b, double.MaxValue), Buff(b, -1), Buff(b, 0.5, "unknown_stat"),
                    new JsonObject { ["id"] = b, ["stat_type"] = "combat_stat_multiply", ["stat_sub_type"] = "max_hp",
                        ["amount"] = 0.5, ["rule_type"] = "unknown_rule", ["is_false_rule"] = false }
                })
                {
                    WriteBuffs(first, Buff(a));
                    WriteBuffs(last, invalid);
                    AssertQuirkRuleRejected(Load(a), ["other"], "");
                }
                WriteBuffs(last);
            }
            WriteBuffs(first, Buff("Az"), Buff("Az", 1));
            Expect(40, "Az", "BE");
            Assert(!Load("Az").Issues.Any(issue => issue.StartsWith("[DDSE:BuffHashOverride]", StringComparison.Ordinal)),
                "Ordinary same-ID replacement does not need a hash-collision notice.");
            Console.WriteLine($"PASS: {kind} Buff hash collisions, AB/BA/ABA ordering, complete replacement, aliases, repeated HP, bilingual diagnostics, unsafe winners and DSON.");
        }

        // Same-path replacement retains its native result slot, not the last
        // position in the list. The later base-file slot can still win by hash.
        var overlayRoot = Path.Combine(runRoot, "buff-hash-overlay-slots");
        var overlay = QuirkRuleContent(overlayRoot, "local");
        WriteBuffs(Path.Combine(overlayRoot, "baseline/shared/buffs/a.buffs.json"), Buff("Az", 2));
        WriteBuffs(Path.Combine(overlayRoot, "baseline/shared/buffs/z.buffs.json"), Buff("Az"));
        WriteBuffs(Path.Combine(overlay.Source, "shared/buffs/a.buffs.json"), Buff("BE", 1));
        File.WriteAllText(overlay.Quirks, ReferenceQuirks("buffs", "BE"));
        WriteFixtureManifest(overlay.Source);
        Assert(GenerateQuirkRuleHero(HeroClassCatalog.Load(overlay.Content), "other").Preview.CurrentHp == 30,
            "A higher-priority Mod's replacement keeps the early slot; the later base file wins by hash.");
        WriteBuffs(Path.Combine(overlay.Source, "shared/buffs/z.buffs.json"), Buff("BE", 1));
        WriteFixtureManifest(overlay.Source);
        Assert(GenerateQuirkRuleHero(HeroClassCatalog.Load(overlay.Content), "other").Preview.CurrentHp == 40,
            "Replacing the later slot must update the same native hash to the new complete definition.");
        Console.WriteLine("PASS: Buff hash resolution follows native effective file slots after provider overlays.");
    }
}
