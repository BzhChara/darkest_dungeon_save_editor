internal static partial class ContractSuite
{
    private static void RunMapResourceConsumerContracts(string runRoot, ActiveContentSnapshot template)
    {
        var root = Path.Combine(runRoot, "map-resource-consumers");
        var baseRoot = Path.Combine(root, "base");
        var highRoot = Path.Combine(root, "high");
        var lowRoot = Path.Combine(root, "low");
        string Write(string path, string text) => WriteMapContentFixture(baseRoot, path, text);
        Write("props/prop_definitions.json", """
            {"props":[
              {"name":"trap","default_data":{"instance_type":"trap"}},
              {"name":"obstacle","default_data":{"instance_type":"obstacle"}},
              {"name":"curio_default","default_data":{}},
              {"name":"seeded_ui","default_data":{"instance_type":"curio","ui_string":"old_name"}},
              {"name":"inline_kind","default_data":{"instance_type":"trap"}},
              {"name":"collision_Az","default_data":{"instance_type":"trap"}},
              {"name":"collision_BE","default_data":{"instance_type":"trap"}}
            ]}
            """);
        var trapPath = Write("props/trap_definitions.json", """
            {"default_data":{"inherits_from":{"prop_type_name":"trap"}},"props":[
              {"name":"file_default","default_data":{}},
              {"name":"same_default","default_data":{"teleport":false}},
              {"name":"same_default","default_data":{"teleport":true}},
              {"name":"first_scripted","default_data":{"teleport":true}},
              {"name":"first_scripted","default_data":{"teleport":false}},
              {"name":"exact_over_fallback","default_data":{"teleport":true}},
              {"name":"exact_over_fallback","default_data":{},"difficulty_variations":[]},
              {"name":"first_variations","default_data":{},"difficulty_variations":[]},
              {"name":"first_variations","default_data":{"teleport":true},"difficulty_variations":[]},
              {"name":"difficulty_script","default_data":{},"difficulty_variations":[{"level":3,"generate_ambush":"trap"}]},
              {"name":"variation_clear","default_data":{},"difficulty_variations":[{"level":3,"teleport":true},{"level":3,"teleport":false}]},
              {"name":"future_child","default_data":{"inherits_from":{"prop_type_name":"future_parent"}}},
              {"name":"future_parent","default_data":{}},
              {"name":"root_first","default_data":{"teleport":true}},
              {"name":"root_trap_parent","default_data":{}},
              {"name":"root_obstacle_child","default_data":{"inherits_from":{"prop_type_name":"root_obstacle_parent"}}},
              {"name":"wrong_kind","default_data":{"inherits_from":{"prop_type_name":"obstacle"}}},
              {"name":"hash_child","default_data":{"inherits_from":{"prop_type_name":"collision_Az"}}},
              {"name":"priority","default_data":{}},
              {"name":"invalid_flag","default_data":{"generate_ambush":true}},
              {"name":"query_parent","default_data":{"teleport":true},"difficulty_variations":[]},
              {"name":"query_parent","default_data":{}},
              {"name":"query_child","default_data":{"inherits_from":{"prop_type_name":"query_parent"}}},
              {"name":"nearest_parent","default_data":{"teleport":true},"difficulty_variations":[{"level":1,"teleport":false}]},
              {"name":"nearest_child","default_data":{"inherits_from":{"prop_type_name":"nearest_parent"}}},
              {"name":"old_parent","default_data":{}},
              {"name":"old_child","default_data":{"inherits_from":{"prop_type_name":"old_parent"}}},
              {"name":"old_parent","default_data":{"teleport":true}}
            ]}
            """);
        Write("props/obstacle_definitions.json", """
            {"props":[{"name":"root_obstacle_parent","default_data":{"instance_type":"trap"}}]}
            """);
        Write("props/stage/trap_definitions.json", """
            {"props":[{"name":"staged_child","default_data":{"inherits_from":{"prop_type_name":"staged_parent"}}}]}
            """);
        var asciiPrefix = new string('q', 63);
        var unicodePrefix = new string('界', 21);
        var ordinaryPrefix = new string('r', 63);
        var rootTraps = JsonNode.Parse(File.ReadAllText(trapPath))!.AsObject();
        foreach (var prefix in new[] { asciiPrefix, unicodePrefix, ordinaryPrefix })
        {
            var entries = rootTraps["props"]!.AsArray();
            entries.Add(new JsonObject { ["name"] = prefix + "suffix", ["default_data"] = new JsonObject { ["teleport"] = prefix != ordinaryPrefix } });
            entries.Add(new JsonObject { ["name"] = prefix, ["default_data"] = new JsonObject { ["teleport"] = prefix == ordinaryPrefix } });
        }
        File.WriteAllText(trapPath, rootTraps.ToJsonString());
        Write("props/clear/trap_definitions.json", """
            {"default_data":{"instance_type":"trap","teleport":true,"ancestor_talk":true,"generate_ambush":"trap"},"props":[
              {"name":"clear_defaults","default_data":{"teleport":false,"ancestor_talk":false,"generate_ambush":""}},
              {"name":"parent_replaces_defaults","default_data":{"inherits_from":{"prop_type_name":"trap"}}},
              {"name":"script_defaults","default_data":{}}
            ]}
            """);
        WriteMapContentFixture(highRoot, "props/stage/prop_definitions.json", """
            {"props":[
              {"name":"staged_parent","default_data":{"instance_type":"trap"}},
              {"name":"root_first","default_data":{"instance_type":"trap"}},
              {"name":"root_parent_child","default_data":{"inherits_from":{"prop_type_name":"root_trap_parent"}}}
            ]}
            """);
        WriteMapContentFixture(highRoot, "props/late/trap_definitions.json", """
            {"props":[{"name":"priority","default_data":{"instance_type":"obstacle"}}]}
            """);
        var curioFiles = WriteMapCurioFixtures(baseRoot, "a", "behavior", "old_behavior");
        File.WriteAllText(curioFiles.Props, "header\n" + """
            control,sprite,behavior,sprite
             leading,sprite,behavior,sprite
            three,sprite,behavior
            blank,sprite,behavior,
            duplicate,sprite,old_behavior,old_name
            duplicate,sprite,behavior,new_name
            keep_name,sprite,behavior,old_name
            keep_name,sprite,behavior,
            seeded_ui,sprite,behavior,
            short_source,sprite,behavior,old_name
            short_target,other_sprite
            skip_empty_type,sprite,behavior,old_name
            skip_empty_type,bad_sprite,,new_name
            "quote""toggle",sprite,behavior,sprite
            "comma,id",sprite,behavior,sprite
            trailing   ,sprite,behavior,sprite
            priority_curio,sprite,behavior,old_name
            """ + "\n\tleading_tab,sprite,behavior,sprite\n");
        // A different file has a fresh column buffer, even though object fields persist.
        Write("curios/fresh_curio_props.csv", "header\nfresh,sprite,behavior\nno_prior_type,sprite\n");
        Write("curios/two_curio_props.csv", "header\nfirst_two,sprite\n");
        Write("curios/headerless_curio_props.csv", "headerless,sprite,behavior\nafter_headerless,sprite,behavior\n");
        Write("curios/chunk_curio_props.csv", new string('h', 4095) + "after_chunk,sprite,behavior\n");
        Write("curios/physical_curio_props.csv", "header\nphysical,sprite,behavior,\"old_name\ncontinued\"\n" +
            "nul_row,sprite,behavior,sprite\0ignored,bad,missing\ncr_row,sprite,behavior,sprite\rignored,bad,missing\n");
        // No ITEM section => the second ID STRING is still inside the first block.
        Write("curios/block_curio_type_library.csv", """
            ,,ID STRING,,RESULT TYPES
            ,,block_first,,Unusual result
            ,,ID STRING,,RESULT TYPES
            ,,not_a_new_type,,Nothing
            ,,,,ITEM
            ,2,separator
            ,,id string,,RESULT TYPES
            ,,lowercase_header,,Nothing
            ,,,,ITEM
            ,3,separator
            ,,ID STRING,,RESULT TYPES
            ,,block_next,,
            ,,,,ITEM
            """);
        Write("curios/block_curio_props.csv", "header\nblock_first,sprite,block_first,sprite\n" +
            "not_a_new_type,sprite,not_a_new_type,sprite\nlowercase_header,sprite,lowercase_header,sprite\nblock_next,sprite,block_next,sprite\n");
        // Same-path overlay retains the base slot. A lower Mod's distinct file then updates the object.
        WriteMapContentFixture(highRoot, "curios/a_curio_props.csv", File.ReadAllText(curioFiles.Props));
        var lowerMapping = WriteMapContentFixture(lowRoot, "curios/z_curio_props.csv", "header\npriority_curio,sprite,behavior,new_name\n");
        WriteFixtureManifest(highRoot);
        WriteFixtureManifest(lowRoot);
        Write("localization/resource.string_table.xml", """
            <root><language id="english">
              <entry id="str_curio_title_sprite">Sprite name</entry>
              <entry id="str_curio_title_old_name">Old name</entry>
              <entry id="str_curio_title_new_name">New name</entry>
            </language></root>
            """);
        var trapIds = new[] { "inline_kind", "file_default", "same_default", "first_scripted", "exact_over_fallback", "first_variations",
            "difficulty_script", "variation_clear", "future_child", "staged_child", "wrong_kind", "hash_child", "priority", "invalid_flag",
            "query_parent", "query_child", "nearest_parent", "nearest_child", "old_child", "clear_defaults", "parent_replaces_defaults", "script_defaults", "collision_Az",
            "root_first", "root_parent_child", "root_obstacle_child", asciiPrefix, unicodePrefix, ordinaryPrefix };
        var curioIds = new[] { "control", " leading", "leading", "three", "blank", "duplicate", "keep_name", "seeded_ui", "short_target", "skip_empty_type",
            "quotetoggle", "comma,id", "trailing", "\tleading_tab", "priority_curio", "fresh", "no_prior_type", "first_two", "headerless", "after_headerless", "after_chunk",
            "physical", "nul_row", "cr_row", "block_first", "not_a_new_type", "lowercase_header", "block_next" };
        string Tokens(IEnumerable<string> ids) => string.Join(' ', ids.Select(id => "\"" + id + "\""));
        Write("dungeons/probe/probe.props.darkest", "traps: .chance 1 .types " + Tokens(trapIds) + "\nroom_curios: .chance 1 .types " + Tokens(curioIds) + "\n");
        var content = template with { Sources = [
            new("base", "Base", "base", baseRoot, 0), new("local:high", "High", "local", highRoot, 1000),
            new("local:low", "Low", "local", lowRoot, 1001)] };
        var catalog = BattleRoomAttachmentCatalog.Load(content);
        var incorrectlyExposed = catalog.Traps.Where(row => row.Id == "root_first" || row.Id == asciiPrefix || row.Id == unicodePrefix).Select(row => row.Id).ToArray();
        Assert(incorrectlyExposed.Length == 0,
            "Root JSON must precede discovered families, and JSON registration must use the native 63-byte name: " + string.Join(",", incorrectlyExposed));
        Assert(catalog.Traps.Select(row => row.Id).ToHashSet(StringComparer.Ordinal).SetEquals([
            "inline_kind", "file_default", "same_default", "exact_over_fallback", "first_variations", "variation_clear", "staged_child", "priority",
            "query_child", "nearest_child", "old_child", "clear_defaults", "parent_replaces_defaults", "root_parent_child", "root_obstacle_child", ordinaryPrefix]),
            "JSON props must apply staged file/entry defaults, copy already loaded parents, select first exact/nearest versions and inspect effective script flags.");
        Assert(catalog.Curios.Select(row => row.Id).ToHashSet(StringComparer.Ordinal).SetEquals([
            "control", " leading", "three", "blank", "duplicate", "keep_name", "seeded_ui", "short_target", "skip_empty_type", "quotetoggle", "comma,id", "trailing", "\tleading_tab",
            "priority_curio", "fresh", "no_prior_type", "after_headerless", "after_chunk", "physical", "nul_row", "cr_row", "block_first", "block_next"]),
            "Curio CSV must use physical chunks, native quote/space rules, persistent mapping columns, fresh file buffers and native type-block boundaries.");
        string Name(string id) => catalog.Curios.Single(row => row.Id == id).EnglishName;
        Assert(new[] { "control", "three", "blank", "fresh", "after_chunk", "quotetoggle" }.All(id => Name(id) == "Sprite name") &&
               new[] { "keep_name", "seeded_ui", "short_target", "skip_empty_type", "physical" }.All(id => Name(id) == "Old name") &&
               Name("duplicate") == "New name" && Name("priority_curio") == "New name",
            "Explicit UI names update; empty cells preserve JSON/previous CSV names, first empty names use sprites, and file order controls updates after overlay.");
        foreach (var definition in catalog.Definitions) BattleRoomAttachmentCatalog.ValidateDefinition(definition);
        foreach (var id in new[] { "root_first", asciiPrefix, unicodePrefix })
        {
            var forged = catalog.Traps.Single(row => row.Id == "file_default") with
            { Id = id, PropHash = unchecked((int)Loc2LocalizationReader.HashName(id)) };
            var blocked = false;
            try { BattleRoomAttachmentCatalog.ValidateDefinition(forged); } catch (InvalidOperationException) { blocked = true; }
            Assert(blocked, "Write preflight must also reject the scripted root/truncated-name winner, even if a caller forges a selected row.");
        }
        var dlcRoot = Path.Combine(root, "dlc-root");
        WriteMapContentFixture(dlcRoot, "props/trap_definitions.json", File.ReadAllText(trapPath));
        var dlcContent = content with { Sources = content.Sources.Append(
            new ActiveContentSource("dlc:root-props", "Root props DLC", "dlc", dlcRoot, 1)
            { VirtualPathPrefix = "dlc/root-props" }).ToArray() };
        var dlcCatalog = BattleRoomAttachmentCatalog.Load(dlcContent);
        Assert(dlcCatalog.Traps.All(row => row.Id != "root_first") && dlcCatalog.Traps.Any(row => row.Id == "root_parent_child"),
            "Mounted DLC root files must keep the independent root-loading stage rather than become nested JSON because of their physical prefix.");
        BattleRoomAttachmentCatalog.ValidateDefinition(dlcCatalog.Traps.Single(row => row.Id == "root_parent_child"));
        var selection = catalog.Curios.Single(row => row.Id == "priority_curio");
        File.WriteAllText(lowerMapping, "header\npriority_curio,sprite,missing_type,new_name\n");
        var rejected = false;
        try { BattleRoomAttachmentCatalog.ValidateDefinition(selection); } catch (InvalidOperationException) { rejected = true; }
        Assert(rejected && !BattleRoomAttachmentCatalog.Load(content).Curios.Any(row => row.Id == "priority_curio"),
            "A changed final mapping must invalidate an old choice and must not revive an earlier valid mapping.");
        var invalidCsv = Write("curios/invalid_curio_props.csv", "header\n" + new string('x', 512) + ",sprite,behavior\n");
        void RejectCatalog(string message)
        {
            var failed = false;
            try { BattleRoomAttachmentCatalog.Load(content); } catch (InvalidDataException) { failed = true; }
            Assert(failed, message);
        }
        RejectCatalog("Overlong native CSV fields cannot be silently truncated or skipped into a partial catalog.");
        File.WriteAllBytes(invalidCsv, [.. Encoding.UTF8.GetBytes("header\ninvalid,sprite,"), 0xff, (byte)'\n']);
        RejectCatalog("Invalid UTF-8 in a consumed CSV field must fail clearly without manufacturing a replacement identity.");
        File.WriteAllText(invalidCsv, "header\n");
        File.WriteAllText(trapPath, """{"props":[{"name":"file_default","difficulty_variations":[{"level":8}]}]}""");
        RejectCatalog("An out-of-range variation must not be silently ignored or exposed as a valid ordinary trap.");
        File.WriteAllText(trapPath, new JsonObject { ["props"] = new JsonArray(new JsonObject
        { ["name"] = new string('x', 62) + "中", ["default_data"] = new JsonObject { ["instance_type"] = "trap" } }) }.ToJsonString());
        RejectCatalog("A JSON name cut through a UTF-8 sequence must not manufacture a different registered identity.");
        Console.WriteLine("PASS: native Curio CSV buffers/blocks/updates and staged JSON prop defaults, difficulty lookup, scripts, collisions and guards.");
    }
}
