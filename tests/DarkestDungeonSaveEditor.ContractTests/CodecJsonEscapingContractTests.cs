internal static partial class ContractSuite
{
    public static async Task RunCodecJsonEscapingOnlyAsync(string repositoryRoot)
    {
        var root = Path.Combine(repositoryRoot, "artifacts", "codec-json-contracts", Guid.NewGuid().ToString("N"));
        var codec = new DsonSaveCodec(Path.Combine(repositoryRoot, "tools", "DDSaveEditor", "DDSaveEditor.jar"));
        await RunCodecJsonEscapingContractsAsync(root, codec);
        Console.WriteLine($"Artifacts: {root}");
    }

    private static async Task RunCodecJsonEscapingContractsAsync(string runRoot, DsonSaveCodec codec)
    {
        var root = Path.Combine(runRoot, "codec-json-escaping");
        Directory.CreateDirectory(root);
        string[] keys = ["ordinary", "", "roo\\", "co[\\", "coV\\", "back\\slash", "two\\\\slashes",
            "quote\"key", "slash/key", "literal\\n", "actual\nnewline", "<>&='", "区域", "emoji-\U0001F600",
            "line\u2028separator", "paragraph\u2029separator"];
        keys = [.. keys, .. Enumerable.Range(0, 32).Select(value => $"control-{value}-" + (char)value)];
        var scalars = new JsonObject();
        var objects = new JsonObject();
        for (var index = 0; index < keys.Length; index++)
        {
            scalars[keys[index]] = 1024 + index;
            objects[keys[index]] = new JsonObject { ["id"] = 2048 + index };
        }
        var expected = new JsonObject
        {
            ["base_root"] = new JsonObject
            {
                ["scalars"] = scalars,
                ["objects"] = objects,
                ["static_save"] = new JsonObject
                {
                    ["base_root"] = new JsonObject
                    {
                        ["scalars"] = scalars.DeepClone(),
                        ["objects"] = objects.DeepClone()
                    }
                },
                ["ordinary_value"] = "unchanged"
            }
        };
        var seed = Path.Combine(root, "keys.seed.json");
        var binary = Path.Combine(root, "keys.dson");
        var decoded = Path.Combine(root, "keys.decoded.json");
        var roundTrip = Path.Combine(root, "keys.roundtrip.dson");
        var decodedAgain = Path.Combine(root, "keys.roundtrip.json");
        File.WriteAllText(seed, expected.ToJsonString(), new UTF8Encoding(false));
        await codec.EncodeAsync(seed, binary, null);
        SetRevision(binary, [0x00, 0x00, 0x9F, 0x6D]);
        var sourceHash = ComputeSha256(binary);
        await codec.DecodeAsync(binary, decoded);
        Assert(JsonNode.DeepEquals(expected, JsonNode.Parse(File.ReadAllText(decoded))),
            "All field names and values must survive DSON decoding, including embedded DSON and escape-like literal names.");
        await codec.EncodeAsync(decoded, roundTrip, binary);
        await codec.DecodeAsync(roundTrip, decodedAgain);
        Assert(JsonNode.DeepEquals(expected, JsonNode.Parse(File.ReadAllText(decodedAgain))) &&
            ReadRevision(binary).SequenceEqual(ReadRevision(roundTrip)) && ComputeSha256(binary) == sourceHash,
            "Encoding decoded JSON must preserve names and revision without modifying the source save.");
        Assert(File.ReadAllBytes(binary).SequenceEqual(File.ReadAllBytes(roundTrip)),
            "A codec-generated fixture must round-trip byte for byte, without doubling literal backslashes.");
        Console.WriteLine($"PASS: {keys.Length} JSON field names in scalar/object and embedded-DSON positions round-trip unchanged.");

        await VerifyEscapedBattleMapKeysAsync(root, codec);
    }

    private static async Task VerifyEscapedBattleMapKeysAsync(string root, DsonSaveCodec codec)
    {
        // The reported IDs occur in both the embedded static DSON and dynamic JSON.
        string[] areaIds = ["roo\\", "co[\\", "coV\\"];
        var staticAreas = new JsonObject();
        var dynamicAreas = new JsonObject();
        for (var index = 0; index < areaIds.Length; index++)
        {
            staticAreas[areaIds[index]] = new JsonObject
            {
                ["id"] = 1000 + index,
                ["kind"] = index == 0 ? 0 : 1,
                ["tiles"] = new JsonObject
                {
                    ["tile0"] = new JsonObject
                    {
                        ["type"] = index == 0 ? 3 : 1, ["obstacle"] = 0,
                        // The upstream writer requires float tokens for mappos.
                        ["mappos"] = JsonNode.Parse($"[{index}.0,0.0]")
                    }
                }
            };
            dynamicAreas[areaIds[index]] = JsonNode.Parse("""
                {"knowledge":3,"reversed":false,"tiles":{"tile0":{"content":0,"knowledge":3,"mash_index":-1,"mash_type":7}}}
                """);
        }
        var map = JsonNode.Parse("""
            {"base_root":{"map":{"bounds":[0.0,2.0,0.0,0.0],"entrance_id":1000,"final_room_id":1000,"static_dynamic":{}}}}
            """)!;
        var staticDynamic = map["base_root"]!["map"]!["static_dynamic"]!.AsObject();
        staticDynamic["static_save"] = new JsonObject { ["base_root"] = new JsonObject { ["areas"] = staticAreas } };
        staticDynamic["areas"] = dynamicAreas;
        var mapSeed = Path.Combine(root, "map.seed.json");
        var raidSeed = Path.Combine(root, "raid.seed.json");
        File.WriteAllText(mapSeed, map.ToJsonString(), new UTF8Encoding(false));
        File.WriteAllText(raidSeed, """
            {"base_root":{"raid_instance":{"dungeon":"cove","difficulty":2,"length":3},
            "in_area":1001,"areatile":0,"last_room_id":1000,"teleported":false,"inbattle":false,
            "in_doorway":{"area_to":1701736302,"tile_to":0,"implied":true},"camp":{"phase":0},
            "loot":{"queue":{},"queue_items":{"items":{}},"owned_items":{"items":{}}},
            "party":{"IsMovingLeft()":false,"retreat_room":1000,"heroes":[]}}}
            """, new UTF8Encoding(false));
        var mapProfile = Path.Combine(root, "map-profile");
        Directory.CreateDirectory(mapProfile);
        var mapBinary = Path.Combine(mapProfile, "persist.map.json");
        var raidBinary = Path.Combine(mapProfile, "persist.raid.json");
        await codec.EncodeAsync(mapSeed, mapBinary, null);
        await codec.EncodeAsync(raidSeed, raidBinary, null);
        var mapHash = ComputeSha256(mapBinary);
        var raidHash = ComputeSha256(raidBinary);
        var snapshot = await new BattleMapSnapshotReader(codec).LoadAsync(mapProfile);
        Assert(snapshot.Areas.Select(area => area.AreaId).ToHashSet(StringComparer.Ordinal).SetEquals(areaIds) &&
            snapshot.PartyAreaId == "co[\\" && snapshot.EntranceAreaId == "roo\\" && snapshot.Issues.Count == 0,
            "The map reader must join special static/dynamic keys and resolve party/entrance IDs without renaming them.");
        Assert(mapHash == ComputeSha256(mapBinary) && raidHash == ComputeSha256(raidBinary),
            "Loading the escaped map must leave the map and raid saves untouched.");
        Console.WriteLine("PASS: native map loading joins escaped static/dynamic area IDs and preserves source saves.");
    }
}
