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
        IReadOnlyDictionary<string, ActiveContentSource> sourcesById,
        IReadOnlyDictionary<string, IReadOnlyList<HeroRecruitEventDefinition>> eventsByClass,
        IReadOnlyDictionary<string, EffectQuirkAssignment> effectiveEffects,
        IReadOnlyDictionary<string, QuirkDefinition> effectiveQuirks,
        IReadOnlyDictionary<string, CampingSkillBuilder> campingSkills,
        IReadOnlyDictionary<string, HeroUpgradeTreeCandidate> effectiveUpgrades,
        IReadOnlyList<int> resolveLevelThresholds,
        List<string> issues)
    {
        var ordered = SelectEffectiveDefinitions(
                candidates,
                sourcesById,
                candidate => candidate.Source,
                candidate => candidate.SourcePath,
                GetHeroCandidateSignature)
            .OrderBy(item => item.SourcePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var sources = candidates
            .SelectMany(item => item.ProviderSources)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var recruitEvents = eventsByClass.TryGetValue(ordered[0].Id, out var classEvents)
            ? classEvents
            : [];
        if (ordered.Length > 1)
        {
            issues.Add(
                $"Hero class '{ordered[0].Id}' has conflicting definitions at the same effective priority and was left unresolved: " +
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

        var selected = ApplyHeroOverrides(ordered[0], overrideFiles, sourcesById);
        selected = selected with
        {
            ColourVariationCount = Math.Max(
                selected.ColourVariationCount,
                candidates.Select(candidate => candidate.ColourVariationCount).DefaultIfEmpty(0).Max())
        };
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
                StringComparer.OrdinalIgnoreCase)
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
                .Where(skill => !skill.IsShared)
                .Select(skill => skill.Id)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            availableCampingSkills
                .Where(skill => skill.IsShared)
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
            CombatSkillLevels = selected.CombatSkillLevels.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<int>)pair.Value.ToArray(),
                StringComparer.Ordinal)
        };
    }

    private static HeroCandidate ApplyHeroOverrides(
        HeroCandidate selected,
        IReadOnlyList<EffectiveContentFile> overrideFiles,
        IReadOnlyDictionary<string, ActiveContentSource> sourcesById)
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

        builder.ColourVariationCount = Math.Max(
            builder.ColourVariationCount,
            applicableOverrides
                .SelectMany(file => file.Providers)
                .Select(provider => CountColourVariations(provider.Path, selected.Id, sourcesById[provider.SourceId]))
                .DefaultIfEmpty(0)
                .Max());
        return builder.Build();
    }

    private static HeroCandidate ReadHeroInfo(EffectiveContentFile file,
        IReadOnlyDictionary<string, ActiveContentSource> sourcesById)
    {
        var path = file.Path;
        var id = ReadHeroClassId(path, HeroInfoSuffix);
        var builder = new HeroCandidateBuilder(
            id,
            file.Source.Id,
            Path.GetFullPath(path),
            file.ProviderSources);
        ApplyHeroDefinitionFile(builder, path);
        builder.ColourVariationCount = file.Providers
            .Select(provider => CountColourVariations(provider.Path, id, sourcesById[provider.SourceId]))
            .DefaultIfEmpty(0)
            .Max();
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
        foreach (var rawLine in File.ReadLines(path, Encoding.UTF8))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            var kind = line[..separator].Trim();
            var attributes = ParseAttributes(line[(separator + 1)..]);
            if (kind.Equals("combat_skill", StringComparison.OrdinalIgnoreCase))
            {
                var level = ReadInt(attributes, "level");
                var skillId = ReadString(attributes, "id");
                if (string.IsNullOrWhiteSpace(skillId))
                {
                    continue;
                }

                builder.AddCombatSkill(skillId, level ?? 0);
                if (level is not null and not 0)
                {
                    continue;
                }

                if (ReadBooleanFlag(attributes, "generation_guaranteed") is { } generationGuaranteed)
                {
                    builder.SetGuaranteedCombatSkill(skillId, generationGuaranteed);
                }

                foreach (var pair in attributes.Where(pair =>
                             pair.Key.Equals("effect", StringComparison.OrdinalIgnoreCase) ||
                             pair.Key.EndsWith("_effects", StringComparison.OrdinalIgnoreCase)))
                {
                    if (pair.Key.Equals("effect", StringComparison.OrdinalIgnoreCase))
                        builder.AppendSkillEffects(skillId, pair.Key, pair.Value);
                    else
                        builder.ReplaceSkillEffects(skillId, pair.Key, pair.Value);
                }
            }
            else if (kind.Equals("skill_selection", StringComparison.OrdinalIgnoreCase))
            {
                builder.CanSelectCombatSkills =
                    ReadBooleanFlag(attributes, "can_select_combat_skills") ?? builder.CanSelectCombatSkills;
                builder.SelectedCombatSkillsMax =
                    ReadInt(attributes, "number_of_selected_combat_skills_max") ?? builder.SelectedCombatSkillsMax;
            }
            else if (kind.Equals("generation", StringComparison.OrdinalIgnoreCase))
            {
                builder.UpdateGeneration(attributes);
            }
            else if (kind.Equals("armour", StringComparison.OrdinalIgnoreCase))
            {
                builder.UpdateEquipmentRank("armour", attributes);
            }
            else if (kind.Equals("weapon", StringComparison.OrdinalIgnoreCase))
            {
                builder.UpdateEquipmentRank("weapon", attributes);
            }
            else if (kind.Equals("quirk_modifier", StringComparison.OrdinalIgnoreCase) &&
                     attributes.TryGetValue("incompatible_class_ids", out var incompatibleIds))
            {
                builder.AddIncompatibleInitialQuirks(incompatibleIds);
            }
        }
    }

    private static int CountColourVariations(string heroInfoPath, string heroClass, ActiveContentSource source)
    {
        var directory = Path.GetDirectoryName(heroInfoPath);
        if (directory is null || !Directory.Exists(directory))
        {
            return 0;
        }

        IEnumerable<string?> names;
        if (source.Kind is "local" or "workshop")
        {
            // A physical skin folder alone is not an active Mod resource. Keep
            // only folders containing an existing texture listed in its manifest.
            var manifestPath = Path.Combine(source.Directory, "modfiles.txt");
            ModManifestFile.Require(manifestPath);
            names = ModManifestFile.ReadEntries(manifestPath, ".png")
                .Select(entry => Path.GetFullPath(Path.Combine(source.Directory, entry.RelativePath)))
                .Where(File.Exists)
                .Select(path => Path.GetRelativePath(directory, path).Replace('\\', '/'))
                .Where(path => !Path.IsPathRooted(path) && !path.StartsWith("../", StringComparison.Ordinal) && path.Contains('/'))
                .Select(path => path.Split('/')[0]);
        }
        else
        {
            names = Directory.EnumerateDirectories(directory, $"{heroClass}_*", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName);
        }
        var suffixes = names
            .Where(name => name is not null &&
                           name.Length == heroClass.Length + 2 &&
                           name.StartsWith($"{heroClass}_", StringComparison.OrdinalIgnoreCase))
            .Select(name => char.ToUpperInvariant(name![^1]))
            .Where(value => value is >= 'A' and <= 'Z')
            .ToHashSet();
        var count = 0;
        while (suffixes.Contains((char)('A' + count)))
        {
            count++;
        }

        return count;
    }
}
