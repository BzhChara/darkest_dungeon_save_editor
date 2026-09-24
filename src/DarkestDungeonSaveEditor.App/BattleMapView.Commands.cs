using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DarkestDungeonSaveEditor.Core;

namespace DarkestDungeonSaveEditor.App;

public partial class BattleMapView : UserControl
{
    private ContextMenu BuildContextMenu(PrototypeMapCell cell)
    {
        CloseActiveContextMenu();
        var isProtectedContent = IsHungerContent(cell) ||
                                 cell.Content == PrototypeContent.Entrance ||
                                 cell.Content == PrototypeContent.SecretDoor ||
                                 IsUnsupportedScriptContent(cell);
        var hasPersistedContent = cell.SourceAreaId is null
            ? cell.Content != PrototypeContent.None
            : cell.RawContent != 0;
        var hasDeletableContent = hasPersistedContent || cell.HasResidualContentBinding;
        var contextMenu = new ContextMenu
        {
            Style = (Style)FindResource("BattleMapContextMenuStyle"),
            PlacementTarget = cell.Visual
        };
        if (!isProtectedContent)
        {
            var changeRoot = CreateMenuItem(
                hasPersistedContent ? EditorText.Get("BattleMapView_Commands_001") : EditorText.Get("BattleMapView_Commands_002"),
                hasPersistedContent ? BattleMenuIcon.Replace : BattleMenuIcon.Add);
            PopulateContentChoices(changeRoot, cell);
            contextMenu.Items.Add(changeRoot);

            if (CanEditBattleAttachment(cell))
            {
                var attachmentRoot = CreateMenuItem(EditorText.Get("BattleMapView_Commands_003"), BattleMenuIcon.Attachment);
                PopulateBattleAttachmentChoices(attachmentRoot, cell);
                contextMenu.Items.Add(attachmentRoot);
            }

            if (hasDeletableContent)
            {
                contextMenu.Items.Add(CreateAsyncActionMenuItem(
                    EditorText.Get("BattleMapView_Commands_004"),
                    BattleMenuIcon.Remove,
                    () => DeleteContentAsync(cell)));
            }

            contextMenu.Items.Add(new Separator
            {
                Style = (Style)FindResource("BattleMapSeparatorStyle")
            });
        }
        var moveItem = CreateAsyncActionMenuItem(
            EditorText.Get("BattleMapView_Commands_005"),
            BattleMenuIcon.MoveParty,
            () => MovePartyAsync(cell));
        moveItem.IsEnabled = !cell.HasParty && !_isApplyingEdit;
        contextMenu.Items.Add(moveItem);
        contextMenu.Opened += (_, _) =>
        {
            if (!_cells.Contains(cell))
            {
                contextMenu.IsOpen = false;
                return;
            }
            _activeContextMenu = contextMenu;
        };
        contextMenu.Closed += (_, _) =>
        {
            if (ReferenceEquals(_activeContextMenu, contextMenu))
            {
                _activeContextMenu = null;
            }
        };
        _activeContextMenu = contextMenu;
        return contextMenu;
    }

    private static bool IsHungerContent(PrototypeMapCell cell) =>
        cell.Content == PrototypeContent.Hunger ||
        (cell.SourceAreaId is not null &&
         cell.RawContent == (int)BattleMapTileContent.Hunger);

    private static bool IsUnsupportedScriptContent(PrototypeMapCell cell) =>
        cell.SourceAreaId is not null &&
        (cell.RawContent is
                (int)BattleMapTileContent.Ambush or
                (int)BattleMapTileContent.Happening or
                (int)BattleMapTileContent.AmbushCurio or
                (int)BattleMapTileContent.AmbushTreasure ||
         cell.RawContent < (int)BattleMapTileContent.Nothing ||
         cell.RawContent > (int)BattleMapTileContent.SecretDoor);

    private void CloseActiveContextMenu()
    {
        var contextMenu = _activeContextMenu;
        _activeContextMenu = null;
        if (contextMenu?.IsOpen == true)
        {
            contextMenu.IsOpen = false;
        }
    }

    private void PopulateContentChoices(MenuItem root, PrototypeMapCell cell)
    {
        var battleMenu = CreateMenuItem(EditorText.Get("BattleMapView_CellVisuals_013"), BattleMenuIcon.Battle);
        var isFinalRoom = _currentSnapshot is not null &&
                          string.Equals(
                              cell.SourceAreaId,
                              _currentSnapshot.FinalRoomId,
                              StringComparison.Ordinal);
        if (!isFinalRoom)
        {
            battleMenu.Items.Add(CreateEncounterPickerItem(
                cell,
                EditorText.Get("BattleEncounterSelectionDialog_014"),
                BattleMenuIcon.Battle,
                BattleEncounterClassification.Ordinary));
            battleMenu.Items.Add(CreateEncounterPickerItem(
                cell,
                EditorText.Get("BattleMapView_Commands_006"),
                BattleMenuIcon.Boss,
                BattleEncounterClassification.RoamingBoss,
                BattleEncounterClassification.RoamingEncounter,
                BattleEncounterClassification.ConditionalOrAdditional));
        }

        if (cell.Kind == PrototypeCellKind.Room)
        {
            battleMenu.Items.Add(CreateEncounterPickerItem(
                cell,
                EditorText.Get("BattleEncounterSelectionDialog_005"),
                BattleMenuIcon.Boss,
                BattleEncounterClassification.FixedBoss));
        }
        root.Items.Add(battleMenu);

        if (isFinalRoom)
        {
            return;
        }

        var propCatalog = GetCurrentRoomAttachmentCatalog();
        root.Items.Insert(0, CreateContentPickerItem(cell, EditorText.Get("BattleMapView_CellVisuals_012"), BattleMenuIcon.Curio,
            cell.Kind == PrototypeCellKind.Room ? propCatalog?.Curios ?? [] : propCatalog?.HallCurios ?? []));
        if (cell.Kind == PrototypeCellKind.Room)
        {
            root.Items.Add(CreateContentPickerItem(cell, EditorText.Get("BattleMapView_Commands_007"), BattleMenuIcon.Treasure, propCatalog?.Treasures ?? []));
        }
        else
        {
            root.Items.Add(CreateRegionalContentItem(cell, EditorText.Get("BattleMapView_CellVisuals_014"), BattleMenuIcon.Trap,
                BattleRoomAttachmentKind.Trap));
            root.Items.Add(CreateRegionalContentItem(cell, EditorText.Get("BattleMapView_CellVisuals_017"), BattleMenuIcon.Obstacle,
                BattleRoomAttachmentKind.Obstacle));
        }
    }

    private bool CanEditBattleAttachment(PrototypeMapCell cell) =>
        _currentSnapshot is not null &&
        cell.Kind == PrototypeCellKind.Room &&
        cell.SourceAreaId is not null &&
        !string.Equals(
            cell.SourceAreaId,
            _currentSnapshot.EntranceAreaId,
            StringComparison.Ordinal) &&
        !string.Equals(
            cell.SourceAreaId,
            _currentSnapshot.FinalRoomId,
            StringComparison.Ordinal) &&
        cell.RawContent is
            (int)BattleMapTileContent.Battle or
            (int)BattleMapTileContent.GuardedCurio or
            (int)BattleMapTileContent.GuardedTreasure &&
        cell.MashType == 1 &&
        cell.MashIndex >= 0;

    private void PopulateBattleAttachmentChoices(MenuItem root, PrototypeMapCell cell)
    {
        var catalog = GetCurrentRoomAttachmentCatalog();
        var curios = catalog?.Curios ?? [];
        var treasures = catalog?.Treasures ?? [];
        root.Items.Add(CreateBattleAttachmentPickerItem(cell, EditorText.Get("BattleMapView_CellVisuals_012"), BattleMenuIcon.Curio, curios));
        root.Items.Add(CreateBattleAttachmentPickerItem(cell, EditorText.Get("BattleMapView_Commands_007"), BattleMenuIcon.Treasure, treasures));
        if (cell.RawContent is
            (int)BattleMapTileContent.GuardedCurio or
            (int)BattleMapTileContent.GuardedTreasure)
        {
            root.Items.Add(new Separator
            {
                Style = (Style)FindResource("BattleMapSeparatorStyle")
            });
            root.Items.Add(CreateAsyncActionMenuItem(
                EditorText.Get("BattleMapView_Commands_008"),
                BattleMenuIcon.Remove,
                () => RemoveBattleAttachmentAsync(cell)));
        }
    }

    private MenuItem CreateBattleAttachmentPickerItem(
        PrototypeMapCell cell,
        string label,
        BattleMenuIcon icon,
        IReadOnlyCollection<BattleRoomAttachmentDefinition> candidates)
    {
        if (candidates.Count == 0)
        {
            var unavailable = CreateMenuItem($"{label}（0）", icon);
            unavailable.IsEnabled = false;
            return unavailable;
        }

        return CreateAsyncActionMenuItem(
            $"{label}（{candidates.Count}）",
            icon,
            () => SelectAndSetBattleAttachmentAsync(cell, candidates, label));
    }

    private BattleRoomAttachmentCatalogResult? GetCurrentRoomAttachmentCatalog()
    {
        if (_profile is null ||
            _activeContentSnapshot is null ||
            _roomAttachmentCatalog is null ||
            !_roomAttachmentCatalog.Guard.GameSaveSha256.Equals(
                _activeContentSnapshot.SourceGameSha256,
                StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFullPath(_roomAttachmentCatalog.Guard.ProfileDirectory).Equals(
                Path.GetFullPath(_profile.ProfileDirectory),
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return _roomAttachmentCatalog;
    }

    private async Task SelectAndSetBattleAttachmentAsync(
        PrototypeMapCell target,
        IReadOnlyCollection<BattleRoomAttachmentDefinition> candidates,
        string kindLabel)
    {
        if (GetCurrentRoomAttachmentCatalog() is null || Window.GetWindow(this) is not { } owner)
        {
            MapSelectionTextBlock.Text = EditorText.Get("BattleMapView_Commands_009");
            return;
        }

        CloseActiveContextMenu();
        var dialog = new BattleRoomAttachmentSelectionDialog(candidates, kindLabel)
        {
            Owner = owner
        };
        if (dialog.ShowDialog() == true && dialog.SelectedAttachment is { } attachment)
        {
            await SetBattleAttachmentAsync(target, attachment);
        }
    }

    private MenuItem CreateEncounterPickerItem(
        PrototypeMapCell cell,
        string label,
        BattleMenuIcon icon,
        params BattleEncounterClassification[] classifications)
    {
        var candidates = GetEncounterPickerCandidates(cell, classifications);
        if (candidates.Count == 0)
        {
            var unavailable = CreateMenuItem($"{label}（0）", icon);
            unavailable.IsEnabled = false;
            return unavailable;
        }

        var targetLabel = classifications.Length == 1 &&
                          classifications[0] == BattleEncounterClassification.FixedBoss
            ? EditorText.Get("BattleEncounterSelectionDialog_016")
            : cell.Kind == PrototypeCellKind.Room ? EditorText.Get("BattleEncounterSelectionDialog_016") : EditorText.Get("BattleEncounterSelectionDialog_015");
        return CreateAsyncActionMenuItem(
            $"{label}（{candidates.Count}）",
            icon,
            () => SelectAndPlaceEncounterAsync(cell, candidates, EditorText.Format("BattleMapView_Commands_010", targetLabel, label)));
    }

    private IReadOnlyList<BattleEncounterDefinition> GetEncounterPickerCandidates(
        PrototypeMapCell cell,
        IReadOnlyCollection<BattleEncounterClassification> classifications)
    {
        var catalog = GetCurrentEncounterCatalog();
        if (catalog is null)
        {
            return [];
        }

        var fixedBossOnly = classifications.Count == 1 &&
                            classifications.Contains(BattleEncounterClassification.FixedBoss);
        var mashType = fixedBossOnly
            ? 2
            : GetOrdinaryMashType(cell);
        return BattleEncounterCatalog.GetSelectionCandidates(catalog, mashType, classifications);
    }

    private static BattleEncounterDefinition? FindDirectlyAddressableEncounter(
        BattleEncounterCatalogResult catalog,
        BattleEncounterDefinition candidate) =>
        catalog.DirectEncounters.FirstOrDefault(encounter =>
            encounter.MashType == candidate.MashType &&
            encounter.MonsterIds.SequenceEqual(candidate.MonsterIds, StringComparer.Ordinal));

    private BattleEncounterCatalogResult? GetCurrentEncounterCatalog()
    {
        if (_currentSnapshot is null ||
            _encounterCatalog is null ||
            !_encounterCatalog.DungeonId.Equals(
                _currentSnapshot.DungeonId,
                StringComparison.Ordinal) ||
            _encounterCatalog.Difficulty != _currentSnapshot.Difficulty)
        {
            return null;
        }

        return _encounterCatalog;
    }

    private static int GetOrdinaryMashType(PrototypeMapCell cell) =>
        cell.Kind == PrototypeCellKind.Room ? 1 : 0;

    private async Task SelectAndPlaceEncounterAsync(
        PrototypeMapCell target,
        IReadOnlyCollection<BattleEncounterDefinition> candidates,
        string title)
    {
        var catalog = GetCurrentEncounterCatalog();
        if (catalog is null || Window.GetWindow(this) is not { } owner)
        {
            MapSelectionTextBlock.Text = EditorText.Get("BattleMapView_Commands_011");
            return;
        }

        CloseActiveContextMenu();
        var dialog = new BattleEncounterSelectionDialog(
            candidates,
            catalog.Difficulty,
            title)
        {
            Owner = owner
        };
        if (dialog.ShowDialog() == true && dialog.SelectedEncounter is { } encounter)
        {
            var directEncounter = encounter.CanPlaceDirectly && encounter.MashIndex is not null
                ? encounter
                : FindDirectlyAddressableEncounter(catalog, encounter);
            if (directEncounter is not null)
            {
                await PlaceBattleAsync(target, directEncounter);
            }
            else
            {
                await PlaceManagedEncounterAsync(target, encounter);
            }
        }
    }

    private async Task PlaceManagedEncounterAsync(
        PrototypeMapCell target,
        BattleEncounterDefinition encounter)
    {
        var catalog = GetCurrentEncounterCatalog();
        if (catalog is null ||
            !TryGetWritableContext(target, out var owner, out var profile, out var snapshot, out var service) ||
            _activeContentSnapshot is null ||
            _managedEncounterBridgeService is null ||
            string.IsNullOrWhiteSpace(_gameDirectory))
        {
            MapSelectionTextBlock.Text = EditorText.Get("BattleMapView_Commands_012");
            return;
        }

        var replacementText = target.RawContent == 0
            ? EditorText.Get("BattleMapView_Commands_002")
            : EditorText.Format("BattleMapView_Commands_014", target.ContentLabel ?? EditorText.Get("BattleMapView_Commands_013"));
        var difficultyWarning = encounter.OriginDifficulty == catalog.Difficulty
            ? string.Empty
            : Environment.NewLine +
              EditorText.Format("BattleMapView_Commands_015", encounter.OriginDifficulty, catalog.Difficulty);
        var classificationLabel = FormatEncounterClassification(encounter);
        if (!ThemedDialog.Confirm(
                owner,
                EditorText.Format("BattleMapView_Commands_016", target.DisplayName, replacementText, encounter.DisplayName) +
                Environment.NewLine +
                EditorText.Format("BattleMapView_Commands_017", encounter.SourceLabel, encounter.OriginDungeonId) +
                difficultyWarning + Environment.NewLine +
                EditorText.Get("BattleMapView_Commands_018") +
                Environment.NewLine +
                EditorText.Get("BattleMapView_Commands_019"),
                EditorText.Format("BattleMapView_Commands_020", classificationLabel)))
        {
            return;
        }

        var activeContent = _activeContentSnapshot;
        var managedService = _managedEncounterBridgeService;
        await ApplySaveEditAsync(
            target,
            profile,
            snapshot,
            service,
            BattleMapEditKind.PlaceBattle,
            encounterResolver: async () =>
            {
                MapSelectionTextBlock.Text =
                    EditorText.Format("BattleMapView_Commands_021", encounter.DisplayName);
                var result = await managedService.EnsureEncounterAsync(
                    profile,
                    snapshot,
                    activeContent,
                    catalog,
                    encounter,
                    _gameDirectory,
                    _workshopDirectory,
                    _localModDirectory,
                    placementTarget: new BattleMapPlacementTarget(target.SourceAreaId!, target.SourceTileId!));
                _activeContentSnapshot = result.ActiveContent;
                _encounterCatalog = result.Catalog;
                await ReloadRoomAttachmentCatalogAsync(result.ActiveContent);
                ActiveContentChanged?.Invoke(result.ActiveContent);
                CrashDiagnostics.RecordStatus(
                    EditorText.Format("BattleMapView_Commands_022", result.PackageDirectory) +
                    EditorText.Format("BattleMapView_Commands_023", catalog.DungeonId, catalog.Difficulty) +
                    EditorText.Format("BattleMapView_Commands_024", encounter.OriginDungeonId, encounter.OriginDifficulty) +
                    EditorText.Format("BattleMapView_Commands_025", encounter.Classification, encounter.SourceKind) +
                    $"mash_type={result.MashType}；" +
                    EditorText.Format("BattleMapView_Commands_026", result.MashIndex, result.EncounterWasAdded) +
                    EditorText.Format("BattleMapView_Commands_027", result.ProfileConfigurationChanged) +
                    EditorText.Format("BattleMapView_Commands_028", string.Join(',', result.MonsterIds)) +
                    EditorText.Format("BattleMapView_Commands_030", result.ProfileBackupDirectory ?? EditorText.Get("BattleMapView_Commands_029")));
                return result.DirectEncounter;
            });
    }

    private static string FormatEncounterClassification(BattleEncounterDefinition encounter) =>
        encounter.Classification switch
        {
            BattleEncounterClassification.FixedBoss => EditorText.Get("BattleEncounterSelectionDialog_005"),
            BattleEncounterClassification.RoamingBoss => EditorText.Get("BattleEncounterSelectionDialog_006"),
            BattleEncounterClassification.RoamingEncounter => EditorText.Get("BattleEncounterSelectionDialog_007"),
            BattleEncounterClassification.ConditionalOrAdditional when
                encounter.ContainsBossMonster &&
                encounter.SourceKind == BattleEncounterSourceKind.Conditional => EditorText.Get("BattleEncounterSelectionDialog_008"),
            BattleEncounterClassification.ConditionalOrAdditional when
                encounter.ContainsBossMonster &&
                encounter.SourceKind == BattleEncounterSourceKind.Additional => EditorText.Get("BattleEncounterSelectionDialog_009"),
            BattleEncounterClassification.ConditionalOrAdditional when
                encounter.ContainsBossMonster => EditorText.Get("BattleEncounterSelectionDialog_010"),
            BattleEncounterClassification.ConditionalOrAdditional => EditorText.Get("BattleMapView_Commands_031"),
            _ => EditorText.Get("BattleMapView_Commands_032")
        };

    private async Task ReloadRoomAttachmentCatalogAsync(ActiveContentSnapshot activeContent)
    {
        var dungeonId = _currentSnapshot?.DungeonId;
        try
        {
            _roomAttachmentCatalog = await Task.Run(
                () => BattleRoomAttachmentCatalog.Load(activeContent, dungeonId));
        }
        catch (Exception ex)
        {
            _roomAttachmentCatalog = null;
            CrashDiagnostics.RecordException(
                "BattleMap: reload room attachment catalog",
                ex,
                EditorText.Format("BattleMapView_Commands_033", activeContent.Profile.ProfileId));
        }
    }

    private MenuItem CreateMenuItem(string header, BattleMenuIcon icon)
    {
        return new MenuItem
        {
            Header = header,
            Icon = CreateMenuIcon(icon),
            Style = (Style)FindResource("BattleMapMenuItemStyle")
        };
    }

    private MenuItem CreateAsyncActionMenuItem(string header, BattleMenuIcon icon, Func<Task> action)
    {
        var menuItem = CreateMenuItem(header, icon);
        menuItem.Click += async (_, _) => await action();
        return menuItem;
    }

    private async Task DeleteContentAsync(PrototypeMapCell target)
    {
        if (_currentSnapshot is null)
        {
            target.Content = PrototypeContent.None;
            target.ContentLabel = null;
            RefreshCellVisual(target);
            MapSelectionTextBlock.Text =
                EditorText.Format("BattleMapView_Commands_034", target.DisplayName);
            return;
        }
        if (!TryGetWritableContext(target, out var owner, out var profile, out var snapshot, out var service))
        {
            MapSelectionTextBlock.Text = EditorText.Get("BattleMapView_Commands_035");
            return;
        }

        var specialWarning = string.Equals(
            target.SourceAreaId,
            snapshot.FinalRoomId,
            StringComparison.Ordinal)
            ? Environment.NewLine + EditorText.Get("BattleMapView_Commands_036")
            : string.Empty;
        var contentDescription = target.RawContent == 0
            ? EditorText.Get("BattleMapView_Commands_037")
            : $"“{target.ContentLabel ?? EditorText.Get("BattleMapView_Commands_013")}”";
        if (!ThemedDialog.Confirm(
                owner,
                EditorText.Format("BattleMapView_Commands_038", target.DisplayName, contentDescription) +
                specialWarning + Environment.NewLine +
                EditorText.Get("BattleMapView_Commands_039") +
                Environment.NewLine + EditorText.Get("BattleMapView_Commands_040"),
                EditorText.Get("BattleMapView_Commands_041")))
        {
            return;
        }

        await ApplySaveEditAsync(target, profile, snapshot, service, BattleMapEditKind.DeleteContent);
    }

    private async Task MovePartyAsync(PrototypeMapCell target)
    {
        if (_currentSnapshot is null)
        {
            MovePartyPreview(target);
            return;
        }
        if (!TryGetWritableContext(target, out var owner, out var profile, out var snapshot, out var service))
        {
            MapSelectionTextBlock.Text = EditorText.Get("BattleMapView_Commands_035");
            return;
        }

        var contentWarning = target.RawContent == 0 || IsHungerContent(target)
            ? string.Empty
            : Environment.NewLine +
              EditorText.Format("BattleMapView_Commands_043", target.ContentLabel ?? EditorText.Get("BattleMapView_Commands_042")) +
              EditorText.Get("BattleMapView_Commands_044");
        if (!ThemedDialog.Confirm(
                owner,
                EditorText.Format("BattleMapView_Commands_045", target.DisplayName) +
                contentWarning + Environment.NewLine +
                EditorText.Get("BattleMapView_Commands_040"),
                EditorText.Get("BattleMapView_Commands_046")))
        {
            return;
        }

        await ApplySaveEditAsync(target, profile, snapshot, service, BattleMapEditKind.MoveParty);
    }

    private async Task PlaceBattleAsync(
        PrototypeMapCell target,
        BattleEncounterDefinition encounter)
    {
        if (!TryGetWritableContext(target, out var owner, out var profile, out var snapshot, out var service))
        {
            MapSelectionTextBlock.Text = EditorText.Get("BattleMapView_Commands_035");
            return;
        }
        if (!encounter.CanPlaceDirectly || encounter.MashIndex is null)
        {
            MapSelectionTextBlock.Text = EditorText.Get("BattleMapView_Commands_047");
            return;
        }

        var replacementText = target.RawContent == 0
            ? EditorText.Get("BattleMapView_Commands_002")
            : EditorText.Format("BattleMapView_Commands_014", target.ContentLabel ?? EditorText.Get("BattleMapView_Commands_013"));
        var naturalQuotaWarning = encounter.Weight is <= 0
            ? Environment.NewLine +
              EditorText.Get("BattleMapView_Commands_048")
            : string.Empty;
        if (!ThemedDialog.Confirm(
                owner,
                EditorText.Format("BattleMapView_Commands_016", target.DisplayName, replacementText, encounter.DisplayName) +
                Environment.NewLine + EditorText.Format("BattleMapView_Commands_049", encounter.SourceLabel) +
                Environment.NewLine +
                EditorText.Format("BattleMapView_Commands_050", encounter.MashType, encounter.MashIndex) +
                naturalQuotaWarning + Environment.NewLine +
                EditorText.Get("BattleMapView_Commands_019"),
                EditorText.Get("BattleMapView_Commands_051")))
        {
            return;
        }

        await ApplySaveEditAsync(
            target,
            profile,
            snapshot,
            service,
            BattleMapEditKind.PlaceBattle,
            encounter);
    }

    private async Task SetBattleAttachmentAsync(
        PrototypeMapCell target,
        BattleRoomAttachmentDefinition attachment)
    {
        if (!TryGetWritableContext(target, out var owner, out var profile, out var snapshot, out var service))
        {
            MapSelectionTextBlock.Text = EditorText.Get("BattleMapView_Commands_035");
            return;
        }

        var kindLabel = attachment.Kind == BattleRoomAttachmentKind.Curio ? EditorText.Get("BattleMapView_CellVisuals_012") : EditorText.Get("BattleMapView_Commands_007");
        var actionLabel = target.RawContent is
            (int)BattleMapTileContent.GuardedCurio or
            (int)BattleMapTileContent.GuardedTreasure
            ? EditorText.Get("BattleMapView_Commands_052")
            : EditorText.Get("BattleMapView_Commands_053");
        if (!ThemedDialog.Confirm(
                owner,
                EditorText.Format("BattleMapView_Commands_054", target.DisplayName, actionLabel,
                    EditorText.ContentName(attachment.LocalizedName, attachment.Id), attachment.Id) +
                Environment.NewLine + $"ID：{attachment.Id}" +
                Environment.NewLine + EditorText.Format("BattleMapView_Commands_049", attachment.SourceLabel) +
                Environment.NewLine +
                EditorText.Get("BattleMapView_Commands_055"),
                EditorText.Format("BattleMapView_Commands_056", kindLabel)))
        {
            return;
        }

        await ApplySaveEditAsync(
            target,
            profile,
            snapshot,
            service,
            BattleMapEditKind.SetBattleAttachment,
            attachment: attachment);
    }

    private async Task RemoveBattleAttachmentAsync(PrototypeMapCell target)
    {
        if (!TryGetWritableContext(target, out var owner, out var profile, out var snapshot, out var service))
        {
            MapSelectionTextBlock.Text = EditorText.Get("BattleMapView_Commands_035");
            return;
        }

        if (!ThemedDialog.Confirm(
                owner,
                EditorText.Format("BattleMapView_Commands_058", target.DisplayName, target.ContentLabel ?? EditorText.Get("BattleMapView_Commands_057")) +
                Environment.NewLine + EditorText.Get("BattleMapView_Commands_040"),
                EditorText.Get("BattleMapView_Commands_059")))
        {
            return;
        }

        await ApplySaveEditAsync(
            target,
            profile,
            snapshot,
            service,
            BattleMapEditKind.RemoveBattleAttachment);
    }

    private bool TryGetWritableContext(
        PrototypeMapCell target,
        out Window owner,
        out SaveProfile profile,
        out BattleMapSnapshot snapshot,
        out BattleMapEditService service)
    {
        owner = Window.GetWindow(this)!;
        profile = _profile!;
        snapshot = _currentSnapshot!;
        service = _editService!;
        return owner is not null &&
               profile is not null &&
               snapshot is not null &&
               service is not null &&
               target.SourceAreaId is not null &&
               target.SourceTileId is not null &&
               _cells.Contains(target) &&
               !_isApplyingEdit;
    }

    private async Task ApplySaveEditAsync(
        PrototypeMapCell target,
        SaveProfile profile,
        BattleMapSnapshot snapshot,
        BattleMapEditService service,
        BattleMapEditKind kind,
        BattleEncounterDefinition? encounter = null,
        BattleRoomAttachmentDefinition? attachment = null,
        Func<Task<BattleEncounterDefinition>>? encounterResolver = null)
    {
        var generation = _profileGeneration;
        var gateTaken = false;
        var committed = false;
        var managedEncounterResolved = false;
        var refreshedSuccessfully = false;
        PreparedBattleMapEdit? prepared = null;
        string? backupDirectory = null;
        _isApplyingEdit = true;
        SaveEditBusyChanged?.Invoke(true);
        CloseActiveContextMenu();
        CancelScheduledRefreshRetry();
        StopProfileMonitoring();
        MapCanvas.IsHitTestVisible = false;
        MapSelectionTextBlock.Text = kind switch
        {
            BattleMapEditKind.DeleteContent => EditorText.Format("BattleMapView_Commands_060", target.DisplayName),
            BattleMapEditKind.MoveParty => EditorText.Format("BattleMapView_Commands_061", target.DisplayName),
            BattleMapEditKind.PlaceBattle => EditorText.Format("BattleMapView_Commands_062", target.DisplayName),
            BattleMapEditKind.PlaceContent => EditorText.Format("BattleMapView_Commands_063", target.DisplayName, attachment?.KindLabel),
            BattleMapEditKind.SetBattleAttachment => EditorText.Format("BattleMapView_Commands_064", target.DisplayName),
            BattleMapEditKind.RemoveBattleAttachment => EditorText.Format("BattleMapView_Commands_065", target.DisplayName),
            _ => EditorText.Get("BattleMapView_Commands_066")
        };

        try
        {
            await _refreshGate.WaitAsync();
            gateTaken = true;
            if (generation != _profileGeneration ||
                !ReferenceEquals(snapshot, _currentSnapshot) ||
                !_cells.Contains(target) ||
                target.SourceAreaId is null ||
                target.SourceTileId is null)
            {
                throw new InvalidOperationException(
                    EditorText.Get("BattleMapView_Commands_067"));
            }

            if (_managedEncounterBridgeService is not null && _codec is not null && !string.IsNullOrWhiteSpace(_gameDirectory))
            {
                var currentContent = await ActiveContentResolver.ResolveAsync(profile, _gameDirectory,
                    _workshopDirectory, _localModDirectory, _codec, SaveEditorLocations.CreateDefault().WorkspaceDirectory);
                var maintenance = await _managedEncounterBridgeService.ReconcileAsync(currentContent, _gameDirectory, _localModDirectory);
                if (maintenance.Changed)
                {
                    SaveEditApplied?.Invoke(maintenance.Message);
                    ActiveContentChanged?.Invoke(currentContent);
                    if (_snapshotReader is not null)
                    {
                        var updated = await _snapshotReader.LoadAsync(profile.ProfileDirectory);
                        _encounterCatalog = await Task.Run(() => BattleEncounterCatalog.Load(currentContent, updated));
                        _activeContentSnapshot = currentContent;
                        RenderSnapshot(updated, fitToView: false);
                    }
                    MapSelectionTextBlock.Text = EditorText.Get("BattleMapView_Commands_068");
                    return;
                }
                if (maintenance.Deferred) throw new InvalidOperationException(maintenance.Message);
            }

            if (encounterResolver is not null)
            {
                if (kind != BattleMapEditKind.PlaceBattle || encounter is not null)
                {
                    throw new InvalidOperationException(EditorText.Get("BattleMapView_Commands_069"));
                }
                encounter = await encounterResolver();
                managedEncounterResolved = true;
            }

            prepared = kind switch
            {
                BattleMapEditKind.DeleteContent => await service.PrepareDeleteContentAsync(
                    profile,
                    snapshot,
                    target.SourceAreaId,
                    target.SourceTileId),
                BattleMapEditKind.MoveParty => await service.PrepareMovePartyAsync(
                    profile,
                    snapshot,
                    target.SourceAreaId,
                    target.SourceTileId),
                BattleMapEditKind.PlaceBattle when encounter is not null =>
                    await service.PreparePlaceBattleAsync(
                        profile,
                        snapshot,
                        target.SourceAreaId,
                        target.SourceTileId,
                        encounter),
                BattleMapEditKind.PlaceContent when attachment is not null =>
                    await service.PreparePlaceContentAsync(
                        profile, snapshot, target.SourceAreaId, target.SourceTileId, attachment),
                BattleMapEditKind.SetBattleAttachment when attachment is not null =>
                    await service.PrepareSetBattleAttachmentAsync(
                        profile,
                        snapshot,
                        target.SourceAreaId,
                        target.SourceTileId,
                        attachment),
                BattleMapEditKind.RemoveBattleAttachment =>
                    await service.PrepareRemoveBattleAttachmentAsync(
                        profile,
                        snapshot,
                        target.SourceAreaId,
                        target.SourceTileId),
                _ => throw new InvalidOperationException(EditorText.Get("BattleMapView_Commands_070"))
            };
            var result = await service.CommitAsync(prepared);
            backupDirectory = result.BackupDirectory;
            committed = true;
            var operationLabel = kind switch
            {
                BattleMapEditKind.DeleteContent => EditorText.Get("BattleMapView_Commands_004"),
                BattleMapEditKind.MoveParty => EditorText.Get("BattleMapView_Commands_071"),
                BattleMapEditKind.PlaceBattle => EditorText.Get("BattleMapView_Commands_072"),
                BattleMapEditKind.PlaceContent => EditorText.Get("BattleMapView_Commands_073"),
                BattleMapEditKind.SetBattleAttachment => EditorText.Get("BattleMapView_Commands_074"),
                BattleMapEditKind.RemoveBattleAttachment => EditorText.Get("BattleMapView_Commands_075"),
                _ => kind.ToString()
            };
            SaveEditApplied?.Invoke(
                EditorText.Format("BattleMapView_Commands_076", operationLabel) +
                EditorText.Format("BattleMapView_Commands_077", prepared.SessionId, profile.ProfileId, profile.ProfileDirectory) +
                EditorText.Format("BattleMapView_Commands_078", target.SourceAreaId, target.SourceTileId) +
                (kind == BattleMapEditKind.MoveParty ? EditorText.Format("BattleMapView_Commands_079", snapshot.PartyAreaId, snapshot.PartyTileIndex) : string.Empty) +
                (prepared.Encounter is { } appliedEncounter ? EditorText.Format("BattleMapView_Commands_080", appliedEncounter.DisplayName, appliedEncounter.OriginDifficulty) : string.Empty) +
                (prepared.Attachment is { } appliedAttachment
                    ? kind == BattleMapEditKind.PlaceContent
                        ? EditorText.Format("BattleMapView_Commands_081", appliedAttachment.KindLabel, appliedAttachment.Id, prepared.Preview.PreviousRawContent) +
                          EditorText.Format("BattleMapView_Commands_082", (int)appliedAttachment.StandaloneContent, appliedAttachment.SourceLabel)
                        : EditorText.Format("BattleMapView_Commands_083", appliedAttachment.Id)
                    : string.Empty) +
                EditorText.Format("BattleMapView_Commands_084", result.BackupDirectory));
            if (generation != _profileGeneration || _snapshotReader is null)
            {
                return;
            }

            var refreshed = await LoadSnapshotWithRetryAsync(
                _snapshotReader,
                profile.ProfileDirectory,
                CancellationToken.None);
            if (generation != _profileGeneration)
            {
                return;
            }

            RenderSnapshot(refreshed, fitToView: false);
            refreshedSuccessfully = true;
            MapSelectionTextBlock.Text = kind switch
            {
                BattleMapEditKind.DeleteContent => EditorText.Format("BattleMapView_Commands_085", target.DisplayName),
                BattleMapEditKind.MoveParty => EditorText.Format("BattleMapView_Commands_086", target.DisplayName),
                BattleMapEditKind.PlaceBattle => EditorText.Format("BattleMapView_Commands_087", target.DisplayName),
                BattleMapEditKind.PlaceContent => EditorText.Format("BattleMapView_Commands_088", target.DisplayName, attachment?.KindLabel),
                BattleMapEditKind.SetBattleAttachment => EditorText.Format("BattleMapView_Commands_089", target.DisplayName),
                BattleMapEditKind.RemoveBattleAttachment => EditorText.Format("BattleMapView_Commands_090", target.DisplayName),
                _ => EditorText.Get("BattleMapView_Commands_091")
            };
        }
        catch (Exception ex)
        {
            CrashDiagnostics.RecordException("Battle map: " + (committed ? "post-commit UI" : "edit"), ex,
                EditorText.Format("BattleMapView_Commands_093", kind, prepared?.SessionId ?? EditorText.Get("BattleMapView_Commands_092"), profile.ProfileId) +
                EditorText.Format("BattleMapView_Commands_094", profile.ProfileDirectory, target.SourceAreaId, target.SourceTileId) +
                EditorText.Format("BattleMapView_Commands_096", committed, managedEncounterResolved, backupDirectory ?? EditorText.Get("BattleMapView_Commands_095")));
            MapSelectionTextBlock.Text = committed
                ? EditorText.Get("BattleMapView_Commands_097")
                : ex is AggregateException
                    ? EditorText.Get("BattleMapView_Commands_098")
                    : managedEncounterResolved
                        ? EditorText.Get("BattleMapView_Commands_099")
                        : EditorText.Get("BattleMapView_Commands_100");
            if (Window.GetWindow(this) is { } owner)
            {
                ThemedDialog.ShowMessage(
                    owner,
                    ex.Message,
                    committed ? EditorText.Get("BattleMapView_Commands_101") : EditorText.Get("BattleMapView_Commands_102"),
                    ThemedDialogKind.Error);
            }
        }
        finally
        {
            if (gateTaken)
            {
                _refreshGate.Release();
            }
            _isApplyingEdit = false;
            MapCanvas.IsHitTestVisible = true;
            SaveEditBusyChanged?.Invoke(false);
            if (generation == _profileGeneration)
            {
                StartProfileMonitoring();
            }
        }

        if (!refreshedSuccessfully && generation == _profileGeneration)
        {
            await RefreshLiveSnapshotAsync(generation);
        }
    }

    private void MovePartyPreview(PrototypeMapCell target)
    {
        foreach (var cell in _cells)
        {
            cell.HasParty = ReferenceEquals(cell, target);
            RefreshCellVisual(cell);
        }

        MapSelectionTextBlock.Text =
            EditorText.Format("BattleMapView_Commands_103", target.DisplayName);
    }

}
