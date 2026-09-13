using System.Globalization;
using System.Text.Json.Nodes;

namespace DarkestDungeonSaveEditor.Core;

public static partial class StagecoachHeroCandidateFactory
{
    private static IReadOnlyList<HeroInitialQuirkDefinition> ResolveSelectedQuirks(
        HeroClassCatalogResult catalog,
        IReadOnlyCollection<string> selectedIds)
    {
        var uniqueIds = new HashSet<string>(StringComparer.Ordinal);
        var selected = new List<HeroInitialQuirkDefinition>(selectedIds.Count);
        foreach (var rawId in selectedIds)
        {
            var id = rawId;
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new InvalidOperationException("初始怪癖 ID 不能为空。");
            }
            if (!uniqueIds.Add(id))
            {
                throw new InvalidOperationException($"初始怪癖 '{id}' 被重复选择。");
            }

            var matches = catalog.InitialQuirks
                .Where(quirk => quirk.Id.Equals(id, StringComparison.Ordinal))
                .ToArray();
            if (matches.Length != 1)
            {
                throw new InvalidOperationException(
                    matches.Length == 0
                        ? $"初始怪癖 '{id}' 不在当前活动内容目录中。"
                        : $"初始怪癖 '{id}' 有多个未解析定义，不能安全创建。");
            }

            var quirk = matches[0];
            if (quirk.IsPositive is null)
            {
                throw new InvalidOperationException($"初始怪癖 '{quirk.Id}' 没有明确的正负类型。");
            }
            var canWriteWithPreviewLimitCheck =
                quirk.WriteStatus == HeroInitialQuirkWriteStatus.RequiresSaveContext &&
                quirk.DefinitionLimit is > 0;
            if (quirk.WriteStatus != HeroInitialQuirkWriteStatus.Direct &&
                !canWriteWithPreviewLimitCheck)
            {
                throw new InvalidOperationException(
                    $"初始怪癖 '{quirk.Id}' 当前不能显式写入：{quirk.WriteStatusReason}");
            }
            selected.Add(quirk);
        }

        // Native positive and disease predicates overlap (0x1404E0D50 / 0x1404E0BF0).
        var positiveCount = selected.Count(quirk => quirk.IsPositive == true);
        var negativeCount = selected.Count(quirk => !quirk.IsDisease && quirk.IsPositive == false);
        var diseaseCount = selected.Count(quirk => quirk.IsDisease);
        var limits = catalog.InitialQuirkLimits;
        if ((positiveCount > 0 && limits.Positive is null) ||
            (negativeCount > 0 && limits.Negative is null) ||
            (diseaseCount > 0 && limits.Diseases is null))
            throw new InvalidOperationException("活动 shared 规则中的怪癖上限无法确定，不能添加对应类别的初始怪癖。");
        if (positiveCount > limits.Positive || negativeCount > limits.Negative || diseaseCount > limits.Diseases)
        {
            throw new InvalidOperationException(
                $"初始怪癖最多正面 {limits.Positive?.ToString(CultureInfo.InvariantCulture) ?? "未知"} 个、" +
                $"负面 {limits.Negative?.ToString(CultureInfo.InvariantCulture) ?? "未知"} 个、" +
                $"疾病 {limits.Diseases?.ToString(CultureInfo.InvariantCulture) ?? "未知"} 个；" +
                $"当前选择为 +{positiveCount}/-{negativeCount}/疾病 {diseaseCount}。");
        }

        for (var leftIndex = 0; leftIndex < selected.Count; leftIndex++)
        {
            for (var rightIndex = leftIndex + 1; rightIndex < selected.Count; rightIndex++)
            {
                var left = selected[leftIndex];
                var right = selected[rightIndex];
                if (left.IncompatibleQuirkIds.Contains(right.Id, StringComparer.Ordinal) ||
                    right.IncompatibleQuirkIds.Contains(left.Id, StringComparer.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"初始怪癖 '{left.Id}' 与 '{right.Id}' 互斥，不能同时选择。");
                }
            }
        }

        return selected;
    }

}
