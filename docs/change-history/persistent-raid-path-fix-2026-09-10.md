# 持久副本子目录读取与写入修复，2026-09-10

## 问题与依据

用户报告在猩红庭院载入 `profile_1` 报错。日志原因为 `QuantityItemSaveScene.Read` 抛出“档案仍标记为副本中，但副本背包数据缺失”。该档案根目录实际没有当前地图、背包；解码 `persist.game.json` 得到 `inraid=true`、`raiddungeon=courtyard`、`raid_save=plot_crimson_court_1/`，相应文件位于这个子目录。旧实现固定使用档案根目录，因而误判缺失。

这是副本存档位置的问题，与 Mod 资源目录、`modfiles.txt`、资源覆盖优先级无关。不能据此断言庭院、农庄一律使用子目录，或 Mod 副本一律使用根目录。实现以存档 `raid_save` 为准，未专门写死地区名称；本轮没有实测农庄的实际 `raid_save` 值。

同时核对了既有存档语料的 15 份 `persist.game.json`（包括备份与旧测试样本）：均有 `raid_save` 字段，值为空；其中实际副本样本包含海湾和兽窟。这支持空字符串对应根目录，与这次庭院的非空相对目录形成对照。[语料路径摘要](../../workspaces/historical-rule-review8-20260910/courtyard-corpus-paths.json)。这些是历史样本，不是本轮新实机测试。

## 修改范围

- `RaidSaveLocation` 集中解读 `raid_save`，校验相对路径、禁止跨出档案、拒绝符号链接和目录联接；缺少字段或空字符串使用根目录。指定子目录但文件缺失时明确报错，不回退到根目录残留文件。
- `SaveProfile` 保留档案根目录，另存副本相对目录；`ActiveContentResolver` 绑定解码后的实际位置。物品载入与预览使用相同入口，提交仍检查 `persist.game.json` 哈希，因此切换目录会使旧预览失效。
- 地图读取、锁定快照、删除／替换／新建战斗、移动队伍、强制回城按实际副本位置操作。地图写入准备期间先锁定并解码游戏入口，再打开所选地图／副本文件；提交继续保留游戏入口和文件版本保护。
- 自动同步按当前游戏入口重新绑定路径，同时将相对目录纳入快照身份；即使两个副本文件字节相同，切换目录也会刷新地图及写入上下文。文件监视和备用轮询覆盖持久副本子目录，忽略游戏 `backup` 副本。
- 档案备份保存子目录相对路径，不把同名地图／背包覆盖到备份根目录；物品、饰品、人物、地图、Bridge、回城备份入口共同采用此规则。符号链接不被递归遍历。
- 地图提交标记接受档案内的副本子目录，继续拒绝跨档案目标、`backup` 下的目标和子目录中的小镇文件；战斗历史按副本相对目录隔离，Bridge 自动维护及中断恢复使用同一目录和相对备份路径。
- 日志记录真实地图和背包文件路径，便于复查来源。
- 删除或重排 Bridge 编号前，按受影响的地区／难度／战斗类型检查其他持久副本是否仍引用旧条目；在小镇、切到别的副本、`raid_save` 清空时均适用。有其他副本依赖时，当前副本已确认属于编辑器的旧战斗可以先清理，Bridge 包保持不变，待逐图完成后再压缩编号；不会写入未激活的地图。这也避免两个持久副本共用同一张表时互相等待。没有失效绑定的正常载入不受影响。历史注销必须匹配目录及副本实例。
- 维护期间锁定实际检查过的保留地图／副本文件及未改写的 Bridge 文件。恢复日志升级为版本 2，记录这些只读依赖的路径和哈希；中断后恢复旧地图引用之前重新核验并持锁，依赖变化时保留当前状态、保持恢复待处理。

人物、怪癖和资源解析规则、Mod 顺序与清单、战斗表编号计算不因本修复变化。强制回城仍只修改 `inraid`、`raiddungeon`，保留 `raid_save` 和持久副本文件。未修改真实存档、活动 Mod、游戏或 Steam 配置，未新增依赖，未提交 Git。

## 验证

只读加载真实 `profile_1`：[结果](../../workspaces/historical-rule-review8-20260910/courtyard-live-read-2.log)。庭院难度 3，13 个房间、11 条走廊、81 个地图格；地图解析无问题，物品目录为副本模式，259 个目录条目、2 个已占用背包槽。共享同步快照成功；读取前后 36 个真实存档文件的集合与哈希一致。

新增 `NestedRaidSaveContractTests`，同时使用隔离 DSON 存档和普通 JSON 存档，覆盖有根目录残留的子目录选择、背包数量写入及刷新、子目录地图写入、外部写入保留、目录切换后预览失效、相同文件哈希的目录切换、备用轮询、回城与完整备份、安全路径拒绝和缺失文件拒绝。既有维护契约增加子目录下的清理、DSON、回滚、中断恢复、外部修改保护场景。

旧回城测试把无实际文件的 `keep-this-value` 当作 `raid_save` 哨兵，不符合该字段真实语义，改为有效的根目录空字符串；“非空 `raid_save` 回城后必须保留”的断言已移入新增真实子目录契约，没有删除这一保护要求。

初轮新增测试发现断言以大小写敏感方式比较 SHA-256，修正为忽略大小写；后续维护测试发现旧提交标记拒绝子目录目标，已修正产品代码；监视负例曾被上一阶段延迟到达的系统通知干扰，改为使用独立监视实例。只读探针初次因程序集相对引用路径写错而未编译，通过修正后成功运行。失败日志保留，不计为通过。

专项回归进一步覆盖了非法提交标记：无效路径应返回“记录不完整”，而非向上传播异常；据此补全 `InvalidDataException` 捕获。子目录维护中的历史实例重建也暴露旧备份读取入口仍固定根目录，已统一按备份中的 `raid_save` 读取。

- [Release 编译](../../workspaces/historical-rule-review8-20260910/courtyard-build-6.log)：退出码 0，0 警告、0 错误。
- [子目录专项契约](../../workspaces/historical-rule-review8-20260910/courtyard-contracts-6.log)：退出码 0，包含基础路径/DSON/同步/回城契约和五组子目录维护场景（清理、DSON、回滚、中断恢复、外部写入）。产物 `workspaces/contract_tests/20260910_073437_895_7bdaf4b292c84efa866ca53801a6f146/`。
- [程序集摘要](../../workspaces/historical-rule-review8-20260910/courtyard-assemblies.json)：Core/App/ContractTests 三处 Core DLL 哈希一致，`A99EEDAE3B9ECD32E01702049B2680A8B4C65C8D637EB702CE60D594681B50E3`。

- [完整契约套件](../../workspaces/historical-rule-review8-20260910/courtyard-full-contracts.log)：退出码 0，36 条 PASS 汇总、0 FAIL、0 SKIP（汇总行数不是断言数）。产物 `workspaces/contract_tests/20260910_073609_922_c9993aaa44b946eb8b392ac520b3d650/`。包含本次路径修复及前一轮 JSON/Buff/引用修复的全部测试。

## 独立复核与补充修复

独立只读复核提出两项实质问题，均由主代理核实：

1. **P1，回城后的持久历史被注销。** 强制回城保留子目录，但镇上维护不载入该地图，旧代码仍删除／重排 Bridge 并写入 `Invalidated=true, RaidIdentity=null`。新增实际服务组合测试复现了删除 1 条、更新 1 个编号、清理 0 场战斗的错误状态（[预期失败日志](../../workspaces/historical-rule-review8-20260910/courtyard-review-repro.log)）。已加入上面的延期和实例匹配规则。新增“回城保留路径”“回城并清空路径但保留目录”两个场景，验证包及地图哈希不变、两条放置记录仍在，恢复副本状态后能完成清理；另验证无实例／其他实例的历史注销记录不能影响当前地图。
2. **P2，正常地图日志未包含子目录。** 之前新增的路径仅位于日志异常处理。已在 `BattleMapLogTracker` 的正常 Information/Trace 信息中加入实际文件路径，将地图／背包路径及副本实例纳入日志上下文。验证同字节 A→B 目录切换会输出一次新的上下文，重复读取 B 不再重复输出。

新增组合测试一度因缺少 `JsonSerializer` 命名空间未编译，修正后才进行上面的错误复现；未把这次编译失败计为测试执行结果。

[补充编译](../../workspaces/historical-rule-review8-20260910/courtyard-review-build.log)退出码 0、0 警告、0 错误；[补充专项契约](../../workspaces/historical-rule-review8-20260910/courtyard-review-contracts.log)退出码 0，包含七组子目录维护场景和正常日志路径契约。产物 `workspaces/contract_tests/20260910_075336_766_dfe02fc6825b4929a96a190ab0631e4a/`。[该阶段全套](../../workspaces/historical-rule-review8-20260910/courtyard-final-full-contracts.log)退出码 0；对应 Core 为 `C8EB389E5412D54C5A7357B924DFB9E90722184E181E7D773F6D3EC037559BA5`，这不是后续依赖修复的最终版本。

针对生命周期的复核确认正常日志修复，并指出只检查“小镇”会漏掉 A→B。改为上面的实际依赖判断和逐图清理。新增“庭院 A→普通 B”“两个持久副本共用表”场景；[依赖专项](../../workspaces/historical-rule-review8-20260910/courtyard-dependency-contracts.log)退出码 0，九组子目录维护场景通过；[依赖全套](../../workspaces/historical-rule-review8-20260910/courtyard-dependency-full-contracts.log)出现最终 PASS／Artifacts，36 条 PASS 汇总、0 FAIL、0 SKIP，产物 `workspaces/contract_tests/20260910_081334_955_8a658169087544829cc5dc3a0dac3f1a/`。

依赖复核另指出：只清 B 的中断日志原来仅含 B 地图，丢失了冻结 Bridge 和保留 A 的只读版本保护。新增“B 已清理、提交标记尚未完成、冻结包被外部修改”的服务契约确实复现错误（[预期失败](../../workspaces/historical-rule-review8-20260910/courtyard-recovery-repro.log)，退出码 1）。因此补全恢复日志和恢复前持锁校验，并增加依赖未变时正常恢复、保留 A 改变时拒绝恢复两个对照。最终验证结果见下文。

## 最终版本验证

- [Release 编译](../../workspaces/historical-rule-review8-20260910/courtyard-recovery-build.log)：退出码 0，0 警告、0 错误。
- [路径与维护专项](../../workspaces/historical-rule-review8-20260910/courtyard-recovery-contracts.log)：退出码 0，包含基础路径／日志／同步／DSON／回城测试及 12 组持久副本维护场景。产物 `workspaces/contract_tests/20260910_082609_291_b5613aa18fe84df3a84c7f50a155a634/`。
- [程序集摘要](../../workspaces/historical-rule-review8-20260910/courtyard-recovery-assemblies.json)：Core/App/ContractTests 三处 Core DLL 一致，`C815B24513D037808FC628CFB61A90411E3936AACC32F99454FBC83184DD772D`。用户此前运行的程序位于 `src/DarkestDungeonSaveEditor.App/bin/Release/net8.0-windows/`，该目录已完成编译更新。
- [完整契约套件](../../workspaces/historical-rule-review8-20260910/courtyard-recovery-full-contracts.log)：退出码 0，36 条 PASS 汇总、0 FAIL、0 SKIP（汇总行数不是断言数）。产物 `workspaces/contract_tests/20260910_082810_133_36df7f33d52f406fa125a7abc17477c6/`。包含本次路径／持久副本／中断恢复修复及前一轮 JSON/Buff/引用修复的全部测试。

独立只读复核最终通过：恢复依赖记录、恢复前核验与持锁、外部版本保留、恢复重试满足验收要求；所有已报告问题均已修复并通过回归，没有遗留复核项。最后仅补充本记录与验收摘要，未再改动已测试的产品代码。`git diff --check` 通过。
