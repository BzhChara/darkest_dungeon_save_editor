# 饰品依赖与 HP 枚举修复（2026-09-17）

## 授权与范围

用户确认修复[第三十五轮审核](historical-rule-audit-35-2026-09-16.md)的三项问题。本次修改未提交；基线为 `f053d28`。没有启动游戏、修改真实存档、已安装 Mod 或 Steam 配置；所有写入测试使用工作区内合成档案。

## 产品改动

- `TrinketCatalog.cs`：同 ID 首条选择前，排除普通 `buffs` 确认缺失的饰品；全部职业要求改为完整 C-string 原生哈希查询，保留真正不同的大小写/空白身份，不修改保存 ID。
- `TrinketBuffDependencies.cs`：通过既有清单、资源查询、文件覆盖模块建立普通 Buff 存在性索引。确认不存在和读取失败分别处理；后者保留原候选但设为只读，不能误选后面的同 ID 定义。缺失的高优先级文件仍遮蔽下层；被覆盖的下层读取故障不影响已确认的获胜文件。来源发现或覆盖关系不确定时不宣称依赖已确认。
- `HeroClassCatalog.QuirkDefinitions.cs`：已支持的 HP 属性类型和条件先按原生哈希解析为内部标准值，再进入原有 HP 计算及可达状态检查。浮点精度、Buff 完整定义后者优先、未知规则与非正 HP 保护保持原有语义。

`SaveEditService.Trinkets.cs` 复用目录依赖规则，并在重建校验快照时保留已有 `Resolution` 上下文。`ActiveContentResolver.cs` 新增内部完整性判断，复用保存配置的 Mod 条目解析，将已启用但未映射的来源保留为不确定状态。Buff 消失后会拒绝旧预览；Buff 恢复导致前面的同 ID 定义重新获胜时也会拒绝过期选择；替换后检测到变化仍走现有存档恢复。

修改影响饰品目录准入、重复定义选择、新实例使用次数和人物怪癖 HP 分析。数量物品、战斗编号/Bridge、原生存档结构及自然掉落判断没有新增规则或迁移。普通 Buff 依赖不是“物品无引用过滤”：没有直接掉落 ID 引用的饰品仍可有效。

## 验证

工件集中在 `workspaces/trinket-dependency-enum-fix-20260916/`；旧审核工件保持只读。

- 新增 `TrinketDependencyContractTests.cs`、`HeroBuffEnumContractTests.cs`，接入 `--trinket-dependencies` 和完整回归入口。覆盖三种来源的 180 个饰品选择样本、120 个 HP 条件样本、有效提供者/读取失败边界、使用次数 DSON 提交，以及依赖变化后的预检、提交和恢复。
- 原审核两个 `Program.cs` 原样复制到新目录重跑：129 + 57 = 186 个样本全部符合预期，0 不符。三次 DSON 提交选中正确使用次数；三个 Buff 删除后的旧预览均被拒绝。没有重写原先记录的 87 处不符结果。
- 第一次受限构建被取消并记录 MSB5021；常规环境构建发现新增测试误用了 `JsonObject.Values`，已改为枚举键值对。修正后 Release 构建通过，0 警告、0 错误。
- 第一次冻结旧工件时，PowerShell 无法枚举旧实验命名为 `nul` 的目录；随后使用扩展路径完整冻结 1,217 个旧文件，最终以 `audit35-before-complete.json` 核对，不使用那次不完整清单作为证据。
- 完整回归：168 个 PASS 组，退出码 0，无 FAIL/SKIP。它在最后的来源完整性收尾修正之前完成；收尾后重跑受影响的 `--trinket-dependencies`（9 个 PASS 组）和 `--content-sync`（41 个 PASS 组），均退出码 0、无 FAIL/SKIP，没有把较早的完整回归冒称为最终代码的再次全量运行。
- 收尾定向测试曾因一个夹具把不存在目录声明为 Mod，而提前命中既有“缺少 modfiles.txt”拒绝。已将该夹具改为消失的物理 mode 来源，单独覆盖目录完整性；没有放宽产品的清单保护。失败日志保留在 `targeted-final.log`，修正后结果在 `targeted-final-2.log`。
- 最终 Release 再构建：0 警告、0 错误。两份审核探针重新绑定最终 Core DLL 后再次原样重跑，186 个样本仍为 0 不符。App 与 Core 输出中的 Core DLL 一致；`git diff --check` 通过。
- 最终核对记录在 `verification.json`，包括 1,217 个第三十五轮工件和 345 个更早工件保持不变、测试日志、任务文件及程序集哈希。规则文档与本次记录已更新，尚未提交。

## 独立审查

一次新建、独立上下文的只读审查确认前述职业引用和 HP 枚举改动未见新增问题，但发现 Buff 依赖判断漏接上游来源完整性：已启用而无法映射的 Mod 会被 `ActiveContentResolver` 省略出 `Sources`，仅检查现有目录无法发现。主代理核对原调用链后修正，并补上真实 `ResolveAsync` 入口的未映射标题、歧义标题、工坊缺失和正常映射四种情况，检查预览及提交校验上下文。这是同一缺失依赖边界的收尾修正，没有新增功能或迁移，按 AGENTS 不对小修正递归启动第二次审查；最终以相关可执行回归核验。

## 证据边界

本次采用当前游戏 EXE SHA-256 `d83a6d578d059c8c9a0bd70453da65d85974715a616872c2dfeff1a5c9ab3a49` 的已定位静态控制流及隔离程序验证，不宣称新增实机生命周期实验。状态饰品其他 Buff 列表和 `remove_if_not_active` 条件的完整语义仍在本次范围之外。规则入口见 `docs/content-save-rules.md` 第 5、8 节及 `docs/resource-duplicate-semantics.md` 第 30 节。
