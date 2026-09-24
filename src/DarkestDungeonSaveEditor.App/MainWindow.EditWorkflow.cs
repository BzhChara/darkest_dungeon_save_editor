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
            AppendStatus(EditorText.Format("MainWindow_EditWorkflow_001", profile.ProfileId));

            if (CatalogTabs.SelectedIndex == 0)
            {
                if (ItemGrid.SelectedItem is not ItemRow selectedItem)
                {
                    throw new InvalidOperationException(EditorText.Get("MainWindow_EditWorkflow_002"));
                }

                if (!int.TryParse(
                        CopiesTextBox.Text,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var targetAmount) ||
                    targetAmount < 0)
                {
                    throw new InvalidOperationException(EditorText.Get("MainWindow_EditWorkflow_003"));
                }

                if (_activeContentSnapshot is null)
                {
                    throw new InvalidOperationException(EditorText.Get("MainWindow_EditWorkflow_004"));
                }

                var preparedItemEdit = await editService.PrepareQuantityItemEditAsync(
                    profile,
                    selectedItem.Definition,
                    targetAmount,
                    _activeContentSnapshot);
                if (previewRevision != _editRevision || CatalogTabs.SelectedIndex != 0)
                {
                    AppendStatus(EditorText.Get("MainWindow_EditWorkflow_005"), level: DiagnosticLogLevel.Warning);
                    return;
                }

                _editService = editService;
                _preparedQuantityItemEdit = preparedItemEdit;
                var preview = preparedItemEdit.Preview;
                PreviewSummaryTextBlock.Text =
                    $"{selectedItem.DisplayName} / {preview.ItemId} · {FormatItemStorage(selectedItem.Definition)} · " +
                    EditorText.Format("MainWindow_EditWorkflow_006", preview.ExistingAmount, preview.TargetAmount) +
                    (preview.StorageKind == QuantityItemStorageKind.RaidInventory
                        ? EditorText.Format("MainWindow_EditWorkflow_007", preview.ExistingInventoryEntries, preview.ResultingInventoryEntries) +
                          $"{FormatStorageCapacity(preview.InventoryCapacity)}"
                        : preview.CreatedEntry
                            ? EditorText.Get("MainWindow_EditWorkflow_008")
                            : string.Empty);
                var itemWarning = FormatQuantityItemBoundaryWarning(selectedItem.Definition);
                PreviewWarningTextBlock.Text = itemWarning;
                PreviewWarningTextBlock.Visibility = string.IsNullOrWhiteSpace(itemWarning)
                    ? Visibility.Collapsed
                    : Visibility.Visible;
                if (!string.IsNullOrWhiteSpace(itemWarning))
                {
                    AppendStatus(EditorText.Format("MainWindow_EditWorkflow_009", itemWarning), level: DiagnosticLogLevel.Warning);
                }
                ApplyButton.IsEnabled = true;
                AppendStatus(
                    EditorText.Format("MainWindow_EditWorkflow_010", preview.ItemId, preview.ExistingAmount, preview.TargetAmount) +
                    EditorText.Format("MainWindow_EditWorkflow_011", FormatItemStorage(preview.StorageKind), preparedItemEdit.WorkspaceDirectory));
                return;
            }

            if (CatalogTabs.SelectedIndex == 1)
            {
                if (TrinketGrid.SelectedItem is not TrinketRow selected)
                {
                    throw new InvalidOperationException(EditorText.Get("MainWindow_EditWorkflow_012"));
                }

                if (!int.TryParse(CopiesTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var copies) ||
                    copies is < 1 or > 999)
                {
                    throw new InvalidOperationException(EditorText.Get("MainWindow_EditWorkflow_013"));
                }

                if (_trinketStorage is null)
                {
                    PreviewWarningTextBlock.Text =
                        EditorText.Get("MainWindow_EditWorkflow_014");
                    PreviewWarningTextBlock.Visibility = Visibility.Visible;
                    AppendStatus(EditorText.Format("MainWindow_EditWorkflow_015", PreviewWarningTextBlock.Text), level: DiagnosticLogLevel.Warning);
                    throw new InvalidOperationException(EditorText.Get("MainWindow_EditWorkflow_016"));
                }

                if (_activeContentSnapshot is null)
                {
                    throw new InvalidOperationException(EditorText.Get("MainWindow_EditWorkflow_004"));
                }

                var preparedEdit = await editService.PrepareTrinketEditAsync(
                    profile,
                    selected.Definition,
                    copies,
                    _trinketStorage,
                    _activeContentSnapshot);
                if (previewRevision != _editRevision || CatalogTabs.SelectedIndex != 1)
                {
                    AppendStatus(EditorText.Get("MainWindow_EditWorkflow_005"), level: DiagnosticLogLevel.Warning);
                    return;
                }

                _editService = editService;
                _preparedTrinketEdit = preparedEdit;
                var preview = preparedEdit.Preview;
                PreviewSummaryTextBlock.Text =
                    EditorText.Format("MainWindow_EditWorkflow_017", preview.TrinketId, preview.ExistingCopies, preview.ResultingCopies) +
                    EditorText.Format("MainWindow_EditWorkflow_018", FormatDefinitionLimit(preview.DefinitionLimit)) +
                    EditorText.Format("MainWindow_EditWorkflow_019", preview.ExistingInventoryEntries, preview.ResultingInventoryEntries) +
                    $"{FormatStorageCapacity(preview.StorageCapacity)}";
                if (preview.ExceedsDefinitionLimit)
                {
                    PreviewWarningTextBlock.Text =
                        EditorText.Format("MainWindow_EditWorkflow_020", preview.DefinitionLimit) +
                        EditorText.Get("MainWindow_EditWorkflow_021");
                    PreviewWarningTextBlock.Visibility = Visibility.Visible;
                    AppendStatus(EditorText.Format("MainWindow_EditWorkflow_022", PreviewWarningTextBlock.Text), level: DiagnosticLogLevel.Warning);
                }
                ApplyButton.IsEnabled = true;
                AppendStatus(EditorText.Format("MainWindow_EditWorkflow_023", preparedEdit.WorkspaceDirectory));
                return;
            }

            if (HeroGrid.SelectedItem is not HeroRow selectedHero || _heroCatalog is null)
            {
                throw new InvalidOperationException(EditorText.Get("MainWindow_CatalogInteraction_001"));
            }
            if (_activeContentSnapshot is null)
            {
                throw new InvalidOperationException(EditorText.Get("MainWindow_EditWorkflow_004"));
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
                AppendStatus(EditorText.Get("MainWindow_EditWorkflow_005"), level: DiagnosticLogLevel.Warning);
                return;
            }

            _editService = editService;
            _preparedHeroEdit = preparedHeroEdit;
            _preparedHeroCandidatePreview = generated.Preview;
            var heroWarning = FormatHeroPreviewWarnings(preparedHeroEdit.Preview);
            if (string.IsNullOrWhiteSpace(heroWarning))
            {
                PreviewWarningTextBlock.Visibility = Visibility.Collapsed;
                PreviewWarningTextBlock.Text = string.Empty;
            }
            else
            {
                PreviewWarningTextBlock.Text = heroWarning;
                PreviewWarningTextBlock.Visibility = Visibility.Visible;
                AppendStatus(EditorText.Format("MainWindow_EditWorkflow_024", heroWarning.Replace(Environment.NewLine, "；", StringComparison.Ordinal)), level: DiagnosticLogLevel.Warning);
            }
            var heroPreview = generated.Preview;
            var targetStagecoach = preparedHeroEdit.Preview.TargetPool == StagecoachRecruitPool.Shard
                ? EditorText.Get("MainWindow_EditWorkflow_025")
                : EditorText.Get("MainWindow_EditWorkflow_026");
            PreviewSummaryTextBlock.Text =
                EditorText.Format("MainWindow_EditWorkflow_027", heroPreview.Name, heroPreview.HeroClass, heroPreview.ResolveLevel, heroPreview.ResolveXp) +
                EditorText.Format("MainWindow_EditWorkflow_028", heroPreview.WeaponRank, heroPreview.ArmourRank, heroPreview.CurrentHp.ToString("0.##", CultureInfo.InvariantCulture)) +
                EditorText.Format("MainWindow_EditWorkflow_029", FormatSelectedQuirks(heroPreview)) +
                EditorText.Format("MainWindow_EditWorkflow_030", heroPreview.CombatSkills.Count, heroPreview.CampingSkills.Count) +
                EditorText.Format("MainWindow_EditWorkflow_031", preparedHeroEdit.Preview.UpgradePurchaseCount) +
                $"{targetStagecoach} {preparedHeroEdit.Preview.ExistingCandidates} → {preparedHeroEdit.Preview.ResultingCandidates}，" +
                $"GUID {preparedHeroEdit.Preview.CandidateGuid}。";
            ApplyButton.IsEnabled = true;
            AppendStatus(
                EditorText.Format("MainWindow_EditWorkflow_032", targetStagecoach, heroPreview.Name, heroPreview.HeroClass, heroPreview.ResolveLevel) +
                EditorText.Format("MainWindow_EditWorkflow_033", heroPreview.ResolveXp, heroPreview.WeaponRank, heroPreview.ArmourRank) +
                EditorText.Format("MainWindow_EditWorkflow_034", string.Join(", ", heroPreview.PositiveQuirks)) +
                EditorText.Format("MainWindow_EditWorkflow_035", string.Join(", ", heroPreview.NegativeQuirks)) +
                EditorText.Format("MainWindow_EditWorkflow_036", string.Join(", ", heroPreview.Diseases)) +
                EditorText.Format("MainWindow_EditWorkflow_037", string.Join(", ", heroPreview.CombatSkills)) +
                EditorText.Format("MainWindow_EditWorkflow_038", string.Join(", ", heroPreview.CampingSkills)) +
                EditorText.Format("MainWindow_EditWorkflow_039", preparedHeroEdit.Preview.UpgradePurchaseCount));
            foreach (var warning in heroPreview.Warnings)
            {
                AppendStatus(EditorText.Format("MainWindow_EditWorkflow_040", warning), level: DiagnosticLogLevel.Warning);
            }
            AppendStatus(EditorText.Format("MainWindow_EditWorkflow_041", preparedHeroEdit.WorkspaceDirectory));
        }
        catch (Exception ex)
        {
            CrashDiagnostics.RecordException("Save edit: preview", ex,
                EditorText.Format("MainWindow_EditWorkflow_042", ProfileDirectoryTextBox.Text.Trim(), CatalogTabs.SelectedIndex));
            AppendStatusSafely(EditorText.Format("MainWindow_EditWorkflow_043", ex.Message), "Save edit: preview failure status", DiagnosticLogLevel.Error);
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
                EditorText.Format("MainWindow_EditWorkflow_044", _preparedQuantityItemEdit.Item.DisplayId) +
                EditorText.Format("MainWindow_EditWorkflow_045", _preparedQuantityItemEdit.Preview.ExistingAmount) +
                $"{_preparedQuantityItemEdit.Preview.TargetAmount}";
            definitionLimitWarningText = FormatQuantityItemBoundaryWarning(_preparedQuantityItemEdit.Item);
        }
        else if (isHeroEdit)
        {
            profileDirectory = _preparedHeroEdit!.Profile.ProfileDirectory;
            var targetStagecoach = _preparedHeroEdit.Preview.TargetPool == StagecoachRecruitPool.Shard
                ? EditorText.Get("MainWindow_EditWorkflow_025")
                : EditorText.Get("MainWindow_EditWorkflow_026");
            changeSummary =
                EditorText.Format("MainWindow_EditWorkflow_046", targetStagecoach, _preparedHeroCandidatePreview?.Name, _preparedHeroCandidatePreview?.HeroClass) +
                EditorText.Format("MainWindow_EditWorkflow_047", _preparedHeroCandidatePreview?.ResolveLevel, _preparedHeroCandidatePreview?.ResolveXp) +
                EditorText.Format("MainWindow_EditWorkflow_048", _preparedHeroCandidatePreview?.WeaponRank, _preparedHeroCandidatePreview?.ArmourRank) +
                EditorText.Format("MainWindow_EditWorkflow_049", _preparedHeroEdit.Preview.UpgradePurchaseCount) +
                $"GUID {_preparedHeroEdit.Preview.CandidateGuid}；" +
                EditorText.Format("MainWindow_EditWorkflow_050", FormatSelectedQuirks(_preparedHeroCandidatePreview));
            definitionLimitWarningText = FormatHeroPreviewWarnings(_preparedHeroEdit.Preview);
        }
        else
        {
            profileDirectory = _preparedTrinketEdit!.Profile.ProfileDirectory;
            changeSummary =
                EditorText.Format("MainWindow_EditWorkflow_051", _preparedTrinketEdit.Preview.RequestedCopies, _preparedTrinketEdit.Trinket.Id);
            definitionLimitWarningText = _preparedTrinketEdit.Preview.ExceedsDefinitionLimit
                ? EditorText.Format("MainWindow_EditWorkflow_052", _preparedTrinketEdit.Preview.ResultingCopies) +
                  EditorText.Format("MainWindow_EditWorkflow_053", _preparedTrinketEdit.Preview.DefinitionLimit)
                : string.Empty;
        }

        var operationDetails = isItemEdit
            ? SaveEditLogFormatter.Describe(_preparedQuantityItemEdit!)
            : isHeroEdit
                ? SaveEditLogFormatter.Describe(_preparedHeroEdit!, _preparedHeroCandidatePreview)
                : SaveEditLogFormatter.Describe(_preparedTrinketEdit!);
        var definitionLimitWarning = string.IsNullOrWhiteSpace(definitionLimitWarningText)
            ? string.Empty
            : EditorText.Format("MainWindow_EditWorkflow_054", definitionLimitWarningText);
        var confirmationRevision = _editRevision;
        var confirmation = ThemedDialog.Confirm(
            this,
            EditorText.Format("MainWindow_EditWorkflow_055", changeSummary) +
            $"{profileDirectory}" + definitionLimitWarning + "\n\n" +
            EditorText.Get("MainWindow_EditWorkflow_056"),
            EditorText.Get("MainWindow_EditWorkflow_057"));
        if (!confirmation)
        {
            AppendStatus(EditorText.Format("MainWindow_EditWorkflow_058", operationDetails));
            return;
        }
        if (confirmationRevision != _editRevision || _editService is null ||
            (_preparedQuantityItemEdit is null && _preparedTrinketEdit is null && _preparedHeroEdit is null))
        {
            AppendStatus(EditorText.Get("MainWindow_EditWorkflow_059"), level: DiagnosticLogLevel.Warning);
            return;
        }

        var committed = false;
        try
        {
            SetBusy(true);
            AppendStatusSafely(EditorText.Format("MainWindow_EditWorkflow_060", operationDetails), "Save edit: start diagnostics");
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

            committed = true;
            AppendStatusSafely(EditorText.Format("MainWindow_EditWorkflow_061", operationDetails, backupDirectory), "Save edit: success diagnostics");

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
                    .Select(item => item.CatalogKey.Equals(editedKey, StringComparison.Ordinal)
                        ? item with
                        {
                            CurrentAmount = targetAmount,
                            IsPresentInSave = resultingEntryCount > 0,
                            SavedEntryCount = resultingEntryCount
                        }
                        : item)
                    .ToArray();
                RefreshCatalogRowsPreservingInput(sceneChanged: false, contentChanged: false);
                UpdateCatalogMode();
            }

            ThemedDialog.ShowMessage(
                this,
                EditorText.Format("MainWindow_EditWorkflow_062", backupDirectory),
                EditorText.Get("MainWindow_EditWorkflow_063"),
                ThemedDialogKind.Information);
            InvalidatePreparedEdit();
        }
        catch (Exception ex)
        {
            var failureLabel = committed ? EditorText.Get("MainWindow_EditWorkflow_064") : EditorText.Get("MainWindow_EditWorkflow_065");
            CrashDiagnostics.RecordException("Save edit: " + (committed ? "post-commit UI" : "commit"), ex, operationDetails);
            AppendStatusSafely($"{failureLabel}：{ex.Message}；{operationDetails}", "Save edit: failure diagnostics", DiagnosticLogLevel.Error);
            ThemedDialog.ShowMessage(this, ex.Message, failureLabel, ThemedDialogKind.Error);
        }
        finally
        {
            RequestProfileSync(invalidatePreview: false);
            SetBusy(false);
        }
    }

}
