# 物品引用完整性与钱包身份修复（2026-09-16）

本轮落实[第三十二轮审核](quantity-reference-identity-audit-2026-09-15.md)中用户已确认的两项修复。基线为 `dac3dbb996e99ab1e2cf5d87af920f754ebd1662`。中断前已完成代码与专项测试编写，本次恢复后继续验证和复核；没有新增兼容层、依赖或存档迁移。

## 修复行为

### 缺失引用文件应在覆盖计算后影响完整性

`QuantityItemReferenceAnalyzer.SourceDiscovery.cs` 保留清单缺失项的安装诊断，但不再在该诊断阶段将全局扫描或升级树扫描标为不完整。实际被消费的文件读取失败，仍由有效文件读取流程处理。

例如上方、下方 Mod 都提供同路径升级文件，上方可读且引用 `high_coin`，下方文件缺失：现在 `high_coin` 保持“已确认有引用”，无关的 `unused_coin` 保持“疑似未使用／默认隐藏”。若缺失的是上方有效文件，仍不回退下方；可叠加的地区建筑输入仍分别参与完整性判断。小镇升级文件不会污染副本目录。

### 分开原始物品身份、钱包保存键与显示名

`QuantityItemCatalog.Definitions.cs`、`QuantityItemCatalog.cs` 为引用分析保留经过 first-match 选择的全部原始定义；小镇余额仍按保存键聚合。`QuantityItemReferenceAnalyzer.InternalModels.cs` 让明确的物品引用按 `(type,id)` 匹配，货币引用按实际钱包类型的原生哈希匹配，estate 货币保留其物品 ID。`JsonRoots.cs`、`Upgrades.cs` 接入货币解析入口；`QuantityItemReferenceAnalyzer.cs` 按最终目录键合并证据及官方来源。

例如 `gold/variant` 显示 ID 为 `variant`，但保存到 `wallet.type=gold`。事件发放 `gold` 时会确认这个余额的用途，事件只发放 `variant` 时不会误算为金币。任务奖励 `gold/variant` 也能匹配；只有 `gold/variant` 定义时，奖励未定义的 `gold/空ID` 不会借用同钱包关系确认用途。多个有效定义共用余额时，其中任一项的合法用途不会因选择了另一条作为显示代表而丢失。

这些改动影响物品用途分类和默认列表显示。数量保存、背包堆叠、同路径覆盖顺序、重复原始 ID 的 first-match、战斗编号及 Bridge 行为均未改动；已有余额保持可见。

## 验证

- Release 构建成功：0 警告、0 错误。首次默认构建未给出具体错误；诊断构建随后揭示两处新测试的原始插值字符串 `CS9007`，并记录沙箱内编译服务器命名管道拒绝访问。修正测试字符串后，获准在沙箱外构建成功。保留 `build-first.log`、`build-diagnostic.log` 与最终 `build-fixed.log`。
- `--reference-consumers` 成功：原有 228 个非人物引用样例、876 次目录检查、12 次 DSON 回读；新增本地／工坊共 60 个覆盖提供者场景和 76 个钱包身份场景，另含恢复、来源和副本对照，以及 4 次隔离存档实际提交／DSON 回读。新增测试位于 `QuantityReferenceProviderContractTests.cs` 与 `QuantityReferenceIdentityContractTests.cs`，已接入专项与完整套件。
- 完整回归：157 组 PASS，0 FAIL、0 SKIP，退出码 0。覆盖物品／饰品数量保存、人物／怪癖、三类战斗与 Bridge、持久副本子目录、强制回城、共享自动同步、预览后变更保护和回滚；完整日志为 `full-suite.log`，工件为 `workspaces/contract_tests/20260916_020124_780_a4dc3b9170e84cf19dee278992b62137/`。
- 独立只读复核：检查 7 个 Core 文件、2 个测试入口、2 个新增测试及相关提供者／引用／身份聚合路径，未发现实质问题。复核者执行范围内 diff 检查，并核对构建与专项测试日志，没有另行构建或运行测试。提出的两处文档歧义已核实修正：缺失是指清单已列出的物理文件；普通 Building 数据仍只提供不确定线索，不扩大为已验证的统一货币消费者。
- 原审核冻结的 412 个工件（排除持续更新的记录索引）哈希全部保持不变。
- 最终 `git diff --check` 通过。`workspaces/reference-identity-fix-20260915/verification.json` 记录测试完成标记、计数、当前源码／文档及 Release 程序集哈希，并确认 App／测试目录使用的 Core DLL 与本次构建一致。

日志位于 `workspaces/reference-identity-fix-20260915/`；专项工件为 `workspaces/contract_tests/20260916_020030_296_ae7f9c82243249268bce907c4e96682c/`。原审核报告和隔离探针保持历史记录，不重新构建旧探针。

当前规则同步到 `docs/content-save-rules.md` 与 `docs/resource-duplicate-semantics.md` 第 28 节，测试入口更新到测试项目 README。没有操作真实存档、游戏、Mod 或 Steam；没有新增实机实验。本次修复未提交。
