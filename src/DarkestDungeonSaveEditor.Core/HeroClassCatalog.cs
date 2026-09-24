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
        var candidates = new Dictionary<string, List<HeroCandidate>>(StringComparer.Ordinal);
        var eventCandidates = new Dictionary<string, List<RecruitEventGroup>>(StringComparer.Ordinal);
        var effectCandidates = new Dictionary<string, List<EffectQuirkAssignment>>(StringComparer.Ordinal);
        var quirkCandidates = new Dictionary<string, List<QuirkDefinition>>(StringComparer.Ordinal);
        var buffCandidates = new Dictionary<string, List<BuffDefinition>>(StringComparer.Ordinal);
        var campingSkills = new Dictionary<string, CampingSkillBuilder>(StringComparer.Ordinal);
        var heroNames = new HashSet<string>(StringComparer.Ordinal);
        var sourceFiles = new List<SourceFiles>();
        var enabledDlcPrefixes = ContentFileOverlay.GetEnabledDlcPrefixes(activeContent.Sources);

        foreach (var source in activeContent.Sources.OrderBy(item => item.LoadOrder))
        {
            sourceFiles.Add(new SourceFiles(source, EnumerateSourceFiles(source, enabledDlcPrefixes, issues)));
        }
        var skinDirectories = activeContent.Sources.SelectMany(source =>
            HeroSkinDirectoryDiscovery.Enumerate(source, enabledDlcPrefixes, issues)
                .Select(path => new ContentFileCandidate(source, path))).ToArray();

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
        var effectResolutionIssues = new List<string>();
        var effectFiles = ResolveFiles(sourceFiles, files => files.EffectFiles, "Effect definition", effectResolutionIssues, effects: true);
        issues.AddRange(effectResolutionIssues);
        var quirkResolutionIssues = new List<string>();
        var quirkFiles = NativeContentFileResolver.Resolve(sourceFiles.SelectMany(item =>
            item.Files.QuirkFiles.Select(path => new ContentFileCandidate(item.Source, path))).ToArray(),
            activeContent.Sources, "Quirk definition", quirkResolutionIssues);
        issues.AddRange(quirkResolutionIssues);
        var eventResolutionIssues = new List<string>();
        var eventFiles = ResolveFiles(sourceFiles, files => files.TownEventFiles, "Town event definition", eventResolutionIssues);
        issues.AddRange(eventResolutionIssues);
        var buffResolutionIssues = new List<string>();
        var buffFiles = ResolveFiles(sourceFiles, files => files.BuffFiles, "Buff definition", buffResolutionIssues);
        issues.AddRange(buffResolutionIssues);
        var campingResolutionIssues = new List<string>();
        var campingFiles = ResolveFiles(sourceFiles, files => files.CampingSkillFiles, "Camping skill definition", campingResolutionIssues);
        issues.AddRange(campingResolutionIssues);
        var nameFiles = ResolveFiles(sourceFiles, files => files.NameFiles, "Hero name definition", issues);
        var upgradeResolutionIssues = new List<string>();
        var upgradeFiles = ResolveFiles(sourceFiles, files => files.HeroUpgradeFiles, "Hero upgrade definition", upgradeResolutionIssues);
        issues.AddRange(upgradeResolutionIssues);
        var rosterVariableFiles = ResolveFiles(sourceFiles, files => files.RosterVariableFiles, "Roster variables", issues,
            openPath: "campaign/roster/roster.variables.json");
        var sharedRuleFiles = ResolveFiles(sourceFiles, files => files.SharedRuleFiles, "Shared rules", issues);
        var initialQuirkLimits = ReadInitialQuirkLimits(sharedRuleFiles, issues);

        foreach (var file in heroFiles)
        {
            try
            {
                var candidate = ReadHeroInfo(file, HeroSkinDirectoryDiscovery.Count(
                    ReadHeroClassId(file.Path, HeroInfoSuffix), skinDirectories, activeContent.Sources));
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
                StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<EffectiveContentFile>)group.ToArray(),
                StringComparer.Ordinal);
        foreach (var pair in heroOverridesByClass.Where(pair => !candidates.ContainsKey(pair.Key)))
        {
            issues.Add(
                $"Hero override for '{pair.Key}' has no active base definition and was ignored: " +
                string.Join(" | ", pair.Value.Select(file => file.Path)));
        }

        var effectReads = new OrderedDefinitionReadState(firstMatch: false, orderKnown: effectResolutionIssues.Count == 0);
        foreach (var file in effectFiles)
        {
            try
            {
                foreach (var effect in ReadEffectAssignments(file.Path, file.Source.Id).ToArray())
                {
                    AddCandidate(effectCandidates, effect.Name, effect);
                    // Omission preserves the old field, so only an explicit
                    // disease assignment (including "") can recover certainty.
                    if (effect.QuirkId is not null) effectReads.RecordDefinition(effect.Name);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                effectReads.RecordFailure();
                issues.Add($"Failed to read effect definitions '{file.Path}': {ex.Message}");
            }
        }

        var quirkReads = new OrderedDefinitionReadState(firstMatch: false, orderKnown: quirkResolutionIssues.Count == 0);
        foreach (var file in quirkFiles)
        {
            try
            {
                var providerSourcesById = ReadProviderSourcesByQuirkId(file);
                foreach (var quirk in ReadQuirkDefinitions(
                             file.Path,
                             file.Source.Id,
                             [file.Source.Id]).ToArray())
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
                    quirkReads.RecordDefinition(quirk.Id);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                quirkReads.RecordFailure();
                issues.Add($"Failed to read quirk definitions '{file.Path}': {ex.Message}");
            }
        }

        var verifiedBuffIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in buffFiles)
        {
            try
            {
                foreach (var buff in ReadBuffDefinitions(file.Path, file.Source.Id).ToArray())
                {
                    AddCandidate(buffCandidates, buff.Id, buff);
                    verifiedBuffIds.Add(buff.Id);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                // An unreadable slot may redefine any earlier ID. Later whole
                // definitions can establish their values again in native order.
                verifiedBuffIds.Clear();
                issues.Add($"Failed to read buff definitions '{file.Path}': {ex.Message}");
            }
        }
        if (buffResolutionIssues.Count > 0 || ActiveContentResolver.HasUnresolvedModSources(activeContent) ||
            activeContent.Sources.Any(source => !Directory.Exists(source.Directory)))
        {
            // Unknown providers have no provable slot relative to the files above.
            verifiedBuffIds.Clear();
            issues.Add("Hero Buff provider discovery is incomplete; referenced attributes could not be verified.");
        }

        var campingReads = new OrderedDefinitionReadState(firstMatch: true, orderKnown: campingResolutionIssues.Count == 0);
        foreach (var file in campingFiles)
        {
            try
            {
                foreach (var skill in ReadCampingSkills(file.Path).ToArray())
                {
                    if (!campingSkills.TryGetValue(skill.Id, out var builder))
                    {
                        builder = new CampingSkillBuilder(skill.Id, skill.IsShared);
                        campingSkills[skill.Id] = builder;
                    }

                    builder.Add(skill);
                    campingReads.RecordDefinition(skill.Id);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                campingReads.RecordFailure();
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

        var eventReads = new OrderedDefinitionReadState(firstMatch: true, orderKnown: eventResolutionIssues.Count == 0);
        foreach (var file in eventFiles)
        {
            try
            {
                foreach (var eventGroup in ReadRecruitEvents(file.Path, file.Source.Id).ToArray())
                {
                    AddCandidate(eventCandidates, eventGroup.EventId, eventGroup);
                    // An empty first result still owns the event ID.
                    eventReads.RecordDefinition(eventGroup.EventId);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or EncoderFallbackException or DecoderFallbackException)
            {
                eventReads.RecordFailure();
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
            issues).Where(pair => effectReads.IsVerified(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        // Native quirk lookup (0x1404ABD00) walks the entire loaded vector and
        // returns the last matching ID. Different filenames do not imply ambiguity.
        var quirkHashCollisions = NativeResourceIdentity.FindCollisions(quirkCandidates.Values.SelectMany(group => group),
            quirk => quirk.Id, quirk => NativeResourceIdentity.HashCString(quirk.Id));
        if (quirkHashCollisions.Count > 0) issues.Add("Quirk IDs share native hashes and remain unresolved: " + string.Join(", ", quirkHashCollisions));
        var effectiveQuirks = quirkCandidates.Where(pair => !pair.Value.Any(quirk => quirkHashCollisions.Contains(quirk.Id)) && pair.Value.Select(value => value.Id)
                .Distinct(StringComparer.Ordinal).Count() == 1).ToDictionary(pair => pair.Key,
            pair => pair.Value[^1], StringComparer.Ordinal);
        var effectiveBuffs = ResolveOrderedDefinitions(
            buffCandidates,
            definition => definition.Id,
            definitions => definitions[^1],
            "Buff",
            issues,
            NativeResourceIdentity.HashCString);
        var verifiedQuirks = effectiveQuirks.Where(pair => quirkReads.IsVerified(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var quirksByHash = verifiedQuirks.Values.ToDictionary(quirk => NativeResourceIdentity.HashCString(quirk.Id));
        var buffsByHash = effectiveBuffs.Values.Where(buff => verifiedBuffIds.Contains(buff.Id))
            .ToDictionary(buff => NativeResourceIdentity.HashCString(buff.Id));
        var knownBuffHashes = buffCandidates.Keys.Select(NativeResourceIdentity.HashCString).ToHashSet();
        var effectiveUpgrades = ResolveHeroUpgradeTrees(upgradeFiles, issues, upgradeResolutionIssues.Count == 0);
        var resolveLevelThresholds = ReadEffectiveResolveLevelThresholds(rosterVariableFiles, enabledDlcPrefixes, issues);
        var effectiveEvents = ResolveOrderedDefinitions(
            eventCandidates,
            definition => definition.EventId,
            definitions => definitions[0],
            "Town event",
            issues);
        var recruitEvents = effectiveEvents.Values
            .Where(group => eventReads.IsVerified(group.EventId))
            .SelectMany(group => group.Recruits)
            .OrderBy(item => item.HeroClass, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var eventsByClass = recruitEvents
            .GroupBy(item => Loc2LocalizationReader.HashName(item.HeroClass))
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<HeroRecruitEventDefinition>)group.ToArray());

        var verifiedCampingSkills = campingSkills.Where(pair => campingReads.IsVerified(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var heroHashCollisions = NativeResourceIdentity.FindCollisions(heroIds);
        if (heroHashCollisions.Count > 0) issues.Add("Hero IDs share native hashes and remain unresolved: " + string.Join(", ", heroHashCollisions));
        var heroClasses = candidates
            .Values
            .Select(classCandidates => MergeHeroClass(
                classCandidates,
                heroOverridesByClass.TryGetValue(classCandidates[0].Id, out var classOverrides)
                    ? classOverrides
                    : [],
                eventsByClass,
                effectiveEffects,
                verifiedQuirks,
                verifiedCampingSkills,
                campingReads.CanProveAbsence,
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
                return BuildInitialQuirk(mergedQuirk, quirksByHash, buffsByHash, knownBuffHashes) with
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
            issues)
        {
            InitialQuirkLimits = initialQuirkLimits
        };
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
                issues.Add(EditorText.Format("HeroClassCatalog_001", hero.Id) +
                           (levels.Length == 0 ? EditorText.Get("InitialQuirkSelectionDialog_016") : string.Join(",", levels)) +
                           EditorText.Format("HeroClassCatalog_002", firstFailure.UnavailableReason));
            }
        }

        return catalog with { HeroClasses = validatedHeroes };
    }
}
