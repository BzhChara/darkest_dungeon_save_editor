using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace DarkestDungeonSaveEditor.Core;

public sealed class BattleMapSnapshotReader(DsonSaveCodec codec)
{
    private const string MapFileName = "persist.map.json";
    private const string RaidFileName = "persist.raid.json";
    private static readonly TimeSpan PairVerificationDelay = TimeSpan.FromMilliseconds(350);

    public async Task<BattleMapSnapshot> LoadAsync(
        string profileDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileDirectory);
        profileDirectory = Path.GetFullPath(profileDirectory);
        var mapPath = Path.Combine(profileDirectory, MapFileName);
        var raidPath = Path.Combine(profileDirectory, RaidFileName);
        if (!File.Exists(mapPath) || !File.Exists(raidPath))
        {
            throw new FileNotFoundException(
                "The selected profile does not currently contain both persist.map.json and persist.raid.json.");
        }

        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "DarkestDungeonSaveEditor",
            "battle-map-read",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            var mapCopyPath = Path.Combine(temporaryDirectory, "persist.map.source");
            var raidCopyPath = Path.Combine(temporaryDirectory, "persist.raid.source");
            var mapSha256 = CopyStable(mapPath, mapCopyPath);
            var raidSha256 = CopyStable(raidPath, raidCopyPath);
            // The game writes the two documents separately. Require the captured pair to remain
            // unchanged through a short quiet window so map(new)+raid(old) is rejected and retried.
            await Task.Delay(PairVerificationDelay, cancellationToken).ConfigureAwait(false);
            if (!ComputeSha256(mapPath).Equals(mapSha256, StringComparison.OrdinalIgnoreCase) ||
                !ComputeSha256(raidPath).Equals(raidSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("Map or raid save changed while the paired snapshot was being captured.");
            }
            var decodedMapPath = Path.Combine(temporaryDirectory, MapFileName);
            var decodedRaidPath = Path.Combine(temporaryDirectory, RaidFileName);
            await codec.DecodeAsync(mapCopyPath, decodedMapPath, cancellationToken).ConfigureAwait(false);
            await codec.DecodeAsync(raidCopyPath, decodedRaidPath, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            return Parse(
                profileDirectory,
                mapPath,
                raidPath,
                mapSha256,
                raidSha256,
                JsonSupport.ReadObject(decodedMapPath),
                JsonSupport.ReadObject(decodedRaidPath));
        }
        finally
        {
            TryDeleteTemporaryDirectory(temporaryDirectory);
        }
    }

    internal static BattleMapSnapshot Parse(
        string profileDirectory,
        string mapSavePath,
        string raidSavePath,
        string mapSha256,
        string raidSha256,
        JsonObject mapDocument,
        JsonObject raidDocument)
    {
        var issues = new List<string>();
        var structuralIssues = new List<string>();
        void AddStructuralIssue(string message)
        {
            issues.Add(message);
            structuralIssues.Add(message);
        }

        var mapRoot = JsonSupport.RequireObject(mapDocument, "base_root", "map");
        var staticDynamic = JsonSupport.RequireObject(mapRoot, "static_dynamic");
        var staticAreas = JsonSupport.RequireObject(
            staticDynamic,
            "static_save",
            "base_root",
            "areas");
        var dynamicAreas = JsonSupport.RequireObject(staticDynamic, "areas");
        var raidRoot = JsonSupport.RequireObject(raidDocument, "base_root");
        var raidInstance = raidRoot["raid_instance"] as JsonObject;
        if (raidInstance is null)
        {
            AddStructuralIssue("The raid save has no raid_instance object.");
        }

        var areaHashes = new Dictionary<int, string>();
        var areas = new List<BattleMapAreaSnapshot>();
        foreach (var staticAreaEntry in staticAreas.OrderBy(entry => NaturalKey(entry.Key)))
        {
            if (staticAreaEntry.Value is not JsonObject staticArea)
            {
                AddStructuralIssue($"Static map area {staticAreaEntry.Key} is not an object.");
                continue;
            }

            var areaId = staticAreaEntry.Key;
            var areaHash = ReadInt(staticArea, "id", 0);
            if (areaHash == 0)
            {
                AddStructuralIssue($"Static map area {areaId} has no valid id hash.");
            }
            else if (!areaHashes.TryAdd(areaHash, areaId))
            {
                AddStructuralIssue(
                    $"Map area hash {areaHash} is shared by {areaHashes[areaHash]} and {areaId}.");
            }

            var areaKind = ResolveAreaKind(areaId, ReadInt(staticArea, "kind", -1));
            if (areaKind == BattleMapAreaKind.Unknown)
            {
                AddStructuralIssue($"Static map area {areaId} has an unknown kind.");
            }
            var dynamicArea = dynamicAreas[areaId] as JsonObject;
            if (dynamicArea is null)
            {
                AddStructuralIssue($"Dynamic map area {areaId} is missing.");
            }

            var rawAreaKnowledge = ReadInt(dynamicArea, "knowledge", 0);
            var staticTiles = staticArea["tiles"] as JsonObject;
            var dynamicTiles = dynamicArea?["tiles"] as JsonObject;
            if (staticTiles is null)
            {
                AddStructuralIssue($"Static map area {areaId} has no tiles object.");
                continue;
            }

            var tiles = new List<BattleMapTileSnapshot>();
            foreach (var staticTileEntry in staticTiles.OrderBy(entry => NaturalKey(entry.Key)))
            {
                if (staticTileEntry.Value is not JsonObject staticTile)
                {
                    AddStructuralIssue(
                        $"Static map tile {areaId}.{staticTileEntry.Key} is not an object.");
                    continue;
                }

                var position = ReadDoubleArray(staticTile, "mappos");
                if (position.Count < 2)
                {
                    AddStructuralIssue(
                        $"Static map tile {areaId}.{staticTileEntry.Key} has no two-value mappos.");
                    continue;
                }

                var dynamicTile = dynamicTiles?[staticTileEntry.Key] as JsonObject;
                if (dynamicTile is null)
                {
                    AddStructuralIssue(
                        $"Dynamic map tile {areaId}.{staticTileEntry.Key} is missing.");
                }

                var rawKnowledge = ReadInt(dynamicTile, "knowledge", rawAreaKnowledge);
                var rawContent = ReadInt(dynamicTile, "content", 0);
                tiles.Add(new BattleMapTileSnapshot(
                    staticTileEntry.Key,
                    ParseTrailingIndex(staticTileEntry.Key),
                    position[0],
                    position[1],
                    ReadInt(staticTile, "type", -1),
                    ReadInt(staticTile, "cur", 0),
                    ReadInt(staticTile, "obstacle", 0),
                    ResolveKnowledge(rawKnowledge),
                    rawKnowledge,
                    ResolveContent(rawContent),
                    rawContent,
                    ReadInt(dynamicTile, "curio_prop", 0),
                    ReadInt(dynamicTile, "trap", 0),
                    ReadInt(dynamicTile, "mash_index", -1),
                    ReadInt(dynamicTile, "mash_type", 7),
                    ReadBool(dynamicTile, "crit_scout", false)));
            }

            if (dynamicTiles is not null && dynamicTiles.Count != tiles.Count)
            {
                AddStructuralIssue(
                    $"Map area {areaId} has {tiles.Count} readable static tiles and {dynamicTiles.Count} dynamic tiles.");
            }

            areas.Add(new BattleMapAreaSnapshot(
                areaId,
                areaHash,
                areaKind,
                ResolveKnowledge(rawAreaKnowledge),
                rawAreaKnowledge,
                ReadBool(dynamicArea, "reversed", false),
                tiles.OrderBy(tile => tile.TileIndex).ToArray()));
        }

        var entranceHash = ReadNullableInt(mapRoot, "entrance_id");
        var finalRoomHash = ReadNullableInt(mapRoot, "final_room_id");
        var partyAreaHash = ReadNullableInt(raidRoot, "in_area");
        var lastRoomHash = ReadNullableInt(raidRoot, "last_room_id");
        var partyAreaId = ResolveAreaId(areaHashes, partyAreaHash);
        var partyTileIndex = ResolvePartyTileIndex(
            areas,
            partyAreaId,
            ReadNullableInt(raidRoot, "areatile"),
            issues);
        if (!partyAreaHash.HasValue || partyAreaHash.Value == 0 || partyAreaId is null)
        {
            AddStructuralIssue("The raid party area does not belong to the captured map topology.");
        }
        if (!partyTileIndex.HasValue)
        {
            AddStructuralIssue("The raid party tile does not belong to the captured party area.");
        }
        if (areas.Count == 0 || areas.All(area => area.Tiles.Count == 0))
        {
            AddStructuralIssue("The captured map has no readable topology.");
        }
        if (structuralIssues.Count > 0)
        {
            throw new InvalidDataException(
                "The map and raid snapshot is incomplete or inconsistent: " +
                string.Join(" | ", structuralIssues.Distinct(StringComparer.Ordinal)));
        }

        return new BattleMapSnapshot(
            profileDirectory,
            mapSavePath,
            raidSavePath,
            mapSha256,
            raidSha256,
            JsonSupport.ReadString(raidInstance ?? new JsonObject(), "dungeon"),
            ReadInt(raidInstance, "difficulty", -1),
            ReadInt(raidInstance, "length", -1),
            entranceHash,
            ResolveAreaId(areaHashes, entranceHash),
            finalRoomHash,
            ResolveAreaId(areaHashes, finalRoomHash),
            partyAreaHash,
            partyAreaId,
            partyTileIndex,
            lastRoomHash,
            ResolveAreaId(areaHashes, lastRoomHash),
            ReadBool(raidRoot, "inbattle", false),
            ReadDoubleArray(mapRoot, "bounds"),
            areas,
            issues,
            DateTime.UtcNow);
    }

    private static int? ResolvePartyTileIndex(
        IReadOnlyList<BattleMapAreaSnapshot> areas,
        string? partyAreaId,
        int? savedAreaTile,
        ICollection<string> issues)
    {
        if (partyAreaId is null || !savedAreaTile.HasValue)
        {
            issues.Add("The raid party location could not be resolved to a map tile.");
            return null;
        }

        var area = areas.FirstOrDefault(candidate =>
            candidate.AreaId.Equals(partyAreaId, StringComparison.OrdinalIgnoreCase));
        if (area is null || area.Tiles.Count == 0)
        {
            issues.Add($"The raid party area {partyAreaId} has no readable map tiles.");
            return null;
        }

        if (area.Kind == BattleMapAreaKind.Room)
        {
            // Current saves normally persist areatile=1 in a room even though its only static tile is tile0.
            return area.Tiles[0].TileIndex;
        }

        // In corridors areatile is traversal progress from the endpoint through which the party entered.
        // It therefore maps directly from tile0 in a forward area and back from tileN-1 when reversed.
        // Stable native saves normally start at 1 (the first interior tile); zero is still representable as
        // the endpoint during a transition, so the reader deliberately accepts it.
        if (savedAreaTile.Value < 0 || savedAreaTile.Value >= area.Tiles.Count)
        {
            issues.Add(
                $"The raid party reports areatile={savedAreaTile.Value} in {partyAreaId}, outside its tile count.");
            return null;
        }

        var physicalOrdinal = area.Reversed
            ? area.Tiles.Count - 1 - savedAreaTile.Value
            : savedAreaTile.Value;
        return area.Tiles[physicalOrdinal].TileIndex;
    }

    private static string? ResolveAreaId(IReadOnlyDictionary<int, string> areaHashes, int? hash) =>
        hash.HasValue && areaHashes.TryGetValue(hash.Value, out var areaId) ? areaId : null;

    private static BattleMapAreaKind ResolveAreaKind(string areaId, int rawKind)
    {
        if (areaId.StartsWith("roo", StringComparison.OrdinalIgnoreCase) || rawKind == 0)
        {
            return BattleMapAreaKind.Room;
        }

        if (areaId.StartsWith("co", StringComparison.OrdinalIgnoreCase) || rawKind == 1)
        {
            return BattleMapAreaKind.Corridor;
        }

        return BattleMapAreaKind.Unknown;
    }

    private static BattleMapTileKnowledge ResolveKnowledge(int rawKnowledge) => rawKnowledge switch
    {
        <= 0 => BattleMapTileKnowledge.Hidden,
        1 => BattleMapTileKnowledge.Unknown,
        2 => BattleMapTileKnowledge.Scouted,
        _ => BattleMapTileKnowledge.Visited
    };

    private static BattleMapTileContent ResolveContent(int rawContent) =>
        Enum.IsDefined(typeof(BattleMapTileContent), rawContent)
            ? (BattleMapTileContent)rawContent
            : BattleMapTileContent.Unknown;

    private static IReadOnlyList<double> ReadDoubleArray(JsonObject? value, string name)
    {
        if (value?[name] is not JsonArray array)
        {
            return [];
        }

        var result = new List<double>(array.Count);
        foreach (var node in array)
        {
            if (node is JsonValue jsonValue && jsonValue.TryGetValue<double>(out var number))
            {
                result.Add(number);
            }
        }

        return result;
    }

    private static int ReadInt(JsonObject? value, string name, int fallback) =>
        ReadNullableInt(value, name) ?? fallback;

    private static int? ReadNullableInt(JsonObject? value, string name)
    {
        if (value?[name] is not JsonValue node)
        {
            return null;
        }

        if (node.TryGetValue<int>(out var integer))
        {
            return integer;
        }

        return node.TryGetValue<long>(out var longValue) && longValue is >= int.MinValue and <= int.MaxValue
            ? (int)longValue
            : null;
    }

    private static bool ReadBool(JsonObject? value, string name, bool fallback) =>
        value?[name] is JsonValue node && node.TryGetValue<bool>(out var result) ? result : fallback;

    private static int ParseTrailingIndex(string value)
    {
        var start = value.Length;
        while (start > 0 && char.IsDigit(value[start - 1]))
        {
            start--;
        }

        return start < value.Length && int.TryParse(value.AsSpan(start), out var index) ? index : int.MaxValue;
    }

    private static string NaturalKey(string value) => $"{ParseTrailingIndex(value):D8}:{value}";

    private static string CopyStable(string sourcePath, string destinationPath)
    {
        var sourceHashBefore = ComputeSha256(sourcePath);
        File.Copy(sourcePath, destinationPath, overwrite: false);
        var copyHash = ComputeSha256(destinationPath);
        var sourceHashAfter = ComputeSha256(sourcePath);
        if (!sourceHashBefore.Equals(copyHash, StringComparison.OrdinalIgnoreCase) ||
            !sourceHashAfter.Equals(copyHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException($"Save file changed while it was being copied: {sourcePath}");
        }

        return copyHash;
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void TryDeleteTemporaryDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
