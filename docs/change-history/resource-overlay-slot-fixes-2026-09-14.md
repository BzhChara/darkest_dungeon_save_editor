# 公共资源合并与战斗编号修复

日期：2026-09-14。基线 `6cf2425`。对应[第二十轮审核](resource-overlay-slot-audit-2026-09-14.md)，用户确认修复。

## 改动和边界

- `NativeContentFileResolver.cs`：枚举文件的合并按固定游戏版本的模式 1、flags 0 分支执行。Base 直接建立初始列表；附加挂载按顺序查找已有路径中首次包含新挂载相对路径的位置，区分大小写，命中则原位替换，无匹配才追加。保留低到高的来源顺序与来源链，删除原来的完整键相等假设。
- 同文件新增独立的 `ResolveOpenedFiles`；人物和怪物标准定义、人物经验配置及地区 props 使用固定路径读取。三份根 props JSON 按带 `>` 的原生路径仅读取 Base。`HeroClassCatalog.DefinitionResolution.cs`、`HeroClassCatalog.cs`、`BattleRoomAttachmentCatalog.cs` 和 `QuantityItemReferenceAnalyzer.SourceDiscovery.cs` 接入相应入口。
- `BattleRoomAttachmentCatalog.Resources.cs`：三个根文件打开与三个子目录查询分组处理，按原有阶段顺序读取。`BattleEncounterCatalog.RuntimeOrder.cs`：每个地区、难度和集合使用独立结果列表，当前表和全局 Bridge 目录一致。
- 新增 `ResourceOverlaySlotContractTests.cs`、命令 `--overlay-slots`，并接入完整套件。`ResourceDirectoryQueryContractTests.cs` 将不符合实际设备结构的双 Base 夹具改成一个 Base 目录，保留原有目录、容量、HP 和写入断言。`MultiFileEncounterContractTests.cs` 将 `Z`／`z` 原位合并的旧预期改成区分大小写的枚举结果，同时保留直接写入拒绝并补测 Bridge 拒绝。
- 规则文档和 README 已更新；本记录和审核报告留在修改历史目录。

复现中的 `archive/dungeons/cove/a.cove.2.mash.darkest` 被正式 `dungeons/cove/a.cove.2.mash.darkest` 替换后不再多占位置。每个文件各含一行走廊、房间和首领时，正式 a 使用编号 0，z 使用 1，Bridge 追加为 2。旧版本分别错误写成 a 为 1、Bridge 为 3。物品和饰品也不再采用被替换文件中的首个定义。

修复影响资源目录、物品堆叠／饰品实例数值、直接放置、Bridge 安装／追加、预检和自动维护的输入表。清单准入、Mod 优先级、各资源重复 ID 规则、人物皮肤模式 0 查询、战斗行计数与保存布局保持原有规则。多 DLC 挂载不确定性、大小写竞争和未知怪物体型等既有保护未被删除。

没有修改真实存档、游戏、Mod 或系统配置，没有新增依赖，也没有新增旧候选兼容逻辑。

## 验证

依据 Windows x64 build 27890 的固定 EXE 反汇编及导入表，哈希与地址详见审核报告。本次没有重新启动游戏实测。

- Release 方案串行构建通过，0 警告、0 错误。首次新增测试误用了不存在的 `SaveProfile.GameSavePath`，编译失败；修正为档案目录下的 `persist.game.json` 后重建通过。没有跳过该测试。
- 定向 `--overlay-slots` 通过：Base／附加挂载首次命中、来源链、Mod 顺序、大小写和固定路径边界；六类来源的 21 组样例、63 张战斗表；物品堆叠与饰品计数；固定路径人物／怪物／经验／地图资源；三种战斗类型的直接和 Bridge 二进制保存、维护及删除。
- 定向样例明确验证原版初始深层文件保留、普通不同文件名保留、大小写不匹配保留、未列清单排除；胜出文件修改会使旧战斗候选失效，被替换文件的行数变化不会清除正确放置的战斗。
- 第一轮完整回归停在旧双 Base 夹具的物品值断言；改为同一 Base 目录内的物理覆盖后重新运行。第二轮通过目录、数值、DLC 副本路径、维护、地图资源和回滚等检查后，停在旧大小写合并断言。固定版本的 `strstr` 证据要求枚举保留 `Z,a,z`，不是 `z,a`；已修正预期并确认既有未验证提供者保护仍覆盖这类竞争。上述两轮调整均未回改产品逻辑或删除业务安全断言。
- 独立 Python 脚本 `workspaces/verify_overlay_slot_artifacts.py` 核对定向产物的 9 次完整地图 JSON 往返、DSON 头、已编码哈希和直接 0／Bridge 2 的会话记录；三个输出目录的 Core DLL 一致。结果见 `workspaces/overlay-slot-artifact-verification.json`。脚本还检查本次文档中的本地文件链接。

日志：`workspaces/overlay-slots-build-20260914.log`（首次编译失败）、`overlay-slots-build-validated-20260914.log`（成功构建）、`overlay-slots-targeted-20260914.log`（定向通过）、`overlay-slots-full-20260914.log`（首次回归失败）。定向产物：`workspaces/contract_tests/20260914_020937_342_057b899d489643d1a5b9fee761429ccc/`。

首次独立审查前的完整结果：

- `workspaces/overlay-slots-build-final-20260914.log`：Release 全方案串行构建通过，0 警告、0 错误。
- `workspaces/overlay-slots-full-final-20260914.log`：完整回归 **66 PASS / 0 FAIL / 0 SKIP**，进程退出 0。覆盖资源目录／同路径提供者／各类重复 ID、物品与饰品、人物技能／皮肤／怪癖／HP／升级、地图编辑、直接和 Bridge 战斗、DLC 副本路径、自动同步／维护、备份、预检、回滚和强制返回。
- 最终产物：`workspaces/contract_tests/20260914_022434_720_d9e9aae8a88c4c5e8d2b439eec61488f/`。该轮新增场景的 9 次地图往返再次由 Python 复核完整 JSON、DSON 头、编码哈希及目标地图格子的直接 0／Bridge 2／删除后 -1。
- `overlay-slot-artifact-verification.json` 记录 12 份源码／测试文件哈希，检查 157 个本地文档链接，并验证 Core、App 和契约测试输出的 Core DLL 完全一致：`aa97d2ca344136b9af263d0da51a0aea5e9ed3eacb69a499c1624be7b5fb2060`。
- `git diff --check` 通过。

## 独立审查

首次审查者因服务报告模型容量不足，未产生审查意见；已关闭。随后全新只读审查者 McClintock（`01a09db7-df84-73c3-9c02-5552c5270a7c`）报告两项 P2：非人物物品引用跨独立查询合并；Curio 的 flags-9 查询被套用 flags 0。

主流程增加隔离端到端夹具，`overlay-slots-review-red.log` 在本地、Workshop、DLC-prefix Mod 三类来源全部复现：事件奖励物品误判为未使用，深层奇物类型及其物品引用丢失。审查者自身只验证了 Core 文件列表，没有运行这些写入夹具或游戏。

修正包括：

- `QuantityItemReferenceAnalyzer.SourceDiscovery.cs` 按 Loot、JSON 消费者种类、建筑 ID、Curio 类型等分组；人物和未建模／固定路径参考仍保留完整路径覆盖。事件文件不能再被另一次 Loot 查询替换。
- `NativeContentFileResolver.cs` 增加 `ResolveAdditiveFiles`；`BattleRoomAttachmentCatalog.Curios.cs`、地图 props 子目录及 Curio 物品引用接入 flags 9 的独立路径列表。三份根 props 打开仍使用原来的固定路径入口。
- 主流程核对 `0x1404D8770` 调用链，发现三个 props 子目录查询也传 flags 9，故一起修正。`0x140247D20` 的 bit-8 分支和字符串 `0x140E28E10`（`>%s`）解释了 Base 路径前缀；`0x140247C1F` 后的 bit-1 分支对非零偏移命中追加。常规物品、饰品和遭遇的 flags-0 分支不变。
- 新增独立查询、深层 Curio 类型／引用、Base 同路径独有类型及每个显式提供者顺序的断言。首轮修正后测试误用“操作必须失败”的助手验证成功的奇物候选，改成直接执行 `ValidateDefinition` 并收集异常后重跑。

这次补充引入了不同原生查询参数的处理分支，因此请原独立审查者针对该边界再核查；不是对小范围断言修改递归审查。

补充验证：

- `workspaces/overlay-slots-reviewed-build-ready.log`：全方案 Release 构建 0 警告、0 错误。
- `workspaces/overlay-slots-reviewed-targeted-ready.log`：定向 **5 PASS / 0 FAIL / 0 SKIP**，退出 0；含原有编号/保存测试及两项审查复现、原版同路径独有类型和显式提供者顺序。
- 产物 `workspaces/contract_tests/20260914_025407_916_4b933ac2dfda4361b9177187f9143bdd/` 的 9 次地图 DSON 往返及目标位置经 Python 再核对；13 份源码/测试哈希、157 个文档本地链接、三个输出目录的 Core DLL 一致。当前 Core SHA-256：`e6fcb0af5cb56c59c03f636722a5d899c24a6b6d2c180d93b810336e6870337a`。
- 增加原版同路径类型对照时，首次夹具只声明类型/映射却未放入地区池，因而目录没有该候选；已补齐地区池再执行上述检查，没有改动产品的候选筛选规则。

### 路径打开分支的补充核对

主流程从 `0x140245FF0` 的路径解析任务、`0x140244C50` 分派继续核对到 `0x140248040`。该函数在 `0x140248051` 比较首字节 `3Eh`；带 `>` 则在 `0x14024812E` 跳过附加挂载查找并去掉前缀，直接查 Base 设备。无前缀路径先查附加挂载及其清单，找不到才回落 Base。由此修正两处相关边界：

- flags 9 给 Base 路径添加 `>`，保留原版内容的分支正确。
- flags 0 不给 Base 路径加前缀，枚举结束后还要解析实际打开的来源。例如 Base `[深层/a, a]`，Mod 的 `a` 替换第一位置后，第二位置仍会打开 Mod 的 `a`；两个位置各占编号，不能按物理文件去重。公共解析器在完成位置合并后处理这一打开阶段。新增目录、直接写入预检、Bridge 下一编号和维护表的三种类型对照。
- 三份根默认路径本身写成 `>props/...`（`0x1404D87A0`–`0x1404D87D4`），`0x1404D76E0` 原样传入 JSON 读取；因此只接受 Base 根文件，排除模式/DLC/Mod 的根默认文件。子目录查询照常读取。六来源地图夹具改为 Base 初始默认值，并放入故意损坏的附加挂载根文件，验证它们确实不会影响加载。普通 Curio 查询夹具使用合法子目录默认定义。

`overlay-slots-reviewed-full.log` 曾停在旧 Curio 显示名称预期：原夹具把高优先级 CSV 放在 Base 旧位置。已按 flags-9 的完整提供者次序修正，并分别验证较早映射失效仍保留最终有效映射、最终映射失效时拒绝放置；`overlay-slots-reviewed-map.log` 四组地图专项全部通过。

打开分支改动的一次验证启动早于构建完成，旧测试进程占用了测试输出的 Core DLL，导致 `overlay-slots-open-build.log` 报 MSB3027/MSB3021。该次旧输出验证不计入最终覆盖。等待该进程退出后重新串行构建，`overlay-slots-open-build-validated.log` 退出 0、0 警告、0 错误；重新运行 `overlay-slots-open-targeted-validated.log` 得到 **6 PASS / 0 FAIL / 0 SKIP**，退出 0。产物为 `workspaces/contract_tests/20260914_031918_028_8855bf5ff9b94f71b13feb181cd11086/`。

### 重打开的清单匹配修正

独立复核确认 `>` 分支，并继续追到实际设备读取：`0x140247800 → 0x140248040` 解析路径后调用设备 `+30`；`+20=0x140373E10`、`+30=0x140373F20` 使用 `_wfopen_s("rb")` 获取长度、读取内容，不再解析挂载。复核同时提出第三项 P2：Base 重打开复用了忽略大小写的提供者结果，会把大写 Base 路径替换为小写清单项。随后 `overlay-slots-open-full.log` 在既有 `[Z,a,z]` 遭遇断言独立复现了这项回归。

已在重打开的提供者选举之前按原始请求筛选 Mod：完整清单路径与请求逐字节匹配，不删除 DLC 前缀或忽略大小写。物理 Base/模式/DLC 的回落继续使用 Windows 查找。候选先按挂载路径分桶，避免对每个 Base 槽反复扫描全部文件。增加实际物品上限 `Base A=7 / Mod a=13 → 7` 的断言，以及“完整 DLC 路径读取前缀 Mod，但原版根路径回落物理 DLC”的两个位置对照。

`workspaces/overlay-slots-case-build.log`：全方案 Release 构建退出 0、0 警告、0 错误。`workspaces/overlay-slots-case-targeted.log`：修正后的 6 组专项全部通过，退出 0。独立审查者只读复核该有限修正，确认原始完整清单路径筛选发生在选举之前、DLC 对照与原生读取分支一致，未发现新增可操作问题；审查者未运行测试。由主流程执行最终完整回归和产物核对。

## 最终结果

- `workspaces/overlay-slots-final-full.log`：最终完整契约回归 **68 PASS / 0 FAIL / 0 SKIP**，进程退出 0。包含六组新增专项及已有目录、物品/饰品、人物/怪癖/HP/升级、地图/Bridge、DLC 副本路径、同步/维护、预检/回滚/强制返回等检查。此前失败的 `[Z,a,z]` 战斗断言通过，未删除或放宽它。
- 最终完整产物：`workspaces/contract_tests/20260914_032956_741_7e59f436be19414a9fcb4171a4007d39/`。Python 再核对其中 9 次地图 DSON 往返：完整 JSON 相同、编码头和 SHA-256 正确、直接编号 0/Bridge 编号 2、删除后为空内容和编号 -1。
- `workspaces/overlay-slot-artifact-verification.json` 记录 16 份实际改动的源码/测试文件哈希，检查 158 个文档本地链接。Core、App 和契约测试输出的 Core DLL 一致，SHA-256 为 `81ba5f7fc992aa1277d6ce60b7e51fa37e1f7ba8d8c9e229aadee3bf0e40ec71`。
- 除初始文件外，`MapResourceConsumerContractTests.cs` 校正 flags-9 映射顺序并保留失效检查，`MapPropJsonContractTests.cs` 验证 Base 根默认值及忽略附加根文件，`TextResourceQueryContractTests.cs` 使用原生会读取的子目录默认定义。`ContractSuite.cs` 和 `Program.cs` 接入新增专项。
- 独立审查发现的三项 P2 均已修复并通过针对性验证；最终有限复核未发现新增可操作问题。`git diff --check` 通过。

本次完成的是上述资源合并、打开与消费者分组修复；证据来自固定游戏版本的原生读取分支与隔离契约测试，没有重新启动游戏实测。没有操作真实存档或 Mod，未提交 Git。
