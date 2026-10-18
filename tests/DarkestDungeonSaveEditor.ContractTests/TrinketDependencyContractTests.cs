using System.Globalization;
using System.Text.Json;

internal static partial class ContractSuite
{
    public static async Task RunTrinketDependenciesOnlyAsync(string repositoryRoot)
    {
        var fixture = BuildContractFixture(repositoryRoot);
        await RunTrinketDependencyContractsAsync(fixture.RunRoot, fixture.Codec);
        await RunBuffEnumContractsAsync(fixture.RunRoot, fixture.Codec);
        await RunHeroReferenceDependencyContractsAsync(fixture.RunRoot, fixture.Codec);
        await RunDefinitionReadCompletenessContractsAsync(fixture.RunRoot, fixture.Codec);
        await RunHeroSupportingReadContractsAsync(fixture.RunRoot, fixture.Codec);
        Console.WriteLine($"Artifacts: {fixture.RunRoot}");
    }

    private static JsonObject DependencyTrinket(int uses, string field, params string[] references) => new()
    {
        ["id"] = "dependency_trinket", ["rarity"] = "common", ["quest_uses"] = uses,
        ["trigger_limit"] = uses + 1, [field] = JsonSerializer.SerializeToNode(references)
    };

    private static void WriteDependencyEntries(string root, string path, params JsonObject[] entries) =>
        WriteMultiMash(root, path, new JsonObject { ["entries"] = new JsonArray(entries.Cast<JsonNode>().ToArray()) }.ToJsonString());

    private const string DependencyBuffPath = "shared/buffs/dependency.buffs.json";
    private const string DependencyFirstPath = "trinkets/a.entries.trinkets.json";
    private const string DependencyLastPath = "trinkets/z.entries.trinkets.json";
    private const string DependencyBuffJson = """{"buffs":[{"id":"Az","stat_type":"combat_stat_add","stat_sub_type":"speed_rating","amount":1,"rule_type":"always","is_false_rule":false,"remove_if_not_active":false}]}""";

    private static async Task RunTrinketDependencyContractsAsync(string runRoot, DsonSaveCodec codec)
    {
        foreach (var kind in new[] { "base", "local", "workshop" })
        {
            var root = Path.Combine(runRoot, "trinket-dependencies", kind);
            var resources = Path.Combine(root, "resources");
            WriteMultiMash(resources, "heroes/Az/Az.info.darkest", "armour: .name test .hp 20\n");
            WriteMultiMash(resources, "inventory/test.inventory.system_configs.darkest", "inventory_system_config: .type trinket_storage .max_slots 100\n");
            WriteMultiMash(resources, DependencyBuffPath, DependencyBuffJson);
            WriteDependencyEntries(resources, DependencyFirstPath);
            WriteDependencyEntries(resources, DependencyLastPath);
            if (kind != "base") WriteFixtureManifest(resources);
            var f = await BuildInventoryProviderProfileAsync(root, [new(kind, kind, kind, resources, 1000)], false, codec);
            var count = 0;
            foreach (var field in new[] { "buffs", "hero_class_requirements" })
            foreach (var test in new (string[] References, bool Valid)[]
            {
                (["Az"], true), (["BE"], true), (["Az\0tail"], true), (["BE\0tail"], true),
                (["az"], false), ([" Az "], false), (["missing"], false), ([], true),
                (["Az", "BE"], true), (["Az", "missing"], false)
            })
            foreach (var layout in new[] { "single", "same-file", "different-files" })
            {
                var first = DependencyTrinket(2, field, test.References);
                WriteDependencyEntries(resources, DependencyFirstPath, layout == "same-file"
                    ? [first, DependencyTrinket(7, field)] : [first]);
                WriteDependencyEntries(resources, DependencyLastPath, layout == "different-files" ? [DependencyTrinket(7, field)] : []);
                var catalog = TrinketCatalog.Load(f.Content);
                var item = catalog.Trinkets.SingleOrDefault();
                int? expected = test.Valid ? 2 : layout == "single" ? null : 7;
                Assert(item?.QuestUses == expected && (item is null || !item.HasProviderConflict),
                    $"{kind}/{field}/{layout}/{string.Join(',', test.References)}: select the first valid native entry, expected uses={expected}.");
                if (item is not null)
                {
                    var updated = TrinketSaveEditor.AddCopies(f.Estate, item, 2, 100).UpdatedRoot;
                    Assert(updated["base_root"]!["trinkets"]!["items"]!.AsObject().Select(pair => pair.Value).All(value =>
                        value!["id"]!.GetValue<string>() == "dependency_trinket" &&
                        value["quest_uses_remaining"]!.GetValue<int>() == expected &&
                        value["triggers_remaining"]!.GetValue<int>() == expected + 1),
                        "Every copy must use the winning entry's original ID and both counters.");
                }
                count++;
            }
            // Full service commits: missing Buff selects 7; a hash-alias hero requirement selects 2.
            foreach (var (field, reference, expected) in new[] { ("buffs", "missing", 7), ("hero_class_requirements", "BE", 2) })
            {
                WriteDependencyEntries(resources, DependencyFirstPath, DependencyTrinket(2, field, reference));
                WriteDependencyEntries(resources, DependencyLastPath, DependencyTrinket(7, "buffs"));
                var catalog = TrinketCatalog.Load(f.Content);
                await f.Service.CommitAsync(await f.Service.PrepareTrinketEditAsync(f.Content.Profile, catalog.Trinkets.Single(), 2, catalog.Storage, f.Content));
                var restored = Path.Combine(root, field + ".committed.json");
                await codec.DecodeAsync(f.Content.Profile.EstateSavePath, restored);
                var copies = JsonSupport.ReadObject(restored)["base_root"]!["trinkets"]!["items"]!.AsObject().ToArray()[^2..];
                Assert(copies.All(pair => pair.Value!["quest_uses_remaining"]!.GetValue<int>() == expected &&
                    pair.Value["triggers_remaining"]!.GetValue<int>() == expected + 1),
                    "Actual DSON commits must persist the first valid definition's counters.");
            }
            await VerifyTrinketDependencyChangesAsync(f, resources);
            Console.WriteLine($"PASS: {kind} trinket Buff/hero dependencies, {count} selection cases, DSON counters, preview/commit/recovery guards.");
        }
        foreach (var kind in new[] { "local", "workshop" })
        {
            VerifyTrinketBuffProviders(Path.Combine(runRoot, "trinket-buff-providers", kind), kind);
            VerifyTrinketHeroProviders(Path.Combine(runRoot, "trinket-hero-providers", kind), kind);
        }
        await VerifyUnmappedTrinketSourcesAsync(Path.Combine(runRoot, "unmapped-trinket-dependencies"), codec);
    }

    private static async Task VerifyTrinketDependencyChangesAsync(InventoryProviderFixture f, string resources)
    {
        WriteDependencyEntries(resources, DependencyFirstPath, DependencyTrinket(2, "buffs", "Az"));
        WriteDependencyEntries(resources, DependencyLastPath, DependencyTrinket(7, "buffs"));
        var catalog = TrinketCatalog.Load(f.Content);
        var valid = catalog.Trinkets.Single();
        var originalHash = ComputeSha256(f.Content.Profile.EstateSavePath);
        foreach (var replacement in new[] { "{\"buffs\":[]}", "invalid JSON" })
        {
            var fingerprint = ProfileCatalogContentFingerprint.Capture(f.Content.Sources);
            var prepared = await f.Service.PrepareTrinketEditAsync(f.Content.Profile, valid, 1, catalog.Storage, f.Content);
            WriteMultiMash(resources, DependencyBuffPath, replacement);
            var current = TrinketCatalog.Load(f.Content).Trinkets.Single();
            Assert(replacement.StartsWith('{') ? current.QuestUses == 7 && !current.HasProviderConflict
                : current.QuestUses == 2 && current.HasProviderConflict,
                "Confirmed absence skips the first entry; failed reads preserve its unresolved slot.");
            Assert(fingerprint != ProfileCatalogContentFingerprint.Capture(f.Content.Sources), "Buff edits must invalidate automatic catalog fingerprints.");
            await RejectProviderEditAsync(() => f.Service.PrepareTrinketEditAsync(f.Content.Profile, valid, 1, catalog.Storage, f.Content));
            if (current.HasProviderConflict)
            {
                var originalCulture = CultureInfo.CurrentUICulture;
                try
                {
                    foreach (var language in new[] { "en", "zh-CN" })
                    {
                        EditorText.Initialize(language, EditorText.SystemCulture);
                        var message = await ReadRejectedTrinketMessageAsync(() =>
                            f.Service.PrepareTrinketEditAsync(f.Content.Profile, current, 1, catalog.Storage, f.Content));
                        Assert(message.Contains(language == "en" ? "definition or dependencies" : "定义或依赖", StringComparison.Ordinal) &&
                            !message.Contains("different content paths", StringComparison.Ordinal) &&
                            !message.Contains("project.xml", StringComparison.Ordinal),
                            "An unreadable Buff must report unverified content without inventing a cross-path conflict or a missing Mod.");
                    }
                }
                finally { EditorText.Initialize(originalCulture.Name, EditorText.SystemCulture); }
            }
            await RejectProviderEditAsync(() => f.Service.CommitAsync(prepared));
            Assert(ComputeSha256(f.Content.Profile.EstateSavePath) == originalHash, "Rejected dependency changes must leave the save unchanged.");
            WriteMultiMash(resources, DependencyBuffPath, DependencyBuffJson);
        }
        // Dependency reappearance can make a formerly skipped earlier entry win.
        WriteMultiMash(resources, DependencyBuffPath, "{\"buffs\":[]}");
        var absent = TrinketCatalog.Load(f.Content);
        var fromLater = await f.Service.PrepareTrinketEditAsync(f.Content.Profile, absent.Trinkets.Single(), 1, absent.Storage, f.Content);
        WriteMultiMash(resources, DependencyBuffPath, DependencyBuffJson);
        await RejectProviderEditAsync(() => f.Service.CommitAsync(fromLater));

        // A change during replacement must trigger the existing recovery path.
        var during = await f.Service.PrepareTrinketEditAsync(f.Content.Profile, valid, 1, catalog.Storage, f.Content);
        f.Service.AfterTargetReplace = _ => WriteMultiMash(resources, DependencyBuffPath, "{\"buffs\":[]}");
        try { await RejectProviderEditAsync(() => f.Service.CommitAsync(during)); }
        finally { f.Service.AfterTargetReplace = null; WriteMultiMash(resources, DependencyBuffPath, DependencyBuffJson); }
        Assert(ComputeSha256(f.Content.Profile.EstateSavePath) == originalHash, "Dependency invalidation after replacement must recover the original save.");
    }

    private static void VerifyTrinketBuffProviders(string root, string kind)
    {
        var low = Path.Combine(root, "base");
        var high = Path.Combine(root, "mod");
        WriteMultiMash(low, DependencyBuffPath, DependencyBuffJson);
        WriteDependencyEntries(low, DependencyFirstPath, DependencyTrinket(2, "buffs", "Az"));
        WriteDependencyEntries(low, DependencyLastPath, DependencyTrinket(7, "buffs"));
        WriteMultiMash(high, DependencyBuffPath, "{\"buffs\":[]}");
        WriteFixtureManifest(high);
        var profile = new SaveProfile("unused", root, Path.Combine(root, "unused.json"), "test", DateTime.UtcNow);
        var content = new ActiveContentSnapshot(profile, "normal", [new("base", "Base", "base", low, 0), new(kind, kind, kind, high, 1000)], [], root, "", 1, "");
        TrinketDefinition Selected() => TrinketCatalog.Load(content).Trinkets.Single();
        Assert(Selected().QuestUses == 7, "An empty higher provider must hide the lower Buff.");
        File.Delete(Path.Combine(high, DependencyBuffPath));
        Assert(Selected() is { QuestUses: 2, HasProviderConflict: true }, "A missing winning manifest slot must not revive a lower Buff or certify fallback trinket counters.");
        WriteMultiMash(high, DependencyBuffPath, DependencyBuffJson);
        File.Delete(Path.Combine(low, DependencyBuffPath));
        Assert(Selected() is { QuestUses: 2, HasProviderConflict: false }, "A failed shadowed provider must not poison the readable winner.");
        WriteMultiMash(high, "shared/buffs/other.buffs.json", "not JSON");
        WriteFixtureManifest(high);
        Assert(Selected() is { QuestUses: 2, HasProviderConflict: false }, "An independent unreadable file cannot erase a Buff proven present in an effective file.");
        WriteMultiMash(high, DependencyBuffPath, "{\"buffs\":[]}");
        Assert(Selected() is { QuestUses: 2, HasProviderConflict: true }, "A missing hash cannot be certified absent in an incomplete Buff table.");
        WriteMultiMash(high, "modfiles.txt", DependencyBuffPath);
        Assert(Selected().QuestUses == 7, "Unlisted physical files must not participate in dependency resolution.");
        WriteMultiMash(high, DependencyBuffPath, """{"buffs":[{"id":"BE"}],"buffs":[]}""");
        Assert(Selected().QuestUses == 2, "Buff identities use first JSON members and native hash references.");
        WriteMultiMash(high, DependencyBuffPath, """{"buffs":null,"buffs":[{"id":"Az"}]}""");
        Assert(Selected().QuestUses == 7, "A wrong-typed first Buff array must not fall through to later JSON members.");
        WriteMultiMash(high, DependencyBuffPath, DependencyBuffJson);
        var unavailableSource = content with { Sources = content.Sources.Append(
            new ActiveContentSource("mode:missing", "Missing mode source", "mode", Path.Combine(root, "not-installed"), 50)).ToArray() };
        Assert(TrinketCatalog.Load(unavailableSource).Trinkets.Single() is { QuestUses: 2, HasProviderConflict: true },
            "Incomplete provider discovery cannot certify a known lower Buff whose override might be unavailable.");
        Console.WriteLine($"PASS: {kind} Buff overlay, missing winner, unknown reads, manifest admission and first JSON members.");
    }

    private static async Task VerifyUnmappedTrinketSourcesAsync(string runRoot, DsonSaveCodec codec)
    {
        foreach (var field in new[] { "buffs", "hero_class_requirements" })
        foreach (var scenario in new[] { "unmapped-title", "ambiguous-title", "missing-workshop", "unavailable-workshop", "multiple-missing", "resolved" })
        {
            var root = Path.Combine(runRoot, field, scenario);
            var game = Path.Combine(root, "game");
            WriteMultiMash(game, "inventory/a.inventory.system_configs.darkest", "inventory_system_config: .type trinket_storage .max_slots 100\n");
            WriteDependencyEntries(game, DependencyFirstPath, DependencyTrinket(2, field, "Az"));
            WriteDependencyEntries(game, DependencyLastPath, DependencyTrinket(7, "buffs"));
            if (scenario is "ambiguous-title" or "resolved")
                foreach (var folder in scenario == "ambiguous-title" ? new[] { "a", "b" } : new[] { "a" })
                {
                    var mod = Path.Combine(game, "mods", folder);
                    WriteMultiMash(mod, "project.xml", "<project><Title>Buff Provider</Title></project>");
                    WriteMultiMash(mod, DependencyBuffPath, DependencyBuffJson);
                    WriteMultiMash(mod, "heroes/Az/Az.info.darkest", "armour: .name test .hp 20\n");
                    WriteFixtureManifest(mod);
                }
            var f = await BuildInventoryProviderProfileAsync(root, [new("base", "Base", "base", game, 0)], false, codec);
            var workshopSource = scenario is "missing-workshop" or "unavailable-workshop";
            var workshop = scenario == "missing-workshop" ? Path.Combine(root, "workshop") : null;
            if (workshop is not null) Directory.CreateDirectory(workshop);
            var gameDoc = new JsonObject { ["base_root"] = new JsonObject
            {
                ["inraid"] = false, ["game_mode"] = "base", ["applied_ugcs_1_0"] = new JsonObject
                {
                    ["0"] = new JsonObject { ["name"] = workshopSource ? "123" : "Buff Provider",
                        ["source"] = workshopSource ? "Steam" : "mod_local_source" }
                }
            } };
            if (scenario == "multiple-missing")
            {
                gameDoc["base_root"]!["applied_ugcs_1_0"]!["1"] = new JsonObject
                    { ["name"] = "Second Missing Mod", ["source"] = "mod_local_source" };
                gameDoc["base_root"]!["applied_ugcs_1_0"]!["2"] = new JsonObject
                    { ["name"] = "456", ["source"] = "Steam" };
            }
            WriteMultiMash(f.Content.Profile.ProfileDirectory, "persist.game.json", gameDoc.ToJsonString());
            var content = await ActiveContentResolver.ResolveAsync(f.Content.Profile, game, workshop, codec, Path.Combine(root, "resolve"));
            var catalog = TrinketCatalog.Load(content);
            var item = catalog.Trinkets.Single();
            var resolved = scenario == "resolved";
            Assert(content.AppliedModCount == (scenario == "multiple-missing" ? 3 : 1) && item.QuestUses == 2 && item.HasProviderConflict != resolved,
                $"{field}/{scenario}: omitted enabled providers must leave the earlier dependent entry unresolved.");
            if (resolved)
            {
                var preparedBeforeRemoval = await f.Service.PrepareTrinketEditAsync(content.Profile, item, 1, catalog.Storage, content);
                var project = Path.Combine(game, "mods", "a", "project.xml");
                var projectBytes = File.ReadAllBytes(project);
                var beforeRemoval = ComputeSha256(content.Profile.EstateSavePath);
                // Keep the directory but remove its saved-title binding, as happens when
                // project metadata is changed outside the game after a successful load.
                File.WriteAllText(project, "<project><Title>Different Provider</Title></project>");
                try
                {
                    await AssertNamedModFailureAsync(() => f.Service.PrepareTrinketEditAsync(content.Profile, item, 1, catalog.Storage, content),
                        ["Buff Provider"], "unmapped-title");
                    await AssertNamedModFailureAsync(() => f.Service.CommitAsync(preparedBeforeRemoval), ["Buff Provider"], "unmapped-title");
                }
                finally { File.WriteAllBytes(project, projectBytes); }
                Assert(ComputeSha256(content.Profile.EstateSavePath) == beforeRemoval,
                    "A Mod disappearing after load must be named at preview and commit without changing the save.");
                var restored = await ActiveContentResolver.ResolveAsync(f.Content.Profile, game, workshop, codec, Path.Combine(root, "restored"));
                var restoredCatalog = TrinketCatalog.Load(restored);
                Assert(!restoredCatalog.Trinkets.Single().HasProviderConflict, "Restoring the source and reloading must remove the read-only restriction.");
                await f.Service.CommitAsync(await f.Service.PrepareTrinketEditAsync(restored.Profile, restoredCatalog.Trinkets.Single(), 1, restoredCatalog.Storage, restored));
                continue;
            }
            var before = ComputeSha256(content.Profile.EstateSavePath);
            var expectedNames = scenario == "multiple-missing" ? new[] { "Buff Provider", "Second Missing Mod", "456" }
                : new[] { workshopSource ? "123" : "Buff Provider" };
            await AssertNamedModFailureAsync(() => f.Service.PrepareTrinketEditAsync(content.Profile, item, 1, catalog.Storage, content),
                expectedNames, scenario, content.Issues);
            // Exercise the commit boundary independently: a hypothetical source-only
            // preview is rebound to the real incomplete configuration. Commit must
            // retain that context when rebuilding its validation catalog.
            var hypothetical = content with { Sources = content.Sources };
            var fallback = TrinketCatalog.Load(hypothetical);
            var prepared = await f.Service.PrepareTrinketEditAsync(content.Profile, fallback.Trinkets.Single(), 1, fallback.Storage, hypothetical);
            prepared = prepared with { ContentGuard = prepared.ContentGuard with { Resolution = content.Resolution } };
            await AssertNamedModFailureAsync(() => f.Service.CommitAsync(prepared), expectedNames, scenario);
            Assert(ComputeSha256(content.Profile.EstateSavePath) == before, "Incomplete-source preflight and commit rejection must not modify the estate.");
        }
        Console.WriteLine("PASS: named bilingual Mod-source failures in logs/preview/commit, missing/ambiguous/multiple sources, removal after preview, restoration and unchanged-save guards.");
    }

    private static async Task<string> ReadRejectedTrinketMessageAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception error) when (error is InvalidOperationException or IOException) { return error.Message; }
        throw new InvalidOperationException("An unverified trinket was accepted.");
    }

    private static async Task AssertNamedModFailureAsync(Func<Task> action, string[] names, string scenario,
        IReadOnlyList<string>? sourceIssues = null)
    {
        var originalCulture = CultureInfo.CurrentUICulture;
        try
        {
            foreach (var language in new[] { "en", "zh-CN" })
            {
                EditorText.Initialize(language, EditorText.SystemCulture);
                var message = await ReadRejectedTrinketMessageAsync(action);
                Assert(names.All(name => message.Contains(name, StringComparison.Ordinal)) &&
                    !message.Contains("different content paths", StringComparison.Ordinal) &&
                    !message.Contains("conflict", StringComparison.OrdinalIgnoreCase) && !message.Contains("冲突", StringComparison.Ordinal),
                    "Source failures must name every unresolved enabled Mod without describing a trinket conflict.");
                var expectedReason = (scenario, language) switch
                {
                    ("ambiguous-title", "en") => "multiple directories",
                    ("ambiguous-title", _) => "多个目录",
                    ("missing-workshop", "en") => "Workshop",
                    ("missing-workshop", _) => "工坊",
                    ("unavailable-workshop", "en") => "Workshop directory",
                    ("unavailable-workshop", _) => "创意工坊目录",
                    (_, "en") => "project.xml",
                    _ => "启用记录"
                };
                Assert(message.Contains(expectedReason, StringComparison.Ordinal), $"{scenario}/{language}: report the actual source problem.");
                if (scenario == "ambiguous-title")
                    Assert(!message.Contains("could not be found", StringComparison.Ordinal) && !message.Contains("找不到", StringComparison.Ordinal),
                        "Ambiguous titles must not be reported as missing files.");
                if (sourceIssues is not null)
                {
                    var logs = CatalogLogDiagnostics.Summarize([("sources", sourceIssues), ("second", sourceIssues)]);
                    Assert(logs.Count == sourceIssues.Count && logs.All(entry => entry.Level == DiagnosticLogLevel.Warning) &&
                        names.All(name => logs.Any(entry => entry.Message.Contains(name, StringComparison.Ordinal))) &&
                        logs.Any(entry => entry.Message.Contains(expectedReason, StringComparison.Ordinal)),
                        "Localized source diagnostics must preserve warning severity and cross-module grouping while naming the missing sources.");
                    var noisyLogs = CatalogLogDiagnostics.Summarize([
                        ("sources", sourceIssues),
                        ("trinkets", Enumerable.Range(0, 40).Select(index =>
                            $"Trinket 'item_{index}' Buff dependencies could not be verified and are read-only: file.json").ToArray())
                    ]);
                    Assert(names.All(name => noisyLogs.Take(30).Any(entry => entry.Message.Contains(name, StringComparison.Ordinal))),
                        "Missing-source names must remain in the UI's first 30 diagnostics ahead of cascading dependency warnings in both languages.");
                }
            }
        }
        finally { EditorText.Initialize(originalCulture.Name, EditorText.SystemCulture); }
    }

    private static void VerifyTrinketHeroProviders(string root, string kind)
    {
        var mod = Path.Combine(root, "mod");
        WriteDependencyEntries(mod, DependencyFirstPath, DependencyTrinket(2, "hero_class_requirements", "BE"));
        WriteDependencyEntries(mod, DependencyLastPath, DependencyTrinket(7, "buffs"));
        WriteFixtureManifest(mod);
        var content = QueryContent(root, [new(kind, kind, kind, mod, 1000)]);
        Assert(TrinketCatalog.Load(content).Trinkets.Single() is { QuestUses: 7, HasProviderConflict: false },
            "Complete actor discovery can prove a required class absent and skip that entry.");
        var incomplete = content with { Sources = content.Sources.Append(new ActiveContentSource(
            "mode:missing", "Missing", "mode", Path.Combine(root, "unavailable"), 50)).ToArray() };
        Assert(TrinketCatalog.Load(incomplete).Trinkets.Single() is { QuestUses: 2, HasProviderConflict: true },
            "An unavailable discovery source leaves an absent required class unknown, preserving the first trinket.");
        // Native actor registration depends on discovery, not successfully parsing
        // a canonical generation template. Even a listed missing info registers Az.
        File.AppendAllText(Path.Combine(mod, "modfiles.txt"), "heroes/Az/Az.info.darkest\n");
        Assert(TrinketCatalog.Load(incomplete).Trinkets.Single() is { QuestUses: 2, HasProviderConflict: false },
            "A proven registered hero remains present despite missing info or other unresolved discovery sources.");
        Console.WriteLine($"PASS: {kind} trinket hero dependency completeness and actor-registration controls.");
    }
}
