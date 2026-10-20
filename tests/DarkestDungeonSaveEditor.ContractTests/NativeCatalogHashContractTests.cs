internal static partial class ContractSuite
{
    public static async Task RunNativeCatalogHashesOnlyAsync(string repositoryRoot)
    {
        var root = Path.Combine(repositoryRoot, "artifacts", "native-identity-contracts", Guid.NewGuid().ToString("N"));
        var codec = DsonSaveCodec.CreateNative();
        await RunNativeCatalogHashContractsAsync(root, codec);
        await RunDefinitionReadCompletenessContractsAsync(root, codec);
        await RunHeroSupportingReadContractsAsync(root, codec);
        await RunInventoryPersistenceContractsAsync(root, codec);
        Console.WriteLine($"Artifacts: {root}");
    }

    private static async Task RunNativeCatalogHashContractsAsync(string root, DsonSaveCodec codec)
    {
        Assert(HashLoc2Key("Az") == 3567 && HashLoc2Key("BE") == 3567, "Independent fixture strings share one native hash.");
        await VerifyNativeSavedAliasCountsAsync(Path.Combine(root, "saved-aliases"), codec);
        foreach (var kind in QuirkRuleSourceKinds)
        {
            await VerifyNativeQuirkHashesAsync(Path.Combine(root, "native-quirks", kind), kind, codec);
            await VerifyNativeEffectHashesAsync(Path.Combine(root, "native-effects", kind), kind);
            await VerifyNativeTrinketHashesAsync(Path.Combine(root, "native-trinkets", kind), kind, codec);
        }
        foreach (var kind in new[] { "local", "workshop" })
            VerifyNativeHashProvenance(Path.Combine(root, "native-hash-provenance", kind), kind);
    }

    private static async Task VerifyNativeSavedAliasCountsAsync(string root, DsonSaveCodec codec)
    {
        var heroRoot = Path.Combine(root, "heroes");
        var h = QuirkRuleContent(heroRoot, "base");
        var singleton = HashQuirk("BE");
        singleton["tags"] = new JsonArray("singleton");
        WriteHashQuirks(h.Quirks, HashQuirk("Az"), singleton);
        var heroCatalog = HeroClassCatalog.Load(h.Content);
        var generated = GenerateQuirkRuleHero(heroCatalog, "BE");
        var service = await SeedReferenceHeroProfileAsync(heroRoot, h.Content.Profile, codec);
        var town = JsonNode.Parse("""
            {"base_root":{"buildings":{"stage_coach":{"store":{
              "hero_recruit":{"generated":{"3":{"quirks":{"Az":{}}}}},
              "shard_hero_recruit":{"generated":{"4":{"quirks":{"BE":{}}}}}
            }}}}}
            """)!.AsObject();
        var roster = JsonNode.Parse("""
            {"base_root":{"nextGuid":950,"heroes":{
              "1":{"hero_file_data":{"raw_data":{"base_root":{"quirks":{"Az":{}}}}}},
              "2":{"quirks":{"Az":{},"BE":{}}},
              "5":{"quirks":{"az":{}," Az ":{}}}
            }}}
            """)!.AsObject();
        var limits = StagecoachHeroSaveEditor.AnalyzeQuirkLimits(town, roster, generated.Candidate, heroCatalog.InitialQuirks);
        Assert(limits.Single() is { ExistingRosterHeroes: 2, ExistingStagecoachCandidates: 2,
                ResultingHeroes: 5, DefinitionLimit: 1, ExceedsDefinitionLimit: true },
            "Singleton previews count saved aliases in both roster shapes and both pools, once per hero, without merging case/space-distinct hashes.");
        foreach (var (name, value) in new[] { ("town", town), ("roster", roster) })
        {
            var input = WriteMultiMash(heroRoot, name + ".aliases.json", value.ToJsonString());
            await codec.EncodeAsync(input, Path.Combine(h.Content.Profile.ProfileDirectory, "persist." + name + ".json"), null);
        }
        var preparedHero = await service.PrepareStagecoachHeroEditAsync(h.Content.Profile, generated, heroCatalog, h.Content);
        Assert(preparedHero.Preview.QuirkLimits.Single() is { ResultingHeroes: 5, ExceedsDefinitionLimit: true },
            "Real hero preparation retains singleton warnings for already saved native aliases.");
        await service.CommitAsync(preparedHero);

        var trinketRoot = Path.Combine(root, "trinkets");
        var resources = Path.Combine(trinketRoot, "resources");
        var limited = HashTrinket("Az", 2);
        limited["limit"] = 1;
        WriteDependencyEntries(resources, DependencyFirstPath, limited, HashTrinket("BE", 7));
        WriteMultiMash(resources, "inventory/a.inventory.system_configs.darkest",
            "inventory_system_config: .type trinket_storage .max_slots 100\n");
        var t = await BuildInventoryProviderProfileAsync(trinketRoot, QuerySources(resources, "base"), false, codec);
        var catalog = TrinketCatalog.Load(t.Content);
        var items = t.Estate["base_root"]!["trinkets"]!["items"]!.AsObject();
        items["0"] = new JsonObject { ["type"] = "trinket", ["id"] = "BE", ["amount"] = 1 };
        items["1"] = new JsonObject { ["type"] = "trinket", ["id"] = "be", ["amount"] = 1 };
        var mutation = TrinketSaveEditor.AddCopies(t.Estate, catalog.Trinkets.Single(), 1, 100);
        Assert(mutation.Preview is { ExistingCopies: 1, ResultingCopies: 2, ExceedsDefinitionLimit: true } &&
            TrinketSaveEditor.Summarize(mutation.UpdatedRoot) is { TrinketCopies: 3, UniqueTrinketIds: 2 },
            "Trinket count, limit warning and summary share native identities while case-distinct items remain separate.");
        await codec.EncodeAsync(WriteMultiMash(trinketRoot, "aliases.json", t.Estate.ToJsonString()), t.Content.Profile.EstateSavePath, null);
        var preparedTrinket = await t.Service.PrepareTrinketEditAsync(t.Content.Profile, catalog.Trinkets.Single(), 1, catalog.Storage, t.Content);
        Assert(preparedTrinket.Preview is { ExistingCopies: 1, ResultingCopies: 2, ExceedsDefinitionLimit: true },
            "Real trinket preparation counts existing saved aliases.");
        await t.Service.CommitAsync(preparedTrinket);
        var decoded = Path.Combine(trinketRoot, "committed.json");
        await codec.DecodeAsync(t.Content.Profile.EstateSavePath, decoded);
        var restored = JsonSupport.ReadObject(decoded);
        Assert(TrinketSaveEditor.CountCopies(restored, "Az") == 2 &&
            restored["base_root"]!["trinkets"]!["items"]!["0"]!["id"]!.GetValue<string>() == "BE" &&
            restored["base_root"]!["trinkets"]!["items"]!["2"]!["id"]!.GetValue<string>() == "Az",
            "DSON verification counts aliases without renaming the existing copy or changing the selected definition ID.");

        // Saved inventory fields are restored through 64-byte C buffers.
        var prefix = new string('x', 63);
        items.Clear();
        items["0"] = new JsonObject { ["type"] = "trinket", ["id"] = prefix + "suffix" };
        items["1"] = new JsonObject { ["type"] = "trinket\0suffix", ["id"] = "BE\0suffix" };
        items["2"] = new JsonObject { ["type"] = "u=inket", ["id"] = "BE" };
        Assert(HashLoc2Key("u=inket") == HashLoc2Key("trinket") &&
            TrinketSaveEditor.CountCopies(t.Estate, prefix) == 1 && TrinketSaveEditor.CountCopies(t.Estate, "Az") == 2,
            "Saved type/ID lookup uses the native buffer and C-string identities, including a type alias.");
        Console.WriteLine("PASS: saved native aliases, singleton/copy limit warnings, summaries, bounded inventory reads and actual DSON commits.");
    }

    private static JsonObject HashQuirk(string id, bool positive = true, bool hp = false) => new()
    {
        ["id"] = id, ["is_positive"] = positive, ["random_chance"] = 0,
        ["buffs"] = hp ? new JsonArray("hp50") : new JsonArray()
    };

    private static void WriteHashQuirks(string path, params JsonObject[] quirks) =>
        File.WriteAllText(path, new JsonObject { ["quirks"] = new JsonArray(quirks.Cast<JsonNode>().ToArray()) }.ToJsonString());

    private static async Task VerifyNativeQuirkHashesAsync(string root, string kind, DsonSaveCodec codec)
    {
        var f = QuirkRuleContent(root, kind);
        var last = WriteMultiMash(f.Source, f.Prefix + "shared/quirk/z.quirk_library.json", """{"quirks":[]}""");
        WriteMultiMash(f.Source, f.Prefix + "shared/buffs/a.buffs.json", ReferenceHpBuff("hp50", .5));
        if (kind is "local" or "workshop" or "dlc-mod") WriteFixtureManifest(f.Source);

        HeroClassCatalogResult Expect(string id, bool positive, double hp)
        {
            var catalog = HeroClassCatalog.Load(f.Content);
            var row = catalog.InitialQuirks.Single();
            Assert(row.Id == id && row.IsPositive == positive && row.WriteStatus == HeroInitialQuirkWriteStatus.Direct &&
                GenerateQuirkRuleHero(catalog, id).Preview.CurrentHp == hp,
                $"{kind}: the final native quirk match supplies one row, classification, and its complete HP definition.");
            return catalog;
        }

        WriteHashQuirks(f.Quirks, HashQuirk("Az", hp: true), HashQuirk("BE", positive: false));
        Expect("BE", false, 20);
        WriteHashQuirks(f.Quirks, HashQuirk("BE", positive: false), HashQuirk("Az", hp: true));
        Expect("Az", true, 30);
        WriteHashQuirks(f.Quirks, HashQuirk("Az"), HashQuirk("BE", positive: false), HashQuirk("Az", hp: true));
        Expect("Az", true, 30); // A/B/A must retain the final A, not the last textual group.
        WriteHashQuirks(f.Quirks, HashQuirk("Az", hp: true));
        WriteHashQuirks(last, HashQuirk("BE", positive: false));
        var crossFile = Expect("BE", false, 20);
        Assert(crossFile.InitialQuirks.Single().SourcePath == last, "The displayed source belongs to the winning file.");
        VerifyNativeHashNotices(() => HeroClassCatalog.Load(f.Content).Issues, "Az", "BE");
        WriteHashQuirks(last, HashQuirk("Az\0tail", hp: true));
        var nul = Expect("Az", true, 30);
        await VerifyProgressionCandidatePersistenceAsync(root, GenerateQuirkRuleHero(nul, "Az"), codec);
        WriteHashQuirks(last, HashQuirk("az"), HashQuirk(" Az "));
        Assert(HeroClassCatalog.Load(f.Content).InitialQuirks.Count == 3, "Case and spaces remain distinct native identities.");

        WriteHashQuirks(last);
        var initial = Expect("Az", true, 30);
        var service = await SeedReferenceHeroProfileAsync(root, f.Content.Profile, codec);
        var paths = new[] { "town", "roster", "upgrades" }
            .Select(name => Path.Combine(f.Content.Profile.ProfileDirectory, "persist." + name + ".json")).ToArray();
        var original = paths.Select(ComputeSha256).ToArray();
        var generated = GenerateQuirkRuleHero(initial, "Az");
        var prepared = await service.PrepareStagecoachHeroEditAsync(f.Content.Profile, generated, initial, f.Content);
        WriteHashQuirks(last, HashQuirk("BE", positive: false));
        Assert(await CaptureSaveFailureAsync(() => service.PrepareStagecoachHeroEditAsync(f.Content.Profile, generated, initial, f.Content))
            is InvalidOperationException, "A changed native quirk winner invalidates stale generation.");
        Assert(await CaptureSaveFailureAsync(() => service.CommitAsync(prepared)) is InvalidOperationException,
            "A prepared hero cannot commit after the effective quirk identity changes.");
        Assert(paths.Select(ComputeSha256).SequenceEqual(original), "Rejected quirk operations preserve every fixture save.");

        foreach (var failure in kind is "local" or "workshop" or "dlc-mod"
            ? new[] { "locked", "malformed", "missing" } : new[] { "locked", "malformed" })
        {
            await WithFailedDefinitionAsync(last, failure, () =>
            {
                Assert(HeroClassCatalog.Load(f.Content).InitialQuirks.Single().WriteStatus == HeroInitialQuirkWriteStatus.Unverified,
                    "A later unknown file invalidates an earlier last-match quirk.");
                return Task.CompletedTask;
            });
            await WithFailedDefinitionAsync(f.Quirks, failure, () =>
            {
                Expect("BE", false, 20);
                return Task.CompletedTask;
            });
        }
        Console.WriteLine($"PASS: {kind}/quirk hash order, canonical selection, HP, NUL, DSON, read failures and stale-write guards.");
    }

    private static async Task VerifyNativeEffectHashesAsync(string root, string kind)
    {
        var f = QuirkRuleContent(root, kind);
        WriteHashQuirks(f.Quirks, HashQuirk("Az"), HashQuirk("other"));
        var heroPath = Path.Combine(root, "baseline/heroes/progression/progression.info.darkest");
        var originalHero = File.ReadAllText(heroPath);
        void Skill(string effect) => File.WriteAllText(heroPath,
            originalHero + $"\ncombat_skill: .id attack .level 0 .effect \"{effect}\"\n");
        Skill("BE");
        var first = WriteMultiMash(f.Source, f.Prefix + "effects/a.effects.darkest", "effect: .name Az .disease BE\n");
        var last = WriteMultiMash(f.Source, f.Prefix + "effects/z.effects.darkest", "");
        if (kind is "local" or "workshop" or "dlc-mod") WriteFixtureManifest(f.Source);
        void Expect(string? id)
        {
            var catalog = HeroClassCatalog.Load(f.Content);
            var clues = catalog.HeroClasses.Single(h => h.Id == "progression").RuntimeQuirkSignals;
            Assert(id is null ? clues.Count == 0 : clues.Count == 1 && clues[0].QuirkId == id,
                $"{kind}: skill -> Effect -> quirk hashes must resolve '{id ?? "(none)"}'.");
            Assert(GenerateQuirkRuleHero(catalog).Candidate["quirks"]!.AsObject().Count == 0,
                "Runtime clues do not add initial quirks to a generated hero.");
        }

        Expect("Az");
        File.WriteAllText(last, "effect: .name BE .disease other\n");
        Expect("other");
        VerifyNativeHashNotices(() => HeroClassCatalog.Load(f.Content).Issues, "Az", "BE");
        File.WriteAllText(last, "effect: .name BE .duration 3\n");
        Expect("Az");
        File.WriteAllText(first, "effect: .name Az .disease BE\neffect: .name BE .disease other\neffect: .name Az .disease BE\n");
        Expect("Az");
        File.WriteAllText(first, "effect: .name Az .disease BE\n");
        File.WriteAllText(last, "effect: .name BE .disease \"\"\neffect: .name Az .duration 3\n");
        Expect(null);
        foreach (var reference in new[] { "az", " Az " })
        {
            Skill(reference);
            File.WriteAllText(last, "");
            Expect(null);
        }
        Skill("BE");
        File.WriteAllText(last, "effect: .name BE .duration 3\n");
        await WithFailedDefinitionAsync(first, "locked", () =>
        {
            Expect(null);
            File.WriteAllText(last, "effect: .name BE .disease other\n");
            Expect("other");
            File.WriteAllText(last, "effect: .name BE .disease \"\"\n");
            Expect(null);
            return Task.CompletedTask;
        });
        File.WriteAllText(last, "effect: .name BE .disease other\n");
        await WithFailedDefinitionAsync(last, "locked", () => { Expect(null); return Task.CompletedTask; });

        // The Effect loader reads these two fields into 64-byte C buffers.
        var prefix = new string('x', 63);
        WriteHashQuirks(f.Quirks, HashQuirk(prefix));
        Skill(prefix);
        File.WriteAllText(first, $"effect: .name {prefix}tail .disease {prefix}suffix\n");
        File.WriteAllText(last, "");
        Expect(prefix);
        File.WriteAllText(last, $"effect: .name {prefix}other .disease \"\"\n");
        Expect(null);
        // A truncated multibyte code point cannot be represented as a safe managed identity.
        File.WriteAllText(last, $"effect: .name {new string('x', 62)}中 .disease other\n");
        Expect(null);
        Assert(HeroClassCatalog.Load(f.Content).Issues.Any(issue => issue.Contains(last, StringComparison.Ordinal)),
            "An undecodable native buffer is reported instead of hashing a replacement character.");
        Console.WriteLine($"PASS: {kind}/Effect and quirk aliases, field updates, omitted/empty values, read certainty and native buffers.");
    }

    private static JsonObject HashTrinket(string id, int uses, string field = "buffs", params string[] references)
    {
        var value = DependencyTrinket(uses, field, references);
        value["id"] = id;
        value["price"] = uses * 100;
        return value;
    }

    private static async Task VerifyNativeTrinketHashesAsync(string root, string kind, DsonSaveCodec codec)
    {
        var resources = Path.Combine(root, "resources");
        var prefix = kind == "dlc-mod" ? "dlc/rq_feature/" : "";
        var first = prefix + DependencyFirstPath;
        var last = prefix + DependencyLastPath;
        var buff = WriteMultiMash(resources, prefix + DependencyBuffPath, DependencyBuffJson);
        WriteMultiMash(resources, prefix + "heroes/Az/Az.info.darkest", "armour: .name test .hp 20\n");
        WriteMultiMash(resources, prefix + "inventory/a.inventory.system_configs.darkest",
            "inventory_system_config: .type trinket_storage .max_slots 100\n");
        WriteDependencyEntries(resources, first);
        WriteDependencyEntries(resources, last);
        if (kind is "local" or "workshop" or "dlc-mod") WriteFixtureManifest(resources);
        var f = await BuildInventoryProviderProfileAsync(root, QuerySources(resources, kind), false, codec);
        TrinketCatalogResult Expect(string id, int uses, bool readOnly = false)
        {
            var catalog = TrinketCatalog.Load(f.Content);
            var item = catalog.Trinkets.Single();
            Assert(item.Id == id && item.QuestUses == uses && item.TriggerLimit == uses + 1 &&
                item.Price == uses * 100 && item.HasProviderConflict == readOnly,
                $"{kind}: first eligible trinket must be {id}/{uses}, readOnly={readOnly}.");
            return catalog;
        }

        WriteDependencyEntries(resources, first, HashTrinket("Az", 2), HashTrinket("BE", 7));
        Expect("Az", 2);
        WriteDependencyEntries(resources, first, HashTrinket("BE", 7), HashTrinket("Az", 2));
        Expect("BE", 7);
        WriteDependencyEntries(resources, first, HashTrinket("Az", 2), HashTrinket("BE", 7), HashTrinket("Az", 9));
        Expect("Az", 2);
        WriteDependencyEntries(resources, first, HashTrinket("Az", 2));
        WriteDependencyEntries(resources, last, HashTrinket("BE", 7));
        Expect("Az", 2);
        VerifyNativeHashNotices(() => TrinketCatalog.Load(f.Content).Issues, "Az", "BE");
        foreach (var field in new[] { "buffs", "hero_class_requirements" })
        {
            WriteDependencyEntries(resources, first, HashTrinket("Az", 2, field, "missing"));
            Expect("BE", 7);
            WriteDependencyEntries(resources, first, HashTrinket("Az", 2, field, "BE"));
            Expect("Az", 2);
        }
        WriteDependencyEntries(resources, first, HashTrinket("Az", 2, "buffs", "Az"));
        await WithFailedDefinitionAsync(buff, "locked", async () =>
        {
            var partial = Expect("Az", 2, readOnly: true);
            Assert(await CaptureSaveFailureAsync(() => f.Service.PrepareTrinketEditAsync(
                f.Content.Profile, partial.Trinkets.Single(), 1, partial.Storage, f.Content)) is InvalidOperationException,
                "Unknown dependencies cannot promote a later alias or permit writing.");
        });
        WriteDependencyEntries(resources, first, HashTrinket("Az", 2));
        foreach (var failure in kind is "local" or "workshop" or "dlc-mod"
            ? new[] { "locked", "malformed", "missing" } : new[] { "locked", "malformed" })
        {
            await WithFailedDefinitionAsync(Path.Combine(resources, first), failure, () =>
            {
                Expect("BE", 7, readOnly: true);
                return Task.CompletedTask;
            });
            await WithFailedDefinitionAsync(Path.Combine(resources, last), failure, () =>
            {
                Expect("Az", 2);
                return Task.CompletedTask;
            });
        }
        var healthy = Expect("Az", 2);
        var prepared = await f.Service.PrepareTrinketEditAsync(f.Content.Profile, healthy.Trinkets.Single(), 2, healthy.Storage, f.Content);
        var original = ComputeSha256(f.Content.Profile.EstateSavePath);
        WriteDependencyEntries(resources, first, HashTrinket("BE", 7), HashTrinket("Az", 2));
        Assert(await CaptureSaveFailureAsync(() => f.Service.PrepareTrinketEditAsync(
            f.Content.Profile, healthy.Trinkets.Single(), 1, healthy.Storage, f.Content)) is InvalidOperationException,
            "Changing the first native identity invalidates a stale trinket selection.");
        Assert(await CaptureSaveFailureAsync(() => f.Service.CommitAsync(prepared)) is InvalidOperationException &&
            ComputeSha256(f.Content.Profile.EstateSavePath) == original, "A stale trinket commit cannot alter the fixture save.");
        var current = Expect("BE", 7);
        await f.Service.CommitAsync(await f.Service.PrepareTrinketEditAsync(f.Content.Profile, current.Trinkets.Single(), 2, current.Storage, f.Content));
        var decoded = Path.Combine(root, "committed.json");
        await codec.DecodeAsync(f.Content.Profile.EstateSavePath, decoded);
        var copies = JsonSupport.ReadObject(decoded)["base_root"]!["trinkets"]!["items"]!.AsObject();
        Assert(copies.Count == 2 && copies.All(pair => pair.Value!["id"]!.GetValue<string>() == "BE" &&
            pair.Value["quest_uses_remaining"]!.GetValue<int>() == 7 && pair.Value["triggers_remaining"]!.GetValue<int>() == 8),
            "DSON writes use the winning trinket ID and original counters.");
        WriteDependencyEntries(resources, first, HashTrinket("Az\0tail", 2));
        Assert(TrinketCatalog.Load(f.Content).Trinkets.Single().SaveIdentityIssue.Length > 0,
            "Hash matching must not relax unsafe trinket save identity validation.");
        Console.WriteLine($"PASS: {kind}/trinket hash order, dependencies, canonical counters, DSON, read failures and stale-write guards.");
    }

    private static void VerifyNativeHashNotices(Func<IReadOnlyList<string>> issues, string a, string b)
    {
        var language = EditorText.Culture.Name;
        var system = EditorText.SystemCulture;
        try
        {
            foreach (var culture in new[] { "en", "zh-CN" })
            {
                EditorText.Initialize(culture, system);
                var notice = issues().Single(value => value.StartsWith("[DDSE:NativeHashResolution]", StringComparison.Ordinal));
                var entries = CatalogLogDiagnostics.Summarize([("Native identities", new[] { notice })]);
                Assert(entries.Count == 1 && entries[0].Level == DiagnosticLogLevel.Information &&
                    notice.Contains(a, StringComparison.Ordinal) && notice.Contains(b, StringComparison.Ordinal) &&
                    !entries[0].Message.Contains("[DDSE:", StringComparison.Ordinal),
                    "A resolved native collision is a localized informational notice with both providers and no raw marker.");
            }
        }
        finally { EditorText.Initialize(language, system); }
    }

    private static void VerifyNativeHashProvenance(string root, string kind)
    {
        var f = QuirkRuleContent(root, "base");
        WriteHashQuirks(f.Quirks, HashQuirk("Az"));
        var last = WriteMultiMash(f.Source, "shared/quirk/z.quirk_library.json", "");
        WriteHashQuirks(last, HashQuirk("BE", positive: false));
        WriteDependencyEntries(f.Source, DependencyFirstPath, HashTrinket("Az", 2));
        var mod = Path.Combine(root, "high-mod");
        var replacement = WriteMultiMash(mod, "shared/quirk/a.quirk_library.json", "");
        WriteHashQuirks(replacement, HashQuirk("Az", hp: true));
        WriteMultiMash(mod, DependencyBuffPath, ReferenceHpBuff("hp50", .5));
        WriteDependencyEntries(mod, DependencyLastPath, HashTrinket("BE", 7));
        WriteFixtureManifest(mod);
        var content = f.Content with
        {
            Sources = f.Content.Sources.Append(new ActiveContentSource("high", "High Mod", kind, mod, 2000)).ToArray()
        };
        var heroCatalog = HeroClassCatalog.Load(content);
        var quirk = heroCatalog.InitialQuirks.Single();
        var trinket = TrinketCatalog.Load(content).Trinkets.Single();
        Assert(quirk.Id == "BE" && quirk.SourcePath == last && GenerateQuirkRuleHero(heroCatalog, "BE").Preview.CurrentHp == 20,
            "Replacing the earlier file retains its native slot; the later base quirk wins, not the highest-priority Mod.");
        Assert(trinket.Id == "Az" && trinket.QuestUses == 2 && trinket.SourcePath == Path.GetFullPath(Path.Combine(f.Source, DependencyFirstPath)),
            "A later high-priority Mod alias does not replace the first base trinket.");
        Assert(quirk.AllSources.Contains("high") && trinket.AllSources.Contains("high"),
            "Definition provenance still includes both providers.");
        var language = EditorText.Culture.Name;
        var system = EditorText.SystemCulture;
        try
        {
            foreach (var culture in new[] { "en", "zh-CN" })
            {
                EditorText.Initialize(culture, system);
                var expected = culture == "en" ? "Base game" : "原版";
                Assert(quirk.SourceLabel == expected && trinket.SourceLabel == expected,
                    "Source labels preserve the effective definition instead of reapplying Mod priority, in either language.");
            }
        }
        finally { EditorText.Initialize(language, system); }
        Console.WriteLine($"PASS: {kind}/native hash overlay slots and source labels use the effective definition in both languages.");
    }
}
