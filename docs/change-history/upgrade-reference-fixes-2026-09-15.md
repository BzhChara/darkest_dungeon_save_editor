# 升级购买码与费用引用修复（2026-09-15）

用户确认修复[第二十六轮审核](upgrade-requirement-consumer-audit-2026-09-15.md)的两项问题，并追问此前是否已修改过。本次在 `c5bb049` 后继续修改，尚未提交。

## 为什么此前修过，这次仍需修改

此前 `3d2bd9c` 已将人物目录改为按有效文件顺序取最后一份同名完整升级树，删除了按整文件挑选人物兼容性的办法。当时保留了更早基线中的“同一购买码出现不同等级要求就报冲突”校验。物品用途分析的升级费用入口也没有同步采用最终树和最终购买码。前一次的整树选择修正仍然有效，但没有覆盖这两个消费者细节；本次补全它们。

## 实现

- 新增 [`NativeUpgradeRequirements.cs`](../../src/DarkestDungeonSaveEditor.Core/NativeUpgradeRequirements.cs)，人物生成和升级费用分析共用最终购买码选择。先按原生 `code` 首个 UTF-8 字节保留最后一条完整要求，再读取其等级、费用等字段。同码不同等级不再被判为冲突。
- [`HeroClassCatalog.Progression.cs`](../../src/DarkestDungeonSaveEditor.Core/HeroClassCatalog.Progression.cs) 使用最终要求计算购买计划。实际生效的购买码仍须通过 DSON 表示能力检查；被覆盖的旧要求不再使正常最终要求失效。购买目标哈希、技能档位和装备可达性检查继续保留。
- 新增 [`QuantityItemReferenceAnalyzer.Upgrades.cs`](../../src/DarkestDungeonSaveEditor.Core/QuantityItemReferenceAnalyzer.Upgrades.cs)，先在原有有效文件顺序中按游戏 ID 哈希选择最终完整树，再从最终购买码中提取费用。`QuantityItemReferenceAnalyzer.cs` 调用该入口，删除 `JsonRoots.cs` 的逐条费用累加分支。空的最终树或费用列表会清除旧引用。
- `QuantityItemReferenceAnalyzer.SourceDiscovery.cs` 单独传递升级文件是否完整可读。缺失／不可读文件可能包含尚未知晓的覆盖定义，升级来源因此保持不确定；已知最终树格式不支持时不回退旧费用。独立事件、其他正常树等已确认用途仍保留。不可解码的树 ID／购买码提供诊断而不是中断物品目录。
- 新增 [`UpgradeReferenceContractTests.cs`](../../tests/DarkestDungeonSaveEditor.ContractTests/UpgradeReferenceContractTests.cs)，纳入完整测试和 `--upgrade-references`。删除旧测试把重复等级要求视为冲突的断言，并为 JSON 费用正常对照补齐真实树 ID 和购买码。更新当前规则文档第 25 节和测试索引。

两个具体结果：购买码 `1` 先要求 3 级、后要求 1 级时，人物按最终 1 级要求生成并写入一次购买；旧树费用 A 被新树费用 B 取代时，物品目录仅从该路径确认 B。更改资源内容后重新加载即可更新结果，不要求修改清单，也不保留旧解析结果作为兼容分支。

本次涉及人物目录预检、生成购买计划及其存档预览，小镇物品的用途说明、隐藏筛选和重新加载。数量保存算法、清单优先级、原生文件枚举位置、战斗及 Bridge 的处理方式未修改。没有新增依赖，没有更改真实档案、活动 Mod 或 Steam 配置，没有启动游戏，也没有保存迁移。

## 验证

固定原生证据沿用审核中的 x64 build 27890，EXE SHA-256 为 `35e5a653279992564809ff8406febd5a02a7d6961044781b1296b38a7096f59b`；没有把静态控制流核对写成新的游戏内实测。

- Release 解决方案构建成功，0 警告、0 错误；默认并行构建曾返回非零却没有编译诊断，之后使用 `-m:1 -nr:false` 成功。一轮编译还等待了编译器服务的命名管道，最终正常完成。
- 专项 **123 个场景**通过：本地 Mod、工坊 Mod、DLC 前缀资源各 41 个。覆盖原审核的全部输入、重复码正反顺序、空最终值、大小写不同的码、原生字节／哈希／NUL 身份、首 JSON 字段、缺失及畸形文件、独立用途、清单不变的内容刷新与最终代码保存限制。
- **12 次 DSON 预览／回读**通过：6 次仍可显式写入隐藏货币，数量与钱包 `type` 正确、源合成存档哈希不变；6 次直接使用原始重复要求生成候选，并验证正确购买记录的编码回读。本次不再像审核探针那样先手工删除被覆盖的要求。
- 资源语义分组 **18 项通过，0 失败、0 跳过**；完整测试 **89 项通过，0 失败、0 跳过**，正常返回退出码 0。完整测试包含上述专项，并继续验证清单、文件覆盖、人物购买与 HP、数量／饰品保存、DLC 副本目录、自动同步、战斗和 Bridge、强制回城以及回滚保护。
- 全新上下文的独立只读复核（Plato）未发现已证实的阻塞问题，确认最终购买码、最终费用引用和不完整来源处理符合本次要求；复核时完整测试仍在运行，之后由主执行者核对其成功结果。复核记录保存在本机 `review.txt`。

测试日志曾因 PowerShell 文件重定向没有及时刷新，被误判为完整测试卡在 UI 检查。随后从隔离目录确认测试已进入后续阶段，该定位已更正。两次提前结束的运行不记作通过；完整验证改用逐行刷新并加时间戳的外层日志脚本，未删减测试或改动其断言。

本机证据保存在忽略目录 `workspaces/upgrade-reference-fix-20260915/`：`build-final.log`、`targeted-final.log`、`semantics.log`、`full-live.log`、`verification.json`。当前 Core 的哈希和最终测试、复核结果在验证清单中固定，避免把旧构建结果算到本次代码上。
