using System.Security.Cryptography;
using System.Text;

namespace DarkestDungeonSaveEditor.Core;

/// <summary>Resolves map artwork through the same enabled-source and manifest rules as game resources.</summary>
public static class BattleMapArtwork
{
    private static readonly string[] Requests = new[]
    {
        "room_empty.png", "room_entrance.png", "room_battle.png", "room_boss.png", "room_curio.png",
        "room_treasure.png", "hall_clear.png", "hall_dim.png", "indicator.png", "marker_room_visited.png",
        "marker_battle.png", "marker_curio.png", "marker_trap.png", "marker_obstacle.png", "marker_hunger.png",
        "marker_secret.png"
    }.Select(name => "panels/icons_map/" + name).Append("panels/panel_map.png").Order(StringComparer.Ordinal).ToArray();

    public static IReadOnlyDictionary<string, string> Resolve(ActiveContentSnapshot content, List<string> issues) =>
        Resolve(content.Sources, issues);

    internal static IReadOnlyDictionary<string, string> Resolve(IReadOnlyList<ActiveContentSource> sources, List<string> issues)
    {
        return NativeContentFileResolver.ResolveOpenRequests(sources, Requests, "Battle map artwork", issues)
            .ToDictionary(pair => pair.Key, pair => pair.Value.Path, StringComparer.Ordinal);
    }

    // Artwork is an input to the shared content-change policy. Include missing
    // requests and providers. The disabled artwork-only refresh experiment is
    // retained in ProfileCatalogSnapshotReader and MainWindow.ProfileSync.
    public static string Capture(IReadOnlyList<ActiveContentSource> sources, CancellationToken cancellationToken = default) =>
        CaptureResolved(Resolve(sources, []), cancellationToken);

    internal static string CaptureResolved(IReadOnlyDictionary<string, string> images, CancellationToken cancellationToken = default)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Add(string value) => hash.AppendData(Encoding.UTF8.GetBytes(value + "\n"));
        foreach (var request in Requests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Add(request);
            if (!images.TryGetValue(request, out var path)) { Add("unresolved"); continue; }
            Add(Path.GetFullPath(path));
            if (!File.Exists(path)) { Add("missing"); continue; }
            using var stream = File.OpenRead(path);
            Add(Convert.ToHexString(SHA256.HashData(stream)));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
