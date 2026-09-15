using System.Reflection;
using DarkestDungeonSaveEditor.App;

internal static partial class ContractSuite
{
    // Runs inside the existing WPF host; no visible window or live save is used.
    private static async Task VerifyMissingResourceSyncAsync(ContractFixture fixture)
    {
        var root = Path.Combine(fixture.RunRoot, "missing-resource-ui");
        var baseline = Path.Combine(root, "base");
        var mod = Path.Combine(root, "mod");
        WriteMultiMash(baseline, "dungeons/weald/weald.props.darkest", "traps: .chance 1 .types probe_trap\n");
        WriteMultiMash(baseline, "monsters/probe/probe_A/probe_A.info.darkest", "display: .size 1\n");
        WriteMultiMash(baseline, "dungeons/weald/probe.weald.1.mash.darkest", "hall: .chance 1 .types probe_A\n");
        const string propText = """{"props":[{"name":"probe_trap","default_data":{"instance_type":"trap"}}]}""";
        var propFile = WriteMultiMash(mod, "props/probe/trap_definitions.json", propText);
        WriteFixtureManifest(mod);
        var content = QueryContent(root, [new("base", "Base", "base", baseline, 0), new("mod", "Mod", "local", mod, 0)]);
        var profile = content.Profile;
        var map = new BattleMapSnapshot(profile.ProfileDirectory, profile.MapSavePath, profile.RaidSavePath,
            "map-hash", "raid-hash", "weald", 1, 1, null, null, null, null, null, null, null, null, null, false, [], [], [], DateTime.UtcNow);
        var quantities = QuantityItemCatalog.LoadRaid(content,
            JsonNode.Parse("""{"base_root":{"party":{"inventory":{"items":{}}}}}""")!.AsObject());
        var snapshot = new ProfileCatalogSnapshot(content, "fixture", quantities,
            new Dictionary<string, string?> { ["persist.map.json"] = map.MapSha256, ["persist.raid.json"] = map.RaidSha256 }, DateTime.UtcNow);
        var view = new BattleMapView();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Get(string name) => typeof(BattleMapView).GetField(name, flags)!.GetValue(view);
        void Set(string name, object? value) => typeof(BattleMapView).GetField(name, flags)!.SetValue(view, value);
        Set("_profileDirectory", profile.ProfileDirectory);
        Set("_snapshotReader", new BattleMapSnapshotReader(fixture.Codec));
        Set("_currentSnapshot", map);
        Set("_isRaidAvailable", true);
        await view.SynchronizeProfileAsync(snapshot, contentChanged: true, CancellationToken.None);
        var original = (BattleRoomAttachmentCatalogResult)Get("_roomAttachmentCatalog")!;
        Assert(original.GetCandidates(BattleRoomAttachmentKind.Trap, "weald").Single().Id == "probe_trap",
            "The sync control must initially publish the readable Mod prop.");
        File.Delete(propFile);
        await view.SynchronizeProfileAsync(snapshot, contentChanged: true, CancellationToken.None);
        Assert(Get("_roomAttachmentCatalog") is null && ReferenceEquals(view.CurrentSnapshot, map) &&
            ((BattleEncounterCatalogResult)Get("_encounterCatalog")!).DirectEncounters.Count == 1 &&
            ReferenceEquals(Get("_activeContentSnapshot"), content),
            "An unreadable prop must clear stale placement choices without throwing into the shared profile retry loop or removing valid battles/maps.");
        File.WriteAllText(propFile, propText);
        await view.SynchronizeProfileAsync(snapshot, contentChanged: true, CancellationToken.None);
        var restored = (BattleRoomAttachmentCatalogResult)Get("_roomAttachmentCatalog")!;
        Assert(restored.GetCandidates(BattleRoomAttachmentKind.Trap, "weald").Single().Id == "probe_trap",
            "A restored provider must rebuild the unavailable prop catalog.");
        Console.WriteLine("PASS: WPF missing prop providers clear stale choices without blocking shared synchronization; valid map/battles and restoration remain available.");
    }
}
