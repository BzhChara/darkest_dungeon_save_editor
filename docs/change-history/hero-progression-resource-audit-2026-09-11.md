# 历史修改第十三轮审核：升级树文件筛选与人物经验配置，2026-09-11

## 提交与范围

按用户要求，先提交上轮资源目录及人物／怪物注册修复：`cd4a3ac595134c26cb7ff8194e896bde5c25ab27`，标题 `fix: align manifest directory queries and actor discovery`。该提交包含上轮已经验证、独立复核的 22 个文件；提交后工作区干净。

随后只读审核历史代码。本轮确认两项条件性错误，均涉及人物成长资源的发现与选择，尚未实施修复。没有修改产品代码、正式测试、游戏文件、真实 Mod 或存档。新增内容仅为本报告、记录索引，以及 Git 忽略工作区内的隔离审查工具和证据。

| 优先级 | 已确认问题 | 可观察影响 |
| --- | --- | --- |
| P2 | 升级树文件名仍按不区分大小写的后缀筛选 | 游戏不枚举的文件被当成有效升级树，改变人物护甲等级、初始 HP 和升级购买记录 |
| P2 | 经验阈值读取用带 DLC 前缀的来源路径与根路径比较 | 已选出的有效经验配置被漏掉，等级下拉框只剩 0，生成 1 级人物被拒绝 |

这两项不是推翻树 ID 最后匹配规则、清单门槛或标准路径读取规则。问题分别发生在“哪些文件有资格参与”及“如何识别挂载后的同一路径”。

## 验证依据

- 游戏静态依据固定为 Windows x64 build 27890，EXE SHA-256：`35E5A653279992564809FF8406FEBD5A02A7D6961044781B1296B38A7096F59B`。本轮没有启动游戏、修改游戏内存或声称进行了新一轮实机测试。
- 审查程序直接引用已提交修复对应的 Release Core DLL，不通过工程引用重编产品。Core、App、正式测试输出及审查程序实际加载的 Core 哈希一致：`9E05EE0A2B7996C7595649870CABF41C427332720A7102B03D0F69DEEF055D11`。
- 完成 **42 组隔离对照**：30 组升级树文件筛选、12 组经验配置路径。15 组升级树反例和 2 组经验配置反例复现上述两项问题；其余 25 组为正常／排除对照。
- 本地 Mod 的 5 组升级树案例均调用实际人物生成器、`StagecoachHeroSaveEditor.AddCandidate` 和现有 Java DSON 编解码器；每组往返 town 与 upgrades，合计 **10 次 DSON 往返**。核对了新人物保存的 `current_hp` 和护甲树购买码，没有只停留在界面预览。
- 提交前核对的上轮完整合同测试为 **43 PASS / 0 FAIL / 0 SKIP**，Release 构建 **0 警告 / 0 错误**。本轮再次检查这些记录与 DLL 哈希，未将它们表述为此次重新运行的完整套件。
- 本轮新增证据：[隔离程序](../../workspaces/historical-rule-review13-20260911/Program.cs)、[最终执行日志](../../workspaces/historical-rule-review13-20260911/probe-final.log)、[42 组原始结果](../../workspaces/historical-rule-review13-20260911/probe-results.json)、[验证脚本](../../workspaces/historical-rule-review13-20260911/verify_audit.py)、[验证结果](../../workspaces/historical-rule-review13-20260911/verification.json)。这些工作区文件不随 Git 分发。

隔离工具最初有一处 C# 多行原始字符串语法错误，验证脚本也曾直接比较斜杠形式不同的同一物理路径；均仅修正于忽略工作区后重跑。最终执行及验证退出码均为 0。这不是产品构建失败，也没有为使结果通过而修改产品实现。

## 1. 升级树文件名的旧后缀判断会误读

### 当前入口

`src/DarkestDungeonSaveEditor.Core/HeroClassCatalog.SourceDiscovery.cs`：

- 第 42 行：Base／模式／官方 DLC 用 Windows 通配符 `*.upgrades.json` 枚举，未再应用原生大小写敏感的文件名过滤。
- 第 113–116 行：Mod 已检查清单中的 `upgrades/` 目录，但仍调用 `EndsWith(HeroUpgradeSuffix, StringComparison.OrdinalIgnoreCase)`。

原生升级文件加载入口 `0x1403E8883` 指定查询 `.*upgrades/.*\.upgrades\.json$`，`0x1403E889D` 传给 `IO_FindFiles`，随后 `0x1403E8920` 逐文件解析。两个点均为字面点，文件名匹配区分大小写。见[本轮反汇编片段](../../workspaces/historical-rule-review13-20260911/native_1403e880c.txt)，以及[此前升级树实测研究](antiquarian-upgrade-probe-2026-09-08.md)和[当前查询规则表](../resource-duplicate-semantics.md)。

因此 `upgrades/z.upgrades.JSON`、`upgrades/z.UPGRADES.json` 即使列在 Mod 清单中，也不能由这个查询提供有效升级树；编辑器目前却会读取。

### 已执行的反例

隔离职业 `audit` 的基础护甲 0 级 HP 20，护甲 1 级 HP 40。正常文件中 `audit.armour` 的购买码 `0` 在人物 1 级开放。另一个文件把同一树 ID 的要求改为人物 4 级。

生成 1 级人物时：

| 另一个文件／清单记录 | 按原生文件查询的预期 | 当前编辑器 |
| --- | --- | --- |
| `upgrades/z.upgrades.json` | 有效；护甲 0 级、HP 20、无护甲购买码 | 正确 |
| `upgrades/z.upgrades.JSON` | 不参与；护甲 1 级、HP 40、购买码 `0` | **误用此树，护甲 0 级、HP 20、没有购买码** |
| `upgrades/z.UPGRADES.json` | 不参与；护甲 1 级、HP 40、购买码 `0` | **同上** |
| `upgrades/z.upgradesXjson` | 不参与；护甲 1 级、HP 40、购买码 `0` | 正确排除 |

上述结果分别覆盖 Base、模式、单个官方 DLC、本地 Mod、工坊 Mod、启用 DLC 前缀内的 Mod。另有“清单后缀小写、磁盘后缀大写”的对照：Mod 按小写清单记录发现后，可以通过 Windows 打开对应文件；此时覆盖是有效的，应继续保留。物理来源直接枚举到的大写文件名则仍不满足原生查询。

本地 Mod 的两个大写清单反例实际编码为 HP **20**，护甲购买码缺失；正常排除对照编码为 HP **40** 并保留购买码 `0`。这证实偏差进入人物持久化数据。此处没有证明生成存档无法进入游戏，也没有将其解释为改变已有英雄的 HP。

### 历史来源与建议

`git blame -C -C -C` 将不区分大小写的 Mod 后缀判断追溯到初始基线 `7b9a67f`。`3d2bd9c` 删除旧升级文件位置兼容、统一为 `upgrades/` 目录时保留了该后缀语义；`cd4a3ac` 修正的是清单目录大小写，未修正升级树文件名。

建议让升级树定义读取使用已核实的文件查询，并同时覆盖物理来源与清单来源。`NativeResourceFileRules` 中升级资源引用查询已经有大小写敏感的 `\.upgrades\.json` 谓词，应避免定义读取与引用分析对同一个文件给出不同资格判断。正常小写文件的树 ID 最后匹配、整个树替换、购买码及哈希保护均应保留。

## 2. DLC 挂载后的有效经验配置被根路径检查丢弃

### 当前入口

`HeroClassCatalog.cs` 第 64 行先对经验配置执行有效文件解析；`HeroClassCatalog.Progression.cs` 第 117–119 行却要求结果的 `file.RelativePath` 必须等于：

```text
campaign/roster/roster.variables.json
```

`NativeContentFileResolver` 会按挂载路径合并覆盖，但为显示来源、Bridge 输出等用途，保留胜出文件原本的路径。当胜出来源位于启用的 DLC 挂载根内时，结果可能是：

```text
dlc/audit_feature/campaign/roster/roster.variables.json
```

它与根文件是同一个挂载后路径，前面的覆盖阶段已将根文件替换。然而最终根路径字面比较不接受 DLC 前缀，因此没有匹配项，返回空经验阈值。问题并不是文件未列入清单或磁盘不存在。

### 原生依据与隔离结果

原生 `0x1404686E0` 在 `0x140468711` 查询标准 `campaign/roster/roster.variables.json`，`0x14046877B` 读取同一路径的 JSON，`0x14046890F` 开始读取 `resolve_level_thresholds`。见[对应反汇编](../../workspaces/historical-rule-review13-20260911/native_1404686e0.txt)。普通路径查找 `0x140248040` 从附加挂载中查找提供方，拼接挂载前缀；没有提供方时才回到默认设备，见[路径查找片段](../../workspaces/historical-rule-review13-20260911/native_140248040.txt)及[此前挂载研究](runtime-mount-research-2026-09-07.md)。这里不是任意嵌套目录的递归读取规则。

隔离基础经验表设为 `[0,10,20,30,40,50,60]`，覆盖表设为 `[0,7,14,21,28,35,42]`：

| 来源和路径 | 实际目录阈值 | 生成 1 级人物 |
| --- | --- | --- |
| Base／模式／本地 Mod／工坊 Mod 的根标准路径 | 覆盖表 | 正确，XP 7 |
| 单个启用 DLC 的标准相对路径 | **空数组** | **拒绝：人物等级必须在 0 到 0 之间，当前为 1** |
| Mod 清单中启用 DLC 前缀下的标准路径 | **空数组** | **同上** |
| 上述六种来源的 `campaign/roster/notes/roster.variables.json` | 基础表 | 正确排除非标准路径，XP 10 |

所有样例均有有效基础经验表，且为职业提供可用姓名、技能和升级树。两个反例的 0 级人物预检仍可用，说明错误来自经验配置选择，而非其他生成条件。界面 `MainWindow.Presentation.cs` 第 20–29 行直接据阈值构建等级选项，因此同样只显示 0 级；Core 生成器也拒绝高等级请求。

这里使用单个 DLC 挂载，未依赖尚有研究边界的多个 DLC 之间的完整顺序。Mod 前缀案例验证了当前有效文件解析器的同类漏读，不扩展为全部多挂载冲突组合已逐一实机确认。

### 历史来源与建议

根路径字面比较同样可追溯到 `7b9a67f`。`4e1db6b` 将人物目录有效文件解析从 `ContentFileOverlay.Resolve` 改为 `NativeContentFileResolver.Resolve`，挂载别名由统一解析器合并后，这个末端检查仍沿用旧假设。

建议按启用挂载去掉前缀后识别标准经验配置，同时保留真实来源路径。应继续排除 `notes/roster.variables.json`，保留缺失／异常阈值的保护，以及 Mod 同路径优先级；不能简单删除根路径限制后取任意同名文件。

## 本轮结论与边界

- 已提交上轮修复；本轮发现的两项问题尚未修改，报告和索引为提交后的新文档。
- 没有发现新的证据需要撤销人物／怪物注册查询、清单目录区别、树 ID 最后匹配、怪癖／Buff 规则或空遭遇占号规则。本轮的可执行深查范围是人物成长资源，不代表再次完整实测所有战斗、物品和存档流程。
- 未检查用户当前 `profile_1` 是否正在使用上述触发文件，因此这里是已复现的条件性错误，不是当前档案已受影响的结论。
- 本轮没有产品代码修改，按只读审查任务要求未触发额外完成审查者。初始提交沿用上轮已完成的独立复核；审查者没有替代本轮的原生证据及可执行对照。
