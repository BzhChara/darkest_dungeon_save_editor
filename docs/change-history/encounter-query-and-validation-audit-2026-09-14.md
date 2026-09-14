# 历史修改第二十二轮审核：遭遇集合查询与旧校验假设（2026-09-14）

后续进展：用户确认后已实施对应修复，见[修复与验证记录](encounter-query-and-validation-fixes-2026-09-14.md)。下文保留审核时的代码位置和结果。

本轮先按用户要求提交上一轮修复：`d170bf2fdaecac7d8e282686b53cfa7f9505aee8`，标题 `fix: honor canonical resource requests and native query flags`，共 39 个文件。提交前核对 39 份源码、正式测试及文档和 3 份 Core 输出的 SHA-256，均与上一轮验证记录一致；`git diff --check` 通过，提交后工作区干净。

上一轮全量为 73 PASS、0 FAIL、0 SKIP；最后的 arena 条件修正后另有 5 组受影响专项通过，以及 Release 零警告、零错误和独立完成审查。没有把修正前全量声称为修正后的新一次运行；本次没有对未变化代码重跑全量。

随后只读审核确认 **1 个 P1、2 个 P2**。以下发现尚未修复。本轮没有修改产品源码、正式测试、现行规则文档或 Release 程序，也没有修改真实存档、游戏资源、活动 Mod 或 Steam 配置。新增的本报告和目录索引尚未提交；所有探针与合成数据均在被忽略的 `workspaces/historical-rule-review22-20260914/`。

## 证据与方法

- 当前 Core DLL SHA-256：`7e7786df779733ff78c27ea106ae1e3e195980fa0f581e45df4b29f592cfe877`。Core、App、正式测试输出一致。隔离探针直接引用并校验这份 DLL，没有重新编译产品。
- 固定游戏：Windows x64 build 27890，EXE SHA-256：`35e5a653279992564809ff8406febd5a02a7d6961044781b1296b38a7096f59b`。
- 从固定 EXE 重新提取 10 个函数的反汇编，核对 MashGuide 查询参数、四个怪物槽的初始化、AddMashEntry 和存储层调用分支。
- “原生应有结果”来自固定 EXE 的分支分析；“编辑器实际结果”来自当前 DLL 的执行式目录、校验、维护及合成 DSON 保存结果。**本轮没有重新进入游戏实测，也没有确认 profile_1 当前是否包含这些触发文件。**

## P1：按文件名任意位置的关键词分类，会漏算标准战斗并写错编号

代码位置：

- `BattleEncounterCatalog.cs:719–734`：`ClassifyFile` 只检查文件名任意位置是否有 `.conditional.`、`.additional.`，并忽略大小写。
- `BattleEncounterCatalog.SourceDiscovery.cs:251–255`：先用上述分类决定是否校验地区表名；被判为特殊表后，仅检查难度与 mash 后缀。
- `BattleEncounterCatalog.RuntimeOrder.cs:47–54`、`BattleEncounterCatalog.cs:632`：这个猜测继续决定查询分组、解析条目所属集合及标准表计数。

### 原生依据

`MashGuide::Load` 在 `0x1404C91B1` 构造 `dungeons/%s/`，在 `0x1404C91D8` 构造 `.*%s.%d.mash.darkest`，再于 `0x1404C9239` 查询文件。第二个字符串参数是本次查询的**表名**。

- 标准表调用 `0x1404ADC22–0x1404ADC2E` 传入地区 ID，例如 `cove`。
- 条件表调用 `0x1405E706A–0x1405E7076` 明确传入 `conditional`。
- 额外表调用 `0x1405E7225–0x1405E7231` 明确传入 `additional`；`0x1405E6555`、`0x1405E746D` 的其他调用同样传入 additional。

因此，文件属于哪个集合，应由对应查询是否匹配决定。文件名前部任意位置的词不能替代查询；大小写和分隔点的通配语义也必须保持。

### 最小编号反例

海湾难度 2，文件均位于正确目录并在 Mod 场景列入清单：

```text
dungeons/cove/a.conditional.cove.2.mash.darkest  → alpha_A
dungeons/cove/b.cove.2.mash.darkest              → bravo_A
```

每个文件各有一条 hall、room、boss，所有怪物均有体型 1 的有效标准定义。

第一份文件匹配原生的 `.*cove.2.mash.darkest`，所以它是标准表输入。编辑器却因为前部出现 `.conditional.`，将其转入条件集合。

| 项目 | 原生分支结果 | 当前编辑器实际结果 |
| --- | --- | --- |
| 标准 alpha_A 编号 | 0 | 被错误归入条件集合，没有标准编号 |
| 标准 bravo_A 编号 | 1 | 0，直接写入校验通过 |
| Bridge 新增 foreign_A 的编号 | 2 | 1，Bridge 安装与地图写入通过 |
| 后续维护 | 应基于两条原生标准记录 | 使用同一份少算的表，未发现编号差异 |

Base、模式、官方 DLC、本地 Mod、工坊 Mod 五类来源均复现目录与编号差异，走廊、房间、首领三类表均受影响。进一步在 Base、本地 Mod 两类来源各执行三种战斗类型的 DSON 保存：**6 次直接放置实际写入 0，6 次 Bridge 替换实际写入 1**；重新读取一致，6 次自动维护均返回未变化。这里的“实际写入”指合成二进制存档，不是实机战斗画面。

### 同根问题还造成误读、漏读和错分类

均在 `dungeons/cove/`，难度为 2：

| 文件名 | 原生查询所属集合 | 编辑器结果 |
| --- | --- | --- |
| `a.additional.cove.2.mash.darkest` | 标准 | 错判额外，漏算标准编号 |
| `a.CONDITIONAL.cove.2.mash.darkest` | 标准 | 错判条件，漏算标准编号 |
| `a.cove.conditionalX2.mash.darkest` | 条件；分隔点可匹配 X | 漏读 |
| `a.cove.additionalX2.mash.darkest` | 额外 | 漏读 |
| `a.cove.CONDITIONAL.2.mash.darkest` | 不匹配上述三个查询 | 错判为条件并进入 Bridge 来源 |
| `a.conditional.unused.2.mash.darkest` | 不匹配上述三个查询 | 错判为条件并进入 Bridge 来源 |
| `a.conditional.additional.2.mash.darkest` | 额外 | 错判条件 |

正常 `a.cove.2.mash.darkest` 和 `a.cove.conditional.2.mash.darkest` 作为对照，结果正确。共 50 个来源/文件名夹具、150 次三类型编号对照。

### 历史来源与修改方向

`ClassifyFile` 的关键词判断从 `a5f38b8`（2026-09-05）一直保留。`9158362` 修正 mash 文件名分隔点、`55e214c` 修正目录与地区大小写时，仍把条件/额外判断当成可靠前提。本轮提交没有新增该判断。

建议删除按关键词位置猜测集合的规则。让标准、条件、额外查询分别携带明确的集合身份，使用原生表名与难度表达式进行筛选，并让当前目录、全局 Bridge 来源、写入前复核和维护使用同一结果。不能只修 UI 分类，或直接把所有特殊表计入标准表；原生的三套集合仍需独立。

## P2：Bridge 把合法重复加载位置误当成多个冲突来源

位置：`BattleEncounterCatalog.cs:421–427` 的 `SingleOrDefault`；真实生成入口 `ManagedBattleEncounterBridgeService.cs:79–80` 调用这项校验。

### 触发场景

Base 存在以下两个有效来源位置：

```text
dungeons/ruins/archive/dungeons/ruins/a.ruins.2.mash.darkest
dungeons/ruins/a.ruins.2.mash.darkest
```

活动 Mod 提供并列出 `dungeons/ruins/a.ruins.2.mash.darkest`，其中组合为 bravo_A。

原生 flags 0 先替换第一个包含路径的槽位，剩余无前缀 Base 请求再通过 OpenFile 打开 Mod 文件。因此最终两个加载位置都读取同一份 Mod 文件。这是已经修正并验证的加载规则：**同一物理文件的两次读取都占战斗编号，不能从运行时表中去重。** 本轮重新核对 `0x140247C0A` 的首个子串匹配、`0x140248051` 后的打开路径。

当前目录正确给出两个标准位置 0、1，直接写入校验、追加计数和维护计数均正确；全局 Bridge 列表也能发现这份组合。但尝试从其他地区桥接它时，`SingleOrDefault` 找到两个同相对路径的结果，直接抛出：

```text
Sequence contains more than one matching element
```

本地、工坊来源以及三种战斗类型均复现。对照去掉 Base 的深层同名来源后，Bridge 校验恢复正常。

另通过 3 组实际 `EnsureEncounterAsync` 合成 DSON 场景确认，失败发生在安装和地图写入之前：游戏配置、地图字节未改变，也未产生第二份 Mod 包。这里会使相关跨地区来源不可用；**并非所有跨地区生成失效**，本地区可以直接寻址的同组合仍能走 UI 的直接写入路径。

### 历史来源与修改方向

这条唯一性假设同样始于 `a5f38b8`。过去最终文件列表被合并成唯一文件，问题被掩盖。`3b19139` 正确恢复原生重复加载位置后，旧校验没有同步调整，因而暴露为回归；本轮 `d170bf2` 未改变这条校验。

已有 `ResourceOverlaySlotContractTests.cs:71–104` 验证重复读取的直接编号、追加计数和维护计数，但没有把这份重复来源当成跨地区 Bridge 选择执行校验。这就是此前测试未覆盖的调用路径。

建议在 Bridge 的来源复核中区分“同一来源记录被读取多次”和“来源发生冲突或变化”，按实际提供者、路径、哈希、记录序号和组合验证来源身份。保留运行时表的重复位置；不能为了让 `SingleOrDefault` 通过而对整个遭遇表全局去重，也不能不校验来源就直接取第一份。

## P2：无参数或省略 `.types` 的合法记录仍连带禁用后续正常战斗

位置：`BattleEncounterCatalog.cs:652–656,676–680`。两处将记录加入 `unparsedTypes` 后跳过，使同类型正常记录失去编号，并使 Bridge 追加、维护复核一起被拦截。

### 本轮补充的原生分支证据

对于已经被 LineReader 正常读出的 hall/room/boss 声明：

1. `0x1404C9E3A–0x1404C9E5C` 每次都清空四个 32 字节怪物槽的字符串开头。
2. `0x1404CA041` 查找 `.types`；字段缺失时，`0x1404CA048` 跳到 `0x1404CA2E1` 继续解析其他数据，**没有跳过 AddMashEntry**。
3. 字段存在但无值时，四次字符串复制得到空字符串，随后同样到达继续分支。
4. `0x1404CA76A` 仍调用 AddMashEntry。`0x1404CB810–0x1404CB909` 中空 ID 解析不到怪物时总体型为 0，`0x1404CB90C` 不走大于四格的丢弃分支，记录进入对应数组。

因此，在没有空 ID 哈希碰撞等其他不确定因素的通常资源中，下列两种记录与已经支持的 `.types ""` 一样，会保留一个不可放置的编号：

```text
hall: .chance 0
hall: .chance 0 .types
```

这指的是带有合法 `hall:` 头的记录，不是普通空白行，也不是任意损坏文本。没有宣称空组合本身能正常进入战斗。

### 执行式复现

在某一种标准表依次放入正常 alpha_A、上述空记录、正常 bravo_A。原生编号应为 alpha_A=0、空记录=1、bravo_A=2；当前编辑器给 bravo_A 的 `MashIndex` 为 null，并拒绝直接写入、Bridge 追加和维护。

Base、本地、工坊三类来源 × 走廊/房间/首领 × 缺失字段、裸字段、字段后仅空白、显式空引号对照，共 36 组：27 组误拦稳定复现，9 组 `.types ""` 对照保持编号 2 且校验通过；同一夹具里未受影响的其他两种表仍可直接写入。此项只验证到目录、写入预检和维护，没有强行越过保护保存。

### 历史来源与修改方向

原先的解析跳过逻辑出自 `a5f38b8`，`730c01e` 加上整类型无法证明编号的保护。`3d2bd9c` 修正了显式空引号，但保留这两个分支。`EmptyEncounterSlotContractTests.cs:100–107` 还明确以“缺少 `.types` 尚未验证”为由要求继续拦截；当前静态分支证据已补上这个知识缺口。

建议将这两个已能确认占号的分支纳入空组合处理，仅禁止放置空记录本身，并补齐后续正常编号、Bridge 追加和维护的回归。其他读取失败、UTF-8 截断、怪物体型未知等无法证明编号的保护仍各有依据，不能一起删除。

## 验证结果及边界

- 当前 DLL 的资源查询探针执行成功：50 个文件名/来源夹具、150 次三类型编号对照，另有 12 次重复打开来源校验；其中包含失败反例和正确对照。这里的 PASS 表示**成功复现并核对观察结果**，不表示产品问题已经修复。
- 二进制存档探针执行成功：12 个独立 DSON 场景、21 次完成的地图放置、3 次按预期失败的 Bridge 尝试。验证脚本检查了 12 份最终地图文件头与哈希，以及 21 份 `commit-result.json` 的档案、目标、备份归属和最终哈希。
- 空 `.types` 探针执行成功：36 组，其中 27 组误拦、9 组有效对照；其他类型的直接校验均通过。
- 三项最终探针日志均无构建警告/错误，命令退出 0。二进制探针初版错误引用了不存在的 `SaveProfile.GameSavePath`，编译失败且当时未运行；已改用实际档案路径后完整重跑，初版日志保留为 `persistence-initial-build.log`，没有未解决的检查失败。
- `verify_results.py` 对固定 HEAD、产品与正式测试无变化、三份 Core 输出哈希、探针结果、10 份原生函数证据和保存标记进行交叉核对，退出 0。结果存入 `verification.json`。
- 本轮关注当前表、全局 Bridge 来源、选择后的直接寻址、写入前复核及维护；没有重新验证全部物品、饰品、人物技能或真实游戏战斗。既有清单加载、Mod 优先级、已知超体型行跳过和四个怪物原始槽位规则没有作为待删除项。
- 本轮是只读审核，没有功能代码 diff，按协作标准不触发完成审查员。以上三项均为待批准实施的发现，产品仍保持本轮开始时提交的版本。

证据入口：

- [资源查询和重复来源结果](../../workspaces/historical-rule-review22-20260914/results.json)
- [合成 DSON 保存结果](../../workspaces/historical-rule-review22-20260914/persistence.json)
- [空类型记录结果](../../workspaces/historical-rule-review22-20260914/empty-types.json)
- [最终证据交叉核对](../../workspaces/historical-rule-review22-20260914/verification.json)
- [原生函数清单及哈希](../../workspaces/historical-rule-review22-20260914/native-evidence.json)

复现命令（探针固定为本次待修复 DLL，之后产品更新时需重新绑定版本与断言）：

```powershell
dotnet run --project workspaces/historical-rule-review22-20260914/ReviewProbe.csproj -c Release -- E:/数据文件/SelfMod/DarkestDungeonSaveEditor
dotnet run --project workspaces/historical-rule-review22-20260914/PersistenceProbe.csproj -c Release -- E:/数据文件/SelfMod/DarkestDungeonSaveEditor
dotnet run --project workspaces/historical-rule-review22-20260914/empty-types/EmptyTypes.csproj -c Release -- E:/数据文件/SelfMod/DarkestDungeonSaveEditor
python -B workspaces/historical-rule-review22-20260914/verify_results.py
```
