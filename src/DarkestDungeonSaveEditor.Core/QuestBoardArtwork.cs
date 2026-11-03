using System.Security.Cryptography;
using System.Text;

namespace DarkestDungeonSaveEditor.Core;

/// <summary>Artwork opened by the saved quest board and its available quest choices.</summary>
public static class QuestBoardArtwork
{
    private const string QuestArt = "campaign/town/quest_select/";

    private static string[] Requests(IEnumerable<QuestBoardQuest> quests)
    {
        var requests = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in new[] { "quest_select.background.png", "quest_select.questverbose_bg.png", "quest_select.icon.png",
                     "dungeon_no_progressionbar.png", "dungeon_progressionbar.png", "quest_select_selected.png",
                     "quest_select_length_generated_1.png" }) requests.Add(QuestArt + file);
        foreach (var file in new[] { "marker_battle.png", "room_boss.png", "marker_curio.png", "room_treasure.png",
                     "marker_trap.png", "marker_obstacle.png", "indicator.png" }) requests.Add("panels/icons_map/" + file);
        foreach (var quest in quests)
        {
            // Include both possible plot icons, even when the dedicated image was
            // previously absent or has since disappeared from a directory provider.
            requests.Add(QuestArt + $"quest_select_{quest.Type}_{quest.Difficulty}.png");
            requests.Add(quest.LengthIconPath);
            if (quest.IsPlotQuest) requests.Add(QuestArt + $"quest_select_{quest.Id}.png");
            foreach (var reward in quest.Rewards)
            {
                requests.Add(reward.IconPath);
                if (reward.Rarity.Length > 0) requests.Add($"panels/icons_equip/trinket/rarity_{reward.Rarity}.png");
            }
        }
        return requests.Where(path => path.Length > 0 && !Path.IsPathRooted(path) &&
            !path.Replace('\\', '/').Split('/').Any(part => part is ".." or ".")).Order(StringComparer.Ordinal).ToArray();
    }

    internal static IReadOnlyDictionary<string, string> Resolve(IReadOnlyList<ActiveContentSource> sources,
        IEnumerable<QuestBoardQuest> quests, List<string> issues) =>
        Resolve(sources, Requests(quests), issues);

    private static IReadOnlyDictionary<string, string> Resolve(IReadOnlyList<ActiveContentSource> sources,
        IEnumerable<string> requests, List<string> issues) =>
        NativeContentFileResolver.ResolveOpenRequests(sources, requests, "Quest board artwork", issues)
            .ToDictionary(pair => pair.Key, pair => pair.Value.Path, StringComparer.Ordinal);

    internal static QuestBoardQuest WithResolvedIcon(QuestBoardQuest quest, IReadOnlyDictionary<string, string> images) =>
        quest with { IconPath = quest.IsPlotQuest && images.ContainsKey(QuestArt + $"quest_select_{quest.Id}.png")
            ? QuestArt + $"quest_select_{quest.Id}.png" : QuestArt + $"quest_select_{quest.Type}_{quest.Difficulty}.png" };

    internal static (QuestBoardSnapshot Board, QuestBoardEditCatalog? Catalog) Refresh(
        IReadOnlyList<ActiveContentSource> sources, QuestBoardSnapshot board, QuestBoardEditCatalog? catalog)
    {
        // Bind both views from ONE resolution. Independent lookups can straddle
        // an image being added/removed and leave one view's chosen icon stale.
        // A cached picker retains its definitions; only its artwork is rebound.
        var issues = new List<string>();
        var images = Resolve(sources, board.Quests.Concat(catalog?.Templates.Select(entry => entry.Quest) ?? []), issues);
        board = board with
        {
            Quests = board.Quests.Select(quest => WithResolvedIcon(quest, images)).ToArray(),
            ImagePaths = images,
            Issues = board.Issues.Concat(issues).Distinct(StringComparer.Ordinal).ToArray()
        };
        if (catalog is not null)
            catalog = catalog with
            {
                Templates = catalog.Templates.Select(entry => entry with { Quest = WithResolvedIcon(entry.Quest, images) }).ToArray(),
                ImagePaths = images,
                Issues = catalog.Issues.Concat(issues).Distinct(StringComparer.Ordinal).ToArray()
            };
        return (board, catalog);
    }

    // Keep the requested-image fingerprint as an input to shared content checks.
    // The artwork-only refresh policy is retained as commented alternatives in
    // ProfileCatalogSnapshotReader and MainWindow.ProfileSync, not enabled here.
    public static string Capture(IReadOnlyList<ActiveContentSource> sources, QuestBoardSnapshot? board,
        QuestBoardEditCatalog? catalog, CancellationToken cancellationToken = default)
    {
        if (board is null) return string.Empty;
        var requests = Requests(board.Quests.Concat(catalog?.Templates.Select(entry => entry.Quest) ?? []));
        return Capture(requests, Resolve(sources, requests, []), cancellationToken);
    }

    internal static string CaptureResolved(QuestBoardSnapshot? board, QuestBoardEditCatalog? catalog,
        CancellationToken cancellationToken = default)
    {
        if (board is null) return string.Empty;
        var images = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in catalog?.ImagePaths ?? new Dictionary<string, string>()) images[pair.Key] = pair.Value;
        foreach (var pair in board.ImagePaths) images[pair.Key] = pair.Value;
        return Capture(Requests(board.Quests.Concat(catalog?.Templates.Select(entry => entry.Quest) ?? [])), images, cancellationToken);
    }

    private static string Capture(IEnumerable<string> requests, IReadOnlyDictionary<string, string> images,
        CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Add(string value) => hash.AppendData(Encoding.UTF8.GetBytes(value + "\n"));
        foreach (var request in requests)
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
