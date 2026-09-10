internal static partial class ContractSuite
{
    private static async Task VerifyNativeEncounterChanceAsync(string runRoot, DsonSaveCodec codec)
    {
        var root = Path.Combine(runRoot, "native-encounter-chance");
        var game = Path.Combine(root, "game");
        var mods = Path.Combine(game, "mods"); Directory.CreateDirectory(mods);
        var profile = WriteBattleSafetyProfile(Path.Combine(root, "profile"));
        CreateBattleMonsterDefinitions(game, ["alpha"]);
        var content = await ActiveContentResolver.ResolveAsync(profile, game, null, mods, codec, Path.Combine(root, "workspace"));
        var snapshot = await new BattleMapSnapshotReader(codec).LoadAsync(profile.ProfileDirectory);
        foreach (var (type, kind) in new[] { (0, "hall"), (1, "room"), (2, "boss") })
        foreach (var (body, weight) in new[]
        {
            (".chance 1 .types alpha alpha alpha alpha", 1d),
            (".chance 1 .types alpha alpha alpha alpha .chance", 0d),
            (".chance 1 .types alpha alpha alpha alpha .chance ", 0d),
            (".chance 1e-50 .types alpha alpha alpha alpha", 0d),
            (".chance -1e-50 .types alpha alpha alpha alpha", -0d),
            (".chance 25% .types alpha alpha alpha alpha", 0.25d),
            (".chance 0.7.types alpha alpha alpha alpha", (double)0.7f),
            (".chance 1 .types alpha alpha alpha alpha .chance\n0", 1d),
            (".chance 1 .types alpha alpha alpha alpha .chance=0", 0d),
            (".types alpha alpha alpha alpha", 0d)
        })
        {
            WriteMultiMash(game, "dungeons/cove/cove.2.mash.darkest", $"{kind}: {body}");
            var catalog = BattleEncounterCatalog.Load(content, snapshot);
            var row = catalog.Encounters.Single();
            var classification = type == 2 ? BattleEncounterClassification.FixedBoss : weight > 0
                ? BattleEncounterClassification.Ordinary : BattleEncounterClassification.ConditionalOrAdditional;
            Assert(row.Weight == weight && row.Classification == classification && row.MashIndex == 0 && row.CanPlaceDirectly,
                $"{kind}/{body}: native float weight must affect classification without deleting the numeric slot.");
            Assert(BattleEncounterCatalog.GetSelectionCandidates(catalog, type, [BattleEncounterClassification.Ordinary]).Count ==
                   (classification == BattleEncounterClassification.Ordinary ? 1 : 0),
                "Ordinary filtering must exclude zero-weight formations while retaining positive native weights.");
            BattleEncounterCatalog.ValidateDirectEncounter(row);
            Assert(BattleEncounterCatalog.ResolveAppendTarget(catalog, type).NextMashIndex == 1,
                "Zero-weight formations must retain their place for direct validation and subsequent Bridge appends.");
        }
        Console.WriteLine("PASS: native chance boundaries/float/percent values, classification, direct validation and Bridge append slots for all three types.");
    }
}
