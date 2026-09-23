using System.Text.Json.Nodes;

namespace DarkestDungeonSaveEditor.Core;

public sealed record GameInstallation(
    string SteamDirectory,
    string LibraryDirectory,
    string GameDirectory,
    string WorkshopDirectory)
{
    public string DefaultLocalModDirectory => Path.Combine(GameDirectory, "mods");
}

public sealed record SaveProfile(
    string ProfileId,
    string ProfileDirectory,
    string EstateSavePath,
    string SteamUserId,
    DateTime LastWriteTimeUtc)
{
    public string RaidSaveRelativeDirectory { get; init; } = string.Empty;
    public RaidSaveLocation RaidLocation => new(ProfileDirectory, RaidSaveRelativeDirectory);
    public string RaidSavePath => RaidLocation.RaidPath;
    public string MapSavePath => RaidLocation.MapPath;
}

public sealed record DiscoverySnapshot(
    IReadOnlyList<GameInstallation> GameInstallations,
    IReadOnlyList<SaveProfile> Profiles,
    IReadOnlyList<string> Issues);

public sealed record ActiveContentSource(
    string Id,
    string DisplayName,
    string Kind,
    string Directory,
    int LoadOrder)
{
    public string VirtualPathPrefix { get; init; } = string.Empty;
}

public sealed record ActiveContentSnapshot(
    SaveProfile Profile,
    string GameMode,
    IReadOnlyList<ActiveContentSource> Sources,
    IReadOnlyList<string> Issues,
    string WorkspaceDirectory,
    string DecodedGamePath,
    int AppliedModCount,
    string SourceGameSha256)
{
    private readonly IReadOnlyList<ActiveContentSource> _sources = Sources;
    public IReadOnlyList<ActiveContentSource> Sources
    {
        get => _sources;
        // An explicit replacement is a hypothetical catalog (e.g. Bridge staging),
        // not the source mapping returned by the resolver.
        init { _sources = value; Resolution = null; }
    }
    public ActiveContentResolution? Resolution { get; init; }
}

// Keep the actual search roots and configuration, independent of rotating decoded scratch files.
public sealed record ActiveContentResolution(
    string GameDirectory,
    string? WorkshopDirectory,
    string? AdditionalLocalModDirectory,
    string ConfigurationJson);

public sealed record HeroGenerationDefinition(
    bool? IsEnabled,
    int? PositiveQuirksMin,
    int? PositiveQuirksMax,
    int? NegativeQuirksMin,
    int? NegativeQuirksMax,
    int? ClassCampingSkills,
    int? SharedCampingSkills,
    int? RandomCombatSkills,
    int? CardsInDeck,
    double? CardChance,
    string TownEventDependency);

public sealed record HeroRecruitEventDefinition(
    string Id,
    string HeroClass,
    double? Count,
    string Source,
    string SourcePath);

public sealed record HeroRuntimeQuirkSignal(
    string QuirkId,
    string EffectName,
    string SkillId,
    bool? IsPositive,
    string Source);

public sealed record HeroLevelProfile(
    int ResolveLevel,
    int ResolveXp,
    int WeaponRank,
    int ArmourRank,
    double ArmourHp);

public enum HeroUpgradeTreeKind
{
    Weapon,
    Armour,
    CombatSkill
}

public sealed record HeroUpgradeRequirementDefinition(
    string Code,
    int PrerequisiteResolveLevel);

public sealed record HeroUpgradeTreeDefinition(
    string Id,
    HeroUpgradeTreeKind Kind,
    IReadOnlyList<HeroUpgradeRequirementDefinition> Requirements)
{
    public string Source { get; init; } = string.Empty;
    public string SourcePath { get; init; } = string.Empty;
    public string UnsupportedReason { get; init; } = string.Empty;
}

public sealed record HeroUpgradePurchase(
    string TreeId,
    string RequirementCode);

public sealed record BilingualContentName(string Chinese, string English)
{
    public static BilingualContentName Empty { get; } = new(string.Empty, string.Empty);
}

public sealed record HeroClassDefinition(
    string Id,
    string Source,
    string SourcePath,
    bool? CanSelectCombatSkills,
    int? SelectedCombatSkillsMax,
    HeroGenerationDefinition? Generation,
    double? BaseHp,
    IReadOnlyList<HeroLevelProfile> LevelProfiles,
    IReadOnlyList<HeroUpgradeTreeDefinition> UpgradeTrees,
    string ProgressionUnsupportedReason,
    int ColourVariationCount,
    IReadOnlyList<string> CombatSkillIds,
    IReadOnlyList<string> SingleLevelCombatSkillIds,
    IReadOnlyList<string> GuaranteedCombatSkillIds,
    IReadOnlyList<string> ClassCampingSkillIds,
    IReadOnlyList<string> SharedCampingSkillIds,
    IReadOnlyList<string> IncompatibleInitialQuirkIds,
    IReadOnlyList<HeroRecruitEventDefinition> RecruitEvents,
    IReadOnlyList<HeroRuntimeQuirkSignal> RuntimeQuirkSignals,
    bool HasProviderConflict,
    IReadOnlyList<string> AllSources)
{
    public BilingualContentName LocalizedName { get; init; } = BilingualContentName.Empty;
    public string SourceLabel { get; init; } = string.Empty;
    public IReadOnlyList<HeroGenerationAvailability> GenerationAvailability { get; init; } = [];
    public bool CampingSkillsComplete { get; init; } = true;
    public IReadOnlyDictionary<string, IReadOnlyList<int>> CombatSkillLevels { get; init; } =
        new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal);
    internal HeroEquipmentDefinition? Equipment { get; init; }
}

public enum HeroMaxHpModifierKind
{
    Flat,
    Percentage
}

public sealed record HeroMaxHpModifier(
    string BuffId,
    HeroMaxHpModifierKind Kind,
    double Amount,
    string RuleType,
    bool IsFalseRule,
    double? RuleFloat,
    string RuleString);

public sealed record HeroQuirkEvolutionDefinition(
    int DurationMin,
    int DurationMax,
    int? TownProgressionDurationChange,
    string? TargetQuirkId,
    bool CausesDeath,
    int? TownAttemptUseItemDurationThreshold);

public enum HeroInitialQuirkKind
{
    Natural,
    Special,
    Disease
}

public enum HeroInitialQuirkWriteStatus
{
    Direct,
    RequiresSaveContext,
    Unverified,
    Unsupported
}

public sealed record HeroInitialQuirkDefinition(
    string Id,
    bool? IsPositive,
    double? RandomChance,
    bool IsDisease,
    HeroQuirkEvolutionDefinition? Evolution,
    IReadOnlyList<string> IncompatibleQuirkIds,
    IReadOnlyList<HeroMaxHpModifier> MaxHpModifiers,
    HeroInitialQuirkKind Kind,
    bool IsNaturalRandomEligible,
    HeroInitialQuirkWriteStatus WriteStatus,
    string WriteStatusReason,
    string Source,
    string SourcePath,
    IReadOnlyList<string> AllSources)
{
    public BilingualContentName LocalizedName { get; init; } = BilingualContentName.Empty;
    public string SourceLabel { get; init; } = string.Empty;
    public int? DefinitionLimit { get; init; }
    public bool HasEvolution => Evolution is not null;
}

public sealed record HeroInitialQuirkLimits(int? Positive, int? Negative, int? Diseases)
{
    // Native shared-rule initialization, x64 build 27890, 0x1404E2F06–0x1404E2F1A.
    // A null value means an effective rule could not be resolved, not this default.
    public static HeroInitialQuirkLimits Default { get; } = new(5, 5, 3);
}

public sealed record HeroClassCatalogResult(
    string GameMode,
    IReadOnlyList<int> ResolveLevelThresholds,
    IReadOnlyList<HeroClassDefinition> HeroClasses,
    IReadOnlyList<HeroRecruitEventDefinition> RecruitEvents,
    IReadOnlyList<HeroInitialQuirkDefinition> InitialQuirks,
    IReadOnlyList<string> HeroNames,
    IReadOnlyList<string> Issues)
{
    public HeroInitialQuirkLimits InitialQuirkLimits { get; init; } = HeroInitialQuirkLimits.Default;
}

public sealed record TrinketDefinition(
    string Id,
    string Rarity,
    int? Limit,
    int? Price,
    string Source,
    string SourcePath,
    bool IsStateful,
    IReadOnlyList<string> StatefulFields,
    bool HasProviderConflict,
    IReadOnlyList<string> AllSources)
{
    public BilingualContentName LocalizedName { get; init; } = BilingualContentName.Empty;
    public string SourceLabel { get; init; } = string.Empty;
    public int? QuestUses { get; init; }
    public int? TriggerLimit { get; init; }
    public string SaveIdentityIssue => NativeInventoryIdentity.GetSaveIssue("trinket", Id);
}

public sealed record TrinketCatalogResult(
    IReadOnlyList<TrinketDefinition> Trinkets,
    TrinketStorageDefinition? Storage,
    IReadOnlyList<string> Issues);

public enum QuantityItemStorageKind
{
    Wallet,
    EstateItems,
    RaidInventory
}

public enum QuantityItemSaveContext
{
    Town,
    Raid
}

public enum QuantityItemReferenceStatus
{
    OfficialContent,
    ConfirmedActive,
    SuspectedUnused,
    AnalysisIncomplete,
    SaveOnly
}

public sealed record QuantityItemDefinition(
    string InventoryType,
    string ItemId,
    QuantityItemStorageKind StorageKind,
    int? BaseStackLimit,
    bool? EstateCanBeProvision,
    int CurrentAmount,
    string Source,
    string SourcePath,
    bool HasProviderConflict,
    IReadOnlyList<string> AllSources)
{
    public BilingualContentName LocalizedName { get; init; } = BilingualContentName.Empty;
    public string SourceLabel { get; init; } = string.Empty;
    public QuantityItemReferenceStatus ReferenceStatus { get; init; } =
        QuantityItemReferenceStatus.AnalysisIncomplete;
    public IReadOnlyList<string> ReferenceEvidence { get; init; } = [];
    public bool IsPresentInSave { get; init; }
    public int SavedEntryCount { get; init; }
    public string DisplayId => string.IsNullOrWhiteSpace(ItemId) ? InventoryType : ItemId;
    public string PersistedType => StorageKind == QuantityItemStorageKind.Wallet
        ? InventoryType.Equals("heirloom", StringComparison.Ordinal) &&
          !string.IsNullOrWhiteSpace(ItemId)
            ? ItemId
            : InventoryType
        : InventoryType;
    public string PersistedId => StorageKind is QuantityItemStorageKind.EstateItems or
        QuantityItemStorageKind.RaidInventory
        ? ItemId
        : string.Empty;
    public string CatalogKey => CreateCatalogKey(StorageKind, PersistedType, PersistedId);
    internal string DefinitionKey => CreateCatalogKey(StorageKind, InventoryType, ItemId);
    public string SaveIdentityIssue => NativeInventoryIdentity.GetSaveIssue(PersistedType, PersistedId);
    // This is an editor key, not a game hash. Length-prefix the type so ':' in
    // an authored type or ID cannot make two different storage entries alias.
    internal static string CreateCatalogKey(QuantityItemStorageKind kind, string type, string id) =>
        $"{kind}:{type.Length}:{type}:{id}";
    public bool IsSaveOnly => string.IsNullOrWhiteSpace(SourcePath);
    public bool IsHiddenByDefault =>
        !IsPresentInSave && ReferenceStatus == QuantityItemReferenceStatus.SuspectedUnused;
}

public sealed record QuantityItemCatalogResult(
    IReadOnlyList<QuantityItemDefinition> Items,
    IReadOnlyList<string> Issues,
    string SourceSaveSha256)
{
    public QuantityItemSaveContext SaveContext { get; init; } = QuantityItemSaveContext.Town;
    public RaidInventoryStorageDefinition? RaidStorage { get; init; }
    public int RaidOccupiedSlots { get; init; }
    public IReadOnlyList<string> DefinitionReadFailures { get; init; } = [];

    // Compatibility alias for callers compiled against the original town-only catalog.
    public string SourceEstateSha256 => SourceSaveSha256;
}

public sealed record QuantityItemMutationPreview(
    string ItemId,
    QuantityItemStorageKind StorageKind,
    int ExistingAmount,
    int TargetAmount,
    int MatchingEntries,
    bool CreatedEntry)
{
    public int ResultingMatchingEntries { get; init; } = MatchingEntries + (CreatedEntry ? 1 : 0);
    public int ExistingInventoryEntries { get; init; }
    public int ResultingInventoryEntries { get; init; }
    public int CreatedEntries { get; init; } = CreatedEntry ? 1 : 0;
    public int RemovedEntries { get; init; }
    public int? InventoryCapacity { get; init; }
}

public sealed record RaidInventoryStorageDefinition(
    int MaxSlots,
    string Source,
    string SourcePath,
    ActiveContentSource ContentSource,
    string SourceSha256);

public sealed record RaidInventoryStorageCatalogResult(
    RaidInventoryStorageDefinition? Storage,
    IReadOnlyList<string> Issues);

public sealed record TrinketStorageDefinition(
    int MaxSlots,
    string Source,
    string SourcePath,
    ActiveContentSource ContentSource,
    string SourceSha256);

public sealed record TrinketStorageCatalogResult(
    TrinketStorageDefinition? Storage,
    IReadOnlyList<string> Issues);

public sealed record TrinketMutationPreview(
    string TrinketId,
    int RequestedCopies,
    int ExistingCopies,
    int ResultingCopies,
    int ExistingInventoryEntries,
    int ResultingInventoryEntries,
    int? DefinitionLimit,
    int? StorageCapacity)
{
    public bool ExceedsDefinitionLimit => DefinitionLimit is > 0 && ResultingCopies > DefinitionLimit.Value;
}

public sealed record SaveProfileSummary(
    int EstateVersion,
    int TrinketCopies,
    int UniqueTrinketIds);

public enum StagecoachRecruitPool
{
    Ordinary,
    Shard
}

public sealed record StagecoachHeroMutationPreview(
    int CandidateGuid,
    string HeroClass,
    int ResolveXp,
    int WeaponRank,
    int ArmourRank,
    int UpgradePurchaseCount,
    int ExistingCandidates,
    int ResultingCandidates,
    int OriginalNextGuid,
    int ResultingNextGuid,
    int RosterHeroCount)
{
    public StagecoachRecruitPool TargetPool { get; init; } = StagecoachRecruitPool.Ordinary;
    public IReadOnlyList<HeroQuirkLimitPreview> QuirkLimits { get; init; } = [];
    public bool MayRefreshOnTownReturn { get; init; }
}

public sealed record HeroQuirkLimitPreview(
    string QuirkId,
    int ExistingRosterHeroes,
    int ExistingStagecoachCandidates,
    int ResultingHeroes,
    int DefinitionLimit)
{
    public int ExistingHeroes => ExistingRosterHeroes + ExistingStagecoachCandidates;
    public bool ExceedsDefinitionLimit => ResultingHeroes > DefinitionLimit;
}

public sealed record StagecoachHeroCandidatePreview(
    string Name,
    string HeroClass,
    int ResolveLevel,
    int ResolveXp,
    int WeaponRank,
    int ArmourRank,
    double CurrentHp,
    int ColourVariation,
    IReadOnlyList<string> PositiveQuirks,
    IReadOnlyList<string> NegativeQuirks,
    IReadOnlyList<string> Diseases,
    IReadOnlyList<string> CombatSkills,
    IReadOnlyList<string> CampingSkills,
    IReadOnlyList<string> Warnings);

public sealed record GeneratedStagecoachHeroCandidate(
    JsonObject Candidate,
    StagecoachHeroCandidatePreview Preview,
    IReadOnlyList<HeroUpgradePurchase> UpgradePurchases);

public sealed record PreparedSaveFile(
    string FileName,
    string TargetPath,
    string SourceCopyPath,
    string ProposedDecodedPath,
    string EncodedPath,
    string RoundTripDecodedPath,
    string OriginalSha256,
    string EncodedSha256,
    bool SourceWasDson);

public sealed record PreparedStagecoachHeroEdit(
    string SessionId,
    SaveProfile Profile,
    StagecoachHeroMutationPreview Preview,
    PreparedStagecoachContentGuard ContentGuard,
    string WorkspaceDirectory,
    PreparedSaveFile TownFile,
    PreparedSaveFile RosterFile,
    PreparedSaveFile UpgradesFile,
    DateTime PreparedAtUtc);

public sealed record PreparedStagecoachContentGuard(
    string SourceGameSha256,
    string GameMode,
    IReadOnlyList<ActiveContentSource> Sources,
    string HeroCatalogSha256,
    IReadOnlyList<PreparedContentFileFingerprint> ManifestFingerprints)
{
    public ActiveContentResolution? Resolution { get; init; }
}

public sealed record PreparedTrinketEdit(
    string SessionId,
    SaveProfile Profile,
    TrinketDefinition Trinket,
    TrinketMutationPreview Preview,
    PreparedTrinketContentGuard ContentGuard,
    SaveProfileSummary OriginalSummary,
    SaveProfileSummary ResultSummary,
    string WorkspaceDirectory,
    string SourceCopyPath,
    string ProposedDecodedPath,
    string EncodedPath,
    string RoundTripDecodedPath,
    string OriginalSha256,
    string EncodedSha256,
    bool SourceWasDson,
    DateTime PreparedAtUtc);

public sealed record PreparedQuantityItemEdit(
    string SessionId,
    SaveProfile Profile,
    QuantityItemDefinition Item,
    QuantityItemMutationPreview Preview,
    PreparedQuantityItemContentGuard ContentGuard,
    string WorkspaceDirectory,
    string SourceCopyPath,
    string ProposedDecodedPath,
    string EncodedPath,
    string RoundTripDecodedPath,
    string OriginalSha256,
    string EncodedSha256,
    bool SourceWasDson,
    DateTime PreparedAtUtc);

public sealed record PreparedQuantityItemContentGuard(
    string SourceGameSha256,
    IReadOnlyList<ActiveContentSource> Sources,
    string ItemSourcePath,
    string ItemSourceSha256,
    IReadOnlyList<PreparedContentFileFingerprint> ManifestFingerprints)
{
    public ActiveContentResolution? Resolution { get; init; }
    public QuantityItemSaveContext SaveContext { get; init; } = QuantityItemSaveContext.Town;
    public int? RaidInventoryCapacity { get; init; }
    public string RaidStorageSourcePath { get; init; } = string.Empty;
    public string RaidStorageSourceSha256 { get; init; } = string.Empty;
}

public sealed record PreparedTrinketContentGuard(
    string SourceGameSha256,
    IReadOnlyList<ActiveContentSource> Sources,
    int StorageCapacity,
    string StorageSource,
    string StorageSourcePath,
    string StorageSourceSha256,
    string TrinketSourcePath,
    string TrinketSourceSha256,
    IReadOnlyList<PreparedContentFileFingerprint> ManifestFingerprints)
{
    public ActiveContentResolution? Resolution { get; init; }
}

public sealed record PreparedContentFileFingerprint(
    string Path,
    bool Exists,
    string Sha256);

public sealed record SaveCommitResult(
    string ProfileDirectory,
    string TargetPath,
    string BackupDirectory,
    string OriginalSha256,
    string FinalSha256,
    DateTime CommittedAtUtc);

public sealed record SaveFileCommitResult(
    string FileName,
    string TargetPath,
    string OriginalSha256,
    string FinalSha256);

public sealed record MultiFileSaveCommitResult(
    string ProfileDirectory,
    string BackupDirectory,
    IReadOnlyList<SaveFileCommitResult> Files,
    DateTime CommittedAtUtc);

public sealed record SaveEditorLocations(
    string ApplicationDataDirectory,
    string WorkspaceDirectory,
    string BackupDirectory)
{
    private const string SolutionFileName = "DarkestDungeonSaveEditor.sln";

    public static SaveEditorLocations CreateDefault()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var root = Path.Combine(local, "DarkestDungeonSaveEditor");
        return new SaveEditorLocations(root, Path.Combine(root, "workspaces"), Path.Combine(root, "backups"));
    }

    public static string ResolveLogDirectory(string applicationBaseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationBaseDirectory);
        var applicationDirectory = new DirectoryInfo(Path.GetFullPath(applicationBaseDirectory));
        for (var directory = applicationDirectory; directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
            {
                return Path.Combine(directory.FullName, "logs");
            }
        }

        return Path.Combine(applicationDirectory.FullName, "logs");
    }
}
