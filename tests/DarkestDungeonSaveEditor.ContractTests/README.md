# Contract test layout

`Program.cs` only handles the command-line boundary and reports an unhandled failure. `ContractSuite.cs` owns the intentionally serial execution order.

The suite is split by responsibility:

- `ContractFixture.cs`: isolated game, Mod, profile, localization, and DSON fixtures;
- `UiContractTests.cs`: XAML, theme, asset, and UI source contracts;
- `UiLayoutContractTests.cs`: fixed load footer, selected-row details for all three catalogs, responsive tab headers, minimal quirk layout, and themed checkbox contracts;
- `ContentDiscoveryContractTests.cs`: manifest-listed enabled-DLC root discovery, missing-manifest rejection, overlay dependencies, XML/loc2 names, capacities, and exclusion of disabled/backup content;
- `ModManifestPreparationContractTests.cs`: enabled-source selection, missing-manifest preparation, unchanged sources, cancellation, concurrent changes, partial installation records, and optional isolated official-tool execution;
- `ContentInventoryContractTests.cs`: diagnostic-only full active-Mod inventory, manifest differences, unlisted XML diagnostics, scope classification, read/cancellation/link failures, log routing, fresh reloads and unchanged catalog/source data;
- `LocalizationPolicyContractTests.cs`: strict manifest gating for XML/LOC/LOC2, malformed XML rejection, per-language format/provider precedence, manifest-listed enabled-DLC roots, and source-file preservation;
- `LegacyLocalizationContractTests.cs`: legacy LOC layout, multi-value hashes, colour controls, whole-file rejection for structural corruption, per-value rejection for invalid UTF-8/colour controls, and binary fixture construction;
- `LocalizationEntryIsolationContractTests.cs`: shared LOC/LOC2 bounded-value isolation, late structural errors rejecting all names, bounded diagnostic summaries, strict XML parsing with individual invalid-entry isolation, XML-only random-name consistency, same-language fallback, and source-file preservation;
- `HeroAvailabilityContractTests.cs`: per-level catalog preflight agreement with generation, missing skill/camping dependencies, and partially available levels;
- `BridgeClassificationContractTests.cs`: original classification preservation, authored zero-weight special rows, legacy metadata recovery, and read-only unknown-origin/corrupt-manifest handling;
- `BattleMapContractTests.cs`: map reading/monitoring, movement, hard deletion, battle placement, room attachments, global encounter catalogs, managed Bridge append/reuse, and force-town transactions;
- `RegionalMapContentContractTests.cs`: current-region trap/obstacle selection, full per-type weights and duplicate contributions, zero/invalid weights, rejected resources, canonical file overlays, native float boundaries, and refreshed catalog guard rebinding;
- `MapPropNativeContractTests.cs`: canonical pool paths and direct-open discovery exclusions, manifest/DLC/priority boundaries, multiline and same-line records, prefix dispatch, comments, last fields, raw list limits, exact IDs/names/hashes, physical source lines, stale-content checks, and missing winning files;
- `QuantityItemCatalogContractTests.cs`: town/raid item discovery and direct mutations;
- `QuantityItemSaveContractTests.cs`: guarded quantity preview and commit transactions;
- `TrinketCatalogContractTests.cs`: trinket overlays, localization, limits, and state definitions;
- `TrinketJsonMemberContractTests.cs`: first JSON members, invalid first values, provider attribution and actual trinket-counter DSON roundtrips;
- `TrinketSaveContractTests.cs`: pristine trinket construction and guarded commits;
- `HeroContractTests.cs`: orchestration of the following hero-related modules;
- `HeroCatalogContractTests.cs`: effective hero definitions, overrides, localization, and progression catalogs;
- `HeroUpgradeTreeContractTests.cs`: native last-match tree resolution, partial/mixed-class/nested files, same-path slot ordering, exact IDs/codes, invalid winners, hash collisions, and DSON purchase round trips;
- `ResourceConsumerRecordContractTests.cs`: current-header termination through public item catalogs, exact upgrade purchase hashes and DSON writes, actual curio type-block/input/Loot columns, notes exclusion and incomplete byte/buffer analysis;
- `EncounterRecordContractTests.cs`: logical record identity across all three types, same-line and multiline records, raw byte-limited slots, roaming overwrite/clear behavior, direct/Bridge map writes and deletion, generated option placement, and obsolete-package cleanup with ownership/tamper guards;
- `HeroSkinManifestContractTests.cs`: unlisted skin exclusion in real candidate generation, missing listed texture refresh, and restored continuous colour ranges;
- `HeroQuirkContractTests.cs`: quirk classification, HP rules, conflicts, and selection boundaries;
- `JsonMemberContractTests.cs`: first exact resource JSON members (including wrong types), repeated Buff occurrences through HP validation and DSON persistence, and raw town-event recruit payload/class binding;
- `JsonReferenceConsumerContractTests.cs`: native JSON filename/structure filtering, town/raid roots, irrelevant notes, duplicate fields, uncertain loaded structures, refresh of nonliteral suffixes, and real actor-to-nested-loot references;
- `CatalogFileQueryContractTests.cs`: native trinket/Buff/quirk/camping filename queries across six source types, exact-dot/root exclusions, manifests, same-path priority, missing files, content refresh and generated HP DSON;
- `TextResourceQueryContractTests.cs`: native inventory/capacity/Effect/Curio filename queries across six source types, mounted-root reference exclusions, actor-documentation rejection with canonical actor controls, manifests, overlays, missing diagnostics, same-size content refresh, curio preflight and capacity-limited DSON;
- `EncounterFileQueryContractTests.cs`: wildcard-separator region/difficulty queries, manifest and DLC boundaries, independent hall/room/boss indexes, empty slots, direct/Bridge DSON replacement and deletion, and maintenance consistency;
- `EncounterDirectoryCaseContractTests.cs`: physical directory aliases versus raw-case manifest directory trees, custom table IDs, exact filename queries, all three indexes, global/direct/append/maintenance consistency, fingerprints and stale selections;
- `ReferenceContextContractTests.cs`: Curio subdirectory context, actor record/field eligibility, normal companion/monster loot, mounted DLC paths, missing/unreadable references and unchanged-manifest content refresh;
- `MapPropJsonContractTests.cs`: first nested JSON members, inherited defaults, difficulty fields, root/nested query stages, missing-path diagnostics and stale-choice guards across six source types;
- `HeroCandidateContractTests.cs`: candidate serialization, progression, full skill unlocks, and initial quirks;
- `StagecoachHeroSaveContractTests.cs`: ordinary/shard pool routing, GUID/upgrade append, stale guards, and multi-file transactions;
- `RealModLocalizationContractTests.cs`: optional read-only probes selected by `DDSE_SCHINESE_LOC_PROBE_MOD_ROOT` (Workshop 1143685298: 43 invalid values skipped while known valid names remain), `DDSE_RULER_LOC_PROBE_MOD_ROOT`, `DDSE_LOC2_PROBE_MOD_ROOT` (Eos_Nyx), and `DDSE_RURUTIA_LOC2_PROBE_MOD_ROOT`;
- `ContractTestSupport.cs`: shared assertions and binary/image/localization helpers.

Keep the modules serial unless their fixtures are made independent. Several transaction contracts deliberately alter a shared file and restore it before the next assertion.

Run the complete suite from the repository root:

```powershell
dotnet run --project tests\DarkestDungeonSaveEditor.ContractTests\DarkestDungeonSaveEditor.ContractTests.csproj -c Release -- "<repository-root>"
```

Close Darkest Dungeon before the suite: save transaction contracts exercise the real process guard even with isolated fixtures. `--catalogs` selects the catalog/save group, `--maintenance` selects encounter maintenance, `--manifests` selects manifest preparation, `--semantics` selects duplicate Buff/Effect/skill/event rules, exact identities, shared record/curio consumers, empty encounter slots and logical encounter/Bridge contracts, and `--map-content` selects the battle-map group including standalone JSON/DSON placements and native prop pools; these partial runs do not replace the complete suite. Set `DDSE_TEST_GAME_DIRECTORY` to the installed game root to additionally exercise the verified official uploader against isolated samples.

## 资源文件查询验证

`dotnet run --project tests/DarkestDungeonSaveEditor.ContractTests -c Release -- . --queries`

只运行上述物品、容量、Effect、奇物和战斗文件查询两组新契约，包含隔离 DSON 保存与 Bridge 安装；不修改真实档案或活动 Mod。完整套件也包含这两组。构建后可添加 `--no-build` 执行。

其中 `ResourceDirectoryQueryContractTests` 覆盖六种来源的清单／物理目录大小写、Buff 与实际人物 HP、物品堆叠及背包容量的 DSON 往返、饰品／技能／奇物资源、引用消费、DLC 前缀和缺失文件诊断。`ActorDiscoveryQueryContractTests` 覆盖人物／怪物不同的文件名表达式、注册与标准路径打开的区别、饰品职业要求、人物伴生物品、三类战斗编号及自动维护。`EncounterFileQueryContractTests` 额外验证未注册大写怪物文件后的正常战斗替换、Bridge 新建和删除。

资源目录测试还包含 24 组清单别名排列对照：错误大小写条目在合法条目之前或之后时，Loot、Curio、配给 JSON 与 DLC 前缀的有效引用必须一致，防止筛选前去重造成漏读。

## 持久副本目录验证

`dotnet run --project tests/DarkestDungeonSaveEditor.ContractTests -c Release -- . --raid-paths`

`NestedRaidSaveContractTests` 覆盖 `raid_save` 子目录读取、背包和地图 DSON 写入、备份、回滚、目录切换保护、自动同步和强制回城。并复用维护契约验证子目录下的直接写入及 Bridge 放置、失效清理、回城和 A→B 后保留引用、两个持久副本共用表时逐图清理，以及中断恢复和外部修改保留。共享表恢复分别验证依赖未变、冻结包改变、未激活地图改变；不会操作真实档案。
