internal static partial class ContractSuite
{
    private static void RunRealModLocalizationContracts(string runRoot)
    {
        var schineseProbeRoot = Environment.GetEnvironmentVariable("DDSE_SCHINESE_LOC_PROBE_MOD_ROOT");
        if (!string.IsNullOrWhiteSpace(schineseProbeRoot))
        {
            var fullRoot = Path.GetFullPath(schineseProbeRoot);
            var locPath = Path.Combine(fullRoot, "localization/1143685298_schinese.loc");
            var originalHash = SHA256.HashData(File.ReadAllBytes(locPath));
            var binary = ReadCompiledLocalizationProbe(locPath, ["hero_class_name_maiden", "hero_class_name_zenith"], legacy: true);
            Assert(binary.Names["hero_class_name_maiden"] == "少女" && binary.Names["hero_class_name_zenith"] == "天顶枪兵" &&
                   binary.Issues.Count == 1 && binary.Issues.Single().Contains("跳过 43 个", StringComparison.Ordinal) &&
                   SHA256.HashData(File.ReadAllBytes(locPath)).SequenceEqual(originalHash),
                "The real Chinese LOC must retain known valid names while skipping 43 invalid text values without touching the source.");
            var profile = new SaveProfile("schinese_loc_probe", runRoot,
                Path.Combine(runRoot, "unused.schinese.persist.estate.json"), "contract-user", DateTime.UtcNow);
            var content = new ActiveContentSnapshot(profile, "base",
                [new ActiveContentSource("workshop:schinese-probe", "Chinese LOC Probe", "workshop", fullRoot, 0)],
                [], runRoot, string.Empty, 1, string.Empty);
            var catalog = ReadLocalizationProbe(content, ["hero_class_name_maiden", "hero_class_name_zenith"]);
            Assert(catalog.Names["hero_class_name_maiden"] == new BilingualContentName("少女", "") &&
                   catalog.Names["hero_class_name_zenith"] == new BilingualContentName("天顶枪兵", "") &&
                   catalog.Issues.Any(issue => issue.Contains("跳过 43 个", StringComparison.Ordinal)),
                "The real manifest-constrained shared catalog must display valid Chinese names without inventing English translations.");
            Console.WriteLine("PASS: read-only Chinese LOC partial-text probe (43 skipped values, valid names retained).");
        }

        var rulerProbeModRoot = Environment.GetEnvironmentVariable("DDSE_RULER_LOC_PROBE_MOD_ROOT");
        if (!string.IsNullOrWhiteSpace(rulerProbeModRoot))
        {
            var fullProbeRoot = Path.GetFullPath(rulerProbeModRoot);
            Assert(Directory.Exists(fullProbeRoot), "DDSE_RULER_LOC_PROBE_MOD_ROOT must point to the existing Ruler Mod directory.");
            var profile = new SaveProfile("ruler_loc_probe", runRoot,
                Path.Combine(runRoot, "unused.ruler.persist.estate.json"), "contract-user", DateTime.UtcNow);
            var content = new ActiveContentSnapshot(profile, "base",
                [new ActiveContentSource("workshop:ruler-probe", "Ruler LOC Probe", "workshop", fullProbeRoot, 0)],
                [], runRoot, string.Empty, 1, string.Empty);
            var namesBefore = Directory.EnumerateFiles(Path.Combine(fullProbeRoot, "localization"), "*", SearchOption.TopDirectoryOnly)
                .Append(Path.Combine(fullProbeRoot, "modfiles.txt"))
                .ToDictionary(path => path, path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
            var catalog = HeroClassCatalog.Load(content);
            Assert(catalog.HeroClasses.Single(hero => hero.Id == "JoanofArc").LocalizedName == new BilingualContentName("Ruler", "Ruler") &&
                   catalog.Issues.All(issue => !issue.Contains("Failed to read localization", StringComparison.Ordinal)),
                "Ruler's manifest-listed legacy LOC files must supply Ruler / Ruler through the real hero catalog.");
            var inventory = ContentFileInventory.Scan(content);
            Assert(inventory.Mods.Single().Files.Single(file => file.RelativePath == "localization/JoanofArc.string_table.xml").ManifestMatch == ContentManifestMatch.Unlisted &&
                   namesBefore.All(pair => pair.Value == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(pair.Key)))),
                "The Ruler probe must use the existing manifest without listing, modifying, or relying on the unlisted XML source.");
            Console.WriteLine("PASS: read-only Ruler legacy LOC probe (JoanofArc = Ruler / Ruler).");
        }

        var loc2ProbeModRoot = Environment.GetEnvironmentVariable("DDSE_LOC2_PROBE_MOD_ROOT");
        if (!string.IsNullOrWhiteSpace(loc2ProbeModRoot))
        {
            var fullProbeRoot = Path.GetFullPath(loc2ProbeModRoot);
            Assert(Directory.Exists(fullProbeRoot), "DDSE_LOC2_PROBE_MOD_ROOT must point to an existing Mod directory.");
            var probeProfile = new SaveProfile(
                "loc2_probe",
                runRoot,
                Path.Combine(runRoot, "unused.persist.estate.json"),
                "contract-user",
                DateTime.UtcNow);
            var probeSnapshot = new ActiveContentSnapshot(
                probeProfile,
                "base",
                [new ActiveContentSource("local:loc2-probe", "LOC2 Probe", "local", fullProbeRoot, 0)],
                [],
                runRoot,
                string.Empty,
                1,
                string.Empty);
            var probeCatalog = TrinketCatalog.Load(probeSnapshot);
            var expectedProbeIds = Enumerable.Range(1, 9)
                .Select(index => $"EosNyx_Trinket{index}")
                .Append("EosNyx_TrinketC")
                .Concat(["Grandmaster_Trinket1", "Grandmaster_Trinket2"])
                .ToArray();
            Assert(
                expectedProbeIds.All(id => probeCatalog.Trinkets.Any(item =>
                    item.Id == id &&
                    !string.IsNullOrWhiteSpace(item.LocalizedName.Chinese) &&
                    !string.IsNullOrWhiteSpace(item.LocalizedName.English))),
                "The real LOC2 probe should resolve all twelve Eos_Nyx trinkets in both languages.");
            Assert(
                probeCatalog.Trinkets.Single(item => item.Id == "EosNyx_Trinket1").LocalizedName ==
                    new BilingualContentName("血循环稳定戒指", "Ring of Circulation Stabilization"),
                "The real LOC2 probe should resolve the known Eos_Nyx trinket name exactly.");
        }

        var rurutiaProbeModRoot = Environment.GetEnvironmentVariable("DDSE_RURUTIA_LOC2_PROBE_MOD_ROOT");
        if (!string.IsNullOrWhiteSpace(rurutiaProbeModRoot))
        {
            var fullProbeRoot = Path.GetFullPath(rurutiaProbeModRoot);
            Assert(
                Directory.Exists(fullProbeRoot),
                "DDSE_RURUTIA_LOC2_PROBE_MOD_ROOT must point to an existing Mod directory.");
            var probeProfile = new SaveProfile(
                "rurutia_loc2_probe",
                runRoot,
                Path.Combine(runRoot, "unused.rurutia.persist.estate.json"),
                "contract-user",
                DateTime.UtcNow);
            var probeSnapshot = new ActiveContentSnapshot(
                probeProfile,
                "base",
                [new ActiveContentSource("local:rurutia-loc2-probe", "Rurutia LOC2 Probe", "local", fullProbeRoot, 0)],
                [],
                runRoot,
                string.Empty,
                1,
                string.Empty);
            var probeCatalog = TrinketCatalog.Load(probeSnapshot);
            var expectedProbeIds = Enumerable.Range(1, 9)
                .Select(index => $"Rurutia_{index}")
                .ToArray();
            Assert(
                expectedProbeIds.All(id => probeCatalog.Trinkets.Any(item =>
                    item.Id == id && !string.IsNullOrWhiteSpace(item.LocalizedName.Chinese))),
                "The real Rurutia LOC2 probe should resolve all nine Simplified Chinese trinket names.");
            Assert(
                expectedProbeIds.Take(8).All(id => probeCatalog.Trinkets.Any(item =>
                    item.Id == id && !string.IsNullOrWhiteSpace(item.LocalizedName.English))),
                "The real Rurutia LOC2 probe should resolve every English trinket name actually supplied by the Mod.");
            Assert(
                probeCatalog.Trinkets.Single(item => item.Id == "Rurutia_1").LocalizedName ==
                    new BilingualContentName("黑塔巫师", "Rurutia's Cutlass") &&
                probeCatalog.Trinkets.Single(item => item.Id == "Rurutia_9").LocalizedName ==
                    new BilingualContentName("塔", string.Empty),
                "The real Rurutia LOC2 probe should preserve exact supplied names without inventing the missing ninth English name.");
            Assert(
                probeCatalog.Issues.All(issue =>
                    !issue.Contains("Failed to read localization", StringComparison.Ordinal)),
                "The real Rurutia LOC2 files should not be rejected for cross-string colour controls.");
        }

    }
}
