using System.Collections.ObjectModel;
using System.ComponentModel;
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
    // WPF tracks selected items by equality/hash. Event subscriptions change as
    // rows are bound or recycled, so these UI objects need stable reference identity.
    private abstract class LocalizedRow : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public void RefreshLanguage() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    private sealed class ItemRow(QuantityItemDefinition definition) : LocalizedRow
    {
        public QuantityItemDefinition Definition { get; } = definition;
        public string Id => Definition.DisplayId;
        public string ChineseName => FormatLocalizedName(Definition.LocalizedName.Chinese);
        public string EnglishName => FormatLocalizedName(Definition.LocalizedName.English);
        public string Location => FormatItemStorage(Definition.StorageKind);
        public string InventoryType => Definition.InventoryType;
        public string StackSummary => Definition.StorageKind != QuantityItemStorageKind.RaidInventory
            ? EditorText.Get("MainWindow_RowModels_001")
            : Definition.BaseStackLimit is > 0
                ? EditorText.Get("MainWindow_RowModels_002") + Definition.BaseStackLimit.Value.ToString(CultureInfo.InvariantCulture)
                : EditorText.Get("BattleEncounterSelectionDialog_017");
        public string ProvisionSummary => Definition.StorageKind != QuantityItemStorageKind.EstateItems
            ? string.Empty
            : Definition.EstateCanBeProvision switch
            {
                true => EditorText.Get("MainWindow_RowModels_003"),
                false => EditorText.Get("MainWindow_RowModels_004"),
                null => EditorText.Get("MainWindow_RowModels_005")
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
                    ? EditorText.Format("MainWindow_RowModels_006", source)
                    : source;
            }
        }
        public string DisplayName => EditorText.ContentName(Definition.LocalizedName, Definition.DisplayId);
    }

    private sealed class TrinketRow(TrinketDefinition definition) : LocalizedRow
    {
        public TrinketDefinition Definition { get; } = definition;
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

    private sealed class HeroLevelChoice(int resolveLevel, int resolveXp) : LocalizedRow
    {
        public int ResolveLevel { get; } = resolveLevel;
        public int ResolveXp { get; } = resolveXp;
        public string DisplayName => EditorText.Format("MainWindow_RowModels_007", ResolveLevel, ResolveXp);
    }

    private sealed class HeroRow(HeroClassDefinition definition) : LocalizedRow
    {
        public HeroClassDefinition Definition { get; } = definition;
        public string Id => Definition.Id;
        public string ChineseName => FormatLocalizedName(Definition.LocalizedName.Chinese);
        public string EnglishName => FormatLocalizedName(Definition.LocalizedName.English);
        public string Source => string.IsNullOrWhiteSpace(Definition.SourceLabel)
            ? Definition.Source
            : Definition.SourceLabel;
        public string GenerationMode => !Definition.GenerationAvailability.Any(level => level.CanGenerate)
            ? EditorText.Get("MainWindow_RowModels_008")
            : Definition.Generation switch
            {
                null => EditorText.Get("MainWindow_RowModels_009"),
                { IsEnabled: true } => EditorText.Get("MainWindow_RowModels_010"),
                { IsEnabled: false } => EditorText.Get("MainWindow_RowModels_011"),
                _ => EditorText.Get("MainWindow_RowModels_012")
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
                    ? EditorText.Get("MainWindow_RowModels_013")
                    : levels.Length == Definition.GenerationAvailability.Count
                        ? EditorText.Format("MainWindow_RowModels_014", levels[^1])
                        : EditorText.Format("MainWindow_RowModels_015", string.Join(",", levels));
            }
        }
        public int ColourVariationCount => Definition.ColourVariationCount;
        public string QuirkRange => Definition.Generation is not { } generation
            ? EditorText.Get("BattleEncounterSelectionDialog_017")
            : EditorText.Format("MainWindow_RowModels_016", FormatRange(generation.PositiveQuirksMin, generation.PositiveQuirksMax)) +
              EditorText.Format("MainWindow_RowModels_017", FormatRange(generation.NegativeQuirksMin, generation.NegativeQuirksMax));
        public string CombatSkillSummary =>
            EditorText.Format("MainWindow_RowModels_018", Definition.CombatSkillIds.Count, Definition.GuaranteedCombatSkillIds.Count) +
            (Definition.GuaranteedCombatSkillIds.Count > 0 ? EditorText.Get("MainWindow_RowModels_019") : " / ") +
            EditorText.Format("MainWindow_RowModels_020", Definition.SelectedCombatSkillsMax?.ToString(CultureInfo.InvariantCulture) ?? EditorText.Get("BattleEncounterSelectionDialog_017"));
        public string CampingSkillSummary => Definition.CampingSkillsComplete
            ? EditorText.Format("MainWindow_RowModels_021", Definition.ClassCampingSkillIds.Count, Definition.SharedCampingSkillIds.Count)
            : EditorText.Get("MainWindow_RowModels_022");
        public string RecruitEventSummary => string.Join(", ", Definition.RecruitEvents.Select(item => item.Id));
        public string RuntimeQuirkSummary => string.Join(
            ", ",
            Definition.RuntimeQuirkSignals
                .Select(item => item.QuirkId)
                .Distinct(StringComparer.Ordinal));

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
