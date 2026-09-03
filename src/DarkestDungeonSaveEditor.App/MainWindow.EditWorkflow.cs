using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DarkestDungeonSaveEditor.Core;
using Microsoft.Win32;

namespace DarkestDungeonSaveEditor.App;

public partial class MainWindow : Window
{
    private async void Preview_Click(object sender, RoutedEventArgs e)
    {
        if (CatalogTabs.SelectedIndex == BattleTabIndex)
        {
            return;
        }

        InvalidatePreparedEdit();
        var previewRevision = _editRevision;
        try
        {
            SetBusy(true);
            var profile = SteamDiscovery.OpenProfile(ProfileDirectoryTextBox.Text.Trim());
            EnsureCatalogMatches(
                profile,
                requireCurrentQuantitySnapshot: CatalogTabs.SelectedIndex == 0,
                requireCurrentEstateSnapshot: CatalogTabs.SelectedIndex == 1);
            var jarPath = Path.Combine(AppContext.BaseDirectory, "tools", "DDSaveEditor", "DDSaveEditor.jar");
            var editService = new SaveEditService(new DsonSaveCodec(jarPath));
            AppendStatus($"正在为 {profile.ProfileId} 创建只读副本并执行编码回环……");

            if (CatalogTabs.SelectedIndex == 0)
            {
                if (ItemGrid.SelectedItem is not ItemRow selectedItem)
                {
                    throw new InvalidOperationException("请先选择一个可计数物品。");
                }

                if (!int.TryParse(
                        CopiesTextBox.Text,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var targetAmount) ||
                    targetAmount < 0)
                {
                    throw new InvalidOperationException("目标数量必须是 0 到 2147483647 的整数。");
                }

                if (_activeContentSnapshot is null)
                {
                    throw new InvalidOperationException("活动内容快照不可用，请重新加载内容目录。");
                }

                var preparedItemEdit = await editService.PrepareQuantityItemEditAsync(
                    profile,
                    selectedItem.Definition,
                    targetAmount,
                    _activeContentSnapshot);
                if (previewRevision != _editRevision || CatalogTabs.SelectedIndex != 0)
                {
                    AppendStatus("预览期间目录状态发生变化，已丢弃本次结果。");
                    return;
                }

                _editService = editService;
                _preparedQuantityItemEdit = preparedItemEdit;
                var preview = preparedItemEdit.Preview;
                PreviewSummaryTextBlock.Text =
                    $"{selectedItem.DisplayName} / {preview.ItemId} · {FormatItemStorage(selectedItem.Definition)} · " +
                    $"数量 {preview.ExistingAmount} → {preview.TargetAmount}" +
                    (preview.StorageKind == QuantityItemStorageKind.RaidInventory
                        ? $" · 背包格 {preview.ExistingInventoryEntries} → {preview.ResultingInventoryEntries}/" +
                          $"{FormatStorageCapacity(preview.InventoryCapacity)}"
                        : preview.CreatedEntry
                            ? "（将创建新的存档条目）"
                            : string.Empty);
                var itemWarning = FormatQuantityItemBoundaryWarning(selectedItem.Definition);
                PreviewWarningTextBlock.Text = itemWarning;
                PreviewWarningTextBlock.Visibility = string.IsNullOrWhiteSpace(itemWarning)
                    ? Visibility.Collapsed
                    : Visibility.Visible;
                ApplyButton.IsEnabled = true;
                AppendStatus(
                    $"物品数量预览通过：{preview.ItemId}，{preview.ExistingAmount} → {preview.TargetAmount}；" +
                    $"保存位置 {FormatItemStorage(preview.StorageKind)}；工作区：{preparedItemEdit.WorkspaceDirectory}");
                return;
            }

            if (CatalogTabs.SelectedIndex == 1)
            {
                if (TrinketGrid.SelectedItem is not TrinketRow selected)
                {
                    throw new InvalidOperationException("请先选择一个饰品。");
                }

                if (!int.TryParse(CopiesTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var copies) ||
                    copies is < 1 or > 999)
                {
                    throw new InvalidOperationException("添加数量必须是 1 到 999 的整数。");
                }

                if (_trinketStorage is null)
                {
                    PreviewWarningTextBlock.Text =
                        "未能从当前活动内容解析唯一有效的饰品仓库上限。为避免写入超过实际容量，饰品预览与应用已禁用。";
                    PreviewWarningTextBlock.Visibility = Visibility.Visible;
                    AppendStatus($"饰品预览已阻止：{PreviewWarningTextBlock.Text}");
                    throw new InvalidOperationException("当前饰品仓库容量未知，无法安全生成预览。");
                }

                if (_activeContentSnapshot is null)
                {
                    throw new InvalidOperationException("活动内容快照不可用，请重新加载内容目录。");
                }

                var preparedEdit = await editService.PrepareTrinketEditAsync(
                    profile,
                    selected.Definition,
                    copies,
                    _trinketStorage,
                    _activeContentSnapshot);
                if (previewRevision != _editRevision || CatalogTabs.SelectedIndex != 1)
                {
                    AppendStatus("预览期间目录状态发生变化，已丢弃本次结果。");
                    return;
                }

                _editService = editService;
                _preparedTrinketEdit = preparedEdit;
                var preview = preparedEdit.Preview;
                PreviewSummaryTextBlock.Text =
                    $"{preview.TrinketId} · 仓库内该饰品 {preview.ExistingCopies} → {preview.ResultingCopies} " +
                    $"（定义上限 {FormatDefinitionLimit(preview.DefinitionLimit)}） · " +
                    $"仓库总槽位 {preview.ExistingInventoryEntries} → {preview.ResultingInventoryEntries} / " +
                    $"{FormatStorageCapacity(preview.StorageCapacity)}";
                if (preview.ExceedsDefinitionLimit)
                {
                    PreviewWarningTextBlock.Text =
                        $"已超过该饰品的定义持有上限 {preview.DefinitionLimit}。编辑器会按控制台模式保留写入能力，" +
                        "但游戏之后不会再正常奖励该饰品；已装备副本未计入这里的仓库数量。";
                    PreviewWarningTextBlock.Visibility = Visibility.Visible;
                    AppendStatus($"饰品预览提示：{PreviewWarningTextBlock.Text}");
                }
                ApplyButton.IsEnabled = true;
                AppendStatus($"饰品预览通过，工作区：{preparedEdit.WorkspaceDirectory}");
                return;
            }

            if (HeroGrid.SelectedItem is not HeroRow selectedHero || _heroCatalog is null)
            {
                throw new InvalidOperationException("请先选择一个人物职业。");
            }
            if (_activeContentSnapshot is null)
            {
                throw new InvalidOperationException("活动内容快照不可用，请重新加载内容目录。");
            }

            var generated = StagecoachHeroCandidateFactory.Generate(
                _heroCatalog,
                selectedHero.Definition,
                RandomNumberGenerator.GetInt32(int.MaxValue),
                GetSelectedHeroLevel(),
                _selectedInitialQuirkIds);
            var preparedHeroEdit = await editService.PrepareStagecoachHeroEditAsync(
                profile,
                generated,
                _heroCatalog,
                _activeContentSnapshot);
            if (previewRevision != _editRevision || CatalogTabs.SelectedIndex != 2)
            {
                AppendStatus("预览期间目录状态发生变化，已丢弃本次结果。");
                return;
            }

            _editService = editService;
            _preparedHeroEdit = preparedHeroEdit;
            _preparedHeroCandidatePreview = generated.Preview;
            var heroLimitWarning = FormatHeroQuirkLimitWarnings(preparedHeroEdit.Preview);
            if (string.IsNullOrWhiteSpace(heroLimitWarning))
            {
                PreviewWarningTextBlock.Visibility = Visibility.Collapsed;
                PreviewWarningTextBlock.Text = string.Empty;
            }
            else
            {
                PreviewWarningTextBlock.Text = heroLimitWarning;
                PreviewWarningTextBlock.Visibility = Visibility.Visible;
                AppendStatus($"人物怪癖上限提示：{heroLimitWarning.Replace(Environment.NewLine, "；", StringComparison.Ordinal)}");
            }
            var heroPreview = generated.Preview;
            PreviewSummaryTextBlock.Text =
                $"{heroPreview.Name} / {heroPreview.HeroClass}：{heroPreview.ResolveLevel}级，XP {heroPreview.ResolveXp}，" +
                $"武器/护甲 {heroPreview.WeaponRank}/{heroPreview.ArmourRank}，HP {heroPreview.CurrentHp.ToString("0.##", CultureInfo.InvariantCulture)}，" +
                $"怪癖 [{FormatSelectedQuirks(heroPreview)}]，" +
                $"技能 {heroPreview.CombatSkills.Count}+{heroPreview.CampingSkills.Count}，" +
                $"个人升级记录 {preparedHeroEdit.Preview.UpgradePurchaseCount}；" +
                $"马车 {preparedHeroEdit.Preview.ExistingCandidates} → {preparedHeroEdit.Preview.ResultingCandidates}，" +
                $"GUID {preparedHeroEdit.Preview.CandidateGuid}。";
            ApplyButton.IsEnabled = true;
            AppendStatus(
                $"人物预览通过：{heroPreview.Name} / {heroPreview.HeroClass} / {heroPreview.ResolveLevel}级；" +
                $"XP {heroPreview.ResolveXp}；武器/护甲 rank {heroPreview.WeaponRank}/{heroPreview.ArmourRank}；" +
                $"正面怪癖 [{string.Join(", ", heroPreview.PositiveQuirks)}]；" +
                $"负面怪癖 [{string.Join(", ", heroPreview.NegativeQuirks)}]；" +
                $"疾病 [{string.Join(", ", heroPreview.Diseases)}]；" +
                $"战斗技能 [{string.Join(", ", heroPreview.CombatSkills)}]；" +
                $"露营技能 [{string.Join(", ", heroPreview.CampingSkills)}]；" +
                $"个人升级记录 {preparedHeroEdit.Preview.UpgradePurchaseCount} 条。");
            foreach (var warning in heroPreview.Warnings)
            {
                AppendStatus($"人物预览提示：{warning}");
            }
            AppendStatus($"人物预览工作区：{preparedHeroEdit.WorkspaceDirectory}");
        }
        catch (Exception ex)
        {
            AppendStatus($"生成预览失败：{ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (_editService is null ||
            (_preparedQuantityItemEdit is null && _preparedTrinketEdit is null && _preparedHeroEdit is null))
        {
            return;
        }

        var isItemEdit = _preparedQuantityItemEdit is not null;
        var isHeroEdit = _preparedHeroEdit is not null;
        string profileDirectory;
        string changeSummary;
        string definitionLimitWarningText;
        if (isItemEdit)
        {
            profileDirectory = _preparedQuantityItemEdit!.Profile.ProfileDirectory;
            changeSummary =
                $"把 {_preparedQuantityItemEdit.Item.DisplayId} 的数量从 " +
                $"{_preparedQuantityItemEdit.Preview.ExistingAmount} 修改为 " +
                $"{_preparedQuantityItemEdit.Preview.TargetAmount}";
            definitionLimitWarningText = FormatQuantityItemBoundaryWarning(_preparedQuantityItemEdit.Item);
        }
        else if (isHeroEdit)
        {
            profileDirectory = _preparedHeroEdit!.Profile.ProfileDirectory;
            changeSummary =
                $"向普通马车加入 {_preparedHeroCandidatePreview?.Name} / {_preparedHeroCandidatePreview?.HeroClass} " +
                $"（{_preparedHeroCandidatePreview?.ResolveLevel}级，XP {_preparedHeroCandidatePreview?.ResolveXp}，" +
                $"武器/护甲 rank {_preparedHeroCandidatePreview?.WeaponRank}/{_preparedHeroCandidatePreview?.ArmourRank}；" +
                $"个人升级记录 {_preparedHeroEdit.Preview.UpgradePurchaseCount} 条；" +
                $"GUID {_preparedHeroEdit.Preview.CandidateGuid}；" +
                $"初始怪癖 [{FormatSelectedQuirks(_preparedHeroCandidatePreview)}]）";
            definitionLimitWarningText = FormatHeroQuirkLimitWarnings(_preparedHeroEdit.Preview);
        }
        else
        {
            profileDirectory = _preparedTrinketEdit!.Profile.ProfileDirectory;
            changeSummary =
                $"加入 {_preparedTrinketEdit.Preview.RequestedCopies} 个 {_preparedTrinketEdit.Trinket.Id}";
            definitionLimitWarningText = _preparedTrinketEdit.Preview.ExceedsDefinitionLimit
                ? $"写入后仓库内该饰品将有 {_preparedTrinketEdit.Preview.ResultingCopies} 个，" +
                  $"超过定义上限 {_preparedTrinketEdit.Preview.DefinitionLimit}。"
                : string.Empty;
        }

        var definitionLimitWarning = string.IsNullOrWhiteSpace(definitionLimitWarningText)
            ? string.Empty
            : $"\n\n注意：\n{definitionLimitWarningText}";
        var confirmation = ThemedDialog.Confirm(
            this,
            $"将对以下档案执行：{changeSummary}\n\n" +
            $"{profileDirectory}" + definitionLimitWarning + "\n\n" +
            "程序会先完整备份当前档案。确认游戏已经关闭并继续吗？",
            "确认应用存档修改");
        if (!confirmation)
        {
            AppendStatus("已取消应用，真实存档未修改。");
            return;
        }

        try
        {
            SetBusy(true);
            string backupDirectory;
            SaveCommitResult? estateCommit = null;
            if (isItemEdit)
            {
                estateCommit = await _editService.CommitAsync(_preparedQuantityItemEdit!);
                backupDirectory = estateCommit.BackupDirectory;
            }
            else if (isHeroEdit)
            {
                backupDirectory = (await _editService.CommitAsync(_preparedHeroEdit!)).BackupDirectory;
            }
            else
            {
                estateCommit = await _editService.CommitAsync(_preparedTrinketEdit!);
                backupDirectory = estateCommit.BackupDirectory;
            }

            if (isItemEdit && estateCommit is not null)
            {
                _catalogQuantitySaveSha256 = estateCommit.FinalSha256;
                if (_quantitySaveContext == QuantityItemSaveContext.Town)
                {
                    _catalogEstateSaveSha256 = estateCommit.FinalSha256;
                }
            }
            else if (estateCommit is not null)
            {
                _catalogEstateSaveSha256 = estateCommit.FinalSha256;
            }

            if (isItemEdit)
            {
                var editedKey = _preparedQuantityItemEdit!.Item.CatalogKey;
                var targetAmount = _preparedQuantityItemEdit.Preview.TargetAmount;
                var resultingEntryCount = _preparedQuantityItemEdit.Preview.ResultingMatchingEntries;
                _allItems = _allItems
                    .Select(item => item.CatalogKey.Equals(editedKey, StringComparison.OrdinalIgnoreCase)
                        ? item with
                        {
                            CurrentAmount = targetAmount,
                            IsPresentInSave = resultingEntryCount > 0,
                            SavedEntryCount = resultingEntryCount
                        }
                        : item)
                    .ToArray();
                ApplyFilter();
                UpdateCatalogMode();
            }

            AppendStatus($"应用成功。备份：{backupDirectory}");
            ThemedDialog.ShowMessage(
                this,
                $"存档修改成功。\n\n备份目录：\n{backupDirectory}",
                "完成",
                ThemedDialogKind.Information);
            InvalidatePreparedEdit();
        }
        catch (Exception ex)
        {
            AppendStatus($"应用失败：{ex.Message}");
            ThemedDialog.ShowMessage(this, ex.Message, "应用失败", ThemedDialogKind.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

}
