# 怪癖组合选择修复（2026-09-15）

用户确认修复[第三十轮审核](quirk-selection-combination-audit-2026-09-15.md)发现的界面误拦。实现基线为 `38502d8`，本次尚未提交。

## 修改

[`InitialQuirkSelectionDialog.xaml.cs`](../../src/DarkestDungeonSaveEditor.App/InitialQuirkSelectionDialog.xaml.cs) 删除了 `BaseUnavailableReason` 及构造时单条怪癖 HP 校验缓存。每次选择变化后，以“当前已选 ID + 准备添加的 ID”调用现有核心校验，更新复选框可用状态和原因；已选组合只计算一次共同错误，已选行始终可以取消。

基础 HP 20 时，先选固定 +40，再选固定 −30，现在可以正常组合并生成 HP 30；+50% 与 −100% 的组合生成 HP 10。取消正向怪癖后，允许用户继续取消剩余项，也允许重新加入正向怪癖修复组合；确认选择时仍检查完整集合。搜索过滤不会改变所选集合，清空操作会同时清除隐藏的已选项。

窗口统一使用现有数量、互斥、定义可用性、等级和可达状态 HP 校验。数量已满时，不能再加入的条目现在会提前置灰并显示当前原因，取消对应类别后恢复可用。Singleton 的紧凑上下文提示仍为警告。未修改核心 HP 算法、资源读取、战斗、Bridge、实际同步调度或存档格式；没有增加旧存档兼容、数据迁移或依赖。

## 测试与文档

- 新增 [`QuirkSelectionInteractionContractTests.cs`](../../tests/DarkestDungeonSaveEditor.ContractTests/QuirkSelectionInteractionContractTests.cs)，从真实窗口的 DataGrid 列加载复选框模板，在 STA Dispatcher 上执行 ToggleButton 点击路径，验证绑定状态、事件处理、错误清除与取消行为；不显示窗口。
- 两个合法组合均通过实际准备、提交和最终 DSON 回读。其他对照覆盖补偿不足、仅生成时有效但运行时危险的条件、缺少 Buff、未知规则、互斥、正面疾病重叠配额、未知配额、重复定义、singleton 提示、高低等级及多个无效预选项的逐个取消。
- 提取 [`WpfContractTestHost.cs`](../../tests/DarkestDungeonSaveEditor.ContractTests/WpfContractTestHost.cs)，让完整套件的两组 UI 检查共用一个 Application；原同步交互测试保留原检查，只移出宿主创建与关闭。这样满足 WPF 每进程只创建一次 Application 的限制。
- `UiContractTests.cs` 删除固定缓存分支的旧字符串断言，保留布局、绑定、提示和显示检查；交互正确性由新测试验证。`ContractSuite.cs`、`Program.cs` 和测试索引接入 `--quirk-selection`。专项同时运行既有初始 HP 条件和同步交互检查。
- [当前内容规则](../content-save-rules.md) 记录组合校验、动态行状态及取消／确认行为。审查与修复过程继续集中放在本目录。

## 验证结果

1. 新测试先在旧产品代码上准确失败于 `ui_flat_loss: selecting compensation must enable the negative quirk and clear its solo HP error`，见 [before-regression.log](../../workspaces/quirk-selection-fix-20260915/before-regression.log)。此前的试跑曾因未等待隐藏模板的 Dispatcher 绑定附着而提前失败；修正测试时序后才得到上述有效反例，没有为通过测试修改期望值。
2. Release 解决方案构建成功：**0 警告、0 错误**，见 [build-final.log](../../workspaces/quirk-selection-fix-20260915/build-final.log)。最初一次 `dotnet run` 内置构建只输出了“生成失败”，没有详细诊断；随后显式 `dotnet build -m:1 --no-restore` 成功，测试使用该构建执行。
3. 最终 `--quirk-selection` 专项 **12 组 PASS、0 FAIL、0 SKIP，退出码 0**，见 [targeted-final.log](../../workspaces/quirk-selection-fix-20260915/targeted-final.log)。其中既有 HP 条件检查为 132 个场景、30 次普通／碎片马车 DSON 回读；新增怪癖 UI 检查为 5 组、2 次实际提交回读；原同步 UI 检查为 6 组。两组 UI 已在同一个宿主中顺序运行。

本次未重跑完整套件，未启动游戏，也未改动真实档案、活动 Mod 或 Steam。隔离测试资源及结果位于 `workspaces/contract_tests/`，具体路径记录在日志中。产品仅一个窗口代码文件发生变化；已更新对应 Release 构建。

独立只读审查员 Dirac 检查最终差异和工件，未发现需要修复的正确性或回归问题；其结果与主任务核对一致。复核没有另跑测试，可见窗口、键盘操作和合法确认后的完整模态返回路径未实测，见 [独立复核记录](../../workspaces/quirk-selection-fix-20260915/independent-review.md)。

[最终证据校验](../../workspaces/quirk-selection-fix-20260915/verification.json) 核对测试数量、结果文件、最终 DSON、Release DLL 副本和改动范围。上轮审核冻结的 10 个工件保持原哈希，产品差异仅在上述窗口文件，核心源代码无差异。`git diff --check` 通过。
