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
        public string Storage => FormatItemStorage(Definition);
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
        public string GenerationMode => Definition.Generation switch
        {
            null => "生成模板缺失",
            { IsEnabled: true } => "游戏自然 / 编辑器手动",
            { IsEnabled: false } => "仅编辑器手动",
            _ => "自然状态未知 / 编辑器手动"
        };
        public bool HasProviderConflict => Definition.HasProviderConflict;
        public double? BaseHp => Definition.BaseHp;
        public string LevelSummary => string.IsNullOrWhiteSpace(Definition.ProgressionUnsupportedReason)
            ? $"0-{Math.Max(0, Definition.LevelProfiles.Count - 1)}级完整"
            : $"仅可用 {string.Join(",", Definition.LevelProfiles.Select(profile => profile.ResolveLevel))}级：" +
              Definition.ProgressionUnsupportedReason;
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
