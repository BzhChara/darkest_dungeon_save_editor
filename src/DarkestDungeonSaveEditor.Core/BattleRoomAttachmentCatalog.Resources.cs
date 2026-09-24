using System.Text.Json;

namespace DarkestDungeonSaveEditor.Core;

public static partial class BattleRoomAttachmentCatalog
{
    private static IReadOnlyList<EffectiveContentFile> ResolveEffectiveResourceFiles(
        IReadOnlyList<ActiveContentSource> sources, List<string> issues)
    {
        var enabledDlcPrefixes = ContentFileOverlay.GetEnabledDlcPrefixes(sources);
        var candidates = sources.SelectMany(source =>
                EnumeratePropFiles(source, enabledDlcPrefixes, issues, "props", "*json", "json",
                    path => NativeResourceFileRules.PropResourceStage(path, enabledDlcPrefixes,
                        source.Kind is "local" or "workshop") is var stage &&
                        (stage >= 3 || stage >= 0 && source.Kind == "base"))
                    .Select(path => new ContentFileCandidate(source, path))).ToArray();
        // 0x1404D87A0 opens >props/... before the subdirectory searches.
        // The leading '>' bypasses alternate mounts (0x140248051/0x14024812E),
        // so only the three Base root files supply these initial defaults.
        // Stable ordering preserves native resolver slots within each searched family.
        return candidates.GroupBy(candidate => NativeResourceFileRules.PropResourceStage(
                ContentFileOverlay.NormalizeRelativePath(candidate.Source, candidate.Path)!, enabledDlcPrefixes,
                candidate.Source.Kind is "local" or "workshop"))
            .OrderBy(group => group.Key)
            .SelectMany(group => group.Key < 3
                ? NativeContentFileResolver.ResolveOpenedFiles(sources, [group.Key switch
                {
                    0 => ">props/prop_definitions.json",
                    1 => ">props/obstacle_definitions.json",
                    _ => ">props/trap_definitions.json"
                }], "Map prop resource", issues)
                : NativeContentFileResolver.ResolveAdditiveFiles(group.ToArray(), sources, "Map prop resource", issues)).ToArray();
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
                throw new InvalidDataException(EditorText.Format("BattleRoomAttachmentCatalog_Resources_001", file.Path, error.Message), error);
            }
        }
        return resources;
    }

    private static string? GetResourceRejection(
        BattleRoomAttachmentDefinition definition, PropResources resources, CurioResources curios)
    {
        if (curios.CollidingPropIds.Contains(definition.Id))
            return EditorText.Format("BattleRoomAttachmentCatalog_Resources_002", definition.Id);
        var isRegional = definition.Kind is BattleRoomAttachmentKind.Trap or BattleRoomAttachmentKind.Obstacle;
        if (isRegional && string.IsNullOrWhiteSpace(definition.OriginDungeonId))
            return EditorText.Get("BattleRoomAttachmentCatalog_Resources_003");
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
            if (resource is null) return EditorText.Format("BattleRoomAttachmentCatalog_Resources_004", definition.Id);
            var data = resource.Data;
            if (data.Rejection is not null) return data.Rejection;
            if (data.Parents.Any(curios.CollidingPropIds.Contains))
                return EditorText.Get("BattleRoomAttachmentCatalog_Resources_005");
            if (data.InstanceType != expectedType)
                return EditorText.Format("BattleRoomAttachmentCatalog_Resources_006", expectedType);
            if (isRegional)
            {
                if (data.GenerateAmbush.Length > 0 || data.Teleport || data.AncestorTalk)
                    return EditorText.Get("BattleRoomAttachmentCatalog_Resources_007");
            }
            else if (string.IsNullOrWhiteSpace(data.SpriteId) || data.CurioTypeId is null ||
                !curios.TypeIds.Contains(data.CurioTypeId) || curios.CollidingTypeIds.Contains(data.CurioTypeId))
            {
                return EditorText.Format("BattleRoomAttachmentCatalog_Resources_008", data.CurioTypeId);
            }
        }
        return null;
    }
}
