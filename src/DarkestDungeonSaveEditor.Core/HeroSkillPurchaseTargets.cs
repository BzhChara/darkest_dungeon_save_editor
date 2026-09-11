using System.Text;

namespace DarkestDungeonSaveEditor.Core;

internal static class HeroSkillPurchaseTargets
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static IReadOnlyDictionary<string, string> Combat(string heroId, IEnumerable<string> skillIds) =>
        Build(heroId, skillIds, "战斗", boundInputNames: true);

    public static IReadOnlyDictionary<string, string> Camping(string heroId, IEnumerable<string> skillIds) =>
        Build(heroId, skillIds, "露营", boundInputNames: false);

    private static IReadOnlyDictionary<string, string> Build(
        string heroId, IEnumerable<string> skillIds, string kind, bool boundInputNames)
    {
        var bySkill = new Dictionary<string, string>(StringComparer.Ordinal);
        var byTarget = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var skillId in skillIds.Distinct(StringComparer.Ordinal))
        {
            if (heroId.Contains('\0') || skillId.Contains('\0'))
                throw new InvalidOperationException($"{kind}技能购买目标的职业或技能 ID 含有 NUL，无法安全生成。");

            string target;
            try
            {
                // Both combat (0x1405C71AE / 0x1406742DE) and camping queries
                // format <class>.<skill> through the 64-byte buffer at 0x14036B3A0.
                // Their resource IDs and the generic save hash remain separate.
                if (boundInputNames && (StrictUtf8.GetByteCount(heroId) > 63 || StrictUtf8.GetByteCount(skillId) > 63))
                    throw new InvalidOperationException(
                        $"职业 '{heroId}' 的战斗技能 '{skillId}'：职业或技能 ID 超过 63 字节，尚不能安全确定原生身份。");
                var bytes = StrictUtf8.GetBytes($"{heroId}.{skillId}");
                target = StrictUtf8.GetString(bytes, 0, Math.Min(bytes.Length, 63));
            }
            catch (Exception error) when (error is EncoderFallbackException or DecoderFallbackException)
            {
                throw new InvalidOperationException(
                    $"职业 '{heroId}' 的{kind}技能 '{skillId}' 无法表示为完整 UTF-8 购买目标（原生上限 63 字节）。", error);
            }

            if (!byTarget.TryAdd(target, skillId))
                throw new InvalidOperationException(
                    $"职业 '{heroId}' 的{kind}技能 '{byTarget[target]}' 与 '{skillId}' 截断后指向同一购买目标 '{target}'，无法安全生成。");
            bySkill.Add(skillId, target);
        }
        var collisions = NativeResourceIdentity.FindCollisions(byTarget.Keys);
        if (collisions.Count > 0)
            throw new InvalidOperationException($"职业 '{heroId}' 的{kind}技能购买编号冲突：{string.Join(", ", collisions)}。");
        return bySkill;
    }
}
