# 物品、战斗、Effect 与奇物资源发现修复：2026-09-10

用户确认修复[第十轮审核](resource-discovery-audit-2026-09-10.md)的四项问题。基于提交 `831b962ae8b7105248216ccbefa85ab925ad7b5e`，本记录描述本次实现；审核报告中的旧 DLL、反例及编号保持历史语义。

## 修复内容与影响

1. **战斗表查询。** `BattleEncounterCatalog.SourceDiscovery.cs` 与 `BattleEncounterCatalog.cs` 按地区和十进制难度匹配原生查询，保留未转义分隔点的通配含义。全局目录、当前表、直接写入复查、Bridge 追加和维护使用同一文件归属判断。两份有效文件各一条时，三种战斗类型分别为 0、1，下一条追加为 2；空组合仍保留位置但自身不可放置。
2. **物品与容量。** `NativeResourceFileRules.cs` 集中定义物品、容量、Effect、奇物类型和映射查询；物品与容量发现入口使用这些规则，不再要求整个固定点分后缀。首个物品定义和最后容量赋值保留原语义。容量为 2 时，拒绝创建第三个格子。
3. **Effect、奇物及刷新。** 人物目录的 Effect 发现、奇物目录及保存前复查、物品引用的 CSV 分发、缺失文件判断共同使用相同查询。`ProfileCatalogContentFingerprint.cs` 纳入匹配范围所需的 `*darkest` / `*csv` 文件，能发现清单、大小和时间戳不变的内容变更。Effect 影响本次涉及的运行时怪癖线索，不意味着技能会直接参与人物初始 MAX HP 计算。
4. **引用误排除。** `QuantityItemReferenceAnalyzer.SourceDiscovery.cs` 将旧任意目录段排除收窄为挂载后的资源根排除。`loot/inventory/`、`loot/effects/`、`loot/trinkets/`、`loot/shared/buffs/`、`loot/localization/` 下的有效掉落文件可以按真实入口形成引用，不再被同名子目录误伤。

独立复核后补充：人物／怪物物品引用必须来自已有标准 info/art/人物 override 入口，其他文本文件不能仅因位于这些根目录而成为引用。清单中的 `heroes/inventory/README.darkest` 即使含有物品示例，也不会使物品显示为已引用；该 README 缺失时也不会使引用分析不完整。名为 inventory 的合法人物／怪物目录及其标准 info 文件保留真实掉落引用。

启用本地／创意工坊 Mod 仍要求清单列出文件，并遵守 DLC 启用范围和实际消费者查询。README、备注、无关配置、未列入清单及未启用 DLC 文件不会因此成为有效资源。原版、模式和官方 DLC 使用各自游戏资源根。

没有修改缺失清单准备流程、Mod 优先级、重复 ID 规则、未引用物品分析本身、持久副本目录路由、Bridge 历史清理政策或游戏原生战斗。本次未添加依赖，也未修改真实存档、游戏目录或活动 Mod。

## 验证

新增 `TextResourceQueryContractTests.cs` 与 `EncounterFileQueryContractTests.cs`，接入完整套件并提供 `--queries` 定向入口。覆盖原版、模式、官方 DLC、本地 Mod、创意工坊 Mod、Mod 的启用 DLC 前缀六种来源，及清单边界、同路径覆盖、缺失诊断、无关文件、内容刷新和保存前检查。

实际 DSON 验证包括：堆叠上限 2 的数量 3 写成 `[2, 1]`；容量 2 拒绝第三格；走廊、房间、首领分别直接替换为编号 1、Bridge 追加为编号 2、重新读取、删除及维护保持一致。

- 最终 Release 编译成功，0 警告、0 错误。日志：`workspaces/historical-rule-review10-20260910/final-build.log`；复核前构建另存于 `fix-build.log`。
- 复核前完整套件退出码 0，**41 条 PASS 汇总**（不是 41 个单独断言），无 SKIP/FAIL。日志：`workspaces/historical-rule-review10-20260910/fix-full-contracts-run.log`；隔离输出：`workspaces/contract_tests/20260910_114804_822_cf4064f8c0f6414ea2c1edf939355e97/`。
- 复核补充修复后，最终 `--queries` 两条 PASS 汇总，退出码 0。日志：`workspaces/historical-rule-review10-20260910/final-queries.log`。新增 18 组人物／怪物文档和标准路径对照覆盖本地、创意工坊及 DLC 前缀 Mod，并检查缺失无关文件不会误报；此前定向结果另存于 `fix-queries.log`。
- 受影响的最终 `--catalogs` 目录／保存回归退出码 0，**20 条 PASS 汇总**，无 SKIP/FAIL。日志：`workspaces/historical-rule-review10-20260910/final-catalogs.log`；隔离输出：`workspaces/contract_tests/20260910_121119_481_8ce53076da5c410db553dbd7892bb93e/`。补充修复没有再改变战斗代码；其直接／Bridge／删除链已在最终 `--queries` 重测。
- Core、App Release、测试输出的最终 Core DLL 一致，SHA-256 为 `299DAED89DDEDEA1086747A9EDC17517528C6C0FFB1B4A29AF4DC676861645B5`。复核前输出为 `3B2A00A227A49F1EE1D983A5FCA03F91AB7ED4994C6D030E400C350CD8639612`，保留供历史日志对照。`git diff --check` 及本次涉及文档的本地链接检查通过。
- 独立只读复核完成，发现一项 P2：目录排除收窄后可能读取人物 README 的物品示例；未确认其他实质问题。审查员完成路径判断和调用链检查，但其完整解析诊断启动受权限限制，未完成端到端探针。主代理随后用正式契约独立复现 `SuspectedUnused` 被误算成 `ConfirmedActive`（`reviewer-repro.log`，预期失败），补齐上述人物／怪物引用入口检查后复测通过。这是对审查发现的验证和小范围修正，按协作标准未递归触发第二名审查员。
- 首次编译遗漏了奇物复查调用的 DLC 前缀参数，已补齐后重新编译成功。首次全量命令误用了不支持的 `--all` 参数，仅返回用法错误；随后按套件规定不加选择参数执行全量检查，分别保存日志。

本次原生规则依据为本机 Windows x64 build 27890 的静态代码及前述历史证据；新增验收为编辑器可执行契约，不是新的实机游戏实验，也未证明所有未来平台／版本／脚本机制都已模拟。

## 当前规则文档

- [资源重复定义语义，第 17 节](../resource-duplicate-semantics.md#17-inventory-effect-curio-and-encounter-file-queries-2026-09-10)
- [战斗运行时编号](../encounter-runtime-order.md)
- [资源与保存规则](../content-save-rules.md#resource-file-eligibility-2026-09-10)
