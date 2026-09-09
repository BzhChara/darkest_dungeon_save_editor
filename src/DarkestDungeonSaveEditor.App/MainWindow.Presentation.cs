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
    private void PopulateHeroLevels(HeroClassCatalogResult catalog)
    {
        _heroLevelChoices.Clear();
        var thresholds = catalog.ResolveLevelThresholds.Count > 0
            ? catalog.ResolveLevelThresholds
            : new[] { 0 };
        for (var level = 0; level < thresholds.Count; level++)
        {
            _heroLevelChoices.Add(new HeroLevelChoice(level, thresholds[level]));
        }

        HeroLevelComboBox.SelectedIndex = _heroLevelChoices.Count > 0 ? 0 : -1;
    }

    private int GetSelectedHeroLevel()
    {
        return HeroLevelComboBox.SelectedItem is HeroLevelChoice choice
            ? choice.ResolveLevel
            : throw new InvalidOperationException("请选择要生成的人物等级。");
    }

    private void UpdateInitialQuirkSelectionSummary()
    {
        if (InitialQuirksButton is null || InitialQuirkSelectionSummaryTextBlock is null)
        {
            return;
        }

        InitialQuirksButton.Content = $"选择初始怪癖… ({_selectedInitialQuirkIds.Count})";
        if (HeroGrid is null || HeroGrid.SelectedItem is not HeroRow)
        {
            InitialQuirksButton.IsEnabled = false;
            InitialQuirkSelectionSummaryTextBlock.Text = "请先选择人物职业；默认生成空白怪癖。";
            return;
        }

        InitialQuirksButton.IsEnabled = CatalogTabs is { IsEnabled: true, SelectedIndex: 2 } &&
            _heroCatalog is not null;

        var positiveCount = 0;
        var negativeCount = 0;
        var diseaseCount = 0;
        if (_heroCatalog is not null)
        {
            foreach (var id in _selectedInitialQuirkIds)
            {
                var definition = _heroCatalog.InitialQuirks.FirstOrDefault(
                    quirk => quirk.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
                diseaseCount += definition?.IsDisease == true ? 1 : 0;
                positiveCount += definition is { IsDisease: false, IsPositive: true } ? 1 : 0;
                negativeCount += definition is { IsDisease: false, IsPositive: false } ? 1 : 0;
            }
        }

        InitialQuirkSelectionSummaryTextBlock.Text =
            $"已选 +{positiveCount}/-{negativeCount}/疾病 {diseaseCount}：" +
            FormatSelectedQuirks(_selectedInitialQuirkIds);
    }

    private static string FormatSelectedQuirks(StagecoachHeroCandidatePreview? preview)
    {
        return preview is null
            ? "未知"
            : FormatSelectedQuirks(
                preview.PositiveQuirks.Concat(preview.NegativeQuirks).Concat(preview.Diseases));
    }

    private static string FormatSelectedQuirks(IEnumerable<string> quirkIds)
    {
        var ids = quirkIds.ToArray();
        return ids.Length == 0 ? "空白" : string.Join(", ", ids);
    }

    private static string FormatHeroPreviewWarnings(StagecoachHeroMutationPreview preview)
    {
        var warnings = new List<string>();
        if (preview.MayRefreshOnTownReturn)
        {
            warnings.Add("当前档案仍在副本中；回城过周刷新马车时，新人物可能被清除。建议回到小镇后再生成。");
        }

        var quirkLimitWarnings = FormatHeroQuirkLimitWarnings(preview);
        if (!string.IsNullOrWhiteSpace(quirkLimitWarnings))
        {
            warnings.Add(quirkLimitWarnings);
        }

        return string.Join(Environment.NewLine, warnings);
    }

    private static string FormatHeroQuirkLimitWarnings(StagecoachHeroMutationPreview preview)
    {
        return string.Join(
            Environment.NewLine,
            preview.QuirkLimits
                .Where(limit => limit.ExceedsDefinitionLimit)
                .Select(limit =>
                    $"怪癖 {limit.QuirkId}（singleton）：当前 roster {limit.ExistingRosterHeroes} 名、" +
                    $"全部马车池 {limit.ExistingStagecoachCandidates} 名；写入后合计 {limit.ResultingHeroes} 名，" +
                    $"超过定义上限 {limit.DefinitionLimit}。编辑器会按控制台模式保留写入能力。"));
    }

    private static string FormatDefinitionLimit(int? limit)
    {
        return limit switch
        {
            0 => "无限",
            null => "未知",
            _ => limit.Value.ToString(CultureInfo.InvariantCulture)
        };
    }

    private static string FormatItemStorage(QuantityItemStorageKind storageKind) => storageKind switch
    {
        QuantityItemStorageKind.Wallet => "钱包",
        QuantityItemStorageKind.EstateItems => "庄园物品",
        QuantityItemStorageKind.RaidInventory => "当前副本背包",
        _ => storageKind.ToString()
    };

    private static string FormatItemStorage(QuantityItemDefinition definition)
    {
        if (definition.StorageKind == QuantityItemStorageKind.RaidInventory)
        {
            return definition.BaseStackLimit is > 0
                ? $"背包 / {definition.InventoryType} / 每格 " +
                  definition.BaseStackLimit.Value.ToString(CultureInfo.InvariantCulture)
                : $"背包 / {definition.InventoryType} / 堆叠未知";
        }

        if (definition.StorageKind != QuantityItemStorageKind.EstateItems)
        {
            return FormatItemStorage(definition.StorageKind);
        }

        return definition.EstateCanBeProvision switch
        {
            true => "庄园库存 / 可手动配给",
            false => "庄园库存 / 不可手动配给",
            null => "庄园库存 / 手动配给未声明"
        };
    }

    private static string FormatQuantityItemBoundaryWarning(QuantityItemDefinition definition)
    {
        if (definition.StorageKind == QuantityItemStorageKind.RaidInventory)
        {
            return definition.InventoryType.Equals("quest_item", StringComparison.Ordinal)
                ? "任务物品可能影响当前任务目标，请确认所选 ID 与当前副本相符。"
                : string.Empty;
        }

        if (definition.StorageKind != QuantityItemStorageKind.EstateItems)
        {
            return string.Empty;
        }

        return definition.EstateCanBeProvision switch
        {
            false => "该庄园库存不可手动配给；人物自带或副本中生成的数量不受本次修改影响。",
            null => "这里只修改小镇庄园库存；是否可手动配给未声明，且不会直接修改副本背包。",
            true => string.Empty
        };
    }

    private static string FormatStorageCapacity(TrinketStorageDefinition? storage)
    {
        return storage is null
            ? "未知"
            : $"{storage.MaxSlots.ToString(CultureInfo.InvariantCulture)}（{storage.ContentSource.DisplayName}）";
    }

    private static string FormatStorageCapacity(int? capacity)
    {
        return capacity?.ToString(CultureInfo.InvariantCulture) ?? "未知";
    }

    private static string FormatRaidInventoryCapacity(RaidInventoryStorageDefinition? storage) =>
        storage?.MaxSlots.ToString(CultureInfo.InvariantCulture) ?? "未知";

    private static string FormatQuantitySaveContext(QuantityItemSaveContext saveContext) =>
        saveContext == QuantityItemSaveContext.Raid ? "副本背包" : "小镇庄园";

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string RequireDirectory(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value) || !Directory.Exists(value.Trim()))
        {
            throw new DirectoryNotFoundException($"{label}不存在：{value}");
        }

        return Path.GetFullPath(value.Trim());
    }

}
