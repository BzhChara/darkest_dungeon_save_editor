# 历史修改第二十轮审核：公共资源合并与战斗编号（2026-09-14）

本轮先按用户要求提交上一轮修复，再只读审查历史实现。已提交 `6cf2425 fix: align hero skin slots and initial skill selection`，共 22 个文件；提交后工作区干净。提交前核对了上一轮验证记录对应的源码、测试和 Core 产物哈希，以及 `git diff --check`，没有重复运行未变化代码的全量测试。

本轮确认 **1 个 P1 根因**，同时影响资源数值选择和战斗编号。没有修改产品源码、正式测试或发布产物，没有操作真实游戏进程、存档、Mod 或 Steam 配置。本记录及目录索引是提交之后新增的审查文档，尚未提交。

## P1：附加挂载按完整相对路径去重，不能复现原生的子串替换槽位

位置：

- `src/DarkestDungeonSaveEditor.Core/NativeContentFileResolver.cs:119–139`，`slots` 使用忽略大小写的完整相对路径字典，只有完整键相同才替换槽位。
- `BattleEncounterCatalog.RuntimeOrder.cs:44`、`QuantityItemCatalog.Definitions.cs:33`、`TrinketCatalog.cs:43` 都使用这份公共结果。

`git blame` 确认这段槽位合并来自 **`4e1db6b`（2026-09-07）**，当时将战斗多文件读取经验推广到公共资源目录。它不是本轮提交的人物皮肤、初始技能修复造成的回归。此前测试覆盖了完全同路径覆盖、普通不同路径追加和 DLC/root 别名，但没有覆盖下述路径包含关系。

### 原生证据与边界

只读检查安装的 Windows x64 build 27890，EXE SHA-256：

`35E5A653279992564809FF8406FEBD5A02A7D6961044781B1296B38A7096F59B`

`IO_FindFiles` 的附加挂载合并 `0x140247970`，在 **返回路径模式 1、flags 0** 下：

1. `0x140247B9C–0x140247BDC` 去掉当前挂载前缀，得到新文件的相对路径字符串。
2. `0x140247C00–0x140247C2D` 依次对已有完整路径执行 `strstr(existingPath, relativePath)`。
3. 找到首个包含该字符串的槽位时，`0x140247C5E–0x140247C70` 原位替换；找不到才追加。

本轮直接解析固定 EXE 的导入表，确认 IAT **`0x140C62BC8` 是 `VCRUNTIME140.dll!strstr`**，不是完整路径相等判断。这个函数比较区分大小写。

确认的调用方包括：

| 消费者 | 查询/调用位置 | 参数 |
| --- | --- | --- |
| 战斗 MashGuide | `0x1404C9239` | mode 1、flags 0、递归深度 255 |
| 物品与库存配置 | `0x1404C7FFB`、`0x1404C80A2` | mode 1、flags 0 |
| 饰品 | `0x1403E834D` | mode 1、flags 0 |
| Buff | `0x1404A32D7` | mode 1、flags 0 |

以下复现只声明已实际检查的物品、饰品和战斗结果，不把 Buff 或其他消费者的全部后果视为已验证。Base 的初始枚举直接建立列表，不执行同一附加挂载合并循环。人物皮肤的模式 0 目录名比较、根据人物/怪物 ID 构造路径后的直接打开也不能直接套用此分支。

### 最小战斗复现

一个启用的本地或创意工坊 Mod，清单同时列出这三份有效文件，每份各声明一条走廊、一条房间、一条首领战斗：

```text
dungeons/cove/archive/dungeons/cove/a.cove.2.mash.darkest  → alpha_A
dungeons/cove/a.cove.2.mash.darkest                       → bravo_A
dungeons/cove/z.cove.2.mash.darkest                       → zulu_A
```

三个怪物均有有效标准定义，体型均为 1。文件都满足地区查询，内容、怪物 ID 和单条组合本身没有异常。

深层路径先被枚举。随后正式目录的 `dungeons/cove/a.cove.2.mash.darkest` 是深层路径的子串，原生分支将原槽位替换成正式文件；编辑器因为完整相对路径不同，保留两份文件。

| 项目 | 原生分支得到的结果 | 当前编辑器实际结果 |
| --- | --- | --- |
| 当前文件序列 | 正式 a、z | 深层 a、正式 a、z |
| bravo_A 编号 | 0 | 1，允许直接写入 |
| zulu_A 编号 | 1 | 2，允许直接写入 |
| Bridge 下一条编号 | 2 | 3 |
| 自动维护统计的当前表长度 | 2 | 3 |

走廊、房间、首领三个独立编号表都复现该偏移。当前的指纹和写入前校验再次调用同一错误合并器，所以文件未改变时仍会放行，不能纠正编号。

隔离保存进一步确认：选择 bravo_A 实际保存 `mash_index=1`；跨地区生成 foreign_A 后，Bridge 安装及地图保存都使用 `mash_index=3`。对照原生表，前者会指向 zulu_A，后者超过实际追加表的最后编号 2。这里只确认错误绑定及写入，未把可能出现的游戏错误提示或崩溃作为实测结论。

### 同一根因的物品和饰品表现

同样将 `inventory/a.inventory.items.darkest` 和 `trinkets/a.entries.trinkets.json` 分别放入正式目录及包含完整相对路径的深层目录，两份都列入清单：

| 对照内容 | 原生分支应保留的正式定义 | 编辑器实际读取 |
| --- | --- | --- |
| 相同物品 `(estate, shared)` 的堆叠上限 | 13 | 7（来自深层文件） |
| 相同饰品 `shared` 的 `quest_uses` | 9 | 2（来自深层文件） |
| 只在被替换深层文件定义的 `ghost` | 不进入本次定义列表 | 仍进入物品、饰品定义目录 |

问题发生在定义解析之前的文件列表。物品、饰品的“重复 ID 取首个定义”规则本身没有被本轮推翻。物品引用判定仍是后续步骤，不能把上述定义列表中的多读直接表述成“所有幽灵物品都能在 UI 新增”。

### 对照与保存验证

使用 **当前 `6cf2425` 的固定 Core DLL**，SHA-256：

`F7359D5FAB0D8A212760FCB97599CA460D18F136AC6634FCD3790AD22C256B55`

执行了 8 组独立资源夹具、24 次战斗类型比较：

- 本地、创意工坊各 1 组路径包含碰撞：都复现差异。
- 6 组对照全部符合原生分支：Base 初始列表保留同一组深层文件；不同文件名的普通嵌套文件保留；Mod 清单未列出的深层文件排除。
- 另用真实 `ActiveContentResolver` 识别隔离本地 Mod，创建隔离 Bridge，分别对三个战斗类型执行直接放置和 Bridge 替换。
- JSON 保存路径与 **6 次 DSON 二进制保存/重读**均复现错误编号；DSON 文件头核对为 `01 b1 00 00`。每组两次地图提交均产生完成标记。
- 三组保存后的 `ReconcileAsync` 都返回未变化，证明自动维护继续信任同一套错误索引。
- 最终重新核对源码和正式测试相对 `6cf2425` 无差异，Core 哈希未变。

这是 **固定版本反汇编的分支对照模型 + 当前编辑器执行/保存验证**，不是新一轮游戏实机运行。分支模型使用 ASCII 短路径说明槽位行为；临时资源所在的 Windows 绝对目录不是游戏安装路径，不据此推导游戏的长路径截断行为。没有检查用户当前 profile 是否实际含有这种路径碰撞。

探针初次调用曾因反射未显式传入可选参数而失败，补齐后重跑完成。首轮保存使用 JSON 种子，独立校验发现它不能作为二进制验证；随后显式编码 DSON 种子，再完成全部六次保存和文件头核验。这两项属于验证脚手架修正，不是本轮新增的产品问题。

证据保存在未跟踪的隔离目录 `workspaces/historical-rule-review20-20260914/`：

- `native_evidence.py`、`native_140247970.txt`、调用方反汇编、`native-hash.json`：原生代码与导入表。
- `Program.cs`、`ReviewProbe.csproj`、`results.json`：8 组目录与数值对照。
- `json-persistence.json`、`persistence.json`、`probe-binary.log`：JSON 与 DSON 保存结果。
- `verify_results.py`、`verification.json`：最终数量、文件头、源码和 DLL 核验。

可复现命令：

```powershell
dotnet run --project workspaces/historical-rule-review20-20260914/ReviewProbe.csproj -c Release --no-restore -- E:/数据文件/SelfMod/DarkestDungeonSaveEditor
python -B workspaces/historical-rule-review20-20260914/verify_results.py
```

## 建议修复范围

将附加挂载的模式 1、flags 0 文件合并按已证明的查找/原位替换流程实现，再统一验证目录、直接放置、Bridge 安装和维护。Base 初始列表、正常不同路径的子目录资源、清单准入、Mod 顺序、各资源重复 ID 规则、人物皮肤与直接文件打开应继续遵守各自入口。

现有“完整键相同才替换”的假设及其规则文档需要调整。修复不需要删除作者的深层文件、清单内容或用户已有地图。大小写碰撞与其他返回模式的完整消费后果尚未单独复现，不在本报告中计为第二个已确认问题；现存多 DLC 挂载顺序等限制也不能仅凭本次证据整体删除。

本轮是只读代码审查，没有功能变更 diff，依协作规则未触发完成审查员。产品修复待用户确认。

后续：用户已确认修复，实施与验证记录见[公共资源合并与战斗编号修复](resource-overlay-slot-fixes-2026-09-14.md)。上述探针针对审核时的旧 DLL，保留为修复前证据；修复后的回归使用正式契约测试 `--overlay-slots`。
