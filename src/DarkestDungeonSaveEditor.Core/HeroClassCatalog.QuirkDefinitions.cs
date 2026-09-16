using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;

namespace DarkestDungeonSaveEditor.Core;

public static partial class HeroClassCatalog
{
    // ActorCombatStat enum table, x64 build 27890 (0x140068E60).
    // Only combat_stat_* uses this subtype table; other Buff families have
    // different subtypes and must not be rejected by this check.
    private static readonly HashSet<uint> CombatStatHashes = new[]
    {
        "damage_low", "damage_high", "defense_rating", "protection_rating", "speed_rating",
        "attack_rating", "crit_chance", "max_hp", "riposte_on_hit_chance", "riposte_on_miss_chance"
    }.Select(Loc2LocalizationReader.HashName).ToHashSet();
    private static readonly uint MaxHpStatHash = Loc2LocalizationReader.HashName("max_hp");
    private static readonly IReadOnlyDictionary<uint, string> HpBuffStatTypes = new[]
        { "combat_stat_add", "combat_stat_multiply" }.ToDictionary(Loc2LocalizationReader.HashName);
    private static readonly IReadOnlyDictionary<uint, string> HpBuffRuleTypes = new[]
        { "always", "no_trinkets", "afflicted", "in_mode", "lightabove" }.ToDictionary(Loc2LocalizationReader.HashName);

    // Resolve after the native 63-byte/C-string read. Downstream HP conditions
    // consume canonical names; unknown enum hashes retain the existing guards.
    private static string ResolveBuffEnum(string value, IReadOnlyDictionary<uint, string> names) =>
        names.TryGetValue(Loc2LocalizationReader.HashName(value), out var name) ? name : value;

    private static HeroInitialQuirkDefinition BuildInitialQuirk(
        QuirkDefinition quirk,
        IReadOnlyDictionary<string, QuirkDefinition> effectiveQuirks,
        IReadOnlyDictionary<string, BuffDefinition> effectiveBuffs,
        IReadOnlyDictionary<string, List<BuffDefinition>> buffCandidates)
    {
        var contextReasons = new List<string>();
        var unverifiedReasons = new List<string>();
        var definitionLimits = new List<int>();
        if (!effectiveQuirks.ContainsKey(quirk.Id))
            unverifiedReasons.Add("怪癖 ID 或定义未能唯一解析，不能确定游戏实际采用的属性");
        var visitedEvolutionIds = new HashSet<string>(StringComparer.Ordinal) { quirk.Id };
        var evolutionStep = quirk;
        while (evolutionStep.Evolution?.TargetQuirkId is { } targetId)
        {
            if (!effectiveQuirks.TryGetValue(targetId, out var target))
            {
                unverifiedReasons.Add($"进化目标 '{targetId}' 缺失或定义未能唯一解析（来自 '{evolutionStep.Id}'）");
                break;
            }
            if (target.Evolution is { ValidationErrors.Count: > 0 })
            {
                unverifiedReasons.Add($"进化链目标 '{targetId}' 的进化配置无效：{string.Join("；", target.Evolution.ValidationErrors)}");
                break;
            }
            // Some authored evolutions cycle; an existing, valid cycle is not a missing target.
            if (!visitedEvolutionIds.Add(targetId))
                break;
            evolutionStep = target;
        }

        if (quirk.IsPositive is null)
        {
            unverifiedReasons.Add("怪癖缺少明确的正负类型");
        }

        if (quirk.Tags.Contains("singleton"))
        {
            definitionLimits.Add(1);
            contextReasons.Add("singleton 定义上限 1；预览统计 roster 与全部马车池，超限仅警告");
        }

        int? definitionLimit = definitionLimits.Count == 0 ? null : definitionLimits.Min();

        var referencedBuffs = new List<BuffDefinition>();
        foreach (var buffId in quirk.BuffIds)
        {
            if (!effectiveBuffs.TryGetValue(buffId, out var buff))
            {
                if (!buffCandidates.TryGetValue(buffId, out var unresolved) || unresolved.Count == 0)
                {
                    unverifiedReasons.Add($"Buff '{buffId}' 缺失，无法排除 max_hp 修正");
                }
                else
                {
                    // A colliding native key may resolve to a different Buff whose stat
                    // is HP, even when this string's own candidates are all non-HP.
                    unverifiedReasons.Add($"Buff '{buffId}' 定义未解析，无法排除 max_hp 修正");
                }

                continue;
            }

            if (buff.HasInvalidNativeString ||
                (buff.StatType is "combat_stat_add" or "combat_stat_multiply" &&
                 !CombatStatHashes.Contains(Loc2LocalizationReader.HashName(buff.StatSubType))))
            {
                unverifiedReasons.Add($"Buff '{buff.Id}' 的原生属性或条件字符串无法确认，不能可靠计算 max_hp");
                continue;
            }
            referencedBuffs.Add(buff);
        }

        var hpBuffs = referencedBuffs
            .Where(buff => Loc2LocalizationReader.HashName(buff.StatSubType) == MaxHpStatHash)
            .ToArray();
        var maxHpModifiers = new List<HeroMaxHpModifier>(hpBuffs.Length);
        foreach (var buff in hpBuffs)
        {
            var modifierKind = buff.StatType switch
            {
                "combat_stat_add" => HeroMaxHpModifierKind.Flat,
                "combat_stat_multiply" => HeroMaxHpModifierKind.Percentage,
                _ => (HeroMaxHpModifierKind?)null
            };
            var hasValidRuleData = buff.RuleType switch
            {
                "always" or "no_trinkets" or "afflicted" => true,
                "in_mode" => buff.RuleString.Length > 0,
                "lightabove" => buff.RuleFloat is { } threshold && double.IsFinite(threshold),
                _ => false
            };
            if (modifierKind is null ||
                buff.Amount is not { } amount ||
                !double.IsFinite(amount) ||
                buff.IsFalseRule is null ||
                !hasValidRuleData)
            {
                unverifiedReasons.Add($"max_hp Buff '{buff.Id}' 使用了尚未验证的规则");
            }
            else
            {
                maxHpModifiers.Add(new HeroMaxHpModifier(
                    buff.Id,
                    modifierKind.Value,
                    amount,
                    buff.RuleType,
                    buff.IsFalseRule.Value,
                    buff.RuleFloat,
                    buff.RuleString));
            }
        }

        HeroQuirkEvolutionDefinition? evolution = null;
        if (quirk.Evolution is { } parsedEvolution)
        {
            unverifiedReasons.AddRange(parsedEvolution.ValidationErrors);
            if (parsedEvolution.ValidationErrors.Count == 0 &&
                parsedEvolution.DurationMin is { } durationMin &&
                parsedEvolution.DurationMax is { } durationMax)
            {
                evolution = new HeroQuirkEvolutionDefinition(
                    durationMin,
                    durationMax,
                    parsedEvolution.TownProgressionDurationChange,
                    parsedEvolution.TargetQuirkId,
                    parsedEvolution.CausesDeath,
                    parsedEvolution.TownAttemptUseItemDurationThreshold);
            }
        }

        var kind = quirk.IsDisease
            ? HeroInitialQuirkKind.Disease
            : quirk.RandomChance is > 0 and < double.PositiveInfinity
                ? HeroInitialQuirkKind.Natural
                : HeroInitialQuirkKind.Special;
        var writeStatus = unverifiedReasons.Count > 0
            ? HeroInitialQuirkWriteStatus.Unverified
            : contextReasons.Count > 0
                ? HeroInitialQuirkWriteStatus.RequiresSaveContext
                : HeroInitialQuirkWriteStatus.Direct;
        var writeStatusReason = string.Join(
            "；",
            unverifiedReasons.Concat(contextReasons));

        return new HeroInitialQuirkDefinition(
            quirk.Id,
            quirk.IsPositive,
            quirk.RandomChance,
            quirk.IsDisease,
            evolution,
            quirk.IncompatibleQuirks,
            maxHpModifiers.ToArray(),
            kind,
            kind == HeroInitialQuirkKind.Natural && writeStatus == HeroInitialQuirkWriteStatus.Direct,
            writeStatus,
            writeStatusReason,
            quirk.Source,
            quirk.SourcePath,
            quirk.AllSources)
        {
            DefinitionLimit = definitionLimit
        };
    }

    private static HeroRuntimeQuirkSignal? ResolveRuntimeQuirk(
        SkillEffectReference reference,
        IReadOnlyDictionary<string, EffectQuirkAssignment> effectiveEffects,
        IReadOnlyDictionary<string, QuirkDefinition> effectiveQuirks)
    {
        if (!effectiveEffects.TryGetValue(reference.EffectName, out var effect) ||
            string.IsNullOrWhiteSpace(effect.QuirkId) ||
            !effectiveQuirks.TryGetValue(effect.QuirkId, out var quirk) ||
            !quirk.Id.Equals(effect.QuirkId, StringComparison.Ordinal) ||
            quirk.RandomChance is null or > 0)
        {
            return null;
        }

        var source = effect.Source.Equals(quirk.Source, StringComparison.OrdinalIgnoreCase)
            ? effect.Source
            : $"{effect.Source} -> {quirk.Source}";
        return new HeroRuntimeQuirkSignal(
            quirk.Id,
            reference.EffectName,
            reference.SkillId,
            quirk.IsPositive,
            source);
    }

    private static IEnumerable<EffectQuirkAssignment> ReadEffectAssignments(string path, string source)
    {
        foreach (var (kind, body) in NativeDarkestReader.ReadRecords(path))
        {
            if (kind != "effect") continue;
            var name = NativeDarkestReader.ReadString(body, ".name");
            var quirkId = NativeDarkestReader.ReadString(body, ".disease");
            // The native Effect object survives duplicate declarations:
            // omitted disease retains the old string; explicit "" clears it.
            // Keep even non-disease entries for native identity validation.
            if (!string.IsNullOrWhiteSpace(name))
            {
                yield return new EffectQuirkAssignment(
                    name,
                    quirkId,
                    source,
                    Path.GetFullPath(path));
            }
        }
    }

    private static IEnumerable<QuirkDefinition> ReadQuirkDefinitions(
        string path,
        string source,
        IReadOnlyList<string> providerSources)
    {
        using var document = JsonDocument.Parse(
            File.ReadAllBytes(path),
            new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });
        if (!NativeJsonReader.TryGetProperty(document.RootElement, "quirks", out var quirks) ||
            quirks.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var item in quirks.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !NativeJsonReader.TryGetProperty(item, "id", out var idNode) ||
                idNode.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(idNode.GetString()))
            {
                continue;
            }

            // Native probability storage is float32 (0x1404DE5E7). Overflow is
            // unresolved, not zero, and must not put infinity into catalog JSON.
            var randomChance = ReadJsonFloat(item, "random_chance");
            if (randomChance is { } chance && !double.IsFinite(chance))
                randomChance = null;

            bool? isPositive = null;
            if (NativeJsonReader.TryGetProperty(item, "is_positive", out var positiveNode) &&
                positiveNode.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                isPositive = positiveNode.GetBoolean();
            }

            var evolution = ReadEvolutionDefinition(item);

            yield return new QuirkDefinition(
                idNode.GetString()!,
                randomChance,
                isPositive,
                ReadJsonBoolean(item, "is_disease") == true,
                ReadJsonStringArray(item, "incompatible_quirks"),
                ReadJsonStringList(item, "buffs"),
                ReadJsonStringArray(item, "tags"),
                ReadJsonInt(item, "roster_limit"),
                evolution,
                source,
                Path.GetFullPath(path),
                providerSources);
        }
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> ReadProviderSourcesByQuirkId(
        EffectiveContentFile file)
    {
        var sourcesById = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var provider in file.Providers)
        {
            try
            {
                foreach (var quirk in ReadQuirkDefinitions(
                             provider.Path,
                             provider.SourceId,
                             [provider.SourceId]))
                {
                    if (!sourcesById.TryGetValue(quirk.Id, out var sources))
                    {
                        sources = [];
                        sourcesById[quirk.Id] = sources;
                    }

                    if (!sources.Contains(provider.SourceId, StringComparer.OrdinalIgnoreCase))
                    {
                        sources.Add(provider.SourceId);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                // Provenance is optional evidence. The effective file is parsed separately;
                // an unreadable overridden provider must not make a winning Mod-only entry
                // inherit an unverified official origin.
            }
        }

        return sourcesById.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)pair.Value.ToArray(),
            StringComparer.Ordinal);
    }

    private static IEnumerable<BuffDefinition> ReadBuffDefinitions(string path, string source)
    {
        using var document = JsonDocument.Parse(
            File.ReadAllBytes(path),
            new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });
        if (!NativeJsonReader.TryGetProperty(document.RootElement, "buffs", out var buffs) ||
            buffs.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var item in buffs.EnumerateArray())
        {
            var id = ReadJsonIdentity(item, "id");
            if (item.ValueKind != JsonValueKind.Object || string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            var hasRuleData = NativeJsonReader.TryGetProperty(item, "rule_data", out var ruleData) &&
                              ruleData.ValueKind == JsonValueKind.Object;
            var invalidString = false;

            yield return new BuffDefinition(
                id,
                ResolveBuffEnum(ReadNativeBuffString(item, "stat_type", ref invalidString), HpBuffStatTypes),
                ReadNativeBuffString(item, "stat_sub_type", ref invalidString),
                ReadJsonFloat(item, "amount"),
                ResolveBuffEnum(ReadNativeBuffString(item, "rule_type", ref invalidString), HpBuffRuleTypes),
                ReadJsonBoolean(item, "is_false_rule"),
                hasRuleData ? ReadJsonFloat(ruleData, "float") : null,
                hasRuleData ? ReadNativeBuffString(ruleData, "string", ref invalidString) : string.Empty,
                source,
                Path.GetFullPath(path)) { HasInvalidNativeString = invalidString };
        }
    }

    private static double? ReadJsonFloat(JsonElement element, string propertyName) =>
        ReadJsonDouble(element, propertyName) is { } value ? (double)(float)value : null;

    private static string ReadNativeBuffString(JsonElement element, string propertyName, ref bool invalid)
    {
        // Buff enum names and rule strings are copied to 64-byte native buffers.
        // Preserve case/whitespace, stop at NUL and never substitute a replacement
        // character when truncation splits a UTF-8 sequence.
        try
        {
            return NativeJsonReader.ReadBoundedString(element, propertyName, 63);
        }
        catch (Exception error) when (error is EncoderFallbackException or DecoderFallbackException)
        {
            invalid = true;
            return string.Empty;
        }
    }

    private static ParsedQuirkEvolutionDefinition? ReadEvolutionDefinition(JsonElement element)
    {
        // 0x1404E02E0 initializes these six fields to zero/false. The quirk
        // loader reads their exact names; an arbitrary evolution_* note has no effect.
        var validationErrors = new List<string>();
        var durationMin = ReadEvolutionInteger(
            element,
            "evolution_duration_min",
            validationErrors);
        var durationMax = ReadEvolutionInteger(
            element,
            "evolution_duration_max",
            validationErrors);
        var townProgressionDurationChange = ReadEvolutionInteger(
            element,
            "evolution_town_progression_duration_change",
            validationErrors);
        var townAttemptUseItemDurationThreshold = ReadEvolutionInteger(
            element,
            "evolution_town_attempt_use_item_duration_threshold",
            validationErrors);

        string? targetQuirkId = null;
        if (NativeJsonReader.TryGetProperty(element, "evolution_class_id", out var targetNode))
        {
            if (targetNode.ValueKind == JsonValueKind.String)
            {
                var target = NativeJsonReader.CString(targetNode.GetString()!);
                // Empty strings hash to the default target zero (0x1404DF0A3).
                targetQuirkId = target.Length == 0 ? null : target;
            }
            else
            {
                validationErrors.Add("进化字段 'evolution_class_id' 必须是字符串；空字符串表示无目标");
            }
        }

        var causesDeath = false;
        if (NativeJsonReader.TryGetProperty(element, "evolution_causes_death", out var causesDeathNode))
        {
            if (causesDeathNode.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                causesDeath = causesDeathNode.GetBoolean();
            }
            else
            {
                validationErrors.Add("进化字段 'evolution_causes_death' 必须是布尔值");
            }
        }

        if (validationErrors.Count == 0 && durationMin == 0 && durationMax == 0 &&
            townProgressionDurationChange == 0 && townAttemptUseItemDurationThreshold == 0 &&
            targetQuirkId is null && !causesDeath)
            return null;

        if (durationMin is < 0)
        {
            validationErrors.Add("进化字段 'evolution_duration_min' 不能为负数");
        }
        if (durationMax is < 0)
        {
            validationErrors.Add("进化字段 'evolution_duration_max' 不能为负数");
        }
        if (durationMin is { } minimum && durationMax is { } maximum && minimum > maximum)
        {
            validationErrors.Add("进化持续值下限不能大于上限（evolution_duration_min / evolution_duration_max）");
        }
        if (townAttemptUseItemDurationThreshold is < 0)
        {
            validationErrors.Add("进化字段 'evolution_town_attempt_use_item_duration_threshold' 不能为负数");
        }
        if (string.IsNullOrWhiteSpace(targetQuirkId) && !causesDeath)
        {
            validationErrors.Add(
                "进化配置必须声明非空 evolution_class_id，或设置 evolution_causes_death=true");
        }

        return new ParsedQuirkEvolutionDefinition(
            durationMin,
            durationMax,
            townProgressionDurationChange,
            targetQuirkId,
            causesDeath,
            townAttemptUseItemDurationThreshold,
            validationErrors);
    }

    private static int? ReadEvolutionInteger(
        JsonElement element,
        string propertyName,
        List<string> validationErrors)
    {
        if (!NativeJsonReader.TryGetProperty(element, propertyName, out var property))
        {
            return 0;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value))
        {
            return value;
        }

        validationErrors.Add($"进化字段 '{propertyName}' 必须是 32 位整数");
        return null;
    }

}
