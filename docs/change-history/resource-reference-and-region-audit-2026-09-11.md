# 历史修改第十一轮审核：物品引用语义与地区查询

按用户要求，先提交上一轮已验证的修复：**9158362**，完整提交为 `9158362c4645016f90c54af494e6bf9ac53aa5dc`，标题为 `fix: align text resource discovery and encounter query indexes`。提交后工作区干净，未推送。随后进行只读审核，本轮没有修改产品代码、正式测试、用户存档或实际 Mod。

本轮确认 **1 处新回归、2 处旧规则遗漏**。新回归影响战斗编号，优先修复；旧问题影响物品引用状态及小镇／副本列表。下面的反例均为隔离样例，没有据此断言当前 profile_1 已存在这些目录或内容。

## 验证方式与边界

- 使用当前生产 Core DLL，SHA-256 为 `299DAED89DDEDEA1086747A9EDC17517528C6C0FFB1B4A29AF4DC676861645B5`。Core、App Release、ContractTests 与当前探针引用的四份 DLL 一致。
- 对照上一版本 **831b962** 的留存 DLL，SHA-256 为 `7AE71991B48FC228C13860C04EAA74BF7B71E431C2848B90C689183B85834206`，其身份在[第十轮审核](resource-discovery-audit-2026-09-10.md)中已有记录。旧 DLL 只放进隔离运行目录，没有替换产品输出。
- 同一份探针分别运行两个版本，每版 **33 组样例**：12 组奇物子目录、15 组人物记录、6 组战斗目录大小写；合计 **66 组执行**。每个战斗样例分别验证走廊、房间、首领三种类型。
- 每版另有 6 次真实 `BattleMapSaveEditor.PlaceBattle` 和 DSON 编码／解码往返，合计 **12 次**。这些只写探针自己的临时档案；没有运行完整 `BattleMapEditService` 事务、安装 Bridge 或进入游戏触发战斗。
- 游戏语义依据已安装 Windows x64 build 27890 的原生反汇编。EXE SHA-256 本轮重新核对为 `35E5A653279992564809FF8406FEBD5A02A7D6961044781B1296B38A7096F59B`。这是静态原生证据与编辑器可执行对照，不是新一轮游戏实测。
- Mod 样例全部已启用并列入 `modfiles.txt`；物品样例覆盖本地、创意工坊和启用 DLC 前缀的 Mod。错误发生在清单筛选后的查询或语义处理。
- 物品最小样例没有提供背包容量定义，均有同一条“未找到有效 raid max_slots，禁用容量编辑”提示。该提示不影响本次引用分类对照，校验脚本也检查了没有额外解析错误。战斗样例没有目录解析告警。

探针及结果保存在忽略目录 [historical-rule-review11-20260911](../../workspaces/historical-rule-review11-20260911/)：

- [探针源码](../../workspaces/historical-rule-review11-20260911/Program.cs)、[隔离地图样例](../../workspaces/historical-rule-review11-20260911/AuditFixtures.cs)。
- [当前结果](../../workspaces/historical-rule-review11-20260911/probe-results.json)、[上一版本结果](../../workspaces/historical-rule-review11-20260911/probe-parent-results.json)。
- [结果校验脚本](../../workspaces/historical-rule-review11-20260911/verify_results.py)、[验证汇总](../../workspaces/historical-rule-review11-20260911/verification.json)。脚本成功表示反例及对照已复现，不表示产品不存在缺陷。

## P1：物理地区目录的大小写被当成查询表 ID，漏读后仍放行错误编号

位置：[BattleEncounterCatalog.SourceDiscovery.cs](../../src/DarkestDungeonSaveEditor.Core/BattleEncounterCatalog.SourceDiscovery.cs) 的 `TryDescribeMashFile`，第 314–327 行；另见 [BattleEncounterCatalog.cs](../../src/DarkestDungeonSaveEditor.Core/BattleEncounterCatalog.cs) 的 `IsCurrentDungeonDifficultyFile`，第 591–597 行。

最近提交将标准战斗文件的地区查询加入全局发现入口：先从路径取 `dungeonId`，再把这个字符串加入区分大小写的正则。当前表的判断也改成了 `originDungeon.Equals(dungeonId, StringComparison.Ordinal)`。问题在于物理目录拼写和原生传入的遭遇表 ID 不是同一个来源。

隔离样例的当前地区为 `cove`、难度为 2：

| 顺序 | 来源中的文件 | 三种类型均使用的怪物 |
| --- | --- | --- |
| 前置文件 1 | `dungeons/Cove/a.cove.2.mash.darkest` | `alpha_A` |
| 前置文件 2 | `dungeons/Cove/b.cove.2.mash.darkest` | `bravo_A` |
| 后续 Mod 文件 | `dungeons/cove/c.cove.2.mash.darkest` | `charlie_A` |

三种怪物都有有效标准定义且体型为 1。每个文件各有一条 hall、room、boss。只有目录大小写不同，文件名中的表 ID 始终为 `cove`。

原生 [MashGuide 加载入口](../../workspaces/historical-rule-review10-20260910/native_1404c90f0.txt) 分别构造目录 `dungeons/%s/` 和查询 `.*%s.%d.mash.darkest`，后者使用调用传入的表 ID（`0x1404C91D5`–`0x1404C91E6`），不是枚举到的物理目录拼写。Windows 上请求 `dungeons/cove/` 可以枚举实际的 `dungeons/Cove/`。本轮探针断言了这一点，并核对原生物理目录设备确实调用 `FindFirstFileW`／`FindNextFileW`，导入地址分别为 `0x140C62330`／`0x140C62328`。两个前置文件的名称都匹配原生的 `cove` 查询。

当前实现却拿路径中的 `Cove` 去匹配文件名中的 `cove`，在最前面的文件发现阶段就排除两个前置文件。结果如下，三个战斗类型一致：

| 检查 | 上一版本 831b962 | 当前版本 9158362 |
| --- | --- | --- |
| `charlie_A` 组合编号 | 2 | **0** |
| 直接放置校验 | 通过 | **仍通过** |
| Bridge 追加规划的下一编号 | 3 | **1** |
| 自动维护读取的表长 | 3 | **1** |
| 修改前置文件内容后的战斗指纹 | 改变 | **不改变** |
| 隔离地图 DSON 往返后的编号 | 2 | **0** |

将前置目录改为全小写 `cove`，两个版本都恢复为编号 2、下一编号 3。前置来源分别设为原版物理源、本地 Mod、创意工坊 Mod 时，当前目录／规划结果均复现；DSON 往返执行的是原版物理源这组对照。

**修复阶段补注（2026-09-11）：上表的原生预期适用于 Windows 物理源，不应直接推广到清单目录树。** 后续核对到 Mod 清单目录的插入（`0x1403EBE30`、`0x140239251`）和查找（`0x1402397E0`）按原始大小写字节哈希；清单自身写成 `dungeons/Cove/` 时，查询 `dungeons/cove/` 不会找到该目录。上面的 Mod 样例仍是两个编辑器版本行为的有效对照，但其中“原生应为编号 2”的推断过宽。若清单写小写 `cove`、物理目录为 `Cove`，清单匹配和 Windows 打开都能成立。实际修复按这两种来源分别处理，见[修复记录](resource-reference-and-region-fixes-2026-09-11.md)，不把物理路径规则统一套到 Mod 清单。

这是 **9158362 引入的新回归**，有上一版本二进制对照，而不只是根据代码推测。它会让新建／替换选择的组合和存档编号不一致，也会让 Bridge 的前置计数偏小。实际 Bridge 写盘事务及游戏战斗结果尚未在本轮验证。

建议修复：分开处理 Windows 路径匹配、实际请求的地区／遭遇表 ID 和文件名正则，统一发现、当前表、追加、维护与指纹入口。保留已经验证的原生正则语义及怪物 ID 大小写规则；不能用“所有字符串一律忽略大小写”来代替。

## P2：任意子目录名仍会改变物品的小镇／副本引用状态

位置：[QuantityItemReferenceAnalyzer.Reachability.cs](../../src/DarkestDungeonSaveEditor.Core/QuantityItemReferenceAnalyzer.Reachability.cs) 的 `GetDefaultReachability`，第 37–51 行；[RootParsing.cs](../../src/DarkestDungeonSaveEditor.Core/QuantityItemReferenceAnalyzer.RootParsing.cs) 的 `ParseRootFile` 在解析 CSV 前使用这个结果。

该函数使用 `Contains("/upgrades/")`、`Contains("/campaign/town_events/")` 等判断“小镇专用”。它没有区分真正的资源根与其他资源中的普通子目录。

用完全相同的奇物 CSV 引用同一张 Loot 表，再由 Loot 表引用同一个 `estate` 物品；该物品没有其他引用，没有出现在样例存档里，也不允许直接作为城镇补给出售。仅改变 CSV 的路径：

| 清单中的路径 | 当前副本物品目录 | 当前小镇物品目录 |
| --- | --- | --- |
| `curios/rewards/audit_curio_type_library.csv` | 已确认引用，可见 | 疑似未使用，默认隐藏 |
| `curios/upgrades/audit_curio_type_library.csv` | **疑似未使用，默认隐藏** | **已确认引用，可见** |
| `curios/campaign/town_events/audit_curio_type_library.csv` | **疑似未使用，默认隐藏** | **已确认引用，可见** |
| `curios/campaign/estate/audit_curio_type_library.csv` | **疑似未使用，默认隐藏** | **已确认引用，可见** |

原生 [CurioLibrary 查询](../../workspaces/historical-rule-review9-20260910/native_1404d8770.txt) 使用 `.*curios/.*curio_type_library.csv`。四个路径都满足同一种资源查询，内部子目录名不会把奇物掉落改成小镇来源。

本地／创意工坊／DLC 前缀 Mod 共 12 组，两个版本结果完全一致。历史追踪显示这段上下文判断从 **d1d18f66（2026-09-03）** 留存至今。

这与上一轮修复相关，但不是同一个判断点：上一轮修的是资源发现时的 `IsDefinitionOnlyPath` 错误排除；此次遗漏在文件已经发现后，决定引用归属的 `GetDefaultReachability`。建议根据已识别的资源消费者或挂载后的真实资源根决定上下文，不能继续搜索任意路径片段。此问题影响引用状态、搜索默认可见性和小镇／副本分类；样例没有发现数量或堆叠数值被改写。

## P2：标准人物文件中的未知记录仍会被当成有效物品引用

位置：[QuantityItemReferenceAnalyzer.RootParsing.cs](../../src/DarkestDungeonSaveEditor.Core/QuantityItemReferenceAnalyzer.RootParsing.cs) 的 `ParseDarkestRoot`，第 99–104 行。

当前代码已按记录类型识别 `extra_battle_loot` 等掉落代码，但对 `.type/.id`、`.item_id`、`.use_item_id` 仍然只看字段出现，没有验证该记录是否是这个文件的原生消费入口。

清单中列出合法标准人物文件 `heroes/audit/audit.info.darkest`，提供正常的 armor 定义，然后分别加入下面的记录：

| 单独加入的内容 | 游戏人物读取器是否产生该物品引用 | 当前目录结果 |
| --- | --- | --- |
| `notes: .type estate .id audit_token` | 否 | **已确认引用** |
| `notes: .item_id audit_token` | 否 | **已确认引用** |
| `notes: .use_item_id audit_token` | 否 | **已确认引用** |
| `extra_battle_loot: .code audit_loot` | 是，Loot 表指向该物品 | 已确认引用 |
| `// notes: .type estate .id audit_token` | 否，注释 | 疑似未使用 |

原生 [HeroClass 记录分派器](../../workspaces/resource_semantics_live_20260908/native_1404c3860.txt)（`0x1404C3860`）按已知前缀分支；`extra_battle_loot:` 有明确分支。正常人物 ID 下未知的 `notes:` 最终走到 `0x1404C4790` 返回，没有泛用的 `.type/.id` 引用消费分支。

本地／创意工坊／DLC 前缀 Mod 共 15 组，两个版本结果完全一致。当前函数形式来自 **27a6c17c（2026-09-09）** 的 NativeDarkestReader 迁移；迁移保留了更早的泛字段引用假设。

此前已经修复的 JSON 备注、文本注释和 `heroes/inventory/README.darkest` 是不同层次：它们分别处理 JSON 字段、注释语法和无关文件。本次问题发生在**合法文件内部的未知记录**，不需要清单漏过滤或非标准路径就能触发。

建议按文件消费者和有效记录类型限制可引用字段，并保留正常伴生掉落／起始物品入口。单独排除 `notes` 这个名称不能解决其他未知记录。影响是未引用物品被错误确认为有效、取消默认隐藏；本次没有证明游戏会因此得到该物品。

## 检查结果与后续范围

本轮没有把未充分验证的加载回退、特殊脚本或命名习惯列为已确认问题，也没有删除现有保护。规则修复仍需要针对上述三个独立判断点实施。

探针当前版本及父版本均退出 0，结果校验脚本通过。早期探针曾因反射调用漏传可选参数而退出 1，随后修正；另有一次目录样例只通过大小写区分，碰到了 Windows 路径别名，现已改为独立目录并增加实际目录名断言。最终 `probe-final.log`、`probe-parent.log` 和 JSON 结果替代这些早期诊断，不将其计入有效样例。

产品代码本轮没有变化，因此未重复运行全量契约套件；上一轮的 Release 构建、完整套件及独立审查后的定向验收记录见[修复记录](resource-discovery-fixes-2026-09-10.md)。旧验收不覆盖此次新发现的反例。按协作规则，只读审核不触发实现完成审查员。

本报告与审查索引为本轮新增／更新的文档，尚未提交。产品仍为 9158362，以上三项尚未修复。
