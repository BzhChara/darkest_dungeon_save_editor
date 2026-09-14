# 固定路径打开、Effect 与 District 查询修复（2026-09-14）

对应[第二十一轮审核](canonical-open-and-query-flags-audit-2026-09-14.md)的 1 个 P1、2 个 P2。用户确认后实施；基线为 `3b19139`。本次没有提交，也没有操作真实游戏进程、存档、Mod 或 Steam 配置。

## 修改内容

### 固定路径按实际请求选择提供者

`NativeContentFileResolver` 的固定打开入口现在接收原始请求及活动来源，不再先按忽略大小写、消除 DLC 前缀后的候选路径选赢家。Mod 必须具有与请求大小写一致的清单键；Base、模式、官方 DLC 的物理目录仍按 Windows 路径打开。Mod 清单正确、物理文件名大小写不同的情况继续可用。

人物 info/art/override、怪物 info/art、人物与怪物的物品引用、经验配置和地区道具池共用这个入口。根部三份地图 JSON 默认定义显式使用 `>props/...`，仅从 Base 打开。普通枚举后剩余的 Base 请求也通过相同入口解析，保留重复读取位置。

例如第一条组合为两个体型 1 的 alpha_A，第二条为 bravo_A；只有大写后缀的 Mod 清单键不能覆盖原版标准 info 请求。修复后第一条正常占号，bravo_A 使用编号 1，Bridge 下一条使用 2。正确的 Mod 清单键若确实提供体型 3，则第一条总体型超限被跳过，bravo_A 使用 0，Bridge 下一条使用 1。没有改变超限跳过、空组合占号或缺失组合的规则。

清单列出但已缺失的赢家仍产生不可用/读取失败，不退回旧定义；这是编辑器的读取失败保护，不声称复刻了游戏所有 I/O 错误分支。

### Effect 使用 flags 1

人物 Effect 文件改为对应原生 flags 1 的输入序列：第一个匹配位于字符串开头时删除该位置并在末尾追加；匹配位于带挂载前缀的路径内部时保留原文件并追加。与 flags 0 的原位替换、flags 9 的具体来源保留分别处理。

因此下方 Mod 定义 `.disease special`、上方同路径 Effect 仅声明 `.duration 3` 时，不再在文件合并阶段丢失下方定义。技能赋予特殊怪癖的线索得以保留。已有“缺省保留、显式空字符串清空”的字段规则不变。

### District 使用 flags 9

建筑物品供给引用改为使用 flags 9 的文件输入，保留同路径 Base/Mod 的具体来源。上方 Mod 的空 `buildings` 不会令下方独有建筑的供给丢失。`estate` 和 `provision` 仍分别影响小镇与副本物品目录。

这里没有新增相同建筑 ID 的覆盖规则。未建模的引用来源继续使用原来的保守分析；物品、饰品、怪癖、Buff、升级树等既有语义 ID 规则保持不变。

### 独立审查补充：地区 ID 在后续选择时仍被合并

审查员指出：固定打开已区分 `cove` 和 `Cove`，但地区定义键及自动选择仍忽略大小写，可能把上方 Mod 的 `Cove` 陷阱/障碍及权重混入当前 `cove`。独立输出目录的执行式反例在修复前确实失败，见 `workspaces/canonical-region-reproduction.log`。

已让地区候选、定义键、自动选择使用原始 ID。地图初次加载、重新加载及同步都传入当前副本 ID；目录 guard 保存请求，后续验证重用，副本 ID 变化时重建缓存。Base/模式/官方 DLC 的大小写物理别名仍可用，因为打开的是当前请求，而非把发现的目录名当作请求 ID。新增本地/工坊大小写双池及独立权重、正确清单配合物理别名、空赢家、三种物理来源的自定义地区 ID 和 guard 重绑测试。

有限复核另外指出新增显式请求必须保留 `arena` 的特殊排除。已核对固定 EXE `0x1404AF1CF–0x1404AF1E6` 在 props 请求前跳过 arena，并给显式请求加回现有排除；三种物理来源均补候选/自动选择为空的断言。这是有限条件修正，未扩大重构，按协作规则不递归触发新的审查。

## 文件与验证范围

- 产品：`NativeContentFileResolver.cs`、`HeroClassCatalog.cs`、`HeroClassCatalog.DefinitionResolution.cs`、`QuantityItemReferenceAnalyzer.SourceDiscovery.cs`、`BattleRoomAttachmentCatalog.cs` 及 `.Resources.cs`。
- 审查补充：`BattleRoomAttachmentCatalog.RegionalSelection.cs` 和 App 的 `BattleMapView.Commands.cs`、`.ProfileLifecycle.cs`、`.ProfileSync.cs`，连接实际地区请求、自动选择与同步缓存。
- 新增 `CanonicalResourceContractTests.cs`，接入完整回归并提供 `--canonical-resources` 入口。
- 12 个本地/工坊固定路径夹具，覆盖清单大小写、物理大小写别名、同一清单同时列出两种拼写的两种顺序、DLC 物理回退和正确根路径覆盖；核对 HP、经验、伴生物品、地区池及 36 张战斗表。
- 6 个 Effect/District 夹具，覆盖同路径、不同路径、显式清空，以及小镇/副本可见性；另核对 flags 1 的删除后追加顺序和剩余 Base 请求重复打开。
- 三类战斗分别通过合成 DSON 存档执行直接新建、Bridge 替换、重新读取、无关文件变化后的维护及删除。
- 修改旧测试中的错误前提：DLC 前缀 Mod 文件不能自动回答根路径请求，大小写不同的 Mod 清单键也不能自动覆盖原版。支持性夹具使用真实可打开的根路径/官方 DLC 文件；专门的大小写及 DLC 测试保留正反两组断言，没有整体移除原测试。
- 当前规则写入 `resource-duplicate-semantics.md`、`content-save-rules.md`、`encounter-runtime-order.md`；本记录单独放在 `change-history/`。

## 验证结果

- 原三项修复的完整回归：`workspaces/canonical-fix-final-full.log`，72 PASS、0 FAIL、0 SKIP，退出 0。随后因独立审查发现地区 ID 混合问题，补充实现和测试。
- 人物生成/皮肤定向回归：`workspaces/canonical-fix-hero-selection.log`，6 PASS、0 FAIL、0 SKIP，退出 0。
- 补充修复后全方案 Release 构建：`workspaces/canonical-fix-reviewed-build.log`，0 警告、0 错误，退出 0。
- 补充修复后定向回归：`workspaces/canonical-fix-reviewed-targeted.log`，5 PASS、0 FAIL、0 SKIP，退出 0；产物 `workspaces/contract_tests/20260914_072102_720_fea789bb989641c884cf5ca32504f9b2/`。
- 完整回归早期停在旧的大小写/DLC 前提，已按上述来源边界修正夹具/预期；没有删除相关检查。新增测试脚手架也修正了辅助方法名和合成副本实例标识。
- 地区反例的首次构建与仍在运行的全量测试争用测试 DLL，产生 MSB3027/MSB3021，反例当时没有执行。随后使用独立输出目录构建（0 警告、0 错误），复现了预期的逻辑断言失败；最终等待旧测试退出，再串行构建和验证。不存在未解决的文件占用失败。

- 地区请求与同步修复后的完整回归：`workspaces/canonical-fix-reviewed-full.log`，73 PASS、0 FAIL、0 SKIP，退出 0；产物 `workspaces/contract_tests/20260914_072332_550_59425ef5743d45459c3fdc5ed6ead069/`。该次全量使用的 Core SHA-256 为 `0252874ad45a9760b6d93b4ff24bc34a23e98ebb64d332d6285d16f170cfe4d8`。
- 最后一次 `arena` 条件修正后的全方案 Release：`workspaces/canonical-fix-final-build.log`，0 警告、0 错误，退出 0。上一条全量对应修正前二进制；末次条件修正通过下述受影响的固定路径/地区/写入专项验证。

- 最后一次专项：`workspaces/canonical-fix-final-targeted.log`，5 PASS、0 FAIL、0 SKIP，退出 0。覆盖 `arena`/`ARENA` 在三种物理来源下的显式请求、地区大小写与权重、固定打开、Effect/District，以及三类型直接/Bridge DSON 保存、维护和删除。产物 `workspaces/contract_tests/20260914_073534_227_cca8cb67f1e044478cc6688decfe2a6c/`。
- 独立只读完成审查共提出两项地区请求相关 P2，均已核实并修复。地区请求/权重/guard/界面缓存的有限复核未发现其他实质性问题；最后的 `arena` 条件修正由主流程执行受影响专项确认，按协作规则不再递归审查。审查员未亲自运行测试。
- `workspaces/canonical-fix-artifact-verification.json` 记录 32 份改动源码/正式测试哈希、7 份文档哈希、154 个本地文档链接检查，以及末次三个合成地图的 DSON 文件头和哈希。Core、App、测试三个输出目录的 Core DLL 一致，最终 SHA-256：`7e7786df779733ff78c27ea106ae1e3e195980fa0f581e45df4b29f592cfe877`。记录分别保存全量运行的旧 Core 哈希和末次专项的最终产物，便于后续提交前核对。
- `git diff --check` 通过。上述修复和测试已完成，本次未提交 Git。

验证使用固定 build 27890 EXE 的既有原生分支证据，以及执行式合成资源/二进制存档测试。本次没有重新运行游戏；未声称已经实测所有资源或原生 I/O 错误行为。
