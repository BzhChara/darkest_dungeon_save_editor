# 重新审查修复：2026-09-07

本轮用户确认修复审查项 1（怪物 ID 大小写导致战斗编号错误）和审查项 3（物品字段读取与游戏不一致）。审查项 2 本轮只核实与 `modfiles.txt` 的关系，未修改怪物标准路径解析。未迁移 Bridge 格式，未操作实际游戏存档或 Mod 文件。

## 怪物 ID 与战斗编号

怪物语义 ID 的候选分组、存在性集合、体型字典和 Boss 集合改为区分大小写。文件路径、来源优先级及挂载顺序仍按各自原有规则处理。未解析 ID 的去重统计同步使用区分大小写的比较。

当只存在 `large_A`（体型 3）时，遭遇行中的 `large_a large_a` 仍占游戏编号，但每个缺失单位对该行体型总和贡献 0。后一条 `tiny_A` 的编号保持为 1，不再因误算前一行体型为 6 而变成 0。这个结果同时用于目录、直接写入前校验、Bridge 追加和自动维护。

回归覆盖：

- hall、room、boss 三类表保留缺失条目的编号，正确条目使用预期编号；旧的错误编号在写入前被拒绝。
- 不同来源同时提供大小写不同的 ID 时，体型和 Boss 标签相互独立。
- 三类 Bridge 都在正确的全局编号追加，地图 DSON 写入后读回仍一致。
- 大小写不同的外区组合不互相复用。删除大写定义后，即使小写定义仍在，维护也会移除依赖大写定义的 Bridge 条目，重排剩余条目，并按既有规则清理有成功记录的本地区与 Bridge 战斗。

## 物品字段读取

新增 `QuantityItemCatalog.NativeParsing.cs`，集中读取物品使用的字符串、整数和布尔字段：

- 字段名按区分大小写的最后一次子串匹配读取；`.type`、`.id` 支持有引号和裸字符串，空字符串不会回退到早先 ID。
- `.base_stack_limit` 读取最后字段后的整数前缀，例如 `+2suffix` 为 2；最后字段为 `"4"` 或非数字时为 0，不回用前面的有效值。整数溢出保持未知并禁止据此新增副本堆叠。
- `.estate_can_be_provision` 也读取最后值，采用游戏明确支持的真值写法；有引号的布尔值可读。
- 声明按照下一个声明头分隔，允许跨行字段；其他种类声明的字段不会覆盖前一个物品。按原生预处理移除 `//` 与 `/* */` 注释；查找声明头时屏蔽 `#` 行中的冒号，字段仍从原生保留的正文读取。

隔离样例 `first → second`、堆叠 `9 → 1` 现在得到 `second / 1`，并通过实际背包修改验证：添加 3 个会写入 3 个数量为 1 的格子，存档 ID 为 `second`。无效、缺失、负数或溢出的堆叠限制仍受写入保护。

## `modfiles.txt` 问题的核实

异常目录问题不只出现在无清单 Mod：无清单时，递归扫描能找到额外分类目录；有清单时，只要异常路径列在清单内，同样会进入按文件名识别的分支。清单没有列出的路径则不会被该清单分支纳入。

本次实际发现的本地 `Shuiyue_Monster_Enhancement` 没有 `modfiles.txt`。这解释了该 Mod 的扫描入口，但不是通用根因。游戏发现 ID 后重建标准路径，而编辑器读取扫描到的物理路径，这个差异仍需单独修复。

隔离对照也确认了当前行为：同一异常路径，无清单和清单明确列出它时均被目录误判为可写；清单未列出它时才被排除。

## 原生依据

仅针对本机 Windows x64 build 27890，`Darkest.exe` SHA-256：`35E5A653279992564809FF8406FEBD5A02A7D6961044781B1296B38A7096F59B`。

- `0x1404CB740`、`0x1404D2EE0`：原始大小写 ID 哈希、缺失体型计 0、超过四格才跳过。
- `0x1404C8490`、`0x14036C950`、`0x14036B300`：物品字段采用最后子串，字符串接受引号／裸值，整数采用前缀。
- `0x14036B0B0`：布尔真值为 `t/T/true/True/TRUE/1/y/Y/yes/Yes/YES/on/On/ON`，其他返回假。
- `0x14028E1C0` 及后续代码段：以声明头而非物理换行划分内容。
- `0x14028E070–0x14028E119`：移除斜杠行注释和块注释；`0x14028E26C–0x14028E28D` 在查找声明边界时跳过 `#` 行。

所有原生检查均为静态读取；未启动额外游戏进程或执行注入。

## 验证

新增测试在修复前准确复现了错误的怪物编号；修复后，新增的怪物／Bridge／维护和物品／堆叠测试均通过。

独立只读复核发现两处物品注释边界回归：块注释内字段被采用、`#` 注释冒号截断跨行声明。主代理已通过隔离调用确认两项问题并局部修正，追加了块注释假定义／假 ID、最终无效值拒绝写入、跨行字段及注释删除后的原生数字拼接测试。

- `dotnet run --project tests/DarkestDungeonSaveEditor.ContractTests/DarkestDungeonSaveEditor.ContractTests.csproj -c Release --no-restore -- <仓库绝对路径>`：补充注释修正后，完整合同测试再次退出码 0，包括场景、人物、饰品、所有 Bridge 类型、自动维护、外部写入保护和回滚。最终运行目录为 `workspaces/contract_tests/20260907_092904_380_cba1bec69b414912acf32550ca8a5e77`。
- `dotnet build DarkestDungeonSaveEditor.sln -c Release --no-restore -m:1`：成功，0 警告、0 错误。
- 当前真实 `profile_1` 只读加载成功：物品 259、饰品 1151、职业 50、地图内容 183；当前荒野难度 1 可直接写入走廊 65／房间 69／Boss 4，Bridge 来源行 45197。七个存档文件的哈希在读取前后完全相同。本轮未在实际游戏中执行战斗或修改存档。
- `git diff --check` 通过。所有复现文件、验证日志与真实档案只读快照保存在忽略的 `workspaces/reaudit_20260907` 下，不纳入产品资源。

注释回归的修复前／后隔离结果分别在 `workspaces/reaudit_20260907/runs/20260907_092708_043`、`20260907_093050_336`：块注释样例上限从错误的 9 恢复为 1，`#` 跨行样例从错误的 9 恢复为 0。最终真实档案只读快照为该目录下的 `20260907_093113_857`。按照项目规则，独立复核后的局部修正复跑受影响检查，不再追加一轮重复审阅。

## 涉及文件

- `BattleEncounterCatalog.SourceDiscovery.cs`、`BattleEncounterCatalog.cs`、`BattleEncounterCatalog.Diagnostics.cs`：怪物语义 ID、元数据查询和未解析 ID 统计。
- `QuantityItemCatalog.Definitions.cs`、`QuantityItemCatalog.InternalModels.cs`、`QuantityItemCatalog.SourceDiscovery.cs`、新增 `QuantityItemCatalog.NativeParsing.cs`：声明分隔、原生字段和注释读取。
- `tests/DarkestDungeonSaveEditor.ContractTests/ContractSuite.cs`、新增 `NativeCatalogReadingContractTests.cs`：接入可执行回归测试。
- `docs/change-history/README.md` 与本文件：修改记录归档和索引。
