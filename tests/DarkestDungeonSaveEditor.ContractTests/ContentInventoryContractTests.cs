using System.Text.Json;

internal static partial class ContractSuite
{
    private static void RunContentInventoryContracts(ActiveContentSnapshot activeContent, ContractFixture fixture, string repositoryRoot)
    {
        var root = Path.Combine(fixture.RunRoot, "file-inventory");
        var manifestRoot = Path.Combine(root, "manifested");
        var fallbackRoot = Path.Combine(root, "no-manifest");
        const string enabled = "dlc/100_feature_pack/features/enabled_feature";
        const string disabled = "dlc/100_feature_pack/features/disabled_feature";
        void Write(string directory, string relative, string text)
        {
            var path = Path.Combine(directory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text, new UTF8Encoding(false));
        }

        const string listedPath = "trinkets/listed probe.entries.trinkets.json";
        Write(manifestRoot, listedPath, """{"entries":[{"id":"inventory_listed","rarity":"common","price":20}]}""");
        Write(manifestRoot, "trinkets/extra.entries.trinkets.json", """{"entries":[{"id":"inventory_unlisted","rarity":"common"}]}""");
        Write(manifestRoot, "heroes/space name/space name.info.darkest", "stats: .hp 12");
        Write(manifestRoot, "inventory/no size.inventory.items.darkest", "inventory_item: .type estate .id inventory_probe .base_stack_limit 1");
        Write(manifestRoot, "localization/new.string_table.xml",
            "<root><language id=\"schinese\"><entry id=\"str_inventory_title_trinketinventory_listed\">补读译文</entry></language></root>");
        Write(manifestRoot, "localization/unused/old.string_table.xml", "<root/>");
        Write(manifestRoot, "preview.png", "not decoded by inventory");
        Write(manifestRoot, "auto_balance.py", "raise RuntimeError('must never be executed')");
        foreach (var prefix in new[] { enabled, disabled, "project-backup" })
        {
            Write(manifestRoot, $"{prefix}/trinkets/probe.entries.trinkets.json", "{\"entries\":[]}");
            Write(manifestRoot, $"{prefix}/localization/probe.string_table.xml", "<root/>");
        }

        var listedLength = new FileInfo(Path.Combine(manifestRoot, listedPath)).Length;
        Write(manifestRoot, "modfiles.txt",
            $"{listedPath} {listedLength + 1}\n" +
            $"./{listedPath.Replace('/', '\\')} {listedLength + 1}\n" +
            "heroes/space name/space name.info.darkest 13\n" +
            "inventory/no size.inventory.items.darkest\n" +
            "desktop.ini 10\n" +
            "dungeons/probe/missing.1.mash.darkest 100\n");
        Write(fallbackRoot, "trinkets/probe.entries.trinkets.json", """{"entries":[{"id":"inventory_fallback","rarity":"common"}]}""");
        Write(fallbackRoot, "localization/unused/old.string_table.xml", "<root/>");
        Write(Path.Combine(root, "inactive"), "trinkets/unused.entries.trinkets.json", "invalid but inactive");
        var content = activeContent with
        {
            Sources = activeContent.Sources.Concat(new[]
            {
                new ActiveContentSource("local:inventory", "Inventory", "local", manifestRoot, -4000),
                new ActiveContentSource("workshop:inventory", "Inventory fallback", "workshop", fallbackRoot, -4001)
            }).ToArray()
        };
        var protectedFiles = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Append(Path.Combine(content.Profile.ProfileDirectory, "persist.game.json"))
            .ToDictionary(path => path, path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
        var catalogContent = content with { Sources = content.Sources.Where(source => source.Directory != fallbackRoot).ToArray() };
        var missingRejected = false;
        try { TrinketCatalog.Load(content); }
        catch (InvalidDataException error) when (error.Message.Contains("modfiles.txt", StringComparison.Ordinal)) { missingRejected = true; }
        Assert(missingRejected, "Missing-manifest Mods are diagnostic-only until explicit preparation succeeds.");
        var trinketsBefore = JsonSerializer.Serialize(TrinketCatalog.Load(catalogContent));
        var heroesBefore = JsonSerializer.Serialize(HeroClassCatalog.Load(catalogContent));
        var estate = JsonNode.Parse(File.ReadAllText(fixture.DecodedSeedPath))!.AsObject();
        var itemsBefore = JsonSerializer.Serialize(QuantityItemCatalog.Load(catalogContent, estate));
        var map = new BattleMapSnapshot(content.Profile.ProfileDirectory, "", "", "", "", "probe", 1, 1,
            null, null, null, null, null, null, null, null, null, false, [], [], [], DateTime.UtcNow);
        var encountersBefore = JsonSerializer.Serialize(BattleEncounterCatalog.Load(catalogContent, map));

        var snapshot = ContentFileInventory.Scan(content);
        var mod = snapshot.Mods.Single(item => item.SourceId == "local:inventory");
        var files = mod.Files.ToDictionary(file => file.RelativePath);
        Assert(mod.IsComplete && mod.HasManifest && mod.Issues.Any(issue => issue.Contains("重复路径", StringComparison.Ordinal)),
            "Complete inventory should tolerate and diagnose duplicate normalized manifest paths.");
        Assert(files[listedPath].ManifestMatch == ContentManifestMatch.Listed && files[listedPath].HasLengthMismatch &&
               files["heroes/space name/space name.info.darkest"].ManifestMatch == ContentManifestMatch.Listed &&
               files["inventory/no size.inventory.items.darkest"].DeclaredLength is null,
            "Manifest matching must support spaces, slash normalization, absent lengths, and size mismatches without filtering content.");
        Assert(files["trinkets/extra.entries.trinkets.json"].ManifestMatch == ContentManifestMatch.Unlisted &&
               files["trinkets/extra.entries.trinkets.json"].IsContentCandidate &&
               files["localization/new.string_table.xml"].ManifestMatch == ContentManifestMatch.Unlisted &&
               files["localization/new.string_table.xml"].IsContentCandidate &&
               files[$"{enabled}/localization/probe.string_table.xml"].IsContentCandidate &&
               !files[$"{disabled}/localization/probe.string_table.xml"].IsContentCandidate,
            "Unlisted XML is only a diagnostic candidate under the same enabled-DLC scope as data files.");
        Assert(files[$"{enabled}/trinkets/probe.entries.trinkets.json"].IsContentCandidate &&
               !files[$"{disabled}/trinkets/probe.entries.trinkets.json"].IsContentCandidate &&
               !files["project-backup/trinkets/probe.entries.trinkets.json"].IsContentCandidate &&
               files["preview.png"].Kind == ContentInventoryFileKind.Asset &&
               files["auto_balance.py"].Kind == ContentInventoryFileKind.Tool &&
               !files["auto_balance.py"].IsContentCandidate,
            "Full inventory retains excluded files but must not classify disabled DLC, backup roots, assets or tools as active data candidates.");
        Assert(files["desktop.ini"].ManifestMatch == ContentManifestMatch.Missing && !files["desktop.ini"].IsContentCandidate &&
               files["dungeons/probe/missing.1.mash.darkest"].ManifestMatch == ContentManifestMatch.Missing &&
               snapshot.Mods.All(item => !item.Directory.EndsWith("inactive", StringComparison.Ordinal)) &&
               snapshot.Mods.Single(item => item.SourceId == "workshop:inventory").Files.All(file => file.ManifestMatch == ContentManifestMatch.NoManifest),
            "Inventory must distinguish missing metadata, missing content, no-manifest files and inactive Mods.");
        var lines = ContentFileInventory.FormatDetails(snapshot).ToArray();
        Assert(ContentFileInventory.FormatSummary(snapshot).Contains("未改变内容纳入规则", StringComparison.Ordinal) &&
               lines.Any(line => line.Contains("清单外数据/译文文件：仅盘点、未加载", StringComparison.Ordinal) && line.Contains("localization/new", StringComparison.Ordinal)) &&
               lines.All(line => !line.Contains("补读范围", StringComparison.Ordinal)) &&
               lines.Any(line => line.Contains("清单长度不同", StringComparison.Ordinal) && line.Contains(listedPath, StringComparison.Ordinal)) &&
               lines.Any(line => line.Contains("清单缺失：Windows 文件夹设置", StringComparison.Ordinal) && line.Contains("desktop.ini", StringComparison.Ordinal)) &&
               lines.All(line => !line.Contains("路径=preview.png", StringComparison.Ordinal)),
            "Detailed logs must explain relevant differences while aggregating unrelated assets and avoiding claims of successful parsing.");
        var severityProbe = snapshot with
        {
            Mods = [mod with
            {
                Files = [
                    new("panels/DESKTOP.INI", ContentInventoryFileKind.Other, ContentManifestMatch.Missing, null, 10, false),
                    new("desktop.ini.bak", ContentInventoryFileKind.Other, ContentManifestMatch.Missing, null, 10, false),
                    new("preview.png", ContentInventoryFileKind.Asset, ContentManifestMatch.Missing, null, 10, false),
                    new("localization/english.loc", ContentInventoryFileKind.LocalizationBinary, ContentManifestMatch.Missing, null, 10, true),
                    files["dungeons/probe/missing.1.mash.darkest"]
                ]
            }]
        };
        var severityEntries = ContentFileInventory.FormatLogDetails(severityProbe)
            .Where(entry => entry.Message.StartsWith("清单缺失：", StringComparison.Ordinal)).ToArray();
        Assert(severityEntries.Count(entry => entry.Level == DiagnosticLogLevel.Information) == 1 &&
               severityEntries.Single(entry => entry.Level == DiagnosticLogLevel.Information).Message.Contains("panels/DESKTOP.INI", StringComparison.Ordinal) &&
               severityEntries.Count(entry => entry.Level == DiagnosticLogLevel.Warning) == 4 &&
               ContentFileInventory.FormatDetails(severityProbe).SequenceEqual(ContentFileInventory.FormatLogDetails(severityProbe).Select(entry => entry.Message)),
            "Only exact desktop.ini metadata may be downgraded; missing definitions, translations, assets and similarly named files remain warnings and the string formatter stays compatible.");
        Assert(trinketsBefore == JsonSerializer.Serialize(TrinketCatalog.Load(catalogContent)) &&
               heroesBefore == JsonSerializer.Serialize(HeroClassCatalog.Load(catalogContent)) &&
               itemsBefore == JsonSerializer.Serialize(QuantityItemCatalog.Load(catalogContent, estate)) &&
               encountersBefore == JsonSerializer.Serialize(BattleEncounterCatalog.Load(catalogContent, map)),
            "Inventory must not change any catalog result, availability, overlay, scene or encounter classification.");
        var trinkets = TrinketCatalog.Load(catalogContent).Trinkets;
        Assert(trinkets.All(item => item.Id is not ("inventory_fallback" or "inventory_unlisted")) &&
               trinkets.Single(item => item.Id == "inventory_listed").LocalizedName == BilingualContentName.Empty &&
               protectedFiles.All(pair => pair.Value == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(pair.Key)))),
            "Diagnostic inventory must not promote missing-manifest/unlisted definitions or XML, or write source files or saves.");

        Write(manifestRoot, "trinkets/new.entries.trinkets.json", "{\"entries\":[]}");
        var refreshed = ContentFileInventory.Scan(content);
        Assert(refreshed.Mods.Single(item => item.SourceId == mod.SourceId).Files.Count == mod.Files.Count + 1,
            "Every explicit catalog reload must acquire a new file inventory, not reuse a stale global cache.");
        var invalidRoot = Path.Combine(root, "invalid-manifest");
        Write(invalidRoot, "trinkets/present.entries.trinkets.json", "{\"entries\":[]}");
        Write(invalidRoot, "modfiles.txt", "../escaped.entries.trinkets.json 1\n" +
            "trinkets/oversized.entries.trinkets.json 999999999999999999999\n");
        var invalidContent = content with { Sources = [new("local:invalid\nlog", "Invalid", "local", invalidRoot, 0)] };
        var invalid = ContentFileInventory.Scan(invalidContent);
        Assert(!invalid.IsComplete && invalid.Mods.Single().Files.All(file => file.ManifestMatch == ContentManifestMatch.Unknown) &&
               invalid.Mods.Single().Issues.Count == 2 &&
               ContentFileInventory.FormatDetails(invalid).All(line => !line.Contains('\n') && !line.Contains('\r')),
            "Unsafe or unparseable manifest entries must produce incomplete diagnostics, never a false unlisted/no-manifest classification or injected log line.");
        foreach (var invalidCharacter in Path.GetInvalidFileNameChars().Where(character => character is not '/' and not '\\' and not '\r' and not '\n'))
        {
            foreach (var invalidPath in new[] { $"trinkets/probe{invalidCharacter}.json", $"trinkets{invalidCharacter}/probe.json" })
            {
                Write(invalidRoot, "modfiles.txt", "trinkets/present.entries.trinkets.json\n" +
                    "trinkets/missing.entries.trinkets.json 12\n" + $"{invalidPath} 12\n");
                var invalidCharacters = ContentFileInventory.Scan(invalidContent);
                Assert(!invalidCharacters.IsComplete && invalidCharacters.Mods.Single().Issues.Count == 1 &&
                       invalidCharacters.Mods.Single().Files.All(file => file.ManifestMatch == ContentManifestMatch.Unknown),
                    $"Invalid path component character U+{(int)invalidCharacter:X4} must invalidate a mixed manifest instead of reporting definite missing files.");
            }
        }

        var missing = ContentFileInventory.Scan(content with
        {
            Sources = [new("local:missing", "Missing", "local", Path.Combine(root, "does-not-exist"), 0)]
        });
        Assert(!missing.IsComplete && missing.Mods.Single().Issues.Count > 0 &&
               ContentFileInventory.FormatSummary(missing).Contains("确认无清单 0 个", StringComparison.Ordinal),
            "Unreadable sources must not be reported as successfully scanned manifest-free Mods.");
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            var cancelled = false;
            try { _ = ContentFileInventory.Scan(content, cancellation.Token); }
            catch (OperationCanceledException) { cancelled = true; }
            Assert(cancelled, "File inventory must honor cancellation.");
        }

        var linkedRoot = Path.Combine(root, "linked-mod");
        var link = Path.Combine(linkedRoot, "linked");
        Directory.CreateDirectory(linkedRoot);
        try
        {
            Directory.CreateSymbolicLink(link, manifestRoot);
            var linked = ContentFileInventory.Scan(content with { Sources = [new("local:link", "Link", "local", linkedRoot, 0)] });
            Assert(!linked.IsComplete && linked.Mods.Single().Files.Count == 0 &&
                   linked.Mods.Single().Issues.Any(issue => issue.Contains("重解析点", StringComparison.Ordinal)),
                "Discovery must not follow recursive links into another directory.");
            File.CreateSymbolicLink(Path.Combine(linkedRoot, "modfiles.txt"), Path.Combine(manifestRoot, "modfiles.txt"));
            var linkedManifest = ContentFileInventory.Scan(content with { Sources = [new("local:link", "Link", "local", linkedRoot, 0)] });
            Assert(!linkedManifest.IsComplete && linkedManifest.Mods.Single().HasManifest &&
                   linkedManifest.Mods.Single().Files.Count == 0,
                "A manifest link must not be opened or reclassified as a missing manifest.");
            var linkedSource = ContentFileInventory.Scan(content with { Sources = [new("local:root-link", "Root link", "local", link, 0)] });
            Assert(!linkedSource.IsComplete && linkedSource.Mods.Single().Files.Count == 0,
                "A source directory that is itself a link must not be traversed.");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or PlatformNotSupportedException ||
                                   ex is IOException && (ex.HResult & 0xFFFF) == 1314)
        {
            Console.WriteLine($"SKIP: symbolic-link inventory fixture unavailable: {ex.Message}");
        }

        var loading = File.ReadAllText(Path.Combine(repositoryRoot, "src", "DarkestDungeonSaveEditor.App", "MainWindow.CatalogLoading.cs"));
        var logging = File.ReadAllText(Path.Combine(repositoryRoot, "src", "DarkestDungeonSaveEditor.App", "MainWindow.CatalogDiagnostics.cs"));
        Assert(loading.IndexOf("await RecordContentFileDiagnosticsAsync(inventory)", StringComparison.Ordinal) >= 0 &&
               loading.IndexOf("await RecordContentFileDiagnosticsAsync(inventory)", StringComparison.Ordinal) <
               loading.IndexOf("Trinkets = diagnosticBatch.Capture", StringComparison.Ordinal) &&
               loading.Contains("Task.Run(() => ScanContentFilesForDiagnostics(activeContent))", StringComparison.Ordinal) &&
               logging.Contains("CrashDiagnostics.RecordStatus(entry.Message, entry.Level);", StringComparison.Ordinal) &&
               logging.Contains("await Task.Run(() =>", StringComparison.Ordinal) &&
               !logging.Contains("AppendStatus(line)", StringComparison.Ordinal),
            "File diagnostics must run off the UI thread before catalog parsing, with full details persisted rather than appended to table/UI messages.");
    }
}
