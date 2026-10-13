using System.Globalization;

internal static partial class ContractSuite
{
    private static async Task RunModManifestDirectoryContractsAsync(string runRoot)
    {
        var root = Path.Combine(runRoot, "manifest-directories");
        var source = Path.Combine(root, "mod");
        const string item = "inventory/probe.inventory.items.darkest";
        const string sprite = "fx/town_event_idle_first_fleshgolem.sprite";
        const string emptyFile = "fx/nested.folder/child/empty.png";
        const string skin = "heroes/directory_hero/directory_hero_A";
        const string dataDirectory = "trinkets/directory.entries.trinkets.json";
        WriteMultiMash(source, item, "inventory_item: .type estate .id probe .base_stack_limit 3\n");
        WriteMultiMash(source, sprite + "/frame.png", "sprite frame");
        WriteMultiMash(source, emptyFile, "");
        WriteMultiMash(source, "fx/ordinary.sprite", "ordinary file");
        WriteMultiMash(source, "fx/name with spaces.sprite/part.png", "part");
        WriteMultiMash(source, dataDirectory + "/unlisted.entries.trinkets.json",
            """{"entries":[{"id":"must_remain_unlisted","rarity":"common"}]}""");
        Directory.CreateDirectory(Path.Combine(source, skin));
        Directory.CreateDirectory(Path.Combine(source, "heroes/directory_hero/directory_hero_B"));
        var snapshot = ModManifestFiles.Snapshot(source, default);
        string FileRow(string path) => $"{path} {snapshot.Files[path].Length}";
        var fileRows = new[] { item, sprite + "/frame.png", emptyFile, "fx/ordinary.sprite", "fx/name with spaces.sprite/part.png" }.Select(FileRow);
        var directoryRows = new[] { sprite, "fx/nested.folder", "fx/nested.folder/child", skin, dataDirectory, "fx/name with spaces.sprite" }
            .Select(path => path + "/ 0");
        var manifest = string.Join('\n', fileRows.Concat(directoryRows)) + "\n";
        var bytes = Encoding.UTF8.GetBytes(manifest);
        var records = ModManifestFiles.Validate(bytes, snapshot);
        Assert(records.Count(record => record.IsDirectory) == 6 && records.Count(record => !record.IsDirectory) == 5 &&
            records.Single(record => record.RelativePath == emptyFile) is { IsDirectory: false, Size: 0 },
            "Official directory records, nested/empty directories, spaces and genuine zero-byte files must remain distinct.");

        foreach (var (row, reason) in new[]
        {
            ("fx/missing.sprite/ 0", "不存在此目录"),
            (sprite + "/ 7", "大小必须为 0"),
            ("fx/ordinary.sprite/ 0", "原 Mod 中是文件"),
            (sprite + " 0", "原 Mod 中是目录"),
            ("fx/missing.sprite 0", "不存在此文件"),
            (emptyFile + " 1", "文件大小不符"),
            ("fx/ordinary.sprite 999", "文件大小不符"),
            (sprite + "// 0", "相对路径无效"),
            (sprite + "/../ 0", "相对路径无效"),
            (sprite + "/./ 0", "相对路径无效"),
            ("../outside/ 0", "相对路径无效"),
            ("/fx/ 0", "相对路径无效"),
            ("C:/fx/ 0", "相对路径无效"),
            ("fx\\nested.folder/ 0", "相对路径无效"),
            ("fx/invalid:name/ 0", "相对路径无效"),
            (FileRow(item).ToUpperInvariant(), "路径重复"),
            (sprite + "/ -1", "行格式无效")
        })
        {
            try
            {
                ModManifestFiles.Validate(Encoding.UTF8.GetBytes(FileRow(item) + "\n\n" + row + "\n"), snapshot);
                throw new InvalidOperationException($"Invalid manifest row was accepted: {row}");
            }
            catch (InvalidDataException error)
            {
                Assert(error.Message.Contains("第 3 行", StringComparison.Ordinal) &&
                    error.Message.Contains(row[..row.LastIndexOf(' ')], StringComparison.Ordinal) &&
                    error.Message.Contains(reason, StringComparison.Ordinal),
                    $"Invalid rows must identify their physical line, path and specific cause: {error.Message}");
            }
        }
        foreach (var output in new[] { "", sprite + "/ 0\n", " \n" })
        {
            var rejected = false;
            try { ModManifestFiles.Validate(Encoding.UTF8.GetBytes(output), snapshot); }
            catch (InvalidDataException) { rejected = true; }
            Assert(rejected, "Directory-only or empty output must not bypass the existing no-valid-files guard.");
        }
        var duplicateDirectoryRejected = false;
        try { ModManifestFiles.Validate(Encoding.UTF8.GetBytes(manifest + sprite.ToUpperInvariant() + "/ 0\n"), snapshot); }
        catch (InvalidDataException error) when (error.Message.Contains("路径重复", StringComparison.Ordinal)) { duplicateDirectoryRejected = true; }
        Assert(duplicateDirectoryRejected, "Duplicate directories must not bypass duplicate-path validation.");

        var profile = WriteBattleSafetyProfile(Path.Combine(root, "profile"));
        var locations = new SaveEditorLocations(root, Path.Combine(root, "workspaces"), Path.Combine(root, "backups"));
        var content = new ActiveContentSnapshot(profile, "base", [new("local:directories", "Directory Probe", "local", source, 1000)], [],
            locations.WorkspaceDirectory, "", 1, ModManifestFiles.Hash(Path.Combine(profile.ProfileDirectory, "persist.game.json")));
        var originalCulture = CultureInfo.CurrentUICulture;
        try
        {
            foreach (var language in new[] { "en", "zh-CN" })
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
                var service = new ModManifestPreparationService(locations, () => false, (_, _, _, _) =>
                    Task.FromResult(Encoding.UTF8.GetBytes(FileRow(item) + "\nfx/missing.sprite/ 0\n")));
                InvalidDataException? failure = null;
                try { await service.EnsureAsync(content, "unused"); }
                catch (InvalidDataException error) { failure = error; }
                Assert(failure is not null && failure.Message.Contains("Directory Probe", StringComparison.Ordinal) &&
                    failure.Message.Contains(source, StringComparison.Ordinal) && failure.Message.Contains("fx/missing.sprite/", StringComparison.Ordinal) &&
                    failure.Message.Contains(language == "en" ? "line 2" : "第 2 行", StringComparison.Ordinal) &&
                    failure.Message.Contains(language == "en" ? "Directory not found" : "不存在此目录", StringComparison.Ordinal) &&
                    !File.Exists(Path.Combine(source, "modfiles.txt")),
                    "Preparation failures must explain the Mod, source, row, directory and cause in the selected language without publishing output.");
            }
        }
        finally { CultureInfo.CurrentUICulture = originalCulture; }

        var result = await new ModManifestPreparationService(locations, () => false, (_, _, _, _) => Task.FromResult(bytes))
            .EnsureAsync(content, "unused");
        Assert(result.CreatedCount == 1 && File.ReadAllBytes(Path.Combine(source, "modfiles.txt")).SequenceEqual(bytes),
            "Preparation must install official directory rows unchanged.");
        ModManifestFiles.RequireSnapshot(source, snapshot, default, ignoreManifest: true);
        Assert(QuantityItemCatalog.LoadDefinitions(content, QuantityItemSaveContext.Raid).Single().BaseStackLimit == 3 &&
            TrinketCatalog.Load(content).Trinkets.Count == 0,
            "Directory records must neither break catalog loading nor authorize recursive loading of unlisted resources.");
        var inventory = ContentFileInventory.Scan(content).Mods.Single();
        Assert(inventory.IsComplete && inventory.Issues.Count == 0 &&
            inventory.Files.Count == snapshot.Files.Count + 1 &&
            inventory.Files.All(file => file.ManifestMatch != ContentManifestMatch.Missing) &&
            inventory.Files.Single(file => file.RelativePath == emptyFile) is { ActualLength: 0, DeclaredLength: 0 },
            "Directory records must not enter file counts or missing-file diagnostics; zero-byte files must still be inventoried.");
        var skinPaths = HeroSkinDirectoryDiscovery.Enumerate(content.Sources.Single(), [], []);
        Assert(skinPaths.SequenceEqual(new[] { Path.Combine(source, skin.Replace('/', Path.DirectorySeparatorChar)) }),
            "An explicitly listed empty skin directory must count, while the physically present unlisted skin must not.");

        var manifestPath = Path.Combine(source, "modfiles.txt");
        File.AppendAllText(manifestPath, "fx/missing.sprite/ 0\nfx/ordinary.sprite/ 0\n" + sprite + "/ 0\n");
        inventory = ContentFileInventory.Scan(content).Mods.Single();
        Assert(inventory.Issues.Count == 3 && inventory.Issues.Any(issue => issue.Contains("目录不存在", StringComparison.Ordinal)) &&
            inventory.Issues.Any(issue => issue.Contains("实际是文件", StringComparison.Ordinal)) &&
            inventory.Issues.Any(issue => issue.Contains("重复路径", StringComparison.Ordinal)) &&
            inventory.Files.All(file => file.ManifestMatch != ContentManifestMatch.Missing),
            "Inventory must distinguish missing directories, file/directory mismatches and duplicate directory records from missing files.");
        foreach (var invalid in new[] { sprite + "/ 1", sprite + "/", sprite + "// 0", "../escape/ 0" })
        {
            File.WriteAllText(manifestPath, manifest + invalid + "\n");
            inventory = ContentFileInventory.Scan(content).Mods.Single();
            Assert(!inventory.IsComplete && inventory.Issues.Count == 1 && inventory.Files.All(file => file.ManifestMatch == ContentManifestMatch.Unknown),
                $"Inventory must report malformed directory records as incomplete: {invalid}");
        }
        File.WriteAllBytes(manifestPath, bytes);
        ModManifestFiles.RequireSnapshot(source, snapshot, default, ignoreManifest: true);
        Assert(ModManifestFiles.Hash(Path.Combine(profile.ProfileDirectory, "persist.game.json")) == content.SourceGameSha256,
            "Manifest directory validation and inventory must not change saves or source payloads.");
        Console.WriteLine("PASS: directory manifest validation, strict failure boundaries, bilingual errors, inventory, skin discovery and unchanged payloads.");
    }
}
