using System.Text;
using System.Text.Json;

namespace DarkestDungeonSaveEditor.Core;

public static partial class BattleRoomAttachmentCatalog
{
    // Only the metadata needed by map placement is modeled here, not combat effects.
    private sealed record PropData
    {
        public string InstanceType { get; init; } = "";
        public string NameId { get; init; } = "";
        public string GenerateAmbush { get; init; } = "";
        public bool Teleport { get; init; }
        public bool AncestorTalk { get; init; }
        public string SpriteId { get; init; } = "";
        public string? CurioTypeId { get; init; }
        public string? Rejection { get; init; }
        public IReadOnlyList<string> Parents { get; init; } = [];
    }

    private sealed class PropResource(int difficulty, PropData data)
    {
        public int Difficulty { get; } = difficulty;
        public PropData Data { get; set; } = data;
    }

    private sealed class PropResources
    {
        private readonly Dictionary<string, List<PropResource>> entries = new(StringComparer.Ordinal);
        public IEnumerable<string> Ids => entries.Keys;

        public PropResource Add(string id, int difficulty, PropData data)
        {
            if (!entries.TryGetValue(id, out var versions)) entries[id] = versions = [];
            var resource = new PropResource(difficulty, data);
            versions.Add(resource);
            return resource;
        }

        // Native register appends; query returns the first exact difficulty, otherwise
        // the nearest difficulty (an equal-distance tie retains the earlier entry).
        public PropResource? Find(string id, int difficulty = -1)
        {
            if (!entries.TryGetValue(id, out var versions)) return null;
            PropResource? best = null;
            foreach (var resource in versions)
            {
                if (resource.Difficulty == difficulty) return resource;
                if (best is null || Math.Abs(resource.Difficulty - difficulty) < Math.Abs(best.Difficulty - difficulty))
                    best = resource;
            }
            return best;
        }

        public PropResource GetOrCreate(string id) => Find(id) ?? Add(id, -1, new PropData());
    }

    private static void ReadPropFile(EffectiveContentFile file, PropResources resources)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(file.Path), new JsonDocumentOptions
        { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"地图资源 JSON 根节点不是对象：{file.Path}");
        var fileDefault = ApplyPropData(new PropData(), root, "default_data", resources);
        foreach (var prop in NativeJsonReader.Array(root, "props").Where(value => value.ValueKind == JsonValueKind.Object))
        {
            var name = NativeJsonReader.ReadString(prop, "name");
            if (string.IsNullOrWhiteSpace(name) || name.Contains('\0'))
            {
                throw new InvalidDataException($"地图资源名称无效：{file.Path}");
            }
            var id = JsonPropRegistrationId(name, file.Path);
            var data = ApplyPropData(fileDefault, prop, "default_data", resources);
            if (!NativeJsonReader.TryGetProperty(prop, "difficulty_variations", out var variations))
            {
                resources.Add(id, -1, data);
            }
            else if (variations.ValueKind == JsonValueKind.Array)
            {
                // Native creates all seven copies even when the variation array is empty.
                var levels = Enumerable.Repeat(data, 7).ToArray();
                foreach (var variation in variations.EnumerateArray())
                {
                    if (!NativeJsonReader.TryGetProperty(variation, "level", out var levelNode) ||
                        levelNode.ValueKind != JsonValueKind.Number || !levelNode.TryGetInt32(out var level) || level is < 1 or > 7)
                    {
                        throw new InvalidDataException($"地图资源难度变化无效：{file.Path}；{id}");
                    }
                    levels[level - 1] = ApplyPropData(levels[level - 1], variation, resources);
                }
                for (var index = 0; index < levels.Length; index++) resources.Add(id, index + 1, levels[index]);
            }
            else
            {
                throw new InvalidDataException($"地图资源 difficulty_variations 不是数组：{file.Path}；{id}");
            }
            // Diagnose unresolved metadata when a supported pool actually references it.
            // Native system props (for example doors) are outside this placement catalog.
        }
    }

    private static bool IsPropIdentity(string id) => !string.IsNullOrWhiteSpace(id) &&
        !id.Contains('\0') && StrictUtf8.GetByteCount(id) < 256;

    private static string JsonPropRegistrationId(string name, string path)
    {
        // JSON registration uses a 64-byte temporary name before hashing, even
        // though the eventual object has a larger name buffer (0x1404D7CF7).
        try
        {
            var bytes = StrictUtf8.GetBytes(name);
            var id = StrictUtf8.GetString(bytes, 0, Math.Min(63, bytes.Length));
            if (string.IsNullOrWhiteSpace(id)) throw new InvalidDataException($"地图资源截断后的名称为空白：{path}");
            return id;
        }
        catch (Exception error) when (error is EncoderFallbackException or DecoderFallbackException)
        {
            throw new InvalidDataException($"地图资源名称的原生 63 字节边界不是完整 UTF-8：{path}", error);
        }
    }

    private static PropData ApplyPropData(PropData current, JsonElement owner, string key, PropResources resources) =>
        !NativeJsonReader.TryGetProperty(owner, key, out var data) ? current : data.ValueKind == JsonValueKind.Object
            ? ApplyPropData(current, data, resources)
            : current with { Rejection = $"{key} 不是对象" };

    private static PropData ApplyPropData(PropData current, JsonElement data, PropResources resources)
    {
        if (NativeJsonReader.TryGetProperty(data, "inherits_from", out var inherits))
        {
            var parent = NativeJsonReader.ReadString(inherits, "prop_type_name");
            if (!IsPropIdentity(parent))
                return current with { Rejection = "资源继承缺少有效父名称" };
            // Copy the already loaded default query result; never resolve a future parent
            // or recursively merge the JSON. A parent copy replaces earlier defaults.
            var parentResource = resources.Find(parent);
            if (parentResource is null) return current with { Rejection = $"未找到已加载父资源 {parent}" };
            current = parentResource.Data with { Parents = [.. parentResource.Data.Parents, parent] };
        }
        string StringField(string key, string previous)
        {
            if (!NativeJsonReader.TryGetProperty(data, key, out var value)) return previous;
            if (value.ValueKind == JsonValueKind.String && !value.GetString()!.Contains('\0'))
                return value.GetString()!;
            current = current with { Rejection = $"{key} 不是有效字符串" };
            return previous;
        }
        bool BoolField(string key, bool previous)
        {
            if (!NativeJsonReader.TryGetProperty(data, key, out var value)) return previous;
            if (value.ValueKind is JsonValueKind.True or JsonValueKind.False) return value.GetBoolean();
            current = current with { Rejection = $"{key} 不是布尔值" };
            return previous;
        }
        var instanceType = StringField("instance_type", current.InstanceType);
        var nameId = StringField("ui_string", current.NameId);
        var ambush = StringField("generate_ambush", current.GenerateAmbush);
        var teleport = BoolField("teleport", current.Teleport);
        var ancestor = BoolField("ancestor_talk", current.AncestorTalk);
        return current with
        {
            InstanceType = instanceType, NameId = nameId, GenerateAmbush = ambush,
            Teleport = teleport, AncestorTalk = ancestor
        };
    }
}
