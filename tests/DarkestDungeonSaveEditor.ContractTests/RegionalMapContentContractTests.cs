internal static partial class ContractSuite
{
    private static void RunRegionalMapContentContracts(string runRoot, ActiveContentSnapshot template)
    {
        var root = Path.Combine(runRoot, "regional-weights");
        var baseRoot = Path.Combine(root, "base");
        var modRoot = Path.Combine(root, "mod");
        WriteMapContentFixture(baseRoot, "dungeons/weald/weald.props.darkest", """
            traps: .chance 1 .types alpha
            traps: .chance 4 .types beta
            traps: .chance 3 .types alpha
            traps: .chance 0 .types zero
            traps: .chance -1 .types negative
            traps: .chance NaN .types nan
            traps: .chance Infinity .types infinity
            traps: .types absent
            traps: .chance 1 .chance 2 .types duplicate
            traps: .chance 100 .types missing_resource
            traps: .chance 100 .types scripted
            obstacles: .chance 1 .types thorny_thicket
            """);
        WriteMapContentFixture(baseRoot, "dungeons/cove/cove.props.darkest", """
            traps: .chance 1 .types alpha
            obstacles: .chance 1 .types shipwreck
            """);
        WriteMapContentFixture(baseRoot, "dungeons/multiple/multiple.props.darkest", """
            traps: .chance 4 .types alpha beta
            traps: .chance 4 .types gamma
            """);
        WriteMapContentFixture(baseRoot, "dungeons/large/large.props.darkest", """
            traps: .chance 1e38 .types alpha
            traps: .chance 1e38 .types beta
            """);
        WriteMapContentFixture(baseRoot, "dungeons/decimal/decimal.props.darkest", """
            traps: .chance 0.5 .types alpha
            traps: .chance 1.5 .types beta
            """);
        WriteMapContentFixture(baseRoot, "dungeons/override/override.props.darkest", """
            traps: .chance 100 .types alpha
            traps: .chance 2 .types beta
            """);
        WriteMapContentFixture(modRoot, "dungeons/override/extra.props.darkest", "traps: .chance 2 .types alpha");
        WriteMapContentFixture(baseRoot, "dungeons/overlay/overlay.props.darkest", "traps: .chance 100 .types alpha");
        WriteMapContentFixture(modRoot, "dungeons/overlay/overlay.props.darkest", "traps: .chance 1 .types beta");
        WriteMapContentFixture(baseRoot, "dungeons/repeated/repeated.props.darkest", "traps: .chance 4 .types alpha alpha beta");
        WriteMapContentFixture(baseRoot, "dungeons/overflow/overflow.props.darkest", "traps: .chance 1e40 .types alpha");
        WriteMapContentFixture(baseRoot, "dungeons/disabled/disabled.props.darkest", "traps: .chance 0 .types zero");
        WriteMapContentFixture(baseRoot, "props/prop_definitions.json", """
            { "props": [ { "name": "trap", "default_data": {} }, { "name": "obstacle", "default_data": {} } ] }
            """);
        var traps = new JsonArray();
        foreach (var id in new[] { "alpha", "beta", "gamma", "zero", "negative", "nan", "infinity", "absent", "duplicate", "scripted" })
        {
            var data = new JsonObject { ["inherits_from"] = new JsonObject { ["prop_type_name"] = "trap" } };
            if (id == "scripted") data["generate_ambush"] = "trap";
            traps.Add(new JsonObject { ["name"] = id, ["default_data"] = data });
        }
        WriteMapContentFixture(baseRoot, "props/trap_definitions.json", new JsonObject { ["props"] = traps }.ToJsonString());
        WriteMapContentFixture(baseRoot, "props/obstacle_definitions.json", """
            { "props": [
              { "name": "thorny_thicket", "default_data": { "inherits_from": { "prop_type_name": "obstacle" } } },
              { "name": "shipwreck", "default_data": { "inherits_from": { "prop_type_name": "obstacle" } } }
            ] }
            """);
        WriteFixtureManifest(modRoot);
        var catalog = BattleRoomAttachmentCatalog.Load(template with
        {
            Sources = [new ActiveContentSource("base", "Base", "base", baseRoot, 0),
                new ActiveContentSource("local:weighted", "Weighted", "local", modRoot, 1000)],
            SourceGameSha256 = ComputeSha256(Path.Combine(template.Profile.ProfileDirectory, "persist.game.json"))
        });
        Assert(catalog.GetRegionalCandidates(BattleRoomAttachmentKind.Trap, "WEALD")
                   .Select(item => item.Id).ToHashSet().SetEquals(["alpha", "beta", "duplicate"]) &&
               catalog.Issues.Count(issue => issue.StartsWith("地图区域资源未参与自动选择：", StringComparison.Ordinal)) == 3,
            "Automatic regional choices must reject zero/invalid weights and missing/scripted resources, without guessing another region.");
        AssertRegionalDistribution(catalog, "weald", new() { ["alpha"] = 400, ["beta"] = 400, ["duplicate"] = 200 });
        AssertRegionalDistribution(catalog, "multiple", new() { ["alpha"] = 400, ["beta"] = 400, ["gamma"] = 400 });
        AssertRegionalDistribution(catalog, "large", new() { ["alpha"] = 500, ["beta"] = 500 });
        AssertRegionalDistribution(catalog, "decimal", new() { ["alpha"] = 250, ["beta"] = 750 });
        AssertRegionalDistribution(catalog, "override", new() { ["alpha"] = 1000, ["beta"] = 20 });
        AssertRegionalDistribution(catalog, "repeated", new() { ["alpha"] = 800, ["beta"] = 400 });
        AssertRegionalDistribution(catalog, "overlay", new() { ["beta"] = 1000 });
        Assert(catalog.SelectRegionalContent(BattleRoomAttachmentKind.Obstacle, "weald", 0)!.Id == "thorny_thicket" &&
               catalog.SelectRegionalContent(BattleRoomAttachmentKind.Obstacle, "cove", 0.999)!.Id == "shipwreck" &&
               catalog.SelectRegionalContent(BattleRoomAttachmentKind.Trap, "cove", 0.7)!.Id == "alpha" &&
               catalog.SelectRegionalContent(BattleRoomAttachmentKind.Trap, "disabled", 0.5) is null &&
               catalog.SelectRegionalContent(BattleRoomAttachmentKind.Trap, "overflow", 0.5) is null &&
               catalog.SelectRegionalContent(BattleRoomAttachmentKind.Obstacle, "missing", 0.5) is null &&
               catalog.GetRegionalCandidates(BattleRoomAttachmentKind.Trap, "").Count == 0,
            "Single-resource regions select their own resource; zero-weight, missing and blank regions never fall back across regions.");

        var newGuard = catalog.Guard with { GameSaveSha256 = "rebound-profile-hash" };
        var rebound = catalog with
        {
            Guard = newGuard,
            Definitions = catalog.Definitions.Select(item => item with { CatalogGuard = newGuard }).ToArray()
        };
        var chosen = rebound.SelectRegionalContent(BattleRoomAttachmentKind.Trap, "weald", 0.5)!;
        Assert(ReferenceEquals(chosen.CatalogGuard, newGuard) && rebound.Definitions.Any(item => ReferenceEquals(item, chosen)),
            "Regional selection after automatic profile refresh must use rebound definitions and guards, not captured old instances.");
        foreach (var sample in new[] { -0.1, 1, double.NaN, double.PositiveInfinity })
        {
            var rejected = false;
            try { _ = catalog.SelectRegionalContent(BattleRoomAttachmentKind.Trap, "weald", sample); }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            Assert(rejected, "Regional selection must validate its random sample.");
        }
        Console.WriteLine("PASS: regional trap/obstacle weighted selection, exclusions, overlays, duplicate rows and rebound guards");
    }

    private static void AssertRegionalDistribution(
        BattleRoomAttachmentCatalogResult catalog, string region, Dictionary<string, int> expected)
    {
        var sampleCount = expected.Values.Sum();
        var counts = Enumerable.Range(0, sampleCount)
            .Select(index => catalog.SelectRegionalContent(BattleRoomAttachmentKind.Trap, region, (index + 0.5) / sampleCount)!.Id)
            .GroupBy(id => id).ToDictionary(group => group.Key, group => group.Count());
        Assert(counts.Count == expected.Count && expected.All(pair => counts.GetValueOrDefault(pair.Key) == pair.Value),
            $"Regional weights must preserve exact relative proportions for {region}.");
    }
}
