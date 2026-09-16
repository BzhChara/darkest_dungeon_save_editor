# 历史修改第三十二轮审核：物品引用完整性与钱包身份（2026-09-15）

本轮先提交上一轮修复：**dac3dbb**（fix: preserve missing providers and separate wallet identities），共 33 个文件，提交后工作区干净。提交前逐一核对这些文件及三个 Release 程序集，均与上一轮完整回归 151 组通过时的记录一致；没有重新运行完整回归。

随后只读审查产品实现，新增本报告、记录索引及忽略目录中的隔离探针。没有修改产品代码、正式测试或当前规则文档，没有操作真实存档、游戏、Mod 或 Steam。本轮确认两项 P2 问题，均影响物品用途分类；没有据此判定数量保存或战斗编号发生错误。

## 1. 已被覆盖的缺失文件仍污染引用完整性（P2）

位置：[QuantityItemReferenceAnalyzer.SourceDiscovery.cs](../../src/DarkestDungeonSaveEditor.Core/QuantityItemReferenceAnalyzer.SourceDiscovery.cs) 第 188–190 行。

`AuditManifestReferenceFiles` 在每个 Mod 的清单上发现缺失路径后，立即将 `scanComplete` 设为 false；对于升级文件，还将 `upgradeFilesComplete` 设为 false。此时尚未进行文件覆盖计算，因此已被可读高优先级文件替换的低优先级文件，也会改变全局引用分析状态。

后续 `LoadEffectiveFiles` 已正确选中高优先级文件，但不会恢复上述两个标志。`ParseUpgradeReferences` 因 `upgradeFilesComplete=false`，把正常获胜升级树的材料引用降为不确定；没有引用的其他 Mod 物品也会从 `SuspectedUnused` 变为 `AnalysisIncomplete`。后者默认显示在普通列表里，而非隐藏列表。

隔离例子：两个 Mod 的清单均列出 `upgrades/building/review.upgrades.json`。下方文件要求 `low_coin`，上方文件要求 `high_coin`；另有未使用的 `unused_coin`。三个物品定义本身始终存在且可读。

| 状态 | 实际解析的升级文件 | high_coin | unused_coin |
| --- | --- | --- | --- |
| 两份文件均存在 | 上方文件 | 已确认有引用 | 疑似未使用，默认隐藏 |
| 仅下方文件缺失，清单仍列出 | 仍只有上方文件 | 错误变为分析不完整 | 错误变为分析不完整，默认显示 |
| 下方缺失路径也不在清单中 | 上方文件 | 已确认有引用 | 疑似未使用，默认隐藏 |
| 上方有效提供者缺失 | 无可读获胜文件 | 分析不完整 | 分析不完整 |

另做两个对照：独立的正常小镇事件能继续确认 `high_coin`，但不能消除无关物品的错误不完整状态；清单仅多出不被升级查询消费的 `upgrades/readme.darkest` 缺失项时，不产生上述污染。副本目录不受这个小镇专用升级文件的缺失影响。

本地和工坊各 6 个场景，共 12 个。探针除了调用公开的物品目录，还只读检查引用发现结果，确认“下方缺失”时实际返回的升级文件确实是上方可读文件，排除了 Mod 顺序反置或错误读取下方字节的解释。

### 历史来源与建议

缺失路径直接修改 `scanComplete` 的逻辑在 `d1d18f6` 已存在；`c281549` 为防止读取不完整时误认旧升级树，又加入 `upgradeFilesComplete`。这个保护对真正缺失的有效提供者有必要，但沿用了覆盖前的全量清单状态。

建议移除“被查询筛选后但尚未计算覆盖”的全局失效赋值，把读取完整性建立在最终被消费的文件上。可保留低优先级文件缺失的安装诊断，但不能据此把已覆盖内容当成未知替换。可叠加查询的各个实际输入、有效获胜者缺失、目录或清单无法完成读取仍应保留不确定状态。

这与上一轮的错误不同：上一轮是提前排除缺失高优先级文件，导致错误恢复下方定义；本项是下方文件已正确被覆盖，却仍通过另一条诊断路径影响用途分类。

## 2. 钱包引用仍以展示 ID 代替货币身份，且遗漏非空 ID 的物品引用（P2）

位置：[QuantityItemReferenceAnalyzer.InternalModels.cs](../../src/DarkestDungeonSaveEditor.Core/QuantityItemReferenceAnalyzer.InternalModels.cs) 第 87–92、109–118 行，以及 [QuantityItemCatalog.Definitions.cs](../../src/DarkestDungeonSaveEditor.Core/QuantityItemCatalog.Definitions.cs) 第 286–298 行。

已建立的规则区分三种身份：原生物品 `(type,id)`、小镇钱包的 `PersistedType`、界面 `DisplayId`。例如 `gold/variant` 的物品 ID 为 `variant`，但小镇钱包仍是 `type=gold`。

当前 `ResolveIdentityHash` 使用 `DisplayId` 匹配事件、升级、庄园等货币消费者。因此事件明确给 `gold` 时，`gold/variant` 对应的金币余额没有得到引用；反过来，事件给另一种货币 `variant`，却会被误算成金币余额的用途。

另一个入口 `Matches` 对非传家宝钱包直接要求引用 ID 为空，因此任务奖励中合法的 `type=gold,id=variant` 也无法匹配。上一轮钱包聚合只保留代表定义和来源，没有保留其他原始物品身份用于引用分析；同一钱包有空 ID 和非空 ID 两个定义时，非空 ID 奖励的用途仍会丢失。

| 有效定义与用途 | 当前目录结果 | 应有的对应关系 |
| --- | --- | --- |
| gold/空 ID；事件奖励货币 gold | 已确认有引用 | 正常对照 |
| gold/variant；事件奖励货币 gold | 疑似未使用，默认隐藏 | 应确认金币余额的用途 |
| gold/variant；事件仅奖励货币 variant | 错误确认金币有引用 | variant 不是该余额的保存键 |
| gold/variant；任务奖励物品 gold/variant | 疑似未使用，默认隐藏 | 应确认该物品对应金币余额的用途 |
| gold/空 ID；任务奖励物品 gold/空 ID | 已确认有引用 | 正常对照 |
| gold/空 ID 与 gold/variant 同时存在；任务奖励后者 | 疑似未使用，默认隐藏 | 聚合后的金币余额也应得到用途证据 |

`shard` 有相同行为。本地、工坊 × 金币、晶片 × 上述 6 个场景，共 24 个。所有案例通过真实 `PrepareQuantityItemEditAsync` 生成数量 29 的预览，并解码 DSON：预览保存键始终正确为 `gold` 或 `shard`，原合成存档字节未变。这说明本项是用途识别错误，不是上一轮数量修改又被误拦或保存成了 `variant`。

另外对每例加入已有余额 7 的存档对照，数量显示正确且条目保持可见。因此“默认隐藏”主要影响尚无保存余额、且没有原版来源等独立可见依据的 Mod 定义。没有确认用户当前真实档案存在这些非空金币／晶片 ID 定义，不能描述为所有普通金币或晶片都无法识别。

### 历史来源与建议

`DisplayId` 匹配和非传家宝钱包只接受空引用 ID 的假设在 `d1d18f6` 已存在；`c14247a` 新增货币哈希匹配时继续采用了 `DisplayId`。上一轮 `dac3dbb` 正确修复了原始定义选择和钱包数量聚合，但没有修正独立的引用消费者身份。

建议让已经验证的货币消费者按实际钱包身份解析；带 `type/id` 的物品消费者先匹配有效原始物品定义，再映射到钱包或背包。需要保留各个有效原始定义的引用身份，或在钱包聚合前完成分析后合并用途证据。不能把所有字符串引用统一改成 `PersistedType`，因为副本、estate 物品和原始 `(type,id)` 仍有各自规则。无需旧格式兼容或存档迁移。

## 验证、其他检查与边界

探针项目引用已提交版本对应的 Release Core DLL，没有重新构建产品。首次探针构建因试图调用内部 `JsonSupport` 出现 CS0122；改用公开的 `JsonNode.Parse` 后成功，失败日志保留为 `run1.log`。后续 `run2.log` 退出码 0，完成 **36 个隔离场景、202 个观测／对照断言，以及其中 24 次真实服务预览和 DSON 回读**。这些断言包含“成功复现错误”，不能写成产品正确性测试全部通过。没有执行 commit 保存。

证据：[探针源码](../../workspaces/historical-rule-review32-20260915/Program.cs)、[项目](../../workspaces/historical-rule-review32-20260915/Review32.csproj)、[首次构建日志](../../workspaces/historical-rule-review32-20260915/run1.log)、[最终运行日志](../../workspaces/historical-rule-review32-20260915/run2.log)、[逐项观测结果](../../workspaces/historical-rule-review32-20260915/run2/results.json)、[范围及工件校验](../../workspaces/historical-rule-review32-20260915/verification.json)。

另只读检查人物怪癖组合与 HP 条件校验、进化状态、战斗地图写入守卫、Bridge 维护与历史归属，以及共享同步的检查／发布流程。所查看路径中未确认第三项可操作问题；没有因此证明所有人物、战斗、同步分支均已重新测试。历史归属和保存状态保护仍有用途，不能因存在旧分支就直接删除。

本轮仅审核和记录，两个问题尚未修复。按 AGENTS 的只读任务边界，不触发实施完成后的独立审查员。产品代码、正式测试及 Release 程序集保持提交后的版本；新增报告和索引改动未再次提交。
