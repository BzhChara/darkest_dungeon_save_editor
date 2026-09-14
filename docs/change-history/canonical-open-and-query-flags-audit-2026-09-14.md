# 历史修改第二十一轮审核：固定路径打开与资源查询参数（2026-09-14）

后续状态：用户已确认修复，实施与最终验证另见[固定路径打开、Effect 与 District 查询修复](canonical-open-and-query-flags-fixes-2026-09-14.md)。下文保留修复前的代码版本、复现结果和审核边界。

本轮先提交上一轮已完成的修复：`3b19139 fix: align native resource enumeration and file opening`，共 23 个文件。提交前核对上一轮验证记录对应的源码、测试和 Core 产物哈希、`git diff --check`；上一轮已有 68 PASS、0 FAIL、0 SKIP，以及 Release 构建和独立审查。提交后工作区干净，没有对未变化代码重复运行全量测试。

随后只读审核，确认 **1 个 P1、2 个 P2**。本轮没有修改产品代码、正式测试、规则文档或发布产物，没有操作真实游戏进程、存档、Mod 或 Steam 配置。临时探针、合成 Mod 和二进制存档均位于被忽略的 `workspaces/historical-rule-review21-20260914/`。本报告及目录索引是提交之后新增、尚未提交的审查文档。

## 证据版本与方法

- 原生游戏：Windows x64 build 27890。
- EXE SHA-256：`35E5A653279992564809FF8406FEBD5A02A7D6961044781B1296B38A7096F59B`。
- 审核时 Core DLL SHA-256：`81BA5F7FC992AA1277D6CE60B7E51FA37E1F7BA8D8C9E229AADEE3BF0E40EC71`。
- 重新从固定 EXE 提取 13 个函数的反汇编，核对原生调用参数及分支；独立探针引用当前已编译 Core DLL，没有重编译产品。
- 下文的“原生应有结果”来自固定 EXE 的分支分析；“编辑器实际结果”来自执行目录加载及合成存档保存。**本轮没有重新运行游戏实测**，也没有检查当前 profile 是否具有这些触发条件。

## P1：固定路径打开仍把清单键当作 Windows 路径，并错误消除 DLC 前缀

位置：

- `NativeContentFileResolver.cs:30–35`：人物/怪物文件先作无请求参数的提供者合并，再消除 DLC 前缀并构建忽略大小写的字典。
- `NativeContentFileResolver.cs:86–92,162–164`：`ResolveOpenedFiles` 仍按忽略大小写的 mounted path 选提供者。
- `HeroClassCatalog.cs:44–51`、`BattleEncounterCatalog.SourceDiscovery.cs:143–157`、`QuantityItemReferenceAnalyzer.SourceDiscovery.cs:84`：依赖这些固定路径结果。

### 与上一轮修复的区别

旧假设源于 **`4e1db6b`（2026-09-07）**：将清单准入后的文件统一按 mounted path 合并，并认为 Windows 固定路径打开可以忽略大小写。

`3b19139` 修正了普通文件枚举的子串替换，以及枚举后剩余 Base 文件再次打开时的原始请求校验。但是，人物/怪物这种“先从文件发现 ID，再构造标准路径打开”的入口仍沿用旧合并结果。它没有经过新增的原始请求过滤。不是本轮提交把已经正确的固定路径读取改坏了。

此外，**`cd4a3ac`（2026-09-11）** 的 `ActorDiscoveryQueryContractTests.cs:84–94` 将“物理文件名和清单后缀均为大写”也视作可用的 Mod 固定路径，给这个错误假设增加了通过测试的预期。Base 物理文件别名与 Mod 清单键应分别验证，不能整体删除这组测试。

### 原生入口

1. 人物 `0x1404C33BC` 构造 `heroes/%s/%s.info.darkest`；怪物 `0x1404CD363` 构造 `monsters/%s/%s/%s.info.darkest`。art、override 也分别构造固定路径。
2. `0x140248040` 处理打开请求。无 `>` 时，按挂载优先级查找附加来源；`0x1402480DC–0x1402480F2` 将**原始请求字符串**交给 Mod 清单查询 `0x1402393C0`。
3. 清单查找分别对目录段、文件名按原始字节计算 `h = h * 0x35 + byte`，没有小写化，也不会先消除请求中的 DLC 前缀。
4. 清单键命中后才打开该提供者的物理文件；无清单的物理目录设备按 Windows 文件系统语义访问。两步不能混为一个忽略大小写的键查找。
5. root 请求在某个 Mod 清单中未命中后，可以回落到官方 DLC 的物理目录设备。不会因此重新用 `dlc/<feature>/...` 请求查询同一 Mod 清单。

### 触发 A：清单文件名大小写不同

Base 正确标准路径提供人物 HP 20、怪物 alpha_A 体型 1，并能发现这两个 ID。上方 Mod 仅提供、列出：

```text
heroes/query/query.info.DARKEST                     → HP 99
monsters/alpha/alpha_A/alpha_A.info.DARKEST          → size 3
```

游戏发出的固定请求以 `.info.darkest` 结尾，无法命中上述大写清单键，继续使用 Base。编辑器把它们视为相同路径，实际使用 Mod 的 HP 99、体型 3。

对照：**物理文件继续保留大写名称，只把清单键改成正确的小写请求**，原生清单命中后能通过 Windows 打开该文件；编辑器与原生都应使用 Mod。问题不是“大写物理文件一律不能读取”。

### 触发 B：只有带 DLC 前缀的 Mod 条目

官方 DLC 的物理目录提供人物 HP 30、alpha_A 体型 1。Mod 仅列出 `dlc/feature/heroes/query/query.info.darkest` 和对应带前缀的怪物定义，值分别为 99、3。

固定 root 请求不命中这些 Mod 键，原生应使用官方 DLC 物理文件。编辑器去掉前缀后把 Mod 当成该 root 请求的覆盖，仍取 99、3。对照中把 Mod 文件及清单键放在 root 标准路径，双方都正确使用 Mod。

### 战斗编号与保存后果

海湾同类型表依次包含两条组合：

```text
.types alpha_A alpha_A
.types bravo_A
```

bravo_A 体型始终为 1。触发 A/B 时，原生第一条总体型为 2，应正常占号；编辑器误取体型 3 后算成 6，按“总体型大于 4 跳过”规则将它剔除。

| 项目 | 原生分支结果 | 编辑器实际结果 |
| --- | --- | --- |
| bravo_A 组合编号 | 1 | 0 |
| Bridge 新组合追加编号 | 2 | 1 |
| 自动维护看到的原生表长度 | 2 | 1 |
| 直接放置前校验 | 应使用编号 1 | 编号 0 通过校验 |

走廊、房间、首领三类表均复现。进一步通过 `ActiveContentResolver` 加载合成本地 Mod，在 DSON 存档中分别执行本地区直接放置和跨地区 Bridge 替换：**六次地图保存/重读实际分别得到 0 和 1**；三次后续自动维护均返回未变化。

这会使直接放置指向另一条组合；Bridge 地图编号 1 会指向原生的 bravo_A，实际追加的 foreign_A 位于 2。这里没有将游戏崩溃或具体战斗画面作为实测结论。

同一错误来源还会影响人物 HP 目录值，以及人物/怪物战利品引用：测试中原生有效的 base_token 被编辑器标为 `SuspectedUnused` 并默认隐藏，未被游戏采用的 mod_token 却被标为 `ConfirmedActive`。本轮没有使用这个最小人物夹具测试生成新人物的二进制保存，不扩大声称该路径已完成验证。

应修正的是**根据实际打开请求选择提供者**，不是删除总体型超限跳过规则、空组合占号规则或大小写正确的物理目录回退。固定路径共用调用方需一起检查，但不能把所有枚举文件都改为固定路径规则。

## P2：Effect 文件误用 flags 0，提前丢弃仍会加载的定义

位置：`HeroClassCatalog.cs:55` → `HeroClassCatalog.DefinitionResolution.cs:17`。

原生 Effect 初始化 `0x1404E494D–0x1404E497D` 明确以 **mode 1、flags 1** 查询 `effects/`。编辑器却使用 flags 0 的 `Resolve`。

`0x140247C1F–0x140247C5C` 中，flags 的 bit 1 改变了合并方式：匹配位于已有字符串开头时删除该槽位、追加新路径；匹配位于带挂载前缀的路径内部时直接追加。不能用 flags 0 的“首个包含路径原位替换”代替，也不能把 flags 1 不加区分地当作永远追加。

最小复现：下方 A、上方 B 两个 Mod 都列出 `effects/a.effects.darkest`：

```text
A：effect: .name FLAGS_RETAIN .disease flags_quirk
B：effect: .name FLAGS_RETAIN .duration 3
```

另有有效人物技能引用 FLAGS_RETAIN，flags_quirk 是有效、随机概率为 0 的怪癖。原生保留两份带挂载来源的 Effect 文件：先读 A，再读 B。Effect 同名复用对象位于 `0x1404E4C8F`，`.disease` setter 位于 `0x1404E6558–0x1404E6573`；其帮助函数 `0x14036C950` 在字段缺失时直接返回，不清空旧值。因此这个技能仍有赋予 flags_quirk 的线索。

编辑器合并阶段只留下 B，实际输出 **0 条线索，原生应有 1 条**。将 B 文件改为不同文件名后，编辑器也得到 1 条，证明 `.disease` 缺省保留的现有解析规则本身有效，错误发生在文件列表。影响已确认到人物运行时怪癖线索、相应搜索/显示；不能表述成所有怪癖定义或 HP Buff 计算都因此失效。

历史上，基线 `7b9a67f` 的 Effect 路径已采用单个优先提供者；`4e1db6b` 接入公共原生解析器后仍套 flags 0，`3b19139` 保留了该调用。应补齐 Effect 的 flags 1 入口及与 flags 0/9 的差异测试，保留已有 Effect 字段级规则。

## P2：District 文件误用 flags 0，漏掉城镇建筑的物品供给引用

位置：`QuantityItemReferenceAnalyzer.SourceDiscovery.cs:82–86`。只有 Curio 分组走 flags 9；`json:Districts` 仍落入普通 flags 0 分支。

原生 District 初始化 `0x140559EB3–0x140559EE2` 明确以 **mode 1、flags 9** 查询 `campaign/town/districts/`。bit 8 在 `0x140247DFC–0x140247E42` 给 Base 路径加 `>`，保留物理来源；bit 1 使带挂载前缀的来源作为独立输入。`0x14055A210–0x14055A311` 对这些文件逐项读取、登记建筑。

复现使用两个 Mod 的相同路径 `campaign/town/districts/a.districts.json`：

- 下方 A 定义一个独有建筑，分别向 estate 和 provision 供给 town_token、raid_token。
- 上方 B 的同路径文件为 `{"buildings":[]}`。
- 两个物品都是有效 Mod 定义，没有其他引用，当前合成存档也没有持有它们。

原生仍保留 A 文件及其建筑；编辑器只读 B，将两个物品分别在小镇、副本目录中判为 **`SuspectedUnused` 并默认隐藏**。把 B 改成不同文件名后，两项都正确成为 `ConfirmedActive`。本例没有使用相同建筑 ID 的重复定义，不依赖尚未确认的建筑重复 ID 规则。

引用文件先按路径折叠的历史实现可追溯至 `fb7b0fd`，`3d2bd9c` 改为共用解析器；`c14247a` 添加 District 专门字段解析，但没有调整查询参数；`3b19139` 仍保留 flags 0 的默认分派。应让 District 分组使用已存在的 flags 9 路径，保留供给目标对应的小镇/副本分类。

## 验证与审查边界

- 固定路径：8 个资源夹具，本地/工坊各包含大小写差异、DLC 前缀差异及两种正确对照；4 个差异、4 个对照，24 次战斗类型比较。
- Effect 与 District：另 4 个资源夹具，本地/工坊各包含同路径触发与不同路径对照；每个夹具同时核对 Effect 线索、小镇供给、副本供给。
- 6 次 DSON 地图保存/重读，3 次 Bridge 维护检查。核对每个最终地图的 DSON 文件头以及两个地图提交完成标记。
- `verify_results.py` 最终核对全部结果、原生反汇编文件哈希、当前 Core 哈希、HEAD 和源码/正式测试无改动，执行成功。
- 探针初版将实际会出现的“超限组合跳过”诊断误断言为空，修正了验证断言；首次二进制脚手架又因比较正斜杠/反斜杠路径而误报“合成 Mod 未激活”，统一该临时根路径后重跑。最终全部验证命令退出 0；这两项属于验证脚手架错误，不是产品新增问题。
- 独立只读审查员检查了非战斗消费者，提出两项查询参数问题；主审重新提取固定 EXE 证据并用当前 DLL 复现两项及对照。其余已核对的 Buff、怪癖、露营技能、事件、升级树和支持的 JSON/Loot 调用参数未发现新的差异；这不等于完整验证它们的全部游戏机制。XML 人名池的原生查询入口未获得充分对应证据，不作为已确认问题。

主要证据：

```text
workspaces/historical-rule-review21-20260914/
  native_evidence.py、native-evidence.json、native_*.txt
  Program.cs、ReviewProbe.csproj、results.json、persistence.json、probe-binary.log
  queryflags/Program.cs、queryflags/QueryFlags.csproj、query-flags.json、query-flags.log
  verify_results.py、verification.json
```

复现命令（当前探针固定为本轮修复前 DLL）：

```powershell
dotnet run --project workspaces/historical-rule-review21-20260914/ReviewProbe.csproj -c Release -- E:/数据文件/SelfMod/DarkestDungeonSaveEditor
dotnet run --project workspaces/historical-rule-review21-20260914/queryflags/QueryFlags.csproj -c Release -- E:/数据文件/SelfMod/DarkestDungeonSaveEditor
python -B workspaces/historical-rule-review21-20260914/verify_results.py
```

本轮没有功能变更 diff，按协作规则不另触发完成审查员。以上为待修复发现，未宣称产品问题已解决。
