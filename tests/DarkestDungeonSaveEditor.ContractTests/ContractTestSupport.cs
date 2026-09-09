internal static class ContractTestSupport
{
    internal static TrinketStorageCatalogResult LoadStorageCapacityProbe(
        ActiveContentSnapshot baseline,
        string runRoot,
        string probeName,
        string configText)
    {
        var modRoot = Path.Combine(runRoot, probeName);
        var inventoryRoot = Path.Combine(modRoot, "inventory");
        Directory.CreateDirectory(inventoryRoot);
        File.WriteAllText(
            Path.Combine(inventoryRoot, $"{probeName}.inventory.system_configs.darkest"),
            configText,
            new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(modRoot, "modfiles.txt"), $"inventory/{probeName}.inventory.system_configs.darkest\n");
        return TrinketStorageCatalog.Load(baseline with
        {
            Sources = baseline.Sources
                .Append(new ActiveContentSource(
                    $"local:{probeName}",
                    probeName,
                    "local",
                    modRoot,
                    -1500))
                .ToArray()
        });
    }

    internal static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Contract assertion failed: {message}");
        }
    }

    internal static (int Width, int Height) ReadPngSize(string path)
    {
        var header = File.ReadAllBytes(path).AsSpan();
        Assert(
            header.Length >= 24 &&
            header[0] == 0x89 &&
            header[1] == 0x50 &&
            header[2] == 0x4E &&
            header[3] == 0x47,
            $"Expected a valid PNG file: {path}");
        return (
            BinaryPrimitives.ReadInt32BigEndian(header.Slice(16, 4)),
            BinaryPrimitives.ReadInt32BigEndian(header.Slice(20, 4)));
    }

    internal static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    internal static BitmapSource LoadBgra32(string path)
    {
        using var stream = File.OpenRead(path);
        var decoder = new PngBitmapDecoder(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        var bitmap = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
        bitmap.Freeze();
        return bitmap;
    }

    internal static (byte R, byte G, byte B, byte A) ReadPixel(BitmapSource bitmap, int x, int y)
    {
        var pixel = new byte[4];
        bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return (pixel[2], pixel[1], pixel[0], pixel[3]);
    }

    internal static bool IsOpaqueDark((byte R, byte G, byte B, byte A) pixel, byte maximumChannel) =>
        pixel.A >= 250 &&
        pixel.R <= maximumChannel &&
        pixel.G <= maximumChannel &&
        pixel.B <= maximumChannel;

    internal static bool IsStraightAlphaRedEdge((byte R, byte G, byte B, byte A) pixel) =>
        pixel.A is >= 20 and <= 45 &&
        pixel.R >= 235 &&
        pixel.G is >= 35 and <= 50 &&
        pixel.B <= 3;

    internal static bool IsRedOutline(
        (byte R, byte G, byte B, byte A) pixel,
        byte minimumRed,
        byte maximumRed) =>
        pixel.A >= 210 &&
        pixel.R >= minimumRed &&
        pixel.R <= maximumRed &&
        pixel.G is >= 35 and <= 43 &&
        pixel.R >= pixel.G * 3 &&
        pixel.B <= 10;

    internal static void AssertHeroGenerationRejected(
        HeroClassCatalogResult catalog,
        HeroClassDefinition heroClass,
        IReadOnlyCollection<string> selectedInitialQuirkIds,
        string expectedMessage)
    {
        var rejected = false;
        try
        {
            _ = StagecoachHeroCandidateFactory.Generate(
                catalog,
                heroClass,
                seed: 1729,
                selectedInitialQuirkIds: selectedInitialQuirkIds);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains(expectedMessage, StringComparison.Ordinal))
        {
            rejected = true;
        }

        Assert(
            rejected,
            $"Selected initial quirks should be rejected with a message containing '{expectedMessage}'.");
    }

    internal static void AssertInitialQuirkSelectionRejected(
        HeroClassCatalogResult catalog,
        HeroClassDefinition heroClass,
        IReadOnlyCollection<string> selectedInitialQuirkIds,
        string expectedMessage)
    {
        var rejected = false;
        try
        {
            StagecoachHeroCandidateFactory.ValidateInitialQuirkSelection(
                catalog,
                heroClass,
                selectedInitialQuirkIds);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains(expectedMessage, StringComparison.Ordinal))
        {
            rejected = true;
        }

        Assert(
            rejected,
            $"Initial quirk validation should reject the selection with a message containing '{expectedMessage}'.");
    }

    internal static void AssertHeroLevelGenerationRejected(
        HeroClassCatalogResult catalog,
        HeroClassDefinition heroClass,
        int resolveLevel,
        string expectedMessage)
    {
        var rejected = false;
        try
        {
            _ = StagecoachHeroCandidateFactory.Generate(
                catalog,
                heroClass,
                seed: 1729,
                resolveLevel: resolveLevel,
                selectedInitialQuirkIds: []);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains(expectedMessage, StringComparison.Ordinal))
        {
            rejected = true;
        }

        Assert(rejected, $"Hero level {resolveLevel} should be rejected with a message containing '{expectedMessage}'.");
    }

    internal static void SetRevision(string path, ReadOnlySpan<byte> revision)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        stream.Position = 4;
        stream.Write(revision);
        stream.Flush(flushToDisk: true);
    }

    internal static byte[] ReadRevision(string path)
    {
        using var stream = File.OpenRead(path);
        stream.Position = 4;
        var result = new byte[4];
        stream.ReadExactly(result);
        return result;
    }

    internal static void WriteLoc2(string path, IReadOnlyDictionary<string, string> entries)
    {
        WriteLoc2Raw(
            path,
            entries.ToDictionary(
                pair => pair.Key,
                pair => Encoding.UTF8.GetBytes(pair.Value),
                StringComparer.Ordinal));
    }

    internal static void WriteLoc2Raw(string path, IReadOnlyDictionary<string, byte[]> entries)
    {
        const int hashRecordOffset = 12 + 4096;
        const int hashRecordSize = 12;
        const int groupRecordSize = 8;
        const int valueRecordSize = 12;
        var groups = entries
            .GroupBy(pair => HashLoc2Key(pair.Key))
            .OrderBy(group => group.Key)
            .Select(group => (Hash: group.Key, Values: group.ToArray()))
            .ToArray();
        var values = groups.SelectMany(group => group.Values).ToArray();
        var groupTableOffset = hashRecordOffset + groups.Length * hashRecordSize;
        var valueTableOffset = groupTableOffset + groups.Length * groupRecordSize;
        var stringDataOffset = valueTableOffset + values.Length * valueRecordSize;

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(groupTableOffset);
        writer.Write(valueTableOffset);
        writer.Write(stringDataOffset);
        for (var bucket = 0; bucket < 512; bucket++)
        {
            var first = -1;
            var count = 0;
            for (var index = 0; index < groups.Length; index++)
            {
                if ((groups[index].Hash >> 23) != bucket)
                {
                    continue;
                }

                first = first < 0 ? index : first;
                count++;
            }

            writer.Write(first < 0 ? 0 : hashRecordOffset + first * hashRecordSize);
            writer.Write(count);
        }

        for (var index = 0; index < groups.Length; index++)
        {
            writer.Write(groups[index].Hash);
            writer.Write(index);
            writer.Write(1);
        }

        var firstValueIndex = 0;
        foreach (var group in groups)
        {
            writer.Write(firstValueIndex);
            writer.Write(group.Values.Length);
            firstValueIndex += group.Values.Length;
        }

        var encodedValues = values.Select(pair => pair.Value).ToArray();
        var relativeStringOffset = 0;
        foreach (var encodedValue in encodedValues)
        {
            writer.Write(relativeStringOffset);
            writer.Write(encodedValue.Length + 1);
            writer.Write(256);
            relativeStringOffset += encodedValue.Length + 1;
        }

        foreach (var encodedValue in encodedValues)
        {
            writer.Write(encodedValue);
            writer.Write((byte)0);
        }

        writer.Flush();
        File.WriteAllBytes(path, stream.ToArray());
    }

    internal static byte[] EncodeLoc2Colour(string visibleText)
    {
        return ConcatenateBytes(
            Encoding.UTF8.GetBytes("<c>"),
            [0x80, 0x81, 0xFF, 0x00, 0x10, 0x20],
            Encoding.UTF8.GetBytes(visibleText),
            Encoding.UTF8.GetBytes("</c>"));
    }

    internal static byte[] EncodeLoc2ColourOpenOnly(string visibleText)
    {
        return ConcatenateBytes(
            Encoding.UTF8.GetBytes("<c>"),
            [0x80, 0x81, 0xFF, 0x00, 0x10, 0x20],
            Encoding.UTF8.GetBytes(visibleText));
    }

    internal static byte[] ConcatenateBytes(params byte[][] values) =>
        values.SelectMany(value => value).ToArray();

    internal static void CorruptLoc2ValueOffset(string path, string key)
    {
        var bytes = File.ReadAllBytes(path);
        var valueRecordOffset = FindLoc2ValueRecordOffset(bytes, key);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(valueRecordOffset, sizeof(uint)), uint.MaxValue);
        File.WriteAllBytes(path, bytes);
    }

    internal static void SetLoc2Hash(string path, string key, uint hash)
    {
        var bytes = File.ReadAllBytes(path);
        var hashRecordOffset = FindLoc2HashRecordOffset(bytes, key);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(hashRecordOffset, sizeof(uint)), hash);
        File.WriteAllBytes(path, bytes);
    }

    internal static void CorruptLoc2ValueLength(string path, string key, uint byteLength)
    {
        var bytes = File.ReadAllBytes(path);
        var valueRecordOffset = FindLoc2ValueRecordOffset(bytes, key);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(valueRecordOffset + 4, sizeof(uint)), byteLength);
        File.WriteAllBytes(path, bytes);
    }

    internal static void CorruptLoc2ValueUtf8(string path, string key)
    {
        var bytes = File.ReadAllBytes(path);
        var valueRecordOffset = FindLoc2ValueRecordOffset(bytes, key);
        var stringDataOffset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8, sizeof(int)));
        var relativeOffset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(valueRecordOffset, sizeof(int)));
        bytes[stringDataOffset + relativeOffset] = 0xFF;
        File.WriteAllBytes(path, bytes);
    }

    internal static void CorruptLoc2ValueTerminator(string path, string key)
    {
        var bytes = File.ReadAllBytes(path);
        var valueRecordOffset = FindLoc2ValueRecordOffset(bytes, key);
        var stringDataOffset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8, sizeof(int)));
        var relativeOffset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(valueRecordOffset, sizeof(int)));
        var byteLength = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(valueRecordOffset + 4, sizeof(int)));
        bytes[stringDataOffset + relativeOffset + byteLength - 1] = 0x7F;
        File.WriteAllBytes(path, bytes);
    }

    internal static int FindLoc2ValueRecordOffset(byte[] bytes, string key)
    {
        const int groupRecordSize = 8;
        const int valueRecordSize = 12;
        var groupTableOffset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0, sizeof(int)));
        var valueTableOffset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4, sizeof(int)));
        var hashRecordOffset = FindLoc2HashRecordOffset(bytes, key);
        var groupIndex = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(hashRecordOffset + 4, sizeof(int)));
        var groupOffset = groupTableOffset + groupIndex * groupRecordSize;
        var firstValueIndex = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(groupOffset, sizeof(int)));
        return valueTableOffset + firstValueIndex * valueRecordSize;
    }

    internal static int FindLoc2HashRecordOffset(byte[] bytes, string key)
    {
        const int hashRecordOffset = 12 + 4096;
        const int hashRecordSize = 12;
        var groupTableOffset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0, sizeof(int)));
        var targetHash = HashLoc2Key(key);
        for (var recordOffset = hashRecordOffset;
             recordOffset < groupTableOffset;
             recordOffset += hashRecordSize)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(recordOffset, sizeof(uint))) != targetHash)
            {
                continue;
            }

            return recordOffset;
        }

        throw new InvalidOperationException($"LOC2 test key '{key}' was not found.");
    }

    internal static uint HashLoc2Key(string value)
    {
        var hash = 0u;
        foreach (var valueByte in Encoding.UTF8.GetBytes(value))
        {
            unchecked
            {
                hash = hash * 53u + valueByte;
            }
        }

        return hash;
    }

    internal static double ContrastRatio(string foregroundHex, string backgroundHex)
    {
        static double RelativeLuminance(string hex)
        {
            if (hex.Length is not (7 or 9) || hex[0] != '#')
            {
                throw new InvalidDataException($"Unsupported contract colour '{hex}'.");
            }

            var channelStart = hex.Length == 9 ? 3 : 1;
            var channels = Enumerable.Range(0, 3)
                .Select(index => byte.Parse(
                    hex.Substring(channelStart + index * 2, 2),
                    System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture) / 255d)
                .Select(channel => channel <= 0.04045
                    ? channel / 12.92
                    : Math.Pow((channel + 0.055) / 1.055, 2.4))
                .ToArray();
            return channels[0] * 0.2126 + channels[1] * 0.7152 + channels[2] * 0.0722;
        }

        var foreground = RelativeLuminance(foregroundHex);
        var background = RelativeLuminance(backgroundHex);
        return (Math.Max(foreground, background) + 0.05) /
               (Math.Min(foreground, background) + 0.05);
    }
}
