internal static partial class ContractSuite
{
    private static void VerifyManifestSkinSelection(ActiveContentSnapshot content, string modRoot)
    {
        var manifest = Path.Combine(modRoot, "modfiles.txt");
        var original = File.ReadAllBytes(manifest);
        var skin = Path.Combine(modRoot, "heroes", "local_hero", "local_hero_B", "skin.png");
        var skinBytes = File.ReadAllBytes(skin);
        try
        {
            File.WriteAllLines(manifest, File.ReadAllLines(manifest)
                .Where(line => !line.Contains("local_hero_B/", StringComparison.Ordinal)));
            var unlisted = HeroClassCatalog.Load(content);
            var hero = unlisted.HeroClasses.Single(item => item.Id == "local_hero");
            Assert(File.Exists(skin) && hero.ColourVariationCount == 1,
                "A physical but unlisted B skin must not become an eligible Mod colour variation.");
            Assert(StagecoachHeroCandidateFactory.Generate(unlisted, hero, 1729, 0, []).Preview.ColourVariation == 0,
                "Candidate generation must not pick an unlisted skin.");

            File.WriteAllBytes(manifest, original);
            var before = ProfileCatalogContentFingerprint.Capture(content.Sources);
            File.Delete(skin);
            var after = ProfileCatalogContentFingerprint.Capture(content.Sources);
            Assert(before == after, "Removing a texture without changing the virtual directory tree does not change the skin selection range.");
            Assert(HeroClassCatalog.Load(content).HeroClasses.Single(item => item.Id == "local_hero").ColourVariationCount == 2,
                "A missing physical payload must not erase the skin slot still present in the manifest directory tree.");
        }
        finally
        {
            File.WriteAllBytes(skin, skinBytes);
            File.WriteAllBytes(manifest, original);
        }
        Assert(HeroClassCatalog.Load(content).HeroClasses.Single(item => item.Id == "local_hero").ColourVariationCount == 2,
            "The A/B directory selection range is independent of texture payload availability.");
        Console.WriteLine("PASS: manifest-only hero skin directory selection, independent of texture payload existence");
    }
}
