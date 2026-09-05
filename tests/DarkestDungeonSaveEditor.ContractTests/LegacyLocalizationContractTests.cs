internal static partial class ContractSuite
{
    private static void RunLegacyLocalizationContracts(ActiveContentSnapshot activeContent, ContractFixture fixture)
    {
        const string key = "hero_class_name_legacy_binary_probe";
        const string otherKey = "str_quirk_name_legacy_unrequested";
        var root = Path.Combine(fixture.RunRoot, "legacy-localization");
        var localization = Path.Combine(root, "localization");
        Directory.CreateDirectory(localization);
        var content = activeContent with
        {
            Sources = [new ActiveContentSource("local:legacy-binary", "Legacy Binary", "local", root, 0)]
        };
        var valid = CreateLegacyLocBytes(
        [
            (HashLoc2Key(key), [Encoding.UTF8.GetBytes(""), EncodeLoc2ColourOpenOnly("Legacy Name")]),
            (HashLoc2Key(otherKey), [Encoding.UTF8.GetBytes("</c>Unrequested Value")])
        ]);
        File.WriteAllBytes(Path.Combine(localization, "valid_english.loc"), valid);
        WriteLegacyLoc(Path.Combine(localization, "valid_schinese.loc"),
            new Dictionary<string, string> { [key] = "旧格式名称" });
        var probe = ReadLocalizationProbe(content, [key]);
        Assert(probe.Names[key] == new BilingualContentName("旧格式名称", "Legacy Name") && probe.Issues.Count == 0,
            "Legacy LOC must support multiple values per hash, first non-empty names, UTF-8, and cross-string compiled colour controls.");

        var basic = CreateLegacyLocBytes(
        [
            (HashLoc2Key(key), [Encoding.UTF8.GetBytes("Do Not Publish")]),
            (HashLoc2Key(otherKey), [Encoding.UTF8.GetBytes("Unrequested")])
        ]);
        static void SetUInt(byte[] bytes, int offset, uint value) =>
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset, 4), value);
        static int ValueRecord(byte[] bytes, string name)
        {
            var valuesOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes);
            for (var offset = 4104; offset < valuesOffset; offset += 12)
            {
                if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4)) == HashLoc2Key(name))
                {
                    var first = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 8, 4));
                    return valuesOffset + first * 12;
                }
            }

            throw new InvalidOperationException("Missing legacy fixture key.");
        }

        static int StringOffset(byte[] bytes, int valueRecord) =>
            (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4, 4)) +
            (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(valueRecord, 4));
        var mutations = new (string Name, Action<byte[]> Mutate)[]
        {
            ("header_overflow", bytes => SetUInt(bytes, 0, uint.MaxValue)),
            ("strings_outside", bytes => SetUInt(bytes, 4, (uint)bytes.Length + 1)),
            ("hash_table_overlap", bytes => SetUInt(bytes, 0, 4100)),
            ("reversed_tables", bytes => SetUInt(bytes, 4, 4104)),
            ("misaligned_hashes", bytes => SetUInt(bytes, 0, 4105)),
            ("misaligned_values", bytes => SetUInt(bytes, 4, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4, 4)) + 1)),
            ("count_overflow", bytes => SetUInt(bytes, 4104 + 4, uint.MaxValue)),
            ("index_overflow", bytes => SetUInt(bytes, 4104 + 8, uint.MaxValue)),
            ("duplicate_hash", bytes => SetUInt(bytes, 4104 + 12, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4104, 4)))),
            ("unrequested_bounds", bytes => SetUInt(bytes, ValueRecord(bytes, otherKey), uint.MaxValue)),
            ("unrequested_length", bytes => SetUInt(bytes, ValueRecord(bytes, otherKey) + 4, uint.MaxValue)),
            ("unrequested_zero_length", bytes => SetUInt(bytes, ValueRecord(bytes, otherKey) + 4, 0)),
            ("unrequested_nul", bytes =>
            {
                var record = ValueRecord(bytes, otherKey);
                var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(record + 4, 4));
                bytes[StringOffset(bytes, record) + length - 1] = 1;
            }),
            ("unrequested_utf8", bytes => bytes[StringOffset(bytes, ValueRecord(bytes, otherKey))] = 0xFF)
        };
        var rejectedNames = new List<string>();
        var partialNames = new List<string>();
        foreach (var mutation in mutations)
        {
            var bytes = basic.ToArray();
            mutation.Mutate(bytes);
            var name = $"broken_{mutation.Name}_english.loc";
            File.WriteAllBytes(Path.Combine(localization, name), bytes);
            (mutation.Name == "unrequested_utf8" ? partialNames : rejectedNames).Add(name);
        }

        File.WriteAllBytes(Path.Combine(localization, "broken_short_english.loc"), basic[..100]);
        rejectedNames.Add("broken_short_english.loc");
        File.WriteAllBytes(Path.Combine(localization, "broken_colour_english.loc"), CreateLegacyLocBytes(
        [
            (HashLoc2Key(key), [Encoding.UTF8.GetBytes("Do Not Publish")]),
            (HashLoc2Key(otherKey), [Encoding.UTF8.GetBytes("<c>")])
        ]));
        partialNames.Add("broken_colour_english.loc");
        WriteLoc2(Path.Combine(localization, "broken_loc2_layout_english.loc"),
            new Dictionary<string, string> { [key] = "Wrong Format" });
        rejectedNames.Add("broken_loc2_layout_english.loc");
        probe = ReadLocalizationProbe(content, [key]);
        Assert(probe.Names[key] == new BilingualContentName("旧格式名称", "Legacy Name") &&
               rejectedNames.All(name => probe.Issues.Any(issue =>
                   issue.Contains(name, StringComparison.Ordinal) &&
                   issue.Contains("Failed to read localization", StringComparison.Ordinal))),
            "Invalid headers, indexes, duplicate hashes, and corrupt value boundaries must reject the entire LOC file without losing valid tables.");
        Assert(partialNames.All(name => probe.Issues.Any(issue =>
                   issue.Contains(name, StringComparison.Ordinal) && issue.Contains("本地化部分读取", StringComparison.Ordinal))) &&
               partialNames.All(name => probe.Issues.All(issue =>
                   !issue.Contains(name, StringComparison.Ordinal) || !issue.Contains("Failed to read localization", StringComparison.Ordinal))),
            "An invalid UTF-8 or colour payload inside valid value boundaries must be skipped locally, not reject the entire LOC.");

        // Zero-hash sentinels may repeat, as in the existing LOC2 reader; they do not hide real keys.
        File.WriteAllBytes(Path.Combine(localization, "zero_sentinels_english.loc"), CreateLegacyLocBytes(
        [
            (0u, [Encoding.UTF8.GetBytes("")]),
            (0u, [Encoding.UTF8.GetBytes("")]),
            (HashLoc2Key(key), [Encoding.UTF8.GetBytes("Sentinel Probe")])
        ]));
        var zeroProbe = ReadLocalizationProbe(content, [key]);
        Assert(zeroProbe.Names[key].English == "Sentinel Probe" &&
               zeroProbe.Issues.All(issue => !issue.Contains("zero_sentinels", StringComparison.Ordinal)),
            "Repeated zero-hash sentinels must not be mistaken for conflicting named keys.");
    }

    private static void WriteLegacyLoc(string path, IReadOnlyDictionary<string, string> entries) =>
        File.WriteAllBytes(path, CreateLegacyLocBytes(entries.Select(pair =>
            (HashLoc2Key(pair.Key), new[] { Encoding.UTF8.GetBytes(pair.Value) })).ToArray()));

    private static byte[] CreateLegacyLocBytes(IReadOnlyList<(uint Hash, byte[][] Values)> entries)
    {
        const int hashStart = 8 + 4096;
        var groups = entries.OrderBy(entry => entry.Hash).ToArray();
        var values = groups.SelectMany(group => group.Values).ToArray();
        var valueStart = hashStart + groups.Length * 12;
        var stringStart = valueStart + values.Length * 12;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(valueStart);
        writer.Write(stringStart);
        for (var bucket = 0; bucket < 512; bucket++)
        {
            var first = Array.FindIndex(groups, group => group.Hash >> 23 == bucket);
            writer.Write(first < 0 ? 0 : hashStart + first * 12);
            writer.Write(groups.Count(group => group.Hash >> 23 == bucket));
        }

        var firstValue = 0;
        foreach (var group in groups)
        {
            writer.Write(group.Hash);
            writer.Write(group.Values.Length);
            writer.Write(firstValue);
            firstValue += group.Values.Length;
        }

        var relativeOffset = 0;
        foreach (var value in values)
        {
            writer.Write(relativeOffset);
            writer.Write(value.Length + 1);
            writer.Write(256);
            relativeOffset += value.Length + 1;
        }

        foreach (var value in values)
        {
            writer.Write(value);
            writer.Write((byte)0);
        }

        writer.Flush();
        return stream.ToArray();
    }
}
