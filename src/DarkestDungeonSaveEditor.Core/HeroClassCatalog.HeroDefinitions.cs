using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;

namespace DarkestDungeonSaveEditor.Core;

public static partial class HeroClassCatalog
{
    private static HeroClassDefinition MergeHeroClass(
        IReadOnlyList<HeroCandidate> candidates,
        IReadOnlyList<EffectiveContentFile> overrideFiles,
        IReadOnlyDictionary<uint, IReadOnlyList<HeroRecruitEventDefinition>> eventsByClass,
        IReadOnlyDictionary<string, EffectQuirkAssignment> effectiveEffects,
        IReadOnlyDictionary<string, QuirkDefinition> effectiveQuirks,
        IReadOnlyDictionary<string, CampingSkillBuilder> campingSkills,
        IReadOnlyDictionary<string, HeroUpgradeTreeCandidate> effectiveUpgrades,
        IReadOnlyList<int> resolveLevelThresholds,
        List<string> issues)
    {
        // Candidate files have already been resolved at the canonical actor
        // path. Repeated discovery of that same file is not another provider
        // election, and semantic similarity cannot justify merging other paths.
        var ordered = candidates
            .GroupBy(candidate => candidate.SourcePath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .OrderBy(item => item.SourcePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var sources = candidates
            .SelectMany(item => item.ProviderSources)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var recruitEvents = eventsByClass.TryGetValue(Loc2LocalizationReader.HashName(ordered[0].Id), out var classEvents)
            ? classEvents
            : [];
        if (ordered.Length > 1)
        {
            issues.Add(
                $"Hero class '{ordered[0].Id}' has multiple canonical definition files and was left unresolved: " +
                string.Join(" | ", ordered.Select(candidate => $"{candidate.Source}:{candidate.SourcePath}")));
            return new HeroClassDefinition(
                ordered[0].Id,
                "unresolved",
                string.Join(" | ", ordered.Select(item => item.SourcePath)),
                null,
                null,
                null,
                null,
                [],
                [],
                "职业定义冲突，无法解析等级模板",
                0,
                [],
                [],
                [],
                [],
                [],
                [],
                recruitEvents,
                [],
                true,
                sources);
        }

        var selected = ApplyHeroOverrides(ordered[0], overrideFiles);
        sources = sources
            .Concat(selected.ProviderSources)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var runtimeQuirks = selected.SkillEffects
            .Select(reference => ResolveRuntimeQuirk(reference, effectiveEffects, effectiveQuirks))
            .Where(signal => signal is not null)
            .Select(signal => signal!)
            .DistinctBy(
                signal => $"{signal.QuirkId}\n{signal.EffectName}\n{signal.SkillId}",
                StringComparer.Ordinal)
            .OrderBy(signal => signal.QuirkId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(signal => signal.SkillId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var availableCampingSkills = campingSkills.Values
            .Where(skill => skill.HeroClasses.Contains(selected.Id))
            .ToArray();
        var upgrade = BindHeroUpgradeTrees(selected, effectiveUpgrades, issues);
        var progression = BuildHeroProgression(selected, upgrade, resolveLevelThresholds);
        if (!string.IsNullOrWhiteSpace(progression.UnsupportedReason))
        {
            issues.Add($"Hero class '{selected.Id}' progression is incomplete: {progression.UnsupportedReason}");
        }

        return new HeroClassDefinition(
            selected.Id,
            selected.Source,
            selected.SourcePath,
            selected.CanSelectCombatSkills,
            selected.SelectedCombatSkillsMax,
            selected.Generation,
            selected.BaseHp,
            progression.LevelProfiles,
            upgrade.Trees,
            progression.UnsupportedReason,
            selected.ColourVariationCount,
            selected.CombatSkillIds.ToArray(),
            selected.CombatSkillIds
                .Where(skillId => selected.CombatSkillLevels.TryGetValue(skillId, out var levels) &&
                                  levels.Count == 1 && levels[0] == 0)
                .ToArray(),
            selected.GuaranteedCombatSkillIds.ToArray(),
            availableCampingSkills
                .Where(skill => skill.IsShared == false)
                .Select(skill => skill.Id)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            availableCampingSkills
                .Where(skill => skill.IsShared == true)
                .Select(skill => skill.Id)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            selected.IncompatibleInitialQuirkIds
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            recruitEvents,
            runtimeQuirks,
            false,
            sources)
        {
            Equipment = CreateEquipmentDefinition(selected),
            CombatSkillLevels = selected.CombatSkillLevels.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<int>)pair.Value.ToArray(),
                StringComparer.Ordinal)
        };
    }

    private static HeroCandidate ApplyHeroOverrides(
        HeroCandidate selected,
        IReadOnlyList<EffectiveContentFile> overrideFiles)
    {
        if (overrideFiles.Count == 0)
        {
            return selected;
        }

        // Native HeroClass opens info, art, then override independently. An
        // effective override still applies when its source is below the info Mod.
        var applicableOverrides = overrideFiles
            .OrderBy(file => file.Path.EndsWith(HeroOverrideSuffix, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ToArray();
        if (applicableOverrides.Length == 0)
        {
            return selected;
        }

        var providerSources = selected.ProviderSources
            .Concat(applicableOverrides.SelectMany(file => file.ProviderSources))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var builder = new HeroCandidateBuilder(selected, providerSources);
        foreach (var file in applicableOverrides)
        {
            ApplyHeroDefinitionFile(builder, file.Path);
        }

        return builder.Build();
    }

    private static HeroCandidate ReadHeroInfo(EffectiveContentFile file, int colourVariationCount)
    {
        var path = file.Path;
        var id = ReadHeroClassId(path, HeroInfoSuffix);
        var builder = new HeroCandidateBuilder(
            id,
            file.Source.Id,
            Path.GetFullPath(path),
            file.ProviderSources);
        ApplyHeroDefinitionFile(builder, path);
        builder.ColourVariationCount = colourVariationCount;
        return builder.Build();
    }

    private static string ReadHeroClassId(string path, string suffix)
    {
        var fileName = Path.GetFileName(path);
        return fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? fileName[..^suffix.Length]
            : Path.GetFileNameWithoutExtension(fileName);
    }

    private static void ApplyHeroDefinitionFile(HeroCandidateBuilder builder, string path)
    {
        foreach (var (kind, body) in NativeDarkestReader.ReadRecords(path))
        {
            if (kind == "combat_skill")
            {
                var level = NativeDarkestReader.ReadInt(body, ".level");
                var skillId = NativeDarkestReader.ReadString(body, ".id");
                if (string.IsNullOrWhiteSpace(skillId))
                {
                    continue;
                }

                builder.AddCombatSkill(skillId, level ?? 0);
                if (level is not null and not 0)
                {
                    continue;
                }

                if (NativeDarkestReader.ReadBoolean(body, ".generation_guaranteed") is { } generationGuaranteed)
                {
                    builder.SetGuaranteedCombatSkill(skillId, generationGuaranteed);
                }

                builder.AppendSkillEffects(skillId, "effect", NativeDarkestReader.ReadStringList(body, ".effect", 16));
                foreach (var mode in NativeDarkestReader.ReadStringList(body, ".valid_modes", 8, skipEmptyOrFieldTokens: true))
                {
                    // Native mode lists append too, but only modes named by
                    // this declaration are visited; arbitrary *_effects keys
                    // are not independently loaded.
                    var key = $"{mode}_effects";
                    builder.AppendSkillEffects(skillId, key, NativeDarkestReader.ReadStringList(body, $".{key}", 12));
                }
            }
            else if (kind == "skill_selection")
            {
                builder.CanSelectCombatSkills =
                    NativeDarkestReader.ReadBoolean(body, ".can_select_combat_skills") ?? builder.CanSelectCombatSkills;
                builder.SelectedCombatSkillsMax =
                    NativeDarkestReader.ReadInt(body, ".number_of_selected_combat_skills_max") ?? builder.SelectedCombatSkillsMax;
            }
            else if (kind == "generation")
            {
                builder.UpdateGeneration(body);
            }
            else if (kind == "armour")
            {
                builder.UpdateEquipmentRank("armour", body);
            }
            else if (kind == "weapon")
            {
                builder.UpdateEquipmentRank("weapon", body);
            }
            else if (kind == "quirk_modifier")
            {
                builder.AddIncompatibleInitialQuirks(NativeDarkestReader.ReadStringList(body, ".incompatible_class_ids", 32));
            }
        }
    }

}
