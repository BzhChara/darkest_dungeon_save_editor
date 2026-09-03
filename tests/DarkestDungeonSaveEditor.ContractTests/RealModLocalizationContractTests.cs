internal static partial class ContractSuite
{
    private static void RunRealModLocalizationContracts(string runRoot)
    {
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
