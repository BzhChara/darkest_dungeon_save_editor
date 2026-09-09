internal static partial class ContractSuite
{
    // Explicit fixture authoring, never called by catalog readers or a general file writer.
    private static void WriteFixtureManifest(string directory)
    {
        File.WriteAllLines(Path.Combine(directory, "modfiles.txt"), Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(directory, path).Equals("modfiles.txt", StringComparison.OrdinalIgnoreCase))
            .Select(path => $"{Path.GetRelativePath(directory, path).Replace('\\', '/')} {new FileInfo(path).Length}"), new UTF8Encoding(false));
    }

    public static async Task RunManifestsOnlyAsync(string repositoryRoot)
    {
        var root = Path.Combine(repositoryRoot, "workspaces", "manifest_contracts", Guid.NewGuid().ToString("N"));
        await RunModManifestPreparationContractsAsync(root);
        var game = Environment.GetEnvironmentVariable("DDSE_TEST_GAME_DIRECTORY");
        if (!string.IsNullOrWhiteSpace(game)) await RunOfficialManifestContractAsync(root, game);
        Console.WriteLine($"Artifacts: {root}");
    }

    private static async Task RunModManifestPreparationContractsAsync(string runRoot)
    {
        (ActiveContentSnapshot Content, SaveEditorLocations Locations, string Mod) Setup(string name)
        {
            var root = Path.Combine(runRoot, "manifest-preparation", name);
            var profile = WriteBattleSafetyProfile(Path.Combine(root, "profile"));
            var mod = Path.Combine(root, "mod");
            WriteMultiMash(mod, "inventory/probe.inventory.items.darkest", "inventory_item: .type estate .id probe .base_stack_limit 3\n");
            var locations = new SaveEditorLocations(root, Path.Combine(root, "workspaces"), Path.Combine(root, "backups"));
            return (new ActiveContentSnapshot(profile, "base", [new("local:probe", "Probe", "local", mod, 1000)], [],
                locations.WorkspaceDirectory, "", 1, ModManifestFiles.Hash(Path.Combine(profile.ProfileDirectory, "persist.game.json"))), locations, mod);
        }
        static byte[] Manifest(string source) => Encoding.UTF8.GetBytes(string.Join('\n', Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)
            .Select(path => $"{Path.GetRelativePath(source, path).Replace('\\', '/')} {new FileInfo(path).Length}")) + "\n");
        static async Task Reject(Func<Task> action, string expected)
        {
            try { await action(); }
            catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException or OperationCanceledException)
            {
                Assert(error.ToString().Contains(expected, StringComparison.Ordinal), $"Expected rejection '{expected}', got {error}");
                return;
            }
            throw new InvalidOperationException($"Expected manifest preparation to reject: {expected}");
        }
        var first = Setup("selected-only");
        var disabled = Path.Combine(first.Locations.ApplicationDataDirectory, "disabled");
        WriteMultiMash(disabled, "inventory/disabled.inventory.items.darkest", "unused");
        var existing = Path.Combine(first.Locations.ApplicationDataDirectory, "existing");
        WriteMultiMash(existing, "modfiles.txt", "existing-authoritative-manifest\n");
        var existingHash = ModManifestFiles.Hash(Path.Combine(existing, "modfiles.txt"));
        var content = first.Content with { Sources = [.. first.Content.Sources, new("workshop:existing", "Existing", "workshop", existing, 1001)] };
        var before = ModManifestFiles.Snapshot(first.Mod, default);
        var calls = 0;
        var service = new ModManifestPreparationService(first.Locations, () => false, (_, source, _, _) =>
        {
            calls++; return Task.FromResult(Manifest(source));
        });
        var result = await service.EnsureAsync(content, "unused");
        Assert(result.CreatedCount == 1 && calls == 1 && !File.Exists(Path.Combine(disabled, "modfiles.txt")) &&
            ModManifestFiles.Hash(Path.Combine(existing, "modfiles.txt")) == existingHash &&
            JsonNode.Parse(File.ReadAllText(result.ReceiptPath!))!["Status"]!.GetValue<string>() == "complete",
            "Only selected missing manifests are created, with a receipt; disabled Mods and existing manifests are unchanged.");
        ModManifestFiles.RequireSnapshot(first.Mod, before, default, ignoreManifest: true);
        Assert(QuantityItemCatalog.LoadDefinitions(first.Content, QuantityItemSaveContext.Raid).Single().BaseStackLimit == 3,
            "A prepared manifest must be consumed by the real item catalog.");
        Assert((await new ModManifestPreparationService(first.Locations, () => true).EnsureAsync(content, "no-tool")).CreatedCount == 0,
            "Existing manifests require neither a running-game interruption nor an installed uploader.");

        var running = Setup("running");
        await Reject(() => new ModManifestPreparationService(running.Locations, () => true).EnsureAsync(running.Content, "unused"), "关闭游戏");
        Assert(!File.Exists(Path.Combine(running.Mod, "modfiles.txt")), "A running game must prevent manifest installation.");

        foreach (var scenario in new[] { "generation-failure", "invalid", "source-change", "profile-change", "cancel", "other-writer" })
        {
            var fixture = Setup(scenario);
            var second = fixture.Mod + "-second";
            WriteMultiMash(second, "inventory/second.inventory.items.darkest", "inventory_item: .type estate .id second .base_stack_limit 1");
            var selected = fixture.Content with { Sources = [.. fixture.Content.Sources, new("local:second", "Second", "local", second, 1001)] };
            using var cancellation = new CancellationTokenSource();
            var generator = new ModManifestPreparationService(fixture.Locations, () => false, (_, source, _, _) =>
            {
                if (source == second)
                {
                    switch (scenario)
                    {
                        case "generation-failure": throw new IOException("fixture generator failed");
                        case "invalid": return Task.FromResult(Encoding.UTF8.GetBytes("../escape.json 1\n"));
                        case "source-change": File.AppendAllText(Path.Combine(fixture.Mod, "inventory/probe.inventory.items.darkest"), "changed"); break;
                        case "profile-change": File.AppendAllText(Path.Combine(fixture.Content.Profile.ProfileDirectory, "persist.game.json"), " "); break;
                        case "cancel": cancellation.Cancel(); break;
                        case "other-writer": File.WriteAllText(Path.Combine(fixture.Mod, "modfiles.txt"), "external\n"); break;
                    }
                }
                return Task.FromResult(Manifest(source));
            });
            var expected = scenario switch { "generation-failure" => "fixture generator failed", "invalid" => "清单", "profile-change" => "Mod 配置", "cancel" => "OperationCanceledException", _ => "变化" };
            await Reject(() => generator.EnsureAsync(selected, "unused", cancellationToken: cancellation.Token), expected);
            Assert(!File.Exists(Path.Combine(second, "modfiles.txt")) &&
                (scenario == "other-writer" ? File.ReadAllText(Path.Combine(fixture.Mod, "modfiles.txt")) == "external\n" : !File.Exists(Path.Combine(fixture.Mod, "modfiles.txt"))),
                $"{scenario} must not publish prepared or partial manifests, or overwrite another writer.");
        }

        var missing = Setup("readers");
        foreach (var read in new Action[] {
            () => QuantityItemCatalog.LoadDefinitions(missing.Content, QuantityItemSaveContext.Raid),
            () => TrinketCatalog.Load(missing.Content), () => HeroClassCatalog.Load(missing.Content),
            () => BattleEncounterCatalog.ReadMaintenanceMonsterSizes(missing.Content.Sources),
            () => BattleRoomAttachmentCatalog.Load(missing.Content),
            () => TrinketStorageCatalog.Load(missing.Content), () => RaidInventoryStorageCatalog.Load(missing.Content),
            () => ProfileCatalogContentFingerprint.Capture(missing.Content.Sources) })
            await Reject(() => { read(); return Task.CompletedTask; }, "modfiles.txt");
        File.WriteAllText(Path.Combine(missing.Mod, "modfiles.txt"), "");
        Assert(QuantityItemCatalog.LoadDefinitions(missing.Content, QuantityItemSaveContext.Raid).Count == 0,
            "An existing empty manifest remains authoritative; readers must not fall back to disk discovery.");
        Console.WriteLine("PASS: manifest preparation selection, idempotence, game/config/source/cancellation guards, failure isolation and strict readers.");
    }

    private static async Task RunOfficialManifestContractAsync(string runRoot, string game)
    {
        var root = Path.Combine(runRoot, "official-manifest");
        var source = Path.Combine(root, "mod");
        var evidence = Path.Combine(root, "evidence");
        Directory.CreateDirectory(evidence);
        WriteMultiMash(source, "inventory/ddse_probe.inventory.items.darkest", "inventory_item: .type estate .id ddse_probe .base_stack_limit 3\n");
        // Invalid source formats would fail or be rewritten if the native compilation phases ran on these originals.
        WriteMultiMash(source, "maps/source.json", "leave authored source untouched");
        WriteMultiMash(source, "localization/probe.loc2", "preserve existing binary payload");
        WriteMultiMash(source, "localization/probe.string_table.xml", "leave authored localization untouched");
        WriteMultiMash(source, "inventory/.backup/unused.inventory.items.darkest", "unused");
        var vanilla = Directory.EnumerateFiles(Path.Combine(game, "inventory"), "*.darkest").First();
        var relative = Path.GetRelativePath(game, vanilla);
        File.Copy(vanilla, Path.Combine(source, relative));
        var original = ModManifestFiles.Snapshot(source, default);
        var bytes = await OfficialModManifestGenerator.GenerateAsync(game, source, evidence, default);
        ModManifestFiles.RequireSnapshot(source, original, default);
        var manifest = Encoding.UTF8.GetString(bytes);
        Assert(manifest.Contains("inventory/ddse_probe.inventory.items.darkest ", StringComparison.Ordinal) &&
            manifest.Contains("maps/source.json ", StringComparison.Ordinal) && manifest.Contains("localization/probe.loc2 ", StringComparison.Ordinal) &&
            !manifest.Contains(relative.Replace('\\', '/'), StringComparison.Ordinal) && !manifest.Contains(".backup", StringComparison.Ordinal),
            "The actual official dont_submit tool must include changed files, omit vanilla-identical/hidden files, and leave compiled/authored originals untouched.");
        Assert(!File.Exists(Path.Combine(source, "modfiles.txt")), "The official generator prepares bytes; only the preparation service installs them.");
        Console.WriteLine("PASS: actual official uploader in an isolated wrapper, vanilla comparison, filtered entries and original resource preservation.");
    }
}
