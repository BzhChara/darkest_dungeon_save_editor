using System.Reflection;

internal static partial class ContractSuite
{
    private static void RunLocalizationEntryIsolationContracts(ActiveContentSnapshot activeContent, ContractFixture fixture)
    {
        const string goodKey = "hero_class_name_isolation_good";
        const string badUtf8Key = "str_quirk_name_isolation_bad_utf8";
        const string badColourKey = "str_inventory_title_trinketisolation_bad_colour";
        const string multiKey = "hero_class_name_isolation_Az";
        const string multiVariantKey = "hero_class_name_isolation_BE";
        const string emptyKey = "str_curio_title_isolation_empty";
        string[] keys = [goodKey, badUtf8Key, badColourKey, multiKey, emptyKey];
        Assert(HashLoc2Key(multiKey) == HashLoc2Key(multiVariantKey),
            "The binary fixture must exercise a genuine multi-value hash group.");
        var root = Path.Combine(fixture.RunRoot, "localization-entry-isolation");

        ActiveContentSnapshot ContentAt(string directory) => activeContent with
        {
            Sources = [new ActiveContentSource("local:entry-isolation", "Entry Isolation", "local", directory, 0)]
        };
        foreach (var extension in new[] { ".loc", ".loc2" })
        {
            var modRoot = Path.Combine(root, extension[1..]);
            var localizationRoot = Path.Combine(modRoot, "localization");
            Directory.CreateDirectory(localizationRoot);
            var relative = $"localization/probe_english{extension}";
            var path = Path.Combine(modRoot, relative);
            var content = ContentAt(modRoot);
            var entries = new Dictionary<string, byte[]>
            {
                [goodKey] = EncodeLoc2ColourOpenOnly("Valid Name"),
                [badUtf8Key] = [0xFF],
                [badColourKey] = "<c>"u8.ToArray(),
                [multiKey] = [0xFE],
                [multiVariantKey] = "Valid Alternative"u8.ToArray(),
                [emptyKey] = "  "u8.ToArray()
            };
            for (var index = 0; index < 40; index++)
            {
                entries[$"unrequested_bad_dialogue_{index}"] = [0xFF];
            }

            WriteBinaryIsolationFixture(path, extension, entries);
            var validBytes = File.ReadAllBytes(path);
            File.WriteAllText(Path.Combine(modRoot, "modfiles.txt"), relative);
            var probe = ReadLocalizationProbe(content, keys);
            Assert(probe.Names[goodKey] == new BilingualContentName("", "Valid Name") &&
                   probe.Names[badUtf8Key] == BilingualContentName.Empty &&
                   probe.Names[badColourKey] == BilingualContentName.Empty &&
                   probe.Names[multiKey].English == "Valid Alternative" &&
                   probe.Names[emptyKey] == BilingualContentName.Empty &&
                   File.ReadAllBytes(path).SequenceEqual(validBytes),
                $"{extension} must keep valid names, skip only bad text, select the first remaining valid group value and leave missing names empty without rewriting bytes.");
            Assert(probe.Issues.Count == 1 && probe.Issues.Single().Contains("跳过 43 个", StringComparison.Ordinal) &&
                   probe.Issues.Single().Contains("本地化部分读取", StringComparison.Ordinal) &&
                   probe.Issues.Single().Split("值索引", StringSplitOptions.None).Length == 4 &&
                   !probe.Issues.Single().Contains('\uFFFD'),
                $"{extension} must aggregate bad requested/unrequested text into one count and at most three examples, without replacement-character decoding.");

            var xmlPath = Path.Combine(localizationRoot, "fallback.string_table.xml");
            WriteLocalizationXml(xmlPath,
                new Dictionary<string, string> { [badColourKey] = "Valid XML Fallback" },
                new Dictionary<string, string> { [badUtf8Key] = "只有中文" });
            File.AppendAllText(Path.Combine(modRoot, "modfiles.txt"), "\nlocalization/fallback.string_table.xml");
            probe = ReadLocalizationProbe(content, keys);
            Assert(probe.Names[badColourKey].English == "Valid XML Fallback" &&
                   probe.Names[badUtf8Key] == new BilingualContentName("只有中文", ""),
                "An invalid binary value may fall back to another eligible value in the same language, but never copy the other language.");

            var lastValueRecord = extension == ".loc"
                ? BinaryPrimitives.ReadInt32LittleEndian(validBytes.AsSpan(4, 4)) - 12
                : BinaryPrimitives.ReadInt32LittleEndian(validBytes.AsSpan(8, 4)) - 12;
            var stringsStart = extension == ".loc"
                ? BinaryPrimitives.ReadInt32LittleEndian(validBytes.AsSpan(4, 4))
                : BinaryPrimitives.ReadInt32LittleEndian(validBytes.AsSpan(8, 4));
            foreach (var fault in new[] { "bounds", "nul", "index" })
            {
                var bytes = validBytes.ToArray();
                if (fault == "bounds")
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(lastValueRecord, 4), uint.MaxValue);
                }
                else if (fault == "nul")
                {
                    var offset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(lastValueRecord, 4));
                    var length = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(lastValueRecord + 4, 4));
                    bytes[stringsStart + offset + length - 1] = 0x7F;
                }
                else
                {
                    // LOC's first-value index and LOC2's group index both begin at byte 4112.
                    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4112, 4), uint.MaxValue);
                }

                File.WriteAllBytes(path, bytes);
                var failed = ReadLocalizationProbe(content, keys);
                Assert(failed.Names[goodKey] == BilingualContentName.Empty && failed.Names[multiKey] == BilingualContentName.Empty &&
                       failed.Names[badColourKey].English == "Valid XML Fallback" &&
                       failed.Issues.Count == 1 && failed.Issues.Single().Contains("Failed to read localization", StringComparison.Ordinal) &&
                       !failed.Issues.Single().Contains("本地化部分读取", StringComparison.Ordinal),
                    $"{extension} structural {fault} damage must reject all binary names, without leaking partial-success diagnostics from earlier text skips.");
            }
        }

        var xmlRoot = Path.Combine(root, "xml");
        Directory.CreateDirectory(Path.Combine(xmlRoot, "localization"));
        var xmlFile = Path.Combine(xmlRoot, "localization/probe.string_table.xml");
        File.WriteAllText(Path.Combine(xmlRoot, "modfiles.txt"), "localization/probe.string_table.xml");
        File.WriteAllText(xmlFile, $"""
            <root>
              <language id="english">
                <entry id="{goodKey}"><![CDATA[Valid XML Name]]></entry>
                <entry>Missing Identity</entry>
                <entry id="{badColourKey}"><entry id="{badUtf8Key}">Ambiguous</entry></entry>
                <entry id="{emptyKey}"> </entry>
                <entry id="hero_name_isolation_good">Valid Personal Name</entry>
              </language>
              <language><entry id="hero_name_isolation_bad">No Language</entry></language>
              <language id="schinese"><entry id="{goodKey}">有效名称</entry></language>
            </root>
            """);
        var xmlOriginal = File.ReadAllBytes(xmlFile);
        var xmlProbe = ReadLocalizationProbe(ContentAt(xmlRoot), keys);
        Assert(xmlProbe.Names[goodKey] == new BilingualContentName("有效名称", "Valid XML Name") &&
               xmlProbe.Names[badColourKey] == BilingualContentName.Empty &&
               xmlProbe.Names[badUtf8Key] == BilingualContentName.Empty &&
               xmlProbe.Issues.Count == 1 && xmlProbe.Issues.Single().Contains("跳过 4 个", StringComparison.Ordinal) &&
               File.ReadAllBytes(xmlFile).SequenceEqual(xmlOriginal),
            "A well-formed XML document must isolate missing identities and ambiguous nested entries without guessing, flattening or rewriting them.");
        var heroes = HeroClassCatalog.Load(ContentAt(xmlRoot));
        Assert(heroes.HeroNames.SequenceEqual(["Valid Personal Name"]) &&
               heroes.Issues.Any(issue => issue.Contains("本地化部分读取", StringComparison.Ordinal)),
            "The personal-name pool must use the same XML entry validation and keep unrelated valid names.");

        File.WriteAllText(xmlFile, $"<root><language id=\"english\"><entry id=\"{goodKey}\">Must Not Leak</entry><entry>broken</wrong></language></root>");
        xmlProbe = ReadLocalizationProbe(ContentAt(xmlRoot), keys);
        Assert(xmlProbe.Names.Values.All(value => value == BilingualContentName.Empty) &&
               xmlProbe.Issues.Single().Contains("Failed to read localization", StringComparison.Ordinal),
            "Late XML syntax failure must not publish earlier valid-looking entries or revive regex recovery.");
        var invalidEncoding = Encoding.UTF8.GetBytes($"<root><language id=\"english\"><entry id=\"{goodKey}\">Must Not Leak</entry><entry id=\"bad\">X</entry></language></root>");
        var marker = Encoding.UTF8.GetString(invalidEncoding).IndexOf(">X<", StringComparison.Ordinal) + 1;
        invalidEncoding[marker] = 0xFF;
        File.WriteAllBytes(xmlFile, invalidEncoding);
        xmlProbe = ReadLocalizationProbe(ContentAt(xmlRoot), keys);
        Assert(xmlProbe.Names.Values.All(value => value == BilingualContentName.Empty) &&
               xmlProbe.Issues.Single().Contains("Failed to read localization", StringComparison.Ordinal),
            "XML byte encoding damage must still reject the document instead of guessing byte boundaries or replacement-decoding text.");
    }

    private static void WriteBinaryIsolationFixture(string path, string extension, IReadOnlyDictionary<string, byte[]> entries)
    {
        if (extension == ".loc2")
        {
            WriteLoc2Raw(path, entries);
        }
        else
        {
            File.WriteAllBytes(path, CreateLegacyLocBytes(entries.GroupBy(pair => HashLoc2Key(pair.Key))
                .Select(group => (group.Key, group.Select(pair => pair.Value).ToArray())).ToArray()));
        }
    }

    private static (IReadOnlyDictionary<string, string> Names, IReadOnlyList<string> Issues)
        ReadCompiledLocalizationProbe(string path, string[] keys, bool legacy)
    {
        var type = typeof(TrinketCatalog).Assembly.GetType(
            $"DarkestDungeonSaveEditor.Core.{(legacy ? "LocLocalizationReader" : "Loc2LocalizationReader")}")!;
        var issues = new List<string>();
        var names = (IReadOnlyDictionary<string, string>)type.GetMethod("Read", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [path, keys, issues])!;
        return (names, issues);
    }
}
