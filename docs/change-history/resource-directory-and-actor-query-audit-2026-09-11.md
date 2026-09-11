# 历史修改第十二轮审核：资源目录大小写与人物／怪物发现

日期：2026-09-11。

后续状态：用户确认后已完成[对应修复与验证](resource-directory-and-actor-query-fixes-2026-09-11.md)。下文保留提交 `55e214c` 时的审核观测，不代表修复后的程序状态。

按用户要求先提交第十一轮修复，再进行只读审核。提交为 `55e214c3a1ebb41679fa34add9e1e925369eaf79`，标题 `fix: align resource reference context and encounter directory matching`。提交前复核差异检查、上一轮反例验收及产物哈希；提交后产品源码、正式测试和已构建程序没有修改。本报告记录两项尚未修复的问题。

## 审核范围与证据

核对了文件发现、清单路径筛选、同路径覆盖、人物／怪物身份发现和标准文件读取、物品／容量／Buff 消费、人物生成、战斗计数、直接写入和 Bridge 追加计算。继续检查缺失文件及刷新入口，但未把未经确认的原生行为作为确定问题。

- 固定游戏：Windows x64 build 27890；EXE SHA-256 `35E5A653279992564809FF8406FEBD5A02A7D6961044781B1296B38A7096F59B`。
- 当前 Core SHA-256 `312F5A49D7B3A2143A6AE4DA15931BB66F103DA7C60A84605FAB16AF96CE9CF7`。Core 输出、App 输出、正式契约测试输出与本次探针输出四处一致，排除了旧 DLL 干扰。
- [隔离探针与原生证据](../../workspaces/historical-rule-review12-20260911/)包含 `Program.cs`、`probe-results.json`、`probe-roundtrip.log`、`verify_results.py`、`verification.json`。有效样例 27 组：18 组正常对照、9 组触发偏差。战斗每组分别检查 hall、room、boss。
- 15 次真实编码／解码往返：人物 HP 3 次、背包堆叠 3 次、地图战斗编号 9 次。仅写入新建的隔离样例，不操作真实档案或实际 Mod；没有启动游戏或声称完成新的实机战斗验证。

`verify_results.py` 的 PASS 表示观测到的反例、对照及文件哈希符合报告，并不表示产品已经正确。原始输出另有 2 组探索样例，其 `audit_template` 文件名本身触发物理 `_template` 排除，正常对照也不能注册；因此不计入上述 27 组，不据此报告通配点导致的漏读。

## 1. P1：人物／怪物发现仍忽略文件名大小写，可能授予错误的战斗编号

主要位置：[怪物物理发现](../../src/DarkestDungeonSaveEditor.Core/BattleEncounterCatalog.SourceDiscovery.cs)，第 227–235 行；同文件第 255 行的清单后缀提取；[共用发现规则](../../src/DarkestDungeonSaveEditor.Core/ContentFileDiscovery.cs)，第 7–14 行；[人物来源发现](../../src/DarkestDungeonSaveEditor.Core/HeroClassCatalog.SourceDiscovery.cs)。

当前物理扫描使用 Windows 的 `*.info.darkest`，Mod 清单路径提取和共用通配符匹配也忽略大小写。因此 `alpha_A.info.DARKEST`、`alpha_A.INFO.darkest` 都能注册 `alpha_A`。之后，标准路径文件映射也能找到这份文件，编辑器会读出其中的体型。

游戏在发现阶段使用的查询是：

| 对象 | 原生查询 | 调用位置 |
| --- | --- | --- |
| 人物 | `.*\.info.darkest$` | `0x1403E7425`–`0x1403E743F` |
| 怪物 | `.*\.info\.darkest$` | `0x1403E7ADE`–`0x1403E7AF8` |

两者都区分大小写，不能把大写后缀当作可注册的定义；人物和怪物的点转义也不同。证据见 `native_1403e7360.txt`、`native_1403e7a6b.txt`。后续标准路径能否在 Windows 打开，是另一阶段，不能倒过来证明这个 ID 已被发现并注册。

### 可以复现的编号错误

只创建 `alpha_A.info.DARKEST`，定义体型 3；正常的 `bravo_A.info.darkest` 定义体型 1。遭遇表依次为：

```text
hall: .chance 1 .types alpha_A alpha_A
hall: .chance 1 .types bravo_A
```

同样为 room、boss 建立两行对照。

| 判断 | 原生规则对应结果 | 当前编辑器 |
| --- | --- | --- |
| `alpha_A` 是否注册 | 否 | 是，且体型为 3 |
| 第一行是否占号 | 两个缺失指针，仍保留编号 0 | 误算体型 6，跳过整行 |
| 第二行 `bravo_A` 的编号 | 1 | 0 |
| Bridge 下一条编号 | 2 | 1 |

缺失怪物仍保留遭遇槽的规则，已有之前的[实际游戏表捕获](../resource-duplicate-semantics.md#5-index-occupancy-of-invalid-encounter-rows)支持。本次静态发现规则与隔离反例结合后，可以确定这个例子的预期序列。

`ValidateDirectEncounter` 没有报错，`CanPlaceDirectly` 为 true。对模拟原版来源执行地图修改并编码重读后，后续正常组合仍被写成编号 **0**。本地 Mod、工坊 Mod 的目录／直接校验／追加计算也复现相同偏差；三种战斗类型均受影响。

人物侧亦有误读：`audit.info.DARKEST` 会被编辑器纳入人物目录，以该职业为 `hero_class_requirements` 的饰品也通过筛选。原生发现阶段没有注册这个职业。

历史来源：怪物的 `*.info.darkest` 物理筛选可追溯到 `73f599a`；共用 `MatchesSimpleExpression(..., ignoreCase: true)` 来自 `4e1db6b`，更早的清单读取分支延续宽松后缀提取。第十一轮修正的是遭遇表文件目录匹配，没有改变这些人物／怪物发现方法；因此不应撤销上轮的修复。

建议：发现阶段分别采用人物、怪物的真实查询，并贯穿清单与物理候选、已注册 ID 集合、饰品职业要求、怪物体型／Boss 标签、战斗预检、追加和维护。保留标准路径直接打开的独立规则。不能只在战斗写入前再加一层任意拦截，也不能删除空组合／缺失怪物占号规则。

## 2. P2：其他资源把 Mod 清单目录按 Windows 大小写规则匹配

位置：[共用资源查询](../../src/DarkestDungeonSaveEditor.Core/NativeResourceFileRules.cs)，第 68–77、117–140 行等；[物品清单发现](../../src/DarkestDungeonSaveEditor.Core/QuantityItemCatalog.SourceDiscovery.cs)，第 51 行；[人物相关清单分发](../../src/DarkestDungeonSaveEditor.Core/HeroClassCatalog.SourceDiscovery.cs)，第 88–105 行。

例如 `IsInventoryItemFile` 和 `IsBuffFile` 对目录前缀使用 `OrdinalIgnoreCase`，调用时又没有区分清单来源与物理来源。于是清单写成 `Inventory/...`、`Shared/buffs/...`、`Trinkets/...` 时，仍被当作小写目录查询的结果。

第十一轮已经核对过：Mod 清单目录树注册与查找按原始目录字节计算 hash；游戏查询 `inventory/` 或 `shared/buffs/` 不会由 Windows 替清单树匹配不同大小写的目录。依据见[上一轮修复证据](resource-reference-and-region-fixes-2026-09-11.md)。库存入口同时明确传入 `inventory/`，见 [原生库存加载](../../workspaces/historical-rule-review10-20260910/native_1404c7ec0.txt) 的 `0x1404C7FF0`、`0x1404C8097`。饰品、Buff、怪癖查询沿用[已核实的资源入口](../resource-duplicate-semantics.md#16-trinket-and-map-json-members-catalog-file-queries-2026-09-10)。

### 隔离样例结果

正常原版定义：物品堆叠上限 2、背包容量 4、HP 20 的人物通过正常 Buff 得到 HP 25。高优先级 Mod 清单的大写目录下提供堆叠 9、容量 99、同 ID Buff +75%，以及额外饰品／怪癖。

| 项目 | 清单未匹配这些目录时的预期 | 编辑器结果 |
| --- | --- | --- |
| 物品堆叠上限 | 2 | 9 |
| 副本背包容量 | 4 | 99 |
| 新人物初始 HP | 25 | 35，怪癖仍为 Direct |
| 大写目录里的新增物品／饰品／怪癖 | 不应由这些文件提供 | 被目录识别 |

实际生成的人物写入城镇 DSON 后重读，HP 仍为 **35**。背包修改也把 40 个物品编码成 **9、9、9、9、4** 五堆，已同时超过真正的单堆上限 2 和容量 4。这不是仅名称显示错误。数量预览重新调用同一套 `LoadDefinitions`／容量目录，并未独立纠正这个候选集合；样例中物品 `HasProviderConflict` 为 false。

上述偏差在本地 Mod、工坊 Mod、已启用 DLC 前缀下的 Mod 各复现一组。正常小写清单，以及清单为小写、仅磁盘目录为大写的对照均正确。原版／模式／官方 DLC 的物理大小写目录对照也正确；修复时不能把所有来源统一改为大小写敏感。

历史来源：库存／Effect／Curio 共用谓词加入于 `9158362`，饰品／Buff 等谓词加入于 `831b962`；这些查询统一后仍延续较早发现层的路径大小写假设。第十一轮仅在遭遇表入口落实了来源区别，其他资源没有同步获得这一区分。

建议：让“清单目录树匹配”“物理目录定位”“资源文件名查询”“资源 ID 比较”保持独立，并把来源类型带入相应候选过滤。至少同步覆盖物品、容量、饰品、Buff／怪癖、Effect、奇物及其相关引用／刷新入口；本轮表格中的实测数值限于列出的消费流程，不宣称所有资源类型均已逐个构造反例。

## 保留的规则与未下结论的部分

- 不删除 `modfiles.txt` 门槛、同路径 Mod 顺序、各类重复 ID 的独立规则、标准路径读取或已有哈希碰撞保护。
- 不删除空行和缺失怪物占号规则；第一项正是错误的怪物发现结果污染了正确的占号／体型逻辑。
- 未发现证据推翻上轮的奇物挂载根上下文和人物／怪物未知记录排除修复。
- 缺失清单文件的处理仍有入口差异，但游戏原生打开失败后的完整回退行为未充分确认。本轮没有将其升级为确定的游戏规则偏差，也没有修改或删除相关保护。
- 本轮没有确认用户当前 `profile_1` 正在使用触发问题的大写路径；这里报告的是已经复现的条件性错误，不是声称当前所有战斗或所有物品都错误。

审核结束后仅新增本报告并更新记录索引，尚未实施两项修复。依据任务类型，这次只读审核未额外触发完成审查者；初始提交沿用上一轮已完成的独立审查与验证结果。
