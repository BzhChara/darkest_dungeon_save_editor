# 历史修改第三十一轮审核：缺失覆盖文件与钱包定义身份（2026-09-15）

本轮先提交上一轮怪癖组合选择修复：**f83f628**（fix: validate quirk availability against the current selection），共 12 个文件，提交后工作区干净。随后只读检查历史实现。本轮新增审核报告、索引及隔离探针，没有修改产品代码、正式测试或当前规则文档，也没有操作真实游戏、存档、Mod 或 Steam。

## 1. 缺失的高优先级 Mod 文件被提前排除，错误恢复低优先级定义（P2）

触发条件：两个启用的 Mod 在清单中列出同一有效资源路径；低优先级文件存在，高优先级文件缺失；这个查询结果由低优先级 Mod 提供，而不是一个仍保留的 Base 查询位置。

[QuantityItemCatalog.SourceDiscovery.cs](../../src/DarkestDungeonSaveEditor.Core/QuantityItemCatalog.SourceDiscovery.cs) 第 56–60 行和 [TrinketCatalog.cs](../../src/DarkestDungeonSaveEditor.Core/TrinketCatalog.cs) 第 280–284 行遇到缺失文件时只添加诊断，然后 continue。高优先级路径因此未进入后续覆盖计算；低优先级 Mod 被当成有效来源，条目没有 HasProviderConflict。数量与饰品保存服务再次加载目录时沿用相同结果，所以重新验证也不能阻止错误来源被写入。

这与已建立的文件打开规则不一致：清单登记的缺失文件仍参与来源选择，选中的文件无法读取时应报告该来源不可用。现有 NativeContentFileResolver.ResolveOpenRequests 已明确保留缺失获胜者，容量读取 InventorySystemConfigCatalog 也已经保留缺失候选。相关依据见 [资源重复定义规则](../resource-duplicate-semantics.md) 的容量与固定路径打开说明。此次未重新实测游戏对全部 I/O 失败的处理；确认的是编辑器违背已有来源规则，并实际写出了错误提供者的字段。

### 隔离复现与对照

使用正常目录及精确清单路径：

- inventory/new_mod.inventory.items.darkest：低优先级定义 supply/probe_token，堆叠上限 2；高优先级正常对照为 5。
- trinkets/new_mod.entries.trinkets.json：低优先级定义 probe_trinket，quest_uses=2、trigger_limit=2；高优先级正常对照均为 5。
- 原版提供独立的背包容量 16、饰品容量 100，避免因容量不可用掩盖这个问题。

| 场景 | 物品和饰品目录 | 实际保存结果 |
| --- | --- | --- |
| 高优先级文件缺失、清单仍列出；低优先级 Mod 文件存在 | 错选低优先级来源，记录缺失提示但保持可写 | 10 个物品写成 5 堆，每堆 2；新饰品两种剩余次数均为 2 |
| 高优先级文件及清单正常 | 选择高优先级来源 | 10 个物品写成 2 堆，每堆 5；新饰品两种剩余次数均为 5 |
| 高优先级物理文件存在、清单未列出 | 正确选择低优先级来源 | 每堆 2，饰品次数 2 |
| 低优先级文件来自 Base，高优先级清单列出缺失文件 | Base 查询结果被重新打开到缺失获胜者；不恢复低优先级条目 | 两个目录均没有该条目，带读取失败诊断；未执行保存 |

每种场景分别测试本地 Mod 和创意工坊 Mod，共 **8 组、50 个观测／断言**，退出码 0。两组缺失获胜者案例都由实际 PrepareQuantityItemEditAsync / PrepareTrinketEditAsync 和 CommitAsync 完成错误来源写入，随后解码 DSON 核对；这两个案例属于成功复现产品错误，不是正确性通过。共完成 12 次隔离保存，其中 4 次复现错误来源写入，8 次为正常对照。游戏状态文件哈希保持不变。

探针同时调用现有固定路径打开解析器，对同一清单和请求确认获胜者仍应是高优先级缺失文件，排除实验中 Mod 顺序反置的可能。原版对照解释了为什么此前某些缺失覆盖测试可能未发现此问题：公共解析器只对仍为 Base 的查询位置执行额外打开解析，已来自 Mod 的结果不会经过这条补救路径。

证据：[探针源码](../../workspaces/historical-rule-review31-missing-provider-20260915/Program.cs)、[项目](../../workspaces/historical-rule-review31-missing-provider-20260915/Review31Missing.csproj)、[运行日志](../../workspaces/historical-rule-review31-missing-provider-20260915/probe.log)、[结果及最终存档路径](../../workspaces/historical-rule-review31-missing-provider-20260915/probe-results.json)。

### 历史来源与修复范围

物品的提前跳过来自 dca5b2b 拆分前已有的目录逻辑；饰品的相同结构在 Git 历史基线 7b9a67f 已存在。后来的标准目录／查询筛选，以及固定路径打开和容量保护，没有改变这两个入口的提前排除。

建议在清单资格、目录及文件名查询筛选之后保留缺失路径，先完成覆盖和查询顺序计算，再处理有效提供者的读取失败。缺失的获胜者不能恢复被覆盖内容；被正常高优先级文件遮住的缺失低优先级文件也不应连带禁用其他条目。还需覆盖“目录已加载后文件删除”和“缺失路径成为保存中已有物品”的服务校验，避免修正目录后从 save-only 路径绕过同一问题。

静态检查还发现 ContentFileDiscovery、人物目录、战斗表、房间附件和本地化的部分入口有类似缺失跳过；其中人物 shared rules、房间 props 已有局部保留处理。各自还有独立的打开或写入保护，不能仅靠相同代码形状宣称都能错误保存。后续修复应一并核对这些入口；本轮完成端到端写入复现的范围是物品与饰品。

## 2. 多个物品定义对应同一钱包余额时，被误报为来源冲突（P2）

[QuantityItemCatalog.Definitions.cs](../../src/DarkestDungeonSaveEditor.Core/QuantityItemCatalog.Definitions.cs) 先按 CatalogKey 分组，此键代表存档位置。钱包金币的键为 Wallet:4:gold:，不包含物品定义的 ID。随后第 269–270 行只要发现组内任一原始 InventoryType 或 ItemId 不同，就设置 HasProviderConflict，并报告无法确认文件优先级。

例如两个都有效的文件：

~~~text
inventory_item: .type gold .id ""        .base_stack_limit 10
inventory_item: .type gold .id "variant" .base_stack_limit 25
~~~

副本中它们是两个不同的 (type,id) 定义，各有堆叠上限。小镇中它们却都映射到 wallet 的 type=gold 余额，定义 ID 不写入钱包。文件提供者没有冲突，钱包修改目标也明确，但上述检查会将整条金币余额标为只读。shard 的非空 ID 变体有相同行为。

钱包和背包身份的区别已经记录在 [内容与保存规则](../content-save-rules.md) 及 [物品保存身份修复](inventory-persistence-fixes-2026-09-14.md)。现有 InventoryPersistenceContractTests 已确认单个金币定义的未使用 ID 不影响钱包可写性，但没有覆盖多个这样的定义同时出现。

### 隔离复现与对照

共 **14 组、218 个观测／断言**，退出码 0：

- 原版、本地 Mod、创意工坊 Mod 各测试单条、同 ID 重复、不同 ID 定义；另测同文件的不同 ID。
- 四组不同 ID 场景均对金币和晶片复现只读，共 8 次真实预览拒绝。余额分别正确显示为 100 和 3，SaveIdentityIssue 为空，服务拒绝理由为 unresolved definitions at the same priority。
- 同样资源的副本目录各保留两个定义，分别使用上限 10、25，没有来源冲突。
- 同路径正常覆盖、清单未列出的文件，以及精确同 ID 的 first-match 行为均正常。
- 10 组正常对照共完成 20 次真实服务保存及 DSON 回读，目标数量为 29，原钱包附加字段保留。
- 四组错误场景另用公开纯数量修改 API 和 DSON 编解码证明钱包目标明确、结果结构可保留；此 API 不承担服务的来源校验，该实验不代表建议绕过服务保护，也未将错误场景提交到服务管理的档案。

证据：[探针源码](../../workspaces/historical-rule-review31-20260915/Program.cs)、[项目](../../workspaces/historical-rule-review31-20260915/Review31.csproj)、[运行日志](../../workspaces/historical-rule-review31-20260915/probe.log)、[结果及回读路径](../../workspaces/historical-rule-review31-20260915/probe-results.json)。

本轮没有确认用户当前档案存在上述非空金币／晶片 ID 变体；它是有效合成输入下的错误保护，不能描述为当前全部金币修改已损坏。

### 历史来源与修复范围

当前这段 Any 检查由 4e1db6b 在接入原生 first-match 规则时引入。当时删除了全局按 Mod 优先级挑同 ID 的旧逻辑，却仍让“存档键分组”承载“定义是否同一身份”的判断。钱包的定义身份与存档身份不相同，因而出现误拦。

建议分别处理原生 (type,id) 定义选择与小镇钱包聚合，按实际保存键确定数量目标，保留真实的文件提供者不确定性、原生哈希冲突与保存字段合法性检查。副本继续按完整类型和 ID 区分条目。无需增加旧存档迁移或兼容。

## 其他范围、验证边界

还检查了人物目录及选择交互、战斗修改的地图状态保护、编辑器战斗历史归属、饰品次数初始化、物品数量分配和当前同步相关代码。已查看路径中，未确认第三个独立问题。历史归属保护、静态／动态地图一致性检查、饰品缺省次数以及已有合法背包堆叠保留均有现行规则依据，不能仅因存在旧分支而删除。

两份探针共 **22 组、268 个观测／断言**，包含明确复现错误的断言，不能当作“产品通过 268 项测试”。探针引用提交所对应的既有 Release Core DLL，没有重建产品。Core、App、契约测试 DLL 及上一轮 11 个冻结工件哈希均通过核对；上一轮的 12 组定向测试为提交前已有证据，本轮未重跑完整测试套件或进入游戏测试。

本轮只记录问题，未实施修复；因此按只读审核边界不触发实施完成后的独立审查员。新增的仓库文件仅为本报告和记录索引，探针及存档位于 Git 忽略的 workspaces/。最终范围与工件哈希见 [证据校验记录](../../workspaces/historical-rule-review31-20260915/verification.json)。
