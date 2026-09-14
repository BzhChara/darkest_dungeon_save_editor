internal static partial class ContractSuite
{
    private static void RunMapPropNativeContracts(string runRoot, ActiveContentSnapshot template)
    {
        var root = Path.Combine(runRoot, "native-prop-pools");
        var baseRoot = Path.Combine(root, "base");
        var modRoot = Path.Combine(root, "mod");
        var lowModRoot = Path.Combine(root, "low-mod");
        var dlcRoot = Path.Combine(root, "dlc");
        const string prefix = "dlc/feature_pack/features/props";
        string Write(string relative, string text) => WriteMapContentFixture(baseRoot, relative, text);
        void Pool(string region, string text) => Write($"dungeons/{region}/{region}.props.darkest", text);

        Pool("multiline", "traps:\n .chance 1\n .types alpha\n");
        Pool("comments", "/*\ntraps: .chance 1 .types beta\n*/\ntraps: .chance 1 .types alpha\n");
        Pool("records", "// skip\n/*\ntraps: .chance 1 .types gamma\n*/\n" +
            "traps:\n .chance 1\n .types alpha\ntraps: .chance 2 .types beta traps: .chance 3 .types alpha\n");
        Pool("last_types", "traps: .chance 1 .types alpha .types beta\n");
        Pool("last_chance", "traps: .chance 1 .chance 3 .types alpha\ntraps: .chance 1 .types beta\n");
        Pool("percent", "traps: .chance 50% .types alpha\ntraps: .chance 0.5suffix .types beta\n");
        Pool("empty", "traps: .chance 1 .types alpha \"\" beta\n");
        Pool("limit", "traps: .chance 1 .types " + string.Join(' ', Enumerable.Repeat("alpha", 64)) + " beta\n");
        Pool("case_fields", "TRAPS: .chance 1 .types beta\ntraps: .chance 1 .TYPES gamma\ntraps: .chance 1 .types alpha\n");
        Pool("prefix_dispatch", "traps_extra: .chance 1 .types alpha\ntraps: .chance 1 .types beta\n" +
            "xtraps: .chance 1 .types gamma\nTRAPS_extra: .chance 1 .types gamma\nobstacles_extra: .chance 1 .types stone\n");
        Pool("copy_template", "traps: .chance 1 .types alpha\n");
        Pool("nul_terminated", "traps: .chance 1 .types alpha\n\0traps: .chance 1 .types beta\n");
        var invalidUtf8 = Write("dungeons/invalid_utf8/invalid_utf8.props.darkest", "");
        File.WriteAllBytes(invalidUtf8, [0xff, .. Encoding.UTF8.GetBytes("traps: .chance 1 .types alpha\n")]);
        var invalidAfterNul = Write("dungeons/bytes_after_nul/bytes_after_nul.props.darkest", "");
        File.WriteAllBytes(invalidAfterNul, [.. Encoding.UTF8.GetBytes("traps: .chance 1 .types alpha\n"), 0, 0xff]);
        Pool("identity", "traps: .chance 1 .types alpha ALPHA \" alpha \" .dot\n");
        var longId = new string('q', 63);
        Pool("long_id", $"traps: .chance 1 .types {longId}suffix\n");
        Pool("split_utf8", $"traps: .chance 1 .types {new string('q', 62)}中 alpha\n");
        Pool("collision", "traps: .chance 1 .types Az BE\n");
        Pool("overlay", "traps: .chance 1 .types alpha\n");
        Pool("ignored", "traps: .chance 1 .types alpha\n");
        Pool("arena", "traps: .chance 1 .types gamma\n");
        Write("dungeons/ignored/extra.props.darkest", "traps: .chance 1 .types gamma\n");
        Write("dungeons/ignored/backup/ignored.props.darkest", "traps: .chance 1 .types gamma\n");
        Write("props/prop_definitions.json", """{"props":[{"name":"trap","default_data":{"instance_type":"trap"}}]}""");
        var props = new JsonArray();
        foreach (var id in new[] { "alpha", "ALPHA", " alpha ", "beta", "gamma", ".dot", longId, "Az", "BE" })
            props.Add(new JsonObject { ["name"] = id, ["default_data"] = new JsonObject
            { ["inherits_from"] = new JsonObject { ["prop_type_name"] = "trap" } } });
        Write("props/trap_definitions.json", new JsonObject { ["props"] = props }.ToJsonString());
        Write("props/obstacle_definitions.json", """
            {"props":[{"name":"obstacle","default_data":{"instance_type":"obstacle"}},
                      {"name":"stone","default_data":{"inherits_from":{"prop_type_name":"obstacle"}}}]}
            """);
        Write("localization/props.string_table.xml", """
            <root><language id="english">
              <entry id="str_curio_title_alpha">Lower trap</entry>
              <entry id="str_curio_title_ALPHA">Upper trap</entry>
              <entry id="str_curio_title_ alpha ">Spaced trap</entry>
            </language></root>
            """);
        WriteMapContentFixture(dlcRoot, "dungeons/overlay/overlay.props.darkest", "traps: .chance 1 .types gamma\n");
        WriteMapContentFixture(dlcRoot, "dungeons/dlc_template/dlc_template.props.darkest", "traps: .chance 1 .types beta\n");
        var highPath = WriteMapContentFixture(modRoot, "dungeons/overlay/overlay.props.darkest", "traps: .chance 1 .types beta\n");
        var lowPath = WriteMapContentFixture(lowModRoot, "dungeons/overlay/overlay.props.darkest", "traps: .chance 1 .types gamma\n");
        var extraPath = WriteMapContentFixture(modRoot, "dungeons/ignored/extra.props.darkest", "traps: .chance 1 .types gamma\n");
        WriteMapContentFixture(modRoot, prefix + "/dungeons/dlc_pool/dlc_pool.props.darkest", "traps: .chance 1 .types beta\n");
        WriteMapContentFixture(modRoot, "dlc/disabled/dungeons/hidden/hidden.props.darkest", "traps: .chance 1 .types gamma\n");
        WriteFixtureManifest(modRoot);
        WriteFixtureManifest(lowModRoot);
        // A physical canonical file omitted from the manifest must remain unused.
        WriteMapContentFixture(modRoot, "dungeons/unlisted/unlisted.props.darkest", "traps: .chance 1 .types gamma\n");
        var content = template with
        {
            Sources = [new("base", "Base", "base", baseRoot, 0),
                new("dlc:props", "Props DLC", "dlc-feature", dlcRoot, 1) { VirtualPathPrefix = prefix },
                new("local:props", "Props", "local", modRoot, 1000),
                new("local:low-props", "Low Props", "local", lowModRoot, 1001)],
            SourceGameSha256 = ComputeSha256(Path.Combine(template.Profile.ProfileDirectory, "persist.game.json"))
        };
        var catalog = BattleRoomAttachmentCatalog.Load(content);
        IReadOnlyList<BattleRoomAttachmentDefinition> Rows(string region) =>
            catalog.GetCandidates(BattleRoomAttachmentKind.Trap, region);
        void Ids(string region, params string[] expected) => Assert(
            Rows(region).Select(row => row.Id).ToHashSet(StringComparer.Ordinal).SetEquals(expected),
            $"Native prop pool membership must match {region}.");

        foreach (var region in new[] { "multiline", "comments", "empty", "limit", "case_fields", "nul_terminated", "bytes_after_nul", "ignored", "split_utf8" })
            Ids(region, "alpha");
        Ids("records", "alpha", "beta");
        Ids("last_types", "beta");
        Ids("prefix_dispatch", "alpha", "beta");
        Assert(catalog.GetCandidates(BattleRoomAttachmentKind.Obstacle, "prefix_dispatch").Single().Id == "stone",
            "Prop header dispatch must use a case-sensitive prefix for obstacles too.");
        Ids("copy_template", "alpha");
        Ids("dlc_template", "beta");
        Ids("identity", "alpha", "ALPHA", " alpha ", ".dot");
        Ids("long_id", longId);
        Ids("overlay", "beta");
        // A prefixed Mod key cannot answer the region's unprefixed canonical request.
        foreach (var region in new[] { "dlc_pool", "arena", "hidden", "unlisted", "collision", "invalid_utf8" }) Ids(region);
        Assert(catalog.Issues.Any(issue => issue.Contains("UTF-8", StringComparison.Ordinal)) &&
               catalog.Issues.Any(issue => issue.Contains("hash collision", StringComparison.Ordinal)),
            "A split UTF-8 native token and a true native hash collision must remain diagnosed and unavailable.");
        var first = Rows("records").Single(row => row.Id == "alpha");
        var second = Rows("records").Single(row => row.Id == "beta");
        Assert(first.SourceLine == 5 && first.SourceRecordIndex == 0 &&
               second.SourceLine == 8 && second.SourceRecordIndex == 1,
            "Log provenance must retain physical source lines across removed comments and multiline/multiple declarations.");
        AssertRegionalDistribution(catalog, "records", new() { ["alpha"] = 800, ["beta"] = 400 });
        AssertRegionalDistribution(catalog, "last_chance", new() { ["alpha"] = 900, ["beta"] = 300 });
        AssertRegionalDistribution(catalog, "percent", new() { ["alpha"] = 600, ["beta"] = 600 });
        AssertRegionalDistribution(catalog, "prefix_dispatch", new() { ["alpha"] = 600, ["beta"] = 600 });
        Assert(Rows("identity").Single(row => row.Id == "alpha").EnglishName == "Lower trap" &&
               Rows("identity").Single(row => row.Id == "ALPHA").EnglishName == "Upper trap" &&
               Rows("identity").Single(row => row.Id == " alpha ").EnglishName == "Spaced trap",
            "Map localization requests must preserve exact case and spaces.");
        Assert(Rows("identity").Single(row => row.Id == " alpha ").PropHash == -925614370 &&
               Rows("identity").Single(row => row.Id == "alpha").PropHash == 781775590,
            "Map resource hashes must preserve their original UTF-8 identities.");
        foreach (var definition in catalog.Definitions) BattleRoomAttachmentCatalog.ValidateDefinition(definition);
        // All 64 repeated alpha entries are legitimate: they must not make validation ambiguous.
        BattleRoomAttachmentCatalog.ValidateDefinition(Rows("limit").Single());

        void Reject(Action action, string message)
        {
            var rejected = false;
            try { action(); }
            catch (Exception error) when (error is IOException or InvalidOperationException) { rejected = true; }
            Assert(rejected, message);
        }
        Reject(() => BattleRoomAttachmentCatalog.ValidateDefinition(first with { SourceRecordIndex = 17 }),
            "Forged record provenance must not pass current-definition validation.");
        var ignored = Rows("ignored").Single();
        Reject(() => BattleRoomAttachmentCatalog.ValidateDefinition(ignored with
        {
            Id = "gamma", PropHash = unchecked((int)Loc2LocalizationReader.HashName("gamma")),
            SourcePath = extraPath, SourceRelativePath = "dungeons/ignored/extra.props.darkest",
            SourceSha256 = ComputeSha256(extraPath), SourceLine = 1, SourceRecordIndex = 0
        }), "A forged definition from an unused basename must fail write preflight.");
        Reject(() => BattleRoomAttachmentCatalog.ValidateDefinition(
            Rows("identity").Single(row => row.Id == " alpha ") with { PropHash = 781775590 }),
            "A trimmed hash cannot be substituted for a spaced resource.");
        File.AppendAllText(extraPath, "traps: .chance 1 .types beta\n");
        BattleRoomAttachmentCatalog.ValidateDefinition(ignored);
        Assert(BattleRoomAttachmentCatalog.Load(content).Guard.Fingerprint == catalog.Guard.Fingerprint,
            "Unused prop basenames must not participate in the map content guard.");

        var before = ProfileCatalogContentFingerprint.Capture(content.Sources);
        File.AppendAllText(highPath, "traps: .chance 1 .types alpha\n");
        Assert(ProfileCatalogContentFingerprint.Capture(content.Sources) != before,
            "Changing a canonical pool must trigger catalog synchronization without changing the manifest.");
        Reject(() => BattleRoomAttachmentCatalog.ValidateDefinition(Rows("overlay").Single()),
            "Changed canonical pools must invalidate old choices before writes.");
        catalog = BattleRoomAttachmentCatalog.Load(content);
        Ids("overlay", "alpha", "beta");
        foreach (var item in Rows("overlay")) BattleRoomAttachmentCatalog.ValidateDefinition(item);

        foreach (var (sourceRoot, region) in new[] { (baseRoot, "copy_template"), (dlcRoot, "dlc_template") })
        {
            var directPath = Path.Combine(sourceRoot, "dungeons", region, region + ".props.darkest");
            var oldDefinition = Rows(region).Single();
            var originalPool = File.ReadAllText(directPath);
            var originalTime = File.GetLastWriteTimeUtc(directPath);
            var directBefore = ProfileCatalogContentFingerprint.Capture(content.Sources);
            File.WriteAllText(directPath, originalPool.Replace(".chance 1", ".chance 2", StringComparison.Ordinal));
            File.SetLastWriteTimeUtc(directPath, originalTime);
            Assert(ProfileCatalogContentFingerprint.Capture(content.Sources) != directBefore,
                "Canonical Base/DLC pools excluded by FindFiles must still trigger refresh on same-size/timestamp changes.");
            Reject(() => BattleRoomAttachmentCatalog.ValidateDefinition(oldDefinition),
                "A changed canonical direct-open pool must invalidate its old source guard.");
            catalog = BattleRoomAttachmentCatalog.Load(content);
            BattleRoomAttachmentCatalog.ValidateDefinition(Rows(region).Single());
        }

        var highText = File.ReadAllText(highPath);
        var lowText = File.ReadAllText(lowPath);
        File.Delete(lowPath);
        _ = BattleRoomAttachmentCatalog.Load(content); // A missing, fully shadowed source is not the effective file.
        File.Delete(highPath);
        Reject(() => BattleRoomAttachmentCatalog.Load(content),
            "An unreadable winning canonical pool must not revive lower Base/DLC bytes.");
        File.WriteAllText(highPath, highText);
        File.WriteAllText(lowPath, lowText);
        var swapped = content with { Sources = content.Sources.Select(source =>
            source.Id == "local:low-props" ? source with { LoadOrder = 999 } : source).ToArray() };
        Assert(BattleRoomAttachmentCatalog.Load(swapped).GetCandidates(BattleRoomAttachmentKind.Trap, "overlay")
                .Single().Id == "gamma", "Changing Mod order must select the winning whole canonical pool.");
        Console.WriteLine("PASS: native map prop paths, fields, limits, identities, weights, names, source lines, refresh and guards");
    }
}
