using System.Text.Json;
using System.Security.Cryptography;

internal static partial class ContractSuite
{
    private static async Task RunSourceBindingInventoryContractsAsync(string runRoot, DsonSaveCodec codec)
    {
    var rows = new List<object>();
    foreach (var workflow in new[] { "raid-item", "trinket" })
    foreach (var scenario in new[] { "unchanged", "description", "rename-before-preview", "rename-after-preview", "remap-after-preview", "manifest-after-preview", "game-after-preview", "duplicate-before-preview", "duplicate-after-preview", "remap-before-preview" })
    {
        var run = Path.Combine(runRoot, workflow, scenario);
        var game = Path.Combine(run, "game");
        var extra = Path.Combine(run, "extra-mods");
        Directory.CreateDirectory(extra);
        var mod = Path.Combine(scenario.StartsWith("remap") ? extra : Path.Combine(game, "mods"), "provider");
        var profilePath = Path.Combine(run, "profile_29");
        Directory.CreateDirectory(profilePath);
        BindingWriteResources(game, 2);
        BindingWriteResources(mod, 5);
        BindingWrite(mod, "project.xml", "<project><Title>Audit Provider</Title><Description>original</Description></project>");
        BindingWrite(mod, "modfiles.txt", "inventory/a.inventory.items.darkest\ntrinkets/a.entries.trinkets.json\n");
        var gameDoc = JsonNode.Parse("""{"base_root":{"inraid":true,"raiddungeon":"cove","game_mode":"base","applied_ugcs_1_0":{"0":{"name":"Audit Provider","source":"mod_local_source"}}}}""")!.AsObject();
        var gamePath = BindingWrite(profilePath, "persist.game.json", gameDoc.ToJsonString());
        var estateSeed = BindingWrite(run, "estate.seed.json", """{"base_root":{"version":1,"wallet":{},"estate_items":{"items":{}},"trinkets":{"items":{}}}}""");
        var raidSeed = BindingWrite(run, "raid.seed.json", """{"base_root":{"party":{"inventory":{"items":{}}}}}""");
        var estatePath = Path.Combine(profilePath, "persist.estate.json");
        var raidPath = Path.Combine(profilePath, "persist.raid.json");
        await codec.EncodeAsync(estateSeed, estatePath, null);
        await codec.EncodeAsync(raidSeed, raidPath, null);
        var profile = new SaveProfile("profile_29", profilePath, estatePath, "audit", DateTime.UtcNow);
        var locations = new SaveEditorLocations(run, Path.Combine(run, "work"), Path.Combine(run, "backups"));
        var active = await ActiveContentResolver.ResolveAsync(profile, game, null, extra, codec, locations.WorkspaceDirectory);
        var fingerprintBefore = ProfileCatalogContentFingerprint.Capture(active.Sources);
        var quantity = QuantityItemCatalog.LoadRaid(active, BindingRead(raidSeed)).Items.Single(x => x.ItemId == "audit_item");
        var trinkets = TrinketCatalog.Load(active);
        var trinket = trinkets.Trinkets.Single(x => x.Id == "audit_trinket");
        if (quantity.BaseStackLimit != 5 || trinket.QuestUses != 5) throw new Exception("Fixture failed Mod overlay");
        var service = new SaveEditService(codec, locations);
        var originalHash = BindingHash(workflow == "raid-item" ? raidPath : estatePath);
        void Mutate()
        {
            if (scenario == "description")
                BindingWrite(mod, "project.xml", "<project><Title>Audit Provider</Title><Description>changed</Description></project>");
            else if (scenario.StartsWith("rename") || scenario.StartsWith("remap"))
            {
                BindingWrite(mod, "project.xml", "<project><Title>Retired Provider</Title></project>");
                if (scenario.StartsWith("remap"))
                {
                    var replacement = Path.Combine(game, "mods", "replacement");
                    BindingWriteResources(replacement, 3);
                    BindingWrite(replacement, "project.xml", "<project><Title>Audit Provider</Title></project>");
                    BindingWrite(replacement, "modfiles.txt", "inventory/a.inventory.items.darkest\ntrinkets/a.entries.trinkets.json\n");
                }
            }
            else if (scenario.StartsWith("duplicate"))
                BindingWrite(Path.Combine(game, "mods", "duplicate"), "project.xml", "<project><Title>Audit Provider</Title></project>");
            else if (scenario == "manifest-after-preview") BindingWrite(mod, "modfiles.txt", "");
            else if (scenario == "game-after-preview")
            {
                gameDoc["base_root"]!["applied_ugcs_1_0"] = new JsonObject();
                File.WriteAllText(gamePath, gameDoc.ToJsonString());
            }
        }
        if (scenario.EndsWith("before-preview")) Mutate();
        string? error = null;
        SaveCommitResult? committed = null;
        PreparedQuantityItemEdit? itemEdit = null;
        PreparedTrinketEdit? trinketEdit = null;
        try
        {
            if (workflow == "raid-item") itemEdit = await service.PrepareQuantityItemEditAsync(profile, quantity, 5, active);
            else trinketEdit = await service.PrepareTrinketEditAsync(profile, trinket, 1, trinkets.Storage, active);
            if (!scenario.EndsWith("before-preview")) Mutate();
            committed = itemEdit is not null ? await service.CommitAsync(itemEdit) : await service.CommitAsync(trinketEdit!);
        }
        catch (Exception e) { error = e.GetType().Name + ": " + e.Message; }
        var fresh = await ActiveContentResolver.ResolveAsync(profile, game, null, extra, codec, locations.WorkspaceDirectory);
        var freshItem = QuantityItemCatalog.LoadRaid(fresh, BindingRead(raidSeed)).Items.Single(x => x.ItemId == "audit_item");
        var freshTrinket = TrinketCatalog.Load(fresh).Trinkets.Single(x => x.Id == "audit_trinket");
        var target = workflow == "raid-item" ? raidPath : estatePath;
        var decoded = Path.Combine(run, "final.decoded.json");
        await codec.DecodeAsync(target, decoded);
        var saved = BindingRead(decoded)["base_root"]!;
        var amounts = workflow == "raid-item" ? saved["party"]!["inventory"]!["items"]!.AsObject().Select(x => x.Value!["amount"]!.GetValue<int>()).ToArray() : [];
        var savedUses = workflow == "trinket" ? saved["trinkets"]!["items"]!.AsObject().Select(x => x.Value!["data"]?.ToJsonString() ?? x.Value!.ToJsonString()).ToArray() : [];
        var expectedBlocked = scenario is not ("unchanged" or "description");
        if (expectedBlocked != (committed is null) || (committed is null && originalHash != BindingHash(target)))
            throw new Exception("Unexpected control outcome: " + workflow + "/" + scenario + ": " + error);
        rows.Add(new { Workflow = workflow, Scenario = scenario, Committed = committed is not null, Error = error,
            BeforeValue = 5, FreshValue = workflow == "raid-item" ? freshItem.BaseStackLimit : freshTrinket.QuestUses,
            FreshSources = fresh.Sources.Select(x => x.Directory).ToArray(), FreshIssues = fresh.Issues,
            FingerprintChanged = fingerprintBefore != ProfileCatalogContentFingerprint.Capture(fresh.Sources),
            Amounts = amounts, SavedUses = savedUses, Target = target, Decoded = decoded,
            Backup = committed?.BackupDirectory, TargetChanged = originalHash != BindingHash(target) });
        Console.WriteLine($"PASS: source binding {workflow}/{scenario}: committed={committed is not null}; fresh={(workflow == "raid-item" ? freshItem.BaseStackLimit : freshTrinket.QuestUses)}; error={error}");
    }
    }

static string BindingHash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
static JsonObject BindingRead(string path) => JsonNode.Parse(File.ReadAllText(path))!.AsObject();
static string BindingWrite(string root, string relative, string text)
{
    var path = Path.Combine(root, relative);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, text);
    return path;
}
static void BindingWriteResources(string root, int value)
{
    BindingWrite(root, "inventory/a.inventory.items.darkest", $"inventory_item: .type estate .id audit_item .base_stack_limit {value} .estate_can_be_provision true\n");
    BindingWrite(root, "inventory/a.inventory.system_configs.darkest", "inventory_system_config: .type raid .max_slots 16\ninventory_system_config: .type trinket_storage .max_slots 100\n");
    BindingWrite(root, "trinkets/a.entries.trinkets.json", $$"""{"entries":[{"id":"audit_trinket","quest_uses":{{value}},"trigger_limit":{{value}}}]}""");
}

}
