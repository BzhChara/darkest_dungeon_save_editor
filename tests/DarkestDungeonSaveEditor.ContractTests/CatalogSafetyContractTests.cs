internal static partial class ContractSuite
{
    private static void VerifyDirectEncounterDependencyContracts(
        ActiveContentSnapshot content,
        BattleMapSnapshot snapshot,
        string mashPath)
    {
        var original = File.ReadAllBytes(mashPath);
        try
        {
            File.WriteAllText(mashPath,
                "hall: .chance 1 .types unresolved_safety_probe\n" + Encoding.UTF8.GetString(original),
                new UTF8Encoding(false));
            var catalog = BattleEncounterCatalog.Load(content, snapshot);
            var missing = catalog.Encounters.Single(row => row.MonsterIds.Contains("unresolved_safety_probe"));
            var firstValid = catalog.DirectEncounters.First(row => row.MashType == 0);
            Assert(!missing.CanPlaceDirectly && missing.MashIndex == 0 && firstValid.MashIndex == 1 &&
                   missing.UnavailableReason.Contains("unresolved_safety_probe", StringComparison.Ordinal) &&
                   catalog.BridgeEncounters.All(row => !row.MonsterIds.Contains("unresolved_safety_probe")),
                $"A missing monster must block direct and Bridge candidates without removing its native index slot. Missing={missing.CanPlaceDirectly}/{missing.MashIndex}/{missing.UnavailableReason}; first valid={firstValid.MashIndex}/{firstValid.DisplayName}.");
            BattleEncounterCatalog.ValidateDirectEncounter(firstValid);
            var rejected = false;
            try
            {
                BattleEncounterCatalog.ValidateDirectEncounter(missing with { CanPlaceDirectly = true });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("敌方定义", StringComparison.Ordinal))
            {
                rejected = true;
            }
            Assert(rejected, "A caller cannot bypass direct dependency checks by setting CanPlaceDirectly to true.");
        }
        finally
        {
            File.WriteAllBytes(mashPath, original);
        }
    }

    private static async Task VerifyPendingEncounterDependencyContractAsync(
        BattleMapEditService service,
        PreparedBattleMapEdit prepared,
        string monsterPath)
    {
        var hiddenPath = monsterPath + ".unavailable";
        var originalSaveHash = ComputeSha256(prepared.TargetFile.TargetPath);
        File.Move(monsterPath, hiddenPath);
        try
        {
            var rejected = false;
            try
            {
                _ = await service.CommitAsync(prepared);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("敌方定义", StringComparison.Ordinal))
            {
                rejected = true;
            }
            Assert(rejected && ComputeSha256(prepared.TargetFile.TargetPath) == originalSaveHash,
                "Removing a required monster after preview must block commit without changing the map save.");
        }
        finally
        {
            File.Move(hiddenPath, monsterPath);
        }
    }

    private static void VerifyEncounterSelectionContracts(BattleEncounterCatalogResult catalog)
    {
        var ordinary = catalog.BridgeEncounters.First(row => row.Classification == BattleEncounterClassification.Ordinary && row.MashType == 0);
        var special = ordinary with { Classification = BattleEncounterClassification.ConditionalOrAdditional, SourceKind = BattleEncounterSourceKind.Conditional };
        var roaming = ordinary with { Classification = BattleEncounterClassification.RoamingBoss, RoamingId = "roaming_probe" };
        var otherDifficulty = ordinary with { OriginDifficulty = ordinary.OriginDifficulty + 1 };
        var isolated = catalog with
        {
            Encounters = [],
            BridgeEncounters = [ordinary, ordinary, special, roaming, otherDifficulty,
                ordinary with { MashType = 1 }, ordinary with { MashType = 2, Classification = BattleEncounterClassification.FixedBoss },
                ordinary with { HasKnownClassification = false, MonsterIds = ["unknown_classification"] }]
        };
        var normal = BattleEncounterCatalog.GetSelectionCandidates(isolated, 0, [BattleEncounterClassification.Ordinary]);
        var specials = BattleEncounterCatalog.GetSelectionCandidates(isolated, 0,
            [BattleEncounterClassification.RoamingBoss, BattleEncounterClassification.RoamingEncounter, BattleEncounterClassification.ConditionalOrAdditional]);
        Assert(normal.Count == 2 && normal.All(row => row.Classification == BattleEncounterClassification.Ordinary) &&
               normal.Select(row => row.OriginDifficulty).Distinct().Count() == 2 &&
               specials.Count == 1 && specials[0].Classification == BattleEncounterClassification.RoamingBoss,
            "Picker deduplication must keep ordinary and special purposes independent, preserve difficulty choices, and merge only the requested special categories.");
        Assert(BattleEncounterCatalog.GetSelectionCandidates(isolated, 1, [BattleEncounterClassification.Ordinary]).Count == 1 &&
               BattleEncounterCatalog.GetSelectionCandidates(isolated, 2, [BattleEncounterClassification.FixedBoss]).Count == 1,
            "Room, hallway, and fixed-boss target kinds must remain separate after candidate deduplication.");
    }

    private static void VerifyQuestItemReachabilityContracts(ActiveContentSnapshot content, string runRoot)
    {
        var root = Path.Combine(runRoot, "quest-item-reachability");
        Directory.CreateDirectory(Path.Combine(root, "inventory"));
        Directory.CreateDirectory(Path.Combine(root, "campaign", "quest"));
        Directory.CreateDirectory(Path.Combine(root, "campaign", "provision"));
        File.WriteAllLines(Path.Combine(root, "inventory", "quest.inventory.items.darkest"),
            new[] { "reward_only", "provision_only", "overlap", "hero_starter" }.Select(id =>
                $"inventory_item: .type \"estate\" .id \"{id}\" .base_stack_limit 3 .estate_can_be_provision false"),
            new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(root, "campaign", "quest", "test.quest.plot_quests.json"),
            """
            { "plot_quests": [{ "id": "test", "quest": {
              "completion_reward": { "items_definition": { "system_config_type": "quest_rewards", "items": {
                "0": { "type": "estate", "id": "reward_only", "amount": 1 },
                "1": { "type": "estate", "id": "overlap", "amount": 1 }
              } } } },
              "additional_provisions": { "system_config_type": "quest_provision", "items": {
                "0": { "type": "estate", "id": "provision_only", "amount": 1 },
                "1": { "type": "estate", "id": "overlap", "amount": 1 }
              } }
            }] }
            """, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(root, "campaign", "provision", "hero.provision.json"),
            """{"raid_starting_hero_class_item_lists":[{"hero_class":"test","item_lists":[{"type":"estate","id":"hero_starter","amount":3}]}]}""",
            new UTF8Encoding(false));
        WriteFixtureManifest(root);
        var isolated = content with { Sources = [new ActiveContentSource("local:quest-items", "quest-items", "local", root, 0)] };
        var townDocument = JsonNode.Parse("""{"base_root":{"wallet":{},"estate_items":{"items":{}}}}""")!.AsObject();
        var raidDocument = JsonNode.Parse("""{"base_root":{"party":{"inventory":{"items":{}}}}}""")!.AsObject();
        var town = QuantityItemCatalog.Load(isolated, townDocument);
        var raid = QuantityItemCatalog.LoadRaid(isolated, raidDocument);
        bool Visible(QuantityItemCatalogResult result, string id) => !result.Items.Single(item => item.ItemId == id).IsHiddenByDefault;
        Assert(Visible(town, "reward_only") && !Visible(raid, "reward_only") &&
               !Visible(town, "provision_only") && Visible(raid, "provision_only") &&
               Visible(town, "overlap") && Visible(raid, "overlap") &&
               !Visible(town, "hero_starter") && Visible(raid, "hero_starter"),
            "Quest completion rewards and starting provisions must use separate reachability contexts, preserving overlap and hero-only starting items.");
    }
}
