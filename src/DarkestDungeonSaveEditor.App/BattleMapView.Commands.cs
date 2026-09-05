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
        var isHiddenSystemContent = IsHiddenSystemContent(cell);
        var isProtectedContent = isHiddenSystemContent ||
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
                hasPersistedContent ? "替换" : "新建",
                hasPersistedContent ? "↻" : "＋");
            PopulateContentChoices(changeRoot, cell);
            contextMenu.Items.Add(changeRoot);

            if (CanEditBattleAttachment(cell))
            {
                var attachmentRoot = CreateMenuItem("战斗附加内容", "✦");
                PopulateBattleAttachmentChoices(attachmentRoot, cell);
                contextMenu.Items.Add(attachmentRoot);
            }

            if (hasDeletableContent)
            {
                contextMenu.Items.Add(CreateAsyncActionMenuItem(
                    "删除",
                    "×",
                    () => DeleteContentAsync(cell)));
            }

            contextMenu.Items.Add(new Separator
            {
                Style = (Style)FindResource("BattleMapSeparatorStyle")
            });
        }
        var moveItem = CreateAsyncActionMenuItem(
            "移动队伍到此",
            "◆",
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

    private static bool IsHiddenSystemContent(PrototypeMapCell cell) =>
        cell.SourceAreaId is not null &&
        cell.RawContent == (int)BattleMapTileContent.Hunger;

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
        var battleMenu = CreateMenuItem("战斗", "⚔");
        var isFinalRoom = _currentSnapshot is not null &&
                          string.Equals(
                              cell.SourceAreaId,
                              _currentSnapshot.FinalRoomId,
                              StringComparison.OrdinalIgnoreCase);
        if (!isFinalRoom)
        {
            battleMenu.Items.Add(CreateEncounterPickerItem(
                cell,
                "普通战斗",
                "⚔",
                BattleEncounterClassification.Ordinary));
            battleMenu.Items.Add(CreateEncounterPickerItem(
                cell,
                "游荡首领 / 特殊遭遇",
                "☠",
                BattleEncounterClassification.RoamingBoss,
                BattleEncounterClassification.RoamingEncounter,
                BattleEncounterClassification.ConditionalOrAdditional));
        }

        if (cell.Kind == PrototypeCellKind.Room)
        {
            battleMenu.Items.Add(CreateEncounterPickerItem(
                cell,
                "固定首领",
                "☠",
                BattleEncounterClassification.FixedBoss));
        }
        root.Items.Add(battleMenu);

        if (isFinalRoom)
        {
            return;
        }

        root.Items.Insert(0, CreateActionMenuItem("奇物", "?", () =>
            ApplyContentPreview(cell, PrototypeContent.Curio, "奇物")));
        if (cell.Kind == PrototypeCellKind.Room)
        {
            root.Items.Add(CreateActionMenuItem("宝箱", "✦", () =>
                ApplyContentPreview(cell, PrototypeContent.Treasure, "宝箱")));
        }
        else
        {
            root.Items.Add(CreateActionMenuItem("陷阱", "!", () =>
                ApplyContentPreview(cell, PrototypeContent.Trap, "陷阱")));
            root.Items.Add(CreateActionMenuItem("障碍", "▰", () =>
                ApplyContentPreview(cell, PrototypeContent.Obstacle, "障碍")));
        }
    }

    private bool CanEditBattleAttachment(PrototypeMapCell cell) =>
        _currentSnapshot is not null &&
        cell.Kind == PrototypeCellKind.Room &&
        cell.SourceAreaId is not null &&
        !string.Equals(
            cell.SourceAreaId,
            _currentSnapshot.EntranceAreaId,
            StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(
            cell.SourceAreaId,
            _currentSnapshot.FinalRoomId,
            StringComparison.OrdinalIgnoreCase) &&
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
        root.Items.Add(CreateBattleAttachmentPickerItem(cell, "奇物", "?", curios));
        root.Items.Add(CreateBattleAttachmentPickerItem(cell, "宝箱", "✦", treasures));
        if (cell.RawContent is
            (int)BattleMapTileContent.GuardedCurio or
            (int)BattleMapTileContent.GuardedTreasure)
        {
            root.Items.Add(new Separator
            {
                Style = (Style)FindResource("BattleMapSeparatorStyle")
            });
            root.Items.Add(CreateAsyncActionMenuItem(
                "移除附加内容",
                "×",
                () => RemoveBattleAttachmentAsync(cell)));
        }
    }

    private MenuItem CreateBattleAttachmentPickerItem(
        PrototypeMapCell cell,
        string label,
        string icon,
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
            MapSelectionTextBlock.Text = "当前奇物/宝箱目录已经失效，请重新加载内容目录。";
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
        string icon,
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
            ? "房间"
            : cell.Kind == PrototypeCellKind.Room ? "房间" : "走廊";
        return CreateAsyncActionMenuItem(
            $"{label}（{candidates.Count}）",
            icon,
            () => SelectAndPlaceEncounterAsync(cell, candidates, $"选择{targetLabel}{label}"));
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
                StringComparison.OrdinalIgnoreCase) ||
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
            MapSelectionTextBlock.Text = "当前遭遇目录已经失效，请重新加载内容目录。";
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
            MapSelectionTextBlock.Text = "当前遭遇目录或写入环境已经失效，请重新加载内容目录。";
            return;
        }

        var replacementText = target.RawContent == 0
            ? "新建"
            : $"替换“{target.ContentLabel ?? "当前内容"}”";
        var difficultyWarning = encounter.OriginDifficulty == catalog.Difficulty
            ? string.Empty
            : Environment.NewLine +
              $"所选组合来自难度 {encounter.OriginDifficulty}，敌人数值不会自动缩放为当前难度 {catalog.Difficulty}。";
        var classificationLabel = FormatEncounterClassification(encounter);
        if (!ThemedDialog.Confirm(
                owner,
                $"将在 {target.DisplayName} {replacementText}为战斗：{encounter.DisplayName}" +
                Environment.NewLine +
                $"来源：{encounter.SourceLabel} · {encounter.OriginDungeonId}" +
                difficultyWarning + Environment.NewLine +
                "编辑器会在后台追加或复用稳定遭遇索引，并自动保持托管 Bridge 启用。" +
                Environment.NewLine +
                "旧奇物、陷阱或障碍绑定会被清除，程序会先完整备份当前档案。",
                $"确认写入{classificationLabel}"))
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
                    $"正在为 {encounter.DisplayName} 更新托管遭遇表并验证索引……";
                var result = await managedService.EnsureEncounterAsync(
                    profile,
                    snapshot,
                    activeContent,
                    catalog,
                    encounter,
                    _gameDirectory,
                    _workshopDirectory,
                    _localModDirectory);
                _activeContentSnapshot = result.ActiveContent;
                _encounterCatalog = result.Catalog;
                await ReloadRoomAttachmentCatalogAsync(result.ActiveContent);
                ActiveContentChanged?.Invoke(result.ActiveContent);
                CrashDiagnostics.RecordStatus(
                    $"托管 Encounter Bridge：目录={result.PackageDirectory}；" +
                    $"地区={catalog.DungeonId}；难度={catalog.Difficulty}；" +
                    $"来源地区={encounter.OriginDungeonId}；来源难度={encounter.OriginDifficulty}；" +
                    $"分类={encounter.Classification}；来源类型={encounter.SourceKind}；" +
                    $"mash_type={result.MashType}；" +
                    $"mash_index={result.MashIndex}；新增={result.EncounterWasAdded}；" +
                    $"自动启用或置顶={result.ProfileConfigurationChanged}；" +
                    $"组合={string.Join(',', result.MonsterIds)}；" +
                    $"配置备份={result.ProfileBackupDirectory ?? "无需改动"}");
                return result.DirectEncounter;
            });
    }

    private static string FormatEncounterClassification(BattleEncounterDefinition encounter) =>
        encounter.Classification switch
        {
            BattleEncounterClassification.FixedBoss => "固定首领",
            BattleEncounterClassification.RoamingBoss => "游荡首领",
            BattleEncounterClassification.RoamingEncounter => "游荡遭遇",
            BattleEncounterClassification.ConditionalOrAdditional when
                encounter.ContainsBossMonster &&
                encounter.SourceKind == BattleEncounterSourceKind.Conditional => "条件首领",
            BattleEncounterClassification.ConditionalOrAdditional when
                encounter.ContainsBossMonster &&
                encounter.SourceKind == BattleEncounterSourceKind.Additional => "额外首领",
            BattleEncounterClassification.ConditionalOrAdditional when
                encounter.ContainsBossMonster => "特殊首领",
            BattleEncounterClassification.ConditionalOrAdditional => "条件 / 额外战斗",
            _ => "战斗遭遇"
        };

    private async Task ReloadRoomAttachmentCatalogAsync(ActiveContentSnapshot activeContent)
    {
        try
        {
            _roomAttachmentCatalog = await Task.Run(
                () => BattleRoomAttachmentCatalog.Load(activeContent));
        }
        catch (Exception ex)
        {
            _roomAttachmentCatalog = null;
            CrashDiagnostics.RecordException(
                "BattleMap: reload room attachment catalog",
                ex,
                $"档案={activeContent.Profile.ProfileId}");
        }
    }

    private MenuItem CreateMenuItem(string header, string icon)
    {
        return new MenuItem
        {
            Header = header,
            Icon = new TextBlock
            {
                Text = icon,
                Foreground = new SolidColorBrush(Color.FromRgb(212, 190, 115)),
                HorizontalAlignment = HorizontalAlignment.Center,
                FontWeight = FontWeights.Bold
            },
            Style = (Style)FindResource("BattleMapMenuItemStyle")
        };
    }

    private MenuItem CreateActionMenuItem(string header, string icon, Action action)
    {
        var menuItem = CreateMenuItem(header, icon);
        menuItem.Click += (_, _) => action();
        return menuItem;
    }

    private MenuItem CreateAsyncActionMenuItem(string header, string icon, Func<Task> action)
    {
        var menuItem = CreateMenuItem(header, icon);
        menuItem.Click += async (_, _) => await action();
        return menuItem;
    }

    private void ApplyContentPreview(
        PrototypeMapCell cell,
        PrototypeContent content,
        string contentLabel)
    {
        cell.Content = content;
        cell.ContentLabel = contentLabel;
        RefreshCellVisual(cell);
        MapSelectionTextBlock.Text =
            $"已在界面预览中把 {cell.DisplayName} 设置为“{contentLabel}”；存档未发生变化。";
    }

    private async Task DeleteContentAsync(PrototypeMapCell target)
    {
        if (_currentSnapshot is null)
        {
            target.Content = PrototypeContent.None;
            target.ContentLabel = null;
            RefreshCellVisual(target);
            MapSelectionTextBlock.Text =
                $"已在界面预览中清空 {target.DisplayName}；存档未发生变化。";
            return;
        }
        if (!TryGetWritableContext(target, out var owner, out var profile, out var snapshot, out var service))
        {
            MapSelectionTextBlock.Text = "当前地图正在刷新或处理其他操作，请重新选择目标。";
            return;
        }

        var specialWarning = string.Equals(
            target.SourceAreaId,
            snapshot.FinalRoomId,
            StringComparison.OrdinalIgnoreCase)
            ? Environment.NewLine + "这是当前最终房间；删除其内容可能使任务无法完成。"
            : string.Empty;
        var contentDescription = target.RawContent == 0
            ? "已完成后留下的资源模型"
            : $"“{target.ContentLabel ?? "当前内容"}”";
        if (!ThemedDialog.Confirm(
                owner,
                $"将彻底删除 {target.DisplayName} 的{contentDescription}。" +
                specialWarning + Environment.NewLine +
                "事件、奇物、陷阱、障碍和战斗绑定会一并清空；探索状态会保留。" +
                Environment.NewLine + "程序会先完整备份当前档案。",
                "确认删除地图内容"))
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
            MapSelectionTextBlock.Text = "当前地图正在刷新或处理其他操作，请重新选择目标。";
            return;
        }

        var contentWarning = target.RawContent == 0 || IsHiddenSystemContent(target)
            ? string.Empty
            : Environment.NewLine +
              $"目标仍保留“{target.ContentLabel ?? "地图事件"}”；移动只修改队伍落点，" +
              "不会模拟走入该格，因此目标事件不保证立即触发。";
        if (!ThemedDialog.Confirm(
                owner,
                $"将当前队伍移动到 {target.DisplayName}。" +
                contentWarning + Environment.NewLine +
                "程序会先完整备份当前档案。",
                "确认移动队伍"))
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
            MapSelectionTextBlock.Text = "当前地图正在刷新或处理其他操作，请重新选择目标。";
            return;
        }
        if (!encounter.CanPlaceDirectly || encounter.MashIndex is null)
        {
            MapSelectionTextBlock.Text = "所选遭遇没有可证明的当前副本索引。";
            return;
        }

        var replacementText = target.RawContent == 0
            ? "新建"
            : $"替换“{target.ContentLabel ?? "当前内容"}”";
        var naturalQuotaWarning = encounter.Weight is <= 0
            ? Environment.NewLine +
              "这是零权重桥接/测试条目；本次写入不会修改条件或额外遭遇的自然生成计数。"
            : string.Empty;
        if (!ThemedDialog.Confirm(
                owner,
                $"将在 {target.DisplayName} {replacementText}为战斗：{encounter.DisplayName}" +
                Environment.NewLine + $"来源：{encounter.SourceLabel}" +
                Environment.NewLine +
                $"当前表索引：mash_type={encounter.MashType}，mash_index={encounter.MashIndex}" +
                naturalQuotaWarning + Environment.NewLine +
                "旧奇物、陷阱或障碍绑定会被清除，程序会先完整备份当前档案。",
                "确认写入战斗遭遇"))
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
            MapSelectionTextBlock.Text = "当前地图正在刷新或处理其他操作，请重新选择目标。";
            return;
        }

        var kindLabel = attachment.Kind == BattleRoomAttachmentKind.Curio ? "奇物" : "宝箱";
        var actionLabel = target.RawContent is
            (int)BattleMapTileContent.GuardedCurio or
            (int)BattleMapTileContent.GuardedTreasure
            ? "替换当前附加内容"
            : "添加到当前战斗";
        if (!ThemedDialog.Confirm(
                owner,
                $"将在 {target.DisplayName} {actionLabel}：{attachment.ChineseName} / {attachment.EnglishName}" +
                Environment.NewLine + $"ID：{attachment.Id}" +
                Environment.NewLine + $"来源：{attachment.SourceLabel}" +
                Environment.NewLine +
                "现有战斗及敌方组合保持不变，程序会先完整备份当前档案。",
                $"确认应用{kindLabel}"))
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
            MapSelectionTextBlock.Text = "当前地图正在刷新或处理其他操作，请重新选择目标。";
            return;
        }

        if (!ThemedDialog.Confirm(
                owner,
                $"将移除 {target.DisplayName} 的“{target.ContentLabel ?? "附加内容"}”，保留原有战斗及敌方组合。" +
                Environment.NewLine + "程序会先完整备份当前档案。",
                "确认移除战斗附加内容"))
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
        _isApplyingEdit = true;
        SaveEditBusyChanged?.Invoke(true);
        CloseActiveContextMenu();
        CancelScheduledRefreshRetry();
        StopProfileMonitoring();
        MapCanvas.IsHitTestVisible = false;
        MapSelectionTextBlock.Text = kind switch
        {
            BattleMapEditKind.DeleteContent => $"正在删除 {target.DisplayName} 的内容并验证存档……",
            BattleMapEditKind.MoveParty => $"正在把队伍移动到 {target.DisplayName} 并验证存档……",
            BattleMapEditKind.PlaceBattle => $"正在向 {target.DisplayName} 写入战斗并验证存档……",
            BattleMapEditKind.SetBattleAttachment => $"正在修改 {target.DisplayName} 的战斗附加内容并验证存档……",
            BattleMapEditKind.RemoveBattleAttachment => $"正在移除 {target.DisplayName} 的战斗附加内容并验证存档……",
            _ => "正在验证战斗地图操作……"
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
                    "地图已刷新；请在最新地图上重新选择目标。");
            }

            if (encounterResolver is not null)
            {
                if (kind != BattleMapEditKind.PlaceBattle || encounter is not null)
                {
                    throw new InvalidOperationException("托管遭遇解析器只能用于尚未解析索引的战斗写入。");
                }
                encounter = await encounterResolver();
                managedEncounterResolved = true;
            }

            var prepared = kind switch
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
                _ => throw new InvalidOperationException("战斗地图操作缺少有效参数。")
            };
            var result = await service.CommitAsync(prepared);
            committed = true;
            var operationLabel = kind switch
            {
                BattleMapEditKind.DeleteContent => "删除",
                BattleMapEditKind.MoveParty => "移动",
                BattleMapEditKind.PlaceBattle => "遭遇写入",
                BattleMapEditKind.SetBattleAttachment => "战斗附加内容写入",
                BattleMapEditKind.RemoveBattleAttachment => "战斗附加内容移除",
                _ => kind.ToString()
            };
            SaveEditApplied?.Invoke(
                $"战斗地图{operationLabel}已应用：" +
                $"目标={target.SourceAreaId}.{target.SourceTileId}；备份={result.BackupDirectory}");
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
                BattleMapEditKind.DeleteContent => $"已删除 {target.DisplayName} 的内容。",
                BattleMapEditKind.MoveParty => $"已将队伍移动到 {target.DisplayName}。",
                BattleMapEditKind.PlaceBattle => $"已在 {target.DisplayName} 写入战斗遭遇。",
                BattleMapEditKind.SetBattleAttachment => $"已更新 {target.DisplayName} 的战斗附加内容。",
                BattleMapEditKind.RemoveBattleAttachment => $"已移除 {target.DisplayName} 的战斗附加内容。",
                _ => "战斗地图操作已完成。"
            };
        }
        catch (Exception ex)
        {
            MapSelectionTextBlock.Text = committed
                ? "操作已写入，但地图刷新失败；请重新加载内容目录。"
                : managedEncounterResolved
                    ? "托管遭遇表已就绪，但目标地图格未写入。可直接重试同一遭遇。"
                    : "操作未应用；存档保持原状或已自动恢复。";
            if (Window.GetWindow(this) is { } owner)
            {
                ThemedDialog.ShowMessage(
                    owner,
                    ex.Message,
                    committed ? "地图刷新失败" : "战斗地图操作失败",
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
            $"界面预览：队伍移动至 {target.DisplayName}。存档未发生变化。";
    }

}
