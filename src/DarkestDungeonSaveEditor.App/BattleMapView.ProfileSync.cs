using System.IO;
using System.Windows;
using System.Windows.Controls;
using DarkestDungeonSaveEditor.Core;

namespace DarkestDungeonSaveEditor.App;

public partial class BattleMapView : UserControl
{
    public void DismissProfileMenu() => CloseActiveContextMenu();

    private bool _usesSharedProfileMonitor;
    public bool UsesSharedProfileMonitor
    {
        get => _usesSharedProfileMonitor;
        set
        {
            _usesSharedProfileMonitor = value;
            LiveStatusTextBlock.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
            if (value)
            {
                StopProfileMonitoring();
                CancelScheduledRefreshRetry();
            }
        }
    }

    public async Task SynchronizeProfileAsync(ProfileCatalogSnapshot profileSnapshot, bool contentChanged,
        CancellationToken cancellationToken)
    {
        var generation = _profileGeneration;
        if (_profileDirectory is null || _snapshotReader is null ||
            !Path.GetFullPath(_profileDirectory).Equals(
                Path.GetFullPath(profileSnapshot.Content.Profile.ProfileDirectory), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (generation != _profileGeneration) return;
            var content = profileSnapshot.Content;
            _profile = content.Profile;
            if (profileSnapshot.QuantityItems.SaveContext == QuantityItemSaveContext.Town)
            {
                _activeContentSnapshot = content;
                if (contentChanged)
                {
                    _encounterCatalog = null;
                    _roomAttachmentCatalog = null;
                }
                if (_currentSnapshot is not null || _isRaidAvailable)
                    ShowUnavailableState(EditorText.Get("BattleMapView_ProfileSync_001"), EditorText.Get("BattleMapView_LiveRefresh_006"));
                return;
            }

            var snapshot = _currentSnapshot;
            if (snapshot is null || !snapshot.MapSavePath.Equals(content.Profile.MapSavePath, StringComparison.OrdinalIgnoreCase) || !snapshot.MapSha256.Equals(profileSnapshot.FileHashes["persist.map.json"], StringComparison.OrdinalIgnoreCase) ||
                !snapshot.RaidSha256.Equals(profileSnapshot.FileHashes["persist.raid.json"], StringComparison.OrdinalIgnoreCase))
            {
                snapshot = await LoadSnapshotWithRetryAsync(_snapshotReader, _profileDirectory, cancellationToken);
            }
            if (!snapshot.MapSavePath.Equals(content.Profile.MapSavePath, StringComparison.OrdinalIgnoreCase) ||
                !snapshot.MapSha256.Equals(profileSnapshot.FileHashes["persist.map.json"], StringComparison.OrdinalIgnoreCase) ||
                !snapshot.RaidSha256.Equals(profileSnapshot.FileHashes["persist.raid.json"], StringComparison.OrdinalIgnoreCase))
                throw new IOException(EditorText.Get("BattleMapView_ProfileSync_002"));

            var encounters = _encounterCatalog;
            if (contentChanged || encounters is null || encounters.DungeonId != snapshot.DungeonId ||
                encounters.Difficulty != snapshot.Difficulty)
            {
                encounters = await Task.Run(() => BattleEncounterCatalog.Load(content, snapshot), cancellationToken);
            }
            var attachments = _roomAttachmentCatalog;
            if (contentChanged || attachments is null || attachments.Guard.RequestedDungeonId != snapshot.DungeonId)
            {
                try
                {
                    attachments = await Task.Run(() => BattleRoomAttachmentCatalog.Load(content, snapshot.DungeonId), cancellationToken);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception error)
                {
                    // Match initial loading: an unavailable prop catalog must
                    // not keep every other page in the shared sync retry loop.
                    cancellationToken.ThrowIfCancellationRequested();
                    if (generation != _profileGeneration) return;
                    attachments = null;
                    CrashDiagnostics.RecordException("BattleMap: room attachment catalog", error,
                        EditorText.Format("BattleMapView_ProfileLifecycle_014", content.Profile.ProfileId, snapshot.DungeonId, snapshot.Difficulty));
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (generation != _profileGeneration) return;

            // Content configuration is unchanged on ordinary saves. Keep definition fingerprints,
            // while rebinding every candidate to the newly verified game-save hash.
            BattleEncounterDefinition Rebind(BattleEncounterDefinition item) => item with
            {
                TableGuard = item.TableGuard with { GameSaveSha256 = content.SourceGameSha256 }
            };
            _encounterCatalog = encounters with
            {
                TableGuard = encounters.TableGuard with { GameSaveSha256 = content.SourceGameSha256 },
                Encounters = encounters.Encounters.Select(Rebind).ToArray(),
                BridgeEncounters = encounters.BridgeEncounters.Select(Rebind).ToArray()
            };
            _roomAttachmentCatalog = null;
            if (attachments is not null)
            {
                var guard = attachments.Guard with { GameSaveSha256 = content.SourceGameSha256 };
                _roomAttachmentCatalog = attachments with
                {
                    Guard = guard,
                    Definitions = attachments.Definitions.Select(item => item with { CatalogGuard = guard }).ToArray()
                };
            }
            _activeContentSnapshot = content;
            if (!ReferenceEquals(snapshot, _currentSnapshot))
                RenderSnapshot(snapshot, fitToView: _currentSnapshot is null || _fitToView);
        }
        finally
        {
            _refreshGate.Release();
        }
    }
}
