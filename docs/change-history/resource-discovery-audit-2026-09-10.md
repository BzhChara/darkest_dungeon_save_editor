# 历史修改第十轮审核：资源发现与错误排除，2026-09-10

按用户要求先提交上一轮已验证的修复：**831b962**，完整提交为 `831b962ae8b7105248216ccbefa85ab925ad7b5e`，标题为 `fix: align trinket and prop JSON reads with native resource queries`。提交后工作区干净。

本轮在该版本上只读审核产品代码，确认以下四项待修复问题。前三项是不同资源入口残留的文件查询差异；第四项是独立的旧目录排除规则。没有修改产品代码、正式测试、有效规则文档、真实存档或活动 Mod。新增本报告和记录索引；诊断代码及样本位于忽略目录 `workspaces/historical-rule-review10-20260910/`。

## 验证方式与范围

- 使用已编译的生产 Core DLL，SHA-256 为 `7AE71991B48FC228C13860C04EAA74BF7B71E431C2848B90C689183B85834206`。Core 输出、App Release 输出及探针引用的三份 DLL 一致。
- [隔离探针](../../workspaces/historical-rule-review10-20260910/Program.cs)编译、执行退出码为 0；[结果](../../workspaces/historical-rule-review10-20260910/probe-results.json)包含 **34 组反例／对照**：物品与容量 4 组、Effect 4 组、奇物 4 组、引用子目录 6 组、刷新指纹 12 组、战斗表 4 组。每组战斗表同时检查走廊、房间、首领三种索引。
- [结果校验脚本](../../workspaces/historical-rule-review10-20260910/verify_results.py)再次核对输出、追加编号和 DLL 哈希，退出码为 0；[校验摘要](../../workspaces/historical-rule-review10-20260910/verification.json)保存实际运行目录。断言通过表示问题已复现，**不表示问题已修复**。
- 初次探针的奇物正常对照失败，原因是诊断夹具缺少地区道具池；补齐 `dungeons/cove/cove.props.darkest` 后完成全部对照。产品代码没有为此修改。
- 原生依据来自本机 Windows x64 build 27890，EXE SHA-256：`35E5A653279992564809FF8406FEBD5A02A7D6961044781B1296B38A7096F59B`。静态检查脚本逐次核验此哈希。
- 原生文件是否应纳入，是按实际加载调用及查询表达式推导；编辑器结果由可执行探针获得。**本轮未启动游戏加载这些反例，也未证明当前 profile_1 存在这些文件名。**
- 本轮是只读审核，按协作规则不触发实现完成审查员。上一轮提交前已有独立审查及 39 条全量契约 PASS 汇总；本轮没有重复运行未改动的全量套件，也没有把旧验收当作新问题不存在的证明。

## 1. P1：战斗表漏读后，仍把错误编号标为可直接写入

位置：[BattleEncounterCatalog.SourceDiscovery.cs](../../src/DarkestDungeonSaveEditor.Core/BattleEncounterCatalog.SourceDiscovery.cs) 第 8–10、61–62、84、291–321 行；[BattleEncounterCatalog.cs](../../src/DarkestDungeonSaveEditor.Core/BattleEncounterCatalog.cs) 第 591–606 行。后续受影响入口包括 `ValidateDirectEncounter`、[ResolveAppendTarget](../../src/DarkestDungeonSaveEditor.Core/BattleEncounterCatalog.RuntimeOrder.cs) 第 107–148 行，以及复用文件集合的维护读取。

原生 [MashGuide::Load](../../workspaces/historical-rule-review10-20260910/native_1404c90f0.txt) 在 `0x1404C91B1` 构造目录 `dungeons/%s/`，在 `0x1404C91D8` 构造查询 `.*%s.%d.mash.darkest`，随后在 `0x1404C9239` 调用资源查询。表达式的分隔点未转义。

海湾难度 1 的隔离资源包含两份文件，分别为 `dungeons/cove/a.coveX1.mash.darkest` 和 `dungeons/cove/b.cove.1.mash.darkest`。前者为 hall、room、boss 各声明一条 `[alpha_A]` 组合，后者各声明一条 `[bravo_A]` 组合。两个怪物均有标准路径的 info，体型均为 1。两个文件都匹配原生查询，目录深度相同，a 在 b 之前。

| 项目（每个战斗类型独立计数） | 原生查询及已验证行序规则 | 当前编辑器 |
| --- | --- | --- |
| 组合 `[alpha_A]` | 编号 0 | 整个文件被排除 |
| 组合 `[bravo_A]` | 编号 1 | 编号 0，`CanPlaceDirectly=true` |
| `ValidateDirectEncounter` | 不应认可该组合的编号 0 | 复查通过，Issues 为空 |
| Bridge 下一条编号 | 2 | 返回 1 |

将第一份文件改为普通点分名称 `a.cove.1.mash.darkest`，相同内容立即得到正确的 0、1 和追加 2。模拟原版来源及清单明确列出的本地 Mod 来源均复现。这里比较的是组合编号，不是怪物 ID 编号。

本轮执行了目录、直接写入复查和 Bridge 追加规划，未实际创建 Bridge 包或提交战斗地图。[BattleMapSaveEditor.cs](../../src/DarkestDungeonSaveEditor.Core/BattleMapSaveEditor.cs) 第 195 行直接写入所选编号；“可能落到其他组合”是该错误编号进入游戏查表后的后果推断，不是本轮实机观察。

历史来源：固定难度后缀来自 `a5f38b88`；`730c01eb` 又增加标准表必须以完整地区名称后缀结束的检查。后续修正逻辑记录、多文件顺序和空组合占号时，没有一起更新这些前置过滤。

建议按实际地区、难度的原生查询确定文件集合，让全局来源描述、当前表、直接复查、Bridge 追加和维护保持一致。若全局来源归属无法证明，应明确处理，不能静默漏读后仍授予可写编号。保留有效清单、DLC 挂载、同路径覆盖及已有编号检查。

## 2. P1：物品与容量仍使用固定后缀，可能生成超出有效容量的背包

位置：[QuantityItemCatalog.SourceDiscovery.cs](../../src/DarkestDungeonSaveEditor.Core/QuantityItemCatalog.SourceDiscovery.cs) 第 25、35 行；[InventorySystemConfigCatalog.cs](../../src/DarkestDungeonSaveEditor.Core/InventorySystemConfigCatalog.cs) 第 11、101、109 行。保存前复查在 [SaveEditService.QuantityItems.cs](../../src/DarkestDungeonSaveEditor.Core/SaveEditService.QuantityItems.cs) 第 370–388 行。

原生 [Inventory 加载入口](../../workspaces/historical-rule-review10-20260910/native_1404c7ec0.txt) 的物品查询为 `.*inventory/.*\.inventory.items.darkest`（`0x1404C808B` / `0x1404C80A2`），容量查询为 `.*inventory/.*\.inventory.system_configs.darkest`（`0x1404C7FE4` / `0x1404C7FFB`）。只有 inventory 前面的点被转义，不能把整段当作固定后缀。

隔离反例沿用已经验证的“物品同键首条定义”“容量同类型后续赋值”语义：

- 第一份有效文件 `a.inventoryXitems.darkest` 定义物品堆叠上限 2；后面的普通文件定义同键上限 9。编辑器漏读前者，采用 9，按原生文件集合应为 2。
- 普通容量文件先设置 raid、trinket_storage 为 8；后面的有效文件 `z.inventory.system_configsXdarkest` 将二者改为 2。编辑器漏读后者，两个目录均继续返回 8。
- 对一个堆叠上限为 1 的普通物品，生产 `RaidInventorySaveEditor.SetAmount` 因此允许创建 3 格；DSON 编码、解码后仍有槽位 0、1、2，超过原生最后赋值的容量 2。普通点分名称对照识别容量 2，并拒绝这次增加。

这里验证的是超出有效容量的输出，不推测游戏随后会断言、丢弃还是采取其他处理。保存前重读仍走同一个漏读入口，不能自动纠正。问题涉及副本数量编辑与小镇饰品库存容量，本轮实际序列化验证的是副本背包。

历史来源：物品固定后缀来自 `dca5b2b5` / `73f599a3`；容量解析器 `29e0a883` 正确修正赋值语义，却保留固定后缀发现。应修正发现规则，保留重复定义语义及容量保护。

## 3. P2：Effect、奇物 CSV 及自动刷新仍漏读有效查询文件

位置：[HeroClassCatalog.SourceDiscovery.cs](../../src/DarkestDungeonSaveEditor.Core/HeroClassCatalog.SourceDiscovery.cs) 第 34、88–92 行；[BattleRoomAttachmentCatalog.Curios.cs](../../src/DarkestDungeonSaveEditor.Core/BattleRoomAttachmentCatalog.Curios.cs) 第 10–12、22、40 行；[物品引用发现](../../src/DarkestDungeonSaveEditor.Core/QuantityItemReferenceAnalyzer.SourceDiscovery.cs) 第 84、126、153–156、175–177 行及 [CSV 分发](../../src/DarkestDungeonSaveEditor.Core/QuantityItemReferenceAnalyzer.RootParsing.cs) 第 71 行；[ProfileCatalogContentFingerprint.cs](../../src/DarkestDungeonSaveEditor.Core/ProfileCatalogContentFingerprint.cs) 第 18 行。

| 资源 | 原生查询 | 反例文件 | 隔离观察 |
| --- | --- | --- | --- |
| Effect | `.*\.effects.darkest$`，目录 effects/ | `effects/a.effectsXdarkest` | 技能 Effect 提供的运行时怪癖线索由 1 条变成 0 条 |
| 奇物类型 | `.*curios/.*curio_type_library.csv` | `curios/a_curio_type_libraryXcsv` | 类型及其掉落入口漏读 |
| 奇物映射 | `.*curios/.*curio_props.csv` | `curios/a_curio_propsXcsv` | 地区池已引用的奇物没有进入可选目录 |

Effect 调用见 [原生入口](../../workspaces/historical-rule-review10-20260910/native_1404e4900.txt) `0x1404E4965` / `0x1404E497D`；CSV 调用见 [PropLibrary 加载顺序](../../workspaces/historical-rule-review9-20260910/native_1404d8770.txt) `0x1404D8A22`、`0x1404D8ACE`。

普通点分名称的同内容对照均正确。本地 Mod 奇物掉落物品的对照为 ConfirmedActive、默认显示；反例为 SuspectedUnused、默认隐藏。Effect 实验影响的是线索读取／搜索／显示，不能扩大为技能会直接改变编辑器初始怪癖或 MAX HP。

指纹仍只接纳 `.darkest` / `.csv`，不会观察上述 Xdarkest / Xcsv 内容变化。12 组指纹对照保持文件长度、时间戳和清单不变，仅改变内容：普通后缀全部改变指纹，非固定后缀全部保持原指纹。容量反例也受此影响。

历史来源：Effect 文件过滤由早期职业目录入口保留；奇物过滤来自 `6523b66c`。`bfd0afb6` 增加 CSV 消费过滤时仍使用固定名称；`37d91673` / `c14247a8` 已扩大 JSON 查询和刷新范围，未覆盖这两类后缀。不是此次提交撤销了上一轮的 JSON 修复。

建议统一候选发现、文件种类分发、缺失诊断、保存前检查及刷新指纹。只扩大物理枚举，不修改消费者的后缀判断、清单路径提取和刷新范围，仍不完整。

## 4. P2：旧目录名排除会误伤标准结构下的有效掉落文件

位置：[QuantityItemReferenceAnalyzer.SourceDiscovery.cs](../../src/DarkestDungeonSaveEditor.Core/QuantityItemReferenceAnalyzer.SourceDiscovery.cs) 第 202–211 行 `IsDefinitionOnlyPath`；调用点为第 99、163 行。

旧规则把路径任意位置出现 /inventory/、/effects/、/trinkets/、/shared/buffs/、/localization/ 等目录的文件都当作纯定义资源排除，未区分挂载根目录和有效消费目录内的普通子目录。

本轮全部使用普通 `.loot.json` 名称，清单、内容及奇物掉落入口一致，只改变子目录：

| 有效掉落文件 | 实际目录结果 |
| --- | --- |
| `loot/rewards/audit.loot.json` | ConfirmedActive，默认显示 |
| `loot/inventory/audit.loot.json` | SuspectedUnused，默认隐藏 |
| `loot/effects/audit.loot.json` | 同上 |
| `loot/trinkets/audit.loot.json` | 同上 |
| `loot/shared/buffs/audit.loot.json` | 同上 |
| `loot/localization/audit.loot.json` | 同上 |

原生 [LootLibrary 加载入口](../../workspaces/historical-rule-review10-20260910/native_140441410.txt) 在 loot/ 下递归查询 `.*loot.json`（`0x140441475`–`0x14044148C`）。子目录与另一种资源目录同名，不会将文件改用另一种消费者。当前 `NativeResourceFileRules.IsLootFile` 也认可这些路径，但随后又被旧 Contains 排除。

这段规则来自 `d1d18f66`，最初用于广泛扫描时排除纯定义文件。后来增加实际消费者识别，旧过滤仍保留，形成第二次错误排除。建议删除或收窄任意子目录名判断，按挂载后的资源根及实际消费者种类处理；保留“未引用物品”分析本身。回归需覆盖正常嵌套目录、DLC 前缀及真正的纯定义目录，不能改为无条件接纳所有 JSON。

## 后续处理边界

本轮核心问题是游戏查询应纳入的文件仍被旧筛选排除，以及旧排除条件作用范围过大。不需要因此改变已确认的 Mod 清单门槛、同路径优先级、重复 ID 消费规则、空组合占号或持久副本存档位置规则。

本报告和索引尚未提交；产品代码仍为 831b962。后续可优先处理战斗表和容量的错误写入风险，再统一其他入口及刷新，清理旧引用目录排除。缺失文件覆盖回退、全部特殊招募脚本和完整战斗运行机制等没有本轮新的充分证据，未列为已确认缺陷。
