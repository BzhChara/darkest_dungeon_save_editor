using System.Globalization;
using System.Text.Json.Nodes;

namespace DarkestDungeonSaveEditor.Core;

public static partial class StagecoachHeroCandidateFactory
{
    private static double GetValidatedInitialCurrentHp(
        string heroClassId,
        double baseHp,
        IEnumerable<HeroInitialQuirkDefinition> selectedQuirks)
    {
        if (!double.IsFinite(baseHp) || baseHp <= 0.0)
        {
            throw new InvalidOperationException($"职业 '{heroClassId}' 的基础生命无效：{baseHp}。");
        }

        var modifiers = selectedQuirks
            .SelectMany(quirk => quirk.MaxHpModifiers)
            .ToArray();
        ValidateAllReachableMaxHpStates(heroClassId, baseHp, modifiers);

        var generationModifiers = modifiers
            .Where(IsActiveAtGeneration)
            .ToArray();
        var flatTotal = generationModifiers
            .Where(modifier => modifier.Kind == HeroMaxHpModifierKind.Flat)
            .Sum(modifier => modifier.Amount);
        var percentageTotal = generationModifiers
            .Where(modifier => modifier.Kind == HeroMaxHpModifierKind.Percentage)
            .Sum(modifier => modifier.Amount);
        return ValidateMaxHpState(heroClassId, baseHp, flatTotal, percentageTotal, generationModifiers);
    }

    private static void ValidateAllReachableMaxHpStates(
        string heroClassId,
        double baseHp,
        IReadOnlyList<HeroMaxHpModifier> modifiers)
    {
        var trinketStates = modifiers.Any(modifier =>
                modifier.RuleType.Equals("no_trinkets", StringComparison.OrdinalIgnoreCase))
            ? new[] { false, true }
            : new[] { false };
        var afflictedStates = modifiers.Any(modifier =>
                modifier.RuleType.Equals("afflicted", StringComparison.OrdinalIgnoreCase))
            ? new[] { false, true }
            : new[] { false };
        var modes = BuildReachableModes(modifiers);
        var lightLevels = BuildReachableLightLevels(modifiers);
        var validatedStates = new HashSet<string>(StringComparer.Ordinal);

        foreach (var hasTrinkets in trinketStates)
        {
            foreach (var isAfflicted in afflictedStates)
            {
                foreach (var mode in modes)
                {
                    foreach (var lightLevel in lightLevels)
                    {
                        var active = modifiers
                            .Where(modifier => IsActiveAtRuntime(
                                modifier,
                                hasTrinkets,
                                isAfflicted,
                                mode,
                                lightLevel))
                            .ToArray();
                        var stateKey = string.Join('\u001f', active
                            .Select(modifier => modifier.BuffId)
                            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase));
                        if (!validatedStates.Add(stateKey))
                        {
                            continue;
                        }

                        var flatTotal = active
                            .Where(modifier => modifier.Kind == HeroMaxHpModifierKind.Flat)
                            .Sum(modifier => modifier.Amount);
                        var percentageTotal = active
                            .Where(modifier => modifier.Kind == HeroMaxHpModifierKind.Percentage)
                            .Sum(modifier => modifier.Amount);
                        _ = ValidateMaxHpState(
                            heroClassId,
                            baseHp,
                            flatTotal,
                            percentageTotal,
                            active);
                    }
                }
            }
        }
    }

    private static IReadOnlyList<string?> BuildReachableModes(
        IEnumerable<HeroMaxHpModifier> modifiers)
    {
        var declaredModes = modifiers
            .Where(modifier => modifier.RuleType.Equals("in_mode", StringComparison.OrdinalIgnoreCase))
            .Select(modifier => modifier.RuleString)
            .Where(mode => !string.IsNullOrWhiteSpace(mode))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (declaredModes.Count == 0)
        {
            return new string?[] { null };
        }

        var otherMode = "__save_editor_other_mode__";
        while (declaredModes.Contains(otherMode, StringComparer.OrdinalIgnoreCase))
        {
            otherMode += "_";
        }

        return new string?[] { null }
            .Concat(declaredModes)
            .Append(otherMode)
            .ToArray();
    }

    private static IReadOnlyList<double?> BuildReachableLightLevels(
        IEnumerable<HeroMaxHpModifier> modifiers)
    {
        var thresholds = modifiers
            .Where(modifier => modifier.RuleType.Equals("lightabove", StringComparison.OrdinalIgnoreCase))
            .Select(modifier => modifier.RuleFloat)
            .Where(value => value is not null)
            .Select(value => value!.Value)
            .Distinct()
            .ToArray();
        if (thresholds.Length == 0)
        {
            return new double?[] { null };
        }

        var levels = new HashSet<double> { 0.0 };
        foreach (var threshold in thresholds)
        {
            levels.Add(threshold);
            var below = Math.BitDecrement(threshold);
            var above = Math.BitIncrement(threshold);
            if (double.IsFinite(below))
            {
                levels.Add(below);
            }
            if (double.IsFinite(above))
            {
                levels.Add(above);
            }
        }

        return new double?[] { null }
            .Concat(levels.Order().Select(level => (double?)level))
            .ToArray();
    }

    private static bool IsActiveAtGeneration(HeroMaxHpModifier modifier)
    {
        var rawCondition = modifier.RuleType.ToLowerInvariant() switch
        {
            "always" => true,
            "no_trinkets" => true,
            _ => false
        };
        return rawCondition && !modifier.IsFalseRule;
    }

    private static bool IsActiveAtRuntime(
        HeroMaxHpModifier modifier,
        bool hasTrinkets,
        bool isAfflicted,
        string? mode,
        double? lightLevel)
    {
        bool? rawCondition = modifier.RuleType.ToLowerInvariant() switch
        {
            "always" => true,
            "no_trinkets" => !hasTrinkets,
            "afflicted" => isAfflicted,
            "in_mode" when mode is not null => mode.Equals(
                modifier.RuleString,
                StringComparison.OrdinalIgnoreCase),
            "lightabove" when lightLevel is not null && modifier.RuleFloat is { } threshold =>
                lightLevel.Value > threshold,
            _ => null
        };
        return rawCondition is { } condition && condition != modifier.IsFalseRule;
    }

    private static double ValidateMaxHpState(
        string heroClassId,
        double baseHp,
        double flatTotal,
        double percentageTotal,
        IReadOnlyCollection<HeroMaxHpModifier> activeModifiers)
    {
        var additiveResult = baseHp + flatTotal;
        var multiplier = 1.0 + percentageTotal;
        var currentHp = additiveResult * multiplier;

        if (!double.IsFinite(flatTotal) ||
            !double.IsFinite(percentageTotal) ||
            !double.IsFinite(additiveResult) ||
            !double.IsFinite(multiplier) ||
            !double.IsFinite(currentHp) ||
            additiveResult <= HpSafetyTolerance ||
            multiplier <= HpSafetyTolerance ||
            currentHp <= HpSafetyTolerance)
        {
            var activeBuffs = activeModifiers.Count == 0
                ? "无"
                : string.Join(", ", activeModifiers.Select(modifier => modifier.BuffId));
            throw new InvalidOperationException(
                $"职业 '{heroClassId}' 的初始怪癖合计 HP 修正无效：存在可达条件会使生命不大于 0；" +
                $"基础 {baseHp}，固定 {flatTotal:+0.###;-0.###;0}，" +
                $"百分比 {percentageTotal:+0.###%;-0.###%;0%}，活动 Buff：{activeBuffs}。");
        }

        return currentHp;
    }

}
