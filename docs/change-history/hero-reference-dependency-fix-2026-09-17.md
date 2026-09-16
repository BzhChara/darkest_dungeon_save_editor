# 人物 Buff 读取、怪癖引用与饰品职业依赖修复（2026-09-17）

## 授权与范围

用户确认修复[第三十六轮审核](historical-rule-audit-36-2026-09-17.md)的三项问题，基线为 `3f3db144ea8e3c314ec37cc25afce5ebd573a69f`。本次没有提交，也没有启动游戏、修改真实存档、已安装 Mod 或 Steam 配置。写入验证全部使用工作区合成档案。

关于“最后一项不是刚修过”：上一轮修复了饰品普通 Buff 的缺失/不完整区分，并修正职业要求的原生哈希查询；职业来源是否完整的相邻分支仍直接把未发现的职业判为不存在。这是上一轮遗漏，本次补齐，没有撤销前次修改。

## 改动

- `HeroClassCatalog.cs`、`HeroClassCatalog.DefinitionResolution.cs`：Buff 读取失败按有效文件槽位使前面的属性结果失效，后续完整定义可以重新确认对应 ID。覆盖来源不明时不宣称属性可靠；不引用 Buff 的怪癖仍可用。缺失获胜文件不暴露被覆盖文件。
- `NativeResourceIdentity.cs`、`HeroClassCatalog.QuirkDefinitions.cs`、`StagecoachHeroCandidateFactory.Quirks.cs`：Buff、怪癖互斥和进化引用使用完整 C-string 原生哈希。只有一个 `Az` 定义时，引用 `BE` 或 `BE\u0000tail` 可匹配；真正不同的定义发生同哈希冲突仍受保护。重复 Buff 引用保留次数，进化链检查保留有效循环。保存 ID 不改名。
- `SaveEditService.StagecoachHeroes.cs`：提交重建目录时保留 `Resolution`，使未知已启用 Mod 的依赖状态与预览一致。既有目录重检和事务恢复复用新的可用性结果，防止读取失败时旧 HP 预览通过。
- `TrinketHeroDependencies.cs`、`TrinketCatalog.cs`：职业依赖区分存在、确认不存在、无法确认；第三种情况保留首条饰品但设为只读，不采用后面同 ID 定义的次数。已发现的职业注册即为存在，不要求其规范 info 能生成新人物。
- `HeroReferenceDependencyContractTests.cs`、`TrinketDependencyContractTests.cs`、`ContractSuite.cs`、`QuirkSelectionInteractionContractTests.cs`：新增持久化回归并接入定向和完整测试，明确完整/缺失来源测试夹具的切换；两份规则文档同步更新。

影响范围是人物初始怪癖、HP/进化预览、马车人物保存校验，以及饰品目录选择与新实例次数。物品数量、战斗编号、Bridge、清单文件准入和既有同路径覆盖算法没有改动；无存档迁移或旧数据兼容。

## 验证

工件目录为 `workspaces/hero-reference-dependency-fix-20260917/`。旧审核目录已冻结，不覆盖原失败证据。

- Release 构建成功，0 警告、0 错误。
- 原审核 `Program.cs`、`BoundaryProgram.cs` 原样复制，绑定当前 Release Core 重跑：73 + 6 = 79 个样本，0 不符。六个健康预览边界样本均拒绝读取故障后的预检/提交，三份目标存档字节保持不变。
- `--trinket-dependencies` 定向回归 21 个 PASS 组，退出码 0。包括 180 个原饰品选择样本、120 个原 HP 枚举样本、六来源怪癖引用矩阵、三来源 Buff 读取失败与正确 DSON HP、真实来源映射、写中恢复及无 Buff 依赖怪癖的成功提交。
- 首次定向测试失败于新夹具：仅在基线添加 `z`，导致 Mod 独有 `a` 追加在其后；产品按正确顺序从 `a` 恢复了该 ID。夹具已同时建立 `a/z` 槽位，确保测试的是后续失败，并重跑通过。失败保留于 `targeted.log`，通过记录在 `targeted-2.log`。没有为迎合夹具改变文件排序规则。
- 首次完整回归在 WPF 怪癖补偿选择断言失败（`full.log`）：通用夹具启用了未安装的工坊 `333`，旧测试仍期待 Buff 属性可确认。保留原缺失来源与怪癖 Unverified 断言后，在进入已知 HP 值的界面/人物保存测试前，仅给合成夹具安装空 `333/modfiles.txt` 并通过 `ResolveAsync` 重新读取完整来源。没有清除 `Resolution`、修改产品保护或跳过用例；两种来源状态都继续测试。
- 修正夹具后的 Release 构建再次成功，0 警告、0 错误。`--quirk-selection` 为 14 个 PASS 组；`--catalogs` 为 151 个 PASS 组，两次退出码均为 0、无 FAIL/SKIP，覆盖先前未完成的界面、人物、饰品/物品与保存流程。
- 首次完整运行在上述夹具失败前已通过 132 组，包括所选战斗组；夹具修正只改测试，Core/App 程序集未变，因此没有重复已通过的战斗组。**首次全量运行的退出码仍为 1，不能称为最终全量运行成功**；最终覆盖依据是该次已通过的战斗检查与修正后通过的目录/保存及界面回归。
- 原审核目录的 945 个文件 SHA-256 均未变化，两份复现源程序与原件一致；复现用 DLL 与最终 Core、App 输出中的 Core DLL 一致。当前游戏 EXE 仍为本记录指定哈希；`git diff --check` 通过，规则文档的 120 个本地链接均可定位。
- `verification.json` 记录最终程序集、任务文件与测试日志哈希，以及旧证据不变核验。本次仍未提交。

## 独立审查

新建独立上下文的只读审查员 Hypatia 已审阅基线差异、新文件及生成/保存调用链，未发现需修改的实质问题。审查核对了 21 组定向通过、79 个原样复现样本零不符、六个保存边界字节不变，以及构建和差异检查。审查时完整回归仍在运行；最终结果由主代理另行核对，没有将运行中状态当作通过。审查员没有修改文件或运行游戏。

审查后仅修正上述既有测试夹具的完整来源前提，产品代码未变化；该测试收尾不重复启动完成审查，验证结果由后续可执行测试确认。

## 证据边界

本次依据当前 EXE SHA-256 `d83a6d578d059c8c9a0bd70453da65d85974715a616872c2dfeff1a5c9ab3a49` 的已定位静态控制流及隔离可执行验证，没有新增实机生命周期实验。未将三种引用语义推广为全部技能、Effect、Buff 字段的统一规则。规则入口：`docs/resource-duplicate-semantics.md` 第 31 节和 `docs/content-save-rules.md` 第 5、7、8 节。
