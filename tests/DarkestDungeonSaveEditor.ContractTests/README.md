# Contract test layout

`Program.cs` only handles the command-line boundary and reports an unhandled failure. `ContractSuite.cs` owns the intentionally serial execution order.

The suite is split by responsibility:

- `LoggingContractTests.cs`, `EncounterDiagnosticContractTests.cs`: `--catalog-diagnostics` covers file-scoped issue grouping, informational fifth-slot notices, field-slot clues, unchanged direct/Bridge eligibility and preflight checks. It also runs the encounter record, Bridge lifecycle and map persistence contracts; all fixtures are isolated.
- `ContractFixture.cs`: isolated game, Mod, profile, localization, and DSON fixtures;
- `CodecCancellationContractTests.cs`: pre-cancelled DSON/JSON operations leave no output directory; running encode/decode cancellation terminates an isolated test process and its child, drains both redirected pipes and prevents late writes (`--codec-cancellation`, `--content-sync` and the full suite). The test executable supplies the deterministic process fixture; no real save or Java installation is modified.
- `SaveReplacementContractTests.cs`: native replacement-access checks beyond the legacy Windows path limit, existing extended paths, real sharing locks and missing targets; atomic replacement, external-version preservation and failed-transaction recovery (`--save-replacement` and the full suite).
- `ProfileSyncContractTests.cs`, `ProfileSyncInteractionContractTests.cs`, `InitialProfileLoadContractTests.cs`: save-only progress/quantity refresh while unrelated resources are locked, explicit content validation, same-size/timestamp updates, cached scenes, partial-save recovery, WPF foreground minute scheduling, inactive polling, queued refreshes, preview preservation, game-exit maintenance and cancelled profile generations. Initial loading also covers continuously disabled controls until the first synchronization completes, intervening save changes, failed-read recovery, closing during the initial read, and reload/close waiting for a previous reader's cancellation cleanup before path validation (`--content-sync` and the full suite).
- `TrinketDependencyContractTests.cs`: 180 ordinary Buff/class-reference selection cases across Base/local/Workshop, first-valid duplicate selection, provider and read-failure boundaries, actual DSON counters, dependency removal/restoration and transaction recovery; actual source resolution also covers unmapped/ambiguous local titles, absent Workshop providers and retained commit context;
- `DefinitionReadCompletenessContractTests.cs`: ordered failures of quirk, hero-upgrade, trinket and item definition files across Base/local/Workshop; missing/locked/malformed files, unknown tree presence, independent winners, saved-amount refresh, healthy stale previews, mid-write rollback and recovered DSON values (`--trinket-dependencies`, `--catalogs`, and the full suite);
- `HeroSupportingReadContractTests.cs`: six-source Effect disease-field omission/clear/reassignment and first-event recruitment certainty after failed reads; Base/local/Workshop camping-pool completeness with zero equipped skills, unchanged visible IDs, old previews, post-replacement rollback and recovered DSON unlocks; independent metadata and shadowed-provider controls (`--trinket-dependencies`, `--catalogs`, and the full suite);
- `HeroBuffEnumContractTests.cs`: 120 native enum-alias cases through initial and reachable HP states, canonical internal conditions, non-positive HP rejection and DSON persistence. Run both new modules with `--trinket-dependencies`; both are also in the full suite;
- `UpgradeReferenceContractTests.cs`: final complete upgrade trees and per-code requirements, generation/purchase DSON, town currency references, same-path slots, local/Workshop/DLC manifests, first JSON members, code bytes, missing/malformed files, independent uses and content-only refresh (`--upgrade-references`, also in the full suite);
- `CaseIdentityContractTests.cs` and `BridgeCaseIdentityContractTests.cs`: case-distinct hero catalogs/overrides and DSON generation, exact map selectors, all three Bridge types across `cove`/`Cove`/`COVE`, collision-free carrier allocation/reuse, invalid-source region isolation, and pre-NUL capacities through actual quantity/trinket commits (`--case-identities`);
- `ManifestCaseAliasContractTests.cs`: one local/Workshop manifest containing both case spellings of a physical file, both line orders, actor registration, region pools, `B,a,b` first/last resource values, HP DSON and all-type append counts; the Bridge cases additionally persist these aliases through actual placement/maintenance/deletion (also `--case-identities`);
- `CanonicalResourceContractTests.cs`: original-request manifest matching versus physical aliases, DLC root fallback, missing winners, hero HP/XP and actor loot, regional pools, Effect flags-1 order, District flags-9 town/raid references, all three battle types and direct/Bridge DSON writes, maintenance and deletion (`--canonical-resources`);
- `UiContractTests.cs`: XAML, theme, asset, and UI source contracts;
- `QuirkSelectionInteractionContractTests.cs`: actual WPF checkbox templates and click handlers for compensating HP quirks, current-combination availability, removal/repair, filtering/clear, level changes, quotas, exclusions, definition guards, singleton warnings and two actual DSON candidate commits (`--quirk-selection`, also in the full suite). `WpfContractTestHost.cs` shares a single STA/Application across full-suite UI groups; no visible window or real save is opened.
- `--quirk-selection` also runs the existing initial-HP condition contracts and profile-sync interaction group, verifying that both UI groups share the same WPF host successfully.
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
- `TrinketJsonMemberContractTests.cs`: first JSON members, optional counter defaults, provider attribution and actual trinket-counter DSON roundtrips;
- `InventoryPersistenceContractTests.cs`: Base/local/Workshop save identities, ASCII/CJK/supplementary UTF-8 boundaries, NUL aliases, wallet persisted-type mapping, save-only refresh, signed/zero/default counter matrix, exact town/raid/trinket DSON commits and definition-change guards (`--inventory-persistence`);
- `InventoryProviderContractTests.cs`, `MissingInventoryProviderContractTests.cs`, `MissingResourceProviderContractTests.cs`: raw definition selection before wallet aggregation, raid limits and native collision controls; local/Workshop missing winners, masked missing lower files, unlisted controls, exact quantity/trinket DSON writes, preview/commit deletion guards, town/raid saved-only refresh and recovery; missing quirk/Buff/localization/mash/prop/shared-query providers, stable missing/restored refresh fingerprints, battle append and maintenance rejection (`--inventory-persistence`, also in the full suite);
- `MissingResourceSyncContractTests.cs`: WPF prop-provider deletion clears stale choices without escaping the shared synchronization call, preserves valid map/battle catalogs and restores prop choices after resource recovery (inside `--content-sync` and the full suite's existing WPF host);
- `TrinketSaveContractTests.cs`: pristine trinket construction and guarded commits;
- `HeroContractTests.cs`: orchestration of the following hero-related modules;
- `EquipmentProgressionContractTests.cs`: six-source equipment byte codes, sequential purchase reachability, free upper ranks/level-zero fallback, unused requirements, bounded targets and actual town/roster/upgrades DSON persistence;
- `EquipmentProgressionGuardContractTests.cs`: inherited/cleared codes, invalid HP and identities, codec round-trip limits, unchanged-manifest refresh, raw-definition fingerprints and stale preview/save preflight;
- `CombatPurchaseIdentityContractTests.cs`: six-source bounded combat target binding, explicit/implicit purchases, full skill metadata, UTF-8/identity/hash guards, invalid winners, content refresh and actual DSON identities;
- `CombatPurchaseCodeContractTests.cs`: authored base-code availability, preserved code case, native reachable tiers, gap warnings, delayed/partial trees, bounded-target majority voting and DSON persistence;
- `CampingPurchaseIdentityContractTests.cs`: native 63-byte camping purchase targets across six source kinds, ASCII/UTF-8 boundaries, code-zero behavior, target/hash collision and malformed-identity preflight guards, definition refresh, and DSON persistence without changing skill IDs or other purchases;
- `HeroCatalogContractTests.cs`: effective hero definitions, overrides, localization, and progression catalogs;
- `HeroUpgradeTreeContractTests.cs`: native last-match tree resolution, partial/mixed-class/nested files, same-path slot ordering, exact IDs/codes, invalid winners, hash collisions, and DSON purchase round trips;
- `ResourceConsumerRecordContractTests.cs`: current-header termination through public item catalogs, exact upgrade purchase hashes and DSON writes, actual curio type-block/input/Loot columns, notes exclusion and incomplete byte/buffer analysis;
- `EncounterRecordContractTests.cs`: logical record identity across all three types, same-line and multiline records, raw byte-limited slots, roaming overwrite/clear behavior, direct/Bridge map writes and deletion, generated option placement, and obsolete-package cleanup with ownership/tamper guards;
- `HeroSkinManifestContractTests.cs`: unlisted skin exclusion in real candidate generation, missing listed texture refresh, and restored continuous colour ranges;
- `HeroQuirkContractTests.cs`: quirk classification, HP rules, conflicts, and selection boundaries;
- `HeroQuirkRuleContractTests.cs`: six-source shared-rule queries, active positive/negative/disease limits, zero/default/unresolved values, exact first fields, whole-file replacement and native file slots, plus DSON persistence above former limits;
- `HeroQuirkEvolutionContractTests.cs`: explicit neutral defaults, ignored notes, empty death targets, countdown DSON persistence, omitted bounds, retained invalid-chain/type guards and separate record/slot-size limits;
- `HeroQuirkGuardContractTests.cs`: same-size/timestamp/manifest resource refresh, actual stale prepare/commit and candidate-rebinding rejection without save mutations, countdown compatibility after evolution/range changes, semantic-equivalent evolution edits and a successful three-file save;
- `JsonMemberContractTests.cs`: first exact resource JSON members (including wrong types), repeated Buff occurrences through HP validation and DSON persistence, and raw town-event recruit payload/class binding;
- `JsonReferenceConsumerContractTests.cs`: native JSON filename/structure filtering, town/raid roots, irrelevant notes, duplicate fields, uncertain loaded structures, refresh of nonliteral suffixes, and real actor-to-nested-loot references;
- `LootReferenceContractTests.cs`: first-member float32 weights, ordered conditional loot variants, compatible nested/cyclic paths, native table hashes/buffers, a concrete-context selection oracle, Mod overlays, refresh, saved hidden items and manual DSON previews (`--loot-references`);
- `QuantityReferenceReadContractTests.cs`: ordered event/loot read failures across six source kinds, complete-file event decoding, earlier and independent winners, conditional/nested uncertainty, shadowed providers, quantity refresh/stacking, shared snapshot recovery and idle cache reuse (included in `--loot-references`, `--catalogs` and the full suite);
- `LootItemIdentityContractTests.cs`: C-string category hashes, 63-byte UTF-8 item references (including split characters), aliases, first JSON members, conflict refusal, unchanged-manifest refresh and actual DSON commits preserving the selected definition ID (included in `--loot-references` and the full suite);
- `CatalogFileQueryContractTests.cs`: native trinket/Buff/quirk/camping filename queries across six source types, exact-dot/root exclusions, manifests, same-path priority, missing files, content refresh and generated HP DSON;
- `TextResourceQueryContractTests.cs`: native inventory/capacity/Effect/Curio filename queries across six source types, mounted-root reference exclusions, actor-documentation rejection with canonical actor controls, manifests, overlays, missing diagnostics, same-size content refresh, curio preflight and capacity-limited DSON;
- `EncounterCollectionQueryContractTests.cs`: six-source independent Standard/Conditional/Additional queries, keyword decoys and wildcard suffixes, overlapping query identities, repeated local/Workshop Bridge sources, full source identity guards, omitted/bare/whitespace/quoted-empty slots, and 12 DSON create/replace/delete/maintenance scenarios. Run these plus existing filename/empty-slot regressions with `--encounter-queries`;
- `EncounterFileQueryContractTests.cs`: wildcard-separator region/difficulty queries, manifest and DLC boundaries, independent hall/room/boss indexes, empty slots, direct/Bridge DSON replacement and deletion, and maintenance consistency;
- `EncounterDirectoryCaseContractTests.cs`: physical directory aliases versus raw-case manifest directory trees, custom table IDs, exact filename queries, all three indexes, global/direct/append/maintenance consistency, fingerprints and stale selections;
- `ReferenceContextContractTests.cs`: Curio subdirectory context, actor record/field eligibility, normal companion/monster loot, mounted DLC paths, missing/unreadable references and unchanged-manifest content refresh;
- `NonActorReferenceContractTests.cs`: known JSON/Curio query gates for non-actor text and missing-file diagnostics; six source types, nested/case variants, native actor controls, per-item uncertainty through Loot, independent confirmed roots, refresh and 12 quantity DSON roundtrips;
- `QuantityReferenceProviderContractTests.cs`, `QuantityReferenceIdentityContractTests.cs`: 60 local/Workshop provider scenarios and 76 wallet-identity scenarios; masked versus effective missing files, additive district inputs, context isolation and byte restoration; raw item references versus persisted currencies, wallet variants, first-match definitions, official provenance, saved-amount refresh and four isolated service/DSON commits (`--reference-consumers`, also in the full suite);
- `MapPropJsonContractTests.cs`: first nested JSON members, inherited defaults, difficulty fields, root/nested query stages, missing-path diagnostics and stale-choice guards across six source types;
- `HeroCandidateContractTests.cs`: candidate serialization, progression, full skill unlocks, and initial quirks;
- `HeroInitialHpConditionContractTests.cs`: initial HP from known unafflicted/unequipped state across six source kinds, inverse and raid-context conditions, mixed/repeated Buff references, unknown/non-positive HP guards and ordinary/shard DSON persistence (`--semantics` and the full suite);
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

非人物引用专项：`dotnet run --project tests/DarkestDungeonSaveEditor.ContractTests -c Release -- . --reference-consumers`。覆盖 228 个内容样例、876 次小镇／副本目录检查及 12 次 DSON 保存回读；`--queries` 和完整套件也包含这一组。样例只写隔离资源与存档，验证无关文本及缺失清单项不会误确认引用或污染其他物品，同时保留未验证来源的不确定性和合法伴生掉落。

该专项还执行物品引用提供者与钱包身份的 136 个新增场景，包含 4 次隔离存档实际提交及 DSON 回读；完整套件也执行这些新增测试。`--queries` 保持原有非人物引用测试范围。逐项结果保存在本次测试工件目录的 `results.json` 中；这些是编辑器契约测试，不代表新增了游戏实机实验。

大小写专项：`dotnet run --project tests/DarkestDungeonSaveEditor.ContractTests -c Release -- . --case-identities`。同时运行既有容量矩阵。完整套件还运行 `CanonicalResourceContractTests` 中原生 `arena` 精确排除、`ARENA` 物理别名及 Mod 清单对照；此部分也可用 `--canonical-resources` 单独执行。专项不能替代整套测试。

`dotnet run --project tests/DarkestDungeonSaveEditor.ContractTests -c Release -- . --queries`

只运行上述物品、容量、Effect、奇物和战斗文件查询两组新契约，包含隔离 DSON 保存与 Bridge 安装；不修改真实档案或活动 Mod。完整套件也包含这两组。构建后可添加 `--no-build` 执行。

其中 `ResourceDirectoryQueryContractTests` 覆盖六种来源的清单／物理目录大小写、Buff 与实际人物 HP、物品堆叠及背包容量的 DSON 往返、饰品／技能／奇物资源、引用消费、DLC 前缀和缺失文件诊断。`ActorDiscoveryQueryContractTests` 覆盖人物／怪物不同的文件名表达式、注册与标准路径打开的区别、饰品职业要求、人物伴生物品、三类战斗编号及自动维护。`EncounterFileQueryContractTests` 额外验证未注册大写怪物文件后的正常战斗替换、Bridge 新建和删除。

资源目录测试还包含 24 组清单别名排列对照：错误大小写条目在合法条目之前或之后时，Loot、Curio、配给 JSON 与 DLC 前缀的有效引用必须一致，防止筛选前去重造成漏读。

## 持久副本目录验证

`dotnet run --project tests/DarkestDungeonSaveEditor.ContractTests -c Release -- . --raid-paths`

`NestedRaidSaveContractTests` 覆盖 `raid_save` 子目录读取、背包和地图 DSON 写入、备份、回滚、目录切换保护、自动同步和强制回城。并复用维护契约验证子目录下的直接写入及 Bridge 放置、失效清理、回城和 A→B 后保留引用、两个持久副本共用表时逐图清理，以及中断恢复和外部修改保留。共享表恢复分别验证依赖未变、冻结包改变、未激活地图改变；不会操作真实档案。

## 人物怪癖上限与进化验证

`dotnet run --project tests/DarkestDungeonSaveEditor.ContractTests -c Release -- . --quirk-rules`

运行上述三组怪癖规则测试；完整套件也包含它们。目录矩阵覆盖原版、模式、官方 DLC、本地 Mod、工坊 Mod、带启用 DLC 前缀的 Mod。共 90 个 shared 文件查询对照、36 次 town / roster / upgrades DSON 往返，并验证实际提交与旧预览保护。全部使用隔离数据，不修改真实存档或 Mod。

## 掉落引用验证

`dotnet run --project tests/DarkestDungeonSaveEditor.ContractTests -c Release -- . --loot-references`

本地、工坊和 DLC 前缀 Mod 各运行 58 个权重／同名表／条件与子表对照；另以 32 个随机种子固定的掉落图分别枚举 216 个具体条件，独立对照范围算法。每条表使用独立物品 ID，避免不同错误路径被同一个物品结果掩盖。还验证同路径覆盖、不同路径先匹配、未列清单文件、清单不变时的内容刷新、已有隐藏物品可见，以及 3 次保留 ID 和堆叠的真实 DSON 写入预览。只使用隔离资源和存档；完整套件也包含这些测试。

另包含三类来源各 26 个掉落身份样本：类别／物品类型／ID 的 NUL、哈希别名、63 字节与 UTF-8 边界、重复字段、空 ID、零权重和不确定权重。验证同哈希的多个定义继续只读，权重与定义不确定原因分别正确记录，重复同 ID 保持首定义数值，内容字节改变但清单／长度／时间戳不变时仍更新引用。三个真实 DSON 提交确认写入定义 ID 而非引用别名；每个样本还验证堆叠、输入不变、数量刷新和小镇／副本边界。
