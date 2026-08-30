using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;

namespace DarkestDungeonSaveEditor.Core;

public static class HeroClassCatalog
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
                foreach (var quirk in ReadQuirkDefinitions(
                             file.Path,
                             file.Source.Id,
                             file.ProviderSources))
                {
                    AddCandidate(quirkCandidates, quirk.Id, quirk);
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
                foreach (var name in ContentLocalizationCatalog.ReadHeroNames(file.Path))
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
            issues);
        var effectiveUpgrades = ResolveUniqueDefinitions(
            upgradeCandidates,
            sourcesById,
            definition => definition.Source,
            definition => definition.SourcePath,
            GetHeroUpgradeSignature,
            "Hero upgrade",
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
                return BuildInitialQuirk(mergedQuirk, effectiveBuffs, buffCandidates) with
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
        return new HeroClassCatalogResult(
            activeContent.GameMode,
            resolveLevelThresholds,
            heroClasses,
            recruitEvents,
            initialQuirks,
            heroNames.OrderBy(name => name, StringComparer.Ordinal).ToArray(),
            issues);
    }

    private static IReadOnlyList<EffectiveContentFile> ResolveFiles(
        IReadOnlyList<SourceFiles> sourceFiles,
        Func<SourceFileSet, IReadOnlyList<string>> selectFiles,
        string contentLabel,
        List<string> issues)
    {
        return ContentFileOverlay.Resolve(
            sourceFiles.SelectMany(item =>
                selectFiles(item.Files).Select(path => new ContentFileCandidate(item.Source, path))),
            contentLabel,
            issues);
    }

    private static void AddCandidate<T>(
        Dictionary<string, List<T>> candidates,
        string id,
        T candidate)
    {
        if (!candidates.TryGetValue(id, out var matches))
        {
            matches = [];
            candidates[id] = matches;
        }

        matches.Add(candidate);
    }

    private static IReadOnlyDictionary<string, T> ResolveUniqueDefinitions<T>(
        Dictionary<string, List<T>> candidates,
        IReadOnlyDictionary<string, ActiveContentSource> sourcesById,
        Func<T, string> getSource,
        Func<T, string> getSourcePath,
        Func<T, string> getSemanticSignature,
        string contentLabel,
        List<string> issues)
    {
        var result = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in candidates)
        {
            var effective = SelectEffectiveDefinitions(
                pair.Value,
                sourcesById,
                getSource,
                getSourcePath,
                getSemanticSignature);
            pair.Value.Clear();
            pair.Value.AddRange(effective);
            if (effective.Count == 1)
            {
                result[pair.Key] = effective[0];
                continue;
            }

            issues.Add(
                $"{contentLabel} '{pair.Key}' has conflicting definitions at the same effective priority and was left unresolved: " +
                string.Join(
                    " | ",
                    effective.Select(candidate => $"{getSource(candidate)}:{getSourcePath(candidate)}")));
        }

        return result;
    }

    private static IReadOnlyList<T> SelectEffectiveDefinitions<T>(
        IReadOnlyList<T> candidates,
        IReadOnlyDictionary<string, ActiveContentSource> sourcesById,
        Func<T, string> getSource,
        Func<T, string> getSourcePath,
        Func<T, string> getSemanticSignature)
    {
        var lastDeclarationByPath = candidates
            .GroupBy(getSourcePath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToArray();
        if (lastDeclarationByPath.Length <= 1)
        {
            return lastDeclarationByPath;
        }

        var highestPrioritySource = sourcesById[getSource(lastDeclarationByPath[0])];
        foreach (var candidate in lastDeclarationByPath.Skip(1))
        {
            var source = sourcesById[getSource(candidate)];
            if (ContentFileOverlay.ComparePriority(source, highestPrioritySource) > 0)
            {
                highestPrioritySource = source;
            }
        }

        var highestPriorityCandidates = lastDeclarationByPath
            .Where(candidate => ContentFileOverlay.ComparePriority(
                sourcesById[getSource(candidate)],
                highestPrioritySource) == 0)
            .ToArray();
        return highestPriorityCandidates
            .Select(getSemanticSignature)
            .Distinct(StringComparer.Ordinal)
            .Skip(1)
            .Any()
            ? highestPriorityCandidates
            : [highestPriorityCandidates[^1]];
    }

    private static string GetQuirkSignature(QuirkDefinition definition) => JsonSerializer.Serialize(new
    {
        definition.RandomChance,
        definition.IsPositive,
        definition.IsDisease,
        IncompatibleQuirks = SortSemanticValues(definition.IncompatibleQuirks),
        BuffIds = SortSemanticValues(definition.BuffIds),
        Tags = SortSemanticValues(definition.Tags),
        definition.RosterLimit,
        EvolutionSignature = definition.Evolution?.Signature
    });

    private static string GetBuffSignature(BuffDefinition definition) => JsonSerializer.Serialize(new
    {
        definition.StatType,
        definition.StatSubType,
        definition.Amount,
        definition.RuleType,
        definition.IsFalseRule
    });

    private static string GetHeroUpgradeSignature(HeroUpgradeDefinition definition) => JsonSerializer.Serialize(new
    {
        Weapon = definition.WeaponRequirements.OrderBy(pair => pair.Key, StringComparer.Ordinal),
        Armour = definition.ArmourRequirements.OrderBy(pair => pair.Key, StringComparer.Ordinal),
        Trees = definition.Trees
            .OrderBy(tree => tree.Id, StringComparer.Ordinal)
            .Select(tree => new
            {
                tree.Id,
                tree.Kind,
                Requirements = tree.Requirements
                    .OrderBy(requirement => requirement.PrerequisiteResolveLevel)
                    .ThenBy(requirement => requirement.Code, StringComparer.Ordinal)
            })
    });

    private static string GetRecruitEventSignature(RecruitEventGroup definition) => JsonSerializer.Serialize(
        definition.Recruits
            .OrderBy(recruit => recruit.HeroClass, StringComparer.OrdinalIgnoreCase)
            .ThenBy(recruit => recruit.Count)
            .Select(recruit => new { recruit.HeroClass, recruit.Count }));

    private static string GetHeroCandidateSignature(HeroCandidate candidate) => JsonSerializer.Serialize(new
    {
        candidate.CanSelectCombatSkills,
        candidate.SelectedCombatSkillsMax,
        candidate.Generation,
        candidate.BaseHp,
        candidate.WeaponRanks,
        candidate.ArmourRanks,
        candidate.ColourVariationCount,
        CombatSkillIds = SortSemanticValues(candidate.CombatSkillIds),
        GuaranteedCombatSkillIds = SortSemanticValues(candidate.GuaranteedCombatSkillIds),
        IncompatibleInitialQuirkIds = SortSemanticValues(candidate.IncompatibleInitialQuirkIds),
        SkillEffects = candidate.SkillEffects
            .OrderBy(reference => reference.SkillId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(reference => reference.AttributeKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(reference => reference.EffectName, StringComparer.OrdinalIgnoreCase)
            .ToArray()
    });

    private static string[] SortSemanticValues(IEnumerable<string> values) => values
        .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static IReadOnlyList<int> ReadEffectiveResolveLevelThresholds(
        IReadOnlyList<EffectiveContentFile> files,
        List<string> issues)
    {
        var matches = files
            .Where(file => file.RelativePath.Equals(
                "campaign/roster/roster.variables.json",
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matches.Length == 0)
        {
            issues.Add("No effective campaign/roster/roster.variables.json was found; only level-zero hero generation is available.");
            return [];
        }

        if (matches.Length > 1)
        {
            issues.Add(
                "Multiple effective root roster.variables files were found; only level-zero hero generation is available: " +
                string.Join(" | ", matches.Select(file => file.Path)));
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(
                File.ReadAllBytes(matches[0].Path),
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip
                });
            if (!document.RootElement.TryGetProperty("resolve_level_thresholds", out var thresholdsNode) ||
                thresholdsNode.ValueKind != JsonValueKind.Array)
            {
                issues.Add($"Resolve level thresholds are missing from '{matches[0].Path}'.");
                return [];
            }

            var thresholds = new List<int>();
            foreach (var item in thresholdsNode.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Number || !item.TryGetInt32(out var threshold))
                {
                    issues.Add($"Resolve level thresholds contain a non-integer value in '{matches[0].Path}'.");
                    return [];
                }

                thresholds.Add(threshold);
            }

            if (thresholds.Count == 0 || thresholds[0] != 0 ||
                thresholds.Any(value => value < 0) ||
                thresholds.Zip(thresholds.Skip(1), (left, right) => right > left).Any(increasing => !increasing))
            {
                issues.Add($"Resolve level thresholds are not a strictly increasing sequence beginning at zero in '{matches[0].Path}'.");
                return [];
            }

            return thresholds;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            issues.Add($"Failed to read resolve level thresholds '{matches[0].Path}': {ex.Message}");
            return [];
        }
    }

    private static HeroUpgradeDefinition ReadHeroUpgrade(EffectiveContentFile file)
    {
        var fileName = Path.GetFileName(file.Path);
        var heroClassId = fileName.EndsWith(HeroUpgradeSuffix, StringComparison.OrdinalIgnoreCase)
            ? fileName[..^HeroUpgradeSuffix.Length]
            : Path.GetFileNameWithoutExtension(fileName);
        var weaponRequirements = new Dictionary<string, int>(StringComparer.Ordinal);
        var armourRequirements = new Dictionary<string, int>(StringComparer.Ordinal);
        var upgradeTrees = new List<HeroUpgradeTreeDefinition>();
        using var document = JsonDocument.Parse(
            File.ReadAllBytes(file.Path),
            new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });
        if (document.RootElement.TryGetProperty("trees", out var trees) &&
            trees.ValueKind == JsonValueKind.Array)
        {
            foreach (var tree in trees.EnumerateArray())
            {
                if (tree.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var tags = ReadJsonStringArray(tree, "tags");
                HeroUpgradeTreeKind? kind = tags.Contains("weapon", StringComparer.OrdinalIgnoreCase)
                    ? HeroUpgradeTreeKind.Weapon
                    : tags.Contains("armour", StringComparer.OrdinalIgnoreCase)
                        ? HeroUpgradeTreeKind.Armour
                        : tags.Contains("combat_skill", StringComparer.OrdinalIgnoreCase)
                            ? HeroUpgradeTreeKind.CombatSkill
                            : null;
                if (kind is null)
                {
                    continue;
                }

                var treeId = ReadJsonString(tree, "id");
                if (string.IsNullOrWhiteSpace(treeId))
                {
                    throw new InvalidDataException(
                        $"A {string.Join('/', tags)} upgrade tree is missing its id.");
                }
                if (!tree.TryGetProperty("requirements", out var requirements) ||
                    requirements.ValueKind != JsonValueKind.Array)
                {
                    throw new InvalidDataException(
                        $"Upgrade tree '{treeId}' is missing its requirements array.");
                }

                var parsedRequirements = new Dictionary<string, int>(StringComparer.Ordinal);

                foreach (var requirement in requirements.EnumerateArray())
                {
                    var code = ReadJsonString(requirement, "code");
                    var prerequisiteLevel = ReadJsonInt(requirement, "prerequisite_resolve_level");
                    if (string.IsNullOrWhiteSpace(code) || prerequisiteLevel is null or < 0)
                    {
                        throw new InvalidDataException(
                            $"A {string.Join('/', tags)} requirement is missing code or prerequisite_resolve_level.");
                    }
                    if (code.Length != 1 || code[0] > 0x7F)
                    {
                        throw new InvalidDataException(
                            $"Upgrade tree '{treeId}' requirement code '{code}' cannot be represented " +
                            "losslessly by persist.upgrades; one ASCII character is required.");
                    }

                    if (parsedRequirements.TryGetValue(code, out var existing) && existing != prerequisiteLevel.Value)
                    {
                        throw new InvalidDataException(
                            $"Upgrade tree '{treeId}' requirement '{code}' has conflicting resolve prerequisites " +
                            $"{existing} and {prerequisiteLevel.Value}.");
                    }

                    parsedRequirements[code] = prerequisiteLevel.Value;
                }

                if (upgradeTrees.Any(existing =>
                        existing.Id.Equals(treeId, StringComparison.Ordinal)))
                {
                    throw new InvalidDataException($"Upgrade tree '{treeId}' is defined more than once.");
                }

                upgradeTrees.Add(new HeroUpgradeTreeDefinition(
                    treeId,
                    kind.Value,
                    parsedRequirements
                        .Select(pair => new HeroUpgradeRequirementDefinition(pair.Key, pair.Value))
                        .OrderBy(requirement => requirement.PrerequisiteResolveLevel)
                        .ThenBy(requirement => requirement.Code, StringComparer.Ordinal)
                        .ToArray()));

                var target = kind.Value switch
                {
                    HeroUpgradeTreeKind.Weapon => weaponRequirements,
                    HeroUpgradeTreeKind.Armour => armourRequirements,
                    _ => null
                };
                if (target is null)
                {
                    continue;
                }

                foreach (var requirement in parsedRequirements)
                {
                    if (target.TryGetValue(requirement.Key, out var existing) &&
                        existing != requirement.Value)
                    {
                        throw new InvalidDataException(
                            $"Upgrade requirement '{requirement.Key}' has conflicting resolve prerequisites " +
                            $"{existing} and {requirement.Value}.");
                    }

                    target[requirement.Key] = requirement.Value;
                }
            }
        }

        return new HeroUpgradeDefinition(
            heroClassId,
            weaponRequirements,
            armourRequirements,
            upgradeTrees,
            file.Source.Id,
            Path.GetFullPath(file.Path));
    }

    private static HeroProgressionBuildResult BuildHeroProgression(
        HeroCandidate hero,
        HeroUpgradeDefinition? upgrade,
        IReadOnlyList<int> resolveLevelThresholds)
    {
        var armourRankZero = hero.ArmourRanks.FirstOrDefault(rank => rank.Rank == 0);
        var levelZeroHp = armourRankZero?.Hp ?? hero.BaseHp;
        var fallback = levelZeroHp is > 0 and < double.PositiveInfinity
            ? new[] { new HeroLevelProfile(0, 0, 0, 0, levelZeroHp.Value) }
            : [];
        if (levelZeroHp is not (> 0 and < double.PositiveInfinity))
        {
            return new HeroProgressionBuildResult([], "缺少可验证的 0 级护甲 HP");
        }

        if (resolveLevelThresholds.Count == 0)
        {
            return new HeroProgressionBuildResult(fallback, "缺少有效的 resolve_level_thresholds");
        }

        var weapon = BuildEquipmentProgression(
            "武器",
            hero.WeaponRanks,
            upgrade?.WeaponRequirements,
            requireHp: false);
        var armour = BuildEquipmentProgression(
            "护甲",
            hero.ArmourRanks,
            upgrade?.ArmourRequirements,
            requireHp: true);
        var reason = string.Join(
            "；",
            new[] { weapon.UnsupportedReason, armour.UnsupportedReason }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
        if (!string.IsNullOrWhiteSpace(reason))
        {
            return new HeroProgressionBuildResult(fallback, reason);
        }

        var profiles = resolveLevelThresholds
            .Select((xp, level) =>
            {
                var weaponRank = weapon.Ranks
                    .Where(rank => rank.MinimumResolveLevel <= level)
                    .MaxBy(rank => rank.Rank)!;
                var armourRank = armour.Ranks
                    .Where(rank => rank.MinimumResolveLevel <= level)
                    .MaxBy(rank => rank.Rank)!;
                return new HeroLevelProfile(
                    level,
                    xp,
                    weaponRank.Rank,
                    armourRank.Rank,
                    armourRank.Hp!.Value);
            })
            .ToArray();
        return new HeroProgressionBuildResult(profiles, string.Empty);
    }

    private static EquipmentProgressionBuildResult BuildEquipmentProgression(
        string label,
        IReadOnlyList<HeroEquipmentRank> sourceRanks,
        IReadOnlyDictionary<string, int>? requirements,
        bool requireHp)
    {
        var ranks = sourceRanks.Count == 0 && !requireHp
            ? new[] { new HeroEquipmentRank(0, string.Empty, null) }
            : sourceRanks.OrderBy(rank => rank.Rank).ToArray();
        if (ranks.Length == 0 || ranks[0].Rank != 0)
        {
            return new EquipmentProgressionBuildResult([], $"{label}缺少 rank 0 定义");
        }

        for (var index = 0; index < ranks.Length; index++)
        {
            if (ranks[index].Rank != index)
            {
                return new EquipmentProgressionBuildResult([], $"{label} rank 必须从 0 连续定义");
            }

            if (requireHp && ranks[index].Hp is not (> 0 and < double.PositiveInfinity))
            {
                return new EquipmentProgressionBuildResult([], $"{label} rank {index} 缺少有效 HP");
            }
        }

        if (ranks.Length == 1 && (requirements is null || requirements.Count == 0))
        {
            return new EquipmentProgressionBuildResult(
                [new ResolvedEquipmentRank(0, 0, ranks[0].Hp)],
                string.Empty);
        }

        if (requirements is null || requirements.Count == 0)
        {
            return new EquipmentProgressionBuildResult([], $"{label}有多个 rank，但缺少有效 upgrade 模板");
        }

        var resolved = new List<ResolvedEquipmentRank>
        {
            new(0, 0, ranks[0].Hp)
        };
        var usedCodes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rank in ranks.Skip(1))
        {
            if (string.IsNullOrWhiteSpace(rank.RequirementCode) ||
                !requirements.TryGetValue(rank.RequirementCode, out var prerequisiteLevel))
            {
                return new EquipmentProgressionBuildResult(
                    [],
                    $"{label} rank {rank.Rank} 的 upgradeRequirementCode '{rank.RequirementCode}' 无法解析");
            }

            usedCodes.Add(rank.RequirementCode);
            resolved.Add(new ResolvedEquipmentRank(rank.Rank, prerequisiteLevel, rank.Hp));
        }

        var unusedCodes = requirements.Keys.Where(code => !usedCodes.Contains(code)).ToArray();
        if (unusedCodes.Length > 0)
        {
            return new EquipmentProgressionBuildResult(
                [],
                $"{label} upgrade 模板存在未对应 rank 的代码：{string.Join(", ", unusedCodes)}");
        }

        return new EquipmentProgressionBuildResult(resolved, string.Empty);
    }

    private static HeroClassDefinition MergeHeroClass(
        IReadOnlyList<HeroCandidate> candidates,
        IReadOnlyList<EffectiveContentFile> overrideFiles,
        IReadOnlyDictionary<string, ActiveContentSource> sourcesById,
        IReadOnlyDictionary<string, IReadOnlyList<HeroRecruitEventDefinition>> eventsByClass,
        IReadOnlyDictionary<string, EffectQuirkAssignment> effectiveEffects,
        IReadOnlyDictionary<string, QuirkDefinition> effectiveQuirks,
        IReadOnlyDictionary<string, CampingSkillBuilder> campingSkills,
        IReadOnlyDictionary<string, HeroUpgradeDefinition> effectiveUpgrades,
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
        effectiveUpgrades.TryGetValue(selected.Id, out var upgrade);
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
            upgrade?.Trees ?? [],
            progression.UnsupportedReason,
            selected.ColourVariationCount,
            selected.CombatSkillIds.ToArray(),
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
            sources);
    }

    private static HeroCandidate ApplyHeroOverrides(
        HeroCandidate selected,
        IReadOnlyList<EffectiveContentFile> overrideFiles,
        IReadOnlyDictionary<string, ActiveContentSource> sourcesById)
    {
        if (overrideFiles.Count == 0 || !sourcesById.TryGetValue(selected.Source, out var selectedSource))
        {
            return selected;
        }

        var applicationComparer = Comparer<ActiveContentSource>.Create(ContentFileOverlay.ComparePriority);
        var applicableOverrides = overrideFiles
            .Where(file => ContentFileOverlay.ComparePriority(file.Source, selectedSource) >= 0)
            .OrderBy(file => file.Source, applicationComparer)
            .ThenBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
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
                .SelectMany(file => file.ProviderPaths)
                .Select(providerPath => CountColourVariations(providerPath, selected.Id))
                .DefaultIfEmpty(0)
                .Max());
        return builder.Build();
    }

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
        HeroMaxHpModifier? maxHpModifier = null;
        if (hpBuffs.Length > 1)
        {
            unverifiedReasons.Add("同一怪癖包含多个 max_hp Buff，叠加方式尚未验证");
        }
        else if (hpBuffs.Length == 1)
        {
            var buff = hpBuffs[0];
            var hasSupportedRule =
                buff.RuleType.Equals("always", StringComparison.OrdinalIgnoreCase) ||
                buff.RuleType.Equals("no_trinkets", StringComparison.OrdinalIgnoreCase);
            if (!buff.StatType.Equals("combat_stat_multiply", StringComparison.OrdinalIgnoreCase) ||
                buff.Amount is null ||
                buff.IsFalseRule != false ||
                !hasSupportedRule)
            {
                unverifiedReasons.Add($"max_hp Buff '{buff.Id}' 使用了尚未验证的规则");
            }
            else
            {
                maxHpModifier = new HeroMaxHpModifier(buff.Id, buff.Amount.Value, buff.RuleType);
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
            maxHpModifier,
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

    private static HeroCandidate ReadHeroInfo(EffectiveContentFile file)
    {
        var path = file.Path;
        var id = ReadHeroClassId(path, HeroInfoSuffix);
        var builder = new HeroCandidateBuilder(
            id,
            file.Source.Id,
            Path.GetFullPath(path),
            file.ProviderSources);
        ApplyHeroDefinitionFile(builder, path);
        builder.ColourVariationCount = file.ProviderPaths
            .Select(providerPath => CountColourVariations(providerPath, id))
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
                if (level is not null and not 0)
                {
                    continue;
                }

                var skillId = ReadString(attributes, "id");
                if (string.IsNullOrWhiteSpace(skillId))
                {
                    continue;
                }

                builder.AddCombatSkill(skillId);
                if (ReadBooleanFlag(attributes, "generation_guaranteed") is { } generationGuaranteed)
                {
                    builder.SetGuaranteedCombatSkill(skillId, generationGuaranteed);
                }

                foreach (var pair in attributes.Where(pair =>
                             pair.Key.Equals("effect", StringComparison.OrdinalIgnoreCase) ||
                             pair.Key.EndsWith("_effects", StringComparison.OrdinalIgnoreCase)))
                {
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

            yield return new BuffDefinition(
                id,
                ReadJsonString(item, "stat_type"),
                ReadJsonString(item, "stat_sub_type"),
                ReadJsonDouble(item, "amount"),
                ReadJsonString(item, "rule_type"),
                ReadJsonBoolean(item, "is_false_rule"),
                source,
                Path.GetFullPath(path));
        }
    }

    private static IEnumerable<CampingSkillDefinition> ReadCampingSkills(string path)
    {
        using var document = JsonDocument.Parse(
            File.ReadAllBytes(path),
            new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });
        var threshold = int.MaxValue;
        if (document.RootElement.TryGetProperty("configuration", out var configuration))
        {
            threshold = ReadJsonInt(configuration, "class_specific_number_of_classes_threshold") ?? threshold;
        }

        if (!document.RootElement.TryGetProperty("skills", out var skills) ||
            skills.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var item in skills.EnumerateArray())
        {
            var id = ReadJsonString(item, "id");
            var heroClasses = ReadJsonStringArray(item, "hero_classes");
            if (string.IsNullOrWhiteSpace(id) || heroClasses.Count == 0)
            {
                continue;
            }

            var isCanonicalShared = id is "encourage" or "first_aid" or "pep_talk";
            yield return new CampingSkillDefinition(
                id,
                heroClasses,
                isCanonicalShared || heroClasses.Count > threshold);
        }
    }

    private static int CountColourVariations(string heroInfoPath, string heroClass)
    {
        var directory = Path.GetDirectoryName(heroInfoPath);
        if (directory is null || !Directory.Exists(directory))
        {
            return 0;
        }

        var suffixes = Directory.EnumerateDirectories(directory, $"{heroClass}_*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
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

    private static IEnumerable<RecruitEventGroup> ReadRecruitEvents(string path, string source)
    {
        using var document = JsonDocument.Parse(
            File.ReadAllBytes(path),
            new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });
        if (!document.RootElement.TryGetProperty("events", out var events) ||
            events.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var eventNode in events.EnumerateArray())
        {
            if (eventNode.ValueKind != JsonValueKind.Object ||
                !eventNode.TryGetProperty("id", out var idNode) ||
                idNode.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(idNode.GetString()))
            {
                continue;
            }

            var eventId = idNode.GetString()!;
            var recruits = new List<HeroRecruitEventDefinition>();
            if (eventNode.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var dataNode in data.EnumerateArray())
                {
                    if (dataNode.ValueKind != JsonValueKind.Object ||
                        !ReadJsonString(dataNode, "type").Equals("bonus_recruit", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var heroClass = ReadJsonString(dataNode, "string_data");
                    if (string.IsNullOrWhiteSpace(heroClass))
                    {
                        continue;
                    }

                    recruits.Add(new HeroRecruitEventDefinition(
                        eventId,
                        heroClass,
                        ReadJsonDouble(dataNode, "number_data"),
                        source,
                        Path.GetFullPath(path)));
                }
            }

            yield return new RecruitEventGroup(eventId, recruits, source, Path.GetFullPath(path));
        }
    }

    private static SourceFileSet EnumerateSourceFiles(
        ActiveContentSource source,
        IReadOnlyList<string> enabledDlcPrefixes,
        List<string> issues)
    {
        if (source.Kind is "workshop" or "local")
        {
            var manifestPath = Path.Combine(source.Directory, "modfiles.txt");
            if (File.Exists(manifestPath))
            {
                return EnumerateManifestFiles(source.Directory, manifestPath, enabledDlcPrefixes, issues);
            }

            issues.Add($"Mod has no modfiles.txt; standard fallback scan used: {source.Directory}");
        }

        return new SourceFileSet(
            EnumerateFiles(source.Directory, "heroes", $"*{HeroInfoSuffix}"),
            EnumerateFiles(source.Directory, "heroes", $"*{HeroOverrideSuffix}"),
            EnumerateFiles(source.Directory, "effects", "*.effects.darkest"),
            EnumerateFiles(source.Directory, Path.Combine("shared", "quirk"), "*quirk_library.json"),
            EnumerateFiles(source.Directory, Path.Combine("campaign", "town_events"), "*.events.json"),
            EnumerateFiles(source.Directory, Path.Combine("shared", "buffs"), "*.buffs.json"),
            EnumerateFiles(source.Directory, Path.Combine("raid", "camping"), "*.camping_skills.json"),
            EnumerateFiles(source.Directory, "localization", "*.string_table.xml"),
            EnumerateHeroUpgradeFiles(source.Directory),
            EnumerateFiles(source.Directory, Path.Combine("campaign", "roster"), "roster.variables.json"));
    }

    private static SourceFileSet EnumerateManifestFiles(
        string root,
        string manifestPath,
        IReadOnlyList<string> enabledDlcPrefixes,
        List<string> issues)
    {
        var heroFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var heroOverrideFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var effectFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var quirkFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var eventFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var buffFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var campingFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var nameFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var upgradeFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rosterVariableFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in File.ReadLines(manifestPath))
        {
            var relativePath = ExtractManifestPath(rawLine);
            if (relativePath is null)
            {
                continue;
            }

            var path = Path.GetFullPath(Path.Combine(root, relativePath));
            var relativeToRoot = Path.GetRelativePath(root, path);
            if (Path.IsPathRooted(relativeToRoot) ||
                relativeToRoot.Equals("..", StringComparison.Ordinal) ||
                relativeToRoot.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                issues.Add($"Ignored hero catalog manifest path outside its Mod directory: {rawLine.Trim()}");
                continue;
            }

            var normalizedRelative = relativeToRoot.Replace('\\', '/');
            HashSet<string>? target = null;
            if (ContentFileOverlay.IsRootOrEnabledDlcPath(normalizedRelative, "heroes", enabledDlcPrefixes) &&
                normalizedRelative.EndsWith(HeroInfoSuffix, StringComparison.OrdinalIgnoreCase))
            {
                target = heroFiles;
            }
            else if (ContentFileOverlay.IsRootOrEnabledDlcPath(normalizedRelative, "heroes", enabledDlcPrefixes) &&
                     normalizedRelative.EndsWith(HeroOverrideSuffix, StringComparison.OrdinalIgnoreCase))
            {
                target = heroOverrideFiles;
            }
            else if (ContentFileOverlay.IsRootOrEnabledDlcPath(normalizedRelative, "effects", enabledDlcPrefixes) &&
                     normalizedRelative.EndsWith(".effects.darkest", StringComparison.OrdinalIgnoreCase))
            {
                target = effectFiles;
            }
            else if (ContentFileOverlay.IsRootOrEnabledDlcPath(normalizedRelative, "shared/quirk", enabledDlcPrefixes) &&
                     normalizedRelative.EndsWith("quirk_library.json", StringComparison.OrdinalIgnoreCase))
            {
                target = quirkFiles;
            }
            else if (ContentFileOverlay.IsRootOrEnabledDlcPath(normalizedRelative, "campaign/town_events", enabledDlcPrefixes) &&
                     normalizedRelative.EndsWith(".events.json", StringComparison.OrdinalIgnoreCase))
            {
                target = eventFiles;
            }
            else if (ContentFileOverlay.IsRootOrEnabledDlcPath(normalizedRelative, "shared/buffs", enabledDlcPrefixes) &&
                     normalizedRelative.EndsWith(".buffs.json", StringComparison.OrdinalIgnoreCase))
            {
                target = buffFiles;
            }
            else if (ContentFileOverlay.IsRootOrEnabledDlcPath(normalizedRelative, "raid/camping", enabledDlcPrefixes) &&
                     normalizedRelative.EndsWith(".camping_skills.json", StringComparison.OrdinalIgnoreCase))
            {
                target = campingFiles;
            }
            else if (ContentFileOverlay.IsRootOrEnabledDlcPath(normalizedRelative, "localization", enabledDlcPrefixes) &&
                     normalizedRelative.EndsWith(".string_table.xml", StringComparison.OrdinalIgnoreCase))
            {
                target = nameFiles;
            }
            else if (IsHeroUpgradeManifestPath(normalizedRelative, enabledDlcPrefixes) &&
                      normalizedRelative.EndsWith(HeroUpgradeSuffix, StringComparison.OrdinalIgnoreCase))
            {
                target = upgradeFiles;
            }
            else if (ContentFileOverlay.IsRootOrEnabledDlcPath(normalizedRelative, "campaign/roster", enabledDlcPrefixes) &&
                     normalizedRelative.EndsWith("roster.variables.json", StringComparison.OrdinalIgnoreCase))
            {
                target = rosterVariableFiles;
            }

            if (target is null)
            {
                continue;
            }

            if (!File.Exists(path))
            {
                issues.Add($"Hero catalog file listed by Mod is missing: {path}");
                continue;
            }

            target.Add(path);
        }

        return new SourceFileSet(
            SortPaths(heroFiles),
            SortPaths(heroOverrideFiles),
            SortPaths(effectFiles),
            SortPaths(quirkFiles),
            SortPaths(eventFiles),
            SortPaths(buffFiles),
            SortPaths(campingFiles),
            SortPaths(nameFiles),
            SortPaths(upgradeFiles),
            SortPaths(rosterVariableFiles));
    }

    private static string? ExtractManifestPath(string rawLine)
    {
        return ModManifestPath.Extract(
            rawLine,
            HeroInfoSuffix,
            HeroOverrideSuffix,
            ".effects.darkest",
            "quirk_library.json",
            ".events.json",
            ".buffs.json",
            ".camping_skills.json",
            ".string_table.xml",
            HeroUpgradeSuffix,
            "roster.variables.json");
    }

    private static bool IsHeroUpgradeManifestPath(
        string normalizedRelative,
        IReadOnlyList<string> enabledDlcPrefixes)
    {
        if (ContentFileOverlay.IsRootOrEnabledDlcPath(
                normalizedRelative,
                "upgrades/heroes",
                enabledDlcPrefixes))
        {
            return true;
        }

        var separator = normalizedRelative.LastIndexOf('/');
        if (separator < 0)
        {
            return false;
        }

        var directory = normalizedRelative[..separator];
        return directory.Equals("upgrades", StringComparison.OrdinalIgnoreCase) ||
               enabledDlcPrefixes.Any(prefix => directory.Equals(
                   $"{prefix.TrimEnd('/')}/upgrades",
                   StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> EnumerateHeroUpgradeFiles(string root)
    {
        var result = new HashSet<string>(
            EnumerateFiles(root, Path.Combine("upgrades", "heroes"), $"*{HeroUpgradeSuffix}"),
            StringComparer.OrdinalIgnoreCase);
        var upgradeRoot = Path.Combine(root, "upgrades");
        if (Directory.Exists(upgradeRoot))
        {
            result.UnionWith(Directory.EnumerateFiles(
                upgradeRoot,
                $"*{HeroUpgradeSuffix}",
                SearchOption.TopDirectoryOnly));
        }

        return SortPaths(result);
    }

    private static IReadOnlyList<string> EnumerateFiles(string root, string relativeDirectory, string pattern)
    {
        var directory = Path.Combine(root, relativeDirectory);
        return Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, pattern, SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : [];
    }

    private static IReadOnlyList<string> SortPaths(IEnumerable<string> paths)
    {
        return paths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> ParseAttributes(string value)
    {
        var tokens = Tokenize(value);
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];
            if (!token.StartsWith(".", StringComparison.Ordinal) || token.Length <= 1)
            {
                continue;
            }

            var key = token[1..];
            var values = new List<string>();
            while (index + 1 < tokens.Count && !tokens[index + 1].StartsWith(".", StringComparison.Ordinal))
            {
                index++;
                values.Add(tokens[index]);
            }

            result[key] = values;
        }

        return result;
    }

    private static IReadOnlyList<string> Tokenize(string value)
    {
        var tokens = new List<string>();
        for (var index = 0; index < value.Length;)
        {
            while (index < value.Length && char.IsWhiteSpace(value[index]))
            {
                index++;
            }

            if (index >= value.Length)
            {
                break;
            }

            if (value[index] == '"')
            {
                index++;
                var start = index;
                while (index < value.Length && value[index] != '"')
                {
                    index++;
                }

                tokens.Add(value[start..Math.Min(index, value.Length)]);
                if (index < value.Length)
                {
                    index++;
                }

                continue;
            }

            var tokenStart = index;
            while (index < value.Length && !char.IsWhiteSpace(value[index]) && value[index] != '"')
            {
                index++;
            }

            tokens.Add(value[tokenStart..index]);
        }

        return tokens;
    }

    private static string? ReadString(
        IReadOnlyDictionary<string, IReadOnlyList<string>> attributes,
        string key)
    {
        return attributes.TryGetValue(key, out var values) && values.Count > 0
            ? values[0]
            : null;
    }

    private static int? ReadInt(
        IReadOnlyDictionary<string, IReadOnlyList<string>> attributes,
        string key)
    {
        return ReadString(attributes, key) is { } value &&
               int.TryParse(value.TrimEnd('%'), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static double? ReadDouble(
        IReadOnlyDictionary<string, IReadOnlyList<string>> attributes,
        string key)
    {
        return ReadString(attributes, key) is { } value &&
               double.TryParse(value.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static bool? ReadBooleanFlag(
        IReadOnlyDictionary<string, IReadOnlyList<string>> attributes,
        string key)
    {
        if (!attributes.TryGetValue(key, out var values))
        {
            return null;
        }

        if (values.Count == 0)
        {
            return true;
        }

        if (bool.TryParse(values[0], out var parsed))
        {
            return parsed;
        }

        return values[0] switch
        {
            "1" => true,
            "0" => false,
            _ => null
        };
    }

    private static string ReadJsonString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()?.Trim() ?? string.Empty
            : string.Empty;
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

    private static double? ReadJsonDouble(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.Number &&
               property.TryGetDouble(out var value)
            ? value
            : null;
    }

    private static int? ReadJsonInt(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.Number &&
               property.TryGetInt32(out var value)
            ? value
            : null;
    }

    private static bool? ReadJsonBoolean(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? property.GetBoolean()
            : null;
    }

    private static IReadOnlyList<string> ReadJsonStringArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return property.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
            .Select(item => item.GetString()!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private sealed class HeroCandidateBuilder
    {
        private bool _hasGeneration;
        private bool? _isGenerationEnabled;
        private int? _positiveQuirksMin;
        private int? _positiveQuirksMax;
        private int? _negativeQuirksMin;
        private int? _negativeQuirksMax;
        private int? _classCampingSkills;
        private int? _sharedCampingSkills;
        private int? _randomCombatSkills;
        private int? _cardsInDeck;
        private double? _cardChance;
        private string _townEventDependency = string.Empty;
        private readonly List<string> _combatSkillIds = [];
        private readonly HashSet<string> _combatSkillSet = new(StringComparer.Ordinal);
        private readonly List<string> _guaranteedCombatSkillIds = [];
        private readonly HashSet<string> _guaranteedCombatSkillSet = new(StringComparer.Ordinal);
        private readonly Dictionary<int, HeroEquipmentRank> _weaponRanks = [];
        private readonly Dictionary<int, HeroEquipmentRank> _armourRanks = [];
        private readonly Dictionary<string, Dictionary<string, List<string>>> _skillEffects =
            new(StringComparer.Ordinal);

        public HeroCandidateBuilder(
            string id,
            string source,
            string sourcePath,
            IReadOnlyList<string> providerSources)
        {
            Id = id;
            Source = source;
            SourcePath = sourcePath;
            ProviderSources = providerSources;
        }

        public HeroCandidateBuilder(
            HeroCandidate candidate,
            IReadOnlyList<string> providerSources)
            : this(candidate.Id, candidate.Source, candidate.SourcePath, providerSources)
        {
            CanSelectCombatSkills = candidate.CanSelectCombatSkills;
            SelectedCombatSkillsMax = candidate.SelectedCombatSkillsMax;
            BaseHp = candidate.BaseHp;
            ColourVariationCount = candidate.ColourVariationCount;
            if (candidate.Generation is { } generation)
            {
                _hasGeneration = true;
                _isGenerationEnabled = generation.IsEnabled;
                _positiveQuirksMin = generation.PositiveQuirksMin;
                _positiveQuirksMax = generation.PositiveQuirksMax;
                _negativeQuirksMin = generation.NegativeQuirksMin;
                _negativeQuirksMax = generation.NegativeQuirksMax;
                _classCampingSkills = generation.ClassCampingSkills;
                _sharedCampingSkills = generation.SharedCampingSkills;
                _randomCombatSkills = generation.RandomCombatSkills;
                _cardsInDeck = generation.CardsInDeck;
                _cardChance = generation.CardChance;
                _townEventDependency = generation.TownEventDependency;
            }

            foreach (var skillId in candidate.CombatSkillIds)
            {
                AddCombatSkill(skillId);
            }

            foreach (var skillId in candidate.GuaranteedCombatSkillIds)
            {
                AddGuaranteedCombatSkill(skillId);
            }

            foreach (var rank in candidate.WeaponRanks)
            {
                _weaponRanks[rank.Rank] = rank;
            }

            foreach (var rank in candidate.ArmourRanks)
            {
                _armourRanks[rank.Rank] = rank;
            }

            AddIncompatibleInitialQuirks(candidate.IncompatibleInitialQuirkIds);
            foreach (var reference in candidate.SkillEffects)
            {
                AddSkillEffect(reference.SkillId, reference.AttributeKey, reference.EffectName);
            }
        }

        public string Id { get; }
        public string Source { get; }
        public string SourcePath { get; }
        public IReadOnlyList<string> ProviderSources { get; }
        public bool? CanSelectCombatSkills { get; set; }
        public int? SelectedCombatSkillsMax { get; set; }
        public double? BaseHp { get; private set; }
        public int ColourVariationCount { get; set; }
        public HashSet<string> IncompatibleInitialQuirkIds { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void AddCombatSkill(string id)
        {
            if (_combatSkillSet.Add(id))
            {
                _combatSkillIds.Add(id);
            }
        }

        public void AddGuaranteedCombatSkill(string id)
        {
            if (_guaranteedCombatSkillSet.Add(id))
            {
                _guaranteedCombatSkillIds.Add(id);
            }
        }

        public void SetGuaranteedCombatSkill(string id, bool isGuaranteed)
        {
            if (isGuaranteed)
            {
                AddGuaranteedCombatSkill(id);
                return;
            }

            if (_guaranteedCombatSkillSet.Remove(id))
            {
                _guaranteedCombatSkillIds.RemoveAll(value =>
                    value.Equals(id, StringComparison.Ordinal));
            }
        }

        public void ReplaceSkillEffects(
            string skillId,
            string attributeKey,
            IEnumerable<string> effectNames)
        {
            if (!_skillEffects.TryGetValue(skillId, out var effectsByAttribute))
            {
                effectsByAttribute = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                _skillEffects[skillId] = effectsByAttribute;
            }

            effectsByAttribute[attributeKey] = effectNames
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private void AddSkillEffect(string skillId, string attributeKey, string effectName)
        {
            if (!_skillEffects.TryGetValue(skillId, out var effectsByAttribute))
            {
                effectsByAttribute = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                _skillEffects[skillId] = effectsByAttribute;
            }

            if (!effectsByAttribute.TryGetValue(attributeKey, out var effectNames))
            {
                effectNames = [];
                effectsByAttribute[attributeKey] = effectNames;
            }

            if (!effectNames.Contains(effectName, StringComparer.OrdinalIgnoreCase))
            {
                effectNames.Add(effectName);
            }
        }

        public void AddIncompatibleInitialQuirks(IEnumerable<string> ids)
        {
            foreach (var id in ids.Where(value => !string.IsNullOrWhiteSpace(value)))
            {
                IncompatibleInitialQuirkIds.Add(id);
            }
        }

        public void UpdateEquipmentRank(
            string equipmentKind,
            IReadOnlyDictionary<string, IReadOnlyList<string>> attributes)
        {
            var name = ReadString(attributes, "name");
            var rank = TryReadEquipmentRank(name, equipmentKind);
            if (rank is null)
            {
                return;
            }

            var target = equipmentKind.Equals("weapon", StringComparison.OrdinalIgnoreCase)
                ? _weaponRanks
                : _armourRanks;
            target.TryGetValue(rank.Value, out var inherited);
            var equipment = new HeroEquipmentRank(
                rank.Value,
                attributes.ContainsKey("upgradeRequirementCode")
                    ? ReadString(attributes, "upgradeRequirementCode") ?? string.Empty
                    : inherited?.RequirementCode ?? string.Empty,
                attributes.ContainsKey("hp")
                    ? ReadDouble(attributes, "hp")
                    : inherited?.Hp);
            target[rank.Value] = equipment;
            if (equipmentKind.Equals("armour", StringComparison.OrdinalIgnoreCase) &&
                rank.Value == 0 &&
                attributes.ContainsKey("hp"))
            {
                BaseHp = equipment.Hp;
            }
        }

        private static int? TryReadEquipmentRank(string? name, string equipmentKind)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            var marker = $"_{equipmentKind}_";
            var markerIndex = name.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
            var rankText = markerIndex >= 0
                ? name[(markerIndex + marker.Length)..]
                : name[(name.LastIndexOf('_') + 1)..];
            return int.TryParse(rankText, NumberStyles.None, CultureInfo.InvariantCulture, out var rank) && rank >= 0
                ? rank
                : null;
        }

        public void UpdateGeneration(IReadOnlyDictionary<string, IReadOnlyList<string>> attributes)
        {
            _hasGeneration = true;
            _isGenerationEnabled = ReadBooleanFlag(attributes, "is_generation_enabled") ?? _isGenerationEnabled;
            _positiveQuirksMin = ReadInt(attributes, "number_of_positive_quirks_min") ?? _positiveQuirksMin;
            _positiveQuirksMax = ReadInt(attributes, "number_of_positive_quirks_max") ?? _positiveQuirksMax;
            _negativeQuirksMin = ReadInt(attributes, "number_of_negative_quirks_min") ?? _negativeQuirksMin;
            _negativeQuirksMax = ReadInt(attributes, "number_of_negative_quirks_max") ?? _negativeQuirksMax;
            _classCampingSkills = ReadInt(attributes, "number_of_class_specific_camping_skills") ?? _classCampingSkills;
            _sharedCampingSkills = ReadInt(attributes, "number_of_shared_camping_skills") ?? _sharedCampingSkills;
            _randomCombatSkills = ReadInt(attributes, "number_of_random_combat_skills") ?? _randomCombatSkills;
            _cardsInDeck = ReadInt(attributes, "number_of_cards_in_deck") ?? _cardsInDeck;
            _cardChance = ReadDouble(attributes, "card_chance") ?? _cardChance;
            _townEventDependency = ReadString(attributes, "town_event_dependency") ?? _townEventDependency;
        }

        public HeroCandidate Build()
        {
            var generation = _hasGeneration
                ? new HeroGenerationDefinition(
                    _isGenerationEnabled,
                    _positiveQuirksMin,
                    _positiveQuirksMax,
                    _negativeQuirksMin,
                    _negativeQuirksMax,
                    _classCampingSkills,
                    _sharedCampingSkills,
                    _randomCombatSkills,
                    _cardsInDeck,
                    _cardChance,
                    _townEventDependency)
                : null;
            var skillEffects = _skillEffects
                .SelectMany(skill => skill.Value.SelectMany(attribute =>
                    attribute.Value.Select(effectName => new SkillEffectReference(
                        skill.Key,
                        attribute.Key,
                        effectName))))
                .ToArray();
            return new HeroCandidate(
                Id,
                Source,
                SourcePath,
                ProviderSources,
                CanSelectCombatSkills,
                SelectedCombatSkillsMax,
                generation,
                BaseHp,
                _weaponRanks.Values.OrderBy(rank => rank.Rank).ToArray(),
                _armourRanks.Values.OrderBy(rank => rank.Rank).ToArray(),
                ColourVariationCount,
                _combatSkillIds.ToArray(),
                _guaranteedCombatSkillIds.ToArray(),
                IncompatibleInitialQuirkIds.ToArray(),
                skillEffects);
        }
    }

    private sealed record HeroCandidate(
        string Id,
        string Source,
        string SourcePath,
        IReadOnlyList<string> ProviderSources,
        bool? CanSelectCombatSkills,
        int? SelectedCombatSkillsMax,
        HeroGenerationDefinition? Generation,
        double? BaseHp,
        IReadOnlyList<HeroEquipmentRank> WeaponRanks,
        IReadOnlyList<HeroEquipmentRank> ArmourRanks,
        int ColourVariationCount,
        IReadOnlyList<string> CombatSkillIds,
        IReadOnlyList<string> GuaranteedCombatSkillIds,
        IReadOnlyList<string> IncompatibleInitialQuirkIds,
        IReadOnlyList<SkillEffectReference> SkillEffects);

    private sealed record SkillEffectReference(string SkillId, string AttributeKey, string EffectName);
    private sealed record EffectQuirkAssignment(
        string Name,
        string QuirkId,
        string Source,
        string SourcePath);
    private sealed record QuirkDefinition(
        string Id,
        double? RandomChance,
        bool? IsPositive,
        bool IsDisease,
        IReadOnlyList<string> IncompatibleQuirks,
        IReadOnlyList<string> BuffIds,
        IReadOnlyList<string> Tags,
        int? RosterLimit,
        ParsedQuirkEvolutionDefinition? Evolution,
        string Source,
        string SourcePath,
        IReadOnlyList<string> AllSources);

    private sealed record ParsedQuirkEvolutionDefinition(
        string Signature,
        int? DurationMin,
        int? DurationMax,
        int? TownProgressionDurationChange,
        string? TargetQuirkId,
        bool CausesDeath,
        int? TownAttemptUseItemDurationThreshold,
        IReadOnlyList<string> ValidationErrors);

    private sealed record BuffDefinition(
        string Id,
        string StatType,
        string StatSubType,
        double? Amount,
        string RuleType,
        bool? IsFalseRule,
        string Source,
        string SourcePath);
    private sealed record CampingSkillDefinition(
        string Id,
        IReadOnlyList<string> HeroClasses,
        bool IsShared);
    private sealed class CampingSkillBuilder(string id)
    {
        public string Id { get; } = id;
        public bool IsShared { get; private set; }
        public HashSet<string> HeroClasses { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void Add(CampingSkillDefinition definition)
        {
            IsShared |= definition.IsShared;
            foreach (var heroClass in definition.HeroClasses)
            {
                HeroClasses.Add(heroClass);
            }
        }
    }
    private sealed record RecruitEventGroup(
        string EventId,
        IReadOnlyList<HeroRecruitEventDefinition> Recruits,
        string Source,
        string SourcePath);
    private sealed record SourceFiles(
        ActiveContentSource Source,
        SourceFileSet Files);
    private sealed record SourceFileSet(
        IReadOnlyList<string> HeroInfoFiles,
        IReadOnlyList<string> HeroOverrideFiles,
        IReadOnlyList<string> EffectFiles,
        IReadOnlyList<string> QuirkFiles,
        IReadOnlyList<string> TownEventFiles,
        IReadOnlyList<string> BuffFiles,
        IReadOnlyList<string> CampingSkillFiles,
        IReadOnlyList<string> NameFiles,
        IReadOnlyList<string> HeroUpgradeFiles,
        IReadOnlyList<string> RosterVariableFiles);

    private sealed record HeroEquipmentRank(
        int Rank,
        string RequirementCode,
        double? Hp);

    private sealed record HeroUpgradeDefinition(
        string HeroClassId,
        IReadOnlyDictionary<string, int> WeaponRequirements,
        IReadOnlyDictionary<string, int> ArmourRequirements,
        IReadOnlyList<HeroUpgradeTreeDefinition> Trees,
        string Source,
        string SourcePath);

    private sealed record ResolvedEquipmentRank(
        int Rank,
        int MinimumResolveLevel,
        double? Hp);

    private sealed record EquipmentProgressionBuildResult(
        IReadOnlyList<ResolvedEquipmentRank> Ranks,
        string UnsupportedReason);

    private sealed record HeroProgressionBuildResult(
        IReadOnlyList<HeroLevelProfile> LevelProfiles,
        string UnsupportedReason);
}
