internal static partial class ContractSuite
{
    private static async Task VerifyHeroSkinDirectoriesAsync(string runRoot, DsonSaveCodec codec)
    {
        foreach (var kind in QuirkRuleSourceKinds)
        foreach (var test in new[] { "AB", "AC", "C-only", "skin-only", "split-info-art", "unlisted" })
        {
            var f = HeroSelectionContent(Path.Combine(runRoot, "hero-skin", kind, test), kind);
            foreach (var letter in test switch { "AB" => new[] { "A", "B" }, "AC" => new[] { "A", "C" }, "C-only" => new[] { "C" }, _ => new[] { "A" } })
                SelectionResource(f, $"heroes/selection/selection_{letter}/anim/idle.png", "fixture");
            var content = f.Content;
            var other = Path.Combine(f.Root, "skin-mod");
            if (test is "skin-only" or "split-info-art" or "unlisted")
            {
                WriteMultiMash(other, "heroes/selection/selection_B/anim/idle.png", "fixture");
                var listed = test == "unlisted" ? new List<string>() : ["heroes/selection/selection_B/anim/idle.png"];
                if (test == "split-info-art")
                {
                    WriteMultiMash(other, "heroes/selection/selection.art.darkest", "commonfx: .deathfx death_medium\n");
                    listed.Add("heroes/selection/selection.art.darkest");
                }
                File.WriteAllLines(Path.Combine(other, "modfiles.txt"), listed);
                content = content with { Sources = content.Sources.Concat(new[] { new ActiveContentSource("skin-only", "Skin-only", "local", other, -1) }).ToArray() };
            }
            var expected = test is "C-only" or "unlisted" ? 1 : 2;
            var catalog = HeroClassCatalog.Load(content);
            Assert(catalog.Issues.Count == 0 && catalog.HeroClasses.Single().ColourVariationCount == expected &&
                   catalog.HeroClasses.Single().GenerationAvailability.All(level => level.CanGenerate),
                $"{kind}/{test}: count all distinct mounted skins without requiring continuous letters or an info/art provider.");
            var candidates = Enumerable.Range(0, 64).Select(seed => GenerateSelectionHero(catalog, seed)).ToArray();
            Assert(candidates.Select(c => c.Preview.ColourVariation).Distinct().Order().SequenceEqual(Enumerable.Range(0, expected)),
                $"{kind}/{test}: random indices cover the complete directory list, not alphabetical offsets.");
            if (kind == "local") await VerifyHeroSelectionPersistenceAsync(f.Root, candidates[0], codec);
            if (test == "skin-only")
            {
                var before = ProfileCatalogContentFingerprint.Capture(content.Sources);
                WriteMultiMash(other, "heroes/selection/selection_C/anim/idle.png", "fixture");
                File.AppendAllText(Path.Combine(other, "modfiles.txt"), "\nheroes/selection/selection_C/anim/idle.png\n");
                Assert(before != ProfileCatalogContentFingerprint.Capture(content.Sources) &&
                       HeroClassCatalog.Load(content).HeroClasses.Single().ColourVariationCount == 3,
                    "An independent skin Mod update must refresh the fingerprint and the actual catalogue count.");
            }
        }
        Console.WriteLine("PASS: 36 mounted skin cases, sparse letters, standalone/art-only providers, manifest exclusion, preview and DSON persistence.");

        foreach (var kind in QuirkRuleSourceKinds)
        foreach (var test in new[] { "non-png", "prefixed-name", "lower-letter", "upper-id", "name-suffix", "nested",
                     "upper-root", "upper-class", "dot-prefix", "template-prefix", "missing-file", "virtual-only", "empty-directory", "disabled-dlc" })
        {
            var f = HeroSelectionContent(Path.Combine(runRoot, "hero-skin-query", kind, test), kind);
            SelectionResource(f, "heroes/selection/selection_A/anim/idle.png", "fixture");
            var before = ProfileCatalogContentFingerprint.Capture(f.Content.Sources);
            var relative = test switch
            {
                "prefixed-name" => "heroes/selection/pack_selection_B/asset.txt",
                "lower-letter" => "heroes/selection/selection_b/asset.txt",
                "upper-id" => "heroes/selection/Selection_B/asset.txt",
                "name-suffix" => "heroes/selection/selection_B_extra/asset.txt",
                "nested" => "heroes/selection/notes/selection_B/asset.txt",
                "upper-root" => "Heroes/selection/selection_B/asset.txt",
                "upper-class" => "heroes/Selection/selection_B/asset.txt",
                "dot-prefix" => "heroes/selection/.copy_selection_B/asset.txt",
                "template-prefix" => "heroes/selection/_template_selection_B/asset.txt",
                "disabled-dlc" => "dlc/not_enabled/heroes/selection/selection_B/asset.txt",
                _ => "heroes/selection/selection_B/asset.txt"
            };
            if (test == "virtual-only")
            {
                if (f.IsMod) File.AppendAllText(Path.Combine(f.Source, "modfiles.txt"), f.Prefix + relative + "\n");
            }
            else if (test == "empty-directory")
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(f.Source, f.Prefix + relative))!);
            else
            {
                var path = SelectionResource(f, relative, "fixture");
                if (test == "missing-file") File.Delete(path);
            }
            var accepted = test switch
            {
                "lower-letter" or "upper-id" or "name-suffix" or "nested" or "disabled-dlc" => false,
                "upper-root" or "upper-class" or "empty-directory" => !f.IsMod,
                "dot-prefix" or "template-prefix" or "virtual-only" => f.IsMod,
                _ => true
            };
            var catalog = HeroClassCatalog.Load(f.Content);
            Assert(catalog.HeroClasses.Single().ColourVariationCount == (accepted ? 2 : 1),
                $"{kind}/{test}: use the native directory query, manifest tree and filename regex.");
            if (accepted) Assert(before != ProfileCatalogContentFingerprint.Capture(f.Content.Sources),
                $"{kind}/{test}: every effective skin-list addition must be observed without requiring a PNG.");
        }
        Console.WriteLine("PASS: 84 skin query cases covering physical/manifest case, regex, depth, empty/virtual directories and DLC scope.");

        var duplicate = HeroSelectionContent(Path.Combine(runRoot, "skin-mount-dedup"), "dlc-mod");
        SelectionResource(duplicate, "heroes/selection/selection_C/idle.png", "fixture");
        var rootSkin = WriteMultiMash(duplicate.Source, "heroes/selection/selection_C/root.png", "fixture");
        File.AppendAllText(Path.Combine(duplicate.Source, "modfiles.txt"), "heroes/selection/selection_C/root.png\nheroes/selection/selection_C/root.png\n");
        var feature = duplicate.Content.Sources.Single(source => source.Kind == "dlc-feature");
        WriteMultiMash(feature.Directory, "heroes/selection/selection_C/dlc.png", "fixture");
        var dedupCatalog = HeroClassCatalog.Load(duplicate.Content);
        Assert(File.Exists(rootSkin) && dedupCatalog.HeroClasses.Single().ColourVariationCount == 1,
            "Root, enabled DLC alias, official DLC and duplicate manifest entries of the same skin must share one slot.");

        var none = HeroSelectionContent(Path.Combine(runRoot, "skin-no-directory"), "local");
        var unavailable = HeroClassCatalog.Load(none.Content).HeroClasses.Single();
        Assert(unavailable.ColourVariationCount == 0 && unavailable.GenerationAvailability.All(level => !level.CanGenerate &&
               level.UnavailableReason.Contains("皮肤", StringComparison.Ordinal)), "A genuinely missing skin list still blocks generation.");

        foreach (var kind in QuirkRuleSourceKinds)
        {
            var f = HeroSelectionContent(Path.Combine(runRoot, "skin-case-slots", kind), kind);
            SelectionResource(f, "heroes/selection/Pack_selection_C/skin.png", "fixture");
            SelectionResource(f, "heroes/selection/pack_selection_C/skin.png", "fixture");
            var withinSource = HeroClassCatalog.Load(f.Content);
            Assert(withinSource.HeroClasses.Single().ColourVariationCount == (f.IsMod ? 2 : 1),
                $"{kind}: raw manifest names remain distinct, while a physical case alias is one actual directory.");
            var other = Path.Combine(f.Root, "other-skins");
            WriteMultiMash(other, "heroes/selection/pack_selection_C/skin.png", "fixture");
            var content = f.Content with { Sources = f.Content.Sources.Append(new ActiveContentSource("other-skins", "Other skins", "base", other, 2000)).ToArray() };
            Assert(HeroClassCatalog.Load(content).HeroClasses.Single().ColourVariationCount == 2,
                $"{kind}: differently cased directory names from separate sources retain two native positions.");
            var identical = Path.Combine(f.Root, "same-skin");
            WriteMultiMash(identical, "heroes/selection/Pack_selection_C/skin.png", "fixture");
            content = content with { Sources = content.Sources.Append(new ActiveContentSource("same-skin", "Same skin", "base", identical, 3000)).ToArray() };
            var merged = HeroClassCatalog.Load(content);
            Assert(merged.HeroClasses.Single().ColourVariationCount == 2 && Enumerable.Range(0, 64)
                    .Select(seed => GenerateSelectionHero(merged, seed).Preview.ColourVariation).Distinct().Order().SequenceEqual([0, 1]),
                $"{kind}: exact spelling duplicates still share one slot; both native case-distinct slots are selectable.");
        }
        Console.WriteLine("PASS: mounted directory deduplication, 18 case-sensitive slot cases and absent-skin safeguard.");
    }
}
