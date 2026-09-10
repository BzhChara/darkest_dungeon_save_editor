# 人物身份与地图资源消费规则修复：2026-09-09

用户确认修复[第五轮审核](hero-curio-rule-audit-2026-09-09.md)发现的五类问题。本次基于提交 `7c0e3a0` 修改产品、契约测试与当前规则文档；原审核保留当时的缺陷结果，不改写成修复后结果。

## 修改内容

| 文件 | 作用 |
| --- | --- |
| [HeroClassCatalog.TextParsing.cs](../../src/DarkestDungeonSaveEditor.Core/HeroClassCatalog.TextParsing.cs)、[HeroClassCatalog.QuirkDefinitions.cs](../../src/DarkestDungeonSaveEditor.Core/HeroClassCatalog.QuirkDefinitions.cs)、[HeroClassCatalog.cs](../../src/DarkestDungeonSaveEditor.Core/HeroClassCatalog.cs) | Buff/怪癖定义和引用保留原始身份，按精确 ID 分组；贯通进化链和来源记录。未解析的 Buff 一律不能排除 HP 影响，避免真实哈希碰撞的另一个对象带 HP 而被漏检。非空的纯空白引用不再被静默丢弃。 |
| [StagecoachHeroCandidateFactory.Quirks.cs](../../src/DarkestDungeonSaveEditor.Core/StagecoachHeroCandidateFactory.Quirks.cs)、[StagecoachHeroSaveEditor.cs](../../src/DarkestDungeonSaveEditor.Core/StagecoachHeroSaveEditor.cs) | 精确选择、互斥、数量统计、普通/碎片马车分流与实际序列化使用同一个怪癖 ID。 |
| [InitialQuirkSelectionDialog.xaml.cs](../../src/DarkestDungeonSaveEditor.App/InitialQuirkSelectionDialog.xaml.cs)、[MainWindow.CatalogInteraction.cs](../../src/DarkestDungeonSaveEditor.App/MainWindow.CatalogInteraction.cs)、[MainWindow.Presentation.cs](../../src/DarkestDungeonSaveEditor.App/MainWindow.Presentation.cs)、[MainWindow.ProfileSync.Selection.cs](../../src/DarkestDungeonSaveEditor.App/MainWindow.ProfileSync.Selection.cs) | 选择去重、显示匹配及刷新恢复采用精确身份；搜索、排序仍保留原有体验。 |
| [NativeCurioCsvReader.cs](../../src/DarkestDungeonSaveEditor.Core/NativeCurioCsvReader.cs)、[BattleRoomAttachmentCatalog.Curios.cs](../../src/DarkestDungeonSaveEditor.Core/BattleRoomAttachmentCatalog.Curios.cs) | 替换通用 CSV 解析器，实现原生物理分块、引号切换、尾部 ASCII 空格处理、类型库块边界、映射首行丢弃、短行缓冲区保留和字段更新。删除重复资源再次比较 Mod 优先级或一律判歧义的旧处理。 |
| [BattleRoomAttachmentCatalog.PropLibrary.cs](../../src/DarkestDungeonSaveEditor.Core/BattleRoomAttachmentCatalog.PropLibrary.cs)、[BattleRoomAttachmentCatalog.Resources.cs](../../src/DarkestDungeonSaveEditor.Core/BattleRoomAttachmentCatalog.Resources.cs)、[BattleRoomAttachmentCatalog.cs](../../src/DarkestDungeonSaveEditor.Core/BattleRoomAttachmentCatalog.cs) | 根目录 prop/obstacle/trap 三份 JSON 先按固定顺序加载，再处理子目录 prop/trap/obstacle 和 CSV；应用文件默认、条目默认、已经加载的父对象和难度变化。JSON 名称按原生 63 字节上限登记，版本追加后按首个精确难度/最近难度查找；检查实际类型和有效剧情字段，名称可以保留 JSON 中已有 UI 键。加载与写入前复查复用同一实现。 |

同一 Buff ID 的最后完整定义规则、同路径 Mod 覆盖、清单约束、标准资源入口继续沿用已确认规则。真正缺失、互斥、未知 HP 条件和哈希碰撞仍受检查。本次没有修改战斗组合编号、Bridge 格式/维护、自动同步调度、强制返回、数量修改或存档事务实现。

## 原生依据与边界

依据仍为 Windows x64 build 27890，游戏文件 SHA-256 为 `35E5A653279992564809FF8406FEBD5A02A7D6961044781B1296B38A7096F59B`。新增依据来自只读静态分析，没有冒充新一轮进游戏实测。当前完整说明已写入[精确 Buff/怪癖身份](../resource-duplicate-semantics.md#11-exact-buff-and-quirk-json-identities)与[CSV/JSON 地图规则](../resource-duplicate-semantics.md#12-curio-csv-and-json-prop-consumption)，并更新[人物与存档规则](../content-save-rules.md)和[地图规则](../battle-map-editor-design.md)。

- CSV 缓冲区越界或无法完整解码的 UTF-8 不做猜测；这些是编辑器支持范围保护，不宣称复现原生越界行为。
- JSON 模型覆盖本次放置所需的类型、名称、父对象、难度和剧情标志，不完整模拟所有效果、贴图、互动结果、原生内建系统对象及剧情机制。普通池未引用的系统门等资源不产生无关的缺失告警；若普通池确实引用未解析资源，仍会记录候选拒绝原因。
- 当前公共目录没有选定任务难度，因此只纳入在等级 1–7 的实际查询结果中都满足相应条件的资源。某个难度的有效对象仍属特殊剧情类型时会继续受限。这是明确保留的范围限制。
- 本次未重新操作 WPF 窗口，也未改动真实存档、已启用 Mod 或 Steam 状态；未新增依赖、未改变配置、未提交 Git。

## 验证

新增 [HeroExactIdentityContractTests.cs](../../tests/DarkestDungeonSaveEditor.ContractTests/HeroExactIdentityContractTests.cs)：覆盖基础 HP=20 时 22/30/26 的不同 Buff 引用、带空格/大小写的怪癖选择、实际马车写入与 DSON 往返、互斥、进化链、所有马车池的 singleton 计数、精确碎片马车分流、缺失和真实哈希碰撞。

新增 [MapResourceConsumerContractTests.cs](../../tests/DarkestDungeonSaveEditor.ContractTests/MapResourceConsumerContractTests.cs)：覆盖上一轮反例及物理分块、短行缓存、文件间重置、表头、引号、空名称、JSON 名称保留、跨 Mod 文件槽更新、类型库边界、默认继承、加载阶段、父对象快照、首个精确/最近难度、空变化数组、重复变化、剧情显式清空、非池内哈希碰撞依赖和过期选择拒绝。

同时纠正旧 [BattleMapContentContractTests.cs](../../tests/DarkestDungeonSaveEditor.ContractTests/BattleMapContentContractTests.cs)、[MapPropNativeContractTests.cs](../../tests/DarkestDungeonSaveEditor.ContractTests/MapPropNativeContractTests.cs)、[RegionalMapContentContractTests.cs](../../tests/DarkestDungeonSaveEditor.ContractTests/RegionalMapContentContractTests.cs)中的类型库块、基础类型及重复定义预期；通过 [HeroContractTests.cs](../../tests/DarkestDungeonSaveEditor.ContractTests/HeroContractTests.cs)、[ResourceDuplicateSemanticsContractTests.cs](../../tests/DarkestDungeonSaveEditor.ContractTests/ResourceDuplicateSemanticsContractTests.cs)接入测试入口。

首次完整回归停止在 [HeroCandidateContractTests.cs](../../tests/DarkestDungeonSaveEditor.ContractTests/HeroCandidateContractTests.cs) 的旧预期：将 `steady` 与不存在的 `STEADY` 判为重复。已分别验证完全相同 ID 重复与不存在的精确 ID，之后完整回归通过。补充的非 HP/HP Buff 哈希碰撞反例先复现旧漏检，再修复拒绝检查。

中间一次增量构建返回失败，但日志只有“0 警告、0 错误”，未提供原因；不能声称已定位为编译错误或网络错误。后续单节点、禁用共享编译服务的 Release 构建成功。用户报告的 remote compact 连接错误属于会话请求失败，不能当成项目测试结果。

原版资源只读检查使用[隔离探针](../../workspaces/historical-rule-review5-20260909/real-resource-smoke/Program.cs)，假档案位于工作目录。原版目录读取到房间奇物 19、走廊奇物 31、宝箱 3、陷阱 6、障碍 6；抽样预检通过，去除无关系统对象告警后 Issues 为空。数字仅对应本机原版资源，不是全部 DLC/Mod 或当前 `profile_1` 的统计，也不证明实际触发效果。

独立只读复核发现两项具体遗漏，主代理对照原生代码并补充执行反例，两项均成立：

1. 根目录 JSON 必须先独立加载，不能和同文件族子目录一同排序。反例中，根目录剧情资源与 Mod 子目录普通资源同名，修复前错误放行普通对象。
2. JSON 注册入口先把名称截为 63 字节；长名称与其截断前缀不能当成两个独立资源。ASCII 与中文边界反例均复现了先声明剧情对象被后面的普通短名遮住的问题。

[反例执行日志](../../workspaces/historical-rule-review5-20260909/reviewer-before-map.log)保留修复前失败，明确列出 `root_first`、63 个 q、21 个“界”三个错误放行项。随后修正了根目录阶段和 JSON 名称登记，补测加载父对象、DLC 虚拟根路径、先声明普通对象的正向对照、伪造选中项的预检拒绝与 UTF-8 边界。此为对复核意见的局部修正，按协作规则复跑受影响检查，没有递归启动新的完成复核者；不能将其描述成复核者再次检查后“零问题”。

过程中也有一轮测试程序集构建因已启动的测试占用 DLL 而失败；结束该轮测试会话后重建通过。该轮旧 DLL 执行不作为新增反例的依据，反例以之后成功重建的测试程序集执行结果为准。

最终验收结果：

| 检查 | 结果 |
| --- | --- |
| [最后一次 Release 构建](../../workspaces/historical-rule-review5-20260909/reviewer-fix-build.log) | 成功，0 警告、0 错误，退出码 0。 |
| [完整契约回归](../../workspaces/historical-rule-review5-20260909/verified-full-tests.log) | 全部通过，无 FAIL/SKIP，[本地记录退出码为 0](../../workspaces/historical-rule-review5-20260909/verified-full-tests.exitcode)。此轮已包含最后的 HP 保护；后续两项地图复核补丁另作受影响范围复测。 |
| [人物/资源语义定向测试](../../workspaces/historical-rule-review5-20260909/final-semantics-tests.log) | 通过，含精确身份、HP、进化/互斥/数量、马车分流、DSON 往返和原有空战斗槽/Bridge 契约。 |
| [复核修正后的地图内容测试](../../workspaces/historical-rule-review5-20260909/reviewer-fixed-map.log) | 全部通过，[退出码 0](../../workspaces/historical-rule-review5-20260909/reviewer-fixed-map.exitcode)，覆盖独立复核的两项反例、正向对照、DLC 虚拟根、伪造项预检和原有地图写入/保护。 |
| [修正后的原版资源只读检查](../../workspaces/historical-rule-review5-20260909/reviewer-fixed-native-smoke.log) | 通过，统计仍为 19/31/3/6/6，Issues 为空；没有执行真实游戏战斗或写入真实档案。 |
| 文档与构建产物核对 | 本次文档的本地链接有效；`git diff --check` 通过。Core、App、ContractTests 三个输出目录的 Core DLL 一致，SHA-256 为 `D3A20AB31834098626FA495B9BB63645628824596CDFBB627396BBAA0ACD3BC6`。 |

全量测试曾输出最终 PASS 后，命令运行器未回传退出状态；重新执行时增加独立的本地退出码记录，得到 0。该监控问题与产品断言结果分开记录，没有把不明确的运行器状态当成成功依据。

本轮已完成五类修复及已核实复核问题的修正。最后两项改动仅影响地图资源阶段和 JSON 名称登记，因此按协作规则复跑相应地图/原版读取检查，没有重复未受影响的全套人物、背包和 Bridge 业务测试。本记录上方列出的支持范围限制仍保留。
