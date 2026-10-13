using System.Text.Json;

internal static partial class ContractSuite
{
    public static async Task RunNativeUploadRegressionAsync(string repositoryRoot, string uploadRoot)
    {
        var root = Path.Combine(repositoryRoot, "artifacts", "native-codec-upload-regression", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var originals = Directory.GetFiles(uploadRoot, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, ComputeSha256);
        var native = DsonSaveCodec.CreateNative();
        var java = new DsonSaveCodec(Path.Combine(repositoryRoot, "tools", "DDSaveEditor", "DDSaveEditor.jar"));
        var files = new List<object>();
        var maps = new List<object>();
        var errors = new List<string>();
        foreach (var group in new[] { "current", "backup" })
        {
            var source = Path.Combine(uploadRoot, "profile_0", group == "backup" ? "backup" : "");
            var copies = Path.Combine(root, group, "source");
            Directory.CreateDirectory(copies);
            var sourceFiles = Directory.GetFiles(source, "*.json");
            Assert(sourceFiles.Length > 0, "The supplied profile must contain saves.");
            foreach (var path in sourceFiles) File.Copy(path, Path.Combine(copies, Path.GetFileName(path)), false);
            foreach (var path in Directory.GetFiles(copies, "*.json").Order())
            {
                var name = Path.GetFileName(path);
                try
                {
                    var decoded = Path.Combine(root, group, "native-json", name);
                    var reference = Path.Combine(root, group, "java-json", name);
                    var encoded = Path.Combine(root, group, "native-roundtrip", name);
                    var proposed = Path.Combine(root, group, "proposed", name);
                    var edited = Path.Combine(root, group, "edited", name);
                    var editedJson = Path.Combine(root, group, "edited-json", name);
                    await native.DecodeAsync(path, decoded);
                    await java.DecodeAsync(path, reference);
                    var nativeView = JsonNode.Parse(File.ReadAllText(decoded));
                    var javaView = JsonNode.Parse(File.ReadAllText(reference));
                    Assert(JsonNode.DeepEquals(nativeView, javaView), "Native and reference JSON views differ: " + name);
                    await native.EncodeAsync(decoded, encoded, path);
                    var originalBytes = File.ReadAllBytes(path);
                    Assert(originalBytes.SequenceEqual(File.ReadAllBytes(encoded)), "No-op is not byte-exact: " + name);
                    var desired = nativeView!.DeepClone();
                    desired["base_root"]!["__native_codec_regression_probe"] = 2147483000;
                    Directory.CreateDirectory(Path.GetDirectoryName(proposed)!);
                    File.WriteAllText(proposed, desired.ToJsonString());
                    await native.EncodeAsync(proposed, edited, path);
                    await native.DecodeAsync(edited, editedJson);
                    Assert(JsonNode.DeepEquals(desired, JsonNode.Parse(File.ReadAllText(editedJson))), "Edited view differs: " + name);
                    var editedBytes = File.ReadAllBytes(edited);
                    var originalData = ReadNativeInt(originalBytes, 60); var editedData = ReadNativeInt(editedBytes, 60);
                    // Appending one root scalar leaves every old native data span
                    // at the same relative offset. This independently verifies all
                    // original payloads, duplicates, embedded files and padding.
                    Assert(originalBytes.AsSpan(originalData).SequenceEqual(
                        editedBytes.AsSpan(editedData, originalBytes.Length - originalData)), "Original DATA bytes changed: " + name);
                    var originalTable = ReadNativeInt(originalBytes, 48); var editedTable = ReadNativeInt(editedBytes, 48);
                    var originalCount = ReadNativeInt(originalBytes, 44);
                    Assert(ReadNativeInt(editedBytes, 44) == originalCount + 1 &&
                        originalBytes.AsSpan(originalTable, originalCount * 12).SequenceEqual(
                            editedBytes.AsSpan(editedTable, originalCount * 12)), "Original field metadata changed: " + name);
                    Assert(originalBytes.AsSpan(4, 4).SequenceEqual(editedBytes.AsSpan(4, 4)), "Revision changed: " + name);
                    files.Add(new { group, name, javaViewMatches = true, noOpByteExact = true,
                        editReadBack = true, originalPayloadsAndFieldMetadataPreserved = true,
                        originalFields = originalCount, sourceSha256 = ComputeSha256(path) });
                    Console.WriteLine($"PASS: {group}/{name}: Java view, byte-exact no-op, edited readback, original DATA/field metadata preserved.");
                }
                catch (Exception ex) { errors.Add($"{group}/{name}: {ex}"); Console.WriteLine($"FAIL: {group}/{name}: {ex.Message}"); }
            }
            try
            {
                var snapshot = await new BattleMapSnapshotReader(native).LoadAsync(copies);
                var after = await new BattleMapSnapshotReader(native).LoadAsync(Path.Combine(root, group, "native-roundtrip"));
                Assert(snapshot.DungeonId == "cove" && snapshot.Areas.Count == 39 && snapshot.Issues.Count == 0 &&
                    after.Areas.Count == 39 && after.Issues.Count == 0 && after.PartyAreaId == snapshot.PartyAreaId,
                    "Uploaded map snapshot mismatch.");
                foreach (var id in new[] { "roo\\", "co[\\", "coV\\" })
                    Assert(snapshot.Areas.Any(area => area.AreaId == id) && after.Areas.Any(area => area.AreaId == id), "Special map ID changed.");
                maps.Add(new { group, snapshot.DungeonId, areas = snapshot.Areas.Count, snapshot.RoomCount,
                    snapshot.CorridorCount, snapshot.TileCount, snapshot.PartyAreaId, snapshot.Issues });
                Console.WriteLine($"PASS: {group} map loads through the existing application reader, including original escaped area IDs.");
            }
            catch (Exception ex) { errors.Add($"{group}/map: {ex}"); Console.WriteLine($"FAIL: {group}/map: {ex.Message}"); }
        }
        var unchanged = originals.All(entry => ComputeSha256(entry.Key) == entry.Value);
        if (!unchanged) errors.Add("Uploaded original files changed.");
        var report = new { root, javaJarSha256 = ComputeSha256(java.JarPath), fileCount = files.Count, files, maps,
            originalFileCount = originals.Count, originalsUnchanged = unchanged, errors,
            inGameValidation = "Not performed. The application still defaults to Java." };
        var reportPath = Path.Combine(root, "report.json");
        File.WriteAllText(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Report: {reportPath}");
        Assert(errors.Count == 0, $"Native uploaded-save regression had {errors.Count} failures; see {reportPath}.");
    }
}
