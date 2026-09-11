using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DarkestDungeonSaveEditor.Core;

public sealed partial class SaveEditService
{
    public async Task<PreparedStagecoachHeroEdit> PrepareStagecoachHeroEditAsync(
        SaveProfile profile,
        GeneratedStagecoachHeroCandidate generatedCandidate,
        HeroClassCatalogResult expectedCatalog,
        ActiveContentSnapshot activeContent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(generatedCandidate);
        ArgumentNullException.ThrowIfNull(expectedCatalog);
        ArgumentNullException.ThrowIfNull(activeContent);
        _codec.ValidateAvailability();
        ValidateProfile(profile);
        EnsureNoUnfinishedStagecoachTransaction(profile);
        ValidateActiveContentSnapshot(profile, activeContent);
        var gameRoot = JsonSupport.RequireObject(JsonSupport.ReadObject(activeContent.DecodedGamePath), "base_root");
        var mayRefreshOnTownReturn = gameRoot["inraid"] is JsonValue inRaidNode &&
            inRaidNode.TryGetValue<bool>(out var inRaid) && inRaid;
        var manifestFingerprints = CaptureManifestFingerprints(activeContent.Sources);
        var expectedCatalogSha256 = ComputeHeroCatalogSha256(expectedCatalog);
        var currentCatalog = HeroClassCatalog.Load(activeContent);
        ValidateManifestFingerprints(
            manifestFingerprints,
            "while the hero catalog was being checked; reload the content catalog");
        if (!ComputeHeroCatalogSha256(currentCatalog).Equals(
                expectedCatalogSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The active hero, quirk, level, or upgrade templates changed after the catalog was loaded; " +
                "reload the content catalog.");
        }

        ValidateGeneratedStagecoachCandidate(currentCatalog, generatedCandidate);
        var contentGuard = new PreparedStagecoachContentGuard(
            activeContent.SourceGameSha256,
            activeContent.GameMode,
            activeContent.Sources.ToArray(),
            expectedCatalogSha256,
            manifestFingerprints);

        var townTargetPath = GetProfileSavePath(profile, "persist.town.json");
        var rosterTargetPath = GetProfileSavePath(profile, "persist.roster.json");
        var upgradesTargetPath = GetProfileSavePath(profile, "persist.upgrades.json");
        ValidateRequiredSaveFile(townTargetPath, "persist.town.json");
        ValidateRequiredSaveFile(rosterTargetPath, "persist.roster.json");
        ValidateRequiredSaveFile(upgradesTargetPath, "persist.upgrades.json");

        var townOriginalHash = ComputeSha256(townTargetPath);
        var rosterOriginalHash = ComputeSha256(rosterTargetPath);
        var upgradesOriginalHash = ComputeSha256(upgradesTargetPath);
        var sessionId = $"{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}";
        var workspace = Path.Combine(_locations.WorkspaceDirectory, sessionId);
        var sourceDirectory = Path.Combine(workspace, "source");
        var decodedDirectory = Path.Combine(workspace, "decoded");
        var encodedDirectory = Path.Combine(workspace, "encoded");
        var roundTripDirectory = Path.Combine(workspace, "roundtrip");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(decodedDirectory);
        Directory.CreateDirectory(encodedDirectory);
        Directory.CreateDirectory(roundTripDirectory);

        var townSourceCopy = Path.Combine(sourceDirectory, "persist.town.json");
        var rosterSourceCopy = Path.Combine(sourceDirectory, "persist.roster.json");
        var upgradesSourceCopy = Path.Combine(sourceDirectory, "persist.upgrades.json");
        var townDecodedPath = Path.Combine(decodedDirectory, "persist.town.json");
        var rosterDecodedPath = Path.Combine(decodedDirectory, "persist.roster.json");
        var upgradesDecodedPath = Path.Combine(decodedDirectory, "persist.upgrades.json");
        var townProposedPath = Path.Combine(decodedDirectory, "persist.town.proposed.json");
        var rosterProposedPath = Path.Combine(decodedDirectory, "persist.roster.proposed.json");
        var upgradesProposedPath = Path.Combine(decodedDirectory, "persist.upgrades.proposed.json");
        var townEncodedPath = Path.Combine(encodedDirectory, "persist.town.json");
        var rosterEncodedPath = Path.Combine(encodedDirectory, "persist.roster.json");
        var upgradesEncodedPath = Path.Combine(encodedDirectory, "persist.upgrades.json");
        var townRoundTripPath = Path.Combine(roundTripDirectory, "persist.town.json");
        var rosterRoundTripPath = Path.Combine(roundTripDirectory, "persist.roster.json");
        var upgradesRoundTripPath = Path.Combine(roundTripDirectory, "persist.upgrades.json");

        File.Copy(townTargetPath, townSourceCopy, overwrite: false);
        File.Copy(rosterTargetPath, rosterSourceCopy, overwrite: false);
        File.Copy(upgradesTargetPath, upgradesSourceCopy, overwrite: false);
        ValidateUnchangedDuringCopy(townTargetPath, townSourceCopy, townOriginalHash, "town");
        ValidateUnchangedDuringCopy(rosterTargetPath, rosterSourceCopy, rosterOriginalHash, "roster");
        ValidateUnchangedDuringCopy(
            upgradesTargetPath,
            upgradesSourceCopy,
            upgradesOriginalHash,
            "upgrades");

        await _codec.DecodeAsync(townSourceCopy, townDecodedPath, cancellationToken).ConfigureAwait(false);
        await _codec.DecodeAsync(rosterSourceCopy, rosterDecodedPath, cancellationToken).ConfigureAwait(false);
        await _codec.DecodeAsync(upgradesSourceCopy, upgradesDecodedPath, cancellationToken).ConfigureAwait(false);
        var townRoot = JsonSupport.ReadObject(townDecodedPath);
        var rosterRoot = JsonSupport.ReadObject(rosterDecodedPath);
        var upgradesRoot = JsonSupport.ReadObject(upgradesDecodedPath);
        var quirkLimits = StagecoachHeroSaveEditor.AnalyzeQuirkLimits(
            townRoot,
            rosterRoot,
            generatedCandidate.Candidate,
            currentCatalog.InitialQuirks);
        var (updatedTown, updatedRoster, updatedUpgrades, mutationPreview) = StagecoachHeroSaveEditor.AddCandidate(
            townRoot,
            rosterRoot,
            upgradesRoot,
            generatedCandidate.Candidate,
            generatedCandidate.UpgradePurchases);
        var preview = mutationPreview with
        {
            QuirkLimits = quirkLimits,
            MayRefreshOnTownReturn = mayRefreshOnTownReturn
        };
        JsonSupport.WriteObject(townProposedPath, updatedTown);
        JsonSupport.WriteObject(rosterProposedPath, updatedRoster);
        JsonSupport.WriteObject(upgradesProposedPath, updatedUpgrades);

        await _codec.EncodeAsync(
            upgradesProposedPath,
            upgradesEncodedPath,
            upgradesSourceCopy,
            cancellationToken).ConfigureAwait(false);
        await _codec.EncodeAsync(
            rosterProposedPath,
            rosterEncodedPath,
            rosterSourceCopy,
            cancellationToken).ConfigureAwait(false);
        await _codec.EncodeAsync(
            townProposedPath,
            townEncodedPath,
            townSourceCopy,
            cancellationToken).ConfigureAwait(false);
        await _codec.DecodeAsync(
            upgradesEncodedPath,
            upgradesRoundTripPath,
            cancellationToken).ConfigureAwait(false);
        await _codec.DecodeAsync(rosterEncodedPath, rosterRoundTripPath, cancellationToken).ConfigureAwait(false);
        await _codec.DecodeAsync(townEncodedPath, townRoundTripPath, cancellationToken).ConfigureAwait(false);

        var upgradesRoundTripRoot = JsonSupport.ReadObject(upgradesRoundTripPath);
        var rosterRoundTripRoot = JsonSupport.ReadObject(rosterRoundTripPath);
        var townRoundTripRoot = JsonSupport.ReadObject(townRoundTripPath);
        var townWasDson = DsonSaveCodec.IsDson(townSourceCopy);
        var rosterWasDson = DsonSaveCodec.IsDson(rosterSourceCopy);
        var upgradesWasDson = DsonSaveCodec.IsDson(upgradesSourceCopy);
        if (!JsonNode.DeepEquals(updatedUpgrades, upgradesRoundTripRoot) ||
            !JsonNode.DeepEquals(updatedRoster, rosterRoundTripRoot) ||
            !(townWasDson
                ? HasEquivalentStagecoachTownRoundTrip(updatedTown, townRoundTripRoot, preview)
                : JsonNode.DeepEquals(updatedTown, townRoundTripRoot)))
        {
            throw new InvalidDataException(
                "DSON roundtrip validation failed for the prepared stagecoach town/roster/upgrades edit.");
        }

        if ((townWasDson && !RevisionMatches(townSourceCopy, townEncodedPath)) ||
            (rosterWasDson && !RevisionMatches(rosterSourceCopy, rosterEncodedPath)) ||
            (upgradesWasDson && !RevisionMatches(upgradesSourceCopy, upgradesEncodedPath)))
        {
            throw new InvalidDataException(
                "An encoded stagecoach save did not preserve its original DSON revision bytes.");
        }

        ValidateStagecoachContentGuard(profile, contentGuard);

        var prepared = new PreparedStagecoachHeroEdit(
            sessionId,
            profile,
            preview,
            contentGuard,
            workspace,
            new PreparedSaveFile(
                "persist.town.json",
                townTargetPath,
                townSourceCopy,
                townProposedPath,
                townEncodedPath,
                townRoundTripPath,
                townOriginalHash,
                ComputeSha256(townEncodedPath),
                townWasDson),
            new PreparedSaveFile(
                "persist.roster.json",
                rosterTargetPath,
                rosterSourceCopy,
                rosterProposedPath,
                rosterEncodedPath,
                rosterRoundTripPath,
                rosterOriginalHash,
                ComputeSha256(rosterEncodedPath),
                rosterWasDson),
            new PreparedSaveFile(
                "persist.upgrades.json",
                upgradesTargetPath,
                upgradesSourceCopy,
                upgradesProposedPath,
                upgradesEncodedPath,
                upgradesRoundTripPath,
                upgradesOriginalHash,
                ComputeSha256(upgradesEncodedPath),
                upgradesWasDson),
            DateTime.UtcNow);
        WriteJson(Path.Combine(workspace, "session.json"), prepared);
        return prepared;
    }

    internal static bool HasEquivalentStagecoachTownRoundTrip(
        JsonObject expectedTown, JsonObject restoredTown, StagecoachHeroMutationPreview preview)
    {
        if (JsonNode.DeepEquals(expectedTown, restoredTown)) return true;

        var pool = preview.TargetPool == StagecoachRecruitPool.Shard ? "shard_hero_recruit" : "hero_recruit";
        var key = preview.CandidateGuid.ToString(System.Globalization.CultureInfo.InvariantCulture);
        JsonObject? Actor(JsonObject town)
        {
            JsonNode? node = town;
            foreach (var part in new[] { "base_root", "buildings", "stage_coach", "store", pool, "generated", key, "actor" })
                node = (node as JsonObject)?[part];
            return node as JsonObject;
        }

        var expectedHp = Actor(expectedTown)?["current_hp"] as JsonValue;
        var restoredHp = Actor(restoredTown)?["current_hp"] as JsonValue;
        if (expectedHp is null || restoredHp is null ||
            !expectedHp.TryGetValue<double>(out var expected) || !restoredHp.TryGetValue<double>(out var restored) ||
            !float.IsFinite((float)expected) || (float)expected <= 0 ||
            !float.IsFinite((float)restored) || (float)restored <= 0 ||
            restoredHp.ToJsonString().IndexOfAny(['.', 'e', 'E']) < 0 ||
            BitConverter.SingleToInt32Bits((float)expected) != BitConverter.SingleToInt32Bits((float)restored))
            return false;

        // Java versions can print different decimals for the identical DSON
        // float (1.0E11 / 9.9999998E10). Normalize only this new hero's HP in a
        // comparison clone after proving identical bits; all other data stays
        // under full-document equality, including existing heroes' HP.
        var comparable = (JsonObject)restoredTown.DeepClone();
        Actor(comparable)!["current_hp"] = expectedHp.DeepClone();
        return JsonNode.DeepEquals(expectedTown, comparable);
    }

    private static void ValidateStagecoachContentGuard(
        SaveProfile profile,
        PreparedStagecoachContentGuard contentGuard)
    {
        var gameSavePath = Path.Combine(profile.ProfileDirectory, "persist.game.json");
        if (!File.Exists(gameSavePath) ||
            !ComputeSha256(gameSavePath).Equals(
                contentGuard.SourceGameSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The active Mod/DLC configuration changed after the hero preview; prepare a new preview.");
        }

        ValidateManifestFingerprints(
            contentGuard.ManifestFingerprints,
            "after the hero preview; prepare a new preview");
        var currentSnapshot = new ActiveContentSnapshot(
            profile,
            contentGuard.GameMode,
            contentGuard.Sources,
            [],
            string.Empty,
            string.Empty,
            0,
            contentGuard.SourceGameSha256);
        var currentCatalog = HeroClassCatalog.Load(currentSnapshot);
        ValidateManifestFingerprints(
            contentGuard.ManifestFingerprints,
            "while the hero preview was being revalidated; prepare a new preview");
        if (!ComputeHeroCatalogSha256(currentCatalog).Equals(
                contentGuard.HeroCatalogSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The active hero, quirk, level, or upgrade templates changed after the hero preview; " +
                "prepare a new preview.");
        }
    }

    private static void ValidateGeneratedStagecoachCandidate(
        HeroClassCatalogResult catalog,
        GeneratedStagecoachHeroCandidate generatedCandidate)
    {
        var heroClassId = JsonSupport.ReadString(generatedCandidate.Candidate, "heroClass");
        var heroClass = catalog.HeroClasses.SingleOrDefault(item =>
            item.Id.Equals(heroClassId, StringComparison.Ordinal));
        if (heroClass is null ||
            !generatedCandidate.Preview.HeroClass.Equals(heroClassId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The generated stagecoach candidate does not match an exact active hero class definition.");
        }

        var levelProfile = heroClass.LevelProfiles.SingleOrDefault(profile =>
            profile.ResolveLevel == generatedCandidate.Preview.ResolveLevel);
        if (levelProfile is null ||
            levelProfile.ResolveXp != generatedCandidate.Preview.ResolveXp ||
            levelProfile.WeaponRank != generatedCandidate.Preview.WeaponRank ||
            levelProfile.ArmourRank != generatedCandidate.Preview.ArmourRank)
        {
            throw new InvalidOperationException(
                "The generated stagecoach candidate no longer matches the active hero level template.");
        }

        var expectedPurchases = StagecoachHeroCandidateFactory.BuildUpgradePurchases(
            heroClass,
            levelProfile.ResolveLevel);
        if (!expectedPurchases.SequenceEqual(generatedCandidate.UpgradePurchases))
        {
            throw new InvalidOperationException(
                "The generated stagecoach candidate no longer matches the active all-skill upgrade plan.");
        }
        HeroEquipmentProgression.Validate(heroClass, levelProfile, expectedPurchases);
    }

    private static string ComputeHeroCatalogSha256(HeroClassCatalogResult catalog)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            catalog.GameMode,
            catalog.ResolveLevelThresholds,
            catalog.HeroClasses,
            Equipment = catalog.HeroClasses.Select(hero => new { hero.Id, hero.Equipment }),
            catalog.RecruitEvents,
            catalog.InitialQuirks,
            catalog.HeroNames
        }, JsonSupport.SerializerOptions);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

}
