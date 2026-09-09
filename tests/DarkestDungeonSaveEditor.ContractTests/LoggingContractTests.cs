internal static partial class ContractSuite
{
    private static void RunLoggingUiContracts(string repositoryRoot)
    {
        var appRoot = Path.Combine(repositoryRoot, "src", "DarkestDungeonSaveEditor.App");
        var app = File.ReadAllText(Path.Combine(appRoot, "App.xaml.cs"));
        var loading = File.ReadAllText(Path.Combine(appRoot, "MainWindow.CatalogLoading.cs"));
        var workflow = File.ReadAllText(Path.Combine(appRoot, "MainWindow.EditWorkflow.cs"));
        var maps = File.ReadAllText(Path.Combine(appRoot, "MainWindow.BattleMapIntegration.cs"));
        var state = File.ReadAllText(Path.Combine(appRoot, "MainWindow.State.cs"));
        var discovery = File.ReadAllText(Path.Combine(appRoot, "MainWindow.Discovery.cs"));
        var interaction = File.ReadAllText(Path.Combine(appRoot, "MainWindow.CatalogInteraction.cs"));
        var inventory = File.ReadAllText(Path.Combine(appRoot, "MainWindow.CatalogDiagnostics.cs"));
        var battleLoading = File.ReadAllText(Path.Combine(appRoot, "BattleMapView.ProfileLifecycle.cs"));
        Assert(app.Contains("RuntimeLogIdentity.Describe(typeof(App).Assembly)", StringComparison.Ordinal) &&
               app.Contains("Level: {level}", StringComparison.Ordinal) &&
               loading.Contains("diagnosticBatch.Summarize()", StringComparison.Ordinal) &&
               loading.Contains("CatalogLogDiagnostics.FormatSourceCounts(activeContent)", StringComparison.Ordinal) &&
               loading.Contains("CrashDiagnostics.RecordCatalogDiagnostics(diagnosticBatch)", StringComparison.Ordinal) &&
               maps.Contains("_battleMapLogTracker.Observe(snapshot)", StringComparison.Ordinal) &&
               maps.Contains("BattleMap: snapshot diagnostics", StringComparison.Ordinal) &&
               state.Contains("_battleMapLogTracker.Reset()", StringComparison.Ordinal),
            "App logging must wire build identity, diagnostic levels, grouped catalog counts and guarded per-window map-delta tracking.");
        var success = workflow.IndexOf("AppendStatusSafely($\"应用成功：", StringComparison.Ordinal);
        var updateUi = workflow.IndexOf("if (isItemEdit && estateCommit is not null)", StringComparison.Ordinal);
        Assert(success > 0 && success < updateUi &&
               workflow.Contains("SaveEditLogFormatter.Describe(_preparedQuantityItemEdit!)", StringComparison.Ordinal) &&
               workflow.Contains("SaveEditLogFormatter.Describe(_preparedTrinketEdit!)", StringComparison.Ordinal) &&
               workflow.Contains("SaveEditLogFormatter.Describe(_preparedHeroEdit!, _preparedHeroCandidatePreview)", StringComparison.Ordinal) &&
               workflow.Contains("committed ? \"存档已写入，但后续界面更新失败\" : \"应用失败\"", StringComparison.Ordinal),
            "Successful commit details must be captured before fallible UI refresh, and post-commit UI failures must not be mislabeled as failed writes.");
        Assert(maps.Contains("CrashDiagnostics.RecordStatus(message, level)", StringComparison.Ordinal) &&
               maps.Contains("AppendStatus(message, level: level)", StringComparison.Ordinal) &&
               workflow.Contains("CrashDiagnostics.RecordException(\"Save edit: preview\", ex,", StringComparison.Ordinal) &&
               workflow.Contains("\"Save edit: preview failure status\", DiagnosticLogLevel.Error", StringComparison.Ordinal) &&
               workflow.Contains("\"Save edit: failure diagnostics\", DiagnosticLogLevel.Error", StringComparison.Ordinal) &&
               loading.Contains("\"LoadCatalog failure status\", DiagnosticLogLevel.Error", StringComparison.Ordinal) &&
               loading.Contains("\"LoadCatalog cleanup status\", DiagnosticLogLevel.Error", StringComparison.Ordinal) &&
               discovery.Contains("CrashDiagnostics.RecordException(\"Discover: handled exception\", ex)", StringComparison.Ordinal) &&
               discovery.Contains("\"Discover: failure status\", DiagnosticLogLevel.Error", StringComparison.Ordinal) &&
               interaction.Contains("CrashDiagnostics.RecordException(\"Initial quirks: selection dialog\", ex)", StringComparison.Ordinal) &&
               interaction.Contains("\"Initial quirks: failure status\", DiagnosticLogLevel.Error", StringComparison.Ordinal),
            "Status helpers must preserve explicit severity, and handled discovery, preview, dialog, load and commit failures must retain error summaries and exceptions.");
        foreach (var prefix in new[] { "物品预览警告：", "饰品预览提示：", "人物预览警告：", "人物预览提示：" })
        {
            Assert(workflow.Split('\n').Any(line => line.Contains(prefix, StringComparison.Ordinal) &&
                       line.Contains("level: DiagnosticLogLevel.Warning", StringComparison.Ordinal)),
                $"Preview advisory '{prefix}' must remain visible when filtering diagnostic warnings.");
        }
        Assert(inventory.Contains("ContentFileInventory.FormatLogDetails(inventory)", StringComparison.Ordinal) &&
               inventory.Contains("CrashDiagnostics.RecordStatus(entry.Message, entry.Level)", StringComparison.Ordinal),
            "Inventory must persist each structured entry's severity instead of inferring it from translated message text.");
        Assert(app.Contains("Lazy<SessionLogFile>", StringComparison.Ordinal) &&
               app.Contains("SessionLog.Value.Append(builder.ToString())", StringComparison.Ordinal) &&
               !app.Contains("app-{DateTime.Now:yyyyMMdd}.log", StringComparison.Ordinal) &&
               app.Contains("本次日志={CrashDiagnostics.LogFilePath}", StringComparison.Ordinal),
            "The application must create one lazy session writer, reuse it for all entries and identify the current file at startup rather than choosing a daily file per write.");
        Assert(loading.Contains("diagnosticBatch: diagnosticBatch", StringComparison.Ordinal) &&
               loading.Contains("Trinkets = diagnosticBatch.Capture(\"饰品\"", StringComparison.Ordinal) &&
               loading.Contains("Heroes = diagnosticBatch.Capture(\"人物/怪癖/姓名\"", StringComparison.Ordinal) &&
               loading.Contains("quantityItemCatalogTask = diagnosticBatch.CaptureAsync(\"物品\"", StringComparison.Ordinal) &&
               loading.Replace("\r\n", "\n", StringComparison.Ordinal).Contains("finally\n        {\n            // Flush partial results", StringComparison.Ordinal) &&
               battleLoading.Contains("diagnosticBatch.Add(\"战斗遭遇\", _encounterCatalog.Issues)", StringComparison.Ordinal) &&
               battleLoading.Contains("diagnosticBatch.Add(\"地图内容\", _roomAttachmentCatalog.Issues)", StringComparison.Ordinal) &&
               battleLoading.Contains("if (ownsDiagnosticBatch)", StringComparison.Ordinal) &&
               app.Contains("foreach (var entry in batch.Drain())", StringComparison.Ordinal) &&
               app.Contains("RecordException(\"Catalog diagnostics: flush\", ex)", StringComparison.Ordinal),
            "Main and battle loads must share one diagnostic batch, flush partial results on exit, and give standalone battle loads their own guarded flush.");
        Assert(battleLoading.Contains("无需 Bridge 的直接索引", StringComparison.Ordinal) &&
               battleLoading.Contains("直接索引为 0 不代表没有 Bridge 候选", StringComparison.Ordinal) &&
               !battleLoading.Contains("当前可写 hall", StringComparison.Ordinal),
            "Encounter counts must explicitly distinguish direct indexes from Bridge candidates, not claim all writing is unavailable when direct indexes are zero.");
    }

    private static void RunLoggingContracts(ActiveContentSnapshot content, string runRoot)
    {
        const string path = @"E:\fixture\localization\bad.string_table.xml";
        const string reason = "Invalid XML at line 5.";
        var raw = new[] { $"Failed to read localization '{path}': {reason}",
            "Enabled content directory appears more than once and was scanned once: E:\\fixture" };
        var rawBefore = raw.ToArray();
        var entries = CatalogLogDiagnostics.Summarize([
            ("物品", raw),
            ("人物/怪癖/姓名", new[] { $"Failed to read hero names '{path.ToLowerInvariant()}': {reason}",
                raw[1] }),
            ("饰品", new[] { raw[0], $"Failed to read localization '{path}': Different failure.", "An unknown warning must survive." })
        ]);
        Assert(entries.Count == 4 && entries.Count(entry => entry.Level == DiagnosticLogLevel.Information) == 1 &&
               entries.Count(entry => entry.Level == DiagnosticLogLevel.Warning) == 3 && raw.SequenceEqual(rawBefore),
            "Catalog logging must merge equivalent directory notices and XML failures without changing raw issues or swallowing distinct/unknown failures.");
        var failure = entries.Single(entry => entry.Message.Contains(reason, StringComparison.Ordinal));
        Assert(failure.Message.Contains("人物/怪癖/姓名", StringComparison.Ordinal) &&
               failure.Message.Contains("物品", StringComparison.Ordinal) && failure.Message.Contains("饰品", StringComparison.Ordinal) &&
               failure.Message.Contains("不代表所有名称缺失", StringComparison.Ordinal) &&
               failure.Message.Contains(path, StringComparison.Ordinal),
            "Merged localization failures must retain the original file, reason and every reporting module without claiming all translations are missing.");
        var counts = CatalogLogDiagnostics.FormatSourceCounts(content with
        {
            AppliedModCount = 4,
            Sources = [new ActiveContentSource("base", "base", "base", runRoot, 0), new ActiveContentSource("local:one", "one", "local", runRoot, 1)],
            Issues = ["Enabled Workshop item is not installed: 123", "Enabled local Mod title is ambiguous and was not scanned: two", "Enabled content directory appears more than once and was scanned once: E:\\fixture"]
        });
        Assert(counts.Contains("存档启用记录 4 条（已解析 4，格式无效 0）；已定位 Mod 来源 1 个；无法定位/识别记录 2 条；重复来源合并 1 条", StringComparison.Ordinal) &&
               counts.Contains("活动来源共 2 个（含本体、模式和 DLC）", StringComparison.Ordinal),
            "Enabled records, resolved Mod sources, unresolved entries and deduplicated directories must use separate counting units.");
        var malformedIssues = new List<string>();
        var appliedRecords = JsonNode.Parse("""{"0":{"name":"one","source":"mod_local_source"},"1":{"name":"incomplete"},"invalid":{}}""")!.AsObject();
        var parsedRecords = (System.Collections.IEnumerable)typeof(ActiveContentResolver)
            .GetMethod("ReadAppliedEntries", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, [appliedRecords, malformedIssues])!;
        var malformedCounts = CatalogLogDiagnostics.FormatSourceCounts(content with
        {
            AppliedModCount = parsedRecords.Cast<object>().Count(),
            Issues = malformedIssues,
            Sources = [new ActiveContentSource("local:one", "one", "local", runRoot, 0)]
        });
        Assert(malformedCounts.Contains("存档启用记录 3 条（已解析 1，格式无效 2）", StringComparison.Ordinal) &&
               malformedCounts.Contains("无法定位/识别记录 0 条", StringComparison.Ordinal),
            "Malformed and incomplete stored records must be counted explicitly without relabeling them as missing installed Mods or changing the resolver.");
        var missing = CatalogLogDiagnostics.Summarize([("来源", new[] { "Enabled Workshop item is not installed: 123" })]).Single();
        Assert(missing.Level == DiagnosticLogLevel.Warning && missing.Message.Contains("其余来源继续加载", StringComparison.Ordinal),
            "An uninstalled enabled source must be a scoped warning, not a whole-load failure.");
        var identity = RuntimeLogIdentity.Describe(typeof(ContractSuite).Assembly);
        Assert(identity.Contains(typeof(ContractSuite).Assembly.ManifestModule.ModuleVersionId.ToString("D"), StringComparison.Ordinal) &&
               identity.Contains(typeof(RuntimeLogIdentity).Assembly.ManifestModule.ModuleVersionId.ToString("D"), StringComparison.Ordinal) &&
               identity.Contains(typeof(ContractSuite).Assembly.Location, StringComparison.Ordinal) && identity.Contains("版本=", StringComparison.Ordinal),
            "Startup identity must distinguish the executable assembly and Core builds even when their public version is unchanged.");

        VerifyLocalizationLogEvidence(content, runRoot);
        VerifyAppliedEditLogContracts(content.Profile);
        VerifySessionLogContracts(runRoot);
        VerifyCatalogDiagnosticBatchContracts();
    }

    private static void VerifyLocalizationLogEvidence(ActiveContentSnapshot content, string runRoot)
    {
        const string key = "hero_class_name_log_probe";
        var root = Path.Combine(runRoot, "logging-localization");
        var localization = Path.Combine(root, "localization");
        Directory.CreateDirectory(localization);
        var xml = Path.Combine(localization, "bad.string_table.xml");
        var binary = Path.Combine(localization, "valid_english.loc2");
        File.WriteAllText(xml, "<root>", new UTF8Encoding(false));
        WriteLoc2(binary, new Dictionary<string, string> { [key] = "Compiled name" });
        WriteFixtureManifest(root);
        var isolated = content with { Sources = [new ActiveContentSource("local:logging", "logging", "local", root, 0)] };
        var originalHash = ComputeSha256(binary);
        var read = ReadLocalizationProbe(isolated, [key]);
        var issue = read.Issues.Single(value => value.StartsWith("Failed to read localization", StringComparison.Ordinal));
        Assert(read.Names[key].English == "Compiled name" && issue.Contains("LOC=0，LOC2=1", StringComparison.Ordinal) &&
               !issue.Contains(binary, StringComparison.Ordinal) &&
               issue.Contains("已从同一来源的其他编译表取得有效条目", StringComparison.Ordinal) &&
               issue.Contains("不保证覆盖失败文件的所有名称", StringComparison.Ordinal) && ComputeSha256(binary) == originalHash,
            "Localization logs may cite actually read same-source compiled entries, without claiming complete replacement or mutating the table.");
        var grouped = CatalogLogDiagnostics.Summarize([("名称", new[] { issue }),
            ("姓名", new[] { $"Failed to read hero names '{xml}': " + issue.Split("': ", 2)[1].Split('\n')[0] })]);
        Assert(grouped.Count == 1 && grouped[0].Message.Contains("LOC=0，LOC2=1", StringComparison.Ordinal),
            "Adding compiler evidence must not prevent file-and-reason deduplication against the XML-only hero-name reader.");
        var differentScopes = CatalogLogDiagnostics.Summarize([
            ("人物", new[] { issue }),
            ("物品", new[] { issue.Split('\n')[0] + "\n本地化读取补充：本次所请求的名称中，未取得有效编译条目" })
        ]);
        Assert(differentScopes.Count == 1 && differentScopes[0].Message.Contains("读取范围[人物]", StringComparison.Ordinal) &&
               differentScopes[0].Message.Contains("读取范围[物品]", StringComparison.Ordinal),
            "Different requested-key scopes must label their compiled-evidence results instead of presenting contradictory unscoped availability claims.");

        File.WriteAllText(Path.Combine(root, "modfiles.txt"), "localization/bad.string_table.xml 7\n", new UTF8Encoding(false));
        var unlisted = ReadLocalizationProbe(isolated, [key]);
        Assert(unlisted.Names[key] == BilingualContentName.Empty &&
               unlisted.Issues.Any(value => value.Contains("未从同一来源的其他编译表取得有效条目", StringComparison.Ordinal)) &&
               unlisted.Issues.All(value => !value.Contains("已从同一来源的其他编译表取得有效条目", StringComparison.Ordinal)),
            "An installed but unlisted binary must not be reported as available translation evidence or be loaded for logging.");
    }

    private static void VerifyAppliedEditLogContracts(SaveProfile profile)
    {
        var item = new QuantityItemDefinition("supply", "torch", QuantityItemStorageKind.RaidInventory, 8, true, 2, "base", "fixture", false, ["base"]);
        var quantity = new PreparedQuantityItemEdit("quantity-op", profile, item,
            new QuantityItemMutationPreview("torch", QuantityItemStorageKind.RaidInventory, 2, 10, 1, false) { ResultingMatchingEntries = 2 },
            null!, "", "", "", "", "", "before", "after", false, DateTime.UtcNow);
        var trinket = new TrinketDefinition("trinket_probe", "common", null, null, "base", "fixture", false, [], false, ["base"]);
        var trinketEdit = new PreparedTrinketEdit("trinket-op", profile, trinket,
            new TrinketMutationPreview("trinket_probe", 2, 3, 5, 1, 3, null, 100),
            null!, null!, null!, "", "", "", "", "", "before", "after", false, DateTime.UtcNow);
        var heroEdit = new PreparedStagecoachHeroEdit("hero-op", profile,
            new StagecoachHeroMutationPreview(9, "hero_probe", 100, 4, 4, 10, 2, 3, 9, 10, 20) { TargetPool = StagecoachRecruitPool.Shard },
            null!, "", null!, null!, null!, DateTime.UtcNow);
        var quantityLog = SaveEditLogFormatter.Describe(quantity);
        var trinketLog = SaveEditLogFormatter.Describe(trinketEdit);
        var heroLog = SaveEditLogFormatter.Describe(heroEdit, null);
        Assert(quantityLog.Contains("supply/torch", StringComparison.Ordinal) && quantityLog.Contains("副本背包", StringComparison.Ordinal) &&
               quantityLog.Contains("数量=2 → 10", StringComparison.Ordinal) && quantityLog.Contains("占用条目=1 → 2", StringComparison.Ordinal) &&
               trinketLog.Contains("仓库数量=3 → 5", StringComparison.Ordinal) && heroLog.Contains("目标=碎片马车", StringComparison.Ordinal) &&
               heroLog.Contains("GUID=9", StringComparison.Ordinal) && heroLog.Contains("候选人数=2 → 3", StringComparison.Ordinal) &&
               new[] { quantityLog, trinketLog, heroLog }.All(value => value.Contains(profile.ProfileDirectory, StringComparison.Ordinal) &&
                   value.Contains(profile.ProfileId, StringComparison.Ordinal) && value.Contains("操作编号=", StringComparison.Ordinal)),
            "Applied edit logs must independently identify profile, operation, item/hero target, and the confirmed before/after quantities or recruit pool.");
    }

    private static void VerifyBattleMapLogContracts(BattleMapSnapshot snapshot)
    {
        var tracker = new BattleMapLogTracker();
        var first = tracker.Observe(snapshot with { Issues = ["probe warning"] });
        Assert(first.Any(entry => entry.Message.StartsWith("战斗地图初始快照", StringComparison.Ordinal)) &&
               first.Single(entry => entry.Level == DiagnosticLogLevel.Trace).Message.Contains(snapshot.MapSha256, StringComparison.Ordinal) &&
               first.Count(entry => entry.Level == DiagnosticLogLevel.Warning) == 1,
            "The first map log must describe context and retain both source hashes and initial warnings.");
        Assert(tracker.Observe(snapshot with { Issues = ["probe warning"], ReadAtUtc = DateTime.UtcNow }).Count == 0,
            "An identical source pair must not emit another full snapshot or repeat the same warning.");
        var sameMap = tracker.Observe(snapshot with { RaidSha256 = "changed-raid", Issues = ["probe warning"] });
        Assert(sameMap.Count == 2 && sameMap[0].Message.Contains("其他存档字段可能变化", StringComparison.Ordinal) &&
               sameMap[1].Message.Contains("changed-raid", StringComparison.Ordinal),
            "A new raid hash with unchanged displayed map state must remain traceable without claiming the save itself is unchanged.");
        var moved = snapshot with { RaidSha256 = "moved", PartyTileIndex = (snapshot.PartyTileIndex ?? 0) + 1, InBattle = !snapshot.InBattle, Issues = [] };
        var movement = tracker.Observe(moved);
        Assert(movement[0].Message.Contains("队伍位置", StringComparison.Ordinal) && movement[0].Message.Contains("战斗状态", StringComparison.Ordinal) &&
               movement[0].Message.Contains("警告已消失", StringComparison.Ordinal),
            "Map delta logs must distinguish movement, battle-state changes, and cleared warnings.");
        var mapChange = tracker.Observe(moved with
        {
            MapSha256 = "new-map",
            Areas = moved.Areas.Reverse().Select(area =>
                area with { RawKnowledge = area.RawKnowledge + 1 }).ToArray()
        });
        Assert(mapChange[0].Message.Contains("地图结构/格子状态变化", StringComparison.Ordinal),
            "Changed map knowledge must be reported even when the party has not moved.");
        Assert(tracker.Observe(moved with { ProfileDirectory = snapshot.ProfileDirectory + "_other" })[0].Message.StartsWith("战斗地图初始快照", StringComparison.Ordinal),
            "Switching profiles must start a fresh diagnostic context.");
        tracker.Reset();
        Assert(tracker.Observe(snapshot)[0].Message.StartsWith("战斗地图初始快照", StringComparison.Ordinal),
            "Explicit catalog invalidation must reset only the diagnostic baseline.");
    }
}
