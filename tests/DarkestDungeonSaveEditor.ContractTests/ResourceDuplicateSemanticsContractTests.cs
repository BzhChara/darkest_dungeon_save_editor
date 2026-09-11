using System.Collections;
using System.Reflection;

internal static partial class ContractSuite
{
    public static async Task RunResourceSemanticsOnlyAsync(string repositoryRoot)
    {
        var fixture = BuildContractFixture(repositoryRoot);
        await VerifyCampingPurchaseIdentitiesAsync(fixture.RunRoot, fixture.Codec);
        await fixture.Codec.EncodeAsync(fixture.DecodedGameSeedPath, fixture.GameSavePath, originalBinaryPath: null);
        var profile = new SaveProfile("profile_7", fixture.ProfileRoot, fixture.EstatePath, "contract-user", DateTime.UtcNow);
        var content = await ActiveContentResolver.ResolveAsync(profile, fixture.GameRoot, fixture.WorkshopRoot,
            fixture.AdditionalLocalModDirectory, fixture.Codec, Path.Combine(fixture.RunRoot, "workspace"));
        var hero = HeroClassCatalog.Load(content).HeroClasses.Single(row => row.Id == "local_hero");
        await VerifyInventoryIdentitiesAsync(content, fixture.RunRoot, fixture.Codec);
        VerifyHeroNativeSemantics(content, hero, fixture.RunRoot);
        VerifyNativeItemReferences(content, fixture.RunRoot);
        VerifyResourceDuplicateSemantics(content, hero, fixture.RunRoot);
        await VerifyHeroExactIdentitiesAsync(content, hero, fixture.RunRoot, fixture.Codec);
        await VerifyResourceConsumerRecordsAsync(content, hero, fixture.RunRoot, fixture.Codec);
        await VerifyBuffPrecisionAndIdentityAsync(content, hero, fixture.RunRoot, fixture.Codec);
        VerifyReferenceFileEligibility(content, hero, fixture.RunRoot);
        await VerifyJsonMembersAndBuffReferencesAsync(content, hero, fixture.RunRoot, fixture.Codec);
        VerifyJsonReferenceConsumers(content, fixture.RunRoot);
        await VerifyNativeEncounterChanceAsync(fixture.RunRoot, fixture.Codec);
        await RunEmptyEncounterSlotContractsAsync(fixture.RunRoot, fixture.Codec);
        await RunEncounterRecordContractsAsync(fixture.RunRoot, fixture.Codec);
        Console.WriteLine($"Artifacts: {fixture.RunRoot}");
    }

    private static void VerifyResourceDuplicateSemantics(ActiveContentSnapshot original, HeroClassDefinition originalHero, string runRoot)
    {
        var root = Path.Combine(runRoot, "resource-duplicate-semantics");
        void Write(string path, string text) => WriteMultiMash(root, path, text);
        var heroPath = $"heroes/{originalHero.Id}/{originalHero.Id}";
        Write(heroPath + ".info.darkest", File.ReadAllText(originalHero.SourcePath) + "\n" + """
            generation: .is_generation_enabled false
            combat_skill: .id "semantics_probe" .level 0 .effect "SD_REPLACE" "SD_SHARED"
            combat_skill: .id "semantics_probe" .level 0 .effect "SD_OMIT" "SD_SHARED" "SD_CLEAR" "SD_SET" "SD_DEFAULT" "SD_EMPTY" "SD_LAST_FIELD"
            combat_skill: .id "semantics_probe" .level 0 .effect ""
            combat_skill: .id "semantics_probe" .level 1 .effect "SD_HIGH_LEVEL"
            """);
        Write(heroPath + ".override.darkest", "combat_skill: .id semantics_probe .level 0 .effect \"SD_OVERRIDE\"\n");
        Write("effects/a.effects.darkest", """
            effect: .name "SD_REPLACE" .disease "sd_old"
            effect: .name "SD_SHARED" .disease "sd_old"
            effect: .name "SD_OMIT" .disease "sd_old"
            effect: .name "SD_CLEAR" .disease "sd_old"
            effect: .name "SD_SET" .disease ""
            effect: .name "SD_DEFAULT" .duration 2
            effect: .name "SD_EMPTY" .disease ""
            effect: .name "SD_LAST_FIELD" .disease "sd_old" .disease "sd_new"
            effect: .name "SD_HIGH_LEVEL" .disease "sd_high"
            effect: .name "SD_OVERRIDE" .disease "sd_new"
            """);
        Write("effects/b.effects.darkest", """
            effect: .name "SD_REPLACE" .disease "sd_new"
            effect: .name "SD_OMIT" .duration 4
            effect: .name "SD_CLEAR" .disease ""
            effect: .name "SD_SET" .disease "sd_new"
            """);
        Write("shared/quirk/semantics.quirk_library.json", """
            {"quirks":[
              {"id":"sd_old","random_chance":0,"is_positive":true},
              {"id":"sd_new","random_chance":0,"is_positive":false},
              {"id":"sd_high","random_chance":0,"is_positive":true},
              {"id":"sd_hp","random_chance":1,"is_positive":true,"buffs":["SD_HP"]},
              {"id":"sd_drop_hp","random_chance":1,"is_positive":true,"buffs":["SD_DROP_HP"]},
              {"id":"sd_bad_hp","random_chance":1,"is_positive":true,"buffs":["SD_BAD_HP"]}
            ]}
            """);
        Write("shared/buffs/a.buffs.json", """
            {"buffs":[
              {"id":"SD_HP","stat_type":"combat_stat_multiply","stat_sub_type":"max_hp","amount":0.1,"rule_type":"always","is_false_rule":false},
              {"id":"SD_DROP_HP","stat_type":"combat_stat_multiply","stat_sub_type":"max_hp","amount":0.1,"rule_type":"always","is_false_rule":false},
              {"id":"SD_BAD_HP","stat_type":"combat_stat_multiply","stat_sub_type":"max_hp","amount":0.1,"rule_type":"always","is_false_rule":false}
            ]}
            """);
        Write("shared/buffs/b.buffs.json", """
            {"buffs":[
              {"id":"SD_HP","stat_type":"combat_stat_multiply","stat_sub_type":"max_hp","amount":0.25,"rule_type":"always","is_false_rule":false},
              {"id":"SD_DROP_HP","stat_type":"combat_stat_add","stat_sub_type":"speed_rating","amount":7,"rule_type":"always","is_false_rule":false},
              {"id":"SD_BAD_HP","stat_type":"combat_stat_multiply","stat_sub_type":"max_hp","rule_type":"always","is_false_rule":false}
            ]}
            """);
        Write("campaign/town_events/a.town_events.events.json", $$$"""
            {"events":[
              {"id":"sd_recruit","data":[{"type":"bonus_recruit","string_data":"{{{originalHero.Id}}}","number_data":1}]},
              {"id":"sd_no_recruit","data":[]},
              {"id":"sd_reward","data":[{"type":"bonus_currency","string_data":"sd_first_item","number_data":1}]},
              {"id":"sd_no_reward"}
            ]}
            """);
        Write("campaign/town_events/b.town_events.events.json", $$$"""
            {"events":[
              {"id":"sd_recruit","data":[{"type":"bonus_recruit","string_data":"{{{originalHero.Id}}}","number_data":2}]},
              {"id":"sd_no_recruit","data":[{"type":"bonus_recruit","string_data":"{{{originalHero.Id}}}","number_data":9}]},
              {"id":"sd_reward","data":[{"type":"bonus_currency","string_data":"sd_later_item","number_data":1}],
                "cost":{"type":"estate","id":"sd_candidate_cost","amount":1}},
              {"id":"sd_no_reward","data":[{"type":"bonus_currency","string_data":"sd_absent_item","number_data":1}]}
            ]}
            """);
        Write("inventory/semantics.inventory.items.darkest", string.Join('\n', new[]
            { "sd_first_item", "sd_later_item", "sd_absent_item", "sd_candidate_cost" }.Select(id =>
            $"inventory_item: .type estate .id {id} .base_stack_limit 3 .estate_can_be_provision false")));
        WriteFixtureManifest(root);
        var content = original with { Sources = original.Sources.Append(
            new ActiveContentSource("local:duplicate-semantics", "Semantics", "local", root, -1500)).ToArray() };
        var catalog = HeroClassCatalog.Load(content);
        var hero = catalog.HeroClasses.Single(row => row.Id == originalHero.Id);
        var signals = hero.RuntimeQuirkSignals.Where(row => row.SkillId == "semantics_probe")
            .ToDictionary(row => row.EffectName, row => row.QuirkId, StringComparer.Ordinal);
        Assert(signals.Count == 6 && signals["SD_REPLACE"] == "sd_new" && signals["SD_SHARED"] == "sd_old" &&
               signals["SD_OMIT"] == "sd_old" && signals["SD_SET"] == "sd_new" && signals["SD_LAST_FIELD"] == "sd_new" &&
               signals["SD_OVERRIDE"] == "sd_new",
            "Disease overwrite/omission/explicit clear/defaults and appended info/override effects must agree; level-1 effects must not leak into level-0 signals.");

        // Inspect the parsed list before the public UI's deliberate signal deduplication.
        // The acceptance list is the native experiment's append order, including Shared twice.
        var files = NativeContentFileResolver.ResolveActorFiles(content.Sources, "heroes", []);
        var sources = content.Sources.ToDictionary(source => source.Id, StringComparer.OrdinalIgnoreCase);
        var candidate = typeof(HeroClassCatalog).GetMethod("ReadHeroInfo", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [files[heroPath + ".info.darkest"], sources])!;
        candidate = typeof(HeroClassCatalog).GetMethod("ApplyHeroOverrides", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [candidate, new[] { files[heroPath + ".override.darkest"] }, sources])!;
        var refs = ((IEnumerable)candidate.GetType().GetProperty("SkillEffects")!.GetValue(candidate)!).Cast<object>()
            .Where(row => (string)row.GetType().GetProperty("SkillId")!.GetValue(row)! == "semantics_probe")
            .Select(row => (string)row.GetType().GetProperty("EffectName")!.GetValue(row)!).ToArray();
        Assert(refs.SequenceEqual(new[] { "SD_REPLACE", "SD_SHARED", "SD_OMIT", "SD_SHARED", "SD_CLEAR", "SD_SET",
                "SD_DEFAULT", "SD_EMPTY", "SD_LAST_FIELD", "SD_OVERRIDE" }),
            "The parsed skill list must preserve duplicate references and source order across the override builder copy.");
        var hp = catalog.InitialQuirks.Single(row => row.Id == "sd_hp");
        var noHp = catalog.InitialQuirks.Single(row => row.Id == "sd_drop_hp");
        Assert(hp.WriteStatus == HeroInitialQuirkWriteStatus.Direct && hp.MaxHpModifiers is [{ Amount: 0.25 }] &&
               noHp.WriteStatus == HeroInitialQuirkWriteStatus.Direct && noHp.MaxHpModifiers.Count == 0 &&
               catalog.InitialQuirks.Single(row => row.Id == "sd_bad_hp").WriteStatus == HeroInitialQuirkWriteStatus.Unverified,
            "Last-complete Buff replacement must replace both amount and stat kind, and must not inherit missing fields from the old object.");
        var baselineHp = StagecoachHeroCandidateFactory.Generate(catalog, hero, 1729).Preview.CurrentHp;
        Assert(Math.Abs(StagecoachHeroCandidateFactory.Generate(catalog, hero, 1729, ["sd_hp"]).Preview.CurrentHp - baselineHp * 1.25) < 1e-9 &&
               StagecoachHeroCandidateFactory.Generate(catalog, hero, 1729, ["sd_drop_hp"]).Preview.CurrentHp == baselineHp,
            "Corrected Buff resolution must reach actual candidate HP even while ordinary recruitment is disabled.");
        Assert(hero.Generation?.IsEnabled == false && hero.RecruitEvents.Single(row => row.Id == "sd_recruit").Count == 1 &&
               catalog.RecruitEvents.All(row => row.Id != "sd_no_recruit"),
            "First event results must retain an empty winner; a disabled random generation flag does not remove bonus_recruit sources.");
        var town = QuantityItemCatalog.Load(content with { Sources = [content.Sources.Last()] },
            JsonNode.Parse("""{"base_root":{"wallet":{},"estate_items":{"items":{}}}}""")!.AsObject());
        QuantityItemReferenceStatus Status(string id) => town.Items.Single(row => row.ItemId == id).ReferenceStatus;
        Assert(Status("sd_first_item") == QuantityItemReferenceStatus.ConfirmedActive &&
               Status("sd_candidate_cost") == QuantityItemReferenceStatus.SuspectedUnused &&
               Status("sd_later_item") == QuantityItemReferenceStatus.SuspectedUnused &&
               Status("sd_absent_item") == QuantityItemReferenceStatus.SuspectedUnused,
            "Item reachability must follow first event result data, including missing/empty winners; arbitrary event cost objects are not native consumers.");
        Console.WriteLine("PASS: native Buff replacement, Effect disease fields, skill effect append, first event results and item dependencies.");
    }
}
