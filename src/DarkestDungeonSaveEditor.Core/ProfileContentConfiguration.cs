using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace DarkestDungeonSaveEditor.Core;

public static class ProfileContentConfiguration
{
    public static string GetKey(string decodedGamePath) => GetKey(JsonSupport.ReadObject(decodedGamePath));

    // Progress, scene, timestamp and other game-save fields are NOT content configuration.
    // Object property order is immaterial, but the numbered Mod entries retain their order.
    public static string GetKey(JsonObject gameRoot)
    {
        var root = JsonSupport.RequireObject(gameRoot, "base_root");
        var mode = JsonSupport.ReadString(root, "game_mode").Trim();
        var configuration = new JsonObject
        {
            ["game_mode"] = string.IsNullOrWhiteSpace(mode) ? "base" : mode,
            ["dlc"] = Canonicalize(root["dlc"]),
            ["applied_ugcs_1_0"] = Canonicalize(root["applied_ugcs_1_0"])
        };
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(configuration.ToJsonString())));
    }

    private static JsonNode? Canonicalize(JsonNode? node) => node switch
    {
        JsonObject value => new JsonObject(value.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => KeyValuePair.Create(pair.Key, Canonicalize(pair.Value)))),
        JsonArray value => new JsonArray(value.Select(Canonicalize).ToArray()),
        _ => node?.DeepClone()
    };
}
