# JSON 字段、Buff 引用次数与资源来源修复，2026-09-10

本轮落实用户确认的[第八轮审核四项问题](json-reference-rule-audit-2026-09-10.md)，基于 `37d9167`。本轮未另行提交，没有改动真实存档、活动 Mod、游戏或 Steam 配置，没有新增依赖。

## 修改内容

| 问题 | 实现与影响 |
| --- | --- |
| 相同 Buff 引用被去重 | `HeroClassCatalog.TextParsing.cs` 将引用列表与集合分开，`QuirkDefinitions.cs` 保留每次 Buff 引用。HP 预览、生成校验和候选保存都使用这些修正；排斥和标签集合仍合理去重。基础 HP 20 加两次相同 +25% 得到 30；两次 -75% 不再漏过非正 HP 校验。 |
| 同一 JSON 对象内重复字段取最后一个 | 新增 `NativeJsonReader.cs`，人物 Buff/怪癖/进化/露营/升级/经验阈值/事件及物品引用入口使用首个精确成员；首项类型错误不取后项补救。进化签名也按精确成员首值记录。这与不同对象的同 ID 规则、`.darkest` 最后字段规则分开。 |
| 任意 JSON 文件或备注对象被当成使用来源 | `NativeResourceFileRules.cs` 增加对应原生文件查询；`QuantityItemReferenceAnalyzer.JsonRoots.cs` 分别读取配给、庄园货币、剧情任务奖励和初始道具、任务生成奖励、目标初始道具、地区建筑、升级消耗和事件结果。删除任意对象递归及依靠父字段名猜测场景的逻辑。已加载但尚未确认消费结构的线索只进入不确定证据。 |
| 事件 payload 被去空格、忽略大小写 | 类型按对应 63 字节有效载荷与哈希判断，`string_data` 保留原始字节至 NUL；职业后续分组也按原生哈希绑定；招募数量按首个 JSON 数字及原生 float 转换。物品事件使用相同类型规则。 |

文件发现、JSON 解析、缺失文件诊断共用消费范围。`ProfileCatalogContentFingerprint` 已经覆盖全部相关目录中的 `*json`，本次明确说明这一范围，增加任务/配给非字面 `Xjson` 名称的刷新测试；没有缩窄其他目录依赖的共同刷新范围。同路径提供者优先级、Mod 清单准备和读取方式不变。

保存格式与事务、物品/饰品数量分配、战斗编号和 Bridge、地图修改历史及自动清理逻辑没有改变；全套测试继续覆盖这些流程。

## 原生依据与验证边界

固定版本仍为 Windows x64 build 27890，游戏 EXE SHA-256 为 `35E5A653279992564809FF8406FEBD5A02A7D6961044781B1296B38A7096F59B`。本轮新增反汇编和原版/DLC 文件结构摘录在 `workspaces/historical-rule-review8-20260910/`，当前有效规则已写入[资源语义第 15 节](../resource-duplicate-semantics.md#15-json-members-buff-occurrences-and-structured-item-roots-2026-09-10)和[内容与存档规则](../content-save-rules.md)。

主要入口包括 JSON 首成员 `0x14028E980`，Buff 逐引用复制/实例化/累加链（审查报告列出），配给 `0x1404503C0`、庄园 `0x14044CDA0`、任务类型 `0x140455680`、剧情任务 `0x140458980`、任务生成 `0x14045CC90`、地区建筑 `0x140559E60`，以及事件类型/引用哈希 `0x14046D426`、`0x14046D725`。

这次采用静态原生证据和工作区隔离执行，没有新增实机测试。尚未确认的建筑专用 data/requirements、目标专用 data、任务 modifier/restriction/exit-penalty 结构保留 `AnalysisIncomplete` 线索，不能声称已经完整模拟全部游戏资源及触发条件。合法来源表示存在消费路径，不保证当前档案一定触发对应任务、建筑或事件。

## 回归样例修正

旧夹具中的几个“正例”依赖被删除的错误递归，并非游戏有效数据：

- `DistrictSupplyBuffData.loot_table_code` 改为实际的 `item_type/item_name/target_inventory/range_min/range_max`；嵌套掉落遍历另用真实英雄 `extra_battle_loot` 根验证，保留来源链断言。
- `campaign/town/provision/identity.json` 改为 `campaign/provision/identity.provision.json` 的商店配给列表；原大小写、空格身份与保存断言保留。
- `test.plot_quests.json` 补为符合任务查询的 `test.quest.plot_quests.json`；奖励/配给交叉场景断言保留。
- 随意命名的 raid/camping JSON 缺失或损坏不再让全目录不确定，改作排除负例；真正有效事件、掉落、地区建筑文件的损坏/缺失仍验证 `AnalysisIncomplete`。缺失地区建筑样例改为 `.districts.json`。
- 事件顶层任意 `cost` 对象改为未引用负例；首条有效事件结果及 `event_cost` 正例继续保留。

新增两组契约覆盖了重复 Buff 预览与实际 DSON HP、危险负值拒绝、JSON 首字段类型/数组/ID/数值/旗标/进化/升级、招募绑定，以及 68 个物品 JSON 结构与场景组合。重复定义的既有 first/last 规则、空遭遇占号、Bridge 和地图写入契约均继续运行。

## 验证结果

- Release 解决方案编译成功，0 警告、0 错误。
- `--semantics` 退出码 0；产物：`workspaces/contract_tests/20260910_064555_129_9cc645c7d97745019200e2a8fb70fb86/`。
- 完整契约套件退出码 0；[完整日志](../../workspaces/historical-rule-review8-20260910/fix-contracts.log)，产物：`workspaces/contract_tests/20260910_064635_853_a34f00021c104ef388314dc2e3e35d70/`。
- [验收与程序集摘要](../../workspaces/historical-rule-review8-20260910/fix-acceptance-summary.json)记录退出码、PASS/FAIL/SKIP 汇总及 Core/App/ContractTests 三处 Core DLL 的哈希。
- `git diff --check` 通过。

过程中一次新增测试编译因原始插值字符串的大括号写法失败；随后误用旧测试程序集运行时在旧配给夹具的身份断言停止。这两次不计为验收通过。修正字符串、替换无效夹具并成功重编译后，以上语义组和完整套件均以新程序集通过。

完整套件共 34 条 PASS 汇总，0 FAIL、0 SKIP；这是汇总行数，不是断言数。此时 Core 哈希为 `301F7266054B832A653041C57A5680C56A86F8C4286EACB5A1B71C3792627E5F`。

之后补入三行外层事件 ID 的 NUL 截止处理，使人物与物品的首条事件结果键一致，并新增“首条 NUL 别名空结果仍占位”的回归。其原生依据为 `0x14046BC50`–`0x14046BC71`。重编译一度因正在运行的编辑器占用 DLL 失败（`fix-build-final.log`，文件名不代表成功）；按用户此前授权，只关闭了该项目的编辑器进程，再次编译[通过](../../workspaces/historical-rule-review8-20260910/fix-build-unlocked.log)，0 警告、0 错误。

用户此时已启动游戏，[最新版语义组重跑](../../workspaces/historical-rule-review8-20260910/fix-semantics-final.log)在 `EnsureGameIsNotRunning` 事务保护处正确中止，退出码 1。没有关闭游戏、削弱保护或把这次计为通过。替代验证使用[独立探针](../../workspaces/historical-rule-review8-20260910/final-probe/Program.cs)，只在新建临时夹具上执行两组新增读取/序列化契约（含最后的 NUL 反例、68 个物品场景和实际 DSON HP），[退出码 0](../../workspaces/historical-rule-review8-20260910/fix-targeted-final.log)。它不重复运行受游戏进程保护的事务测试；这些事务的通过记录来自上面的完整套件。

该阶段 Core/App/ContractTests 三处 Core DLL 哈希均为 `F64A86F7D3A3501A640E54A496CF0CD2D7141E1390FE1FE3C57778689BBA1741`；验收摘要区分完整套件时的程序集和最后的针对性验证。待游戏关闭后如需再次运行完整套件，可执行 `dotnet run --project tests/DarkestDungeonSaveEditor.ContractTests -c Release --no-build -- .`。

独立只读复核完成，发现一处不确定引用恢复遗漏：合法 JSON 中使用 Unicode 转义的物品 ID／掉落表 code 未解码即匹配，会误判为疑似未引用。已改为提取首成员规则下的解码字符串和成员名，仅生成不确定证据，没有把未知结构提升为有效来源。新增两个转义反例后共 70 个 JSON 场景，[针对性验证通过](../../workspaces/historical-rule-review8-20260910/fix-targeted-reviewed.log)。复核没有提出其他实质问题。

用户关闭游戏后，再次运行包含该修复的[完整契约套件](../../workspaces/historical-rule-review8-20260910/fix-contracts-reviewed.log)，退出码 0、34 条 PASS 汇总、0 FAIL、0 SKIP。产物 `workspaces/contract_tests/20260910_071447_603_ea943400c956442faf28adc6d5bc6275/`，Core SHA-256 为 `DFFBB7E6EFC5ACA70162C4005720197996857022C169BF4C776AEA20633D0A53`。至此本记录中的四项修复完成验证；后续庭院存档路径修复另行记录。
