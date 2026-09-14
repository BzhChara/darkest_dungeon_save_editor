# 新人物初始 HP 条件修复（2026-09-15）

用户确认修复[第二十七轮审核](initial-hp-condition-audit-2026-09-15.md)的反向折磨条件遗漏。实现基线为 `c281549`；本次修改尚未提交。

## 实现与范围

[`StagecoachHeroCandidateFactory.HitPoints.cs`](../../src/DarkestDungeonSaveEditor.Core/StagecoachHeroCandidateFactory.HitPoints.cs) 删除原先仅接受 `always`、`no_trinkets` 正向条件的独立判断。生成时复用已有条件求值方法，传入新人物的明确状态：未装备饰品、未陷入折磨，模式及光照上下文为空。

`afflicted` 在这里是已知 false，反向后为 true；`in_mode`、`lightabove` 则缺少必要上下文，正向和反向都不预先生效。未知 HP 条件仍由目录标为未验证并拒绝生成。初始 HP 计算与所有可达状态的安全检查现在使用同一套条件求值逻辑，避免两处解释反向标记的方式不同。

基础 HP 20 时，“未折磨时 +50%”“未折磨时 −25%”“未折磨时固定 +4”分别生成 30、15、24，并将相同值写入候选及马车。修改影响怪癖选择预检、人物预览和新候选保存；普通马车及碎片马车共用修正结果。

保留可达状态的非正 HP 检查、数值精度与非有限值检查、重复 Buff 引用次数，以及不将怪癖 Buff 另写入 `actor.buff_group` 的规则。没有新增旧存档兼容或迁移，没有改写已有英雄。物品数量、资源清单／覆盖、升级树、战斗及 Bridge 规则没有改动。本次没有修改真实档案、活动 Mod 或 Steam 配置，没有启动游戏或增加依赖。

## 回归与文档

新增 [`HeroInitialHpConditionContractTests.cs`](../../tests/DarkestDungeonSaveEditor.ContractTests/HeroInitialHpConditionContractTests.cs)，由 `ContractSuite.cs` 的完整套件和 `ResourceDuplicateSemanticsContractTests.cs` 的 `--semantics` 分组调用。`HeroCandidateContractTests.cs` 的旧断言说明改为具体的正向折磨及缺少副本上下文的条件，避免再次把所有条件归为“生成时不生效”。测试索引同时更新。

- 六种合成来源各 22 个场景，共 **132 个**：原版、模式、DLC 包、DLC 功能、本地 Mod、工坊 Mod；Mod 均明确生成测试清单。
- 覆盖原审核的 48 个场景，并补充模式及光照正反向、固定与百分比混合、多个及重复 Buff 引用、互斥折磨状态、两种折磨状态下的非正百分比／固定 HP、未知条件正反向。
- 其中 **96 个**生成及预检成功，**36 个**危险／未知输入在预检和生成两个入口均被正确拒绝。
- **30 次 DSON 保存回读**：24 次普通马车、6 次碎片马车。核对完整 town 文档、正确池与 GUID 的初始 HP、原始候选及输入文档不变、已有 roster 不变和新 GUID 分配。

[`content-save-rules.md`](../content-save-rules.md) 增加初始状态与反向条件对照表，澄清既有三种运行时怪癖示例的适用范围；[`resource-duplicate-semantics.md`](../resource-duplicate-semantics.md) 区分已知折磨状态和缺失模式／光照上下文。原生依据沿用审核中的固定 Windows x64 build 27890 静态分支。30、15、24 是该条件下按编辑器满血生成约定计算的值，不是本轮游戏内生成结果采样。

## 验证记录

- 新测试先在未修改产品代码上运行，准确失败于 `base/unafflicted: expected initial HP 30, got preview 20, candidate 20`，证明回归能捕获原问题。
- 首次测试构建因新文件缺少 `using System.Text.Json` 出现 2 个编译错误；补齐引用后构建成功。没有新增依赖或更改产品接口。
- 修改后 Release 解决方案构建成功，0 警告、0 错误。专项的 132 个场景和 30 次 DSON 回读已通过。

- 语义分组 **19 项通过，0 失败、0 跳过**，正常退出码 0；包含上述 132 个场景及 30 次保存回读。
- 全新上下文的独立只读复核（Descartes）未发现已证实的正确性、回归或数据风险问题，并单独核对了 30 份 DSON 回读工件。复核完成时完整套件仍在运行，最终完整结果由主执行者继续核对。
- 完整套件 **90 项通过，0 失败、0 跳过**，正常退出码 0；包含新增回归及既有人物、物品／饰品、资源覆盖、战斗及 Bridge、持久副本、自动同步、强制回城和事务回滚测试。语义分组和完整套件分别执行了同一组 132 个场景、30 次回读，不将重复执行计算成额外的不同场景。
- 最终证据检查通过：两次运行的场景结果和 DSON 工件符合预期，差异仅涉及本次文件及上一轮保留的审核报告，文档链接与 `git diff --check` 均通过。Core、界面程序和测试目录中的核心 DLL 哈希一致：`b6dffed56da076be2383783703d7d0e25b2ec35e6cebf9b722d35b445dc9c068`。

本机日志保存在 Git 忽略目录 `workspaces/initial-hp-condition-fix-20260915/`：[最终构建](../../workspaces/initial-hp-condition-fix-20260915/build-final.log)、[语义分组](../../workspaces/initial-hp-condition-fix-20260915/semantics.log)、[完整套件](../../workspaces/initial-hp-condition-fix-20260915/full.log)、[独立复核](../../workspaces/initial-hp-condition-fix-20260915/review.txt)、[最终证据清单](../../workspaces/initial-hp-condition-fix-20260915/verification.json)。历史失败日志单独保留，不计作通过。
