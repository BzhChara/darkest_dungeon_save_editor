# 缺失覆盖文件与钱包定义身份修复（2026-09-15）

本次根据用户确认，修复[第三十一轮审核](inventory-provider-and-wallet-audit-2026-09-15.md)确认的两项问题，并处理共享发现入口和自动同步的必要联动。基线为 `f83f628`。没有执行新的提交，没有修改真实存档、游戏、Mod 或 Steam；测试资源、存档及日志全部位于 Git 忽略的 `workspaces/`。

## 修复内容

### 1. 缺失文件先参与来源计算，再报告读取失败

旧实现会在发现阶段跳过清单中存在、物理文件却缺失的路径。当较高优先级 Mod 的文件缺失时，较低优先级 Mod 的同路径定义因此重新成为有效来源。物品和饰品服务再次读取时仍得到相同的错误来源，可能使用错误的堆叠上限和饰品次数。

现在先筛选清单资格、目录和原生查询，再保留候选路径，完成既有文件位置及提供者计算。选中的文件不可读时保留诊断和不可用状态，不恢复已被替换的较低来源。正常高优先级文件遮住的缺失低优先级文件，以及未列清单文件，继续按既有规则处理。

涉及 `QuantityItemCatalog.SourceDiscovery.cs`、`TrinketCatalog.cs`、`ContentFileDiscovery.cs`、`HeroClassCatalog.SourceDiscovery.cs`、`BattleEncounterCatalog.SourceDiscovery.cs`、`BattleRoomAttachmentCatalog.cs` 和 `ContentLocalizationCatalog.cs`。共享解析器的原生排序算法、不同查询的 flags、同 ID 的各类取值规则没有更换；可叠加查询仍保留各自输入。

独立复核另外发现 `QuantityItemReferenceAnalyzer.SourceDiscovery.cs` 的独立候选入口仍提前排除缺失文件。主代理确认后补齐：物品引用分析同样不能恢复被覆盖的低优先级掉落表。正式用例通过实际副本物品目录验证：定义仍可读、上方掉落表缺失时，状态是引用分析不完整，不能被下方表误确认；恢复文件后重新确认为有引用。

### 2. 防止通过“仅存档”条目绕过读取失败

物品定义读取失败时，存档里存在但没有解析到定义的物品，不能直接被认定为普通残留条目。目录保留数量显示及读取失败证据，将这类条目标为只读。只刷新存档数量时，新出现的残留条目同样保留该状态。

`QuantityItemCatalog.cs`、`.Refresh.cs`、`.Definitions.cs`、`Models.cs` 和 `SaveEditService.QuantityItems.cs` 共同处理目录、预览、提交和缓存刷新。即使用户已经生成预览，之后有效定义文件才被删除，提交时也重新检查。文件恢复并完成读取后，可重新预览、修改真正的残留条目，不建立永久黑名单或旧格式兼容。

### 3. 小镇钱包聚合与物品定义身份分开

先按原始 `(type,id)` 选择第一个有效定义，再按小镇实际保存键合并钱包显示和来源。例如 `gold/""`、`gold/variant`、`heirloom/gold` 都可对应 `wallet.type=gold`；它们的 ID 不同本身不构成来源冲突。金币、晶片修改仍更新已有余额，不会生成以定义 ID 区分的多份钱包条目。

涉及 `QuantityItemCatalog.Definitions.cs`、`.InternalModels.cs` 和 `Models.cs`。原生哈希冲突在钱包聚合前检查，真实来源不确定性也保留。副本中的不同 `(type,id)` 继续分开，各自采用对应定义的堆叠上限；小镇 estate 物品也不跨 ID 合并。

### 4. 战斗与自动同步联动

`BattleEncounterCatalog.cs`、`.RuntimeOrder.cs` 和 `.Maintenance.cs` 为缺失的有效战斗表保留读取诊断，阻止无法确认编号的直接写入、Bridge 追加和维护，不把它当成空表计算编号。

`ProfileCatalogContentFingerprint.cs` 与战斗内容指纹稳定记录缺失文件状态；重复检查相同缺失状态不会产生新的指纹，恢复原字节后能再次识别变化。这是共享文件发现改动的必要联动，没有修改同步定时周期或界面调度。

`BattleMapView.ProfileSync.cs` 同时对齐首次载入已有的错误处理：地图道具目录不可读时清空旧候选并记录错误，继续发布正常的地图和战斗目录，不把局部资源错误上抛为整个档案的同步重试。下一次资源变化可重新加载该目录，取消和档案代次检查仍保留。

地图道具预览后的守卫继续将有效文件删除／不可读报告为“内容定义已经变化”的 `InvalidOperationException`，携带底层异常和文件路径，避免新增的缺失候选在指纹计算中泄漏不符合既有接口约定的文件异常。

## 验证

正式回归加入三个目录／服务测试文件：`InventoryProviderContractTests.cs`、`MissingInventoryProviderContractTests.cs`、`MissingResourceProviderContractTests.cs`，由完整测试套件和既有 `--inventory-persistence` 入口共同执行。另加 `MissingResourceSyncContractTests.cs`，通过既有 WPF 同步测试宿主验证道具目录的正常载入、缺失后清除旧候选、同步成功返回和文件恢复。

新增 15 组测试覆盖：

- 原版、本地、工坊钱包聚合；同 ID first-match；副本独立堆叠上限；estate 独立 ID；原生哈希冲突及钱包 DSON 实际保存。
- 本地、工坊的有效高优先级、未列清单高优先级、被遮住的缺失低优先级、缺失高优先级四组对照；实际物品数量和饰品两类次数回读。
- 小镇、副本预览后删除有效文件，已准备的物品／残留／饰品提交均拒绝；缓存选择重新预览同样拒绝，两个存档字节保持不变；恢复文件后实际清除残留成功。
- 怪癖、Buff HP、本地化、战斗表、地图道具、共享 Loot 查询的缺失来源；战斗追加、旧选择和维护拒绝；公共及战斗指纹对缺失状态保持稳定并识别恢复。

执行中的问题如实保留：首次构建因新增测试的原始字符串语法失败，修正后构建成功；首次定向运行因测试未捕获地图资源模块原有的 `InvalidDataException` 失败，修正异常断言后新增组全部通过。第一次完整回归发现公共同步指纹在缺失路径上抛 `FileNotFoundException`，随后补齐稳定缺失状态并重新构建、运行完整回归。这项产品问题没有通过放宽旧测试规避。

第二次完整回归通过 58 组后，在 DLC 前缀奇物 CSV 删除测试中发现守卫泄漏 `FileNotFoundException`，而既有契约要求内容变化异常。随后修复该异常处理，保留原测试不变并重跑；该次中断日志保留为 `full-final.log`，不能算完整通过。隔离构建运行的同步专项已通过 41 组，包括新增 WPF 用例。

地图专项随后发现 `MapPropJsonContractTests` 的旧缺失文件样例只要求警告后返回空目录。本次规则要求已被查询消费的缺失资源保留为不可用输入，因此加强该断言为明确拒绝，并检查错误包含对应路径；未被查询消费的文件仍要求无缺失诊断且不阻止目录。原生查询筛选本身没有放宽。

独立审查员仅报告上述引用入口遗漏，未发现其他可操作问题。主代理核实并修正该项；依照 AGENTS 对审查后小修的要求，不递归发起新一轮完成审查。复核结论、核实及处理见 [独立复核记录](../../workspaces/inventory-provider-fix-20260915/independent-review.md)。`full-complete.log` 是为处理该项而主动中止的中间运行，不能当成完整通过。

最终验证结果：

- [最终 Release 构建](../../workspaces/inventory-provider-fix-20260915/build-reviewed.log)：退出码 0，零警告、零错误。
- [最终完整回归](../../workspaces/inventory-provider-fix-20260915/full-verified.log)：退出码 0，**151 组 PASS，无失败、无跳过**。使用审查修正后的最终 Core、App 与测试程序集，包括本次新增的 15 组目录／服务测试和 1 组 WPF 测试。组数按日志的 PASS 输出统计，不是内部断言总数。
- [同步专项](../../workspaces/inventory-provider-fix-20260915/ui-final.log)：41 组通过；[地图专项](../../workspaces/inventory-provider-fix-20260915/map-complete.log)：4 组 PASS 及完整地图内容流程通过。两项专项后的最终完整回归再次覆盖相关路径。
- 独立复核的 1 项遗漏已核实、修正并纳入上述最终回归；没有待处理的实质性发现。
- 范围核对与 `git diff --check` 通过，Release 的 Core／App／测试 DLL 与最终运行前记录的哈希一致。旧审核的 587 个工件（排除本次追加条目的共享索引）及上一轮修复的 11 个工件保持原哈希。见 [最终证据记录](../../workspaces/inventory-provider-fix-20260915/verification.json)。

日志和工件保存在 `workspaces/inventory-provider-fix-20260915/`。旧审核探针及原始结果保留，不能将其中“成功复现错误”的断言算成修复通过；本次使用当前产品及正式回归重新验证。未做新的游戏内实测，也不声称覆盖所有原生 I/O 错误的运行时行为。
