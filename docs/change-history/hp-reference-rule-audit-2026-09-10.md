# 历史修改第七轮审核：HP、资源引用与战斗权重，2026-09-10

按用户要求先提交上一轮修复：`bfd0afb`，`fix: align encounter records and resource consumers with native parsing`，包含 33 个文件。提交后工作区干净，再开始本轮审核。

确认六类遗留问题：两类影响人物 HP 计算与校验，四类影响人物线索、招募来源、物品引用状态或战斗分类。**本轮没有修复产品代码。** 以下是隔离输入下的可复现缺陷，未据此断言当前 `profile_1` 正在受到影响。

## 验证依据与边界

- 产品代码固定在 `bfd0afb`。探针引用现有 Release Core 与契约测试程序集，复用测试夹具；没有重建或改写产品代码，也没有添加依赖。
- 本轮没有启动游戏，没有修改真实档案、活动 Mod 或 Steam 状态。假资源、假档案和探针均放在忽略目录 `workspaces/historical-rule-review7-20260910/`。
- 原生依据是本机 Windows x64 build 27890。游戏 SHA-256：`35E5A653279992564809FF8406FEBD5A02A7D6961044781B1296B38A7096F59B`。新证据来自只读反汇编及调用链核对，不是新一轮实机观测。
- [探针项目](../../workspaces/historical-rule-review7-20260910/ReviewProbe.csproj)、[源码](../../workspaces/historical-rule-review7-20260910/Program.cs)、[最终 19 个样例](../../workspaces/historical-rule-review7-20260910/runs/b0ec02a4b8df43ccb2b8eecf2df25996/results.json)、[执行日志](../../workspaces/historical-rule-review7-20260910/probe-final-verified.log)、[退出码 0](../../workspaces/historical-rule-review7-20260910/probe-final-verified.exitcode)已保留。
- [结果断言脚本](../../workspaces/historical-rule-review7-20260910/verify_results.py)和[通过日志](../../workspaces/historical-rule-review7-20260910/verification.log)核对了全部观察结果、一次隔离 DSON 往返、游戏哈希，以及 Core/App/ContractTests 三处 Core DLL。DLL 哈希均为 `EDE4E410445285AF4BC92C7255C3F608C4E71F18421C11ACBE4C1F30CAFFB35B`。
- 19 个样例包括六个 Buff/HP 输入、三个模式条件、三个 Effect/事件输入、四个掉落引用输入和三个战斗权重输入。结果中的 `nativeAmount` 是相同数值转成单精度后的值，`expectedNative` 是按原生代码得出的预期，均不是从运行中的游戏采集。
- 早期探针曾因访问内部类型而编译失败，及因反射遗漏可选第三参数而执行失败；都只修正探针，原日志保留。进一步核对发现早期事件样例的 `audit.events.json` 不符合游戏的事件文件匹配条件；最终大小写样例已改用 `audit.town_events.events.json` 并补齐事件字段，另把旧文件名作为独立的加载条件反例。损坏 JSON 样例也改用原生任务文件路径。失败运行及前提不完整的样例不计为最终证据。

## 1. P2：HP 校验没有先采用原生单精度数值

位置：[HeroClassCatalog.QuirkDefinitions.cs](../../src/DarkestDungeonSaveEditor.Core/HeroClassCatalog.QuirkDefinitions.cs)第 345、348 行；[StagecoachHeroCandidateFactory.HitPoints.cs](../../src/DarkestDungeonSaveEditor.Core/StagecoachHeroCandidateFactory.HitPoints.cs)第 26–33、195–212 行。

Buff 的 `amount`、`rule_data.float` 仍通过 `ReadJsonDouble` 保留双精度，HP 校验直接使用这些值。主体来自 `1a03bb42`，条件校验来自 `827ca422`，此前的 Buff 身份修复没有调整数值表示。

隔离职业基础 HP 为 20，怪癖引用一个始终生效的百分比 HP Buff：

| amount | 原生读取后的单精度值 | 当前编辑器结果 |
| --- | ---: | --- |
| `0.5` | 0.5 | 正常对照，生成 HP 30 |
| `-1` | -1 | 正常对照，拒绝非正生命修正 |
| `-0.99999999` | -1 | 错误放行，生成约 `0.0000002` HP |

最后一项还调用了 `StagecoachHeroSaveEditor.AddCandidate`，编码为 DSON 后重新解码，实际保存的 `actor.current_hp` 为 `2e-7`。这已到达写入层，不只是预览小数显示。

[原生 Buff 读取](../../workspaces/historical-rule-review7-20260910/native_1404a3450.txt)在 `0x1404A3D62` 对 amount 执行 `cvtsd2ss`，`0x1404A3D66` 存入单精度；rule float 在 `0x1404A4067` 同样转换。因此编辑器允许的该输入，在游戏资源中已经与明确禁止的 `-1` 相同。

影响：新人物 current_hp、怪癖可用性和组合生命校验可能不一致。这里证明的是原生输入精度与产品保护条件不一致；未以此断言游戏最终面板 HP、内部钳制行为或一定崩溃。

建议修复：按原生消费方式读取 Buff 数值，再贯穿分类、预览与保存校验；补上舍入到 -1、上下溢出、条件阈值的回归。后续若要更改整个 HP 运算的舍入顺序，仍应核对对应原生运算链，不能仅把所有 double 机械替换为 float。

## 2. P2：模式条件和 Buff 枚举的字符串归一化会改变含义

位置：[Buff 读取](../../src/DarkestDungeonSaveEditor.Core/HeroClassCatalog.QuirkDefinitions.cs)第 78–90、343–349 行；[字符串 helper](../../src/DarkestDungeonSaveEditor.Core/HeroClassCatalog.TextParsing.cs)第 19–22 行；[HP 条件枚举](../../src/DarkestDungeonSaveEditor.Core/StagecoachHeroCandidateFactory.HitPoints.cs)第 98–101、177–181 行。

当前会对 `stat_type`、`stat_sub_type`、`rule_type`、`rule_data.string` 去掉首尾空格，并在匹配时忽略大小写。模式条件还用不区分大小写的集合去重。这沿用了 `1a03bb42`、`827ca422` 的规则。

隔离职业声明两个模式 `Mode`、`mode`，基础 HP 20。两个 Buff 分别在对应模式下固定减少 30 和增加 30 HP。编辑器把两个条件视为相同，同时激活后相互抵消，允许生成。正常对照使用两个完全相同的 `Mode`，相抵消是预期行为；不同大小写的反例则不应相抵消。

原生模式哈希分别为 `11780729`、`16544793`。空格变体 ` Mode ` 为 `1121732557`，编辑器却将其读成 `Mode`，同样误放行。最终模式样例在人物 info 中明确声明了相应模式，并非仅凭 Buff 猜测职业可能具有这些模式。

原生依据：[Buff 读取](../../workspaces/historical-rule-review7-20260910/native_1404a3450.txt)复制 rule string 并按原字节计算哈希；[规则枚举初始化](../../workspaces/historical-rule-review7-20260910/native_140077f00.txt)登记 `in_mode` 为索引 27；[Actor 条件执行](../../workspaces/historical-rule-review7-20260910/native_1404736a0.txt)的该分支在 `0x140473CFE` 读取 Actor 当前模式，在 `0x140473D04` 与 Buff 的模式哈希直接比较。它不会把上述三个哈希合并。按该分支，负向模式只激活 -30，编辑器应发现的非正生命状态被遗漏。

另有三个字符串对照：`MAX_HP`、` max_hp `、`ALWAYS` 都被编辑器当作有效的 `max_hp` / `always`，标为 `Direct` 并计算 HP 30。原生 stat/rule 采用原字节哈希查枚举，无法据此认定这些输入等价于合法的小写名称。未知枚举后续可能走错误处理，故这里不臆测其最终游戏 HP，只确认编辑器给出了没有依据的可写与数值结论。

建议修复：区分显示文字与原生身份/枚举字段，保留条件字符串的原生字节意义及已核实的缓冲区限制；按真实模式身份计算活动条件。保留非正 HP 校验，修正的是输入和条件比较，不是删除保护。

## 3. P3：Effect、事件大小写不同的合法 ID 被合并为冲突

位置：[HeroClassCatalog.cs](../../src/DarkestDungeonSaveEditor.Core/HeroClassCatalog.cs)第 21–22 行；[DefinitionResolution](../../src/DarkestDungeonSaveEditor.Core/HeroClassCatalog.DefinitionResolution.cs)第 42–49 行。

Effect 和招募事件候选字典仍使用 `OrdinalIgnoreCase`，来自初始 `7b9a67f`。后来 `3d2bd9c` 的保护发现一个候选组有多个原始 ID 就整组拒绝。因此合法的大小写不同 ID 被旧分组规则放在一起，再被新冲突保护同时排除。

最终样例在合法文件中分别定义 `AUDIT_EFFECT`、`audit_effect`，关联两个不同怪癖；同时定义 `AUDIT_EVENT`、`audit_event` 两个招募事件。只保留大写项时，公共人物目录正常显示一条技能怪癖线索和一条招募事件；加入小写项后，两类列表都变成空，并各产生一条 `conflicting native identities`。

原生依据：[Effect 定义](../../workspaces/historical-rule-review7-20260910/native_1404e4ad0.txt)与[查询](../../workspaces/historical-rule-review7-20260910/native_1404e8140.txt)按原字节哈希；[事件解析](../../workspaces/historical-rule-review7-20260910/native_14046b7d0.txt)在 `0x14046BC50`–`0x14046BC71` 对 JSON id 的原字符串计算哈希，并写到事件结构的 `+0x40`；[事件查找](../../workspaces/historical-rule-review7-20260910/native_14058b390.txt)在 `0x14058B411` 比较该哈希。样例中的大小写变体哈希各不相同，不是实际的哈希碰撞。

影响限于编辑器的人物运行时怪癖线索、招募来源及其显示/搜索；本例没有证明游戏技能值被改变，也没有导致该职业整体不能生成。

建议修复：候选分组保留原生身份，继续按各资源已验证的 first/last 规则解决真正重复定义；保留真正的哈希冲突保护，检查后续去重链路是否再次忽略大小写。

## 4. P3：部分文件资格判断仍过宽，清单中的非消费文件被当成有效资源

位置：[QuantityItemReferenceAnalyzer.SourceDiscovery.cs](../../src/DarkestDungeonSaveEditor.Core/QuantityItemReferenceAnalyzer.SourceDiscovery.cs)第 168–187、210–214 行；[HeroClassCatalog.SourceDiscovery.cs](../../src/DarkestDungeonSaveEditor.Core/HeroClassCatalog.SourceDiscovery.cs)第 34、95–98 行。

物品引用的 `IsLootPath` 使用“路径包含 `/loot/` **或**后缀为 `.loot.json`”，沿用 `d1d18f66`。事件目录使用 `*.events.json`。这两处都比实际资源加载入口宽。

所有 Mod 文件都由探针生成的 `modfiles.txt` 明确列出，以下差异不是缺少清单或覆盖顺序导致：

| 文件 | 当前编辑器观察 | 原生加载条件 |
| --- | --- | --- |
| `loot/audit.loot.json` | 物品 `ConfirmedActive` | 正常对照，属于 loot 入口 |
| `loot/notes.json` | 物品 `ConfirmedActive`，无提示 | 不符合 loot 文件匹配表达式 |
| `heroes/audit.loot.json` | 物品 `ConfirmedActive`，无提示 | 不属于 loot 搜索根 |
| `campaign/town_events/audit.town_events.events.json` | 正常显示招募来源 | 正常事件文件对照 |
| `campaign/town_events/audit.events.json` | 仍显示相同招募来源，无提示 | 不符合事件文件匹配表达式 |

[Loot 加载入口](../../workspaces/historical-rule-review7-20260910/native_140441410.txt)在 `0x140441475`、`0x140441481` 将 `.*loot.json` 和 `loot/` 交给文件查询；[事件加载入口](../../workspaces/historical-rule-review7-20260910/native_1403e8730.txt)在 `0x1403E877E`、`0x1403E878A` 使用 `.*campaign/town_events/.*\.town_events.events.json` 和 `campaign/town_events/`，再逐个调用事件解析器。

因此，清单列出只满足挂载资格，不能代替消费入口的路径与文件名匹配。这里不把原生正则简写为另一个未经验证的“统一后缀规则”。

影响：备用/说明文件可使没有有效掉落入口的 Mod 物品被肯定为已引用，改变默认隐藏与选择；非事件文件可制造错误招募来源。没有证明这些文件会改变游戏资源或造成存档结构损坏。

建议修复：按资源消费类型收紧根目录和实际匹配条件，再做引用分析；贯穿清单来源、原版/DLC 来源及引用端。物品“有没有可达引用”的功能仍需要保留。

## 5. P3：损坏 JSON 中的字符串线索被提升为确定引用

位置：[QuantityItemReferenceAnalyzer.RootParsing.cs](../../src/DarkestDungeonSaveEditor.Core/QuantityItemReferenceAnalyzer.RootParsing.cs)第 51–63 行；[Matching](../../src/DarkestDungeonSaveEditor.Core/QuantityItemReferenceAnalyzer.Matching.cs)第 9–25 行；[结果分类](../../src/DarkestDungeonSaveEditor.Core/QuantityItemReferenceAnalyzer.cs)第 84、103–118 行。

这是 `d1d18f66` 留下的宽松兜底：JSON 解析失败后，只要文本包含一个加引号的掉落表名，`MarkQuotedLootCodes` 就将其写入确定的 `rootLootEvidence`。随后掉落图传播到 `activeEvidence`，结果又优先检查 active，最后虽然记录了解析错误，物品仍得到 `ConfirmedActive`。

最终反例只有合法掉落表，及无法解析的原生任务文件路径 `campaign/quest/quest.plot_quests.json`，内容为 `{ broken_json "audit_loot" `，没有其他有效引用入口。公共目录依然肯定该物品已引用，证据文字却是“无法完整解析但包含掉落表引用”。这两个置信结论相互矛盾。

影响：将不确定的文本命中误报为确定用途，掩盖引用分析未完成的状态。不能根据这个反例推断游戏会接受损坏的任务 JSON。

建议修复：兜底命中只能传播不确定线索；没有其他确定入口时返回 `AnalysisIncomplete`。若另有合法入口已证明可达，应仍保留 `ConfirmedActive`，不因不相关文件损坏一律降级。

## 6. P3：战斗 chance 的旧解析遗漏结尾空字段及单精度转换

位置：[BattleEncounterCatalog.NativeParsing.cs](../../src/DarkestDungeonSaveEditor.Core/BattleEncounterCatalog.NativeParsing.cs)第 17–33 行；[分类入口](../../src/DarkestDungeonSaveEditor.Core/BattleEncounterCatalog.cs)第 570–572、701 行。

旧实现来自 `730c01eb`。查找 `.chance` 的表达式要求后面是 TAB、空格或等号，没有包含记录结尾；数值还保留 double。上一轮只扩展了数字前导空白，没有解决这两个差异。

单行、四个已知体型 1 怪物的合法表，其三个对照均保持编号 0：

| 记录内容 | 原生 chance | 编辑器 Weight / 分类 |
| --- | ---: | --- |
| `hall: .chance 1 .types alpha alpha alpha alpha` | 1 | 1 / 普通，正常对照 |
| `hall: .chance 1 .types alpha alpha alpha alpha .chance`，紧接 EOF | 0 | 1 / 普通 |
| `hall: .chance 1e-50 .types alpha alpha alpha alpha` | 0 | `1e-50` / 普通 |

原生 [MashGuide](../../workspaces/historical-rule-review7-20260910/native_1404c90f0.txt)的字段边界掩码 `0x2000000100000201` 包含 NUL。`0x1404C9EE0`–`0x1404C9F35` 保留最后一个匹配字段并读取数值；`0x1404C9F3E` 把结果转成单精度。最后空字段的 strtod 结果为 0；`1e-50` 转为单精度后也为 0。

两项反例通过公共 `BattleEncounterCatalog.Load` 和 `GetSelectionCandidates` 验证，均错误进入普通候选列表。**本次没有编号偏移，也不应把 chance 为零的条目从编号表删除。** 权重为零与能否按明确编号手动放置是不同问题。

建议修复：让 chance 消费采用已验证的字段边界、最后字段、单精度与百分号处理；复用共享读取器时保留该消费者的默认值 0。补分类和普通候选的回归，不取消直接/Bridge 的索引校验。

## 不应回滚的现有规则

这六类问题来自尚未统一的旧读取、归一化和兜底逻辑，不能据此撤掉此前已验证的机制。当前仍应保留：清单挂载与消费路径分层、同路径覆盖优先级、各资源分别验证的重复定义规则、原生空遭遇占号、已知超体型行的跳过规则、无法证明索引时的保护，以及 Bridge 归属和地图实例/文件哈希检查。

本轮还检查了目录内容指纹、自动同步时的前后资源/存档状态复查及地图写入链路，没有取得新的可复现问题。该结论只限已检查的路径，不代表所有同步、地图或 Mod 组合均已完成实机验证。

上一轮 Release 构建与完整契约测试通过记录见[上一轮修复报告](encounter-consumer-fixes-2026-09-10.md)。本轮没有产品代码变更，未重复完整构建/套件；新增隔离断言通过表示问题已被复现，不表示这些六类缺陷已经修复。按协作规则，只读审核不触发完成复核者。

本轮跟踪文件仅新增本报告并更新目录索引；规则文档、源码与正式测试保持提交时状态。建议下轮优先修复两项 HP 问题，再处理身份分组、文件资格、引用置信度和战斗分类。
