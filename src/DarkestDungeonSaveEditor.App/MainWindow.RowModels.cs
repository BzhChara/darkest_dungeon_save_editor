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
    private sealed record ItemRow(QuantityItemDefinition Definition)
    {
        public string Id => Definition.DisplayId;
        public string ChineseName => FormatLocalizedName(Definition.LocalizedName.Chinese);
        public string EnglishName => FormatLocalizedName(Definition.LocalizedName.English);
        public string Location => FormatItemStorage(Definition.StorageKind);
        public string InventoryType => Definition.InventoryType;
        public string StackSummary => Definition.StorageKind != QuantityItemStorageKind.RaidInventory
            ? "不适用"
            : Definition.BaseStackLimit is > 0
                ? "每格 " + Definition.BaseStackLimit.Value.ToString(CultureInfo.InvariantCulture)
                : "未知";
        public string ProvisionSummary => Definition.StorageKind != QuantityItemStorageKind.EstateItems
            ? string.Empty
            : Definition.EstateCanBeProvision switch
            {
                true => "配给：可手动配给",
                false => "配给：不可手动配给",
                null => "配给：未声明"
            };
        public int CurrentAmount => Definition.CurrentAmount;
        public string Source
        {
            get
            {
                var source = string.IsNullOrWhiteSpace(Definition.SourceLabel)
                    ? Definition.Source
                    : Definition.SourceLabel;
                return Definition.IsPresentInSave &&
                       Definition.ReferenceStatus == QuantityItemReferenceStatus.SuspectedUnused
                    ? $"{source}（存档残留）"
                    : source;
            }
        }
        public string DisplayName => !string.IsNullOrWhiteSpace(Definition.LocalizedName.Chinese)
            ? Definition.LocalizedName.Chinese
            : !string.IsNullOrWhiteSpace(Definition.LocalizedName.English)
                ? Definition.LocalizedName.English
                : Definition.DisplayId;
    }

    private sealed record TrinketRow(TrinketDefinition Definition)
    {
        public string Id => Definition.Id;
        public string ChineseName => FormatLocalizedName(Definition.LocalizedName.Chinese);
        public string EnglishName => FormatLocalizedName(Definition.LocalizedName.English);
        public string Rarity => Definition.Rarity;
        public string Source => string.IsNullOrWhiteSpace(Definition.SourceLabel)
            ? Definition.Source
            : Definition.SourceLabel;
        public string LimitDisplay => FormatDefinitionLimit(Definition.Limit);
        public bool IsStateful => Definition.IsStateful;
        public bool HasProviderConflict => Definition.HasProviderConflict;
        public string StatefulFieldSummary => string.Join(", ", Definition.StatefulFields);
    }

    private sealed record HeroLevelChoice(int ResolveLevel, int ResolveXp)
    {
        public string DisplayName => $"{ResolveLevel}级 / XP {ResolveXp}";
    }

    private sealed record HeroRow(HeroClassDefinition Definition)
    {
        public string Id => Definition.Id;
        public string ChineseName => FormatLocalizedName(Definition.LocalizedName.Chinese);
        public string EnglishName => FormatLocalizedName(Definition.LocalizedName.English);
        public string Source => string.IsNullOrWhiteSpace(Definition.SourceLabel)
            ? Definition.Source
            : Definition.SourceLabel;
        public string GenerationMode => !Definition.GenerationAvailability.Any(level => level.CanGenerate)
            ? "暂不可生成"
            : Definition.Generation switch
            {
                null => "生成模板缺失",
                { IsEnabled: true } => "普通招募开启 / 编辑器手动",
                { IsEnabled: false } => "普通招募关闭 / 编辑器手动",
                _ => "自然状态未知 / 编辑器手动"
            };
        public bool HasProviderConflict => Definition.HasProviderConflict;
        public double? BaseHp => Definition.BaseHp;
        public string LevelSummary
        {
            get
            {
                var levels = Definition.GenerationAvailability.Where(level => level.CanGenerate)
                    .Select(level => level.ResolveLevel).ToArray();
                return levels.Length == 0
                    ? "不可生成"
                    : levels.Length == Definition.GenerationAvailability.Count
                        ? $"0-{levels[^1]}级可生成"
                        : $"可生成 {string.Join(",", levels)} 级";
            }
        }
        public int ColourVariationCount => Definition.ColourVariationCount;
        public string QuirkRange => Definition.Generation is not { } generation
            ? "未知"
            : $"正面 {FormatRange(generation.PositiveQuirksMin, generation.PositiveQuirksMax)} / " +
              $"负面 {FormatRange(generation.NegativeQuirksMin, generation.NegativeQuirksMax)}";
        public string CombatSkillSummary =>
            $"定义 {Definition.CombatSkillIds.Count} / 必选 {Definition.GuaranteedCombatSkillIds.Count} / " +
            $"选择上限 {Definition.SelectedCombatSkillsMax?.ToString(CultureInfo.InvariantCulture) ?? "未知"}";
        public string CampingSkillSummary =>
            $"职业 {Definition.ClassCampingSkillIds.Count} / 共享 {Definition.SharedCampingSkillIds.Count}";
        public string RecruitEventSummary => string.Join(", ", Definition.RecruitEvents.Select(item => item.Id));
        public string RuntimeQuirkSummary => string.Join(
            ", ",
            Definition.RuntimeQuirkSignals
                .Select(item => item.QuirkId)
                .Distinct(StringComparer.OrdinalIgnoreCase));

        private static string FormatRange(int? minimum, int? maximum)
        {
            if (minimum is null && maximum is null)
            {
                return "?";
            }

            return minimum == maximum || maximum is null
                ? minimum?.ToString(CultureInfo.InvariantCulture) ?? maximum!.Value.ToString(CultureInfo.InvariantCulture)
                : $"{minimum?.ToString(CultureInfo.InvariantCulture) ?? "?"}–{maximum.Value.ToString(CultureInfo.InvariantCulture)}";
        }
    }

    private static string FormatLocalizedName(string value) =>
        string.IsNullOrWhiteSpace(value) ? "—" : value;
}
