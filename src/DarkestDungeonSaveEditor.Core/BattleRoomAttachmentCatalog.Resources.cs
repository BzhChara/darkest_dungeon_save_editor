using System.Text.Json;
using System.Text.Json.Nodes;

namespace DarkestDungeonSaveEditor.Core;

public static partial class BattleRoomAttachmentCatalog
{
    private static IReadOnlyList<EffectiveContentFile> ResolveEffectiveResourceFiles(
        IReadOnlyList<ActiveContentSource> sources, List<string> issues)
    {
        var enabledDlcPrefixes = ContentFileOverlay.GetEnabledDlcPrefixes(sources);
        return ContentFileOverlay.Resolve(sources.SelectMany(source =>
                EnumeratePropFiles(source, enabledDlcPrefixes, issues, "props", "*.json", ".json")
                    .Where(path => Path.GetFileName(path).ToLowerInvariant() is
                        "prop_definitions.json" or "trap_definitions.json" or "obstacle_definitions.json")
                    .Select(path => new ContentFileCandidate(source, path))),
            "Map prop resource", issues);
    }

    private static Dictionary<string, PropResource> ReadResources(
        IReadOnlyList<EffectiveContentFile> files, List<string> issues)
    {
        var resources = new Dictionary<string, PropResource>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            try
            {
                if (JsonSupport.ReadObject(file.Path)["props"] is not JsonArray props)
                {
                    continue;
                }
                foreach (var prop in props.OfType<JsonObject>())
                {
                    if (prop["name"] is not JsonValue nameNode ||
                        !nameNode.TryGetValue<string>(out var name) || string.IsNullOrWhiteSpace(name))
                    {
                        issues.Add($"地图资源定义缺少名称，已跳过该项：{file.Path}");
                        continue;
                    }
                    var resource = new PropResource(file.Source, prop, false);
                    if (!resources.TryGetValue(name, out var previous) ||
                        ContentFileOverlay.ComparePriority(file.Source, previous.Source) > 0)
                    {
                        resources[name] = resource;
                    }
                    else if (ContentFileOverlay.ComparePriority(file.Source, previous.Source) == 0 &&
                        !JsonNode.DeepEquals(prop, previous.Data))
                    {
                        resources[name] = previous with { IsAmbiguous = true };
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                issues.Add($"地图资源定义读取失败：{file.Path}；{ex.Message}");
            }
        }
        return resources;
    }

    private static string? GetResourceRejection(
        BattleRoomAttachmentDefinition definition, IReadOnlyDictionary<string, PropResource> resources,
        CurioResources curios)
    {
        if (definition.Kind is not (BattleRoomAttachmentKind.Trap or BattleRoomAttachmentKind.Obstacle))
        {
            if (!curios.Props.TryGetValue(definition.Id, out var prop))
            {
                return $"未找到活动奇物道具映射 {definition.Id}（curio_props.csv）";
            }
            if (prop.IsAmbiguous)
            {
                return $"同优先级奇物道具映射存在歧义：{definition.Id}";
            }
            if (string.IsNullOrWhiteSpace(prop.SpriteId) || string.IsNullOrWhiteSpace(prop.TypeId) ||
                !curios.TypeIds.Contains(prop.TypeId))
            {
                return $"奇物道具缺少图像引用或活动互动类型 {prop.TypeId}（curio_type_library.csv）";
            }
            return null;
        }
        if (string.IsNullOrWhiteSpace(definition.OriginDungeonId))
        {
            return "无法从资源池路径确认所属副本区域";
        }
        var expectedType = definition.Kind == BattleRoomAttachmentKind.Trap ? "trap" : "obstacle";
        var current = definition.Id;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (visited.Add(current))
        {
            if (!resources.TryGetValue(current, out var resource))
            {
                return $"未找到活动资源定义 {current}";
            }
            if (resource.IsAmbiguous)
            {
                return $"同优先级资源定义存在歧义：{current}";
            }
            if (HasScriptedBehavior(resource.Data))
            {
                return "资源带有伏击、传送或剧情交互，尚不属于普通地图内容写入范围";
            }
            if (current.Equals(expectedType, StringComparison.Ordinal))
            {
                return null;
            }
            if (resource.Data["default_data"] is not JsonObject data ||
                data["inherits_from"] is not JsonObject inherits ||
                inherits["prop_type_name"] is not JsonValue parentNode ||
                !parentNode.TryGetValue<string>(out var parent) || string.IsNullOrWhiteSpace(parent))
            {
                return $"无法确认资源属于 {expectedType} 类型";
            }
            current = parent;
        }
        return "资源继承存在循环";
    }

    private static bool HasScriptedBehavior(JsonNode? node)
    {
        if (node is JsonArray array)
        {
            return array.Any(HasScriptedBehavior);
        }
        if (node is not JsonObject obj)
        {
            return false;
        }
        return obj.Any(pair =>
            (pair.Key is "generate_ambush" or "teleport" or "ancestor_talk" &&
             pair.Value is JsonValue value &&
             ((value.TryGetValue<bool>(out var flag) && flag) ||
              (value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text)))) ||
            HasScriptedBehavior(pair.Value));
    }

    private sealed record PropResource(ActiveContentSource Source, JsonObject Data, bool IsAmbiguous);
}
