using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;

namespace DarkestDungeonSaveEditor.Core;

public static partial class HeroClassCatalog
{
    private const string HeroInfoSuffix = ".info.darkest";
    private const string HeroOverrideSuffix = ".override.darkest";
    private const string HeroUpgradeSuffix = ".upgrades.json";

    public static HeroClassCatalogResult Load(ActiveContentSnapshot activeContent)
    {
        ArgumentNullException.ThrowIfNull(activeContent);
        var issues = new List<string>();
        var candidates = new Dictionary<string, List<HeroCandidate>>(StringComparer.OrdinalIgnoreCase);
        var eventCandidates = new Dictionary<string, List<RecruitEventGroup>>(StringComparer.OrdinalIgnoreCase);
        var effectCandidates = new Dictionary<string, List<EffectQuirkAssignment>>(StringComparer.OrdinalIgnoreCase);
        var quirkCandidates = new Dictionary<string, List<QuirkDefinition>>(StringComparer.OrdinalIgnoreCase);
        var buffCandidates = new Dictionary<string, List<BuffDefinition>>(StringComparer.OrdinalIgnoreCase);
        var upgradeCandidates = new Dictionary<string, List<HeroUpgradeDefinition>>(StringComparer.OrdinalIgnoreCase);
        var campingSkills = new Dictionary<string, CampingSkillBuilder>(StringComparer.OrdinalIgnoreCase);
        var heroNames = new HashSet<string>(StringComparer.Ordinal);
        var sourceFiles = new List<SourceFiles>();
        var enabledDlcPrefixes = ContentFileOverlay.GetEnabledDlcPrefixes(activeContent.Sources);
        var sourcesById = activeContent.Sources.ToDictionary(
            source => source.Id,
            StringComparer.OrdinalIgnoreCase);

        foreach (var source in activeContent.Sources.OrderBy(item => item.LoadOrder))
        {
            sourceFiles.Add(new SourceFiles(source, EnumerateSourceFiles(source, enabledDlcPrefixes, issues)));
        }

        var heroFiles = ResolveFiles(sourceFiles, files => files.HeroInfoFiles, "Hero definition", issues);
        var heroOverrideFiles = ResolveFiles(sourceFiles, files => files.HeroOverrideFiles, "Hero override", issues);
        var effectFiles = ResolveFiles(sourceFiles, files => files.EffectFiles, "Effect definition", issues);
        var quirkFiles = ResolveFiles(sourceFiles, files => files.QuirkFiles, "Quirk definition", issues);
        var eventFiles = ResolveFiles(sourceFiles, files => files.TownEventFiles, "Town event definition", issues);
        var buffFiles = ResolveFiles(sourceFiles, files => files.BuffFiles, "Buff definition", issues);
        var campingFiles = ResolveFiles(sourceFiles, files => files.CampingSkillFiles, "Camping skill definition", issues);
        var nameFiles = ResolveFiles(sourceFiles, files => files.NameFiles, "Hero name definition", issues);
        var upgradeFiles = ResolveFiles(sourceFiles, files => files.HeroUpgradeFiles, "Hero upgrade definition", issues);
        var rosterVariableFiles = ResolveFiles(sourceFiles, files => files.RosterVariableFiles, "Roster variables", issues);

        foreach (var file in heroFiles)
        {
            try
            {
                var candidate = ReadHeroInfo(file);
                if (!candidates.TryGetValue(candidate.Id, out var classCandidates))
                {
                    classCandidates = [];
                    candidates[candidate.Id] = classCandidates;
                }

                classCandidates.Add(candidate);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                issues.Add($"Failed to read hero definition '{file.Path}': {ex.Message}");
            }
        }

        var heroOverridesByClass = heroOverrideFiles
            .GroupBy(
                file => ReadHeroClassId(file.Path, HeroOverrideSuffix),
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<EffectiveContentFile>)group.ToArray(),
                StringComparer.OrdinalIgnoreCase);
        foreach (var pair in heroOverridesByClass.Where(pair => !candidates.ContainsKey(pair.Key)))
        {
            issues.Add(
                $"Hero override for '{pair.Key}' has no active base definition and was ignored: " +
                string.Join(" | ", pair.Value.Select(file => file.Path)));
        }

        foreach (var file in effectFiles)
        {
            try
            {
                foreach (var effect in ReadEffectAssignments(file.Path, file.Source.Id))
                {
                    AddCandidate(effectCandidates, effect.Name, effect);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                issues.Add($"Failed to read effect definitions '{file.Path}': {ex.Message}");
            }
        }

        foreach (var file in quirkFiles)
        {
            try
            {
                var providerSourcesById = ReadProviderSourcesByQuirkId(file);
                foreach (var quirk in ReadQuirkDefinitions(
                             file.Path,
                             file.Source.Id,
                             [file.Source.Id]))
                {
                    var providerSources = providerSourcesById.TryGetValue(
                        quirk.Id,
                        out var declaringSources)
                        ? declaringSources
                        : [file.Source.Id];
                    AddCandidate(
                        quirkCandidates,
                        quirk.Id,
                        quirk with { AllSources = providerSources });
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                issues.Add($"Failed to read quirk definitions '{file.Path}': {ex.Message}");
            }
        }

        foreach (var file in buffFiles)
        {
            try
            {
                foreach (var buff in ReadBuffDefinitions(file.Path, file.Source.Id))
                {
                    AddCandidate(buffCandidates, buff.Id, buff);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                issues.Add($"Failed to read buff definitions '{file.Path}': {ex.Message}");
            }
        }

        foreach (var file in campingFiles)
        {
            try
            {
                foreach (var skill in ReadCampingSkills(file.Path))
                {
                    if (!campingSkills.TryGetValue(skill.Id, out var builder))
                    {
                        builder = new CampingSkillBuilder(skill.Id);
                        campingSkills[skill.Id] = builder;
                    }

                    builder.Add(skill);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                issues.Add($"Failed to read camping skill definitions '{file.Path}': {ex.Message}");
            }
        }

        foreach (var file in nameFiles)
        {
            try
            {
                foreach (var name in ContentLocalizationCatalog.ReadHeroNames(file.Path, issues))
                {
                    heroNames.Add(name);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException or RegexMatchTimeoutException)
            {
                issues.Add($"Failed to read hero names '{file.Path}': {ex.Message}");
            }
        }

        foreach (var file in upgradeFiles)
        {
            try
            {
                var upgrade = ReadHeroUpgrade(file);
                AddCandidate(upgradeCandidates, upgrade.HeroClassId, upgrade);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                issues.Add($"Failed to read hero upgrade definition '{file.Path}': {ex.Message}");
            }
        }

        foreach (var file in eventFiles)
        {
            try
            {
                foreach (var eventGroup in ReadRecruitEvents(file.Path, file.Source.Id))
                {
                    AddCandidate(eventCandidates, eventGroup.EventId, eventGroup);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                issues.Add($"Failed to read town event definitions '{file.Path}': {ex.Message}");
            }
        }

        var localization = ContentLocalizationCatalog.Load(
            activeContent,
            candidates.Keys
                .Select(ContentLocalizationCatalog.GetHeroClassKey)
                .Concat(quirkCandidates.Keys.Select(ContentLocalizationCatalog.GetQuirkKey)));
        issues.AddRange(localization.Issues);
        var quirkProviderSourcesById = quirkCandidates.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)pair.Value
                .SelectMany(quirk => quirk.AllSources)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            StringComparer.OrdinalIgnoreCase);

        var effectiveEffects = ResolveUniqueDefinitions(
            effectCandidates,
            sourcesById,
            definition => definition.Source,
            definition => definition.SourcePath,
            definition => definition.QuirkId,
            "Effect",
            issues);
        var effectiveQuirks = ResolveUniqueDefinitions(
            quirkCandidates,
            sourcesById,
            definition => definition.Source,
            definition => definition.SourcePath,
            GetQuirkSignature,
            "Quirk",
            issues);
        var effectiveBuffs = ResolveUniqueDefinitions(
            buffCandidates,
            sourcesById,
            definition => definition.Source,
            definition => definition.SourcePath,
            GetBuffSignature,
            "Buff",
            issues,
            reportConflicts: false);
        var effectiveUpgrades = ResolveHeroUpgradeDefinitions(
            upgradeCandidates,
            candidates,
            heroOverridesByClass,
            sourcesById,
            issues);
        var resolveLevelThresholds = ReadEffectiveResolveLevelThresholds(rosterVariableFiles, issues);
        var effectiveEvents = ResolveUniqueDefinitions(
            eventCandidates,
            sourcesById,
            definition => definition.Source,
            definition => definition.SourcePath,
            GetRecruitEventSignature,
            "Town event",
            issues);
        var recruitEvents = effectiveEvents.Values
            .SelectMany(group => group.Recruits)
            .OrderBy(item => item.HeroClass, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var eventsByClass = recruitEvents
            .GroupBy(item => item.HeroClass, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<HeroRecruitEventDefinition>)group.ToArray(),
                StringComparer.OrdinalIgnoreCase);

        var heroClasses = candidates
            .Values
            .Select(classCandidates => MergeHeroClass(
                classCandidates,
                heroOverridesByClass.TryGetValue(classCandidates[0].Id, out var classOverrides)
                    ? classOverrides
                    : [],
                sourcesById,
                eventsByClass,
                effectiveEffects,
                effectiveQuirks,
                campingSkills,
                effectiveUpgrades,
                resolveLevelThresholds,
                issues))
            .Select(heroClass => heroClass with
            {
                LocalizedName = localization.GetHeroClassName(heroClass.Id),
                SourceLabel = ContentSourceLabelFormatter.Format(
                    heroClass.Source,
                    heroClass.AllSources,
                    activeContent.Sources)
            })
            .OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        // Keep unresolved cross-path conflicts in the selection catalog so the
        // editor can show them as unavailable. When one effective definition was
        // resolved (including repeated declarations in the same path), show only
        // that selected definition to keep display and runtime semantics aligned.
        var initialQuirks = quirkCandidates.Values
            .SelectMany(definitions =>
                effectiveQuirks.TryGetValue(definitions[0].Id, out var effectiveQuirk)
                    ? new[] { effectiveQuirk }
                    : definitions.ToArray())
            .Select(quirk =>
            {
                var mergedQuirk = quirk with
                {
                    AllSources = quirkProviderSourcesById.TryGetValue(quirk.Id, out var providerSources)
                        ? providerSources
                        : quirk.AllSources
                };
                return BuildInitialQuirk(mergedQuirk, effectiveQuirks, effectiveBuffs, buffCandidates) with
                {
                    LocalizedName = localization.GetQuirkName(mergedQuirk.Id),
                    SourceLabel = ContentSourceLabelFormatter.Format(
                        mergedQuirk.Source,
                        mergedQuirk.AllSources,
                        activeContent.Sources)
                };
            })
            .OrderBy(quirk => quirk.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var catalog = new HeroClassCatalogResult(
            activeContent.GameMode,
            resolveLevelThresholds,
            heroClasses,
            recruitEvents,
            initialQuirks,
            heroNames.OrderBy(name => name, StringComparer.Ordinal).ToArray(),
            issues);
        var validatedHeroes = heroClasses.Select(hero => hero with
        {
            GenerationAvailability = StagecoachHeroCandidateFactory.GetGenerationAvailability(catalog, hero)
        }).ToArray();
        foreach (var hero in validatedHeroes)
        {
            var firstFailure = hero.GenerationAvailability.FirstOrDefault(level => !level.CanGenerate);
            if (firstFailure is not null)
            {
                var levels = hero.GenerationAvailability.Where(level => level.CanGenerate)
                    .Select(level => level.ResolveLevel).ToArray();
                issues.Add($"人物生成预检：{hero.Id}；可用等级：" +
                           (levels.Length == 0 ? "无" : string.Join(",", levels)) +
                           $"；首个限制：{firstFailure.UnavailableReason}");
            }
        }

        return catalog with { HeroClasses = validatedHeroes };
    }
}
