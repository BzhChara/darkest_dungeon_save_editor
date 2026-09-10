# 战斗记录与资源消费规则修复，2026-09-10

按用户“确认，进行修改”的要求，修复 [第六轮审核](encounter-consumer-audit-2026-09-10.md) 的六项问题。基线为 `78fd748`。本轮不提交 Git，不修改真实档案、活动 Mod 或 Steam 状态。

## 实现范围

| 问题 | 修复与影响 |
| --- | --- |
| 战斗按物理行解析 | `BattleEncounterCatalog` 改用共同逻辑记录读取器；同一行多声明、跨行声明、注释、NUL 和大小写在发现、目录、直接预检、Bridge 追加及维护中一致。增加来源记录序号，避免同一行的不同来源被合并。 |
| 怪物槽字符串 | 前四个原始槽各按 31 UTF-8 字节读取，之后才解析怪物、体型和编号。空槽仍占位置，点号不再终止列表，第五槽仍忽略；UTF-8 截断无法确认时保留限制。 |
| 共用读取器越过非法开头 | `NativeDarkestReader` 按当前游标读取，非法当前声明头结束本文件读取；删除全文寻找下一合法头的旧正则和不再使用的注释掩码函数。 |
| 升级购买身份被裁剪 | `StagecoachHeroSaveEditor` 用生成计划的原始树 ID 计算哈希，保留原始需求代码。带尾部空格的树与无空格树不会串写。 |
| 游荡分类取错字段 | 使用最后一个大小写精确匹配的 roaming 字段，显式空值清空；目录分类与 Bridge 来源分类使用同一来源记录身份。 |
| 奇物说明被当作物品引用 | `NativeCurioCsvReader` 的类型块同时服务地图目录和数量引用；只读取有效类型库中的互动输入与 Loot 结果列，排除笔记、说明列、块外数据、零权重默认结果。 |

读取入口仍为现有清单和有效文件覆盖模型，同路径 Mod 优先级与各资源重复 ID 规则没有改动。数量/堆叠存档形状、饰品流程、人物怪癖选择规则、强制回城和自动同步周期没有新增业务规则。

## 连带修正：Bridge 生成文本

原生成器把 `.limit 1 .can_be_ambush false` 放在 `.types` 后面。原生不足四只怪物时会继续把这些参数当作怪物槽。新建 `EncounterBridgeRow`，安装、维护重建及诊断探针统一把参数写在 `.types` 前；包含空格的 ID 加引号，不能无损表示的 ID 明确拒绝。

旧版第 4 格式包中，若实际槽位与托管清单不同，仅在包校验通过、文件哈希匹配、整个文件又与旧生成器文本完全一致、记录数和序号吻合时，按用户既有政策删除该错误记录。清理前仍检查当前副本成功放置历史；失去归属或外部改动的包暂缓处理。受影响记录触发清空当前仍存在的编辑器战斗，包括直接放置的本地区战斗；原生地图区域与其他内容不清理。正确的四怪物组合可继续保留。不是给全局解析器加入忽略旧参数的特例。

## 验证依据与边界

依据仍为 Windows x64 build 27890，游戏 SHA-256 `35E5A653279992564809FF8406FEBD5A02A7D6961044781B1296B38A7096F59B`。本轮使用静态原生调用链与隔离可执行测试，不冒充新一轮实机战斗验证。

新增原生列消费证据保留于忽略目录 `workspaces/historical-rule-review6-20260910/`：`native_1404d95b0.txt`、`native_1404d93a0.txt`、`native_1404d9130.txt`、`native_1404da1c0.txt`、`native_1404a8050.txt`。Loot 将代码与次数组合到一个 64 字节缓冲区，分隔符地址 `0x140E7BE68` 为 `&`。RTTI `CurioInteractionLootResult` 对应虚表 `0x140E7BE78`，其构造分支按 `&` 分词，忽略空 token，再把每个代码复制到 31 字节加终止符。超过可确认组合容量、整数溢出或无效 UTF-8 时记录“分析未完成”，不把被截断前的代码肯定为有效引用。并未模拟全部互动触发条件或不安全原生缓冲区行为。

正式测试：

- `EncounterRecordContractTests`：三种战斗类型各八种声明布局；同一行相同组合仍有独立来源；伪造来源记录号拒绝；长 ID/点号槽位、游荡字段清空、空槽和第五槽；直接与 Bridge 保存/删除；旧包清理、外部改动与缺失归属拒绝、原生战斗保留、清理后重建。
- `ResourceConsumerRecordContractTests`：非法头与正常物品数量对照；树 ID 原样保存及 DSON 往返、哈希冲突/重复购买拒绝；二十二种 CSV 引用样例，包括说明文本、实际互动输入、第二结果代码、`&` 拆分、代码字节截断、缓冲区超限、整数溢出、无效编码、UTF-8/UTF-16 BOM 对照及三处数值单元格前缀。CSV 从原始字节严格按 UTF-8 解码，避免 `File.ReadAllText` 自动识别 BOM 后把 UTF-16 文件转换成原生不会按同样方式读取的有效输入。
- 原有完整契约组继续覆盖文件优先级、直接/Bridge 编号、来源变化、DSON 保存事务和故障恢复等链路。

复验中纠正了原审核点号怪物样例的来源前提：普通目录不发现点号目录；正式样例改为清单明确列出的 Mod，而不是删除目录发现规则。初次新增测试还补齐了副本实例标识及清单字段大小写，均为夹具问题。初次测试构建缺少 `System.Text.Json` 导入，补齐后构建通过。早期失败日志保留在同一忽略目录，不把失败运行计为验收通过。

原有 `EncounterDiagnosticContractTests`、`BattleMapContractTests`、`BridgeClassificationContractTests` 中也有依赖旧 `.types` 提前停止规则的样例。正常对照改为参数前置；诊断样例继续保留参数后置，并要求准确报告这些额外槽、真实未解析字符串数及保留的运行时编号。未将旧的错误预期作为产品规则继续保留。

`HeroDefinitionSafetyContractTests` 的正常注释夹具也需在数字与 `//` 之间保留一个空格。原生删除整条斜线注释及换行后，`33//...\ncombat_skill:` 会连接成 `33combat_skill:`；读取器回退到第二个 `3`，当前 armour 记录只复制到前一个 `3`，下一次读取停在非法数字前缀。正常夹具用 `33 //...` 保留有效分隔，另外在共享读取契约中单独验证无分隔情况会得到 HP 字段 3、且不登记下一技能。该边界来自 `0x14028E293`–`0x14028E2C1`，不是为测试增加的产品修补规则。

## 验收

- 最终 Release 全解决方案构建通过，0 警告、0 错误：[构建日志](../../workspaces/historical-rule-review6-20260910/review-fix-build.log)，[退出码 0](../../workspaces/historical-rule-review6-20260910/review-fix-build.exitcode)。
- 修补前曾单独通过 `--semantics` 定向检查：[日志](../../workspaces/historical-rule-review6-20260910/acceptance-semantics.log)，[退出码 0](../../workspaces/historical-rule-review6-20260910/acceptance-semantics.exitcode)。最终完整套件包含这组检查以及后补的 BOM、数值单元格反例。
- 最终完整契约套件通过：[完整日志](../../workspaces/historical-rule-review6-20260910/review-fix-contracts.log)，[退出码 0](../../workspaces/historical-rule-review6-20260910/review-fix-contracts.exitcode)。覆盖物品/饰品数量、人物/怪癖、三类战斗和 Bridge、地图新建/替换/删除、自动维护和同步、强制回城、DSON/事务故障恢复。本次完整日志无失败或跳过条目。
- Core、App、ContractTests 三个 Release 输出目录引用相同 Core DLL，SHA-256 为 `EDE4E410445285AF4BC92C7255C3F608C4E71F18421C11ACBE4C1F30CAFFB35B`：[哈希记录](../../workspaces/historical-rule-review6-20260910/review-fix-assemblies.json)。`git diff --check` 通过，更新文档中的本地链接检查通过。
- 一名全新、独立、只读复核者已完成检查，发现一项 P3：CSV 数值被包装成 `.count`/`.weight` 后走最后字段查找，导致 `0.count 1`、`0.weight 1` 误读为 1。主任务直接调用当时构建的读取器复现了两个返回值均为 1，随后提取共用 `ReadIntPrefix`，让 CSV 从单元格开头读整数，darkest 调用端仍先找最后字段再读前缀。默认权重、首组次数和后续组次数均增加公共目录回归样例。该修正为已确认的小范围解析修补，按协作规则不再递归启动完成复核；修正后已重新构建并通过上述完整回归。

本轮未进行新一轮游戏内触发战斗测试；正式二进制已构建。没有改写真实档案、活动 Mod 或 Steam 设置，也没有提交本轮变更。仍不可证明的跨 DLC 挂载、异常资源/字节和地图归属情形继续保留原有保护；不将本轮六项修复宣称为全游戏资源和所有平台语义的完整模拟。
