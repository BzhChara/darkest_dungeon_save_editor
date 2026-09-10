# HP、资源引用与战斗权重修复，2026-09-10

用户确认修复[第七轮审核](hp-reference-rule-audit-2026-09-10.md)中的六类问题。实现基线为 `bfd0afb`；本轮没有改写真实档案、活动 Mod、Steam 状态或系统配置，也没有新增依赖。

## 已实施的变更

| 问题 | 实现与结果 |
| --- | --- |
| HP 输入精度不一致 | `HeroClassCatalog.QuirkDefinitions` 对 Buff amount、rule float 先做原生单精度转换，再进入目录和候选生成。`-0.99999999` 与原生 `-1` 一致，生成前拒绝；溢出后的非有限值保留不可用状态。`StagecoachHeroCandidateFactory.CandidateSerialization` 将最终 HP 序列化为 float；`SaveEditService.StagecoachHeroes` 仅按实际 float 位值核对这次新人物的 HP，解决不同 Java 数字表示造成的往返误拦。最终生命超出 float 范围也在生成前拒绝。 |
| 模式、枚举归一化改变含义 | Buff stat/rule 字符串保留大小写与空格，按 64 字节缓冲区和 NUL 处理；截断成无效 UTF-8 时标为未验证。`StagecoachHeroCandidateFactory.HitPoints` 按模式哈希枚举和比较活动条件，光照阈值边界采用相邻 float。 |
| Effect／事件误合并 | 候选字典和运行时怪癖线索去重使用精确身份。大小写不同的合法条目独立显示，真实哈希冲突仍明确拒绝；Effect 字段保留/清空、事件首匹配规则不变。 |
| 资源消费范围过宽 | 新增 `NativeResourceFileRules`，供人物目录、物品引用及缺失文件检查共用。按消费根与文件名表达式过滤 loot/event 文件，保留有效嵌套目录、原版/DLC、Mod 中的已启用 DLC 路径。 |
| 损坏 JSON 被肯定为有效引用 | 为兜底掉落线索建立单独的不确定根集合，沿掉落图传播到 `AnalysisIncomplete`，保留原始错误证据。另一条合法根证明的物品仍为 `ConfirmedActive`。 |
| 战斗 chance 误读 | 删除独立旧正则，复用 `NativeDarkestReader.ReadFloat`，无字段默认 0。结尾空字段和浮点下溢正确得到 0，普通候选过滤正确；有效组合继续占号，直接验证和 Bridge 追加索引不变。 |

本轮影响人物的怪癖选择/HP 校验、生成预览与候选保存，人物技能怪癖线索及招募来源，物品引用状态与默认显示，以及战斗分类与普通候选。HP 公式和预览计算顺序保留，最终候选数值按 DSON 可保存的 float 序列化。往返比较仅对本次新人物的 HP 验证精确 float 位值；所有其他字段、已有英雄、roster/upgrades 和纯 JSON 源文件仍严格比较。存档结构、编解码工具、物品/饰品数量分配、Mod 覆盖优先级和各资源重复定义策略未另行改变；Bridge 归属、地图实例、过期内容检查和保存事务保护保留。

Buff 字符串检查只将 `combat_stat_add`/`combat_stat_multiply` 的 subtype 对照原生十项 ActorCombatStat 哈希，不把该枚举套到其他 Buff 家族。新增静态依据为 `0x140068E60` 的十项初始化和 `0x140489550` 的查找。不能根据本轮结果宣称已经逐项模拟所有 Buff、事件触发条件或整套游戏 HP 运算。

## 规则文档与证据

- [资源语义规则第 14 节](../resource-duplicate-semantics.md#14-buff-scalarcondition-identities-and-resource-eligibility-2026-09-10)记录当前实现的边界；[内容与存档规则](../content-save-rules.md)及[战斗编号规则](../encounter-runtime-order.md)同步更新。
- 原生依据仍为 Windows x64 build 27890，游戏 SHA-256 `35E5A653279992564809FF8406FEBD5A02A7D6961044781B1296B38A7096F59B`。证据位于忽略目录 `workspaces/historical-rule-review7-20260910/`，包括 `native_1404a3450.txt`、`native_140068e60.txt`、`native_140489550.txt`、`native_1404736a0.txt`、`native_14046b7d0.txt`、`native_140441410.txt`、`native_1403e8730.txt` 和 `native_1404c90f0.txt`。
- 本轮是静态原生核对与隔离可执行验证，没有启动游戏观察新一轮人物/战斗表现。

## 回归内容

- `HeroBuffPrecisionContractTests`：舍入到 -1、原生浮点溢出/下溢、最终 HP 超出 float；六组近零、小数及大数的实际马车插入/DSON 往返，要求新 HP 位值一致、其他整份文档 `DeepEquals`。八类篡改反例覆盖 HP 相差一 ULP、缺失、字符串、零、整数类型、压力、旧人物 HP 同位但小数不同，以及无关字段；比较不得改动原文档。另保留 Java 8 大数表示的正向样例，以及六种错误枚举拼写、合法非 HP subtype、NUL、完整/截断 UTF-8、模式条件/哈希/63 字节边界、同值光照阈值和 Effect/事件身份测试。
- `ResourceEligibilityContractTests`：九种路径分别覆盖 local、base、DLC 和 Mod 中的 DLC 路径，共 36 组；人物来源与物品引用结果一致；原生正则接受的 `.lootXjson`、`.town_events.eventsXjson` 同时覆盖发现、解析和内容指纹刷新；缺失但不会被消费的清单条目不污染分析，缺失有效掉落文件仍报未完成；损坏 JSON 的不确定根穿过嵌套/循环表，同时保留另一确定根。
- `EncounterChanceContractTests`：走廊、房间、Boss 各十种数值/字段输入，验证实际权重、分类、普通候选、编号 0、直接复查与下一追加编号 1。Boss 分类继续遵循类型 2 的既有规则。
- 事件夹具的 25 处文件名/清单/证据引用改为符合消费规则的名字，其中包括“损坏有效文件”和“缺失有效文件”的异常测试；不把旧误读样例继续当作正常来源。旧 HP/战斗断言改用原生 float 值；预览仍验证既有计算公式，候选 JSON 与 DSON 按存储精度核对，并额外要求整个文档往返相等。三处非 HP Buff 夹具把不存在的 subtype `accuracy` 改为原生 `attack_rating`，继续验证合法非 HP Buff 可用。未删除索引、身份或写入断言。

## 验证过程

首次默认并行构建返回失败，只有“0 警告、0 错误”，没有可归因的编译诊断。未将其算作通过；随后禁用构建服务器、使用单进程构建，Release 解决方案通过。没有为此修改系统设置或关闭其他项目进程。

首次定向运行在旧 HP JSON 精确相等断言停止：原生 float 转成 double 后出现小数，既有 JSON 格式化会损失很小的十进制尾数。断言分别核对原生输入值、预览值及格式化允许误差后，定向套件全部通过：[日志](../../workspaces/historical-rule-review7-20260910/fix-semantics-v2.log)、[退出码 0](../../workspaces/historical-rule-review7-20260910/fix-semantics-v2.exitcode)。

首次完整套件在 DLC 遭遇旧断言 `Weight == 0.7` 停止，改为原生 `(double)0.7f`，其余行序、缺失定义、体型和索引断言保留。失败日志保留为 `fix-semantics.log`、`fix-contracts.log`，不计为最终验收。

第二次完整套件在物品目录异常测试停止：原测试把 `campaign/town_events/malformed_reference.json` 当作有效事件文件，另外“缺失文件”样例也有相同前提问题。两者改成合法 `.town_events.events.json` 路径，继续保留原来的损坏/缺失警告与 `AnalysisIncomplete` 断言；新路径测试则明确检查不被消费的文件不会触发这些警告。该次失败日志为 `fix-contracts-final.log`，文件名中的 final 不代表那次已通过。

目录回归又揭示了 HP 保存链的真实问题：等级 4 样例的预览计算结果为 `38.4000000953674`，DSON 解码后为 `38.4`，整份文档校验因此拒绝。只采用 .NET 的 float `R` 格式仍不足以通过接近零样例，因为 `.NET 8 JsonNode.DeepEquals` 会区分 `1.1920929E-06` 与编码器输出的 `1.1920929E-6`。失败日志 `acceptance-catalogs.log`、`acceptance-catalogs-v2.log` 保留；`fix-catalogs.log` 则记录上述旧 `accuracy` 夹具失败。

曾尝试模拟编码器的十进制文本表示，Java 21 的完整套件通过；但项目支持 Java 8 及以上，随后本机 Java 8 实测证明该方案仍会误拦大数：[输入](../../workspaces/historical-rule-review7-20260910/java8-hp/case-3.json)为 `1.0E11`，[解码结果](../../workspaces/historical-rule-review7-20260910/java8-hp/case-3.java8.json)为 `9.9999998E10`，两者对应同一个 float。该文本模拟代码已删除。最终使用只针对本次候选 HP 的精确 float 位比较，不添加误差阈值，不规范化已有英雄或其他字段，也不改动实际解码文件。纯 JSON 存档不需要 DSON 舍入，继续完整严格比较。

独立只读复核发现一项 P3：原生文件名正则允许非字面 `.json` 后缀，但清单抽取和发现入口会漏掉。主代理核实后，已贯通人物、物品引用的发现与解析，以及 `ProfileCatalogContentFingerprint` 刷新，并补上四种来源的正向用例。因后续新增了 HP 保存行为，另做保存专项补充复核；最终结论无发现，确认仅本次池/GUID 的新人物 HP 按精确 float 位值比较，其他数据、哈希、事务及回滚保护保留。复核还完成了普通/碎片池和 GUID 隔离的 20 项只读内存检查；没有修改文件或操作真实存档。

## 最终验收

- Release 解决方案：`dotnet build DarkestDungeonSaveEditor.sln -c Release --no-restore --disable-build-servers -m:1 -p:UseSharedCompilation=false`，0 警告、0 错误，[日志](../../workspaces/historical-rule-review7-20260910/acceptance-build-v4.log)、[退出码 0](../../workspaces/historical-rule-review7-20260910/acceptance-build-v4.exitcode)。
- 目录/人物/数量/保存回归：`--catalogs`，Java 21 的[日志](../../workspaces/historical-rule-review7-20260910/acceptance-catalogs-v4.log)和[退出码 0](../../workspaces/historical-rule-review7-20260910/acceptance-catalogs-v4.exitcode)，以及 Java 8 的[日志](../../workspaces/historical-rule-review7-20260910/acceptance-catalogs-java8.log)和[退出码 0](../../workspaces/historical-rule-review7-20260910/acceptance-catalogs-java8.exitcode)，均全部通过。旧版实际为本机 Java 1.8.0_461；只对测试命令的进程环境选择该运行时，没有修改系统 PATH。
- 完整契约套件：最终版本不带分组选项运行，32 条 PASS 汇总、无 FAIL/SKIP，[日志](../../workspaces/historical-rule-review7-20260910/acceptance-contracts-v4.log)、[退出码 0](../../workspaces/historical-rule-review7-20260910/acceptance-contracts-v4.exitcode)。覆盖清单与覆盖顺序、物品/饰品数量、怪癖/升级/马车、DSON、三类遭遇编号、Bridge 追加/复用/维护、地图新建/替换/删除/附件、自动同步、强制回城、外部修改保护与保存回滚。32 是汇总行数，不是断言总数。
- [验收摘要](../../workspaces/historical-rule-review7-20260910/fix-acceptance-summary.json)记录实际退出码；[程序集核验](../../workspaces/historical-rule-review7-20260910/fix-assemblies.json)确认 Core/App/ContractTests 三处 Core DLL 的 SHA-256 均为 `0FFE308EB9B79BB70CEF4D840553BDEA15A9782596C495F8AE19FE616A4C6264`。游戏哈希与固定原生证据一致。
- `git diff --check` 通过。测试与探针只使用工作区内的隔离样例；没有新增实机测试。
