internal static partial class ContractSuite
{
    private static void RunManifestDiscoveryContracts(ActiveContentSnapshot activeContent, ContractFixture fixture)
    {
        var extract = typeof(TrinketCatalog).Assembly
            .GetType("DarkestDungeonSaveEditor.Core.ModManifestPath")!
            .GetMethod("Extract", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!;
        string? Extract(string line, params string[] suffixes) =>
            (string?)extract.Invoke(null, [line, suffixes]);
        foreach (var relative in new[]
                 {
                     "trinkets/normal.entries.trinkets.json",
                     "trinkets/目录 有空格/probe.entries.trinkets.json",
                     "trinkets/foo.entries.trinkets.json backup/probe.entries.trinkets.json",
                     "trinkets/old.json folder/x.darkest folder/probe.entries.trinkets.json"
                 })
        {
            foreach (var line in new[] { relative, $"{relative} 123", $"  {relative}\t123\t " })
            {
                Assert(
                    Extract(line, ".entries.trinkets.json")?.Replace('\\', '/') == relative &&
                    Extract(line, ".darkest", ".json")?.Replace('\\', '/') == relative,
                    "Manifest parsing must preserve the complete path regardless of embedded extensions, spaces, suffix order, or an optional trailing size.");
            }
        }
        Assert(Extract("localization/old.string_table.xml.unused 12", ".string_table.xml") is null &&
               Extract("localization/table.loc2.unused 12", ".loc2") is null &&
               Extract("   ", ".json") is null,
            "Unsupported final extensions and empty manifest rows must remain excluded.");

        var root = Path.Combine(fixture.RunRoot, "manifest-discovery");
        var modRoot = Path.Combine(root, "mod");
        var manifestPath = Path.Combine(modRoot, "modfiles.txt");
        const string relativePath = "trinkets/foo.entries.trinkets.json backup/probe.entries.trinkets.json";
        var filePath = Path.Combine(modRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, "{\"entries\":[{\"id\":\"manifest_path_probe\",\"limit\":0,\"price\":1,\"rarity\":\"common\"}]}");
        var manifestText = $"{relativePath} {new FileInfo(filePath).Length}\n";
        File.WriteAllText(manifestPath, manifestText);
        var content = activeContent with
        {
            Sources = [new ActiveContentSource("local:manifest-probe", "Manifest Probe", "local", modRoot, 0)]
        };
        var catalog = TrinketCatalog.Load(content);
        Assert(catalog.Trinkets.Single().Id == "manifest_path_probe" &&
               !catalog.Issues.Any(issue => issue.Contains("listed by Mod is missing", StringComparison.Ordinal)),
            "A valid manifest path containing an embedded resource extension must resolve to its real file.");

        var map = new BattleMapSnapshot(
            content.Profile.ProfileDirectory, "", "", "", "", "probe", 1, 1,
            null, null, null, null, null, null, null, null, null, false, [], [], [], DateTime.UtcNow);
        var estate = JsonNode.Parse(File.ReadAllText(fixture.DecodedSeedPath))!.AsObject();
        var loaders = new (string Name, string InvalidPath, Action Load)[]
        {
            ("trinkets", "trinkets/bad\0.entries.trinkets.json", () => TrinketCatalog.Load(content)),
            ("heroes", "heroes/probe/bad\0.info.darkest", () => HeroClassCatalog.Load(content)),
            ("items", "inventory/bad\0.inventory.items.darkest", () => QuantityItemCatalog.Load(content, estate)),
            ("trinket capacity", "inventory/bad\0.inventory.system_configs.darkest", () => TrinketStorageCatalog.Load(content)),
            ("raid capacity", "inventory/bad\0.inventory.system_configs.darkest", () => RaidInventoryStorageCatalog.Load(content)),
            ("encounters", "dungeons/probe/bad\0.1.mash.darkest", () => BattleEncounterCatalog.Load(content, map)),
            ("room attachments", "dungeons/probe/bad\0.props.darkest", () => BattleRoomAttachmentCatalog.Load(content)),
            ("localization", "localization/bad\0.string_table.xml", () => TrinketCatalog.Load(content)),
            ("item references", "loot/bad\0.loot.json", () => QuantityItemCatalog.Load(content, estate))
        };
        void ExpectManifestFailure(Action load, string name, string expectedDetail)
        {
            try
            {
                load();
            }
            catch (InvalidDataException ex)
            {
                Assert(ex.Message.Contains(manifestPath, StringComparison.Ordinal) &&
                       ex.Message.Contains("目录加载已停止", StringComparison.Ordinal) &&
                       ex.Message.Contains(expectedDetail, StringComparison.Ordinal),
                    $"{name} must identify the failed manifest and refuse partial/fallback loading.");
                return;
            }

            throw new InvalidOperationException($"{name} unexpectedly accepted an unreadable manifest.");
        }

        using (var locked = new FileStream(manifestPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            foreach (var loader in loaders)
            {
                ExpectManifestFailure(loader.Load, loader.Name, "无法读取 Mod 清单");
            }
            Assert(!ContentFileInventory.Scan(content).IsComplete,
                "Diagnostic inventory must still report an incomplete read without becoming an effective-content fallback.");
        }
        Assert(TrinketCatalog.Load(content).Trinkets.Single().Id == "manifest_path_probe",
            "Releasing a manifest file lock must allow a fresh normal load without a persistent failure cache.");

        foreach (var loader in loaders)
        {
            File.WriteAllText(manifestPath, manifestText + loader.InvalidPath + " 1\n");
            ExpectManifestFailure(loader.Load, loader.Name, "第 2 行路径无效");
        }

        // A readable empty manifest is authoritative, not permission to discover unlisted files.
        File.WriteAllText(manifestPath, string.Empty);
        Assert(TrinketCatalog.Load(content).Trinkets.Count == 0,
            "An empty manifest must not activate otherwise present unlisted definitions.");

        var directoryManifestRoot = Path.Combine(root, "directory-manifest");
        Directory.CreateDirectory(Path.Combine(directoryManifestRoot, "modfiles.txt"));
        var invalidContent = content with
        {
            Sources = [new ActiveContentSource("local:directory-manifest", "Invalid Manifest", "local", directoryManifestRoot, 0)]
        };
        try
        {
            _ = TrinketCatalog.Load(invalidContent);
            throw new InvalidOperationException("A directory named modfiles.txt must not select manifest-free fallback.");
        }
        catch (InvalidDataException ex)
        {
            Assert(ex.Message.Contains("清单路径不是文件", StringComparison.Ordinal),
                "A non-file manifest path must produce an explicit diagnostic.");
        }
    }
}
