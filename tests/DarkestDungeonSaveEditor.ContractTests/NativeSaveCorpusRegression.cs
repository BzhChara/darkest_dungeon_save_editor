using System.Text.Json;
using DarkestDungeonSaveEditor.Core.Dson;

internal static partial class ContractSuite
{
    // The manifest lists read-only input directories. Every conversion/edit is
    // performed on a new snapshot below artifacts, including profile_0 inputs.
    public static async Task RunNativeSaveRegressionAsync(string repositoryRoot, string manifestPath)
    {
        var root = Path.Combine(repositoryRoot, "artifacts", "native-save-regression", Guid.NewGuid().ToString("N"));
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        var sources = manifest["sources"]!.AsArray().Select(item => (
            Name: item!["name"]!.GetValue<string>(), Directory: Path.GetFullPath(item["directory"]!.GetValue<string>()))) .ToArray();
        Assert(sources.Length > 0 && sources.Select(s => s.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() == sources.Length,
            "Source names must be nonempty and unique.");
        foreach (var source in sources)
        {
            Assert(source.Name.Length > 0 && source.Name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-'), "Unsafe source name.");
            Assert(!root.StartsWith(Path.TrimEndingDirectorySeparator(source.Directory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
                "The regression output must not be inside an input directory.");
        }
        Directory.CreateDirectory(root);
        var native = DsonSaveCodec.CreateNative();
        var java = new DsonSaveCodec(Path.Combine(repositoryRoot, "tools", "DDSaveEditor", "DDSaveEditor.jar"));
        var originals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var files = new List<object>();
        var maps = new List<object>();
        var workflows = new List<object>();
        var errors = new List<string>();
        var referenceLimitations = new List<object>();
        var copied = new List<(string Source, string Relative, string Path)>();
        try
        {
            foreach (var source in sources)
            {
                foreach (var path in CorpusInputFiles(source.Directory))
                {
                    var hash = ComputeSha256(path);
                    originals[path] = hash;
                    if (!Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase)) continue;
                    var relative = Path.GetRelativePath(source.Directory, path);
                    var copy = Path.Combine(root, source.Name, "source", relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
                    File.Copy(path, copy, false);
                    Assert(ComputeSha256(copy) == hash && ComputeSha256(path) == hash, "Input changed during snapshot: " + path);
                    copied.Add((source.Name, relative, copy));
                }
            }
            Assert(copied.Count > 0, "The corpus must contain JSON-named save files.");
            Console.WriteLine($"Snapshot: {copied.Count} save files copied; {originals.Count} original files hashed. All subsequent writes use {root}.");

            foreach (var file in copied)
            {
                var binary = DsonSaveCodec.IsDson(file.Path);
                string Output(string stage) => Path.Combine(root, file.Source, stage, file.Relative);
                try
                {
                    await native.DecodeAsync(file.Path, Output("native-json"));
                    var view = JsonNode.Parse(File.ReadAllText(Output("native-json")))!.AsObject();
                    await native.EncodeAsync(Output("native-json"), Output("native-roundtrip"), file.Path);
                    var bytes = File.ReadAllBytes(file.Path);
                    Assert(bytes.SequenceEqual(File.ReadAllBytes(Output("native-roundtrip"))), "No-op was not byte-exact.");
                    var duplicateGroups = 0;
                    var opaqueFields = 0;
                    var opaqueNames = new HashSet<string>(StringComparer.Ordinal);
                    var document = binary ? DsonReader.Read(bytes, default) : null;
                    if (document is not null) CountCorpusFields(document.Fields, ref duplicateGroups, ref opaqueFields, opaqueNames);
                    var javaViewMatches = false;
                    string? javaUnsupportedField = null;
                    try
                    {
                        await java.DecodeAsync(file.Path, Output("java-json"));
                        Assert(JsonNode.DeepEquals(view, JsonNode.Parse(File.ReadAllText(Output("java-json")))), "Native and JAR JSON views differ.");
                        javaViewMatches = true;
                    }
                    catch (InvalidOperationException ex) when (opaqueNames.Any(name =>
                        ex.Message.Split('\n').Any(line => line.TrimEnd('\r').EndsWith("Couldn't parse field " + name, StringComparison.Ordinal))))
                    {
                        // The retained JAR has no type rule for some multiplayer
                        // fields. This is an unavailable reference, not a match.
                        javaUnsupportedField = opaqueNames.First(name => ex.Message.Contains("Couldn't parse field " + name, StringComparison.Ordinal));
                        referenceLimitations.Add(new { source = file.Source, path = file.Relative, field = javaUnsupportedField, error = ex.Message });
                    }
                    string? editedField = null;
                    if (binary)
                    {
                        var desired = view.DeepClone().AsObject();
                        Assert(desired["base_root"] is JsonObject, "Missing base_root for the append probe.");
                        Assert(!desired["base_root"]!.AsObject().ContainsKey("__native_codec_regression_probe"), "Append probe name already exists.");
                        desired["base_root"]!["__native_codec_regression_probe"] = 2147483000;
                        WriteCorpusJson(Output("append-input"), desired);
                        await native.EncodeAsync(Output("append-input"), Output("append-output"), file.Path);
                        await native.DecodeAsync(Output("append-output"), Output("append-readback"));
                        Assert(JsonNode.DeepEquals(desired, JsonNode.Parse(File.ReadAllText(Output("append-readback")))), "Append readback differs.");
                        var appended = File.ReadAllBytes(Output("append-output"));
                        var dataStart = ReadNativeInt(bytes, 60);
                        Assert(bytes.AsSpan(dataStart).SequenceEqual(appended.AsSpan(ReadNativeInt(appended, 60), bytes.Length - dataStart)),
                            "Appending a root field changed existing native DATA bytes.");
                        Assert(ReadNativeInt(appended, 44) == ReadNativeInt(bytes, 44) + 1 &&
                            bytes.AsSpan(ReadNativeInt(bytes, 48), ReadNativeInt(bytes, 44) * 12).SequenceEqual(
                                appended.AsSpan(ReadNativeInt(appended, 48), ReadNativeInt(bytes, 44) * 12)),
                            "Appending a field changed existing field metadata.");
                        Assert(bytes.AsSpan(4, 4).SequenceEqual(appended.AsSpan(4, 4)), "Revision changed.");

                        // Exercise a real existing value, including variable-length
                        // strings, rather than relying only on an appended probe.
                        var editView = view.DeepClone().AsObject();
                        var editPath = FindCorpusEditableScalar(document!.Fields, []);
                        if (editPath is not null)
                        {
                            var parent = editView;
                            foreach (var segment in editPath[..^1]) parent = parent[segment]!.AsObject();
                            var key = editPath[^1];
                            var old = parent[key]!;
                            parent[key] = old.GetValueKind() switch
                            {
                                JsonValueKind.String => JsonValue.Create(old.GetValue<string>() + " C#\"\\回归"),
                                JsonValueKind.True or JsonValueKind.False => JsonValue.Create(!old.GetValue<bool>()),
                                _ => JsonValue.Create(old.GetValue<int>() == int.MaxValue ? int.MaxValue - 1 : old.GetValue<int>() + 1)
                            };
                            WriteCorpusJson(Output("edit-input"), editView);
                            await native.EncodeAsync(Output("edit-input"), Output("edit-output"), file.Path);
                            await native.DecodeAsync(Output("edit-output"), Output("edit-readback"));
                            Assert(JsonNode.DeepEquals(editView, JsonNode.Parse(File.ReadAllText(Output("edit-readback")))), "Existing-field edit readback differs.");
                            editedField = string.Join("/", editPath);
                        }
                    }
                    files.Add(new { source = file.Source, path = file.Relative, binary, sourceSha256 = ComputeSha256(file.Path),
                        javaViewMatches, javaUnsupportedField, noOpByteExact = true, appendPreservesNativeData = binary,
                        editedField, duplicateGroups, opaqueFields });
                    Console.WriteLine($"PASS: {file.Source}/{file.Relative}: {(binary ? "native preservation + edit" : "plain JSON passthrough")}, {(javaViewMatches ? "JAR view matches" : "JAR reference unavailable: " + javaUnsupportedField)}, byte-exact no-op; duplicates={duplicateGroups}, opaque={opaqueFields}.");
                }
                catch (Exception ex)
                {
                    errors.Add($"{file.Source}/{file.Relative}: {ex}");
                    Console.WriteLine($"FAIL: {file.Source}/{file.Relative}: {ex.Message}");
                }
            }

            // Use the application's map reader on complete profile snapshots.
            // Diagnostics present in the input are recorded rather than mistaken
            // for a codec regression; both views must retain the same map state.
            foreach (var file in copied.Where(f => Path.GetFileName(f.Path).Equals("persist.game.json", StringComparison.OrdinalIgnoreCase)))
            {
                var directory = Path.GetDirectoryName(file.Path)!;
                if (!File.Exists(Path.Combine(directory, "persist.estate.json"))) continue;
                try
                {
                    var afterDirectory = Path.GetDirectoryName(Path.Combine(root, file.Source, "native-roundtrip", file.Relative))!;
                    var beforeLocation = await RaidSaveLocation.ReadAsync(directory, native);
                    var afterLocation = await RaidSaveLocation.ReadAsync(afterDirectory, native);
                    Assert(beforeLocation.RelativeDirectory == afterLocation.RelativeDirectory &&
                        File.Exists(beforeLocation.MapPath) == File.Exists(afterLocation.MapPath) &&
                        File.Exists(beforeLocation.RaidPath) == File.Exists(afterLocation.RaidPath), "Active raid location changed.");
                    if (!File.Exists(beforeLocation.MapPath) || !File.Exists(beforeLocation.RaidPath))
                    {
                        maps.Add(new { source = file.Source, profile = Path.GetDirectoryName(file.Relative), status = "No active map pair in input or roundtrip" });
                        continue;
                    }
                    var before = await new BattleMapSnapshotReader(native).LoadAsync(directory);
                    var after = await new BattleMapSnapshotReader(native).LoadAsync(afterDirectory);
                    Assert(before.DungeonId == after.DungeonId && before.PartyAreaId == after.PartyAreaId &&
                        before.RoomCount == after.RoomCount && before.CorridorCount == after.CorridorCount &&
                        before.TileCount == after.TileCount && before.Areas.Select(a => a.AreaId).SequenceEqual(after.Areas.Select(a => a.AreaId)),
                        "Application map view changed after native no-op.");
                    maps.Add(new { source = file.Source, profile = Path.GetDirectoryName(file.Relative), before.DungeonId,
                        areas = before.Areas.Count, before.RoomCount, before.CorridorCount, before.TileCount, before.PartyAreaId,
                        beforeIssues = before.Issues, afterIssues = after.Issues });
                    Console.WriteLine($"PASS: {file.Source}/{Path.GetDirectoryName(file.Relative)} application map reader; {before.Areas.Count} areas.");
                }
                catch (Exception ex) { errors.Add($"Map {file.Source}/{file.Relative}: {ex}"); }
            }

            foreach (var file in copied.Where(f => Path.GetFileName(f.Path).Equals("persist.map.json", StringComparison.OrdinalIgnoreCase)))
            {
                var directory = Path.GetDirectoryName(file.Path)!;
                if (File.Exists(Path.Combine(directory, "persist.game.json")) || !File.Exists(Path.Combine(directory, "persist.raid.json"))) continue;
                var parent = Path.GetDirectoryName(directory)!;
                if (!File.Exists(Path.Combine(parent, "persist.game.json"))) continue;
                try
                {
                    var afterDirectory = Path.GetDirectoryName(Path.Combine(root, file.Source, "native-roundtrip", file.Relative))!;
                    var relative = Path.GetFileName(directory);
                    var before = await new BattleMapSnapshotReader(native).LoadPersistentAsync(parent, relative, default);
                    var after = await new BattleMapSnapshotReader(native).LoadPersistentAsync(Path.GetDirectoryName(afterDirectory)!, relative, default);
                    Assert(before.DungeonId == after.DungeonId && before.PartyAreaId == after.PartyAreaId &&
                        before.Areas.Select(a => a.AreaId).SequenceEqual(after.Areas.Select(a => a.AreaId)) && before.TileCount == after.TileCount,
                        "Retained expedition map changed.");
                    maps.Add(new { source = file.Source, profile = Path.GetDirectoryName(file.Relative), status = "Retained expedition",
                        before.DungeonId, areas = before.Areas.Count, before.TileCount });
                    Console.WriteLine($"PASS: {file.Source}/{Path.GetDirectoryName(file.Relative)} retained expedition map; {before.Areas.Count} areas.");
                }
                catch (Exception ex) { errors.Add($"Retained map {file.Source}/{file.Relative}: {ex}"); }
            }
            foreach (var workflow in manifest["workflows"]?.AsArray() ?? [])
            {
                var source = workflow!["source"]!.GetValue<string>();
                var profile = workflow["profile"]!.GetValue<string>();
                try
                {
                    Assert(sources.Any(s => s.Name == source), "Unknown workflow source.");
                    var snapshot = RaidSaveLocation.ResolvePath(Path.Combine(root, source, "source"), profile);
                    var target = RaidSaveLocation.ResolvePath(Path.Combine(root, source, "business-edit"), profile);
                    workflows.Add(await VerifyCorpusBusinessEditAsync(snapshot, target, manifest["gameDirectory"]!.GetValue<string>(),
                        manifest["workshopDirectory"]?.GetValue<string>()));
                    Console.WriteLine($"PASS: {source}/{profile}: application quantity catalog, preview, native commit, verified backup, and readback on a second copy.");
                }
                catch (Exception ex) { errors.Add($"Workflow {source}/{profile}: {ex}"); Console.WriteLine($"FAIL: workflow {source}/{profile}: {ex.Message}"); }
            }
        }
        catch (Exception ex) { errors.Add("Corpus setup: " + ex); }
        finally
        {
            var originalChanges = new List<string>();
            foreach (var original in originals)
            {
                try { if (ComputeSha256(original.Key) != original.Value) originalChanges.Add(original.Key); }
                catch (Exception ex) { originalChanges.Add(original.Key + ": " + ex.Message); }
            }
            try
            {
                foreach (var path in sources.SelectMany(s => CorpusInputFiles(s.Directory)).Distinct(StringComparer.OrdinalIgnoreCase))
                    if (!originals.ContainsKey(path)) originalChanges.Add("Added: " + path);
            }
            catch (Exception ex) { originalChanges.Add("Input recheck failed: " + ex.Message); }
            if (originalChanges.Count > 0) errors.Add("Original input changed: " + string.Join("; ", originalChanges));
            var report = new { root, sources = sources.Select(s => new { name = s.Name, directory = s.Directory }),
                inputSaveFiles = copied.Count, checkedFiles = files.Count, originalFilesHashed = originals.Count,
                originalsUnchanged = originalChanges.Count == 0, originalChanges, files, maps, workflows, referenceLimitations, errors,
                javaJarSha256 = ComputeSha256(java.JarPath),
                inGameValidation = "Not performed; all edits are isolated regression artifacts." };
            var reportPath = Path.Combine(root, "report.json");
            File.WriteAllText(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllText(Path.Combine(root, "source-hashes.json"), JsonSerializer.Serialize(originals, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"Report: {reportPath}");
        }
        Assert(errors.Count == 0, $"Native corpus regression had {errors.Count} failures; see {root}.");
    }

    private static IEnumerable<string> CorpusInputFiles(string directory)
    {
        Assert((File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0, "Input directories must not be links: " + directory);
        foreach (var file in Directory.EnumerateFiles(directory).Order())
        {
            Assert((File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0, "Input files must not be links: " + file);
            yield return file;
        }
        foreach (var child in Directory.EnumerateDirectories(directory).Order())
            foreach (var file in CorpusInputFiles(child)) yield return file;
    }

    private static void WriteCorpusJson(string path, JsonObject value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, value.ToJsonString());
    }

    private static async Task<object> VerifyCorpusBusinessEditAsync(string snapshot, string target, string gameDirectory, string? workshopDirectory)
    {
        foreach (var source in CorpusInputFiles(snapshot))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(snapshot, source));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, false);
        }
        var codec = DarkestDungeonSaveEditor.App.DesktopCodecSession.Create(["--codec=native"], target).Codec;
        var profile = SteamDiscovery.OpenProfile(target);
        var work = target + "-work";
        var locations = new SaveEditorLocations(work, Path.Combine(work, "sessions"), Path.Combine(work, "backups"));
        // Resolver/catalogs only read installed game and Mod content. The desktop
        // manifest-generation service is deliberately not invoked on real content.
        var content = await ActiveContentResolver.ResolveAsync(profile, gameDirectory, workshopDirectory, null, codec, work);
        var catalog = await QuantityItemCatalog.LoadAsync(content, codec);
        var item = catalog.Items.Where(item => item.CurrentAmount > 0 && !item.HasProviderConflict && string.IsNullOrEmpty(item.SaveIdentityIssue))
            .OrderBy(item => item.DisplayId == "gold" ? 0 : 1).First();
        var amount = item.CurrentAmount - 1; // Fits both town and occupied raid stacks.
        var service = new SaveEditService(codec, locations);
        var prepared = await service.PrepareQuantityItemEditAsync(profile, item, amount, content);
        var originals = CorpusInputFiles(target).ToDictionary(path => path, ComputeSha256);
        var commit = await service.CommitAsync(prepared);
        Assert(ComputeSha256(commit.TargetPath).Equals(commit.FinalSha256, StringComparison.OrdinalIgnoreCase), "Committed checksum differs.");
        foreach (var original in originals)
            if (!original.Key.Equals(commit.TargetPath, StringComparison.OrdinalIgnoreCase))
                Assert(ComputeSha256(original.Key) == original.Value, "A sibling save changed during quantity commit.");
        var backup = ProfileSaveFiles.BackupPath(target, commit.BackupDirectory, commit.TargetPath);
        Assert(ComputeSha256(backup).Equals(prepared.OriginalSha256, StringComparison.OrdinalIgnoreCase), "Quantity backup must match the untouched target bytes.");
        var decoded = Path.Combine(work, "committed.json");
        await codec.DecodeAsync(commit.TargetPath, decoded);
        Assert(JsonNode.DeepEquals(JsonNode.Parse(File.ReadAllText(prepared.ProposedDecodedPath)), JsonNode.Parse(File.ReadAllText(decoded))),
            "Committed save must equal the desktop preview, including untouched data.");
        var refreshed = await QuantityItemCatalog.LoadAsync(content with { WorkspaceDirectory = Path.Combine(work, "refresh") }, codec);
        Assert(refreshed.Items.Single(i => i.CatalogKey == item.CatalogKey).CurrentAmount == amount, "Catalog refresh lost the saved quantity.");
        return new { profile = target, scene = catalog.SaveContext.ToString(), item = item.DisplayId,
            before = item.CurrentAmount, after = amount, commit.BackupDirectory, commit.FinalSha256,
            contentIssues = content.Issues, catalogIssues = catalog.Issues };
    }

    private static void CountCorpusFields(List<DsonField> fields, ref int duplicates, ref int opaque, HashSet<string> opaqueNames)
    {
        duplicates += fields.GroupBy(f => f.Name, StringComparer.Ordinal).Count(group => group.Count() > 1);
        foreach (var field in fields)
        {
            if (field.Kind == DsonKind.Opaque) { opaque++; opaqueNames.Add(field.Name); }
            CountCorpusFields(field.Children, ref duplicates, ref opaque, opaqueNames);
            if (field.Embedded is not null) CountCorpusFields(field.Embedded.Fields, ref duplicates, ref opaque, opaqueNames);
        }
    }

    private static string[]? FindCorpusEditableScalar(List<DsonField> fields, string[] prefix)
    {
        foreach (var field in fields.Where(f => fields.Count(other => other.Name == f.Name) == 1))
        {
            string[] path = [.. prefix, field.Name];
            if (field.Kind is DsonKind.String or DsonKind.Integer or DsonKind.Boolean) return path;
            var child = field.Kind == DsonKind.Object ? FindCorpusEditableScalar(field.Children, path) : null;
            if (child is not null) return child;
        }
        return null;
    }
}
