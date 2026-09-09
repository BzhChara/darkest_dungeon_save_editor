using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;

namespace DarkestDungeonSaveEditor.Core;

public static partial class HeroClassCatalog
{
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
        private readonly Dictionary<string, HashSet<int>> _combatSkillLevels =
            new(StringComparer.Ordinal);
        private readonly List<string> _guaranteedCombatSkillIds = [];
        private readonly HashSet<string> _guaranteedCombatSkillSet = new(StringComparer.Ordinal);
        private readonly Dictionary<string, HeroEquipmentRank> _weaponRanks = new(StringComparer.Ordinal);
        private readonly Dictionary<string, HeroEquipmentRank> _armourRanks = new(StringComparer.Ordinal);
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

            foreach (var skill in candidate.CombatSkillLevels)
            {
                foreach (var level in skill.Value)
                {
                    AddCombatSkill(skill.Key, level);
                }
            }

            foreach (var skillId in candidate.GuaranteedCombatSkillIds)
            {
                AddGuaranteedCombatSkill(skillId);
            }

            foreach (var rank in candidate.WeaponRanks)
            {
                _weaponRanks[EquipmentNameKey(rank.Name)] = rank;
            }

            foreach (var rank in candidate.ArmourRanks)
            {
                _armourRanks[EquipmentNameKey(rank.Name)] = rank;
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

        public void AddCombatSkill(string id, int level)
        {
            if (!_combatSkillLevels.TryGetValue(id, out var levels))
            {
                levels = [];
                _combatSkillLevels[id] = levels;
            }

            levels.Add(level);
            if (level == 0 && _combatSkillSet.Add(id))
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

        public void AppendSkillEffects(string skillId, string attributeKey, IEnumerable<string> effectNames)
        {
            foreach (var effectName in effectNames.Where(value => !string.IsNullOrWhiteSpace(value)))
                AddSkillEffect(skillId, attributeKey, effectName);
        }

        private void AddSkillEffect(string skillId, string attributeKey, string effectName)
        {
            if (!_skillEffects.TryGetValue(skillId, out var effectsByAttribute))
            {
                effectsByAttribute = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                _skillEffects[skillId] = effectsByAttribute;
            }

            if (!effectsByAttribute.TryGetValue(attributeKey, out var effectNames))
            {
                effectNames = [];
                effectsByAttribute[attributeKey] = effectNames;
            }

            // Repeated .effect references are retained by the native skill
            // parser, including when info is followed by an override file.
            effectNames.Add(effectName);
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
            string body)
        {
            var name = NativeDarkestReader.ReadString(body, ".name");
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            var target = equipmentKind == "weapon"
                ? _weaponRanks
                : _armourRanks;
            var nameKey = EquipmentNameKey(name);
            target.TryGetValue(nameKey, out var inherited);
            // Native equipment lookup compares names and keeps the first
            // insertion slot. Numeric suffixes are only authoring convention.
            var rank = inherited?.Rank ?? target.Count;
            var equipment = new HeroEquipmentRank(
                rank,
                NativeDarkestReader.ReadString(body, ".upgradeRequirementCode") ?? inherited?.RequirementCode ?? string.Empty,
                NativeDarkestReader.ReadFloat(body, ".hp") ?? inherited?.Hp,
                name);
            target[nameKey] = equipment;
            if (equipmentKind == "armour" && rank == 0)
            {
                BaseHp = equipment.Hp;
            }
        }

        private static string EquipmentNameKey(string name)
        {
            // GetString writes at most 63 bytes plus NUL before the native
            // name comparison. Keep byte identity even if truncation splits
            // UTF-8; decoding replacement characters would conflate names.
            var bytes = Encoding.UTF8.GetBytes(name);
            return Convert.ToHexString(bytes.AsSpan(0, Math.Min(bytes.Length, 63)));
        }

        public void UpdateGeneration(string body)
        {
            _hasGeneration = true;
            _isGenerationEnabled = NativeDarkestReader.ReadBoolean(body, ".is_generation_enabled") ?? _isGenerationEnabled;
            _positiveQuirksMin = NativeDarkestReader.ReadInt(body, ".number_of_positive_quirks_min") ?? _positiveQuirksMin;
            _positiveQuirksMax = NativeDarkestReader.ReadInt(body, ".number_of_positive_quirks_max") ?? _positiveQuirksMax;
            _negativeQuirksMin = NativeDarkestReader.ReadInt(body, ".number_of_negative_quirks_min") ?? _negativeQuirksMin;
            _negativeQuirksMax = NativeDarkestReader.ReadInt(body, ".number_of_negative_quirks_max") ?? _negativeQuirksMax;
            _classCampingSkills = NativeDarkestReader.ReadInt(body, ".number_of_class_specific_camping_skills") ?? _classCampingSkills;
            _sharedCampingSkills = NativeDarkestReader.ReadInt(body, ".number_of_shared_camping_skills") ?? _sharedCampingSkills;
            _randomCombatSkills = NativeDarkestReader.ReadInt(body, ".number_of_random_combat_skills") ?? _randomCombatSkills;
            _cardsInDeck = NativeDarkestReader.ReadInt(body, ".number_of_cards_in_deck") ?? _cardsInDeck;
            _cardChance = NativeDarkestReader.ReadFloat(body, ".card_chance") ?? _cardChance;
            _townEventDependency = NativeDarkestReader.ReadString(body, ".town_event_dependency") ?? _townEventDependency;
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
                _combatSkillLevels.ToDictionary(
                    pair => pair.Key,
                    pair => (IReadOnlyList<int>)pair.Value.OrderBy(level => level).ToArray(),
                    StringComparer.Ordinal),
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
        IReadOnlyDictionary<string, IReadOnlyList<int>> CombatSkillLevels,
        IReadOnlyList<string> GuaranteedCombatSkillIds,
        IReadOnlyList<string> IncompatibleInitialQuirkIds,
        IReadOnlyList<SkillEffectReference> SkillEffects);

    private sealed record SkillEffectReference(string SkillId, string AttributeKey, string EffectName);
    private sealed record EffectQuirkAssignment(
        string Name,
        string? QuirkId,
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
        double? RuleFloat,
        string RuleString,
        string Source,
        string SourcePath);
    private sealed record CampingSkillDefinition(
        string Id,
        IReadOnlyList<string> HeroClasses,
        bool? IsShared);
    private sealed class CampingSkillBuilder(string id, bool? isShared)
    {
        public string Id { get; } = id;
        public bool? IsShared { get; } = isShared;
        public HashSet<string> HeroClasses { get; } = new(StringComparer.Ordinal);

        public void Add(CampingSkillDefinition definition)
        {
            // Every declaration grants class access, but generation looks up
            // the first stored skill record for its class-specific flag.
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
        double? Hp,
        string Name = "");

    private sealed record HeroUpgradeDefinition(
        IReadOnlyDictionary<string, int> WeaponRequirements,
        IReadOnlyDictionary<string, int> ArmourRequirements,
        IReadOnlyList<HeroUpgradeTreeDefinition> Trees);

    private sealed record HeroUpgradeTreeCandidate(
        string Id,
        IReadOnlyList<HeroUpgradeRequirementDefinition> Requirements,
        string Source,
        string SourcePath,
        string UnsupportedReason);

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
