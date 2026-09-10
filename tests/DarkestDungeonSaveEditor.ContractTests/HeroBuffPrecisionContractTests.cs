internal static partial class ContractSuite
{
    private static async Task VerifyBuffPrecisionAndIdentityAsync(ActiveContentSnapshot original,
        HeroClassDefinition originalHero, string runRoot, DsonSaveCodec codec)
    {
        var root = Path.Combine(runRoot, "buff-precision-identities");
        void Write(string path, string text) => WriteMultiMash(root, path, text);
        var heroPath = $"heroes/{originalHero.Id}/{originalHero.Id}.info.darkest";
        Write(heroPath, File.ReadAllText(originalHero.SourcePath));
        var content = original with { Sources = original.Sources.Append(
            new ActiveContentSource("local:buff-precision", "Buff precision", "local", root, -2200)).ToArray() };
        JsonObject Buff(string id, double amount, string subtype = "max_hp", string rule = "always", string mode = "") => new()
        {
            ["id"] = id, ["stat_type"] = "combat_stat_multiply", ["stat_sub_type"] = subtype,
            ["amount"] = amount, ["rule_type"] = rule, ["is_false_rule"] = false,
            ["rule_data"] = new JsonObject { ["float"] = 0, ["string"] = mode }
        };
        HeroClassCatalogResult Load(params JsonObject[] buffs)
        {
            Write("shared/buffs/bp.buffs.json", new JsonObject { ["buffs"] = new JsonArray(buffs.Cast<JsonNode>().ToArray()) }.ToJsonString());
            Write("shared/quirk/bp.quirk_library.json", new JsonObject { ["quirks"] = new JsonArray(new JsonObject
            {
                ["id"] = "bp_quirk", ["is_positive"] = true,
                ["buffs"] = new JsonArray(buffs.Select(buff => JsonValue.Create(buff["id"]!.GetValue<string>())).ToArray())
            }) }.ToJsonString());
            WriteFixtureManifest(root);
            return HeroClassCatalog.Load(content);
        }
        GeneratedStagecoachHeroCandidate Generate(HeroClassCatalogResult catalog) => StagecoachHeroCandidateFactory.Generate(
            catalog, catalog.HeroClasses.Single(hero => hero.Id == originalHero.Id), 1729, ["bp_quirk"]);
        void Reject(HeroClassCatalogResult catalog, string name)
        {
            var rejected = false;
            try { Generate(catalog); } catch (InvalidOperationException) { rejected = true; }
            Assert(rejected, $"{name}: unsafe or unverified native Buff input must be rejected by public generation.");
        }

        foreach (var amount in new[] { -1d, -0.99999999, double.MaxValue, -double.MaxValue, (double)float.MaxValue })
            Reject(Load(Buff("bp_value", amount)), $"native amount {amount}");
        foreach (var amount in new[] { 0.5, 0.2, 1e-50, -1e-50, -0.99999994 })
        {
            var catalog = Load(Buff("bp_value", amount));
            Assert(catalog.InitialQuirks.Single(q => q.Id == "bp_quirk").MaxHpModifiers.Single().Amount == (double)(float)amount &&
                   Generate(catalog).Preview.CurrentHp == 20 * (1 + (double)(float)amount),
                "Finite native float input, including underflow and a positive near-zero boundary, must reach preview unchanged.");
        }
        var town = JsonNode.Parse("""{"base_root":{"buildings":{"stage_coach":{"store":{"hero_recruit":{"generated":{"893":{"actor":{"current_hp":13.4}}}}}}}}}""")!.AsObject();
        var roster = JsonNode.Parse("""{"base_root":{"nextGuid":894,"heroes":{}}}""")!.AsObject();
        var upgrades = JsonNode.Parse("""{"base_root":{"purchases":{}}}""")!.AsObject();
        var roundTripIndex = 0;
        foreach (var amount in new[] { -0.99999994, 0.2, 1.234567, 499999.0, 4999999.0, 4999999999.0 })
        {
            var stable = Generate(Load(Buff("bp_value", amount)));
            var mutation = StagecoachHeroSaveEditor.AddCandidate(town, roster, upgrades, stable.Candidate, stable.UpgradePurchases);
            var prefix = Path.Combine(root, "town-" + roundTripIndex++);
            var decoded = prefix + ".decoded.json";
            var binary = prefix + ".dson";
            var restored = prefix + ".roundtrip.json";
            File.WriteAllText(decoded, mutation.UpdatedTown.ToJsonString());
            await codec.EncodeAsync(decoded, binary, null);
            await codec.DecodeAsync(binary, restored);
            var restoredTown = JsonNode.Parse(File.ReadAllText(restored))!.AsObject();
            Assert(SaveEditService.HasEquivalentStagecoachTownRoundTrip(mutation.UpdatedTown, restoredTown, mutation.Preview) &&
                   (float)restoredTown["base_root"]!["buildings"]!["stage_coach"]!["store"]!
                       ["hero_recruit"]!["generated"]!["894"]!["actor"]!["current_hp"]!.GetValue<double>() == (float)stable.Preview.CurrentHp,
                "Finite boundary/fractional HP must survive actual DSON serialization with exact float bits and full equality of every other field.");

            JsonObject Actor(JsonObject document, string key = "894") => document["base_root"]!["buildings"]!["stage_coach"]!["store"]!
                ["hero_recruit"]!["generated"]![key]!["actor"]!.AsObject();
            var before = restoredTown.ToJsonString();
            foreach (var change in new Action<JsonObject>[]
            {
                document => Actor(document)["current_hp"] = (double)MathF.BitIncrement((float)stable.Preview.CurrentHp),
                document => Actor(document).Remove("current_hp"),
                document => Actor(document)["current_hp"] = "not-a-number",
                document => Actor(document)["current_hp"] = 0,
                document => Actor(document)["current_hp"] = 24,
                document => Actor(document)["m_Stress"] = 1.0,
                document => Actor(document, "893")["current_hp"] = JsonNode.Parse("13.40000001"),
                document => document["extra_data"] = 1
            })
            {
                var damaged = (JsonObject)restoredTown.DeepClone(); change(damaged);
                Assert(!SaveEditService.HasEquivalentStagecoachTownRoundTrip(mutation.UpdatedTown, damaged, mutation.Preview),
                    "Round-trip comparison must reject changed HP bits/types, other new-hero fields, existing HP and unrelated save data.");
            }
            Assert(restoredTown.ToJsonString() == before, "Float comparison must not mutate the decoded document.");
            if (amount == 4999999999.0)
            {
                var java8 = (JsonObject)restoredTown.DeepClone(); Actor(java8)["current_hp"] = JsonNode.Parse("9.9999998E10");
                Assert(SaveEditService.HasEquivalentStagecoachTownRoundTrip(mutation.UpdatedTown, java8, mutation.Preview),
                    "Java 8 and newer Java decimal spellings of the identical generated HP float must both remain valid.");
            }
        }

        foreach (var (field, value) in new[]
        {
            ("stat_sub_type", "MAX_HP"), ("stat_sub_type", " max_hp "),
            ("stat_type", "COMBAT_STAT_MULTIPLY"), ("stat_type", " combat_stat_multiply "),
            ("rule_type", "ALWAYS"), ("rule_type", " always ")
        })
        {
            var buff = Buff("bp_raw", 0.5); buff[field] = value;
            var catalog = Load(buff);
            Assert(catalog.InitialQuirks.Single(q => q.Id == "bp_quirk").WriteStatus == HeroInitialQuirkWriteStatus.Unverified,
                $"{field}={value}: normalized spelling must not certify a native enum.");
            Reject(catalog, field);
        }
        Assert(Generate(Load(Buff("bp_non_hp", 0.5, "speed_rating"))).Preview.CurrentHp == 20,
            "Valid non-HP combat subtypes must remain selectable without HP modification.");
        var nul = Buff("bp_nul", 0.5, "max_hp\0ignored", "always\0ignored");
        Assert(Generate(Load(nul)).Preview.CurrentHp == 30, "Native Buff C strings stop at NUL without trimming meaningful bytes.");

        foreach (var (leftMode, rightMode, sameCondition) in new[]
        {
            ("Mode", "Mode", true), ("Mode", "mode", false), ("Mode", " Mode ", false),
            ("bp_Az", "bp_BE", true), (new string('x', 63) + "a", new string('x', 63) + "b", true),
            (new string('甲', 21) + "a", new string('甲', 21) + "b", true)
        })
        {
            Write(heroPath, File.ReadAllText(originalHero.SourcePath) + $"\nmode: .id \"{leftMode}\" .is_raid_default true\nmode: .id \"{rightMode}\"\n");
            var negative = Buff("bp_negative", -30, rule: "in_mode", mode: leftMode); negative["stat_type"] = "combat_stat_add";
            var positive = Buff("bp_positive", 30, rule: "in_mode", mode: rightMode); positive["stat_type"] = "combat_stat_add";
            var catalog = Load(negative, positive);
            if (sameCondition) Assert(Generate(catalog).Preview.CurrentHp == 20, "Identical native mode hashes activate together, including byte truncation.");
            else Reject(catalog, $"distinct modes {leftMode}/{rightMode}");
        }
        var invalidMode = Buff("bp_bad_mode", -30, rule: "in_mode", mode: new string('x', 62) + "甲");
        Reject(Load(invalidMode), "truncated UTF-8 mode");
        var low = Buff("bp_low", -30, rule: "lightabove"); low["stat_type"] = "combat_stat_add"; low["rule_data"]!["float"] = 0.50000001;
        var high = Buff("bp_high", 30, rule: "lightabove"); high["stat_type"] = "combat_stat_add"; high["rule_data"]!["float"] = 0.5;
        var light = Load(low, high);
        Assert(light.InitialQuirks.Single(q => q.Id == "bp_quirk").MaxHpModifiers.All(m => m.RuleFloat == 0.5) && Generate(light).Preview.CurrentHp == 20,
            "Thresholds which round to the same native float must not create a fictitious one-Buff state.");
        var overflow = Buff("bp_threshold", 0.5, rule: "lightabove"); overflow["rule_data"]!["float"] = double.MaxValue;
        Reject(Load(overflow), "nonfinite native rule threshold");

        Write("effects/bp.effects.darkest", """
            effect: .name BP_EFFECT .disease BP_SIGNAL
            effect: .name bp_effect .disease bp_signal
            effect: .name BP_COLLIDE_Az .disease BP_SIGNAL
            effect: .name BP_COLLIDE_BE .disease bp_signal
            """);
        Write(heroPath, File.ReadAllText(originalHero.SourcePath) + "\ncombat_skill: .id bp_skill .level 0 .effect BP_EFFECT bp_effect BP_COLLIDE_Az BP_COLLIDE_BE\n");
        Write("shared/quirk/bp.quirk_library.json", """{"quirks":[{"id":"BP_SIGNAL","is_positive":true,"random_chance":0},{"id":"bp_signal","is_positive":true,"random_chance":0}]}""");
        var events = new JsonArray(new[] { "BP_EVENT", "bp_event", "BP_COLLIDE_Az", "BP_COLLIDE_BE" }.Select(id => (JsonNode)new JsonObject
        {
            ["id"] = id, ["data"] = new JsonArray(new JsonObject { ["type"] = "bonus_recruit", ["string_data"] = originalHero.Id, ["number_data"] = 1 })
        }).ToArray());
        Write("campaign/town_events/bp.town_events.events.json", new JsonObject { ["events"] = events }.ToJsonString());
        WriteFixtureManifest(root);
        var identities = HeroClassCatalog.Load(content);
        var hero = identities.HeroClasses.Single(h => h.Id == originalHero.Id);
        Assert(hero.RuntimeQuirkSignals.Where(s => s.SkillId == "bp_skill").Select(s => s.QuirkId).ToHashSet(StringComparer.Ordinal)
                   .SetEquals(["BP_SIGNAL", "bp_signal"]) &&
               hero.RecruitEvents.Where(e => e.Id.StartsWith("BP_", StringComparison.Ordinal) || e.Id == "bp_event").Select(e => e.Id)
                   .ToHashSet(StringComparer.Ordinal).SetEquals(["BP_EVENT", "bp_event"]),
            "Case-distinct Effect/event IDs and downstream signals must remain separate, while real hash collisions remain unresolved.");
        Assert(identities.Issues.Count(issue => issue.Contains("BP_COLLIDE", StringComparison.Ordinal) && issue.Contains("conflicting native identities")) == 4,
            "Changing case-sensitive groups must retain each genuine Effect/event collision diagnostic.");
        Console.WriteLine("PASS: native Buff float/strings/mode hashes, HP guard and DSON write, exact Effect/event identities.");
    }
}
