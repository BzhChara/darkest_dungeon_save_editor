namespace DarkestDungeonSaveEditor.Core;

public static partial class BattleRoomAttachmentCatalog
{
    private static IReadOnlyList<EffectiveContentFile> ResolveEffectiveCurioFiles(
        IReadOnlyList<ActiveContentSource> sources, List<string> issues)
    {
        var enabledDlcPrefixes = ContentFileOverlay.GetEnabledDlcPrefixes(sources);
        return NativeContentFileResolver.Resolve(sources.SelectMany(source =>
                EnumeratePropFiles(source, enabledDlcPrefixes, issues, "curios", "*csv", "csv",
                    path => NativeResourceFileRules.IsCurioTypeFile(path, enabledDlcPrefixes) ||
                        NativeResourceFileRules.IsCurioPropFile(path, enabledDlcPrefixes))
                    .Select(path => new ContentFileCandidate(source, path))).ToArray(), sources,
            "Curio resource", issues);
    }

    private static CurioResources ReadCurioResources(
        IReadOnlyList<EffectiveContentFile> files, PropResources resources, List<string> issues,
        IReadOnlyList<string> enabledDlcPrefixes)
    {
        var types = new HashSet<string>(StringComparer.Ordinal);
        // Native loads every type library before it consumes any prop mapping.
        foreach (var file in files.Where(file => NativeResourceFileRules.IsCurioTypeFile(file.RelativePath, enabledDlcPrefixes)))
        {
            foreach (var block in NativeCurioCsvReader.TypeBlocks(NativeCurioCsvReader.Read(file.Path, 24, mapping: false)))
            {
                var first = block.FirstOrDefault();
                if (first is null || first.Count < 3 || !IsPropIdentity(first.Fields[2]))
                {
                    issues.Add($"奇物互动类型缺少有效 ID 行，已跳过该块：{file.Path}:{first?.Line}");
                    continue;
                }
                var id = first.Fields[2];
                types.Add(id);
                var resource = resources.Find(id);
                if (resource is null && resources.Find("curio_default") is { } parent)
                    resource = resources.Add(id, -1, parent.Data with { Parents = [.. parent.Data.Parents, "curio_default"] });
                if (resource is not null) resource.Data = resource.Data with { InstanceType = "curio" };
            }
        }
        foreach (var file in files.Where(file => NativeResourceFileRules.IsCurioPropFile(file.RelativePath, enabledDlcPrefixes)))
        foreach (var row in NativeCurioCsvReader.Read(file.Path, 16, mapping: true))
        {
            var fields = row.Fields;
            if (row.Count < 2 || fields[2].Length == 0) continue;
            if (!IsPropIdentity(fields[0]))
                throw new InvalidDataException($"奇物道具名称无效或超过原生缓冲区：{file.Path}:{row.Line}");
            var resource = resources.GetOrCreate(fields[0]);
            var name = fields[3].Length > 0 ? fields[3] :
                resource.Data.NameId.Length > 0 ? resource.Data.NameId : fields[1];
            resource.Data = resource.Data with
            {
                InstanceType = "curio", SpriteId = fields[1], CurioTypeId = fields[2], NameId = name
            };
        }
        var collidingProps = NativeResourceIdentity.FindCollisions(resources.Ids);
        var collidingTypes = NativeResourceIdentity.FindCollisions(types);
        foreach (var id in collidingProps.Concat(collidingTypes).Distinct(StringComparer.Ordinal))
            issues.Add($"地图资源原生 hash collision：{id}");
        return new CurioResources(types, collidingProps, collidingTypes);
    }

    private static string GetCurioNameId(BattleRoomAttachmentDefinition definition, PropResources resources) =>
        !definition.IsRegionBound && resources.Find(definition.Id) is { } prop ? prop.Data.NameId : definition.Id;

    private sealed record CurioResources(
        IReadOnlySet<string> TypeIds, IReadOnlySet<string> CollidingPropIds, IReadOnlySet<string> CollidingTypeIds);
}
