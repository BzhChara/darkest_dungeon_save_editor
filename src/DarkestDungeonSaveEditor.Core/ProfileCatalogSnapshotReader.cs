using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace DarkestDungeonSaveEditor.Core;

public sealed record ProfileCatalogSnapshot(
    ActiveContentSnapshot Content,
    string ConfigurationKey,
    QuantityItemCatalogResult QuantityItems,
    IReadOnlyDictionary<string, string?> FileHashes,
    DateTime ReadAtUtc);

/// <summary>One read-only, stable disk snapshot shared by the catalog pages.</summary>
public sealed class ProfileCatalogSnapshotReader
{
    public static IReadOnlyList<string> WatchedFileNames { get; } = Array.AsReadOnly(new[]
    {
        "persist.game.json", "persist.estate.json", "persist.roster.json", "persist.town.json",
        "persist.upgrades.json", "persist.raid.json", "persist.map.json"
    });

    private readonly SaveProfile _profile;
    private readonly DsonSaveCodec _codec;
    private readonly string _gameDirectory;
    private readonly string? _workshopDirectory;
    private readonly string? _localModDirectory;
    private readonly string _workspace;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ActiveContentSnapshot _content;
    private string _configurationKey;
    private Dictionary<QuantityItemSaveContext, QuantityItemCatalogResult> _quantityCache;
    private ProfileCatalogSnapshot? _lastSnapshot;
    private int _slot;

    public ProfileCatalogSnapshotReader(
        ActiveContentSnapshot initialContent,
        QuantityItemCatalogResult initialItems,
        DsonSaveCodec codec,
        string gameDirectory,
        string? workshopDirectory,
        string? localModDirectory,
        string? workspaceRoot = null)
    {
        _content = initialContent;
        _profile = initialContent.Profile;
        _codec = codec;
        _gameDirectory = gameDirectory;
        _workshopDirectory = workshopDirectory;
        _localModDirectory = localModDirectory;
        _configurationKey = ProfileContentConfiguration.GetKey(JsonSupport.ReadObject(initialContent.DecodedGamePath));
        _quantityCache = new() { [initialItems.SaveContext] = initialItems };
        _workspace = Path.Combine(workspaceRoot ?? SaveEditorLocations.CreateDefault().WorkspaceDirectory,
            "profile_sync", Guid.NewGuid().ToString("N"));
    }

    public async Task<ProfileCatalogSnapshot> ReadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var before = CaptureHashes(_profile.ProfileDirectory);
            if (_lastSnapshot is not null && HashesEqual(before, _lastSnapshot.FileHashes))
            {
                return _lastSnapshot with { ReadAtUtc = DateTime.UtcNow };
            }

            RequireHash(before, "persist.game.json");
            RequireHash(before, "persist.estate.json");
            RequireHash(before, "persist.roster.json");
            RequireHash(before, "persist.town.json");
            RequireHash(before, "persist.upgrades.json");
            // Reuse two private scratch slots, not a new archive for every movement/save event.
            var nextSlot = 1 - _slot;
            var workspace = Path.Combine(_workspace, nextSlot.ToString());
            var gamePath = await DecodeCopyAsync(workspace, "persist.game.json", before, cancellationToken)
                .ConfigureAwait(false);
            var key = ProfileContentConfiguration.GetKey(JsonSupport.ReadObject(gamePath));
            var contentChanged = key != _configurationKey;
            var content = contentChanged
                ? ActiveContentResolver.ResolveDecoded(_profile, _gameDirectory, _workshopDirectory,
                    _localModDirectory, workspace, gamePath, before["persist.game.json"]!, cancellationToken)
                : _content with
                {
                    WorkspaceDirectory = workspace,
                    DecodedGamePath = gamePath,
                    SourceGameSha256 = before["persist.game.json"]!
                };
            var scene = QuantityItemSaveScene.Read(content).Context;
            if (scene == QuantityItemSaveContext.Raid)
            {
                RequireHash(before, "persist.raid.json");
                RequireHash(before, "persist.map.json");
            }
            var quantityFile = scene == QuantityItemSaveContext.Raid ? "persist.raid.json" : "persist.estate.json";
            // Hero/quirk context and the trinket warehouse have no live rows to rebuild, but
            // changed files must still decode successfully before calling the whole profile synced.
            foreach (var name in new[] { "persist.estate.json", "persist.roster.json", "persist.town.json", "persist.upgrades.json" })
            {
                if (name == quantityFile || (_lastSnapshot is not null && _lastSnapshot.FileHashes[name] == before[name]))
                    continue;
                var decodedContext = await DecodeCopyAsync(workspace, name, before, cancellationToken).ConfigureAwait(false);
                _ = JsonSupport.RequireObject(JsonSupport.ReadObject(decodedContext), "base_root");
            }
            var cache = contentChanged ? new Dictionary<QuantityItemSaveContext, QuantityItemCatalogResult>()
                : new Dictionary<QuantityItemSaveContext, QuantityItemCatalogResult>(_quantityCache);
            QuantityItemCatalogResult quantities;
            if (cache.TryGetValue(scene, out var cached) && cached.SourceSaveSha256 == before[quantityFile])
            {
                quantities = cached;
            }
            else
            {
                var decoded = await DecodeCopyAsync(workspace, quantityFile, before, cancellationToken).ConfigureAwait(false);
                var root = JsonSupport.ReadObject(decoded);
                quantities = cached is not null
                    ? QuantityItemCatalog.RefreshSavedAmounts(content, cached, root, before[quantityFile]!)
                    : scene == QuantityItemSaveContext.Raid
                        ? QuantityItemCatalog.LoadRaid(content, root, before[quantityFile]!)
                        : QuantityItemCatalog.Load(content, root, before[quantityFile]!);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (!HashesEqual(before, CaptureHashes(_profile.ProfileDirectory)))
            {
                throw new IOException("游戏仍在保存，等待完整存档后自动重试。");
            }
            cache[scene] = quantities;
            _quantityCache = cache;
            _content = content;
            _configurationKey = key;
            _slot = nextSlot;
            return _lastSnapshot = new(content, key, quantities, before, DateTime.UtcNow);
        }
        finally
        {
            _gate.Release();
        }
    }

    public static IReadOnlyDictionary<string, string?> CaptureHashes(string profileDirectory)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in WatchedFileNames)
        {
            var path = Path.Combine(profileDirectory, name);
            if (!File.Exists(path))
            {
                result[name] = null;
                continue;
            }
            using var stream = File.OpenRead(path);
            result[name] = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }
        return result;
    }

    public static bool HashesEqual(IReadOnlyDictionary<string, string?> left, IReadOnlyDictionary<string, string?> right) =>
        WatchedFileNames.All(name => string.Equals(left.GetValueOrDefault(name), right.GetValueOrDefault(name),
            StringComparison.OrdinalIgnoreCase));

    private static void RequireHash(IReadOnlyDictionary<string, string?> hashes, string name)
    {
        if (hashes.GetValueOrDefault(name) is null)
        {
            throw new IOException($"等待完整存档：{name} 尚不可用。");
        }
    }

    private async Task<string> DecodeCopyAsync(string workspace, string name,
        IReadOnlyDictionary<string, string?> expected, CancellationToken cancellationToken)
    {
        var sourceDirectory = Path.Combine(workspace, "source");
        var decodedDirectory = Path.Combine(workspace, "decoded");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(decodedDirectory);
        var source = Path.Combine(sourceDirectory, name);
        var decoded = Path.Combine(decodedDirectory, name);
        File.Copy(Path.Combine(_profile.ProfileDirectory, name), source, overwrite: true);
        using (var stream = File.OpenRead(source))
        {
            if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(expected[name], StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("存档复制期间发生变化，稍后自动重试。");
            }
        }
        File.Delete(decoded); // Only this reader's inactive, generated scratch file.
        await _codec.DecodeAsync(source, decoded, cancellationToken).ConfigureAwait(false);
        return decoded;
    }
}
