using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;

namespace DarkestDungeonSaveEditor.Core;

public static partial class HeroClassCatalog
{
    private static HeroInitialQuirkDefinition BuildInitialQuirk(
        QuirkDefinition quirk,
        IReadOnlyDictionary<string, BuffDefinition> effectiveBuffs,
        IReadOnlyDictionary<string, List<BuffDefinition>> buffCandidates)
    {
        var contextReasons = new List<string>();
        var unverifiedReasons = new List<string>();
        var definitionLimits = new List<int>();

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
                else if (unresolved.Any(candidate => candidate.StatSubType.Equals(
                             "max_hp",
                             StringComparison.OrdinalIgnoreCase)))
                {
                    unverifiedReasons.Add($"max_hp Buff '{buffId}' 定义未解析");
                }

                continue;
            }

            referencedBuffs.Add(buff);
        }

        var hpBuffs = referencedBuffs
            .Where(buff => buff.StatSubType.Equals("max_hp", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var maxHpModifiers = new List<HeroMaxHpModifier>(hpBuffs.Length);
        foreach (var buff in hpBuffs)
        {
            var modifierKind = buff.StatType.ToLowerInvariant() switch
            {
                "combat_stat_add" => HeroMaxHpModifierKind.Flat,
                "combat_stat_multiply" => HeroMaxHpModifierKind.Percentage,
                _ => (HeroMaxHpModifierKind?)null
            };
            var hasValidRuleData = buff.RuleType.ToLowerInvariant() switch
            {
                "always" or "no_trinkets" or "afflicted" => true,
                "in_mode" => !string.IsNullOrWhiteSpace(buff.RuleString),
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
        foreach (var rawLine in File.ReadLines(path, Encoding.UTF8))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            var separator = line.IndexOf(':');
            if (separator <= 0 || !line[..separator].Trim().Equals("effect", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var attributes = ParseAttributes(line[(separator + 1)..]);
            var name = ReadString(attributes, "name");
            var quirkId = ReadString(attributes, "disease");
            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(quirkId))
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
        if (!document.RootElement.TryGetProperty("quirks", out var quirks) ||
            quirks.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var item in quirks.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !item.TryGetProperty("id", out var idNode) ||
                idNode.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(idNode.GetString()))
            {
                continue;
            }

            double? randomChance = null;
            if (item.TryGetProperty("random_chance", out var chanceNode) &&
                chanceNode.ValueKind == JsonValueKind.Number &&
                chanceNode.TryGetDouble(out var chance))
            {
                randomChance = chance;
            }

            bool? isPositive = null;
            if (item.TryGetProperty("is_positive", out var positiveNode) &&
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
                ReadJsonStringArray(item, "buffs"),
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
        var sourcesById = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
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
            StringComparer.OrdinalIgnoreCase);
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
        if (!document.RootElement.TryGetProperty("buffs", out var buffs) ||
            buffs.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var item in buffs.EnumerateArray())
        {
            var id = ReadJsonString(item, "id");
            if (item.ValueKind != JsonValueKind.Object || string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            var hasRuleData = item.TryGetProperty("rule_data", out var ruleData) &&
                              ruleData.ValueKind == JsonValueKind.Object;

            yield return new BuffDefinition(
                id,
                ReadJsonString(item, "stat_type"),
                ReadJsonString(item, "stat_sub_type"),
                ReadJsonDouble(item, "amount"),
                ReadJsonString(item, "rule_type"),
                ReadJsonBoolean(item, "is_false_rule"),
                hasRuleData ? ReadJsonDouble(ruleData, "float") : null,
                hasRuleData ? ReadJsonString(ruleData, "string") : string.Empty,
                source,
                Path.GetFullPath(path));
        }
    }

    private static string? ReadEvolutionSignature(JsonElement element)
    {
        var fields = element
            .EnumerateObject()
            .Where(property => property.Name.StartsWith("evolution_", StringComparison.OrdinalIgnoreCase))
            .OrderBy(property => property.Name, StringComparer.OrdinalIgnoreCase)
            .Select(property =>
                $"{property.Name.ToLowerInvariant()}={NormalizeSemanticJsonValue(property.Value)}")
            .ToArray();
        return fields.Length == 0 ? null : string.Join("|", fields);
    }

    private static ParsedQuirkEvolutionDefinition? ReadEvolutionDefinition(JsonElement element)
    {
        var signature = ReadEvolutionSignature(element);
        if (signature is null)
        {
            return null;
        }

        var validationErrors = new List<string>();
        var durationMin = ReadEvolutionInteger(
            element,
            "evolution_duration_min",
            required: true,
            validationErrors);
        var durationMax = ReadEvolutionInteger(
            element,
            "evolution_duration_max",
            required: true,
            validationErrors);
        var townProgressionDurationChange = ReadEvolutionInteger(
            element,
            "evolution_town_progression_duration_change",
            required: false,
            validationErrors);
        var townAttemptUseItemDurationThreshold = ReadEvolutionInteger(
            element,
            "evolution_town_attempt_use_item_duration_threshold",
            required: false,
            validationErrors);

        string? targetQuirkId = null;
        if (element.TryGetProperty("evolution_class_id", out var targetNode))
        {
            if (targetNode.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(targetNode.GetString()))
            {
                targetQuirkId = targetNode.GetString()!.Trim();
            }
            else
            {
                validationErrors.Add("进化字段 'evolution_class_id' 必须是非空字符串");
            }
        }

        var causesDeath = false;
        if (element.TryGetProperty("evolution_causes_death", out var causesDeathNode))
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
            validationErrors.Add("进化持续值下限不能大于上限");
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
            signature,
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
        bool required,
        List<string> validationErrors)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            if (required)
            {
                validationErrors.Add($"进化配置缺少 '{propertyName}'");
            }

            return null;
        }

        if (TryReadExactInt32(property, out var value))
        {
            return value;
        }

        validationErrors.Add($"进化字段 '{propertyName}' 必须是 32 位整数");
        return null;
    }

    private static bool TryReadExactInt32(JsonElement element, out int value)
    {
        if (element.ValueKind != JsonValueKind.Number)
        {
            value = default;
            return false;
        }

        if (element.TryGetInt32(out value))
        {
            return true;
        }

        if (element.TryGetDecimal(out var decimalValue) &&
            decimalValue == decimal.Truncate(decimalValue) &&
            decimalValue >= int.MinValue &&
            decimalValue <= int.MaxValue)
        {
            value = (int)decimalValue;
            return true;
        }

        value = default;
        return false;
    }

    private static string NormalizeSemanticJsonValue(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDecimal(out var number) =>
                number.ToString("G29", CultureInfo.InvariantCulture),
            JsonValueKind.Number when value.TryGetDouble(out var number) =>
                number.ToString("R", CultureInfo.InvariantCulture),
            JsonValueKind.String => JsonSerializer.Serialize(value.GetString()),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => "null",
            _ => value.GetRawText()
        };
    }

}
