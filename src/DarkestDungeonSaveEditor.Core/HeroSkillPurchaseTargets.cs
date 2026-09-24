using System.Text;

namespace DarkestDungeonSaveEditor.Core;

internal static class HeroSkillPurchaseTargets
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static IReadOnlyDictionary<string, string> Combat(string heroId, IEnumerable<string> skillIds) =>
        Build(heroId, skillIds, EditorText.Get("HeroSkillPurchaseTargets_001"), boundInputNames: true);

    public static IReadOnlyDictionary<string, string> Camping(string heroId, IEnumerable<string> skillIds) =>
        Build(heroId, skillIds, EditorText.Get("HeroSkillPurchaseTargets_002"), boundInputNames: false);

    public static IReadOnlyDictionary<string, string> Equipment(string heroId) =>
        Build(heroId, ["weapon", "armour"], EditorText.Get("HeroSkillPurchaseTargets_003"), boundInputNames: true);

    private static IReadOnlyDictionary<string, string> Build(
        string heroId, IEnumerable<string> skillIds, string label, bool boundInputNames)
    {
        var bySkill = new Dictionary<string, string>(StringComparer.Ordinal);
        var byTarget = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var skillId in skillIds.Distinct(StringComparer.Ordinal))
        {
            if (heroId.Contains('\0') || skillId.Contains('\0'))
                throw new InvalidOperationException(EditorText.Format("HeroSkillPurchaseTargets_004", label));

            string target;
            try
            {
                // Combat, camping and equipment refresh (0x1405C75F4 / 0x1405C771C)
                // format <class>.<skill> through the 64-byte buffer at 0x14036B3A0.
                // Their resource IDs and the generic save hash remain separate.
                if (boundInputNames && (StrictUtf8.GetByteCount(heroId) > 63 || StrictUtf8.GetByteCount(skillId) > 63))
                    throw new InvalidOperationException(
                        EditorText.Format("HeroSkillPurchaseTargets_005", heroId, label, skillId));
                var bytes = StrictUtf8.GetBytes($"{heroId}.{skillId}");
                target = StrictUtf8.GetString(bytes, 0, Math.Min(bytes.Length, 63));
            }
            catch (Exception error) when (error is EncoderFallbackException or DecoderFallbackException)
            {
                throw new InvalidOperationException(
                    EditorText.Format("HeroSkillPurchaseTargets_006", heroId, label, skillId), error);
            }

            if (!byTarget.TryAdd(target, skillId))
                throw new InvalidOperationException(
                    EditorText.Format("HeroSkillPurchaseTargets_007", heroId, label, byTarget[target], skillId, target));
            bySkill.Add(skillId, target);
        }
        var collisions = NativeResourceIdentity.FindCollisions(byTarget.Keys);
        if (collisions.Count > 0)
            throw new InvalidOperationException(EditorText.Format("HeroSkillPurchaseTargets_008", heroId, label, string.Join(", ", collisions)));
        return bySkill;
    }
}
