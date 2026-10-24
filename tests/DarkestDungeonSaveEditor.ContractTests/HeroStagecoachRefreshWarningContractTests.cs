internal static partial class ContractSuite
{
    private static async Task VerifyStagecoachRefreshWarningContractsAsync(
        ActiveContentSnapshot activeContent,
        SaveProfile profile,
        DsonSaveCodec codec,
        string runRoot,
        HeroClassCatalogResult catalog,
        HeroCandidateContractContext candidates)
    {
        var sourceFiles = new[] { "persist.game.json", "persist.estate.json", "persist.town.json", "persist.roster.json", "persist.upgrades.json" };
        var originalHashes = sourceFiles.ToDictionary(name => name,
            name => ComputeSha256(Path.Combine(profile.ProfileDirectory, name)), StringComparer.Ordinal);
        foreach (var inRaid in new[] { false, true })
        {
            foreach (var pool in new[] { StagecoachRecruitPool.Ordinary, StagecoachRecruitPool.Shard })
            {
                var scenarioRoot = Path.Combine(runRoot, "stagecoach-refresh-warning", $"{inRaid}-{pool}");
                var profileRoot = Path.Combine(scenarioRoot, "profile_8");
                Directory.CreateDirectory(profileRoot);
                foreach (var name in sourceFiles)
                {
                    File.Copy(Path.Combine(profile.ProfileDirectory, name), Path.Combine(profileRoot, name));
                }
                var scenarioProfile = profile with
                {
                    ProfileId = "profile_8",
                    ProfileDirectory = profileRoot,
                    EstateSavePath = Path.Combine(profileRoot, "persist.estate.json")
                };
                // Both scenes retain a raid file: file presence must not cause a town warning.
                await File.WriteAllTextAsync(scenarioProfile.RaidSavePath, """{"base_root":{"test_residue":true}}""", new UTF8Encoding(false));
                var game = JsonNode.Parse(await File.ReadAllTextAsync(activeContent.DecodedGamePath))!.AsObject();
                game["base_root"]!["inraid"] = inRaid;
                game["base_root"]!["raiddungeon"] = inRaid ? "weald" : "none";
                var decodedGame = Path.Combine(scenarioRoot, "game.decoded.json");
                var gamePath = Path.Combine(profileRoot, "persist.game.json");
                await File.WriteAllTextAsync(decodedGame, game.ToJsonString(), new UTF8Encoding(false));
                await codec.EncodeAsync(decodedGame, gamePath, Path.Combine(profile.ProfileDirectory, "persist.game.json"));
                var scenarioContent = activeContent with
                {
                    Profile = scenarioProfile,
                    WorkspaceDirectory = scenarioRoot,
                    DecodedGamePath = decodedGame,
                    SourceGameSha256 = ComputeSha256(gamePath)
                };
                var service = new SaveEditService(codec, new SaveEditorLocations(
                    Path.Combine(scenarioRoot, "appdata"), Path.Combine(scenarioRoot, "workspaces"), Path.Combine(scenarioRoot, "backups")));
                var candidate = pool == StagecoachRecruitPool.Ordinary ? candidates.ContextLimitedCandidate : candidates.ShardCandidate;
                var before = sourceFiles.Append("persist.raid.json").ToDictionary(name => name,
                    name => ComputeSha256(Path.Combine(profileRoot, name)), StringComparer.Ordinal);
                if (inRaid)
                {
                    var rejected = false;
                    try
                    {
                        await service.PrepareStagecoachHeroEditAsync(scenarioProfile, candidate, catalog, scenarioContent);
                    }
                    catch (InvalidOperationException error) when (error.Message == EditorText.Get("TownOnlyOperation"))
                    {
                        rejected = true;
                    }
                    Assert(rejected && before.All(pair => ComputeSha256(Path.Combine(profileRoot, pair.Key)) == pair.Value),
                        "Both stagecoach pools must reject in-raid generation before changing any save.");
                    continue;
                }
                var prepared = await service.PrepareStagecoachHeroEditAsync(scenarioProfile, candidate, catalog, scenarioContent);
                Assert(!prepared.Preview.MayRefreshOnTownReturn && prepared.Preview.TargetPool == pool,
                    "A stale raid file must not block generation in a town profile.");
                Assert(before.All(pair => ComputeSha256(Path.Combine(profileRoot, pair.Key)) == pair.Value),
                    "Preparing a scene warning must not mutate any source save.");
                if (pool == StagecoachRecruitPool.Ordinary)
                {
                    Assert(prepared.Preview.QuirkLimits.Any(limit => limit.ExceedsDefinitionLimit),
                        "A refresh warning must coexist with the existing singleton limit warning.");
                }

                if (pool == StagecoachRecruitPool.Ordinary)
                {
                    game["base_root"]!["inraid"] = true;
                    game["base_root"]!["raiddungeon"] = "weald";
                    await File.WriteAllTextAsync(decodedGame, game.ToJsonString(), new UTF8Encoding(false));
                    await codec.EncodeAsync(decodedGame, gamePath, Path.Combine(profile.ProfileDirectory, "persist.game.json"));
                    var rejected = false;
                    try
                    {
                        await service.CommitAsync(prepared);
                    }
                    catch (InvalidOperationException error) when (error.Message.Contains("changed after the hero preview", StringComparison.Ordinal))
                    {
                        rejected = true;
                    }
                    Assert(rejected && new[] { "persist.town.json", "persist.roster.json", "persist.upgrades.json" }
                               .All(name => ComputeSha256(Path.Combine(profileRoot, name)) == before[name]),
                        "A scene change after a no-warning preview must invalidate the edit without touching its target saves.");
                }
            }
        }
        Assert(originalHashes.All(pair => ComputeSha256(Path.Combine(profile.ProfileDirectory, pair.Key)) == pair.Value),
            "Refresh-warning scenarios must leave the shared contract fixture unchanged.");
    }
}
