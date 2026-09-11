internal static partial class ContractSuite
{
    private static async Task VerifyEncounterDirectoryCaseAsync(string runRoot)
    {
        foreach (var kind in new[] { "base", "mode", "dlc-feature", "local", "workshop", "dlc-mod" })
        foreach (var (table, folder, label) in new[]
                 { ("cove", "cove", "lowercase"), ("cove", "Cove", "capitalized"), ("MistyGrove", "mIsTyGrOvE", "custom-case"),
                   ("cove", "Cove", "manifest-path-alias") })
        {
            if (label == "manifest-path-alias" && kind is not ("local" or "workshop")) continue;
            var root = Path.Combine(runRoot, "encounter-directory-case", kind, label);
            var source = Path.Combine(root, "source");
            var prefix = kind == "dlc-mod" ? "dlc/rq_feature/" : "";
            var first = $"dungeons/{folder}/a.{table}.2.mash.darkest";
            var second = $"dungeons/{folder}/b.{table}X2.mashYdarkest";
            WriteMultiMash(source, prefix + first, QueryMashes("alpha_A"));
            WriteMultiMash(source, prefix + second, QueryMashes("bravo_A"));
            WriteMultiMash(source, prefix + $"dungeons/{folder}/{table}.conditional.2.mash.darkest", QueryMashes("alpha_A"));
            // These filenames refer to a DIFFERENT case-sensitive table ID.
            // Windows directory matching must not relax the requested query.
            WriteMultiMash(source, prefix + $"dungeons/{folder}/0.{table.ToUpperInvariant()}.2.mash.darkest", QueryMashes("alpha_A"));
            foreach (var id in new[] { "alpha_A", "bravo_A", "charlie_A" })
                WriteMultiMash(source, prefix + $"monsters/{id[..^2]}/{id}/{id}.info.darkest", "display: .size 1\n");
            var sources = QuerySources(source, kind);
            if (kind == "dlc-mod")
            {
                WriteMultiMash(sources[1].Directory, first, QueryMashes("alpha_A"));
                WriteMultiMash(sources[1].Directory, second, QueryMashes("bravo_A"));
            }
            if (kind is "local" or "workshop" or "dlc-mod")
            {
                WriteFixtureManifest(source);
                if (label == "manifest-path-alias")
                {
                    var manifest = Path.Combine(source, "modfiles.txt");
                    File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("dungeons/Cove/", "dungeons/cove/", StringComparison.Ordinal));
                }
            }
            Assert(Directory.Exists(Path.Combine(source, prefix + "dungeons/" + table)),
                "The requested table directory must resolve the physical Windows directory alias.");
            var later = Path.Combine(root, "later");
            WriteMultiMash(later, $"dungeons/{table}/c.{table}.2.mash.darkest", QueryMashes("charlie_A"));
            WriteFixtureManifest(later);
            var content = QueryContent(root, sources.Append(new ActiveContentSource("local:case-later", "Later", "local", later, -1000)).ToArray());
            var map = new BattleMapSnapshot(content.Profile.ProfileDirectory, "", "", "", "", table, 2, 1,
                null, null, null, null, null, null, null, null, null, false, [], [], [], DateTime.UtcNow);
            var catalog = BattleEncounterCatalog.Load(content, map);
            var excludedModDirectory = kind is "local" or "workshop" && folder != table && label != "manifest-path-alias";
            var expectedIds = excludedModDirectory ? new[] { "charlie_A" } : new[] { "alpha_A", "bravo_A", "charlie_A" };
            var excludedConditionalDirectory = kind is "local" or "workshop" or "dlc-mod" && folder != table && label != "manifest-path-alias";
            Assert(catalog.SpecialEncounters.Count == (excludedConditionalDirectory ? 0 : 3),
                "Separate conditional collections must respect the same source-specific directory query.");
            foreach (var type in new[] { 0, 1, 2 })
            {
                var rows = catalog.Encounters.Where(row => row.SourceKind == BattleEncounterSourceKind.Standard && row.MashType == type).ToArray();
                Assert(rows.Length == expectedIds.Length &&
                       rows.Select(row => row.MashIndex).SequenceEqual(Enumerable.Range(0, expectedIds.Length).Select(index => (int?)index)) &&
                       rows.Select(row => row.MonsterIds.Single()).SequenceEqual(expectedIds),
                    $"{kind}/{label}/{type}: Windows directory aliases and case-sensitive manifest trees must each select the native table.");
                Assert(rows.All(row => row.OriginDungeonId == table), "Table identities must preserve their authored case, including custom regions.");
                BattleEncounterCatalog.ValidateDirectEncounter(rows.Last());
                var global = catalog.BridgeEncounters.Single(row => row.SourcePath == rows[0].SourcePath && row.MashType == type);
                BattleEncounterCatalog.ValidateBridgeEncounter(global);
                Assert(BattleEncounterCatalog.ResolveAppendTarget(catalog, type).NextMashIndex == expectedIds.Length &&
                       BattleEncounterCatalog.ReadMaintenanceTable(content, table, 2, type).Count == expectedIds.Length,
                    "Direct/global/Bridge/maintenance must agree on region case and the same table length.");
            }
            var before = BattleEncounterCatalog.CaptureContentFingerprint(content.Sources);
            if (excludedModDirectory)
            {
                WriteMultiMash(source, prefix + first, QueryMashes("bravo_A"));
                Assert(BattleEncounterCatalog.CaptureContentFingerprint(content.Sources) == before,
                    "A manifest directory outside the requested table must not change the encounter inventory.");
                BattleEncounterCatalog.ValidateDirectEncounter(catalog.DirectEncounters.Last());
            }
            File.WriteAllText(catalog.Encounters.First(row => row.SourceKind == BattleEncounterSourceKind.Standard).SourcePath, QueryMashes("bravo_A"));
            Assert(BattleEncounterCatalog.CaptureContentFingerprint(content.Sources) != before &&
                   BattleEncounterCatalog.ReadMaintenanceTable(content, table, 2, 0)[0].MonsterIds.Single() == "bravo_A",
                $"{kind}/{label}: changes to a directory alias must refresh the encounter fingerprint and maintenance table.");
            Assert(await CaptureSaveFailureAsync(() => {
                BattleEncounterCatalog.ValidateDirectEncounter(catalog.DirectEncounters.Last()); return Task.CompletedTask;
            }) is InvalidOperationException, "A prior choice must not pass validation after its predecessor table changes.");
        }
    }
}
