# 历史修改第三十轮审核：怪癖组合选择与旧预检限制（2026-09-15）

本轮先提交上一轮修复：`38502d8`（`fix: keep idle synchronization interactive and validate source bindings`），共 29 个文件，提交后工作区干净。随后只读检查历史实现；本轮未修改产品代码、正式测试或当前规则文档。

## 确认的问题：单条怪癖的 HP 失败被当成永久禁止条件（P2）

初始怪癖选择窗口可能禁止一个合计 HP 正常、核心允许生成和保存的组合。

- [`InitialQuirkSelectionDialog.xaml.cs`](../../src/DarkestDungeonSaveEditor.App/InitialQuirkSelectionDialog.xaml.cs) 第 59 行为每条怪癖计算 `BaseUnavailableReason`。第 90–104 行只把这一条怪癖传给完整的初始怪癖校验，因此返回的错误也可能只是“单独使用时 HP 不足”。
- 第 218–223 行在每次刷新时直接使用这个固定错误，将尚未选中的行禁用。它没有检查其他已选怪癖能否补足 HP。对应 [XAML](../../src/DarkestDungeonSaveEditor.App/InitialQuirkSelectionDialog.xaml) 第 113 行把复选框的 `IsEnabled` 绑定到 `IsSelectable`，所以用户不能点击这条合法的后续选择。
- [`StagecoachHeroCandidateFactory.HitPoints.cs`](../../src/DarkestDungeonSaveEditor.Core/StagecoachHeroCandidateFactory.HitPoints.cs) 已按全部所选怪癖合计固定及百分比修正，并检查可达状态；预检、生成及保存服务均接受下面的合法组合。界面却在这些入口之前将其拦下。

### 隔离复现

用现有契约测试夹具生成独立游戏目录及档案，在其标准资源目录中加入六个 HP 怪癖和一个缺少 Buff 的对照怪癖。复用提交对应的 Release DLL，在 STA 线程中实例化真实 WPF 对话框并调用真实复选框事件处理器；没有显示窗口或启动游戏。

| 基础 HP | 第一个选择 | 想追加的怪癖 | 合计 HP | 核心结果 | 界面结果 |
| --- | --- | --- | --- | --- | --- |
| 20 | 固定 +40 | 固定 −30 | 30 | 预检、生成、保存成功 | −30 仍置灰 |
| 20 | +50% | −100% | 10 | 预检、生成成功 | −100% 仍置灰 |

两组 Buff 均为 `always`，没有互斥或数量超限；六个有效怪癖均由真实目录加载为 `Direct`。关闭后以正向怪癖已选中的状态重新创建对话框，仍无法选择负向怪癖。若直接把完整合法组合传给对话框构造函数，同一个窗口的组合校验又能通过，说明问题是选入路径的提前禁用。

固定值样例还通过实际 `PrepareStagecoachHeroEditAsync`、`CommitAsync` 写入隔离档案，随后解码最终 DSON：两个怪癖均保留，`current_hp=30`；town、roster、upgrades 均为 DSON，estate 和 game 字节不变。百分比样例验证到核心生成及 WPF 交互，没有另做保存。没有进入游戏验证这两个合成组合，亦未修改用户实际存档、活动 Mod 或 Steam。

探针共有 **26 个观测／断言**，全部符合预期，退出码 0。其中 **4 个断言是成功复现误拦**（两种数值形式，各验证选择后及重新打开），不是“产品通过正确性测试”。其余对照包括：

- 单独 −30、单独 −100%、−30 配 +5，核心继续拒绝。
- 缺少 Buff、真实怪癖互斥，核心及界面继续拒绝。
- 可用的 −5 怪癖在正向怪癖选中后仍可选择。
- 合法组合生成正确 HP，固定值组合最终保存回读正确。

证据：[探针源码](../../workspaces/historical-rule-review30-20260915/Program.cs)、[项目](../../workspaces/historical-rule-review30-20260915/Review30.csproj)、[运行日志](../../workspaces/historical-rule-review30-20260915/probe.log)、[结果及保存文件位置](../../workspaces/historical-rule-review30-20260915/probe-results.json)、[最终 town 回读](../../workspaces/historical-rule-review30-20260915/committed-town.json)。测试资源及档案由夹具写入 `workspaces/contract_tests/`，具体绝对路径记录在结果中；探针自身与验证证据位于 Git 忽略的 `workspaces/historical-rule-review30-20260915/`。

### 历史来源与应调整范围

当前 Git 历史基线 `7b9a67f` 已有“单条校验错误缓存后禁用”的结构；历史对象 `a07b5c0` 也能看到同一逻辑。`fb7b0fd` 后来增加了目标等级，并让已选中的无效项仍能取消，但未解决合法组合被单条预检误拦。最近修复的是 Buff 读取、合计值或初始状态条件，以及自动同步；这些修复不会自动改变此窗口的行禁用规则。

建议后续区分“定义本身不可用”和“当前选择组合不可用”：不要将单条 HP 校验失败永久存为行不可用原因，结合当前所选集合重新判断是否能加入。保留最终组合的可达状态 HP、缺失定义、互斥和数量限制校验，以及取消选择能力。这里需要调整的是界面旧预检策略，不应删掉核心的非正 HP 保护，也无需新增旧存档兼容。

[`UiContractTests.cs`](../../tests/DarkestDungeonSaveEditor.ContractTests/UiContractTests.cs) 第 1273、1289 行检查了相关源代码字符串，未执行这类组合交互。后续修复应更新对应旧断言，并增加真实对话框的“先选正向，再选负向”行为回归；不能仅靠保留源代码形状说明行为正确。

## 其他审核范围与保留项

本轮还查看了自动同步的选择保留、资源指纹及来源校验，物品数量写入、已有堆叠保留、保存身份限制，以及人物生成中若干历史约束。在本轮已检查的路径中，未确认第二个独立错误：

- 副本增加数量时采用基础堆叠上限、保留原有合法运行时超限堆叠，有现行规则依据；不能据此删掉相关处理。
- 物品保存身份的 63 UTF-8 字节限制对应已验证的原生缓冲区，不是多余的兼容规则。
- 职业 `incompatible_class_ids` 未用于当前手动怪癖分配的职业禁止，符合现有手动控制模式约定，不能仅因字段未进入选择校验就判定漏处理。
- 同步定时调度仍是存档通知、2 秒轻量兜底和 10 秒资源检查，共用发布流程；上轮提交解决空闲检查禁用界面，不代表已经合并了两个周期。本轮没有修改此行为。

上述保留项参见 [内容及保存规则](../content-save-rules.md)。这是已查看路径的审核结果，不是整个项目无其他问题的证明。

## 验证边界

探针直接引用既有 Release App/Core DLL，未重新构建产品。其 Core、App、测试 DLL 哈希分别与上一轮冻结的 [verification.json](../../workspaces/content-sync-fix-20260915/verification.json) 相同。上一轮完整套件 129 项在最后一次替换访问预检补丁之前执行；最终版本另外有 40 项定向检查及 1 组维护检查。本轮没有重跑完整套件，不能把这些旧结果表述为本轮新跑的测试。

本轮只编写审核报告、记录索引及隔离探针；未实施修复。按只读审核边界，没有触发实施完成后的独立审查员。[证据校验结果](../../workspaces/historical-rule-review30-20260915/verification.json) 记录提交、DLL／文件哈希、断言数量及改动范围。
