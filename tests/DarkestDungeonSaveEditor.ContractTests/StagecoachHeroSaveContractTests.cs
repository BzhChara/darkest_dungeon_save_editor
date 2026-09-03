internal static partial class ContractSuite
{
    private static async Task VerifyStagecoachHeroSaveContractsAsync(
        ActiveContentSnapshot activeContent,
        SaveProfile profile,
        DsonSaveCodec codec,
        string runRoot,
        string localHeroUpgradeRoot,
        string upgradesSavePath,
        string townSavePath,
        string rosterSavePath,
        JsonObject rosterHeroesSeed,
        HeroClassCatalogResult heroCatalog,
        HeroCandidateContractContext candidates)
    {
        var levelFourCandidate = candidates.LevelFourCandidate;
        var contextLimitedCandidate = candidates.ContextLimitedCandidate;
        var shardCandidate = candidates.ShardCandidate;
        var stagecoachCandidate = levelFourCandidate.Candidate;
        var existingCandidateTown = JsonNode.Parse(
            """
    {
      "base_root": {
        "buildings": {
          "stage_coach": {
            "store": {
              "hero_recruit": {
                "generated": {
                  "100": {
                    "heroClass": "hellion",
                    "actor": {},
                    "quirks": { "context_special": {} }
                  }
                }
              },
              "shard_hero_recruit": {
                "generated": {
                  "200": {
                    "heroClass": "shieldbreaker",
                    "actor": {},
                    "quirks": {
                      "context_special": {},
                      "context_roster_limited": {}
                    }
                  }
                }
              }
            }
          }
        }
      }
    }
    """) as JsonObject ?? throw new InvalidDataException("Stagecoach mutation town fixture is invalid.");
        var existingCandidateRoster = JsonNode.Parse(
            """
    {
      "base_root": {
        "nextGuid": 364,
        "heroes": {
          "1": {
            "heroClass": "crusader",
            "hero_file_data": {
              "raw_data": {
                "base_root": {
                  "quirks": {
                    "context_special": {},
                    "context_roster_limited": {}
                  }
                }
              }
            }
          }
        }
      }
    }
    """) as JsonObject ?? throw new InvalidDataException("Stagecoach mutation roster fixture is invalid.");
        var existingCandidateUpgrades = JsonNode.Parse(
            """
    {
      "base_root": {
        "version": 1,
        "purchases": {
          "7": {
            "instance_number": 1,
            "tree_id": 123456,
            "requirement_code": "e",
            "is_purchased": true
          }
        }
      }
    }
    """) as JsonObject ?? throw new InvalidDataException("Stagecoach mutation upgrades fixture is invalid.");
        var contextualLimitPreviews = StagecoachHeroSaveEditor.AnalyzeQuirkLimits(
            existingCandidateTown,
            existingCandidateRoster,
            contextLimitedCandidate.Candidate,
            heroCatalog.InitialQuirks);
        Assert(
            contextualLimitPreviews.Count == 1 &&
            contextualLimitPreviews.All(item => item.QuirkId == "context_special"),
            "roster_limit must not enter stagecoach-generation limit analysis because the game enforces it later during recruitment.");
        var singletonLimitPreview = contextualLimitPreviews.Single(item => item.QuirkId == "context_special");
        Assert(
            singletonLimitPreview is
            {
                ExistingRosterHeroes: 1,
                ExistingStagecoachCandidates: 2,
                ExistingHeroes: 3,
                ResultingHeroes: 4,
                DefinitionLimit: 1,
                ExceedsDefinitionLimit: true
            },
            "A singleton preview must count the owned roster, ordinary and shard stagecoach candidates before adding one.");
        var (mutatedTown, mutatedRoster, mutatedUpgrades, mutationPreview) = StagecoachHeroSaveEditor.AddCandidate(
            existingCandidateTown,
            existingCandidateRoster,
            existingCandidateUpgrades,
            stagecoachCandidate,
            levelFourCandidate.UpgradePurchases);
        var mutatedGenerated = mutatedTown["base_root"]?["buildings"]?["stage_coach"]?["store"]?["hero_recruit"]?["generated"] as JsonObject;
        var mutatedPurchases = mutatedUpgrades["base_root"]?["purchases"] as JsonObject;
        Assert(mutationPreview.CandidateGuid == 364, "Stagecoach candidate should use roster nextGuid without filling holes.");
        Assert(
            mutationPreview is
            {
                TargetPool: StagecoachRecruitPool.Ordinary,
                ResolveXp: 24,
                WeaponRank: 3,
                ArmourRank: 3,
                UpgradePurchaseCount: 18
            },
            "Stagecoach mutation preview should preserve the candidate progression metadata.");
        Assert(mutationPreview.ExistingCandidates == 1 && mutationPreview.ResultingCandidates == 2, "Existing normal recruits must be preserved while appending.");
        Assert(mutatedGenerated?.ContainsKey("100") == true && mutatedGenerated.ContainsKey("364"), "Normal recruit append removed an existing candidate or missed the new GUID.");
        Assert(
            JsonNode.DeepEquals(
                mutatedTown["base_root"]?["buildings"]?["stage_coach"]?["store"]?["shard_hero_recruit"],
                existingCandidateTown["base_root"]?["buildings"]?["stage_coach"]?["store"]?["shard_hero_recruit"]),
            "Ordinary routing must leave the entire shard recruit pool unchanged.");
        Assert(mutatedRoster["base_root"]?["nextGuid"]?.GetValue<int>() == 365, "Roster nextGuid should advance exactly once.");
        Assert((mutatedRoster["base_root"]?["heroes"] as JsonObject)?.Count == 1, "Stagecoach append must not modify the owned roster.");
        Assert(
            JsonNode.DeepEquals(
                mutatedRoster["base_root"]?["heroes"],
                existingCandidateRoster["base_root"]?["heroes"]),
            "Stagecoach append changed the owned roster hero subtree.");
        Assert((existingCandidateTown["base_root"]?["buildings"]?["stage_coach"]?["store"]?["hero_recruit"]?["generated"] as JsonObject)?.Count == 1, "Pure mutation changed the input town document.");
        Assert(existingCandidateRoster["base_root"]?["nextGuid"]?.GetValue<int>() == 364, "Pure mutation changed the input roster document.");
        Assert(
            mutatedPurchases?.Count == 19 &&
            mutatedPurchases.ContainsKey("7") &&
            mutatedPurchases.ContainsKey("8") &&
            mutatedPurchases.Select(pair => pair.Value)
                .OfType<JsonObject>()
                .Count(purchase => purchase["instance_number"]?.GetValue<int>() == 364) == 18,
            "Stagecoach mutation should append every level-derived upgrade purchase after the highest existing key.");
        var expectedLocalSkillHash = unchecked((int)HashLoc2Key("local_hero.local_skill"));
        Assert(
            mutatedPurchases!.Select(pair => pair.Value).OfType<JsonObject>().Any(purchase =>
                purchase["instance_number"]?.GetValue<int>() == 364 &&
                purchase["tree_id"]?.GetValue<int>() == expectedLocalSkillHash &&
                purchase["requirement_code"]?.GetValue<string>() == "B"),
            "Stagecoach mutation should use the game's existing name hash and preserve a Mod's custom requirement code.");
        Assert(
            mutatedPurchases!.Select(pair => pair.Value).OfType<JsonObject>().Where(purchase =>
                    purchase["instance_number"]?.GetValue<int>() == 364 &&
                    purchase["tree_id"]?.GetValue<int>() == expectedLocalSkillHash)
                .Select(purchase => purchase["requirement_code"]?.GetValue<string>())
                .ToHashSet(StringComparer.Ordinal)
                .IsSupersetOf(["a", "A"]),
            "Requirement codes that differ only by case must remain distinct purchases.");
        var expectedCampingSkillHash = unchecked((int)HashLoc2Key("local_hero.local_camp_two"));
        Assert(
            mutatedPurchases!.Select(pair => pair.Value).OfType<JsonObject>().Any(purchase =>
                purchase["instance_number"]?.GetValue<int>() == 364 &&
                purchase["tree_id"]?.GetValue<int>() == expectedCampingSkillHash &&
                purchase["requirement_code"]?.GetValue<string>() == "0"),
            "Stagecoach mutation should persist implicit class-prefixed camping unlock trees with requirement code zero.");
        Assert(
            (existingCandidateUpgrades["base_root"]?["purchases"] as JsonObject)?.Count == 1,
            "Pure mutation changed the input upgrades document.");

        var (mutatedShardTown, mutatedShardRoster, mutatedShardUpgrades, shardMutationPreview) =
            StagecoachHeroSaveEditor.AddCandidate(
                existingCandidateTown,
                existingCandidateRoster,
                existingCandidateUpgrades,
                shardCandidate.Candidate,
                shardCandidate.UpgradePurchases);
        var mutatedShardGenerated = mutatedShardTown["base_root"]?["buildings"]?["stage_coach"]?["store"]?
            ["shard_hero_recruit"]?["generated"] as JsonObject;
        Assert(
            shardMutationPreview is
            {
                TargetPool: StagecoachRecruitPool.Shard,
                ExistingCandidates: 1,
                ResultingCandidates: 2,
                CandidateGuid: 364
            },
            "A candidate carrying shard_hungry should target the shard stagecoach and report that pool's counts.");
        Assert(
            mutatedShardGenerated?.ContainsKey("200") == true &&
            mutatedShardGenerated.ContainsKey("364"),
            "Shard routing should preserve the existing shard candidate and append the generated candidate.");
        Assert(
            JsonNode.DeepEquals(
                mutatedShardTown["base_root"]?["buildings"]?["stage_coach"]?["store"]?["hero_recruit"],
                existingCandidateTown["base_root"]?["buildings"]?["stage_coach"]?["store"]?["hero_recruit"]),
            "Shard routing must not modify the ordinary recruit pool.");
        Assert(
            mutatedShardRoster["base_root"]?["nextGuid"]?.GetValue<int>() == 365 &&
            (mutatedShardUpgrades["base_root"]?["purchases"] as JsonObject)?.Count ==
            1 + shardCandidate.UpgradePurchases.Count,
            "Shard routing should retain the shared roster GUID and upgrade-purchase transaction behavior.");

        var missingShardPoolTown = existingCandidateTown.DeepClone() as JsonObject
            ?? throw new InvalidDataException("Missing-shard-pool fixture could not be cloned.");
        var missingShardPoolStore = (JsonObject)missingShardPoolTown["base_root"]!["buildings"]!["stage_coach"]!["store"]!;
        missingShardPoolStore.Remove("shard_hero_recruit");
        var missingShardPoolSnapshot = missingShardPoolTown.DeepClone();
        var missingShardPoolBlocked = false;
        try
        {
            _ = StagecoachHeroSaveEditor.AddCandidate(
                missingShardPoolTown,
                existingCandidateRoster,
                existingCandidateUpgrades,
                shardCandidate.Candidate,
                shardCandidate.UpgradePurchases);
        }
        catch (InvalidDataException)
        {
            missingShardPoolBlocked = true;
        }

        Assert(
            missingShardPoolBlocked && JsonNode.DeepEquals(missingShardPoolTown, missingShardPoolSnapshot),
            "A missing shard recruit pool must fail closed without synthesizing or mutating the input town save.");

        var nonRoundTrippableRequirementBlocked = false;
        try
        {
            _ = StagecoachHeroSaveEditor.AddCandidate(
                existingCandidateTown,
                existingCandidateRoster,
                existingCandidateUpgrades,
                stagecoachCandidate,
                [new HeroUpgradePurchase("local_hero.local_skill", "too_long")]);
        }
        catch (InvalidDataException ex) when (ex.Message.Contains("losslessly", StringComparison.Ordinal))
        {
            nonRoundTrippableRequirementBlocked = true;
        }

        Assert(
            nonRoundTrippableRequirementBlocked,
            "A multi-character requirement code must be rejected before DSON can silently truncate it.");

        Assert(HashLoc2Key("tree_1e") == HashLoc2Key("tree_20"), "The upgrade collision fixture is invalid.");
        var collidingTreeIdsBlocked = false;
        try
        {
            _ = StagecoachHeroSaveEditor.AddCandidate(
                existingCandidateTown,
                existingCandidateRoster,
                existingCandidateUpgrades,
                stagecoachCandidate,
                [
                    new HeroUpgradePurchase("tree_1e", "0"),
            new HeroUpgradePurchase("tree_20", "1")
                ]);
        }
        catch (InvalidDataException ex) when (ex.Message.Contains("same game hash", StringComparison.Ordinal))
        {
            collidingTreeIdsBlocked = true;
        }

        Assert(
            collidingTreeIdsBlocked,
            "Different upgrade tree ids with the same game hash must be rejected even when their requirement codes differ.");

        var invalidGuidRoster = JsonNode.Parse(
            """
    {
      "base_root": {
        "nextGuid": 200,
        "heroes": {}
      }
    }
    """) as JsonObject ?? throw new InvalidDataException("Invalid GUID roster fixture is invalid.");
        var invalidGuidBlocked = false;
        try
        {
            _ = StagecoachHeroSaveEditor.AddCandidate(
                existingCandidateTown,
                invalidGuidRoster,
                existingCandidateUpgrades,
                stagecoachCandidate,
                levelFourCandidate.UpgradePurchases);
        }
        catch (InvalidDataException ex) when (ex.Message.Contains("highest existing hero GUID", StringComparison.Ordinal))
        {
            invalidGuidBlocked = true;
        }

        Assert(invalidGuidBlocked, "A stale or colliding roster nextGuid must be blocked instead of probing for a hole.");

        var stagecoachLocations = new SaveEditorLocations(
            Path.Combine(runRoot, "stagecoach-appdata"),
            Path.Combine(runRoot, "stagecoach-appdata", "workspaces"),
            Path.Combine(runRoot, "stagecoach-appdata", "backups"));
        var stagecoachService = new SaveEditService(codec, stagecoachLocations);
        var preparedShardStagecoach = await stagecoachService.PrepareStagecoachHeroEditAsync(
            profile,
            shardCandidate,
            heroCatalog,
            activeContent);
        var preparedShardTown = JsonNode.Parse(File.ReadAllText(preparedShardStagecoach.TownFile.ProposedDecodedPath))
            as JsonObject ?? throw new InvalidDataException("Prepared shard-stagecoach town file is invalid.");
        Assert(
            preparedShardStagecoach.Preview is
            {
                TargetPool: StagecoachRecruitPool.Shard,
                ExistingCandidates: 1,
                ResultingCandidates: 2,
                CandidateGuid: 364
            } &&
            preparedShardTown["base_root"]?["buildings"]?["stage_coach"]?["store"]?
                ["shard_hero_recruit"]?["generated"]?["364"] is JsonObject &&
            (preparedShardTown["base_root"]?["buildings"]?["stage_coach"]?["store"]?
                ["hero_recruit"]?["generated"] as JsonObject)?.Count == 0,
            "A prepared shard candidate should survive encode/decode validation in the shard pool without entering the ordinary pool.");
        var preparedContextLimitedStagecoach = await stagecoachService.PrepareStagecoachHeroEditAsync(
            profile,
            contextLimitedCandidate,
            heroCatalog,
            activeContent);
        var preparedSingletonLimit = preparedContextLimitedStagecoach.Preview.QuirkLimits.Single(item =>
            item.QuirkId == "context_special");
        Assert(
            preparedSingletonLimit is
            {
                ExistingRosterHeroes: 1,
                ExistingStagecoachCandidates: 0,
                ResultingHeroes: 2,
                DefinitionLimit: 1,
                ExceedsDefinitionLimit: true
            },
            "Prepared hero preview should retain an over-limit singleton warning without blocking the candidate edit.");
        Assert(
            !preparedContextLimitedStagecoach.Preview.QuirkLimits.Any(item =>
                item.QuirkId == "context_roster_limited"),
            "A roster_limit quirk must not create a stagecoach preview warning because the editor does not recruit the candidate into the owned roster.");
        var preparedStagecoach = await stagecoachService.PrepareStagecoachHeroEditAsync(
            profile,
            levelFourCandidate,
            heroCatalog,
            activeContent);
        Assert(preparedStagecoach.Preview.CandidateGuid == 364, "Prepared stagecoach candidate should use save nextGuid 364.");
        Assert(
            preparedStagecoach.Preview is { ResolveXp: 24, WeaponRank: 3, ArmourRank: 3, UpgradePurchaseCount: 18 },
            "Prepared stagecoach preview should retain non-zero XP and equipment ranks.");
        Assert(preparedStagecoach.Preview.ExistingCandidates == 0 && preparedStagecoach.Preview.ResultingCandidates == 1, "Empty normal recruit pool should receive exactly one candidate.");
        Assert(preparedStagecoach.Preview.RosterHeroCount == 36, "A full 36-hero roster should remain valid for stagecoach generation.");
        Assert(ReadRevision(preparedStagecoach.TownFile.SourceCopyPath).SequenceEqual(ReadRevision(preparedStagecoach.TownFile.EncodedPath)), "Town revision bytes were not preserved.");
        Assert(ReadRevision(preparedStagecoach.RosterFile.SourceCopyPath).SequenceEqual(ReadRevision(preparedStagecoach.RosterFile.EncodedPath)), "Roster revision bytes were not preserved.");
        Assert(ReadRevision(preparedStagecoach.UpgradesFile.SourceCopyPath).SequenceEqual(ReadRevision(preparedStagecoach.UpgradesFile.EncodedPath)), "Upgrades revision bytes were not preserved.");

        var activeHeroUpgradePath = Path.Combine(localHeroUpgradeRoot, "local_hero.upgrades.json");
        var activeHeroUpgradeBytes = File.ReadAllBytes(activeHeroUpgradePath);
        var staleHeroTemplateCommitBlocked = false;
        try
        {
            var changedUpgradeTemplate = File.ReadAllText(activeHeroUpgradePath)
                .Replace(
                    "\"code\": \"c\", \"prerequisite_resolve_level\": 5",
                    "\"code\": \"c\", \"prerequisite_resolve_level\": 4",
                    StringComparison.Ordinal);
            Assert(
                !changedUpgradeTemplate.Equals(
                    Encoding.UTF8.GetString(activeHeroUpgradeBytes),
                    StringComparison.Ordinal),
                "The stale hero upgrade template fixture did not change.");
            File.WriteAllText(activeHeroUpgradePath, changedUpgradeTemplate, new UTF8Encoding(false));
            try
            {
                _ = await stagecoachService.CommitAsync(preparedStagecoach);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains(
                "templates changed after the hero preview",
                StringComparison.Ordinal))
            {
                staleHeroTemplateCommitBlocked = true;
            }
        }
        finally
        {
            File.WriteAllBytes(activeHeroUpgradePath, activeHeroUpgradeBytes);
        }

        Assert(
            staleHeroTemplateCommitBlocked,
            "Stagecoach commit should reject a hero upgrade template changing after preview.");
        Assert(
            !Directory.Exists(stagecoachLocations.BackupDirectory),
            "A stale active-content preview should be rejected before creating a profile backup.");

        var staleUpgrades = File.ReadAllBytes(upgradesSavePath);
        staleUpgrades[^1] ^= 0x01;
        File.WriteAllBytes(upgradesSavePath, staleUpgrades);
        var staleStagecoachCommitBlocked = false;
        try
        {
            _ = await stagecoachService.CommitAsync(preparedStagecoach);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("changed after preview", StringComparison.Ordinal))
        {
            staleStagecoachCommitBlocked = true;
        }

        Assert(staleStagecoachCommitBlocked, "Stagecoach commit should reject any live transaction file changing after preview.");
        Assert(!Directory.Exists(stagecoachLocations.BackupDirectory), "A stale three-file preview should be rejected before creating a backup.");
        File.Copy(preparedStagecoach.UpgradesFile.SourceCopyPath, upgradesSavePath, overwrite: true);

        var rollbackObserved = false;
        string? rollbackBackupDirectory = null;
        using (File.Open(townSavePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            try
            {
                _ = await stagecoachService.CommitAsync(preparedStagecoach);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("restored", StringComparison.Ordinal))
            {
                rollbackObserved = true;
                rollbackBackupDirectory = Directory.EnumerateDirectories(
                        Path.Combine(stagecoachLocations.BackupDirectory, "contract-user", "profile_7"))
                    .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
                    .First();
            }
        }

        Assert(rollbackObserved, "A town replacement failure should restore the already-replaced upgrades and roster files.");
        Assert(File.ReadAllBytes(rosterSavePath).SequenceEqual(File.ReadAllBytes(preparedStagecoach.RosterFile.SourceCopyPath)), "Roster was not restored byte-for-byte after town replacement failed.");
        Assert(File.ReadAllBytes(upgradesSavePath).SequenceEqual(File.ReadAllBytes(preparedStagecoach.UpgradesFile.SourceCopyPath)), "Upgrades were not restored byte-for-byte after town replacement failed.");
        Assert(File.ReadAllBytes(townSavePath).SequenceEqual(File.ReadAllBytes(preparedStagecoach.TownFile.SourceCopyPath)), "Unreplaced town file changed during rollback test.");
        Assert(rollbackBackupDirectory is not null && File.Exists(Path.Combine(rollbackBackupDirectory, "transaction-state.json")), "Rollback transaction state was not recorded.");
        Assert(!File.Exists(Path.Combine(rollbackBackupDirectory!, "commit-result.json")), "A rolled-back transaction must not retain a successful commit result.");
        var rollbackState = JsonNode.Parse(File.ReadAllText(Path.Combine(rollbackBackupDirectory!, "transaction-state.json"))) as JsonObject;
        Assert(rollbackState?["status"]?.GetValue<string>() == "restored", "Rollback transaction state should finish as restored.");

        var stagecoachCommit = await stagecoachService.CommitAsync(preparedStagecoach);
        Assert(File.Exists(Path.Combine(stagecoachCommit.BackupDirectory, "persist.town.json")), "Town backup is missing.");
        Assert(File.Exists(Path.Combine(stagecoachCommit.BackupDirectory, "persist.roster.json")), "Roster backup is missing.");
        Assert(File.Exists(Path.Combine(stagecoachCommit.BackupDirectory, "persist.upgrades.json")), "Upgrades backup is missing.");
        Assert(File.Exists(Path.Combine(stagecoachCommit.BackupDirectory, "backup-manifest.json")), "Stagecoach backup manifest is missing.");
        var stagecoachBackupManifest = JsonNode.Parse(
            File.ReadAllText(Path.Combine(stagecoachCommit.BackupDirectory, "backup-manifest.json"))) as JsonObject;
        Assert(
            stagecoachBackupManifest?["targetPool"]?.GetValue<string>() == "Ordinary" &&
            stagecoachBackupManifest["resolveXp"]?.GetValue<int>() == 24 &&
            stagecoachBackupManifest["weaponRank"]?.GetValue<int>() == 3 &&
            stagecoachBackupManifest["armourRank"]?.GetValue<int>() == 3 &&
            stagecoachBackupManifest["upgradePurchaseCount"]?.GetValue<int>() == 18,
            "Stagecoach backup manifest should record the generated level profile metadata.");
        Assert(File.Exists(Path.Combine(stagecoachCommit.BackupDirectory, "transaction-state.json")), "Stagecoach transaction state is missing.");
        Assert(File.Exists(Path.Combine(stagecoachCommit.BackupDirectory, "commit-result.json")), "Stagecoach commit result is missing.");

        var committedTownDecoded = Path.Combine(runRoot, "committed.persist.town.json");
        var committedRosterDecoded = Path.Combine(runRoot, "committed.persist.roster.json");
        var committedUpgradesDecoded = Path.Combine(runRoot, "committed.persist.upgrades.json");
        await codec.DecodeAsync(townSavePath, committedTownDecoded);
        await codec.DecodeAsync(rosterSavePath, committedRosterDecoded);
        await codec.DecodeAsync(upgradesSavePath, committedUpgradesDecoded);
        var committedTownRoot = JsonNode.Parse(File.ReadAllText(committedTownDecoded)) as JsonObject
            ?? throw new InvalidDataException("Committed town did not decode to an object.");
        var committedRosterRoot = JsonNode.Parse(File.ReadAllText(committedRosterDecoded)) as JsonObject
            ?? throw new InvalidDataException("Committed roster did not decode to an object.");
        var committedUpgradesRoot = JsonNode.Parse(File.ReadAllText(committedUpgradesDecoded)) as JsonObject
            ?? throw new InvalidDataException("Committed upgrades did not decode to an object.");
        var committedGenerated = committedTownRoot["base_root"]?["buildings"]?["stage_coach"]?["store"]?["hero_recruit"]?["generated"] as JsonObject;
        Assert(committedGenerated?.ContainsKey("364") == true, "Committed town is missing candidate GUID 364.");
        Assert(committedRosterRoot["base_root"]?["nextGuid"]?.GetValue<int>() == 365, "Committed roster nextGuid is wrong.");
        Assert((committedRosterRoot["base_root"]?["heroes"] as JsonObject)?.Count == 36, "Committed roster heroes were modified.");
        Assert(
            JsonNode.DeepEquals(committedRosterRoot["base_root"]?["heroes"], rosterHeroesSeed),
            "Committed roster hero subtree differs from the original 36 heroes.");
        var committedPurchases = committedUpgradesRoot["base_root"]?["purchases"] as JsonObject;
        Assert(
            committedPurchases?.Select(pair => pair.Value).OfType<JsonObject>().Count(purchase =>
                purchase["instance_number"]?.GetValue<int>() == 364) == 18,
            "Committed upgrades are missing the candidate's complete level-derived purchase plan.");

        var unfinishedBackupDirectory = Path.Combine(
            stagecoachLocations.BackupDirectory,
            "contract-user",
            "profile_7",
            "synthetic-unfinished");
        Directory.CreateDirectory(unfinishedBackupDirectory);
        var unfinishedStatePath = Path.Combine(unfinishedBackupDirectory, "transaction-state.json");
        File.WriteAllText(
            unfinishedStatePath,
            """{ "status": "replaced_roster" }""",
            new UTF8Encoding(false));
        var unfinishedTransactionBlocked = false;
        try
        {
            _ = await stagecoachService.PrepareStagecoachHeroEditAsync(
                profile,
                levelFourCandidate,
                heroCatalog,
                activeContent);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("unfinished stagecoach save transaction", StringComparison.Ordinal))
        {
            unfinishedTransactionBlocked = true;
        }

        Assert(unfinishedTransactionBlocked, "A non-terminal prior transaction must block a new stagecoach preview.");
        File.WriteAllText(
            unfinishedStatePath,
            """{ "status": "restored" }""",
            new UTF8Encoding(false));

    }
}
