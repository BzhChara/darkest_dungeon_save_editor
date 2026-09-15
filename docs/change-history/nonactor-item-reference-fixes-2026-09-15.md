# 非人物文本的物品引用修复（2026-09-15）

用户确认修复[第二十八轮审核](nonactor-item-reference-audit-2026-09-15.md)发现的泛字段引用与缺失文件误报。实现基线为 `fc30df8`；本次修改尚未提交。

## 实现与范围

[`NativeResourceFileRules.cs`](../../src/DarkestDungeonSaveEditor.Core/NativeResourceFileRules.cs) 将已知 JSON／Curio 引用目录的资格检查应用到所有候选文件。配给、事件、庄园、任务、区域建筑、建筑、升级根使用已有原生 JSON 查询，奇物根使用 type-library CSV 查询。普通 `.darkest` 文件不能只靠清单列出就进入这些消费者；缺失文件诊断调用同一规则，因此也不会再被无关文件污染。

目录家族的辨认不区分大小写，真正接受文件仍使用既有的清单／Windows 目录规则和原生文件名表达式。这样既保留物理目录别名、原生通配点和启用 DLC 路径，也避免清单大小写错误落入“未知文本来源”分支。没有新增无清单扫描方式或重新按 Mod 优先级挑选重复 ID。

[`QuantityItemReferenceAnalyzer.RootParsing.cs`](../../src/DarkestDungeonSaveEditor.Core/QuantityItemReferenceAnalyzer.RootParsing.cs) 将其他未验证非人物文本的泛字段证据写入不确定集合。物品字段和掉落表线索不能再变成 ConfirmedActive；掉落不确定性沿嵌套表传播，仅影响相关物品。独立的确定来源仍可确认同一个物品。保留原生注释、同字段最后赋值、标准人物／怪物掉落和未知人物记录排除。

影响范围是物品引用状态、小镇／副本默认可见性、相应告警及重载后的统计。数量、堆叠上限、存档中已有条目、人物 HP、战斗编号和 Bridge 逻辑没有修改。没有添加旧存档迁移或 Mod 专用兼容，没有修改真实档案、活动 Mod 或 Steam 配置，也没有增加依赖或启动游戏。

## 回归与验证

新增 [`NonActorReferenceContractTests.cs`](../../tests/DarkestDungeonSaveEditor.ContractTests/NonActorReferenceContractTests.cs)，由 `--reference-consumers`、`--queries` 和完整套件调用。更新测试入口、索引及两份当前规则文档。

- 六种来源：原版、模式、官方 DLC、本地 Mod、工坊 Mod、带启用 DLC 前缀的 Mod。引用目标统一来自独立 Mod 定义，避免原版条目“始终可见”规则掩盖判断错误。
- **228 个内容样例、876 次目录检查**：覆盖已知目录中的任意文本、嵌套及大小写变体、缺失文件、正常配给／事件／庄园／升级／奇物与人物／怪物、未知文本逐字段对照、嵌套掉落、无关物品、独立确定来源与内容指纹刷新。
- **12 次 DSON 保存回读**：六种来源分别验证小镇和副本写入数量 5，校验整个文档与预览一致，输入文档不变，小镇数量为 5，副本分为 `2、2、1`。
- 产品修改前，新测试准确失败于 `base/ignored/campaign/provision/notes.darkest/False/na_pair: expected SuspectedUnused, got ConfirmedActive`。
- 修复后首次专项遇到测试样例错误：用于“未知怪物记录”对照的公共文本含有合法 `loot:` 记录。将该对照改为未知 `notes:` 后，专项全部通过；保留独立合法怪物掉落对照。没有为迎合错误样例修改产品解析规则。
- 首次解决方案构建退出 1，但日志只给出“0 警告、0 错误”，没有提供失败原因。使用 `--disable-build-servers -m:1` 重新执行完整 Release 构建成功，**0 警告、0 错误**；没有修改持久构建配置。

构建与专项日志保存在 Git 忽略目录 [nonactor-reference-fix-20260915](../../workspaces/nonactor-reference-fix-20260915/)：[基线失败](../../workspaces/nonactor-reference-fix-20260915/baseline.log)、[样例纠正前](../../workspaces/nonactor-reference-fix-20260915/targeted-first.log)、[专项通过](../../workspaces/nonactor-reference-fix-20260915/targeted.log)、[首次构建](../../workspaces/nonactor-reference-fix-20260915/build.log)、[构建重试](../../workspaces/nonactor-reference-fix-20260915/build-retry.log)。

原审查的 120 次目录观测已用同一探针回放到独立修复工作目录：**30 次错误观测全部修正，90 次正确对照保持不变**；另有原探针的 4 次 DSON 往返和 2 次刷新对照通过。没有覆盖原审查的冻结工件。见[回放日志](../../workspaces/nonactor-reference-fix-20260915/audit-replay.log)和[回放结果](../../workspaces/nonactor-reference-fix-20260915/probe-results.json)。

全新上下文独立审查员 Poincare 完成只读复核，未发现可证实的正确性、回归或数据风险阻塞问题；并核对专项工件、额外执行查询、混合证据及已有物品重载检查。见[审查原文](../../workspaces/nonactor-reference-fix-20260915/review.json)。审查结束时完整套件尚未结束，完整套件最终结果由主执行者继续核对。

完整套件已正常退出（退出码 0），**91 PASS / 0 FAIL / 0 SKIP**。除上述新回归外，覆盖既有资源覆盖与重复定义、人物／怪癖与升级、数量／饰品写入、战斗和 Bridge、持久副本维护、自动同步、强制回城、事务回滚与外部版本保护。专项与完整套件分别执行同一组 228 个样例、12 次 DSON 回读，不将重复执行计为额外的不同场景。见[完整日志](../../workspaces/nonactor-reference-fix-20260915/full-suite.log)。

最终[证据校验脚本](../../workspaces/nonactor-reference-fix-20260915/verify.py)通过，核对了原审查冻结工件未变、原反例回放结果、专项及完整套件的 JSON／DSON 工件、Core／界面／测试／回放目录的四份相同核心 DLL、任务文件范围和本地文档链接；`git diff --check` 通过。[验证摘要](../../workspaces/nonactor-reference-fix-20260915/verification.json)保留各文件哈希。

Release 构建输出已更新。本轮未进行游戏内或 GUI 自动刷新实测；当前结论来自既有原生研究、隔离可执行回归与独立复核。未验证非人物文本仍按上文保留不确定性，不宣称已完整模拟其游戏机制。修改尚未提交。
