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
    public string RaidSavePath => Path.Combine(ProfileDirectory, "persist.raid.json");
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
    string SourceGameSha256);

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
    IReadOnlyList<HeroUpgradeRequirementDefinition> Requirements);

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

public sealed record HeroClassCatalogResult(
    string GameMode,
    IReadOnlyList<int> ResolveLevelThresholds,
    IReadOnlyList<HeroClassDefinition> HeroClasses,
    IReadOnlyList<HeroRecruitEventDefinition> RecruitEvents,
    IReadOnlyList<HeroInitialQuirkDefinition> InitialQuirks,
    IReadOnlyList<string> HeroNames,
    IReadOnlyList<string> Issues);

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
        ? InventoryType.Equals("heirloom", StringComparison.OrdinalIgnoreCase) &&
          !string.IsNullOrWhiteSpace(ItemId)
            ? ItemId
            : InventoryType
        : InventoryType;
    public string PersistedId => StorageKind is QuantityItemStorageKind.EstateItems or
        QuantityItemStorageKind.RaidInventory
        ? ItemId
        : string.Empty;
    public string CatalogKey =>
        $"{StorageKind}:{PersistedType}:{PersistedId}".ToUpperInvariant();
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
    public IReadOnlyList<HeroQuirkLimitPreview> QuirkLimits { get; init; } = [];
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
    IReadOnlyList<PreparedContentFileFingerprint> ManifestFingerprints);

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
    IReadOnlyList<PreparedContentFileFingerprint> ManifestFingerprints);

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
