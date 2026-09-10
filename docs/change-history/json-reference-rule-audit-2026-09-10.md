# 历史修改第八轮审核：JSON 字段、Buff 引用次数与资源来源，2026-09-10

已先提交上轮修复：`37d9167`，`fix: align HP precision and resource reference eligibility`。本轮在该提交上进行只读审核；没有修改产品代码、正式测试、真实存档、活动 Mod 或 Steam 配置，也没有新增依赖。仓库内新增本报告并更新记录索引；隔离探针、原生反汇编和结果位于忽略目录 `workspaces/historical-rule-review8-20260910/`。

本轮确认四类遗漏。前两类影响人物 HP 分析和候选写入，后两类影响资源来源与默认显示。它们与上轮修复有关，但不是上轮修复被撤回。

## 与刚完成的修复有何区别

| 范围 | 上轮已修复且本轮复测通过 | 本轮发现的另一层遗漏 |
| --- | --- | --- |
| 物品引用 | 损坏 JSON 的兜底字符串只提供不确定线索；loot/event 文件按消费路径与文件名过滤 | 合法 JSON 中的备注对象仍被递归视为有效引用；任务目录的普通 notes.json 仍能提供肯定来源 |
| Buff / HP | 同 ID 的完整 Buff 定义取值、数值转换、枚举/条件身份、候选 HP 的 DSON 往返 | 同一个 Buff 在一个怪癖的数组中出现两次仍被去重；同一 JSON 对象内重复字段仍取最后一个 |
| 事件 | 事件 ID 保留大小写、首条结果的选择、文件消费范围 | data.type、data.string_data 仍使用早期字符串归一化逻辑 |

本轮重新调用已提交的 `VerifyBuffPrecisionAndIdentityAsync` 和 `VerifyReferenceFileEligibility`，两组契约均通过。前者包括 HP 保存与反例校验，后者包括文件范围、缺失文件与损坏 JSON 的不确定传播。这些结果支持“旧修复仍然有效，但覆盖不全”，不支持“同一问题改完又复发”。

## 1. P1：Buff 引用数组去重，漏算 HP 修正次数

位置：[HeroClassCatalog.TextParsing.cs](../../src/DarkestDungeonSaveEditor.Core/HeroClassCatalog.TextParsing.cs)第 51–62 行；[怪癖读取](../../src/DarkestDungeonSaveEditor.Core/HeroClassCatalog.QuirkDefinitions.cs)第 283–285 行。

`ReadJsonStringArray` 同时服务于排斥列表、标签和 Buff 引用，并统一执行 `Distinct(StringComparer.Ordinal)`。但 Buff 数组不是集合。这里的原始去重来自 `1a03bb42`；`78fd748` 修正了身份比较方式，仍保留去重，`37d9167` 没有改动该 helper。

隔离职业基础 HP 为 20，测试 Buff 为始终生效的 max_hp 百分比修正：

| 输入 | 当前编辑器结果 | 按原生消费链应保留的修正 |
| --- | --- | --- |
| +25% Buff 引用一次 | HP 25，1 个修正 | +25% |
| 同一个 +25% Buff 引用两次 | HP 25，仍只有 1 个修正 | +50%，该对照应得到 HP 30 |
| 两个不同 ID、各 +25% 的 Buff | HP 30，2 个修正 | +50% |
| 同一个 -75% Buff 引用两次 | 允许生成，实际隔离 DSON 保存 HP 5 | -150%，应触发现有的合计 HP 安全校验 |
| 两个不同 ID、各 -75% 的 Buff | 拒绝生成，错误明确记录 -150% | -150% |

这不是“同 ID 的多份定义选哪一份”的问题。每次引用仍应解析到有效定义，但引用次数不能丢失。

原生证据链，固定 Windows x64 build 27890：

1. `0x1404DEC20`–`0x1404DEDA5` 按数组顺序处理每次 Buff 引用，解析成功后复制并追加一个 `0x1D0` 字节的 Buff 对象；没有按 ID 合并。
2. `0x1405C32D0` 从怪癖的 `+0xA8/+0xB0` 列表逐个取 Buff，通过 Hero 的虚函数 `+0x68` 添加。构造函数设置的 Hero vtable 为 `0x140E790F0`，该槽指向 `0x1405C12D0`。
3. `0x1405C12D0` 每次递增 Actor 的 Buff 实例编号，再由 `0x1405B8190` 追加一个 `0x1D8` 字节的实例；重复定义 ID 不会复用实例编号。
4. `0x140473570` 枚举全部实例，逐个检查条件并保留活动 Buff。`0x1404799A0` 逐个调用 `0x1404746F0` 累加修正。`combat_stat_multiply` 的分支为 `0x1404747BB`，在 `0x1404747D3` 执行 `addss amount`。其清理回调 `0x14047FD20` 针对 remove-if-inactive 与活动条件，不进行同 ID 去重。

本轮确认到了引用、实例添加和修正累加链；没有启动游戏测量上述新样例的最终画面，也不声称游戏在极端负生命情况下必然崩溃。负值例证明的是当前编辑器对同样的合计修正作出了不同的安全判断。

建议后续修复：Buff 引用单独使用保留顺序和重复项的读取入口，贯穿 MaxHpModifiers、预览及生成校验；不必删除排斥集合和标签集合的合理去重。

## 2. P1：同一 JSON 对象内重复字段的取值与原生不一致

位置：[HeroClassCatalog.TextParsing.cs](../../src/DarkestDungeonSaveEditor.Core/HeroClassCatalog.TextParsing.cs)第 13–55 行，尤其 `ReadJsonDouble` 第 25–31 行；[Buff amount 入口](../../src/DarkestDungeonSaveEditor.Core/HeroClassCatalog.QuirkDefinitions.cs)第 364、374–375 行。

这里使用 `JsonElement.TryGetProperty`，相同属性名出现多次时选择最后一个。上轮新增的 `ReadJsonFloat` 只转换数值，仍调用这个旧 helper。原生 JSON 字段查找选择第一个匹配成员。

同一个 Buff 对象，其他字段均保持合法：

| 对象中字段顺序 | 当前编辑器 | 原生字段读取 |
| --- | --- | --- |
| `"amount": -1, "amount": 0.2` | 采用 +20%，允许生成；隔离 DSON 保存 HP 24 | 采用 -100%，按现有 HP 安全规则应拒绝 |
| `"amount": 0.2, "amount": -1` | 采用 -100%，拒绝生成 | 采用 +20% |

原生 `0x14024CF60` 的 JSON 对象解析循环逐个压入成员，成员计数每次增加；`0x14024D114`–`0x14024D14C` 按原顺序复制整块成员，不做重名合并。查找函数 `0x14028E980` 从第一个成员向后比较名称，首次匹配立即返回。Buff 的 amount 消费在 `0x1404A3D3C`–`0x1404A3D66` 通过 `0x14028E900` 调用该查找，再进行 float 转换。

应明确区分三个规则：同路径文件的覆盖、多个资源对象的同 ID 解析、一个 JSON 对象内部的同名字段读取。这项结论不改变已经验证的“Buff 完整定义最后覆盖”，也不改变 `.darkest` 的最后字段规则。

建议后续修复：对照原生 JSON 消费函数统一有关字段 helper，保留第一个字段的类型与值；首字段类型错误也不能跳到后面的字段冒充有效。审核各入口的直接 `TryGetProperty`，避免只修 amount 而遗漏根数组、ID 和条件字段。此次可执行反例确认的是 Buff amount；其他字段需随修复补定向覆盖。

## 3. P2：物品来源仍存在“任意文件／任意对象即引用”的旧推断

位置：[文件发现](../../src/DarkestDungeonSaveEditor.Core/QuantityItemReferenceAnalyzer.SourceDiscovery.cs)第 173–182 行；[事件根对象](../../src/DarkestDungeonSaveEditor.Core/QuantityItemReferenceAnalyzer.RootParsing.cs)第 104–112 行；同文件第 176–228 行的通用递归。

隔离 Mod 的每个文件均列入 modfiles.txt。同一个 estate 物品 audit_token 没有其他来源、未在存档中出现：

| 文件与内容 | 当前编辑器结果 | 原生消费范围 |
| --- | --- | --- |
| `campaign/quest/notes.json` 为 `{}` | 小镇、副本均 SuspectedUnused | 对照 |
| 同一 notes.json 加入 `completion_reward: {type: estate, id: audit_token}` | 小镇 ConfirmedActive，默认显示 | 不符合任务资源文件的消费查询 |
| `campaign/quest/audit.quest.plot_quests.json` 中 `plot_quests: []`，另有 `notes: {type: estate, id: audit_token}` | 副本 ConfirmedActive，默认显示 | 该文件虽能加载，notes 不在 plot_quests 消费入口内 |
| `campaign/town_events/audit.town_events.events.json` 中 `events: []`，另有同样 notes | 小镇 ConfirmedActive，默认显示 | 事件加载只处理 events 数组 |

因此，仅仅“是合法 JSON”或“列在清单中”不能提供肯定的使用证据；对象的位置也必须属于游戏真正消费的结构。

原生任务加载函数 `0x140458980` 查询 `campaign/quest/` 与 `.*quest.plot_quests.json`（`0x1404589E8`–`0x1404589FF`），随后只取 `plot_quests` 数组。数组为空时 `0x140458B75`–`0x140458B79` 结束该文件处理。事件加载 `0x14046B7D0` 只取 `events`，空数组在 `0x14046B930`–`0x14046B933` 直接结束。

通用 JSON 递归来自 `d1d18f66`；当前路径的宽泛放行还明确保留了“其他独立资源沿用自己的规则”的分支。上轮只修正 loot/event 的文件消费范围以及损坏 JSON 的不确定传播，没有把全部 campaign 数据改为按结构消费。

影响的是未引用判断、小镇／副本来源说明和默认显示。以上反例没有证明数量分配代码错误，也没有修改真实物品数量。

建议后续修复：按实际消费文件种类和已知结构收集根引用；备注、未匹配的旧文件不产生 ConfirmedActive。尚不能解释的有效数据应保留明确的不确定性，不能依靠任意字段匹配证明来源。文件读取与内容刷新指纹需要同步调整。

## 4. P2：事件内部类型与职业引用仍被归一化

位置：[招募事件读取](../../src/DarkestDungeonSaveEditor.Core/HeroClassCatalog.SupportingDefinitions.cs)第 82–97 行；[字符串 helper](../../src/DarkestDungeonSaveEditor.Core/HeroClassCatalog.TextParsing.cs)第 19–23 行；[物品事件结果判断](../../src/DarkestDungeonSaveEditor.Core/QuantityItemReferenceAnalyzer.RootParsing.cs)第 187–198 行。

以下样例都使用符合原生规则的事件文件名：

| data 内容 | 当前编辑器 | 原生身份判断 |
| --- | --- | --- |
| type 为 `bonus_recruit`，职业为 `local_hero` | 识别招募来源 | 正向对照 |
| type 为 `BONUS_RECRUIT` 或 ` bonus_recruit ` | 仍识别成 local_hero 的招募事件 | 不匹配合法事件类型 |
| type 正确，职业为 ` local_hero ` | 去空格后绑定 local_hero | 原始字符串哈希不同，不绑定该职业 |
| type 正确，职业为 `local_hero\u0000ignored` | 保留整个字符串，未绑定 local_hero | 原生哈希到 NUL 截止，匹配 local_hero |
| 物品事件 type 为 `BONUS_CURRENCY` | 仍把 audit_token 标为小镇 ConfirmedActive | 不匹配 `bonus_currency` 类型 |

原生事件解析 `0x14046D426`–`0x14046D4B6` 对类型缓冲区按原字节计算哈希并查询枚举，失败记录 Invalid type；不会去空格或忽略大小写。`0x14046D725`–`0x14046D741` 对原始 string_data 计算到 NUL 为止的哈希。招募执行 `0x14058B575`–`0x14058B59A` 将它与职业哈希比较，随后按该哈希生成英雄。

招募 payload 的 Trim/OrdinalIgnoreCase 来自 `1a03bb42`，物品事件分支来自 `d1d18f66`。上轮修复的是外层事件 ID 的候选分组，不是这些内部字段。

建议后续修复：事件类型使用对应原生缓冲区与枚举规则；职业绑定使用实际消费的原始字符串哈希规则。不要把类型的 64 字节复制限制机械套到完整 string_data 的哈希上。本轮结果只描述招募来源和物品来源的识别，不代表模拟了全部事件触发条件。

## 验证记录与边界

- 使用已提交 Release Core，SHA-256 为 `0FFE308EB9B79BB70CEF4D840553BDEA15A9782596C495F8AE19FE616A4C6264`。隔离探针引用现有 Core/ContractTests DLL，没有重新编译或替换产品程序集。
- [探针源码](../../workspaces/historical-rule-review8-20260910/Program.cs)、[最终日志](../../workspaces/historical-rule-review8-20260910/probe-v3.log)、[18 组结果](../../workspaces/historical-rule-review8-20260910/runs/15ff1db38a7a4cea9aea4120c22834c1/results.json)、[验证摘要](../../workspaces/historical-rule-review8-20260910/verification-summary.json)。18 组包括 7 个 Buff/JSON 样例、5 个招募样例、6 个物品引用样例；不把已通过的上轮契约组重复计入样例总数。
- 首次探针编译因使用了错误模型属性名 HeroClassId 失败，记录于 `probe.log`；改为实际属性 HeroClass 后 v2 通过。v3 将数值对照改为可精确表示的 25%/-75%，并加入上轮两组回归，退出码 0。`verify_results.py` 对上述实际结果、程序集哈希和原生 Buff 分支表再次断言，退出码 0。这里的通过代表稳定复现和证据核对，不能解释为四类产品缺陷已修复。
- 原生依据为固定游戏 `Darkest.exe`，SHA-256 `35E5A653279992564809FF8406FEBD5A02A7D6961044781B1296B38A7096F59B`。本轮反汇编位于 `workspaces/historical-rule-review8-20260910/native_*.txt`；Buff/事件原有完整解析证据仍在第七轮目录。全部为静态分析，没有新一轮实机观察，也没有对 profile_1 作这些实验。
- 同时复查了战斗维护的内容指纹、重新编号与组合匹配链，目前未在所检查路径确认新的回退错误；不据此宣称所有战斗和 Mod 组合都已穷尽验证。本轮没有删除这些保护，也没有调整覆盖顺序、数量分配或地图历史。
- 按只读审核边界，本轮未触发实现完成复核。上述四类问题保持待修复；报告与索引未另行提交。
