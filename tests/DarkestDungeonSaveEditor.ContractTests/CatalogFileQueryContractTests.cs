internal static partial class ContractSuite
{
    private static async Task VerifyCatalogFileQueriesAsync(ActiveContentSnapshot original, HeroClassDefinition originalHero,
        string runRoot, DsonSaveCodec codec)
    {
        var root = Path.Combine(runRoot, "catalog-file-queries");
        foreach (var kind in new[] { "base", "mode", "dlc-feature", "local", "workshop", "dlc-mod" })
        {
            var sourceRoot = Path.Combine(root, kind);
            var prefix = kind == "dlc-mod" ? "dlc/rq_feature/" : "";
            void Write(string path, string text) => WriteMultiMash(sourceRoot, prefix + path, text);
            Write("heroes/rq/rq.info.darkest", "armour: .name rq.armour.0 .hp 20\n");
            Write("shared/buffs/a.buffs.json", QueryBuff(.25));
            Write("shared/buffs/z.buffsXjson", QueryBuff(.75));
            Write("shared/quirk/rq.quirk_libraryXjson", """{"quirks":[{"id":"rq_hp","is_positive":true,"buffs":["rq_hp"]}]}""");
            Write("raid/camping/camping_skills.json", """{"skills":[{"id":"rq_bare","hero_classes":["rq"]}]}""");
            Write("raid/camping/rq.camping_skillsXjson", """{"skills":[{"id":"rq_dot","hero_classes":["rq"]}]}""");
            Write("trinkets/rq.entriesXtrinketsYjson", """{"entries":[{"id":"rq_trinket","quest_uses":2}]}""");
            Write("trinkets/zz.entries.trinkets.json", """{"entries":[{"id":"rq_trinket","quest_uses":9}]}""");
            // Native queries require the literal leading dot for Buffs/trinkets;
            // upgrades escape both dots, and correct suffixes need correct roots.
            foreach (var ignored in new[] { "trinkets/rqXentries.trinkets.json", "shared/buffs/rqXbuffs.json",
                "shared/quirk/notes.json", "raid/camping/notes.json", "upgrades/rq.upgradesXjson",
                "heroes/rq.quirk_library.json", "heroes/camping_skills.json" }) Write(ignored, "{ broken");
            WriteMultiMash(sourceRoot, "dlc/disabled/trinkets/hidden.entries.trinkets.json", """{"entries":[{"id":"rq_hidden"}]}""");
            if (kind is "local" or "workshop" or "dlc-mod")
            {
                WriteFixtureManifest(sourceRoot);
                Write("trinkets/unlisted.entries.trinkets.json", """{"entries":[{"id":"rq_unlisted"}]}""");
            }
            var sources = QuerySources(sourceRoot, kind);
            var content = original with { Sources = sources };
            var heroes = HeroClassCatalog.Load(content);
            var trinkets = TrinketCatalog.Load(content);
            Assert(heroes.InitialQuirks.Single(q => q.Id == "rq_hp").MaxHpModifiers.Single().Amount == .75 &&
                   heroes.HeroClasses.Single(h => h.Id == "rq").SharedCampingSkillIds.Order().SequenceEqual(new[] { "rq_bare", "rq_dot" }) &&
                   trinkets.Trinkets.Count == 1 && trinkets.Trinkets.Single() is { Id: "rq_trinket", QuestUses: 2 },
                $"{kind}: native resource queries must accept bare camping/nonliteral dots, retaining Buff-last and trinket-first definition rules.");
            Assert(!heroes.Issues.Concat(trinkets.Issues).Any(i => i.Contains("{ broken") || i.Contains("notes.json") ||
                   i.Contains("rqXbuffs.json") || i.Contains("rqXentries.trinkets.json") || i.Contains("rq.upgradesXjson")),
                $"{kind}: files outside the native query must not be parsed or poison the catalogs.");

            var before = ProfileCatalogContentFingerprint.Capture(sources);
            var buffPath = Path.Combine(sourceRoot, prefix + "shared/buffs/z.buffsXjson");
            var previousTime = File.GetLastWriteTimeUtc(buffPath);
            Write("shared/buffs/z.buffsXjson", QueryBuff(.25));
            File.SetLastWriteTimeUtc(buffPath, previousTime);
            Assert(ProfileCatalogContentFingerprint.Capture(sources) != before &&
                   HeroClassCatalog.Load(content).InitialQuirks.Single(q => q.Id == "rq_hp").MaxHpModifiers.Single().Amount == .25,
                $"{kind}: a consumed nonliteral JSON name must refresh with unchanged size, timestamp and manifest.");

            var overlayRoot = Path.Combine(root, kind + "-overlay");
            WriteMultiMash(overlayRoot, prefix + "shared/buffs/z.buffsXjson", QueryBuff(.875));
            WriteFixtureManifest(overlayRoot);
            var overlayContent = content with { Sources = sources.Append(
                new ActiveContentSource("local:rq-overlay", "Higher Mod", "local", overlayRoot, -1000)).ToArray() };
            Assert(HeroClassCatalog.Load(overlayContent).InitialQuirks.Single(q => q.Id == "rq_hp").MaxHpModifiers.Single().Amount == .875,
                $"{kind}: new query eligibility must preserve higher-Mod same-path replacement.");
        }

        foreach (var eligible in new[] { false, true })
        {
            var missing = Path.Combine(root, "missing-" + eligible);
            Directory.CreateDirectory(missing);
            File.WriteAllText(Path.Combine(missing, "modfiles.txt"), eligible
                ? "trinkets/missing.entries.trinketsXjson\nshared/buffs/missing.buffsXjson\nshared/quirk/missing.quirk_libraryXjson\nraid/camping/camping_skills.json\n"
                : "trinkets/notes.json\nshared/buffs/notes.json\nshared/quirk/notes.json\nraid/camping/notes.json\nheroes/camping_skills.json\n");
            var content = original with { Sources = [new("local:rq-missing", "Missing", "local", missing, 0)] };
            Assert(TrinketCatalog.Load(content).Issues.Count(i => i.Contains("Trinket file listed by Mod is missing")) == (eligible ? 1 : 0) &&
                   HeroClassCatalog.Load(content).Issues.Count(i => i.Contains("Hero catalog file listed by Mod is missing")) == (eligible ? 3 : 0),
                "Missing-file diagnostics must share catalog query eligibility, including the newly accepted filenames.");
        }

        var hpRoot = Path.Combine(root, "hp-generation");
        WriteMultiMash(hpRoot, "shared/buffs/a.rq.buffs.json", QueryBuff(.25));
        WriteMultiMash(hpRoot, "shared/buffs/z.rq.buffsXjson", QueryBuff(.75));
        WriteMultiMash(hpRoot, "shared/quirk/rq.quirk_libraryXjson", """{"quirks":[{"id":"rq_hp","is_positive":true,"buffs":["rq_hp"]}]}""");
        WriteFixtureManifest(hpRoot);
        var hpContent = original with { Sources = original.Sources.Append(
            new ActiveContentSource("local:rq-hp", "Query HP", "local", hpRoot, -2600)).ToArray() };
        var hpCatalog = HeroClassCatalog.Load(hpContent);
        var candidate = StagecoachHeroCandidateFactory.Generate(hpCatalog,
            hpCatalog.HeroClasses.Single(h => h.Id == originalHero.Id), 1729, ["rq_hp"]);
        Assert(candidate.Preview.CurrentHp == 35, "The replacement Buff must reach generated hero HP, not only the catalog.");
        var mutation = StagecoachHeroSaveEditor.AddCandidate(
            JsonNode.Parse("""{"base_root":{"buildings":{"stage_coach":{"store":{"hero_recruit":{"generated":{}}}}}}}""")!.AsObject(),
            JsonNode.Parse("""{"base_root":{"nextGuid":950,"heroes":{}}}""")!.AsObject(),
            JsonNode.Parse("""{"base_root":{"purchases":{}}}""")!.AsObject(), candidate.Candidate, candidate.UpgradePurchases);
        var proposed = Path.Combine(hpRoot, "town.proposed.json"); var binary = Path.Combine(hpRoot, "town.dson");
        var restored = Path.Combine(hpRoot, "town.roundtrip.json");
        File.WriteAllText(proposed, mutation.UpdatedTown.ToJsonString());
        await codec.EncodeAsync(proposed, binary, null); await codec.DecodeAsync(binary, restored);
        Assert(JsonNode.Parse(File.ReadAllText(restored))!["base_root"]!["buildings"]!["stage_coach"]!["store"]!["hero_recruit"]!
                   ["generated"]!["950"]!["actor"]!["current_hp"]!.GetValue<double>() == 35,
            "HP from the newly consumed Buff must survive the actual town DSON roundtrip.");
        Console.WriteLine("PASS: native catalog filename queries across six source types, same-path priority, missing files, refresh and generated HP DSON.");
    }

    private static string QueryBuff(double amount) => new JsonObject { ["buffs"] = new JsonArray(new JsonObject
    {
        ["id"] = "rq_hp", ["stat_type"] = "combat_stat_multiply", ["stat_sub_type"] = "max_hp", ["amount"] = amount,
        ["rule_type"] = "always", ["is_false_rule"] = false
    }) }.ToJsonString();

    private static IReadOnlyList<ActiveContentSource> QuerySources(string sourceRoot, string kind)
    {
        var resource = new ActiveContentSource("query:resource", "Query resources", kind == "dlc-mod" ? "local" : kind, sourceRoot, 1000)
        { VirtualPathPrefix = kind == "dlc-feature" ? "dlc/rq_feature" : "" };
        if (kind != "dlc-mod") return [resource];
        var featureRoot = Path.Combine(sourceRoot, "enabled-feature"); Directory.CreateDirectory(featureRoot);
        return [resource, new("dlc:rq", "Query DLC", "dlc-feature", featureRoot, 0) { VirtualPathPrefix = "dlc/rq_feature" }];
    }
}
