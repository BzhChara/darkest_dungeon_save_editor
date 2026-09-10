internal static partial class ContractSuite
{
    private static void RunMapPropJsonContracts(string runRoot, ActiveContentSnapshot template)
    {
        var root = Path.Combine(runRoot, "map-prop-json");
        var membersRoot = Path.Combine(root, "members");
        void Write(string path, string text) => WriteMapContentFixture(membersRoot, path, text);
        Write("dungeons/json/json.props.darkest", "traps: .chance 1 .types alpha beta bad_default bad_parent bad_flag\n");
        Write("props/prop_definitions.json", """
            {"default_data":{"instance_type":"trap","instance_type":"obstacle"},
             "default_data":{"instance_type":"obstacle"},
             "props":[{"name":"parent","name":"wrong_parent"}],"props":[]}
            """);
        Write("props/trap_definitions.json", """
            {"props":[
              {"name":"alpha","name":"wrong_alpha","default_data":{
                "inherits_from":{"prop_type_name":"parent","prop_type_name":"missing"},
                "inherits_from":{"prop_type_name":"missing"},
                "instance_type":"trap","instance_type":"obstacle",
                "generate_ambush":"","generate_ambush":"ambush",
                "teleport":false,"teleport":true,"ancestor_talk":false,"ancestor_talk":true},
               "default_data":{"instance_type":"obstacle"},
               "difficulty_variations":[],"difficulty_variations":null},
              {"name":"beta","default_data":{"inherits_from":{"prop_type_name":"parent"}},
               "difficulty_variations":[{"level":1,"level":99,"instance_type":"trap","instance_type":"obstacle"}]},
              {"name":"bad_default","default_data":null,"default_data":{"instance_type":"trap"}},
              {"name":"bad_parent","default_data":{"inherits_from":{"prop_type_name":null,"prop_type_name":"parent"}}},
              {"name":"bad_flag","default_data":{"instance_type":"trap","teleport":null,"teleport":false}}
            ],"props":[]}
            """);
        var content = template with { Sources = [new("base", "Map JSON", "base", membersRoot, 0)] };
        var catalog = BattleRoomAttachmentCatalog.Load(content);
        Assert(catalog.GetCandidates(BattleRoomAttachmentKind.Trap, "json").Select(t => t.Id).Order().SequenceEqual(new[] { "alpha", "beta" }) &&
               catalog.Issues.Any(i => i.Contains("default_data 不是对象")) && catalog.Issues.Any(i => i.Contains("资源继承缺少有效父名称")) &&
               catalog.Issues.Any(i => i.Contains("teleport 不是布尔值")),
            "Nested first JSON members must govern map defaults, names, inheritance, flags and difficulty fields without duplicate-key exceptions.");
        foreach (var definition in catalog.Definitions) BattleRoomAttachmentCatalog.ValidateDefinition(definition);

        foreach (var invalid in new[]
        {
            """{"props":[{"name":null,"name":"alpha"}]}""",
            """{"props":[{"name":"alpha","difficulty_variations":null,"difficulty_variations":[]}]}""",
            """{"props":[{"name":"alpha","difficulty_variations":[{"level":null,"level":1}]}]}"""
        })
        {
            Write("props/trap_definitions.json", invalid);
            var rejected = false;
            try { BattleRoomAttachmentCatalog.Load(content); } catch (InvalidDataException) { rejected = true; }
            Assert(rejected, "A structurally invalid first map JSON field must not be rescued by a later duplicate.");
        }
        Write("props/trap_definitions.json", """{"props":null,"props":[{"name":"alpha","default_data":{"instance_type":"trap"}}]}""");
        Assert(BattleRoomAttachmentCatalog.Load(content).Definitions.Count == 0,
            "A wrong-typed first props array must not consume the later array.");

        foreach (var kind in new[] { "base", "mode", "dlc-feature", "local", "workshop", "dlc-mod" })
        {
            var sourceRoot = Path.Combine(root, kind);
            var prefix = kind == "dlc-mod" ? "dlc/rq_feature/" : "";
            void Resource(string path, string text) => WriteMapContentFixture(sourceRoot, prefix + path, text);
            Resource("dungeons/query/query.props.darkest", "traps: .chance 1 .types alpha\nobstacles: .chance 1 .types stone\n");
            Resource("props/prop_definitions.json", """{"props":[{"name":"root_parent","default_data":{"instance_type":"trap"}}]}""");
            Resource("props/obstacle_definitions.json", """{"props":[{"name":"root_parent","default_data":{"instance_type":"obstacle"}}]}""");
            Resource("props/z_parent/prop_definitionsXjson", """{"props":[{"name":"nested_parent","default_data":{"inherits_from":{"prop_type_name":"root_parent"}}}]}""");
            Resource("props/a_child/trap_definitionsXjson", """{"props":[{"name":"alpha","default_data":{"inherits_from":{"prop_type_name":"nested_parent"}}}]}""");
            Resource("props/a_stone/obstacle_definitionsXjson", """{"props":[{"name":"stone","default_data":{"inherits_from":{"prop_type_name":"nested_parent"},"instance_type":"obstacle"}}]}""");
            foreach (var ignored in new[] { "props/trap_definitionsXjson", "props/notes.json", "props/nested/notes.json" })
                Resource(ignored, "{ broken");
            if (kind is "local" or "workshop" or "dlc-mod") WriteFixtureManifest(sourceRoot);
            var sources = QuerySources(sourceRoot, kind);
            var queryContent = template with { Sources = sources };
            var query = BattleRoomAttachmentCatalog.Load(queryContent);
            var alpha = query.GetCandidates(BattleRoomAttachmentKind.Trap, "query").Single();
            Assert(alpha.Id == "alpha" && query.GetCandidates(BattleRoomAttachmentKind.Obstacle, "query").Single().Id == "stone",
                $"{kind}: root opens precede nested prop/trap/obstacle query stages, even when filenames sort child before parent.");
            foreach (var definition in query.Definitions) BattleRoomAttachmentCatalog.ValidateDefinition(definition);
            var file = Path.Combine(sourceRoot, prefix + "props/a_child/trap_definitionsXjson");
            var before = ProfileCatalogContentFingerprint.Capture(sources);
            File.AppendAllText(file, " ");
            Assert(ProfileCatalogContentFingerprint.Capture(sources) != before,
                $"{kind}: queried prop names must participate in catalog refresh.");
            var staleRejected = false;
            try { BattleRoomAttachmentCatalog.ValidateDefinition(alpha); }
            catch (InvalidOperationException) { staleRejected = true; }
            Assert(staleRejected, $"{kind}: an old map choice must fail preflight after its queried resource changes.");
            var refreshed = BattleRoomAttachmentCatalog.Load(queryContent);
            BattleRoomAttachmentCatalog.ValidateDefinition(refreshed.GetCandidates(BattleRoomAttachmentKind.Trap, "query").Single());
        }

        foreach (var eligible in new[] { false, true })
        {
            var missingRoot = Path.Combine(root, "missing-" + eligible); Directory.CreateDirectory(missingRoot);
            File.WriteAllText(Path.Combine(missingRoot, "modfiles.txt"), eligible
                ? "props/addon/trap_definitionsXjson\n"
                : "props/trap_definitionsXjson\nprops/notes.json\nprops/addon/notes.json\n");
            var missingContent = template with { Sources = [new("local:map-missing", "Missing", "local", missingRoot, 0)] };
            Assert(BattleRoomAttachmentCatalog.Load(missingContent).Issues.Any(i => i.Contains("Room prop file listed by Mod is missing")) == eligible,
                "Only missing native-consumed prop paths should produce missing-resource diagnostics.");
        }
        Console.WriteLine("PASS: first map JSON members, inheritance/difficulty validation, six-source filename stages and stale-choice guards.");
    }
}
