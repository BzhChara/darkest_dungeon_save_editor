# 历史修改第二十九轮审核：来源映射与写入校验（2026-09-15）

本轮先提交上一轮非人物文本引用修复：`13a7c7f`（`fix: gate non-actor item references by resource consumers`），随后只读审核产品代码。提交前核对 11 个文件与上轮冻结验证的哈希相同，复核原反例回放、专项工件和完整套件 **91 PASS / 0 FAIL / 0 SKIP**。提交后工作区干净；本轮没有修改产品代码、正式测试或当前规则文档。

## 确认的问题：本地 Mod 来源映射变化后仍能使用旧预览写入（P2）

旧校验重新读取了资源文件，但复用了载入时的 `Sources`，没有重新核对这些目录是否仍是存档中勾选的 Mod。文件及 `modfiles.txt` 可以完全不变，`project.xml` 的 `Title` 变化却会改变本地 Mod 的解析结果。

- [`ActiveContentResolver.cs`](../../src/DarkestDungeonSaveEditor.Core/ActiveContentResolver.cs) 依照存档的本地 Mod 名称查找项目标题。标题不存在时报告未映射；同一标题改由另一个目录提供时，重新解析会选择新目录。
- [`SaveEditService.Shared.cs`](../../src/DarkestDungeonSaveEditor.Core/SaveEditService.Shared.cs) 的 `CaptureManifestFingerprints` 只保存 `modfiles.txt` 指纹。
- [`SaveEditService.QuantityItems.cs`](../../src/DarkestDungeonSaveEditor.Core/SaveEditService.QuantityItems.cs) 和 [`SaveEditService.Trinkets.cs`](../../src/DarkestDungeonSaveEditor.Core/SaveEditService.Trinkets.cs) 在提交前用 `prepared.ContentGuard.Sources` 重建目录；旧目录中的文件仍在，就可能继续通过。
- [`BattleEncounterCatalog.cs`](../../src/DarkestDungeonSaveEditor.Core/BattleEncounterCatalog.cs) 的 `ValidateGuard` / `ValidateDirectEncounter` 同样使用原来的 `guard.ActiveSources` 验证表和怪物，不能发现来源映射已经改变。

这里的旧假设是“存档 Mod 列表、原目录资源及清单没变，活动来源就没变”。它不是上一轮物品引用修复引入的回归：最初基线 `7b9a67f` 已有饰品校验复用来源和仅记录清单的结构，`fb7b0fd` 的物品流程及 `ec9b171` 的拆文件继续沿用；战斗校验至少从 `a5f38b8` 起存在该结构。当前资源覆盖、重复 ID 规则即使正确，也不能补上这一步来源身份核对。

### 隔离复现

使用提交后 Release Core DLL：

`25ee6f1fbd96a80003b671d12381f87c46843b83c072819e0afa1c149bd02064`

所有游戏目录、Mod、档案、写入和备份均由探针在 Git 忽略的 [review29 工作目录](../../workspaces/historical-rule-review29-20260915/) 内生成。没有操作真实游戏目录、真实档案、Steam 或运行中的游戏。

| 流程 | 操作前与来源变化 | 实际结果 |
| --- | --- | --- |
| 副本物品 | 原版上限 2，本地 Mod 同路径覆盖为 5；载入后或预览后改掉 Mod 标题 | 重新解析上限为 2，旧操作仍成功写入一堆 5 |
| 饰品 | 原版任务／触发次数均为 2，本地覆盖为 5；载入后或预览后改标题 | 当前定义已是 2，DSON 回读仍为 `quest_uses_remaining=5`、`triggers_remaining=5` |
| 物品及饰品 | 预览后把旧项目改名，再让新目录用原项目标题提供数值 3 | 重新解析无来源错误、有效值为 3，旧操作仍成功写入 5 |
| 战斗 | 预览的走廊表编号 0 对应 `mod_unit`；预览后改标题 | 当前有效表编号 0 已对应 `native`，旧预览仍成功写入 `mash_type=0 / mash_index=0` |
| 战斗 | 选择旧条目后将原项目标题重新映射到新目录，再执行界面同样的重新解析及维护步骤 | 新来源无错误、维护返回未变化／未暂缓，旧条目仍能通过准备和提交；编号 0 当前对应 `native` |

最后一个样例也核对了 [`BattleMapView.Commands.cs`](../../src/DarkestDungeonSaveEditor.App/BattleMapView.Commands.cs) 的实际调用顺序：`ApplySaveEditAsync` 先解析当前来源并维护历史战斗，但维护成功且没有旧记录可清理时，后面仍将先前选中的 `encounter` 传给准备接口。历史清理成功不等于当前选择已重新绑定。

共有 **19 个不同场景**：14 个物品／饰品场景、5 个战斗场景。其中 **8 个错误写入观测**，其余 **11 个正确对照**。全部最终目标均为 DSON，并执行了最终保存回读。对照包括：

- 不修改资源时正常提交。
- 只改项目说明、保持标题及来源映射不变时正常提交。
- 修改 `modfiles.txt` 或存档中的 Mod 配置时，既有校验拒绝提交，目标字节不变。

证据：[物品／饰品结果](../../workspaces/historical-rule-review29-20260915/probe-results.json)、[日志](../../workspaces/historical-rule-review29-20260915/probe.log)、[战斗结果](../../workspaces/historical-rule-review29-20260915/battle-results.json)、[日志](../../workspaces/historical-rule-review29-20260915/battle.log)。这些是反例观测，不表示产品通过了应有的拒绝过期操作要求。本轮未进游戏触发这些战斗，战斗偏差由当前有效表和已写入地图的编号对照证明。

### 自动同步与修复范围

共享自动同步每 10 秒重新解析来源，正常完成后能识别标题／目录变化并失效旧预览。因此不能描述为“自动同步永远发现不了”或“所有本地 Mod 修改都会出错”。缺口出现在同步尚未发布新目录时，或已经开始准备／提交的操作中；后台刷新不能代替服务端的最终校验。

建议后续修复保存并重新验证活动来源的解析依据和结果，覆盖项目标题对应目录、来源种类、加载顺序及 DLC 挂载。物品、饰品和战斗共用一致的来源校验，准备及提交均拒绝已经改变映射的旧选择。不能只在准备时给旧 `project.xml` 补一个当前哈希：如果载入后、准备前已改名，这会把过期来源当成新基线。也不应把所有项目说明变化都视为来源变化。

[`SaveEditService.StagecoachHeroes.cs`](../../src/DarkestDungeonSaveEditor.Core/SaveEditService.StagecoachHeroes.cs) 也复用旧来源并重建人物目录；修复时应纳入同一入口检查。本轮没有单独复现人物写入、奇物写入或 Bridge 包生成，因此不把它们计入已证实的错误场景，也不宣称跨地区 Bridge 已普遍失效。

## 其他审核与验证

本轮还沿自动同步、场景缓存、持久副本路径、战斗历史归属、维护暂缓、原子替换和恢复调用链检查了旧保护。在这些已查看的路径中没有确认第二个独立问题；不能据此删除保护，也不代表整个项目已穷尽审核。

用现有测试 DLL 单独执行并通过三组相关合同检查：

1. 档案同步、场景缓存、文件锁、部分写入、配置变化和定义原位更新。
2. 持久副本路径、DSON 物品／地图写入、路径过期、备份、回滚及强制回城。
3. 原子替换边界、部分替换失败、外部版本保留、恢复锁及恢复源损坏。

见 [sync.log](../../workspaces/historical-rule-review29-20260915/sync.log)。测试运行器的第四条 PASS 是上述调用完成的汇总，不另计为第四组。没有重新执行整套 91 项；提交前复核的是上一轮完整套件的冻结证据。

战斗探针首次运行因反射调用漏传测试辅助方法的第三个参数而退出 1，见 [首次日志](../../workspaces/historical-rule-review29-20260915/battle-first.log)。只修正探针参数后正常完成，随后加入来源重映射场景；没有修改产品或正式测试来迎合结果。最终三次探针运行均退出 0。

[证据校验脚本](../../workspaces/historical-rule-review29-20260915/verify.py) 核对场景、DSON 工件、提交结果、DLL 哈希、相关测试输出和任务改动范围，结果保存在 [verification.json](../../workspaces/historical-rule-review29-20260915/verification.json)。

本轮只新增此审查报告及记录索引，未实施修复。按只读审核边界，未触发实施完成后的独立审查员。
