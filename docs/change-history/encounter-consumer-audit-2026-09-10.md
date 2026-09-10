# 历史修改第六轮审核：战斗记录与资源消费，2026-09-10

按用户要求先提交上一轮完成的人物身份与地图资源消费修复：`78fd748`，`fix: align hero identities and curio resource consumption`。提交包含 28 个文件，提交后工作区干净。随后进行本轮只读审核。

确认六类遗留问题：战斗记录边界、怪物槽字符串、通用记录读取器、升级购买身份、漫游分类、物品引用识别。前两类可导致战斗编号错误；第三、四类可接受或写出与游戏身份不一致的数据；后两类主要影响分类、搜索与默认显示。**本轮没有修复这六类问题。** 隔离反例也不代表已经确认当前 `profile_1` 存在同样输入或受到了影响。

## 验证依据与边界

- 产品代码固定在提交 `78fd748`。探针直接引用既有 Release Core 与契约测试程序集，复用测试夹具，不重建产品、不添加依赖。
- 所有假游戏目录、Mod、地图和升级存档均位于已忽略的 `workspaces/`。本轮没有启动游戏，没有修改真实档案、活动 Mod 或 Steam 状态。
- 游戏侧依据为本机 Windows x64 build 27890 二进制及此前研究。SHA-256：`35E5A653279992564809FF8406FEBD5A02A7D6961044781B1296B38A7096F59B`。本轮新结论来自静态调用路径、字符串复制和查表分析，**没有把它们写成新一轮实机观测**。
- [探针项目](../../workspaces/historical-rule-review6-20260910/ReviewProbe.csproj)、[探针代码](../../workspaces/historical-rule-review6-20260910/Program.cs)、[最终 29 个样例结果](../../workspaces/historical-rule-review6-20260910/runs/85aae5e33d6e4a57b9b0caee9b38d943/results.json)、[执行日志](../../workspaces/historical-rule-review6-20260910/probe-final.log)、[退出码 0](../../workspaces/historical-rule-review6-20260910/probe-final.exitcode)均已保留。结果内的 `expectedNative*` 是按已核对原生代码得出的预期，不是探针启动游戏采集的数值。
- 29 个样例包括：走廊、房间、Boss 各六个记录样例；两个怪物槽样例；一个漫游字段样例；一次完整 Bridge 追加与假地图提交；一次升级购买 DSON 往返；四个 CSV 引用样例；两个通用记录头样例。其中五个为正常对照。
- [结果校验脚本](../../workspaces/historical-rule-review6-20260910/verify_results.py)核对全部观察结果、DSON 往返、游戏哈希和三个输出目录的 Core DLL。Core SHA-256 均为 `D3A20AB31834098626FA495B9BB63645628824596CDFBB627396BBAA0ACD3BC6`。
- 探针编译与执行退出码为 0，复现断言通过。这表示缺陷被稳定复现，**不表示产品已经通过修复验收**。最终怪物槽反例只使用体型 1、4 的怪物，通过组合总宽度超过四格检验计数差异，不依赖单个怪物体型大于四的额外假设。

## 1. P1：战斗表仍按物理行读取，可静默写错直接战斗和 Bridge 编号

位置：[BattleEncounterCatalog.cs](../../src/DarkestDungeonSaveEditor.Core/BattleEncounterCatalog.cs)，第 621、644–688 行；[BattleEncounterCatalog.RuntimeOrder.cs](../../src/DarkestDungeonSaveEditor.Core/BattleEncounterCatalog.RuntimeOrder.cs)，第 47–51 行。

当前入口先 `File.ReadAllLines`，每一物理行最多生成一条组合；声明头又经 `ToLowerInvariant()`。后续补入的“最后一个 `.types`”“前四个槽”“空组合占号”仍包在这层旧解析中。`git blame` 显示物理行、头部大小写和主要分词框架来自 `a5f38b88`；`730c01eb` 等后续修正没有整体替换这一层。

例如有效表内容为：

```text
hall: .chance 1 .types alpha hall: .chance 1 .types bravo
hall: .chance 1 .types charlie
```

原生记录读取器以声明边界切分，得到三条组合：`alpha=0`、`bravo=1`、`charlie=2`。编辑器取第一物理行的最后一个 `.types`，得到 `bravo=0`、`charlie=1`，两条都显示可直接放置且通过写入前复查；Bridge 也认为下一条是 2。

在同一假档案中，探针实际调用 `EnsureEncounterAsync` 创建其他地区组合，再调用 `PreparePlaceBattleAsync`、`CommitAsync` 并重新解码地图：**Bridge 分配 2，地图确实保存 `mash_index=2`；依据原生记录序列，新增组合的位置应是 3。** 因此这是已经到达存档写入的错误。按这套表进入游戏会查到旧 `charlie` 位置，是原生查表规则下的后果推断，本轮未实际进入游戏触发。

三个战斗类型分别验证了以下边界：

| 输入形态 | 原生该类型的记录数 | 当前编辑器结果 |
| --- | ---: | --- |
| 三条声明分别独占一行 | 3 | 正常对照，编号 0、1、2，追加 3 |
| 两条声明共一行，再跟一条声明 | 3 | 只计两条，直接复查与追加均放行 |
| 首条声明头、字段分两行，后接正常声明 | 2 | 漏掉首条，并阻止该类型追加 |
| 块注释包含一条假声明，外面一条正常声明 | 1 | 把注释中的声明计入，复查与追加放行 |
| 正常声明后出现 NUL，再跟假声明 | 1 | 继续读 NUL 后内容并计数，复查与追加放行 |
| 大写声明头后跟小写正常声明 | 1 | 把大写头也计入对应小写类型 |

大写头需要精确表述：原生不是一概丢弃它，而是未匹配 `hall:`、`room:`、`boss:` 后转入其他命名分支；它不属于本例所统计的小写索引类型。编辑器把它改成小写才产生这个计数差异。

原生依据：[MashGuide 入口](../../workspaces/historical-rule-review6-20260910/native_1404c90f0.txt)在 `0x1404C92CB`、`0x1404CA776` 调用 `0x14028E1C0`，在 `0x1404C92EB`–`0x1404C9391` 精确匹配三种头，未知头分支为 `0x1404C9E0C`。[记录扫描](../../workspaces/historical-rule-review6-20260910/native_14028e210.txt)不以物理换行结束记录；[NUL 处理](../../workspaces/historical-rule-review6-20260910/native_14028e1c0.txt)与[注释预处理](../../workspaces/historical-rule-review6-20260910/native_14028dea0.txt)分别约束读取终点和有效内容。

建议修复：统一发现、目录、直接复查、Bridge 追加所使用的逻辑记录与字段解析，同时保留原生头部大小写。不能只改目录显示，也不能用取消预检掩盖编号不一致。[Bridge 来源键](../../src/DarkestDungeonSaveEditor.Core/ManagedBattleEncounterBridgeService.Classification.cs)第 5–6 行目前只有文件路径和物理行号；支持一行多声明时还必须增加记录序号或字节位置，防止多条来源再次合并。

## 2. P2：怪物槽没有应用原生字符串上限，还会在点号开头的 ID 前提前结束

位置：[BattleEncounterCatalog.cs](../../src/DarkestDungeonSaveEditor.Core/BattleEncounterCatalog.cs)，第 682–688 行；[运行时行数检查](../../src/DarkestDungeonSaveEditor.Core/BattleEncounterCatalog.RuntimeOrder.cs)，第 73–103 行。

当前只限制前四个 token，没有先将每个 token 按原生槽复制规则限制到 31 字节；还沿用 `a5f38b88` 的 `TakeWhile(!StartsWith("."))`，把点号开头的内容当成下一字段。怪物定义目录的 ID 限制与遭遇槽的 32 字节缓冲区是不同层次，前者不能代替后者。

**后续修复复验更正：** 下述第 2 个点号 ID 反例，必须由 `modfiles.txt` 明确列出其定义才满足原生发现前提。原探针只把 `.oversize_A` 放在普通目录里，目录枚举会跳过点号组件，因此不能据此断言该次原生总体型为 5。正式回归已改用清单来源并验证截断/点号槽位规则；第 1 个长 ID 反例不依赖这个更正。原探针输出保留为历史记录，修复验收见 [本轮修复记录](encounter-consumer-fixes-2026-09-10.md)。

两个隔离反例均错误保留了应跳过的第一条组合，并将后面的 `bravo` 分成 1、下一追加位置分成 2；原生应只有 `bravo=0`、追加位置 1。当前目录 Issues 为空，直接复查和追加检查都接受：

1. 定义一个长度 31 的 ASCII 怪物 ID：29 个 `q` 加 `_A`，体型 4；再定义 `该 ID + _extra_A`，体型 1。第一条组合写 `alpha`（体型 1）加这个长 ID。原生槽把长 ID 截为前一个 ID，总宽度为 5，因此不占号；编辑器按完整长 ID 计算宽度 2，错误保留。
2. 第一条组合写 `.types alpha .oversize_A`，两只怪物体型分别为 1、4。点号是实际 ID 的首字符。原生读取并查找第二个槽，总宽度 5；编辑器在 `.oversize_A` 前停止，仅保留 `alpha`，因此错误计数。

原生依据：[槽读取](../../workspaces/historical-rule-review6-20260910/native_1404c90f0.txt)在 `0x1404CA0DC`–`0x1404CA0FC` 将复制目标长度限制到 `0x20`，最多保留 31 字节与终止符，随后同样处理其余槽；[AddMashEntry](../../workspaces/historical-rule-review6-20260910/native_1404cb740.txt)对槽中原始字符计算哈希、查怪物、累计体型，在 `0x1404CB909`–`0x1404CB90C` 跳过总宽度大于 4 的行，没有把点号作为字段终止条件。

建议修复：先得到真实四个槽的内容，再判断怪物身份、体型和占号。既有“已知总体型超过四格不占号”“空组合占号但不能放置”“无法确定时不能猜编号”仍有意义，不应直接删除这些保护。本轮可执行反例使用 ASCII，不宣称已经逐项覆盖 UTF-8 被截断后的所有异常情况。

## 3. P2：共用 darkest 读取器跳过非法开头，继续认可原生不会登记的定义

位置：[NativeDarkestReader.cs](../../src/DarkestDungeonSaveEditor.Core/NativeDarkestReader.cs)，第 9–12、32–36 行；[物品调用端](../../src/DarkestDungeonSaveEditor.Core/QuantityItemCatalog.Definitions.cs)，第 201 行。正则入口来自 `4e1db6b3`。

当前 `EntryRegex().Matches(...)` 会在全文搜索下一处看起来合法的声明，遇到前面的非声明文本仍可跳过去。原生 `ReadNextLine` 则在当前游标处读字母/下划线声明头，紧接着必须是冒号，失败就返回 false，由资源加载循环结束本文件读取。

隔离物品文件：

```text
BROKEN
inventory_item: .type estate .id header_token .base_stack_limit 3
```

原生在 `BROKEN` 后遇到换行而非冒号，不会继续登记本文件后面的 `header_token`。编辑器仍生成正常、无冲突、非“仅存档”条目，`QuantityItemSaveEditor.SetAmount` 可以把它的数量写成 1。去掉 `BROKEN` 的正常对照同样读取到一个定义。

这里验证的是目录加纯内存数量修改，没有在本样例中调用真实保存服务或做 DSON 落盘。其他资源复用该读取器，但本轮只以物品作为端到端反例，不将所有调用者都计为已逐一复现。

原生依据：[读取器](../../workspaces/historical-rule-review6-20260910/native_14028e210.txt)的 `0x14028E239`–`0x14028E25C` 检查冒号并在失败时返回 false；[物品加载器](../../workspaces/historical-rule-review6-20260910/native_1404c8490.txt)的 `0x1404C84FE`–`0x1404C8505`、`0x1404C8A08`–`0x1404C8A0F` 按该返回值控制加载循环。

建议修复：按游标推进实现记录边界及终止规则，不能继续用全局正则搜索绕过错误。修复第 1 项时也应先修正这个共享入口，不能认为换用当前 `NativeDarkestReader` 就已经完全一致。

## 4. P2：升级树读取身份已正确，保存购买记录时仍被 Trim 成另一个哈希

位置：[StagecoachHeroSaveEditor.cs](../../src/DarkestDungeonSaveEditor.Core/StagecoachHeroSaveEditor.cs)，第 208–210 行。此处理在仓库历史边界 `7b9a67f` 已存在。

构造技能 ID `local_skill `，末尾有一个空格，对应升级树为 `local_hero.local_skill `。当前目录与生成器正确保留了技能、树的完整 ID；生成 level 1 人物时，计划包含该树的 `0`、`1` 两条购买记录。

保存端却执行 `purchase.TreeId.Trim()` 后再算哈希：

| 身份 | 有符号哈希 | 实际保存的对应购买数 |
| --- | ---: | ---: |
| 原始 `local_hero.local_skill ` | 900047903 | 0 |
| 被改写的 `local_hero.local_skill` | -1036500509 | 2 |

[保存前 JSON](../../workspaces/historical-rule-review6-20260910/runs/85aae5e33d6e4a57b9b0caee9b38d943/upgrade.decoded.json)与[DSON 编码再解码结果](../../workspaces/historical-rule-review6-20260910/runs/85aae5e33d6e4a57b9b0caee9b38d943/upgrade.roundtrip.json)一致，说明错误已落在实际持久化字段，不是预览字符串问题。按真实树 ID 查询时无法找到这两条预期购买，是身份查找规则下的后果；本轮没有进游戏实际招募该人物。

原生技能 ID 读取入口见[反汇编](../../workspaces/historical-rule-review6-20260910/native_1404c5140.txt) `0x1404C5193`–`0x1404C51A4`；其调用的[字符串读取函数](../../workspaces/historical-rule-review6-20260910/native_14036c950.txt)保留引号内空格。本例 ID 小于其缓冲区上限。升级树原始身份规则沿用[既有研究](../resource-duplicate-semantics.md)。

建议修复：从已解析的技能/树 ID、购买去重到 `tree_id` 哈希序列化始终使用同一原始身份。保留真实哈希碰撞和重复购买检查。这不推翻已经验证的“同一个树 ID 取最后匹配树”，问题发生在后续写入阶段。

## 5. P3：漫游字段仍取第一项，显式清空后仍被分类为漫游遭遇

位置：[BattleEncounterCatalog.cs](../../src/DarkestDungeonSaveEditor.Core/BattleEncounterCatalog.cs)，第 709–715 行；分类位于同文件第 558–574 行。旧 `Array.FindIndex` 和忽略字段大小写处理来自 `a5f38b88`。

同一声明依次出现 `.random_dungeon_roaming_id shambler` 和 `.random_dungeon_roaming_id ""`，后面 `.types alpha`。原生用最后一次精确字段匹配读取空字符串；当前编辑器仍取第一项 `shambler`，最终分类是 `RoamingEncounter`（枚举值 3）。本例没有 Boss 怪物标签，不应称为已复现“漫游 Boss”。

这会使普通组合落入错误分类，影响按类型搜索和选择。该样例编号仍是 0、追加仍是 1，因此不能将这个反例本身说成编号错位。

原生依据：[MashGuide](../../workspaces/historical-rule-review6-20260910/native_1404c90f0.txt)的 `0x1404CA714`–`0x1404CA72C` 调用[最后字符串字段读取](../../workspaces/historical-rule-review6-20260910/native_14036c950.txt)。建议使用同一个精确字段与显式空值规则，删除这里的 first-match 特例。

## 6. P3：物品引用扫描仍将 CSV 备注当成有效掉落引用

位置：[QuantityItemReferenceAnalyzer.RootParsing.cs](../../src/DarkestDungeonSaveEditor.Core/QuantityItemReferenceAnalyzer.RootParsing.cs)，第 254–273 行；[SourceDiscovery](../../src/DarkestDungeonSaveEditor.Core/QuantityItemReferenceAnalyzer.SourceDiscovery.cs)，第 147–171 行。整行包含 `Loot` 后扫描所有列的启发式来自 `d1d18f66`，后续身份修复保留了这一层。

当前入口允许 `curios/` 中多种 CSV 文件进入引用分析，只要一物理行含 `Loot` 字样，就按逗号拆整行，在任意列发现已知掉落表 ID 后认定它是活动引用，没有核对游戏消费的文件后缀、类型库块和结果字段。

四个隔离 Mod 均定义物品 `audit_token` 与包含它的掉落表 `audit_loot`，没有其他活动来源，且物品不是补给。全部被编辑器判为 `ConfirmedActive`、没有 Issues：

| CSV 内容 | 应如何解释 |
| --- | --- |
| 正规 `curio_type_library.csv`，有 `ID STRING` 块与实际 `Loot` 结果 | 正常对照，存在有效引用 |
| `curios/notes.csv` 仅写 `Author notes about Loot,audit_loot` | 文件不属于游戏类型库消费入口，不能证明有效引用 |
| 正规文件名，但只有上面的说明文本，没有 `ID STRING` 块 | 没有创建类型块，不能证明有效引用 |
| 正规块，实际结果为 `Nothing`；后面的备注列写 `audit_loot`、`Loot notes only` | 备注不是该结果使用的掉落表引用 |

原生文件入口只枚举对应后缀，见[前轮类型库入口](../../workspaces/historical-rule-review5-20260909/native_1404d8770.txt)，`0x1404D8A22` 使用 `.*curios/.*curio_type_library.csv`；[块读取](../../workspaces/historical-rule-review6-20260910/native_1404d8e0f.txt)在 `0x1404D8E99`–`0x1404D8EB5` 检查第三列 `ID STRING`。这不是扫描任意文本出现某个表名的机制。

[Models.cs](../../src/DarkestDungeonSaveEditor.Core/Models.cs)第 294–295 行默认隐藏未在存档出现的 `SuspectedUnused` 物品。错误标为 `ConfirmedActive` 会把本应存疑的物品放入默认列表，并给出错误的使用证据。本例物品定义本身存在，不据此宣称一定损坏存档或数量算法错误。

建议修复：引用根应按实际消费者的文件与字段识别；不能证实的线索只能保留为不确定信息。上一轮修正的是地图奇物目录入口，这个独立的物品引用入口尚未同步。应修正扫描依据，不应直接删除全部物品引用判断，也不应因此为饰品强加同样的未引用过滤。

## 本轮没有推翻的规则与交付状态

- Mod 清单约束、同路径文件覆盖、各资源已验证的重复 ID 规则继续保留。本轮错误发生在文件被选中之后的消费、身份或引用阶段，不能通过取消清单、全局统一 first-match/last-match 来解决。
- 空组合占号与不可放置的区分、真实未知体型和哈希碰撞保护、Bridge 所有权与保存并发保护继续有必要。修改过多次不是删除这些检查的证据。
- 没有新增证据说明自动同步调度、强制返回、删除操作、日志系统本身出现另一个独立缺陷。它们若消费错误目录可能受间接影响，但本轮没有将此扩展为已复现的独立问题。
- 最终产品代码和正式测试保持 `78fd748` 不变，仅新增本审核记录及目录入口。探针留在已忽略的工作目录；本轮文档尚未另行提交。
- 上一轮构建/回归结果已保存在[修复记录](hero-curio-rules-fix-2026-09-09.md)。本轮没有产品改动，未重复完整产品回归；执行了上述有针对性的只读分析及隔离反例。
- 结果校验通过，报告及目录中的 67 个本地链接均存在，`git diff --check` 通过；Git 仅提示既有 LF/CRLF 转换策略，没有修改换行配置。由于当前任务是只读审核、没有产品代码差异，按协作规则不触发实现完成复核者。

后续优先修正战斗记录和槽解析，并把共用读取器的终止规则一起纳入；再贯通升级购买身份、漫游字段与物品引用入口。六类问题目前均仍存在，不能将本记录视为已完成修复。
