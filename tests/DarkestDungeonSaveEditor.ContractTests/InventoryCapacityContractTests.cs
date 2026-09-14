internal static partial class ContractSuite
{
    public static void RunInventoryCapacitiesOnly(string repositoryRoot)
    {
        var root = Path.Combine(repositoryRoot, "workspaces", "contract_tests", $"capacities_{Guid.NewGuid():N}");
        RunInventoryCapacityContracts(root);
        Console.WriteLine($"Artifacts: {root}");
    }

    private static void RunInventoryCapacityContracts(string runRoot)
    {
        var root = Path.Combine(runRoot, "inventory-capacity-semantics");
        var baseRoot = Path.Combine(root, "base");
        const string firstPath = "inventory/a.inventory.system_configs.darkest";
        const string lastPath = "inventory/b.inventory.system_configs.darkest";
        static string Both(string fields) =>
            $"inventory_system_config: .type \"raid\" {fields}\ninventory_system_config: .type \"trinket_storage\" {fields}\n";
        var first = WriteMultiMash(baseRoot, firstPath, Both(".max_slots 4"));
        var last = WriteMultiMash(baseRoot, lastPath, Both(".max_slots 2"));
        var baseSource = new ActiveContentSource("base", "Base", "base", baseRoot, 0);
        var content = new ActiveContentSnapshot(
            new SaveProfile("profile_7", root, Path.Combine(root, "persist.estate.json"), "contract-user", DateTime.UtcNow),
            "base", [baseSource], [], root, string.Empty, 0, string.Empty);

        void Expect(ActiveContentSnapshot snapshot, int? slots, string reason, string? sourcePath = null)
        {
            var raid = RaidInventoryStorageCatalog.Load(snapshot);
            var town = TrinketStorageCatalog.Load(snapshot);
            Assert(raid.Storage?.MaxSlots == slots && town.Storage?.MaxSlots == slots,
                $"{reason} Expected {slots?.ToString() ?? "unresolved"}; raid={raid.Storage?.MaxSlots}, town={town.Storage?.MaxSlots}. " +
                string.Join(" | ", raid.Issues.Concat(town.Issues)));
            if (sourcePath is null || slots is null) return;
            Assert(raid.Storage!.SourcePath == sourcePath && town.Storage!.SourcePath == sourcePath &&
                raid.Storage.SourceSha256.Equals(ComputeSha256(sourcePath), StringComparison.OrdinalIgnoreCase) &&
                town.Storage.SourceSha256.Equals(ComputeSha256(sourcePath), StringComparison.OrdinalIgnoreCase),
                "Capacity provenance must pin the file bytes that last assigned max_slots.");
        }

        Expect(content, 2, "Later same-type declarations in different effective files must update the existing config.", last);
        WriteMultiMash(baseRoot, firstPath, Both(".max_slots 2"));
        WriteMultiMash(baseRoot, lastPath, Both(".max_slots 4"));
        Expect(content, 4, "Swapping capacity declarations must swap the effective result.", last);

        foreach (var (fields, expected) in new (string, int?)[]
        {
            (".max_slots 3 .max_slots 1", 1),
            (".max_slots +3suffix", 3),
            (".max_slots 1/*native comment*/2", 12),
            (".max_slots 3 // ignored .max_slots 90\n", 3),
            (".max_slots invalid", null),
            (".max_slots \"3\"", null),
            (".max_slots", null),
            (".max_slots -2", null),
            (".max_slots 2147483648", null),
            (".use_stack_limits true", 2),
            (".MAX_SLOTS 99", 2)
        })
        {
            WriteMultiMash(baseRoot, lastPath, Both(fields));
            Expect(content, expected, $"Native max_slots field parsing: {fields}",
                fields is ".use_stack_limits true" or ".MAX_SLOTS 99" ? first : last);
        }

        WriteMultiMash(baseRoot, lastPath, Both(".max_slots invalid") + Both(".max_slots 5") + Both(".use_stack_limits false"));
        Expect(content, 5, "A later valid assignment must recover an earlier invalid value; omission must retain it.", last);
        WriteMultiMash(baseRoot, lastPath, """
            inventory_system_config: .type wrong .type raid .max_slots 6
            inventory_system_config: .type wrong .type "trinket_storage" .max_slots 6
            inventory_system_config: .type RAID .max_slots 99
            inventory_system_config: .type TRINKET_STORAGE .max_slots 99
            unrelated_record: .type raid .max_slots 99
            """);
        Expect(content, 6, "Type and record identity are exact, and a repeated type field uses its last string.", last);

        WriteMultiMash(baseRoot, firstPath, Both(".use_stack_limits true"));
        WriteMultiMash(baseRoot, lastPath, string.Empty);
        Expect(content, null, "An unassigned native capacity defaults to zero and is not writable.");
        WriteMultiMash(baseRoot, firstPath, Both(".max_slots 2"));

        foreach (var nulText in new[]
        {
            Both(".max_slots 2") + "\0" + Both(".max_slots 100"),
            Both(".max_slots 2\0 .max_slots 100")
        })
        {
            WriteMultiMash(baseRoot, lastPath, nulText);
            Expect(content, 2, "NUL ends this file; preserve preceding assignments and never expose post-NUL capacity.");
        }
        WriteMultiMash(baseRoot, lastPath, "\0" + Both(".max_slots 100"));
        Expect(content, 2, "An empty terminated file must not invalidate a preceding independent file.", first);
        WriteMultiMash(baseRoot, lastPath, "inventory_system_config: .type loot .max_slots 9\n\0");
        Expect(content, 2, "NUL after another inventory type must not invalidate raid or trinket capacity.", first);
        WriteMultiMash(baseRoot, firstPath, Both(".max_slots 2") + "\0" + Both(".max_slots 100"));
        WriteMultiMash(baseRoot, lastPath, Both(".max_slots 6"));
        Expect(content, 6, "A later independent file remains loadable after an earlier file terminates.", last);
        WriteMultiMash(baseRoot, firstPath, Both(".max_slots 2"));
        WriteMultiMash(baseRoot, lastPath, string.Empty);

        var highRoot = Path.Combine(root, "high");
        var lowRoot = Path.Combine(root, "low");
        var highPath = WriteMultiMash(highRoot, firstPath, Both(".max_slots 9"));
        WriteMultiMash(lowRoot, firstPath, Both(".max_slots 7"));
        var lowLast = WriteMultiMash(lowRoot, "inventory/z.inventory.system_configs.darkest", Both(".max_slots 3"));
        WriteFixtureManifest(highRoot);
        WriteFixtureManifest(lowRoot);
        var high = new ActiveContentSource("local:high-capacity", "High", "local", highRoot, -100);
        var low = new ActiveContentSource("local:low-capacity", "Low", "local", lowRoot, 100);
        var overlaid = content with { Sources = [high, baseSource, low] };
        Expect(overlaid, 3,
            "A high-priority same-path replacement retains the early file slot; a later unique low-priority file still updates capacity.", lowLast);
        File.WriteAllText(Path.Combine(lowRoot, "modfiles.txt"), firstPath + "\n");
        Expect(overlaid, 9, "An unlisted later config must not participate, and same-path Mod priority must remain intact.", highPath);

        var missingRoot = Path.Combine(root, "missing");
        Directory.CreateDirectory(missingRoot);
        File.WriteAllText(Path.Combine(missingRoot, "modfiles.txt"), firstPath + "\n");
        var missing = new ActiveContentSource("local:missing-capacity", "Missing", "local", missingRoot, -200);
        Expect(overlaid with { Sources = [baseSource, high, missing] }, null,
            "A missing listed winning config must not revive an overridden lower file.");
        Expect(overlaid with { Sources = [baseSource, high, missing with { LoadOrder = 200 }] }, 9,
            "A missing lower provider fully replaced by readable winning bytes must not invalidate that winner.", highPath);
        Expect(overlaid with { Sources = [baseSource, high, low with { LoadOrder = high.LoadOrder }] }, null,
            "Two distinct providers for the same path at an unverified equal priority must still disable capacity writes.");
        using (var locked = new FileStream(highPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Expect(overlaid, null, "An unreadable effective file must keep writes disabled.");

        // Adjacent ASCII deltas +1/-53 preserve the native polynomial-53 hash.
        WriteMultiMash(baseRoot, lastPath, """
            inventory_system_config: .type "raj/" .max_slots 99
            inventory_system_config: .type "trinket_storah0" .max_slots 99
            """);
        Expect(content, null, "A different type string with the same native hash must not silently alter a writable capacity.");
        WriteMultiMash(baseRoot, lastPath, string.Empty);

        var raidCapacity = RaidInventoryStorageCatalog.Load(content).Storage!.MaxSlots;
        var item = new QuantityItemDefinition("provision", "food", QuantityItemStorageKind.RaidInventory,
            2, false, 0, "base", first, false, ["base"]);
        var raidJson = JsonNode.Parse("""{"base_root":{"party":{"inventory":{"items":{}}}}}""")!.AsObject();
        var updatedRaid = RaidInventorySaveEditor.SetAmount(raidJson, item, 4, raidCapacity).UpdatedRoot;
        Assert(updatedRaid["base_root"]!["party"]!["inventory"]!["items"]!.AsObject().Count == 2 &&
            RaidInventorySaveEditor.CountAmount(updatedRaid, item) == 4,
            "The parsed raid capacity must allow exactly two full item stacks.");
        var raidRejected = false;
        try { RaidInventorySaveEditor.SetAmount(raidJson, item, 5, raidCapacity); }
        catch (InvalidOperationException) { raidRejected = true; }
        Assert(raidRejected && raidJson["base_root"]!["party"]!["inventory"]!["items"]!.AsObject().Count == 0,
            "One item beyond the parsed capacity must reject without mutating the input save.");

        var townCapacity = TrinketStorageCatalog.Load(content).Storage!.MaxSlots;
        var trinket = new TrinketDefinition("capacity_ring", "common", null, 100, "base", first, false, [], false, ["base"]);
        var townJson = JsonNode.Parse("""{"base_root":{"trinkets":{"items":{}}}}""")!.AsObject();
        var updatedTown = TrinketSaveEditor.AddCopies(townJson, trinket, 2, townCapacity).UpdatedRoot;
        Assert(TrinketSaveEditor.CountCopies(updatedTown, trinket.Id) == 2,
            "The parsed town capacity must allow exactly two trinket copies.");
        var townRejected = false;
        try { TrinketSaveEditor.AddCopies(townJson, trinket, 3, townCapacity); }
        catch (InvalidOperationException) { townRejected = true; }
        Assert(townRejected && townJson["base_root"]!["trinkets"]!["items"]!.AsObject().Count == 0,
            "One trinket beyond the parsed capacity must reject without mutating the input save.");
        Console.WriteLine("PASS: native capacity field updates, file overlay order, manifests, provenance, unresolved-input guards and actual quantity/trinket mutations.");
    }
}
