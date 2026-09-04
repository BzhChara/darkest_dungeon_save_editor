using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DarkestDungeonSaveEditor.Core;

public sealed record BattleEncounterBridgePackage(
    string PackageDirectory,
    string ProjectTitle,
    string MashFilePath,
    string ManifestPath,
    int MashType,
    int ExpectedMashIndex,
    IReadOnlyList<string> MonsterIds,
    string SourceMashSha256,
    string GeneratedMashSha256)
{
    public string OriginDungeonId { get; init; } = string.Empty;

    public int OriginDifficulty { get; init; }

    public BattleEncounterSourceKind SourceKind { get; init; }
}

public static class BattleEncounterBridgeBuilder
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private const string BridgeTitlePrefix = "DDSE Encounter Bridge - ";

    public static BattleEncounterBridgePackage Build(
        BattleEncounterCatalogResult catalog,
        BattleEncounterDefinition encounter,
        string? outputRoot = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(encounter);
        if (!encounter.TableGuard.Fingerprint.Equals(
                catalog.TableGuard.Fingerprint,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("所选遭遇不属于当前遭遇目录快照。");
        }
        if (catalog.TableGuard.ActiveSources.Any(IsGeneratedBridgeSource))
        {
            throw new InvalidOperationException(
                "当前档案已经启用另一个 DDSE Encounter Bridge。请先完成或删除其遭遇，" +
                "确认地图与战斗不再引用新增索引，再禁用旧 Bridge 并重新加载内容目录。");
        }

        BattleEncounterCatalog.ValidateBridgeEncounter(encounter);
        var currentRows = catalog.Encounters
            .Where(candidate =>
                candidate.SourceKind == BattleEncounterSourceKind.Standard &&
                candidate.MashType == encounter.MashType)
            .OrderBy(candidate => candidate.MashIndex)
            .ToArray();
        if (currentRows.Select(candidate => candidate.SourcePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Skip(1)
                .Any())
        {
            throw new InvalidOperationException(
                "当前类型由多个标准遭遇文件共同扩展，无法证明追加后的运行时索引顺序。" +
                "本次不会生成 Bridge。");
        }

        string sourceMashPath;
        var expectedMashIndex = currentRows.Length;
        if (currentRows.Length > 0)
        {
            if (!currentRows.Select((candidate, index) =>
                        candidate.CanPlaceDirectly && candidate.MashIndex == index)
                    .All(matches => matches))
            {
                throw new InvalidOperationException(
                    "当前类型没有连续且可证明的标准遭遇索引，不能生成 Bridge。");
            }
            sourceMashPath = currentRows[0].SourcePath;
        }
        else
        {
            sourceMashPath = ResolveEmptyTypeTargetMashPath(catalog);
        }

        var sourceMashSha256 = ComputeSha256(sourceMashPath);
        var guardedSource = catalog.TableGuard.EffectiveFiles.SingleOrDefault(file =>
            Path.GetFullPath(file.Path).Equals(
                Path.GetFullPath(sourceMashPath),
                StringComparison.OrdinalIgnoreCase));
        if (guardedSource is null ||
            !sourceMashSha256.Equals(guardedSource.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("标准遭遇文件在桥接准备期间已经变化。");
        }

        outputRoot ??= Path.Combine(
            SaveEditorLocations.CreateDefault().WorkspaceDirectory,
            "encounter_bridge");
        outputRoot = Path.GetFullPath(outputRoot);
        Directory.CreateDirectory(outputRoot);
        var encounterSlug = CreateSlug(string.Join("_", encounter.MonsterIds));
        var sessionId = $"{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}";
        var projectTitle =
            $"{BridgeTitlePrefix}{catalog.DungeonId} {catalog.Difficulty} - {encounterSlug}";
        var packageName = projectTitle;
        var packageDirectory = Path.Combine(outputRoot, sessionId, packageName);
        if (Directory.Exists(packageDirectory))
        {
            throw new IOException($"Bridge 输出目录已经存在：{packageDirectory}");
        }

        var relativeMashPath = ResolveLocalModMashPath(catalog.TableGuard, sourceMashPath);
        var mashFilePath = Path.Combine(
            packageDirectory,
            relativeMashPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(mashFilePath)!);
        var sourceBytes = File.ReadAllBytes(sourceMashPath);
        var newline = EndsWithLineBreak(sourceBytes) ? string.Empty : Environment.NewLine;
        var kind = encounter.MashType switch
        {
            0 => "hall",
            1 => "room",
            2 => "boss",
            _ => throw new InvalidOperationException("Encounter Bridge 不支持该 mash_type。")
        };
        var appendedOptions = encounter.MashType == 2
            ? string.Empty
            : " .limit 1 .can_be_ambush false";
        var appendedLine =
            $"{newline}{kind}: .chance 0 .types {string.Join(' ', encounter.MonsterIds)}" +
            appendedOptions + Environment.NewLine;
        var appendedBytes = Utf8NoBom.GetBytes(appendedLine);
        using (var output = new FileStream(
                   mashFilePath,
                   FileMode.CreateNew,
                   FileAccess.Write,
                   FileShare.None))
        {
            output.Write(sourceBytes);
            output.Write(appendedBytes);
            output.Flush(flushToDisk: true);
        }

        var projectPath = Path.Combine(packageDirectory, "project.xml");
        var modDataPath = packageDirectory.Replace('\\', '/').TrimEnd('/') + "/";
        File.WriteAllText(
            projectPath,
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <project>
              <PreviewIconFile/>
              <ItemDescriptionShort>Deterministic encounter carrier generated for one active expedition table.</ItemDescriptionShort>
              <ModDataPath>{SecurityElement.Escape(modDataPath)}</ModDataPath>
              <Title>{SecurityElement.Escape(projectTitle)}</Title>
              <Language>english</Language>
              <Visibility>private</Visibility>
              <UploadMode>direct_upload</UploadMode>
              <VersionMajor>0</VersionMajor>
              <VersionMinor>0</VersionMinor>
              <TargetBuild>0</TargetBuild>
              <Tags><Tags>Gameplay Tweaks</Tags></Tags>
              <ItemDescription>Local Encounter Bridge generated by Darkest Dungeon Save Editor. Keep enabled while any save still references its appended mash index.</ItemDescription>
              <PublishedFileId>0</PublishedFileId>
            </project>
            """,
            Utf8NoBom);

        var manifestPath = Path.Combine(packageDirectory, "ddse-encounter-bridge.json");
        var generatedMashSha256 = ComputeSha256(mashFilePath);
        File.WriteAllText(
            manifestPath,
            JsonSerializer.Serialize(
                new
                {
                    version = 2,
                    generatedAtUtc = DateTime.UtcNow,
                    catalog.DungeonId,
                    catalog.Difficulty,
                    mashType = encounter.MashType,
                    expectedMashIndex,
                    monsterIds = encounter.MonsterIds,
                    sourceKind = encounter.SourceKind.ToString(),
                    originDungeonId = encounter.OriginDungeonId,
                    originDifficulty = encounter.OriginDifficulty,
                    roamingId = encounter.RoamingId,
                    sourceLabel = encounter.SourceLabel,
                    sourcePath = encounter.SourcePath,
                    sourceRelativePath = encounter.SourceRelativePath,
                    sourceLine = encounter.SourceLine,
                    sourceMashPath,
                    sourceMashSha256,
                    generatedMashPath = relativeMashPath,
                    generatedMashSha256,
                    tableFingerprint = catalog.TableGuard.Fingerprint,
                    appendedLine = appendedLine.Trim()
                },
                JsonSupport.SerializerOptions),
            Utf8NoBom);
        File.WriteAllText(
            Path.Combine(packageDirectory, "modfiles.txt"),
            $"{relativeMashPath} {new FileInfo(mashFilePath).Length}" + Environment.NewLine,
            Utf8NoBom);

        return new BattleEncounterBridgePackage(
            packageDirectory,
            projectTitle,
            mashFilePath,
            manifestPath,
            encounter.MashType,
            expectedMashIndex,
            encounter.MonsterIds,
            sourceMashSha256,
            generatedMashSha256)
        {
            OriginDungeonId = encounter.OriginDungeonId,
            OriginDifficulty = encounter.OriginDifficulty,
            SourceKind = encounter.SourceKind
        };
    }

    private static bool IsGeneratedBridgeSource(ActiveContentSource source) =>
        source.DisplayName.StartsWith("DDSE Encounter Bridge", StringComparison.OrdinalIgnoreCase) ||
        File.Exists(Path.Combine(source.Directory, "ddse-encounter-bridge.json")) ||
        File.Exists(Path.Combine(source.Directory, "ddse-encounter-bridge-probe.json"));

    private static string ResolveEmptyTypeTargetMashPath(BattleEncounterCatalogResult catalog)
    {
        var expectedFileName =
            $"{catalog.DungeonId}.{catalog.Difficulty}.mash.darkest";
        var exact = catalog.TableGuard.EffectiveFiles
            .Where(file =>
                BattleEncounterCatalog.ClassifyFile(file.Path) == BattleEncounterSourceKind.Standard &&
                Path.GetFileName(file.RelativePath).Equals(
                    expectedFileName,
                    StringComparison.OrdinalIgnoreCase))
            .OrderBy(file => file.RelativePath.Length)
            .ThenBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (exact.Length > 0)
        {
            return exact[0].Path;
        }

        var standardFiles = catalog.TableGuard.EffectiveFiles
            .Where(file =>
                BattleEncounterCatalog.ClassifyFile(file.Path) == BattleEncounterSourceKind.Standard)
            .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (standardFiles.Length > 0)
        {
            return standardFiles[0].Path;
        }

        throw new InvalidOperationException(
            "当前副本没有可承载首个索引的标准遭遇文件。" +
            "本次不会生成 Bridge。");
    }

    private static string ResolveLocalModMashPath(
        BattleEncounterTableGuard guard,
        string sourceMashPath)
    {
        var fingerprint = guard.EffectiveFiles.SingleOrDefault(file =>
            Path.GetFullPath(file.Path).Equals(
                Path.GetFullPath(sourceMashPath),
                StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("无法在有效遭遇文件清单中定位标准表。");
        var normalized = fingerprint.RelativePath.Replace('\\', '/');
        var padded = $"/{normalized.TrimStart('/')}";
        if (!padded.Contains("/dungeons/", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("标准遭遇文件不位于可桥接的 dungeons 目录。");
        }

        return normalized.TrimStart('/');
    }

    private static bool EndsWithLineBreak(byte[] bytes) =>
        bytes.Length > 0 && bytes[^1] is (byte)'\r' or (byte)'\n';

    private static string CreateSlug(string value)
    {
        var result = new StringBuilder(value.Length);
        foreach (var character in value.Trim())
        {
            result.Append(char.IsLetterOrDigit(character) || character is '-' or '_'
                ? character
                : '_');
        }

        var slug = result.ToString().Trim('_');
        if (slug.Length == 0)
        {
            slug = "encounter";
        }
        const int readableLength = 36;
        if (slug.Length > readableLength)
        {
            slug = slug[..readableLength].TrimEnd('_');
        }

        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..8];
        return $"{slug}-{hash}";
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}

public static class BattleEncounterBridgeProbeBuilder
{
    public static BattleEncounterBridgePackage Build(
        BattleEncounterCatalogResult catalog,
        BattleEncounterDefinition encounter,
        string? outputRoot = null) =>
        BattleEncounterBridgeBuilder.Build(catalog, encounter, outputRoot);
}
