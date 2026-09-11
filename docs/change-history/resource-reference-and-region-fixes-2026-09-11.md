# 物品引用语义与地区查询修复：2026-09-11

用户确认修复[第十一轮审核](resource-reference-and-region-audit-2026-09-11.md)的三项问题。基于 `9158362c4645016f90c54af494e6bf9ac53aa5dc`，本轮修改产品代码、契约测试及规则文档；未修改真实档案或实际 Mod。

## 修复内容

- `BattleEncounterCatalog.SourceDiscovery.cs`、主文件及 `RuntimeOrder.cs`：将物理目录匹配、清单目录匹配与文件名表 ID 分开处理。原版／模式／官方 DLC 的 Windows 路径允许目录大小写别名，表 ID 保留文件名中的真实拼写，当前查询仍严格匹配该 ID。Mod 清单则保留目录树原始大小写。文件发现、当前表、全局来源、直接校验、Bridge 追加、维护与指纹采用一致的来源规则，条件／额外集合保持独立。
- `QuantityItemReferenceAnalyzer` 的来源、内部模型、用途分类、文本／JSON 入口：保存去掉已启用 DLC 挂载前缀后的路径，按真实资源根判断小镇／副本用途。`curios/upgrades/`、`curios/campaign/town_events/` 等子目录不会改变奇物来源。缺失、读取失败和 JSON 不确定性诊断使用同一个上下文，避免出现“成功读取与错误处理分到不同场景”的分歧。
- `QuantityItemReferenceAnalyzer.RootParsing.cs`：标准人物文件只用 `extra_battle_loot`、`extra_curio_loot` 记录建立掉落根；怪物文件使用 `loot`。未知记录或其他记录中的 `.type/.id`、`.item_id`、`.use_item_id` 不再成为人物／怪物物品引用。已有 NativeDarkestReader 注释和最后字段规则保留，配给／起始物品、任务奖励等 JSON 入口保留。

物品改动影响小镇／副本目录中的引用状态、默认可见性及刷新，不改变物品数量、堆叠定义和存档结构。战斗改动影响编号和相关写前校验；原有独立类型计数、空行占号、怪物 ID 大小写、体型不确定性、DLC 顺序和覆盖路径大小写冲突保护保留。没有恢复缺少 `modfiles.txt` 的 Mod 扫描回退。

## 修复阶段补充的原生证据

上轮审核用 Windows 物理路径推导的预期只适用于物理目录设备。对 Mod 的“清单目录树”和随后“按路径打开磁盘文件”必须分开。

- Windows 物理枚举在 `0x140374460` 使用 `FindFirstFileW`／`FindNextFileW`；前轮已核对导入地址。
- Mod 清单读取 `0x1403866A0` 保留行内路径的原始拼写。注册目录树在 `0x1403EBE30`、子目录插入 `0x140239251` 按原始字节计算 `hash = hash * 53 + byte`；查找 `0x1402397E0` 使用同样的原始字节。没有大小写折叠步骤。相关导入 `0x140C632E0` 是 `strnlen`，不是字符串转小写函数。
- `0x140247B3A` 明确调用清单目录树枚举；没有该树的物理来源走设备枚举。MashGuide 传入的表 ID 与物理目录实际拼写不是同一个数据来源。

因此，当前请求 `cove` 时：

| 来源 | 路径情况 | 是否计入 cove 表 |
| --- | --- | --- |
| 原版／模式／官方 DLC 物理源 | 目录 `Cove`，文件名 `a.cove.2.mash.darkest` | 是 |
| 任意来源 | 文件名 `a.COVE.2.mash.darkest` | 否；对应不同的查询字符串 |
| 本地／工坊 Mod | 清单列出 `dungeons/Cove/a.cove.2.mash.darkest` | 否 |
| 本地／工坊 Mod | 清单列出 `dungeons/cove/a.cove.2.mash.darkest`，实际磁盘目录为 `Cove` | 是；清单匹配后 Windows 能打开路径 |

上轮两个编辑器版本的输出记录保留，并在审核报告中补充了原生预期的适用范围。原版物理源“应为编号 2，却写成 0”的回归仍成立。新的 Mod 规则不会把本应忽略的文件额外算进去。

只读反汇编与导入核对保存在 [review11 工作目录](../../workspaces/historical-rule-review11-20260911/)：`native_manifest_read.txt`、`native_manifest_directory_insert.txt`、`manifest-mount-imports.json`，并结合之前的 `disasm_1403ebb50.txt`、`disasm_1402396f0.txt`、`disasm_140247970.txt`。EXE 仍为已固定的 Windows x64 build 27890，SHA-256 `35E5A653279992564809FF8406FEBD5A02A7D6961044781B1296B38A7096F59B`。本轮没有进入游戏进行新的实机对照。

## 验证与审查

新增 `EncounterDirectoryCaseContractTests.cs` 和 `ReferenceContextContractTests.cs`，接入既有 `--queries` 与完整串行契约套件。覆盖 20 组目录来源／大小写组合、18 组奇物路径、48 组人物／怪物记录，共 86 组新样例；每组战斗分别检查 hall、room、boss。既有战斗查询事务样例现在使用大写物理目录，实际验证 Bridge 安装、直接放置／替换、删除、DSON 重读与稳定维护。

验证日志保留在上述工作目录中：

- 最终产品代码的 Release 整体构建成功，0 警告、0 错误：`dotnet build DarkestDungeonSaveEditor.sln -c Release --no-restore -m:1`，见 `final-build.log`。
- `--queries` 定向检查通过；最终完整契约套件 `dotnet run --project tests/DarkestDungeonSaveEditor.ContractTests -c Release -- .` 退出码 0、41 项 PASS 汇总、没有失败或跳过。见 `final-full-contracts-rerun.log`，产物为 `workspaces/contract_tests/20260911_010552_761_a2b8d54e2f9745feaddaf0b101c60e33`。
- 原始 33 组审核反例重测通过，另核对 6 次直接写入的 DSON 编号往返；断言也保留清单大小写不匹配时应排除条目的情况。见 `probe-final-fix-results.json`、`final-audit-probe-rerun.log`、`fix-verification.json`。
- Core、App、契约测试及重测探针所使用的 Core DLL 哈希一致：`312F5A49D7B3A2143A6AE4DA15931BB66F103DA7C60A84605FAB16AF96CE9CF7`；规则依据的游戏 EXE 哈希也已重新核对。

验证过程中有三处失败，均保留原始日志：默认并行构建曾无诊断退出 1，改用串行整体构建成功；新增条件战斗样例后，测试误将首条条件记录当成普通记录修改，已明确选择 Standard 并重跑完整套件；旧审核探针还把 `parent-runtime/` 的历史 DLL 当作 MSBuild 候选引用，已从该诊断项目的默认项目项中排除该目录，保留原始历史数据，重测后通过上述哈希校验。前面的旧 DLL 输出不作为此次修复的验证结果。

已完成一次全新上下文的独立只读审查，检查本轮 8 个 Core 文件、测试入口、两个新增测试文件及规则文档，未发现阻止完成的问题。审查者独立核对构建与完整测试日志，并重新断言 33 组反例、6 次 DSON 编号往返及四处 Core DLL 哈希。本轮没有新的游戏实机运行结果；结论限定于当前差异及上述固定 Windows 版本，其他游戏版本和实际战斗运行效果未验证。
