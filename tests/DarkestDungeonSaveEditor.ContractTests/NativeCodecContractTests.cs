using System.Text.Json;
using DarkestDungeonSaveEditor.Core.Dson;

internal static partial class ContractSuite
{
    private static bool UseNativeCodec;

    public static async Task RunNativeAllAsync(string repositoryRoot)
    {
        UseNativeCodec = true;
        try { await RunAsync(repositoryRoot); }
        finally { UseNativeCodec = false; }
    }

    public static async Task RunNativeEquipmentOnlyAsync(string repositoryRoot)
    {
        var root = Path.Combine(repositoryRoot, "artifacts", "native-equipment-contracts", Guid.NewGuid().ToString("N"));
        UseNativeCodec = true;
        try { await VerifyEquipmentProgressionGuardsAsync(root, DsonSaveCodec.CreateNative()); }
        finally { UseNativeCodec = false; }
        Console.WriteLine($"Artifacts: {root}");
    }

    public static async Task RunNativeCodecOnlyAsync(string repositoryRoot)
    {
        var root = Path.Combine(repositoryRoot, "artifacts", "native-codec-contracts", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var codec = DsonSaveCodec.CreateNative();
        await RunCodecJsonEscapingContractsAsync(root, codec);
        var values = new[] { "An \"New\" estate", "roo\\", "literal\\n", "line\nfeed\r\ttab", "###Estate", "区域😀", "a\0b" };
        foreach (var text in values)
        {
            // Author native bytes without either implementation's encoder.
            var original = NativeScalarFixture(("test_note", NativeString(text), true));
            var document = DsonReader.Read(original, default);
            var view = document.ToJson(default);
            Assert(view["base_root"]!["test_note"]!.GetValue<string>() == text, "Native string bytes must decode literally.");
            Assert(DsonWriter.Encode(view, document, default).SequenceEqual(original), "No-op must retain original bytes.");
            view["base_root"]!["test_note"] = text + "!";
            var edited = DsonWriter.Encode(view, document, default);
            Assert(NativeScalarPayloads(edited).Single().Payload.SequenceEqual(NativeString(text + "!")),
                "An independent binary inspection must see the exact UTF-8 string, including a literal ### prefix.");
        }
        Console.WriteLine("PASS: independently authored native strings, including escapes, Unicode, NUL and ###, decode and edit without loss.");

        var seed = JsonNode.Parse("""
            {"base_root":{"goal_ids":["ordinary","###literal","quote\"","slash\\"],"requirement_code":"\\",
            "current_hp":30,"killRange":[1,2],"party":{"heroes":[10,20]},"pair":[true,false],
            "map":{"bounds":[0,1,2,3]},"static_save":{"base_root":{"text":"embedded\\"}}}}
            """)!.AsObject();
        var nativeBytes = DsonWriter.Encode(seed, null, default);
        var read = DsonReader.Read(nativeBytes, default);
        Assert(read.ToJson(default)["base_root"]!["current_hp"]!.GetValue<float>() == 30 &&
            read.ToJson(default)["base_root"]!["goal_ids"]![1]!.GetValue<string>() == "###literal",
            "Supported native types must retain numeric values and literal string prefixes.");
        var expectedSeed = seed.DeepClone();
        expectedSeed["base_root"]!["current_hp"] = JsonNode.Parse("30.0");
        expectedSeed["base_root"]!["map"]!["bounds"] = JsonNode.Parse("[0.0,1.0,2.0,3.0]");
        Assert(JsonNode.DeepEquals(expectedSeed, read.ToJson(default)), "Native float views retain the existing decimal-token contract.");
        var changed = read.ToJson(default);
        changed["base_root"]!["goal_ids"]![0] = "longer string with \"quote\"\tand\\";
        changed["base_root"]!["requirement_code"] = "\"";
        changed["base_root"]!["current_hp"] = 31;
        changed["base_root"]!["static_save"]!["base_root"]!["text"] = "changed";
        var changedBytes = DsonWriter.Encode(changed, read, default);
        changed["base_root"]!["current_hp"] = JsonNode.Parse("31.0");
        Assert(JsonNode.DeepEquals(changed, DsonReader.Read(changedBytes, default).ToJson(default)),
            "Editing vectors, chars, integer-spelled floats and embedded files must preserve their native types.");
        Console.WriteLine("PASS: vectors, char escaping, finite floats with integer JSON tokens, pairs and embedded DSON edits.");

        var duplicates = NativeScalarFixture(("tag", NativeString("first"), true),
            ("tag", NativeString("second"), true), ("count", NativeInt(7), true));
        var duplicateDocument = DsonReader.Read(duplicates, default);
        var duplicateView = duplicateDocument.ToJson(default);
        duplicateView["base_root"]!["count"] = 8;
        var duplicateEdit = DsonWriter.Encode(duplicateView, duplicateDocument, default);
        var tags = NativeScalarPayloads(duplicateEdit).Where(item => item.Name == "tag").ToArray();
        Assert(tags.Length == 2 && tags[0].Payload.SequenceEqual(NativeString("first")) &&
            tags[1].Payload.SequenceEqual(NativeString("second")), "Both differing duplicate values must remain in the binary.");
        duplicateView["base_root"]!["tag"] = "ambiguous";
        RejectNative(() => DsonWriter.Encode(duplicateView, duplicateDocument, default), "Ambiguous duplicate edit");
        duplicateView["base_root"]!.AsObject().Remove("tag");
        RejectNative(() => DsonWriter.Encode(duplicateView, duplicateDocument, default), "Ambiguous duplicate removal");
        Console.WriteLine("PASS: differing duplicate fields survive unrelated edits; ambiguous changes and removals are rejected.");

        var unordered = NativeScalarFixture(("one", NativeInt(1111), true), ("two", NativeInt(2222), true),
            ("tri", NativeInt(3333), true));
        var table = ReadNativeInt(unordered, 48); var dataStart = ReadNativeInt(unordered, 60);
        var two = ReadNativeInt(unordered, table + 2 * 12 + 4);
        var tri = ReadNativeInt(unordered, table + 3 * 12 + 4);
        var twoBytes = unordered.AsSpan(dataStart + two, 8).ToArray();
        unordered.AsSpan(dataStart + tri, 8).CopyTo(unordered.AsSpan(dataStart + two));
        twoBytes.CopyTo(unordered, dataStart + tri);
        PutNativeInt(unordered, table + 2 * 12 + 4, tri); PutNativeInt(unordered, table + 3 * 12 + 4, two);
        var unorderedDoc = DsonReader.Read(unordered, default);
        var unorderedView = unorderedDoc.ToJson(default);
        Assert(unorderedView["base_root"]!["two"]!.GetValue<int>() == 2222 &&
            unorderedView["base_root"]!["tri"]!.GetValue<int>() == 3333, "Read values by actual offset, not metadata order.");
        Assert(DsonWriter.Encode(unorderedView, unorderedDoc, default).SequenceEqual(unordered), "No-op keeps physical order.");
        unorderedView["base_root"]!["one"] = 4444;
        Assert(JsonNode.DeepEquals(unorderedView, DsonReader.Read(DsonWriter.Encode(unorderedView, unorderedDoc, default), default).ToJson(default)),
            "A reordered file must remain editable.");
        Console.WriteLine("PASS: non-monotonic native data offsets decode and edit correctly.");

        var opaqueBytes = NativeScalarFixture(("mystery", new byte[] { 0xE1, 0x82, 0x13 }, false),
            ("count", NativeInt(4), true));
        var opaqueDoc = DsonReader.Read(opaqueBytes, default);
        var opaqueView = opaqueDoc.ToJson(default);
        Assert(opaqueView["base_root"]!["mystery"] is JsonObject, "Unrecognized bytes require an opaque view.");
        RejectNative(() => DsonWriter.Encode(opaqueView, null, default), "Opaque view without original metadata");
        var movedOpaque = opaqueView.DeepClone();
        movedOpaque["base_root"]!["copied"] = movedOpaque["base_root"]!["mystery"]!.DeepClone();
        RejectNative(() => DsonWriter.Encode(movedOpaque.AsObject(), opaqueDoc, default), "Opaque view copied to a new field");
        opaqueView["base_root"]!["count"] = 5;
        var opaqueResult = DsonWriter.Encode(opaqueView, opaqueDoc, default);
        Assert(DsonReader.Read(opaqueResult, default).Fields[0].Children[0].Raw.SequenceEqual(new byte[] { 0xE1, 0x82, 0x13 }),
            "An unrelated edit must preserve opaque bytes.");
        opaqueView["base_root"]!["mystery"]!["$dsonOpaque"] = "00";
        RejectNative(() => DsonWriter.Encode(opaqueView, opaqueDoc, default), "Opaque edit");
        opaqueView["base_root"]!.AsObject().Remove("mystery");
        RejectNative(() => DsonWriter.Encode(opaqueView, opaqueDoc, default), "Opaque removal");
        var relocateBytes = NativeScalarFixture(("note", NativeString("a"), true),
            ("mystery", new byte[] { 0xE1, 0x82, 0x13 }, false));
        var relocateDoc = DsonReader.Read(relocateBytes, default);
        var relocateView = relocateDoc.ToJson(default); relocateView["base_root"]!["note"] = "ab";
        RejectNative(() => DsonWriter.Encode(relocateView, relocateDoc, default), "Unknown alignment relocation");
        Console.WriteLine("PASS: opaque bytes survive unrelated edits; unsupported edits and alignment changes fail explicitly.");

        var flagged = NativeScalarFixture(("note", NativeString("a"), true), ("count", NativeInt(1), true));
        PutNativeInt(flagged, 4, 0x6D9F0000); PutNativeInt(flagged, 12, 0x12345678);
        PutNativeInt(flagged, 28, 0x76543210); PutNativeInt(flagged, 52, 0x24681357);
        var flaggedTable = ReadNativeInt(flagged, 48);
        for (var i = 0; i < 3; i++) PutNativeInt(flagged, flaggedTable + i * 12 + 8,
            ReadNativeInt(flagged, flaggedTable + i * 12 + 8) | unchecked((int)0x80000002));
        var flaggedDoc = DsonReader.Read(flagged, default); var flaggedView = flaggedDoc.ToJson(default);
        flaggedView["base_root"]!["note"] = "a much longer edited string";
        var flaggedEdit = DsonWriter.Encode(flaggedView, flaggedDoc, default);
        foreach (var position in new[] { 4, 12, 28, 52 })
            Assert(ReadNativeInt(flagged, position) == ReadNativeInt(flaggedEdit, position), "Header metadata must survive edits.");
        var newTable = ReadNativeInt(flaggedEdit, 48);
        for (var i = 0; i < 3; i++) Assert((ReadNativeInt(flaggedEdit, newTable + i * 12 + 8) & unchecked((int)0x80000002)) ==
            unchecked((int)0x80000002), "Unknown field-info bits must survive edits.");
        Assert(NativeScalarPayloads(flaggedEdit).Last().Payload.SequenceEqual(NativeInt(1)), "Untouched integer payload survives data relocation.");
        Console.WriteLine("PASS: revision, reserved header bytes, field flags and untouched payloads survive variable-length edits.");

        foreach (var bad in new[] { "{\"base_root\":{\"requirement_code\":\"AB\"}}", "{\"base_root\":{\"requirement_code\":\"é\"}}",
            "{\"base_root\":{\"current_hp\":1e99}}", "{\"base_root\":{\"unknown_float\":1.5}}", "{\"base_root\":{\"x\":null}}",
            "{\"base_root\":{\"x\":1,\"x\":2}}", "{\"base_root\":{\"killRange\":[1]}}" })
            RejectNative(() => DsonWriter.Encode(DsonDocument.ReadJson(bad), null, default), "Invalid input");
        var maxName = new JsonObject { ["base_root"] = new JsonObject { [new string('a', 510)] = 1 } };
        _ = DsonReader.Read(DsonWriter.Encode(maxName, null, default), default);
        maxName["base_root"]!.AsObject().Add(new string('a', 511), 1);
        RejectNative(() => DsonWriter.Encode(maxName, null, default), "Oversized native field name");
        for (var length = 0; length < duplicates.Length; length++)
        {
            var truncated = duplicates[..length];
            RejectNative(() => DsonReader.Read(truncated, default), "Truncated binary");
        }
        var badOffset = (byte[])duplicates.Clone(); PutNativeInt(badOffset, ReadNativeInt(badOffset, 48) + 4, int.MaxValue);
        RejectNative(() => DsonReader.Read(badOffset, default), "Bad offset");
        Console.WriteLine("PASS: name limits, char width, numeric range, null/duplicate JSON, truncation and invalid offsets fail safely.");

        var zeroDoc = DsonReader.Read(NativeScalarFixture(("number", NativeInt(0), true)), default);
        var tinyEdit = zeroDoc.ToJson(default); tinyEdit["base_root"]!["number"] = JsonNode.Parse("1e-99");
        RejectNative(() => DsonWriter.Encode(tinyEdit, zeroDoc, default), "Tiny non-integer is not an integer no-op");
        var charDoc = DsonReader.Read(NativeScalarFixture(("letter", new byte[] { (byte)'a' }, false)), default);
        var charView = charDoc.ToJson(default); charView["base_root"]!["letter"] = "\0";
        RejectNative(() => DsonWriter.Encode(charView, charDoc, default), "An untyped character cannot become a boolean byte");

        var inputPath = Path.Combine(root, "original.dson"); File.WriteAllBytes(inputPath, duplicates);
        var jsonPath = Path.Combine(root, "input.json"); File.WriteAllText(jsonPath, "{\"base_root\":{}}");
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            foreach (var operation in new Func<Task>[] {
                () => codec.DecodeAsync(inputPath, Path.Combine(root, "cancelled", "read"), cancelled.Token),
                () => codec.EncodeAsync(jsonPath, Path.Combine(root, "cancelled", "write"), inputPath, cancelled.Token) })
            {
                var threw = false;
                try { await operation(); } catch (OperationCanceledException) { threw = true; }
                Assert(threw && !Directory.Exists(Path.Combine(root, "cancelled")), "Pre-cancelled native operations do no output work.");
            }
        }
        var failedOutput = Path.Combine(root, "existing-output"); File.WriteAllText(failedOutput, "keep-existing");
        File.WriteAllText(jsonPath, "{\"base_root\":{\"x\":null}}");
        var failed = false;
        try { await codec.EncodeAsync(jsonPath, failedOutput, null); } catch (InvalidDataException) { failed = true; }
        Assert(failed && File.ReadAllText(failedOutput) == "keep-existing" && !Directory.GetFiles(root, "*.tmp").Any(),
            "Failed native conversion must not publish output or leave temporary files.");
        var samePathFailed = false;
        try { await codec.DecodeAsync(inputPath, inputPath); } catch (InvalidOperationException) { samePathFailed = true; }
        Assert(samePathFailed && File.ReadAllBytes(inputPath).SequenceEqual(duplicates), "Input paths cannot be overwritten.");
        Console.WriteLine("PASS: native cancellation, input protection and failed-output preservation.");
        Console.WriteLine($"Artifacts: {root}");
    }

    private static void RejectNative(Action action, string label)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException(label + " was not rejected.");
    }

    // A small independently authored flat DSON fixture, deliberately separate
    // from the production reader/writer. Used to test semantics, not snapshots.
    private static byte[] NativeScalarFixture(params (string Name, byte[] Payload, bool Aligned)[] children)
    {
        using var data = new MemoryStream();
        data.Write(Encoding.UTF8.GetBytes("base_root\0"));
        var fields = new List<(string Name, int Offset)> { ("base_root", 0) };
        foreach (var child in children)
        {
            fields.Add((child.Name, (int)data.Position)); data.Write(Encoding.UTF8.GetBytes(child.Name + "\0"));
            if (child.Aligned) while (data.Position % 4 != 0) data.WriteByte(0);
            data.Write(child.Payload);
        }
        var dataOffset = 80 + fields.Count * 12;
        var bytes = new byte[dataOffset + data.Length];
        PutNativeInt(bytes, 0, 0xB101); PutNativeInt(bytes, 8, 64); PutNativeInt(bytes, 16, 16);
        PutNativeInt(bytes, 20, 1); PutNativeInt(bytes, 24, 64); PutNativeInt(bytes, 44, fields.Count);
        PutNativeInt(bytes, 48, 80); PutNativeInt(bytes, 56, (int)data.Length); PutNativeInt(bytes, 60, dataOffset);
        PutNativeInt(bytes, 64, -1); PutNativeInt(bytes, 68, 0); PutNativeInt(bytes, 72, children.Length); PutNativeInt(bytes, 76, children.Length);
        for (var i = 0; i < fields.Count; i++)
        {
            var encoded = Encoding.UTF8.GetBytes(fields[i].Name); var hash = 0;
            foreach (var value in encoded) hash = unchecked(hash * 53 + value);
            PutNativeInt(bytes, 80 + i * 12, hash); PutNativeInt(bytes, 84 + i * 12, fields[i].Offset);
            PutNativeInt(bytes, 88 + i * 12, ((encoded.Length + 1) << 2) | (i == 0 ? 1 : 0));
        }
        data.ToArray().CopyTo(bytes, dataOffset);
        return bytes;
    }

    private static byte[] NativeInt(int value) { var bytes = new byte[4]; PutNativeInt(bytes, 0, value); return bytes; }
    private static byte[] NativeString(string value)
    {
        var encoded = Encoding.UTF8.GetBytes(value); var bytes = new byte[encoded.Length + 5];
        PutNativeInt(bytes, 0, encoded.Length + 1); encoded.CopyTo(bytes, 4); return bytes;
    }
    private static int ReadNativeInt(byte[] bytes, int position) => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(position, 4));
    private static void PutNativeInt(byte[] bytes, int position, int value) => BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(position, 4), value);

    private static (string Name, byte[] Payload)[] NativeScalarPayloads(byte[] bytes)
    {
        var table = ReadNativeInt(bytes, 48); var data = ReadNativeInt(bytes, 60); var count = ReadNativeInt(bytes, 44);
        var entries = Enumerable.Range(0, count).Select(i => (Offset: ReadNativeInt(bytes, table + i * 12 + 4),
            Info: ReadNativeInt(bytes, table + i * 12 + 8))).ToArray();
        var result = new List<(string, byte[])>();
        foreach (var entry in entries.Where(entry => (entry.Info & 1) == 0))
        {
            var length = (entry.Info >> 2) & 511;
            var name = Encoding.UTF8.GetString(bytes, data + entry.Offset, length - 1);
            var end = entries.Where(other => other.Offset > entry.Offset).Select(other => other.Offset).DefaultIfEmpty(bytes.Length - data).Min();
            var start = (entry.Offset + length + 3) & ~3;
            result.Add((name, bytes.AsSpan(data + start, end - start).ToArray()));
        }
        return result.ToArray();
    }
}
