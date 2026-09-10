using System.Text.Json;

namespace DarkestDungeonSaveEditor.Core;

public static partial class BattleRoomAttachmentCatalog
{
    private static IReadOnlyList<EffectiveContentFile> ResolveEffectiveResourceFiles(
        IReadOnlyList<ActiveContentSource> sources, List<string> issues)
    {
        var enabledDlcPrefixes = ContentFileOverlay.GetEnabledDlcPrefixes(sources);
        var files = NativeContentFileResolver.Resolve(sources.SelectMany(source =>
                EnumeratePropFiles(source, enabledDlcPrefixes, issues, "props", "*json", "json",
                    path => NativeResourceFileRules.PropResourceStage(path, enabledDlcPrefixes) >= 0)
                    .Select(path => new ContentFileCandidate(source, path))).ToArray(), sources,
            "Map prop resource", issues);
        // 0x1404D87A0 opens the three root paths before the subdirectory searches.
        // Stable ordering preserves native resolver slots within each searched family.
        return files.OrderBy(file => NativeResourceFileRules.PropResourceStage(file.RelativePath, enabledDlcPrefixes)).ToArray();
    }

    private static PropResources ReadResources(
        IReadOnlyList<EffectiveContentFile> files)
    {
        var resources = new PropResources();
        foreach (var file in files)
        {
            try { ReadPropFile(file, resources); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
            {
                // A partial library could revive an earlier object or miss a parent.
                throw new InvalidDataException($"地图资源定义读取失败：{file.Path}；{error.Message}", error);
            }
        }
        return resources;
    }

    private static string? GetResourceRejection(
        BattleRoomAttachmentDefinition definition, PropResources resources, CurioResources curios)
    {
        if (curios.CollidingPropIds.Contains(definition.Id))
            return $"地图资源存在原生 hash collision：{definition.Id}";
        var isRegional = definition.Kind is BattleRoomAttachmentKind.Trap or BattleRoomAttachmentKind.Obstacle;
        if (isRegional && string.IsNullOrWhiteSpace(definition.OriginDungeonId))
            return "无法从资源池路径确认所属副本区域";
        var expectedType = definition.Kind switch
        {
            BattleRoomAttachmentKind.Trap => "trap",
            BattleRoomAttachmentKind.Obstacle => "obstacle",
            _ => "curio"
        };
        // The shared catalog has no selected quest difficulty. Accept only when every
        // supported query resolves to a suitable object; never infer a universal winner.
        var views = Enumerable.Range(1, 7).Select(level => resources.Find(definition.Id, level)).Distinct();
        foreach (var resource in views)
        {
            if (resource is null) return $"未找到活动资源定义 {definition.Id}";
            var data = resource.Data;
            if (data.Rejection is not null) return data.Rejection;
            if (data.Parents.Any(curios.CollidingPropIds.Contains))
                return "继承资源存在原生 hash collision";
            if (data.InstanceType != expectedType)
                return $"无法确认资源在全部难度下属于 {expectedType} 类型";
            if (isRegional)
            {
                if (data.GenerateAmbush.Length > 0 || data.Teleport || data.AncestorTalk)
                    return "资源带有伏击、传送或剧情交互，尚不属于普通地图内容写入范围";
            }
            else if (string.IsNullOrWhiteSpace(data.SpriteId) || data.CurioTypeId is null ||
                !curios.TypeIds.Contains(data.CurioTypeId) || curios.CollidingTypeIds.Contains(data.CurioTypeId))
            {
                return $"奇物道具缺少图像映射或活动互动类型 {data.CurioTypeId}（curio_props.csv / curio_type_library.csv）";
            }
        }
        return null;
    }
}
