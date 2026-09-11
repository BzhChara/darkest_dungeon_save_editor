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
    private const string HeroArtSuffix = ".art.darkest";
    private const string HeroUpgradeSuffix = ".upgrades.json";

    public static HeroClassCatalogResult Load(ActiveContentSnapshot activeContent)
    {
        ArgumentNullException.ThrowIfNull(activeContent);
        var issues = new List<string>();
        var candidates = new Dictionary<string, List<HeroCandidate>>(StringComparer.OrdinalIgnoreCase);
        var eventCandidates = new Dictionary<string, List<RecruitEventGroup>>(StringComparer.Ordinal);
        var effectCandidates = new Dictionary<string, List<EffectQuirkAssignment>>(StringComparer.Ordinal);
        var quirkCandidates = new Dictionary<string, List<QuirkDefinition>>(StringComparer.Ordinal);
        var buffCandidates = new Dictionary<string, List<BuffDefinition>>(StringComparer.Ordinal);
        var campingSkills = new Dictionary<string, CampingSkillBuilder>(StringComparer.Ordinal);
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

        var heroIds = sourceFiles.SelectMany(item => item.Files.HeroInfoFiles.Select(path =>
                NativeContentFileResolver.ReadDiscoveredActorId(Path.GetRelativePath(item.Source.Directory, path))))
            .Distinct(StringComparer.Ordinal).ToArray();
        var actorFiles = NativeContentFileResolver.ResolveActorFiles(activeContent.Sources, "heroes", issues);
        var heroFiles = new List<EffectiveContentFile>();
        var heroOverrideFiles = new List<EffectiveContentFile>();
        foreach (var id in heroIds)
        {
            if (!actorFiles.TryGetValue($"heroes/{id}/{id}{HeroInfoSuffix}", out var info))
            {
                issues.Add($"Hero '{id}' has no canonical info file; no generation template is available.");
                continue;
            }
            heroFiles.Add(info);
            foreach (var suffix in new[] { HeroArtSuffix, HeroOverrideSuffix })
                if (actorFiles.TryGetValue($"heroes/{id}/{id}{suffix}", out var file)) heroOverrideFiles.Add(file);
        }
        var effectFiles = ResolveFiles(sourceFiles, files => files.EffectFiles, "Effect definition", issues);
        var quirkFiles = NativeContentFileResolver.Resolve(sourceFiles.SelectMany(item =>
            item.Files.QuirkFiles.Select(path => new ContentFileCandidate(item.Source, path))).ToArray(),
            activeContent.Sources, "Quirk definition", issues);
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
                var candidate = ReadHeroInfo(file, sourcesById);
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
                file => ReadHeroClassId(file.Path, file.Path.EndsWith(HeroArtSuffix, StringComparison.OrdinalIgnoreCase)
                    ? HeroArtSuffix : HeroOverrideSuffix),
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
                        builder = new CampingSkillBuilder(skill.Id, skill.IsShared);
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

        foreach (var id in NativeResourceIdentity.FindCollisions(campingSkills.Keys))
        {
            campingSkills.Remove(id);
            issues.Add($"Camping skill '{id}' has conflicting native identities and was left unresolved.");
        }
        foreach (var skill in campingSkills.Values.Where(skill => skill.IsShared is null))
            issues.Add($"Camping skill '{skill.Id}' has no valid hero_classes array in its first record; generation classification is unavailable.");

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

        foreach (var file in eventFiles)
        {
            try
            {
                foreach (var eventGroup in ReadRecruitEvents(file.Path, file.Source.Id))
                {
                    AddCandidate(eventCandidates, eventGroup.EventId, eventGroup);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or EncoderFallbackException or DecoderFallbackException)
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
            StringComparer.Ordinal);

        var effectiveEffects = ResolveOrderedDefinitions(
            effectCandidates,
            definition => definition.Name,
            definitions => definitions.LastOrDefault(definition => definition.QuirkId is not null) ?? definitions[^1],
            "Effect",
            issues);
        // Native quirk lookup (0x1404ABD00) walks the entire loaded vector and
        // returns the last matching ID. Different filenames do not imply ambiguity.
        var quirkHashCollisions = NativeResourceIdentity.FindCollisions(quirkCandidates.Values.SelectMany(group => group).Select(quirk => quirk.Id));
        if (quirkHashCollisions.Count > 0) issues.Add("Quirk IDs share native hashes and remain unresolved: " + string.Join(", ", quirkHashCollisions));
        var effectiveQuirks = quirkCandidates.Where(pair => !pair.Value.Any(quirk => quirkHashCollisions.Contains(quirk.Id)) && pair.Value.Select(value => value.Id)
                .Distinct(StringComparer.Ordinal).Count() == 1).ToDictionary(pair => pair.Key,
            pair => pair.Value[^1], StringComparer.Ordinal);
        var effectiveBuffs = ResolveOrderedDefinitions(
            buffCandidates,
            definition => definition.Id,
            definitions => definitions[^1],
            "Buff",
            issues);
        var effectiveUpgrades = ResolveHeroUpgradeTrees(upgradeFiles, issues);
        var resolveLevelThresholds = ReadEffectiveResolveLevelThresholds(rosterVariableFiles, issues);
        var effectiveEvents = ResolveOrderedDefinitions(
            eventCandidates,
            definition => definition.EventId,
            definitions => definitions[0],
            "Town event",
            issues);
        var recruitEvents = effectiveEvents.Values
            .SelectMany(group => group.Recruits)
            .OrderBy(item => item.HeroClass, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var eventsByClass = recruitEvents
            .GroupBy(item => Loc2LocalizationReader.HashName(item.HeroClass))
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<HeroRecruitEventDefinition>)group.ToArray());

        var heroHashCollisions = NativeResourceIdentity.FindCollisions(heroIds);
        if (heroHashCollisions.Count > 0) issues.Add("Hero IDs share native hashes and remain unresolved: " + string.Join(", ", heroHashCollisions));
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
            .Select(heroClass => heroHashCollisions.Contains(heroClass.Id)
                ? heroClass with { HasProviderConflict = true, Source = "unresolved" } : heroClass)
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
        // Keep unresolved native-identity conflicts in the selection catalog so
        // the editor can show them as unavailable. When one definition was
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
