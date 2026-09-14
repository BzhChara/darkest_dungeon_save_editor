# 遭遇集合查询、重复来源与空组合修复（2026-09-14）

用户确认修复[第二十二轮审核](encounter-query-and-validation-audit-2026-09-14.md)的三项发现。本轮基于 `d170bf2`，没有提交新 commit。

## 修改内容

1. `BattleEncounterCatalog.SourceDiscovery.cs`、`RuntimeOrder.cs`、`BattleEncounterCatalog.cs`：删除文件名关键词分类，按地区、难度和 Standard/Conditional/Additional 的原生查询分别筛选、排序和解析。查询身份随实际打开的文件保留，当前目录、全局来源、直接写入、Bridge 追加和维护共用同一套逻辑。比如 `a.conditional.cove.2.mash.darkest` 仍是海湾难度 2 标准表；前部的 `conditional` 不会再让后续编号少算一条。保留 Mod 清单、物理目录/清单目录区别、大小写、覆盖优先级、DLC 和不确定编号保护。
2. Bridge 来源复核：允许同一来源记录因合法重复加载出现多次，按所选来源查询、实际文件、哈希、记录序号、类型和组合复核；不再要求相对路径只有一个结果。特殊遭遇复核同步修正。运行时重复位置仍全部计数，来源变更不会被放行。
3. 空组合：合法 hall/room/boss 声明省略 `.types`、只写 `.types`、后接空白或显式 `""` 时，都保留不可放置的编号。后续正常条目、Bridge 尾部和自动维护继续计数。普通空行不计数；超过四格的已知组合仍由游戏跳过，UTF-8 截断和未知体型等不确定输入仍受保护。

源码修改限于战斗目录的三个 partial 文件；物品、饰品、人物、怪癖解析、支付弹窗及通用文件覆盖实现没有修改。没有更改真实档案、游戏资源、活动 Mod 或 Steam 配置。

## 测试与规则更新

- 新增 `EncounterCollectionQueryContractTests.cs`：六种来源的集合查询、大小写和关键词反例、独立/重叠集合、合法重复读取、来源伪造/变化拦截，以及四种空列表输入的三类型编号。
- 12 个真实 DSON 合成档案场景：Base 与 Mod 文件分类、重复跨地区来源、空槽计数 × hall/room/boss。执行直接放置、Bridge 安装与替换、重新解码、自动维护和删除。
- 更正旧的空列表拦截测试和日志预期，改用确实无法确定的 UTF-8 截断案例继续验证拒绝追加、延迟维护及无副作用保护。
- 更新 `docs/encounter-runtime-order.md`、`docs/resource-duplicate-semantics.md` 和测试说明。原生证据仍限定 Windows build 27890，地址与原始反例见本轮审核报告；没有把合成存档验证表述成实机战斗测试。

## 验证结果

- 首轮专项 `--encounter-queries`：7 PASS、0 FAIL、0 SKIP，退出 0，日志 `workspaces/encounter-query-fixes-20260914/targeted.log`。随后补充按所选来源地区重放查询、物理目录别名的特殊遭遇验证；因此首轮专项不作为最后这项补充的验证结果。
- 最终全方案 Release：0 警告、0 错误，退出 0，日志 `workspaces/encounter-query-fixes-20260914/build.log`。Core、App 和测试目录内的 Core DLL 一致，SHA-256 为 `4374df8f819931ed0354a7d70cb4040321f20b8c97b0f3123575b3620ec1fcfa`。
- 一位新启动、独立说明任务的只读审查员检查最终源码、测试、规则、直接/Bridge/维护调用链，未发现有依据的实质性问题。审查时完整回归已通过 57 组但仍在执行；审查员未运行测试，没有将当时进度当作最终全量结果。原文保存在 `workspaces/encounter-query-fixes-20260914/independent-review.txt`。
- 最终完整回归：77 PASS、0 FAIL、0 SKIP，退出 0；`workspaces/encounter-query-fixes-20260914/full.log`。使用上述最终 Core，包含目录别名补充测试，以及全部既有物品、饰品、人物、怪癖、战斗、同步、维护与保存回滚检查。合成产物：`workspaces/contract_tests/20260914_082344_044_5767acf4d7934d0b85ed2ad7148cbf10/`。
- `verify_artifacts.py` 通过：记录 16 份改动文件的哈希、三个 Core 输出、完整测试/构建日志哈希；核对新增 12 个地图的 DSON 文件头及哈希、120 个本地文档链接。结果保存在 `workspaces/encounter-query-fixes-20260914/verification.json`。
- `git diff --check` 通过。本轮代码、测试和文档修改已完成，Release 输出位于 `src/DarkestDungeonSaveEditor.App/bin/Release/net8.0-windows/`，尚未提交 Git。

本轮以固定 EXE 的既有静态证据和执行式合成存档验证为依据，没有重新启动游戏进行实机战斗测试。
